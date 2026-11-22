"""Dev check: compare the match recorder's records with Replay Watcher's own catalog of the same games
(hero, place, rounds, rating, final board).

  python -I tools/check-match-records.py <matches folder> <replays folder with .catalog.txt>...
"""
import glob, json, os, sys

matches, folders = sys.argv[1], sys.argv[2:]
cat = {}
for d in folders:
    p = os.path.join(d, ".catalog.txt")
    if os.path.exists(p):
        for line in open(p, encoding="utf-8"):
            f = line.rstrip("\n").split("|")
            if len(f) > 8:
                cat[f[0]] = f


def minutes(stamp):  # yyyyMMdd_HHmmss
    return int(stamp[9:11]) * 60 + int(stamp[11:13])


bad = 0
recs = sorted(glob.glob(os.path.join(matches, "2*.json")))
for r in recs:
    m = json.load(open(r, encoding="utf-8"))
    # A record is named by the moment the match was first seen, a recording by its connect: minutes apart.
    key = next((k for k in cat if k[:8] == m["id"][:8] and 0 <= minutes(m["id"]) - minutes(k) <= 2), None)
    if key is None:
        print(m["id"], "NO CATALOG ROW: place", m["place"], "rounds", len(m["turns"]))
        bad += 1
        continue
    c = cat.pop(key)
    flags = []
    if c[2] != m["hero"]:
        flags.append(f"hero {c[2]} != {m['hero']}")
    if int(c[3]) != m["place"]:
        flags.append(f"place {c[3]} != {m['place']}")
    if int(c[4]) != len(m["turns"]):
        flags.append(f"rounds {c[4]} != {len(m['turns'])}")
    if (c[6] or None) != (str(m["rating"]) if m.get("rating") is not None else None):
        flags.append(f"rating {c[6]} != {m.get('rating')}")
    theirs = [":".join(x.split(":")[:3]) for x in c[8].split(",") if x]
    ours = [f"{x['card']}:{x['atk']}:{x['hp']}" for x in m["finalBoard"]]
    if theirs != ours:
        flags.append("final board: catalog " + " ".join(theirs) + " | record " + " ".join(ours))
    last = m["turns"][-1]
    print(m["id"], f"S{m.get('season', 0)}/R{m.get('ratingSeason', 0)}", "place", m["place"], "last fight", last.get("result"),
          "OK" if not flags else "; ".join(flags))
    bad += bool(flags)
print(len(recs), "records,", bad, "with differences;", len(cat), "catalog rows without a record")
