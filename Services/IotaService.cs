using System.Globalization;
using System.Text.Json;

namespace CallsignLookup.Services
{
    // Island is blank when only the group is known (from the QRZ record).
    // Note explains anything short of a straight match.
    public sealed record IotaMatch(string RefNo, string GroupName, string Island, string Note = "")
    {
        public string Reference => $"{RefNo} - {GroupName}";
    }

    // IOTA (Islands On The Air) reference and island for a station, offline.
    //
    // Two data files:
    //   * The IOTA directory - every group's reference, name, DXCC entities
    //     and rough bounding box. iota-world.org's full list, read from the
    //     copy IotaListUpdater keeps in %LocalAppData%\Callsign Lookup (so new
    //     groups show up without a new release), or failing that the one
    //     shipped in Data\iota.json.
    //   * Data\iotaIslands.json - the outline of each island on a group's
    //     island list, plus the unlisted islands within ~10 km of those, from
    //     OpenStreetMap (built by Tools/build_iota_islands.py).
    //
    // A station is on an island when its location is inside one of those
    // outlines, for a group whose DXCC entity and box fit. So London is on
    // the island "Great Britain" -> EU-005, and Kekaha on Kauai -> OC-019.
    // Not on any (the mainland, or an approximate location out at sea): the
    // IOTA reference on the QRZ record, if it has one, flagged as such.
    public static class IotaService
    {
        internal sealed record IotaGroup(string Ref, string Name, int[] Dxcc,
            double South, double North, double West, double East)
        {
            // West > East: the box crosses the 180th meridian.
            public bool Contains(double lat, double lon, double margin) =>
                lat >= South - margin && lat <= North + margin &&
                (West <= East
                    ? lon >= West - margin && lon <= East + margin
                    : lon >= West - margin || lon <= East + margin);
        }

        private sealed class IslandShape
        {
            public string Ref { get; set; } = "";
            public string Island { get; set; } = "";
            public double MinLon { get; set; }
            public double MinLat { get; set; }
            public double MaxLon { get; set; }
            public double MaxLat { get; set; }
            public List<List<double[]>> Polys { get; set; } = new();
            public List<double[]> Points { get; set; } = new(); // islets OSM only has as a point

            // False for an island IOTA doesn't name but that lies close to
            // one it does (Aunu'u, off Tutuila) - it very likely counts for
            // the group, but that's for the user to check.
            public bool Listed { get; set; } = true;

            public bool BoxContains(double lat, double lon, double margin) =>
                lat >= MinLat - margin && lat <= MaxLat + margin &&
                lon >= MinLon - margin && lon <= MaxLon + margin;

            public double BoxArea => (MaxLon - MinLon) * (MaxLat - MinLat);
        }

        private sealed class IslandFile
        {
            public List<IslandShape> Islands { get; set; } = new();
        }

        // IOTA's boxes are only to the nearest few minutes of arc.
        private const double BoxMargin = 0.1;

        // A location this close to a listed island (about 1 km), but not on
        // it, still counts: a geocoded address on the shore, or the outline
        // being simplified to ~50 m.
        private const double NearbyDegrees = 0.01;

        private const string ListFileName = "iota.json";

        private static IotaGroup[]? _groups;

        private static IotaGroup[] Groups => _groups ??= LoadGroups();

        private static readonly Lazy<IslandShape[]> Islands = new(() =>
            JsonSerializer.Deserialize<IslandFile>(
                File.ReadAllText(DataFiles.PathFor("iotaIslands.json")),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })?.Islands.ToArray() ?? []);

        // The downloaded copy of the IOTA list, kept up to date by IotaListUpdater.
        public static string DownloadedListPath => Path.Combine(AppSettings.Folder, ListFileName);

        // Called after IotaListUpdater saves a newer list.
        public static void ReloadList() => _groups = null;

        // Islands in the downloaded IOTA list that weren't in the one shipped
        // in Data\ - which is the list Data\iotaIslands.json was built from,
        // so these have no outline and can't be found until the island data
        // is rebuilt and a new version released. Empty when there's no
        // download yet, or nothing new. Each is "REF Island", e.g.
        // "OC-045 Aunu'u".
        public static List<string> IslandsMissingOutlines()
        {
            try
            {
                if (!File.Exists(DownloadedListPath)) return [];
                return NewIslands(File.ReadAllText(DataFiles.PathFor(ListFileName)), File.ReadAllText(DownloadedListPath));
            }
            catch (Exception ex) when (IsBadList(ex) || ex is IOException or UnauthorizedAccessException)
            {
                return [];
            }
        }

