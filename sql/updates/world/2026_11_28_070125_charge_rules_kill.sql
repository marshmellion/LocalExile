-- (effect-rules branch: a world.db that has our rules holds these ids already, as our engine's rules. Main's take their
-- place: they go first, with their rows. On a new world.db there is nothing to delete.)
DELETE FROM fx_rule_sources WHERE rule IN (
    'add_endurance_charge_on_kill', 'add_endurance_charge_on_skill_hit_%', 'add_frenzy_charge_on_critical_strike', 'add_frenzy_charge_on_kill',
    'add_frenzy_charge_when_hit_%', 'add_power_charge_on_critical_strike_%', 'add_power_charge_on_hit_while_poisoned_%', 'chance_to_gain_endurance_charge_on_block_%',
    'chance_to_gain_endurance_charge_on_crit_%', 'chance_to_gain_endurance_charge_on_hit_%_vs_bleeding_enemy', 'chance_to_gain_endurance_charge_on_melee_crit_%', 'chance_to_gain_endurance_charge_when_hit_%',
    'chance_to_gain_endurance_charge_when_you_stun_enemy_%', 'chance_to_gain_frenzy_charge_on_block_%', 'chance_to_gain_frenzy_charge_on_block_attack_%', 'chance_to_gain_frenzy_charge_on_stun_%',
    'chance_to_gain_power_charge_on_melee_stun_%', 'chance_to_gain_power_charge_on_rare_or_unique_enemy_hit_%', 'chance_to_gain_power_charge_on_stun_%', 'chance_to_gain_power_charge_when_block_%',
    'gain_endurance_charge_on_melee_stun', 'gain_endurance_charge_on_melee_stun_%', 'gain_frenzy_charge_on_critical_strike_%', 'gain_frenzy_charge_on_hit_while_bleeding',
    'gain_frenzy_charge_on_hitting_marked_enemy_%', 'gain_frenzy_charge_on_hitting_rare_or_unique_enemy_%', 'gain_frenzy_charge_on_hitting_unique_enemy_%', 'gain_frenzy_charge_per_enemy_you_crit_%_chance',
    'gain_frenzy_power_endurance_charges_on_vaal_skill_use', 'gain_maximum_endurance_charges_when_crit_chance_%', 'gain_power_charge_on_critical_strike_with_wands_%', 'gain_power_charge_on_curse_cast_%',
    'gain_power_charge_on_hit_%_chance_against_frozen_enemy', 'gain_power_charge_on_hit_while_bleeding', 'gain_power_charge_on_non_critical_strike_%', 'gain_power_charge_on_vaal_skill_use_%',
    'gain_power_charge_per_enemy_you_crit', 'gain_power_charge_when_throwing_trap_%', 'gain_random_charge_on_block', 'lose_all_power_charges_on_block',
    'power_charge_on_block_%_chance', 'power_charge_on_non_critical_strike_%_chance_with_claws_daggers', 'power_charge_on_spell_block_%_chance', 'power_frenzy_or_endurance_charge_on_kill_%',
    'unique_gain_power_charge_on_non_crit', 'unique_lose_a_power_charge_when_hit', 'unique_lose_all_endurance_charges_when_hit', 'unique_lose_all_power_charges_on_crit');
