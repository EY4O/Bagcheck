using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace Bagcheck.Windows;

public sealed class MainWindow : ThemedWindow
{
    private const string PatreonUrl = "https://www.patreon.com/Looneth";
    private const string KoFiUrl = "https://ko-fi.com/looneth";

    private readonly RetainersTab retainers;
    private readonly ShoppingListTab shopping;
    private readonly AboutTab about;
    private bool showList;
    private float supportWidth;

    public MainWindow(Plugin plugin) : base("Bagcheck###BagcheckMain")
    {
        retainers = new RetainersTab(plugin);
        shopping = new ShoppingListTab(plugin);
        about = new AboutTab(plugin);
        Size = new Vector2(780, 580);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(660, 420), MaximumSize = new Vector2(1600, 1400) };
        TitleBarButtons.Add(new TitleBarButton
        {
            Icon = FontAwesomeIcon.Cog,
            IconOffset = new Vector2(2, 1),
            Click = _ => plugin.ToggleSettings(),
            ShowTooltip = () => ImGui.SetTooltip("Settings"),
        });
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
        var tabRow = ImGui.GetCursorPos();
        using (var tabs = ImRaii.TabBar("##tabs"))
        {
            if (tabs.Success) DrawTabs();
        }
        DrawSupport(tabRow);
    }

    private void DrawTabs()
    {
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
        using (var tab = ImRaii.TabItem("About"))
        {
            if (tab.Success) about.Draw();
        }
    }

    /// <summary>
    /// The support button at the right end of the tab row: left click opens Patreon, right click Ko-fi. It's drawn after
    /// the tabs, over the empty end of their row, and its width is measured once drawn so it sits flush from then on.
    /// </summary>
    private void DrawSupport(Vector2 tabRow)
    {
        ImGui.SetCursorPos(new Vector2(Math.Max(tabRow.X, ImGui.GetWindowContentRegionMax().X - supportWidth), tabRow.Y));
        if (Theme.AccentIconButton(FontAwesomeIcon.Heart, "Patreon / Ko-fi")) Ui.OpenUrl(PatreonUrl);
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right)) Ui.OpenUrl(KoFiUrl);
        supportWidth = ImGui.GetItemRectSize().X;
        Ui.Tip("If Bagcheck has saved you some time or gil, please consider supporting its developer.\n\n" +
               "Left click: Patreon\nRight click: Ko-fi");
    }
}
