using System;
using System.IO;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace Bagcheck.Windows;

/// <summary>A short tour for new players: what Bagcheck does, and the few settings worth a look.</summary>
public sealed class WelcomeWindow : ThemedWindow
{
    private static readonly string[] Titles = ["Your retainers", "Your shopping list", "Prices", "A few settings"];

    private readonly Plugin plugin;
    private int step;

    public WelcomeWindow(Plugin plugin) : base("Welcome to Bagcheck###BagcheckWelcome", ImGuiWindowFlags.NoCollapse)
    {
        this.plugin = plugin;
        Size = new Vector2(600, 500);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(520, 420), MaximumSize = new Vector2(1000, 1000) };
    }

    public void Open()
    {
        step = 0;
        IsOpen = true;
    }

    public override void OnClose()
    {
        if (plugin.Configuration.WelcomeSeen) return;
        plugin.Configuration.WelcomeSeen = true;
        plugin.MarkDirty();
    }

    public override void Draw()
    {
        if (step == 0)
        {
            DrawWelcome();
            return;
        }

        ImGui.ProgressBar(step / (float)Titles.Length, new Vector2(-1, 6 * ImGuiHelpers.GlobalScale), "");
        ImGui.TextDisabled($"Step {step} of {Titles.Length}");
        using (ImRaii.PushColor(ImGuiCol.Text, Theme.AccentText)) ImGui.TextUnformatted(Titles[step - 1]);
        ImGui.Separator();

        var footer = ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y;
        using (var body = ImRaii.Child("##body", new Vector2(0, -footer)))
        {
            if (body.Success)
            {
                switch (step)
                {
                    case 1: Retainers(); break;
                    case 2: ShoppingList(); break;
                    case 3: Prices(); break;
                    default: Settings(); break;
                }
            }
        }

        ImGui.Separator();
        if (ImGui.Button("Back")) step--;
        ImGui.SameLine();
        if (ImGui.Button("Close")) IsOpen = false;
        var last = step == Titles.Length;
        var label = last ? "Finish" : "Next";
        ImGui.SameLine(Math.Max(ImGui.GetCursorPosX(), ImGui.GetWindowContentRegionMax().X - ImGui.CalcTextSize(label).X - ImGui.GetStyle().FramePadding.X * 2));
        if (Theme.PrimaryButton(label))
        {
            if (!last) step++;
            else
            {
                IsOpen = false;
                plugin.ShowMainWindow();
            }
        }
    }

    private void DrawWelcome()
    {
        ImGuiHelpers.ScaledDummy(8);
        Logo(96);
        ImGuiHelpers.ScaledDummy(6);
        using (ImRaii.PushColor(ImGuiCol.Text, Theme.AccentText)) Ui.Centered("Welcome to Bagcheck");
        ImGuiHelpers.ScaledDummy(4);
        ImGui.TextWrapped("Bagcheck is a shopping list and retainer tracker for crafters. It keeps count of what you need and " +
                          "what you already have, so you know what's left to buy and roughly what it will cost.");
        ImGuiHelpers.ScaledDummy(4);
        Bullet("Keeps a shopping list, by hand, from the right-click menu, or pasted from Teamcraft.");
        Bullet("Counts what you hold in your bags, crystals, saddlebag and retainers.");
        Bullet("Checks prices across your data centre, and remembers your retainers' gil and listings.");
        ImGuiHelpers.ScaledDummy(6);
        using (ImRaii.PushColor(ImGuiCol.Text, Theme.Good))
            ImGui.TextWrapped("Bagcheck never clicks, buys or sells anything for you. It only reads what you already have open, " +
                              "and asks Universalis for prices.");
        ImGuiHelpers.ScaledDummy(10);

        const string tour = "Show me around", later = "Not now";
        var style = ImGui.GetStyle();
        var width = ImGui.CalcTextSize(tour).X + ImGui.CalcTextSize(later).X + style.FramePadding.X * 4 + style.ItemSpacing.X;
        ImGui.SetCursorPosX(Math.Max(0, (ImGui.GetContentRegionAvail().X - width) / 2) + ImGui.GetCursorPosX());
        if (Theme.PrimaryButton(tour)) step = 1;
        ImGui.SameLine();
        if (ImGui.Button(later)) IsOpen = false;
        ImGuiHelpers.ScaledDummy(4);
        using (ImRaii.PushColor(ImGuiCol.Text, Theme.Muted)) Ui.Centered("You can open this again from the About tab or with /bagcheck welcome.");
    }

    private static void Retainers()
    {
        ImGui.TextWrapped("Bagcheck learns about your retainers as you use them, the way you always do.");
        ImGuiHelpers.ScaledDummy(2);
        Bullet("Open a summoning bell: every retainer's name and gil is noted.");
        Bullet("Open a retainer's market: its listings are noted.");
        Bullet("Open a retainer's inventory: what it holds counts towards your shopping list.");
        ImGuiHelpers.ScaledDummy(2);
        ImGui.TextWrapped("On the Retainers tab, Check prices compares your listings with your home world's market and " +
                          "shows which have been undercut. Your own retainers never count as competition.");
    }

    private static void ShoppingList()
    {
        ImGui.TextWrapped("The Shopping List is what you need for your crafts. There are three ways to add to it:");
        ImGuiHelpers.ScaledDummy(2);
        Bullet("Search for an item at the top of the list.");
        Bullet("Right-click an item in your bags and choose \"Add to Shopping List\".");
        Bullet("In Teamcraft, press \"Copy as text\" on a panel, then use Import from Teamcraft. Raw materials are ticked for you.");
        ImGuiHelpers.ScaledDummy(2);
        ImGui.TextWrapped("Each item shows what you need, what you have and what's still missing. Hover over Have to see where " +
                          "your stock is. Groups keep each craft's materials together, and you can pause a group you're not " +
                          "working on yet.");
    }

    private static void Prices()
    {
        ImGui.TextWrapped("Check prices asks Universalis where the things you still need are cheapest across your data centre.");
        ImGuiHelpers.ScaledDummy(2);
        Bullet("Price is the average cost per unit, with the market's 5% tax, and Where says which world has them.");
        Bullet("Give an item a target price and Bagcheck shows whether anything is listed at or under it.");
        Bullet("If an NPC sells it for less, Where says NPC, and the tooltip says who and where.");
        ImGuiHelpers.ScaledDummy(2);
        ImGui.TextWrapped("At the top you'll see roughly what finishing the list would cost. The shopping is up to you.");
    }

    private void Settings()
    {
        var c = plugin.Configuration;
        var changed = false;
        ImGui.TextWrapped("These can be changed any time in Settings (the cog on the Bagcheck window).");
        ImGuiHelpers.ScaledDummy(2);
        changed |= Checkbox("Count the saddlebag", c.CountSaddlebag, v => c.CountSaddlebag = v);
        changed |= Checkbox("Count what your retainers hold", c.CountRetainers, v => c.CountRetainers = v);
        changed |= Checkbox("Add \"Add to Shopping List\" to the right-click menu", c.ShowContextMenu, v => c.ShowContextMenu = v);
        changed |= Checkbox("Use the Bagcheck theme", c.UseTheme, v => c.UseTheme = v);
        if (changed) plugin.MarkDirty();
        ImGuiHelpers.ScaledDummy(4);
        ImGui.TextWrapped("That's it. Open Bagcheck any time with /bagcheck.");
    }

    private static bool Checkbox(string label, bool value, Action<bool> set)
    {
        var v = value;
        if (!ImGui.Checkbox(label, ref v)) return false;
        set(v);
        return true;
    }

    private static void Bullet(string text)
    {
        ImGui.Bullet();
        ImGui.SameLine();
        ImGui.TextWrapped(text);
    }

    public static void Logo(float size)
    {
        var path = Path.Combine(Plugin.PluginInterface.AssemblyLocation.DirectoryName ?? "", "images", "logo.png");
        if (Plugin.TextureProvider.GetFromFile(path).GetWrapOrDefault() is not { } icon) return;
        var scaled = new Vector2(size, size) * ImGuiHelpers.GlobalScale;
        ImGui.SetCursorPosX(Math.Max(0, (ImGui.GetContentRegionAvail().X - scaled.X) / 2) + ImGui.GetCursorPosX());
        ImGui.Image(icon.Handle, scaled);
    }
}
