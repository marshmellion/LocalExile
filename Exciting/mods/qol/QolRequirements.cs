using System.Reflection;
using GameServer.Core.Protocol.Instance;

/// <summary>Item restrictions, each a switch (settings: requirements). The client checks an item's level requirement
/// against the byte the server sends with it, and its attribute requirements against virtual_*_for_requirements: both
/// come from the server, so these work with the public server too. Gems' attribute checks and the hand rules (a bow with
/// a shield, a quiver without a bow, two two-handed weapons) need the private build's hooks; without them those
/// switches say so in the log.</summary>
public sealed partial class QolMod
{
    sealed record RequirementChoices(bool NoLevel = false, bool NoStrength = false, bool NoDexterity = false,
        bool NoIntelligence = false, bool Gems = true, bool AnyHands = false, bool TwoHandedOffHand = false);

    static Func<Item, int>? _ownLevel;
    static bool _levelHooked;
    static RequirementChoices _requirements = new();

    const int Plenty = 1_000_000;

    void LoadRequirements(IModContext mod)
    {
        string id = mod.Manifest.Id;
        _requirements = _choices.Requirements ?? new();
        var r = _requirements;
        // The level byte: wrapped once, and the wrapper asks the switch each time (a reload of the mod keeps it).
        if (!_levelHooked)
        {
            _ownLevel = Item.LevelRequirementOf;
            Item.LevelRequirementOf = item => _requirements.NoLevel ? 0 : _ownLevel?.Invoke(item) ?? 0;
            _levelHooked = true;
        }
        GameEvents.Stats.Add(id, sheet =>
        {
            if (_requirements.NoStrength) sheet.Add(Stat.VirtualStrengthForRequirements, Plenty);
            if (_requirements.NoDexterity) sheet.Add(Stat.VirtualDexterityForRequirements, Plenty);
            if (_requirements.NoIntelligence) sheet.Add(Stat.VirtualIntelligenceForRequirements, Plenty);
        });
        // The server's own gem check (private build: Game.RequirementAttributeOverride), so a gem the client now
        // accepts is not turned off by the server.
        var gemHook = typeof(Game).GetProperty("RequirementAttributeOverride", BindingFlags.Public | BindingFlags.Static);
        if (gemHook is not null)
            gemHook.SetValue(null, (Func<Player, uint, int, int?>)((_, attribute, value) =>
                !_requirements.Gems ? null
                : attribute == Stat.Strength && _requirements.NoStrength || attribute == Stat.Dexterity && _requirements.NoDexterity
                    || attribute == Stat.Intelligence && _requirements.NoIntelligence ? Plenty : null));
        else if (r.NoStrength || r.NoDexterity || r.NoIntelligence)
            _log("requirements: this server has no gem attribute hand-over (private build): gems keep their attribute requirements on the server's side");
        var inventories = typeof(PlayerInventories);
        var any = inventories.GetProperty("IgnoreHandRules", BindingFlags.Public | BindingFlags.Static);
        var two = inventories.GetProperty("TwoHandedInOffHand", BindingFlags.Public | BindingFlags.Static);
        if (any is not null && two is not null) { any.SetValue(null, r.AnyHands); two.SetValue(null, r.TwoHandedOffHand); }
        else if (r.AnyHands || r.TwoHandedOffHand) _log("requirements: this server has no hand-rule switches (private build)");
        _log($"requirements: level {(r.NoLevel ? "off" : "on")}, strength {(r.NoStrength ? "off" : "on")}, dexterity {(r.NoDexterity ? "off" : "on")}, " +
            $"intelligence {(r.NoIntelligence ? "off" : "on")}, any hands {(r.AnyHands ? "on" : "off")}, two-handed off hand {(r.TwoHandedOffHand ? "on" : "off")}");
    }

    /// <summary>Unloaded: every switch back to the game's rules (the level wrapper stays, asking a switch that is off).</summary>
    static void DisposeRequirements()
    {
        _requirements = new();
        typeof(Game).GetProperty("RequirementAttributeOverride", BindingFlags.Public | BindingFlags.Static)?.SetValue(null, null);
        foreach (string name in new[] { "IgnoreHandRules", "TwoHandedInOffHand" })
            typeof(PlayerInventories).GetProperty(name, BindingFlags.Public | BindingFlags.Static)?.SetValue(null, false);
    }
}
