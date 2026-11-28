-- "#% increased Damage with Wands per Power Charge": main's code gained the row (ChargeEffects.PerCharge); the table
-- that replaces that list while the effect rules are on follows.
INSERT OR IGNORE INTO fx_modifiers (stat, ord, quantity, how, scale, conditions, target, enabled, checked, note, counts_as, per)
VALUES ('wand_damage_+%_per_power_charge', 0, 'wand_damage_+%', 'flat', 1.0, '[]', 'self', 1, 0, NULL, NULL, '{"of":"power_charge"}');
