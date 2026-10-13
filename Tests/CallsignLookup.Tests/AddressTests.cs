using CallsignLookup.Services;

namespace CallsignLookup.Tests
{
    public class AddressLocatorTests
    {
        // W7I as QRZ has it: Tacoma, WA with an Idaho ZIP and the AA00aa placeholder.
        private static QrzCallsignRecord W7i() => new()
        {
            Call = "W7I", FirstName = "Bigfoot", LastName = "Special Event Station", City = "Tacoma", State = "WA",
            Zip = "83669", Country = "United States", Dxcc = 291, Grid = "AA00aa", Latitude = -89.979167, Longitude = -179.958333,
            County = "Ada",
        };

        private sealed class FakeGeocoder(Func<string, GeocodeMatch?> answer) : IGeocoder
        {
            public List<string> Queries { get; } = [];

            public Task<GeocodeMatch?> FindAsync(string query, string countryCodes, CancellationToken cancellationToken = default)
            {
                Queries.Add(query);
                return Task.FromResult(answer(query));
            }
        }

        private static readonly GeocodeMatch TacomaCentre = new(47.2531, -122.4443, "Tacoma, Pierce County, Washington, United States", "");

        [Fact]
        public void Parse_HrdsAddressBox_DropsTheNameAndCountryLines_AndTheWrongZip()
        {
            const string text = "Bigfoot Special Event Station, W7I\nTacoma, WA 83669\nUnited States";
            var address = AddressLocator.Parse(text, W7i());

            Assert.Equal("Tacoma", address.City);
            Assert.Equal("WA", address.State);
            Assert.Equal("", address.Zip);                       // an Idaho ZIP on a Washington address
            Assert.Contains("ID ZIP", address.Note);
            Assert.Equal("", address.Street);
            Assert.False(address.PoBox);
        }

        [Fact]
        public void Parse_PoBoxAndRightZip_KeepsTheZip()
        {
            var address = AddressLocator.Parse("Radio Club of Tacoma\nPO Box 11188\nTacoma, WA 98411\nUnited States", W7i());

            Assert.True(address.PoBox);
            Assert.Equal("98411", address.Zip);
            Assert.Equal("Tacoma", address.City);
            Assert.Equal("", address.Note);
            Assert.Equal("", address.Street);
        }

        [Fact]
        public void Parse_StreetAddress()
        {
            var address = AddressLocator.Parse("Jane Doe, W7I\n123 Main Street\nTacoma, WA 98402\nUnited States", W7i());

            Assert.Equal("123 Main Street", address.Street);
            Assert.Equal("98402", address.Zip);
        }

        [Fact]
        public void Parse_NothingTyped_UsesQrzsCityAndState_WithItsZipChecked()
        {
            var address = AddressLocator.Parse("", W7i());

            Assert.Equal("Tacoma", address.City);
            Assert.Equal("WA", address.State);
            Assert.Equal("", address.Zip);
        }

        [Theory]
        [InlineData("98411", "WA")]
        [InlineData("83669", "ID")]
        [InlineData("14464", "NY")]
        [InlineData("78666", "TX")]
        [InlineData("73301", "TX")]          // Austin - 733 is the IRS ZIP in Texas
        [InlineData("733", null)]          // not a ZIP
        [InlineData("ABCDE", null)]
        public void ZipStates_FindsTheState(string zip, string? state) => Assert.Equal(state, ZipStates.StateFor(zip));

        [Fact]
        public async Task Locate_StreetAddress_GivesThatPoint()
        {
            var geocoder = new FakeGeocoder(q => q.StartsWith("123 Main") ? new GeocodeMatch(47.25, -122.44, "123 Main", "98402") : null);
            var own = CallsignLookupService.Resolve(W7i());
            var address = AddressLocator.Parse("Jane, W7I\n123 Main Street\nTacoma, WA 98402", W7i());

            var result = await AddressLocator.LocateAsync(address, own, geocoder);

            Assert.Equal(47.25, result!.Latitude);
            Assert.Contains("street address", result.LocationSource);
            Assert.Contains("approximate", result.LocationSource);
            Assert.False(result.QrzHadLocation);
            Assert.Equal("W7I", result.Qrz.Call);
            Assert.Equal("Pierce", result.County?.County);
            Assert.Equal("WA", result.Qrz.State);
        }

