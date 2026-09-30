using System.Text.Json;

namespace CallsignLookup.Services
{
    public sealed record ArrlSectionMatch(string Section, string Note = "");

    // ARRL/RAC section for a station, from Data\arrlSections.json (built from
    // https://www.arrl.org/section-boundaries and RAC's 2023 Ontario table).
    // Most US states are one section; CA, FL, MA, NJ, NY, PA, TX and WA are
    // split along county lines, so for those the county from
    // CountyLookupService decides it. Canadian sections follow provinces,
    // except Ontario's four (GH/ONE/ONN/ONS), which follow census divisions -
    // see OntarioDivisionService.
    public static class ArrlSectionService
    {
        private sealed class SectionTable
        {
            public Dictionary<string, string> States { get; set; } = new();
            public Dictionary<string, Dictionary<string, List<string>>> SplitStates { get; set; } = new();
            public Dictionary<string, string> Canada { get; set; } = new();
            public Dictionary<string, string> Ontario { get; set; } = new();
        }

        private static readonly Lazy<(SectionTable Table, Dictionary<(string State, string County), string> CountyIndex)> Data = new(() =>
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var table = JsonSerializer.Deserialize<SectionTable>(
                File.ReadAllText(DataFiles.PathFor("arrlSections.json")), options) ?? new SectionTable();

            var index = new Dictionary<(string, string), string>();
            foreach (var (state, sections) in table.SplitStates)
                foreach (var (section, counties) in sections)
                    foreach (var county in counties)
                        index[(state, Normalize(county))] = section;

            return (table, index);
        });

        // US station, from the county/state CountyLookupService found.
        public static ArrlSectionMatch? FromUsCounty(CountyMatch county)
        {
            var (table, countyIndex) = Data.Value;

            if (table.SplitStates.ContainsKey(county.StateAbbrev))
            {
                return countyIndex.TryGetValue((county.StateAbbrev, Normalize(county.County)), out var section)
                    ? new ArrlSectionMatch(section)
                    : null;
            }

            return table.States.TryGetValue(county.StateAbbrev, out var stateSection)
                ? new ArrlSectionMatch(stateSection)
                : null;
        }

        // Canadian station, from the province/territory on its QRZ record -
        // plus its location, which only Ontario needs.
        public static ArrlSectionMatch? FromCanadianStation(string province, double latitude, double longitude)
        {
            var table = Data.Value.Table;
            string p = province.Trim().ToUpperInvariant();

            if (p != "ON")
                return table.Canada.TryGetValue(p, out var section) ? new ArrlSectionMatch(section) : null;

            var division = OntarioDivisionService.FindDivision(latitude, longitude);
            if (division == null || !table.Ontario.TryGetValue(division.Name, out var ontarioSection))
                return null;

            if (division.Name == "Nipissing" && division.InOrSouthOfAlgonquinPark)
                return new ArrlSectionMatch("ONE", "Nipissing District, inside or south of Algonquin Park");

            return new ArrlSectionMatch(ontarioSection, $"Ontario census division: {division.Name}");
        }

        // "St. Johns" / "St Johns" / "DeWitt" / "De Witt" all compare equal.
        private static string Normalize(string county) =>
            new string(county.Where(char.IsLetter).Select(char.ToLowerInvariant).ToArray());
    }
}
