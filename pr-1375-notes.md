More detail than fits the description. The guide (`Agents/docs/dev/effect-rules.md`), the client findings (`Agents/docs/re/item-triggered-skills.md`) and the next steps (`Agents/roadmap/items-and-crafting.md`) are in the branch; this is the evidence and the data as it stands.

## Every rule in the branch

| Stat that turns it on | Event | Conditions | Effect | Note |
|---|---|---|---|---|
| `attack_trigger_on_hitting_bleeding_enemy_%` | hit | attack, bleeding | skill | Trigger this Attack when you Hit a Bleeding Enemy |
| `attack_trigger_on_kill_%` | kill | — | skill | Trigger this Attack on Kill, where the enemy died |
| `attack_trigger_on_melee_critical_hit_%` | hit | melee, critical | skill | Trigger this Attack on Melee Critical Strike |
| `attack_trigger_on_melee_hit_%` | hit | melee | skill | Trigger this Attack on Melee Hit |
| `attack_trigger_on_melee_hit_with_atleast_150_strength_%` | hit | melee; needs strength ≥ 150 | skill | Trigger this Attack on Melee Hit if you have at least 150 Strength |
| `cast_on_attack_use_%` | skill_use | attack | skill | Trigger this Spell when you use an Attack, where the attack was aimed |
| `cast_on_crit_%` | hit | critical | skill | Trigger this Spell when you deal a Critical Strike |
| `cast_on_gain_skill` | worn | — | skill at self | A skill that is on while its item is worn: as its effect_skills row has it (aura, pulse) |
| `cast_on_gain_skill` | worn | only BlindingAura | stats | Blinding Aura: nearby enemies are blinded while it is worn |
| `cast_on_hit_%` | hit | — | skill | Trigger this Spell when you Hit |
| `cast_on_skill_use_%` | skill_use | — | skill | Trigger this Spell when you use a Skill |
| `cast_when_cast_curse_%` | skill_use | curse | skill | Trigger this Spell when you Cast a Curse Spell |
| `cast_when_hit_%` | when_hit | — | skill at self | Trigger this Spell when Hit, around the character |
| `chance_to_cast_on_kill_%` | kill | — | skill | Trigger this Spell on Kill, where the enemy died (Stalking Pustule) |
| `chance_to_cast_on_kill_%_target_self` | kill | — | skill at self | Trigger this Spell on Kill (it targets its user: a nova around the character) |
| `chance_to_gain_onslaught_on_kill_%` | kill | — | onslaught | % chance to gain Onslaught for 4 seconds on Kill; onslaught_time_granted_on_kill_ms replaces the 4 seconds |
| `chance_to_gain_onslaught_on_kill_for_10_seconds_%` | kill | — | onslaught | % chance to gain Onslaught for 10 seconds on Kill |
| `chance_to_gain_onslaught_on_kill_for_4_seconds_%` | kill | — | onslaught | % chance to gain Onslaught for 4 seconds on Kill |
| `gain_adrenaline_for_X_seconds_on_kill` | kill | — | buff SentinelAdrenaline | You gain Adrenaline for X seconds on Kill (Death Rush) |
| `gain_onslaught_on_hit_duration_ms` | hit | — | onslaught | You gain Onslaught for X seconds on Hit |
| `onslaught_buff_duration_on_kill_ms` | kill | — | onslaught | You gain Onslaught for X seconds on Kill |
| `onslaught_on_crit_duration_ms` | hit | critical | onslaught | You gain Onslaught for X seconds on Critical Strike (Kongor's Undying Rage) |
| `skill_has_trigger_from_unique_item` | hit | melee; only FieryImpactHeistMaceImplicit | skill | Trigger Level X Fiery Impact on Melee Hit with this Weapon (any melee hit for now: a hit does not say which weapon made it) |
| `trigger_on_block_%_chance` | block | — | skill at self | Trigger this skill when you Block |
| `trigger_on_bow_attack_%` | skill_use | bow | skill | Trigger this skill when you Attack with a Bow |
| `trigger_on_hit_against_rare_or_unique_if_no_marked_enemy` | hit | rare_or_unique, !has_mark | skill | Trigger Level X <Mark> when you Hit a Rare or Unique Enemy and have no Mark |
| `trigger_on_hit_vs_frozen_enemy_%` | hit | frozen | skill | Trigger this skill when you Hit a Frozen Enemy |
| `trigger_on_ignited_enemy_death` | kill | ignited | skill | Trigger this skill when an Ignited Enemy is Killed (Ignition Blast), where the enemy died |
| `trigger_on_skill_use_from_chest_%` | skill_use | — | skill at self | Trigger this skill when you use a Socketed Skill. Fires for any skill use: there is no fact yet for a skill socketed in the item |
| `unique_mjolner_lightning_spells_triggered` | hit | attack | skill | The mutated Mjolner's Lightning Bolt on Hit. A guess from the mod's name: every attack hit, within the skill's cooldown |

## Every skill row (`effect_skills`), all `checked = 0`

| Skill | How | Art | Radius | Hits | What is guessed |
|---|---|---|---|---|---|
| `BitingBraidNova` | area | `BitingBraidNova` | the skill’s own | 1 | Caustic Retribution (Biting Braid). Art: the client's own. Damage per character level, a guess |
| `BoneNova` | area | `BoneSegmentExplode` | 18 | 1 | Bone Nova on hitting a bleeding enemy: the real one is a nova of 9 bone projectiles; here one hit around the target. Art and radius are guesses |
| `FieryImpactHeistMaceImplicit` | area | `InfernalCryOnHitExplodeEffect` | 15 | 1 | Fiery Impact (the heist maces' implicit): an attack with the weapon around the target. Art: Infernal Cry's on-hit explosion (the skill's action is that one); radius a guess |
| `FireBurstOnHit` | area | `PrismaticBurstFire` | the skill’s own | 1 | Essence of Hysteria. Art: a guess |
| `GoreShockwave` | area | `ShockwaveSlamPulseBlood` | the skill’s own | 1 | Gore Shockwave on melee hit with 150 Strength: an attack with the weapon, radius from the skill. Art: a guess |
| `IcicleBurst` | area | `FrostBombExplosion` | 18 | 1 | Icicle Burst on hitting a frozen enemy: the real one is a nova of 9 icicles; here one hit around the target. Art and radius are guesses |
| `IgnitionBlast` | area | `IgnitionBlastExplode` | the skill’s own | 1 | Art: the client's own for the skill; size unchecked. Its "% of the monster's maximum life as fire" part is not dealt yet |
| `LightningBolt` | area | `HeraldOfThunderStrike` | the skill’s own | 1 | Choir of the Storm, Voice of the Storm. Art: a guess (the tables name none for the skill) |
| `LightningBoltOnHit` | area | `HeraldOfThunderStrike` | the skill’s own | 1 | The mutated Mjolner's Lightning Bolt. Art: a guess, as Lightning Bolt's |
| `ShieldShatter` | area | `ShieldShatterExplosion` | 20 | 1 | Shield Shatter on block. Art: the client's own; radius 20 a guess (the stat set has none). Its 4 second added spell damage buff is not given yet |
| `SolartwineBlindingNova` | area | `SolartwineNova` | the skill’s own | 1 | Blazing Glare (Solartwine). Art: the client's own. Damage: its added fire damage per level taken as per character level, a guess |
| `StalkingPustule` | area | `VolatileDeadExplode` | 15 | 1 | An approximation: the real orb seeks an enemy before it bursts; here it bursts where the kill was. Art: Volatile Dead's burst, a guess |
| `StormCascadeTriggered` | area | `StormCallImpact` | 8 | 6 every 0.08s within 8 | Storm Cascade on attack: the real one is 6 spikes in a line toward the aim; here 6 hits around the aim. Art and sizes are guesses |
| `TentacleSmash` | area | `ElderTentacleSlamImpact` | 14 | 1 | Tentacle Whip on kill: an attack with the weapon where the enemy died. Art and radius are guesses |
| `TriggeredExplosion` | area | `FireNovaEnchantment` | the skill’s own | 1 | Fire Burst on Kill. Art: a guess |
| `TriggeredMoltenStrike` | area | `MoltenStrikeProjectileImpact` | 6 | 4 every 0.1s within 12 | Molten Burst on melee hit: 4 molten balls landing around the target. Art: Molten Strike's; sizes guessed |
| `TriggeredStarfall` | area | `StarfellImpact` | 5 | 22 every 0.05s within 16 | Starfall on melee critical hit: 22 arrows (its projectile count) falling around the target. Art: the client's own impact; sizes guessed |
| `UniqueEnchantmentOfInfernoOnCrit` | area | `EnchantmentMeteorImpact` | 10 | 20 every 0.1s within 22 | Commandment of Inferno on crit: a storm around the character, a meteor every fire_storm_fireball_delay_ms (100) for its 2 seconds. The meteor's radius and the storm's width are guesses (the old Firestorm's 10 and about 25); art: the enchantment's meteor impact, the falling meteor is not shown |
| `DeathAura` | aura | `unique_deaths_oath_aura,unique_deaths_oath_aura,AncestralTotemChaosDegen` | 30 | 1 | Death Aura (Death's Oath): its chaos damage per minute to enemies within 30 cells, a guess (the stat set has no radius). art: the buff on the enemies inside, the buff the wearer states as the aura's source, the circle drawn at the radius (a guess). The character's damage-over-time modifiers are not applied |
| `RepeatingShockwave` | pulse | `FireNovaEnchantment` ×0.42 | 18 | every 0.5s | Abberath's Fury (Abberath's Hooves): a burst around the character every half second while moving. Art, radius 18 and the period are guesses; scale 0.42 measured from a recording so the ring ends about where the hit does |
| `BlindingAura` | worn | — | the skill’s own | 1 | Blinding Aura: its rule gives virtual_nearby_enemies_are_blinded, which blinds within 35 cells; light radius does not grow it yet |

## Buff rows (`effect_buffs`)

| Skill | Buff or routine | Values | Note |
|---|---|---|---|
| `JunTriggeredBloodRage` | routine `BloodRage` | `{}` | Blood Rage on kill (a veiled weapon mod): the gem's own routine with the item skill's numbers |
| `ShadeForm` | `shade_form` | `{"physical_immunity":1,"stealth_+%":"stealth_+%"}` | Shade Form (Shadowstitch). Its cooldown should not recover while the buff lasts: not modelled |
| `ShadeFormWhenHit` | `shade_form` | `{"physical_immunity":1,"stealth_+%":"stealth_+%"}` | Shade Form when hit. Its cooldown should not recover while the buff lasts: not modelled |

## Survey: the 188 item-granted skills that have a trigger stat

Counted from the client's tables before this work, by what releasing each would need:

| What the skill is | Count | What takes it now |
|---|---|---|
| Spells without a hit (auras, summons, buffs, ground) | 75 | the `summon`, `buff`, `aura` and `worn` executors, for the skills that have a row or are in the summon catalog |
| Spells with a hit | 37 | the `area` executor, for those with an `effect_skills` row |
| Attacks with a hit | 35 | the `area` executor too (they hit with the weapon; the game's own weapon restrictions hold) |
| Projectiles without a definition | 20 | nothing yet: four have one-hit stand-ins |
| Projectiles with a definition | 10 | unchanged: the trigger gems' projectile path |
| Curses and marks | 5 | the `curse` executor, for those in the curse catalog |

Before this work the trigger gems' executors could release 12 of the 188 (a projectile with a definition, a curse, Ice Nova, a guard or a summon).

Of the 99 that uniques, essences and influence mods actually grant, the ones still without a way to release them are listed in the roadmap entry.

## Offline checks (harnesses against the real client tables and a world.db built from `sql/`)

- 40 kills with a 25% Raging Spirit ring and Arakaali's line: 12 or 13 spirits (10 mana each) and 1 spider (its 2 s cooldown). The Scourge's line: wolves up to the limit of 10.
- Lightning Bolt on Hit fires on an attack hit and not on a spell hit. Commandment of Inferno on a critical hit: 20 hits around the character over 2 s, a far monster untouched. An ignited kill adds Ignition Blast; a plain kill does not.
- Blazing Glare when hit: 2,347 to a monster beside the character at level 100. Shield Shatter on block: 7,101 with a quality 20 shield. Storm Cascade: 6 hits where an attack was aimed, none for a spell. Starfall: 22 hits on a melee critical hit. Fiery Impact on melee hits only.
- Shade Form: the buff for 3 s, then its 40 s cooldown. Blood Rage: started within 30 kills at 10%, with the item skill's 7 s and 5% attack speed.
- Speed, per hit: 25 to 35 ns with no hit rule on the actor, about 200 ns with two rules that do not fire, about 320 ns firing.

## Compared with the code it will replace

| | Today | With a rule |
|---|---|---|
| Onslaught on kill | `OnslaughtOnKill.cs`, one stat | a row: any stat, chance, duration, on any event |
| A charge on an event | one of about 80 entries written out in `ChargeGains.cs` | a row with kind `charge` (not moved yet) |
| An item's triggered skill | a handler per skill, or nothing | a rule row for the trigger stat and a row for the skill |
| A new condition ("against bleeding enemies") | an `if` in each place that wants it | one fact, usable by every rule |
| Who made the hit (totem, trap, minion) | each site decides for itself | `who` on the row |
| A mod's own effect | the mod hooks the code it needs | the mod adds rows, or a kind or executor of its own |
| Turning one effect off | a code change | `enabled = 0` on its row |
