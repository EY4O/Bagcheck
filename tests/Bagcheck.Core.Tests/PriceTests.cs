using Bagcheck.Core.Market;
using Bagcheck.Core.Shopping;
using Xunit;

namespace Bagcheck.Core.Tests;

public class PriceTests
{
    private const int Here = 73, Jenova = 40, Siren = 57;

    private static MarketListing L(int price, int quantity, int world, bool hq = false) => new(price, quantity, hq, world);

    private static MarketSnapshot Market(MarketListing[] listings, MarketSale[]? sales = null) =>
        new("Aether", DateTimeOffset.UnixEpoch, new MarketData { ItemID = 1, LastUploadTime = 1, Listings = listings, RecentHistory = sales });

    [Fact]
    public void FillTakesWholeStacksCheapestFirstAndAddsTax()
    {
        var fill = Prices.Cheapest([L(100, 10, Here), L(80, 5, Jenova), L(90, 10, Siren)], 12, false);

        Assert.Equal([80, 90], fill.Stacks.Select(s => s.UnitPrice));
        Assert.Equal(15, fill.Units);                     // whole stacks: 15 for 12 wanted
        Assert.Equal(1_365ul, fill.Cost);                 // (400 + 900) * 1.05
        Assert.Equal([(Siren, 10L), (Jenova, 5L)], fill.ByWorld());
    }

    [Fact]
    public void FillRespectsQualityAndWorld()
    {
        MarketListing[] listings = [L(50, 5, Jenova, hq: true), L(100, 5, Here)];

        Assert.Equal(50, Prices.Cheapest(listings, 5, false).Stacks[0].UnitPrice);   // HQ counts for any quality
        Assert.Equal(50, Prices.Cheapest(listings, 5, true).Stacks[0].UnitPrice);
        Assert.Equal(5, Prices.Cheapest(listings, 10, true).Units);                  // only the HQ stack
        Assert.Equal(100, Prices.Cheapest(listings, 5, false, Here).Stacks[0].UnitPrice);
        Assert.Same(Fill.None, Prices.Cheapest(listings, 0, false));
    }

    [Fact]
    public void TrollListingsDoNotOverflow()
    {
        var fill = Prices.Cheapest([L(999_999_999, 9_999, Jenova), L(999_999_999, 9_999, Siren)], 20_000, false);
        Assert.True(fill.Cost > 20_000_000_000_000ul);
    }

    [Fact]
    public void MedianUsesTheLatestSalesOfTheRightQuality()
    {
        MarketSale[] sales = [new(10, 1, false, 1), new(30, 1, false, 2), new(20, 1, false, 3), new(1_000, 1, true, 4)];

        Assert.Equal(25, Prices.Median(sales, false));     // 10, 20, 30, 1000 -> (20 + 30) / 2
        Assert.Equal(1_000, Prices.Median(sales, true));
        Assert.Equal(510, Prices.Median(sales, false, sample: 2)); // the two newest, 20 and the HQ 1,000
        Assert.Null(Prices.Median([], false));
    }

    [Fact]
    public void CheckReportsHereTargetAndMedian()
    {
        var market = Market([L(120, 10, Here), L(80, 5, Jenova), L(90, 10, Siren)], [new(95, 1, false, 1)]);
        var check = Prices.Check(new ItemKey(1, false), 12, 90, market, Here, null);

        Assert.Equal(15, check.Market.Units);
        Assert.Equal(10, check.Here!.Units);
        Assert.Equal(95, check.Median);
        Assert.Equal(15, check.AtTarget);
        Assert.Equal(Jenova, check.AtTargetWorld);
        Assert.Equal(80, check.CheapestListing);
        Assert.True(check.CanFinish);
    }

    [Fact]
    public void AnNpcWinsWhenCheaperOrWhenTheMarketRunsShort()
    {
        var npc = new NpcOffer("Vral", "The Crystarium", 100);
        var cheapMarket = Prices.Check(new ItemKey(1, false), 10, 0, Market([L(50, 10, Jenova)]), Here, npc);
        var dearMarket = Prices.Check(new ItemKey(1, false), 10, 0, Market([L(150, 10, Jenova)]), Here, npc);
        var shortMarket = Prices.Check(new ItemKey(1, false), 10, 0, Market([L(50, 2, Jenova)]), Here, npc);
        var hqOnly = Prices.Check(new ItemKey(1, true), 10, 0, Market([L(150, 10, Jenova, hq: true)]), Here, npc);

        Assert.False(cheapMarket.NpcIsCheaper);
        Assert.True(dearMarket.NpcIsCheaper);
        Assert.Equal(1_000ul, dearMarket.CostToFinish);
        Assert.True(shortMarket.NpcIsCheaper);
        Assert.Null(hqOnly.Npc);                           // NPCs only sell NQ
    }

    [Fact]
    public void TotalAddsTheCheaperWayForEachItem()
    {
        var npc = new NpcOffer("Vral", "The Crystarium", 100);
        PriceCheck[] checks =
        [
            Prices.Check(new ItemKey(1, false), 10, 0, Market([L(150, 10, Jenova)]), Here, npc),  // NPC: 1,000
            Prices.Check(new ItemKey(2, false), 10, 0, Market([L(10, 10, Jenova)]), Here, null),  // market: 105
            Prices.Check(new ItemKey(3, false), 10, 0, Market([L(10, 4, Jenova)]), Here, null),   // short: 42
        ];

        var total = Prices.Total(checks);

        Assert.Equal(1_147ul, total.Cost);
        Assert.Equal(3, total.Priced);
        Assert.Equal(1, total.CantFinish);
        Assert.Equal(1, total.FromNpc);
    }
}
