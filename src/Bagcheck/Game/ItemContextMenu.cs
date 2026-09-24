using System;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Plugin.Services;

namespace Bagcheck.Game;

/// <summary>"Add to Shopping List" on the right-click menu of items in your bags, saddlebag and retainers.</summary>
public sealed class ItemContextMenu : IDisposable
{
    private readonly IContextMenu contextMenu;
    private readonly Plugin plugin;

    public ItemContextMenu(IContextMenu contextMenu, Plugin plugin)
    {
        this.contextMenu = contextMenu;
        this.plugin = plugin;
        contextMenu.OnMenuOpened += OnMenuOpened;
    }

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (!plugin.Configuration.ShowContextMenu || args.MenuType != ContextMenuType.Inventory ||
            args.Target is not MenuTargetInventory { TargetItem: { } item } || item.IsEmpty || item.IsCollectable)
            return;

        // Copy these now; the slot may hold something else by the time the entry is clicked.
        var itemId = item.BaseItemId;
        var hq = item.IsHq;
        if (plugin.Items.Get(itemId) is not { IsMarketable: true }) return;

        args.AddMenuItem(new MenuItem
        {
            Name = "Add to Shopping List",
            PrefixChar = 'B',
            OnClicked = _ => plugin.AddToList(itemId, hq),
        });
    }

    public void Dispose() => contextMenu.OnMenuOpened -= OnMenuOpened;
}
