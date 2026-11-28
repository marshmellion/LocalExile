# Cosmetic Drops

> **Needs:** the Exciting build of LocalExile (github.com/marshmellion/LocalExile, branch exciting).  
> **Works with:** Body Auras (body auras drop too and are locked until found).

With this mod on, an account no longer owns every cosmetic (microtransaction) and hideout. It owns what it unlocked,
and the rest drop.

- **Target farming:** each cosmetic drops from one source that fits it, as a **Sealed Cosmetic** (with the Mirror's
  drop sound). A right click unlocks what's inside. Examples: Sirus's from Sirus, Kitava's from Kitava, Delirium's
  from Delirium monsters, Legion's from Legion encounters. Anything without a theme drops in one of 15 map tilesets
  (tombs, jungles, deserts, coasts...), with a whole set always in the same place.
- **Cosmetic Caches:** a cache drops from monsters, bosses and strongboxes. A right click unlocks a random cosmetic you
  don't own yet.
- **Hybrid** (the default) does both.
- **Never dropped** (unless turned on in the ownership settings): mystery boxes, the shop's upgrade and convert
  scrolls, and the "Premium Expired Pet" cosmetics.
- **Other mods' cosmetics:** a mod folder beside this one with a `cosmeticdrops.json` (a `cosmetics` list in
  `cosmetics.json`'s form) adds its cosmetics to the drops, once they are in the game data (Body Auras' body auras drop
  where their weapon effects do).

When one unlocks: the game's own reward pop-up on the right of the screen with the cosmetic's picture, as a league
challenge reward gets ("New cosmetic: <name> (Complete)", "<name> unlocked!"), a chat line, a pop-up with a chime on the
QoL overlay, and the game's new-cosmetic flag. A hideout gets the game's "Challenge Complete!" banner. Both come from
hidden challenge rows in `content/cosmeticnotices.json` (a dead 2020 event's set, never shown in the Challenges panel)
and need the game's challenge notifications on in its options.

## What stays yours

- The masters' hideout decorations, the dances, and the default and campaign hideouts.
- The invisible effects (Invisible Buff Effect, Invisible Flask Effect), with 100 copies each.
- Pets and weapon effects come with 2 copies when they unlock; hideout decorations come 5 to 20 at a time and add up
  (to 127).
- From caches, pricier cosmetics are rarer (by the shop's price; supporter-pack pieces count as expensive).
- Cosmetics you wore that you don't own are only hidden; turning the mod off brings them back.
- Decorations already placed in your hideout stay where they are.
- Your unlocks are saved per account in `data/moddata/cosmeticdrops/saves/` (beside the game's other data, not in the
  mod's folder: a file changing there would reload the mod).

League content the server doesn't run yet (Sanctum, Ultimatum, Ritual...) drops its cosmetics in a fitting tileset
instead. 6,520 cosmetics and 93 hideouts are in `cosmetics.json` (made by `tools/make_cosmetics.py`).

## Commands

| Command | Does |
| --- | --- |
| `/cosmetics` | How many you own |
| `/cosmetics where <name>` | Where a cosmetic or hideout drops |
| `/cosmetics sources` | Every source and how many cosmetics it has |
| `/cosmetics source <id>` · `missing [id]` | What a source has and what you're missing (`missing` alone: this map's tileset) |
| `/cosmetics owned [words]` | What you own |
| `/cosmetics give <name>` · `give all` | Unlocks one, or everything (test) |
| `/cosmetics sealed <name>` | A Sealed Cosmetic with that inside (test) |
| `/cosmetics cache [n]` | Cosmetic Caches (test) |
| `/cosmetics lock <name>` · `lock all` | Locks again (test) |

## Settings

- The mode.
- The chances per monster rarity, bosses, strongboxes and league monsters.
- A multiplier per source, and moving any cosmetic or set to another source.
- Only cosmetics you don't own yet.
- Sealed item or instant unlock.
- What is locked (bought hideouts, found hideouts, decorations, mystery boxes, the offline shop's grants).
- The copies of pets, weapon effects and invisible effects.
- Always-owned names and kinds.
- Each notice.
- The test commands.

Needs the modding API's `GameEvents.OwnedCosmetics` and `OwnedHideouts`. The strongbox drops need the private build's
`GameEvents.ChestDrops`.
