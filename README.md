<img src="logo-256.png" alt="Callsign Lookup logo" width="128" align="right" />

# Callsign Lookup

A Windows desktop tool that looks up an amateur radio callsign on [QRZ.com](https://www.qrz.com/) and works out, from the station's location:

- **6-character Maidenhead grid square** (e.g. `EM12kv`)
- **CQ zone** and **ITU zone**
- **County** and **state** (US; for Canadian stations the province)
- **ARRL/RAC section**
- **IOTA reference and island**, e.g. `AF-004 - Canary Islands` / `Alegranza`, or `EU-005 - Great Britain` / `Great Britain` for anywhere in England, Scotland or Wales

Each result is in its own read-only box: copy one with Ctrl+C or right-click → Copy, or all of them with **Copy All**. **Clear** empties the window. Tick **Stay on top** to keep the window above other programs (HRD Logbook, for instance); the choice is remembered.

The **Grid Square** box is the exception: if the grid on the QRZ record is wrong, paste or type the right one there, and every field is worked out again from the centre of that grid. Everything else, such as the DXCC entity and state, still comes from the QRZ record. A 6-character grid takes effect as soon as it's complete; press Enter for a 4-character one. Use 6 characters for islands, because the centre of a 4-character square is often out at sea.

If you log with HRD Logbook, it can also fill in the QSO you have open there, or a whole selection of QSOs at once: see [Filling in a QSO in HRD Logbook](#filling-in-a-qso-in-hrd-logbook) and [Filling many QSOs at once](#filling-many-qsos-at-once).

Built by Jeremy S. Gaynor, K5JSG.

## Filling in a QSO in HRD Logbook

If you log with [Ham Radio Deluxe](https://www.hamradiodeluxe.com/) Logbook, Callsign Lookup can do the per-QSO cleanup in HRD's log entry window for you.

1. Open the QSO in HRD (Edit, or a new one in Add). Within a couple of seconds Callsign Lookup shows "*CALL* open" in its **HRD Logbook** row, puts the callsign in its box and looks it up. Opening another QSO, or changing or deleting the callsign in HRD, does the same.
2. Do HRD's own Lookup as usual.
3. Pick where you're operating from under **Working from**. These are HRD's My Station profiles (Tools > Configure > My Station). The choice is remembered.
4. Click **Fill HRD QSO**, and leave HRD alone until the report appears. In order, it:
   - checks name, QTH, state and US county against QRZ (for a POTA QSO the state and county are the park's instead), and fills any that are blank or don't match (a name with HRD's quoted nickname, `WILLIAM "Bill" HAMALAINEN`, matches). A portable call (KL4RL/W2, VE3/KL4RL) is looked up as it is first; if QRZ has no page for it, the home call (the longest part, KL4RL) is looked up instead, and that counts as a match. If the call's prefix puts the station in another DXCC entity than that home (ZF/KN2P is in the Cayman Islands, not Colorado) and there's no park, QRZ's home location is no use. The location, grid, zones, state, county and section are then left as HRD has them (HRD sets them from the prefix), no location question is asked, and the report says so. The IOTA still comes from HRD's own position, and for an entity with a single IOTA group at sea or off the island data (Cayman) just the group is filled in and the island is left for you.
   - takes POTA park references out of the Comment (`US-3033`, `POTA: US-4579 & US-4566`) and puts them in the POTA field in place of whatever was there, with the park's name and location from HRD's own park list. Several parks go in as a comma list, like HRD's "Multiple POTAs"; the first park is the one entered, so the POTA tab and the QSO get its name and location. Only the park references come out, with a leading "POTA:" and the spaces (and any comma or dash joining them) around them; any other text in the Comment stays exactly as written (`Nice signal POTA: US-3033 73` becomes `Nice signal 73`). If something that looks like a park isn't in HRD's park list, the Comment and POTA field are left alone and the report says so;
   - for a POTA QSO, sets the Country to the park's DXCC entity (from HRD's park list), so a station working portable from another country (KL4RL/VE9, an Alaska call in a New Brunswick park) gets the park's country, state/province, zones and section rather than its QRZ home ones. The QTH stays the QRZ home one. A park outside the US and Canada clears the state; a US county or ARRL section still left in HRD after the fill is flagged in the report (HRD empties the county itself when the Country changes, so it usually isn't). HRD's country names (it calls 230 "Fed. Republic of Germany") come from `Data/hrdCountries.json`; for an entity not in it, the report asks you to check the Country by hand;
   - re-selects the My Station profile, but only if the QSO's My Station is already that station (same callsign and locator, and the position either matches the profile's or is just the locator's centre, which is all WSJT-X logs) or has none yet. A QSO that has a different station, such as an old Penfield QSO opened while working from Home, is [your choice](#when-the-qsos-my-station-isnt-the-selected-one): the report warns, and you pick to keep what's in HRD (the default) or use the selected station;
   - sets the QSO's location to the park (or, for no park, the QRZ location), and from that the grid square, CQ and ITU zones and ARRL section, worked out as in the main window. If QRZ has no usable location for the station (none, or a placeholder such as grid `AA00aa`, grid `JJ00aa` or lat/long 0,0, which special-event pages often carry), the address is used instead, see [Stations with no location on QRZ](#stations-with-no-location-on-qrz);
   - clears the distance and presses HRD's Recalc (HRD measures grid square centre to grid square centre, from your My Station locator);
   - clears QSL Manager/VIA, unless **Clear QSL via in HRD** (next to Stay on top, remembered) is unticked, in which case it's left alone. A Silent Key note in it (`SK March 28, 2025`, `SK2023`) is never cleared;
   - for a Silent Key (QRZ's record is `CALL/SK2023`, or `/SK` followed by anything), puts `SK 2023` in QSL Manager/VIA (ahead of any other note it keeps if Clear QSL via is off; an SK note already there is left) and ticks the Silent Key box on the Contact tab, whichever way Clear QSL via is set;
   - sets the IOTA reference and island if the location is on one.
5. The report opens in front of the main window (even with Stay on top ticked), half an inch above it. It lists every field that had a different value (to verify), every blank field it filled in, the My Station fields the profile changed, notes, and anything HRD wouldn't take. Then:
   - **Approve - Update in HRD** presses Update (F7) in HRD's window, saving the QSO;
   - **Close** leaves the QSO open in HRD, unsaved, to finish there. Cancel in HRD throws the changes away.

Only blank fields and ones that don't match are changed; a location within about 100 m of the right one is left alone. If QRZ's page for the callsign is for a different callsign (a changed call; a portable call's home call and a Silent Key's `/SK` suffix don't count), it asks before going on. An IOTA reference in HRD for a group nowhere near the location is cleared; one whose area the location is in (it could be a small island the island data hasn't got) is only flagged.

Callsign Lookup fills HRD's window through Windows UI Automation, the same interface screen readers use, so HRD needs no add-ons or settings. It only reads the description, callsign, locator, position and the My Station values a profile copies into a QSO (city, street, rig and so on), not the passwords stored beside them. Tested with HRD Logbook 6.9.

### Stations with no location on QRZ

A special-event page often has no real location: lat/long and grid are blank or a placeholder (W7I's is `AA00aa`, the South Pole; W6Z's is `JJ00aa`, open ocean off West Africa) and the ZIP may be wrong. Then, in the main window, Fill HRD QSO and the batch fill, the station's address is used to find an approximate location, best first:

1. **A street address**: that point.
2. **A PO box, or just a ZIP**: a post office in that mail area. The ZIP is first checked against the state (an Idaho ZIP on a Washington address is ignored, and the report says so), and the post office must be in the same city and share the ZIP's first three digits, because a PO-box-only ZIP is housed in a post office whose own address has a different ZIP.
3. **A city and state**: the middle of the city.

The address is the one in HRD's **Address** box on the Location tab, which you can correct (from the certificate address on the QRZ page, say); if that has nothing, QRZ's own city, state and ZIP. The report (and the main window's status line) always says which of the three was used and that the location is approximate. County, zones and ARRL section follow the new place, not QRZ's. The lookups go to [OpenStreetMap's Nominatim](https://nominatim.org/release-docs/latest/api/Search/) service, so the address text is sent there; the app identifies itself, makes at most one request a second, and remembers answers while it's running. QRZ's website, which has the page's own certificate address, isn't read: its terms don't allow automated access, and its XML service doesn't carry it.

### When QRZ's location and its state disagree

Sometimes QRZ has a real location that is in another state than the one its own record lists. The Bigfoot special-event stations are the example: W7F's record says Tacoma, WA, but its lat/long is in Maricopa County, AZ. One of the two is wrong for your QSO, so the report warns (*"QRZ's location (DM33up - Maricopa County, AZ) is in another state than the one its record lists (WA) - check which is right"*) and you choose:

- **Fill HRD QSO** asks before it touches HRD: **Use QRZ's location**, **Use the address's location** (found as above from the address in HRD's Address box), or Cancel.
- **Fill selected...** gives each such QSO a **Location** column (`QRZ` or `address`) and two tick boxes in its detail pane, one of which is always ticked. **Address location for all** and **QRZ location for all** set every such QSO at once. A QSO with nothing left to write once you've chosen is unticked.

QRZ's location is the default. US stations only.

### When the QSO's My Station isn't the selected one

A QSO that already has a My Station different from the profile picked under **Working from** (an old Penfield QSO while Home is selected, say) isn't silently changed or silently skipped: the report warns (*"This QSO's My Station (Penfield FN13gd) doesn't match the selected "Home - K5JSG""*) and you choose.

- **Fill HRD QSO** asks before it touches HRD: **Keep what's in HRD** or **Use the selected station**, or Cancel.
- **Fill selected...** gives each such QSO a **Station** column (`HRD's` or `selected`) and two tick boxes in its detail pane, one always ticked. **Selected station for all** and **HRD's station for all** set every such QSO at once. This works alongside the location choice above.

Keeping what's in HRD is the default. A QSO whose position is only the locator's centre (as WSJT-X logs it) counts as the same station, so it just gets the exact position.

## Filling many QSOs at once

**Fill selected...** (bottom row of the main window) does the same cleanup on QSOs you have selected in HRD's log:

1. In HRD Logbook, select the QSOs in the log and press **Ctrl+C**, then click **Fill selected...**.
2. A warning says this changes HRD's log file directly, and asks you to make sure no Add/Edit window is open (one saved later would undo the changes). If one is open, it offers to close it without saving.
3. Each callsign is looked up on QRZ and the same rules as above are applied (including **Working from** and **Clear QSL via in HRD**). The preview lists every QSO with what would change; click one to see old and new values field by field. QSOs with nothing to change, or where QRZ's record is for another callsign, start unticked or greyed. Where QRZ's location and its own state disagree, you [choose which location is used](#when-qrzs-location-and-its-state-disagree), and where a QSO's My Station isn't the selected one, [which station](#when-the-qsos-my-station-isnt-the-selected-one).
4. **Apply** writes the ticked QSOs. First it checks nothing has changed in HRD since the preview, makes a backup of the log beside it (`<log> backup <date time> before Callsign Lookup batch fill.hrdsql`; the last 10 of these are kept), writes everything in one transaction (all or nothing), reads it back, and asks HRD to reload through its IP Client port. A summary shows what changed and can be copied.
5. **Undo this batch** puts every changed field back as it was (a backup is made first), unless a QSO has been changed since.

This writes into HRD's SQLite log directly, because HRD's log list can't be driven through UI Automation. It uses the same planning code as the single fill, so the results match what HRD's own Update saves.

## How it works

1. The callsign is looked up through QRZ's [XML data service](https://www.qrz.com/docs/xml/current_spec.html) with your QRZ login.
2. The station's location is the lat/long on the QRZ record. If the record has no lat/long (lat/long needs a QRZ **XML Logbook Data subscription**), the center of the record's grid square is used, and the status bar says the location is approximate. A placeholder (lat/long 0,0, the `JJ00` grid square around it, or the `AA00` grid square at the South Pole) counts as no location, and the station's address is used instead, see [Stations with no location on QRZ](#stations-with-no-location-on-qrz).
3. Everything else is computed **offline** from that location rather than taken from QRZ's own (user-entered) county/zone fields:
   - **Grid square**: standard Maidenhead math.
   - **County**: US stations only, going by the DXCC entity number on the QRZ record: United States (291) plus the separate entities that still have Census counties: Alaska (6), Hawaii (110), Puerto Rico (202), US Virgin Islands (285), Guam (103), American Samoa (9), Swains Island (515) and Mariana Islands (166). Other US possessions (Guantanamo Bay, Navassa, Wake, etc.) get no county. Point-in-polygon against US Census county boundaries (`Data/counties.json`, shared with POTA Activator Park Activations). A US point outside every county outline (just offshore, a barrier island, an approximate grid-square center in a lake) gets the nearest county in the state on its QRZ record. Stations anywhere else, Canada included, never get a county.
   - **CQ/ITU zone**: point-in-polygon against zone boundaries (`Data/cqZones.json`, `Data/ituZones.json`), simplified from HB9HIL's MIT-licensed [hamradio-zones-geojson](https://github.com/HB9HIL/hamradio-zones-geojson), the dataset behind [zone-check.eu](https://zone-check.eu/).
   - **ARRL section**: `Data/arrlSections.json`, from the [ARRL section boundaries](https://www.arrl.org/section-boundaries). Most states are one section. CA, FL, MA, NJ, NY, PA, TX and WA are split by county, so the county decides it. Canadian sections go by the province on the QRZ record. Ontario's four sections (GH/ONE/ONN/ONS) follow census divisions per [RAC's 2023 table](https://www.va3cco.com/ontariosections2023.pdf), so the station's division is looked up in `Data/ontarioDivisions.json` (Statistics Canada 2021 census divisions). Nipissing District is split: inside or south of Algonquin Park is ONE, the rest is ONN. The park outline comes from OpenStreetMap.
   - **IOTA**: the IOTA directory gives each group's DXCC entities, a rough bounding box and the names of the islands that count for it, but not where those islands are. So `Data/iotaIslands.json` holds the outline of every listed island, taken from OpenStreetMap and matched to the IOTA list by name. The station is on whichever island outline contains its location (or is within about 1 km of it), for a group whose box and DXCC entity fit, so the Island field is the island's name as IOTA lists it. IOTA often lists only a group's main islands, so the file also has the named islands within about 5 km of a listed one (e.g. Aunuʻu, off Tutuila, for OC-045). A station on one of those gets the group and the island's own name, with a note to check that it counts. Islands inside a listed island, such as those in Scottish lochs, are left out: they can't count for IOTA. A station that isn't on a listed island (the mainland, or an approximate location out at sea) gets the IOTA reference from its QRZ record if it has one, and the status bar says so. If the station is on an island but its QRZ record gives a different IOTA reference (say, an address and IOTA on Wallis but a grid square on Futuna), the location still decides, and the status bar warns that the two disagree. The IOTA list itself is downloaded from [iota-world.org](https://www.iota-world.org/) into `%LocalAppData%\Callsign Lookup\iota.json` and refreshed weekly, so new groups show up without an app update. Until the first download (or if it fails) the copy shipped in `Data/iota.json` is used. New islands in a downloaded list have no outline until the island data is rebuilt, so when IOTA has added any, a warning bar at the top of the window lists them and says the island data needs updating. `python Tools/build_iota_islands.py --update <cache dir>` does that in a few minutes: it rebuilds only the groups IOTA changed.

Your QRZ password is stored encrypted with Windows DPAPI (readable only by your Windows account on that PC) in `%LocalAppData%\Callsign Lookup\settings.json`.

Address search: [OpenStreetMap](https://www.openstreetmap.org/copyright) data via Nominatim (ODbL).

Map data: Ontario census divisions © Statistics Canada ([Open Government Licence – Canada](https://open.canada.ca/en/open-government-licence-canada)); Algonquin Park and island outlines © [OpenStreetMap contributors](https://www.openstreetmap.org/copyright) (ODbL). IOTA directory from [Islands On The Air](https://www.iota-world.org/).

## Installation

Download the latest installer from the [Releases](https://github.com/K5JSG/Callsign-Lookup/releases) page and run it. The app is self-contained: no separate .NET runtime install is required. Installing a new version upgrades the existing copy in place: Installed apps keeps a single entry that shows the new version, the desktop and Start menu shortcuts stay where they are, and files the new version no longer uses are removed. Any other copy of the app installed separately (e.g. an older per-user install) is uninstalled. Your saved QRZ login is kept.

You'll need a [QRZ.com](https://www.qrz.com/) account. The first time the app runs it asks for your QRZ username and password.

## Building from source

Requirements: [.NET 10 SDK](https://dotnet.microsoft.com/download) (Windows) and, optionally, [Inno Setup](https://jrsoftware.org/isdl.php) to build the installer.

```powershell
dotnet build "Callsign Lookup.slnx"
dotnet test "Callsign Lookup.slnx"
```

To run the tests and produce a self-contained release build and installer:

```powershell
.\build.ps1 -Version <version>
```

This publishes a self-contained, single-file executable plus its `Data` folder to `publish\` and, if Inno Setup is installed, builds the installer into `dist\`.

## Project layout

| Path | What it is |
|------|------------|
| `MainForm.cs`, `QrzLoginForm.cs`, `HrdFillReportForm.cs`, `BatchFillForm.cs` | The WinForms UI (the last is the batch preview and summary) |
| `Services/Hrd/HrdQsoFiller.cs` | Works out what an HRD QSO needs and fills it in, in the order above |
| `Services/Hrd/HrdEditWindow.cs` | Reads and fills HRD Logbook's log entry window through UI Automation |
| `Services/Hrd/HrdPotaParks.cs`, `HrdStationProfiles.cs` | HRD's POTA park list and My Station profiles |
| `Services/Hrd/HrdCountries.cs` | HRD's name for each DXCC entity, for its Country box |
| `Services/Hrd/HrdLogDatabase.cs`, `HrdRowMapper.cs`, `HrdBatchFiller.cs`, `HrdClipboard.cs` | The batch fill: HRD's SQLite log (read, backup, write, refresh), turning a log row into the planner's input and its changes back into columns, the preview/apply/undo, and reading the Ctrl+C selection |
| `Services/GeocodingService.cs`, `AddressLocator.cs` | Nominatim address search, and working out a location from an address for stations QRZ has none for |
| `AppLogo.cs`, `logo.ico`, `logo-256.png` | Program icon and window logo, built into the exe (`Logo.png` is the full-size source artwork) |
| `Services/QrzService.cs` | QRZ XML client: login, session key reuse, re-login on timeout |
| `Services/CallsignLookupService.cs` | Callsign → location → grid/county/zones/section |
| `Services/Maidenhead.cs` | Grid square ↔ lat/long |
| `Services/CountyLookupService.cs` | Offline county lookup |
| `Services/ZoneLookupService.cs` | Offline CQ/ITU zone lookup |
| `Services/ArrlSectionService.cs` | ARRL/RAC section lookup |
| `Services/IotaService.cs` | Offline IOTA reference/island lookup, and the weekly IOTA list download |
| `Services/OntarioDivisionService.cs` | Offline Ontario census division lookup (for Ontario sections) |
| `Services/PolygonMath.cs` | Point-in-polygon and distance math shared by the lookups |
| `Data/` | Boundary and section data shipped next to the exe |
| `Tests/CallsignLookup.Tests` | xUnit tests (reference locations, QRZ response parsing, HRD fill planning) |
| `Installer/InnoSetup/` | Inno Setup installer script (built by `build.ps1`) |
| `Tools/build_ontario_divisions.py` | Regenerates `Data/ontarioDivisions.json` (instructions inside) |
| `Tools/build_hrd_countries.py` | Regenerates `Data/hrdCountries.json` from an HRD log (HRD keeps its country list inside its program file) |
| `Tools/build_iota_islands.py` | Regenerates `Data/iotaIslands.json` from OpenStreetMap: a full build, or `--update` for just the groups IOTA has changed (instructions inside) |

## License

GNU General Public License v3.0; see [License.txt](License.txt).
