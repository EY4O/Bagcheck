using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Bagcheck.Core.Shopping;
using Bagcheck.Core.Stock;
using Bagcheck.Game;

namespace Bagcheck.Windows;

/// <summary>The shopping list: what you need, what you have, and what's still missing.</summary>
public sealed class ShoppingListTab(Plugin plugin)
{
    private readonly HashSet<Guid> collapsed = [];
    private string search = "";
    private string lastSearch = "";
    private IReadOnlyList<ItemInfo> results = [];
    private string filter = "";
    private string newGroup = "";
    private bool hideDone;
    private Guid? addTo;
    private Guid? selectedItem;
    private Guid? selectedGroup;
    private bool confirmRemove;

    // Worked out once per frame and shared by the rows.
    private Dictionary<ItemKey, Holding> holdings = [];
    private IReadOnlyDictionary<ItemKey, Need> needs = new Dictionary<ItemKey, Need>();

    private ShoppingList List => plugin.Configuration.List;

    private Holding Held(ItemKey key) => holdings.TryGetValue(key, out var holding) ? holding : plugin.Reader.Held(key);

    /// <summary>Selects an entry, for example one just added from the right-click menu.</summary>
    public void Select(Guid id)
    {
        selectedItem = id;
        selectedGroup = null;
        confirmRemove = false;
    }

    public void Draw()
    {
        var list = List;
        if (addTo is { } target && list.Groups.All(g => g.Id != target)) addTo = null;

        if (plugin.CharacterId == 0) ImGui.TextColored(Theme.Warning, "Log in to count what you hold.");
        DrawAddRow(list);

        // After the add row, which may have just added an item.
        holdings = list.Items.Select(i => i.GetKey()).Distinct().ToDictionary(k => k, plugin.Reader.Held);
        needs = ShoppingNeeds.Of(list, key => Held(key).Total);
        DrawSummary();
        DrawPrices();
        ImGui.Separator();

        var item = list.Items.FirstOrDefault(i => i.Id == selectedItem);
        var group = item == null ? list.Groups.FirstOrDefault(g => g.Id == selectedGroup) : null;
        // Both editors are two lines: a heading with buttons, then the fields.
        var editorHeight = item != null || group != null
            ? ImGui.GetFrameHeightWithSpacing() * 2 + ImGui.GetStyle().ItemSpacing.Y * 2 + 6 * ImGuiHelpers.GlobalScale
            : 0;
        using (var child = ImRaii.Child("##list", new Vector2(0, -editorHeight - 4 * ImGuiHelpers.GlobalScale)))
        {
            if (child.Success) DrawTable(list);
        }

        if (item != null)
        {
            ImGui.Separator();
            DrawItemEditor(list, item);
        }
        else if (group != null)
        {
            ImGui.Separator();
            DrawGroupEditor(list, group);
        }
    }

    private void DrawAddRow(ShoppingList list)
    {
        ImGui.SetNextItemWidth(260 * ImGuiHelpers.GlobalScale);
        ImGui.InputTextWithHint("##add", "Add an item: name or item id", ref search, 100);
        if (search != lastSearch)
        {
            lastSearch = search;
            results = plugin.Items.Search(search);
        }

        if (list.Groups.Count > 0)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(170 * ImGuiHelpers.GlobalScale);
            using (var combo = ImRaii.Combo("##addTo", "Add to: " + GroupName(list, addTo)))
            {
                if (combo.Success)
                {
                    if (ImGui.Selectable("No group", addTo == null)) addTo = null;
                    foreach (var g in list.Groups.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        using var id = ImRaii.PushId(g.Id.ToString());
                        if (ImGui.Selectable(g.Name, addTo == g.Id)) addTo = g.Id;
                    }
                }
            }
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(150 * ImGuiHelpers.GlobalScale);
        ImGui.InputTextWithHint("##newGroup", "New group name", ref newGroup, 64);
        ImGui.SameLine();
        using (ImRaii.Disabled(newGroup.Trim().Length == 0))
        {
            if (ImGui.Button("Create group"))
            {
                var group = new ShoppingGroup { Name = newGroup.Trim() };
                list.Groups.Add(group);
                addTo = group.Id;
                newGroup = "";
                plugin.MarkDirty();
            }
        }
        ImGui.SameLine();
        if (ImGui.Button("Import from Teamcraft")) plugin.OpenImport(null);
        Ui.Tip("Paste a list copied with Teamcraft's \"Copy as text\" button into a new group.");

        if (results.Count == 0) return;
        using var child = ImRaii.Child("##results", new Vector2(0, Math.Min(results.Count, 6) * ImGui.GetFrameHeightWithSpacing() + 4), true);
        if (!child.Success) return;
        foreach (var result in results)
        {
            using var id = ImRaii.PushId((int)result.Id);
            using (ImRaii.Disabled(!result.IsMarketable))
            {
                if (!ImGui.Selectable(result.IsMarketable ? result.Name : $"{result.Name} (can't be bought on the market)")) continue;
            }
            var entry = list.Add(result.Id, result.Name, false, 1, addTo, out _);
            plugin.MarkDirty();
            Select(entry.Id);
            search = lastSearch = "";
            results = [];
            return;
        }
    }

