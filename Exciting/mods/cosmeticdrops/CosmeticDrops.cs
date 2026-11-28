using System.Collections.Concurrent;
using System.Text.Json;
using GameServer.Core;
using GameServer.Core.Protocol.Instance;

/// <summary>
/// Cosmetic Drops: with it on, an account owns no cosmetics (microtransactions) or bought hideouts it has not unlocked.
/// They drop. Target farming: each cosmetic has a source that fits it (cosmetics.json: a boss, league content, a map
/// tileset, strongboxes) and drops there as a Sealed Cosmetic; a right click unlocks it. The box: a Cosmetic Cache drops
/// from monsters, bosses and strongboxes and unlocks a random one. Hybrid (the default) does both.
/// Free things stay owned (the masters' decorations, dances, the default and campaign hideouts); the invisible effects come
/// with 100 copies, pets and weapon effects with 2. Worn cosmetics the account does not own are only hidden: the saves keep
/// them, so turning the mod off brings them back. Needs the modding API's GameEvents.OwnedCosmetics and OwnedHideouts.
/// </summary>
public sealed class CosmeticDropsMod : IMod
{
    sealed record Choices(bool Enabled = true, string Mode = "hybrid", TargetChoices? Target = null, BoxChoices? Box = null,
        OwnershipChoices? Ownership = null, NoticeChoices? Notices = null, bool TestCommands = true);
    sealed record TargetChoices(int Pinnacle = 4, int MapBoss = 50, int Rare = 1500, int Magic = 8000, int Normal = 30000,
        int Strongbox = 25, double LeagueMonsters = 3.0, bool OnlyNew = true, bool FallbackUnbuilt = true, bool TilesetForOtherMaps = true,
        bool SealedItem = true, bool SealedNote = true, Dictionary<string, double>? Sources = null, Dictionary<string, string>? Moves = null);
    sealed record BoxChoices(int Bosses = 20, int Rare = 800, int Magic = 5000, int Normal = 20000, int Strongbox = 10, bool OnlyNew = true,
        bool Hideouts = true, bool PriceWeights = true, double PriceStrength = 1.0);
    sealed record OwnershipChoices(bool BoughtHideouts = true, bool FoundHideouts = true, bool Decorations = true, bool MysteryBoxes = false,
        bool UpgradeScrolls = false, bool ExpiredPets = false, bool KeepShopGrants = false, int PetCopies = 2, int WeaponEffectCopies = 2, int InvisibleCopies = 100, string[]? AlwaysOwned = null,
        string[]? AlwaysOwnedKinds = null, int DecorationsMin = 5, int DecorationsMax = 20);
    sealed record NoticeChoices(bool Chat = true, bool Overlay = true, bool Sound = true, bool InGame = true, bool AnnounceDrops = true,
        bool Banner = true, bool RewardToast = true);

    sealed record Source(string Id, string Label, string Kind, string[] Match, string? Fallback);
    sealed class Cosmetic
    {
        public int Index; public uint Hash; public string Name = ""; public string Kind = ""; public string Set = "";
        public string? Source; public int Weight = 8; public int Copies = 1; public bool Always; public int Value;
    }
    sealed record Hideout(int Row, string Name, string Kind, string Source, int Weight, int Value);
    sealed class Save
    {
        public HashSet<uint> Cosmetics { get; set; } = [];
        public HashSet<int> Hideouts { get; set; } = [];
        /// <summary>How many of each stackable cosmetic (hideout decorations) the account has.</summary>
        public Dictionary<uint, int> Counts { get; set; } = [];
    }

    const string SealedId = "Metadata/Items/Currency/LocalExileSealedCosmetic";
    const string CacheId = "Metadata/Items/Currency/LocalExileCosmeticCache";
    const int HideoutCode = 1_000_000;
    const uint LocalShopIds = 0x100000;

    Choices _c = new();
    TargetChoices T => _c.Target ?? new();
    BoxChoices B => _c.Box ?? new();
    OwnershipChoices O => _c.Ownership ?? new();
    NoticeChoices N => _c.Notices ?? new();
    bool Targeting => _c.Mode is "hybrid" or "target";
    bool Boxes => _c.Mode is "hybrid" or "box";

    readonly List<Source> _sources = [];
    readonly List<Cosmetic> _cosmetics = [];
    readonly Dictionary<uint, Cosmetic> _byHash = [];
    readonly List<Hideout> _hideouts = [];
    readonly Dictionary<int, Hideout> _hideoutByRow = [];
    readonly Dictionary<string, string> _areaNames = new(StringComparer.Ordinal);
    // The "Challenge Complete!" banner rows (content/cosmeticnotices.json): Achievements row by its Id.
    readonly Dictionary<string, ushort> _banners = new(StringComparer.Ordinal);
    // The cosmetics' challenge steps, whose completion (S->C 0x125) brings the game's reward toast with the cosmetic's icon.
    readonly Dictionary<string, ushort> _steps = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, Save> _saves = new(StringComparer.OrdinalIgnoreCase);
    string _folder = "";
    string _savesFolder = "";
    Action<string> _log = _ => { };

