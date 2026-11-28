"""Builds cosmetics.json for Cosmetic Drops from the client's tables (client.db).

Every cosmetic (an MtxTypes row the client can show, i.e. with an MtxTypeGameSpecific row) and every unlockable
hideout gets one source where it can be target-farmed: a boss, league content, a map tileset or strongboxes. A
cosmetic whose name names a theme (Shaper, Kitava, Delirium...) farms where that theme lives; the pieces of one set
(the same name before Helmet, Boots, Portal Effect...) farm together; anything else is spread over the tilesets by
its set's name, so every source has a share. Free things (master decorations, dances, test rows) are left out:
those are always owned.

usage: python3 make_cosmetics.py <client.db> <out.json>
"""
import json, re, sqlite3, sys, zlib

db = sqlite3.connect(sys.argv[1])

# Where things can drop. kind: boss (a monster whose variety Id has one of match), monster (league monsters, any rarity),
# area (the area's Id or name has one of match), strongbox (strongboxes and chests).
SOURCES = [
    ("shaper", "The Shaper", "boss", ["AtlasBosses/TheShaper"]),
    ("elder", "The Elder", "boss", ["AtlasBosses/TheElder"]),
    ("maven", "The Maven", "boss", ["MavenBoss"]),
    ("sirus", "Sirus, Awakener of Worlds", "boss", ["AtlasExiles/AtlasExile5", "AtlasExile5"]),
    ("conquerors", "The Conquerors of the Atlas", "boss", ["AtlasExiles/AtlasExile1", "AtlasExiles/AtlasExile2", "AtlasExiles/AtlasExile3", "AtlasExiles/AtlasExile4"]),
    ("eater", "The Eater of Worlds", "boss", ["ConsumeBoss"]),
    ("exarch", "The Searing Exarch", "boss", ["CleansingBoss"]),
    ("atziri", "Atziri", "boss", ["Atziri/Atziri"]),
    ("kitava", "Kitava", "boss", ["KitavaBoss"]),
    ("guardians", "The Shaper's and Elder's Guardians", "boss", ["AtlasBosses/", "ElderGuardian", "ShaperGuardian"]),
    ("mapbosses", "Map bosses", "mapboss", []),
    ("legion", "Legion encounters", "monster", ["LegionLeague"]),
    ("breach", "Breaches", "monster", ["/Breach/", "BreachLeague"]),
    ("abyss", "Abysses", "monster", ["AbyssTwoLeague", "/Abyss"]),
    ("delve", "Delve", "area", ["Delve"]),
    ("heist", "Heists", "monster", ["LeagueHeist"]),
    ("harvest", "Harvest and Oshabi", "monster", ["LeagueHarvest"]),
    ("betrayal", "The Immortal Syndicate", "monster", ["LeagueBetrayal"]),
    ("blight", "Blight", "monster", ["LeagueBlight"]),
    ("delirium", "Delirium", "monster", ["LeagueAffliction"]),
    ("bestiary", "Einhar's beasts", "monster", ["LeagueBestiary"]),
    ("incursion", "Incursion and the Temple of Atzoatl", "monster", ["VaalArchitect", "LeagueIncursion", "Incursion"]),
    ("synthesis", "Synthesis and Venarius", "monster", ["LeagueSynthesis"]),
    ("expedition", "Expeditions", "monster", ["LeagueExpedition"]),
    ("ritual", "Rituals", "monster", ["LeagueRitual", "Ritual"]),
    ("ultimatum", "Ultimatum", "monster", ["LeagueUltimatum"]),
    ("azmeri", "The Wildwood", "monster", ["LeagueAzmeri"]),
    ("necropolis", "The Necropolis", "monster", ["LeagueNecropolis"]),
    ("crucible", "Crucible", "monster", ["LeagueCrucible"]),
    ("kalandra", "The Lake of Kalandra", "monster", ["LeagueLake"]),
    ("ancestral", "The Ancestors", "monster", ["LeagueAncestral"]),
    ("sanctum", "Sanctum monsters", "monster", ["LeagueSanctum"]),
    ("mirage", "Mirage (Faridun)", "monster", ["FaridunLeague"]),
    ("phrecia", "Phrecia's Hellscape", "monster", ["LeagueHellscape"]),
    ("deepwater", "Deepwater", "monster", ["DeepwaterLeague"]),
    ("harbinger", "Harbingers", "monster", ["Harbinger"]),
    ("rogueexiles", "Rogue Exiles", "monster", ["/Exiles/"]),
    ("mercenaries", "Mercenaries", "monster", ["Mercenar"]),
    ("strongboxes", "Strongboxes", "strongbox", []),
    # Map tilesets: the area's Id or name.
    ("tombs", "Tombs and crypts", "area", ["Crypt", "Tomb", "Burial", "Catacomb", "Necropolis", "Mausoleum", "Sepulchre", "Ossuary", "Graveyard", "Cemetery"]),
    ("jungle", "Jungles", "area", ["Jungle", "Arachnid", "Mangrove", "Tropical", "Overgrown"]),
    ("desert", "Deserts", "area", ["Desert", "Dunes", "Sand", "Oasis", "Arid", "Wasteland"]),
    ("volcanic", "Volcanoes and fire", "area", ["Volcan", "Lava", "Magma", "Core", "Infernal", "Ashen", "Crater", "Furnace"]),
    ("frozen", "Ice and snow", "area", ["Glacier", "Frozen", "Ice", "Arctic", "Frost", "Winter"]),
    ("swamp", "Swamps and bogs", "area", ["Marsh", "Swamp", "Bog", "Fungal", "Toxic", "Mire", "Sewer", "Geyser", "Waste Pool", "Mud"]),
    ("city", "Cities and plazas", "area", ["City", "Plaza", "Promenade", "Bazaar", "Residence", "Villa", "Courtyard", "Palace", "Courthouse", "Mansion", "Precinct", "Terrace", "Arcade", "Ghetto", "Colonnade", "Arsenal", "Market", "Estate", "Slums"]),
    ("coast", "Coasts and seas", "area", ["Beach", "Cove", "Reef", "Port", "Shore", "Lighthouse", "Atoll", "Bay", "Wharf", "Strand", "Shipyard", "Island", "Lagoon", "Pier", "Peninsula", "Channel", "Canal", "Waterways"]),
    ("forest", "Forests and groves", "area", ["Forest", "Grove", "Arboreum", "Wood", "Thicket", "Glade", "Park", "Orchard", "Gardens", "Paradise"]),
    ("caves", "Caves and mines", "area", ["Cave", "Cavern", "Mine", "Grotto", "Tunnel", "Underground", "Mineral", "Excavation", "Geode", "Crystal", "Ore"]),
    ("temples", "Temples and shrines", "area", ["Temple", "Shrine", "Cathedral", "Chapel", "Altar", "Basilica", "Cloister", "Sanctuary", "Monastery", "Reliquary", "Pyramid", "Convent", "Pantheon", "Sanctum"]),
    ("dungeons", "Dungeons and prisons", "area", ["Dungeon", "Prison", "Vault", "Labyrinth", "Pit", "Cells", "Gaol", "Arena", "Colosseum", "Bastion", "Maze", "Asylum"]),
    ("workshops", "Labs, forges and factories", "area", ["Lab", "Factory", "Foundry", "Forge", "Workshop", "Museum", "Library", "Academy", "Spider", "Dig"]),
    ("peaks", "Peaks, cliffs and ruins", "area", ["Peak", "Summit", "Cliff", "Ridge", "Plateau", "Ruin", "Canyon", "Spire", "Tower", "Citadel", "Fortress", "Keep", "Castle"]),
]
TILESETS = [s[0] for s in SOURCES if s[2] == "area" and s[0] != "delve"]
# League content the server does not run yet: its cosmetics drop in a fitting tileset instead (the mod moves them back
# if a setting says the content runs).
FALLBACK = {"sanctum": "temples", "ultimatum": "dungeons", "ritual": "forest", "azmeri": "forest", "necropolis": "tombs",
            "crucible": "volcanic", "kalandra": "coast", "ancestral": "peaks", "phrecia": "mapbosses", "synthesis": "workshops",
            "harbinger": "mapbosses"}

