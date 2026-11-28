using System.Text.RegularExpressions;
using GameServer.Core.Protocol.Instance;

/// <summary>
/// A reader for the client's loot filters, enough to tell whether an item lying on the ground is shown or hidden: Show,
/// Hide and Minimal blocks, Continue, and the conditions that name what an item is (class, base type, rarity, levels,
/// stack size, sockets and links, influence, modifiers by name, the usual flags). A condition the reader does not know
/// is never met, so a block that needs one is passed over (an unknown condition counted as met made the first block of
/// most filters match every item).
/// </summary>
public sealed class LootFilter
{
    public sealed record Facts(string Class, string BaseType, ItemRarity Rarity, int ItemLevel, int DropLevel, int AreaLevel,
        int StackSize, int Quality, bool Identified, bool Corrupted, bool Mirrored, bool Fractured, bool Synthesised,
        bool Vestigial, int Sockets, int Links, IReadOnlyList<string> Groups, int Width, int Height, int MapTier,
        ItemInfluence Influences, IReadOnlyList<string> ExplicitMods, bool AnyEnchantment, int MemoryStrands,
        IReadOnlyList<string>? ImplicitMods = null, IReadOnlyList<string>? Enchantments = null);

    sealed record Condition(string Key, string Op, string[] Values);
    sealed record Block(bool Show, bool Continue, List<Condition> Conditions);

    readonly List<Block> _blocks = [];
    public string Path { get; }
    public DateTime Written { get; }

    LootFilter(string path, DateTime written) { Path = path; Written = written; }

    static readonly Regex Token = new("\"[^\"]*\"|\\S+", RegexOptions.Compiled);
    static readonly string[] Ops = ["==", "!=", "<=", ">=", "=", "<", ">", "!"];

    public static LootFilter Read(string path)
    {
        var filter = new LootFilter(path, File.GetLastWriteTimeUtc(path));
        Block? block = null;
        foreach (string raw in File.ReadLines(path))
        {
            string line = raw;
            int hash = IndexOutsideQuotes(line, '#');
            if (hash >= 0) line = line[..hash];
            line = line.Trim();
            if (line.Length == 0) continue;
            var tokens = Token.Matches(line).Select(m => m.Value).ToList();
            string key = tokens[0];
            if (key is "Show" or "Hide" or "Minimal")
            {
                block = new(key != "Hide", false, []);
                filter._blocks.Add(block);
                continue;
            }
            if (block is null) continue;
            if (key == "Continue") { filter._blocks[^1] = block = block with { Continue = true }; continue; }
            if (key.StartsWith("Set", StringComparison.Ordinal) || key.StartsWith("Play", StringComparison.Ordinal)
                || key is "MinimapIcon" or "CustomAlertSound" or "CustomAlertSoundOptional" or "DisableDropSound" or "EnableDropSound"
                    or "DisableDropSoundIfAlertSound" or "EnableDropSoundIfAlertSound") continue;
            string op = "";
            int next = 1;
            if (tokens.Count > 1 && Ops.Contains(tokens[1])) { op = tokens[1]; next = 2; }
            else if (tokens.Count > 1 && Ops.FirstOrDefault(o => tokens[1].StartsWith(o, StringComparison.Ordinal) && tokens[1].Length > o.Length) is { } glued)
            {
                op = glued;
                tokens[1] = tokens[1][glued.Length..];
            }
            block.Conditions.Add(new(key, op, [.. tokens.Skip(next).Select(t => t.Trim('"'))]));
        }
        return filter;
    }

