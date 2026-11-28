#!/usr/bin/env python3
"""Builds Body Auras' content pack from the game's weapon effects and character effects.

make_auras.py --client <LocalExile folder>
    reads the effects from that LocalExile's client (through its content patcher) and data/client.db, and writes this
    mod's content/; the setup runs it, since the game's effect files are not ours to share.
make_auras.py <extracted game files> <client.db> <the mod's folder> [Cosmetic Drops' cosmetics.json]

Every weapon effect (MtxTypeGameSpecific Category 2) becomes a body aura:
- a character effect "<Name> Body Aura" in the Cosmetics tab's Character Effect slot (MtxTypes, MtxTypeGameSpecific,
  ItemVisualIdentity, ItemisedVisualEffect rows, copies of the Celestial Character Effect's);
- a buff with the same look (BuffVisuals: a player version and a monster version; BuffDefinitions: an invisible buff
  that only shows it), which the server mod puts on minions, pets, totems, traps and mines.
Every character effect (Category 16) gets a monster version of its look as a buff too, so minions can wear the
character effect your character wears.

The looks: the effect's material passes (the glow over the model's surface) and its particles, moved from a weapon's
points to a body's: the player's arms, legs and body, and "frameupper", which every monster has. A weapon's particles
spawn along part of a blade, so the body copies spawn along the whole line, more often and a little bigger.

Also writes auras.index.json (what the server mod reads) and cosmeticdrops.json (Cosmetic Drops adds the body auras to
its drops, from the same places as their weapon effects).
"""
import json, os, re, sqlite3, struct, sys, hashlib, subprocess, tempfile

PLAYER_PARTICLES = ["full_L_arm", "full_R_arm", "full_L_leg", "full_R_leg", "bodyfx"]
PLAYER_TRAILS = ["Larmfx", "Rarmfx"]
MONSTER_GROUP = "frameupper"

TEMPLATE_MTX = "Metadata/Items/MicrotransactionCharacterEffects/MicrotransactionCelestialCharacterEffect"
TEMPLATE_MTX_HASH = 529136631
TEMPLATE_IDENTITY = "AddedEffectCelestialCharacterEffect"
TEMPLATE_BUFF = "visual_display_buff_removed_on_death"
TEMPLATE_BUFF_VISUAL = "celestial_spectres"
OUT_DIR = "Metadata/Effects/LocalExile/BodyAuras"
WEAPON_PREFERENCE = ["OneHandedSwordEPKFile", "TwoHandedSwordEPKFile", "TwoHandedStaffEPKFile", "OneHandedAxeEPKFile",
                     "TwoHandedAxeEPKFile", "OneHandedMaceEPKFile", "TwoHandedMaceEPKFile", "ClawEPKFile", "DaggerEPKFile",
                     "BowEPKFile"]

BODY_EMIT_SCALE = 1.0
BODY_RATE = 3.0
BODY_SIZE = 1.4


def murmur2(data: bytes) -> int:
    m = 0x5bd1e995
    h = len(data) & 0xffffffff
    i = 0
    while len(data) - i >= 4:
        k = struct.unpack_from("<I", data, i)[0]
        k = (k * m) & 0xffffffff; k ^= k >> 24; k = (k * m) & 0xffffffff
        h = (h * m) & 0xffffffff; h ^= k
        i += 4
    rest = len(data) - i
    if rest == 3: h ^= data[i + 2] << 16
    if rest >= 2: h ^= data[i + 1] << 8
    if rest >= 1:
        h ^= data[i]; h = (h * m) & 0xffffffff
    h ^= h >> 13; h = (h * m) & 0xffffffff; h ^= h >> 15
    return h


def read_text(path):
    b = open(path, "rb").read()
    return b.decode("utf-16") if b[:2] in (b"\xff\xfe", b"\xfe\xff") else b.decode("utf-8", "replace")


