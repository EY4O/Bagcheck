using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lumina.Excel.Sheets;
using Bagcheck.Core.Vendors;

namespace Bagcheck.Game;

/// <summary>
/// Reads the game's shop, NPC and placement sheets into a <see cref="VendorIndex"/>. That takes a moment, so it runs
/// once in the background the first time anything asks.
/// </summary>
public sealed class VendorCatalog
{
    // A Level row of this type places an event NPC in a zone.
    private const byte NpcPlacement = 8;

    private Task<VendorIndex>? building;

    public VendorIndex Index
    {
        get
        {
            building ??= Task.Run(Build);
            return building.IsCompletedSuccessfully ? building.Result : VendorIndex.Empty;
        }
    }

    public bool Ready => building is { IsCompletedSuccessfully: true };

    private static VendorIndex Build()
    {
        var data = Plugin.DataManager;

        var rows = new List<ShopRow>();
        foreach (var shop in data.GetSubrowExcelSheet<GilShopItem>())
            foreach (var row in shop)
                rows.Add(new(row.RowId, row.Item.RowId, row.QuestRequired.Any(q => q.RowId != 0)));
        var shops = rows.Select(r => r.ShopId).ToHashSet();

        // An NPC opens a shop directly, through a PreHandler, or from a TopicSelect menu whose entries can be either.
        var preHandlers = data.GetExcelSheet<PreHandler>();
        var topics = data.GetExcelSheet<TopicSelect>();
        IEnumerable<uint> Opens(uint id)
        {
            if (shops.Contains(id)) yield return id;
            if (preHandlers.GetRowOrDefault(id) is { } pre && shops.Contains(pre.Target.RowId)) yield return pre.Target.RowId;
            if (topics.GetRowOrDefault(id) is not { } topic) yield break;
            foreach (var entry in topic.Shop)
            {
                if (shops.Contains(entry.RowId)) yield return entry.RowId;
                if (preHandlers.GetRowOrDefault(entry.RowId) is { } inner && shops.Contains(inner.Target.RowId))
                    yield return inner.Target.RowId;
            }
        }

        var npcShops = new List<(uint, uint)>();
        foreach (var npc in data.GetExcelSheet<ENpcBase>())
            foreach (var handler in npc.ENpcData)
                if (handler.RowId != 0)
                    foreach (var shop in Opens(handler.RowId)) npcShops.Add((npc.RowId, shop));
        var vendors = npcShops.Select(p => p.Item1).ToHashSet();

        var withAetheryte = data.GetExcelSheet<Aetheryte>().Where(a => a.IsAetheryte).Select(a => a.Territory.RowId).ToHashSet();
        var territories = data.GetExcelSheet<TerritoryType>();
        var placements = new List<(uint, VendorPlace)>();
        foreach (var level in data.GetExcelSheet<Level>())
        {
            if (level.Type != NpcPlacement || !vendors.Contains(level.Object.RowId)) continue;
            var territory = level.Territory.RowId;
            var zone = territories.GetRowOrDefault(territory)?.PlaceName.ValueNullable?.Name.ExtractText() is { Length: > 0 } name
                ? name : $"#{territory}";
            placements.Add((level.Object.RowId, new VendorPlace(territory, zone, withAetheryte.Contains(territory))));
        }

        var names = data.GetExcelSheet<ENpcResident>();
        var items = data.GetExcelSheet<Item>();
        var index = VendorIndex.Build(rows, npcShops, placements,
            npc => names.GetRowOrDefault(npc)?.Singular.ExtractText() is { Length: > 0 } n ? n : $"NPC {npc}",
            item => items.GetRowOrDefault(item)?.PriceMid ?? 0);
        Plugin.Log.Information($"Vendor data: {index.ItemCount:N0} items sold by NPCs.");
        return index;
    }
}
