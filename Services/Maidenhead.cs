using System.Globalization;

namespace CallsignLookup.Services
{
    // Maidenhead locator math, both directions: lat/long -> 6-character grid
    // square (e.g. "EM12ab"), and grid square -> the lat/long of its center
    // (used as the fallback location when a QRZ record has a grid but no
    // lat/long of its own).
    public static class Maidenhead
    {
        public static string ToGridSquare(double latitude, double longitude)
        {
            if (double.IsNaN(latitude) || double.IsNaN(longitude))
                throw new ArgumentException("Latitude and longitude must be numbers.");

            // Shift to 0..360 / 0..180, keeping the exact north pole and the
            // antimeridian inside the last square instead of running off the end.
            double lon = Math.Clamp(longitude + 180.0, 0.0, 359.999999);
            double lat = Math.Clamp(latitude + 90.0, 0.0, 179.999999);

            int fieldLon = (int)(lon / 20);
            int fieldLat = (int)(lat / 10);
            lon -= fieldLon * 20;
            lat -= fieldLat * 10;

            int squareLon = (int)(lon / 2);
            int squareLat = (int)lat;
            lon -= squareLon * 2;
            lat -= squareLat;

            int subLon = (int)(lon * 12);   // 2 degrees / 24 subsquares
            int subLat = (int)(lat * 24);   // 1 degree / 24 subsquares

            return string.Concat(
                (char)('A' + fieldLon), (char)('A' + fieldLat),
                (char)('0' + squareLon), (char)('0' + squareLat),
                (char)('a' + subLon), (char)('a' + subLat));
        }

        // Center of a 4- or 6-character grid square (anything past 6
        // characters is ignored). Returns false for anything that isn't a
        // valid locator.
        public static bool TryGetCenter(string? grid, out double latitude, out double longitude)
        {
            latitude = longitude = 0;
            if (string.IsNullOrWhiteSpace(grid)) return false;

            string g = grid.Trim();
            if (g.Length < 4) return false;

            char f1 = char.ToUpperInvariant(g[0]), f2 = char.ToUpperInvariant(g[1]);
            if (f1 < 'A' || f1 > 'R' || f2 < 'A' || f2 > 'R') return false;
            if (!char.IsAsciiDigit(g[2]) || !char.IsAsciiDigit(g[3])) return false;

            double lon = (f1 - 'A') * 20 + (g[2] - '0') * 2;
            double lat = (f2 - 'A') * 10 + (g[3] - '0');

            if (g.Length >= 6)
            {
                char s1 = char.ToLowerInvariant(g[4]), s2 = char.ToLowerInvariant(g[5]);
                if (s1 < 'a' || s1 > 'x' || s2 < 'a' || s2 > 'x') return false;
                lon += (s1 - 'a') * (2.0 / 24) + (1.0 / 24);
                lat += (s2 - 'a') * (1.0 / 24) + (0.5 / 24);
            }
            else
            {
                lon += 1.0;
                lat += 0.5;
            }

            longitude = lon - 180.0;
            latitude = lat - 90.0;
            return true;
        }

        // A typed or pasted locator in the usual form - "AH16" or "AH16xx" -
        // cut to 6 characters. Doesn't check it's valid; TryGetCenter does.
        public static string Normalize(string grid)
        {
            string g = grid.Trim();
            if (g.Length > 6) g = g[..6];
            return g.Length > 4
                ? g[..4].ToUpperInvariant() + g[4..].ToLowerInvariant()
                : g.ToUpperInvariant();
        }

        public static string FormatCoordinate(double value) =>
            value.ToString("0.000000", CultureInfo.InvariantCulture);
    }
}
