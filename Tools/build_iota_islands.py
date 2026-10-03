"""Builds Data/iotaIslands.json - the outline of every island on an IOTA
group's island list, from OpenStreetMap, so the app can tell offline which
island (and so which IOTA group) a station is on. Not part of the app build;
re-run by hand when the IOTA list gains islands, or to pick up OSM fixes.

The IOTA directory has island names but no locations, so:
  1. Overpass lists every named place=island/islet in the world (tags and
     a bounding box only - one query per 30 x 30 degree tile that has an
     IOTA group in it).
  2. For each IOTA group, the OSM names (name, name:en, alt_name, ...) of
     the islands centred inside its bounding box are matched against the
     group's island list, ignoring accents, case and words like "Island".
  3. The outlines of the matched islands are downloaded (from QLever's
     OSM endpoint, which serves finished outlines far faster than
     Overpass) and simplified.
  4. Named islands that aren't on a group's list but lie within ~5 km of
     one that is (Aunu'u, 1.5 km off Tutuila, for OC-045) are added too,
     flagged "listed": false - IOTA often names only a group's main
     islands. Each goes with the group of the nearest listed island. Left
     out: ones inside a listed island (islands in Scottish lochs), which
     can't count for IOTA, and rocks too small to live on.

Overpass responses are cached in <cache dir>, so an interrupted run (the
public Overpass servers are often busy) picks up where it left off.

A full build takes hours (most of it listing the world's islands on
Overpass). When IOTA has only added a few islands - the app's yellow
"IOTA has added N new islands" bar - use --update instead: it compares
the current IOTA list with Data/iota.json (the list Data/iotaIslands.json
was built from), rebuilds only the groups whose islands or box changed,
searching just those groups' boxes (on QLever, so it doesn't need the
often-overloaded Overpass servers at all), and writes both files back into
Data/. Minutes, not hours. Then build and release a new version as usual.
Do a full build now and then anyway, to pick up OSM coastline fixes.

Input (download first): the IOTA full list from
  https://www.iota-world.org/islands-on-the-air/downloads/download-file.html?path=fulllist.json

Requires: pip install shapely

Usage:
  python build_iota_islands.py <fulllist.json> <cache dir> <out.json>
  python build_iota_islands.py --update <cache dir> [<fulllist.json>] [--data <Data dir>]
    (--update downloads the current list when no fulllist.json is given;
     --data defaults to the repo's Data folder)
"""

import json
import os
import re
import socket
import sys
import time
import unicodedata
import urllib.error
import urllib.parse
import urllib.request

from shapely import wkt
from shapely import STRtree
from shapely.geometry import LineString, MultiPolygon, Point, Polygon

IOTA_LIST_URL = ("https://www.iota-world.org/islands-on-the-air/downloads/download-file.html"
                 "?path=fulllist.json")
DATA_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), os.pardir, "Data")

# The main public server. When busy it answers 504 quickly, so retrying it
# beats waiting out a mirror that may not answer at all.
SERVER = "https://overpass-api.de/api/interpreter"
QLEVER = "https://qlever.dev/api/osm-planet"
USER_AGENT = "CallsignLookup-build/1.0 (+https://github.com/K5JSG/Callsign-Lookup)"

# Outline detail: ~50 m for most islands - plenty to tell them apart -
SIMPLIFY_DEGREES = 0.0005
# ...up to ~300 m for the giants (Greenland, Baffin, Great Britain), whose
# full coastlines would otherwise be most of the file. The app treats a
# location within ~1 km of an island as on it, which covers the difference.
MAX_SIMPLIFY_DEGREES = 0.003


def simplify(shape, min_tolerance=SIMPLIFY_DEGREES):
    min_x, min_y, max_x, max_y = shape.bounds
    size = ((max_x - min_x) * (max_y - min_y)) ** 0.5
    tolerance = min(MAX_SIMPLIFY_DEGREES, max(min_tolerance, size * 0.0003))
    return shape.simplify(tolerance, preserve_topology=True)


