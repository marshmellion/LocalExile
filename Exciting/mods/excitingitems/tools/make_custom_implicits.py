"""Builds Exciting Items' custom implicits: trigger + effect combinations made of effect-rule parts, about 100 per gear
slot, each slot with its own design (damage on weapons and gloves, movement on boots, buffs and auras on helmets and
off hands, defences on body armours, flasks on belts ...).

Writes
  content/customimplicits.json   the client pack: per combination a stat, its text and three tiers of implicit
                                 modifier; per timed boost a buff of its own
  customimplicits.json           the mod's list: the combinations with their rules, and each slot's list

Run again after changing the lists below. Ids come from the names, so items keep their modifiers; a combination two
slots share is one stat and one set of modifiers.
"""
import json, os, random, re

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PER_SLOT = 100
# The pair of existing item stats every custom implicit rides on (no new Stats rows: the client cannot take them). Both
# are local, not virtual, without text and used by nothing in the game's tables or the server.
INDEX = "local_display_trigger_level_x_blade_vortex_every_second"
VALUE = "local_maximum_brequel_resource_+%"

# ---- triggers: id -> (effect-rule trigger, conditions, text after the effect, the enemy's name or None, frequent) ----
TRIGGERS = {
    "hit": ("hit", [], "on Hit", "Enemies", True),
    "crit": ("hit", [(0, "critical", False, {})], "on Critical Strike", "Enemies", False),
    "kill": ("kill", [], "on Kill", None, False),
    "hit_rare": ("hit", [(0, "enemy_rarity", False, {"is": "rare_or_unique"})], "on Hitting a Rare or Unique Enemy", "Enemies", True),
    "kill_rare": ("kill", [(0, "enemy_rarity", False, {"is": "rare_or_unique"})], "on Killing a Rare or Unique Enemy", None, False),
    "when_hit": ("when_hit", [], "when Hit", "Attackers", False),
    "when_crit": ("when_hit", [(0, "critical", False, {})], "when Hit by a Critical Strike", "Attackers", False),
    "block": ("block", [], "when you Block", "Attackers", False),
    "stun": ("stun", [], "when you Stun an Enemy", None, False),
    "flask": ("flask_use", [], "when you use a Flask", None, False),
    "life_flask": ("flask_use", [(0, "flask_kind", False, {"is": "life"})], "when you use a Life Flask", None, False),
    "mana_flask": ("flask_use", [(0, "flask_kind", False, {"is": "mana"})], "when you use a Mana Flask", None, False),
    "utility_flask": ("flask_use", [(0, "flask_kind", False, {"is": "utility"})], "when you use a Utility Flask", None, False),
    "skill": ("skill_use", [(0, "cooldown", False, {"seconds": {"fixed": 1}})], "when you use a Skill", None, True),
    "warcry": ("skill_use", [(0, "skill_type", False, {"is": "warcry"})], "when you use a Warcry", None, False),
    "movement_skill": ("skill_use", [(0, "skill_type", False, {"is": "movement"})], "when you use a Movement Skill", None, False),
    "spell_cast": ("skill_use", [(0, "skill_type", False, {"is": "spell"}), (0, "cooldown", False, {"seconds": {"fixed": 1}})], "when you Cast a Spell", None, True),
    "attack_use": ("skill_use", [(0, "skill_type", False, {"is": "attack"}), (0, "cooldown", False, {"seconds": {"fixed": 1}})], "when you Attack", None, True),
    "spell_hit": ("hit", [(0, "skill_type", False, {"is": "spell"})], "on Spell Hit", "Enemies", True),
    "melee_hit": ("hit", [(0, "skill_type", False, {"is": "melee"})], "on Melee Hit", "Enemies", True),
}

LEVELS = [1, 45, 75]
CHANCE = [(5, 10), (11, 16), (17, 25)]
CHANCE_FREQUENT = [(2, 4), (5, 7), (8, 12)]
CHANCE_STRONG = [(2, 4), (5, 8), (9, 13)]


def timed(seconds):
    return [{"timer": "duration", "seconds": {"fixed": seconds}}]


# ---- effects: id -> dict(kind, category, do, text, tiers, enemy?) ----
# kind "chance": "{0}% chance to <text> <trigger>"; "boost": "<text with {0}> for N seconds <trigger>";
# "amount": "<text with {0}> <trigger>"; "flag": "<text> <trigger>" (no number).
EFFECTS = {}


def effect(id, kind, category, do, text, tiers=None, enemy=False, strong=False):
    EFFECTS[id] = dict(id=id, kind=kind, category=category, do=do, text=text, tiers=tiers, enemy=enemy, strong=strong)


