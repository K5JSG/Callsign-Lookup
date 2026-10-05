using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CallsignLookup.Services.Hrd
{
    // LocationDesc is POTA's "US-TX" style location.
    public sealed record PotaPark(string Reference, string Name, string LocationDesc, double Latitude, double Longitude, string Grid);

    // The POTA park list HRD Logbook downloads every day for its POTA "..."
    // picker (%AppData%\HRDLLC\HRDCommon\all_parks_ext.csv, ~95,000 parks).
    // Using HRD's own copy means a park is found exactly when HRD's picker
    // would find it.
    public static partial class HrdPotaParks
    {
        public static string ListPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HRDLLC", "HRDCommon", "all_parks_ext.csv");

        private static Dictionary<string, PotaPark>? _parks;
        private static DateTime _loadedFileTime;

        // Null when the park isn't on the list (or there's no list).
        public static PotaPark? Find(string reference)
        {
            var parks = Load();
            return parks.TryGetValue(reference.Trim(), out var park) ? park : null;
        }

        private static Dictionary<string, PotaPark> Load()
        {
            DateTime fileTime = File.Exists(ListPath) ? File.GetLastWriteTimeUtc(ListPath) : DateTime.MinValue;
            if (_parks != null && fileTime == _loadedFileTime) return _parks;

            var parks = new Dictionary<string, PotaPark>(StringComparer.OrdinalIgnoreCase);
            if (fileTime != DateTime.MinValue)
            {
                using var reader = new StreamReader(ListPath, Encoding.UTF8);
                reader.ReadLine(); // header
                string? line;
                while ((line = reader.ReadLine()) != null)
                    if (ParseLine(line) is PotaPark park) parks[park.Reference] = park;
            }
            _parks = parks;
            _loadedFileTime = fileTime;
            return parks;
        }

        // "reference","name","active","entityId","locationDesc","latitude","longitude","grid"
        internal static PotaPark? ParseLine(string line)
        {
            var f = SplitCsv(line);
            if (f.Count < 8) return null;
            if (!double.TryParse(f[5], NumberStyles.Float, CultureInfo.InvariantCulture, out double lat) ||
                !double.TryParse(f[6], NumberStyles.Float, CultureInfo.InvariantCulture, out double lon))
                return null;
            return new PotaPark(f[0].Trim(), f[1].Trim(), f[4].Trim(), lat, lon, f[7].Trim());
        }

        private static List<string> SplitCsv(string line)
        {
            var fields = new List<string>();
            var sb = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else if (c == '"') quoted = false;
                    else sb.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
            fields.Add(sb.ToString());
            return fields;
        }

        // Park references the user parks in the Comment until the POTA field
        // is filled: "US-3033", "POTA: US-3033", "POTA: US-4579 & US-4566",
        // "US-10236, US-7442". Returns the references in order and the comment
        // with them (and a leading "POTA:") taken out. Only references isPark
        // accepts are taken - if anything that just looks like one is there,
        // it's in Unknown and the comment is left alone.
        public static (List<string> References, string RemainingComment, List<string> Unknown) TakeReferences(
            string comment, Func<string, bool> isPark)
        {
            var refs = ParkReference().Matches(comment).Select(m => m.Value.ToUpperInvariant()).Distinct().ToList();
            var unknown = refs.Where(r => !isPark(r)).ToList();
            if (refs.Count == 0 || unknown.Count > 0) return ([], comment, unknown);

            // The whole list - "POTA: US-4579 & US-4566 & US-4576" - comes out
            // as one piece, so its "&" / "," / "and" go with it.
            string rest = ReferenceList().Replace(comment, " ");
            rest = Regex.Replace(rest, @"\s{2,}", " ").Trim().Trim(',', ';', '-', ' ');
            return (refs, rest, []);
        }

        // POTA references: a 1-4 character program prefix and a 4-5 digit park
        // number - but not part of a longer dash-joined run like the date in
        // "Requested QSL Via Bureau From ClubLog on 09-DEC-2024".
        private const string Ref = @"(?<![-\w])[A-Z0-9]{1,4}-\d{4,5}(?![-\w])";

        [GeneratedRegex(Ref, RegexOptions.IgnoreCase)]
        private static partial Regex ParkReference();

        [GeneratedRegex(@"(?:\bPOTA\b\s*:?\s*)?" + Ref + @"(?:\s*(?:,|&|;|/|\+|\band\b)?\s*" + Ref + ")*", RegexOptions.IgnoreCase)]
        private static partial Regex ReferenceList();
    }
}