        [Fact]
        public async Task Locate_PoBox_UsesThePostOfficeInThatMailArea_NotOneInAnotherCityOrArea()
        {
            var own = CallsignLookupService.Resolve(W7i());
            var address = AddressLocator.Parse("Club\nPO Box 11188\nTacoma, WA 98411", W7i());

            // What OpenStreetMap really has for "post office 98411": the Evergreen post office,
            // tagged 98409 (the PO boxes of 98411 are inside it).
            var evergreen = new GeocodeMatch(47.2201724, -122.4726252,
                "US Post Office - Evergreen, 4001, South Pine Street, South Tacoma, Tacoma, Washington, 98409", "98409", "Tacoma");
            var result = await AddressLocator.LocateAsync(address, own,
                new FakeGeocoder(q => q.StartsWith("post office") ? evergreen : TacomaCentre));
            Assert.Equal(47.2201724, result!.Latitude);
            Assert.Contains("a post office near ZIP 98411", result.LocationSource);
            Assert.Contains("Evergreen", result.LocationSource);

            // Another city, or another mail area: not that ZIP's - the city is used.
            foreach (var other in new[]
                     {
                         evergreen with { City = "Seattle" },
                         evergreen with { Postcode = "98101" },
                     })
            {
                result = await AddressLocator.LocateAsync(address, own,
                    new FakeGeocoder(q => q.StartsWith("post office") ? other : TacomaCentre));
                Assert.Contains("middle of Tacoma, WA", result!.LocationSource);
            }
        }

        [Fact]
        public async Task Locate_WrongZipIsIgnored_AndSaidSo_AndCityIsUsed()
        {
            var own = CallsignLookupService.Resolve(W7i());
            var geocoder = new FakeGeocoder(_ => TacomaCentre);
            var address = AddressLocator.Parse("Bigfoot Special Event Station, W7I\nTacoma, WA 83669", W7i());

            var result = await AddressLocator.LocateAsync(address, own, geocoder);

            Assert.DoesNotContain(geocoder.Queries, q => q.Contains("83669"));
            Assert.Contains("middle of Tacoma, WA", result!.LocationSource);
            Assert.Contains("ID ZIP", result.LocationSource);
            Assert.Equal("Pierce", result.County?.County);      // not QRZ's Ada
            Assert.Equal("CN87", result.GridSquare[..4]);
        }

        [Fact]
        public async Task FillWarnsTheLocationIsApproximate_WhenItCameFromTheAddress_EvenWithAPlaceholderInQrz()
        {
            var own = CallsignLookupService.Resolve(W7i());                 // QRZ's lat/long is the AA00aa placeholder
            var located = await AddressLocator.LocateAsync(
                AddressLocator.Parse("", W7i()), own, new FakeGeocoder(_ => TacomaCentre));

            var qso = new Dictionary<string, string>
                { ["edtCALL"] = "W7I", ["edtLAT"] = "-89.9792", ["edtLON"] = "-179.958", ["edtGRIDSQUARE"] = "EM87" };
            var plan = CallsignLookup.Services.Hrd.HrdQsoFiller.Plan(qso, located!, _ => null, null);

            Assert.Contains(plan.Warnings, w => w.Contains("Location is approximate") && w.Contains("middle of Tacoma, WA"));
        }

        // W7F: QRZ's lat/long is in Maricopa County, AZ; the record says Tacoma, WA.
        private static QrzCallsignRecord W7f() => new()
        {
            Call = "W7F", City = "Tacoma", State = "WA", Zip = "98411", Country = "United States", Dxcc = 291,
            Latitude = 33.645, Longitude = -112.293333,
        };

        [Fact]
        public void LocationDisagreesWithState_WhenQrzPutsTheStationInAnotherState()
        {
            Assert.True(AddressLocator.LocationDisagreesWithState(CallsignLookupService.Resolve(W7f())));

            // Consistent: WA coordinates, WA record.
            var consistent = CallsignLookupService.Resolve(new QrzCallsignRecord
                { Call = "W7X", State = "WA", Latitude = 47.25, Longitude = -122.44, Dxcc = 291 });
            Assert.False(AddressLocator.LocationDisagreesWithState(consistent));

            // No QRZ location at all (W7I): that's the other case, not a disagreement.
            Assert.False(AddressLocator.LocationDisagreesWithState(CallsignLookupService.Resolve(W7i())));
        }