# A theme word in a cosmetic's name (or Id) → its source. First match wins, so the specific words come first.
THEMES = [
    (r"Awakener|Sirus|Storm Herald|Stormcaller", "sirus"),
    (r"Conqueror|Baran|Veritania|Al-Hezmin|Drox", "conquerors"),
    (r"Shaper|Shaped|Cosmic|Celestial", "shaper"),
    (r"Elder|Darkseer|\bVoid\b", "elder"),
    (r"Maven|Astrolabe|Echoes of", "maven"),
    (r"Eater|Tangle|Tentacle|Consum", "eater"),
    (r"Exarch|Searing|Cleansing|Remnant", "exarch"),
    (r"Atziri|\bVaal\b|Corrupt", "atziri"),
    (r"Kitava|Cannibal", "kitava"),
    (r"Guardian|Minotaur|Phoenix|Hydra|Chimera", "guardians"),
    (r"Legion|Timeless|Karui|Maraketh|Templar|Eternal Empire", "legion"),
    (r"Breach|\bXoph|\bTul\b|\bEsh\b|\bUul|Chayula", "breach"),
    (r"Abyss|Abyssal|Stygian", "abyss"),
    (r"Delve|Azurite|\bNiko\b|\bAul\b|Sulphite|Fossil|Resonat", "delve"),
    (r"Heist|Thief|Smuggler|Rogue|Locker|Grand Heist|Blueprint", "heist"),
    (r"Harvest|Oshabi|Sacred Grove|Lifeforce|Wildwood Harvest", "harvest"),
    (r"Betrayal|Syndicate|Catarina|\bJun\b|Mastermind", "betrayal"),
    (r"Blight|Cassia|Sister|Fungal|Oil", "blight"),
    (r"Delirium|Mirror|Fog|Simulacrum|Nightmare", "delirium"),
    (r"Bestiary|Einhar|Beast|Menagerie|Saqawal|Farrul|Craiceann|Fenumus", "bestiary"),
    (r"Incursion|Alva|Atzoatl|Temple of|Architect", "incursion"),
    (r"Synthesis|Venarius|Cortex|Memory|Synthes", "synthesis"),
    (r"Expedition|\bRog\b|Tujen|Gwennen|Dannig|Olroth|Kalguur|Runesmith|Kingsmarch|Settlers", "expedition"),
    (r"Ritual|Tribal|K'Tash|\bMaata", "ritual"),
    (r"Ultimatum|Trialmaster|Trial", "ultimatum"),
    (r"Affliction|Wildwood|Azmeri|Wisp|Tuber", "azmeri"),
    (r"Necropolis|Allflame|Grave", "necropolis"),
    (r"Crucible|Forge of the Titans|Titan", "crucible"),
    (r"Kalandra|Reflection|\bLake\b", "kalandra"),
    (r"Ancestral|Ancestor|Tattoo|Hinekora|Tukohama|Valako|Ngamahu|Rongokurai|Ramako|Arohongui|Tawhoa", "ancestral"),
    (r"Sanctum|Lycia|Divine", "sanctum"),
    (r"Mirage|Faridun|Saresh|Djinn|Afarud", "mirage"),
    (r"Phrecia|Hellscape|Idol", "phrecia"),
    (r"Deepwater|Fairgraves|Pirate", "deepwater"),
    (r"Harbinger", "harbinger"),
    (r"Mercenar", "mercenaries"),
    (r"\bExile", "rogueexiles"),
    (r"Mystery Box|Scroll|Fireworks|Potion|Tracking", "strongboxes"),
]