DELETE FROM fx_rule_when WHERE rule IN (
    'add_endurance_charge_on_kill', 'add_endurance_charge_on_skill_hit_%', 'add_frenzy_charge_on_critical_strike', 'add_frenzy_charge_on_kill',
    'add_frenzy_charge_when_hit_%', 'add_power_charge_on_critical_strike_%', 'add_power_charge_on_hit_while_poisoned_%', 'chance_to_gain_endurance_charge_on_block_%',
    'chance_to_gain_endurance_charge_on_crit_%', 'chance_to_gain_endurance_charge_on_hit_%_vs_bleeding_enemy', 'chance_to_gain_endurance_charge_on_melee_crit_%', 'chance_to_gain_endurance_charge_when_hit_%',
    'chance_to_gain_endurance_charge_when_you_stun_enemy_%', 'chance_to_gain_frenzy_charge_on_block_%', 'chance_to_gain_frenzy_charge_on_block_attack_%', 'chance_to_gain_frenzy_charge_on_stun_%',
    'chance_to_gain_power_charge_on_melee_stun_%', 'chance_to_gain_power_charge_on_rare_or_unique_enemy_hit_%', 'chance_to_gain_power_charge_on_stun_%', 'chance_to_gain_power_charge_when_block_%',
    'gain_endurance_charge_on_melee_stun', 'gain_endurance_charge_on_melee_stun_%', 'gain_frenzy_charge_on_critical_strike_%', 'gain_frenzy_charge_on_hit_while_bleeding',
    'gain_frenzy_charge_on_hitting_marked_enemy_%', 'gain_frenzy_charge_on_hitting_rare_or_unique_enemy_%', 'gain_frenzy_charge_on_hitting_unique_enemy_%', 'gain_frenzy_charge_per_enemy_you_crit_%_chance',
    'gain_frenzy_power_endurance_charges_on_vaal_skill_use', 'gain_maximum_endurance_charges_when_crit_chance_%', 'gain_power_charge_on_critical_strike_with_wands_%', 'gain_power_charge_on_curse_cast_%',
    'gain_power_charge_on_hit_%_chance_against_frozen_enemy', 'gain_power_charge_on_hit_while_bleeding', 'gain_power_charge_on_non_critical_strike_%', 'gain_power_charge_on_vaal_skill_use_%',
    'gain_power_charge_per_enemy_you_crit', 'gain_power_charge_when_throwing_trap_%', 'gain_random_charge_on_block', 'lose_all_power_charges_on_block',
    'power_charge_on_block_%_chance', 'power_charge_on_non_critical_strike_%_chance_with_claws_daggers', 'power_charge_on_spell_block_%_chance', 'power_frenzy_or_endurance_charge_on_kill_%',
    'unique_gain_power_charge_on_non_crit', 'unique_lose_a_power_charge_when_hit', 'unique_lose_all_endurance_charges_when_hit', 'unique_lose_all_power_charges_on_crit');
DELETE FROM fx_rule_if WHERE rule IN (
    'add_endurance_charge_on_kill', 'add_endurance_charge_on_skill_hit_%', 'add_frenzy_charge_on_critical_strike', 'add_frenzy_charge_on_kill',
    'add_frenzy_charge_when_hit_%', 'add_power_charge_on_critical_strike_%', 'add_power_charge_on_hit_while_poisoned_%', 'chance_to_gain_endurance_charge_on_block_%',
    'chance_to_gain_endurance_charge_on_crit_%', 'chance_to_gain_endurance_charge_on_hit_%_vs_bleeding_enemy', 'chance_to_gain_endurance_charge_on_melee_crit_%', 'chance_to_gain_endurance_charge_when_hit_%',
    'chance_to_gain_endurance_charge_when_you_stun_enemy_%', 'chance_to_gain_frenzy_charge_on_block_%', 'chance_to_gain_frenzy_charge_on_block_attack_%', 'chance_to_gain_frenzy_charge_on_stun_%',
    'chance_to_gain_power_charge_on_melee_stun_%', 'chance_to_gain_power_charge_on_rare_or_unique_enemy_hit_%', 'chance_to_gain_power_charge_on_stun_%', 'chance_to_gain_power_charge_when_block_%',
    'gain_endurance_charge_on_melee_stun', 'gain_endurance_charge_on_melee_stun_%', 'gain_frenzy_charge_on_critical_strike_%', 'gain_frenzy_charge_on_hit_while_bleeding',
    'gain_frenzy_charge_on_hitting_marked_enemy_%', 'gain_frenzy_charge_on_hitting_rare_or_unique_enemy_%', 'gain_frenzy_charge_on_hitting_unique_enemy_%', 'gain_frenzy_charge_per_enemy_you_crit_%_chance',
    'gain_frenzy_power_endurance_charges_on_vaal_skill_use', 'gain_maximum_endurance_charges_when_crit_chance_%', 'gain_power_charge_on_critical_strike_with_wands_%', 'gain_power_charge_on_curse_cast_%',
    'gain_power_charge_on_hit_%_chance_against_frozen_enemy', 'gain_power_charge_on_hit_while_bleeding', 'gain_power_charge_on_non_critical_strike_%', 'gain_power_charge_on_vaal_skill_use_%',
    'gain_power_charge_per_enemy_you_crit', 'gain_power_charge_when_throwing_trap_%', 'gain_random_charge_on_block', 'lose_all_power_charges_on_block',
    'power_charge_on_block_%_chance', 'power_charge_on_non_critical_strike_%_chance_with_claws_daggers', 'power_charge_on_spell_block_%_chance', 'power_frenzy_or_endurance_charge_on_kill_%',
    'unique_gain_power_charge_on_non_crit', 'unique_lose_a_power_charge_when_hit', 'unique_lose_all_endurance_charges_when_hit', 'unique_lose_all_power_charges_on_crit');
