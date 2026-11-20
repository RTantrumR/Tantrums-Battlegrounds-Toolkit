v1.0.1

### In the match
- A bell now sits left of the search button. A green number means a new version of the toolkit is out, a yellow one means new patch notes. Click it for a small menu: download the update, skip it, or open the patch notes in your language. Read items stay in the menu, so you can still open a link after the match.
- The bell replaces the "Update available" tag above the search button and the bell inside the card search.
- A pinned opponent board has a resize arrow in its top-right corner while the cursor is on it. Drag it and the board grows or shrinks around the middle of its top edge. To move the board, use "Position the board" in Settings as before.
- After two panels join, a short hint under them says how to undo it: right-click the panel.

### Settings
- Portrait/Sidebar overlay: two size sliders for what is drawn by the leaderboard portraits, one for the name and MMR plate and one for the tavern tier and minion type icons, 70% to 135% each. A picture of the leaderboard under them shows the result as you drag. In duos the icons stop at 120%, where teammates' icons would touch.
- General: the lobby minion types box can be laid out as one row (1 × 5), one column (5 × 1), two rows (2 × 3) or three rows (3 × 2).
- General: a line under the "Assemble panels" picture says that a right-click takes an assembled panel apart.
- New "BepInEx mods" page with two tabs. Catalogue lists the mods from hsbg.cards with a short description, which of them you have and at which version, and a Download button that opens the mod's Setup in your browser. Installed lists the mods in Hearthstone's BepInEx folder: show one in Explorer, check it for an update, or move it to the Recycle Bin.
- The toolkit now asks hsbg.cards and GitHub for news at most every two hours, and remembers the answer across restarts.
- A "Website" button at the bottom of the main page opens hsbg.cards in your language.

### Standalone program
- The overlay no longer draws over Hearthstone's friends list or its Esc menu. While either is open, that part of the screen belongs to the game, and the toolkit's elements there disappear until it closes.

### Setup
- The Setup and the toolkit's own files now name the toolkit's author (TantrumUA) and publisher (hsbg.cards) and carry a signature. Windows still shows "Unknown publisher": the signature comes from our own certificate, which no authority vouches for.
- The Setup is larger (about 16 MB) because its files are no longer packed. Some antivirus scanners read the packed version as suspicious.
