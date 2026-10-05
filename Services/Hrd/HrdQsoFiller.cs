using System.Globalization;

namespace CallsignLookup.Services.Hrd
{
    // How a field is set in HRD's window.
    public enum HrdSetBy
    {
        Text,           // a text box: set its value
        DropDown,       // open the list, type Typed, Enter
        TypeInList,     // type Typed into the (closed) list, Enter - the IOTA ref
    }

    // One field to change. Tab is the HRD tab it's on ("" = the top half of
    // the window, which is always showing). ReadId and WriteId differ where
    // HRD shows a value in one box and takes it in another (the county).
    // Typed is what's typed into a drop-down; the entry it lands on starts
    // with it ("4766" -> "4766 - Key West").
    public sealed record HrdFieldChange(
        string Label, string Tab, string ReadId, string WriteId, HrdSetBy How,
        string Old, string New, string Typed = "")
    {
        public bool WasBlank => IsBlank(Old);

        internal static bool IsBlank(string value) => value.Length == 0 || value == "-none-";
    }

    public sealed class HrdFillPlan
    {
        public string Callsign { get; init; } = "";
        public List<HrdFieldChange> Changes { get; } = [];
        public List<string> Warnings { get; } = [];
        public HrdStationProfile? Profile { get; init; }
    }

    public sealed record HrdAppliedChange(HrdFieldChange Change, string Actual, bool Took);

    public sealed class HrdFillReport
    {
        public required HrdFillPlan Plan { get; init; }
        public List<HrdAppliedChange> Applied { get; } = [];
        // My Station fields the profile changed: (field, was, now).
        public List<(string Field, string Old, string New)> StationChanges { get; } = [];
        public bool ProfileSelected { get; set; }
        public string DistanceOld { get; set; } = "";
        public string DistanceNew { get; set; } = "";
        public List<string> Problems { get; } = [];
    }

    // The per-QSO cleanup the user does by hand in HRD Logbook, in their order:
    //   1. Check the contact details HRD's QRZ lookup filled in against QRZ.
    //   2. A park reference parked in the Comment goes into the POTA field
    //      (with the park's name and location) and comes out of the Comment.
    //   3. The QSO's location is the park's, if there is one, otherwise the
    //      QRZ record's; grid square follows.
    //   4. QSL Manager/VIA is cleared.
    //   5. My Station is re-picked (the profile the user is working from).
    //   6. IOTA ref and island, if the location is on one.
    //   7. CQ and ITU zones, and the ARRL section for US/Canada stations.
    //   8. Distance is cleared and recalculated (HRD's Recalc: grid square
    //      centre to grid square centre, from the My Station locator).
    // Only blank fields and ones that don't match are changed. Everything
    // that was worked out from a location is worked out the way the main
    // window does it - from the location, not from QRZ's own fields.
    public static class HrdQsoFiller
    {
        // Tabs read for the "before" picture, and in the order changes are
        // made: My Station before Location, because Recalc measures from the
        // My Station locator.
        private static readonly string[] TabsToRead = ["Location", "POTA", "QSL", "My Station", "IOTA"];

        // ---- reading --------------------------------------------------------

        // Every field on the tabs this uses, by id.
        public static Dictionary<string, string> ReadQso(HrdEditWindow window)
        {
            string startTab = window.SelectedTab();
            var values = new Dictionary<string, string>();
            foreach (string tab in TabsToRead)
            {
                window.SelectTab(tab);
                foreach (var (id, value) in window.ReadAll()) values.TryAdd(id, value);
            }
            if (startTab.Length > 0) window.SelectTab(startTab);
            return values;
        }

        // ---- planning (no HRD needed - testable) -----------------------------