DELETE FROM fx_rule_do WHERE rule IN (
    'add_endurance_charge_on_kill', 'add_endurance_charge_on_skill_hit_%', 'add_frenzy_charge_on_critical_strike', 'add_frenzy_charge_on_kill',
    'add_frenzy_charge_when_hit_%', 'add_power_charge_on_critical_strike_%', 'add_power_charge_on_hit_while_poisoned_%', 'chance_to_gain_endurance_charge_on_block_%',
    'chance_to_gain_endurance_charge_on_crit_%', 'chance_to_gain_endurance_charge_on_hit_%_vs_bleeding_enemy', 'chance_to_gain_endurance_charge_on_melee_crit_%', 'chance_to_gain_endurance_charge_when_hit_%',
    'chance_to_gain_endurance_charge_when_you_stun_enemy_%', 'chance_to_gain_frenzy_charge_on_block_%', 'chance_to_gain_frenzy_charge_on_block_attack_%', 'chance_to_gain_frenzy_charge_on_stun_%',
    'chance_to_gain_power_charge_on_melee_stun_%', 'chance_to_gain_power_charge_on_rare_or_unique_enemy_hit_%', 'chance_to_gain_power_charge_on_stun_%', 'chance_to_gain_power_charge_when_block_%',
    'gain_endurance_charge_on_melee_stun', 'gain_endurance_charge_on_melee_stun_%', 'gain_frenzy_charge_on_critical_strike_%', 'gain_frenzy_charge_on_hit_while_bleeding',
    'gain_frenzy_charge_on_hitting_marked_enemy_%', 'gain_frenzy_charge_on_hitting_rare_or_unique_enemy_%', 'gain_frenzy_charge_on_hitting_unique_enemy_%', 'gain_frenzy_charge_per_enemy_you_crit_%_chance',
    'gain_frenzy_power_endurance_charges_on_vaal_skill_use', 'gain_maximum_endurance_charges_when_crit_chance_%', 'gain_power_charge_on_critical_strike_with_wands_%', 'gain_power_charge_on_curse_cast_%',
    'gain_power_charge_on_hit_%_chance_against_frozen_enemy', 'gain_power_charge_on_hit_while_bleeding', 'gain_power_charge_on_non_critical_strike_%', 'gain_power_charge_on_vaal_skill_use_%',
    'gain_power_charge_per_enemy_you_crit', 'gain_power_charge_when_throwing_trap_%', 'gain_random_charge_on_block', 'lose_all_power_charges_on_block',
    'power_charge_on_block_%_chance', 'power_charge_on_non_critical_strike_%_chance_with_claws_daggers', 'power_charge_on_spell_block_%_chance', 'power_frenzy_or_endurance_charge_on_kill_%',
    'unique_gain_power_charge_on_non_crit', 'unique_lose_a_power_charge_when_hit', 'unique_lose_all_endurance_charges_when_hit', 'unique_lose_all_power_charges_on_crit');
