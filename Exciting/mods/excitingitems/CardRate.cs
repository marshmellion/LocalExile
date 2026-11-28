/// <summary>Divination cards. DropChance scales how often a kill drops a card at all (the server's own card chance, in
/// maps and in the campaign: 2 = twice as often); PerCardChance makes one card (by its name) that much likelier to be the
/// one picked. Multiplier and PerCard are copies instead: every card that drops comes that many times (2.5: two of it and
/// half the time a third). Chests and strongboxes roll their cards before the mod sees them, so only the copies apply
/// there.</summary>
sealed record CardRateChoices(double Multiplier = 1.0, Dictionary<string, double>? PerCard = null, bool MapsOnly = false,
    double DropChance = 1.0, Dictionary<string, double>? PerCardChance = null);

public sealed partial class ExcitingItemsMod
{
    void CardRateDrops(DropRoll d)
    {
        var c = _choices.CardRate ?? new();
        if (c.MapsOnly && Game.MapRunOf(d.Player) is not { Atlas: true }) return;
        // How often a card drops: the server's own card chance, scaled (several mods' factors multiply).
        if (Math.Abs(c.DropChance - 1.0) > 1e-9) d.Weights.Cards *= Math.Clamp(c.DropChance, 0, 1000);
        if (c.PerCardChance is { Count: > 0 } chances)
        {
            var catalog = Game.ItemsFor(d.Player);
            foreach (var (name, factor) in chances)
                if (catalog.All.FirstOrDefault(t => t.Class == "DivinationCard" && t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } card)
                    d.Weights.Items[card.Id] = Math.Clamp(factor, 0, 1000) * d.Weights.Item(card.Id);
        }
        if ((c.PerCard is null || c.PerCard.Count == 0) && Math.Abs(c.Multiplier - 1.0) < 1e-9) return;
        d.Changes.Add(CardRateChange);
    }

    /// <summary>Chests and strongboxes too (the private build's hand-over).</summary>
    void CardRateChests(Player player, string kind, string id, List<ItemDrops.Drop> drops)
    {
        var c = _choices.CardRate ?? new();
        if ((c.PerCard is null || c.PerCard.Count == 0) && Math.Abs(c.Multiplier - 1.0) < 1e-9) return;
        if (c.MapsOnly && Game.MapRunOf(player) is not { Atlas: true }) return;
        var changed = drops.SelectMany(drop => CardRateChange(drop, Random.Shared)).ToList();
        drops.Clear();
        drops.AddRange(changed);
    }

    IEnumerable<ItemDrops.Drop> CardRateChange(ItemDrops.Drop drop, Random rng)
    {
        var c = _choices.CardRate ?? new();
        {
            if (drop.Type.Class != "DivinationCard") return [drop];
            double m = c.PerCard?.FirstOrDefault(p => p.Key.Equals(drop.Type.Name, StringComparison.OrdinalIgnoreCase)) is { Key: not null } own
                ? own.Value : c.Multiplier;
            m = Math.Clamp(m, 0, 1000);
            int n = (int)Math.Floor(m) + (rng.NextDouble() < m - Math.Floor(m) ? 1 : 0);
            n *= Math.Max(1, (int)drop.Item.StackSize);
            if (n <= 0) return [];
            var drops = new List<ItemDrops.Drop>();
            int stack = Math.Max(1, (int)drop.Type.MaxStack);
            for (int left = n; left > 0; left -= stack)
                drops.Add(drop with { Item = drop.Item with { StackSize = (ushort)Math.Min(left, stack) } });
            return drops;
        }
    }
}