    private void DrawSummary()
    {
        var active = needs.Values.ToList();
        var done = active.Count(n => n.Done);
        ImGui.TextUnformatted(active.Count == 0 ? "Nothing on the list yet."
            : $"{active.Count} item{(active.Count == 1 ? "" : "s")}   ·   {done} done   ·   {active.Count - done} still needed");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
        ImGui.InputTextWithHint("##filter", "Filter the list", ref filter, 64);
        ImGui.SameLine();
        ImGui.Checkbox("Hide done", ref hideDone);
    }

    private void DrawPrices()
    {
        var checker = plugin.Prices;
        var missing = needs.Values.Where(n => !n.Done).ToList();
        using (ImRaii.Disabled(checker.Running || missing.Count == 0 || plugin.CharacterId == 0))
        {
            if (Theme.PrimaryButton("Check prices")) checker.Start(missing);
        }
        Ui.TipAlways(missing.Count == 0
            ? "Nothing is still needed."
            : $"Ask Universalis for prices across {Worlds.DataCentre} for the {missing.Count} items you still need.");

        ImGui.SameLine();
        if (checker.Running)
        {
            ImGui.TextDisabled($"Checking {checker.Done} of {checker.Total}...");
        }
        else if (checker.CheckedAt is { } at)
        {
            var total = Prices.Total(missing.Select(n => checker.For(n.Key)).OfType<PriceCheck>());
            ImGui.TextUnformatted($"About {total.Cost:N0} gil to finish the list");
            Ui.Tip($"The cheapest way to buy what's still needed, across {checker.DataCentre}, with an estimated " +
                   $"{Prices.EstimatedTax:P0} market tax.\n{total.Priced} items priced" +
                   (total.FromNpc > 0 ? $", {total.FromNpc} cheaper from an NPC" : "") + ".\n" +
                   "Prices move, so treat this as a guide.");
            ImGui.SameLine();
            ImGui.TextDisabled($"({checker.DataCentre}, {Ui.Ago(at)})");
            if (total.CantFinish > 0)
            {
                ImGui.SameLine();
                ImGui.TextColored(Theme.Warning, $"{total.CantFinish} not fully listed");
                Ui.Tip("There aren't enough listings on the data centre to cover these; the total counts what is listed.");
            }
        }
        else
        {
            ImGui.TextDisabled("Prices not checked yet.");
        }

        if (checker.Failures.Count > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(Theme.Bad, $"{checker.Failures.Count} couldn't be checked");
            Ui.Tip(string.Join("\n", checker.Failures));
        }
    }

