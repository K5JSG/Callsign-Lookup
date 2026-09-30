"""Builds Data/ontarioDivisions.json - Ontario census division outlines plus
Algonquin Provincial Park's outline, used to pick a station's Ontario RAC
section (GH / ONE / ONN / ONS). Not part of the app build; re-run by hand if
the boundaries ever need refreshing.

Inputs (download first):
  * Statistics Canada 2021 census division cartographic boundary file,
    lcd_000b21a_e.zip, unzipped:
    https://www12.statcan.gc.ca/census-recensement/2021/geo/sip-pis/boundary-limites/files-fichiers/lcd_000b21a_e.zip
  * Algonquin Provincial Park from OpenStreetMap (relation 910784), as
    Overpass JSON:
    [out:json];relation["name"="Algonquin Provincial Park"];out geom;

Requires: pip install pyshp pyproj shapely

Usage:
  python build_ontario_divisions.py <lcd_000b21a_e (no extension)> <algonquin.json> <out.json>
"""

import json
import sys

import shapefile
from pyproj import CRS, Transformer
from shapely.geometry import LineString, MultiPolygon, shape
from shapely.ops import polygonize, transform, unary_union

ONTARIO_PRUID = "35"
# ~200 m - plenty for picking a section, and keeps the file small.
SIMPLIFY_DEGREES = 0.002
# Islands smaller than roughly 1 km^2 (thousands of them along Georgian Bay)
# are dropped - a station on one still gets the nearest division, since the
# app snaps any point QRZ says is in Ontario to the closest division.
MIN_POLYGON_AREA = 0.0001


def rings(geometry):
    """shapely (Multi)Polygon -> [[flat lon,lat ring, ...], ...] per polygon."""
    polys = geometry.geoms if isinstance(geometry, MultiPolygon) else [geometry]
    out = []
    for poly in polys:
        if poly.is_empty or poly.area < MIN_POLYGON_AREA:
            continue
        poly_rings = [poly.exterior] + list(poly.interiors)
        out.append([[round(v, 5) for pt in ring.coords for v in pt] for ring in poly_rings])
    return out


def bounds(geometry):
    min_lon, min_lat, max_lon, max_lat = geometry.bounds
    return {"minLon": round(min_lon, 5), "minLat": round(min_lat, 5),
            "maxLon": round(max_lon, 5), "maxLat": round(max_lat, 5)}


def main(shp_path, park_path, out_path):
    reader = shapefile.Reader(shp_path, encoding="latin-1")
    with open(shp_path + ".prj", encoding="ascii") as f:
        to_wgs84 = Transformer.from_crs(CRS.from_wkt(f.read()), "EPSG:4326", always_xy=True).transform

    divisions = []
    for sr in reader.iterShapeRecords():
        rec = sr.record.as_dict()
        if rec["PRUID"] != ONTARIO_PRUID:
            continue
        geometry = transform(to_wgs84, shape(sr.shape.__geo_interface__))
        geometry = geometry.simplify(SIMPLIFY_DEGREES, preserve_topology=True)
        # "Greater Sudbury / Grand Sudbury" -> "Greater Sudbury"
        name = rec["CDNAME"].split(" / ")[0]
        divisions.append({"cduid": rec["CDUID"], "name": name, **bounds(geometry), "polys": rings(geometry)})

    with open(park_path, encoding="utf-8") as f:
        park_relation = json.load(f)["elements"][0]
    outer_lines = [LineString([(p["lon"], p["lat"]) for p in m["geometry"]])
                   for m in park_relation["members"] if m.get("role") == "outer" and "geometry" in m]
    park = unary_union(list(polygonize(unary_union(outer_lines))))
    park = park.simplify(SIMPLIFY_DEGREES, preserve_topology=True)

    output = {
        "_source": "Statistics Canada 2021 census divisions (lcd_000b21a_e); Algonquin Provincial Park "
                   "from OpenStreetMap relation 910784 (ODbL). Built by Tools/build_ontario_divisions.py.",
        "divisions": sorted(divisions, key=lambda d: d["cduid"]),
        "algonquinPark": {**bounds(park), "polys": rings(park)},
    }
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(output, f, separators=(",", ":"))
    print(f"{len(divisions)} divisions written to {out_path}")


if __name__ == "__main__":
    main(*sys.argv[1:4])