# The words after which a set's name ends: "Kitava Helmet" and "Kitava Boots" are the set "Kitava".
SLOT_WORDS = r"\b(Helmet|Hood|Hat|Mask|Crown|Brimmed Hat|Boots|Gloves|Gauntlets?|Body Armour|Armour|Cloak|Wings|Back Attachment|Portal Effect|Portal|Weapon Effect|Weapon Skin|Footprints Effect|Footprints|Character Effect|Pet|Portrait Frame|Portrait|Shield|Quiver|Apparition|Map Device|Hideout|Stash|Guild Stash|Challenger Trophy|Effect|Skin|Attachment|Decoration|Totem|Crest|Aura|Sigil)\b.*$"

CATEGORY = {r[0]: r[2] for r in db.execute("select _row, Id, Name from MicrotransactionCategory")}
gs = {h: c for h, c in db.execute("select HASH32, Category from MtxTypeGameSpecific")}
# Free hideout decorations: the masters' (HideoutDoodads with IsNonMasterDoodad 0) and achievement rewards stay ones you drop.
gs_rows = [h for (h,) in db.execute("select HASH32 from MtxTypeGameSpecific order by _row")]
free = set()
for gsrow, nonmaster in db.execute("select MtxTypeGameSpecific, IsNonMasterDoodad from HideoutDoodads"):
    if gsrow is not None and nonmaster == 0 and 0 <= gsrow < len(gs_rows):
        free.add(gs_rows[gsrow])

