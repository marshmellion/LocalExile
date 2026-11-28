# Classic Harvest Crafts

> **Needs:** nothing extra.  
> **Works with:** Works on its own.

Chat commands for the Harvest crafts the game removed after 3.11, run on the item in the Horticrafting Station's slot.
The station's own list comes from the client's data and cannot be extended by a server mod, so the crafts are typed.

Works with LocalExile 0.5.2-beta.

## Commands

`<type>` is fire, cold, lightning, physical, life, defence, chaos, attack, caster, speed, critical or influence.

- `/hc`: the list.
- `/hc augment <type> [lucky]`, `/hc augment lucky`: add a modifier of the type (or any, with lucky values).
- `/hc remove <type>`: remove a random modifier of the type.
- `/hc removeadd <type>`: remove a modifier of the type and add a new one.
- `/hc removenon <type>`: remove a modifier that is not of the type and add one that is.
- `/hc randomise <type>`: reroll the values of the modifiers of the type.
- `/hc lucky prefixes|suffixes|all`: reroll values with lucky rolls (`all` includes implicits).
- `/hc reforge <type> [more]`: reforge as a rare item with a modifier of the type (`more`: the type is more common).
- `/hc reforge keepprefixes|keepsuffixes|best`: keep one side, or the best of ten reforges.
- `/hc upgrade magic 2|3|4 [high]`, `/hc upgrade normal 1|2`, `/hc upgrade best`.
- `/hc fracture [prefix|suffix]`.
- `/hc cost`: the current cost setting.

## Settings

- `costMultiplier` (default 1): multiplies each craft's lifeforce cost; 0 makes them free. The base costs and the
  lifeforce colour per type are this mod's own, as the original crafts had no lifeforce cost.

## Not from the game

"More common" reforges keep the best of three reforges, "lucky" keeps the higher of two rolls per value, and
"high-tier" picks only the highest-level modifier of each kind. The original weightings are not known.

## Publishing

The crafts are GGG's own removed content. LocalExile's rules may place them in `Modules/Legacy/` (a module a league
lists) rather than in LocalExile-Mods.
