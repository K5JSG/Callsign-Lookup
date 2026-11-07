using CallsignLookup.Services.Hrd;
using CallsignLookup.Services;

namespace CallsignLookup.Tests
{
    public class QsoEditTests
    {
        private static Dictionary<string, object?> Row() => new()
        {
            ["COL_CALL"] = "W1AW",
            ["COL_NAME"] = "Hiram",
            ["COL_COUNTRY"] = "United States",
            ["COL_DXCC"] = "291",
            ["COL_CONT"] = "NA",
            ["COL_STATE"] = "CT",
            ["COL_GRIDSQUARE"] = "FN31pr",
            ["COL_LAT"] = 41.7146,
            ["COL_LON"] = -72.7272,
            ["COL_MY_GRIDSQUARE"] = "FN13ah",
            ["COL_DISTANCE"] = 450.0,
            ["COL_ANT_AZ"] = 100.0,
            ["COL_CQZ"] = 5L,
            ["COL_ITUZ"] = 8L,
            ["COL_IOTA"] = "NA-062",
            ["COL_IOTA_ISLAND_ID"] = 4766L,
            ["COL_APP_HAMRADIODELUXE_ISLANDNAME"] = "Key West",
            ["COL_POTA_REF"] = "US-1234",
            ["COL_QSL_VIA"] = "",
            ["COL_COMMENT"] = "SK 2025-01-02",
            ["COL_SILENT_KEY"] = "1",
        };

        private static HrdBatchItem Item(HrdRowChanges? planned = null) =>
            new() { Pk = 1, Call = "W1AW", Row = Row(), Changes = planned ?? new HrdRowChanges() };

        private static string? Continent(int dxcc) => dxcc == 1 ? "NA" : dxcc == 256 ? "AF" : null;

        [Theory]
        [InlineData("COL_GRIDSQUARE", "fn13", "FN13")]
        [InlineData("COL_GRIDSQUARE", "FN13AH", "FN13ah")]
        [InlineData("COL_IOTA", "na-062", "NA-062")]
        [InlineData("COL_POTA_REF", "us-1234, us-5678", "US-1234,US-5678")]
        [InlineData("COL_STATE", "ny", "NY")]
        public void ParseNormalizes(string column, string text, string expected)
        {
            var (value, error) = HrdQsoEdits.Parse(column, text, "");
            Assert.Null(error);
            Assert.Equal(expected, value);
        }

        [Theory]
        [InlineData("COL_GRIDSQUARE", "FN1")]
        [InlineData("COL_GRIDSQUARE", "ZZ99")]
        [InlineData("COL_LAT", "91")]
        [InlineData("COL_LON", "-181")]
        [InlineData("COL_LAT", "north")]
        [InlineData("COL_CQZ", "41")]
        [InlineData("COL_CQZ", "0")]
        [InlineData("COL_ITUZ", "91")]
        [InlineData("COL_IOTA", "NA-62")]
        [InlineData("COL_POTA_REF", "US1234")]
        [InlineData("COL_COUNTRY", "Atlantis")]
        [InlineData("COL_COUNTRY", "")]
        [InlineData("COL_IOTA_ISLAND_ID", "Key West")]
        public void ParseRejects(string column, string text)
        {
            var (_, error) = HrdQsoEdits.Parse(column, text, "");
            Assert.NotNull(error);
        }

        [Fact]
        public void ParseTakesNumbersAndIslandIds()
        {
            Assert.Equal(43.5, HrdQsoEdits.Parse("COL_LAT", "43.5", null).Value);
            Assert.Equal(14, HrdQsoEdits.Parse("COL_CQZ", "14", null).Value);
            Assert.Equal(4766L, HrdQsoEdits.Parse("COL_IOTA_ISLAND_ID", "4766 - Key West", null).Value);
            Assert.Null(HrdQsoEdits.Parse("COL_CQZ", "", 5L).Value);
            Assert.Equal("", HrdQsoEdits.Parse("COL_NAME", "(blank)", "Hiram").Value);
        }

        [Fact]
        public void ParseUnknownColumnByOriginalType()
        {
            Assert.Equal(7L, HrdQsoEdits.Parse("COL_K_INDEX", "7", 3L).Value);
            Assert.NotNull(HrdQsoEdits.Parse("COL_K_INDEX", "x", 3L).Error);
            Assert.Equal(1.5, HrdQsoEdits.Parse("COL_SFI", "1.5", 2.0).Value);
            Assert.Equal("abc", HrdQsoEdits.Parse("COL_OTHER", " abc ", "x").Value);
        }

