# Board part captures

The opponent-board minions are drawn from `HsbgCardLookup/data/board/*.png`: HS_CardExtractor renders of an
in-play minion with one part left on. To redo them (a patch changed a frame, a new keyword):

1. `powershell -File E:\VvS-tournament\tools\rebuild-and-render.ps1 -List <this folder>\pieces.json`
   (and `pieces-art.json`). It rebuilds the extractor, restarts Hearthstone and writes 1024x1345 PNGs to
   `E:\PYPY\HS_CardExtractor\v2-output\custom\toolkit-board\pieces\`. Restart Hearthstone afterwards: the
   capture scene stays up.
2. Scale each to 307x404 into `data/board/`. Two are processed on the way:
   - `frame`: the normal ring has a grey plate behind the art; subtract `art.png`'s alpha (shrunk 4 px) from it.
   - `badge-1..7`, `deathrattle`, `rally`, `trigger`, `venomous`, `poisonous`: multiply RGB by
     (0.86, 0.78, 0.73). The capture scene lights them whiter than the board does; measured 2026-11-19
     against real shop frames (skull 225,213,212 -> 207,173,159; badge magenta 236,49,222 -> 184,66,160).
3. `diag.json` lists a minion's renderer names (`frameDiag`) in `BepInEx/LogOutput.log`.
