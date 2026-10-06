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
            Assert.Equal(new PotaPark("US-3033", "Lockhart State Park", "US-TX", 29.8494, -97.6969, "EL19du", 291), park);
            Assert.Equal("TX", park?.State);
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
            "US-2068" => new PotaPark("US-2068", "Hamlin Beach State Park", "US-NY", 43.3619, -77.9574, "FN13ai", 291),
            "CA-0792" => new PotaPark("CA-0792", "Parlee Beach Provincial Park", "CA-NB", 46.2395, -64.5101, "FN76rf", 1),
            "DL-0001" => new PotaPark("DL-0001", "Jasmund National Park", "DL", 54.5434, 13.6211, "JO64ln", 230),
            _ => null,
        };

        // W5ERX as HRD had it after its own QRZ lookup, before the user's cleanup.
        private static Dictionary<string, string> W5erxQso() => new()
        {
            ["edtCALL"] = "W5ERX",
            ["cbxCOUNTRY"] = "United States",
            ["edtNAME"] = "Wade T Bolling",
            ["edtQTH"] = "Kyle",
            ["edtSTATE"] = "TX",
            ["edtCNTY"] = "Hays",
            ["cbxLocationStateProvince"] = "TX",
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

        // KL4RL/VE9: an Alaska call hunted at a New Brunswick park. HRD's
        // QRZ lookup filled in her Anchorage home.
        private static Dictionary<string, string> Kl4rlQso() => new()
        {
            ["edtCALL"] = "KL4RL/VE9",
            ["cbxCOUNTRY"] = "Alaska",
            ["edtNAME"] = "Jacquelyn C \"Cozette\" Green",
            ["edtQTH"] = "Anchorage",
            ["edtSTATE"] = "AK",
            ["edtCNTY"] = "Anchorage",
            ["memCOMMENT"] = "CA-0792",
            ["edtLAT"] = "61.1919",
            ["edtLON"] = "-149.8873",
            ["edtGRIDSQUARE"] = "BP51dd",
            ["edtCQZ"] = "1",
            ["edtITUZ"] = "1",
            ["cbxARRLSECT"] = "AK",
            ["edtIOTARefNo"] = "-none-",
        };

        private static QrzCallsignRecord Kl4rl() => new()
        {
            Call = "KL4RL",
            FirstName = "Jacquelyn C",
            LastName = "Green",
            Nickname = "Cozette",
            City = "Anchorage",
            State = "AK",
            County = "Anchorage",
            Dxcc = 6,
            Land = "Alaska",
            Latitude = 61.1919,
            Longitude = -149.8873,
        };

        [Fact]
        public void PotaQso_ParkInAnotherCountry_UsesTheParksCountryAndProvince()
        {
            var plan = Plan(Kl4rlQso(), Kl4rl());
            var changes = Changes(plan);

            Assert.Equal("CA-0792", changes["POTA Ref"]);
            Assert.Equal("Canada", changes["Country"]);
            Assert.Equal("NB", changes["State"]);
            Assert.Equal("NB", changes["ARRL Section"]);
            Assert.Equal("FN76rf", changes["Grid Square"]);
            Assert.Equal("5", changes["CQ Zone"]);
            Assert.Equal("9", changes["ITU Zone"]);
            Assert.False(changes.ContainsKey("QTH"));        // the home QTH stays
            Assert.False(changes.ContainsKey("US County"));  // no county in Canada
            Assert.Contains(plan.Warnings, w => w.Contains("not at home in Alaska"));
            Assert.DoesNotContain(plan.Warnings, w => w.Contains("check it's the same station"));
            Assert.Contains(plan.Warnings, w => w.Contains("US county Anchorage"));
        }

        [Theory]
        [InlineData("KL4RL/W2", "KL4RL")]
        [InlineData("VE3/KL4RL", "KL4RL")]
        [InlineData("K5JSG/P", "K5JSG")]
        [InlineData("W5ERX", "W5ERX")]
        public void HomeCall_IsTheLongestPart(string call, string home) =>
            Assert.Equal(home, CallsignLookupService.HomeCall(call));

        [Theory]
        [InlineData("KL4RL/VE9", "KL4RL", true)]
        [InlineData("VE9/KL4RL", "KL4RL", true)]
        [InlineData("K5JSG/P", "K5JSG", true)]
        [InlineData("W5ERX", "w5erx", true)]
        [InlineData("KL4RL/VE9", "VE9ABC", false)]
        [InlineData("N1ABC", "N1ABD", false)]
        public void SameStation_HomeCallOfAPortableCall(string call, string qrzCall, bool same) =>
            Assert.Equal(same, HrdQsoFiller.SameStation(call, qrzCall));

        // KL4RL (no /) at a New York park: HRD says Alaska from the prefix.
        // Changing the Country empties HRD's state picker, so the state and
        // county are set again even though they looked right before.
        [Fact]
        public void PotaQso_CountryChange_SetsStatePickerThenCounty()
        {
            var qso = Kl4rlQso();
            qso["edtCALL"] = "KL4RL";
            qso["memCOMMENT"] = "US-2068";
            qso["edtSTATE"] = "NY";
            qso["cbxLocationStateProvince"] = "NY";
            qso["edtCNTY"] = "Monroe";
            var plan = Plan(qso, Kl4rl());
            var labels = plan.Changes.Select(c => c.Label).ToList();
            var changes = Changes(plan);

            Assert.Equal("United States", changes["Country"]);
            Assert.False(changes.ContainsKey("State"));   // the box above already says NY
            Assert.Equal("NY", changes["State (Location tab)"]);
            Assert.Equal("Monroe", changes["US County"]);
            Assert.True(labels.IndexOf("State (Location tab)") < labels.IndexOf("US County"));
        }

        [Fact]
        public void PotaQso_ParkOutsideUsAndCanada_ClearsTheHomeState()
        {
            var qso = Kl4rlQso();
            qso["memCOMMENT"] = "DL-0001";
            var changes = Changes(Plan(qso, Kl4rl()));

            Assert.Equal("Fed. Republic of Germany", changes["Country"]);
            Assert.Equal("", changes["State"]);
            Assert.Equal("14", changes["CQ Zone"]);
        }

        [Fact]
        public void PotaQso_CanadianAtAParkInAnotherProvince_GetsThatProvince()
        {
            var record = new QrzCallsignRecord { Call = "VE3TST", Dxcc = 1, State = "ON", City = "Ottawa", Latitude = 45.42, Longitude = -75.69 };
            var qso = new Dictionary<string, string> { ["edtCALL"] = "VE3TST", ["edtSTATE"] = "ON", ["memCOMMENT"] = "CA-0792" };
            var changes = Changes(Plan(qso, record));

            Assert.Equal("NB", changes["State"]);
            Assert.Equal("NB", changes["ARRL Section"]);
            Assert.Equal("Ottawa", changes["QTH"]);
            Assert.Equal("Canada", changes["Country"]);   // HRD had nothing in Country
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
            qso["edtCALL"] = "AA5ZZ";
            Assert.Contains(Plan(qso, W5erx()).Warnings, w => w.Contains("not AA5ZZ"));
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