    private void DrawTable(ShoppingList list)
    {
        if (list.Items.Count == 0 && list.Groups.Count == 0)
        {
            ImGui.TextWrapped("Your list is empty. Search for an item above, right-click an item in your bags and choose " +
                              "\"Add to Shopping List\", or import a list from Teamcraft.");
            return;
        }

        using var table = ImRaii.Table("##shopping", 7,
            ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingFixedFit);
        if (!table.Success) return;
        var scale = ImGuiHelpers.GlobalScale;
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("##icon", ImGuiTableColumnFlags.WidthFixed, 24 * scale);
        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Need", ImGuiTableColumnFlags.WidthFixed, 60 * scale);
        ImGui.TableSetupColumn("Have", ImGuiTableColumnFlags.WidthFixed, 110 * scale);
        ImGui.TableSetupColumn("Still need", ImGuiTableColumnFlags.WidthFixed, 80 * scale);
        ImGui.TableSetupColumn("Price", ImGuiTableColumnFlags.WidthFixed, 80 * scale);
        ImGui.TableSetupColumn("Where", ImGuiTableColumnFlags.WidthFixed, 120 * scale);
        ImGui.TableHeadersRow();

        var rows = list.Items.Where(Visible).OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var loose = rows.Where(i => list.GroupOf(i) == null).ToList();
        if (list.Groups.Count == 0)
        {
            DrawRows(list, loose, false);
            return;
        }
        if (loose.Count > 0 && GroupRow(list, null, loose)) DrawRows(list, loose, true);
        foreach (var group in list.Groups.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
        {
            var members = rows.Where(i => i.GroupId == group.Id).ToList();
            if (members.Count == 0 && (filter.Length > 0 || hideDone)) continue;
            using var id = ImRaii.PushId(group.Id.ToString());
            if (GroupRow(list, group, members)) DrawRows(list, members, true);
        }
    }

    private bool Visible(ShoppingItem item)
    {
        if (filter.Length > 0 && !item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) && item.ItemId.ToString() != filter)
            return false;
        return !hideDone || !List.IsActive(item) || needs.GetValueOrDefault(item.GetKey()) is not { Done: true };
    }

    /// <summary>A group's header row. Returns whether its items are shown.</summary>
    private bool GroupRow(ShoppingList list, ShoppingGroup? group, List<ShoppingItem> members)
    {
        var key = group?.Id ?? Guid.Empty;
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TableNextColumn();
        ImGui.SetNextItemOpen(filter.Length > 0 || !collapsed.Contains(key), ImGuiCond.Always);
        var done = members.Count(i => needs.GetValueOrDefault(i.GetKey()) is { Done: true });
        var label = $"{group?.Name ?? "No group"}   ({done} of {members.Count} done)" + (group is { Active: false } ? "   (paused)" : "");
        bool open;
        using (ImRaii.PushColor(ImGuiCol.Text, Theme.Muted, group is { Active: false }))
            open = ImGui.TreeNodeEx("##group", ImGuiTreeNodeFlags.NoTreePushOnOpen | ImGuiTreeNodeFlags.SpanAvailWidth, label);
        if (ImGui.IsItemClicked() && filter.Length == 0 && !collapsed.Remove(key)) collapsed.Add(key);

        if (group != null)
        {
            ImGui.TableNextColumn();
            ImGui.TableNextColumn();
            ImGui.TableNextColumn();
            ImGui.TableNextColumn();
            if (ImGui.SmallButton("Edit"))
            {
                selectedGroup = group.Id;
                selectedItem = null;
                confirmRemove = false;
            }
        }
        return open;
    }

    private void DrawRows(ShoppingList list, List<ShoppingItem> rows, bool indent)
    {
        foreach (var item in rows)
        {
            using var id = ImRaii.PushId(item.Id.ToString());
            var key = item.GetKey();
            var active = list.IsActive(item);
            var need = active ? needs.GetValueOrDefault(key) : null;
            using var muted = ImRaii.PushColor(ImGuiCol.Text, Theme.Muted, !active);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            Ui.Icon(plugin.Items, item.ItemId, item.HqOnly, 20);

            ImGui.TableNextColumn();
            if (indent) ImGui.Indent();
            var name = item.HqOnly ? item.Name + " (HQ)" : item.Name;
            if (ImGui.Selectable(name, selectedItem == item.Id, ImGuiSelectableFlags.SpanAllColumns))
            {
                if (selectedItem == item.Id) selectedItem = null;
                else Select(item.Id);
            }
            if (indent) ImGui.Unindent();

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(item.Needed.ToString("N0"));

            ImGui.TableNextColumn();
            DrawHave(item, Held(key), need);

            ImGui.TableNextColumn();
            if (need == null) ImGui.TextDisabled("paused");
            else if (need.Done) Theme.Pill("Done", Tone.Good);
            else ImGui.TextUnformatted(need.Short.ToString("N0"));
            if (need is { Entries: > 1 })
                Ui.Tip($"This item is on your list {need.Entries} times; together they need {need.Needed:N0}.");

            DrawPrice(need is { Done: false } ? plugin.Prices.For(key) : null);
        }
    }

