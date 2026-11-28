using System.Text.Json;
using System.Text.Json.Nodes;
using GameServer.Core.Protocol.Instance;

/// <summary>Custom implicits (private build): new implicit modifiers made of effect-rule parts, a trigger and an effect
/// ("8% chance to gain Onslaught for 4 seconds on Critical Strike"). tools/make_custom_implicits.py writes the pool:
/// content/customimplicits.json gives the client each one's stat, text and three tiers of modifier, customimplicits.json
/// gives this mod their rules, which it adds to the effect rules engine at start (EffectRules.Add). A drop or a Radiant
/// Orb picks one at random among those the settings allow, its tier weighted and gated by item level.</summary>
sealed record CustomImplicitChoices(bool Enabled = true, int Chance = 1500, string[]? Rarities = null, string[]? Triggers = null,
    string[]? Categories = null, string[]? Slots = null, int Tier1 = 100, int Tier2 = 40, int Tier3 = 10, bool IgnoreItemLevel = false,
    int ValueScale = 100, int Most = 1, bool InJackpots = true, bool AnySlot = false, string[]? Blacklist = null,
    int FlaskChance = 300, bool RarityWeights = true);

public sealed partial class ExcitingItemsMod
{
    sealed record CustomTier(string Mod, int Level, int Min, int Max);
    sealed record CustomImplicit(string Id, string Trigger, string Category, string Text, string Stat, CustomTier[] Tiers,
        string? Unique = null, double? Weight = null);
    List<CustomImplicit> _custom = [];
    // Each gear slot's list (customimplicits.json "slots"): its item classes and the combinations it rolls.
    Dictionary<string, (string[] Classes, HashSet<string> Ids)> _customSlots = [];
    IDisposable? _customRules;

    /// <summary>The effect rules engine (EffectRules, from the effect-rules branch of the build), found at run time: a build
    /// without it still loads the mod, with the custom implicits off.</summary>
    static class Engine
    {
        static readonly Type? Rules = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => { try { return a.GetType("EffectRules"); } catch { return null; } }).FirstOrDefault(t => t is not null);
        static readonly Type? NewRuleType = Rules?.GetNestedType("NewRule");

        public static object NewRule(string id, string title, List<(string, string)> sources, List<(string, string)> when,
            List<(int, string, bool, string)> conditions, List<(string, string, string, string, string, string)> actions)
        {
            if (NewRuleType is null) throw new InvalidOperationException("this build has no effect rules engine (EffectRules)");
            var ctor = NewRuleType.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
            var parameters = ctor.GetParameters();
            object?[] args = new object?[parameters.Length];
            object[] given = [id, title, sources, when, conditions, actions];
            for (int i = 0; i < parameters.Length; i++)
                args[i] = i < given.Length ? Convert(given[i], parameters[i].ParameterType)
                    : parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null;
            return ctor.Invoke(args);
        }

