using CallsignLookup.Services.Hrd;
using CallsignLookup.Services.Upload;
using Microsoft.Data.Sqlite;

namespace CallsignLookup.Tests
{
    public class AdifWriterTests
    {
        private static Dictionary<string, object?> ParkQso() => new()
        {
            ["COL_CALL"] = "W5ERX",
            ["COL_QSO_DATE"] = "2026-11-07",
            ["COL_TIME_ON"] = "17:51:05.000",
            ["COL_TIME_OFF"] = "00:00:00.000",
            ["COL_BAND"] = "17M",
            ["COL_FREQ"] = 18071600L,
            ["COL_MODE"] = "CW",
            ["COL_SUBMODE"] = "",
            ["COL_PROP_MODE"] = null,
            ["COL_STATION_CALLSIGN"] = "K5JSG",
            ["COL_MY_GRIDSQUARE"] = "FN13af",
            ["COL_MY_POTA_REF"] = "US-8098,US-6532",
            ["COL_MY_CQ_ZONE"] = 5L,
            ["COL_MY_DXCC"] = "",
            ["COL_NAME"] = "José",
            ["COL_LAT"] = 43.1234,
            ["COL_LON"] = -77.5,
            ["COL_DXCC"] = "291",
            ["COL_COMMENT"] = "line one\r\nline two",
        };

        [Fact]
        public void LotwRecord_HasOnlyWhatLotwMatchesOn()
        {
            string record = AdifWriter.Record(ParkQso(), ["CALL", "QSO_DATE", "TIME_ON", "BAND", "MODE", "SUBMODE", "FREQ", "PROP_MODE"]);

            Assert.Equal("<CALL:5>W5ERX <QSO_DATE:8>20261107 <TIME_ON:6>175105 <BAND:3>17m <MODE:2>CW <FREQ:7>18.0716 <EOR>", record);
        }

        [Fact]
        public void LotwFile_LeavesOutTheStationsOwnDetails()
        {
            string file = AdifWriter.LotwFile([ParkQso()]);

            Assert.Contains("<EOH>", file);
            Assert.Contains("<CALL:5>W5ERX", file);
            Assert.DoesNotContain("MY_GRIDSQUARE", file);
            Assert.DoesNotContain("STATION_CALLSIGN", file);
        }

        [Fact]
        public void FullRecord_ConvertsHrdsColumns()
        {
            string record = AdifWriter.FullRecord(ParkQso());

            Assert.Contains("<MY_POTA_REF:15>US-8098,US-6532 ", record);
            Assert.Contains("<MY_GRIDSQUARE:6>FN13af ", record);
            Assert.Contains("<MY_CQ_ZONE:1>5 ", record);
            Assert.Contains("<NAME:5>José ", record);            // UTF-8 bytes
            Assert.Contains("<LAT:11>N043 07.404 ", record);
            Assert.Contains("<LON:11>W077 30.000 ", record);
            Assert.Contains("<COMMENT:17>line one line two ", record);
            Assert.DoesNotContain("MY_DXCC", record);            // empty
            Assert.DoesNotContain("TIME_OFF", record);
            Assert.EndsWith("<EOR>", record);
        }

        [Fact]
        public void TimeWithoutSeconds_AndMissingFrequency()
        {
            var row = ParkQso();
            row["COL_TIME_ON"] = "17:51";
            row["COL_FREQ"] = null;

            string record = AdifWriter.Record(row, ["TIME_ON", "FREQ"]);

            Assert.Equal("<TIME_ON:4>1751 <EOR>", record);
        }
    }

    public class TqslLocationTests
    {
        private const string StationData =
            "<StationDataFile>" +
            "<StationData name=\"Home\"><CALL>K5JSG</CALL><GRIDSQUARE>FN13AH</GRIDSQUARE><US_STATE>NY</US_STATE></StationData>" +
            "<StationData name=\"Penfield - NY\"><CALL>K5JSG</CALL><GRIDSQUARE>FN13GD</GRIDSQUARE></StationData>" +
            "<StationData name=\"Irvine - CA\"><CALL>K5JSG</CALL><GRIDSQUARE>DM13CQ</GRIDSQUARE></StationData>" +
            "<StationData name=\"Home/P\"><CALL>K5JSG/P</CALL><GRIDSQUARE>FN13AH</GRIDSQUARE></StationData>" +
            "<StationData name=\"Corbett Park US-8098\"><CALL>K5JSG</CALL><GRIDSQUARE>FN13AF</GRIDSQUARE></StationData>" +
            "<StationData name=\"Empire State Trail US-6532\"><CALL>K5JSG</CALL><GRIDSQUARE>FN13AF</GRIDSQUARE></StationData>" +
            "<StationData name=\"Lakeside Beach\"><CALL>K5JSG</CALL><GRIDSQUARE>FN03VI</GRIDSQUARE></StationData>" +
            "<StationData name=\"Oak Orchard Marine\"><CALL>K5JSG</CALL><GRIDSQUARE>FN03VI</GRIDSQUARE></StationData>" +
            "</StationDataFile>";

