using System.Globalization;
using System.Reflection;
using System.Xml.Linq;

namespace CallsignLookup.Services
{
    // The fields this app uses from a QRZ XML callsign record. Every field is
    // optional on QRZ's side (and several - lat/lon among them - are only sent
    // to XML Logbook Data subscribers), so all of them may be blank.
    public sealed class QrzCallsignRecord
    {
        public string Call { get; init; } = "";
        public string FirstName { get; init; } = "";
        public string LastName { get; init; } = "";
        public string Address1 { get; init; } = "";
        public string City { get; init; } = "";        // QRZ's "addr2"
        public string State { get; init; } = "";
        public string Zip { get; init; } = "";
        public string Country { get; init; } = "";
        public string County { get; init; } = "";
        public string Grid { get; init; } = "";
        public double? Latitude { get; init; }
        public double? Longitude { get; init; }
        public string CqZone { get; init; } = "";
        public string ItuZone { get; init; } = "";
        public string Iota { get; init; } = "";         // IOTA reference, e.g. "EU-005" (user-entered)
        // How QRZ derived lat/lon: user, geocode, grid, zip, state, dxcc or none.
        public string GeoLoc { get; init; } = "";

        public int? Dxcc { get; init; }                 // DXCC entity number of the callsign
        public string Land { get; init; } = "";         // DXCC entity name of the callsign

        public string FullName => $"{FirstName} {LastName}".Trim();

        // Every DXCC entity that has counties in counties.json: the lower 48
        // plus the entities that are separate for DXCC but still US states or
        // territories with Census county equivalents. Deliberately not the
        // other US possessions (Guantanamo Bay, Navassa, Desecheo, Baker &
        // Howland, Wake, Midway, Palmyra & Jarvis, Johnston, Kure) - they
        // have no county, and snapping them to the nearest one would be wrong.
        private static readonly Dictionary<int, string> UsCountyEntities = new()
        {
            [291] = "United States",
            [6] = "Alaska",
            [110] = "Hawaii",
            [202] = "Puerto Rico",
            [285] = "US Virgin Islands",
            [103] = "Guam",
            [9] = "American Samoa",
            [515] = "Swains Island",    // part of American Samoa
            [166] = "Mariana Islands",
        };

        private const int CanadaDxcc = 1;

        // Only used when QRZ sends no DXCC number: the DXCC entity name, or
        // failing that the QSL mailing address's country.
        private string EntityName => Land.Length > 0 ? Land : Country;

        public bool IsUnitedStates => Dxcc is int dxcc
            ? UsCountyEntities.ContainsKey(dxcc)
            : UsCountyEntities.Values.Contains(EntityName, StringComparer.OrdinalIgnoreCase);

        public bool IsCanadian => Dxcc is int dxcc
            ? dxcc == CanadaDxcc
            : EntityName.Equals("Canada", StringComparison.OrdinalIgnoreCase);
    }

    public sealed class QrzException(string message) : Exception(message);

    // Client for QRZ.com's XML data service (https://www.qrz.com/docs/xml/current_spec.html).
    // Logs in once for a session key, reuses it for every lookup, and logs in
    // again automatically when QRZ says the key has expired.
    public sealed class QrzService
    {
        private const string BaseUrl = "https://xmldata.qrz.com/xml/current/";

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

        // QRZ asks every client to identify itself with a program name/version.
        private static readonly string Agent =
            "CallsignLookup-" + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0");

        private readonly string _username;
        private readonly string _password;
        private string? _sessionKey;

        public QrzService(string username, string password)
        {
            _username = username;
            _password = password;
        }

        // Non-empty when QRZ's login response said something worth showing -
        // most usefully that the account has no XML subscription, which means
        // lookups come back without lat/lon.
        public string LoginMessage { get; private set; } = "";

        public async Task LoginAsync(CancellationToken cancellationToken = default)
        {
            string url = $"{BaseUrl}?username={Uri.EscapeDataString(_username)}" +
                         $"&password={Uri.EscapeDataString(_password)}&agent={Uri.EscapeDataString(Agent)}";
            var session = GetSession(await GetXmlAsync(url, cancellationToken));

            string key = Child(session, "Key");
            if (key.Length == 0)
            {
                string error = Child(session, "Error");
                throw new QrzException(error.Length > 0 ? error : "QRZ login failed with no error message.");
            }

            _sessionKey = key;
            LoginMessage = Child(session, "Message");
        }

        public async Task<QrzCallsignRecord> LookupAsync(string callsign, CancellationToken cancellationToken = default)
        {
            if (_sessionKey == null) await LoginAsync(cancellationToken);

            var doc = await QueryAsync(callsign, cancellationToken);
            string error = Child(GetSession(doc), "Error");

            // Session keys expire (and are invalidated by logging in elsewhere
            // with the same account) - get a new one and try once more.
            if (error.Contains("session", StringComparison.OrdinalIgnoreCase))
            {
                await LoginAsync(cancellationToken);
                doc = await QueryAsync(callsign, cancellationToken);
            }

            return ParseCallsignResponse(doc);
        }

        private async Task<XDocument> QueryAsync(string callsign, CancellationToken cancellationToken) =>
            await GetXmlAsync(
                $"{BaseUrl}?s={Uri.EscapeDataString(_sessionKey!)}&callsign={Uri.EscapeDataString(callsign)}",
                cancellationToken);

        // Separate from the HTTP call so it can be tested against sample XML.
        public static QrzCallsignRecord ParseCallsignResponse(XDocument doc)
        {
            var callsign = doc.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "Callsign");
            if (callsign == null)
            {
                string error = Child(GetSession(doc), "Error");
                throw new QrzException(error.Length > 0 ? error : "QRZ returned no callsign record.");
            }

            return new QrzCallsignRecord
            {
                Call = Child(callsign, "call"),
                FirstName = Child(callsign, "fname"),
                LastName = Child(callsign, "name"),
                Address1 = Child(callsign, "addr1"),
                City = Child(callsign, "addr2"),
                State = Child(callsign, "state"),
                Zip = Child(callsign, "zip"),
                Country = Child(callsign, "country"),
                County = Child(callsign, "county"),
                Grid = Child(callsign, "grid"),
                Latitude = ParseDouble(Child(callsign, "lat")),
                Longitude = ParseDouble(Child(callsign, "lon")),
                CqZone = Child(callsign, "cqzone"),
                ItuZone = Child(callsign, "ituzone"),
                Iota = Child(callsign, "iota"),
                GeoLoc = Child(callsign, "geoloc"),
                Land = Child(callsign, "land"),
                Dxcc = int.TryParse(Child(callsign, "dxcc"), NumberStyles.None, CultureInfo.InvariantCulture, out int dxcc) ? dxcc : null,
            };
        }

        public static QrzCallsignRecord ParseCallsignResponse(string xml) =>
            ParseCallsignResponse(XDocument.Parse(xml));

        private static async Task<XDocument> GetXmlAsync(string url, CancellationToken cancellationToken)
        {
            using var response = await Http.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            return XDocument.Parse(body);
        }

        private static XElement GetSession(XDocument doc) =>
            doc.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "Session")
            ?? throw new QrzException("QRZ response had no <Session> element.");

        // Matched by local name so the xmlns QRZ puts on every element (which
        // changes with the spec version) doesn't matter.
        private static string Child(XElement parent, string name) =>
            parent.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim() ?? "";

        private static double? ParseDouble(string text) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : null;
    }
}
