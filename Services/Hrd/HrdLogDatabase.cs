using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;

namespace CallsignLookup.Services.Hrd
{
    // The columns to write for one QSO, by log column name.
    public sealed record HrdRowUpdate(long Pk, IReadOnlyDictionary<string, object?> Columns);

    // HRD Logbook's log: a SQLite file (WAL mode) with one table of QSOs, one
    // column per ADIF-ish field (COL_*). Used by the batch fill, which writes
    // changes straight into it - so every write is preceded by a backup, done
    // in one transaction, and checked afterwards. Tested 2026-10-03: a write
    // while HRD has the log open is fine, and HRD shows it after a refresh;
    // but an open Add/Edit window saves the whole QSO as it loaded it, so it
    // would undo a change made meanwhile (the caller checks for those first).
    public sealed class HrdLogDatabase
    {
        public const string Table = "TABLE_HRD_CONTACTS_V07";
        public const int BackupsToKeep = 10;
        public const string BatchFillPurpose = "batch fill";
        public const string FillQsoPurpose = "Fill HRD QSO";
        private static string BackupSuffix(string purpose) => $" before Callsign Lookup {purpose}.hrdsql";
        private const int IpCliPort = 7826;

        private static readonly Regex ColumnName = new("^COL_[A-Z0-9_]+$", RegexOptions.Compiled);

        public string FilePath { get; }

        // HRD's name for the log ("K5JSG"), which IPCli's db refresh takes.
        public string Title { get; }

        public HrdLogDatabase(string filePath, string title)
        {
            FilePath = filePath;
            Title = title;
        }

        public static string DatabasesFile => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HRDLLC", "HRD Logbook", "LogbookDatabases8.xml");

        // HRD's local SQLite logs (title and file), in HRD's order. Empty, with
        // the reason, when the list can't be read.
        public static List<(string Title, string Path)> ListLogs(out string error, string? databasesFile = null)
        {
            error = "";
            databasesFile ??= DatabasesFile;
            try
            {
                if (!File.Exists(databasesFile))
                {
                    error = "Couldn't find HRD Logbook's list of logbooks - is HRD Logbook installed?";
                    return new();
                }

                var logs = XDocument.Load(databasesFile).Descendants("Database")
                    .Where(d => (string?)d.Attribute("DatabaseType") == "Local/SQLite")
                    .Select(d => (Title: (string?)d.Attribute("Title") ?? "", Path: (string?)d.Attribute("Database") ?? ""))
                    .Where(d => d.Path.Length > 0)
                    .ToList();
                if (logs.Count == 0) error = "HRD Logbook has no local SQLite log.";
                return logs;
            }
            catch (Exception ex) when (ex is IOException or System.Xml.XmlException or UnauthorizedAccessException)
            {
                error = $"Couldn't read HRD's list of logbooks: {ex.Message}";
                return new();
            }
        }

        // The log to fill: the only local log, or with several, the one picked
        // in the "HRD log" drop-down (by title). Null, with the reason, when
        // there are several and none is picked - this never guesses which.
        public static HrdLogDatabase? Find(string? chosenTitle, out string error, string? databasesFile = null)
        {
            var logs = ListLogs(out error, databasesFile);
            if (logs.Count == 0) return null;

            var log = logs.Count == 1 ? logs[0] : logs.FirstOrDefault(l => l.Title == chosenTitle);
            if (log.Path == null)
            {
                error = $"HRD Logbook has {logs.Count} local logs ({string.Join(", ", logs.Select(l => l.Title))}). " +
                        "Pick the one to fill in the \"HRD log\" drop-down.";
                return null;
            }
            if (!File.Exists(log.Path))
            {
                error = $"HRD's log file isn't there: {log.Path}";
                return null;
            }
            return new HrdLogDatabase(log.Path, log.Title);
        }