        private static readonly List<TqslLocation> Locations = TqslLocations.Parse(StationData);

        [Fact]
        public void Parse_ReadsEveryLocation_ByName()
        {
            Assert.Equal(8, Locations.Count);
            Assert.Equal("Corbett Park US-8098", Locations[0].Name);
            var home = Locations.Single(l => l.Name == "Home");
            Assert.Equal(("K5JSG", "FN13AH", "NY"), (home.Callsign, home.Grid, home.State));
        }

        [Theory]
        [InlineData("K5JSG", "FN13gd", "", "Penfield - NY")]
        [InlineData("K5JSG", "DM13cq", "Home", "Irvine - CA")]         // HRD's last location doesn't fit the QSO
        [InlineData("K5JSG/P", "FN13ah", "", "Home/P")]
        [InlineData("K5JSG", "FN13ah", "", "Home")]
        [InlineData("K5JSG", "FN03vi", "Oak Orchard Marine", "Oak Orchard Marine")]   // HRD's choice settles a tie
        public void Pick_TheLocationThatFits(string call, string grid, string hrd, string expected) =>
            Assert.Equal(expected, TqslLocations.Pick(Locations, call, grid, hrd)?.Name);

        [Fact]
        public void Pick_TwoParksInOneSquare_ParkRefSettlesIt() =>
            Assert.Equal("Empire State Trail US-6532",
                TqslLocations.Pick(Locations, "K5JSG", "FN13af", "", ["US-6532"])?.Name);

        [Theory]
        [InlineData("K5JSG", "FN03vi")]           // a tie with nothing to settle it
        [InlineData("K5JSG", "")]                 // no grid
        [InlineData("W1AW", "FN31")]              // no location for the call
        public void Pick_NothingWhenItCantTell(string call, string grid) =>
            Assert.Null(TqslLocations.Pick(Locations, call, grid));
    }

    public class QrzLogbookReplyTests
    {
        [Theory]
        [InlineData("RESULT=OK&LOGID=130877825&COUNT=1", true, "130877825")]
        [InlineData("RESULT=REPLACE&LOGID=130877825&COUNT=1", true, "130877825")]
        [InlineData("RESULT=FAIL&REASON=wrong+station_callsign&COUNT=0", false, "")]
        [InlineData("RESULT=AUTH&REASON=invalid api key", false, "")]
        [InlineData("<html>oops</html>", false, "")]
        public void Interpret(string reply, bool sent, string logId)
        {
            var result = QrzLogbookSender.Interpret(reply);

            Assert.Equal(sent, result.Sent);
            Assert.Equal(logId, result.LogId);
            Assert.Equal(reply, result.Reply);
        }

        [Fact]
        public void Interpret_ShowsQrzsReason() =>
            Assert.Contains("wrong station_callsign", QrzLogbookSender.Interpret("RESULT=FAIL&REASON=wrong+station_callsign").Message);
    }

    public class LotwExitCodeTests
    {
        [Theory]
        [InlineData(0, true)]
        [InlineData(8, true)]
        [InlineData(9, true)]
        [InlineData(2, false)]
        [InlineData(11, false)]
        public void Interpret(int code, bool sent) => Assert.Equal(sent, LotwSender.Interpret(code, "").Sent);

        [Fact]
        public void Interpret_SpotsAPasswordRequest()
        {
            var result = LotwSender.Interpret(4, "Password for certificate K5JSG is required");

            Assert.False(result.Sent);
            Assert.True(result.NeedsPassword);
        }
    }

