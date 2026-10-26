#!/usr/bin/env python3
"""Refresh the checked-in Fort Collins bike network.

Run this by hand when the trails change. It is not used by tests or the web app.

One COTREX query (Colorado Parks and Wildlife open trails, hosted by CDOT) and
a short sequence of bounded Overpass queries (OpenStreetMap). The results are
cached into src/GpsTrainer.Server/Loops/fort-collins-network.json.

Overpass usage: https://wiki.openstreetmap.org/wiki/Overpass_API
The public servers often time out on one large Fort Collins query, so this
script sends a few smaller queries one at a time and waits between them.
Do not loop this script. COTREX is public ArcGIS data; keep it to this box.
"""

from __future__ import annotations

import hashlib
import json
import math
import sys
import time
import urllib.parse
import urllib.request
from collections import defaultdict
from pathlib import Path

USER_AGENT = "gpsTrainer-fort-collins-refresh/1.0 (local course-generator cache)"
ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "src" / "GpsTrainer.Server" / "Loops" / "fort-collins-network.json"

SOUTH, WEST, NORTH, EAST = 40.45, -105.22, 40.66, -104.90
COTREX = "https://maps.codot.gov/server/rest/services/Hosted/CPW_Trails/FeatureServer/2/query"
OVERPASS_ENDPOINTS = (
    "https://overpass-api.de/api/interpreter",
    "https://overpass.kumi.systems/api/interpreter",
)

PAVED = {"paved", "asphalt", "concrete", "boardwalk", "chipseal"}
UNPAVED = {"dirt", "gravel", "ground", "earth", "native", "unpaved", "fine_gravel", "pebblestone", "sand", "mud", "grass"}
ROADS = {
    "motorway", "trunk", "primary", "secondary", "tertiary", "unclassified",
    "residential", "living_street", "service", "motorway_link", "trunk_link",
    "primary_link", "secondary_link", "tertiary_link",
}


def main() -> None:
    print("Querying COTREX once…", flush=True)
    cotrex = fetch_cotrex()
    print(f"  {len(cotrex)} bike=yes trail parts", flush=True)
    print("Querying Overpass in small bounded requests…", flush=True)
    osm_elements = fetch_overpass()
    print(f"  {len(osm_elements)} OSM elements", flush=True)
    places, osm_segments = read_osm(osm_elements, cotrex)
    segments = cotrex + osm_segments
    document = {
        "name": "Fort Collins bike network",
        "fetchedUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "bbox": {"south": SOUTH, "west": WEST, "north": NORTH, "east": EAST},
        "attribution": {
            "osm": "© OpenStreetMap contributors. Data is available under the Open Database License.",
            "osmLicense": "https://www.openstreetmap.org/copyright",
            "cotrex": "Trail corridors from COTREX, Colorado Parks and Wildlife.",
            "cotrexHome": "https://cpw.state.co.us/",
            "note": "Paved COTREX trails are preferred. OSM supplies roads with bike lanes and designated cycleways.",
        },
        "places": places,
        "segments": segments,
    }
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_text(json.dumps(document, separators=(",", ":")), encoding="utf-8")
    kinds = defaultdict(int)
    for segment in segments:
        kinds[segment["kind"] + "/" + segment["surface"]] += 1
    print(f"Wrote {OUTPUT.relative_to(ROOT)} ({OUTPUT.stat().st_size} bytes)")
    print(f"  segments {len(segments)} places {len(places)} kinds {dict(kinds)}")


