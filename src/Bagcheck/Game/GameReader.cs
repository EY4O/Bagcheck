using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dalamud.Game.Inventory;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Bagcheck.Core.Retainers;
using Bagcheck.Core.Shopping;
using Bagcheck.Core.Stock;

namespace Bagcheck.Game;

/// <summary>
/// Reads what the game already has loaded, once a second: your bags and crystals, the saddlebag while it's open, the
/// retainer list, and the inventory and listings of a retainer you have open. It never opens a window or asks the
/// game for anything.
/// </summary>
public sealed class GameReader
{
    private static readonly TimeSpan ReadEvery = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan SaveEvery = TimeSpan.FromSeconds(10);

    private static readonly GameInventoryType[] BagTypes =
        [GameInventoryType.Inventory1, GameInventoryType.Inventory2, GameInventoryType.Inventory3, GameInventoryType.Inventory4];

    private static readonly (InventoryType Native, GameInventoryType Dalamud)[] RetainerPages =
    [
        (InventoryType.RetainerPage1, GameInventoryType.RetainerPage1), (InventoryType.RetainerPage2, GameInventoryType.RetainerPage2),
        (InventoryType.RetainerPage3, GameInventoryType.RetainerPage3), (InventoryType.RetainerPage4, GameInventoryType.RetainerPage4),
        (InventoryType.RetainerPage5, GameInventoryType.RetainerPage5), (InventoryType.RetainerPage6, GameInventoryType.RetainerPage6),
        (InventoryType.RetainerPage7, GameInventoryType.RetainerPage7),
    ];

    private readonly Plugin plugin;
    private ulong activeRetainer;
    private (ulong Retainer, string Signature)? pendingRetainerStock;
    private DateTimeOffset nextRead;
    private DateTimeOffset nextSave;
    private bool stopped;

    public GameReader(Plugin plugin)
    {
        this.plugin = plugin;
        var folder = Plugin.PluginInterface.GetPluginConfigDirectory();
        Retainers = new RetainerStore(Path.Combine(folder, "retainers.json"));
        Stock = new StockStore(Path.Combine(folder, "stock.json"));
    }

    public RetainerStore Retainers { get; }
    public StockStore Stock { get; }
    public ulong CharacterId { get; private set; }
    public StockItem[] Bags { get; private set; } = [];
    public StockItem[] Crystals { get; private set; } = [];

    /// <summary>The saddlebag is open right now, so its count is live rather than remembered.</summary>
    public bool SaddlebagOpen { get; private set; }

    /// <summary>Set if reading stopped after an unexpected error.</summary>
    public string? Problem { get; private set; }

    public Holding Held(ItemKey key) =>
        StockCount.Of(key, Bags, Crystals, Stock.For(CharacterId), plugin.Configuration.GetStockOptions(), DateTimeOffset.UtcNow);

    public void Update()
    {
        var character = plugin.CharacterId;
        if (character != CharacterId)
        {
            CharacterId = character;
            activeRetainer = 0;
            pendingRetainerStock = null;
            Bags = [];
            Crystals = [];
            SaddlebagOpen = false;
        }

        var now = DateTimeOffset.UtcNow;
        if (character == 0 || stopped || now < nextRead) return;
        nextRead = now + ReadEvery;

        try
        {
            Read();
        }
        catch (Exception ex)
        {
            stopped = true;
            Problem = "Bagcheck stopped reading game data after an error. Reload the plugin to try again. " + ex.Message;
            Plugin.Log.Error(ex, "Reading game data failed");
        }

        if (now >= nextSave) Save();
    }

    public void Save()
    {
        if (Retainers.Dirty) Retainers.Save();
        if (Stock.Dirty) Stock.Save();
        nextSave = DateTimeOffset.UtcNow + SaveEvery;
    }

    private unsafe void Read()
    {
        Bags = Totals(BagTypes.SelectMany(Items));
        Crystals = Totals(Items(GameInventoryType.Crystals));
        ReadSaddlebag();

        var manager = RetainerManager.Instance();
        if (manager == null || !manager->IsReady) { ClearRetainer(); return; }
        ReadRoster(manager);

        var agent = AgentRetainer.Instance();
        if (agent == null || !agent->IsAgentActive()) { ClearRetainer(); return; }
        var retainer = manager->GetActiveRetainer();
        if (retainer == null || retainer->RetainerId == 0) { ClearRetainer(); return; }

        // Give a retainer switch a second to settle before trusting what's loaded.
        if (activeRetainer != retainer->RetainerId)
        {
            activeRetainer = retainer->RetainerId;
            pendingRetainerStock = null;
            return;
        }
        ReadRetainerStock(retainer);
        ReadListings(retainer->RetainerId, retainer->NameString);
    }

    private void ClearRetainer()
    {
        activeRetainer = 0;
        pendingRetainerStock = null;
    }

