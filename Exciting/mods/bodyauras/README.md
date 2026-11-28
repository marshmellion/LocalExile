# Body Auras

> **Needs:** the Exciting build of LocalExile (github.com/marshmellion/LocalExile, branch exciting) and Python (the setup makes its effects from your own client).  
> **Works with:** Cosmetic Drops (body auras drop and must be owned).

Every weapon effect worn on the body, and character effects worn by minions.

- **Your character:** each of the game's 245 weapon effects has a **Body Aura** cosmetic (e.g. *Crucible Body Aura*)
  in the Cosmetics tab's Character Effect slot: the effect's glow over your whole body and its particles on your arms,
  legs and body. `character.extra` puts an aura on you as well as whatever is in that slot.
- **Minions:** spectres (`minions.spectres`), every other minion (`minions.everything`: zombies, skeletons, raging
  spirits, phantasms, golems, skitterbots, reapers, animated weapons and guardians, relics, spiders, wolves, clones...)
  and any kind on its own (`minions.perKind`, e.g. `"ragingspirit": "crucible"`). A spectre wears it in place of its
  see-through spectral look, as the game's spectre skins do (`minions.spectreLook`: `replace`, or `add` to keep the
  spectral look under it, fainter).
- **Pets, totems, traps and mines:** `others.pets`, `others.totems`, `others.traps`, `others.mines`.
- Each choice is `off`, `character` (what your character wears in the Character Effect slot: a body aura, or one of
  the game's 149 character effects, which have a minion look too) or an aura's key or name.
- **In the game:** `/aura` shows the choices; `/aura list [words]` the auras you can use; `/aura kinds` the minion kinds
  you have summoned; `/aura <spectres|minions|pets|totems|traps|mines|me|a kind> <off|character|default|aura>` changes
  one for the character you play, without reloading the mod (kept in `data/moddata/bodyauras/aura-choices.json`).
- **With Cosmetic Drops:** body auras are locked and drop where their weapon effects drop (Cosmetic Drops 1.4.0 reads
  this mod's `cosmeticdrops.json`). An aura works on minions and the rest only once your account owns its cosmetic
  (`ownedOnly`); owning a body aura does not give you its weapon effect.

Not possible this way: projectiles (Fireball), Toxic Rain's pods' flight, Arc's beams and other skill effects that are
not objects of their own: the auras are buffs, and only objects (the player, minions, pets, totems, traps, mines) carry
buffs.

## Setting it up

The effects are made on your own computer from your own client, since the game's effect files are not ours to share.
The Exciting build's setup does it (`Exciting\setup.cmd`); by hand, from the LocalExile folder, after the game has started
once (it needs `data\client.db`):

```
python Mods\Binaries\bodyauras\tools\make_auras.py --client .
```

It reads the effect files through LocalExile's content patcher (`tools\ContentPatch`), writes `content\` (the content
pack and its effect files), `auras.index.json` and `cosmeticdrops.json`, and the next start with the play button puts
them into the game. Run it again after a game update.

## How it works

`tools/make_auras.py --client <LocalExile folder>` (or `<extracted game files> <client.db> . [cosmetics.json]`) writes:
- `content/auras.json`, the content pack. Per weapon effect: MtxTypes and MtxTypeGameSpecific rows (copies of the
  Celestial Character Effect's), an ItemVisualIdentity row and its ItemisedVisualEffect row (no item class: with the
  Belt class the Cosmetics tab said "Type Mismatch"), a BuffVisuals row (player and monster versions) and an invisible
  BuffDefinitions row. Per character effect: the BuffVisuals and BuffDefinitions rows only.
- `content/effects/`: `<aura>_character.epk` (the weapon effect's material passes, its particles on the arms, legs and
  body, its trails on the arms), `<aura>_monster.epk` (the passes and particles on `frameupper`, which every monster
  has), and body copies of the particle files (`p_*.pet`: spawning along the whole line, three times as often, 1.4
  times as big; a weapon's spawn along part of a blade). The weapon effects' attached models are left out.
- `auras.index.json` (what the server mod reads) and `cosmeticdrops.json`.


The game's content is patched when the game is started with the play button, so new or changed auras need a game and
server restart. The server mod puts the buffs on every `updateSeconds`.
