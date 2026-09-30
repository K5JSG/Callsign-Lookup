# Callsign Lookup

A Windows desktop tool that looks up an amateur radio callsign on [QRZ.com](https://www.qrz.com/) and works out, from the station's location:

- **6-character Maidenhead grid square** (e.g. `EM12kv`)
- **CQ zone** and **ITU zone**
- **County** and **state** (US; for Canadian stations the province)
- **ARRL/RAC section**

Each result is in its own read-only box: copy one with Ctrl+C or right-click → Copy, or all of them with **Copy All**.

Built by Jeremy S. Gaynor, K5JSG.

## How it works

1. The callsign is looked up through QRZ's [XML data service](https://www.qrz.com/docs/xml/current_spec.html) with your QRZ login.
2. The station's location is the lat/long on the QRZ record. If the record has no lat/long (lat/long needs a QRZ **XML Logbook Data subscription**), the center of the record's grid square is used, and the status bar says the location is approximate.
3. Everything else is computed **offline** from that location rather than taken from QRZ's own (user-entered) county/zone fields:
   - **Grid square**: standard Maidenhead math.
   - **County**: US stations only, going by the DXCC entity number on the QRZ record: United States (291) plus the separate entities that still have Census counties: Alaska (6), Hawaii (110), Puerto Rico (202), US Virgin Islands (285), Guam (103), American Samoa (9), Swains Island (515) and Mariana Islands (166). Other US possessions (Guantanamo Bay, Navassa, Wake, etc.) get no county. Point-in-polygon against US Census county boundaries (`Data/counties.json`, shared with POTA Activator Park Activations). A US point outside every county outline (just offshore, a barrier island, an approximate grid-square center in a lake) gets the nearest county in the state on its QRZ record. Stations anywhere else, Canada included, never get a county.
   - **CQ/ITU zone**: point-in-polygon against zone boundaries (`Data/cqZones.json`, `Data/ituZones.json`), simplified from HB9HIL's MIT-licensed [hamradio-zones-geojson](https://github.com/HB9HIL/hamradio-zones-geojson), the dataset behind [zone-check.eu](https://zone-check.eu/).
   - **ARRL section**: `Data/arrlSections.json`, from the [ARRL section boundaries](https://www.arrl.org/section-boundaries). Most states are one section. CA, FL, MA, NJ, NY, PA, TX and WA are split by county, so the county decides it. Canadian sections go by the province on the QRZ record. Ontario's four sections (GH/ONE/ONN/ONS) follow census divisions per [RAC's 2023 table](https://www.va3cco.com/ontariosections2023.pdf), so the station's division is looked up in `Data/ontarioDivisions.json` (Statistics Canada 2021 census divisions). Nipissing District is split: inside or south of Algonquin Park is ONE, the rest is ONN. The park outline comes from OpenStreetMap.

Your QRZ password is stored encrypted with Windows DPAPI (readable only by your Windows account on that PC) in `%LocalAppData%\Callsign Lookup\settings.json`.

Map data: Ontario census divisions © Statistics Canada ([Open Government Licence – Canada](https://open.canada.ca/en/open-government-licence-canada)); Algonquin Park outline © [OpenStreetMap contributors](https://www.openstreetmap.org/copyright) (ODbL).

## Building from source

Requirements: [.NET 10 SDK](https://dotnet.microsoft.com/download) (Windows).

```powershell
dotnet build "Callsign Lookup.slnx"
dotnet test "Callsign Lookup.slnx"
```

To produce a self-contained, single-file release build in `publish\`:

```powershell
.\build.ps1 -Version <version>
```

## Project layout

| Path | What it is |
|------|------------|
| `MainForm.cs`, `QrzLoginForm.cs` | The WinForms UI |
| `Services/QrzService.cs` | QRZ XML client: login, session key reuse, re-login on timeout |
| `Services/CallsignLookupService.cs` | Callsign → location → grid/county/zones/section |
| `Services/Maidenhead.cs` | Grid square ↔ lat/long |
| `Services/CountyLookupService.cs` | Offline county lookup |
| `Services/ZoneLookupService.cs` | Offline CQ/ITU zone lookup |
| `Services/ArrlSectionService.cs` | ARRL/RAC section lookup |
| `Services/OntarioDivisionService.cs` | Offline Ontario census division lookup (for Ontario sections) |
| `Services/PolygonMath.cs` | Point-in-polygon and distance math shared by the lookups |
| `Data/` | Boundary and section data shipped next to the exe |
| `Tests/CallsignLookup.Tests` | xUnit tests (reference locations, QRZ response parsing) |
| `Tools/build_ontario_divisions.py` | Regenerates `Data/ontarioDivisions.json` (instructions inside) |