    public void Load(IModContext mod)
    {
        string id = mod.Manifest.Id;
        _c = mod.Settings<Choices>();
        _log = mod.Log;
        // What the mod writes lives outside its own folder: the server reloads a mod whenever a file in its folder
        // changes, so saving an unlock there reloaded the mod (three times on one Cosmetic Cache).
        _folder = Path.GetFullPath(Path.Combine("data", "moddata", id));
        _savesFolder = Path.Combine(_folder, "saves");
        Directory.CreateDirectory(_savesFolder);
        MoveOldSaves(Path.Combine(mod.Folder, "saves"));
        LoadCatalog(Path.Combine(mod.Folder, "cosmetics.json"));
        LoadOtherMods(mod.Folder);
        WriteChime(Path.Combine(_folder, "unlock.wav"));
        if (ClientTables.TableOrNull("WorldAreas") is { } areas)
            foreach (var a in areas)
                if (a.TextOrNull("Id") is { } aid && a.TextOrNull("Name") is { } aname) _areaNames.TryAdd(aid, aname);
        if (ClientTables.TableOrNull("Achievements") is { } achievements)
        {
            int row = 0;
            foreach (var a in achievements)
            {
                if (a.TextOrNull("Id") is { } aid && aid.StartsWith("LocalExile", StringComparison.Ordinal) && row <= ushort.MaxValue) _banners[aid] = (ushort)row;
                row++;
            }
        }
        // The cosmetics' hidden challenge steps (content/cosmeticnotices.json): AchievementItems row by Id, for the reward toast.
        if (ClientTables.TableOrNull("AchievementItems") is { } steps)
        {
            int row = 0;
            foreach (var a in steps)
            {
                if (a.TextOrNull("Id") is { } sid && sid.StartsWith("LocalExileCosmeticItem", StringComparison.Ordinal) && row <= ushort.MaxValue) _steps[sid] = (ushort)row;
                row++;
            }
        }
        if (_banners.Count == 0) mod.Log("no unlock banners in the game data yet (the content pack applies at the next start)");
        mod.Log($"{_cosmetics.Count} cosmetics and {_hideouts.Count} hideouts from {_sources.Count} sources; mode {_c.Mode}{(_c.Enabled ? "" : " (off: everything owned)")}");

        if (typeof(GameEvents).GetField("OwnedCosmetics")?.GetValue(null) is GameEvent<Func<Player, IReadOnlyList<InstanceMicrotransactions.Record>, IReadOnlyList<InstanceMicrotransactions.Record>>> owned)
            owned.Add(id, OwnedCosmetics);
        else mod.Log("this server has no GameEvents.OwnedCosmetics (the modding API's): every cosmetic stays owned");
        if (typeof(GameEvents).GetField("OwnedHideouts")?.GetValue(null) is GameEvent<Func<Player, IReadOnlyList<byte>, IReadOnlyList<byte>>> hideouts)
            hideouts.Add(id, OwnedHideouts);
        GameEvents.Drops.Add(id, Drops);
        GameEvents.ItemUse.Add(id, Use);
        if (typeof(GameEvents).GetField("ChestDrops")?.GetValue(null) is GameEvent<Action<Player, string, string, List<ItemDrops.Drop>>> chests)
            chests.Add(id, Chests);
        GameEvents.Command(id, "cosmetics",
            "/cosmetics (what you own) | where <name> | sources | source <id> | owned [words] | missing [source] | give <name> | sealed <name> | cache [n] | unlock <name>|all | lock <name>|all",
            Command);
    }

    /// <summary>The unlock chime (four rising bell notes, 1.6 s) the overlay plays, written by the mod itself: a copied .wav can
    /// arrive damaged, and this needs no file shipped. Rewritten when it is not the right size.</summary>
    // Saves made by 1.0.0 (in the mod's own folder): copied once to the new place; the old files stay as they were.
    void MoveOldSaves(string old)
    {
        try
        {
            if (!Directory.Exists(old)) return;
            foreach (string file in Directory.GetFiles(old, "*.json"))
            {
                string to = Path.Combine(_savesFolder, Path.GetFileName(file));
                if (!File.Exists(to)) { File.Copy(file, to); _log($"carried over the saved unlocks in {Path.GetFileName(file)}"); }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _log($"old saves not carried over: {e.Message}"); }
    }

    void WriteChime(string file)
    {
        const int rate = 44100, frames = 70560;
        try
        {
            if (File.Exists(file) && new FileInfo(file).Length == 44 + frames * 2) return;
            (double Start, double Hz)[] notes = [(0.0, 1318.5), (0.12, 1760.0), (0.24, 2093.0), (0.36, 2637.0)];
            using var w = new BinaryWriter(File.Create(file));
            w.Write("RIFF"u8); w.Write(36 + frames * 2); w.Write("WAVE"u8); w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16); w.Write("data"u8); w.Write(frames * 2);
            for (int i = 0; i < frames; i++)
            {
                double t = (double)i / rate, v = 0;
                foreach (var (start, hz) in notes)
                    if (t >= start)
                    {
                        double u = t - start, env = Math.Exp(-u * 3.2) * Math.Min(1, u * 80);
                        v += env * (Math.Sin(2 * Math.PI * hz * u) + 0.35 * Math.Sin(2 * Math.PI * hz * 2.01 * u) + 0.15 * Math.Sin(2 * Math.PI * hz * 3.02 * u));
                    }
                w.Write((short)(Math.Clamp(v * 0.22, -1, 1) * 32000));
            }
        }
        catch (Exception e) { _log($"the chime was not written: {e.Message}"); }
    }

    // ---- The catalogue (tools/make_cosmetics.py) ----