# How far (degrees, ~5 km) an unlisted island can be from a listed one in
# the same group and still be included...
NEAR_DEGREES = 0.05
# ...and how big it must be (square degrees, ~0.05 km2 or 220 x 220 m) - the
# tens of thousands of named rocks along Scandinavian and Scottish coasts
# would otherwise be most of the file, with no stations on them.
MIN_UNLISTED_AREA = 4e-6

# IOTA's boxes are only to the nearest few minutes of arc.
BOX_MARGIN = 0.1

NOISE_WORDS = {
    "island", "islands", "isle", "isles", "islet", "islets",
    "isla", "islas", "islote", "ile", "iles", "ilha", "ilhas", "ilheu",
    "isola", "isole", "insel", "eiland", "ostrov", "of", "the", "de", "del",
    "da", "do", "des", "du", "d", "l",
}
SAINT = {"saint", "sankt", "sainte", "ste"}


def name_key(name):
    """'Isla de Montaña Clara' -> 'montana clara', 'Saint Paul' -> 'st paul'."""
    name = name.split("(")[0]
    chars = []
    for c in unicodedata.normalize("NFD", name):
        if unicodedata.category(c) == "Mn":
            continue
        chars.append(c.lower() if c.isalnum() else " ")
    words = ["st" if w in SAINT else w for w in "".join(chars).split()]
    return " ".join(w for w in words if w not in NOISE_WORDS)


def is_name_tag(key):
    return (key in ("name", "alt_name", "official_name", "short_name", "old_name", "loc_name", "int_name")
            or key.startswith(("name:", "alt_name:", "official_name:")))


def osm_name_keys(tags, group_words=frozenset()):
    """Every normalised name the island goes by - each also without the IOTA
    group's own name words, since OSM often spells out what IOTA leaves
    implied ("Agalega North Island" for AF-001 Agalega Islands' "North")."""
    keys = set()
    for key, value in tags.items():
        if is_name_tag(key):
            for part in re.split(r";| / ", value):
                k = name_key(part.strip())
                if k:
                    keys.add(k)
                    short = " ".join(w for w in k.split() if w not in group_words)
                    if short:
                        keys.add(short)
    return keys


def lon_range(a, b):
    """IOTA's longitude_min/max are by magnitude, not sign, and a few groups
    straddle 180. Returns (west, east); west > east crosses 180."""
    lo, hi = sorted((a, b))
    if hi - lo >= 359:
        return -180.0, 180.0
    if hi - lo > 180:
        return hi, lo
    return lo, hi


_getaddrinfo = socket.getaddrinfo


def _prefer_ipv6(*args, **kwargs):
    """IPv6 addresses first: overpass-api.de's IPv4 addresses have at times
    refused connections while its IPv6 ones answered."""
    return sorted(_getaddrinfo(*args, **kwargs), key=lambda a: a[0] != socket.AF_INET6)


socket.getaddrinfo = _prefer_ipv6


def overpass(query, timeout=130, attempts=60):
    """POSTs a query, retrying with backoff until it works."""
    data = urllib.parse.urlencode({"data": query}).encode()
    for attempt in range(attempts):
        try:
            req = urllib.request.Request(SERVER, data=data, headers={"User-Agent": USER_AGENT})
            with urllib.request.urlopen(req, timeout=timeout) as resp:
                return json.loads(resp.read().decode("utf-8"))
        except (urllib.error.URLError, TimeoutError, ConnectionError, json.JSONDecodeError, OSError) as e:
            wait = min(5 * (attempt + 1), 60)
            print(f"    {e} - retrying in {wait}s", flush=True)
            time.sleep(wait)
    raise RuntimeError("Overpass kept failing")


