-- Spectres' area spells land where they are aimed (0 = around the spectre, as before).
INSERT OR IGNORE INTO system_settings (key, value) VALUES ('spectre_spells_hit_at_target', 1.0);
