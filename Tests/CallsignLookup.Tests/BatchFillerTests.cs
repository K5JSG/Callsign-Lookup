using CallsignLookup.Services;
using CallsignLookup.Services.Hrd;
using Microsoft.Data.Sqlite;

namespace CallsignLookup.Tests
{
    public class HrdClipboardTests
    {
        [Fact]
        public void ParsePrimaryKeys_FromAdx_InOrder_EachOnce()
        {
            const string adx = "<ADX><RECORDS><RECORD><CALL>W5ERX</CALL><PRIMARY_KEY>5863</PRIMARY_KEY></RECORD>" +
                               "<RECORD><CALL>K1ABC</CALL><PRIMARY_KEY>12</PRIMARY_KEY></RECORD>" +
                               "<RECORD><PRIMARY_KEY>5863</PRIMARY_KEY></RECORD></RECORDS></ADX>";

            Assert.Equal([5863L, 12L], HrdClipboard.ParsePrimaryKeys(adx));
        }

        [Fact]
        public void ParsePrimaryKeys_FromAdi() =>
            Assert.Equal([5864L, 7L], HrdClipboard.ParsePrimaryKeys("<CALL:5>W5ERX <primary_key:4>5864 <EOR>\n<PRIMARY_KEY:1>7<EOR>"));

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("K5JSG W5ERX 599")]
        public void ParsePrimaryKeys_NothingForOtherText(string? text) => Assert.Empty(HrdClipboard.ParsePrimaryKeys(text));
    }

    public sealed class HrdBatchFillerApplyTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "CallsignLookupTests-" + Guid.NewGuid().ToString("N"));
        private readonly HrdLogDatabase _log;
        private readonly HrdBatchFiller _filler;

        public HrdBatchFillerApplyTests()
        {
            Directory.CreateDirectory(_folder);
            string path = Path.Combine(_folder, "Test Logbook.hrdsql");
            using (var c = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                c.Open();
                using var command = c.CreateCommand();
                command.CommandText =
                    "create table TABLE_HRD_CONTACTS_V07 (COL_PRIMARY_KEY integer primary key, COL_CALL text, COL_COMMENT text, COL_CQZ integer);" +
                    "insert into TABLE_HRD_CONTACTS_V07 values (1, 'W5ERX', 'US-3033', null);" +
                    "insert into TABLE_HRD_CONTACTS_V07 values (2, 'K1ABC', 'hello', 5);";
                command.ExecuteNonQuery();
            }
            _log = new HrdLogDatabase(path, "Test");
            _filler = new HrdBatchFiller(_log, new CallsignLookupService(new QrzService("user", "password")));
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(_folder, true); } catch (IOException) { }
        }

        private static HrdBatchItem Item(long pk, params HrdColumnChange[] changes)
        {
            var rowChanges = new HrdRowChanges();
            rowChanges.Changes.AddRange(changes);
            return new HrdBatchItem { Pk = pk, Call = "X", Changes = rowChanges, TickedAtStart = true };
        }

        [Fact]
        public void Apply_WritesTheChosen_AfterABackup_AndReadsThemBack()
        {
            var result = _filler.Apply([
                Item(1, new HrdColumnChange("Comment", "COL_COMMENT", "US-3033", ""), new HrdColumnChange("CQ zone", "COL_CQZ", null, 4)),
            ]);

            Assert.Single(result.Applied);
            Assert.Empty(result.NotApplied);
            Assert.Empty(result.Failed);
            Assert.True(File.Exists(result.BackupPath));
            var row = _log.ReadRows([1])[1];
            Assert.Equal("", row["COL_COMMENT"]);
            Assert.Equal(4L, row["COL_CQZ"]);
            Assert.Equal("hello", _log.ReadRows([2])[2]["COL_COMMENT"]);   // not chosen, untouched
        }

        [Fact]
        public void Apply_SkipsAQsoChangedSinceThePreview_WithoutWritingAnything()
        {
            // The preview said the comment was "US-3033"; it's since become something else.
            var result = _filler.Apply([
                Item(1, new HrdColumnChange("Comment", "COL_COMMENT", "something else", "")),
                Item(2, new HrdColumnChange("CQ zone", "COL_CQZ", 5, 9)),
            ]);

            Assert.Single(result.Applied);
            var skipped = Assert.Single(result.NotApplied);
            Assert.Equal(1, skipped.Item.Pk);
            Assert.Contains("changed in HRD", skipped.Why);
            Assert.Equal("US-3033", _log.ReadRows([1])[1]["COL_COMMENT"]);
            Assert.Equal(9L, _log.ReadRows([2])[2]["COL_CQZ"]);
        }

        [Fact]
        public void Apply_WithNothingToWrite_MakesNoBackup()
        {
            var result = _filler.Apply([Item(99, new HrdColumnChange("Comment", "COL_COMMENT", "x", ""))]);

            Assert.Empty(result.Applied);
            Assert.Single(result.NotApplied);
            Assert.Equal("", result.BackupPath);
            Assert.Empty(Directory.GetFiles(_folder, "*before Callsign Lookup batch fill*"));
        }

        [Fact]
        public void Undo_PutsEveryColumnBackAsItWas()
        {
            var batch = _filler.Apply([
                Item(1, new HrdColumnChange("Comment", "COL_COMMENT", "US-3033", ""), new HrdColumnChange("CQ zone", "COL_CQZ", null, 4)),
                Item(2, new HrdColumnChange("Comment", "COL_COMMENT", "hello", "bye")),
            ]);
            Assert.Equal("bye", _log.ReadRows([2])[2]["COL_COMMENT"]);

            var undone = _filler.Undo(batch);

            Assert.Equal(2, undone.Applied.Count);
            Assert.Empty(undone.NotApplied);
            Assert.Empty(undone.Failed);
            var rows = _log.ReadRows([1, 2]);
            Assert.Equal("US-3033", rows[1]["COL_COMMENT"]);
            Assert.Null(rows[1]["COL_CQZ"]);
            Assert.Equal("hello", rows[2]["COL_COMMENT"]);
            Assert.Contains("Comment: (blank) -> US-3033", HrdBatchFiller.Summary(undone, "log", undo: true));
        }

        [Fact]
        public void Undo_LeavesAQsoChangedSinceTheBatch()
        {
            var batch = _filler.Apply([Item(1, new HrdColumnChange("Comment", "COL_COMMENT", "US-3033", ""))]);
            _log.Apply([new HrdRowUpdate(1, new Dictionary<string, object?> { ["COL_COMMENT"] = "typed later" })]);

            var undone = _filler.Undo(batch);

            Assert.Empty(undone.Applied);
            Assert.Contains("changed since", Assert.Single(undone.NotApplied).Why);
            Assert.Equal("typed later", _log.ReadRows([1])[1]["COL_COMMENT"]);
        }

        [Fact]
        public void Summary_ListsWhatWasWritten()
        {
            var result = _filler.Apply([Item(1, new HrdColumnChange("Comment", "COL_COMMENT", "US-3033", ""))]);
            string text = HrdBatchFiller.Summary(result, "log.hrdsql");

            Assert.Contains("Applied to 1 QSO.", text);
            Assert.Contains("Comment: US-3033 -> (blank)", text);
            Assert.Contains("Backup: ", text);
        }
    }
}
