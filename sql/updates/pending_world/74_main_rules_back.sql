-- Main's world rules of 2026-11-28 back in their place (000_main_rules_step_aside.sql), replacing our rules of the
-- same ids: main's engine runs those now (engine NULL). Nothing is held on a world.db that had our rules already.
DELETE FROM fx_rule_sources WHERE rule IN (SELECT id FROM fx_hold_rules);
DELETE FROM fx_rule_when WHERE rule IN (SELECT id FROM fx_hold_rules);
DELETE FROM fx_rule_if WHERE rule IN (SELECT id FROM fx_hold_rules);
DELETE FROM fx_rule_do WHERE rule IN (SELECT id FROM fx_hold_rules);
DELETE FROM fx_rules WHERE id IN (SELECT id FROM fx_hold_rules);
INSERT INTO fx_rules (id, book, title, replaces, status, enabled, checked, origin, note, position)
SELECT id, book, title, replaces, status, enabled, checked, origin, note, position FROM fx_hold_rules;
INSERT INTO fx_rule_sources (rule, ord, kind, key) SELECT rule, ord, kind, key FROM fx_hold_sources;
INSERT INTO fx_rule_when (rule, ord, trigger, args) SELECT rule, ord, trigger, args FROM fx_hold_when;
INSERT INTO fx_rule_if (rule, grp, ord, condition, negate, args) SELECT rule, grp, ord, condition, negate, args FROM fx_hold_if;
INSERT INTO fx_rule_do (rule, ord, action, args, target, target_args, value, timers)
SELECT rule, ord, action, args, target, target_args, value, timers FROM fx_hold_do;
DROP TABLE fx_hold_do;
DROP TABLE fx_hold_if;
DROP TABLE fx_hold_when;
DROP TABLE fx_hold_sources;
DROP TABLE fx_hold_rules;
-- Main's rules are main's engine's, whichever order the files ran in.
UPDATE fx_rules SET engine = NULL WHERE id IN (
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