def islands_in_box(south, west, size, cache):
    """Named islands in one box, cached. A box too dense for the (busy)
    server to answer is split into four smaller ones."""
    path = os.path.join(cache, "tags", f"tile{south:g}_{west:g}_{size:g}.json")
    legacy = os.path.join(cache, "tags", f"tile{south}_{west}.json")  # earlier runs' 30-degree tiles
    if size == TILE and os.path.exists(legacy):
        path = legacy
    split_marker = path[:-len(".json")] + ".split"
    half = size / 2

    def split():
        return [el for ds in (0, half) for dw in (0, half)
                for el in islands_in_box(south + ds, west + dw, half, cache)]

    if os.path.exists(split_marker):  # a previous run found it too dense
        return split()
    if not os.path.exists(path):
        query = (f'[out:json][timeout:300];nwr["place"~"^(island|islet)$"]["name"]'
                 f'({south},{west},{south + size},{west + size});out tags bb;')
        try:
            result = overpass(query, timeout=330, attempts=4 if size > 4 else 60)
            if "remark" in result and "error" in result["remark"].lower():
                raise RuntimeError(result["remark"])
        except RuntimeError as e:
            if size <= 4:
                raise
            print(f"    too slow ({e}) - splitting into four {half:g}-degree tiles", flush=True)
            open(split_marker, "w").close()
            return split()
        with open(path, "w", encoding="utf-8") as f:
            json.dump(result, f)
    with open(path, encoding="utf-8") as f:
        return json.load(f)["elements"]


def in_group_box(g, lon, lat):
    south, north = sorted((float(g["latitude_min"]), float(g["latitude_max"])))
    west, east = lon_range(float(g["longitude_min"]), float(g["longitude_max"]))
    if not south - BOX_MARGIN <= lat <= north + BOX_MARGIN:
        return False
    if west <= east:
        return west - BOX_MARGIN <= lon <= east + BOX_MARGIN
    return lon >= west - BOX_MARGIN or lon <= east + BOX_MARGIN


TILE = 30


def tiles_needed(groups):
    """The TILE-degree tiles that overlap some IOTA group's box."""
    tiles = set()
    for g in groups:
        south, north = sorted((float(g["latitude_min"]), float(g["latitude_max"])))
        west, east = lon_range(float(g["longitude_min"]), float(g["longitude_max"]))
        spans = [(west, east)] if west <= east else [(west, 180.0), (-180.0, east)]
        for w, e in spans:
            for lat in range(-90, 90, TILE):
                for lon in range(-180, 180, TILE):
                    if (lat <= north + BOX_MARGIN and lat + TILE >= south - BOX_MARGIN
                            and lon <= e + BOX_MARGIN and lon + TILE >= w - BOX_MARGIN):
                        tiles.add((lat, lon))
    return sorted(tiles)


def world_islands(groups, cache):
    """Every named island/islet in OSM near an IOTA group ->
    [(element, center lon, center lat, box area)]."""
    os.makedirs(os.path.join(cache, "tags"), exist_ok=True)
    elements = []
    tiles = tiles_needed(groups)
    for n, (south, west) in enumerate(tiles, 1):
        print(f"Named islands, tile {n}/{len(tiles)} ({south},{west})...", flush=True)
        elements += islands_in_box(south, west, TILE, cache)
    return unique_islands(elements)


# The tags --update needs from each island: the ones osm_name_keys reads.
NAME_TAG_REGEX = "^(name|alt_name|official_name|short_name|old_name|loc_name|int_name)$|^(name|alt_name|official_name):"
OSM_KEY = "https://www.openstreetmap.org/wiki/Key:"


def tsv_value(v):
    """A QLever TSV cell -> plain text: '"Uvea"' -> 'Uvea', '"1.5"^^<...>' -> '1.5'."""
    if v.startswith('"'):
        v = v[1:v.rindex('"')]
        return v.replace("\\t", "\t").replace("\\n", "\n").replace('\\"', '"').replace("\\\\", "\\")
    return v


