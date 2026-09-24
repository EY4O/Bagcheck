using Bagcheck.Core.Shopping;
using Xunit;

namespace Bagcheck.Core.Tests;

public class TeamcraftTests
{
    private const string Paste = """
        Gathering :
        3x Iron Ore
        12x Cotton Boll

        Crystals
        40x Fire Shard
        2x Iron Ore
        """;

    [Fact]
    public void ParsesPanelsAndAddsRepeatedItemsTogether()
    {
        var text = TeamcraftText.Parse(Paste);

        Assert.Empty(text.Rejected);
        Assert.Equal(3, text.Lines.Count);
        Assert.Equal(new TeamcraftLine(5, "Iron Ore", "Gathering"), text.Lines[0]);
        Assert.Equal("Crystals", text.Lines[2].Section);
    }

    [Fact]
    public void LinesThatArentItemsAreListed()
    {
        // A plain line followed by an item is a panel heading; one at the end is not.
        var text = TeamcraftText.Parse("Gathering :\r\n0x Nothing\r\nMining\r\n3x Iron Ore\r\nsome note");

        Assert.Equal("Mining", Assert.Single(text.Lines).Section);
        Assert.Equal(2, text.Rejected.Count);
    }

    private static ItemMatch? Find(string name) => name switch
    {
        "Iron Ore" => new(5111, "Iron Ore", true, false),
        "Iron Ingot" => new(5057, "Iron Ingot", true, true),
        "Allagan Tomestone" => new(28, "Allagan Tomestone", false, false),
        _ => null,
    };

    [Fact]
    public void RowsAreSortedIntoRawCraftableUnmarketableAndUnknown()
    {
        var rows = TeamcraftImport.Resolve(TeamcraftText.Parse("3x Iron Ore\n2x Iron Ingot\n1x Allagan Tomestone\n4x Mystery"), Find);

        Assert.Equal([ImportStatus.Ready, ImportStatus.Craftable, ImportStatus.NotMarketable, ImportStatus.Unknown],
            rows.Select(r => r.Status));
        Assert.True(rows[0].TickedByDefault);
        Assert.False(rows[1].TickedByDefault);
        Assert.True(rows[1].Importable);
        Assert.False(rows[2].Importable);
    }

    [Fact]
    public void ANewImportMakesATeamcraftGroup()
    {
        var list = new ShoppingList();
        var rows = TeamcraftImport.Resolve(TeamcraftText.Parse("3x Iron Ore"), Find);
        var changes = TeamcraftImport.Plan(list, null, rows);
        var outcome = TeamcraftImport.Apply(list, null, " Ingots ", changes, false, DateTimeOffset.UnixEpoch);

        Assert.Equal("Ingots", outcome.Group.Name);
        Assert.Equal(ShoppingGroup.Teamcraft, outcome.Group.Source);
        var item = Assert.Single(list.Items);
        Assert.Equal(3, item.Needed);
        Assert.Equal(outcome.Group.Id, item.GroupId);
    }

    [Fact]
    public void UpdatingAGroupKeepsTargetsAndOnlyRemovesWhenAsked()
    {
        var list = new ShoppingList();
        var group = new ShoppingGroup { Name = "Ingots", Source = ShoppingGroup.Teamcraft };
        list.Groups.Add(group);
        list.Add(5111, "Iron Ore", false, 3, group.Id, out _).TargetPrice = 25;
        list.Add(5057, "Iron Ingot", false, 2, group.Id, out _);

        var rows = TeamcraftImport.Resolve(TeamcraftText.Parse("6x Iron Ore"), Find);
        var changes = TeamcraftImport.Plan(list, group.Id, rows);
        Assert.Equal([ChangeKind.Update, ChangeKind.Remove], changes.Select(c => c.Kind));

        var kept = TeamcraftImport.Apply(list, group.Id, "", changes, false, DateTimeOffset.UnixEpoch);
        Assert.Equal((1, 0), (kept.Updated, kept.Removed));
        Assert.Equal(2, list.Items.Count);

        var ore = list.Items.Single(i => i.ItemId == 5111);
        Assert.Equal(6, ore.Needed);
        Assert.Equal(25u, ore.TargetPrice);

        var removed = TeamcraftImport.Apply(list, group.Id, "", TeamcraftImport.Plan(list, group.Id, rows), true, DateTimeOffset.UnixEpoch);
        Assert.Equal(1, removed.Removed);
        Assert.Single(list.Items);
    }
}
