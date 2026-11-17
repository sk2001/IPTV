"""Faux serveur Xtream Codes pour tester l'application (Windows ou Android) sans compte réel.

    python tools/mock-xtream/server.py [port]        # défaut 8099 ; identifiants : demo / demo

Depuis l'émulateur Android, le PC est joignable à http://10.0.2.2:<port>.
Les flux (live, films, épisodes) sont redirigés vers un flux HLS de test public.
"""
import json, struct, sys, zlib
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlparse

PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 8099
USER, PASSWORD = "demo", "demo"
STREAM = "https://test-streams.mux.dev/x36xhzz/x36xhzz.m3u8"

LIVE_CATS = [("1", "NEWS"), ("2", "SPORTS"), ("3", "KIDS")]
VOD_CATS = [("10", "ACTION"), ("11", "COMEDY"), ("12", "DRAMA")]
SERIES_CATS = [("20", "CRIME"), ("21", "ANIMATION")]


def png(seed: int) -> bytes:
    """Petit PNG uni (couleur dérivée de l'identifiant) pour tester le chargement des logos."""
    r, g, b = (seed * 53) % 200 + 40, (seed * 97) % 200 + 40, (seed * 31) % 200 + 40
    w = h = 48
    raw = b"".join(b"\x00" + bytes([r, g, b]) * w for _ in range(h))
    def chunk(t, d): return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xFFFFFFFF)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b"")


def live():
    out = []
    for i in range(1, 61):
        cat = LIVE_CATS[(i - 1) % 3][0]
        out.append({"stream_id": 1000 + i, "name": f"Channel {i:02d} {LIVE_CATS[(i - 1) % 3][1].title()}", "category_id": cat,
                    "stream_icon": f"http://10.0.2.2:{PORT}/logo/{i}.png", "epg_channel_id": f"ch{i}"})
    return out


def vod():
    titles = ["Night Run", "The Last Harbor", "Blue Horizon", "Paper Moon", "Iron Valley", "Silent Echo"]
    return [{"stream_id": 2000 + i, "name": f"{titles[i % len(titles)]} {2000 + i}", "category_id": VOD_CATS[i % 3][0],
             "stream_icon": f"http://10.0.2.2:{PORT}/logo/{100 + i}.png", "container_extension": "mp4", "rating": str(round(5 + (i % 5) * 0.8, 1))}
            for i in range(1, 46)]


def series():
    names = ["Harbor Lights", "Detective Moon", "Space Crew", "Little Chefs", "Dark Water"]
    return [{"series_id": 3000 + i, "name": f"{names[i % len(names)]} {i}", "category_id": SERIES_CATS[i % 2][0],
             "cover": f"http://10.0.2.2:{PORT}/logo/{200 + i}.png", "rating": str(round(6 + (i % 4) * 0.7, 1))} for i in range(1, 13)]


def series_info(sid: str):
    eps = {}
    for season in (1, 2):
        eps[str(season)] = [{"id": f"{sid}{season}{n}", "episode_num": n, "season": season, "container_extension": "mp4",
                             "title": f"Series {sid} - S{season:02d}E{n:02d} - Episode title {season}.{n}"} for n in range(1, 5)]
    return {"info": {"name": f"Series {sid}"}, "episodes": eps}


class Handler(BaseHTTPRequestHandler):
    def log_message(self, fmt, *args): print("%s %s" % (self.address_string(), fmt % args), flush=True)

    def send_bytes(self, body: bytes, ctype: str, code: int = 200):
        self.send_response(code); self.send_header("Content-Type", ctype); self.send_header("Content-Length", str(len(body))); self.end_headers(); self.wfile.write(body)

    def send_json(self, obj): self.send_bytes(json.dumps(obj).encode(), "application/json")

    def do_GET(self):
        u = urlparse(self.path); q = {k: v[0] for k, v in parse_qs(u.query).items()}
        if u.path == "/player_api.php":
            ok = q.get("username") == USER and q.get("password") == PASSWORD
            a = q.get("action", "")
            if not a: return self.send_json({"user_info": {"auth": 1 if ok else 0, "status": "Active" if ok else "Disabled"}, "server_info": {"url": "mock"}})
            if not ok: return self.send_json([])
            data = {"get_live_categories": lambda: [{"category_id": c, "category_name": n} for c, n in LIVE_CATS],
                    "get_vod_categories": lambda: [{"category_id": c, "category_name": n} for c, n in VOD_CATS],
                    "get_series_categories": lambda: [{"category_id": c, "category_name": n} for c, n in SERIES_CATS],
                    "get_live_streams": live, "get_vod_streams": vod, "get_series": series,
                    "get_series_info": lambda: series_info(q.get("series_id", "0"))}
            return self.send_json(data.get(a, lambda: [])())
        if u.path.startswith("/logo/"):
            try: seed = int(u.path.split("/")[-1].split(".")[0])
            except ValueError: seed = 1
            return self.send_bytes(png(seed), "image/png")
        if u.path.split("/")[1:2] and u.path.split("/")[1] in ("live", "movie", "series"):
            self.send_response(302); self.send_header("Location", STREAM); self.send_header("Content-Length", "0"); self.end_headers(); return
        if u.path == "/xmltv.php":
            return self.send_bytes(b'<?xml version="1.0"?><tv></tv>', "application/xml")
        self.send_bytes(b"not found", "text/plain", 404)


if __name__ == "__main__":
    print(f"Faux serveur Xtream sur http://0.0.0.0:{PORT}  (identifiants {USER} / {PASSWORD})", flush=True)
    ThreadingHTTPServer(("0.0.0.0", PORT), Handler).serve_forever()