def islands_in_rect(south, west, north, east, cache):
    """Named islands centred in one box (west <= east), cached, in the same
    shape Overpass's "out tags bb" gives - --update's version of
    islands_in_box. From QLever, since the public Overpass servers are
    often too busy to answer at all; its whole-world scan for the island
    centres takes ~20 s whatever the box."""
    path = os.path.join(cache, "tags", f"rect{south:.2f}_{west:.2f}_{north:.2f}_{east:.2f}.json")
    if not os.path.exists(path):
        rows = qlever(f"""PREFIX osmkey: <{OSM_KEY}>
PREFIX geo: <http://www.opengis.net/ont/geosparql#>
PREFIX geof: <http://www.opengis.net/def/function/geosparql/>
SELECT ?osm ?minx ?miny ?maxx ?maxy ?key ?value WHERE {{
  ?osm osmkey:place ?place . FILTER(?place = "island" || ?place = "islet")
  ?osm osmkey:name ?name .
  ?osm geo:hasGeometry/geo:asWKT ?wkt .
  BIND(geof:minX(?wkt) AS ?minx) BIND(geof:minY(?wkt) AS ?miny)
  BIND(geof:maxX(?wkt) AS ?maxx) BIND(geof:maxY(?wkt) AS ?maxy)
  FILTER((?minx + ?maxx) / 2 >= {west} && (?minx + ?maxx) / 2 <= {east} &&
         (?miny + ?maxy) / 2 >= {south} && (?miny + ?maxy) / 2 <= {north})
  ?osm ?key ?value .
  FILTER(STRSTARTS(STR(?key), "{OSM_KEY}") && REGEX(SUBSTR(STR(?key), {len(OSM_KEY) + 1}), "{NAME_TAG_REGEX}"))
}}""")
        elements = {}
        for osm, minx, miny, maxx, maxy, key, value in rows:
            kind, osm_id = osm.strip("<>").rsplit("/", 2)[-2:]
            el = elements.get((kind, osm_id))
            if el is None:
                x1, y1, x2, y2 = (float(tsv_value(v)) for v in (minx, miny, maxx, maxy))
                el = {"type": kind, "id": int(osm_id), "tags": {}}
                if kind == "node":
                    el["lon"], el["lat"] = x1, y1
                else:
                    el["bounds"] = {"minlon": x1, "minlat": y1, "maxlon": x2, "maxlat": y2}
                elements[(kind, osm_id)] = el
            el["tags"][key.strip("<>")[len(OSM_KEY):]] = tsv_value(value)
        with open(path, "w", encoding="utf-8") as f:
            json.dump({"elements": list(elements.values())}, f)
    with open(path, encoding="utf-8") as f:
        return json.load(f)["elements"]


def group_islands(groups, cache):
    """Every named island/islet in OSM in these groups' boxes - the same
    as world_islands, but only searching where it has to."""
    os.makedirs(os.path.join(cache, "tags"), exist_ok=True)
    elements = []
    for n, g in enumerate(groups, 1):
        print(f"Named islands, {g['refno']} ({n}/{len(groups)})...", flush=True)
        south, north = sorted((float(g["latitude_min"]), float(g["latitude_max"])))
        west, east = lon_range(float(g["longitude_min"]), float(g["longitude_max"]))
        south, north = max(-90.0, south - BOX_MARGIN), min(90.0, north + BOX_MARGIN)
        spans = [(west, east)] if west <= east else [(west, 180.0), (-180.0, east)]
        for w, e in spans:
            elements += islands_in_rect(round(south, 2), round(max(-180.0, w - BOX_MARGIN), 2),
                                        round(north, 2), round(min(180.0, e + BOX_MARGIN), 2), cache)
    return unique_islands(elements)


def unique_islands(elements):
    """-> [(element, center lon, center lat, box area)], once each."""
    unique = {}
    for el in elements:
        if "lat" in el:
            unique[(el["type"], el["id"])] = (el, el["lon"], el["lat"], 0.0)
        elif "bounds" in el:
            b = el["bounds"]
            area = (b["maxlon"] - b["minlon"]) * (b["maxlat"] - b["minlat"])
            unique[(el["type"], el["id"])] = (el, (b["minlon"] + b["maxlon"]) / 2, (b["minlat"] + b["maxlat"]) / 2, area)
    print(f"{len(unique)} named islands in OSM", flush=True)
    return list(unique.values())