# Shop price (points) or box value, for the Cosmetic Cache's weights: MicrotransactionRecycleSalvageValues. Supporter-pack
# pieces carry none: they count as expensive (PACK_VALUE), as do tiered cosmetics.
PACK_VALUE = 400
value = {}
for gsrow, a, b in db.execute("select MtxTypeGameSpecific, __col1, __col2 from MicrotransactionRecycleSalvageValues"):
    if gsrow is not None and 0 <= gsrow < len(gs_rows):
        value[gs_rows[gsrow]] = max(a or 0, b or 0)
tiered = set()
for (mtx,) in db.execute("select MTX from TieredMicrotransactions"):
    if mtx is not None:
        r = db.execute("select HASH32 from MtxTypes where _row = ?", (mtx,)).fetchone()
        if r: tiered.add(r[0])

def source_of(name, mid, category):
    text = f"{name} {mid}"
    for pattern, source in THEMES:
        if re.search(pattern, text, re.I):
            return source
    return None

def set_of(name):
    s = re.sub(SLOT_WORDS, "", name or "", flags=re.I).strip(" -'s")
    return s or name

def spread(key, pool):
    return pool[zlib.crc32(key.encode()) % len(pool)]

# Rarer kinds (whole hideouts, armour pieces, pets, weapon effects) weigh less: a source's drop picks by weight.
WEIGHT = {"Armour Skin": 6, "Helmet Skin": 8, "Gloves Skin": 8, "Boots Skin": 8, "Weapon Skin": 8, "Shield Skin": 8,
          "Weapon Effect": 6, "Weapon Added Effect": 6, "Pet": 5, "Portal": 7, "Character Effect": 6, "Hideout Item": 12,
          "Footprints": 7, "Apparition": 6, "Armour Attachment": 7, "Helmet Attachment": 8, "Skill Gem Effect": 10,
          "Social Frame": 12, "Consumable": 14, "Account": 2}
COPIES = {"Pet": 2, "Weapon Effect": 2, "Weapon Added Effect": 2}

out = []
skipped = {"free decoration": 0, "test row": 0, "dance": 0, "no name": 0}
for row, mid, h, name in db.execute("select _row, Id, HASH32, Name from MtxTypes"):
    if h not in gs:
        continue
    cat = CATEGORY.get(gs[h], "Miscellaneous")
    if not name:
        skipped["no name"] += 1; continue
    if re.search(r"\[DNT\]|\bTest\b|UNUSED", name):
        skipped["test row"] += 1; continue
    if cat == "Dance":
        skipped["dance"] += 1; continue
    if h in free:
        skipped["free decoration"] += 1; continue
    entry = {"hash": h, "name": name, "kind": cat, "set": set_of(name)}
    v = value.get(h, 0)
    if h in tiered: v = max(v, 1000)
    if v == 0 and cat in ("Armour Skin", "Helmet Skin", "Gloves Skin", "Boots Skin", "Weapon Skin", "Shield Skin", "Armour Attachment", "Pet", "Portal", "Character Effect", "Weapon Effect"):
        v = PACK_VALUE
    entry["value"] = v
    if re.search(r"Invisible", name, re.I):
        entry["always"] = True
        entry["copies"] = 100
    else:
        src = source_of(name, mid, cat) or spread(entry["set"], TILESETS)
        entry["source"] = src
        entry["weight"] = WEIGHT.get(cat, 8)
        if cat in COPIES:
            entry["copies"] = COPIES[cat]
    out.append(entry)

