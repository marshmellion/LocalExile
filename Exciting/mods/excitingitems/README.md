# Exciting Items

> **Needs:** the Exciting build of LocalExile (github.com/marshmellion/LocalExile, branch exciting).  
> **Works with:** Quality of Life (its gem panel lists the alternate quality gems) and any loot filter (HasEnchantment "Exciting").

Rare chances on magic, rare and unique drops, each with its own chance and all able to land on the same item:

- **Unique implicits.** The item gets one, two or three modifiers of unique items as extra implicits. A modifier of a
  rarer unique comes up as seldom as that unique drops.
- **An empowered modifier.** One of its modifiers, implicit or explicit (a unique implicit from above included), rolls 10-100% higher than it did (a random amount in the
  range the settings give), past the top of its tier. A modifier with a downside, or a switch such as "has no
  sockets", is never chosen.
- **An extra fractured modifier.** The item gets one modifier more than it could have: a rare is filled to three
  prefixes and three suffixes and gets a seventh (a rare jewel a fifth), a magic item a third, a unique one rare-style
  prefix or suffix. Every modifier of that side, prefixes or suffixes, is fractured (on a unique, the one added).
  These modifiers can also be ones only an influenced item rolls (one influence the item does not have, at the tiers its
  item level allows), without the item becoming influenced.

They apply to influenced items too: the game's crafting rules against fracturing influenced or synthesised items are
for currency, not for drops. An item with any of them drops with the Exciting marker, an enchantment of this mod's own
with no text, which loot filters see also while the item is unidentified (`HasEnchantment "Exciting"`), and a rule put
at the top of your loot filters shows it with a beam and a sound. The marker is new game data, in `content/marker.json`:
it needs the content patcher run on the play client (`content-patch.cmd apply`); without it the item drops synthesised,
as before, and the rule matches `SynthesisedItem True`. Chat names which effects a drop got, not what
they are: identify it to see.

## Which unique modifiers can come

`implicits.mode` picks one:

| Mode | Gets |
|---|---|
| Wild (default) | any unique modifier on any gear |
| Sanity | only modifiers of uniques of the same kind of item: swords' on swords, belts' on belts |
| Thematic | offence on weapons, quivers and gloves; defence on body armours and shields; speed and ailments on boots; defence and scaling (per charge, per stat, auras, minions) on helmets; utility and odd unique effects on rings, amulets and belts |