        public static HrdFillPlan Plan(
            IReadOnlyDictionary<string, string> qso, LookupResult lookup,
            Func<string, PotaPark?> findPark, HrdStationProfile? profile)
        {
            var record = lookup.Qrz;
            string Get(string id) => qso.TryGetValue(id, out string? v) ? v : "";

            var plan = new HrdFillPlan { Callsign = Get("edtCALL"), Profile = profile };

            if (!plan.Callsign.Equals(record.Call, StringComparison.OrdinalIgnoreCase))
                plan.Warnings.Add($"QRZ's record is for {record.Call}, not {plan.Callsign} - check it's the same station.");

            void Change(string label, string tab, string readId, string writeId, HrdSetBy how, string newValue, string typed = "")
            {
                string old = Get(readId);
                if (how == HrdSetBy.Text ? SameText(old, newValue) : SameEntry(old, typed)) return;
                plan.Changes.Add(new HrdFieldChange(label, tab, readId, writeId, how, old, newValue, typed));
            }

            // 1. Contact details, against QRZ. A name that matches with or
            // without the nickname is fine.
            if (record.FullName.Length > 0 && !SameText(Get("edtNAME"), record.FullName))
                Change("Name", "", "edtNAME", "edtNAME", HrdSetBy.Text, record.NameWithNickname);
            if (record.City.Length > 0)
                Change("QTH", "", "edtQTH", "edtQTH", HrdSetBy.Text, record.City);
            // (State and county come after the park - for a POTA QSO they're the park's.)

            // 2. POTA. A Comment holding nothing but park references is where
            // the user parks them: they replace whatever is in the POTA field
            // (as a comma list, like HRD's "Multiple POTAs" - the first is
            // the park entered) and the Comment is cleared. A Comment with
            // anything else in it is a real comment - left alone, parks and all.
            string comment = Get("memCOMMENT");
            var (commentRefs, commentRest, notParks) = HrdPotaParks.TakeReferences(comment, r => findPark(r) != null);
            var refs = Get("edtPOTA_REF")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(r => r.ToUpperInvariant())
                .Distinct()
                .ToList();
            if (notParks.Count > 0)
            {
                plan.Warnings.Add($"The Comment has {string.Join(", ", notParks)}, which looks like a park but isn't in HRD's " +
                                  "park list - Comment and POTA left as they are.");
            }
            else if (commentRefs.Count > 0 && commentRest.Length > 0)
            {
                plan.Warnings.Add($"The Comment has other text as well as park {string.Join(", ", commentRefs)} " +
                                  $"(\"{comment}\") - Comment and POTA left as they are. Move the parks by hand.");
            }
            else if (commentRefs.Count > 0)
            {
                refs = commentRefs;
                Change("Comment", "", "memCOMMENT", "memCOMMENT", HrdSetBy.Text, "");
            }

            PotaPark? park = null;
            if (refs.Count > 0)
            {
                string refList = string.Join(",", refs);
                // Changing the POTA field makes HRD empty its POTA tab, so then
                // the whole tab is filled in again, as HRD's park picker does.
                bool refChanging = !SameText(Get("edtPOTA_REF"), refList);
                void PotaTab(string label, string id, string value, bool same)
                {
                    if (refChanging)
                        plan.Changes.Add(new HrdFieldChange(label, "POTA", id, id, HrdSetBy.Text, Get(id), value));
                    else if (!same)
                        Change(label, "POTA", id, id, HrdSetBy.Text, value);
                }

                Change("POTA Ref", "", "edtPOTA_REF", "edtPOTA_REF", HrdSetBy.Text, refList);
                park = findPark(refs[0]);
                if (park == null)
                {
                    plan.Warnings.Add($"Park {refs[0]} isn't in HRD's park list - the QSO keeps the station's QRZ location.");
                    PotaTab("Park Ref (POTA tab)", "edtPOTA_REF2", refList, SameText(Get("edtPOTA_REF2"), refList));
                }
                else
                {
                    // The first park is the one entered: its name and location
                    // go on the POTA tab even with several (the user's choice -
                    // HRD's own picker leaves them blank then).
                    PotaTab("Park Ref (POTA tab)", "edtPOTA_REF2", refList, SameText(Get("edtPOTA_REF2"), refList));
                    PotaTab("Park Name", "edtAPP_HAMRADIODELUXE_POTA_NAME", park.Name,
                        SameText(Get("edtAPP_HAMRADIODELUXE_POTA_NAME"), park.Name));
                    PotaTab("Park Latitude", "edtAPP_HAMRADIODELUXE_POTA_LAT", Coordinate(park.Latitude),
                        SameCoordinate(Get("edtAPP_HAMRADIODELUXE_POTA_LAT"), park.Latitude));
                    PotaTab("Park Longitude", "edtAPP_HAMRADIODELUXE_POTA_LON", Coordinate(park.Longitude),
                        SameCoordinate(Get("edtAPP_HAMRADIODELUXE_POTA_LON"), park.Longitude));
                    // HRD shows these two but doesn't keep them in the log -
                    // only filled in again after HRD has emptied them.
                    if (refChanging)
                    {
                        PotaTab("Park Location", "edtPOTACountry", park.LocationDesc, true);
                        PotaTab("Park Grid", "edtPOTAGrid", park.Grid, true);
                    }
                    if (refs.Count > 1)
                        plan.Warnings.Add($"{refs.Count} parks - the QSO and the POTA tab have the first one, {park.Reference} {park.Name}.");
                }
            }

            // 3. Location: the park's, or the QRZ record's.
            LookupResult located = lookup;
            if (park != null)
            {
                located = CallsignLookupService.ResolveAt(record, park.Latitude, park.Longitude, $"POTA park {park.Reference}");
            }
            else if (lookup.Latitude == null)
            {
                plan.Warnings.Add("QRZ has no location for this station - location, grid, zones, section and IOTA left as they are.");
            }
            else if (record.Latitude == null)
            {
                plan.Warnings.Add($"Location is approximate: {lookup.LocationSource}.");
            }

            // State and US county: where the QSO was - the park's for a POTA
            // QSO (from the park's location, so a park across a state line
            // gets that state), otherwise QRZ's.
            string state = "", county = "";
            if (park != null)
            {
                state = located.County?.StateAbbrev
                        ?? (park.LocationDesc.StartsWith("US-", StringComparison.OrdinalIgnoreCase) ||
                            park.LocationDesc.StartsWith("CA-", StringComparison.OrdinalIgnoreCase)
                            ? park.LocationDesc[3..] : "");
                county = located.County?.County ?? "";
            }
            else
            {
                if (record.IsUnitedStates || record.IsCanadian) state = record.State;
                if (record.IsUnitedStates) county = record.County.Length > 0 ? record.County : lookup.County?.County ?? "";
            }
            if (state.Length > 0)
                Change("State", "", "edtSTATE", "edtSTATE", HrdSetBy.Text, state.ToUpperInvariant());
            if (county.Length > 0)
                Change("US County", "Location", "edtCNTY", "cbxLocationCounty", HrdSetBy.DropDown, county, county);

            if (located.Latitude is double lat && located.Longitude is double lon)
            {
                if (!SameCoordinate(Get("edtLAT"), lat))
                    Change("Latitude", "Location", "edtLAT", "edtLAT", HrdSetBy.Text, Coordinate(lat));
                if (!SameCoordinate(Get("edtLON"), lon))
                    Change("Longitude", "Location", "edtLON", "edtLON", HrdSetBy.Text, Coordinate(lon));
                string grid = park != null && Maidenhead.TryGetCenter(park.Grid, out _, out _)
                    ? Maidenhead.Normalize(park.Grid)
                    : located.GridSquare;
                Change("Grid Square", "Location", "edtGRIDSQUARE", "edtGRIDSQUARE", HrdSetBy.Text, grid);

                // 7. Zones and section, from the same location.
                if (located.CqZone is int cq)
                    Change("CQ Zone", "Location", "edtCQZ", "edtCQZ", HrdSetBy.Text, cq.ToString(CultureInfo.InvariantCulture));
                if (located.ItuZone is int itu)
                    Change("ITU Zone", "Location", "edtITUZ", "edtITUZ", HrdSetBy.Text, itu.ToString(CultureInfo.InvariantCulture));
                if (located.ArrlSection is ArrlSectionMatch section)
                    Change("ARRL Section", "Location", "cbxARRLSECT", "cbxARRLSECT", HrdSetBy.DropDown, section.Section, section.Section);

                // 6. IOTA - only ever from the location.
                PlanIota(plan, located.Iota, Get("edtIOTARefNo"), lat, lon, Change);
            }

            // 4. QSL Manager/VIA.
            if (Get("edtQSL_VIA").Length > 0)
                Change("QSL Manager/VIA", "QSL", "edtQSL_VIA", "edtQSL_VIA", HrdSetBy.Text, "");

            return plan;
        }