for name, buff, text in [("onslaught", "onslaught", "gain Onslaught for 4 seconds"), ("arcane_surge", "arcane_surge", "gain Arcane Surge for 4 seconds"),
                         ("phasing", "phasing", "gain Phasing for 4 seconds"), ("unholy_might", None, "gain Unholy Might for 4 seconds")]:
    args = {"buff": buff} if buff else {"buff": "old_unholy_might", "values": [30]}
    effect(name, "chance", "buffs", ("buff", args, "self", {}, timed(4)), text)
for charge in ["endurance", "frenzy", "power"]:
    effect(f"{charge}_charge", "chance", "charges", ("charge", {"type": charge}, "self", {"fixed": 1}, []),
           f"gain an {charge.title()} Charge" if charge == "endurance" else f"gain a {charge.title()} Charge", strong=True)

for name, debuff, secs, text in [("intimidate", "intimidated", 4, "Intimidate {who} for 4 seconds"), ("unnerve", "unnerved", 4, "Unnerve {who} for 4 seconds"),
                                 ("blind", "blinded", 4, "Blind {who} for 4 seconds"), ("maim", "maimed", 4, "Maim {who} for 4 seconds"),
                                 ("hinder", "hindered", 4, "Hinder {who} for 4 seconds"), ("crush", "crushed", 4, "Crush {who} for 4 seconds"),
                                 ("wither", "withered", 2, "inflict Withered on {who} for 2 seconds"),
                                 ("fire_exposure", "fire_exposure", 4, "inflict Fire Exposure on {who} for 4 seconds"),
                                 ("cold_exposure", "cold_exposure", 4, "inflict Cold Exposure on {who} for 4 seconds"),
                                 ("lightning_exposure", "lightning_exposure", 4, "inflict Lightning Exposure on {who} for 4 seconds")]:
    effect(name, "chance", "debuffs", ("debuff", {"debuff": debuff}, "enemy", {}, timed(secs)), text, enemy=True)
for name, secs, text in [("chill", 2, "Chill {who} for 2 seconds"), ("shock", 2, "Shock {who} for 2 seconds"), ("freeze", 1, "Freeze {who} for 1 second"),
                         ("scorch", 4, "Scorch {who} for 4 seconds"), ("brittle", 4, "inflict Brittle on {who} for 4 seconds"), ("sap", 4, "Sap {who} for 4 seconds")]:
    effect(name, "chance", "ailments", ("ailment", {"ailment": name}, "enemy", {}, timed(secs)), text, enemy=True)
for cid, cname in [("Despair", "Despair"), ("Enfeeble", "Enfeeble"), ("Vulnerability", "Vulnerability"), ("TemporalChains", "Temporal Chains"),
                   ("ElementalWeakness", "Elemental Weakness"), ("Conductivity", "Conductivity"), ("Flammability", "Flammability"),
                   ("Frostbite", "Frostbite"), ("Punishment", "Punishment"), ("random", "a random Hex")]:
    effect(f"curse_{cid.lower()}", "chance", "curses", ("curse", {"curse": cid}, "enemy", {"fixed": 10}, []), f"Curse {{who}} with {cname}",
           enemy=True, strong=True)
for pool, pname, tiers in [("life", "Life", [(1, 2), (2, 3), (3, 5)]), ("mana", "Mana", [(1, 2), (2, 4), (4, 6)]),
                           ("energy shield", "Energy Shield", [(1, 2), (2, 4), (4, 6)])]:
    effect(f"recover_{pool.replace(' ', '_')}", "amount", "recovery", ("recover", {"pool": pool, "of": "maximum"}, "self", {"from": "roll"}, []),
           f"Recover {{0}}% of {pname}", tiers)
for dtype, dname in [("physical", "Physical"), ("fire", "Fire"), ("cold", "Cold"), ("lightning", "Lightning"), ("chaos", "Chaos")]:
    effect(f"reflect_{dtype}", "amount", "reflect", ("damage", {"type": dtype}, "enemy", {"from": "roll"}, []),
           f"Reflects {{0}} {dname} Damage to Attackers", [(10, 25), (40, 80), (100, 180)], enemy=True)
for what, text in [("ignite", "Remove Ignite and Burning"), ("shock", "Remove Shock"), ("chill_freeze", "Remove Chill and Freeze"),
                   ("bleeding", "Remove Bleeding"), ("poison", "Remove Poison"), ("corrupted_blood", "Remove Corrupted Blood")]:
    effect(f"remove_{what}", "flag", "cleanse", ("remove", {"what": what}, "self", {}, []), text, [(1, 1), (1, 1), (1, 1)])