    /// <summary>Cosmetics other mods add to the game (Body Aura's body auras): each mod folder beside this one may have a
    /// cosmeticdrops.json with a "cosmetics" list in cosmetics.json's form. Only the ones the game data has (that mod's
    /// content applied) and this catalog does not are added; they drop like the rest.</summary>
    void LoadOtherMods(string ownFolder)
    {
        string? parent = Path.GetDirectoryName(Path.GetFullPath(ownFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (parent is null || !Directory.Exists(parent)) return;
        var known = new HashSet<uint>();
        try { foreach (var r in Game.Mtx.Owned) known.Add(r.MtxTypeHash); } catch (Exception) { }
        foreach (string dir in Directory.GetDirectories(parent).Order(StringComparer.OrdinalIgnoreCase))
        {
            string file = Path.Combine(dir, "cosmeticdrops.json");
            if (Path.GetFullPath(dir).Equals(Path.GetFullPath(ownFolder).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) || !File.Exists(file)) continue;
            int added = 0, absent = 0;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                foreach (var e in doc.RootElement.GetProperty("cosmetics").EnumerateArray())
                {
                    uint hash = e.GetProperty("hash").GetUInt32();
                    if (_byHash.ContainsKey(hash)) continue;
                    if (known.Count > 0 && !known.Contains(hash)) { absent++; continue; }
                    var c = new Cosmetic
                    {
                        Index = _cosmetics.Count, Hash = hash, Name = e.GetProperty("name").GetString() ?? "",
                        Kind = e.TryGetProperty("kind", out var k) ? k.GetString() ?? "" : "",
                        Set = e.TryGetProperty("set", out var st) ? st.GetString() ?? "" : "",
                        Source = e.TryGetProperty("source", out var src) && src.ValueKind == JsonValueKind.String ? src.GetString() : null,
                        Weight = e.TryGetProperty("weight", out var w) ? w.GetInt32() : 8,
                        Copies = e.TryGetProperty("copies", out var cp) ? cp.GetInt32() : 1,
                        Always = e.TryGetProperty("always", out var al) && al.GetBoolean(),
                        Value = e.TryGetProperty("value", out var v) ? v.GetInt32() : 0,
                    };
                    _cosmetics.Add(c);
                    _byHash.TryAdd(c.Hash, c);
                    added++;
                }
            }
            catch (Exception ex) { _log($"{file}: not read: {ex.Message}"); continue; }
            _log($"{Path.GetFileName(dir)}: {added} more cosmetic(s) drop" + (absent > 0 ? $" ({absent} not in the game data: start the game with the play button first)" : ""));
        }
    }

    void LoadCatalog(string file)
    {
        if (!File.Exists(file)) { _log("cosmetics.json is missing: nothing is locked"); return; }
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        var root = doc.RootElement;
        foreach (var s in root.GetProperty("sources").EnumerateArray())
            _sources.Add(new(s.GetProperty("id").GetString()!, s.GetProperty("label").GetString()!, s.GetProperty("kind").GetString()!,
                [.. s.GetProperty("match").EnumerateArray().Select(m => m.GetString()!)],
                s.TryGetProperty("fallback", out var f) ? f.GetString() : null));
        foreach (var e in root.GetProperty("cosmetics").EnumerateArray())
        {
            var c = new Cosmetic
            {
                Index = _cosmetics.Count, Hash = e.GetProperty("hash").GetUInt32(), Name = e.GetProperty("name").GetString() ?? "",
                Kind = e.GetProperty("kind").GetString() ?? "", Set = e.TryGetProperty("set", out var st) ? st.GetString() ?? "" : "",
                Source = e.TryGetProperty("source", out var src) ? src.GetString() : null,
                Weight = e.TryGetProperty("weight", out var w) ? w.GetInt32() : 8,
                Copies = e.TryGetProperty("copies", out var cp) ? cp.GetInt32() : 1,
                Always = e.TryGetProperty("always", out var al) && al.GetBoolean(),
                Value = e.TryGetProperty("value", out var v) ? v.GetInt32() : 0,
            };
            _cosmetics.Add(c);
            _byHash.TryAdd(c.Hash, c);
        }
        foreach (var h in root.GetProperty("hideouts").EnumerateArray())
        {
            var hideout = new Hideout(h.GetProperty("row").GetInt32(), h.GetProperty("name").GetString() ?? "", h.GetProperty("kind").GetString() ?? "",
                h.GetProperty("source").GetString() ?? "", h.GetProperty("weight").GetInt32(),
                h.TryGetProperty("value", out var hv) ? hv.GetInt32() : 0);
            _hideouts.Add(hideout);
            _hideoutByRow[hideout.Row] = hideout;
        }
    }

    Source? SourceById(string id) => _sources.FirstOrDefault(s => s.Id == id);

    /// <summary>Where a cosmetic drops now: a move from the settings, else its own source, else (content the server does not
    /// run) its fallback.</summary>
    string? SourceOf(Cosmetic c)
    {
        if (T.Moves is { Count: > 0 } moves && (moves.TryGetValue(c.Name, out var to) || moves.TryGetValue(c.Set, out to))) return to;
        return Resolve(c.Source);
    }

    string? Resolve(string? source) =>
        source is not null && T.FallbackUnbuilt && SourceById(source) is { Fallback: { } fb } ? fb : source;

    int CopiesOf(Cosmetic c, Save? save = null) => c.Kind == "Hideout Item" && save is not null && save.Counts.TryGetValue(c.Hash, out int n) ? Math.Clamp(n, 1, 127)
        : c.Always ? Math.Clamp(O.InvisibleCopies, 1, 127)
        : c.Kind == "Pet" ? Math.Clamp(O.PetCopies, 1, 127)
        : c.Kind is "Weapon Effect" or "Weapon Added Effect" ? Math.Clamp(O.WeaponEffectCopies, 1, 127)
        : Math.Clamp(c.Copies, 1, 127);

    /// <summary>Whether the account owns this cosmetic whatever it unlocked: the invisible ones, the always-owned names and kinds,
    /// decorations when they are not locked, mystery boxes when they do not drop.</summary>
    bool Free(Cosmetic c) => c.Always
        || O.AlwaysOwned?.Contains(c.Name, StringComparer.OrdinalIgnoreCase) == true
        || (O.AlwaysOwnedKinds ?? ["Account"]).Contains(c.Kind, StringComparer.OrdinalIgnoreCase)
        || (!O.Decorations && c.Kind == "Hideout Item");

    bool Droppable(Cosmetic c) => !Free(c) && (O.MysteryBoxes || !c.Name.Contains("Mystery Box", StringComparison.OrdinalIgnoreCase))
        && (O.UpgradeScrolls || !IsUpgradeScroll(c)) && (O.ExpiredPets || !IsExpiredPet(c));

    /// <summary>The shop's upgrade and convert scrolls (consumables that change another cosmetic: "Pet Upgrade Scroll",
    /// "Goddess Set Upgrade Scroll" ...): nothing to use them on here, so by default they don't drop.</summary>
    static bool IsUpgradeScroll(Cosmetic c) => c.Kind == "Consumable" && c.Name.Contains("Scroll", StringComparison.OrdinalIgnoreCase);

    /// <summary>"... Premium Expired Pet": a premium pet's lapsed copy; by default it doesn't drop.</summary>
    static bool IsExpiredPet(Cosmetic c) => c.Kind == "Pet" && c.Name.Contains("Expired", StringComparison.OrdinalIgnoreCase);

    bool Locked(Hideout h) => h.Kind == "bought" ? O.BoughtHideouts : O.FoundHideouts;

    // ---- Saves: per account, in the mod's saves folder ----

    static string AccountOf(Player player) => player.Character.Account ?? Program.PlayerOf(player).Character;

    Save SaveOf(Player player) => _saves.GetOrAdd(AccountOf(player), account =>
    {
        try
        {
            string file = SaveFile(account);
            if (File.Exists(file) && JsonSerializer.Deserialize<Save>(File.ReadAllText(file)) is { } s) return s;
        }
        catch (Exception e) { _log($"the save of {account} is not readable ({e.Message}): starting empty"); }
        return new Save();
    });

    string SaveFile(string account) => Path.Combine(_savesFolder, string.Concat(account.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_')) + ".json");

    void Write(Player player)
    {
        var save = SaveOf(player);
        try
        {
            Directory.CreateDirectory(_savesFolder);
            string json;
            lock (save) json = JsonSerializer.Serialize(save);
            File.WriteAllText(SaveFile(AccountOf(player)), json);
        }
        catch (Exception e) { _log($"save not written: {e.Message}"); }
    }

    // ---- The hooks: what the account owns ----

    IReadOnlyList<InstanceMicrotransactions.Record> OwnedCosmetics(Player player, IReadOnlyList<InstanceMicrotransactions.Record> all)
    {
        if (!_c.Enabled || _cosmetics.Count == 0) return all;
        // A player the server drives (a bot, or a Player bots copy of one of your characters) owns what it wears: only
        // the copy's worn picks reach it, and those are the original's.
        if (!player.HasClient) return all;
        var save = SaveOf(player);
        var owned = new List<InstanceMicrotransactions.Record>(all.Count);
        lock (save)
            foreach (var r in all)
            {
                // The offline shop's grants (and what its mystery boxes gave).
                if (r.Id >= LocalShopIds && r.Id < GameEvents.ModCosmeticIds) { if (O.KeepShopGrants) owned.Add(r); continue; }
                if (!_byHash.TryGetValue(r.MtxTypeHash, out var c)) { owned.Add(r); continue; }
                if (!Free(c) && !save.Cosmetics.Contains(c.Hash)) continue;
                owned.Add(r);
                int copies = CopiesOf(c, save);
                for (int k = 1; k < copies; k++) owned.Add(r with { Id = CopyId(c, k) });
            }
        return owned;
    }

    static uint CopyId(Cosmetic c, int k) => GameEvents.ModCosmeticIds + (uint)c.Index * 128 + (uint)k;

    IReadOnlyList<byte> OwnedHideouts(Player player, IReadOnlyList<byte> rows)
    {
        if (!_c.Enabled) return rows;
        var save = SaveOf(player);
        lock (save) return [.. rows.Where(r => !_hideoutByRow.TryGetValue(r, out var h) || !Locked(h) || save.Hideouts.Contains(r))];
    }

    // ---- Unlocking ----

    async Task Unlock(Player player, Cosmetic c, string how)
    {
        var save = SaveOf(player);
        bool fresh, decoration = c.Kind == "Hideout Item";
        int had, now, gained = 0;
        lock (save)
        {
            fresh = save.Cosmetics.Add(c.Hash);
            had = fresh ? 0 : CopiesOf(c, save);
            // Hideout decorations come several at a time, and more of one you have add up (127 at most).
            if (decoration)
            {
                int lo = Math.Clamp(O.DecorationsMin, 1, 127), hi = Math.Clamp(Math.Max(O.DecorationsMax, O.DecorationsMin), 1, 127);
                gained = Random.Shared.Next(lo, hi + 1);
                save.Counts[c.Hash] = Math.Min(127, save.Counts.GetValueOrDefault(c.Hash) + gained);
            }
            now = CopiesOf(c, save);
        }
        if (fresh || decoration) Write(player);
        if (Game.Mtx.ByHash(c.Hash) is { } record)
        {
            if (fresh) await Program.Send(player, InstanceMicrotransactions.RecordUpsert(record, false, false, N.InGame), $"cosmeticdrops: unlocked {c.Name}");
            for (int k = Math.Max(1, had); k < now; k++)
                await Program.Send(player, InstanceMicrotransactions.RecordUpsert(record with { Id = CopyId(c, k) }, false, false, false), $"cosmeticdrops: copy {k} of {c.Name}");
        }
        string title = decoration ? $"Decoration{(gained == 1 ? "" : "s")}: {c.Name} x{gained}{(now > gained ? $" ({now} in all)" : "")}"
            : fresh ? $"Cosmetic unlocked: {c.Name}{(now > 1 ? $" (x{now})" : "")}" : $"Already owned: {c.Name}";
        await Notice(player, title, $"{c.Kind} - {how}", $"LocalExileCosmetic{c.Index}");
    }

    async Task Unlock(Player player, Hideout h, string how)
    {
        var save = SaveOf(player);
        bool fresh;
        lock (save) fresh = save.Hideouts.Add(h.Row);
        if (fresh) Write(player);
        await Program.Send(player, InstanceMicrotransactions.Hideouts(GameEvents.HideoutsOwnedBy(player, HideoutRows())), $"cosmeticdrops: unlocked {h.Name}");
        await Notice(player, fresh ? $"Hideout unlocked: {h.Name}" : $"Already owned: {h.Name}", $"Hideout - {how}", $"LocalExileHideout{h.Row}");
    }

    static byte[] HideoutRows() => HideoutCatalog.OwnedRows;

    async Task Notice(Player player, string title, string detail, string? banner = null)
    {
        // A cosmetic: the game's reward toast on the right of the screen, with the cosmetic's own icon (its challenge step
        // completes: "New cosmetic: <name> (Complete)" and "<name> unlocked!"). Needs the game's challenge notifications on.
        bool toasted = false;
        if (N.RewardToast && banner is not null && _steps.TryGetValue(banner.Replace("LocalExileCosmetic", "LocalExileCosmeticItem"), out ushort step))
        {
            await Program.Send(player, InstanceAchievements.AchievementItemComplete(step), $"cosmeticdrops: reward toast {banner}");
            toasted = true;
        }
        // A hideout (or a cosmetic without the toast): the "Challenge Complete!" banner with its name.
        if (N.Banner && !toasted && banner is not null && _banners.TryGetValue(banner, out ushort row))
            await Program.Send(player, InstanceAchievements.AchievementComplete(row), $"cosmeticdrops: banner {banner}");
        if (N.Chat) await Program.PlayerOf(player).Tell($"* {title} ({detail})");
        if (N.Overlay && AppDomain.CurrentDomain.GetData("localexile.qol.toast") is Action<Player, string, string?> toast)
            toast(player, $"{title}\n{detail}", N.Sound ? Path.Combine(_folder, "unlock.wav") : null);
    }

    void Say(Player player, string text) => _ = Task.Run(async () =>
    {
        try { await Program.PlayerOf(player).Tell(text); } catch (Exception e) { _log($"not told: {e.Message}"); }
    });

    // ---- Drops ----

    string AreaId(Player player) => Game.MapRunOf(player)?.Area.Id ?? player.Layout.Id;
    string AreaName(Player player) => Game.MapRunOf(player)?.Area.Name ?? _areaNames.GetValueOrDefault(player.Layout.Id, player.Layout.Id);

    /// <summary>The sources this kill can drop from, each with its chance (1 in X, after the source's multiplier).</summary>
    List<(Source Source, double OneIn)> SourcesFor(Player player, MonsterInfo monster)
    {
        var found = new List<(Source, double)>();
        string variety = monster.VarietyId, areaId = AreaId(player), areaName = AreaName(player);
        bool inMap = Game.MapRunOf(player) is { Atlas: true };
        bool boss = monster.Rarity == MonsterRarity.Unique;
        double ByRarity() => monster.Rarity switch
        {
            MonsterRarity.Unique => T.MapBoss, MonsterRarity.Rare => T.Rare, MonsterRarity.Magic => T.Magic, _ => T.Normal,
        };
        bool tileset = false;
        foreach (var s in _sources)
        {
            double oneIn = s.Kind switch
            {
                "boss" when boss && s.Match.Any(m => variety.Contains(m, StringComparison.OrdinalIgnoreCase)) => T.Pinnacle,
                "monster" when s.Match.Any(m => variety.Contains(m, StringComparison.OrdinalIgnoreCase)) => ByRarity() / Math.Max(0.01, T.LeagueMonsters),
                "area" when s.Match.Any(m => areaId.Contains(m, StringComparison.OrdinalIgnoreCase) || areaName.Contains(m, StringComparison.OrdinalIgnoreCase)) => ByRarity(),
                "mapboss" when boss && inMap => T.MapBoss,
                _ => 0,
            };
            if (oneIn <= 0) continue;
            if (s.Kind == "area" && s.Id != "delve") tileset = true;
            found.Add((s, oneIn / Multiplier(s.Id)));
        }
        // A map no tileset's words name still has one, chosen by its name.
        if (!tileset && inMap && T.TilesetForOtherMaps)
        {
            var tilesets = _sources.Where(s => s.Kind == "area" && s.Id != "delve").ToList();
            if (tilesets.Count > 0)
            {
                var s = tilesets[(int)(StableHash(areaId) % (uint)tilesets.Count)];
                found.Add((s, ByRarity() / Multiplier(s.Id)));
            }
        }
        return [.. found.Where(f => !double.IsInfinity(f.Item2) && f.Item2 > 0)];
    }

    double Multiplier(string source) => T.Sources is { } m && m.TryGetValue(source, out double x) ? Math.Max(0, x) : 1.0;

    static uint StableHash(string s)
    {
        uint h = 2166136261;
        foreach (char ch in s) h = (h ^ ch) * 16777619;
        return h;
    }

    /// <summary>One cosmetic or hideout of this source, by weight (only ones not owned, when the setting says): the Charges code
    /// a Sealed Cosmetic carries, or 0 for none.</summary>
    int PickFrom(Player player, string? source, bool onlyNew, Random rng, bool hideouts = true, bool priced = false)
    {
        var save = SaveOf(player);
        var pool = new List<(int Code, int Weight)>();
        lock (save)
        {
            foreach (var c in _cosmetics)
                if (Droppable(c) && (source is null || SourceOf(c) == source) && !(onlyNew && save.Cosmetics.Contains(c.Hash)
                    && (c.Kind != "Hideout Item" || save.Counts.GetValueOrDefault(c.Hash) >= 127)))
                    pool.Add((c.Index + 1, Weigh(c.Weight, c.Value, priced)));
            if (hideouts)
                foreach (var h in _hideouts)
                    if (Locked(h) && (source is null || Resolve(h.Source) == source) && !(onlyNew && save.Hideouts.Contains(h.Row)))
                        pool.Add((HideoutCode + h.Row, Weigh(h.Weight, h.Value, priced)));
        }
        if (pool.Count == 0) return 0;
        int at = rng.Next(pool.Sum(p => p.Weight));
        foreach (var (code, weight) in pool) { if (at < weight) return code; at -= weight; }
        return pool[^1].Code;
    }

    /// <summary>A pick's weight: its kind's, and from a Cosmetic Cache also its price (the shop's points; 0 for unknown): one at 400
    /// points weighs a fifth of a free one at the default strength.</summary>
    int Weigh(int weight, int value, bool priced)
    {
        double w = Math.Max(1, weight) * 100;
        if (priced && B.PriceWeights) w *= Math.Pow(100.0 / (100 + Math.Max(0, value)), Math.Max(0, B.PriceStrength));
        return Math.Max(1, (int)Math.Round(w));
    }

    string NameOf(int code) => code >= HideoutCode
        ? _hideoutByRow.TryGetValue(code - HideoutCode, out var h) ? h.Name + " (hideout)" : "a hideout"
        : code > 0 && code <= _cosmetics.Count ? _cosmetics[code - 1].Name : "a cosmetic";

    Item? Sealed(Player player, int code) =>
        Game.ItemsFor(player).Find(SealedId) is { Supported: true } type ? ItemCatalog.Create(type) with { Charges = code } : null;

    Item? Cache(Player player, int count = 1) =>
        Game.ItemsFor(player).Find(CacheId) is { Supported: true } type ? ItemCatalog.Create(type) with { StackSize = (ushort)Math.Clamp(count, 1, type.MaxStack) } : null;

    void Drops(DropRoll d)
    {
        if (!_c.Enabled || _cosmetics.Count == 0) return;
        var player = d.Player;
        var rng = Random.Shared;
        double quantity = Math.Max(0, d.Quantity);
        if (Targeting)
            foreach (var (source, oneIn) in SourcesFor(player, d.Monster))
            {
                if (rng.NextDouble() * oneIn >= quantity) continue;
                int code = PickFrom(player, source.Id, T.OnlyNew, rng);
                if (code == 0) continue;
                Deliver(player, code, d.Made, $"from {source.Label}", SealedNote(player, source, d.Monster));
            }
        if (Boxes)
        {
            int oneIn = d.Monster.Rarity switch { MonsterRarity.Unique => B.Bosses, MonsterRarity.Rare => B.Rare, MonsterRarity.Magic => B.Magic, _ => B.Normal };
            if (oneIn > 0 && rng.NextDouble() * oneIn < quantity && Cache(player) is { } cache)
            {
                d.Made.Add(cache);
                if (N.AnnounceDrops) Say(player, "* A Cosmetic Cache dropped.");
            }
        }
    }

    /// <summary>A target-farmed find: a Sealed Cosmetic among the drops, or (with SealedItem off) unlocked at once.</summary>
    void Deliver(Player player, int code, List<Item> into, string how, string? note = null)
    {
        if (T.SealedItem && Sealed(player, code) is { } item)
        {
            if (note is not null) item = item with { Note = note };
            into.Add(item);
            if (N.AnnounceDrops) Say(player, $"* A Sealed Cosmetic dropped {how}.");
            return;
        }
        _ = Task.Run(() => UnlockCode(player, code, how));
    }

    /// <summary>The note a Sealed Cosmetic carries (shown on the item): what dropped it, and how much of that source's
    /// collection this account has unlocked, so it can be finished there. Null with the setting off.</summary>
    string? SealedNote(Player player, Source source, MonsterInfo monster)
    {
        if (!T.SealedNote) return null;
        var (owned, total) = Collection(player, source.Id);
        string who = string.IsNullOrWhiteSpace(monster.Name) || string.Equals(monster.Name, source.Label, StringComparison.OrdinalIgnoreCase)
            ? source.Label : $"{monster.Name} ({source.Label})";
        return $"Dropped by {who}. Its collection: {total} cosmetics, {owned} unlocked before this one";
    }

    /// <summary>How many of a source's cosmetics and hideouts the account has unlocked, and how many drop there.</summary>
    (int Owned, int Total) Collection(Player player, string source)
    {
        var save = SaveOf(player);
        int owned = 0, total = 0;
        lock (save)
        {
            foreach (var c in _cosmetics)
                if (Droppable(c) && SourceOf(c) == source) { total++; if (save.Cosmetics.Contains(c.Hash)) owned++; }
            foreach (var h in _hideouts)
                if (Locked(h) && Resolve(h.Source) == source) { total++; if (save.Hideouts.Contains(h.Row)) owned++; }
        }
        return (owned, total);
    }

    async Task UnlockCode(Player player, int code, string how)
    {
        try
        {
            if (code >= HideoutCode && _hideoutByRow.TryGetValue(code - HideoutCode, out var h)) await Unlock(player, h, how);
            else if (code > 0 && code <= _cosmetics.Count) await Unlock(player, _cosmetics[code - 1], how);
        }
        catch (Exception e) { _log($"unlock failed: {e.Message}"); }
    }

    void Chests(Player player, string kind, string id, List<ItemDrops.Drop> drops)
    {
        if (!_c.Enabled || !kind.EndsWith("strongbox", StringComparison.Ordinal)) return;
        var rng = Random.Shared;
        if (Targeting && T.Strongbox > 0 && rng.Next(Math.Max(1, (int)(T.Strongbox / Multiplier("strongboxes")))) == 0)
        {
            int code = PickFrom(player, "strongboxes", T.OnlyNew, rng);
            if (code == 0) code = PickFrom(player, TilesetHere(player), T.OnlyNew, rng);
            var made = new List<Item>();
            if (code != 0) Deliver(player, code, made, "from the strongbox");
            foreach (var item in made)
                if (Game.ItemsFor(player).ByHash(item.TypeHash) is { } type) drops.Add(new ItemDrops.Drop(item, type));
        }
        if (Boxes && B.Strongbox > 0 && rng.Next(B.Strongbox) == 0 && Cache(player) is { } cache && Game.ItemsFor(player).ByHash(cache.TypeHash) is { } ct)
        {
            drops.Add(new ItemDrops.Drop(cache, ct));
            if (N.AnnounceDrops) Say(player, "* A Cosmetic Cache was in the strongbox.");
        }
    }

    /// <summary>The tileset of the area the player is in (a strongbox there falls back to it).</summary>
    string? TilesetHere(Player player)
    {
        string areaId = AreaId(player), areaName = AreaName(player);
        return _sources.FirstOrDefault(s => s.Kind == "area" && s.Id != "delve"
            && s.Match.Any(m => areaId.Contains(m, StringComparison.OrdinalIgnoreCase) || areaName.Contains(m, StringComparison.OrdinalIgnoreCase)))?.Id;
    }

    // ---- Right click: a Sealed Cosmetic or a Cosmetic Cache ----

    async Task<bool> Use(Player player, byte row, InventoryItem item, ItemCatalog.BaseType type)
    {
        if (type.Id != SealedId && type.Id != CacheId) return false;
        if (!_c.Enabled) { await Program.PlayerOf(player).Tell("Cosmetic Drops is off: you own every cosmetic already."); return true; }
        bool sealedOne = type.Id == SealedId;
        int code = sealedOne ? item.Item.Charges : 0;
        if (code == 0) code = PickFrom(player, null, sealedOne ? T.OnlyNew : B.OnlyNew, Random.Shared, hideouts: !sealedOne ? B.Hideouts : true, priced: !sealedOne);
        if (code == 0) { await Program.PlayerOf(player).Tell("You own every cosmetic this could give."); return true; }
        var items = Game.ItemsOf(player);
        var taken = Saves.Atomically(() => items.ConsumeOne(row, item.Id));
        if (taken is null) return true;
        await Program.Send(player, InstanceItems.InventoryChange(PlayerInventories.InventoryId(row), items.NextRevision(row), [item.Id],
            taken.Left is { } left ? [left] : []), $"cosmeticdrops: {type.Name} used");
        await UnlockCode(player, code, sealedOne ? "from a Sealed Cosmetic" : "from a Cosmetic Cache");
        return true;
    }

    // ---- /cosmetics ----

    async Task Command(Player player, string[] args)
    {
        var who = Program.PlayerOf(player);
        string what = args.Length > 0 ? args[0].ToLowerInvariant() : "";
        string rest = string.Join(' ', args.Skip(1));
        var save = SaveOf(player);
        switch (what)
        {
            case "":
            {
                int lockable, owned, hl, ho;
                lock (save)
                {
                    lockable = _cosmetics.Count(c => !Free(c));
                    owned = _cosmetics.Count(c => !Free(c) && save.Cosmetics.Contains(c.Hash));
                    hl = _hideouts.Count(Locked);
                    ho = _hideouts.Count(h => Locked(h) && save.Hideouts.Contains(h.Row));
                }
                await who.Tell($"Cosmetic Drops ({(_c.Enabled ? _c.Mode : "off")}): {owned} of {lockable} cosmetics, {ho} of {hl} hideouts. /cosmetics where <name> tells where one drops.");
                return;
            }
            case "where":
            {
                var hits = _cosmetics.Where(c => c.Name.Contains(rest, StringComparison.OrdinalIgnoreCase)).Take(8).ToList();
                var hh = _hideouts.Where(h => h.Name.Contains(rest, StringComparison.OrdinalIgnoreCase)).Take(4).ToList();
                if (rest.Length == 0 || hits.Count + hh.Count == 0) { await who.Tell("No cosmetic or hideout by that name."); return; }
                foreach (var c in hits)
                    await who.Tell($"{c.Name} ({c.Kind}): {(Free(c) ? "always owned" : SourceById(SourceOf(c) ?? "")?.Label ?? "nowhere")}{(save.Cosmetics.Contains(c.Hash) ? ", owned" : "")}");
                foreach (var h in hh)
                    await who.Tell($"{h.Name} (hideout): {(Locked(h) ? SourceById(Resolve(h.Source) ?? "")?.Label ?? "nowhere" : "always owned")}{(save.Hideouts.Contains(h.Row) ? ", owned" : "")}");
                return;
            }
            case "sources":
            {
                var counts = _cosmetics.Where(Droppable).GroupBy(c => SourceOf(c) ?? "").ToDictionary(g => g.Key, g => g.Count());
                await who.Tell("Cosmetic Drops sources (id: cosmetics): " + string.Join(", ", _sources.Where(s => counts.ContainsKey(s.Id))
                    .Select(s => $"{s.Id} {counts[s.Id]}")));
                return;
            }
            case "source" or "missing":
            {
                string id = rest.Length > 0 ? rest.ToLowerInvariant() : what == "missing" ? TilesetHere(player) ?? "" : "";
                var list = _cosmetics.Where(c => Droppable(c) && (id.Length == 0 || SourceOf(c) == id)).ToList();
                List<Cosmetic> missing;
                lock (save) missing = [.. list.Where(c => !save.Cosmetics.Contains(c.Hash))];
                await who.Tell($"{(id.Length > 0 ? SourceById(id)?.Label ?? id : "Everywhere")}: {list.Count - missing.Count} of {list.Count} owned. Missing: " +
                    string.Join(", ", missing.Take(15).Select(c => c.Name)) + (missing.Count > 15 ? $" and {missing.Count - 15} more" : ""));
                return;
            }
            case "owned":
            {
                List<string> names;
                lock (save) names = [.. _cosmetics.Where(c => save.Cosmetics.Contains(c.Hash) && c.Name.Contains(rest, StringComparison.OrdinalIgnoreCase)).Select(c => c.Name)];
                await who.Tell($"{names.Count} owned{(rest.Length > 0 ? $" like \"{rest}\"" : "")}: " + string.Join(", ", names.Take(25)) + (names.Count > 25 ? " ..." : ""));
                return;
            }
        }
        if (!_c.TestCommands) { await who.Tell("Test commands are off in Cosmetic Drops' settings."); return; }
        switch (what)
        {
            case "give" or "unlock" when rest.Equals("all", StringComparison.OrdinalIgnoreCase):
                lock (save) { foreach (var c in _cosmetics) save.Cosmetics.Add(c.Hash); foreach (var h in _hideouts) save.Hideouts.Add(h.Row); }
                Write(player);
                await who.Tell("Every cosmetic and hideout unlocked. Change area (or log in again) to see all of them.");
                return;
            case "lock" when rest.Equals("all", StringComparison.OrdinalIgnoreCase):
                lock (save) { save.Cosmetics.Clear(); save.Hideouts.Clear(); }
                Write(player);
                player.Character.WornCosmetics = null;
                await Game.SendWornCosmetics(player, "cosmetics locked");
                await who.Tell("Every cosmetic and hideout locked again. Log in again to clear the panels.");
                return;
            case "give" or "unlock" or "sealed" or "lock":
            {
                int code = Find(rest);
                if (code == 0) { await who.Tell("No cosmetic or hideout by that name (/cosmetics where <name>)."); return; }
                if (what == "lock")
                {
                    lock (save) { if (code >= HideoutCode) save.Hideouts.Remove(code - HideoutCode); else save.Cosmetics.Remove(_cosmetics[code - 1].Hash); }
                    Write(player);
                    player.Character.WornCosmetics = null;
                    await Game.SendWornCosmetics(player, "cosmetic locked");
                    await who.Tell($"Locked again: {NameOf(code)}.");
                    return;
                }
                if (what == "sealed")
                {
                    if (Sealed(player, code) is not { } item) { await who.Tell("No Sealed Cosmetic in the game data (apply the content pack, then start again)."); return; }
                    await Give(player, item, "a Sealed Cosmetic");
                    await who.Tell($"A Sealed Cosmetic ({NameOf(code)} inside). Right click it.");
                    return;
                }
                await UnlockCode(player, code, "a test command");
                return;
            }
            case "cache":
            {
                int n = int.TryParse(rest, out int k) ? k : 1;
                if (Cache(player, n) is not { } cache) { await who.Tell("No Cosmetic Cache in the game data (apply the content pack, then start again)."); return; }
                await Give(player, cache, "Cosmetic Caches");
                await who.Tell($"{cache.StackSize} Cosmetic Cache{(cache.StackSize == 1 ? "" : "s")}. Right click to open.");
                return;
            }
        }
        await who.Tell("/cosmetics | where <name> | sources | source <id> | missing [source] | owned [words] | give <name>|all | sealed <name> | cache [n] | lock <name>|all");
    }

    int Find(string name)
    {
        if (name.Length == 0) return 0;
        var c = _cosmetics.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? _cosmetics.FirstOrDefault(x => x.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
        if (c is not null) return c.Index + 1;
        var h = _hideouts.FirstOrDefault(x => x.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
        return h is null ? 0 : HideoutCode + h.Row;
    }

    async Task Give(Player player, Item item, string what)
    {
        var inventory = Game.ItemsOf(player);
        byte row = PlayerInventories.MainInventory;
        if (Game.ShapeOf(item) is { } shape && inventory.FreeCell(row, shape.Width, shape.Height, Game.ShapeOf) is { } cell)
        {
            var placed = inventory.Add(row, cell.X, cell.Y, item);
            await Program.Send(player, InstanceItems.InventoryChange(PlayerInventories.InventoryId(row), inventory.NextRevision(row), [], [placed]),
                $"cosmeticdrops: {what} into the inventory");
            return;
        }
        var (x, y) = Program.PlayerOf(player).Position;
        var lying = new WorldItemObject(x, y, item);
        uint id = player.Instance.Ground.Add(lying, player);
        await Game.SendGroundItem(player, id, lying, $"cosmeticdrops: 0x02bb create world item 0x{id:x} ({what})", justDropped: true);
    }
}
