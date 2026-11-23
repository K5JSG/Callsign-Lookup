using CallsignLookup.Services.Hrd;
using Xunit;

namespace CallsignLookup.Tests
{
    // Which HRD log the fills change: the only one, or the one picked in the
    // "HRD log" drop-down. With several and none picked, nothing is guessed.
    public sealed class HrdLogChoiceTests : IDisposable
    {
        private readonly string _folder = Directory.CreateTempSubdirectory("hrd-logs-").FullName;
        private readonly string _list;

        public HrdLogChoiceTests() => _list = Path.Combine(_folder, "LogbookDatabases8.xml");

        public void Dispose() => Directory.Delete(_folder, recursive: true);

        private string Log(string name)
        {
            string path = Path.Combine(_folder, name + ".hrdsql");
            File.WriteAllText(path, "");
            return path;
        }

        private void WriteList(params (string Title, string Path, string Type)[] logs) =>
            File.WriteAllText(_list, "<Databases>" + string.Concat(logs.Select(l =>
                $"<Database Title=\"{l.Title}\" Database=\"{l.Path}\" DatabaseType=\"{l.Type}\"/>")) + "</Databases>");

        [Fact]
        public void A_single_local_log_is_used_whatever_is_saved()
        {
            string home = Log("home");
            WriteList(("K5JSG", home, "Local/SQLite"), ("Cloud", "server", "MySQL"));

            var log = HrdLogDatabase.Find("K5JSG/P Portable", out string error, _list);

            Assert.Equal(home, log?.FilePath);
            Assert.Equal("", error);
        }

        [Fact]
        public void With_two_logs_the_picked_one_is_used()
        {
            WriteList(("K5JSG/P Portable", Log("portable"), "Local/SQLite"), ("K5JSG", Log("home"), "Local/SQLite"));

            var log = HrdLogDatabase.Find("K5JSG", out _, _list);

            Assert.Equal("K5JSG", log?.Title);
            Assert.Equal(Path.Combine(_folder, "home.hrdsql"), log?.FilePath);
        }

        [Theory]
        [InlineData("")]
        [InlineData("Old log")]
        public void With_two_logs_and_none_picked_nothing_is_guessed(string saved)
        {
            WriteList(("K5JSG/P Portable", Log("portable"), "Local/SQLite"), ("K5JSG", Log("home"), "Local/SQLite"));

            Assert.Null(HrdLogDatabase.Find(saved, out string error, _list));
            Assert.Contains("\"HRD log\" drop-down", error);
            Assert.Contains("K5JSG/P Portable, K5JSG", error);
        }

        [Fact]
        public void The_list_gives_the_local_logs_in_HRDs_order()
        {
            WriteList(("B", Log("b"), "Local/SQLite"), ("Cloud", "server", "MySQL"), ("A", Log("a"), "Local/SQLite"));

            Assert.Equal(new[] { "B", "A" }, HrdLogDatabase.ListLogs(out _, _list).Select(l => l.Title));
        }
    }
}