    public sealed class QsoUploaderTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "CallsignLookupTests-" + Guid.NewGuid().ToString("N"));
        private readonly HrdLogDatabase _log;

        public QsoUploaderTests()
        {
            Directory.CreateDirectory(_folder);
            string path = Path.Combine(_folder, "Test Logbook.hrdsql");
            using (var c = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                c.Open();
                using var command = c.CreateCommand();
                command.CommandText =
                    "create table TABLE_HRD_CONTACTS_V07 (COL_PRIMARY_KEY integer primary key, COL_CALL text, COL_QSO_DATE text, " +
                    "COL_TIME_ON text, COL_BAND text, COL_MODE text, COL_STATION_CALLSIGN text, COL_LOTW_QSL_SENT text, " +
                    "COL_LOTW_QSLSDATE text, COL_APP_HAMRADIODELUXE_LOTW_STATUS text, COL_APP_HAMRADIODELUXE_QRZCOM_SENT text, " +
                    "COL_QRZCOM_QSO_UPLOAD_DATE text, COL_QRZCOM_QSO_UPLOAD_STATUS text);" +
                    "insert into TABLE_HRD_CONTACTS_V07 (COL_PRIMARY_KEY, COL_CALL, COL_QSO_DATE, COL_TIME_ON, COL_BAND, COL_MODE, COL_STATION_CALLSIGN) values " +
                    "(1, 'W5ERX', '2026-11-07', '17:51:05.000', '17M', 'CW', 'K5JSG'), " +
                    "(2, 'K1ABC', '2026-11-07', '17:51:40', '20M', 'SSB', 'K5JSG/P'), " +
                    "(3, 'W5ERX', '2026-11-06', '17:51:05.000', '17M', 'CW', 'K5JSG');";
                command.ExecuteNonQuery();
            }
            _log = new HrdLogDatabase(path, "Test");
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(_folder, true); } catch (IOException) { }
        }

        [Theory]
        [InlineData("w5erx", "17:51:05", new long[] { 1 })]
        [InlineData("W5ERX", "17:51", new long[] { 1 })]
        [InlineData("K1ABC", "17:51:40", new long[] { 2 })]
        [InlineData("W5ERX", "17:51:06", new long[0])]
        public void FindQsos_ByCallDateAndTime(string call, string time, long[] expected) =>
            Assert.Equal(expected, _log.FindQsos(call, ["2026-11-07"], time));

        [Fact]
        public async Task Send_SetsHrdsFlags_OnlyForWhatWent()
        {
            var home = new TqslLocation("Home", "K5JSG", "FN13AH");
            var sentRows = new List<string>();
            var uploader = new QsoUploader(_log,
                (tqsl, location, rows, password, token) =>
                {
                    sentRows.AddRange(rows.Select(r => (string)r["COL_CALL"]!));
                    return Task.FromResult(new LotwResult(true, false, 0, "Sent to LoTW."));
                },
                (key, row, token) => Task.FromResult((string)row["COL_CALL"]! == "W5ERX"
                    ? new QrzResult(true, "Sent to QRZ.", "77", "RESULT=OK&LOGID=77&COUNT=1")
                    : new QrzResult(false, "QRZ didn't take it.", "", "RESULT=FAIL")),
                station => "KEY",
                () => "tqsl.exe");

            var report = await uploader.SendAsync(new UploadRequest(new Dictionary<long, TqslLocation> { [1] = home }, [1, 2]));

            Assert.Equal(["W5ERX"], sentRows);
            Assert.Equal(1, report.LotwSent);
            Assert.Equal(1, report.QrzSent);
            Assert.Equal("", report.FlagError);
            var rows = _log.ReadRows([1, 2]);
            Assert.Equal("Y", rows[1]["COL_LOTW_QSL_SENT"]);
            Assert.Equal("Upload OK.", rows[1]["COL_APP_HAMRADIODELUXE_LOTW_STATUS"]);
            Assert.Equal(DateTime.UtcNow.ToString("yyyy-MM-dd"), rows[1]["COL_LOTW_QSLSDATE"]);
            Assert.Equal("Y", rows[1]["COL_APP_HAMRADIODELUXE_QRZCOM_SENT"]);
            Assert.Equal("RESULT=OK&LOGID=77&COUNT=1", rows[1]["COL_QRZCOM_QSO_UPLOAD_STATUS"]);
            Assert.Null(rows[2]["COL_APP_HAMRADIODELUXE_QRZCOM_SENT"]);
            Assert.Null(rows[2]["COL_LOTW_QSL_SENT"]);
        }

        [Fact]
        public async Task Send_PasswordNeeded_ListsTheQsos_AndSetsNoFlag()
        {
            var home = new TqslLocation("Home", "K5JSG", "FN13AH");
            var uploader = new QsoUploader(_log,
                (tqsl, location, rows, password, token) => Task.FromResult(new LotwResult(false, password == null, 4, "password")),
                (key, row, token) => throw new InvalidOperationException("not asked to send to QRZ"),
                station => "",
                () => "tqsl.exe");

            var report = await uploader.SendAsync(new UploadRequest(new Dictionary<long, TqslLocation> { [1] = home }, []));

            Assert.Equal(home, Assert.Single(report.LotwNeedingPassword).Value);
            Assert.Null(_log.ReadRows([1])[1]["COL_LOTW_QSL_SENT"]);
        }

        [Fact]
        public async Task Send_NoQrzKey_SaysSo()
        {
            var uploader = new QsoUploader(_log,
                (tqsl, location, rows, password, token) => throw new InvalidOperationException(),
                (key, row, token) => throw new InvalidOperationException(),
                station => "",
                () => null);

            var report = await uploader.SendAsync(new UploadRequest(new Dictionary<long, TqslLocation>(), [2]));

            var (pk, result) = Assert.Single(report.Qrz);
            Assert.Equal(2, pk);
            Assert.Contains("K5JSG/P", result.Message);
        }
    }

    public class HrdUploadSettingsTests
    {
        [Theory]
        [InlineData("K5JSG/P", "K5JSG")]
        [InlineData("VE3/K5JSG", "K5JSG")]
        [InlineData("K5JSG", "K5JSG")]
        public void BaseCall(string call, string expected) => Assert.Equal(expected, HrdUploadSettings.BaseCall(call));
    }
}
