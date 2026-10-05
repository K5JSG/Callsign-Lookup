using CallsignLookup.Services;
using CallsignLookup.Services.Hrd;

namespace CallsignLookup.Tests
{
    public class HrdPotaParksTests
    {
        private static bool AnyPark(string reference) => true;

        [Theory]
        [InlineData("US-3033", "US-3033", "")]
        [InlineData("POTA: US-3033", "US-3033", "")]
        [InlineData("POTA: US-4579 & US-4566 & US-4576", "US-4579,US-4566,US-4576", "")]
        [InlineData("POTA: US-10236, US-7442 & US-7335", "US-10236,US-7442,US-7335", "")]
        [InlineData("Nice signal POTA: US-1611 & US-6544 73", "US-1611,US-6544", "Nice signal 73")]
        [InlineData("pota: ca-4214", "CA-4214", "")]
        public void TakeReferences_FromComment(string comment, string refs, string rest)
        {
            var (found, remaining, unknown) = HrdPotaParks.TakeReferences(comment, AnyPark);
            Assert.Equal(refs, string.Join(",", found));
            Assert.Equal(rest, remaining);
            Assert.Empty(unknown);
        }

        [Theory]
        [InlineData("Requested QSL Via Bureau From ClubLog on 09-DEC-2024")]
        [InlineData("")]
        [InlineData("TNX QSO 73")]
        public void TakeReferences_IgnoresThingsThatArentParks(string comment)
        {
            var (found, remaining, unknown) = HrdPotaParks.TakeReferences(comment, AnyPark);
            Assert.Empty(found);
            Assert.Empty(unknown);
            Assert.Equal(comment, remaining);
        }

        [Fact]
        public void TakeReferences_LeavesCommentAloneForAnUnknownPark()
        {
            var (found, remaining, unknown) = HrdPotaParks.TakeReferences("POTA: US-3033 & US-99999", r => r == "US-3033");
            Assert.Empty(found);
            Assert.Equal("POTA: US-3033 & US-99999", remaining);
            Assert.Equal(["US-99999"], unknown);
        }

        [Fact]
        public void ParseLine_ReadsHrdParkList()
        {
            var park = HrdPotaParks.ParseLine("\"US-3033\",\"Lockhart State Park\",\"1\",\"291\",\"US-TX\",\"29.8494\",\"-97.6969\",\"EL19du\"");
            Assert.Equal(new PotaPark("US-3033", "Lockhart State Park", "US-TX", 29.8494, -97.6969, "EL19du"), park);
        }

        [Fact]
        public void ParseLine_QuotedCommaInName()
        {
            var park = HrdPotaParks.ParseLine("\"US-0001\",\"Park, The \"\"Big\"\" One\",\"1\",\"291\",\"US-ME\",\"44.31\",\"-68.2034\",\"FN54vh\"");
            Assert.Equal("Park, The \"Big\" One", park?.Name);
        }
    }

    public class IotaIslandIdTests
    {
        [Theory]
        [InlineData("NA-062", "Key West", "4766")]
        [InlineData("EU-005", "Great Britain", "9292")]
        [InlineData("na-062", "key west", "4766")]
        public void IslandId_MatchesHrd(string refNo, string island, string id) =>
            Assert.Equal(id, IotaService.IslandId(refNo, island));

        [Fact]
        public void IslandId_NullForAnIslandNotOnTheList() =>
            Assert.Null(IotaService.IslandId("NA-062", "Not An Island"));
    }

    public class HrdQsoFillerTests
    {
        private static readonly PotaPark Lockhart = new("US-3033", "Lockhart State Park", "US-TX", 29.8494, -97.6969, "EL19du");

        private static PotaPark? FindPark(string reference) => reference switch
        {
            "US-3033" => Lockhart,
            "US-4566" => new PotaPark("US-4566", "Second Park", "US-TX", 30.0, -97.0, "EM10ia"),
            _ => null,
        };