        [Fact]
        public void EditReplacesPlannedChange()
        {
            var planned = new HrdRowChanges();
            planned.Changes.Add(new HrdColumnChange("Name", "COL_NAME", "Hiram", "Hiram Percy"));
            var item = Item(planned);

            Assert.Null(HrdQsoEdits.Edit(item, "Name", "COL_NAME", "Hiram P.", Continent));
            var change = Assert.Single(item.Active!.Changes);
            Assert.Equal("Hiram P.", change.New);
            Assert.Equal("Hiram", change.Old);
        }

        [Fact]
        public void EditBackToWasDropsTheChange()
        {
            var planned = new HrdRowChanges();
            planned.Changes.Add(new HrdColumnChange("Name", "COL_NAME", "Hiram", "Hiram Percy"));
            var item = Item(planned);

            HrdQsoEdits.Edit(item, "Name", "COL_NAME", "hiram", Continent);
            Assert.False(item.Applicable);
        }

        [Fact]
        public void UntickedColumnIsLeftOut()
        {
            var planned = new HrdRowChanges();
            planned.Changes.Add(new HrdColumnChange("Name", "COL_NAME", "Hiram", "Hiram Percy"));
            planned.Changes.Add(new HrdColumnChange("QTH", "COL_QTH", null, "Newington"));
            var item = Item(planned);
            item.Excluded.Add("COL_NAME");

            var change = Assert.Single(item.Active!.Changes);
            Assert.Equal("COL_QTH", change.Column);
            var rows = HrdQsoEdits.Rows(item);
            Assert.False(rows.Single(r => r.Column == "COL_NAME").Ticked);
            Assert.True(rows.Single(r => r.Column == "COL_QTH").Ticked);
        }

        [Fact]
        public void GridMovesPositionAndDistance()
        {
            var item = Item();
            Assert.Null(HrdQsoEdits.Edit(item, "Grid square", "COL_GRIDSQUARE", "EM12", Continent));

            Assert.Equal("EM12", item.Current("COL_GRIDSQUARE"));
            var (lat, lon) = ((double)item.Current("COL_LAT")!, (double)item.Current("COL_LON")!);
            Assert.InRange(lat, 32, 33);
            Assert.InRange(lon, -98, -96);
            var (km, bearing) = HrdRowMapper.Recalc("FN13ah", "EM12")!.Value;
            Assert.Equal(km, item.Current("COL_DISTANCE"));
            Assert.Equal(bearing, item.Current("COL_ANT_AZ"));
        }

        [Fact]
        public void GridKeepsPositionAlreadyInside()
        {
            var item = Item();
            HrdQsoEdits.Edit(item, "Grid square", "COL_GRIDSQUARE", "FN31", Continent);
            Assert.Equal(41.7146, item.Current("COL_LAT"));
            Assert.DoesNotContain(item.Active!.Changes, c => c.Column == "COL_LAT");
        }

        [Fact]
        public void LatitudeMovesGrid()
        {
            var item = Item();
            Assert.Null(HrdQsoEdits.Edit(item, "Latitude", "COL_LAT", "43.1234", Continent));
            Assert.Equal(Maidenhead.Normalize(Maidenhead.ToGridSquare(43.1234, -72.7272)), item.Current("COL_GRIDSQUARE"));
            Assert.Contains(item.Active!.Changes, c => c.Column == "COL_DISTANCE");
        }

        [Fact]
        public void CountrySetsDxccAndContinent()
        {
            var item = Item();
            Assert.Null(HrdQsoEdits.Edit(item, "Country", "COL_COUNTRY", "madeira is.", Continent));
            Assert.Equal("Madeira Is.", item.Current("COL_COUNTRY"));
            Assert.Equal("256", item.Current("COL_DXCC"));
            Assert.Equal("AF", item.Current("COL_CONT"));
        }

        [Fact]
        public void NewIotaEmptiesIsland()
        {
            var item = Item();
            Assert.Null(HrdQsoEdits.Edit(item, "IOTA", "COL_IOTA", "AF-014", Continent));
            Assert.Null(item.Current("COL_IOTA_ISLAND_ID"));
            Assert.Equal("", item.Current("COL_APP_HAMRADIODELUXE_ISLANDNAME"));
        }

