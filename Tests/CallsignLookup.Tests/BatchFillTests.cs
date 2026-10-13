using CallsignLookup.Services;
using CallsignLookup.Services.Hrd;
using Microsoft.Data.Sqlite;

namespace CallsignLookup.Tests
{
    public class HrdRowMapperTests
    {
        private static PotaPark? FindPark(string reference) => reference switch
        {
            "US-3033" => new PotaPark("US-3033", "Lockhart State Park", "US-TX", 29.8494, -97.6969, "EL19du"),
            "CA-0792" => new PotaPark("CA-0792", "Parlee Beach Provincial Park", "CA-NB", 46.2395, -64.5101, "FN76rf", 1),
            _ => null,
        };

        private static QrzCallsignRecord W5erx(string call = "W5ERX") => new()
        {
            Call = call, FirstName = "Wade T", LastName = "Bolling", City = "Kyle", State = "TX", County = "Hays",
            Dxcc = 291, Latitude = 30.021276, Longitude = -97.880774,
        };

        // W5ERX as it sits in the log after HRD's own lookup, park ref in the Comment.
        private static Dictionary<string, object?> W5erxRow() => new()
        {
            ["COL_PRIMARY_KEY"] = 5900L, ["COL_CALL"] = "W5ERX", ["COL_COUNTRY"] = "United States", ["COL_DXCC"] = "291",
            ["COL_CONT"] = "NA", ["COL_NAME"] = "Wade T Bolling", ["COL_QTH"] = "Kyle", ["COL_STATE"] = "TX", ["COL_CNTY"] = "Hays",
            ["COL_COMMENT"] = "US-3033", ["COL_POTA_REF"] = "", ["COL_LAT"] = 30.0213, ["COL_LON"] = -97.8808,
            ["COL_GRIDSQUARE"] = "EM10ba", ["COL_CQZ"] = null, ["COL_ITUZ"] = null, ["COL_ARRL_SECT"] = "", ["COL_QSL_VIA"] = "",
            ["COL_IOTA"] = "", ["COL_IOTA_ISLAND_ID"] = null, ["COL_SILENT_KEY"] = "0",
            ["COL_MY_GRIDSQUARE"] = "FN13ah", ["COL_MY_LAT"] = 43.31545, ["COL_MY_LON"] = -77.937824,
            ["COL_MY_CITY"] = "Hamlin", ["COL_STATION_CALLSIGN"] = "K5JSG", ["COL_MY_RIG"] = "FTdx-10",
            ["COL_DISTANCE"] = 1000.0, ["COL_ANT_AZ"] = 100.0, ["COL_ANT_PATH"] = "S",
        };

        private static HrdRowChanges Run(Dictionary<string, object?> row, QrzCallsignRecord record,
            HrdStationProfile? profile = null, bool clearQslVia = true, Func<int, string?>? continent = null)
        {
            var plan = HrdQsoFiller.Plan(HrdRowMapper.ToQsoFields(row), CallsignLookupService.Resolve(record), FindPark, profile, clearQslVia);
            return HrdRowMapper.ToColumnUpdates(plan, row, continent ?? (_ => "NA"));
        }

        private static object? New(HrdRowChanges changes, string column) =>
            changes.Changes.SingleOrDefault(c => c.Column == column)?.New;

        [Fact]
        public void ToQsoFields_ReadsTheRowTheWayTheWindowShowsIt()
        {
            var row = W5erxRow();
            row["COL_IOTA"] = "EU-005";
            row["COL_IOTA_ISLAND_ID"] = 9292L;
            row["COL_APP_HAMRADIODELUXE_ISLANDNAME"] = "Great Britain";
            row["COL_SILENT_KEY"] = "1";
            var fields = HrdRowMapper.ToQsoFields(row);

            Assert.Equal("W5ERX", fields["edtCALL"]);
            Assert.Equal("30.0213", fields["edtLAT"]);
            Assert.Equal("EU-005", fields["edtIOTARefNo"]);
            Assert.Equal("9292 - Great Britain", fields["edtIOTAIsland"]);
            Assert.Equal("Yes", fields["chkSILENT_KEY"]);
            Assert.Equal("-none-", HrdRowMapper.ToQsoFields(W5erxRow())["edtIOTARefNo"]);
        }