        internal static List<string> NewIslands(string shippedJson, string downloadedJson)
        {
            var shipped = IslandNames(shippedJson).ToHashSet();
            return IslandNames(downloadedJson).Where(island => !shipped.Contains(island)).Distinct().ToList();
        }

        private static IEnumerable<string> IslandNames(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var names = new List<string>();
            foreach (var g in doc.RootElement.EnumerateArray())
            {
                string refNo = g.GetProperty("refno").GetString()?.Trim() ?? "";
                foreach (var sub in g.GetProperty("sub_groups").EnumerateArray())
                    foreach (var island in sub.GetProperty("islands").EnumerateArray())
                        names.Add($"{refNo} {island.GetProperty("island_name").GetString()?.Trim()}");
            }
            return names;
        }

        private static IotaGroup[] LoadGroups()
        {
            try
            {
                if (File.Exists(DownloadedListPath))
                    return ParseList(File.ReadAllText(DownloadedListPath));
            }
            catch (Exception ex) when (IsBadList(ex) || ex is IOException or UnauthorizedAccessException)
            {
                // Damaged download - the shipped copy will do.
            }
            return ParseList(File.ReadAllText(DataFiles.PathFor(ListFileName)));
        }

        // iota-world.org's fulllist.json. Its numbers are all strings, and its
        // longitude_min/max go by magnitude rather than sign (west of
        // Greenwich, "min" is the eastern edge).
        internal static IotaGroup[] ParseList(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var groups = new List<IotaGroup>();
            foreach (var g in doc.RootElement.EnumerateArray())
            {
                string Str(string name) => g.GetProperty(name).GetString() ?? "";
                double Num(string name) => double.Parse(Str(name), NumberStyles.Float, CultureInfo.InvariantCulture);

                double lat1 = Num("latitude_min"), lat2 = Num("latitude_max");
                var (west, east) = LongitudeRange(Num("longitude_min"), Num("longitude_max"));
                int[] dxcc = Str("dxcc_num")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(d => int.Parse(d, CultureInfo.InvariantCulture))
                    .ToArray();

                groups.Add(new IotaGroup(Str("refno").Trim(), Str("name").Trim(), dxcc,
                    Math.Min(lat1, lat2), Math.Max(lat1, lat2), west, east));
            }
            if (groups.Count == 0) throw new InvalidOperationException("The IOTA list has no groups.");
            return groups.ToArray();
        }

        // What ParseList throws for a file that isn't a usable IOTA list.
        internal static bool IsBadList(Exception ex) =>
            ex is JsonException or InvalidOperationException or FormatException or KeyNotFoundException;

        // (West, East); West > East means the box crosses the 180th meridian.
        private static (double West, double East) LongitudeRange(double a, double b)
        {
            double lo = Math.Min(a, b), hi = Math.Max(a, b);
            if (hi - lo >= 359) return (-180, 180); // all the way round (Antarctica)
            if (hi - lo > 180) return (hi, lo);     // e.g. 178.25 / -175.25
            return (lo, hi);
        }

        internal static IotaGroup? FindGroup(string refNo) =>
            Groups.FirstOrDefault(g => g.Ref.Equals(refNo.Trim(), StringComparison.OrdinalIgnoreCase));

        // The groups a station at this location could be in. With no DXCC
        // number (QRZ didn't send one) any entity's groups will do.
        internal static List<IotaGroup> CandidateGroups(double lat, double lon, int? dxcc) =>
            Groups
                .Where(g => g.Contains(lat, lon, BoxMargin) && (dxcc is not int d || g.Dxcc.Contains(d)))
                .ToList();