        // A list of tuples into the engine's own parameter type (an array or a list of the same tuple shape).
        static object Convert(object value, Type target)
        {
            if (target.IsInstanceOfType(value)) return value;
            if (value is not System.Collections.IList list) return value;
            Type element = target.IsArray ? target.GetElementType()! : target.IsGenericType ? target.GetGenericArguments()[0] : typeof(object);
            var array = Array.CreateInstance(element, list.Count);
            for (int i = 0; i < list.Count; i++) array.SetValue(list[i], i);
            if (target.IsAssignableFrom(array.GetType())) return array;
            var typed = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(element))!;
            foreach (var x in array) typed.Add(x);
            return typed;
        }

        public static IDisposable? Add(List<object> rules)
        {
            if (Rules is null || NewRuleType is null) throw new InvalidOperationException("this build has no effect rules engine (EffectRules)");
            var add = Rules.GetMethods().First(m => m.Name == "Add" && m.GetParameters().Length == 1);
            var typed = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(NewRuleType))!;
            foreach (var r in rules) typed.Add(r);
            object arg = add.GetParameters()[0].ParameterType.IsArray ? ToArray(typed, NewRuleType) : typed;
            return add.Invoke(null, [arg]) as IDisposable;
        }

        static Array ToArray(System.Collections.IList list, Type element)
        {
            var a = Array.CreateInstance(element, list.Count);
            list.CopyTo(a, 0);
            return a;
        }
    }

    void LoadCustomImplicits(IModContext mod)
    {
        _customRules?.Dispose();
        _customRules = null;
        _custom = [];
        string file = Path.Combine(mod.Folder, "customimplicits.json");
        if (!File.Exists(file)) { mod.Log("customimplicits.json is missing: no custom implicits"); return; }
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(file))!;
            _customSlots = (root["slots"] as JsonObject ?? []).ToDictionary(kv => kv.Key, kv => (
                (kv.Value?["classes"] as JsonArray ?? []).Select(x => (string)x!).ToArray(),
                (kv.Value?["implicits"] as JsonArray ?? []).Select(x => (string)x!).ToHashSet()));
            var rules = new List<object>();
            foreach (var n in (root["implicits"] as JsonArray ?? []).OfType<JsonObject>())
            {
                var tiers = (n["tiers"] as JsonArray ?? []).OfType<JsonObject>()
                    .Select(t => new CustomTier((string)t["mod"]!, (int)t["level"]!, (int)t["min"]!, (int)t["max"]!)).ToArray();
                var c = new CustomImplicit((string)n["id"]!, (string)n["trigger"]!, (string)n["category"]!, (string)n["text"]!, (string)n["stat"]!, tiers,
                    (string?)n["unique"], n["weight"] is JsonValue wv && wv.TryGetValue(out double wd) ? wd : null);
                _custom.Add(c);
                string J(JsonNode? x) => x?.ToJsonString() ?? "{}";
                // One rule, or several (a flask's: while it runs it gives a hidden stat, whose rule acts on the trigger).
                var ruleNodes = n["rules"] is JsonArray many ? many.OfType<JsonObject>().ToList() : [n["rule"]!.AsObject()];
                int part = 0;
                foreach (var rule in ruleNodes)
                rules.Add(Engine.NewRule("excitingitems_custom_" + c.Id + (part++ == 0 ? "" : "_" + part), c.Text.Replace("{0}", "#"),
                    [((string?)rule["source"] ?? "stat", (string?)rule["key"] ?? c.Stat)],
                    [.. (rule["when"] as JsonArray ?? []).Select(w => ((string)w!["trigger"]!, J(w["args"])))],
                    [.. (rule["if"] as JsonArray ?? []).Select(i => ((int)i!["group"]!, (string)i["condition"]!, (bool)i["negate"]!, J(i["args"])))],
                    [.. (rule["do"] as JsonArray ?? []).Select(d => ((string)d!["action"]!, J(d["args"]), (string)d["target"]!, J(d["targetArgs"]),
                        J(d["value"]), d["timers"]?.ToJsonString() ?? "[]"))]));
            }
            // Their modifiers come from content/customimplicits.json; with that pack off there are none, and nothing rolls.
            if (_custom.Count > 0 && Program.Items.Mods.ById(_custom[0].Tiers[0].Mod) is null)
                throw new InvalidOperationException("their modifiers are not in the game data (content/customimplicits.json is off)");
            _customRules = Engine.Add(rules);
            mod.Log($"{_custom.Count} custom implicits in {_customSlots.Count} gear slots, their {rules.Count} effect rules added");
        }
        catch (Exception e)
        {
            // Without their client rows (content/customimplicits.json is off: the client cannot take new Stats rows) there is
            // nothing to roll; the rest of the mod loads as usual.
            _customRules?.Dispose(); _customRules = null; _custom = [];
            mod.Log($"custom implicits off: {e.Message}");
        }
    }

    /// <summary>One custom implicit (or a few, up to Most) on the item; null when none can go on it.</summary>
    Item? WithCustomImplicit(Player player, Item item, Random rng, List<string> log)
    {
        var c = _choices.CustomImplicits ?? new();
        if (!c.Enabled || _custom.Count == 0) return null;
        var catalog = Game.ItemsFor(player);
        // The item's slot: its list only, unless "any slot" is on (then every combination).
        string itemClass = catalog.ByHash(item.TypeHash)?.Class ?? "";
        var slot = _customSlots.FirstOrDefault(kv => kv.Value.Classes.Contains(itemClass));
        if (slot.Key is null && !c.AnySlot) return null;
        if (slot.Key is not null && c.Slots is not null && !c.Slots.Contains(slot.Key)) return null;
        var have = item.Mods.Select(m => catalog.Mods.ByHash(m.Hash)?.Id ?? "").ToHashSet();
        // Every custom implicit rides on the same pair of item stats (which one, its roll), so an item holds one.
        if (have.Any(id => id.StartsWith("LocalExileCustomImplicit_", StringComparison.Ordinal))) return null;
        var mods = item.Mods.ToList();
        int added = 0;
        var weights = new[] { c.Tier1, c.Tier2, c.Tier3 };
        var open = _custom.Where(x => (c.AnySlot || slot.Value.Ids.Contains(x.Id)) && (c.Triggers is null || c.Triggers.Contains(x.Trigger)) && (c.Categories is null || c.Categories.Contains(x.Category))
            && !(c.Blacklist ?? []).Any(b => x.Id.Contains(b, StringComparison.OrdinalIgnoreCase) || x.Text.Contains(b, StringComparison.OrdinalIgnoreCase))
            && !x.Tiers.Any(t => have.Contains(t.Mod))).ToList();
        // Better effects rarer: a unique's effect is as likely as that unique drops (its drop weight), the rest even.
        double WeightOf(CustomImplicit x) => c.RarityWeights && x.Weight is double w ? Math.Max(0.0005, w) : 1;
        while (added < 1 && open.Count > 0)
        {
            double at0 = rng.NextDouble() * open.Sum(WeightOf);
            var pick = open.First(x => (at0 -= WeightOf(x)) <= 0 || ReferenceEquals(x, open[^1]));
            open.Remove(pick);
            var tiers = pick.Tiers.Select((t, i) => (Tier: t, Mod: catalog.Mods.ById(t.Mod), Weight: i < weights.Length ? weights[i] : 0))
                .Where(t => t.Mod is not null && t.Weight > 0 && (c.IgnoreItemLevel || item.ItemLevel >= t.Tier.Level)).ToList();
            if (tiers.Count == 0) continue;
            int at = rng.Next(tiers.Sum(t => t.Weight));
            var chosen = tiers.First(t => (at -= t.Weight) < 0);
            var rolled = chosen.Mod!.Roll(ModSection.Implicit, rng);
            // The modifier's first value says which implicit it is; only the second, its roll, is scaled.
            if (c.ValueScale != 100 && rolled.Values.Count > 1)
                rolled = rolled with { Values = [rolled.Values[0], .. rolled.Values.Skip(1).Select(v => Math.Max(1, (int)Math.Round(v * c.ValueScale / 100.0)))] };
            int place = mods.FindLastIndex(m => m.Section == ModSection.Implicit) + 1;
            if (place == 0) place = mods.FindIndex(m => m.Section == ModSection.Explicit) is int first and >= 0 ? first : mods.Count;
            mods.Insert(place, rolled);
            log.Add($"custom implicit \"{pick.Text.Replace("{0}", rolled.Values.LastOrDefault().ToString())}\" (tier {Array.IndexOf(pick.Tiers, chosen.Tier) + 1})");
            added++;
        }
        return added == 0 ? null : item with { Mods = mods, RequiredLevel = null };
    }
}
