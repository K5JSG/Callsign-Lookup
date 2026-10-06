using System.Globalization;
using System.Text.Json;

namespace CallsignLookup.Services.Hrd
{
    // The name HRD Logbook gives each DXCC entity in its Country box
    // ("Fed. Republic of Germany" for 230), from Data\hrdCountries.json -
    // built from an HRD log by Tools\build_hrd_countries.py, as HRD keeps
    // its own list inside its program file.
    public static class HrdCountries
    {
        private static readonly Lazy<Dictionary<int, string>> Names = new(() =>
            JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(DataFiles.PathFor("hrdCountries.json")))!
                .ToDictionary(kv => int.Parse(kv.Key, CultureInfo.InvariantCulture), kv => kv.Value));

        // Null for an entity not in the file.
        public static string? Name(int dxcc) => Names.Value.TryGetValue(dxcc, out string? name) ? name : null;
    }
}