    static int IndexOutsideQuotes(string s, char c)
    {
        bool quoted = false;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '"') quoted = !quoted;
            else if (s[i] == c && !quoted) return i;
        }
        return -1;
    }

    /// <summary>Whether the filter shows the item: the last matching block's verdict, a block without Continue ending
    /// the search; an item no block matches is shown.</summary>
    public bool Shows(Facts item) => Decide(item).Shown;

    /// <summary>The verdict and the number (from 1) of the block that gave it; 0 when no block matched.</summary>
    public (bool Shown, int Block) Decide(Facts item)
    {
        bool shown = true;
        int deciding = 0;
        for (int i = 0; i < _blocks.Count; i++)
        {
            var block = _blocks[i];
            if (!block.Conditions.All(c => Meets(c, item))) continue;
            shown = block.Show;
            deciding = i + 1;
            if (!block.Continue) return (shown, deciding);
        }
        return (shown, deciding);
    }

    public string DescribeBlock(int number) => number < 1 || number > _blocks.Count ? "no block"
        : $"{(_blocks[number - 1].Show ? "Show" : "Hide")} block {number}: " +
          string.Join("; ", _blocks[number - 1].Conditions.Select(c => $"{c.Key} {c.Op} {string.Join(" ", c.Values.Take(4))}"));

    static bool Compare(int have, string op, int want) => op switch
    {
        "<" => have < want, "<=" => have <= want, ">" => have > want, ">=" => have >= want,
        "!=" or "!" => have != want, _ => have == want,
    };

    static bool Number(int have, Condition c) =>
        c.Values.Length == 0 || (c.Op is "" or "=" or "==" ? c.Values.Any(v => int.TryParse(v, out int n) && n == have)
            : int.TryParse(c.Values[0], out int want) && Compare(have, c.Op, want));

    static bool Flag(bool have, Condition c) =>
        c.Values.Length == 0 || (bool.TryParse(c.Values[0], out bool want) ? (c.Op is "!" or "!=" ? have != want : have == want) : true);

    static bool Names(string have, Condition c)
    {
        bool exact = c.Op == "==";
        bool any = c.Values.Any(v => exact ? have.Equals(v, StringComparison.OrdinalIgnoreCase) : have.Contains(v, StringComparison.OrdinalIgnoreCase));
        return c.Op is "!" or "!=" ? !any : any;
    }

    static readonly string[] RarityOrder = ["Normal", "Magic", "Rare", "Unique"];

    static bool Meets(Condition c, Facts item)
    {
        switch (c.Key)
        {
            case "Class": return Names(item.Class, c);
            case "BaseType": return Names(item.BaseType, c);
            case "Rarity":
                int have = (int)item.Rarity;
                if (c.Op is "" or "=" or "==") return c.Values.Any(v => Array.IndexOf(RarityOrder, v) == have);
                return c.Values.Length > 0 && Array.IndexOf(RarityOrder, c.Values[0]) is int want and >= 0 && Compare(have, c.Op, want);
            case "ItemLevel": return Number(item.ItemLevel, c);
            case "DropLevel": return Number(item.DropLevel, c);
            case "AreaLevel": return Number(item.AreaLevel, c);
            case "StackSize": return Number(item.StackSize, c);
            case "Quality": return Number(item.Quality, c);
            case "Width": return Number(item.Width, c);
            case "Height": return Number(item.Height, c);
            case "MapTier": return item.MapTier > 0 && Number(item.MapTier, c);
            case "MemoryStrands": return Number(item.MemoryStrands, c);
            case "LinkedSockets": return Number(item.Links, c);
            case "Sockets":
                return c.Values.Length == 0 || c.Values.All(v => int.TryParse(v, out _)) ? Number(item.Sockets, c)
                    : SocketsLike(c, [string.Concat(item.Groups)], item.Sockets);
            case "SocketGroup": return SocketsLike(c, item.Groups, item.Links);
            case "Identified": return Flag(item.Identified, c);
            case "Corrupted": return Flag(item.Corrupted, c);
            case "Mirrored": return Flag(item.Mirrored, c);
            case "FracturedItem": return Flag(item.Fractured, c);
            case "SynthesisedItem": return Flag(item.Synthesised, c);
            case "Vestigial": return Flag(item.Vestigial, c);
            case "AnyEnchantment": return Flag(item.AnyEnchantment, c);
            case "ElderMap" or "ShapedMap" or "BlightedMap" or "UberBlightedMap" or "Replica" or "TransfiguredGem" or "Scourged"
                or "HasCruciblePassiveTree" or "ZanaMemory" or "Imbued" or "Foulborn":
                return Flag(false, c);
            case "HasInfluence":
                if (c.Values.Any(v => v.Equals("None", StringComparison.OrdinalIgnoreCase))) return item.Influences == ItemInfluence.None;
                var named = c.Values.Select(v => Enum.TryParse<ItemInfluence>(v, true, out var i) ? i : ItemInfluence.None).Where(i => i != ItemInfluence.None).ToList();
                return c.Op == "==" ? named.All(i => item.Influences.HasFlag(i)) : named.Any(i => item.Influences.HasFlag(i));
            case "HasExplicitMod": return HasMods(c, item.ExplicitMods);
            case "HasImplicitMod": return HasMods(c, item.ImplicitMods ?? []);
            case "HasEnchantment": return HasMods(c, item.Enchantments ?? []);
            case "CorruptedMods": return Number(0, c);
            default: return false;
        }
    }

    // HasExplicitMod [op count] "name" ...: the count, when given, is the first value.
    static bool HasMods(Condition c, IReadOnlyList<string> mods)
    {
                var values = c.Values.ToList();
                string op = c.Op;
                int count = 1;
                if (values.Count > 0 && int.TryParse(values[0], out int n)) { count = n; values.RemoveAt(0); if (op == "") op = ">="; }
                else if (op is "" or "=" or "==") op = ">=";
                int found = mods.Count(m => values.Any(v => c.Op == "==" ? m.Equals(v, StringComparison.OrdinalIgnoreCase)
                    : m.Contains(v, StringComparison.OrdinalIgnoreCase)));
                return Compare(found, op, count);
    }

    // "5RGB", ">= 2GG", "RRR": a group (or the whole item) with at least that many sockets holding those colours.
    static bool SocketsLike(Condition c, IReadOnlyList<string> groups, int size)
    {
        foreach (string v in c.Values)
        {
            var m = Regex.Match(v.ToUpperInvariant(), "^(\\d*)([RGBWAD]*)$");
            if (!m.Success) continue;
            int want = m.Groups[1].Value.Length > 0 ? int.Parse(m.Groups[1].Value) : 0;
            string colours = m.Groups[2].Value;
            foreach (string g in groups)
            {
                if (want > 0 && !Compare(g.Length, c.Op is "" ? ">=" : c.Op, want)) continue;
                if (colours.GroupBy(ch => ch).All(x => g.Count(ch => ch == x.Key) >= x.Count())) return true;
            }
        }
        return false;
    }
}
