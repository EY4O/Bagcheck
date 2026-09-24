using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace Bagcheck.Game;

public sealed record ItemInfo(uint Id, string Name, uint StackSize, bool CanBeHq, bool IsMarketable, uint Icon);

/// <summary>Lookups against the game's Item sheet.</summary>
public sealed class ItemCatalog(IDataManager data)
{
    private List<ItemInfo>? searchable;
    private Dictionary<string, ItemInfo>? byName;

    public ItemInfo? Get(uint itemId) => data.GetExcelSheet<Item>().GetRowOrDefault(itemId) is { } item ? ToInfo(item) : null;

    /// <summary>Items whose name contains the query, or the item with that id if the query is a number.</summary>
    public IReadOnlyList<ItemInfo> Search(string query, int max = 25)
    {
        query = query.Trim();
        if (query.Length == 0) return [];
        if (uint.TryParse(query, out var id)) return Get(id) is { } found ? [found] : [];
        if (query.Length < 2) return [];

        searchable ??= data.GetExcelSheet<Item>()
            .Select(ToInfo)
            .Where(i => i.IsMarketable && i.Name.Length > 0)
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return searchable.Where(i => i.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(max).ToList();
    }

    /// <summary>
    /// The item with exactly this name, ignoring case. All items are indexed, not only marketable ones, so an
    /// import can say an item can't be bought instead of calling it unknown. A marketable item wins a name clash.
    /// </summary>
    public ItemInfo? FindExact(string name)
    {
        if (byName == null)
        {
            byName = new Dictionary<string, ItemInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var info in data.GetExcelSheet<Item>().Select(ToInfo).Where(i => i.Name.Length > 0))
                if (!byName.TryGetValue(info.Name, out var seen) || (!seen.IsMarketable && info.IsMarketable))
                    byName[info.Name] = info;
        }
        return byName.GetValueOrDefault(name.Trim());
    }

    /// <summary>Whether any crafting class has a recipe for the item.</summary>
    public bool IsCraftable(uint itemId) => data.GetExcelSheet<RecipeLookup>().GetRowOrDefault(itemId) is { } r &&
        new[] { r.CRP.RowId, r.BSM.RowId, r.ARM.RowId, r.GSM.RowId, r.LTW.RowId, r.WVR.RowId, r.ALC.RowId, r.CUL.RowId }
            .Any(id => id != 0);

    // Items with a market search category can be listed on the market board.
    private static ItemInfo ToInfo(Item item) => new(
        item.RowId,
        item.Name.ExtractText(),
        item.StackSize,
        item.CanBeHq,
        !item.IsUntradable && item.ItemSearchCategory.RowId != 0,
        item.Icon);
}