        [Fact]
        public void ParkInTheComment_BecomesTheParksColumns_WithDistanceFromMyStation()
        {
            var changes = Run(W5erxRow(), W5erx());

            Assert.Equal("", New(changes, "COL_COMMENT"));
            Assert.Equal("US-3033", New(changes, "COL_POTA_REF"));
            Assert.Equal("Lockhart State Park", New(changes, "COL_APP_HAMRADIODELUXE_POTA_NAME"));
            Assert.Equal("29.8494", New(changes, "COL_APP_HAMRADIODELUXE_POTA_LAT"));
            Assert.Equal(29.8494, (double)New(changes, "COL_LAT")!, 6);
            Assert.Equal("EL19du", New(changes, "COL_GRIDSQUARE"));
            Assert.IsType<int>(New(changes, "COL_CQZ"));
            // FN13ah -> EL19du, grid centre to grid centre: 1430.53 mi, 236 degrees (HRD's own Recalc).
            Assert.Equal(2302.21, (double)New(changes, "COL_DISTANCE")!, 2);
            Assert.Equal(236.0, (double)New(changes, "COL_ANT_AZ")!, 1);
            Assert.False(changes.Changes.Any(c => c.Column == "COL_ANT_PATH"));   // already 'S'
        }

        [Fact]
        public void WhatWasWritten_ChangesNothingTheSecondTime()
        {
            var row = W5erxRow();
            foreach (var (column, value) in Run(row, W5erx()).Columns) row[column] = value;

            Assert.False(Run(row, W5erx()).Any);
        }

        [Fact]
        public void ParkInAnotherCountry_SetsCountryDxccAndContinent_AndEmptiesTheCounty()
        {
            var row = W5erxRow();
            row["COL_COMMENT"] = "CA-0792";
            var changes = Run(row, W5erx(), continent: dxcc => dxcc == 1 ? "NA" : null);

            Assert.Equal("Canada", New(changes, "COL_COUNTRY"));
            Assert.Equal("1", New(changes, "COL_DXCC"));
            Assert.Equal("NB", New(changes, "COL_STATE"));
            Assert.Equal("", New(changes, "COL_CNTY"));          // HRD empties it with a new Country
            Assert.DoesNotContain(changes.Warnings, w => w.Contains("US county"));
        }

        [Fact]
        public void SilentKey_SetsTheBoxColumnAndTheNote()
        {
            var changes = Run(W5erxRow(), W5erx("W5ERX/SK2023"));

            Assert.Equal("1", New(changes, "COL_SILENT_KEY"));
            Assert.Equal("SK 2023", New(changes, "COL_QSL_VIA"));
        }

        [Fact]
        public void QslViaAndIota_AreWrittenAsColumns()
        {
            var row = W5erxRow();
            row["COL_QSL_VIA"] = "BURO";
            row["COL_IOTA"] = "EU-005";
            row["COL_IOTA_ISLAND_ID"] = 9292L;
            row["COL_APP_HAMRADIODELUXE_ISLANDNAME"] = "Great Britain";
            var changes = Run(row, W5erx());

            Assert.Equal("", New(changes, "COL_QSL_VIA"));
            // Texas isn't on an island: HRD's IOTA is taken out, island and all.
            Assert.Equal("", New(changes, "COL_IOTA"));
            Assert.Null(New(changes, "COL_IOTA_ISLAND_ID"));
            Assert.Contains(changes.Changes, c => c.Column == "COL_IOTA_ISLAND_ID" && c.New is null);
        }