        public static IotaMatch? Find(double lat, double lon, int? dxcc, string qrzIota)
        {
            var groups = CandidateGroups(lat, lon, dxcc).ToDictionary(g => g.Ref, StringComparer.OrdinalIgnoreCase);
            var nearby = Islands.Value
                .Where(i => groups.ContainsKey(i.Ref) && i.BoxContains(lat, lon, NearbyDegrees))
                .ToList();

            // Smallest first: an islet just off (or in a lake on) a bigger
            // island is the more specific answer.
            var on = nearby
                .Where(i => PolygonMath.Contains(i.Polys, lon, lat))
                .MinBy(i => i.BoxArea);
            if (on != null) return Match(on, groups[on.Ref], "", qrzIota);

            var closest = nearby
                .Select(i => (Island: i, Distance: Math.Sqrt(DistanceSquared(i, lon, lat))))
                .Where(x => x.Distance <= NearbyDegrees)
                .OrderBy(x => x.Distance)
                .FirstOrDefault();
            if (closest.Island is IslandShape island)
            {
                // Only an islet OSM maps as a point can't contain the location.
                return Match(island, groups[island.Ref], island.Polys.Count > 0 ? $"Location is just off {island.Island}" : "", qrzIota);
            }

            return FromQrz(qrzIota, "Location isn't on a listed IOTA island");
        }

        private static IotaMatch Match(IslandShape island, IotaGroup group, string note, string qrzIota)
        {
            if (!island.Listed)
                note = AddNote(note, $"{island.Island} isn't on IOTA's island list for {group.Ref} - check it counts");

            // The QRZ record naming a different group usually means one of its
            // grid/location and its IOTA field is wrong (FW1P: address and IOTA
            // say Wallis, the grid is on Futuna). The location still wins.
            if (FindGroup(qrzIota) is IotaGroup qrzGroup && !qrzGroup.Ref.Equals(group.Ref, StringComparison.OrdinalIgnoreCase))
                note = AddNote(note, $"QRZ record says {qrzGroup.Ref} ({qrzGroup.Name}), but its location is on " +
                                     $"{island.Island} ({group.Ref}) - check which is right");

            return new IotaMatch(group.Ref, group.Name, island.Island, note);
        }

        private static string AddNote(string note, string more) => note.Length > 0 ? $"{note}. {more}" : more;

        private static double DistanceSquared(IslandShape island, double lon, double lat)
        {
            double best = island.Polys.Count > 0 ? PolygonMath.DistanceSquared(island.Polys, lon, lat) : double.MaxValue;
            foreach (var p in island.Points)
            {
                double dx = p[0] - lon, dy = p[1] - lat;
                best = Math.Min(best, dx * dx + dy * dy);
            }
            return best;
        }

        // The group on the QRZ record, if it's a real IOTA reference.
        internal static IotaMatch? FromQrz(string qrzIota, string reason)
        {
            if (qrzIota.Trim().Length == 0 || FindGroup(qrzIota) is not IotaGroup group) return null;
            string note = (reason.Length > 0 ? reason + " - " : "") + "IOTA reference from the QRZ record (user-entered)";
            return new IotaMatch(group.Ref, group.Name, "", note);
        }
    }

    // Keeps a copy of iota-world.org's IOTA list in %LocalAppData%\Callsign
    // Lookup, re-downloaded once a week, so groups IOTA adds turn up without a
    // new release. A failed download (offline, site down) just leaves the
    // saved copy - or the one shipped in Data\ - in use.
    public static class IotaListUpdater
    {
        private const string Url =
            "https://www.iota-world.org/islands-on-the-air/downloads/download-file.html?path=fulllist.json";

        // The list changes a few times a year at most.
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromDays(7);

        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            string version = typeof(IotaListUpdater).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"CallsignLookup/{version}");
            return http;
        }

        // True if a newer list was downloaded (and is now in use).
        public static async Task<bool> RefreshIfStaleAsync(CancellationToken cancellationToken = default)
        {
            string path = IotaService.DownloadedListPath;
            if (File.Exists(path) && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < RefreshInterval)
                return false;

            try
            {
                string json = await Http.GetStringAsync(Url, cancellationToken);
                IotaService.ParseList(json); // don't replace a good copy with a broken one

                string temp = path + ".tmp";
                await File.WriteAllTextAsync(temp, json, cancellationToken);
                File.Move(temp, path, overwrite: true);
                IotaService.ReloadList();
                return true;
            }
            catch (Exception ex) when (IotaService.IsBadList(ex)
                                       || ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}
