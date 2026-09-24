using Bagcheck.Core.Vendors;
using Xunit;

namespace Bagcheck.Core.Tests;

public class VendorTests
{
    private static readonly VendorPlace Limsa = new(129, "Limsa Lominsa Lower Decks", true);
    private static readonly VendorPlace Island = new(900, "Somewhere Remote", false);
    private static readonly VendorPlace Gridania = new(132, "New Gridania", true);

    private static VendorIndex Index() => VendorIndex.Build(
        [new(1, 5057, false), new(2, 5057, false), new(3, 6000, true)],
        [(10, 1), (11, 2), (12, 3), (10, 1)],
        [(10, Limsa), (11, Island), (11, Gridania)],
        npc => $"NPC {npc}",
        item => item == 5057 ? 18u : 0u);

    [Fact]
    public void OffersListReachableZonesFirst()
    {
        var offers = Index().For(5057);

        Assert.Equal(["Limsa Lominsa Lower Decks", "New Gridania", "Somewhere Remote"], offers.Select(o => o.Place.Zone));
        Assert.All(offers, o => Assert.Equal(18u, o.Price));
        Assert.Equal("NPC 10", offers[0].Npc);
    }

    [Fact]
    public void QuestShopsAndUnknownItemsHaveNoOffers()
    {
        var index = Index();
        Assert.Empty(index.For(6000));
        Assert.Null(index.Price(6000));
        Assert.Empty(index.For(1));
        Assert.Equal(18u, index.Price(5057));
        Assert.Equal(1, index.ItemCount);
    }
}
