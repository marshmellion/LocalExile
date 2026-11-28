# Minion Unnerf

> **Needs:** nothing extra.  
> **Works with:** on its own (Body Auras can dress the spectres it makes).

Undoes some spectre and minion nerfs: spectre level and minion damage and life as before 3.19, Minion Damage Support as
before 3.15, and any monster's corpse can become a spectre, bosses too. Each part has its own setting.

## Commands

- `/spectre [name]`: raises the nearest corpse (or the nearest one with that name) as a spectre.

## Settings

Values are kept in `data\mods\minionunnerf.json`; the defaults are in `settings.json`.

- `enabled` (True): Minion Unnerf on
- **Spectres**
  - `spectres.oldLevel` (True): Spectre level as before 3.19. Gem level 20 makes level 76 spectres (now 70), gem level 25 makes 83 (now 78).
  - `spectres.extraLevels` (0): Extra spectre levels. Added to the spectre level after that.
  - `spectres.maxLevel` (100): Highest spectre level
  - `spectres.moreDamage` (0): Spectres deal more damage. A "more" multiplier on top of everything else.
  - `spectres.moreLife` (0): Spectres have more life
  - `spectres.anyCorpse` (True): Any monster can be a spectre. Every corpse can be raised, including monsters GGG made un-raisable.
  - `spectres.uniques` (True): Unique monsters too. Unique monsters and bosses can be raised. A few bosses may not act right as spectres.
  - `spectres.bosses` (True): Map and pinnacle bosses too. Off: unique monsters can be raised, but not map, Atlas or pinnacle bosses.
  - `spectres.commandIgnoresSpellEcho` (True): /spectre raises one spectre. The /spectre command's cast does not repeat with Spell Echo (which would raise a second corpse). Your own Raise Spectre casts are unchanged.
- **Other minions**
  - `minions.oldScaling` (True): Minion damage and life as before 3.19. 3.19 cut the damage and life of minions (not spectres) by about 20% at gem level 20. This gives it back.
  - `minions.moreDamage` (25): More minion damage. 25% more makes up for 20% less. Animate Guardian, Animate Weapon, Blink Arrow and Mirror Arrow are left out: 3.19 did not change their damage.
  - `minions.moreLife` (25): More minion life
- **Support gems**
  - `supports.oldMinionDamage` (True): Minion Damage Support as before 3.15. 25% to 49% more damage at gem levels 1 to 20 (now 25% to 39%).
  - `supports.noLifePenalty` (True): No "25% less maximum life" on Minion Damage Supports. 3.15 added it (the Awakened one has it too).
- **Kills**
  - `kills.minionKillsAreYours` (True): Minion kills count as your kills too. A kill by your minion (or your totem, trap or mine) also counts as yours: your on-kill effects, charges on kill and other "when you kill" bonuses happen for it. It still counts as the minion's kill.
