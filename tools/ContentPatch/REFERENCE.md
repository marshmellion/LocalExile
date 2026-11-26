# Modding API reference (play setup)

Everything needed to pick this work up again: what the API is for, the decisions, the client's file formats as the code
writes them, ids, commands, the PC layout, how it was tested, and what is open. Kept in three places with the same text:
this file (branch `modding-api` of marshmellion/LocalExile), the project doc `claude/modding-api-reference.md`, and
`Z:\PATH OF EXILE LOCAL EDITION\Claude Stuff\modding-api-reference.md`. Update all three together.

Last updated 2026-11-20 (orb and sun confirmed live; docs PR #1201; no session links).

---

## 1. Goal and ground rules

- `MAIN GAME PLAY HERE` is marshmellion's private testing ground for invasive mods and a modding API that would not be
  allowed on LocalExile's GitHub. Upstream main is still folded in (`Claude Stuff\update-main.cmd`); main takes
  priority, and our mods/branches are changed to fit when main changes.
- The API should allow (marshmellion's list): new pictures/textures, GUIs, buttons, keybinds, new items and item ids,
  currencies, item modifiers, base types, gems; changing existing data: loot tables, drop chances, passive tree,
  skills/gems.
- Decisions (2026-11-20, AskUserQuestion):
  - **Separate play client** (not the shared one) for patching.
  - **Custom UI: an overlay injected into the game** (draws windows/buttons, catches keys).
  - **Mods: C# for logic + JSON data files ("content packs")**.
  - **First milestone: new items end to end.** marshmellion's test case: Exciting Items marked without the
    synthesised flag: a graphic beside the item name when it drops, matchable by loot filters, not a new influence.
- Standing rules that still apply: public posts (PRs, comments) need marshmellion's approval of the text; never
  install software or change system settings on the PC; start the client only via live.ps1/Waypoint; never commit game
  files or secrets. Commits: author marshmellion (`14154274+marshmellion@users.noreply.github.com`), trailer
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. **No session links** (`Claude-Session:` or claude.ai/code
  URLs) in commits or pull requests: marshmellion's rule, 2026-11-20.

## 2. Two halves of the API

| Half | Where it acts | How |
|---|---|---|
| Server side | the Gateway and modules | C# mods (existing LocalExile mod system: `Mods/Binaries/<id>/` script mods, `Mods/Source/<id>/` project mods; GameEvents hooks; local-only hooks in `Claude Stuff\apply-local-hooks.ps1`) |
| Client data | the client's `Content.ggpk` **and** `data/client.db` | content packs applied by `tools/ContentPatch` (this folder) |
| Client UI | the running client | planned: injected overlay (not started) |

Anything the client must display or know (a new base type, modifier, currency, art, passive node) needs the client
data half. Pure rules (drop chances, loot tables the server rolls, monster behaviour) are server side only.

## 3. Key findings

### 3.1 Raw blocks: no compressor needed
- Bundle chunks are Oodle streams. The client's own `OodleLZ_Decompress` (**0x142c9d520** in the 3.29.3c exe; emulated
  by `scripts/re/formats/oodle_client.py`, needs `pip install unicorn pefile`) accepts a chunk that is **one Kraken
  stream with a single uncompressed block: the two bytes `CC 06` followed by the raw bytes**. Verified identical for
  1, 255, 65 536 and 262 144 bytes (a full chunk). Test: `test/client_oodle_raw_blocks.py` (its exe path is the staged
  copy `/mnt/user-data/uploads/PATH OF EXILE LOCAL EDITION/LOCAL EXILE/Client/PathOfExile.exe`).
- Header `8C 06` is refused. A chunk with compressed size == size and **no** header is refused.
- The server's managed decoder (OozSharp, `ManagedOodleDecoder`) reads raw-block chunks identically (tested 1 B to
  1 MB, multi-chunk).
