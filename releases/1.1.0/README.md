v1.1.0

### In the match
- New Session Recap box: your rating when the session started and now, and your latest matches with the hero, the place and the rating change. It shows during a match and on the Battlegrounds menu, and follows Solos or Duos, whichever you play. A session ends after 4 hours without a ranked match; right-click the box to start a new one yourself.
- New default layout: the lobby minion types, the standings sidebar and the Session Recap stand as one column in the strip left of the leaderboard, from under your opponent's name to above yours. Tavern tiers and minion types sit by the portraits.
- The sidebar and the Session Recap can put each row on two lines, the name on the first and its numbers under it: narrower boxes, larger text. When the sidebar shows only names and ratings, each player still fits on one line.
- A name too long for the sidebar or the Session Recap is cut off with "…" instead of being written smaller.
- The lobby minion types box draws its icons and names larger in the same width.
- Hold any box of an assembled panel to join the whole panel to another box.
- Fixed: a box that had never been on screen could save a broken position, after which the assembled boxes could no longer be dragged.
- Fixed: with Hearthstone on a second monitor, a panel's right-click menu opened on the first one.
- A bell now sits left of the search button. A green number means a new version of the toolkit is out, a yellow one means new patch notes. Click it for a small menu: download the update, skip it, or open the patch notes in your language. Read items stay in the menu, so you can still open a link after the match.
- The bell replaces the "Update available" tag above the search button and the bell inside the card search.
- A pinned opponent board has a resize arrow in its top-right corner while the cursor is on it. Drag it and the board grows or shrinks around the middle of its top edge. To move the board, use "Position the board" in Settings as before.
- Right-click the lobby minion types box, while it stands on its own, to pick its layout and look on the spot.
- The lobby minion types box can also show the banned types: the season's types that are not in this lobby, crossed out next to the five that are. Off by default.
- The box has three looks: icons with names, icons alone, or names alone with each type in its own colour.
- After two panels join, a short hint under them says how to undo it: right-click the panel.
- The standings sidebar keeps the same width while players' names come in, so a box joined to it no longer keeps resizing. Switch a part of the sidebar on or off and the box joined to it follows, narrower as well as wider.
- Joined panels keep the size you gave them by hand while players load into the lobby, and in the next match.

### Settings
- Language: Ukrainian. The menus, the panels and the Setup are in Ukrainian; cards, card text and the search stay in English.
- Config page: a new ready-made config, "Default Standalone (Recommended)", first in the list, sets the toolkit's own default layout and features. The Firestone and Hearthstone Deck Tracker configs now start from it and then turn off what that app already shows. Your hotkeys are kept.
- In-match panels: switches for the Session Recap (on or off, the latest matches, how many of them: 1 to 10), "Joined boxes" (stretch to one width, scale everything up, or keep the sizes you gave them) and "Two-line rows".
- The BepInEx mods page lists Tantrum's Board Fix, which keeps your board from jumping around in the Battlegrounds shop.
- Portrait/Sidebar overlay: two size sliders for what is drawn by the leaderboard portraits, one for the name and MMR plate and one for the tavern tier and minion type icons, 70% to 135% each. A picture of the leaderboard under them shows the result as you drag. In duos the icons stop at 120%, where teammates' icons would touch.
- The General page is now two: "In-match panels" (turn counter, lobby minion types, joining the boxes), which the window opens on, and "Card search" (hotkeys, the in-game button, floating cards, the card art folder). "Start with Windows" moved to the Updates page, now "App & updates".
- In-match panels: the lobby minion types box can be laid out as one row (1 × 5), one column (5 × 1), two rows (2 × 3) or three rows (3 × 2), with a picture of the box that follows the layout, the look and the banned types as you change them.
- In-match panels: a line under the "Assemble panels" picture says that a right-click takes an assembled panel apart.
- More pictures instead of words, drawn over a real game: the turn counter, hovering and pinning an opponent's board (the game's own leaderboard, the board as the toolkit draws it, a minion's big card), opening the search and dragging a card out of it, and "Assemble panels" with the real boxes: a one-row lobby types box joining the sidebar's top, then a one-column one with the banned types joining its side.
- The Trinket HUD and Anomaly HUD pages show their sample card at full size while Hearthstone is closed. In the standalone program it used to shrink to a speck.
- New "BepInEx mods" page with two tabs. Catalogue lists the mods from hsbg.cards with a short description, which of them you have and at which version, and a Download button that opens the mod's Setup in your browser. Installed lists the mods in Hearthstone's BepInEx folder: show one in Explorer, check it for an update, or move it to the Recycle Bin.
- The toolkit now asks hsbg.cards and GitHub for news at most every two hours, and remembers the answer across restarts.
- A "Website" button at the bottom of the main page opens hsbg.cards in your language.
- The in-game search button and the bell can be moved and resized like the other boxes: the search button's Arrange sits on its own line on the Card search page, the bell's on App & updates. A bell you never moved stays left of the search button, wherever that goes.
- Arrange with Hearthstone closed starts the game through Battle.net (and Battle.net too, if needed), then begins the arrange once the game is up.
- Config page: "Ready-made configs" for players who run Firestone or Hearthstone Deck Tracker beside the toolkit. Apply one and the toolkit turns off what that app already shows, so nothing appears twice.
- Tooltips in the settings, the card search and over the game are drawn in the toolkit's own style and wrap long text, instead of Windows' small plain box.

### Standalone program
- The first start after updating from an older version opens the settings once, on the Config page, with the new default layout at the top of the ready-made configs.
- The Language page no longer has an "Automatic" row: it followed Hearthstone Deck Tracker's language, which the standalone program does not have. A language you never picked stays English.
- After a first install the settings open by themselves, once, on the Config page: if you run Firestone or Hearthstone Deck Tracker too, its ready-made config is the first thing to apply.
- When the settings close, they fold into the toolkit's logo, which flies to its icon in the Windows tray: that icon is where they open from again.
- Click the toolkit's tray icon to open the settings, also while it waits for Hearthstone. A double click still opens the card search.
- The tray icon's right-click menu is drawn in the toolkit's dark colours instead of Windows' light one.
- The overlay no longer draws over Hearthstone's friends list or its Esc menu. While either is open, that part of the screen belongs to the game, and the toolkit's elements there disappear until it closes.

### Match data
- After each Solos match the toolkit sends a short report for community statistics on hsbg.cards: the lobby's heroes, minion types and places, your rating and your final board. No names are in it, neither yours nor your opponents'.
- The Setup asks about it on its own page, ticked by default. Settings has the same switch on its own "Match data" page; turn it off and nothing is sent.
- The toolkit keeps a record of your last 200 matches in its data folder.
- A "Community stats" button on the Match data page opens hsbg.cards/stats/heroes: what everyone's shared matches add up to.

### Setup
- The Setup and the toolkit's own files now name the toolkit's author (TantrumUA) and publisher (hsbg.cards) and carry a signature. Windows still shows "Unknown publisher": the signature comes from our own certificate, which no authority vouches for.
- The Setup installs the standalone program only. Hearthstone Deck Tracker stops running plugins on November 30, so the "Install as HDT plugin" choice is gone. If the HDT plugin is still switched on, the Setup says so, closes HDT and switches the plugin off so the toolkit doesn't run twice; on any other PC it no longer mentions HDT at all. The toolkit language page lost its "Same as Hearthstone Deck Tracker" row.
- The Setup is larger than before (about 11 MB) because its files are no longer packed. Some antivirus scanners read the packed version as suspicious.
