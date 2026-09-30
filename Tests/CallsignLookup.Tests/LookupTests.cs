using CallsignLookup.Services;

namespace CallsignLookup.Tests
{
    public class MaidenheadTests
    {
        [Theory]
        [InlineData(41.714775, -72.727260, "FN31pr")] // W1AW, Newington CT
        [InlineData(-33.8688, 151.2093, "QF56od")]    // Sydney
        [InlineData(0.0, 0.0, "JJ00aa")]
        [InlineData(90.0, 180.0, "RR99xx")]           // edges stay inside the last square
        public void ToGridSquare_KnownLocations(double lat, double lon, string expected) =>
            Assert.Equal(expected, Maidenhead.ToGridSquare(lat, lon));

        [Theory]
        [InlineData("FN31pr")]
        [InlineData("em12KV")]
        [InlineData("QF56od")]
        public void TryGetCenter_RoundTrips(string grid)
        {
            Assert.True(Maidenhead.TryGetCenter(grid, out double lat, out double lon));
            Assert.Equal(grid, Maidenhead.ToGridSquare(lat, lon), ignoreCase: true);
        }

        [Fact]
        public void TryGetCenter_FourCharacterGrid()
        {
            Assert.True(Maidenhead.TryGetCenter("EM12", out double lat, out double lon));
            Assert.Equal(32.5, lat, 6);
            Assert.Equal(-97.0, lon, 6);
        }

        [Theory]
        [InlineData("")]
        [InlineData("ZZ12")]
        [InlineData("EM1")]
        [InlineData("EM12zz")]
        public void TryGetCenter_RejectsInvalid(string grid) =>
            Assert.False(Maidenhead.TryGetCenter(grid, out _, out _));
    }

    public class CountyLookupTests
    {
        [Theory]
        [InlineData(32.7767, -96.7970, "Dallas", "TX")]
        [InlineData(41.6979, -72.7237, "Hartford", "CT")]
        [InlineData(61.2181, -149.9003, "Anchorage", "AK")]
        public void FindCounty_UsLocations(double lat, double lon, string county, string state)
        {
            var match = CountyLookupService.FindCounty(lat, lon);
            Assert.NotNull(match);
            Assert.Equal(county, match.County);
            Assert.Equal(state, match.StateAbbrev);
        }

        [Theory]
        [InlineData(29.20, -94.60, "TX", "Galveston")] // just offshore in the Gulf
        [InlineData(43.00, -87.00, "WI", "")]          // mid-Lake Michigan, WI side preferred
        [InlineData(43.00, -87.00, "MI", "")]          // same point, MI side preferred
        public void FindCounty_OutsideEveryOutline_SnapsToNearestInState(double lat, double lon, string state, string county)
        {
            var match = CountyLookupService.FindCounty(lat, lon, state);
            Assert.NotNull(match);
            Assert.Equal(state, match.StateAbbrev);
            if (county.Length > 0) Assert.Equal(county, match.County);
        }
    }

    public class ZoneLookupTests
    {
        [Theory]
        [InlineData(32.7767, -96.7970, 4, 7)]    // Dallas
        [InlineData(41.714775, -72.727260, 5, 8)] // W1AW
        [InlineData(51.5074, -0.1278, 14, 27)]   // London
        [InlineData(35.6762, 139.6503, 25, 45)]  // Tokyo
        [InlineData(-33.8688, 151.2093, 30, 59)] // Sydney
        [InlineData(21.3069, -157.8583, 31, 61)] // Honolulu
        [InlineData(52.93, 173.2, 1, 1)]         // Attu Island - east of the antimeridian
        public void FindZones_KnownLocations(double lat, double lon, int cq, int itu)
        {
            Assert.Equal(cq, ZoneLookupService.FindCqZone(lat, lon));
            Assert.Equal(itu, ZoneLookupService.FindItuZone(lat, lon));
        }
    }

    public class ArrlSectionTests
    {
        [Theory]
        [InlineData(32.7767, -96.7970, "NTX")]   // Dallas
        [InlineData(29.7604, -95.3698, "STX")]   // Houston
        [InlineData(31.7619, -106.4850, "WTX")]  // El Paso
        [InlineData(41.714775, -72.727260, "CT")]
        [InlineData(34.0522, -118.2437, "LAX")]
        [InlineData(33.7175, -117.8311, "ORG")]  // Santa Ana
        [InlineData(39.2904, -76.6122, "MDC")]   // Baltimore
        [InlineData(38.9072, -77.0369, "MDC")]   // Washington DC
        [InlineData(21.3069, -157.8583, "PAC")]  // Honolulu
        [InlineData(47.6062, -122.3321, "WWA")]  // Seattle
        [InlineData(47.6588, -117.4260, "EWA")]  // Spokane
        [InlineData(40.6782, -73.9442, "NLI")]   // Brooklyn
        [InlineData(43.0481, -76.1474, "WNY")]   // Syracuse
        [InlineData(25.7617, -80.1918, "SFL")]   // Miami
        [InlineData(27.9506, -82.4572, "WCF")]   // Tampa
        [InlineData(30.3322, -81.6557, "NFL")]   // Jacksonville
        [InlineData(39.9526, -75.1652, "EPA")]   // Philadelphia
        [InlineData(40.4406, -79.9959, "WPA")]   // Pittsburgh
        [InlineData(39.3643, -74.4229, "SNJ")]   // Atlantic City
        [InlineData(40.7357, -74.1724, "NNJ")]   // Newark
        [InlineData(42.3601, -71.0589, "EMA")]   // Boston
        [InlineData(42.1015, -72.5898, "WMA")]   // Springfield MA
        public void FromUsCounty_KnownLocations(double lat, double lon, string expected)
        {
            var county = CountyLookupService.FindCounty(lat, lon);
            Assert.NotNull(county);
            Assert.Equal(expected, ArrlSectionService.FromUsCounty(county)?.Section);
        }