- **But OozSharp cannot read the client's own bundles**: the real 3.29.3c `_.index.bin` header says encoder **12**,
  word16 = 1, 128 chunks, size 33 449 776, payload 20 825 916, head 560, words 44-59 zero, and its chunks hold
  **Selkie** blocks ("Decoder type Selkie not supported"). Reading GGG's bundles needs the extractor's ooz-wasm worker in
  Node (`Tools/ooz/ooz_worker.mjs` via `Tools/PoEDataExtractor/NodeOodleDecoder.cs`; node from PATH, `POE_NODE`, or
  `C:\Program Files\nodejs\node.exe`; `POE_OOZ_WORKER` overrides the script). ContentPatch's `Oodle.cs` copies our raw
  blocks out itself and sends every other chunk to that worker (started on first use). `GameFileSystem.Open(location)`
  with no decoder fails the same way on this client: always pass one.

### 3.2 The servers never read the client
- Servers read the client's tables from **`data/client.db`** (SQLite), built from the SQL in git (`sql/base/client/`,
  `sql/updates/client*/`) by `GameServer.Storage.SqlUpdater` at Gateway/Auth start. It is **incremental** (each SQL file
  runs once, recorded in `updates`), never rebuilt from scratch, so rows we insert persist.
- So every content change must be written to the client and to client.db (ServerMirror does this).
- client.db layout: one table per client table, named as in the schema; `_row INTEGER PRIMARY KEY` (= the dat row
  index), then the schema's named columns; arrays as JSON text (`[5,6]`), row references as the row number (INTEGER),
  bools as INTEGER 0/1, strings TEXT. Example: `Mods` has 40 355 rows; `BaseItemTypes` 5 461 (max _row 5460).

### 3.3 Protocol ids (copied rows need their own)
- Mods, WorldAreas, ItemVisualIdentity: **`HASH16`**. BaseItemTypes, Words: **`HASH32`**. The client and server name
  rows by these on the wire, so a copied row keeping its template's value shows as the template.
