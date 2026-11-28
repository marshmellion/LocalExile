using System.Text.Json;
using GameServer.Core;

/// <summary>
/// Body Auras: the game's weapon effects worn on the body, and character effects worn by minions. The content pack
/// (tools/make_auras.py) makes, for every weapon effect, a character effect "&lt;Name&gt; Body Aura" (Cosmetics tab,
/// Character Effect slot, which the server's own cosmetics code draws) and a buff with the same look; and for every
/// character effect a buff with a monster version of its look. This mod puts those buffs on:
/// - spectres (in place of their spectral look by default, as the game's spectre skins do) and every other minion
///   (each kind on its own: zombies, skeletons, raging spirits, phantasms, golems, skitterbots, reapers ...);
/// - pets, totems, traps and mines;
/// - the player too, if asked (on top of whatever is in the Character Effect slot).
/// Each takes off, character (what the character wears in the Character Effect slot) or an aura's key; settings give
/// the defaults and /aura changes them for the character it is typed on (data/moddata/bodyauras/aura-choices.json,
/// outside the mod's folder, so the mod does not reload). With Cosmetic Drops locking cosmetics, an aura is used only
/// once the account owns its cosmetic (the body aura for a weapon effect's, the character effect for one's).
/// </summary>
public sealed class BodyAurasMod : IMod
{
    sealed record Choices(bool Enabled = true, MinionChoices? Minions = null, OtherChoices? Others = null,
        CharacterChoices? Character = null, bool OwnedOnly = true, double UpdateSeconds = 0.5, bool Commands = true);
    sealed record MinionChoices(string Spectres = "crucible", string Everything = "off", Dictionary<string, string>? PerKind = null,
        string WhenCharacterHasNone = "off", string SpectreLook = "replace");
    sealed record OtherChoices(string Pets = "off", string Totems = "off", string Traps = "off", string Mines = "off");
    sealed record CharacterChoices(string Extra = "off");

    /// <summary>An aura: key, name, kind (weapon / character), buff and visual rows, the cosmetic that unlocks it.</summary>
    sealed record Aura(string Key, string Name, string Kind, ushort Buff, ushort Visual, uint Cosmetic);

    Choices _c = new();
    Action<string> _log = _ => { };
    readonly Dictionary<string, Aura> _auras = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<uint, Aura> _byCosmetic = [];
    ushort? _spectreBuff;

    // Static: a reload of the mod (its files changed) clears what the old load put on.
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Player, State> _states = new();
    static readonly object SaveLock = new();
    Dictionary<string, Dictionary<string, string>> _picks = new(StringComparer.OrdinalIgnoreCase);
    static string PicksPath => Path.GetFullPath(Path.Combine("data", "moddata", "bodyauras", "aura-choices.json"));

    sealed class State
    {
        public object? Area;
        public double Wait;
        public HashSet<(uint Entity, ushort Row)> Applied = [];
        public Dictionary<uint, ushort?> Replaced = [];
        public SortedSet<string> Kinds = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<uint>? Owned;
        public double OwnedAge = double.MaxValue;
    }

    MinionChoices M => _c.Minions ?? new();
    OtherChoices O => _c.Others ?? new();
    CharacterChoices C => _c.Character ?? new();

    public void Load(IModContext mod)
    {
        _log = mod.Log;
        _c = mod.Settings<Choices>();
        if (!_c.Enabled) { mod.Log("off"); return; }
        LoadPicks();
        Unpack(mod.Folder);
        FindAuras(Path.Combine(mod.Folder, "auras.index.json"));
        if (_auras.Count == 0)
        {
            mod.Log("the game data has no body auras yet: start the game with the play button (it applies the mods' content), then the server again");
            return;
        }
        string id = mod.Manifest.Id;
        GameEvents.AreaTick.Add(id, GameEvents.Always(Tick));
        if (_c.Commands)
            GameEvents.Command(id, "aura", "/aura [list [words] | kinds | <who> <off|character|default|aura>]", Command);
        mod.Log($"{_auras.Values.Count(a => a.Kind == "weapon")} body auras, {_auras.Values.Count(a => a.Kind == "character")} character effect looks; "
            + $"spectres {M.Spectres} ({M.SpectreLook}), other minions {M.Everything}, pets {O.Pets}, totems {O.Totems}, traps {O.Traps}, "
            + $"mines {O.Mines}, character extra {C.Extra}, owned only {_c.OwnedOnly}");
    }

