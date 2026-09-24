using System;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace Bagcheck.Windows;

public sealed class AboutTab(Plugin plugin)
{
    private const string SiteUrl = "https://ey4o.github.io/XIV-Plugins/";
    private const string SourceUrl = "https://github.com/EY4O/Bagcheck";

    public void Draw()
    {
        ImGuiHelpers.ScaledDummy(6);
        Ui.Centered($"Bagcheck {Plugin.PluginInterface.Manifest.AssemblyVersion}");
        using (ImRaii.PushColor(ImGuiCol.Text, Theme.Muted))
            Ui.Centered("A shopping list and retainer tracker for crafters.");
        ImGuiHelpers.ScaledDummy(10);
        WelcomeWindow.Logo(112);
        ImGuiHelpers.ScaledDummy(10);
        Ui.Centered("Bagcheck never clicks, buys or sells anything for you.");
        Ui.Centered("It reads what you have open and asks Universalis for prices.");
        ImGuiHelpers.ScaledDummy(10);

        (string Label, Action Click, string Tip)[] buttons =
        [
            ("Welcome guide", plugin.OpenWelcome, "A short tour of what Bagcheck does."),
            ("Settings", plugin.ToggleSettings, "What counts as held, the right-click menu, and the look."),
            ("Plugin site", () => Ui.OpenUrl(SiteUrl), SiteUrl),
            ("Source code", () => Ui.OpenUrl(SourceUrl), SourceUrl + "\nMIT licence."),
        ];
        var style = ImGui.GetStyle();
        var width = buttons.Sum(b => ImGui.CalcTextSize(b.Label).X + style.FramePadding.X * 2) + style.ItemSpacing.X * (buttons.Length - 1);
        ImGui.SetCursorPosX(Math.Max(0, (ImGui.GetContentRegionAvail().X - width) / 2) + ImGui.GetCursorPosX());
        for (var i = 0; i < buttons.Length; i++)
        {
            if (i > 0) ImGui.SameLine();
            if (ImGui.Button(buttons[i].Label)) buttons[i].Click();
            Ui.Tip(buttons[i].Tip);
        }

        ImGuiHelpers.ScaledDummy(10);
        using (ImRaii.PushColor(ImGuiCol.Text, Theme.Muted))
            Ui.Centered("Market data from Universalis (universalis.app), gathered by players like you.");
    }
}
