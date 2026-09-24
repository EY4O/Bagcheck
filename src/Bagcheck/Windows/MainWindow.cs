using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace Bagcheck.Windows;

public sealed class MainWindow : ThemedWindow
{
    private readonly RetainersTab retainers;

    public MainWindow(Plugin plugin) : base("Bagcheck###BagcheckMain")
    {
        retainers = new RetainersTab(plugin);
        Size = new Vector2(780, 580);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(660, 420), MaximumSize = new Vector2(1600, 1400) };
    }

    public override void Draw()
    {
        using var tabs = ImRaii.TabBar("##tabs");
        if (!tabs.Success) return;
        using (var tab = ImRaii.TabItem("Retainers"))
        {
            if (tab.Success) retainers.Draw();
        }
    }
}
