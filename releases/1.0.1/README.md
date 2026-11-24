v1.0.1

### In the match
- A bell now sits left of the search button. A green number means a new version of the toolkit is out, a yellow one means new patch notes. Click it for a small menu: download the update, skip it, or open the patch notes in your language. Read items stay in the menu, so you can still open a link after the match.
- The bell replaces the "Update available" tag above the search button and the bell inside the card search.
- A pinned opponent board has a resize arrow in its top-right corner while the cursor is on it. Drag it and the board grows or shrinks around the middle of its top edge. To move the board, use "Position the board" in Settings as before.
- Right-click the lobby minion types box, while it stands on its own, to pick its layout and look on the spot.
- The lobby minion types box can also show the banned types: the season's types that are not in this lobby, crossed out next to the five that are. Off by default.
- The box has three looks: icons with names, icons alone, or names alone with each type in its own colour.
- After two panels join, a short hint under them says how to undo it: right-click the panel.
- The standings sidebar keeps the same width while players' names come in, so a box joined to it no longer keeps resizing. Switch a part of the sidebar on or off and the box joined to it follows, narrower as well as wider.

### Settings
- Portrait/Sidebar overlay: two size sliders for what is drawn by the leaderboard portraits, one for the name and MMR plate and one for the tavern tier and minion type icons, 70% to 135% each. A picture of the leaderboard under them shows the result as you drag. In duos the icons stop at 120%, where teammates' icons would touch.
- The General page is now two: "In-match panels" (turn counter, lobby minion types, joining the boxes), which the window opens on, and "Card search" (hotkeys, the in-game button, floating cards, the card art folder). "Start with Windows" moved to the Updates page, now "App & updates".
- In-match panels: the lobby minion types box can be laid out as one row (1 × 5), one column (5 × 1), two rows (2 × 3) or three rows (3 × 2), with a picture of the box that follows the layout, the look and the banned types as you change them.
- In-match panels: a line under the "Assemble panels" picture says that a right-click takes an assembled panel apart.
- More pictures instead of words, drawn over a real game: the turn counter, hovering and pinning an opponent's board (the game's own leaderboard, the board as the toolkit draws it, a minion's big card), opening the search and dragging a card out of it, and "Assemble panels" with the real boxes: a one-row lobby types box joining the sidebar's top, then a one-column one with the banned types joining its side.
- The Trinket HUD and Anomaly HUD pages show their sample card at full size while Hearthstone is closed. In the standalone program it used to shrink to a speck.
- New "BepInEx mods" page with two tabs. Catalogue lists the mods from hsbg.cards with a short description, which of them you have and at which version, and a Download button that opens the mod's Setup in your browser. Installed lists the mods in Hearthstone's BepInEx folder: show one in Explorer, check it for an update, or move it to the Recycle Bin.
- The toolkit now asks hsbg.cards and GitHub for news at most every two hours, and remembers the answer across restarts.
- A "Website" button at the bottom of the main page opens hsbg.cards in your language.
- The in-game search button and the bell can be moved and resized like the other boxes: "Position the search button" is on the Card search page, "Position the notifications bell" on App & updates. A bell you never moved stays left of the search button, wherever that goes.

### Standalone program
- After a first install the settings open by themselves, once, so you see what can be set up.
- When the settings close, they fold into the toolkit's logo, which flies to its icon in the Windows tray: that icon is where they open from again.
- Click the toolkit's tray icon to open the settings, also while it waits for Hearthstone. A double click still opens the card search.
- The tray icon's right-click menu is drawn in the toolkit's dark colours instead of Windows' light one.
- The overlay no longer draws over Hearthstone's friends list or its Esc menu. While either is open, that part of the screen belongs to the game, and the toolkit's elements there disappear until it closes.

### Setup
- The Setup and the toolkit's own files now name the toolkit's author (TantrumUA) and publisher (hsbg.cards) and carry a signature. Windows still shows "Unknown publisher": the signature comes from our own certificate, which no authority vouches for.
- The Setup is larger (about 16 MB) because its files are no longer packed. Some antivirus scanners read the packed version as suspicious.