        // W5ERX as HRD had it after its own QRZ lookup, before the user's cleanup.
        private static Dictionary<string, string> W5erxQso() => new()
        {
            ["edtCALL"] = "W5ERX",
            ["edtNAME"] = "Wade T Bolling",
            ["edtQTH"] = "Kyle",
            ["edtSTATE"] = "TX",
            ["edtCNTY"] = "Hays",
            ["memCOMMENT"] = "US-3033",
            ["edtPOTA_REF"] = "",
            ["edtPOTA_REF2"] = "",
            ["edtAPP_HAMRADIODELUXE_POTA_NAME"] = "",
            ["edtAPP_HAMRADIODELUXE_POTA_LAT"] = "",
            ["edtAPP_HAMRADIODELUXE_POTA_LON"] = "",
            ["edtLAT"] = "30.0213",
            ["edtLON"] = "-97.8808",
            ["edtGRIDSQUARE"] = "EM10ba",
            ["edtCQZ"] = "",
            ["edtITUZ"] = "",
            ["cbxARRLSECT"] = "",
            ["edtQSL_VIA"] = "",
            ["edtIOTARefNo"] = "-none-",
            ["edtIOTAIsland"] = "",
        };

        private static QrzCallsignRecord W5erx() => new()
        {
            Call = "W5ERX",
            FirstName = "Wade T",
            LastName = "Bolling",
            City = "Kyle",
            State = "TX",
            County = "Hays",
            Dxcc = 291,
            Latitude = 30.021276,
            Longitude = -97.880774,
        };

        private static HrdFillPlan Plan(Dictionary<string, string> qso, QrzCallsignRecord record) =>
            HrdQsoFiller.Plan(qso, CallsignLookupService.Resolve(record), FindPark, new HrdStationProfile("Home", "K5JSG"));

        private static Dictionary<string, string> Changes(HrdFillPlan plan) =>
            plan.Changes.ToDictionary(c => c.Label, c => c.New);

        [Fact]
        public void PotaQso_TheUsersW5erxCleanup()
        {
            var plan = Plan(W5erxQso(), W5erx());

            Assert.Equal(new Dictionary<string, string>
            {
                ["Comment"] = "",
                ["POTA Ref"] = "US-3033",
                ["Park Ref (POTA tab)"] = "US-3033",
                ["Park Name"] = "Lockhart State Park",
                ["Park Latitude"] = "29.8494",
                ["Park Longitude"] = "-97.6969",
                ["Park Location"] = "US-TX",
                ["Park Grid"] = "EL19du",
                ["Latitude"] = "29.8494",
                ["Longitude"] = "-97.6969",
                ["US County"] = "Caldwell",   // the park's county, not his home county (Hays)
                ["Grid Square"] = "EL19du",
                ["CQ Zone"] = "4",
                ["ITU Zone"] = "7",
                ["ARRL Section"] = "STX",
            }, Changes(plan));
            Assert.Empty(plan.Warnings);
            Assert.Equal("Home - K5JSG", plan.Profile?.DisplayName);
        }

        [Fact]
        public void AlreadyDone_ChangesNothing()
        {
            var qso = W5erxQso();
            qso["memCOMMENT"] = "";
            qso["edtPOTA_REF"] = "US-3033";
            qso["edtPOTA_REF2"] = "US-3033";
            qso["edtCNTY"] = "Caldwell";
            qso["edtAPP_HAMRADIODELUXE_POTA_NAME"] = "Lockhart State Park";
            qso["edtAPP_HAMRADIODELUXE_POTA_LAT"] = "29.8494";
            qso["edtAPP_HAMRADIODELUXE_POTA_LON"] = "-97.6969";
            qso["edtLAT"] = "29.8494";
            qso["edtLON"] = "-97.6969";
            qso["edtGRIDSQUARE"] = "EL19DU";
            qso["edtCQZ"] = "4";
            qso["edtITUZ"] = "7";
            qso["cbxARRLSECT"] = "STX";

            Assert.Empty(Plan(qso, W5erx()).Changes);
        }

        [Fact]
        public void HrdPickersRoundedParkLocation_CountsAsTheSame()
        {
            // W5ERX as the user saved it: HRD's POTA picker rounds to 3 places.
            var qso = W5erxQso();
            qso["memCOMMENT"] = "";
            qso["edtPOTA_REF"] = "US-3033";
            qso["edtPOTA_REF2"] = "US-3033";
            qso["edtCNTY"] = "Caldwell";
            qso["edtAPP_HAMRADIODELUXE_POTA_NAME"] = "Lockhart State Park";
            qso["edtAPP_HAMRADIODELUXE_POTA_LAT"] = "29.849";
            qso["edtAPP_HAMRADIODELUXE_POTA_LON"] = "-97.697";
            qso["edtLAT"] = "29.849";
            qso["edtLON"] = "-97.697";
            qso["edtGRIDSQUARE"] = "EL19du";
            qso["edtCQZ"] = "4";
            qso["edtITUZ"] = "7";
            qso["cbxARRLSECT"] = "STX";

            Assert.Empty(Plan(qso, W5erx()).Changes);
        }