        private delegate void ChangeFn(string label, string tab, string readId, string writeId, HrdSetBy how, string newValue, string typed = "");

        private static void PlanIota(HrdFillPlan plan, IotaMatch? iota, string oldRef, double lat, double lon, ChangeFn change)
        {
            bool hrdHasIota = !HrdFieldChange.IsBlank(oldRef);
            if (iota == null || iota.Island.Length == 0)
            {
                if (iota != null && !hrdHasIota)
                    plan.Warnings.Add($"QRZ's record says IOTA {iota.RefNo}, but the location isn't on a listed island - not filled in.");
                if (!hrdHasIota) return;

                // An IOTA group nowhere near the location is just wrong (AF-006
                // Diego Garcia for a station in New York) - take it out. One
                // whose area the location is in could be right: an island too
                // small for the island data, so only flag that.
                string oldRefNo = oldRef.Split(' ', 2)[0];
                if (IotaService.CouldBeIn(oldRefNo, lat, lon))
                    plan.Warnings.Add($"HRD has IOTA {oldRef}, but the location isn't on one of its listed islands - left as it is.");
                else
                    change("IOTA Ref", "IOTA", "edtIOTARefNo", "edtIOTARefNo", HrdSetBy.TypeInList, NoIota, NoIota);
                return;
            }

            if (iota.Note.Length > 0) plan.Warnings.Add($"IOTA: {iota.Note}.");
            change("IOTA Ref", "IOTA", "edtIOTARefNo", "edtIOTARefNo", HrdSetBy.TypeInList, iota.Reference, iota.RefNo);

            string? id = IotaService.IslandId(iota.RefNo, iota.Island);
            if (id == null)
                plan.Warnings.Add($"{iota.Island} isn't on IOTA's island list for {iota.RefNo}, so HRD can't record the island.");
            else
                change("IOTA Island", "IOTA", "edtIOTAIsland", "edtIOTAIsland", HrdSetBy.DropDown, $"{id} - {iota.Island}", id);
        }