def find_matches(groups, cache, world=None):
    """-> {(ref, island name): set of (osm type, id)} plus node points."""
    if world is None:
        world = world_islands(groups, cache)
    matches, points, areas, in_boxes = {}, {}, {}, {}
    for n, g in enumerate(groups, 1):
        ref = g["refno"]
        listed = [i["island_name"].strip() for s in g["sub_groups"] for i in s["islands"]
                  if i.get("excluded", "0") == "0"]
        wanted = {}
        for island in listed:
            for alt in island.split(";"):
                k = name_key(alt)
                if k:
                    wanted.setdefault(k, island)

        in_box = [(el, area) for el, lon, lat, area in world if in_group_box(g, lon, lat)]
        elements = [el for el, _ in in_box]
        in_boxes[ref] = elements
        for el, area in in_box:
            areas[(el["type"], el["id"])] = area

        group_words = frozenset(name_key(g["name"]).split())
        found = 0
        for el in elements:
            hits = {wanted[k] for k in osm_name_keys(el.get("tags", {}), group_words) if k in wanted}
            for island in hits:
                matches.setdefault((ref, island), set()).add((el["type"], el["id"]))
                if el["type"] == "node":
                    points[el["id"]] = (el["lon"], el["lat"])
                found += 1
        if n % 100 == 0:
            print(f"[{n}/{len(groups)}] matched", flush=True)
    return matches, points, areas, in_boxes


def bounds_of(el):
    if "bounds" in el:
        b = el["bounds"]
        return b["minlon"], b["minlat"], b["maxlon"], b["maxlat"]
    return el["lon"], el["lat"], el["lon"], el["lat"]


def find_unlisted(matches, areas, in_boxes, cache, others=()):
    """Named islands near a group's listed ones but not on its list ->
    {(ref, OSM name): [shapes]}, each with the group of its nearest listed
    island. others: listed islands of groups not being built (--update) -
    an island in a lake on one, or nearer one, isn't added."""
    matched_ids = {x for ids in matches.values() for x in ids}
    listed = {}
    for (ref, _), ids in matches.items():
        for t, i in ids:
            shape = load_geometry(t, i, cache) if t != "node" else None
            if shape is not None and not shape.is_empty:
                listed.setdefault(ref, []).append(shape.simplify(0.001))
    others = list(others)
    all_listed = [s for shapes in listed.values() for s in shapes] + others
    tree = STRtree(all_listed)
    others_tree = STRtree(others) if others else None

    candidates = {}
    for ref, elements in in_boxes.items():
        boxes = [s.bounds for s in listed.get(ref, [])]
        for el in elements:
            key = (el["type"], el["id"])
            if key in matched_ids or el["type"] == "node" or areas.get(key, 0) < MIN_UNLISTED_AREA:
                continue
            x1, y1, x2, y2 = bounds_of(el)
            if any(x1 <= bx2 + NEAR_DEGREES and x2 >= bx1 - NEAR_DEGREES and
                   y1 <= by2 + NEAR_DEGREES and y2 >= by1 - NEAR_DEGREES for bx1, by1, bx2, by2 in boxes):
                candidates.setdefault(ref, []).append(el)
    print(f"{sum(map(len, candidates.values()))} unlisted islands near listed ones to check", flush=True)
    fetch_geometry({(el["type"], el["id"]) for els in candidates.values() for el in els}, areas, cache)

    nearest = {}  # (osm type, id) -> (distance, ref, element, shape)
    for ref, elements in candidates.items():
        for el in elements:
            key = (el["type"], el["id"])
            shape = load_geometry(el["type"], el["id"], cache)
            if shape is None or shape.is_empty or shape.area < MIN_UNLISTED_AREA:
                continue
            inside = shape.representative_point()
            if any(all_listed[j].contains(inside) for j in tree.query(inside)):
                continue  # in a lake on a listed island - not IOTA
            distance = min(s.distance(shape) for s in listed[ref])
            if others_tree is not None and any(
                    others[j].distance(shape) < distance
                    for j in others_tree.query(shape.buffer(NEAR_DEGREES).envelope)):
                continue  # belongs with a group that isn't being rebuilt
            if distance <= NEAR_DEGREES and (key not in nearest or distance < nearest[key][0]):
                nearest[key] = (distance, ref, el, shape)

    result = {}
    for _, ref, el, shape in nearest.values():
        tags = el.get("tags", {})
        result.setdefault((ref, tags.get("name:en") or tags.get("name")), []).append(shape)
    return result


