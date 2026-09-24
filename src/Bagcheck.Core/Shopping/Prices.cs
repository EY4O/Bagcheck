using Bagcheck.Core.Market;

namespace Bagcheck.Core.Shopping;

/// <summary>One market listing picked to fill an amount.</summary>
public sealed record Stack(int WorldId, int Quantity, int UnitPrice);

/// <summary>Whole stacks picked cheapest first until the amount is covered. Cost includes the estimated tax.</summary>
public sealed record Fill(IReadOnlyList<Stack> Stacks, long Units, ulong Cost)
{
    public static readonly Fill None = new([], 0, 0);

    public decimal UnitCost => Units == 0 ? 0 : (decimal)Cost / Units;

    /// <summary>How many units come from each world, most first.</summary>
    public IReadOnlyList<(int WorldId, long Units)> ByWorld() => Stacks
        .GroupBy(s => s.WorldId)
        .Select(g => (g.Key, g.Sum(s => (long)s.Quantity)))
        .OrderByDescending(w => w.Item2)
        .ToList();
}

/// <summary>An NPC selling the item for gil. NPCs sell NQ, at a fixed price with no tax.</summary>
public sealed record NpcOffer(string Npc, string Zone, uint Price);

/// <summary>Prices for one shopping list item, for the amount still needed when it was checked.</summary>
/// <param name="Market">The cheapest way to buy <see cref="Wanted"/> across the data centre.</param>
/// <param name="Here">The same on the world you were on, or null if nothing is listed there.</param>
/// <param name="Median">Median price of recent sales, before tax.</param>
public sealed record PriceCheck(
    ItemKey Key,
    long Wanted,
    Fill Market,
    Fill? Here,
    int? Median,
    NpcOffer? Npc,
    DateTimeOffset? UploadedAt)
{
    /// <summary>The NPC is the better buy: cheaper per unit than the market, or the market can't cover it.</summary>
    public bool NpcIsCheaper => Npc != null && (Market.Units < Wanted || Npc.Price < Market.UnitCost);

    public bool CanFinish => NpcIsCheaper || Market.Units >= Wanted;

    public ulong CostToFinish => NpcIsCheaper ? Npc!.Price * (ulong)Wanted : Market.Cost;
}

public sealed record ShoppingTotal(ulong Cost, int Priced, int CantFinish, int FromNpc);

public static class Prices
{
    /// <summary>Market tax is 5% in most places. Universalis prices don't include it, so totals here are estimates.</summary>
    public const decimal EstimatedTax = 0.05m;

    public static bool Counts(MarketListing listing, bool hqOnly) =>
        listing is { PricePerUnit: > 0, Quantity: > 0 } && (listing.Hq || !hqOnly);

    /// <param name="world">Only listings from this world, or null for all of them.</param>
    public static Fill Cheapest(IEnumerable<MarketListing> listings, long wanted, bool hqOnly, int? world = null)
    {
        if (wanted <= 0) return Fill.None;
        var stacks = new List<Stack>();
        long units = 0;
        decimal gil = 0;
        foreach (var listing in listings
                     .Where(l => Counts(l, hqOnly) && (world == null || l.WorldID == world))
                     .OrderBy(l => l.PricePerUnit)
                     .ThenBy(l => l.Quantity))
        {
            if (units >= wanted) break;
            stacks.Add(new(listing.WorldID ?? 0, listing.Quantity, listing.PricePerUnit));
            units += listing.Quantity;
            gil += (decimal)listing.Quantity * listing.PricePerUnit;
        }
        var cost = decimal.Round(gil * (1 + EstimatedTax));
        return new(stacks, units, cost >= ulong.MaxValue ? ulong.MaxValue : (ulong)cost);
    }

    public static int? Median(IEnumerable<MarketSale> sales, bool hqOnly, int sample = 20)
    {
        var prices = sales.Where(s => s is { PricePerUnit: > 0, Quantity: > 0 } && (s.Hq || !hqOnly))
            .OrderByDescending(s => s.Timestamp)
            .Take(sample)
            .Select(s => s.PricePerUnit)
            .Order()
            .ToArray();
        if (prices.Length == 0) return null;
        var middle = prices.Length / 2;
        return prices.Length % 2 == 1 ? prices[middle] : (int)Math.Round((prices[middle - 1] + (long)prices[middle]) / 2.0);
    }

    /// <param name="currentWorld">The world you're on, for the "here" price.</param>
    /// <param name="npc">An NPC selling the item; ignored for HQ-only items, as NPCs sell NQ.</param>
    public static PriceCheck Check(ItemKey key, long wanted, MarketSnapshot market, int currentWorld, NpcOffer? npc)
    {
        var listings = market.Data.Listings ?? [];
        var here = Cheapest(listings, wanted, key.HqOnly, currentWorld);
        return new(
            key,
            wanted,
            Cheapest(listings, wanted, key.HqOnly),
            here.Units > 0 ? here : null,
            Median(market.Data.RecentHistory ?? [], key.HqOnly),
            key.HqOnly ? null : npc,
            market.UploadedAt);
    }

    public static ShoppingTotal Total(IEnumerable<PriceCheck> checks)
    {
        ulong cost = 0;
        int priced = 0, cantFinish = 0, fromNpc = 0;
        foreach (var check in checks.Where(c => c.Wanted > 0))
        {
            priced++;
            if (!check.CanFinish) cantFinish++;
            if (check.NpcIsCheaper) fromNpc++;
            cost = cost > ulong.MaxValue - check.CostToFinish ? ulong.MaxValue : cost + check.CostToFinish;
        }
        return new(cost, priced, cantFinish, fromNpc);
    }
}