def fetch_cotrex() -> list[dict]:
    features: list[dict] = []
    offset = 0
    while True:
        params = {
            "where": "bike='yes' OR bike='Yes'",
            "geometry": f"{WEST},{SOUTH},{EAST},{NORTH}",
            "geometryType": "esriGeometryEnvelope",
            "inSR": "4326",
            "spatialRel": "esriSpatialRelIntersects",
            "outFields": "name,surface,bike,type",
            "returnGeometry": "true",
            "outSR": "4326",
            "f": "geojson",
            "resultRecordCount": "2000",
            "resultOffset": str(offset),
        }
        payload = get_json(COTREX + "?" + urllib.parse.urlencode(params))
        batch = payload.get("features") or []
        features.extend(batch)
        if not payload.get("exceededTransferLimit") or not batch:
            break
        offset += len(batch)
        if offset > 20000:
            break
    segments = []
    for feature in features:
        geometry = feature.get("geometry") or {}
        props = feature.get("properties") or {}
        name = (props.get("name") or "").strip()
        surface = surface_of(props.get("surface"))
        for line in lines_of(geometry):
            simplified = simplify(line, 12)
            if len(simplified) < 2 or length_m(simplified) < 25:
                continue
            segments.append({
                "name": name,
                "kind": "trail",
                "surface": surface,
                "source": "cotrex",
                "coords": round_line(simplified),
            })
    return segments


def fetch_overpass() -> list[dict]:
    box = f"({SOUTH:.2f},{WEST:.2f},{NORTH:.2f},{EAST:.2f})"
    queries = [
        ("cycleway", f'[out:json][timeout:90];way["highway"="cycleway"]{box};out geom;', True),
        ("lanes", f'[out:json][timeout:90];(way["cycleway"~"^(lane|track)$"]{box};way["cycleway:both"~"^(lane|track)$"]{box};way["cycleway:left"~"^(lane|track)$"]{box};way["cycleway:right"~"^(lane|track)$"]{box};);out geom;', True),
        ("designated", f'[out:json][timeout:90];way["bicycle"="designated"]["highway"!="cycleway"]{box};out geom;', False),
        ("places", '[out:json][timeout:40];node["leisure"="park"]["name"](40.52,-105.12,40.62,-105.02);out;', False),
        ("trailheads", f'[out:json][timeout:40];node["information"="trailhead"]{box};out;', False),
    ]
    elements: list[dict] = []
    for index, (label, query, required) in enumerate(queries):
        if index:
            time.sleep(8)
        try:
            payload = overpass(query)
        except SystemExit as error:
            print(f"  {label} failed ({error})", flush=True)
            if required:
                raise
            continue
        batch = payload.get("elements") or []
        print(f"  {label}: {len(batch)} elements", flush=True)
        elements.extend(batch)
    return elements


def overpass(query: str) -> dict:
    cache = Path("/tmp/fc-overpass-cache")
    cache.mkdir(exist_ok=True)
    key = hashlib.sha256(query.encode("utf-8")).hexdigest()[:16]
    cached = cache / f"{key}.json"
    if cached.exists() and cached.stat().st_size > 20:
        print(f"  using cached Overpass response {key}", flush=True)
        return json.loads(cached.read_text(encoding="utf-8"))
    last_error = "Overpass did not respond."
    for attempt, endpoint in enumerate(OVERPASS_ENDPOINTS):
        try:
            payload = post_json(endpoint, query)
        except Exception as error:  # noqa: BLE001 — one fallback instance, then stop
            last_error = str(error)
            print(f"  {endpoint} failed ({last_error})", flush=True)
            if attempt == 0:
                time.sleep(8)
            continue
        cached.write_text(json.dumps(payload), encoding="utf-8")
        return payload
    raise SystemExit(last_error)