def qlever(query):
    """Runs a SPARQL query on QLever's OSM planet endpoint -> TSV rows."""
    data = urllib.parse.urlencode({"query": query}).encode()
    for attempt in range(20):
        try:
            req = urllib.request.Request(QLEVER, data=data, headers={
                "User-Agent": USER_AGENT, "Accept": "text/tab-separated-values"})
            with urllib.request.urlopen(req, timeout=400) as resp:
                text = resp.read().decode("utf-8")
            if "An error has occurred" in text:
                raise RuntimeError(text[-300:])
            return [line.split("\t") for line in text.splitlines()[1:] if line]
        except (urllib.error.URLError, TimeoutError, ConnectionError, OSError, RuntimeError) as e:
            wait = min(5 * (attempt + 1), 60)
            print(f"    QLever: {e} - retrying in {wait}s", flush=True)
            time.sleep(wait)
    raise RuntimeError("QLever kept failing")


def fetch_geometry(ids, areas, cache):
    """Downloads (and caches) outlines for the matched ways/relations, as
    WKT from QLever - much faster than Overpass for this, and it hands back
    finished (multi)polygons, so relations needn't be assembled here."""
    os.makedirs(os.path.join(cache, "geom"), exist_ok=True)
    pending = [(t, i) for t, i in sorted(ids, key=lambda x: areas.get(x, 0)) if t != "node"
               and not os.path.exists(os.path.join(cache, "geom", f"{t}{i}.wkt"))]
    print(f"{len(pending)} outlines to download", flush=True)

    # Lots of small islands per request, big ones (Great Britain's outline
    # is ~20 MB) a few at a time.
    batch, batch_area, done = [], 0.0, 0
    def flush():
        nonlocal batch, batch_area, done
        values = " ".join(f"<https://www.openstreetmap.org/{t}/{i}>" for t, i in batch)
        rows = qlever("PREFIX geo: <http://www.opengis.net/ont/geosparql#> "
                      f"SELECT ?osm ?wkt WHERE {{ VALUES ?osm {{ {values} }} ?osm geo:hasGeometry/geo:asWKT ?wkt }}")
        got = {}
        for osm, wkt in rows:
            kind, osm_id = osm.strip("<>").rsplit("/", 2)[-2:]
            got[(kind, int(osm_id))] = wkt.strip('"').split('"^^')[0]
        for key in batch:
            with open(os.path.join(cache, "geom", f"{key[0]}{key[1]}.wkt"), "w", encoding="utf-8") as f:
                f.write(got.get(key, ""))
        done += len(batch)
        print(f"  outlines: {done}/{len(pending)} done", flush=True)
        batch, batch_area = [], 0.0

    for key in pending:
        area = areas.get(key, 0)
        if batch and (len(batch) >= 500 or batch_area + area > 5):
            flush()
        batch.append(key)
        batch_area += area
    if batch:
        flush()


def load_geometry(kind, osm_id, cache):
    path = os.path.join(cache, "geom", f"{kind}{osm_id}.wkt")
    if not os.path.exists(path):
        return None
    with open(path, encoding="utf-8") as f:
        text = f.read().strip()
    if not text:
        return None
    geometry = wkt.loads(text)
    if isinstance(geometry, (Polygon, MultiPolygon)):
        return geometry if geometry.is_valid else geometry.buffer(0)
    if isinstance(geometry, LineString) and geometry.is_closed and len(geometry.coords) >= 4:
        return Polygon(geometry.coords)  # an island way QLever kept as a line
    return None


