using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using static Game;

/// <summary>
/// The monsters a boss's summon skill brings when the client's tables do not say. A summon skill names its monster
/// through its stat set's <c>alternate_minion</c> (SkillCatalog.MonsterSummon), but most campaign bosses' summons
/// carry no such stat: which monster comes is in the official server's AI scripts, which the client does not have
/// (Agents/docs/game/campaign/act2.md). Those got left out of the kit, so Merveil never called her daughters.
/// <para>A known skill takes the monster below. Any other summon takes a monster of the boss's own family (its
/// metadata folder) whose id or name holds the word the skill summons ("SummonSpawn": a "Spawn"), or none. Either way
/// the summon then works as any other monster summon does: cast time, cooldown, our cap of five living, hostile, no
/// experience or items.</para>
/// </summary>
public static partial class BossSummons
{
    // Skill → (monster, how many). From the encounters as the official game plays them.
    static readonly Dictionary<string, (string Monster, int Count)> Known = new(StringComparer.Ordinal)
    {
        // Merveil, the Twisted (The Cavern of Anger): her daughters, then the lesser spawn.
        ["MerveilSummonDaughters"] = ("Metadata/Monsters/Seawitch/SeaWitchSpawn", 2),
        ["MerveilSummonSpawn"] = ("Metadata/Monsters/Seawitch/SeaWitchSpawnExploding", 3),
    };

    static readonly ConcurrentDictionary<(int Boss, int Effect), SkillCatalog.MonsterSummonInfo?> Cache = new();

    /// <summary>The summon for a boss's skill whose tables name no monster, or null for a skill that summons nothing
    /// (or nothing the server can create).</summary>
    public static SkillCatalog.MonsterSummonInfo? For(SkillCatalog catalog, MonsterCatalog.Variety boss, int effect) =>
        Cache.GetOrAdd((boss.Row, effect), _ => Resolve(catalog, boss, effect));

    static SkillCatalog.MonsterSummonInfo? Resolve(SkillCatalog catalog, MonsterCatalog.Variety boss, int effect)
    {
        string? id = catalog.EffectId(effect);
        if (id is null || !SummonWord().IsMatch(id) || id.Contains("Statue", StringComparison.Ordinal)) return null;
        string? monster;
        int count;
        if (Known.TryGetValue(id, out var known)) (monster, count) = known;
        else (monster, count) = (Guess(boss, id), 2);
        if (monster is null || Game.Monsters.Find(monster) is not { Supported: true } variety || variety.Row == boss.Row) return null;
        double cast = catalog.CastTimeMs(effect) / 1000d;
        double cooldown = catalog.CooldownMs(effect, 1) / 1000d;
        return new(variety.Row, count, 6, NoRewards: true, cast > 0 ? cast : 1, cooldown > 0 ? cooldown : 14);
    }

    [GeneratedRegex("Summon|Revive|Raise|SpawnMinion|CallMinion", RegexOptions.CultureInvariant)]
    private static partial Regex SummonWord();

    /// <summary>A monster of the boss's family the skill's last word names: for "AtziriSummonDemons" one whose id or name
    /// holds "Demon", in the boss's folder.</summary>
    static string? Guess(MonsterCatalog.Variety boss, string skill)
    {
        int slash = boss.Id.LastIndexOf('/');
        if (slash < 0) return null;
        string folder = boss.Id[..(slash + 1)];
        var words = Regex.Matches(skill, "[A-Z][a-z]+").Select(m => m.Value).ToList();
        int at = words.FindLastIndex(w => w is "Summon" or "Revive" or "Raise" or "Spawn" or "Call");
        if (at < 0) return null;
        var rest = words.Skip(at + 1).Where(w => w.Length > 2).ToList();
        if (rest.Count == 0) return null;
        // The whole name first ("BloodElementals": a BloodElemental), in the family and then anywhere; then any one word
        // of it, in the family only (one word anywhere would match half the game).
        string joined = Singular(string.Concat(rest));
        // A word that names no kind of monster finds whichever one holds it.
        if (joined is "Minion" or "Friend" or "Monster" or "Ally" or "Help" or "Add") return null;
        // Never a helper object (a daemon, an invisible marker, a [DNT] row), a map or standalone copy of a boss, or
        // the boss itself under another row (Konley summoning Konley); its own clones are fine when the row says so.
        bool Candidate(MonsterCatalog.Variety v) => v.Row != boss.Row && !v.BossHealthBar && v.MovementSpeed > 0 && v.Supported
            && !v.Id.Contains("Boss", StringComparison.Ordinal) && !v.Id.Contains("Standalone", StringComparison.Ordinal)
            && !v.Id.Contains("Daemon", StringComparison.Ordinal) && !v.Id.Contains("Invisible", StringComparison.Ordinal)
            && !v.Name.Contains("DNT", StringComparison.Ordinal) && !v.Name.Contains("Daemon", StringComparison.Ordinal)
            && v.Name != "Invisible" && (v.Name != boss.Name || v.Id.Contains("Clone", StringComparison.Ordinal))
            // A named monster ("Kalria, The Risen", Soulmourn) is an encounter of its own, not a summon.
            && (!v.Name.Contains(',') || v.Name == boss.Name) && !BossKits.IsCampaignBoss(v) && !Named(v);
        bool Holds(MonsterCatalog.Variety v, string w) => v.Id.Contains(w, StringComparison.OrdinalIgnoreCase)
            || v.Name.Replace(" ", "").Contains(w, StringComparison.OrdinalIgnoreCase);
        var family = Game.Monsters.All.Where(v => v.Id.StartsWith(folder, StringComparison.Ordinal) && Candidate(v)).ToList();
        if (family.FirstOrDefault(v => Holds(v, joined)) is { } exact) return exact.Id;
        // Anywhere, only a monster whose own name starts with the whole word ("BloodElemental/BloodElemental"), so
        // "Skeletons" finds no "RhoaSkeleton".
        if (joined.Length >= 6 && Game.Monsters.All.Where(v => Candidate(v)
                && v.Id[(v.Id.LastIndexOf('/') + 1)..].StartsWith(joined, StringComparison.OrdinalIgnoreCase))
            .OrderBy(v => v.Id.Length).FirstOrDefault() is { } anywhere) return anywhere.Id;
        return null;
    }

    // A unique monster's row: the rarity-locked mods the tables give named monsters.
    static bool Named(MonsterCatalog.Variety v) => v.Id.Contains("Unique", StringComparison.Ordinal) || v.Name.StartsWith("The ", StringComparison.Ordinal);

    static string Singular(string word) =>
        word.EndsWith("ies", StringComparison.Ordinal) ? word[..^3] + "y"
        : word.EndsWith("s", StringComparison.Ordinal) && !word.EndsWith("ss", StringComparison.Ordinal) ? word[..^1] : word;
}
