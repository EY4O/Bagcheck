using Bagcheck.Core.Shopping;
using Bagcheck.Core.Stock;
using Xunit;

namespace Bagcheck.Core.Tests;

public class StockTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly StockOptions CountEverything = new(true, true, 72);
    private readonly string path = Path.Combine(Path.GetTempPath(), $"bagcheck-stock-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private static StockSnapshot Saddlebag(params StockItem[] items) => new(1, StockSnapshot.Saddlebag, 0, "Saddlebag", Now, items);

    private static StockSnapshot Retainer(string name, TimeSpan age, params StockItem[] items) =>
        new(1, StockSnapshot.Retainer, (ulong)name.Length, name, Now - age, items);

    [Fact]
    public void AnyQualityCountsHqButHqOnlyDoesNot()
    {
        StockItem[] bags = [new(5057, false, 10), new(5057, true, 3)];

        Assert.Equal(13, StockCount.Of(new(5057, false), bags, [], [], CountEverything, Now).Total);
        Assert.Equal(3, StockCount.Of(new(5057, true), bags, [], [], CountEverything, Now).Total);
    }

    [Fact]
    public void BagsCrystalsSaddlebagAndRetainersAddUp()
    {
        var key = new ItemKey(2, false);
        var holding = StockCount.Of(key, [new(2, false, 100)], [new(2, false, 50)],
            [Saddlebag(new StockItem(2, false, 20)), Retainer("Alpha", TimeSpan.FromHours(1), new StockItem(2, false, 5))],
            CountEverything, Now);

        Assert.Equal(175, holding.Total);
        Assert.Equal(["Bags", "Crystals", "Saddlebag", "Alpha"], holding.Sources.Select(s => s.Label));
    }

    [Fact]
    public void OldRetainerContentsAndSwitchedOffSourcesAreShownButNotCounted()
    {
        var key = new ItemKey(2, false);
        StockSnapshot[] stored = [Saddlebag(new StockItem(2, false, 20)), Retainer("Alpha", TimeSpan.FromHours(100), new StockItem(2, false, 5))];

        var stale = StockCount.Of(key, [], [], stored, CountEverything, Now);
        Assert.Equal(20, stale.Total);
        Assert.NotNull(stale.Sources.Single(s => s.Label == "Alpha").NotCounted);

        var off = StockCount.Of(key, [], [], stored, new(false, false, 1000), Now);
        Assert.Equal(0, off.Total);
        Assert.Equal(3, off.Sources.Count);
    }

    [Fact]
    public void StoreKeepsTheLatestReadingAndSurvivesAReload()
    {
        var store = new StockStore(path, () => Now);
        store.Record(Saddlebag(new StockItem(2, false, 20)));
        store.Record(Saddlebag(new StockItem(2, false, 25)));
        store.Save();

        var snapshot = Assert.Single(new StockStore(path, () => Now).For(1));
        Assert.Equal(25, Assert.Single(snapshot.Items).Quantity);
    }

    [Fact]
    public void DismissedRetainersAreForgotten()
    {
        var store = new StockStore(path, () => Now);
        store.Record(Retainer("Alpha", TimeSpan.Zero, new StockItem(2, false, 5)));
        store.Record(Retainer("Beta", TimeSpan.Zero, new StockItem(2, false, 5)));
        store.Record(Saddlebag(new StockItem(2, false, 1)));

        store.Forget(1, new HashSet<ulong> { (ulong)"Beta".Length });

        Assert.Equal(["Beta", "Saddlebag"], store.For(1).Select(s => s.Name).Order());
    }
}