def read_osm(elements: list[dict], trails: list[dict]) -> tuple[list[dict], list[dict]]:
    trail_cells = set()
    for segment in trails:
        if segment["surface"] != "paved":
            continue
        for lon, lat in segment["coords"]:
            trail_cells.add(cell(lat, lon, 25))
    places = []
    segments = []
    seen_places: list[tuple[str, float, float]] = []
    for element in elements:
        tags = element.get("tags") or {}
        if element.get("type") == "node" and tags.get("information") == "trailhead":
            add_place(places, seen_places, tags.get("name") or "Trailhead", "trailhead", element.get("lat"), element.get("lon"))
            continue
        if tags.get("leisure") == "park" and tags.get("name"):
            center = element.get("center") or {}
            lat = element.get("lat", center.get("lat"))
            lon = element.get("lon", center.get("lon"))
            add_place(places, seen_places, tags["name"], "park", lat, lon)
            continue
        if element.get("type") != "way":
            continue
        if tags.get("bicycle") == "no" or tags.get("access") == "private":
            continue
        kind = osm_kind(tags)
        if kind is None:
            continue
        line = [(point["lon"], point["lat"]) for point in element.get("geometry") or [] if "lat" in point and "lon" in point]
        simplified = simplify(line, 12)
        if len(simplified) < 2 or length_m(simplified) < 20:
            continue
        if kind == "trail" and trail_cells and duplicates_paved_trail(simplified, trail_cells):
            continue
        surface = surface_of(tags.get("surface"))
        if surface == "unknown" and kind == "lane":
            surface = "paved"
        segments.append({
            "name": (tags.get("name") or "").strip(),
            "kind": kind,
            "surface": surface,
            "source": "osm",
            "coords": round_line(simplified),
        })
    if len(places) < 8:
        places.extend(nominatim_parks(seen_places))
    places.sort(key=lambda place: place["name"])
    return places[:120], segments


def nominatim_parks(seen) -> list[dict]:
    """One request per second. Used only when the Overpass park query is thin."""
    names = [
        "Lee Martinez Park, Fort Collins, Colorado",
        "City Park, Fort Collins, Colorado",
        "Edora Park, Fort Collins, Colorado",
        "Rolland Moore Park, Fort Collins, Colorado",
        "Spring Canyon Park, Fort Collins, Colorado",
        "Fossil Creek Park, Fort Collins, Colorado",
        "Legacy Park, Fort Collins, Colorado",
        "Creekside Park, Fort Collins, Colorado",
    ]
    found = []
    for index, name in enumerate(names):
        if index:
            time.sleep(1.1)
        url = "https://nominatim.openstreetmap.org/search?" + urllib.parse.urlencode({
            "q": name,
            "format": "jsonv2",
            "limit": "1",
        })
        try:
            payload = get_json(url)
        except Exception as error:  # noqa: BLE001 — parks are optional labels
            print(f"  nominatim skipped ({error})", flush=True)
            break
        if not payload:
            continue
        hit = payload[0]
        add_place(found, seen, hit.get("name") or name.split(",")[0], "park", float(hit["lat"]), float(hit["lon"]))
    print(f"  nominatim parks {len(found)}", flush=True)
    return found


def osm_kind(tags: dict) -> str | None:
    highway = tags.get("highway") or ""
    lane_values = [
        tags.get("cycleway") or "",
        tags.get("cycleway:both") or "",
        tags.get("cycleway:left") or "",
        tags.get("cycleway:right") or "",
    ]
    has_lane = any(value in ("lane", "track") for value in lane_values)
    if has_lane and highway in ROADS:
        return "lane"
    if highway == "cycleway" or (tags.get("bicycle") == "designated" and highway in ("cycleway", "path", "footway")):
        return "trail"
    if tags.get("bicycle") == "designated" and highway in ROADS:
        return "lane"
    if has_lane:
        return "lane"
    return None


def duplicates_paved_trail(line: list[tuple[float, float]], trail_cells: set[tuple[int, int]]) -> bool:
    hits = 0
    for lon, lat in line:
        if cell(lat, lon, 25) in trail_cells:
            hits += 1
    return hits / len(line) >= 0.7


def add_place(places, seen, name, kind, lat, lon) -> None:
    if lat is None or lon is None:
        return
    label = " ".join(str(name).split())
    if not label:
        return
    for existing, existing_lat, existing_lon in seen:
        if existing.casefold() == label.casefold() and haversine(existing_lat, existing_lon, lat, lon) < 250:
            return
    seen.append((label, lat, lon))
    places.append({
        "name": label,
        "kind": kind,
        "lat": round(float(lat), 5),
        "lon": round(float(lon), 5),
    })


