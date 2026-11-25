# Tantrum's Battlegrounds Toolkit

**English** | [Українська](README.ua.md)

A program for Hearthstone Battlegrounds players. It draws over the game and puts the things you would
otherwise hover, alt-tab or memorise on screen while you play.

It runs on its own. It started as a [Hearthstone Deck Tracker](https://hsdecktracker.net/) plugin; HDT
stops running plugins on 30 November 2026, so the toolkit is a standalone program since v1.0.0 and
needs neither HDT nor any other tracker. It also runs beside HDT or Firestone if you use one.

<img src="assets/opponent-mmr.png" align="right" width="230" alt="Standings sidebar and leaderboard portraits showing each opponent's rating, tavern tier and most common minion type" />

**On screen, all match long**

- **Opponent tiers and minion types**: every player's tavern tier and the minion type they have most
  of, with its count, next to their leaderboard portrait from turn 1. The game shows this only while
  you hover a portrait; here it is always on screen.
- **Opponent names and MMR**: each opponent's name, rating and today's gain or loss, by the portraits,
  in a movable sidebar, or both. Solo and Duos.
- **Opponents' last boards**: hover a leaderboard portrait to see the board that player last fought
  you with and the turn it is from. Click the portrait to keep it open.
- **Lobby minion types**: the five types in this lobby stay on screen, as icons, names or both, and
  can show the banned types crossed out.
- **Turn counter**: the turn number, top right.
- **Dark Gifts**: a "?" above the Dark Discovery button opens every gift you can still be offered,
  what it does and which minions can carry it. From turn 6 it also shows the minion pool of your
  guaranteed type.
- **Trinkets and anomaly**: your trinkets and the lobby anomaly stay visible for the whole match.

Every box can be moved and resized in the game itself, and the lobby types box, the sidebar and the
turn counter join into one panel when you drag them against each other.

**One key away**

- **Card search**: press `F3` and type to find any card by name, tier, minion type, keyword or stats
  (`t3`, `5/5`, `trinket mech`). Golden versions, tokens, buddies and hero powers included.
- **Floating cards**: drag a card out of the search and leave it on screen at any size, for yourself
  or for your viewers.

A new install shows opponents' tavern tiers and minion types by their portraits, the sidebar with
names and MMR, and Dark Gifts. Everything else is switched on in the settings.
Card data and art come from [hsbg.cards](https://hsbg.cards).

<br clear="right" />

![Dark Gift panel: the gifts on offer and the guaranteed-type minion pool over a Battlegrounds match](assets/Dark%20Gifts%20Showcase_both.png)

<p align="center">
  <img src="assets/overlay.png" width="32%" alt="Card search over a Battlegrounds match" />
  <img src="assets/screen-trinkets_anomalies.png" width="32%" alt="Trinkets and the anomaly shown during a match" />
  <img src="assets/screen-cards-drag.png" width="32%" alt="Cards dragged out of the search onto the screen" />
  <img src="assets/search.png" width="32%" alt="Browsing tier 7" />
  <img src="assets/filters.png" width="32%" alt="Search filters" />
  <img src="assets/golden.png" width="32%" alt="Golden version of a card" />
</p>

## Install

Windows only. Run Hearthstone in **Borderless** or **Windowed** mode.

1. **Download the Setup.** Get `TantrumsBattlegroundsToolkit-Setup-X.Y.Z.exe` from
   [Releases](https://github.com/RTantrumR/Tantrums-Battlegrounds-Toolkit/releases/latest).
2. **Run it.** It asks for the toolkit's language, whether the toolkit starts with Windows, and
   whether to share match data (see [Privacy](#privacy)). If Windows SmartScreen warns about an
   unknown publisher, click *More info → Run anyway*.
3. **Play.** The toolkit shows over Hearthstone in a Battlegrounds match. Press `F3` for the card
   search.

The first start downloads card art in the background (about 200 MB, once).

**Coming from the HDT plugin?** Run the same Setup. It installs the standalone program and switches
the old plugin off in HDT, so nothing shows twice.

## How it runs

The toolkit lives in the Windows tray. Click its icon to open the settings, double-click it for the
card search, right-click it for the menu.

With **Start with Windows** on, it waits in the tray using about 2 MB of memory, comes up when
Hearthstone starts and closes again with the game. With it off, start it from its shortcut when you
play. You can change this in the settings.

## Controls

| Key | Action |
|---|---|
| `F3` | Open / close the card search |
| typing | Search (`Tab` toggles smart search, `Enter` opens the first result, `Esc` closes) |
| `G` | Show the golden version of the selected card |
| `S` | Put the cursor back in the search box |

All three keys can be rebound in the settings. There is also a magnifying-glass button next to the
game's card-list book for opening the search with the mouse.

## Settings

Open them from the tray icon. Each feature has its own page with a picture of what it does.
**Arrange** lets you drag a box to where you want it, in the game itself.

- **Config**: export your setup to a file, import someone else's, or keep named presets. Two
  ready-made configs, for players who run Firestone or Hearthstone Deck Tracker beside the toolkit,
  turn off what that program already shows.
- **BepInEx mods**: the mods from hsbg.cards, which of them you have installed and at which version.

Opponent ratings come from the hsbg.cards leaderboard. Players who are not on it show as `8000↓`.

## Languages

**Settings → Language** sets everything at once: card names, text and art, the words the search
understands (minion types, keywords, card types) and the toolkit's own menus and messages. Available
in English, German, Spanish (EU and Latin America), French, Italian, Japanese, Korean, Polish,
Portuguese, Thai, Simplified and Traditional Chinese. The search keeps understanding English, so
`taunt` and `Spott` both work in German. Corrections to the toolkit's own texts are welcome as edits
to `HsbgCardLookup/data/ui/<language>.json`.

A language's card data (about 1 MB) is fetched the first time it is used, and its card art card by
card as you look at them.

## Streaming with OBS

Add a **Window Capture** above your Hearthstone capture and pick
**[TantrumsToolkitStandalone.exe]: Tantrum's BG Toolkit - Overlay**. It needs no filter and no setting
in the toolkit.

## Updates

A bell next to the in-game search button tells you when a new version is out and links to its Setup.
The toolkit never downloads or installs a program by itself: updating is the same Setup as the first
install. Card data and art refresh automatically.

[Release notes](https://github.com/RTantrumR/Tantrums-Battlegrounds-Toolkit/releases)

## Privacy

The toolkit talks to two places, and only for these things:

- **hsbg.cards**: card data, card art, the list of mods and the notices in the bell are downloaded
  from it.
- **GitHub**: the check for a new release.
- **Match data, if you leave it on**: after each Solos match a short report goes to hsbg.cards for
  community statistics. It holds the lobby's heroes, minion types and places, your rating and your
  final board. It holds no names, neither yours nor your opponents'. The Setup asks about it on its
  own page, and **Settings → Match data** has the same switch: turn it off and nothing is sent.

The full text is hsbg.cards' [privacy policy](https://hsbg.cards/privacy#match-stats); see also its
[terms](https://hsbg.cards/terms#stats) and [about](https://hsbg.cards/about#stats) pages.

## Uninstall

Remove "Tantrum's Battlegrounds Toolkit" under Windows **Settings → Apps**. Your settings and the
card-art cache live in `%APPDATA%\TantrumsBattlegroundsToolkit\`; delete that folder too to remove
everything.

## Building from source

See [BUILDING.md](BUILDING.md).

## Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by
[SignPath Foundation](https://signpath.org/).

- Committers and reviewers: [RTantrumR](https://github.com/RTantrumR)
- Approvers: [RTantrumR](https://github.com/RTantrumR)

Releases are built from this repository by GitHub Actions ([build.yml](.github/workflows/build.yml)); only those
builds are signed. Privacy policy: see [Privacy](#privacy) above.

## License

[Apache License 2.0](LICENSE). See [NOTICE](NOTICE) for third-party components.

MMR showcase is inspired by [HDT-BGMMRPlugin](https://github.com/Reign-in-blood/HDT-BGMMRPlugin) and was made in cooperation with the author.

Hearthstone is a trademark of Blizzard Entertainment, Inc. This is an unofficial, fan-made tool, not
affiliated with or endorsed by Blizzard.