        [Fact]
        public async Task FindAlternative_GivesTheAddressesLocation_OnlyWhenTheyDisagree()
        {
            var geocoder = new FakeGeocoder(_ => TacomaCentre);
            var w7f = CallsignLookupService.Resolve(W7f());

            var other = await AddressLocator.FindAlternativeAsync(w7f, "Club\nPO Box 11188\nTacoma, WA 98411", geocoder);

            Assert.Equal(47.2531, other!.Latitude);
            Assert.Equal("Pierce", other.County?.County);
            Assert.Contains("chosen over QRZ's own location", other.LocationSource);
            Assert.NotEqual(w7f.GridSquare, other.GridSquare);

            var consistent = CallsignLookupService.Resolve(new QrzCallsignRecord
                { Call = "W7X", State = "WA", Latitude = 47.25, Longitude = -122.44, Dxcc = 291 });
            Assert.Null(await AddressLocator.FindAlternativeAsync(consistent, "Tacoma, WA", geocoder));
            Assert.Null(await AddressLocator.FindAlternativeAsync(w7f, "Tacoma, WA", null));
        }

        [Fact]
        public void FillWarnsWhenQrzsLocationIsInAnotherState()
        {
            var qso = new Dictionary<string, string> { ["edtCALL"] = "W7F" };
            var plan = CallsignLookup.Services.Hrd.HrdQsoFiller.Plan(qso, CallsignLookupService.Resolve(W7f()), _ => null, null);

            Assert.Contains(plan.Warnings, w => w.Contains("in another state") && w.Contains("WA") && w.Contains("Maricopa"));
        }

        [Fact]
        public void BatchItem_ActiveChanges_FollowTheChoice()
        {
            var qrz = new CallsignLookup.Services.Hrd.HrdRowChanges();
            qrz.Changes.Add(new CallsignLookup.Services.Hrd.HrdColumnChange("Grid", "COL_GRIDSQUARE", "x", "DM33up"));
            var address = new CallsignLookup.Services.Hrd.HrdRowChanges();
            address.Changes.Add(new CallsignLookup.Services.Hrd.HrdColumnChange("Grid", "COL_GRIDSQUARE", "x", "CN87sf"));
            var item = new CallsignLookup.Services.Hrd.HrdBatchItem { Pk = 1, Call = "W7F", Changes = qrz, AlternativeChanges = address };

            Assert.Same(qrz, item.Active);
            item.UseAlternative = true;
            Assert.Same(address, item.Active);

            // With no alternative the choice has no effect.
            var plain = new CallsignLookup.Services.Hrd.HrdBatchItem { Pk = 2, Changes = qrz, UseAlternative = true };
            Assert.Same(qrz, plain.Active);
        }

        [Fact]
        public async Task UseAddress_LeavesAResultAloneThatQrzHadALocationFor()
        {
            var good = CallsignLookupService.Resolve(new QrzCallsignRecord
                { Call = "W5ERX", Latitude = 30.0, Longitude = -97.8, State = "TX", Dxcc = 291 });
            var geocoder = new FakeGeocoder(_ => TacomaCentre);

            Assert.Same(good, await AddressLocator.UseAddressAsync(good, "Tacoma, WA", geocoder));
            Assert.Empty(geocoder.Queries);
        }

        [Fact]
        public async Task UseAddress_NothingFound_ReturnsTheOriginal()
        {
            var own = CallsignLookupService.Resolve(W7i());

            Assert.Same(own, await AddressLocator.UseAddressAsync(own, "Tacoma, WA", new FakeGeocoder(_ => null)));
            Assert.Same(own, await AddressLocator.UseAddressAsync(own, "Tacoma, WA", null));
        }
    }

    public class NominatimParseTests
    {
        [Fact]
        public void ParseFirst_ReadsLatLonAndPostcode()
        {
            const string json = "[{\"lat\":\"47.2201724\",\"lon\":\"-122.4726252\",\"display_name\":\"US Post Office - Evergreen, Tacoma\"," +
                                "\"address\":{\"postcode\":\"98409\",\"city\":\"Tacoma\"}}]";
            var match = NominatimGeocoder.ParseFirst(json);

            Assert.Equal(47.2201724, match!.Latitude, 6);
            Assert.Equal(-122.4726252, match.Longitude, 6);
            Assert.Equal("98409", match.Postcode);
            Assert.StartsWith("US Post Office", match.DisplayName);
        }

        [Theory]
        [InlineData("[]")]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("{\"error\":\"x\"}")]
        [InlineData("[{\"lat\":\"x\",\"lon\":\"y\"}]")]
        public void ParseFirst_NullForNoResult(string json) => Assert.Null(NominatimGeocoder.ParseFirst(json));
    }
}