    /// <summary>The Price and Where cells of a row.</summary>
    private void DrawPrice(PriceCheck? check)
    {
        ImGui.TableNextColumn();
        if (check == null)
        {
            ImGui.TextDisabled("-");
            ImGui.TableNextColumn();
            return;
        }

        var details = Details(check);
        if (check.NpcIsCheaper)
        {
            ImGui.TextUnformatted(check.Npc!.Price.ToString("N0"));
            Ui.Tip(details);
            ImGui.TableNextColumn();
            Theme.Pill("NPC", Tone.Info);
            Ui.Tip(details);
            return;
        }

        if (check.Market.Units == 0)
        {
            ImGui.TextDisabled("-");
            ImGui.TableNextColumn();
            ImGui.TextColored(Theme.Warning, "none listed");
            Ui.Tip(details);
            return;
        }

        ImGui.TextUnformatted(decimal.Round(check.Market.UnitCost).ToString("N0"));
        Ui.Tip(details);

        ImGui.TableNextColumn();
        var worlds = check.Market.ByWorld();
        var where = Worlds.Name(worlds[0].WorldId) + (worlds.Count > 1 ? $" +{worlds.Count - 1}" : "");
        if (check.Market.Units < check.Wanted) ImGui.TextColored(Theme.Warning, where);
        else ImGui.TextUnformatted(where);
        Ui.Tip(details);
    }

    private static string Details(PriceCheck check)
    {
        var lines = new List<string> { $"Still needed when checked: {check.Wanted:N0}" };
        if (check.Market.Units > 0)
        {
            lines.Add($"Cheapest on the data centre: {check.Market.Units:N0} for about {check.Market.Cost:N0} gil " +
                      $"({check.Market.UnitCost:N0} each with tax)" +
                      (check.Market.Units < check.Wanted ? ", all that's listed" : ""));
            lines.AddRange(check.Market.ByWorld().Select(w => $"    {Worlds.Name(w.WorldId)}: {w.Units:N0}"));
        }
        else
        {
            lines.Add("Nothing listed on the data centre.");
        }
        lines.Add(check.Here is { } here
            ? $"On {Worlds.CurrentName}: {here.Units:N0} for about {here.Cost:N0} gil"
            : $"Nothing listed on {Worlds.CurrentName}.");
        if (check.Median is { } median) lines.Add($"Recent sales: {median:N0} gil (median, before tax)");
        if (check.Npc is { } npc)
            lines.Add($"{npc.Npc} ({npc.Zone}) sells it for {npc.Price:N0} gil each" + (check.NpcIsCheaper ? ", the better buy" : ""));
        if (check.UploadedAt is { } uploaded) lines.Add($"Universalis data from {Ui.Ago(uploaded)}.");
        return string.Join("\n", lines);
    }

    private void DrawHave(ShoppingItem item, Holding holding, Need? need)
    {
        var target = Math.Max(1, need?.Needed ?? item.Needed);
        ImGui.ProgressBar(Math.Clamp((float)holding.Total / target, 0f, 1f),
            new Vector2(100 * ImGuiHelpers.GlobalScale, ImGui.GetTextLineHeight()), holding.Total.ToString("N0"));
        if (!ImGui.IsItemHovered()) return;
        var lines = holding.Sources.Where(s => s.Quantity > 0 || s.Label == "Bags").Select(s =>
            $"{s.Label}: {s.Quantity:N0}" + (s.SeenAt is { } at ? $" (seen {Ui.Ago(at)})" : "") +
            (s.NotCounted is { } why ? $", not counted: {why}" : ""));
        var hint = plugin.Reader.SaddlebagOpen ? "" : "\nThe saddlebag counts as last seen; open it to refresh.";
        ImGui.SetTooltip((item.HqOnly ? "HQ only.\n" : "Any quality.\n") + string.Join("\n", lines) + hint);
    }