# Timed boosts: a buff of this mod's own carrying one stat; its strength is the line's roll.
BOOSTS = [
    # id, stat, text ({0} is the roll), value sign, tiers, category
    ("movement_speed", "movement_velocity_+%", "{0}% increased Movement Speed", 1, [(5, 8), (9, 12), (13, 18)], "mobility"),
    ("attack_speed", "attack_speed_+%", "{0}% increased Attack Speed", 1, [(5, 8), (9, 12), (13, 16)], "offence"),
    ("cast_speed", "base_cast_speed_+%", "{0}% increased Cast Speed", 1, [(5, 8), (9, 12), (13, 16)], "offence"),
    ("damage", "damage_+%", "{0}% increased Damage", 1, [(10, 15), (16, 25), (26, 40)], "offence"),
    ("spell_damage", "spell_damage_+%", "{0}% increased Spell Damage", 1, [(10, 15), (16, 25), (26, 40)], "offence"),
    ("attack_damage", "attack_damage_+%", "{0}% increased Attack Damage", 1, [(10, 15), (16, 25), (26, 40)], "offence"),
    ("physical_damage", "physical_damage_+%", "{0}% increased Physical Damage", 1, [(10, 20), (21, 30), (31, 45)], "offence"),
    ("fire_damage", "fire_damage_+%", "{0}% increased Fire Damage", 1, [(10, 20), (21, 30), (31, 45)], "offence"),
    ("cold_damage", "cold_damage_+%", "{0}% increased Cold Damage", 1, [(10, 20), (21, 30), (31, 45)], "offence"),
    ("lightning_damage", "lightning_damage_+%", "{0}% increased Lightning Damage", 1, [(10, 20), (21, 30), (31, 45)], "offence"),
    ("chaos_damage", "chaos_damage_+%", "{0}% increased Chaos Damage", 1, [(10, 20), (21, 30), (31, 45)], "offence"),
    ("elemental_damage", "elemental_damage_+%", "{0}% increased Elemental Damage", 1, [(10, 15), (16, 25), (26, 40)], "offence"),
    ("crit_chance", "critical_strike_chance_+%", "{0}% increased Critical Strike Chance", 1, [(20, 30), (31, 50), (51, 80)], "offence"),
    ("crit_multi", "base_critical_strike_multiplier_+", "+{0}% to Critical Strike Multiplier", 1, [(8, 12), (13, 20), (21, 30)], "offence"),
    ("accuracy", "accuracy_rating_+%", "{0}% increased Accuracy Rating", 1, [(10, 15), (16, 25), (26, 40)], "offence"),
    ("projectile_speed", "base_projectile_speed_+%", "{0}% increased Projectile Speed", 1, [(10, 15), (16, 25), (26, 35)], "offence"),
    ("area", "base_skill_area_of_effect_+%", "{0}% increased Area of Effect", 1, [(5, 8), (9, 12), (13, 18)], "offence"),
    ("minion_damage", "minion_damage_+%", "Minions deal {0}% increased Damage", 1, [(10, 15), (16, 25), (26, 40)], "offence"),
    ("cooldown", "base_cooldown_speed_+%", "{0}% increased Cooldown Recovery Rate", 1, [(5, 8), (9, 12), (13, 18)], "utility"),
    ("skill_duration", "skill_effect_duration_+%", "{0}% increased Skill Effect Duration", 1, [(5, 8), (9, 12), (13, 18)], "utility"),
    ("mana_regen", "mana_regeneration_rate_+%", "{0}% increased Mana Regeneration Rate", 1, [(15, 25), (26, 40), (41, 60)], "utility"),
    ("flask_charges", "flask_charges_gained_+%", "{0}% increased Flask Charges gained", 1, [(10, 15), (16, 25), (26, 35)], "utility"),
    ("armour", "physical_damage_reduction_rating_+%", "{0}% increased Armour", 1, [(20, 30), (31, 50), (51, 80)], "defence"),
    ("evasion", "evasion_rating_+%", "{0}% increased Evasion Rating", 1, [(20, 30), (31, 50), (51, 80)], "defence"),
    ("es_recharge", "energy_shield_recharge_rate_+%", "{0}% increased Energy Shield Recharge Rate", 1, [(10, 20), (21, 30), (31, 45)], "defence"),
    ("resists", "resist_all_elements_%", "+{0}% to all Elemental Resistances", 1, [(5, 8), (9, 12), (13, 18)], "defence"),
    ("chaos_res", "base_chaos_damage_resistance_%", "+{0}% to Chaos Resistance", 1, [(7, 11), (12, 17), (18, 25)], "defence"),
    ("block", "additional_block_%", "+{0}% Chance to Block Attack Damage", 1, [(2, 3), (4, 5), (6, 8)], "defence"),
    ("suppression", "base_spell_suppression_chance_%", "+{0}% chance to Suppress Spell Damage", 1, [(4, 6), (7, 10), (11, 15)], "defence"),
    ("less_damage_taken", "damage_taken_+%", "{0}% reduced Damage taken", -1, [(3, 4), (5, 6), (7, 10)], "defence"),
    ("avoid_stun", "base_avoid_stun_%", "{0}% chance to Avoid being Stunned", 1, [(10, 15), (16, 25), (26, 40)], "defence"),
    ("avoid_status", "avoid_all_elemental_status_%", "{0}% chance to Avoid Elemental Ailments", 1, [(10, 15), (16, 25), (26, 40)], "defence"),
    ("mana_before_life", "base_damage_removed_from_mana_before_life_%", "{0}% of Damage is taken from Mana before Life", 1, [(3, 4), (5, 6), (7, 9)], "defence"),
]
for bid, stat, text, sign, tiers, cat in BOOSTS:
    effect(f"boost_{bid}", "boost", cat, ("buff", {"buff": f"localexile_custom_boost_{bid}", "values": [100 * sign], "effect": {"from": "roll"}},
                                          "self", {}, timed(4)), text, tiers)