DELETE FROM fx_rules WHERE id IN (
    'add_endurance_charge_on_kill', 'add_endurance_charge_on_skill_hit_%', 'add_frenzy_charge_on_critical_strike', 'add_frenzy_charge_on_kill',
    'add_frenzy_charge_when_hit_%', 'add_power_charge_on_critical_strike_%', 'add_power_charge_on_hit_while_poisoned_%', 'chance_to_gain_endurance_charge_on_block_%',
    'chance_to_gain_endurance_charge_on_crit_%', 'chance_to_gain_endurance_charge_on_hit_%_vs_bleeding_enemy', 'chance_to_gain_endurance_charge_on_melee_crit_%', 'chance_to_gain_endurance_charge_when_hit_%',
    'chance_to_gain_endurance_charge_when_you_stun_enemy_%', 'chance_to_gain_frenzy_charge_on_block_%', 'chance_to_gain_frenzy_charge_on_block_attack_%', 'chance_to_gain_frenzy_charge_on_stun_%',
    'chance_to_gain_power_charge_on_melee_stun_%', 'chance_to_gain_power_charge_on_rare_or_unique_enemy_hit_%', 'chance_to_gain_power_charge_on_stun_%', 'chance_to_gain_power_charge_when_block_%',
    'gain_endurance_charge_on_melee_stun', 'gain_endurance_charge_on_melee_stun_%', 'gain_frenzy_charge_on_critical_strike_%', 'gain_frenzy_charge_on_hit_while_bleeding',
    'gain_frenzy_charge_on_hitting_marked_enemy_%', 'gain_frenzy_charge_on_hitting_rare_or_unique_enemy_%', 'gain_frenzy_charge_on_hitting_unique_enemy_%', 'gain_frenzy_charge_per_enemy_you_crit_%_chance',
    'gain_frenzy_power_endurance_charges_on_vaal_skill_use', 'gain_maximum_endurance_charges_when_crit_chance_%', 'gain_power_charge_on_critical_strike_with_wands_%', 'gain_power_charge_on_curse_cast_%',
    'gain_power_charge_on_hit_%_chance_against_frozen_enemy', 'gain_power_charge_on_hit_while_bleeding', 'gain_power_charge_on_non_critical_strike_%', 'gain_power_charge_on_vaal_skill_use_%',
    'gain_power_charge_per_enemy_you_crit', 'gain_power_charge_when_throwing_trap_%', 'gain_random_charge_on_block', 'lose_all_power_charges_on_block',
    'power_charge_on_block_%_chance', 'power_charge_on_non_critical_strike_%_chance_with_claws_daggers', 'power_charge_on_spell_block_%_chance', 'power_frenzy_or_endurance_charge_on_kill_%',
    'unique_gain_power_charge_on_non_crit', 'unique_lose_a_power_charge_when_hit', 'unique_lose_all_endurance_charges_when_hit', 'unique_lose_all_power_charges_on_crit');

-- The additional charges on kill, at their old place: after the rules at negative positions and before
-- every rule at a positive one, which moves up by 10 to make room (custom rows included).
UPDATE fx_rules SET position = position + 10
WHERE position > 0 AND id IN (SELECT w.rule FROM fx_rule_when w JOIN fx_rule_do d ON d.rule = w.rule WHERE w.trigger = 'kill' AND d.action = 'charge' AND d.target = 'self');

INSERT INTO fx_rules (id, book, title, replaces, status, position) VALUES
('add_endurance_charge_on_kill', 'charges', 'Gain an Endurance Charge on Kill', 'add_endurance_charge_on_kill', 'built', 2),
('add_frenzy_charge_on_kill', 'charges', 'Gain a Frenzy Charge on Kill', 'add_frenzy_charge_on_kill', 'built', 4),
('power_frenzy_or_endurance_charge_on_kill_%', 'charges', 'Chance to gain a Power, Frenzy or Endurance Charge on Kill', 'power_frenzy_or_endurance_charge_on_kill_%', 'built', 6);

INSERT INTO fx_rule_sources (rule, kind, key)
SELECT id, 'stat', replaces FROM fx_rules WHERE id IN ('add_endurance_charge_on_kill', 'add_frenzy_charge_on_kill', 'power_frenzy_or_endurance_charge_on_kill_%');

INSERT INTO fx_rule_when (rule, ord, trigger, args) VALUES
('add_endurance_charge_on_kill', 0, 'kill', '{}'),
('add_frenzy_charge_on_kill', 0, 'kill', '{}'),
('power_frenzy_or_endurance_charge_on_kill_%', 0, 'kill', '{}');

INSERT INTO fx_rule_if (rule, ord, condition, negate, args) VALUES
('power_frenzy_or_endurance_charge_on_kill_%', 0, 'chance', 0, '{"percent":{"from":"roll"}}');

INSERT INTO fx_rule_do (rule, ord, action, args) VALUES
('add_endurance_charge_on_kill', 0, 'charge', '{"charge":"endurance"}'),
('add_frenzy_charge_on_kill', 0, 'charge', '{"charge":"frenzy"}'),
('power_frenzy_or_endurance_charge_on_kill_%', 0, 'charge', '{"charge":"random"}');