    private void DrawItemEditor(ShoppingList list, ShoppingItem item)
    {
        var info = plugin.Items.Get(item.ItemId);
        Ui.Icon(plugin.Items, item.ItemId, item.HqOnly);
        ImGui.SameLine();
        ImGui.TextUnformatted($"{item.Name}  (#{item.ItemId})");
        ImGui.SameLine();
        if (ImGui.SmallButton("Universalis")) Ui.OpenUrl($"https://universalis.app/market/{item.ItemId}");
        ImGui.SameLine();
        if (ImGui.SmallButton("Close")) selectedItem = null;
        ImGui.SameLine();
        if (!confirmRemove)
        {
            if (Theme.DangerButton("Remove")) confirmRemove = true;
        }
        else
        {
            ImGui.TextColored(Theme.Warning, "Remove it?");
            ImGui.SameLine();
            if (Theme.DangerButton("Yes, remove"))
            {
                list.Items.Remove(item);
                selectedItem = null;
                confirmRemove = false;
                plugin.MarkDirty();
                return;
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("Keep")) confirmRemove = false;
        }

        // One line: Needed: [amount]  Group [dropdown]  (HQ only, for items that can be HQ).
        var scale = ImGuiHelpers.GlobalScale;
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Needed:");
        ImGui.SameLine();
        var needed = item.Needed;
        ImGui.SetNextItemWidth(110 * scale);
        if (ImGui.InputInt("##needed", ref needed))
        {
            item.Needed = Math.Clamp(needed, 1, ShoppingList.MaxNeeded);
            plugin.MarkDirty();
        }

        ImGui.SameLine(0, 16 * scale);
        ImGui.TextUnformatted("Group");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(200 * scale);
        using (var combo = ImRaii.Combo("##group", GroupName(list, item.GroupId)))
        {
            if (combo.Success)
            {
                if (ImGui.Selectable("No group", item.GroupId == null))
                {
                    item.GroupId = null;
                    plugin.MarkDirty();
                }
                foreach (var g in list.Groups.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
                {
                    using var gid = ImRaii.PushId(g.Id.ToString());
                    if (!ImGui.Selectable(g.Name, item.GroupId == g.Id)) continue;
                    item.GroupId = g.Id;
                    plugin.MarkDirty();
                }
            }
        }

        if (info is not { CanBeHq: true }) return;
        ImGui.SameLine(0, 16 * scale);
        var hqOnly = item.HqOnly;
        if (ImGui.Checkbox("HQ only", ref hqOnly))
        {
            item.HqOnly = hqOnly;
            plugin.MarkDirty();
        }
        Ui.Tip("Off: HQ and NQ both count, and prices include both.");
    }

    private void DrawGroupEditor(ShoppingList list, ShoppingGroup group)
    {
        var count = list.Items.Count(i => i.GroupId == group.Id);
        ImGui.TextUnformatted($"Group: {group.Name}");
        ImGui.SameLine();
        ImGui.TextDisabled(group.Source == ShoppingGroup.Teamcraft && group.ImportedAt is { } at
            ? $"{count} items, imported from Teamcraft {Ui.Ago(at)}"
            : $"{count} items");
        ImGui.SameLine();
        if (ImGui.SmallButton("Close")) selectedGroup = null;
        ImGui.SameLine();
        if (!confirmRemove)
        {
            if (Theme.DangerButton("Remove group")) confirmRemove = true;
            Ui.Tip("Removes the group and everything in it.");
        }
        else
        {
            ImGui.TextColored(Theme.Warning, $"Remove \"{group.Name}\" and its {count} items?");
            ImGui.SameLine();
            if (Theme.DangerButton("Yes, remove"))
            {
                list.RemoveGroup(group);
                selectedGroup = null;
                confirmRemove = false;
                plugin.MarkDirty();
                return;
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("Keep")) confirmRemove = false;
        }

        var name = group.Name;
        ImGui.SetNextItemWidth(240 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputText("Name", ref name, 64) && name.Trim().Length > 0)
        {
            group.Name = name.Trim();
            plugin.MarkDirty();
        }
        ImGui.SameLine();
        var active = group.Active;
        if (ImGui.Checkbox("Active", ref active))
        {
            group.Active = active;
            plugin.MarkDirty();
        }
        Ui.Tip("A paused group stays on the list but isn't counted or price checked.");
        if (group.Source == ShoppingGroup.Teamcraft)
        {
            ImGui.SameLine();
            if (ImGui.Button("Update from Teamcraft")) plugin.OpenImport(group.Id);
            Ui.Tip("Paste the list again to update the amounts.");
        }
    }

    private static string GroupName(ShoppingList list, Guid? id) =>
        id is { } g ? list.Groups.FirstOrDefault(x => x.Id == g)?.Name ?? "No group" : "No group";
}
