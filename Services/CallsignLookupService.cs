namespace CallsignLookup.Services
{
    public sealed class LookupResult
    {
        public required QrzCallsignRecord Qrz { get; init; }

        // Null when QRZ gave neither a lat/lon nor a usable grid square.
        public double? Latitude { get; init; }
        public double? Longitude { get; init; }
        public string LocationSource { get; init; } = "";

        public string GridSquare { get; init; } = "";
        public CountyMatch? County { get; init; }
        public int? CqZone { get; init; }
        public int? ItuZone { get; init; }
        public ArrlSectionMatch? ArrlSection { get; init; }
        public IotaMatch? Iota { get; init; }

        // True when the location is QRZ's own (its lat/long or its grid square);
        // false when QRZ gave none - or a placeholder like AA00aa - and it's
        // from the station's address or there is none.
        public bool QrzHadLocation { get; init; }

        // Said when there's no usable location at all. Empty otherwise.
        public string LocationNote { get; set; } = "";
    }

    // The whole lookup: callsign -> QRZ record -> a location -> grid square,
    // county, CQ/ITU zone, ARRL section and IOTA island, all computed locally from that
    // location rather than trusted from QRZ's own (user-entered) fields.
    public sealed class CallsignLookupService(QrzService qrz, IGeocoder? geocoder = null)
    {
        // A portable call (KL4RL/W2, VE3/KL4RL, K5JSG/P) is looked up as it
        // is first, as it may have a QRZ page of its own. If not, QRZ
        // doesn't fall back to the home call by itself - for KL4RL/W2 it
        // says "Not found: W2" (tested 2026-10-06) - so the home call is
        // looked up next.
        public async Task<LookupResult> LookupAsync(string callsign, CancellationToken cancellationToken = default)
        {
            var result = await LookupOwnPageAsync(callsign, cancellationToken);
            if (result.Latitude != null) return result;

            // QRZ's record has no usable location (W7I's is the South Pole's AA00aa;
            // the web page's "QRZ Admin KO7T" isn't in QRZ's XML, tested 2026-10-12).
            // The record's own city, state and ZIP are the next best.
            result = await AddressLocator.UseAddressAsync(result, "", geocoder, cancellationToken);
            if (result.Latitude == null)
                result.LocationNote = $"QRZ has no usable location for {result.Qrz.Call}, so its grid, zones and county can't be worked out.";
            return result;
        }

        private async Task<LookupResult> LookupOwnPageAsync(string callsign, CancellationToken cancellationToken)
        {
            string call = callsign.Trim().ToUpperInvariant();
            string home = HomeCall(call);
            if (home == call) return Resolve(await qrz.LookupAsync(call, cancellationToken));

            QrzCallsignRecord? record = null;
            try
            {
                record = await qrz.LookupAsync(call, cancellationToken);
            }
            catch (QrzException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
            {
            }
            // A page for some other part of the call ("W2") isn't this station.
            if (record == null || !(record.Call.Equals(call, StringComparison.OrdinalIgnoreCase) ||
                                    record.Call.Equals(home, StringComparison.OrdinalIgnoreCase)))
                record = await qrz.LookupAsync(home, cancellationToken);
            return Resolve(record);
        }

        // The home call in a portable one: the longest part (KL4RL in
        // KL4RL/W2 or VE3/KL4RL, K5JSG in K5JSG/P). The call itself if it
        // has no "/".
        public static string HomeCall(string call) =>
            call.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .OrderByDescending(part => part.Length)
                .FirstOrDefault() ?? call;

        // Everything after the QRZ call - public so it's testable offline.
        public static LookupResult Resolve(QrzCallsignRecord record)
        {
            double? lat = record.Latitude, lon = record.Longitude;
            string source;

            // A QRZ record with nowhere real to put it (W7I's is grid AA00aa, at
            // the South Pole) is a record with no location, not one there.
            if (lat.HasValue && lon.HasValue && IsPlaceholderLocation(lat.Value, lon.Value)) lat = lon = null;
            bool usableGrid = !IsPlaceholderGrid(record.Grid);

            if (lat.HasValue && lon.HasValue)
            {
                source = "QRZ lat/long" + (record.GeoLoc.Length > 0 ? $" (from {DescribeGeoLoc(record.GeoLoc)})" : "");
            }
            else if (usableGrid && Maidenhead.TryGetCenter(record.Grid, out double gridLat, out double gridLon))
            {
                // No lat/long (typically an account without an XML subscription) -
                // the center of the QRZ grid square is the next best thing.
                lat = gridLat;
                lon = gridLon;
                source = $"Center of QRZ grid {record.Grid} (approximate - no lat/long on the QRZ record)";
            }
            else
            {
                return new LookupResult
                {
                    Qrz = record,
                    LocationSource = "None - QRZ record has no lat/long or grid square",
                    Iota = IotaService.FromQrz(record.Iota, ""),
                };
            }

            return Resolve(record, lat.Value, lon.Value, source, Maidenhead.ToGridSquare(lat.Value, lon.Value), qrzHadLocation: true);
        }

        // 0,0 ("null island"), the JJ00 grid square around it (open ocean off West
        // Africa - W6Z's QRZ record has JJ00aa, whose centre is 0.0208,0.0417, not
        // exactly 0,0) and the AA00 grid square at the South Pole - what QRZ and
        // loggers put when there's no real location.
        internal static bool IsPlaceholderLocation(double lat, double lon) =>
            (Math.Abs(lat) < 0.01 && Math.Abs(lon) < 0.01) ||
            (lat is >= 0 and < 1 && lon is >= 0 and < 2) ||
            (lat <= -89 && lon <= -178);

        // The same, as a grid square: AA00xx (South Pole) or JJ00xx (null island).
        internal static bool IsPlaceholderGrid(string grid) =>
            grid.StartsWith("AA00", StringComparison.OrdinalIgnoreCase) ||
            grid.StartsWith("JJ00", StringComparison.OrdinalIgnoreCase);

        // The same, but from a grid square the user typed in place of the
        // QRZ record's location (FW1P: the record's grid is on Futuna, the
        // station is on Wallis). Everything else - DXCC entity, state, the
        // QRZ IOTA field - still comes from the record. Null if the grid
        // isn't a valid 4- or 6-character locator.
        public static LookupResult? ResolveAtGrid(QrzCallsignRecord record, string grid)
        {
            string normalized = Maidenhead.Normalize(grid);
            if (!Maidenhead.TryGetCenter(normalized, out double lat, out double lon)) return null;
            return Resolve(record, lat, lon, $"Center of grid {normalized} (entered, not from QRZ)", normalized);
        }

        // The same for a location that isn't the QRZ record's - a POTA park
        // the station worked from.
        public static LookupResult ResolveAt(QrzCallsignRecord record, double lat, double lon, string source) =>
            Resolve(record, lat, lon, source, Maidenhead.ToGridSquare(lat, lon));

        private static LookupResult Resolve(QrzCallsignRecord record, double lat, double lon, string source, string gridSquare, bool qrzHadLocation = false)
        {
            // Counties only for US stations - and for those, always one (the
            // nearest, if the point is just outside every outline). Anywhere
            // else, Canada included, has no county.
            CountyMatch? county = null;
            ArrlSectionMatch? section = null;
            if (record.IsUnitedStates)
            {
                county = CountyLookupService.FindCounty(lat, lon, record.State);
                if (county != null) section = ArrlSectionService.FromUsCounty(county);
            }
            else if (record.IsCanadian && record.State.Length > 0)
            {
                section = ArrlSectionService.FromCanadianStation(record.State, lat, lon);
            }

            return new LookupResult
            {
                Qrz = record,
                Latitude = lat,
                Longitude = lon,
                LocationSource = source,
                QrzHadLocation = qrzHadLocation,
                GridSquare = gridSquare,
                County = county,
                CqZone = ZoneLookupService.FindCqZone(lat, lon),
                ItuZone = ZoneLookupService.FindItuZone(lat, lon),
                ArrlSection = section,
                Iota = IotaService.Find(lat, lon, record.Dxcc, record.Iota),
            };
        }

        private static string DescribeGeoLoc(string geoLoc) => geoLoc.ToLowerInvariant() switch
        {
            "user" => "user-entered",
            "geocode" => "geocoded address",
            "grid" => "grid square",
            "zip" => "ZIP code - approximate",
            "state" => "state - approximate",
            "dxcc" => "country - very approximate",
            _ => geoLoc,
        };
    }
}
