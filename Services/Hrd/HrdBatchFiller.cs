using System.Globalization;
using System.Text;

namespace CallsignLookup.Services.Hrd
{
    // One QSO in a batch: what was found, and what would change.
    public sealed class HrdBatchItem
    {
        public long Pk { get; init; }
        public string Call { get; init; } = "";
        public string When { get; init; } = "";

        // Why there's nothing to apply (not in the log, QRZ has no page...);
        // null if the QSO was checked.
        public string? Skipped { get; init; }
        public HrdRowChanges? Changes { get; init; }

        // Ticked in the preview to start with: it has changes and QRZ's record
        // is for this callsign. One whose QRZ record is for another callsign
        // needs the user to look first (as in the single fill's prompt).
        public bool TickedAtStart { get; init; }

        public bool Applicable => Skipped == null && Changes is { Any: true };
    }

    public sealed class HrdBatchResult
    {
        public string BackupPath { get; set; } = "";
        public List<HrdBatchItem> Applied { get; } = [];
        public List<(HrdBatchItem Item, string Why)> NotApplied { get; } = [];

        // Written, but didn't read back as written - the backup has the original.
        public List<(HrdBatchItem Item, string Why)> Failed { get; } = [];
        public bool Refreshed { get; set; }
        public string RefreshMessage { get; set; } = "";
    }

    // The batch fill: for QSOs picked in HRD's log, look each callsign up on
    // QRZ, work out what the single "Fill HRD QSO" would do to it (the same
    // HrdQsoFiller.Plan, fed from the log row instead of HRD's window), and -
    // once the user has seen the preview and ticked what they want - write
    // those changes straight into the log: backup first, one transaction,
    // read back to check, then ask HRD to reload.
    public sealed class HrdBatchFiller(HrdLogDatabase log, CallsignLookupService lookup, IGeocoder? geocoder = null)
    {
        // Gives up after this many QRZ failures in a row that aren't "not found"
        // (no network, QRZ down) rather than lookup them all.
        private const int MaxConsecutiveNetworkFailures = 3;

        public async Task<List<HrdBatchItem>> PrepareAsync(
            IReadOnlyList<long> pks, HrdStationProfile? profile, bool clearQslVia,
            IProgress<(int Done, int Total, string Call)>? progress, CancellationToken cancellationToken)
        {
            var rows = log.ReadRows(pks);
            var lookups = new Dictionary<string, (LookupResult? Result, string? Error)>();
            var continents = new Dictionary<int, string?>();
            string? Continent(int dxcc)
            {
                if (!continents.TryGetValue(dxcc, out string? continent)) continents[dxcc] = continent = log.ContinentFor(dxcc);
                return continent;
            }

            var items = new List<HrdBatchItem>();
            int networkFailures = 0;
            for (int i = 0; i < pks.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long pk = pks[i];
                if (!rows.TryGetValue(pk, out var row))
                {
                    items.Add(new HrdBatchItem { Pk = pk, Skipped = "not in the log" });
                    continue;
                }

                string call = Text(row, "COL_CALL").ToUpperInvariant();
                string when = (Text(row, "COL_QSO_DATE") + " " + Text(row, "COL_TIME_ON")).Trim();
                if (when.Length > 16) when = when[..16];
                progress?.Report((i, pks.Count, call));
                if (call.Length == 0)
                {
                    items.Add(new HrdBatchItem { Pk = pk, When = when, Skipped = "no callsign" });
                    continue;
                }

                if (!lookups.TryGetValue(call, out var found))
                {
                    try
                    {
                        found = (await lookup.LookupAsync(call, cancellationToken), null);
                        networkFailures = 0;
                    }
                    catch (QrzException ex)
                    {
                        found = (null, "QRZ: " + ex.Message);
                        // A bad login fails every lookup the same way - no point going on.
                        if (!ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                            && ++networkFailures >= MaxConsecutiveNetworkFailures)
                            throw;
                    }
                    catch (Exception ex) when (ex is HttpRequestException or System.Xml.XmlException
                                                   or TaskCanceledException && !cancellationToken.IsCancellationRequested)
                    {
                        if (++networkFailures >= MaxConsecutiveNetworkFailures)
                            throw new HttpRequestException($"Couldn't reach QRZ: {ex.Message}", ex);
                        found = (null, "couldn't reach QRZ: " + ex.Message);
                    }
                    lookups[call] = found;
                }

                if (found.Result is not LookupResult result)
                {
                    items.Add(new HrdBatchItem { Pk = pk, Call = call, When = when, Skipped = found.Error });
                    continue;
                }

                // QRZ has no location for this station: the QSO's address in the log (which
                // the user can correct in HRD) locates it instead.
                result = await AddressLocator.UseAddressAsync(result, Text(row, "COL_ADDRESS"), geocoder, cancellationToken);
                var plan = HrdQsoFiller.Plan(HrdRowMapper.ToQsoFields(row), result, HrdPotaParks.Find, profile, clearQslVia);
                var changes = HrdRowMapper.ToColumnUpdates(plan, row, Continent);
                items.Add(new HrdBatchItem
                {
                    Pk = pk, Call = call, When = when, Changes = changes,
                    TickedAtStart = changes.Any && HrdQsoFiller.SameStation(call, result.Qrz.Call),
                });
            }
            progress?.Report((pks.Count, pks.Count, ""));
            return items;
        }

