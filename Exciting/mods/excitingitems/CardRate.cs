/// <summary>Divination card drop rate: every card that drops (area-bound ones too, such as The Doctor in Burial Chambers)
/// comes Multiplier times as often. 2.5: two of it and half the time a third; 0.5: half of them drop. PerCard sets one
/// card's own multiplier (by its name), over Multiplier.</summary>
sealed record CardRateChoices(double Multiplier = 1.0, Dictionary<string, double>? PerCard = null, bool MapsOnly = false);

public sealed partial class ExcitingItemsMod
{
    void CardRateDrops(DropRoll d)
    {
        var c = _choices.CardRate ?? new();
        if ((c.PerCard is null || c.PerCard.Count == 0) && Math.Abs(c.Multiplier - 1.0) < 1e-9) return;
        if (c.MapsOnly && Game.MapRunOf(d.Player) is not { Atlas: true }) return;
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