In every mode a modifier is welcome on its own kind of item (a helmet unique's on helmets), and every modifier not blacklisted can come somewhere: one the chosen mode leaves without a slot that drops follows Wild's rules, and one of a kind of item that never drops (a fishing rod's) can go on any gear. Rare and magic jewels get unique jewels' modifiers in every mode, and gear never gets those. A modifier that names a
kind of item or is local to it (adds to the item's own armour, its sockets) stays on that kind of item. Cluster jewels
and Timeless Jewels get nothing.

`blacklist` lists modifiers never used: a Mods id or its start, `stat:` and a piece of a stat id, or `class:` and a
kind of item whose uniques' modifiers stay with it. The defaults are the modifiers tied to their own item: radius
jewels' and Timeless Jewels' tree effects, Thread of Hope's ring, granted supports chosen by index (Forbidden Shako),
effects only that unique has (`local_unique_` stats), Tabula Rasa's and level-requirement modifiers, and flasks',
tinctures', maps', idols' and Sanctum relics' modifiers (`class:`). Every other unique modifier can come on some kind
of item in every mode.

## Commands

| Command | Does |
|---|---|
| `/exciting` | the chances and the mode |
| `/exciting next implicit [1-3]` | the next magic, rare or unique drop gets that many unique implicits |
| `/exciting next empowered` | the next one gets an empowered modifier |
| `/exciting next extra` | the next one gets an extra fractured modifier |
| `/exciting next all` | the next one gets all three |

## Settings

| Setting | Default | Does |
|---|---|---|
| `implicits.chance`, `chanceTwo`, `chanceThree` | 1000, 100000, 1000000 | one, two or three unique implicits: 1 in this many drops |
| `implicits.mode` | wild | wild, sanity or thematic (above) |
| `implicits.rarityWeights` | on | rarer uniques' modifiers come up less often |
| `implicits.rarities`, `empowered.rarities`, `extraModifier.rarities` | magic, rare, unique | which drops can get each effect |
| `empowered.chance` | 500 | 1 in this many drops |
| `empowered.percent` | 10-100 | how much stronger, picked at random |
| `extraModifier.chance` | 1000 | 1 in this many drops |
| `extraModifier.influencedMods` | on | the extra modifier can also be an influenced item's modifier (the item stays uninfluenced) |
| `blacklist` | (above) | modifiers never used as implicits |
| `mark`, `showInFilters` | on, on | the Exciting marker (or the synthesised look), and the loot filter rule |
| `announce` | on | the chat line |
| `dropSounds` | only the filter's alert sounds | the game's own drop sounds (currency, cards, maps, gems, uniques) play even for hidden items: `alertsOnly` keeps only the sounds your filter sets, `shownOnly` silences hidden items, `game` changes nothing. It adds `DisableDropSound True # excitingitems: dropSounds` lines to the filters (and the (Exciting) copies of online filters), taken out again when the setting changes |
| `filterFolder` | (empty) | the loot filter folder; empty is Documents\My Games\Path of Exile\LocalExile |

The loot filter rule goes between two marker lines at the top of every `.filter` file in the folder (by default
`Documents\My Games\Path of Exile`, where the game lists filters), and comes out again when the setting is off. Without
a filter of your own the mod writes one that holds only the rule. Online filters (followed on pathofexile.com, kept in
`OnlineFilters`) are rewritten by the game, so the mod leaves them alone and writes a copy of each beside your filters,
`<its name> (Exciting).filter`, with the rule on top; it makes the copies again at every server start, so they follow
the online filter's updates. Pick the copy in the game's Options to get the highlight.

Formerly Unique Implicits: its loot filter rule and filter file are taken out when this mod loads.

Works with LocalExile 0.5.2-beta and its main branch; it needs no change in LocalExile.

## Radiant Orb (private version)

A currency of this mod's own: right-click it, then a magic, rare or unique item. The item gets one result, picked by
weight (settings `radiantOrb.oneImplicit` 300, `twoImplicits` 15, `threeImplicits` 1, `empowered` 300, `extra` 300;
0 turns a result off), and the Exciting mark. It works on corrupted, mirrored, fractured, synthesised and influenced
items (`radiantOrb.anyItem`); an already radiant item is refused and nothing is spent. It goes in the currency tab's
spare slots. `/exciting orb [n]` puts some at your feet.

## Legacy (formerly the Legacy Restored mod)

Merged into Exciting Items: its settings are the "Legacy" group. Imprints saved by Legacy Restored are kept.
New: Enchants can be set to drop only in the Labyrinth, with a chance per Labyrinth chest.

Things Path of Exile removed, back for your own play:

- **Labyrinth enchantments.** The Enchant currency drops. Use it on a helmet, gloves, boots or belt for a random
  Labyrinth enchantment of the chosen Labyrinth, which replaces the one it has. A helmet enchantment that names a skill
  works on that skill.
- **Alternate quality gems.** Anomalous, Divergent and Phantasmal gems as they were in 3.22 (442 gems, 974 quality
  types) drop from any monster or only from unique ones, and Prime and Secondary Regrading Lenses change a gem to
  another type. The gem's tooltip cannot show the alternate quality: a Shaper (Anomalous), Elder (Divergent) or
  Crusader (Phantasmal) look tells them apart, and `/exciting gems` lists them.
- **Eternal Orbs.** They drop, ten times rarer than a Divine Orb. One makes an Imprint of an item; the Imprint, used on
  that item, puts it back as it was.
- **Unnerfs**, each on by default and off in the settings: Cyclone as in 3.7, its area growing with its stages.

## Commands

| Command | Does |
|---|---|
| `/exciting legacy` | what is on |
| `/exciting gems` | your alternate quality gems, in the backpack and socketed |
| `/exciting imprints`, `/exciting restore <n>`, `/exciting forget <n>` | imprints kept as a list, on a server that cannot send Imprint items |
| `/exciting use enchant\|eternal\|imprint\|lens\|lens2` | uses that currency on the item in the backpack's top-left cell |
| `/exciting give eternal\|enchant\|lens\|lens2 [n]`, `/exciting give gem <anomalous\|divergent\|phantasmal> <gem> [level]` | test items at your feet (the `testCommands` setting) |

## Settings

The settings are in four groups, `unnerfs`, `eternalOrbs`, `enchants` and `altGems`, each with a switch and its drop
chances; `settings.json` describes each one.

## What it needs from LocalExile

Changing one skill's stats, a currency used on an item and the Imprint item are not handed to mods by LocalExile yet.
This mod uses three hand-overs a local copy of the server adds (`LocalHooks`: a skill's stats, its supports, a currency
used on an item) and the Imprint item's data; without them it loads with its drops only and says so in `/exciting legacy`.
To be published, those become general hooks in a LocalExile pull request. Because the mod brings back GGG's own past
content, LocalExile's rules may place it in `Modules/Legacy/` as a module instead.

Data: `altquality.json` is RePoE's 3.22 export (gems' quality stats); `conversions.json` the 3.22 skill stat conversions
the current client no longer lists.

## Limit Break Orb (private build)

A currency of this mod's own. Used on gear, it adds one socket above what the item could have, up to 6 (settings: `limitBreakOrb`). The game's own socket limits are left alone, so this orb is the only way past them. Drops 1 in 3,000 normal monsters by default. `/exciting orb limit [n]` puts some at your feet.

## Krangle Orb (private build)

A currency of this mod's own: the Blood Crucible / Dream Furnace transform. The item or map becomes corrupted and gets one Scourge upside and one downside (tier 1 to 3 by item level, tier 4 at 1 in 8 at item level 68+), replacing any it had (settings: `krangleOrb`). `/exciting orb krangle [n]`.

## Legacy affixes

Modifiers that rolled in an older patch and no longer do, taken from 3.9 to 3.19 patch data (`legacymods.json`; 526 affixes, each with the item tags it rolled on then). A magic or rare drop gets one at 1 in 150 by default, and the Legacy mark: the loot filters show it with an orange border and beam. It does not make the item exciting. `/exciting next legacy` makes the next drop that can take one get one.

## Recombinators (private build)

The Sentinel league's Armour, Weapon and Jewellery Recombinators, as strong as in 3.18. Use one on an item in your inventory, then on a second item of the same class: both are destroyed and one is made from their properties. Either base at 50%, item level averaged, the modifier counts of the old table, no limits on exclusive, crafted, fractured or off-base modifiers, about 1 in 50 results gets one of the 52 Sentinel-only modifiers, and tiers can shift. Every later nerf is a switch (settings: `recombinators`), off by default. `/exciting orb armour|weapon|jewellery [n]`.

## Defiled Vaal Orb (private build)

Corrupts a corrupted item a second time: gems can reach two levels over their cap or have their quality moved again, gear can get a second corrupted implicit or another Vaal outcome, and maps, jewels and flasks that dropped corrupted get a Vaal Orb's outcome. Drops in Vaal side areas, in corrupted maps and from corrupted strongboxes (settings: `defiledOrb`). `/exciting orb defiled [n]`.

## Reflecting Mist (private build)

The server had no use for Reflecting Mist: it now splits an item into two mirrored halves with stronger modifiers, positive on one and reflected on the other (settings: `reflectingMist`: drop chance and source, the effect range, which items, corrupted/mirrored/influenced). `/exciting orb mist [n]`.

## Jackpots

Every exciting roll is its own, so one item can get all of them. A jackpot also gives 2, 3 or all 4 effects at once (a unique implicit, an empowered modifier, an extra fractured modifier, a legacy affix): 1 in 20,000, 1 in 200,000 and 1 in 2,000,000 drops by default (settings: `jackpot`). `/exciting next jackpot [2-4]`.

## Custom implicits (private build)

636 new implicits built from effect rules: a trigger (on hit, on crit, on kill, when hit, on block, on flask use, on skill use ...) and an effect (a buff, a charge, a debuff or ailment, a curse, a timed boost such as "15% increased Movement Speed for 4 seconds when Hit", recovery, reflected damage, removing an ailment from yourself). Each gear slot has about 100 of its own: damage on weapons and gloves, movement and cleansing on boots, buffs and curses on helmets and shields, defences on body armours, flasks on belts. Three tiers by item level. A roll of their own (1 in 1,500 by default), a Radiant Orb outcome, and one of a jackpot's effects (settings: `customImplicits`). `tools/make_custom_implicits.py` makes the pool: `content/customimplicits.json` (stats, text, modifiers and boost buffs for the client) and `customimplicits.json` (the rules). `/exciting next custom`.

How they are built: the game's stat list is fixed in the client, so custom implicits add no stats. Every one rides on a
pair of existing item stats that nothing else uses: one says which implicit it is (1 to 850), the other holds its roll.
Their text is one block added to the stat descriptions; their rules are item rules that check that number (flask
"during effect" ones only while the flask runs). So an item holds one custom implicit (`most` is 1).
## Legacy uniques

Old versions of uniques (Kaom's Heart before 1.0.0, Bramblejack before 3.19.0 ...) and uniques the game removed, from Path of Building's records: `legacyuniques.json` (819 uniques, 692 with old versions, 163 removed; `tools/make_legacy_uniques.py` makes it). Bosses drop one at 1 in 25 by default; the version is the oldest, the newest old one, a random one, or the one named for that unique in the settings (settings: `legacyUniques`). They get the Legacy mark. `/exciting legacyuniques <name>` lists a unique's versions; `/exciting legacyunique [name] [version]` gives one.

## Legacy currencies

Currencies the game no longer drops, each with its own chance from monsters and from strongboxes (settings: `legacyCurrencies`, a name to 1 in X): Orb of Binding, Ancient Orb, Orb of Horizons, Harbinger's Orb, Exalted, Annulment and Mirror Shards, the nine Tainted currencies and the Orb of Dominance; any other currency can be added by name. The server already uses most of them; the private build gives the rest a use: Orb of Horizons (another map of its tier), Harbinger's Orb (a map of the next tier), Tainted Mythic Orb (a corrupted rare becomes a corrupted unique), Tainted Divine Teardrop (a modifier of a corrupted item moves a tier) and Orb of Dominance (elevates an influenced modifier and removes another).

## Crucible trees (pre-rolled)

The client still has Crucible's weapon tree nodes (2,632, each a modifier, in five columns), but the server cannot send a tree to choose from (that would need the client's tree data and forge screen worked out), so a weapon or shield drop gets a path of nodes already allocated: one per column, 2 to 4 by default, for its kind of item and item level, now and then a notable. Their lines show as enchantments by default (settings: `crucible`). Also a Radiant Orb outcome. `/exciting next crucible`.

## Idols

Legacy of Phrecia's Idols from more places, each with its own chance: rare monsters 1 in 600, bosses 1 in 40, Rogue Exiles 1 in 15, strongboxes 1 in 25 (private build), normal and magic monsters off; in maps only by default (settings: `idols`). 1 in 100 has one of the 198 scarab effects as an implicit, which adds to the map beside the scarabs in the device. They need the server's Idols module in the league: `data/league-modules.json` `{"Standard": {"add": ["Legacy/Idols"]}}`, and `data/modules/Idols.json` `{"enableAtlasTree": true, "dropRate": 0}` keeps the Atlas tree and leaves the drops to this mod. `/exciting idol [n] [scarab]`.

## Flask implicits, exciting Idols, empowered on anything

- Flasks: 214 custom implicits of their own (settings: `customImplicits.flaskChance`, 1 in 300 flasks): on use ("Remove Bleeding when used"), during the effect ("During Effect, 15% increased Attack Speed"), during the effect on a trigger ("During Effect, 8% chance to Curse Enemies with Despair on Hit"), and 70 unique effects during the effect (Headhunter's "When you Kill a Rare monster, you gain its Modifiers for 60 seconds" and more), the rarer uniques' effects rarer (`rarityWeights`). Marked, so loot filters show them.
- Idols: 1 in 60 is exciting, with one of 182 map modifiers that change maps in fun ways (old leaguestones, 27 Atlas sextant modifiers with their old weights, so the better ones are rarer, and more) and the Exciting mark (settings: `idols`). Navali's prophecies were not modifiers, so they cannot come back this way.
- Empowered: also on flasks, Heist trinkets and Idols (1 in 1,000), and, drop only, gems: a level over the cap or 21-30% quality (1 in 20,000) (settings: `empowered`).
- The Idols module is on in Standard and every Allflame league (`data/league-modules.json`).

## Mirage

The server has no Mirage encounters, but the league's boss is in its data: The Black Barya opens Saresh, of the Weeping Black's arena (the map device's FaridunBoss recipe). It now drops from map bosses (1 in 50), rare monsters (1 in 3,000), strongboxes (1 in 150, private build) and Faridun Rogue Exiles (1 in 5). Saresh drops one of the 13 Mirage uniques (as likely as each drops elsewhere). Screams of the Desiccated gets two shrine buffs instead of always Acceleration: any two of its thirteen by default, or one offensive and one other as the game had (settings: `mirage`). `/exciting barya [n]`, `/exciting mirageunique [name]`.

## Unobtainable uniques acquirable?

295 uniques have no working source on the server yet: their league content, boss or card isn't built (Heist's Replicas,
Sanctum, the Trialmaster, the Incursion temple, Abyssal Depths, the Breach lords, Delve, Ancestor, race rewards and more).
`unobtainable.json` lists them. They drop instead: a unique that drops is one of these 1 in 25 times, bosses 1 in 40,
pinnacle bosses 1 in 5, Rogue Exiles 1 in 30, strongboxes 1 in 60 (private build). Each group can be switched off, and
there are a never-list and an also-list. One the server starts dropping normally leaves the list by itself.
`/exciting unobtainable [name]` drops one at your feet; `/exciting unobtainable list` counts them.

## Divination cards

`cardRate.dropChance` makes cards drop more often (3: a kill is three times as likely to drop one, in maps and in the
campaign), and `cardRate.perCardChance` makes one card likelier to be the one that drops (`"The Doctor": 5`). The
copies settings are something else: `cardRate.multiplier` and `perCard` make every card that drops come several times
(they also apply to chests and strongboxes, whose cards are rolled before the mod sees them).

## Ascendant jewels

Jewels with "Allocates <ascendancy passive>" implicits. Each source drops its own, with a separate "1 in N" for one,
two and three nodes and a lowest map tier: normal, magic and rare monsters, map bosses, pinnacle bosses, uber bosses,
rogue exiles, Izaro, Labyrinth chests (rolled when Izaro dies until the server has the treasure room), strongboxes and
bosses outside maps. Defaults: rares 1 in 10,000 (one node), map bosses 1 in 400 / 1,500, pinnacle bosses 1 in 40 / 150
/ 600, uber bosses 1 in 15 / 50 / 120, Izaro 1 in 60 / 250 / 1,000. Jewels that drop anyway can get nodes too (2% of
jewels, 1% of abyss jewels). While the jewel sits in an allocated socket of the passive tree, you have that node on any
class: its stats, buffs, auras and the skill it grants (the server's jewel-allocates part). Any ascendancy's notables can
roll: the normal ones, the Phrecia ones (their own nodes, in every league), Reliquarian and Luminary, and if you switch
them on, old unused nodes. A node that asks you to pick (the Ascendant's, Herald Specialty...) rolls as one of its
options, and the jewel names that option. The Phrecia Harbinger's companion summons stay out (only the Phrecia league
runs them). Chances, groups, weights, a never-list and small nodes are in the settings.
`/exciting ascendant [node or ascendancy] [1-3]` puts one at your feet; `/exciting ascendant list [filter]` shows what
can roll.
They are marked for loot filters (the Exciting enchantment, `ascendantJewels.mark`) and have an influence's look
(`ascendantJewels.influence`: all six by default, or one, `both` for Shaper and Elder, or `none`).
