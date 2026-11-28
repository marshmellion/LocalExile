-- Main's world rules of 2026-11-28 use ids our rules had first (a charge rule named after its stat). On a new
-- world.db main's updates run before ours, so its rows step aside here while our older files insert theirs, and
-- 74_main_rules_back.sql puts main's back in their place. A world.db that already has our rules (73 is recorded)
-- holds nothing: there main's update itself replaces ours (the DELETE at its top, 2026_11_28_070125).
CREATE TABLE fx_hold_rules AS SELECT id, book, title, replaces, status, enabled, checked, origin, note, position FROM fx_rules
WHERE NOT EXISTS (SELECT 1 FROM updates WHERE name = 'updates/pending_world/73_fx_rules_engine.sql') AND id IN (
    'add_endurance_charge_on_kill', 'add_endurance_charge_on_skill_hit_%', 'add_frenzy_charge_on_critical_strike', 'add_frenzy_charge_on_kill',
    'add_frenzy_charge_when_hit_%', 'add_power_charge_on_critical_strike_%', 'add_power_charge_on_hit_while_poisoned_%', 'chance_to_gain_endurance_charge_on_block_%',
    'chance_to_gain_endurance_charge_on_crit_%', 'chance_to_gain_endurance_charge_on_hit_%_vs_bleeding_enemy', 'chance_to_gain_endurance_charge_on_melee_crit_%', 'chance_to_gain_endurance_charge_when_hit_%',
    'chance_to_gain_endurance_charge_when_you_stun_enemy_%', 'chance_to_gain_frenzy_charge_on_block_%', 'chance_to_gain_frenzy_charge_on_block_attack_%', 'chance_to_gain_frenzy_charge_on_stun_%',
    'chance_to_gain_power_charge_on_melee_stun_%', 'chance_to_gain_power_charge_on_rare_or_unique_enemy_hit_%', 'chance_to_gain_power_charge_on_stun_%', 'chance_to_gain_power_charge_when_block_%',
    'endurance_charge_on_hit_%_vs_no_armour', 'frenzy_%_chance_to_gain_additional_frenzy_charge', 'gain_endurance_charge_on_melee_stun', 'gain_endurance_charge_on_melee_stun_%',
    'gain_frenzy_charge_on_critical_strike_%', 'gain_frenzy_charge_on_hit_while_bleeding', 'gain_frenzy_charge_on_hitting_marked_enemy_%', 'gain_frenzy_charge_on_hitting_rare_or_unique_enemy_%',
    'gain_frenzy_charge_on_hitting_unique_enemy_%', 'gain_frenzy_charge_per_enemy_you_crit_%_chance', 'gain_frenzy_power_endurance_charges_on_vaal_skill_use', 'gain_maximum_endurance_charges_when_crit_chance_%',
    'gain_power_charge_on_critical_strike_with_wands_%', 'gain_power_charge_on_curse_cast_%', 'gain_power_charge_on_hit_%_chance_against_frozen_enemy', 'gain_power_charge_on_hit_while_bleeding',
    'gain_power_charge_on_non_critical_strike_%', 'gain_power_charge_on_vaal_skill_use_%', 'gain_power_charge_per_enemy_you_crit', 'gain_power_charge_when_throwing_trap_%',
    'gain_random_charge_on_block', 'ice_spear_%_chance_to_gain_power_charge_on_critical_strike', 'lose_all_power_charges_on_block', 'power_charge_on_block_%_chance',
    'power_charge_on_non_critical_strike_%_chance_with_claws_daggers', 'power_charge_on_spell_block_%_chance', 'power_frenzy_or_endurance_charge_on_kill_%', 'power_siphon_%_chance_to_gain_power_charge_on_kill',
    'sweep_add_endurance_charge_on_hit_%', 'unique_gain_power_charge_on_non_crit', 'unique_lose_a_power_charge_when_hit', 'unique_lose_all_endurance_charges_when_hit',
    'unique_lose_all_power_charges_on_crit');
CREATE TABLE fx_hold_sources AS SELECT rule, ord, kind, key FROM fx_rule_sources WHERE rule IN (SELECT id FROM fx_hold_rules);
CREATE TABLE fx_hold_when AS SELECT rule, ord, trigger, args FROM fx_rule_when WHERE rule IN (SELECT id FROM fx_hold_rules);
CREATE TABLE fx_hold_if AS SELECT rule, grp, ord, condition, negate, args FROM fx_rule_if WHERE rule IN (SELECT id FROM fx_hold_rules);
CREATE TABLE fx_hold_do AS SELECT rule, ord, action, args, target, target_args, value, timers FROM fx_rule_do WHERE rule IN (SELECT id FROM fx_hold_rules);
DELETE FROM fx_rule_sources WHERE rule IN (SELECT id FROM fx_hold_rules);
DELETE FROM fx_rule_when WHERE rule IN (SELECT id FROM fx_hold_rules);
DELETE FROM fx_rule_if WHERE rule IN (SELECT id FROM fx_hold_rules);
DELETE FROM fx_rule_do WHERE rule IN (SELECT id FROM fx_hold_rules);
DELETE FROM fx_rules WHERE id IN (SELECT id FROM fx_hold_rules);