        private static readonly HrdStationProfile Home = new("Home", "K5JSG", "FN13ah", 43.31545, -77.937824,
            new Dictionary<string, string>
            {
                ["Callsign"] = "K5JSG", ["Operator"] = "K5JSG", ["Locator"] = "fn13AH", ["Latitude"] = "43.315450",
                ["Longitude"] = "-77.937824", ["City"] = "Hamlin", ["Country"] = "United States", ["Equipment"] = "FTdx-10",
                ["Altitude"] = "322.0", ["cqzone"] = "5", ["ituzone"] = "8", ["FistsID"] = "20110",
            });

        [Fact]
        public void MyStation_ProfileFillsTheMyStationColumns_WhenTheQsoHasNone()
        {
            var row = W5erxRow();
            foreach (string column in row.Keys.Where(k => k.StartsWith("COL_MY_", StringComparison.Ordinal)).ToList())
                row[column] = null;
            row["COL_STATION_CALLSIGN"] = "";
            var changes = Run(row, W5erx(), Home);

            Assert.Equal("FN13ah", New(changes, "COL_MY_GRIDSQUARE"));     // HRD's case: FN13ah
            Assert.Equal(43.31545, (double)New(changes, "COL_MY_LAT")!, 6);
            Assert.Equal("291", New(changes, "COL_MY_DXCC"));
            Assert.Equal(5, New(changes, "COL_MY_CQ_ZONE"));
            Assert.Equal("K5JSG", New(changes, "COL_STATION_CALLSIGN"));
        }

        [Fact]
        public void MyStation_DifferentStationIsLeftAlone_AndAnOldRigIsKept()
        {
            var penfield = W5erxRow();
            penfield["COL_MY_GRIDSQUARE"] = "FN13gd"; penfield["COL_MY_LAT"] = 43.133482; penfield["COL_MY_LON"] = -77.44069;
            var changes = Run(penfield, W5erx(), Home);
            Assert.DoesNotContain(changes.Changes, c => c.Column == "COL_MY_CITY");
            Assert.Contains(changes.Warnings, w => w.Contains("doesn't match"));

            var home = W5erxRow();
            home["COL_MY_RIG"] = "FT-991a";
            var same = Run(home, W5erx(), Home);
            Assert.DoesNotContain(same.Changes, c => c.Column == "COL_MY_RIG");
        }

        [Fact]
        public void MyStation_ProfileWithoutALocationIsNotApplied()
        {
            var empty = new HrdStationProfile("Home", "K5JSG/P", Fields: new Dictionary<string, string>());
            var changes = Run(W5erxRow(), W5erx(), empty);

            Assert.DoesNotContain(changes.Changes, c => c.Column.StartsWith("COL_MY_", StringComparison.Ordinal));
            Assert.Contains(changes.Warnings, w => w.Contains("no location"));
        }

        [Fact]
        public void Recalc_MatchesHrdsOwn()
        {
            var (km, bearing) = HrdRowMapper.Recalc("FN13ah", "EL19du")!.Value;
            Assert.Equal(2302.21, km, 2);
            Assert.Equal(236.0, bearing, 1);
            Assert.Null(HrdRowMapper.Recalc("", "EL19du"));
        }
    }

