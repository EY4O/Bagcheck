namespace Bagcheck.Core.Shopping;

/// <summary>An item and quality. <see cref="HqOnly"/> false means any quality: HQ counts too.</summary>
public readonly record struct ItemKey(uint ItemId, bool HqOnly);

public sealed class ShoppingItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public uint ItemId { get; set; }
    public string Name { get; set; } = "";
    public bool HqOnly { get; set; }
    public int Needed { get; set; } = 1;
    public Guid? GroupId { get; set; }

    // A method rather than a property, so it isn't written into the saved configuration.
    public ItemKey GetKey() => new(ItemId, HqOnly);
}

public sealed class ShoppingGroup
{
    public const string Manual = "Manual";
    public const string Teamcraft = "Teamcraft";

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";

    /// <summary>An inactive group stays on the list but isn't counted or price checked.</summary>
    public bool Active { get; set; } = true;

    public string Source { get; set; } = Manual;
    public DateTimeOffset? ImportedAt { get; set; }
}

public sealed class ShoppingList
{
    public const int MaxNeeded = 99_999;

    public List<ShoppingItem> Items { get; set; } = [];
    public List<ShoppingGroup> Groups { get; set; } = [];

    public ShoppingGroup? GroupOf(ShoppingItem item) =>
        item.GroupId is { } id ? Groups.FirstOrDefault(g => g.Id == id) : null;

    public bool IsActive(ShoppingItem item) => GroupOf(item) is not { Active: false };

    /// <summary>
    /// Adds an item to a group (null for ungrouped). If that group already has the same item and quality, the existing
    /// entry is returned instead and <paramref name="added"/> is false.
    /// </summary>
    public ShoppingItem Add(uint itemId, string name, bool hqOnly, int needed, Guid? groupId, out bool added)
    {
        var existing = Items.FirstOrDefault(i => i.ItemId == itemId && i.HqOnly == hqOnly && i.GroupId == groupId);
        added = existing == null;
        if (existing != null) return existing;
        var item = new ShoppingItem
        {
            ItemId = itemId,
            Name = name,
            HqOnly = hqOnly,
            Needed = Math.Clamp(needed, 1, MaxNeeded),
            GroupId = groupId,
        };
        Items.Add(item);
        return item;
    }

    /// <summary>Removes a group and everything in it.</summary>
    public int RemoveGroup(ShoppingGroup group)
    {
        var removed = Items.RemoveAll(i => i.GroupId == group.Id);
        Groups.Remove(group);
        return removed;
    }

    /// <summary>Entries whose group no longer exists become ungrouped.</summary>
    public void Tidy()
    {
        var ids = Groups.Select(g => g.Id).ToHashSet();
        foreach (var item in Items.Where(i => i.GroupId is { } id && !ids.Contains(id))) item.GroupId = null;
    }
}

/// <summary>What the list needs of one item and quality, added up across every active entry, against what you hold.</summary>
public sealed record Need(ItemKey Key, string Name, long Needed, long Held, int Entries)
{
    public long Short => Math.Max(0, Needed - Held);
    public bool Done => Short == 0;
}

public static class ShoppingNeeds
{
    public static IReadOnlyDictionary<ItemKey, Need> Of(ShoppingList list, Func<ItemKey, long> held) =>
        list.Items.Where(list.IsActive)
            .GroupBy(i => i.GetKey())
            .ToDictionary(g => g.Key, g => new Need(
                g.Key,
                g.First().Name,
                g.Sum(i => (long)i.Needed),
                held(g.Key),
                g.Count()));
}