# ---- slots: item classes, triggers and effect categories or ids each one rolls ----
WEAPONS_1H = ["Claw", "Dagger", "Rune Dagger", "Wand", "One Hand Sword", "Thrusting One Hand Sword", "One Hand Axe", "One Hand Mace", "Sceptre"]
WEAPONS_2H = ["Bow", "Staff", "Warstaff", "Two Hand Sword", "Two Hand Axe", "Two Hand Mace"]
OFFENCE = ["boost_" + b[0] for b in BOOSTS if b[5] == "offence"]
DEFENCE = ["boost_" + b[0] for b in BOOSTS if b[5] == "defence"]
UTILITY = ["boost_" + b[0] for b in BOOSTS if b[5] == "utility"]
SLOTS = {
    "weapon_1h": dict(classes=WEAPONS_1H, triggers=["hit", "crit", "kill", "hit_rare", "kill_rare", "stun", "melee_hit", "spell_hit"],
                      effects=OFFENCE + ["onslaught", "unholy_might", "frenzy_charge", "power_charge", "intimidate", "maim", "crush", "wither",
                                         "fire_exposure", "cold_exposure", "lightning_exposure", "chill", "shock", "freeze", "scorch", "brittle", "sap",
                                         "recover_life", "recover_mana"]),
    "weapon_2h": dict(classes=WEAPONS_2H, triggers=["hit", "crit", "kill", "hit_rare", "kill_rare", "stun", "melee_hit", "warcry"],
                      effects=OFFENCE + ["onslaught", "unholy_might", "endurance_charge", "frenzy_charge", "power_charge", "intimidate", "maim",
                                         "crush", "hinder", "chill", "shock", "freeze", "scorch", "brittle", "sap", "recover_life"]),
    "gloves": dict(classes=["Gloves"], triggers=["hit", "crit", "kill", "hit_rare", "attack_use", "spell_cast", "melee_hit", "spell_hit"],
                   effects=OFFENCE + ["frenzy_charge", "power_charge", "wither", "fire_exposure", "cold_exposure", "lightning_exposure", "maim",
                                      "curse_despair", "curse_enfeeble", "curse_vulnerability", "curse_temporalchains", "curse_elementalweakness",
                                      "curse_conductivity", "curse_flammability", "curse_frostbite", "curse_punishment", "curse_random",
                                      "recover_life", "recover_mana"]),
    "boots": dict(classes=["Boots"], triggers=["when_hit", "kill", "flask", "movement_skill", "skill", "stun", "block"],
                  effects=["boost_movement_speed", "phasing", "onslaught", "frenzy_charge", "boost_evasion", "boost_suppression", "boost_avoid_stun",
                           "boost_avoid_status", "boost_cooldown", "boost_flask_charges", "remove_ignite", "remove_shock", "remove_chill_freeze",
                           "remove_bleeding", "remove_poison", "remove_corrupted_blood", "recover_life", "boost_resists"]),
    "helmet": dict(classes=["Helmet"], triggers=["crit", "kill", "when_hit", "when_crit", "spell_cast", "flask", "warcry", "skill"],
                   effects=["arcane_surge", "unholy_might", "onslaught", "power_charge", "unnerve", "blind", "intimidate", "curse_despair",
                            "curse_enfeeble", "curse_temporalchains", "curse_elementalweakness", "curse_vulnerability", "curse_punishment",
                            "curse_random", "boost_cast_speed", "boost_crit_chance", "boost_area", "boost_skill_duration", "boost_cooldown",
                            "boost_mana_regen", "boost_minion_damage", "recover_mana", "recover_energy_shield", "boost_mana_before_life"]),
    "body": dict(classes=["Body Armour"], triggers=["when_hit", "when_crit", "block", "kill", "life_flask", "flask", "warcry"],
                 effects=DEFENCE + ["endurance_charge", "recover_life", "recover_energy_shield", "reflect_physical", "reflect_fire", "reflect_cold",
                                    "reflect_lightning", "reflect_chaos", "intimidate", "blind", "chill", "unholy_might", "onslaught"]),
    "shield": dict(classes=["Shield"], triggers=["block", "when_hit", "kill", "skill", "warcry"],
                   effects=DEFENCE + ["arcane_surge", "unholy_might", "onslaught", "endurance_charge", "power_charge", "recover_life",
                                      "recover_energy_shield", "reflect_physical", "reflect_fire", "reflect_cold", "reflect_lightning",
                                      "curse_despair", "curse_enfeeble", "curse_temporalchains", "curse_punishment", "curse_random", "crush",
                                      "intimidate", "boost_spell_damage", "boost_cast_speed"]),
    "quiver": dict(classes=["Quiver"], triggers=["hit", "crit", "kill", "hit_rare", "kill_rare", "attack_use"],
                   effects=["boost_attack_speed", "boost_projectile_speed", "boost_physical_damage", "boost_elemental_damage", "boost_crit_chance",
                            "boost_crit_multi", "boost_accuracy", "boost_attack_damage", "boost_movement_speed", "frenzy_charge", "onslaught",
                            "blind", "maim", "hinder", "wither", "fire_exposure", "cold_exposure", "lightning_exposure", "chill", "shock"]),
    "amulet": dict(classes=["Amulet"], triggers=["kill", "crit", "flask", "skill", "when_hit", "kill_rare", "warcry", "hit"],
                   effects=["arcane_surge", "unholy_might", "onslaught", "phasing", "endurance_charge", "frenzy_charge", "power_charge",
                            "boost_damage", "boost_elemental_damage", "boost_crit_multi", "boost_area", "boost_cooldown", "boost_skill_duration",
                            "boost_resists", "boost_less_damage_taken", "boost_minion_damage", "recover_life", "recover_mana", "recover_energy_shield"]),
    "ring": dict(classes=["Ring"], triggers=["hit", "crit", "kill", "when_hit", "flask", "spell_hit", "hit_rare"],
                 effects=["boost_fire_damage", "boost_cold_damage", "boost_lightning_damage", "boost_chaos_damage", "boost_elemental_damage",
                          "boost_crit_chance", "boost_accuracy", "boost_mana_regen", "power_charge", "frenzy_charge", "fire_exposure", "cold_exposure",
                          "lightning_exposure", "wither", "chill", "shock", "scorch", "brittle", "sap", "freeze", "recover_life", "recover_mana",
                          "recover_energy_shield", "curse_conductivity", "curse_flammability", "curse_frostbite"]),
    "belt": dict(classes=["Belt"], triggers=["flask", "life_flask", "mana_flask", "utility_flask", "when_hit", "kill", "block"],
                 effects=DEFENCE + UTILITY + ["endurance_charge", "onslaught", "recover_life", "recover_mana", "recover_energy_shield", "boost_damage",
                                              "remove_ignite", "remove_shock", "remove_chill_freeze", "remove_bleeding", "remove_poison",
                                              "remove_corrupted_blood"]),
}