# Hideouts: the ones bought (UnlockMtx) and the ones found in maps (Rarity above 0); the default and campaign ones stay.
hideouts = []
for row, area, rarity, unlock in db.execute("select _row, HideoutArea, Rarity, UnlockMtx from Hideouts"):
    wa = db.execute("select Id, Name from WorldAreas where _row = ?", (area,)).fetchone()
    if not wa:
        continue
    kind = "bought" if unlock is not None else "found" if rarity and rarity > 0 else None
    if kind is None:
        continue
    src = source_of(wa[1], wa[0], "Hideout") or spread(wa[1], TILESETS)
    hideouts.append({"row": row, "name": wa[1], "kind": kind, "source": src, "weight": 3 if kind == "bought" else 4, "value": 600 if kind == "bought" else 150})

json.dump({
    "about": "Made by tools/make_cosmetics.py from the client's tables. Each cosmetic's source is where it can be target-farmed; settings can move any of them.",
    "sources": [dict({"id": s[0], "label": s[1], "kind": s[2], "match": s[3]}, **({"fallback": FALLBACK[s[0]]} if s[0] in FALLBACK else {})) for s in SOURCES],
    "cosmetics": out, "hideouts": hideouts}, open(sys.argv[2], "w"), indent=0, ensure_ascii=False)
# The game's "Challenge Complete!" banner shows an Achievements row's description: one hidden row per cosmetic and
# hideout, which the mod sends (S->C 0x124) for a hideout. They copy a dead 2020 Flashback event's row (set 21, one of
# the client's own sets, never shown now): a set number past the client's (250) was a suspect in a crash at character
# select that turned out to be new Stats rows.
import os
notices = []
def notice(rid, text, hide_items=1):
    notices.append({"table": "Achievements", "copyOf": {"Id": "Flashback2ReachLevel35"},
                    "set": {"Id": rid, "Description": text, "Objective": "", "HideAchievementItems": hide_items}})
# The reward toast on the right of the screen (the one a league challenge reward gets, with the cosmetic's own icon):
# one hidden challenge step per cosmetic and its AchievementItemRewards row naming its MtxTypeGameSpecific row. The mod sends the
# step's completion (S->C 0x125); the client shows "{challenge}: {step} (Complete)" and the reward toast with the icon.
mtx_id = {h: mid for mid, h in db.execute("select Id, HASH32 from MtxTypes")}
for i, e in enumerate(out):
    rid = f"LocalExileCosmetic{i}"
    notice(rid, "New cosmetic", hide_items=0)
    notices.append({"table": "AchievementItems", "copyOf": {"Id": "Flashback2ReachLevel35"},
                    "set": {"Id": f"LocalExileCosmeticItem{i}", "Name": e["name"],
                            "AchievementsKey": {"row": {"table": "Achievements", "Id": rid}}}})
    notices.append({"table": "AchievementItemRewards", "copyOf": {"Id": "KoreanHat"},
                    "set": {"Id": f"LocalExileCosmeticReward{i}", "Message": f"{e['name']} unlocked!",
                            "AchievementItemsKey": {"row": {"table": "AchievementItems", "Id": f"LocalExileCosmeticItem{i}"}},
                            # Rewards is a MtxTypeGameSpecific row (not a MtxTypes one): the one whose Type is the cosmetic.
                            "Rewards": {"row": {"table": "MtxTypeGameSpecific", "Type": {"row": {"table": "MtxTypes", "Id": mtx_id[e["hash"]]}}}}}})
for h in hideouts:
    notice(f"LocalExileHideout{h['row']}", f"Hideout unlocked: {h['name']}")
if os.environ.get("BANNERS") == "0": notices = []
folder = os.path.join(os.path.dirname(os.path.abspath(sys.argv[2])), "content")
os.makedirs(folder, exist_ok=True)
with open(os.path.join(folder, "cosmeticnotices.json"), "w") as f:
    f.write("// Cosmetic Drops' unlock banners: one hidden Achievements row per cosmetic and hideout (made by tools/make_cosmetics.py).\n")
    json.dump({"rows": notices}, f, ensure_ascii=False)

from collections import Counter
print(len(out), "cosmetics,", len(hideouts), "hideouts; left out:", skipped)
c = Counter(e.get("source", "always") for e in out)
print(sorted(c.items(), key=lambda x: -x[1]))
print(Counter(h["kind"] for h in hideouts))
