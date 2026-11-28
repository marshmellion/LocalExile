"""Builds Exciting Items' legacy uniques (legacyuniques.json) from Path of Building's unique export
(src/Export/Uniques/*.lua: names, bases and Mods.dat ids per variant, with PoB's [min,max] where a roll differs).

Kept: every unique with more than one variant (its old versions), and every unique PoB marks "No longer obtainable".
Each variant lists its base (BaseItemTypes Id), its implicits and explicits as Mods ids with per-stat ranges. A
variant whose modifiers the client no longer has is left out; a unique with no variant left is left out.

Usage: python tools/make_legacy_uniques.py <PoB folder> <client.db> <world.db>
"""
import json, os, re, sqlite3, sys
from collections import defaultdict

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
VARIANT = re.compile(r"^\{variant:([^}]*)\}(.+)$")
MOD = re.compile(r"^([A-Za-z0-9_]+)((?:\[-?\d+,-?\d+\])*)$")
RANGE = re.compile(r"\[(-?\d+),(-?\d+)\]")
META = re.compile(r"^(Source|League|Radius|Limited to|Variant|Selected Variant|Selected Alt Variant[ A-Za-z]*|Upgrade|DropLevel|Level|"
                  r"LevelReq|Requires Level|Item Level|Crafted|Implicits|Has Alt Variant[ A-Za-z]*|Sockets|Energy Shield|Armour|Evasion|Quality):")


def tagged(line):
    m = VARIANT.match(line)
    return (set(int(x) for x in m.group(1).split(",") if x.strip().isdigit()), m.group(2)) if m else (None, line)


def main(pob, client_db, world_db):
    c = sqlite3.connect(client_db)
    mods = {r[0] for r in c.execute("select Id from Mods")}
    bases = defaultdict(list)
    for r in c.execute("select Id, Name from BaseItemTypes where Id like 'Metadata/Items/%'"):
        bases[r[1]].append(r[0])
    w = sqlite3.connect(world_db)
    current = defaultdict(list)
    for r in w.execute("select name, base_type from unique_items"):
        current[r[0]].append(r[1])

    out, skipped = [], defaultdict(int)
    folder = os.path.join(pob, "src", "Export", "Uniques")
    for file in sorted(os.listdir(folder)):
        if not file.endswith(".lua") or file == "ModTextMap.lua":
            continue
        text = open(os.path.join(folder, file), encoding="utf-8").read()
        for block in re.findall(r"\[\[\n(.*?)\n\]\]", text, re.S):
            lines = [l.strip() for l in block.split("\n") if l.strip()]
            if len(lines) < 2:
                continue
            name = lines[0]
            body = lines[1:]
            base_lines, meta, rest = [], [], []
            for l in body:
                if META.match(l) or l in ("Corrupted", "Mirrored"):
                    meta.append(l)
                elif not rest and not meta and (tagged(l)[1] in bases or not MOD.match(tagged(l)[1])):
                    base_lines.append(l)
                elif not rest and tagged(l)[1] in bases and not MOD.match(tagged(l)[1]):
                    base_lines.append(l)
                else:
                    rest.append(l)
            variant_names = [l.split(":", 1)[1].strip() for l in meta if l.startswith("Variant:")]
            removed = any(l.startswith("Source:") and "no longer obtainable" in l.lower() for l in meta)
            if len(variant_names) < 2 and not removed:
                continue
            if not variant_names:
                variant_names = ["Legacy"]
            implicits = next((int(l.split(":")[1]) for l in meta if l.startswith("Implicits:")), 0)
            corrupted = "Corrupted" in meta
            variants = []
            for i, vname in enumerate(variant_names, start=1):
                def take(line):
                    vs, t = tagged(line)
                    return t if vs is None or i in vs else None
                base_name = next((t for t in reversed([take(l) for l in base_lines]) if t), None)
                if base_name is None or base_name not in bases:
                    skipped["base"] += 1
                    continue
                own = current.get(name, [])
                base_id = next((b for b in bases[base_name] if b in own), bases[base_name][0])
                chosen = [t for t in (take(l) for l in rest) if t]
                n_impl = sum(1 for l in rest[:implicits] if take(l))
                lines_ok, parsed = True, []
                for k, t in enumerate(chosen):
                    m = MOD.match(t)
                    if not m:
                        lines_ok = False
                        break
                    if m.group(1) not in mods:
                        lines_ok = False
                        break
                    parsed.append({"id": m.group(1), "implicit": k < n_impl,
                                   "ranges": [[int(a), int(b)] for a, b in RANGE.findall(m.group(2))]})
                if not lines_ok or not parsed:
                    skipped["mods"] += 1
                    continue
                variants.append({"name": vname, "base": base_id, "mods": parsed})
            if not variants:
                continue
            out.append({"name": name, "kind": file[:-4], "removed": removed, "corrupted": corrupted, "inGame": name in current,
                        "variants": variants})
    out.sort(key=lambda u: u["name"])
    with open(os.path.join(HERE, "legacyuniques.json"), "w", encoding="utf-8") as f:
        json.dump({"_note": "Exciting Items' legacy uniques (tools/make_legacy_uniques.py, from Path of Building's export). Each: its "
                            "variants oldest first (the last is the current one when the unique is still in the game), with base "
                            "and Mods ids (ranges: [min,max] per stat where they differ from Mods.dat). removed: no longer obtainable.",
                   "uniques": out}, f, indent=1, ensure_ascii=False)
    old = sum(1 for u in out if len(u["variants"]) > 1)
    print(f"{len(out)} uniques ({old} with old versions, {sum(1 for u in out if u['removed'])} removed), skipped {dict(skipped)}")


if __name__ == "__main__":
    main(*sys.argv[1:4])