def lines_of(geometry: dict) -> list[list[tuple[float, float]]]:
    kind = geometry.get("type")
    coordinates = geometry.get("coordinates") or []
    if kind == "LineString":
        return [as_line(coordinates)]
    if kind == "MultiLineString":
        return [as_line(line) for line in coordinates]
    return []


def as_line(coordinates) -> list[tuple[float, float]]:
    line = []
    for point in coordinates:
        if not isinstance(point, (list, tuple)) or len(point) < 2:
            continue
        lon, lat = float(point[0]), float(point[1])
        if not (math.isfinite(lat) and math.isfinite(lon)):
            continue
        line.append((lon, lat))
    return line


def surface_of(value) -> str:
    text = (value or "").strip().lower()
    if text in PAVED:
        return "paved"
    if text in UNPAVED:
        return "unpaved"
    return "unknown"


def round_line(line: list[tuple[float, float]]) -> list[list[float]]:
    rounded = []
    for lon, lat in line:
        point = [round(lon, 5), round(lat, 5)]
        if rounded and rounded[-1] == point:
            continue
        rounded.append(point)
    return rounded


def cell(lat: float, lon: float, meters: float) -> tuple[int, int]:
    height = meters / 111_320
    width = meters / (111_320 * math.cos(math.radians(lat)))
    return (int(math.floor(lat / height)), int(math.floor(lon / max(width, 1e-9))))


def length_m(line: list[tuple[float, float]]) -> float:
    total = 0.0
    for (lon1, lat1), (lon2, lat2) in zip(line, line[1:]):
        total += haversine(lat1, lon1, lat2, lon2)
    return total


def haversine(lat1, lon1, lat2, lon2) -> float:
    radius = 6_371_000.0
    p1, p2 = math.radians(lat1), math.radians(lat2)
    dp, dl = math.radians(lat2 - lat1), math.radians(lon2 - lon1)
    a = math.sin(dp / 2) ** 2 + math.cos(p1) * math.cos(p2) * math.sin(dl / 2) ** 2
    return 2 * radius * math.atan2(math.sqrt(a), math.sqrt(1 - a))


def simplify(line: list[tuple[float, float]], epsilon_m: float) -> list[tuple[float, float]]:
    if len(line) < 3:
        return line
    return douglas(line, epsilon_m)


def douglas(line: list[tuple[float, float]], epsilon_m: float) -> list[tuple[float, float]]:
    if len(line) < 3:
        return line
    start, end = line[0], line[-1]
    worst = 0.0
    index = 0
    for i in range(1, len(line) - 1):
        distance = point_segment_m(line[i], start, end)
        if distance > worst:
            worst = distance
            index = i
    if worst <= epsilon_m:
        return [start, end]
    left = douglas(line[: index + 1], epsilon_m)
    right = douglas(line[index:], epsilon_m)
    return left[:-1] + right


def point_segment_m(point, start, end) -> float:
    lat = (start[1] + end[1]) / 2
    x, y = project(point[1], point[0], lat)
    x1, y1 = project(start[1], start[0], lat)
    x2, y2 = project(end[1], end[0], lat)
    dx, dy = x2 - x1, y2 - y1
    if dx == 0 and dy == 0:
        return math.hypot(x - x1, y - y1)
    t = max(0.0, min(1.0, ((x - x1) * dx + (y - y1) * dy) / (dx * dx + dy * dy)))
    return math.hypot(x - (x1 + t * dx), y - (y1 + t * dy))


def project(lat: float, lon: float, ref_lat: float) -> tuple[float, float]:
    return (
        math.radians(lon) * 6_371_000.0 * math.cos(math.radians(ref_lat)),
        math.radians(lat) * 6_371_000.0,
    )


def get_json(url: str) -> dict:
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    with urllib.request.urlopen(request, timeout=120) as response:
        return json.load(response)


def post_json(url: str, query: str) -> dict:
    data = urllib.parse.urlencode({"data": query}).encode("utf-8")
    request = urllib.request.Request(url, data=data, headers={"User-Agent": USER_AGENT})
    with urllib.request.urlopen(request, timeout=30) as response:
        return json.load(response)


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        sys.exit(1)