        [Fact]
        public void SkNoteCantBeDropped()
        {
            var item = Item();
            Assert.NotNull(HrdQsoEdits.Edit(item, "Comment", "COL_COMMENT", "nice op", Continent));
            Assert.False(item.Applicable);

            // Moved to QSL via first, it can go from the comment.
            Assert.Null(HrdQsoEdits.Edit(item, "QSL via", "COL_QSL_VIA", "SK 2025-01-02", Continent));
            Assert.Null(HrdQsoEdits.Edit(item, "Comment", "COL_COMMENT", "nice op", Continent));
        }

        [Fact]
        public void MainFieldsAlwaysShown()
        {
            var rows = HrdQsoEdits.Rows(Item());
            foreach (var field in HrdQsoEdits.MainFields) Assert.Contains(rows, r => r.Column == field.Column && !r.Changes);
        }

        [Fact]
        public void RestoresPutBackWhatLookupCleared()
        {
            var before = Row();
            var after = Row();
            after["COL_POTA_REF"] = "";
            after["COL_IOTA"] = "NA-001";
            after["COL_IOTA_ISLAND_ID"] = null;
            after["COL_COMMENT"] = "";
            after["COL_SILENT_KEY"] = "0";
            after["COL_NAME"] = "Someone else";

            var restores = HrdQsoEdits.Restores(before, after);
            Assert.Equal(
                ["COL_POTA_REF", "COL_IOTA", "COL_IOTA_ISLAND_ID", "COL_COMMENT", "COL_SILENT_KEY"],
                restores.Select(r => r.Column));
            Assert.Equal("US-1234", restores[0].New);
            Assert.Equal("", restores[0].Old);
        }

        [Fact]
        public void RestoresIgnoreSameOrBlank()
        {
            var before = Row();
            before["COL_POTA_REF"] = "";
            var after = Row();
            after["COL_POTA_REF"] = "US-9999";
            after["COL_COMMENT"] = "SK 2025-01-02 tnx";
            Assert.Empty(HrdQsoEdits.Restores(before, after));
        }

        [Fact]
        public void RestoreWinsOverPlanAndEditWinsOverRestore()
        {
            var planned = new HrdRowChanges();
            planned.Changes.Add(new HrdColumnChange("POTA", "COL_POTA_REF", "US-1234", ""));
            var item = Item(planned);
            item.Restores.Add(new HrdColumnChange("POTA", "COL_POTA_REF", "", "US-1234"));
            Assert.False(item.Applicable);   // the plan's clearing is undone

            HrdQsoEdits.Edit(item, "POTA", "COL_POTA_REF", "US-5555", Continent);
            Assert.Equal("US-5555", Assert.Single(item.Active!.Changes).New);
        }
    

        [Theory]
        [InlineData("2026-11-07")]
        [InlineData("11/07/2026")]
        [InlineData("20261107")]
        [InlineData("07-Nov-2026")]
        public void WindowDateIsRead(string shown) =>
            Assert.Contains("2026-11-07", MainForm.DateCandidates(shown));

        [Fact]
        public void AmbiguousWindowDateGivesBoth()
        {
            var dates = MainForm.DateCandidates("03/04/2026");
            Assert.Contains("2026-03-04", dates);
            Assert.Contains("2026-04-03", dates);
        }
    }
}

namespace CallsignLookup.Tests
{
    public class HrdCountyTests
    {
        [Theory]
        [InlineData("NY", "Monroe", "NY,MONROE")]
        [InlineData("", "Monroe", "MONROE")]
        [InlineData("NY", "NY,Monroe", "NY,MONROE")]
        [InlineData("NY", "", "")]
        public void WrittenHrdsWay(string state, string county, string expected) =>
            Assert.Equal(expected, CallsignLookup.Services.Hrd.HrdRowMapper.HrdCounty(state, county));

        [Theory]
        [InlineData("NY,MONROE", "Monroe", true)]
        [InlineData("Monroe", "MONROE", true)]
        [InlineData("NY,MONROE", "NY,Monroe", true)]
        [InlineData("NY,ERIE", "Monroe", false)]
        [InlineData("", "Monroe", false)]
        public void SameCountyEitherForm(string a, string b, bool same) =>
            Assert.Equal(same, CallsignLookup.Services.Hrd.HrdRowMapper.SameCounty(a, b));
    }
}
