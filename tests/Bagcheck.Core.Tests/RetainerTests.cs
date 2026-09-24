using Bagcheck.Core.Market;
using Bagcheck.Core.Retainers;
using Xunit;

namespace Bagcheck.Core.Tests;

public class RetainerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
    private readonly string path = Path.Combine(Path.GetTempPath(), $"bagcheck-retainers-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private RetainerStore Store() => new(path, () => Now);

    private static RetainerListing Listing(uint item, string name, ulong price, bool hq = false, int quantity = 1) =>
        new(item, name, hq, quantity, price, 0);

    [Fact]
    public void RosterAddsUpdatesAndDropsDismissedRetainers()
    {
        var store = Store();
        store.UpdateRoster(1, [(10, "Alpha", true, 500, 3), (11, "Beta", false, 0, 0)]);
        store.UpdateRoster(1, [(10, "Alpha", true, 750, 4)]);

        var retainer = Assert.Single(store.For(1));
        Assert.Equal(750u, retainer.Gil);
        Assert.Equal(4, retainer.MarketCount);
        Assert.True(store.Dirty);
    }

    [Fact]
    public void RetainersAreKeptPerCharacter()
    {
        var store = Store();
        store.UpdateRoster(1, [(10, "Alpha", true, 0, 0)]);
        store.UpdateRoster(2, [(20, "Gamma", true, 0, 0)]);

        Assert.Equal("Alpha", Assert.Single(store.For(1)).Name);
        Assert.Equal("Gamma", Assert.Single(store.For(2)).Name);
    }

    [Fact]
    public void ListingsSurviveASaveAndReload()
    {
        var store = Store();
        store.UpdateRoster(1, [(10, "Alpha", true, 0, 1)]);
        store.UpdateListings(1, 10, "Alpha", [Listing(5057, "Iron Ore", 12, quantity: 99)]);
        store.Save();

        var loaded = Assert.Single(new RetainerStore(path, () => Now).For(1));
        var listing = Assert.Single(loaded.Listings!);
        Assert.Equal("Iron Ore", listing.Name);
        Assert.Equal(99, listing.Quantity);
        Assert.Equal(Now, loaded.ListingsSeenAt);
    }

    [Fact]
    public void AnUnreadableFileIsLeftAlone()
    {
        File.WriteAllText(path, "{ not json");
        var store = Store();
        store.UpdateRoster(1, [(10, "Alpha", true, 0, 0)]);
        store.Save();

        Assert.NotNull(store.Error);
        Assert.Equal("{ not json", File.ReadAllText(path));
    }

    [Fact]
    public void TotalsCountActiveRetainersOnly()
    {
        RetainerRecord[] records =
        [
            new(1, 10, "Alpha", 0, true, 1_000, 2, Now, [Listing(1, "A", 5), Listing(2, "B", 5), Listing(3, "C", 5)]),
            new(1, 11, "Beta", 1, false, 50, 4, Now),
        ];
        var totals = RetainerView.Totals(records);

        Assert.Equal(1, totals.Active);
        Assert.Equal(2, totals.Total);
        Assert.Equal(3, totals.Listed);   // seen listings win over the game's count
        Assert.Equal(20, totals.Slots);
        Assert.Equal(1_050ul, totals.Gil);
    }

    [Fact]
    public void FilterMatchesRetainerNamesAndListedItems()
    {
        RetainerRecord[] records =
        [
            new(1, 10, "Alpha", 0, true, 0, 1, Now, [Listing(5057, "Iron Ore", 12)]),
            new(1, 11, "Beta", 1, true, 0, 0, Now, []),
        ];

        Assert.Equal(2, RetainerView.Filter(records, "", true).Count);
        var byItem = Assert.Single(RetainerView.Filter(records, "iron", true));
        Assert.True(byItem.ListingMatched);
        var byName = Assert.Single(RetainerView.Filter(records, "bet", true));
        Assert.False(byName.ListingMatched);
    }

    private static MarketSnapshot Market(params MarketListing[] listings) =>
        new("Gilgamesh", Now, new MarketData { ItemID = 5057, LastUploadTime = 1, Listings = listings });

    [Fact]
    public void CompareIgnoresYourOwnRetainersAndTheOtherQuality()
    {
        var own = new HashSet<string>(["Alpha"], StringComparer.OrdinalIgnoreCase);
        var listing = Listing(5057, "Iron Ore", 12);
        var market = Market(new(8, 5, false, RetainerName: "alpha"), new(9, 5, true, RetainerName: "Zed"), new(10, 5, false, RetainerName: "Zed"));

        var comparison = RetainerView.Compare(listing, market, own);

        Assert.Equal(Standing.Undercut, comparison.Standing);
        Assert.Equal(2ul, comparison.By);
        Assert.Equal(10, comparison.Cheapest);
    }

    [Fact]
    public void CompareSaysLowestOrNotChecked()
    {
        var listing = Listing(5057, "Iron Ore", 12);
        Assert.Equal(Standing.Lowest, RetainerView.Compare(listing, Market(new MarketListing(12, 5, false)), new HashSet<string>()).Standing);
        Assert.Equal(Standing.Lowest, RetainerView.Compare(listing, Market(), new HashSet<string>()).Standing);
        Assert.Equal(Standing.Unknown, RetainerView.Compare(listing, null, new HashSet<string>()).Standing);
    }
}