        [Theory]
        [InlineData("AB", 51.0447, -114.0719, "AB")]
        [InlineData("nt", 62.4540, -114.3718, "TER")]
        [InlineData("PE", 46.2382, -63.1311, "PE")]
        [InlineData("ON", 43.6532, -79.3832, "GH")]   // Toronto
        [InlineData("ON", 43.2557, -79.8711, "GH")]   // Hamilton (moved from ONS in 2023)
        [InlineData("ON", 43.1594, -79.2469, "GH")]   // St. Catharines, Niagara (moved from ONS in 2023)
        [InlineData("ON", 45.4215, -75.6972, "ONE")]  // Ottawa
        [InlineData("ON", 44.2312, -76.4860, "ONE")]  // Kingston
        [InlineData("ON", 44.3091, -78.3197, "ONE")]  // Peterborough
        [InlineData("ON", 45.5700, -78.7300, "ONE")]  // Canoe Lake, inside Algonquin Park (Nipissing)
        [InlineData("ON", 46.3091, -79.4608, "ONN")]  // North Bay (Nipissing, outside the park)
        [InlineData("ON", 46.3100, -78.7000, "ONN")]  // Mattawa (Nipissing, north of the park)
        [InlineData("ON", 46.4917, -80.9930, "ONN")]  // Greater Sudbury
        [InlineData("ON", 48.3809, -89.2477, "ONN")]  // Thunder Bay
        [InlineData("ON", 42.9849, -81.2453, "ONS")]  // London
        [InlineData("ON", 44.3894, -79.6903, "ONS")]  // Barrie, Simcoe
        [InlineData("ON", 45.3269, -79.2167, "ONS")]  // Huntsville, Muskoka
        [InlineData("ON", 42.3149, -83.0364, "ONS")]  // Windsor
        public void FromCanadianStation_KnownLocations(string province, double lat, double lon, string expected) =>
            Assert.Equal(expected, ArrlSectionService.FromCanadianStation(province, lat, lon)?.Section);
    }

    public class QrzParseTests
    {
        private const string SampleResponse = """
            <?xml version="1.0" encoding="utf-8" ?>
            <QRZDatabase version="1.34" xmlns="http://xmldata.qrz.com">
              <Callsign>
                <call>W1AW</call>
                <fname>ARRL HQ</fname>
                <name>OPERATORS CLUB</name>
                <addr1>225 MAIN ST</addr1>
                <addr2>NEWINGTON</addr2>
                <state>CT</state>
                <zip>06111</zip>
                <country>United States</country>
                <land>United States</land>
                <dxcc>291</dxcc>
                <lat>41.714775</lat>
                <lon>-72.727260</lon>
                <grid>FN31pr</grid>
                <county>Hartford</county>
                <cqzone>5</cqzone>
                <ituzone>8</ituzone>
                <geoloc>user</geoloc>
              </Callsign>
              <Session>
                <Key>2331uf894c4bd29f3923f3bacf02c532d7bd9</Key>
                <Count>123</Count>
              </Session>
            </QRZDatabase>
            """;

        [Fact]
        public void ParseCallsignResponse_ReadsFields()
        {
            var record = QrzService.ParseCallsignResponse(SampleResponse.Trim());
            Assert.Equal("W1AW", record.Call);
            Assert.Equal("ARRL HQ OPERATORS CLUB", record.FullName);
            Assert.Equal("NEWINGTON", record.City);
            Assert.Equal(41.714775, record.Latitude);
            Assert.Equal(-72.727260, record.Longitude);
            Assert.Equal("user", record.GeoLoc);
            Assert.Equal(291, record.Dxcc);
            Assert.True(record.IsUnitedStates);
        }

        [Fact]
        public void ParseCallsignResponse_NotFound_Throws()
        {
            const string xml = """
                <QRZDatabase version="1.34" xmlns="http://xmldata.qrz.com">
                  <Session><Error>Not found: XX9XXX</Error></Session>
                </QRZDatabase>
                """;
            var ex = Assert.Throws<QrzException>(() => QrzService.ParseCallsignResponse(xml));
            Assert.Contains("Not found", ex.Message);
        }

