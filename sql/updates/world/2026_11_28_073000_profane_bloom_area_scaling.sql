-- Profane Bloom's explosion (cursed enemies' quarter-life chaos blast) grows with area of effect, its art with it
-- (0 = the fixed radius, as before); the radius stays at most profane_bloom_max_radius cells.
INSERT OR IGNORE INTO system_settings (key, value) VALUES ('profane_bloom_area_scaling', 1.0);
INSERT OR IGNORE INTO system_settings (key, value) VALUES ('profane_bloom_max_radius', 60.0);
