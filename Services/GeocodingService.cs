using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace CallsignLookup.Services
{
    // A place a geocoder found.
    public sealed record GeocodeMatch(double Latitude, double Longitude, string DisplayName, string Postcode, string City = "");

    public interface IGeocoder
    {
        // The best match for the query, or null. countryCodes: "us,ca" to limit
        // it, or empty for anywhere.
        Task<GeocodeMatch?> FindAsync(string query, string countryCodes, CancellationToken cancellationToken = default);
    }

    // OpenStreetMap's Nominatim search, the same free service the POTA
    // Activation Tool's map uses for addresses (MapAssets\rove.js). Its usage
    // policy asks for an identifying User-Agent, no more than one request a
    // second, and that results be cached - so this does all three. Only used
    // for a QSO whose QRZ record has no usable location.
    public sealed class NominatimGeocoder : IGeocoder
    {
        private const string SearchUrl = "https://nominatim.openstreetmap.org/search";
        private static readonly TimeSpan MinGap = TimeSpan.FromMilliseconds(1100);

        private static readonly HttpClient SharedHttp = CreateHttp();

        private readonly HttpClient _http;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly Dictionary<string, GeocodeMatch?> _cache = new(StringComparer.OrdinalIgnoreCase);
        private DateTime _lastRequest = DateTime.MinValue;

        public NominatimGeocoder(HttpClient? http = null) => _http = http ?? SharedHttp;

        private static HttpClient CreateHttp()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
            http.DefaultRequestHeaders.UserAgent.ParseAdd(
                $"CallsignLookup/{version} (K5JSG; https://github.com/K5JSG/Callsign-Lookup)");
            return http;
        }

        public async Task<GeocodeMatch?> FindAsync(string query, string countryCodes, CancellationToken cancellationToken = default)
        {
            string key = countryCodes + "|" + query.Trim();
            await _gate.WaitAsync(cancellationToken);
            try
            {
                if (_cache.TryGetValue(key, out var cached)) return cached;

                var wait = _lastRequest + MinGap - DateTime.UtcNow;
                if (wait > TimeSpan.Zero) await Task.Delay(wait, cancellationToken);

                string url = $"{SearchUrl}?format=jsonv2&addressdetails=1&limit=1&q={Uri.EscapeDataString(query.Trim())}" +
                             (countryCodes.Length > 0 ? $"&countrycodes={countryCodes}" : "");
                _lastRequest = DateTime.UtcNow;
                string json = await _http.GetStringAsync(url, cancellationToken);
                var match = ParseFirst(json);
                _cache[key] = match;
                return match;
            }
            finally
            {
                _gate.Release();
            }
        }

        // The first result of Nominatim's jsonv2 answer; null for none or
        // anything unexpected. Public so it's testable offline.
        public static GeocodeMatch? ParseFirst(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0) return null;
                var first = doc.RootElement[0];
                if (!double.TryParse(first.GetProperty("lat").GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double lat) ||
                    !double.TryParse(first.GetProperty("lon").GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double lon))
                    return null;

                string display = first.TryGetProperty("display_name", out var d) ? d.GetString() ?? "" : "";
                string postcode = "", city = "";
                if (first.TryGetProperty("address", out var a))
                {
                    if (a.TryGetProperty("postcode", out var p)) postcode = p.GetString() ?? "";
                    foreach (string key in new[] { "city", "town", "village", "municipality" })
                        if (city.Length == 0 && a.TryGetProperty(key, out var c)) city = c.GetString() ?? "";
                }
                return new GeocodeMatch(lat, lon, display, postcode, city);
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                return null;
            }
        }
    }
}
