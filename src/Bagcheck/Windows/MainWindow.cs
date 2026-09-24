using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Bagcheck.Windows;

public sealed class MainWindow : Window
{
    private readonly Plugin plugin;

    public MainWindow(Plugin plugin) : base("Bagcheck###BagcheckMain")
    {
        this.plugin = plugin;
        Size = new Vector2(760, 560);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(640, 420), MaximumSize = new Vector2(1600, 1400) };
    }

    public override void Draw()
    {
        ImGui.TextUnformatted($"Bagcheck {Plugin.PluginInterface.Manifest.AssemblyVersion}");
    }
}