# ---- flasks: implicits a flask carries (source kind "item"), for its use or its effect ----
FLASK_CLASSES = ["LifeFlask", "ManaFlask", "HybridFlask", "UtilityFlask"]
FLASK_PER_TRIGGER = 13      # combinations of each "during effect, on <trigger>" kind
FLASK_UNIQUES = 70          # unique effects during the flask's effect
SPECIAL_BAD = re.compile(r"^local_|socketed|jewel|flask|_radius|in_radius|passive|allocated|keystone_|grants_level|display_|cannot_|"
                         r"_per_|minimum_|maximum_added|base_|added_|lose_|glorious_madness|flame_touched|while_affected_by|instead_of|"
                         r"is_equal_to|^no_|reserves|_limit|totem|minion|aura_|virtual_|unique_|_gem_level|skill_gem")


def flask_combos(rng, world_db, client_db):
    import sqlite3
    out = []
    # During the effect: a boost the roll sizes.
    for bid, stat, text, sign, tiers, cat in BOOSTS:
        out.append(dict(id=f"flask_during__{bid}", trigger="during", effect=f"boost_{bid}", category=cat, kind="during",
                        text="During Effect, " + text, ranges=tiers,
                        rules=[dict(source="item", when="active", conds=[], do=("quantity", {"stats": {stat: sign}}, "self", {"from": "roll", "scale": 100},
                                                                                     [{"timer": "source"}]))]))
    # On use: the flask's own use.
    for eid in ["onslaught", "arcane_surge", "phasing", "unholy_might", "endurance_charge", "frenzy_charge", "power_charge",
                "remove_ignite", "remove_shock", "remove_chill_freeze", "remove_bleeding", "remove_poison", "remove_corrupted_blood"]:
        e = EFFECTS[eid]
        conds = [] if e["kind"] == "flag" else [(0, "chance", False, {"percent": {"from": "roll"}})]
        text = (f"{{0}}% chance to {e['text']} when used" if e["kind"] == "chance" else f"{e['text']} when used")
        out.append(dict(id=f"flask_use__{eid}", trigger="flask_use", effect=eid, category=e["category"], kind=e["kind"], text=text,
                        ranges=e["tiers"] or CHANCE, rules=[dict(source="item", when="flask_use", conds=conds, do=e["do"])]))
    # During the effect, on a trigger: one rule on the flask, live only while the flask runs (carrier_active).
    during = {"hit": "on Hit", "crit": "on Critical Strike", "kill": "on Kill", "kill_rare": "on Killing a Rare or Unique Enemy",
              "when_hit": "when Hit", "block": "when you Block", "melee_hit": "on Melee Hit", "spell_hit": "on Spell Hit"}
    for tid, after in during.items():
        when, tconds, _, who, frequent = TRIGGERS[tid]
        effects = [e for e in EFFECTS.values() if e["kind"] in ("chance", "amount") and e["category"] != "reflect"
                   and (not e["enemy"] or who) and not (e["category"] == "recovery" and frequent)]
        rng.shuffle(effects)
        for e in effects[:FLASK_PER_TRIGGER]:
            body = e["text"].replace("{who}", who or "Enemies")
            text = (f"During Effect, {{0}}% chance to {body} {after}" if e["kind"] == "chance" else f"During Effect, {body} {after}")
            cid = f"flask_during_{tid}__{e['id']}"
            conds = list(tconds) + [(0, "carrier_active", False, {})] + ([(0, "chance", False, {"percent": {"from": "roll"}})] if e["kind"] == "chance" else [])
            ranges = e["tiers"] or (CHANCE_STRONG if e["strong"] else CHANCE_FREQUENT if frequent else CHANCE)
            out.append(dict(id=cid, trigger=tid, effect=e["id"], category=e["category"], kind=e["kind"], text=text, ranges=ranges,
                            rules=[dict(source="item", when=when, conds=conds, do=e["do"])]))
    # A unique's effect during the flask's effect, the unique's own value (its best roll): the ones the server acts on.
    w = sqlite3.connect(world_db); c = sqlite3.connect(client_db)
    handled = {r[0]: (r[1], r[2], r[3]) for r in w.execute("select stat, handled, kind, text from fx_stats")}
    stats = {r[0]: r[1] for r in c.execute("select _row, Id from Stats")}
    mods = {r[0]: r[1:] for r in c.execute("select Id, StatsKey1, StatsKey2, Stat1Min, Stat1Max from Mods")}
    weights = {}
    try:
        for r in w.execute("select name, weight from unique_weights"):
            weights[r[0]] = r[1]
    except sqlite3.Error:
        pass
    seen, uniques = set(), []
    for name, mod in w.execute("select name, mod from unique_item_mods where implicit = 0 order by name"):
        if mod not in mods or mods[mod][1] is not None:
            continue
        stat = stats.get(mods[mod][0])
        if not stat or stat in seen or SPECIAL_BAD.search(stat) or stat not in handled:
            continue
        h, k, t = handled[stat]
        if h not in ("code", "rule") or k not in ("flag", "trigger", "conditional number") or not t:
            continue
        value = mods[mod][3] if abs(mods[mod][3] or 0) >= abs(mods[mod][2] or 0) else mods[mod][2]
        shown = render(t, stat, value)
        if not shown or DOWNSIDE.search(shown):
            continue
        seen.add(stat)
        uniques.append((name, stat, value, shown))
    rng.shuffle(uniques)
    uniques.sort(key=lambda u: u[1] not in MUST)  # the favourites first, then the shuffled rest
    for name, stat, value, shown in uniques[:FLASK_UNIQUES]:
        key = re.sub(r"[^a-z0-9]+", "_", stat.lower()).strip("_")[:60]
        out.append(dict(id=f"flask_unique__{key}", trigger="during", effect="unique", category="unique_effects", kind="flag",
                        text=f"During Effect: {shown}", ranges=[(1, 1), (1, 1), (1, 1)], unique=name, weight=weights.get(name),
                        rules=[dict(source="item", when="active", conds=[], do=("quantity", {"stats": {stat: value}}, "self", {}, [{"timer": "source"}]))]))
    return out