- **BaseItemTypes HASH32 = MurmurHash2-32, seed 0, of the Id's UTF-8** (every row of the client's own table) and the
  client relies on it: a new base type with any other value closed the client the moment the item appeared
  (2026-11-20). ContentPatch now derives it (and refuses an Id whose hash another base type has). The HASH16 columns
  (Mods, ItemVisualIdentity, WorldAreas) are not derived this way: for those ContentPatch takes MurmurHash64A of the
  `Id` masked to 16 bits (0 and taken values skipped). Same Id → same value on every apply, so saved items keep it.
  Example: test area "LocalExile_Test" got HASH16 4624 (template Lioneye's Watch: 9052).

### 3.4 Loot filter conditions the 3.29.3c client knows
From the exe's strings (UTF-16, near "HasExplicitMod"): ItemLevel, DropLevel, AreaLevel, BaseType, Class, Rarity…,
LinkedSockets, SocketGroup, Width, Height, Identified, Corrupted, Mirrored, TwiceCorrupted, CorruptedMods, ShaperItem,
ElderItem, HasInfluence (Shaper/Elder/Crusader/Redeemer/Hunter/Warlord), FracturedItem, SynthesisedItem,
**HasExplicitMod**, **HasImplicitMod**, AnyEnchantment, HasEnchantment, EnchantmentPassiveNode, EnchantmentPassiveNum,
HasSearingExarchImplicit, HasEaterOfWorldsImplicit, UnidentifiedItemTier, IsVaalUnique, HasVaalUniqueMod, Imbued,
Replica, TransfiguredGem, **Scourged**, **Foulborn**, **Vestigial**, MirageMap, MemoryStrands, ZanaMemory, BlightedMap,
UberBlightedMap, ShapedMap, ElderMap, MapTier, HasCruciblePassiveTree, StackSize, GemLevel, BaseArmour, BaseEvasion,
BaseEnergyShield, BaseWard, BaseDefencePercentile, AlternateQuality. Actions: SetTextColor, SetBorderColor,
SetBackgroundColor, SetFontSize, PlayAlertSound(Positional), CustomAlertSound(Optional), PlayEffect, MinimapIcon,
DisableDropSound, EnableDropSound(IfAlertSound), Continue. Operators include WHOLEWORD/PARTIAL/INWORD/STARTSWITH/ENDSWITH.

Exciting Items mark (decided, v1.2.0): an **enchantment** of our own, `LocalExileExcitingMarker`, Name "Exciting",
Domain 1, GenerationType 10, no stats, its own ModType row (`excitingitems/content/marker.json`; client row 40355,
HASH16 24961 at the 2026-11-20 export). Filter: `HasEnchantment "Exciting"` (enchant mods match by their Name, e.g.
"Enchantment Word of Blades"). Why an enchantment: `MagicProperties.Write` sends the explicit list only for an
identified item; implicit, enchant, scourge and hidden lists go for every item, so `HasExplicitMod` can't see an
unidentified drop. Side effects: `AnyEnchantment True` also matches; a bench or lab enchant that replaces the
Enchant section drops the marker. Without the patched data the mod falls back to synthesised.
**Rule: never reuse a retired mechanic's flag or symbol** (Vestigial, Breach, Scourged, Foulborn, Imbued…) for our
content: they are to be reimplemented one day. New marks get new data (a modifier) and new art (a new sprite).
Item names can't carry a filterable mark: filters have no name condition, and a rare's name isn't sent until it is
identified.

### 3.5 Real client export (2026-11-20)
- `inspect` on the play client: 46 884 bundles, 1 245 702 files, records **not** sorted by hash (new records are
  appended), index round trip **identical**. Original index kept in `MAIN GAME PLAY HERE\data\content-patch\`.
- Exported (in `Claude Stuff\export\`): Mods (40 355 × 654 B), BaseItemTypes (5 461 × 374), ItemVisualIdentity
  (15 516 × 604), CurrencyItems (1 962 × 205), ItemClasses (103 × 153), Words (4 117 × 64), ItemFrameType (14 × 111),
  ModType (14 554 × 41): all round-trip byte-identical with the schema; row counts equal client.db's. `paths.txt`
  (99 MB, lower-case paths). UI sprite lists to export next: `art/uiimages1.txt` (and uidivinationimages, uishopimages).
- ItemFrameType rows: Normal, Magic, Rare, Unique, Gem, Currency, DivinationCard, Quest, Prophecy, Foil, SupporterFoil,
  Necropolis, Gold, BreachGraftPseudoGem (header art names like ItemsHeaderRare; chosen by rarity, not per item).
- UI art: atlases `art/textures/interface/2d/2dart/uiimages/common/<n>.dds` (+ `4k/`); sprite names
  (`Art/2DArt/UIImages/InGame/…`) map to atlas rectangles in `art/uiimages1.txt`.

### 3.6 Tooltip header symbols (beside the item name)
Sprites the exe names: ShaperItemSymbol, ElderItemSymbol, CrusaderItemSymbol, BasiliskItemSymbol (Hunter),
EyrieItemSymbol (Redeemer), JudicatorItemSymbol (Warlord), SynthesisedItemSymbol, FracturedItemSymbol,
VeiledItemSymbol, ArchnemesisItemSymbol, BreachItemSymbol, VestigialUniqueItemsIcons, ExperimentedUniqueSymbol,
RosePetalInfluencedItemSymbol, Heist/StolenItemSymbol, Deepwater/ContrabandItemSymbol, CleansingFireItemSymbol
(Searing Exarch implicit), TangledItemSymbol (Eater implicit). Second users of most of them around 0x1416d1100.
The picker near **0x140c99de7** reads byte flags of a tooltip-side item struct (rdi): kind == 0x4c → Archnemesis;
`+0xf3d` → Stolen (with `+0xf3e` → Contraband); `+0xf36` → Synthesised, else `+0xf35` → Fractured (0x140c9a7cc); **`+0xf37` → Breach**;
**`+0xf38` → Vestigial**; then veiled mods (count of 40-byte records at `+0xf58..+0xf60`), influences etc.
Server side: the Mods component's u16 flags (InstanceItems.cs ~line 224): 0x2 mirrored, 0x4 split, 0x10 relic style,
0x20 synthetic, 0x40 incubator, 0x80 a 0x1c8 record, 0x100 foreseeing, **0x400 vestigial** (`Item.Vestigial`; the
client then refuses currency on the item). The client's debug dump of that component is 0x1421e7b00 (fields
identified/duplicated/split/disable_mirrored_visuals +0x2e1/relic +0x2e3/synthetic +0x2e5, implicit/enchant/hellscape
mods). Which network flag sets the tooltip's Breach byte `+0xf37` is not traced yet.
**Item symbol hook (itemSymbols; live 2026-11-20: the sun shows beside the name in the tooltip and on the ground label).** Pickers: tooltip header 0x140c99e27 (struct rdi)
and item header/label 0x1416d11a0 (struct rbx); both pick by struct bytes, neither has the item. The builder
0x142401e00 (struct r15, item handle at rsp+0x6718, its +8 the entity) is hooked at 0x142402b2c (before its two paths
split on rsp+0x67b8): it reads the Mods component (0x14013fb60, looked up by name "Mods"; returns 0 without one), walks
the enchant mods (component +0xf0..+0xf8, 0x38 bytes each, +0x28 → Mods row, row+0 → Id as UTF-16; layout from the
component's debug dump 0x1421e7b00: implicit +0xc0, explicit +0xd8, enchant +0xf0, hellscape +0x108) and writes the
answer into struct padding: +0x1022 u16 0x584C, +0x1024 table index + 1, +0x1025 its complement. Every byte
+0xf2d..+0xf51 is a used, copied field (copy routine 0x1416edcb0), so padding was the only free place; the copy
routine does not copy padding (gaps +0x1021..+0x1027, +0xf52..+0xf57 …). The pickers check it before the synthesised/
fractured symbols and jump to the client's own draw code with rdx = our sprite name (tooltip → 0x140c9a7e0, label →
0x1416d11b0). Code: `hooks/item_symbols.py` (keystone) → `hooks/ItemSymbolsCode.cs`; installed by `ClientHooks` into a
section ".lxhook" (RWX, 0x2000, RVA 0x4D9A000 = the release's SizeOfImage) added to `Client/Game.patched.exe`, the
signature dropped; table at +0x800 (u32 count, {u32 Id offset, u32 sprite offset}), strings at +0xA00.

## 4. Client file formats (as ContentPatch reads and writes them)

### 4.1 Content.ggpk (standalone client, GGPK version 3)
Records: `u32 length, 4-byte tag, body`.
- `GGPK`: length 28; `u32 version (3)`, `u64 root directory offset`, `u64 first free record offset`.
- `PDIR`: `u32 name length in UTF-16 chars incl. NUL`, `u32 entry count`, `32-byte SHA-256`, name (UTF-16LE + NUL),
  entries `(u32 name hash, u64 offset)` sorted by name hash. Name hash = **MurmurHash2-32, seed 0, of the lower-case
  name's UTF-16LE bytes** (LibGGPK3 finds entries with it; `test/mkggpk.py` implements it).
- `FILE`: `u32 name length`, `32-byte SHA-256 of the data`, name (UTF-16LE + NUL), data.
- `FREE`: free space chain.
- The root directory's hash: the patch server's welcome must match it (`data/client-constants.json` GgpkRootHash).
  `GgpkMaintenance.RepairRootHash` gives a zero root hash a value (SHA-256 of children's hashes);
  `GgpkMaintenance.RootHash` reads it.
- Library: LibGGPK3 2.7.5. `new GGPK(path)` (read/write), `ggpk.Root.TryFindNode(path, out node)`,
  `FileRecord.Read()`, `FileRecord.Write(bytes)` (updates hashes up to the root), `DirectoryRecord.FindOrAddDirectory`,
  `AddFile(name, out FileRecord, size)`, `GGPK.RenewHashes`, `EraseRootHash`, `FastCompact`, `FixFreeRecordList`.
  It cannot create a new GGPK (the test fixture is written by `test/mkggpk.py`).

### 4.2 Bundles (`Bundles2/*.bundle.bin`)
Little-endian. Offsets:
```
0  u32 decompressed size        4  u32 payload size (sum of chunk sizes)   8  u32 head size (= 48 + 4 × chunks)
12 u32 first-file encoder (8 = Kraken)      16 u32 0
20 u64 decompressed size        28 u64 payload size
36 u32 chunk count              40 u32 granularity (262 144: uncompressed bytes per chunk; last chunk = remainder)
44 4 × u32 (0 in ours; the real index's values: see `inspect`)
60 u32 chunk compressed sizes[count], then the chunks
```
Raw-block chunk: `CC 06` + up to 262 144 raw bytes; its size field = raw size + 2. `RawBundle.Write` / `TryReadRaw`.
**Word 16 must be 1** (as in the client's own index). With 0 there, the real 3.29.3c client closed silently during
startup (its log stops after "Selected XInput.dll", before "[ENGINE] Ready"); with 1 it loads (2026-11-20, commit
b96a7ea0).

### 4.2b Textures (`X.dds` + `X.dds.header`)
From the real Chaos Orb art (`Art/2DItems/Currency/CurrencyRerollRare.dds`, 80×80): a DX10 DDS, flags 0x21007, depth 1,
DXGI format 28 (R8G8B8A8, uncompressed), full mip chain (7 for 80×80). `.dds.header`: u32 3, width, height, mip count,
u32 21, size of the DDS that follows, size of X.dds, then a legacy DDS (flags 0x2100f, pitch w×4, masks
ff/ff00/ff0000/ff000000, caps 0x401008) of the mips from the first ≤ 20 px wide. `Texture.FromPng` writes both; on the
Chaos Orb's own pixels its headers are byte-identical and its sizes equal. UI image list `art/uiimages1.txt`: UTF-16LE
**without** a BOM, lines `"<sprite>" "<texture .dds>" x1 y1 x2 y2` (the item symbols span 43 in each direction).

### 4.3 The index (`Bundles2/_.index.bin`, itself a bundle)
Decompressed:
```
i32 bundle count; per bundle: i32 name length, UTF-8 name (no "Bundles2/", no ".bundle.bin"), i32 decompressed size
i32 file count;   per file (20 bytes): u64 path hash, i32 bundle index, i32 offset in the bundle, i32 size
i32 directory count; per directory (20 bytes): u64 hash, i32 offset, i32 size, i32 recursive size  (into the path table)
rest: the path table, a compressed bundle of its own (only needed to list paths)
```
- Path hash = **MurmurHash64A, seed 0x1337b33f, of the lower-case path's UTF-8 bytes** (`MurmurHash64A.Hash`,
  `IndexEditor.PathHash`). The client finds files by hash; new files need no path-table entry.
- `IndexEditor` keeps everything and writes it back; when the client's records are sorted by hash it writes ours in hash
  order too (in case the client binary-searches). `inspect` reports `records sorted by hash`.
- Path table decoding: `GameIndex.Paths` (per directory: stream of `u32 n` + NUL-terminated string; n = 0 toggles base
  mode; else append to base n-1).

### 4.4 Tables (`Data/<Table>.datc64`)
```
u32 row count
row count × row length fixed bytes      (row length = boundary / rows)
8 × 0xBB, then the variable section     (strings and arrays; offsets in rows are from the start of the 0xBB marker)
```
Column sizes: bool 1; i16/u16 2; i32/u32/f32/enumrow 4; **string 8** (u64 offset to UTF-16LE text ended by 4 zero
bytes); **row 8** (u64 row index; null = `FE FE FE FE FE FE FE FE`); **foreignrow 16** (u64 row + u64, null = 16 × FE;
we write 0 in the second u64 for non-null); **array 16** (u64 count, u64 offset); interval = 2 × atom.
Schema: `data/schema/schema.min.json` (poe-tool-dev dat-schema; Game1 variant preferred; `DatSchema.LoadFile`).
Unnamed columns are `__col<i>`. Column names are exact and case-sensitive (e.g. WorldAreas connections are
`Connections_WorldAreasKeys`, not `Connections`).
`DatEditor`: Parse/Write (all 14 tables in game-files.zip round-trip byte-identical), `Find`, `GetString/GetInt/GetArray`,
`AddCopyOf(template)`, `SetString/SetInt/SetFloat/SetArray/SetStringArray` (new strings/arrays appended to the variable
section; existing offsets never move).
Server-side reader: `GameServer.Data.Dat.DatFile.Parse(bytes).ReadRows(schemaTable)`.

## 5. ContentPatch (tools/ContentPatch)

### 5.1 Files
| File | Does |
|---|---|
| `Program.cs` | commands: `inspect`, `export <out> <path>...`, `paths <out.txt>`, `apply [--mods <folder>]`, `restore`; client argument optional (default `Client`); a standalone client only (Content.ggpk) |
| `Patcher.cs` | reads originals through the **original** index (kept once in `data/content-patch/original_index.bin`; refuses if the client is patched and the backup is missing), applies packs, writes `Bundles2/LocalExile/Content.bundle.bin` + the new index, repairs/returns the root hash; `Restore` |
| `ContentPack.cs` | loads packs from enabled mods: `Mods/Binaries/<id>/content/*.json` then `Mods/Source/<id>/content/*.json`, id order; skips ids `off` in `Mods/Binaries/mod-state.txt`; JSON allows comments and trailing commas |
| `ServerMirror.cs` | replays the same row adds/sets into `data/client.db`; undoes the previous apply first (tables `localexile_content_tables(tbl, original_count)` and `localexile_content_undo(tbl,row,col,value)`); `Restore` |
| `RawBundle.cs`, `IndexEditor.cs`, `DatEditor.cs` | formats (section 4) |
| `Oodle.cs` | the decoder: raw blocks itself, others via the Node ooz worker (`NodeOodleDecoder.cs` is linked from Tools/PoEDataExtractor in the csproj) |
| `test/` | fixture builders and checks (section 7), excluded from the build |

Our bundle's index name: `LocalExile/Content` (`Patcher.OurBundle`). GGG's bundles are never touched.
Row-count guard: a table whose client row count differs from client.db's original count is refused (rows would
misalign).

### 5.2 Content pack format
```jsonc
{
  "rows": [   // new rows: a copy of the first row matching copyOf, with set written over it
    { "table": "WorldAreas", "copyOf": { "Id": "1_1_town" },
      "set": { "Id": "LocalExile_Test", "Name": "The Test Grounds", "AreaLevel": 77,
               "Connections_WorldAreasKeys": [ { "row": { "table": "WorldAreas", "Id": "1_1_1" } } ] } } ],
  "edits": [  // change every row matching where
    { "table": "WorldAreas", "where": { "Id": "1_1_town" }, "set": { "Name": "Lioneye's Watch (patched)" } } ],
  "files": [  // add a file or replace one the client has (path inside the client, file relative to the pack's folder)
    { "path": "Art/LocalExile/hello.txt", "from": "hello.txt" } ]
}
```
Values: string, integer, float, true/false, null (a null row), arrays of numbers or strings, and row references
`{ "row": { "table": T, <column>: value, ... } }` (also inside arrays; may name a row an earlier pack or entry adds).
`where`/`copyOf`/references match string and integer columns, and row-reference columns by a `{ "row": … }`. A new row
whose `Id` already exists in this run updates that row. HASH16/HASH32: see 3.3. Order: edits, rows, serverRows, files.
More entry kinds:
- `files[]` with `"image": "x.png"` (path ending .dds): writes the texture pair (4.2b) from an 8-bit RGBA/RGB PNG.
  `"imageBase64": "<the PNG in base64>"` does the same with the PNG inside the pack's text, for packs copied by tools that
  only carry text.
- `files[]` with `"appendLines": [ … ]`: lines added to a client text file in its own encoding (UTF-16LE with or
  without BOM, else UTF-8), e.g. a sprite line in `Art/UIImages1.txt`.
- `"serverRows": [{ "table": "item_types", "copyOf": { "type": … }, "set": { … } }]`: rows only client.db has (arrays as
  JSON). A new base type needs its `item_types` row (components; the server's ItemCatalog reads it).
- `"itemSymbols": [{ "mod": <Mods Id of an enchantment>, "sprite": <UI image name> }]`: the client hook (3.6).
A new currency (Radiant Orb, `excitingitems/content/radiantorb.json`): ItemVisualIdentity, BaseItemTypes (InheritsFrom
StackableCurrency: no .it file of its own is needed, Chaos Orb has none either), CurrencyItems copied from the Divine
Orb's (client then allows it on magic/rare/unique), item_types server row, texture. The server's use comes from the
play setup's LocalHooks.CurrencyItemUse (reached for unknown currencies; corrupted/mirrored targets are refused first).

### 5.3 Running it on the PC
`Z:\PATH OF EXILE LOCAL EDITION\Claude Stuff\content-patch.cmd <command>` (cd's to MAIN GAME PLAY HERE, runs
`dotnet run --project tools\ContentPatch -c Release -- …`, logs to `Claude Stuff\logs\content-patch.log`):
- `inspect` (read-only): index header, bundle/file counts, sorted-by-hash, whether ours is present, and a check that the
  index writes back identical (**must say identical before any apply**).
- `export` (read-only): copies Mods, BaseItemTypes, ItemVisualIdentity, CurrencyItems, ItemClasses, Words,
  ItemFrameType, ModType tables and `paths.txt` (every client path) into `Claude Stuff\export\`.
- `apply` / `restore`: refused while `MAIN GAME PLAY HERE\Client` is still a junction (shared client).
  `content-patch-apply.cmd` / `content-patch-restore.cmd` are double-click versions.
- `apply --if-changed` (what live.ps1 runs before the client data is opened, play setup only): skips when the packs'
  folders, the patcher's own code (module version id) and the client's patched state match `data/content-patch/
  applied.txt`; restores when no packs are left. Item symbols are checked every time (a re-made Game.patched.exe gets
  its hook again). Output in `logs\content-patch.log` of the checkout.
- Close the game and stop the servers first (the client holds Content.ggpk open).
- After apply: start the play setup normally (Waypoint/live.ps1); client.db and client-constants.json are updated.

## 6. PC layout (Z:\PATH OF EXILE LOCAL EDITION)
- `MAIN GAME PLAY HERE` – the play checkout (origin LocalExile/LocalExile, local branch `play`, rebuilt by
  `Claude Stuff\update-main.cmd` → `update-play.ps1`: main + every branch in `play-branches.txt` fetched from
  marshmellion/LocalExile, then the local hooks). Branches now: skill-visuals, totem-multi-visuals, character-at-rest,
  stash-folders, boss-skills, modding-api.
- Until `Claude Stuff\make-play-client.cmd` is run, `MAIN GAME PLAY HERE\Client`, `LocalExile-dev\Client` and
  `LOCAL EXILE\Client` are the **same shared client** (identical mtimes; a junction). Content.ggpk 73 560 124 750 bytes,
  standalone layout.
- `POE CLIENT 3.29.0d\Client_POE_3.29.3c.7z\Client` – an unpacked separate copy of the 3.29.3c client (Game.exe mtime
  differs from the shared one); `make-play-client.cmd` moves it to `MAIN GAME PLAY HERE\Client` after removing the
  junction (refuses if Client is a real folder or the game runs). `POE CLIENT 3.29.0d\Client_POE_3.29.3c.7z.001-.009`:
  the archive (backup).
- Play `data\`: client.db (~180 MB), world.db, characters.db, auth.db, client-constants.json, game-files.zip (126 MB:
  art/data/metadata subset; only 14 datc64 tables: level generation ones), schema/.
- `Claude Stuff\`: scripts (update-main, apply-local-hooks, make-play-client, content-patch, dev-check…),
  `play-branches.txt`, `branch.txt` (dev copy's branch), `logs\`, `export\` (after `content-patch export`).
- PC access from the Claude session: file staging/commit only (no shell). Files ≤ 400 MB per stage; paths > 7 folders
  deep cannot be staged. Outputs go through fresh `/mnt/user-data/outputs/vN/` folders (v68 used last).

## 7. Testing in the Claude workspace
- Build without NuGet: `/home/claude/build/build.sh <name> <srcdir> <exe|library> <asm> [refs]` (`OUT=` output folder,
  `EXTRA=` extra files such as `/tmp/ivt.cs` for InternalsVisibleTo); third-party DLLs in `/home/claude/build/lib`
  (LibGGPK3, OozSharp, Microsoft.Data.Sqlite, SQLitePCLRaw…). ContentPatch compiles with
  `csc … -warnaserror+ -nowarn:CS1701,CS1702 usings.cs tools/ContentPatch/*.cs` against `GameServer.Data`, `Core`,
  `Storage` from an out folder (`/home/claude/build/out8`, built from the boss-skills branch). Runtime config: copy any `*.runtimeconfig.json` (net10.0).
- Fixture: `test/MakeFixture.cs` writes `fixture/_.index.bin` (one bundle "Tables" holding Data/WorldAreas.datc64) and
  `fixture/Tables.bundle.bin`; `python3 test/mkggpk.py spec.json fixture/Content.ggpk` with
  `{"Bundles2":[["_.index.bin","fixture/_.index.bin"],["Tables.bundle.bin","fixture/Tables.bundle.bin"]]}`.
  A run folder needs `Mods/Binaries/testmod/content/` (test/testmod), `data/schema/`, `data/client.db` (a real one:
  WorldAreas 1935 rows), the DLLs and libe_sqlite3.so.
- `test/ReadBack.cs`: reads WorldAreas and the test file back through `GameFileSystem` (the server's reader).
- `test/DatRoundTrip.cs`, `test/BundleRoundTrip.cs`: the round-trip checks.
- Results 2026-11-20: apply → client row 1935 + edit + new file, client.db row 1935 with HASH16 4624; apply again → no
  duplicates; restore → both sides original.
- 2026-11-20 first `inspect` on the real client: failed on Selkie in OozSharp (fixed with Oodle.cs, commit aa97d619);
  nothing was written (the crash came before the backup and any write).
- 2026-11-20 real client: patched archive loads (after the word-16 fix). Exciting marker on a drop: the unidentified
  tooltip shows no line for it, and `HasEnchantment "Exciting"` matches unidentified drops (beam, colours, minimap
  star, sound). The client accepts a filter naming our new enchantment.
- Loot filters: the play client lists `Documents\My Games\Path of Exile` (not its `LocalExile` subfolder). Online
  filters live in `OnlineFilters\<id>` (no extension, header lines `#name:` etc.) and the game rewrites them; Exciting
  Items 1.2.1 (local copy) writes `<name> (Exciting).filter` beside them with the rule on top at every server start.
- Play client settings: `Client\config.json` names the settings and saved-login files (`LocalExile\production_Config.ini`,
  `LocalExile\production_login.credentials` under `Documents\My Games\Path of Exile`); without it the client used
  the read-only `production_Config.ini` there and kept nothing. Loot filters are still listed from the parent folder.
- Play client setup notes: the copy's `Game.exe` had 13 bytes changed by another tool (kept as `Game.modified.exe`;
  `Claude Stuff\fix-play-client-exe.cmd` copies the clean `PathOfExile.exe` over it); `Game.patched.exe` has to be made
  for the play checkout's own `data/login-keys.json` (`Claude Stuff\patch-play-client.cmd`).

## 8. Relevant code elsewhere
- `src/GameServer.Data/Ggpk/`: GameIndex (index reader), BundleFile (bundle reader, header offsets), GameFileSystem
  (opens GGPK or Steam folder; `TryReadFile`, `EnumeratePaths`), ClientFiles (`TryReadFile`, `ReplaceFile`, layouts),
  GgpkMaintenance (root hash), ClientConstants (`data/client-constants.json`: HncMask, GgpkRootHash), MurmurHash64A.
- `src/GameServer.Data/Dat/`: DatFile, DatSchema, DatValue.
- `src/GameServer.Core/Protocol/LooseFilesPatch.cs`: exe patch so a standalone-mode client reads a Steam install's loose
  Bundles2 (0x1400fc426, 0x1428f3221).
- `src/GameServer.Patch/Program.cs`: the local patch server and its GGPK tools (root-hash, gateway list write: the one
  archive write LocalExile itself does).
- `src/GameServer.Storage/SqlUpdater.cs`: how client.db is built (section 3.2).
- `scripts/dev/extract-data.ps1`: the extraction manifest (it does **not** build client.db; client.db comes from sql/).
- Local-only Game hooks for private mods: `Modules/Core/Game/LocalHooks.cs` (Supports, SkillStats, CurrencyItemUse,
  UsedItem), applied by `Claude Stuff\apply-local-hooks.ps1`.

## 9. Open items and next steps
1. Done: play client separated, inspect identical, export (3.5).
2. Done: Exciting marker (3.4) end to end on the play client.
3. Done (live): radiant sun beside exciting items' names (tooltip and ground label).
3b. Done (live): Radiant Orb drops, stacks, shows its art, is used on items. `radiantorb-test.json` (Divine art copy) can go.
3c. Findings sent upstream as docs: LocalExile PR #1201 (branch `docs/client-data-findings`).
4. Done: a new currency with its own art, the Radiant Orb (5.2).
5. Done: live.ps1 runs `apply --if-changed` (modding-api branch).
6. In-game overlay (UI, buttons, keybinds): not started. The client folder has `imgui.ini` and a 2012 `d3d11.dll`
   (another tool's), worth checking before choosing an injection route.
7. C# API surface for mods (registering items/currencies/mods in code and generating packs), passive tree
   (`Metadata/PassiveSkillGraph.psg` + PassiveSkills table), gems, loot tables.
