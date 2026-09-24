using Bagcheck.Core.Shopping;
using Xunit;

namespace Bagcheck.Core.Tests;

public class ShoppingListTests
{
    [Fact]
    public void AddingTheSameItemToTheSameGroupReturnsTheExistingEntry()
    {
        var list = new ShoppingList();
        var first = list.Add(5057, "Iron Ore", false, 10, null, out var added);
        var again = list.Add(5057, "Iron Ore", false, 99, null, out var addedAgain);
        var hq = list.Add(5057, "Iron Ore", true, 1, null, out _);

        Assert.True(added);
        Assert.False(addedAgain);
        Assert.Same(first, again);
        Assert.NotSame(first, hq);
        Assert.Equal(10, first.Needed);
    }

    [Fact]
    public void NeededIsKeptWithinRange()
    {
        var list = new ShoppingList();
        Assert.Equal(1, list.Add(1, "A", false, 0, null, out _).Needed);
        Assert.Equal(ShoppingList.MaxNeeded, list.Add(2, "B", false, 1_000_000, null, out _).Needed);
    }

    [Fact]
    public void NeedsAddUpAcrossGroupsButSkipPausedOnes()
    {
        var list = new ShoppingList();
        var tinctures = new ShoppingGroup { Name = "Tinctures" };
        var paused = new ShoppingGroup { Name = "Later", Active = false };
        list.Groups.AddRange([tinctures, paused]);
        list.Add(5057, "Iron Ore", false, 10, null, out _);
        list.Add(5057, "Iron Ore", false, 15, tinctures.Id, out _);
        list.Add(5057, "Iron Ore", false, 100, paused.Id, out _);

        var need = ShoppingNeeds.Of(list, _ => 7)[new ItemKey(5057, false)];

        Assert.Equal(25, need.Needed);
        Assert.Equal(18, need.Short);
        Assert.Equal(2, need.Entries);
        Assert.False(need.Done);
    }

    [Fact]
    public void HoldingEnoughMarksAnItemDone()
    {
        var list = new ShoppingList();
        list.Add(5057, "Iron Ore", false, 10, null, out _);

        var need = ShoppingNeeds.Of(list, _ => 12)[new ItemKey(5057, false)];

        Assert.True(need.Done);
        Assert.Equal(0, need.Short);
    }

    [Fact]
    public void RemovingAGroupRemovesItsItemsAndTidyFreesOrphans()
    {
        var list = new ShoppingList();
        var group = new ShoppingGroup { Name = "Gone" };
        list.Groups.Add(group);
        list.Add(1, "A", false, 1, group.Id, out _);
        list.Add(2, "B", false, 1, null, out _);

        Assert.Equal(1, list.RemoveGroup(group));
        Assert.Single(list.Items);

        list.Items.Add(new ShoppingItem { ItemId = 3, Name = "C", GroupId = Guid.NewGuid() });
        list.Tidy();
        Assert.All(list.Items, i => Assert.Null(i.GroupId));
    }
}