    /// <summary>The content pack and its effect files (about a thousand) come as content.zip (auras.json and effects/):
    /// unpacked into content/ when the zip is new (content/effects/.unpacked holds its hash). Until then the pack there
    /// is the one before, whose files are there. The unpacking changes the mod's folder, so the server loads the mod
    /// once more; the game takes the new pack at its next start (play button), and the server the start after.</summary>
    void Unpack(string folder)
    {
        string zip = Path.Combine(folder, "content.zip"), content = Path.Combine(folder, "content");
        string effects = Path.Combine(content, "effects"), stamp = Path.Combine(effects, ".unpacked");
        if (!File.Exists(zip)) return;
        try
        {
            string hash;
            using (var f = File.OpenRead(zip)) hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(f));
            if (File.Exists(stamp) && File.ReadAllText(stamp).Trim() == hash) return;
            if (Directory.Exists(effects)) Directory.Delete(effects, true);
            System.IO.Compression.ZipFile.ExtractToDirectory(zip, content, overwriteFiles: true);
            File.WriteAllText(stamp, hash);
            _log($"unpacked the content pack ({Directory.GetFiles(effects).Length - 1} effect files): start the game with the play button "
                + "to add the new auras to the game (the server knows them from the start after)");
        }
        catch (Exception e) { _log($"content.zip not unpacked: {e.Message}"); }
    }

    void FindAuras(string indexFile)
    {
        if (!File.Exists(indexFile)) { _log("auras.index.json is missing"); return; }
        var rows = new Dictionary<string, ushort>(StringComparer.Ordinal);
        if (ClientTables.TableOrNull("BuffDefinitions") is { } defs)
            foreach (var row in defs)
            {
                string? bid = row.TextOrNull("Id");
                if (bid is not null && bid.StartsWith("localexile_", StringComparison.Ordinal)) rows[bid] = (ushort)row.Index;
                if (bid == "spectre_buff") _spectreBuff = (ushort)row.Index;
            }
        var visuals = new Dictionary<string, ushort>(StringComparer.Ordinal);
        if (ClientTables.TableOrNull("BuffVisuals") is { } vis)
            foreach (var row in vis)
                if (row.TextOrNull("Id") is { } vid && vid.StartsWith("localexile_", StringComparison.Ordinal)) visuals[vid] = (ushort)row.Index;
        using var doc = JsonDocument.Parse(File.ReadAllText(indexFile));
        int missing = 0;
        foreach (var e in doc.RootElement.GetProperty("auras").EnumerateArray())
        {
            string key = e.GetProperty("key").GetString()!, buff = e.GetProperty("buff").GetString()!;
            if (!rows.TryGetValue(buff, out ushort row)) { missing++; continue; }
            var aura = new Aura(key, e.GetProperty("name").GetString() ?? key, e.GetProperty("kind").GetString() ?? "weapon", row,
                visuals.GetValueOrDefault(buff), e.GetProperty("cosmetic").GetUInt32());
            _auras[key] = aura;
            // A body aura's cosmetic and a character effect are both worn in the Character Effect slot.
            _byCosmetic.TryAdd(aura.Cosmetic, aura);
        }
        if (missing > 0) _log($"{missing} aura(s) are not in the game data yet: start the game with the play button, then the server again");
    }

    // ---- Choices ----

    /// <summary>Who a choice is for, as /aura and the per-character file name it.</summary>
    static string Who(string word) => word.ToLowerInvariant() switch
    {
        "spectres" or "spectre" => "spectre",
        "minions" or "minion" or "everything" or "all" or "others" => "minions",
        "me" or "self" or "character" or "player" => "me",
        "pets" or "pet" => "pets",
        "totems" or "totem" => "totems",
        "traps" or "trap" => "traps",
        "mines" or "mine" => "mines",
        var w => w,
    };

    static readonly HashSet<string> NotMinions = ["spectre", "me", "pets", "totems", "traps", "mines", "minions"];

    /// <summary>The choice for one minion kind (or me, pets, totems, traps, mines): this character's /aura pick, else
    /// the settings.</summary>
    string Choice(Player player, string who)
    {
        lock (SaveLock)
            if (_picks.TryGetValue(Program.CharacterName(player), out var mine))
            {
                if (mine.TryGetValue(who, out var v)) return v;
                if (!NotMinions.Contains(who) && mine.TryGetValue("minions", out var all)) return all;
            }
        if (M.PerKind?.FirstOrDefault(p => p.Key.Equals(who, StringComparison.OrdinalIgnoreCase)) is { Key: not null } own) return own.Value;
        return who switch
        {
            "spectre" => M.Spectres, "me" => C.Extra, "pets" => O.Pets, "totems" => O.Totems, "traps" => O.Traps, "mines" => O.Mines,
            _ => M.Everything,
        };
    }

    Aura? Find(string word)
    {
        string w = word.Trim();
        return _auras.GetValueOrDefault(w)
            ?? _auras.Values.FirstOrDefault(a => a.Name.Equals(w, StringComparison.OrdinalIgnoreCase))
            ?? _auras.Values.FirstOrDefault(a => a.Name.Replace(" Body Aura", "").Equals(w, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The aura a choice names: off, character (what the character wears, or the fallback), or an aura; one the
    /// account does not own (with Cosmetic Drops locking them) is none.</summary>
    Aura? Pick(string choice, Player player, State state)
    {
        if (string.IsNullOrWhiteSpace(choice) || choice.Equals("off", StringComparison.OrdinalIgnoreCase)) return null;
        Aura? aura;
        if (choice.Equals("character", StringComparison.OrdinalIgnoreCase))
            aura = Worn(player) ?? (M.WhenCharacterHasNone.Equals("character", StringComparison.OrdinalIgnoreCase) ? null : Pick(M.WhenCharacterHasNone, player, state));
        else aura = Find(choice);
        return aura is not null && Owns(player, state, aura) ? aura : null;
    }

    /// <summary>The aura of what the character wears in the Character Effect slot (a body aura or a character effect).</summary>
    Aura? Worn(Player player)
    {
        try
        {
            foreach (var e in Game.WornCosmetics(player))
                if (_byCosmetic.TryGetValue(e.MtxTypeHash, out var aura)) return aura;
        }
        catch (Exception) { }
        return null;
    }

    bool Owns(Player player, State state, Aura aura)
    {
        if (!_c.OwnedOnly || !GameEvents.OwnedCosmetics.Any || !player.HasClient) return true;
        if (state.Owned is null || state.OwnedAge > 5)
        {
            try { state.Owned = [.. GameEvents.CosmeticsOwnedBy(player, Game.Mtx.Owned).Select(r => r.MtxTypeHash)]; }
            catch (Exception) { state.Owned = null; return true; }
            state.OwnedAge = 0;
        }
        return state.Owned.Contains(aura.Cosmetic);
    }

    void LoadPicks()
    {
        try
        {
            if (!File.Exists(PicksPath)) return;
            var read = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(PicksPath));
            if (read is null) return;
            _picks = new(StringComparer.OrdinalIgnoreCase);
            foreach (var (character, picks) in read)
            {
                var mine = _picks[character] = new(StringComparer.OrdinalIgnoreCase);
                foreach (var (k, v) in picks) mine[Who(k)] = v;
            }
        }
        catch (Exception e) { _log($"aura-choices.json not read: {e.Message}"); }
    }

    void SavePicks()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PicksPath)!);
            string text;
            lock (SaveLock) text = JsonSerializer.Serialize(_picks, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(PicksPath + ".tmp", text);
            File.Move(PicksPath + ".tmp", PicksPath, true);
        }
        catch (Exception e) { _log($"aura-choices.json not written: {e.Message}"); }
    }

    // ---- Putting them on ----

    Task Tick(Player player, double seconds)
    {
        var state = _states.GetOrCreateValue(player);
        state.Wait -= seconds;
        state.OwnedAge += seconds;
        if (state.Wait > 0) return Task.CompletedTask;
        state.Wait = Math.Clamp(_c.UpdateSeconds, 0.1, 10);
        try { Update(player, state); }
        catch (InvalidOperationException) { } // a list changed while it was read: the next update catches up
        catch (Exception e) { _log($"update: {e.Message}"); }
        return Task.CompletedTask;
    }

    static bool Alive(SpawnedMonster m) => !m.Dead && m.Life != 0;

    void Update(Player player, State state)
    {
        var area = player.Area;
        if (area is null) return;
        if (!ReferenceEquals(state.Area, area)) { state.Area = area; state.Applied.Clear(); state.Replaced.Clear(); }
        bool replace = M.SpectreLook.Equals("replace", StringComparison.OrdinalIgnoreCase) && _spectreBuff is not null;
        var want = new HashSet<(uint, ushort)>();
        var reskin = new Dictionary<uint, ushort>();
        var picked = new Dictionary<string, Aura?>(StringComparer.OrdinalIgnoreCase);
        Aura? For(string who) => picked.TryGetValue(who, out var a) ? a : picked[who] = Pick(Choice(player, who), player, state);

        if (For("me") is { } mine) want.Add((player.Id, mine.Buff));

        void Minion(uint id, string kind, bool alive)
        {
            if (!alive) return;
            state.Kinds.Add(kind);
            if (For(kind) is not { } aura) return;
            // A spectre: its spectral look becomes the aura (as a spectre skin does), else (no spectral buff, or "add")
            // the aura's own buff.
            if (kind == "spectre" && replace && aura.Visual != 0 && area.Buffs.Get(id, _spectreBuff!.Value, id) is not null) reskin[id] = aura.Visual;
            else want.Add((id, aura.Buff));
        }
        foreach (var (id, live) in player.Summons.Fighters.ToArray())
            Minion(id, (player.Summons.Definitions.GetValueOrDefault(id)?.Family ?? "minion").ToLowerInvariant(), Alive(live.Monster));
        foreach (var (id, g) in player.Golems.All.ToArray())
            Minion(id, (g.Definition.Kind is { Length: > 0 } k ? k : "golem").ToLowerInvariant(), Alive(g.Monster));
        foreach (var (id, g) in player.Reservations.Fighters.ToArray())
            Minion(id, (g.Definition.Kind is { Length: > 0 } k ? k : g.Definition.Id).ToLowerInvariant(), Alive(g.Monster));
        if (For("pets") is { } pet)
            foreach (uint id in player.Pets.Keys.ToArray()) want.Add((id, pet.Buff));
        if (For("totems") is { } totem)
            foreach (var t in player.Totems.All.ToArray()) want.Add((t.Id, totem.Buff));
        Aura? trap = For("traps"), mineAura = For("mines");
        if (trap is not null || mineAura is not null)
            foreach (var p in player.Placed.All.Values.ToArray())
                if ((p.Mine ? mineAura : trap) is { } a) want.Add((p.Id, a.Buff));

        if (_spectreBuff is ushort sb)
        {
            foreach (var (entity, visual) in reskin)
                if (area.Buffs.Get(entity, sb, entity) is { } own && own.Visual != visual)
                {
                    state.Replaced.TryAdd(entity, own.Visual);
                    area.Buffs.Set(entity, own with { Visual = visual });
                }
            foreach (var (entity, original) in state.Replaced.Where(r => !reskin.ContainsKey(r.Key)).ToList())
            {
                if (area.Buffs.Get(entity, sb, entity) is { } own) area.Buffs.Set(entity, own with { Visual = original });
                state.Replaced.Remove(entity);
            }
        }
        foreach (var (entity, row) in state.Applied.Where(a => !want.Contains(a)).ToList())
        {
            area.Buffs.Clear(entity, row, entity);
            state.Applied.Remove((entity, row));
        }
        foreach (var (entity, row) in want)
        {
            if (!area.Buffs.Has(entity, row, entity)) area.Buffs.Set(entity, new Buff(row, entity));
            state.Applied.Add((entity, row));
        }
    }

    // ---- /aura ----

    async Task Command(Player player, string[] args)
    {
        var who = Program.PlayerOf(player);
        var state = _states.GetOrCreateValue(player);
        const string Usage = "/aura list [words] | kinds | <me|spectres|minions|pets|totems|traps|mines|a minion kind> <off|character|default|aura>";
        if (args.Length == 0)
        {
            var worn = Worn(player);
            var mine = new List<string>();
            lock (SaveLock)
                if (_picks.TryGetValue(Program.CharacterName(player), out var p)) mine.AddRange(p.Select(kv => $"{kv.Key} {kv.Value}"));
            await who.Tell($"Body Auras: spectres {Choice(player, "spectre")}, other minions {Choice(player, "minions")}, pets {Choice(player, "pets")}, "
                + $"totems {Choice(player, "totems")}, traps {Choice(player, "traps")}, mines {Choice(player, "mines")}, you {Choice(player, "me")}"
                + (mine.Count > 0 ? $" (this character: {string.Join(", ", mine)})" : "")
                + $". Character Effect slot: {worn?.Name ?? "no aura"}. {Usage}");
            return;
        }
        string first = args[0].ToLowerInvariant();
        if (first == "list")
        {
            string words = string.Join(' ', args.Skip(1));
            var hits = _auras.Values.Where(a => words.Length == 0 || a.Name.Contains(words, StringComparison.OrdinalIgnoreCase) || a.Key.Contains(words, StringComparison.OrdinalIgnoreCase))
                .Where(a => Owns(player, state, a)).ToList();
            await who.Tell($"{hits.Count} aura(s){(words.Length > 0 ? $" like \"{words}\"" : "")} you can use: "
                + string.Join(", ", hits.Take(30).Select(a => $"{a.Key} ({a.Name})")) + (hits.Count > 30 ? " ..." : ""));
            return;
        }
        if (first == "kinds")
        {
            await who.Tell("Minion kinds seen so far (each can have its own aura: /aura <kind> <aura>): "
                + (state.Kinds.Count > 0 ? string.Join(", ", state.Kinds) : "none yet, summon some first"));
            return;
        }
        if (args.Length < 2) { await who.Tell(Usage); return; }
        string target = Who(first), value = string.Join(' ', args.Skip(1)).Trim();
        string stored;
        if (value.Equals("off", StringComparison.OrdinalIgnoreCase) || value.Equals("character", StringComparison.OrdinalIgnoreCase)
            || value.Equals("default", StringComparison.OrdinalIgnoreCase)) stored = value.ToLowerInvariant();
        else if (Find(value) is { } aura)
        {
            if (!Owns(player, state, aura)) { await who.Tell($"You don't own the {aura.Name} yet."); return; }
            stored = aura.Key;
        }
        else { await who.Tell($"No aura \"{value}\" (/aura list <words>)."); return; }
        if (target == "me" && stored == "character") { await who.Tell("\"character\" is for minions and the rest: wear a body aura in the Cosmetics tab for yourself."); return; }
        string name = Program.CharacterName(player);
        lock (SaveLock)
        {
            if (!_picks.TryGetValue(name, out var mine)) _picks[name] = mine = new(StringComparer.OrdinalIgnoreCase);
            if (stored == "default") mine.Remove(target); else mine[target] = stored;
            if (mine.Count == 0) _picks.Remove(name);
        }
        SavePicks();
        await who.Tell(stored == "default" ? $"Body Auras: {target} back to the setting ({Choice(player, target)}) for {name}."
            : $"Body Auras: {target} {(_auras.TryGetValue(stored, out var a) ? a.Name : stored)} for {name}.");
    }
}