        [Fact]
        public void NoPark_UsesTheQrzLocation_AndClearsQslVia()
        {
            var qso = W5erxQso();
            qso["memCOMMENT"] = "TNX FER QSO";
            qso["edtQSL_VIA"] = "BURO";
            var changes = Changes(Plan(qso, W5erx()));

            Assert.False(changes.ContainsKey("Comment"));
            Assert.False(changes.ContainsKey("POTA Ref"));
            Assert.False(changes.ContainsKey("Latitude"));    // 30.0213 is QRZ's, to 4 places
            Assert.False(changes.ContainsKey("Grid Square")); // EM10ba vs EM10BA - only the case differs
            Assert.Equal("4", changes["CQ Zone"]);
            Assert.Equal("", changes["QSL Manager/VIA"]);
        }

        [Fact]
        public void MultiplePark_CommaListAndFirstParksLocation()
        {
            var qso = W5erxQso();
            qso["memCOMMENT"] = "POTA: US-3033 & US-4566";
            var plan = Plan(qso, W5erx());
            var changes = Changes(plan);

            Assert.Equal("US-3033,US-4566", changes["POTA Ref"]);
            Assert.Equal("", changes["Comment"]);
            Assert.Equal("Lockhart State Park", changes["Park Name"]); // the first park's, by the user's choice
            Assert.Equal("29.8494", changes["Park Latitude"]);
            Assert.Equal("29.8494", changes["Latitude"]);
            Assert.Contains(plan.Warnings, w => w.Contains("2 parks"));
        }

        [Fact]
        public void ParksInTheComment_ReplaceWhateverIsInThePotaField()
        {
            // KD2SER: junk US-0001 in the POTA field, the real parks in the Comment.
            var qso = W5erxQso();
            qso["edtPOTA_REF"] = "US-0001";
            qso["memCOMMENT"] = "US-3033,US-4566";
            var changes = Changes(Plan(qso, W5erx()));

            Assert.Equal("US-3033,US-4566", changes["POTA Ref"]);
            Assert.Equal("", changes["Comment"]);
            Assert.Equal("Lockhart State Park", changes["Park Name"]);
            Assert.Equal("29.8494", changes["Latitude"]);     // the QSO is at the first park
        }

        [Theory]
        [InlineData("Nice signal POTA: US-3033 73")]
        [InlineData("US-3033 - worked him again from home later")]
        public void CommentWithOtherText_IsLeftAlone(string comment)
        {
            var qso = W5erxQso();
            qso["edtPOTA_REF"] = "US-4566";
            qso["memCOMMENT"] = comment;
            var plan = Plan(qso, W5erx());
            var changes = Changes(plan);

            Assert.False(changes.ContainsKey("Comment"));
            Assert.False(changes.ContainsKey("POTA Ref"));             // the POTA field's own park stands
            Assert.Equal("Second Park", changes["Park Name"]);
            Assert.Contains(plan.Warnings, w => w.Contains("other text"));
        }

        [Fact]
        public void Name_WithHrdsNicknameIsFine()
        {
            var qso = W5erxQso();
            qso["edtNAME"] = "WILLIAM \"Bill\" HAMALAINEN";
            var record = new QrzCallsignRecord
            {
                Call = "W5ERX", FirstName = "WILLIAM", Nickname = "Bill", LastName = "HAMALAINEN",
                City = "Kyle", State = "TX", County = "Hays", Dxcc = 291, Latitude = 30.021276, Longitude = -97.880774,
            };
            Assert.False(Changes(Plan(qso, record)).ContainsKey("Name"));

            qso["edtNAME"] = "Bill Hamalainen";
            Assert.Equal("WILLIAM \"Bill\" HAMALAINEN", Changes(Plan(qso, record))["Name"]);
        }

        [Fact]
        public void WrongOrBlankContactDetails_AreFixed()
        {
            var qso = W5erxQso();
            qso["memCOMMENT"] = ""; // no park, so QRZ's county
            qso["edtQTH"] = "";
            qso["edtCNTY"] = "Travis";
            var plan = Plan(qso, W5erx());

            var qth = plan.Changes.Single(c => c.Label == "QTH");
            Assert.True(qth.WasBlank);
            var county = plan.Changes.Single(c => c.Label == "US County");
            Assert.False(county.WasBlank);
            Assert.Equal(("Travis", "Hays", HrdSetBy.DropDown), (county.Old, county.New, county.How));
        }

