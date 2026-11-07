using System.Net;

namespace CallsignLookup.Services.Upload
{
    // Sent: QRZ's logbook has the QSO (added, or its copy replaced). Reply is
    // QRZ's answer as it came ("RESULT=OK&LOGID=...&COUNT=1"), which HRD
    // keeps in the QSO's QRZ upload status.
    public sealed record QrzResult(bool Sent, string Message, string LogId, string Reply);

    // Sends QSOs to the QRZ.com logbook through its API, with the logbook's
    // API key (HRD's own, see HrdUploadSettings):
    //   POST https://logbook.qrz.com/api  KEY=... ACTION=INSERT OPTION=REPLACE ADIF=<record>
    // REPLACE makes QRZ swap its copy of a QSO it already has for this one,
    // so a corrected QSO replaces the old one instead of failing as a
    // duplicate. The key never goes anywhere but QRZ.
    public sealed class QrzLogbookSender
    {
        private static readonly Uri Api = new("https://logbook.qrz.com/api");
        private readonly HttpClient _http;

        public QrzLogbookSender(HttpClient? http = null)
        {
            _http = http ?? CreateClient();
        }

        private static HttpClient CreateClient()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            string version = typeof(QrzLogbookSender).Assembly.GetName().Version?.ToString(3) ?? "1.0";
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"CallsignLookup/{version}");
            return http;
        }

        public async Task<QrzResult> SendAsync(string apiKey, IReadOnlyDictionary<string, object?> row, CancellationToken token = default)
        {
            try
            {
                using var form = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["KEY"] = apiKey,
                    ["ACTION"] = "INSERT",
                    ["OPTION"] = "REPLACE",
                    ["ADIF"] = AdifWriter.FullRecord(row),
                });
                using var response = await _http.PostAsync(Api, form, token);
                string reply = (await response.Content.ReadAsStringAsync(token)).Trim();
                if (!response.IsSuccessStatusCode)
                    return new QrzResult(false, $"QRZ answered {(int)response.StatusCode} {response.ReasonPhrase}.", "", reply);
                return Interpret(reply);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !token.IsCancellationRequested)
            {
                return new QrzResult(false, $"Couldn't reach QRZ's logbook: {ex.Message}", "", "");
            }
        }

        // "RESULT=OK&LOGID=123&COUNT=1", "RESULT=REPLACE&LOGID=123&COUNT=1",
        // "RESULT=FAIL&REASON=...", "RESULT=AUTH&REASON=...".
        internal static QrzResult Interpret(string reply)
        {
            var parts = Parse(reply);
            string result = parts.GetValueOrDefault("RESULT", "").ToUpperInvariant();
            string logId = parts.GetValueOrDefault("LOGID", parts.GetValueOrDefault("LOGIDS", ""));
            string reason = parts.GetValueOrDefault("REASON", "");
            return result switch
            {
                "OK" => new QrzResult(true, "Sent to QRZ.", logId, reply),
                "REPLACE" => new QrzResult(true, "Sent to QRZ (replaced its copy).", logId, reply),
                "AUTH" => new QrzResult(false, "QRZ refused HRD's API key" + (reason.Length > 0 ? $": {reason}" : "."), "", reply),
                "FAIL" => new QrzResult(false, "QRZ didn't take it" + (reason.Length > 0 ? $": {reason}" : "."), "", reply),
                _ => new QrzResult(false, "QRZ gave an answer this app doesn't understand.", "", reply),
            };
        }

        internal static Dictionary<string, string> Parse(string reply)
        {
            var parts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string pair in reply.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                parts[pair[..eq].Trim()] = WebUtility.UrlDecode(pair[(eq + 1)..]).Trim();
            }
            return parts;
        }
    }
}