def rings(geometry):
    polys = geometry.geoms if isinstance(geometry, MultiPolygon) else [geometry]
    out = []
    for poly in polys:
        if poly.is_empty or not isinstance(poly, Polygon):
            continue
        out.append([[round(v, 4) for pt in ring.coords for v in pt[:2]]
                    for ring in [poly.exterior] + list(poly.interiors)])
    return out


def make_entry(ref, island, shapes, pts, listed=True):
    # Unlisted islands at ~100 m: they're only there to say which group
    # a station near a listed island is in.
    shapes = [simplify(s, SIMPLIFY_DEGREES if listed else 2 * SIMPLIFY_DEGREES)
              for s in shapes if s is not None and not s.is_empty]
    if not shapes and not pts:
        return None
    polys = [p for s in shapes for p in rings(s)]
    xs = [v for poly in polys for v in poly[0][0::2]] + [p[0] for p in pts]
    ys = [v for poly in polys for v in poly[0][1::2]] + [p[1] for p in pts]
    e = {"ref": ref, "island": island,
         "minLon": min(xs), "minLat": min(ys), "maxLon": max(xs), "maxLat": max(ys),
         "polys": polys, "points": pts}
    if not listed:
        e["listed"] = False
    return e


def build_entries(groups, cache, world=None, others=()):
    """The islands file's entries for these groups -> (listed entries,
    unlisted entries, number matched in OSM with no usable outline)."""
    matches, points, areas, in_boxes = find_matches(groups, cache, world)
    fetch_geometry({x for ids in matches.values() for x in ids}, areas, cache)

    listed, missing = [], 0
    for (ref, island), ids in sorted(matches.items()):
        # ids is a set, whose order changes from run to run (string hashes
        # are randomised), so sort it to make every build byte-identical.
        ids = sorted(ids)
        e = make_entry(ref, island,
                       [load_geometry(t, i, cache) for t, i in ids if t != "node"],
                       [[round(c, 5) for c in points[i]] for t, i in ids if t == "node"])
        if e:
            listed.append(e)
        else:
            missing += 1

    unlisted = []
    for (ref, name), shapes in sorted(find_unlisted(matches, areas, in_boxes, cache, others).items()):
        e = make_entry(ref, name,
                       [s for s in shapes if not isinstance(s, Point)],
                       [[round(s.x, 5), round(s.y, 5)] for s in shapes if isinstance(s, Point)],
                       listed=False)
        if e:
            unlisted.append(e)
    return listed, unlisted, missing


