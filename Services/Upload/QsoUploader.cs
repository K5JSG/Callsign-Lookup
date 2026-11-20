using CallsignLookup.Services.Hrd;

namespace CallsignLookup.Services.Upload
{
    // What to send: LotwLocations gives the TQSL location for each QSO to
    // send to LoTW (a QSO not in it isn't sent there). QrzPks are the QSOs
    // to send to QRZ.
    public sealed record UploadRequest(
        IReadOnlyDictionary<long, TqslLocation> LotwLocations, IReadOnlyCollection<long> QrzPks, string? LotwPassword = null);

    public sealed class UploadReport
    {
        public List<(long Pk, string Location, LotwResult Result)> Lotw { get; } = [];
        public List<(long Pk, QrzResult Result)> Qrz { get; } = [];

        // TQSL wanted a password it wasn't given: these QSOs, by location,
        // weren't sent and can be tried again with one.
        public Dictionary<long, TqslLocation> LotwNeedingPassword { get; } = [];

        // Writing HRD's sent flags failed (the uploads themselves went).
        public string FlagError { get; set; } = "";

        public int LotwSent => Lotw.Count(l => l.Result.Sent);
        public int QrzSent => Qrz.Count(q => q.Result.Sent);
    }

    // Sends QSOs from HRD's log to LoTW and QRZ the way HRD does, then sets
    // HRD's own "sent" flags on the ones that went, in one transaction (the
    // caller has already made a backup of the log).
    public sealed class QsoUploader
    {
        private readonly HrdLogDatabase _log;
        private readonly Func<string, TqslLocation, IReadOnlyList<IReadOnlyDictionary<string, object?>>, string?, CancellationToken, Task<LotwResult>> _sendLotw;
        private readonly Func<string, IReadOnlyDictionary<string, object?>, CancellationToken, Task<QrzResult>> _sendQrz;
        private readonly Func<string, string> _qrzKey;
        private readonly Func<string?> _tqslPath;

        public QsoUploader(HrdLogDatabase log)
            : this(log,
                (tqsl, location, rows, password, token) => LotwSender.SendAsync(tqsl, location.Name, rows, password, token),
                new QrzLogbookSender().SendAsync,
                HrdUploadSettings.QrzApiKey,
                HrdUploadSettings.TqslPath)
        {
        }

        internal QsoUploader(
            HrdLogDatabase log,
            Func<string, TqslLocation, IReadOnlyList<IReadOnlyDictionary<string, object?>>, string?, CancellationToken, Task<LotwResult>> sendLotw,
            Func<string, IReadOnlyDictionary<string, object?>, CancellationToken, Task<QrzResult>> sendQrz,
            Func<string, string> qrzKey,
            Func<string?> tqslPath)
        {
            _log = log;
            _sendLotw = sendLotw;
            _sendQrz = sendQrz;
            _qrzKey = qrzKey;
            _tqslPath = tqslPath;
        }

        public async Task<UploadReport> SendAsync(UploadRequest request, IProgress<string>? progress = null, CancellationToken token = default)
        {
            var report = new UploadReport();
            var rows = _log.ReadRows(request.LotwLocations.Keys.Concat(request.QrzPks));
            var flags = new Dictionary<long, Dictionary<string, object?>>();
            DateTime today = DateTime.UtcNow.Date;

            // The row is updated too, so a QSO sent to LoTW goes to QRZ with
            // LoTW marked as sent.
            void Flag(long pk, Dictionary<string, object?> columns)
            {
                if (!flags.TryGetValue(pk, out var all)) flags[pk] = all = [];
                foreach (var (column, value) in columns) all[column] = rows[pk][column] = value;
            }

            // LoTW first, so QRZ gets the QSO already marked as sent to LoTW.
            // One TQSL run per location.
            if (request.LotwLocations.Count > 0)
            {
                string? tqsl = _tqslPath();
                foreach (var group in request.LotwLocations.Where(l => rows.ContainsKey(l.Key))
                             .GroupBy(l => l.Value.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var location = group.First().Value;
                    var pks = group.Select(g => g.Key).ToList();
                    LotwResult result;
                    if (tqsl == null)
                    {
                        result = new LotwResult(false, false, -1, "TQSL isn't installed (HRD's TQSL setting points nowhere).");
                    }
                    else
                    {
                        progress?.Report($"Sending {pks.Count} QSO{(pks.Count == 1 ? "" : "s")} to LoTW as \"{location.Name}\"...");
                        result = await _sendLotw(tqsl, location, pks.Select(pk => (IReadOnlyDictionary<string, object?>)rows[pk]).ToList(),
                            request.LotwPassword, token);
                    }
                    foreach (long pk in pks)
                    {
                        report.Lotw.Add((pk, location.Name, result));
                        if (result.Sent) Flag(pk, HrdUploadFlags.LotwSent(today));
                        else if (result.NeedsPassword) report.LotwNeedingPassword[pk] = location;
                    }
                }
            }

            // QRZ: one QSO at a time, each with its station's key.
            foreach (long pk in request.QrzPks.Distinct())
            {
                if (!rows.TryGetValue(pk, out var row)) continue;
                string station = Text(row, "COL_STATION_CALLSIGN");
                if (station.Length == 0) station = Text(row, "COL_OPERATOR");
                string key = _qrzKey(station);
                if (key.Length == 0)
                {
                    report.Qrz.Add((pk, new QrzResult(false, $"HRD has no QRZ logbook API key for {station}.", "", "")));
                    continue;
                }
                progress?.Report($"Sending {Text(row, "COL_CALL")} to QRZ...");
                var result = await _sendQrz(key, row, token);
                report.Qrz.Add((pk, result));
                if (result.Sent) Flag(pk, HrdUploadFlags.QrzSent(today, result.Reply));
            }

            if (flags.Count > 0)
            {
                try
                {
                    _log.Apply(flags.Select(f => new HrdRowUpdate(f.Key, f.Value)));
                }
                catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or HrdException or IOException)
                {
                    report.FlagError = ex.Message;
                }
            }
            return report;
        }

        private static string Text(IReadOnlyDictionary<string, object?> row, string column) =>
            row.TryGetValue(column, out object? value) ? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)?.Trim() ?? "" : "";
    }
}