        // Writes the chosen QSOs. Each one is first checked against the log as
        // it is now: if HRD (or anyone) changed it since the preview, it's not
        // written - the preview's "was" would be a lie. Everything else goes
        // in one transaction after a backup, then is read back.
        public HrdBatchResult Apply(IReadOnlyList<HrdBatchItem> chosen)
        {
            var result = new HrdBatchResult();
            var current = log.ReadRows(chosen.Select(c => c.Pk));
            var toWrite = new List<HrdBatchItem>();
            foreach (var item in chosen.Where(c => c.Applicable))
            {
                if (!current.TryGetValue(item.Pk, out var row))
                    result.NotApplied.Add((item, "no longer in the log"));
                else if (item.Changes!.Changes.Any(c => !HrdRowMapper.SameValue(row.GetValueOrDefault(c.Column), c.Old, 1e-6)))
                    result.NotApplied.Add((item, "changed in HRD since the preview"));
                else
                    toWrite.Add(item);
            }
            if (toWrite.Count == 0) return result;

            result.BackupPath = log.Backup();
            log.Apply(toWrite.Select(i => new HrdRowUpdate(i.Pk, i.Changes!.Columns)));

            var after = log.ReadRows(toWrite.Select(i => i.Pk));
            foreach (var item in toWrite)
            {
                var wrong = item.Changes!.Changes.FirstOrDefault(c =>
                    !after.TryGetValue(item.Pk, out var row) || !HrdRowMapper.SameValue(row.GetValueOrDefault(c.Column), c.New, 1e-6));
                if (wrong != null) result.Failed.Add((item, $"{wrong.Label} didn't read back as written"));
                else result.Applied.Add(item);
            }
            return result;
        }

        // Puts back what a batch wrote: each QSO's columns go back to what they
        // were before, if they're still what the batch wrote (otherwise
        // someone's changed them since and they're left). A backup first, as
        // for the batch itself. The result's Applied items are the reverse of
        // the batch's - "was" is what the batch wrote, "now" what's back.
        public HrdBatchResult Undo(HrdBatchResult batch)
        {
            var result = new HrdBatchResult();
            var current = log.ReadRows(batch.Applied.Select(i => i.Pk));
            var toRevert = new List<HrdBatchItem>();
            foreach (var item in batch.Applied)
            {
                if (!current.TryGetValue(item.Pk, out var row))
                    result.NotApplied.Add((item, "no longer in the log"));
                else if (item.Changes!.Changes.Any(c => !HrdRowMapper.SameValue(row.GetValueOrDefault(c.Column), c.New, 1e-6)))
                    result.NotApplied.Add((item, "changed since the batch wrote it"));
                else
                    toRevert.Add(Reverse(item));
            }
            if (toRevert.Count == 0) return result;

            result.BackupPath = log.Backup();
            log.Apply(toRevert.Select(i => new HrdRowUpdate(i.Pk, i.Changes!.Columns)));

            var after = log.ReadRows(toRevert.Select(i => i.Pk));
            foreach (var item in toRevert)
            {
                var wrong = item.Changes!.Changes.FirstOrDefault(c =>
                    !after.TryGetValue(item.Pk, out var row) || !HrdRowMapper.SameValue(row.GetValueOrDefault(c.Column), c.New, 1e-6));
                if (wrong != null) result.Failed.Add((item, $"{wrong.Label} didn't read back as it was"));
                else result.Applied.Add(item);
            }
            return result;
        }

        // The same QSO with every change turned round.
        private static HrdBatchItem Reverse(HrdBatchItem item)
        {
            var reversed = new HrdRowChanges();
            foreach (var c in item.Changes!.Changes) reversed.Changes.Add(new HrdColumnChange(c.Label, c.Column, c.New, c.Old));
            return new HrdBatchItem { Pk = item.Pk, Call = item.Call, When = item.When, Changes = reversed };
        }

        // The result as text, to show and to copy.
        public static string Summary(HrdBatchResult result, string logPath, bool undo = false)
        {
            var text = new StringBuilder();
            text.AppendLine($"{(undo ? "Undo of batch fill" : "Batch fill")} - {DateTime.Now:yyyy-MM-dd HH:mm}");
            text.AppendLine($"Log: {logPath}");
            if (result.BackupPath.Length > 0) text.AppendLine($"Backup: {result.BackupPath}");
            text.AppendLine();
            text.AppendLine($"{(undo ? "Put back on" : "Applied to")} {result.Applied.Count} QSO{(result.Applied.Count == 1 ? "" : "s")}.");
            foreach (var item in result.Applied)
            {
                text.AppendLine($"  {item.Call} {item.When}");
                foreach (var change in item.Changes!.Changes)
                    text.AppendLine($"      {change.Label}: {Show(change.Old)} -> {Show(change.New)}");
            }

            if (result.NotApplied.Count > 0)
            {
                text.AppendLine().AppendLine("Not applied:");
                foreach (var (item, why) in result.NotApplied) text.AppendLine($"  {item.Call} {item.When} - {why}");
            }
            if (result.Failed.Count > 0)
            {
                text.AppendLine().AppendLine("Written, but didn't read back as written (restore the backup if in doubt):");
                foreach (var (item, why) in result.Failed) text.AppendLine($"  {item.Call} {item.When} - {why}");
            }

            text.AppendLine().AppendLine(result.Applied.Count == 0 && result.Failed.Count == 0
                ? "Nothing was written."
                : result.Refreshed
                    ? "HRD was told to reload the log."
                    : $"{(result.RefreshMessage.Length > 0 ? result.RefreshMessage + " " : "")}Refresh HRD's log (or restart HRD) to see the changes.");
            return text.ToString();
        }

        // "(blank)" for nothing, numbers the way the log shows them.
        public static string Show(object? value) => value switch
        {
            null => "(blank)",
            string { Length: 0 } => "(blank)",
            string s => s,
            double d => d.ToString("0.######", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
        };

        private static string Text(IReadOnlyDictionary<string, object?> row, string column) =>
            row.TryGetValue(column, out object? value) ? Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? "" : "";
    }
}
