using System;
using System.Collections.Generic;
using System.IO;
using Dalamud.Game.Inventory;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Bagcheck.Core.Retainers;

namespace Bagcheck.Game;

/// <summary>
/// Reads what the game already has loaded, once a second: the retainer list, and the listings of a retainer whose
/// market you have open. It never opens a window or asks the game for anything.
/// </summary>
public sealed class GameReader
{
    private static readonly TimeSpan ReadEvery = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan SaveEvery = TimeSpan.FromSeconds(10);

    private readonly Plugin plugin;
    private ulong activeRetainer;
    private DateTimeOffset nextRead;
    private DateTimeOffset nextSave;
    private bool stopped;

    public GameReader(Plugin plugin)
    {
        this.plugin = plugin;
        Retainers = new RetainerStore(Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), "retainers.json"));
    }

    public RetainerStore Retainers { get; }
    public ulong CharacterId { get; private set; }

    /// <summary>Set if reading stopped after an unexpected error.</summary>
    public string? Problem { get; private set; }

    public void Update()
    {
        var character = plugin.CharacterId;
        if (character != CharacterId)
        {
            CharacterId = character;
            activeRetainer = 0;
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
        nextSave = DateTimeOffset.UtcNow + SaveEvery;
    }

    private unsafe void Read()
    {
        var manager = RetainerManager.Instance();
        if (manager == null || !manager->IsReady) { activeRetainer = 0; return; }
        ReadRoster(manager);

        var agent = AgentRetainer.Instance();
        if (agent == null || !agent->IsAgentActive()) { activeRetainer = 0; return; }
        var retainer = manager->GetActiveRetainer();
        if (retainer == null || retainer->RetainerId == 0) { activeRetainer = 0; return; }

        // Give a retainer switch a second to settle before trusting its market container.
        if (activeRetainer != retainer->RetainerId) { activeRetainer = retainer->RetainerId; return; }
        ReadListings(retainer->RetainerId, retainer->NameString);
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
        Retainers.UpdateRoster(CharacterId, roster);
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
}
