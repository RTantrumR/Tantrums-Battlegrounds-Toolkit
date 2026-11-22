"""Dev tool: turn Replay Watcher recordings (raw game-server packets) into state streams the toolkit's
match recorder can be fed with (TantrumsToolkitStandalone.exe --feed-states), so the recorder is tested on
real games with their real dates.

  python -I tools/replay-states.py <recordings folder>... --out <folder> [--hsserver E:\PYPY\Test_HS_Server]

One <game id>.states.jsonl.gz per Battlegrounds game. Lines:
  {"id", "startLocal": "YYYY-MM-DD HH:MM:SS", "build", "gameType", "friendly": <player id>}
  {"ms", "game": <entity id>, "players": {"<player id>": <entity id>}}        a CreateGame
  {"ms", "e": [[entity id, card id, {tag: value}], ...]}                       what one packet changed
  {"ms", "rating": <new>, "change": <delta>}
"""
import argparse, gzip, json, sys
from datetime import datetime
from pathlib import Path

ap = argparse.ArgumentParser()
ap.add_argument("folders", nargs="+")
ap.add_argument("--out", required=True)
ap.add_argument("--hsserver", default=r"E:\PYPY\Test_HS_Server")
args = ap.parse_args()
sys.path.insert(0, args.hsserver)
from hsserver.recording import load, recordings, game_id          # noqa: E402
from hsserver.catalog import is_local_session, is_battlegrounds, POWER_HISTORY, ENTITY_CHOICES, BG_RATING_CHANGE  # noqa: E402
from hsserver.gamestate import GameState                           # noqa: E402
from hsserver.proto import decode                                   # noqa: E402

KINDS = ("FullEntity", "ShowEntity", "ChangeEntity", "HideEntity", "TagChange")
out = Path(args.out); out.mkdir(parents=True, exist_ok=True)
done = 0
for folder in args.folders:
    for path in recordings(folder):
        rec = load(path)
        if is_local_session(rec) or not is_battlegrounds(rec):
            continue
        s2c = [p for p in rec.packets if p.dir == "S2C"]
        if not any(p.type == POWER_HISTORY for p in s2c):
            continue
        friendly = next((decode("PegasusGame.EntityChoices", p.body).get("PlayerId")
                         for p in s2c if p.type == ENTITY_CHOICES), None)
        if friendly is None:
            continue
        gid = game_id(path)
        try:
            start = datetime.strptime(gid[:15], "%Y%m%d_%H%M%S")
        except ValueError:
            start = datetime.fromtimestamp(path.stat().st_mtime)
        c = rec.connect or {}
        lines = [{"id": gid, "startLocal": start.strftime("%Y-%m-%d %H:%M:%S"), "build": c.get("clientVersion") or c.get("version"),
                  "gameType": c.get("nextGameType"), "friendly": friendly}]
        gs = GameState()
        seen: dict[int, tuple[str, dict]] = {}
        for p in s2c:
            if p.type == BG_RATING_CHANGE:
                rc = decode("PegasusGame.BattlegroundsRatingChange", p.body)
                lines.append({"ms": p.ms, "rating": rc.get("NewRating"), "change": rc.get("RatingChange")})
                continue
            if p.type != POWER_HISTORY:
                continue
            ph = decode("PegasusGame.PowerHistory", p.body)
            touched: set[int] = set()
            for d in ph.get("List", []):
                if "CreateGame" in d or "ResetGame" in d:
                    cg = d.get("CreateGame") or d["ResetGame"].get("CreateGame", {})
                    gs.apply_data(d)
                    seen.clear()
                    lines.append({"ms": p.ms, "game": gs.game_entity_id,
                                  "players": {str(pl.get("Id", 0)): pl.get("Entity", {}).get("Id", 0) for pl in cg.get("Players", [])}})
                    touched.update(gs.entities)
                    continue
                gs.apply_data(d)
                for k in KINDS:
                    if k in d:
                        touched.add(d[k].get("Entity", 0))
            changes = []
            for eid in sorted(touched):
                ent = gs.entities.get(eid)
                if ent is None:
                    continue
                card0, tags0 = seen.get(eid, (None, {}))
                diff = {str(t): v for t, v in ent.tags.items() if tags0.get(t) != v}
                if diff or card0 != ent.card_id:
                    changes.append([eid, ent.card_id, diff])
                    seen[eid] = (ent.card_id, dict(ent.tags))
            if changes:
                lines.append({"ms": p.ms, "e": changes})
        with gzip.open(out / (gid + ".states.jsonl.gz"), "wt", encoding="utf-8") as f:
            for line in lines:
                f.write(json.dumps(line, separators=(",", ":")) + "\n")
        done += 1
        print(gid, len(lines), "lines")
print(done, "games")