DOWNSIDE = re.compile(r"deal no|no Damage|[Cc]annot|[Ll]ose |\bless\b|reduced|[Tt]ake |taken|[Yy]ou are |against you|Enemies have|your Life|Unaffected|Sacrifice", re.I)
MUST = {"gain_rare_monster_mods_on_kill_ms"}   # Headhunter


def render(text, stat, value):
    """A stat's text with its value: {0} and {0:+d} filled in, milliseconds and permyriad turned to the shown units."""
    v = value
    if stat.endswith("_ms") and "second" in text:
        v = value / 1000
    elif "permyriad" in stat:
        v = value / 100
    elif "per_minute" in stat and "per second" in text:
        v = value / 60
    shown = ("%g" % v)
    text = text.replace("{0:+d}", ("+" if v >= 0 else "") + shown).replace("{0}", shown)
    if "{" in text:
        return None
    return text.replace("\n", " ").replace('"', "'")


def text_of(e, trigger):
    _, _, after, who, _ = TRIGGERS[trigger]
    body = e["text"].replace("{who}", who or "Enemies")
    if e["kind"] == "chance":
        return f"{{0}}% chance to {body} {after}"
    if e["kind"] == "boost":
        return f"{body} for 4 seconds {after}"
    return f"{body} {after}"


