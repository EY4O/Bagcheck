namespace Bagcheck.Core.Vendors;

/// <param name="HasAetheryte">The zone has an aetheryte, so you can teleport there.</param>
public sealed record VendorPlace(uint TerritoryId, string Zone, bool HasAetheryte);

public sealed record VendorOffer(uint ItemId, string Npc, VendorPlace Place, uint Price);

/// <summary>A gil shop row. Rows that need a quest are left out of the index.</summary>
public sealed record ShopRow(uint ShopId, uint ItemId, bool QuestGated);

/// <summary>Which NPCs sell each item for gil, and where they stand, built from the game's shop and NPC sheets.</summary>
public sealed class VendorIndex
{
    private readonly Dictionary<uint, List<uint>> sellers;
    private readonly Dictionary<uint, VendorPlace[]> places;
    private readonly Dictionary<uint, string> names;
    private readonly Dictionary<uint, uint> prices;

    private VendorIndex(Dictionary<uint, List<uint>> sellers, Dictionary<uint, VendorPlace[]> places,
        Dictionary<uint, string> names, Dictionary<uint, uint> prices)
    {
        this.sellers = sellers;
        this.places = places;
        this.names = names;
        this.prices = prices;
    }

    public static readonly VendorIndex Empty = new([], [], [], []);

    public int ItemCount => sellers.Count;

    /// <summary>The item's vendor price, if any NPC sells it for gil.</summary>
    public uint? Price(uint itemId) => sellers.ContainsKey(itemId) ? prices.GetValueOrDefault(itemId) : null;

    /// <summary>NPCs selling the item and where: zones you can teleport to first, then by zone and name.</summary>
    public IReadOnlyList<VendorOffer> For(uint itemId)
    {
        if (!sellers.TryGetValue(itemId, out var npcs)) return [];
        var price = prices.GetValueOrDefault(itemId);
        return npcs
            .SelectMany(npc => (places.GetValueOrDefault(npc) ?? []).Select(place => new VendorOffer(itemId, names[npc], place, price)))
            .OrderBy(o => o.Place.HasAetheryte ? 0 : 1)
            .ThenBy(o => o.Place.Zone, StringComparer.Ordinal)
            .ThenBy(o => o.Npc, StringComparer.Ordinal)
            .ToList();
    }

    /// <param name="npcShops">Every (NPC, shop) pair, including shops reached through an NPC's menus.</param>
    /// <param name="placements">Where each NPC stands; an NPC may stand in several zones.</param>
    public static VendorIndex Build(IEnumerable<ShopRow> rows, IEnumerable<(uint Npc, uint Shop)> npcShops,
        IEnumerable<(uint Npc, VendorPlace Place)> placements, Func<uint, string> npcName, Func<uint, uint> price)
    {
        var shopItems = new Dictionary<uint, HashSet<uint>>();
        foreach (var row in rows.Where(r => r.ItemId != 0 && !r.QuestGated))
        {
            if (!shopItems.TryGetValue(row.ShopId, out var items)) shopItems[row.ShopId] = items = [];
            items.Add(row.ItemId);
        }

        var sellers = new Dictionary<uint, List<uint>>();
        var names = new Dictionary<uint, string>();
        foreach (var (npc, shop) in npcShops.Where(p => shopItems.ContainsKey(p.Shop)).Distinct())
        {
            if (!names.ContainsKey(npc)) names[npc] = npcName(npc);
            foreach (var item in shopItems[shop])
            {
                if (!sellers.TryGetValue(item, out var list)) sellers[item] = list = [];
                if (!list.Contains(npc)) list.Add(npc);
            }
        }

        var places = placements.Where(p => names.ContainsKey(p.Npc)).GroupBy(p => p.Npc)
            .ToDictionary(g => g.Key, g => g.Select(p => p.Place).DistinctBy(p => p.TerritoryId).ToArray());
        return new(sellers, places, names, sellers.Keys.ToDictionary(item => item, price));
    }
}
