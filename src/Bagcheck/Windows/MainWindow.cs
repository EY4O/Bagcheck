using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace Bagcheck.Windows;

public sealed class MainWindow : ThemedWindow
{
    private readonly RetainersTab retainers;
    private readonly ShoppingListTab shopping;
    private bool showList;

    public MainWindow(Plugin plugin) : base("Bagcheck###BagcheckMain")
    {
        retainers = new RetainersTab(plugin);
        shopping = new ShoppingListTab(plugin);
        Size = new Vector2(780, 580);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(660, 420), MaximumSize = new Vector2(1600, 1400) };
    }

    /// <summary>Opens the window on the Shopping List with an entry selected.</summary>
    public void ShowListEntry(Guid id)
    {
        shopping.Select(id);
        showList = true;
        IsOpen = true;
    }

    public override void Draw()
    {
        using var tabs = ImRaii.TabBar("##tabs");
        if (!tabs.Success) return;

        var listFlags = showList ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
        showList = false;
        using (var tab = ImRaii.TabItem("Shopping List", listFlags))
        {
            if (tab.Success) shopping.Draw();
        }
        using (var tab = ImRaii.TabItem("Retainers"))
        {
            if (tab.Success) retainers.Draw();
        }
    }
}