        private SqliteConnection Open(bool writable)
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = FilePath,
                Mode = writable ? SqliteOpenMode.ReadWrite : SqliteOpenMode.ReadOnly,
                Pooling = false,
                DefaultTimeout = 30,
            };
            var connection = new SqliteConnection(builder.ToString());
            connection.Open();
            return connection;
        }

        // The QSOs with these keys, every column, by key. Keys not in the log
        // are left out.
        public Dictionary<long, Dictionary<string, object?>> ReadRows(IEnumerable<long> pks)
        {
            var rows = new Dictionary<long, Dictionary<string, object?>>();
            using var connection = Open(false);
            foreach (long[] chunk in pks.Distinct().Chunk(500))
            {
                using var command = connection.CreateCommand();
                command.CommandText = $"select * from {Table} where COL_PRIMARY_KEY in " +
                                      $"({string.Join(",", chunk.Select((_, i) => "$p" + i))})";
                for (int i = 0; i < chunk.Length; i++) command.Parameters.AddWithValue("$p" + i, chunk[i]);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var row = new Dictionary<string, object?>(StringComparer.Ordinal);
                    for (int i = 0; i < reader.FieldCount; i++)
                        row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    rows[Convert.ToInt64(row["COL_PRIMARY_KEY"])] = row;
                }
            }
            return rows;
        }

        // The keys of the QSOs with this callsign, on one of these dates
        // ("yyyy-mm-dd"), at this time. HRD keeps the time as "hh:mm:ss" or
        // "hh:mm:ss.000", so a time with seconds is matched to the second and
        // one without to the minute.
        public List<long> FindQsos(string call, IEnumerable<string> dates, string time)
        {
            var found = new List<long>();
            string wanted = new(time.Where(char.IsAsciiDigit).ToArray());
            if (call.Trim().Length == 0 || wanted.Length is not (4 or 6)) return found;

            using var connection = Open(false);
            foreach (string date in dates.Distinct())
            {
                using var command = connection.CreateCommand();
                command.CommandText = $"select COL_PRIMARY_KEY, COL_TIME_ON from {Table} " +
                                      "where upper(trim(COL_CALL)) = $call and substr(COL_QSO_DATE, 1, 10) = $date";
                command.Parameters.AddWithValue("$call", call.Trim().ToUpperInvariant());
                command.Parameters.AddWithValue("$date", date);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    string stored = reader.IsDBNull(1) ? "" : new(reader.GetString(1).Split('.')[0].Where(char.IsAsciiDigit).ToArray());
                    if (stored.Length >= wanted.Length && stored[..wanted.Length] == wanted)
                        found.Add(reader.GetInt64(0));
                }
            }
            return found;
        }

        // The continent most QSOs with this DXCC entity have in the log -
        // HRD's own continent for the entity - or null if the log has none.
        public string? ContinentFor(int dxcc)
        {
            using var connection = Open(false);
            using var command = connection.CreateCommand();
            command.CommandText = $"select COL_CONT from {Table} where COL_DXCC = $d and COL_CONT <> '' " +
                                  "group by COL_CONT order by count(*) desc limit 1";
            command.Parameters.AddWithValue("$d", dxcc.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return command.ExecuteScalar() as string;
        }

        // A copy of the log beside it, named "<log> backup <date time> before
        // Callsign Lookup batch fill.hrdsql" (or "... Fill HRD QSO.hrdsql" for a
        // single fill). Older such backups beyond the last BackupsToKeep, of
        // both kinds together, are deleted - only ones with exactly those names.
        public string Backup(string purpose = BatchFillPurpose)
        {
            string folder = Path.GetDirectoryName(FilePath)!;
            string stem = Path.GetFileNameWithoutExtension(FilePath);
            string suffix = BackupSuffix(purpose);
            string destination = Path.Combine(folder, $"{stem} backup {DateTime.Now:yyyy-MM-dd HHmm}{suffix}");
            for (int n = 2; File.Exists(destination); n++)
                destination = Path.Combine(folder, $"{stem} backup {DateTime.Now:yyyy-MM-dd HHmm} ({n}){suffix}");

            using (var source = Open(false))
            using (var copy = new SqliteConnection(new SqliteConnectionStringBuilder
                   { DataSource = destination, Pooling = false }.ToString()))
            {
                copy.Open();
                source.BackupDatabase(copy);
            }
            PruneBackups(folder);
            return destination;
        }

        private static void PruneBackups(string folder)
        {
            var dir = new DirectoryInfo(folder);
            var old = new[] { BatchFillPurpose, FillQsoPurpose }
                .SelectMany(p => dir.GetFiles("*" + BackupSuffix(p)))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Skip(BackupsToKeep);
            foreach (var file in old)
            {
                try { file.Delete(); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        // Writes every update in one transaction: all of them, or - if any
        // fails - none.
        public void Apply(IEnumerable<HrdRowUpdate> updates)
        {
            using var connection = Open(true);
            using var transaction = connection.BeginTransaction();
            foreach (var update in updates)
            {
                if (update.Columns.Count == 0) continue;
                foreach (string column in update.Columns.Keys)
                    if (!ColumnName.IsMatch(column)) throw new HrdException($"Not a log column: {column}");

                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                var columns = update.Columns.ToList();
                command.CommandText = $"update {Table} set " +
                                      string.Join(", ", columns.Select((c, i) => $"{c.Key} = $v{i}")) +
                                      " where COL_PRIMARY_KEY = $pk";
                for (int i = 0; i < columns.Count; i++)
                    command.Parameters.AddWithValue("$v" + i, columns[i].Value ?? DBNull.Value);
                command.Parameters.AddWithValue("$pk", update.Pk);
                if (command.ExecuteNonQuery() != 1)
                    throw new HrdException($"QSO {update.Pk} wasn't found in the log - nothing was written.");
            }
            transaction.Commit();
        }

        // Asks HRD (through its IP Client port, which the user has switched
        // on) to reload the log so it shows the changes. False, with what
        // went wrong, if it couldn't - the user can refresh HRD by hand.
        public async Task<(bool Ok, string Message)> RefreshAsync()
        {
            try
            {
                using var client = new TcpClient();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                await client.ConnectAsync("127.0.0.1", IpCliPort, timeout.Token);
                var stream = client.GetStream();

                await ReadQuietAsync(stream, timeout.Token);                       // banner and prompt
                byte[] command = Encoding.ASCII.GetBytes($"db refresh \"{Title}\"\r\n");
                await stream.WriteAsync(command, timeout.Token);
                string reply = await ReadQuietAsync(stream, timeout.Token);
                await stream.WriteAsync(Encoding.ASCII.GetBytes("exit\r\n"), timeout.Token);
                return (true, reply.Trim());
            }
            catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException)
            {
                return (false, "HRD's IP Client port (7826) didn't answer, so HRD wasn't told to reload the log.");
            }
        }

        // Reads until the other end has said nothing for a moment.
        private static async Task<string> ReadQuietAsync(NetworkStream stream, CancellationToken token)
        {
            var text = new StringBuilder();
            var buffer = new byte[4096];
            while (true)
            {
                using var quiet = CancellationTokenSource.CreateLinkedTokenSource(token);
                quiet.CancelAfter(TimeSpan.FromMilliseconds(1200));
                try
                {
                    int read = await stream.ReadAsync(buffer, quiet.Token);
                    if (read == 0) break;
                    text.Append(Encoding.ASCII.GetString(buffer, 0, read));
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    break;
                }
            }
            return text.ToString();
        }
    }
}