def write_islands(islands, out_path):
    output = {
        "_source": "Island outlines from OpenStreetMap (ODbL), matched by name to the IOTA directory "
                   "(iota-world.org). Built by Tools/build_iota_islands.py.",
        "islands": islands,
    }
    with open(out_path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(output, f, ensure_ascii=False, separators=(",", ":"))


def main(src, cache, out_path):
    with open(src, encoding="utf-8") as f:
        groups = json.load(f)

    listed, unlisted, missing = build_entries(groups, cache)
    write_islands(listed + unlisted, out_path)
    total = sum(len([i for s in g["sub_groups"] for i in s["islands"]]) for g in groups)
    print(f"{len(listed)} of {total} IOTA islands located ({missing} matched with no usable "
          f"outline), plus {len(unlisted)} unlisted ones near them -> {out_path}")


def group_signature(g):
    """What the islands file depends on for a group: its box, entities and
    island list. A group whose signature changed has to be rebuilt."""
    islands = sorted((i["island_name"].strip(), i.get("excluded", "0"))
                     for s in g["sub_groups"] for i in s["islands"])
    return (g["refno"].strip(), g["name"].strip(), g["dxcc_num"],
            g["latitude_min"], g["latitude_max"], g["longitude_min"], g["longitude_max"], islands)


def entry_shape(e):
    """An islands-file entry back into a shape (None for an islet that's
    only a point)."""
    polys = []
    for poly in e["polys"]:
        rs = [list(zip(r[0::2], r[1::2])) for r in poly]
        if len(rs[0]) >= 4:
            polys.append(Polygon(rs[0], [r for r in rs[1:] if len(r) >= 4]))
    if not polys:
        return None
    shape = MultiPolygon(polys) if len(polys) > 1 else polys[0]
    return shape if shape.is_valid else shape.buffer(0)


def update(cache, new_list=None, data_dir=DATA_DIR):
    """Rebuilds only the groups that changed since Data/iota.json - see the
    top of the file."""
    list_path = os.path.join(data_dir, "iota.json")
    islands_path = os.path.join(data_dir, "iotaIslands.json")
    os.makedirs(cache, exist_ok=True)

    if new_list:
        with open(new_list, encoding="utf-8") as f:
            new_text = f.read()
    else:
        print("Downloading the current IOTA list...", flush=True)
        req = urllib.request.Request(IOTA_LIST_URL, headers={"User-Agent": USER_AGENT})
        with urllib.request.urlopen(req, timeout=120) as resp:
            new_text = resp.read().decode("utf-8")
    new_groups = json.loads(new_text)
    with open(list_path, encoding="utf-8") as f:
        old_groups = json.load(f)

    old_sigs = {g["refno"].strip(): group_signature(g) for g in old_groups}
    changed = [g for g in new_groups if old_sigs.get(g["refno"].strip()) != group_signature(g)]
    changed_refs = {g["refno"].strip() for g in changed}
    removed = set(old_sigs) - {g["refno"].strip() for g in new_groups}
    if not changed and not removed:
        print("Nothing has changed since Data/iota.json - the island data is up to date.")
        return
    print(f"{len(changed)} groups changed: {', '.join(sorted(changed_refs)) or '-'}"
          + (f"; removed: {', '.join(sorted(removed))}" if removed else ""), flush=True)

    with open(islands_path, encoding="utf-8") as f:
        existing = json.load(f)["islands"]
    kept = [e for e in existing if e["ref"] not in changed_refs | removed]
    others = [s for s in (entry_shape(e) for e in kept if e.get("listed", True)) if s is not None]

    listed, unlisted, missing = (build_entries(changed, cache, group_islands(changed, cache), others)
                                 if changed else ([], [], 0))

    # An island the other groups had as unlisted that a changed group now
    # lists by name is that group's now.
    new_shapes = [s for s in (entry_shape(e) for e in listed) if s is not None]
    if new_shapes:
        tree = STRtree(new_shapes)

        def now_listed(e):
            shape = entry_shape(e)
            if shape is None:
                return False
            p = shape.representative_point()
            return any(new_shapes[j].contains(p) for j in tree.query(p))
        kept = [e for e in kept if e.get("listed", True) or not now_listed(e)]

    def key(e):
        return e["ref"], e["island"]
    write_islands(sorted([e for e in kept if e.get("listed", True)] + listed, key=key)
                  + sorted([e for e in kept if not e.get("listed", True)] + unlisted, key=key),
                  islands_path)
    with open(list_path, "w", encoding="utf-8", newline="") as f:
        f.write(new_text)

    found = {(e["ref"], e["island"]) for e in listed}
    not_found = [f"{g['refno'].strip()} {i['island_name'].strip()}" for g in changed
                 for s in g["sub_groups"] for i in s["islands"]
                 if i.get("excluded", "0") == "0" and (g["refno"], i["island_name"].strip()) not in found]
    print(f"Rebuilt {len(changed_refs)} groups: {len(listed)} islands located ({missing} with no usable "
          f"outline), {len(unlisted)} unlisted ones near them. Wrote {islands_path} and {list_path}.")
    if not_found:
        print(f"{len(not_found)} listed islands in those groups aren't in OSM under their IOTA name, "
              f"so the app can't place stations on them: {', '.join(not_found[:20])}"
              + (", ..." if len(not_found) > 20 else ""))


if __name__ == "__main__":
    args = sys.argv[1:]
    if args and args[0] == "--update":
        data = DATA_DIR
        if "--data" in args:
            i = args.index("--data")
            data = args[i + 1]
            del args[i:i + 2]
        update(args[1], args[2] if len(args) > 2 else None, data)
    else:
        main(*args[:3])