def valid(e, trigger):
    who = TRIGGERS[trigger][3]
    if e["enemy"] and who is None:
        return False
    if e["category"] == "reflect" and trigger not in ("when_hit", "when_crit", "block"):
        return False
    if e["category"] == "recovery" and TRIGGERS[trigger][4]:
        return False  # too often for a percent of the pool
    if e["category"] == "cleanse" and trigger in ("hit", "hit_rare", "melee_hit", "spell_hit", "crit"):
        return False
    return True


def main():
    rng = random.Random(329)
    combos, slots = {}, {}
    for slot, s in SLOTS.items():
        pairs = [(t, e) for t in s["triggers"] for e in s["effects"] if valid(EFFECTS[e], t)]
        rng.shuffle(pairs)
        # Every effect at least once, then the rest up to PER_SLOT.
        chosen, seen = [], set()
        for t, e in pairs:
            if e not in seen:
                chosen.append((t, e)); seen.add(e)
        for t, e in pairs:
            if len(chosen) >= PER_SLOT:
                break
            if (t, e) not in chosen:
                chosen.append((t, e))
        slots[slot] = {"classes": s["classes"], "implicits": []}
        for t, e in sorted(chosen[:PER_SLOT]):
            cid = f"{t}__{e}"
            slots[slot]["implicits"].append(cid)
            if cid in combos:
                continue
            ef = EFFECTS[e]
            when, conds, _, _, frequent = TRIGGERS[t]
            ranges = ef["tiers"] or (CHANCE_STRONG if ef["strong"] else CHANCE_FREQUENT if frequent else CHANCE)
            combos[cid] = dict(id=cid, trigger=t, effect=e, category=ef["category"], kind=ef["kind"], when=when, conds=conds, do=ef["do"],
                               text=text_of(ef, t), ranges=ranges)

    client_db = os.environ.get("CLIENT_DB", "/home/claude/build/run20/data/client.db")
    world_db = os.environ.get("WORLD_DB", "/home/claude/build/run20/data/world.db")
    flask = flask_combos(random.Random(4), world_db, client_db)
    slots["flask"] = {"classes": FLASK_CLASSES, "implicits": [f["id"] for f in flask]}
    for f in flask:
        combos[f["id"]] = f

    rows = [{"table": "ModType", "copyOf": {"Name": "SocketedCursesUncastableUnique"},
             "set": {"Name": "LocalExileCustomImplicit", "Stats": [], "ModSellPriceTypesKeys": []}}]
    used_boosts = {c["effect"][6:] for c in combos.values() if c["kind"] == "boost"}
    for bid, stat, text, sign, tiers, cat in BOOSTS:
        if bid not in used_boosts:
            continue
        name = text.replace("{0}", "").replace("+%", "").replace("% ", " ").strip(" +%")
        rows.append({"table": "BuffDefinitions", "copyOf": {"Id": "old_unholy_might"},
                     "set": {"Id": f"localexile_custom_boost_{bid}", "Name": name[:1].upper() + name[1:],
                             "Description": "", "StatsKeys": [{"row": {"table": "Stats", "Id": stat}}]}})
    # No new stats: the client's stat list is fixed in the game (a new Stats row crashed it). Every custom implicit rides
    # on one pair of existing item (local) stats nothing else uses: INDEX says which implicit it is (1..N), VALUE holds
    # its roll. The text is one description block over the pair, a line per index; the rules are "item" rules on VALUE
    # that need INDEX to be theirs. One pair, so one custom implicit per item.
    lines, listed = [], []
    texts = []
    for i, cid in enumerate(sorted(combos), start=1):
        c = combos[cid]
        stat = VALUE
        texts.append((i, c["text"].replace("{0}", "{1}")))
        tiers = []
        for t, (level, (lo, hi)) in enumerate(zip(LEVELS, c["ranges"]), start=1):
            mod = f"LocalExileCustomImplicit_{cid}_{t}"
            rows.append({"table": "Mods", "copyOf": {"Id": "AllDefensesImplicitJetRing"}, "set": {
                "Id": mod, "Name": "", "Level": level, "Domain": 1, "GenerationType": 3,
                "ModTypeKey": {"row": {"table": "ModType", "Name": "LocalExileCustomImplicit"}},
                "StatsKey1": {"row": {"table": "Stats", "Id": INDEX}}, "Stat1Min": i, "Stat1Max": i,
                "StatsKey2": {"row": {"table": "Stats", "Id": VALUE}}, "Stat2Min": lo, "Stat2Max": hi, "StatsKey3": None, "StatsKey4": None, "StatsKey5": None, "StatsKey6": None, "StatsKey7": None, "StatsKey8": None,
                "Families": [], "TagsKeys": [], "ImplicitTagsKeys": [], "GrantedEffectsPerLevelKeys": [],
                "SpawnWeight_TagsKeys": [], "SpawnWeight_Values": [], "GenerationWeight_TagsKeys": [], "GenerationWeight_Values": [],
                "CraftingItemClassRestrictions": [], "HasHardmodeStatValues": False}})
            tiers.append({"mod": mod, "level": level, "min": lo, "max": hi})
        def rule_of(when, conds, do, source="stat", key=None):
            action, args, target, value, timers = do
            conds = list(conds) + [(0, "stat_at_least", False, {"stat": INDEX, "min": i, "max": i})]
            return {"source": "item", "key": VALUE, "when": [{"trigger": when, "args": {}}],
                    "if": [{"group": g, "condition": cond, "negate": neg, "args": a} for g, cond, neg, a in conds],
                    "do": [{"action": action, "args": args, "target": target, "targetArgs": {}, "value": value, "timers": timers}]}
        if "rules" in c:
            rules = [rule_of(r["when"], r["conds"], r["do"], r["source"], r.get("source_key")) for r in c["rules"]]
        else:
            conds = list(c["conds"]) + ([(0, "chance", False, {"percent": {"from": "roll"}})] if c["kind"] == "chance" else [])
            rules = [rule_of(c["when"], conds, c["do"])]
        entry = {"id": cid, "index": i, "trigger": c["trigger"], "effect": c["effect"], "category": c["category"], "text": c["text"], "stat": stat,
                 "tiers": tiers, "rules": rules}
        if c.get("unique"):
            entry["unique"] = c["unique"]
            entry["weight"] = c.get("weight")
        listed.append(entry)
    lines = ["description", f"\t2 {INDEX} {VALUE}", f"\t{len(texts)}"] + [f"\t\t{i} # \"{t}\"" for i, t in texts] + [""]
    pack = {"rows": rows, "files": [{"path": "Metadata/StatDescriptions/stat_descriptions.txt", "appendLines": lines}]}
    with open(os.path.join(HERE, "content", "customimplicits.json"), "w", encoding="utf-8") as f:
        f.write("// Exciting Items' custom implicits (tools/make_custom_implicits.py writes this): per combination a stat, its line\n"
                "// of text and three tiers of implicit modifier; per timed boost a buff of its own. The effects are effect rules\n"
                "// the mod adds at start.\n")
        json.dump(pack, f, indent=0, ensure_ascii=False)
    with open(os.path.join(HERE, "customimplicits.json"), "w", encoding="utf-8") as f:
        json.dump({"_note": "Exciting Items' custom implicits (tools/make_custom_implicits.py). implicits: each combination's stat, "
                            "three tiers (modifier, item level, roll range) and the effect rule the mod adds for it. slots: the item "
                            "classes of each gear slot and the combinations it rolls. The client's text comes from "
                            "content/customimplicits.json, so change both by running the tool again.",
                   "slots": slots, "implicits": listed}, f, indent=1, ensure_ascii=False)
    cats = {}
    for c in combos.values():
        cats[c["category"]] = cats.get(c["category"], 0) + 1
    print(len(combos), "combinations,", len(rows), "client rows,", {k: len(v["implicits"]) for k, v in slots.items()}, cats)


if __name__ == "__main__":
    main()