    public sealed class HrdLogDatabaseTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "CallsignLookupTests-" + Guid.NewGuid().ToString("N"));
        private readonly string _log;

        public HrdLogDatabaseTests()
        {
            Directory.CreateDirectory(_folder);
            _log = Path.Combine(_folder, "Test Logbook.hrdsql");
            using var c = new SqliteConnection($"Data Source={_log};Pooling=False");
            c.Open();
            Exec(c, "create table TABLE_HRD_CONTACTS_V07 (COL_PRIMARY_KEY integer primary key, COL_CALL text, COL_COMMENT text, " +
                    "COL_DXCC text, COL_CONT text, COL_DISTANCE real, COL_CQZ integer)");
            Exec(c, "insert into TABLE_HRD_CONTACTS_V07 values (1, 'W5ERX', 'US-3033', '291', 'NA', 1.5, 4)");
            Exec(c, "insert into TABLE_HRD_CONTACTS_V07 values (2, 'KL4RL', '', '6', 'NA', null, null)");
            Exec(c, "insert into TABLE_HRD_CONTACTS_V07 values (3, 'VE3ABC', '', '1', 'NA', null, null)");
            Exec(c, "insert into TABLE_HRD_CONTACTS_V07 values (4, 'VE3XYZ', '', '1', 'NA', null, null)");
            Exec(c, "insert into TABLE_HRD_CONTACTS_V07 values (5, 'XX0X', '', '1', 'SA', null, null)");
        }

        private static void Exec(SqliteConnection c, string sql)
        {
            using var command = c.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(_folder, true); } catch (IOException) { }
        }

        private HrdLogDatabase Db => new(_log, "Test");

        [Fact]
        public void ReadRows_ByKey_SkipsKeysNotInTheLog()
        {
            var rows = Db.ReadRows([1, 2, 99]);

            Assert.Equal(2, rows.Count);
            Assert.Equal("W5ERX", rows[1]["COL_CALL"]);
            Assert.Equal(4L, rows[1]["COL_CQZ"]);
            Assert.Null(rows[2]["COL_CQZ"]);
        }

        [Fact]
        public void ContinentFor_IsTheMostCommonInTheLog() => Assert.Equal("NA", Db.ContinentFor(1));

        [Fact]
        public void ContinentFor_NullForAnEntityNotInTheLog() => Assert.Null(Db.ContinentFor(999));

        [Fact]
        public void Apply_WritesEveryUpdate()
        {
            Db.Apply([
                new HrdRowUpdate(1, new Dictionary<string, object?> { ["COL_COMMENT"] = "", ["COL_DISTANCE"] = 2.5, ["COL_CQZ"] = null }),
                new HrdRowUpdate(2, new Dictionary<string, object?> { ["COL_CQZ"] = 1 }),
            ]);
            var rows = Db.ReadRows([1, 2]);

            Assert.Equal("", rows[1]["COL_COMMENT"]);
            Assert.Equal(2.5, rows[1]["COL_DISTANCE"]);
            Assert.Null(rows[1]["COL_CQZ"]);
            Assert.Equal(1L, rows[2]["COL_CQZ"]);
        }

        [Fact]
        public void Apply_IsAllOrNothing()
        {
            Assert.Throws<HrdException>(() => Db.Apply([
                new HrdRowUpdate(1, new Dictionary<string, object?> { ["COL_COMMENT"] = "changed" }),
                new HrdRowUpdate(99, new Dictionary<string, object?> { ["COL_COMMENT"] = "x" }),
            ]));

            Assert.Equal("US-3033", Db.ReadRows([1])[1]["COL_COMMENT"]);
        }

        [Fact]
        public void Apply_RefusesAnythingButALogColumn() =>
            Assert.Throws<HrdException>(() => Db.Apply([
                new HrdRowUpdate(1, new Dictionary<string, object?> { ["COL_CALL = 'x' --"] = "y" }),
            ]));

        [Fact]
        public void Backup_CopiesTheLog_AndKeepsOnlyTheLastTen()
        {
            string first = Db.Backup();
            Assert.True(File.Exists(first));
            Assert.EndsWith(" before Callsign Lookup batch fill.hrdsql", first);
            using (var copy = new SqliteConnection($"Data Source={first};Mode=ReadOnly;Pooling=False"))
            {
                copy.Open();
                using var command = copy.CreateCommand();
                command.CommandText = "select count(*) from TABLE_HRD_CONTACTS_V07";
                Assert.Equal(5L, command.ExecuteScalar());
            }

            // A backup someone else made isn't touched, however many there are.
            string other = Path.Combine(_folder, "Test Logbook backup before something else.hrdsql");
            File.WriteAllText(other, "x");
            for (int i = 0; i < 14; i++) Db.Backup();

            Assert.Equal(HrdLogDatabase.BackupsToKeep, Directory.GetFiles(_folder, "* before Callsign Lookup batch fill.hrdsql").Length);
            Assert.True(File.Exists(other));
        }
    }
}
