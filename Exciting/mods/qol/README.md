# Quality of Life (private)

> **Needs:** nothing extra; on the Exciting build of LocalExile (github.com/marshmellion/LocalExile, branch exciting) its bigger stacks, price popups and currency tab additions work too.  
> **Works with:** Exciting Items, Live Economy (prices) and the QoL client mod (panels inside the game, NativeMods).

For the private play copy only. Buttons and keybinds come from the overlay (`overlay/`), which the mod starts with the
server; everything also works as a chat command.

| Action | Overlay | Key (default) | Command |
|---|---|---|---|
| Bring the loot your filter shows to your feet | Loot | F5 | `/qol loot` |
| Sort the inventory | Sort | F6 | `/qol sort` |
| Deposit the inventory into the stash | Deposit all (stash open) | F7 | `/qol deposit` |
| Price popups on picking up currency, on/off | Prices | F8 | `/qol prices [on/off]` |
| Hide or show the overlay | | F9 | |
| Sell everything in the stash tab shown (press twice) | Vendor tab (normal, premium, quad tabs) | | `/qol vendor` |
| Three maps of a tier into one of the next | Upgrade maps (map tab shown) | | `/qol maps` |

- **Deposit all** goes: special tabs (currency, fragments, essences, maps, uniques…), then tabs you gave the item's
  affinity, then tabs named after the item's kind, then the tab shown. Name a normal, premium or quad tab `Boots`,
  `Gloves`, `Wands`, `Axes`, `Weapons`, `Armour`, `Jewellery`, `Jewels`, `Flasks`, `Gems`… (several with `/`:
  `Boots / Gloves`). Charts and Dead Man's Sulphur go into the Chart Locker on the Sovereign's deck first (settings
  `deposit.chartLocker`, `deposit.sulphurToLocker`). The Chart Locker's affinity tick is kept (the server refused it), and
  with it on a Ctrl + click on a chart puts it in the locker. A tab named `Allflame` takes the league's charts and Allflame embers
  (`Charts` or `Embers` takes one kind), which no special tab holds. Portal Scrolls and Scrolls of Wisdom stay (setting `deposit.keep`).
- **Unique copies:** a unique the unique tab already holds goes to a quad tab `Unique Copies` (made the first time).
  When you take the shown one out of the unique tab, the next copy moves into its place.
- **Loot** reads the filter chosen in the game's options, from `Documents\My Games\Path of Exile`. Items it hides stay
  where they are; gold always comes. A filter condition the reader does not know never matches.
- **Prices** use the rates the live economy mod saved (Faustus). The popup shows the stack's worth in divines from one
  divine up, otherwise in chaos, with the game's Chaos or Divine Orb picture (`prices.popupIcon` `gold`: the gold coin); nothing below 1 chaos. A pickup worth less than that gets a line on the overlay
  instead (to the cent), which floats up from beside your character and fades away, with no box (`prices.style` box
  brings back the old box). A pickup that gets the popup gets no overlay line (`prices.overlayWithPopups` shows both). The Prices switch
  is kept per character, across areas and (with `prices.remember`) across sessions.
- **Stash buttons** sit in a row under the stash grid, above Highlight Items, out of the way of the tab list's folders
  (`stashButtons` moves them, or puts them back beside the stash).
- **Stacks:** every stackable item stacks to 65,000 (`stackSize`; the client cannot show more than 65,535). Divination
  cards and items that become another item at a full stack (shards, splinters) keep their size. The special stash tabs'
  slots still hold 5,000. Applied by the content patcher on the next start.

The overlay needs the game in windowed or borderless windowed mode. Keys are in `overlay/qol-overlay-keys.json`
(names like `F5`, `Ctrl+Q`, `Shift+Alt+L`). If the overlay does not appear, run `overlay/Start QoL Overlay.cmd` and
look for `overlay/qol-overlay.log`.

With the in-game QoL panels (the NativeMods client mod in `Mods\Client\qol`), the overlay hides itself while they run
and comes back when they stop, for example with NativeMods off or when the panels did not load
(`overlayWithInGameUi` keeps it shown alongside them).