        // HRD's IOTA list entry for "no IOTA".
        private const string NoIota = "-none-";

        // HRD keeps 4 decimal places in the log. Via decimal so 43.31545
        // (really 43.3154499... as a double) rounds up, as it reads.
        internal static string Coordinate(double value) =>
            Math.Round((decimal)value, 4, MidpointRounding.AwayFromZero).ToString("0.####", CultureInfo.InvariantCulture);

        // Within about 100 m counts as the same place: HRD's own POTA picker
        // rounds a park to 3 decimal places (29.849 for 29.8494), and that's
        // not worth changing - or asking the user to check.
        internal static bool SameCoordinate(string old, double value) =>
            double.TryParse(old, NumberStyles.Float, CultureInfo.InvariantCulture, out double o) &&
            Math.Abs(o - value) < 0.001;

        // Case and spacing don't count.
        internal static bool SameText(string a, string b) =>
            string.Join(' ', a.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Equals(string.Join(' ', b.Split(' ', StringSplitOptions.RemoveEmptyEntries)), StringComparison.OrdinalIgnoreCase);

        // A drop-down entry is "4766 - Key West" or "NA-062 - Florida State
        // (Florida Keys) group"; what's typed is the part before " - ".
        internal static bool SameEntry(string entry, string typed) =>
            entry.Equals(typed, StringComparison.OrdinalIgnoreCase) ||
            entry.StartsWith(typed + " ", StringComparison.OrdinalIgnoreCase);

        // ---- applying ---------------------------------------------------------

        public static HrdFillReport Apply(HrdEditWindow window, HrdFillPlan plan)
        {
            var report = new HrdFillReport { Plan = plan };

            // The top half of the window, then the tabs in workflow order.
            ApplyTab(window, plan, report, "");
            ApplyTab(window, plan, report, "POTA");

            if (plan.Profile is HrdStationProfile profile)
            {
                window.SelectTab("My Station");
                var before = StationFields(window);
                report.ProfileSelected = window.SelectProfile(profile.DisplayName);
                if (!report.ProfileSelected)
                {
                    report.Problems.Add($"Couldn't find the My Station profile \"{profile.DisplayName}\" in HRD.");
                }
                else
                {
                    var after = StationFields(window);
                    foreach (var (id, old) in before)
                        if (after.TryGetValue(id, out string? now) && !SameText(old, now) && !SameNumber(old, now))
                            report.StationChanges.Add((id.Replace("edtMY_", "My ").Replace("edt", ""), old, now));
                }
            }

            ApplyTab(window, plan, report, "Location");
            window.SelectTab("Location");
            report.DistanceOld = window.Read("edtDISTANCE") ?? "";
            window.ClearByKeyboard("edtDISTANCE");
            window.Press("btnLocationRecalc");
            report.DistanceNew = window.Read("edtDISTANCE") ?? "";
            if (report.DistanceNew.Length == 0) report.Problems.Add("HRD's Recalc didn't fill in the distance - press Recalc on the Location tab.");

            ApplyTab(window, plan, report, "QSL");
            ApplyTab(window, plan, report, "IOTA");

            // Back where the user can see the location and map.
            window.SelectTab("Location");
            return report;
        }

        private static void ApplyTab(HrdEditWindow window, HrdFillPlan plan, HrdFillReport report, string tab)
        {
            var changes = plan.Changes.Where(c => c.Tab == tab).ToList();
            if (changes.Count == 0) return;
            if (tab.Length > 0) window.SelectTab(tab);

            foreach (var change in changes)
            {
                try
                {
                    switch (change.How)
                    {
                        case HrdSetBy.Text:
                            window.SetText(change.WriteId, change.New);
                            break;
                        case HrdSetBy.DropDown:
                            window.ChooseInDropDown(change.WriteId, change.Typed);
                            break;
                        case HrdSetBy.TypeInList:
                            window.ChooseInDropDown(change.WriteId, change.Typed, openFirst: false);
                            break;
                    }
                    string actual = window.Read(change.ReadId) ?? "";
                    bool took = change.How == HrdSetBy.Text
                        ? SameText(actual, change.New) || SameNumber(actual, change.New)
                        : SameEntry(actual, change.Typed);
                    report.Applied.Add(new HrdAppliedChange(change, actual, took));
                    if (!took) report.Problems.Add($"{change.Label}: HRD didn't take \"{change.New}\" (it shows \"{actual}\") - set it by hand.");
                }
                catch (Exception ex) when (ex is HrdException or InvalidOperationException or System.Windows.Automation.ElementNotAvailableException)
                {
                    report.Applied.Add(new HrdAppliedChange(change, "", false));
                    report.Problems.Add($"{change.Label}: {ex.Message}");
                }
            }
        }

        private static Dictionary<string, string> StationFields(HrdEditWindow window) =>
            window.ReadAll()
                .Where(kv => kv.Key.StartsWith("edtMY_", StringComparison.Ordinal) ||
                             kv.Key is "edtOPERATOR" or "edtSTATION_CALLSIGN")
                .ToDictionary();

        // "43.31545" and "43.315450" are the same.
        private static bool SameNumber(string a, string b) =>
            double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out double x) &&
            double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out double y) &&
            Math.Abs(x - y) < 0.000001;
    }
}
