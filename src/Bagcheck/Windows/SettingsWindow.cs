using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace Bagcheck.Windows;

public sealed class SettingsWindow : ThemedWindow
{
    private readonly Plugin plugin;

    public SettingsWindow(Plugin plugin) : base("Bagcheck Settings###BagcheckSettings", ImGuiWindowFlags.NoCollapse)
    {
        this.plugin = plugin;
        Size = new Vector2(520, 460);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(440, 360), MaximumSize = new Vector2(900, 1200) };
    }

    public override void Draw()
    {
        var c = plugin.Configuration;
        var changed = false;

        Theme.Section("What counts as held");
        using (var grid = Grid("##holdings"))
        {
            if (grid.Success)
            {
                changed |= Row("Count the saddlebag",
                    "Counts your saddlebag as it was when you last opened it.",
                    () => Checkbox("##saddlebag", c.CountSaddlebag, v => c.CountSaddlebag = v));
                changed |= Row("Count retainers",
                    "Counts what your retainers held when you last opened each of them at a summoning bell.",
                    () => Checkbox("##retainers", c.CountRetainers, v => c.CountRetainers = v));
                changed |= Row("Retainer counts expire after (hours)",
                    "Retainer contents seen longer ago than this are still shown, but not counted, since ventures or " +
                    "other changes may have happened since.",
                    () =>
                    {
                        var hours = c.RetainerMaxAgeHours;
                        if (!ImGui.InputInt("##maxAge", ref hours)) return false;
                        c.RetainerMaxAgeHours = Math.Clamp(hours, 1, 24 * 60);
                        return true;
                    });
            }
        }

        Theme.Section("Shopping list");
        using (var grid = Grid("##list"))
        {
            if (grid.Success)
                changed |= Row("Right-click \"Add to Shopping List\"",
                    "Adds an entry to the right-click menu of items in your bags, saddlebag and retainers.",
                    () => Checkbox("##contextMenu", c.ShowContextMenu, v => c.ShowContextMenu = v));
        }

        Theme.Section("Look");
        using (var grid = Grid("##look"))
        {
            if (grid.Success)
            {
                changed |= Row("Use the Bagcheck theme",
                    "Dark panels, rounded corners and one accent colour. Off gives Bagcheck's windows Dalamud's own style.",
                    () => Checkbox("##theme", c.UseTheme, v => c.UseTheme = v));
                changed |= Row("Accent colour", "The colour of main buttons, checkmarks, the selected tab and progress bars.", () =>
                {
                    var names = Theme.Accents.Select(a => a.Name).ToArray();
                    var index = Math.Max(0, Array.FindIndex(Theme.Accents, a => a.Choice == c.Accent));
                    if (!ImGui.Combo("##accent", ref index, names)) return false;
                    c.Accent = Theme.Accents[index].Choice;
                    return true;
                });
                if (c.Accent == AccentChoice.Custom)
                    changed |= Row("Custom colour", "Text on it turns dark or light by itself so it stays readable.", () =>
                    {
                        var rgb = Theme.Rgb(c.CustomAccent);
                        var colour = new Vector3(rgb.X, rgb.Y, rgb.Z);
                        if (!ImGui.ColorEdit3("##custom", ref colour, ImGuiColorEditFlags.DisplayHex)) return false;
                        c.CustomAccent = Theme.ToRgb(colour);
                        return true;
                    });
            }
        }
        ImGui.Spacing();
        Theme.PrimaryButton("Main button##preview");
        ImGui.SameLine();
        ImGui.Button("Other button##preview");
        ImGui.SameLine();
        Theme.Pill("Done", Tone.Good);
        ImGui.SameLine();
        Theme.Pill("Undercut", Tone.Warning);
        ImGui.SameLine();
        Theme.Pill("NPC", Tone.Info);

        ImGui.Spacing();
        ImGui.Separator();
        if (ImGui.Button("Open the welcome guide")) plugin.OpenWelcome();

        if (changed) plugin.MarkDirty();
    }

    private static ImRaii.TableDisposable Grid(string id)
    {
        var table = ImRaii.Table(id, 2, ImGuiTableFlags.SizingFixedFit);
        if (!table.Success) return table;
        ImGui.TableSetupColumn("##label", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("##value", ImGuiTableColumnFlags.WidthFixed, 170 * ImGuiHelpers.GlobalScale);
        return table;
    }

    private static bool Row(string label, string help, Func<bool> control)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        ImGui.SameLine();
        ImGuiComponents.HelpMarker(help);
        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(-1);
        return control();
    }

    private static bool Checkbox(string id, bool value, Action<bool> set)
    {
        var v = value;
        if (!ImGui.Checkbox(id, ref v)) return false;
        set(v);
        return true;
    }
}