def write_text(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as f:
        f.write(b"\xff\xfe" + text.replace("\r\n", "\n").replace("\n", "\r\n").encode("utf-16-le"))


def passes(source, main):
    """The effect's material passes, the first one main when asked (on a monster the main pass shows, an extra one
    under a spectre's spectral pass was too faint)."""
    m = re.search(r"RenderPasses\s*\{(.*?)\n\}", source, re.S)
    if not m: return ""
    block = json.loads("{" + m.group(1) + "}")
    for k, p in enumerate(block["passes"]):
        p["is_main"] = main and k == 0
    return "RenderPasses \n" + json.dumps(block, indent=4) + "\n"


def lines_of(source, kind):
    """(groups, file, rest) of each line of a kind, e.g. ParticleEffect "blade" "x.pet"."""
    out = []
    for m in re.finditer(r'^%s\s+"([^"]*)"\s+(?:(\d+)\s+)?"([^"]+)"(.*)$' % kind, source, re.M):
        out.append((m.group(1), m.group(3), m.group(2), m.group(4).strip()))
    return out


def body_pet(text):
    """A weapon particle file made for a body: every emitter spawns along its whole line, more often, a little bigger.
    Plain lines (7 values) and curves (1 7 t v "Linear" ...) both."""
    out = []
    for line in text.replace("\r\n", "\n").split("\n"):
        m = re.match(r"^(\s*)(emit_scale|particles_per_second|scale)\s+(.*)$", line)
        if m:
            name, toks = m.group(2), m.group(3).split()
            def change(v):
                if name == "emit_scale": return BODY_EMIT_SCALE
                return v * (BODY_RATE if name == "particles_per_second" else BODY_SIZE)
            try:
                if all(re.fullmatch(r"-?[0-9.eE+-]+", t) for t in toks):
                    toks = [f"{change(float(t)):g}" for t in toks]
                elif len(toks) >= 5 and toks[0] == "1":
                    # A curve: "1 <count>" then (time, value, "interp") for each point.
                    for k in range(3, len(toks), 3):
                        toks[k] = f"{change(float(toks[k])):g}"
                line = m.group(1) + name + " " + " ".join(toks)
            except ValueError:
                pass
        out.append(line)
    return "\n".join(out)


def title_of(mtx_id, drop):
    piece = mtx_id.rsplit("/", 1)[-1]
    piece = re.sub(r"^Microtransaction", "", piece)
    for suffix in drop:
        if piece.endswith(suffix) and len(piece) > len(suffix): piece = piece[: -len(suffix)]; break
    return re.sub(r"[^A-Za-z0-9]", "", piece)


def export_from_client(root, paths, to):
    """The files as the client has them (before any patch), through LocalExile's content patcher, 60 at a time."""
    project = os.path.join(root, "tools", "ContentPatch")
    # CONTENTPATCH_DLL: a built ContentPatch.dll instead of building the project (tests).
    command = ["dotnet", os.environ["CONTENTPATCH_DLL"]] if os.environ.get("CONTENTPATCH_DLL") else ["dotnet", "run", "--project", project, "-c", "Release", "--"]
    for i in range(0, len(paths), 60):
        r = subprocess.run(command + ["export", to] + paths[i:i + 60],
                           cwd=root, capture_output=True, text=True)
        if r.returncode != 0:
            raise SystemExit("make_auras: the content patcher could not read the client:\n" + r.stdout[-2000:] + r.stderr[-2000:])
        print(f"make_auras: read {min(i + 60, len(paths))} of {len(paths)} files from the client", flush=True)


def main():
    client_root = None
    if len(sys.argv) > 2 and sys.argv[1] == "--client":
        client_root = os.path.abspath(sys.argv[2])
        game = tempfile.mkdtemp(prefix="bodyauras")
        dbpath = os.path.join(client_root, "data", "client.db")
        mod = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
        cosmetics = os.path.join(client_root, "Mods", "Binaries", "cosmeticdrops", "cosmetics.json")
        if not os.path.exists(dbpath): raise SystemExit(f"make_auras: {dbpath} is missing: start the game once first")
    else:
        game, dbpath, mod = sys.argv[1], sys.argv[2], sys.argv[3]
        cosmetics = sys.argv[4] if len(sys.argv) > 4 else ""
    drops = {}
    if cosmetics and os.path.exists(cosmetics):
        drops = {c["hash"]: c for c in json.load(open(cosmetics))["cosmetics"]}
    db = sqlite3.connect(dbpath).cursor()
    cols = [r[1] for r in db.execute("pragma table_info('ItemVisualEffect')")]

    weapons, seen = [], set()
    for r in db.execute("""select t.Id, t.Name, t.HASH32, m.DDSFile, v.* from ItemisedVisualEffect i
            join MtxTypeGameSpecific m on m._row = i.Mtx join MtxTypes t on t._row = m.Type
            join ItemVisualEffect v on v._row = i.VisualEffect
            where i.VisualEffect is not null and m.Category = 2 and t.Id not like '%LocalExile%' order by t._row"""):
        mtx, name, h, icon = r[:4]
        v = dict(zip(cols, r[4:]))
        if h in seen: continue
        seen.add(h)
        epk = next((v[p] for p in WEAPON_PREFERENCE if v.get(p)), None) or next(iter(json.loads(v["Shield"] or "[]")), None)
        if epk: weapons.append((mtx, name, h, icon, epk))
    characters = db.execute("""select t.Id, t.Name, t.HASH32, v.EPKFile from ItemisedVisualEffect i
            join MtxTypeGameSpecific m on m._row = i.Mtx join MtxTypes t on t._row = m.Type
            join ItemVisualIdentity v on v._row = i.VisualIdentity
            where m.Category = 16 and v.EPKFile != '' and t.Id not like '%LocalExile%' order by t._row""").fetchall()

    if client_root:
        # The effect files first, then the particle files they name.
        epks = sorted({w[4] for w in weapons} | {c[3] for c in characters})
        export_from_client(client_root, epks, game)
        pets = set()
        for e in epks:
            path = os.path.join(game, e)
            if os.path.exists(path):
                pets.update(p for _, p, _, _ in lines_of(read_text(path), "ParticleEffect"))
        export_from_client(client_root, sorted(pets), game)

    effects_dir = os.path.join(mod, "content", "effects")
    if os.path.isdir(effects_dir):
        for f in os.listdir(effects_dir): os.remove(os.path.join(effects_dir, f))
    pack = {"rows": [], "files": []}
    index, extra_drops, used_keys, used_names = [], [], set(), set()
    body_pets = {}

    def pet_copy(src):
        if src not in body_pets:
            local = "p_" + hashlib.md5(src.encode()).hexdigest()[:10] + "_" + os.path.basename(src)
            path = os.path.join(game, src)
            if not os.path.exists(path): body_pets[src] = src; return src
            write_text(os.path.join(effects_dir, local), body_pet(read_text(path)))
            pack["files"].append({"path": f"{OUT_DIR}/{local}", "from": f"effects/{local}"})
            body_pets[src] = f"{OUT_DIR}/{local}"
        return body_pets[src]

    def unique_key(base):
        key, n = base, 2
        while key in used_keys: key, n = f"{base}{n}", n + 1
        used_keys.add(key)
        return key

    def buff_rows(buff, player_epk, monster_epk):
        return [
            {"table": "BuffVisuals", "copyOf": {"Id": TEMPLATE_BUFF_VISUAL},
             "set": {"Id": buff, "EPKFile": player_epk, "EPKFiles1": [monster_epk] * 3}},   # small, medium, large
            {"table": "BuffDefinitions", "copyOf": {"Id": TEMPLATE_BUFF},
             "set": {"Id": buff, "BuffVisualsKey": {"row": {"table": "BuffVisuals", "Id": buff}}}},
        ]

    skipped = []
    for mtx, name, h, icon, epk in weapons:
        path = os.path.join(game, epk)
        if not os.path.exists(path): skipped.append(name); continue
        src = read_text(path)
        key = unique_key(title_of(mtx, ["WeaponEffect", "Weapon"]).lower())
        title = title_of(mtx, ["WeaponEffect", "Weapon"]) + key[len(title_of(mtx, ["WeaponEffect", "Weapon"])):]
        pets = [p for _, p, _, _ in lines_of(src, "ParticleEffect")]
        pets = list(dict.fromkeys(pet_copy(p) for p in pets))
        trails = list(dict.fromkeys(t for _, t, _, _ in lines_of(src, "TrailEffect")))
        player_epk, monster_epk = f"{OUT_DIR}/{key}_character.epk", f"{OUT_DIR}/{key}_monster.epk"
        text = passes(src, True)
        text += "".join(f'ParticleEffect "{g}" "{p}"\n' for p in pets for g in PLAYER_PARTICLES)
        text += "".join(f'TrailEffect "{g}" "{t}"\n' for t in trails for g in PLAYER_TRAILS)
        write_text(os.path.join(effects_dir, f"{key}_character.epk"), text)
        text = passes(src, True) + "".join(f'ParticleEffect "{MONSTER_GROUP}" "{p}"\n' for p in pets)
        write_text(os.path.join(effects_dir, f"{key}_monster.epk"), text)
        pack["files"] += [{"path": player_epk, "from": f"effects/{key}_character.epk"},
                          {"path": monster_epk, "from": f"effects/{key}_monster.epk"}]

        aura_name = name.replace("Weapon Effect", "Body Aura") if "Weapon Effect" in name else name.strip() + " Body Aura"
        n = 2
        while aura_name in used_names: aura_name, n = re.sub(r" \(\d+\)$", "", aura_name) + f" ({n})", n + 1
        used_names.add(aura_name)
        mtx_id = f"Metadata/Items/MicrotransactionCharacterEffects/LocalExile{title}BodyAura"
        mtx_hash = murmur2(mtx_id.encode())
        identity = f"LocalExile{title}BodyAura"
        buff = f"localexile_{key}_body_aura"
        pack["rows"] += [
            {"table": "MtxTypes", "copyOf": {"Id": TEMPLATE_MTX},
             "set": {"Id": mtx_id, "HASH32": mtx_hash, "Name": aura_name,
                     "Description1": f"Wraps your character in the look of the {name}.",
                     "__col18": identity}},
            {"table": "MtxTypeGameSpecific", "copyOf": {"HASH32": TEMPLATE_MTX_HASH},
             "set": {"Type": {"row": {"table": "MtxTypes", "Id": mtx_id}}, "HASH32": mtx_hash, "DDSFile": icon}},
            {"table": "ItemVisualIdentity", "copyOf": {"Id": TEMPLATE_IDENTITY},
             "set": {"Id": identity, "EPKFile": player_epk}},
            {"table": "ItemisedVisualEffect",
             "copyOf": {"Mtx": {"row": {"table": "MtxTypeGameSpecific", "HASH32": TEMPLATE_MTX_HASH}}},
             "set": {"Mtx": {"row": {"table": "MtxTypeGameSpecific", "HASH32": mtx_hash}},
                     "VisualIdentity": {"row": {"table": "ItemVisualIdentity", "Id": identity}},
                     # The game's character effects name the Belt class here, and the Cosmetics tab then refused this
                     # one as "Type Mismatch"; empty (as the Aura Effects have it) fits.
                     "ItemClasses": []}},
        ] + buff_rows(buff, player_epk, monster_epk)
        index.append({"key": key, "name": aura_name, "kind": "weapon", "buff": buff, "cosmetic": mtx_hash, "weapon": h})
        if h in drops:
            d = drops[h]
            extra_drops.append({"hash": mtx_hash, "name": aura_name, "kind": "Character Effect", "set": d.get("set", aura_name),
                                "value": d.get("value", 100), "source": d.get("source"), "weight": d.get("weight", 6)})

    for mtx, name, h, epk in characters:
        path = os.path.join(game, epk)
        if not os.path.exists(path): skipped.append(name); continue
        src = read_text(path)
        title = title_of(mtx, ["CharacterEffect", "Character"])
        key = unique_key("ce_" + title.lower())
        monster_epk = f"{OUT_DIR}/{key}_monster.epk"
        text = passes(src, True)
        files = list(dict.fromkeys(p for _, p, _, _ in lines_of(src, "ParticleEffect")))
        text += "".join(f'ParticleEffect "{MONSTER_GROUP}" "{p}"\n' for p in files)
        for kind in ("AttachedObject", "AttachedObjectEx"):
            for group, f, number, rest in lines_of(src, kind):
                g = group if group in ("", "<root>") else MONSTER_GROUP
                text += f'{kind} "{g}" ' + (f"{number} " if number else "") + f'"{f}"' + (f" {rest}" if rest else "") + "\n"
        write_text(os.path.join(effects_dir, f"{key}_monster.epk"), text)
        pack["files"].append({"path": monster_epk, "from": f"effects/{key}_monster.epk"})
        buff = f"localexile_{key}_body_aura"
        # The player version is the character effect's own look.
        pack["rows"] += buff_rows(buff, epk, monster_epk)
        index.append({"key": key, "name": name, "kind": "character", "buff": buff, "cosmetic": h})

    header = "// Made by tools/make_auras.py: change that and run it again rather than editing this.\n"
    with open(os.path.join(mod, "content", "auras.json"), "w") as f:
        f.write(header + json.dumps(pack, indent=1) + "\n")
    with open(os.path.join(mod, "auras.index.json"), "w") as f:
        json.dump({"about": "Body Auras' auras (made by tools/make_auras.py): key, name, kind (weapon: a weapon effect's "
                   "body aura and its cosmetic; character: a character effect's minion look), buff Id, cosmetic MTX hash.",
                   "auras": index}, f, indent=1)
    with open(os.path.join(mod, "cosmeticdrops.json"), "w") as f:
        json.dump({"about": "Cosmetic Drops adds these to its drops (each body aura where its weapon effect drops).",
                   "cosmetics": extra_drops}, f, indent=1)
    print(f"{sum(1 for i in index if i['kind'] == 'weapon')} body auras, "
          f"{sum(1 for i in index if i['kind'] == 'character')} character effect minion looks, "
          f"{len(body_pets)} particle files, {len(pack['files'])} files, {len(pack['rows'])} rows; skipped: {skipped}")


if __name__ == "__main__":
    main()
