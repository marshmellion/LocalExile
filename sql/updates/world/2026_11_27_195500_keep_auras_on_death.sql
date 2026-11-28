-- Auras, heralds and stances come back on after a resurrect (0 = they stay off, as before).
INSERT OR IGNORE INTO system_settings (key, value) VALUES ('keep_auras_on_death', 1.0);