        [Fact]
        public void QrzRecordForAnotherCall_IsWarned()
        {
            var qso = W5erxQso();
            qso["edtCALL"] = "W5ERX/P";
            Assert.Contains(Plan(qso, W5erx()).Warnings, w => w.Contains("not W5ERX/P"));
        }

        [Fact]
        public void Island_RefAndHrdIslandId()
        {
            var qso = W5erxQso();
            qso["memCOMMENT"] = "";
            var record = new QrzCallsignRecord
            {
                Call = "W5ERX", FirstName = "Wade T", LastName = "Bolling", City = "Key West", State = "FL",
                County = "Monroe", Dxcc = 291, Latitude = 24.5551, Longitude = -81.7800,
            };
            var plan = Plan(qso, record);

            var iota = plan.Changes.Single(c => c.Label == "IOTA Ref");
            Assert.Equal(("NA-062", HrdSetBy.TypeInList), (iota.Typed, iota.How));
            var island = plan.Changes.Single(c => c.Label == "IOTA Island");
            Assert.Equal(("4766", "4766 - Key West"), (island.Typed, island.New));

            qso["edtIOTARefNo"] = "NA-062 - Florida State (Florida Keys) group";
            qso["edtIOTAIsland"] = "4766 - Key West";
            Assert.DoesNotContain(Plan(qso, record).Changes, c => c.Tab == "IOTA");
        }

        [Fact]
        public void HrdIotaFarFromTheLocation_IsCleared()
        {
            // KD2SER in New York with AF-006 Diego Garcia.
            var qso = W5erxQso();
            qso["edtIOTARefNo"] = "AF-006 - Diego Garcia Island";
            var plan = Plan(qso, W5erx());
            var iota = plan.Changes.Single(c => c.Tab == "IOTA");
            Assert.Equal(("IOTA Ref", "-none-", "-none-", HrdSetBy.TypeInList), (iota.Label, iota.New, iota.Typed, iota.How));
        }

        [Fact]
        public void HrdIotaInTheGroupsArea_IsOnlyWarned()
        {
            // Off the Florida Keys but inside NA-062's box: could be a small
            // island the island data hasn't got.
            var qso = W5erxQso();
            qso["memCOMMENT"] = "";
            qso["edtIOTARefNo"] = "NA-062 - Florida State (Florida Keys) group";
            var record = new QrzCallsignRecord
            {
                Call = "W5ERX", FirstName = "Wade T", LastName = "Bolling", City = "Key West", State = "FL",
                County = "Monroe", Dxcc = 291, Latitude = 24.70, Longitude = -81.20,
            };
            var plan = Plan(qso, record);
            Assert.DoesNotContain(plan.Changes, c => c.Tab == "IOTA");
            Assert.Contains(plan.Warnings, w => w.Contains("HRD has IOTA"));
        }

        [Fact]
        public void NewPotaRef_RefillsTheWholePotaTab()
        {
            // HRD empties its POTA tab when the POTA field changes, so every
            // field there is set even if it looked right beforehand.
            var qso = W5erxQso();
            qso["edtAPP_HAMRADIODELUXE_POTA_NAME"] = "Lockhart State Park";
            var pota = Plan(qso, W5erx()).Changes.Where(c => c.Tab == "POTA").ToDictionary(c => c.WriteId, c => c.New);
            Assert.Equal(new Dictionary<string, string>
            {
                ["edtPOTA_REF2"] = "US-3033",
                ["edtAPP_HAMRADIODELUXE_POTA_NAME"] = "Lockhart State Park",
                ["edtAPP_HAMRADIODELUXE_POTA_LAT"] = "29.8494",
                ["edtAPP_HAMRADIODELUXE_POTA_LON"] = "-97.6969",
                ["edtPOTACountry"] = "US-TX",
                ["edtPOTAGrid"] = "EL19du",
            }, pota);
        }

        [Theory]
        [InlineData(29.8494, "29.8494")]
        [InlineData(29.849, "29.849")]
        [InlineData(-97.69694, "-97.6969")]
        [InlineData(43.315450, "43.3155")]
        public void Coordinate_FourPlacesLikeHrd(double value, string expected) =>
            Assert.Equal(expected, HrdQsoFiller.Coordinate(value));
    }
}