    private unsafe void ReadRoster(RetainerManager* manager)
    {
        var roster = new List<(ulong, string, bool, uint, int)>();
        for (uint i = 0; i < manager->GetRetainerCount(); i++)
        {
            var retainer = manager->GetRetainerBySortedIndex(i);
            // A half-loaded list is skipped until it's complete.
            if (retainer == null || retainer->RetainerId == 0 || string.IsNullOrWhiteSpace(retainer->NameString)) return;
            roster.Add((retainer->RetainerId, retainer->NameString, retainer->Available, retainer->Gil, retainer->MarketItemCount));
        }
        if (roster.Count == 0) return;
        Retainers.UpdateRoster(CharacterId, roster);
        Stock.Forget(CharacterId, roster.Select(r => r.Item1).ToHashSet());
    }

    /// <summary>The saddlebag is only loaded while it's open, and it can only change then.</summary>
    private void ReadSaddlebag()
    {
        SaddlebagOpen = Loaded(InventoryType.SaddleBag1) && Loaded(InventoryType.SaddleBag2);
        if (!SaddlebagOpen) return;
        var items = Items(GameInventoryType.SaddleBag1).Concat(Items(GameInventoryType.SaddleBag2));
        if (Loaded(InventoryType.PremiumSaddleBag1) && Loaded(InventoryType.PremiumSaddleBag2))
            items = items.Concat(Items(GameInventoryType.PremiumSaddleBag1)).Concat(Items(GameInventoryType.PremiumSaddleBag2));
        Stock.Record(new(CharacterId, StockSnapshot.Saddlebag, 0, "Saddlebag", DateTimeOffset.UtcNow, Totals(items)));
    }

    /// <summary>
    /// Reads an open retainer's inventory and crystals. Pages left over from the previous retainer must never be
    /// filed under this one, so a reading only counts when every page is loaded, the number of filled slots matches
    /// the game's own item count for this retainer, and two readings a second apart agree.
    /// </summary>
    private unsafe void ReadRetainerStock(RetainerManager.Retainer* retainer)
    {
        var inventory = InventoryManager.Instance();
        if (inventory == null) return;
        foreach (var operation in inventory->PendingOperations)
            if (!operation.IsEmpty) { pendingRetainerStock = null; return; }
        if (RetainerPages.Any(p => !Loaded(p.Native)) || !Loaded(InventoryType.RetainerCrystals))
        {
            pendingRetainerStock = null;
            return;
        }

        var slots = RetainerPages.SelectMany(p => Items(p.Dalamud)).ToList();
        if (slots.Count != retainer->ItemCount) { pendingRetainerStock = null; return; }

        var items = Totals(slots.Concat(Items(GameInventoryType.RetainerCrystals)));
        var signature = string.Join(";", items.OrderBy(i => i.ItemId).ThenBy(i => i.Hq).Select(i => $"{i.ItemId}:{i.Hq}:{i.Quantity}"));
        if (pendingRetainerStock != (retainer->RetainerId, signature))
        {
            pendingRetainerStock = (retainer->RetainerId, signature);
            return;
        }
        Stock.Record(new(CharacterId, StockSnapshot.Retainer, retainer->RetainerId, retainer->NameString, DateTimeOffset.UtcNow, items));
    }

    private unsafe void ReadListings(ulong retainerId, string name)
    {
        var inventory = InventoryManager.Instance();
        var container = inventory == null ? null : inventory->GetInventoryContainer(InventoryType.RetainerMarket);
        if (container == null || !container->IsLoaded || container->Size != 20) return;

        var listings = new List<RetainerListing>();
        foreach (var item in Plugin.GameInventory.GetInventoryItems(GameInventoryType.RetainerMarket))
        {
            if (item.IsEmpty || item.Quantity <= 0) continue;
            if (item.InventorySlot >= 20) return;
            var price = inventory->GetRetainerMarketPrice((short)item.InventorySlot);
            // Prices arrive a moment after the items; try again next second.
            if (price == 0 || plugin.Items.Get(item.BaseItemId) is not { } info) return;
            listings.Add(new(info.Id, info.Name, item.IsHq, item.Quantity, price, item.InventorySlot));
        }
        listings.Sort((a, b) => a.Slot.CompareTo(b.Slot));
        Retainers.UpdateListings(CharacterId, retainerId, name, listings.ToArray());
    }

    private static List<(uint Id, bool Hq, long Quantity)> Items(GameInventoryType type)
    {
        var items = new List<(uint, bool, long)>();
        foreach (var item in Plugin.GameInventory.GetInventoryItems(type))
            if (!item.IsEmpty && item.Quantity > 0)
                items.Add((item.BaseItemId, item.IsHq, item.Quantity));
        return items;
    }

    private static StockItem[] Totals(IEnumerable<(uint Id, bool Hq, long Quantity)> items) => items
        .GroupBy(i => (i.Id, i.Hq))
        .Select(g => new StockItem(g.Key.Id, g.Key.Hq, g.Sum(i => i.Quantity)))
        .ToArray();

    private static unsafe bool Loaded(InventoryType type)
    {
        var manager = InventoryManager.Instance();
        var container = manager == null ? null : manager->GetInventoryContainer(type);
        return container != null && container->IsLoaded;
    }
}