        [Fact]
        public void Resolve_FullRecord()
        {
            var result = CallsignLookupService.Resolve(QrzService.ParseCallsignResponse(SampleResponse.Trim()));
            Assert.Equal("FN31pr", result.GridSquare);
            Assert.Equal("Hartford", result.County?.County);
            Assert.Equal(5, result.CqZone);
            Assert.Equal(8, result.ItuZone);
            Assert.Equal("CT", result.ArrlSection?.Section);
        }

        [Fact]
        public void Resolve_NoLatLong_FallsBackToGridCenter()
        {
            var record = new QrzCallsignRecord { Call = "K5TST", Grid = "EM12kv", Country = "United States", State = "TX" };
            var result = CallsignLookupService.Resolve(record);
            Assert.Equal("EM12kv", result.GridSquare);
            Assert.Contains("approximate", result.LocationSource);
            Assert.Equal("NTX", result.ArrlSection?.Section);
        }

        [Fact]
        public void Resolve_Canada_UsesProvince_NoCounty()
        {
            var record = new QrzCallsignRecord { Call = "VE6TST", Latitude = 51.0447, Longitude = -114.0719, Country = "Canada", State = "AB" };
            var result = CallsignLookupService.Resolve(record);
            Assert.Null(result.County);
            Assert.Equal("AB", result.ArrlSection?.Section);
            Assert.Equal(4, result.CqZone);
            Assert.Equal(2, result.ItuZone);
        }

        [Fact]
        public void Resolve_CanadaNextToBorder_NoUsCounty()
        {
            // Windsor sits right across the river from Detroit.
            var record = new QrzCallsignRecord { Call = "VA3TST", Latitude = 42.3149, Longitude = -83.0364, Land = "Canada", State = "ON" };
            var result = CallsignLookupService.Resolve(record);
            Assert.Null(result.County);
            Assert.Equal("ONS", result.ArrlSection?.Section);
        }

        [Fact]
        public void Resolve_Mexico_NoCounty()
        {
            var record = new QrzCallsignRecord { Call = "XE2TST", Latitude = 31.6904, Longitude = -106.4245, Land = "Mexico" };
            var result = CallsignLookupService.Resolve(record);
            Assert.Null(result.County);
            Assert.Null(result.ArrlSection);
        }

        [Fact]
        public void Resolve_UsOffshore_StillGetsCounty()
        {
            var record = new QrzCallsignRecord { Call = "K5TST", Latitude = 29.20, Longitude = -94.60, Land = "United States", State = "TX" };
            var result = CallsignLookupService.Resolve(record);
            Assert.Equal("Galveston", result.County?.County);
            Assert.Equal("STX", result.ArrlSection?.Section);
        }

        [Fact]
        public void Resolve_Hawaii_DxccEntityCountsAsUs()
        {
            var record = new QrzCallsignRecord { Call = "KH6TST", Latitude = 21.3069, Longitude = -157.8583, Dxcc = 110, Land = "Hawaii", Country = "United States", State = "HI" };
            var result = CallsignLookupService.Resolve(record);
            Assert.Equal("Honolulu", result.County?.County);
            Assert.Equal("PAC", result.ArrlSection?.Section);
        }

        [Theory]
        [InlineData(6, 61.2181, -149.9003, "Anchorage", "AK")]
        [InlineData(202, 18.4655, -66.1057, "San Juan", "PR")]
        [InlineData(285, 18.3419, -64.9307, "St. Thomas", "VI")]
        [InlineData(103, 13.4443, 144.7937, "Guam", "GU")]
        [InlineData(9, -14.2756, -170.7020, "Eastern", "AS")]
        [InlineData(166, 15.1850, 145.7467, "Saipan", "MP")]
        public void Resolve_SeparateUsDxccEntities_GetCounties(int dxcc, double lat, double lon, string county, string state)
        {
            var record = new QrzCallsignRecord { Call = "N0TST", Latitude = lat, Longitude = lon, Dxcc = dxcc };
            var result = CallsignLookupService.Resolve(record);
            Assert.Equal(county, result.County?.County);
            Assert.Equal(state, result.County?.StateAbbrev);
        }

        [Fact]
        public void Resolve_GuantanamoBay_NoCounty()
        {
            // US-administered (KG4) but its own DXCC entity with no county -
            // must not snap to the nearest Florida or Puerto Rico county.
            var record = new QrzCallsignRecord { Call = "KG4TST", Latitude = 19.9031, Longitude = -75.0998, Dxcc = 105, Country = "United States" };
            var result = CallsignLookupService.Resolve(record);
            Assert.Null(result.County);
            Assert.Null(result.ArrlSection);
        }

        [Fact]
        public void IsUnitedStates_FallsBackToEntityNameWithoutDxccNumber()
        {
            Assert.True(new QrzCallsignRecord { Land = "puerto rico" }.IsUnitedStates);
            Assert.True(new QrzCallsignRecord { Country = "United States" }.IsUnitedStates);
            Assert.False(new QrzCallsignRecord { Land = "Mexico" }.IsUnitedStates);
            Assert.True(new QrzCallsignRecord { Dxcc = 1, Country = "United States" }.IsCanadian);
        }
    }
}
