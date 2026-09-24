using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Bagcheck.Core.Shopping;

namespace Bagcheck.Windows;

/// <summary>Paste a Teamcraft list, check it, then add it to the shopping list as a group.</summary>
public sealed class ImportWindow : ThemedWindow
{
    private readonly Plugin plugin;
    private readonly HashSet<int> ticked = [];
    private string text = "";
    private string parsedText = "";
    private string groupName = "";
    private bool removeMissing;
    private Guid? targetGroup;
    private TeamcraftText parsed = new([], []);
    private IReadOnlyList<ImportRow> rows = [];

    public ImportWindow(Plugin plugin) : base("Import from Teamcraft###BagcheckImport", ImGuiWindowFlags.NoCollapse)
    {
        this.plugin = plugin;
        Size = new Vector2(620, 520);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(540, 400), MaximumSize = new Vector2(1100, 1400) };
    }

    /// <param name="groupId">A group imported earlier, to update it; null for a new group.</param>
    public void Open(Guid? groupId)
    {
        targetGroup = groupId;
        groupName = groupId is { } id ? plugin.Configuration.List.Groups.FirstOrDefault(g => g.Id == id)?.Name ?? "" : "";
        text = parsedText = "";
        parsed = new([], []);
        rows = [];
        ticked.Clear();
        removeMissing = false;
        IsOpen = true;
    }

    public override void Draw()
    {
        var list = plugin.Configuration.List;
        if (targetGroup is { } id && list.Groups.All(g => g.Id != id)) targetGroup = null;
        var target = targetGroup is { } gid ? list.Groups.First(g => g.Id == gid) : null;

        ImGui.TextWrapped("In Teamcraft, open your list and press \"Copy as text\" on a panel (the copy icon in its header), " +
                          "then paste it here. You can paste several panels at once.");
        if (target != null)
        {
            ImGui.TextUnformatted($"Updating \"{target.Name}\".");
            ImGui.SameLine();
            if (ImGui.SmallButton("Make a new group instead"))
            {
                targetGroup = null;
                groupName = "";
            }
        }
        else
        {
            ImGui.SetNextItemWidth(260 * ImGuiHelpers.GlobalScale);
            ImGui.InputTextWithHint("Group name", "e.g. Grade 8 tinctures", ref groupName, 64);
        }

        if (ImGui.Button("Paste")) text = ImGui.GetClipboardText() ?? "";
        ImGui.SameLine();
        if (ImGui.Button("Clear")) text = "";
        ImGui.InputTextMultiline("##text", ref text, 65536, new Vector2(-1, 110 * ImGuiHelpers.GlobalScale));
        if (text != parsedText) Reparse();

        if (rows.Count == 0 && parsed.Rejected.Count == 0)
        {
            ImGui.TextDisabled("Nothing pasted yet.");
            return;
        }

        var counts = rows.GroupBy(r => r.Status).ToDictionary(g => g.Key, g => g.Count());
        ImGui.TextWrapped($"{rows.Count} items: {counts.GetValueOrDefault(ImportStatus.Ready)} raw materials, " +
                          $"{counts.GetValueOrDefault(ImportStatus.Craftable)} you can craft (not ticked), " +
                          $"{counts.GetValueOrDefault(ImportStatus.NotMarketable)} not on the market, " +
                          $"{counts.GetValueOrDefault(ImportStatus.Unknown)} not recognised" +
                          (parsed.Rejected.Count > 0 ? $"; {parsed.Rejected.Count} lines skipped." : "."));

        var changes = TeamcraftImport.Plan(list, targetGroup, rows.Where((_, i) => ticked.Contains(i)));
        var byItem = changes.Where(c => c.Kind != ChangeKind.Remove).ToDictionary(c => c.ItemId);
        var removals = changes.Where(c => c.Kind == ChangeKind.Remove).ToList();
        var footer = ImGui.GetFrameHeightWithSpacing() * (2 + (removals.Count > 0 ? 1 : 0) + (parsed.Rejected.Count > 0 ? 1 : 0));
        using (var child = ImRaii.Child("##preview", new Vector2(0, -footer)))
        {
            if (child.Success) DrawPreview(byItem);
        }

        if (parsed.Rejected.Count > 0)
        {
            using var node = ImRaii.TreeNode($"Skipped lines ({parsed.Rejected.Count})###skipped");
            if (node.Success)
                foreach (var line in parsed.Rejected) ImGui.TextDisabled(line);
        }
        if (removals.Count > 0)
        {
            ImGui.Checkbox($"Remove {removals.Count} item{(removals.Count == 1 ? "" : "s")} no longer on this list", ref removeMissing);
            Ui.Tip(string.Join(", ", removals.Select(r => r.Name)));
        }

        var blocked = changes.All(c => c.Kind == ChangeKind.Remove) && !(removeMissing && removals.Count > 0) ? "Tick at least one item."
            : target == null && groupName.Trim().Length == 0 ? "Give the group a name first."
            : null;
        using (ImRaii.Disabled(blocked != null))
        {
            if (Theme.PrimaryButton(target == null ? "Add to list" : "Update group")) Import(changes);
        }
        if (blocked != null) Ui.TipAlways(blocked);
        ImGui.SameLine();
        ImGui.TextDisabled("Bagcheck compares these amounts with what you already hold.");
    }

    private void DrawPreview(Dictionary<uint, ImportChange> byItem)
    {
        using var table = ImRaii.Table("##rows", 5,
            ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingFixedFit);
        if (!table.Success) return;
        var scale = ImGuiHelpers.GlobalScale;
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("##tick", ImGuiTableColumnFlags.WidthFixed, 28 * scale);
        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Amount", ImGuiTableColumnFlags.WidthFixed, 60 * scale);
        ImGui.TableSetupColumn("Kind", ImGuiTableColumnFlags.WidthFixed, 110 * scale);
        ImGui.TableSetupColumn("Change", ImGuiTableColumnFlags.WidthFixed, 90 * scale);
        ImGui.TableHeadersRow();

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            using var id = ImRaii.PushId(i);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            using (ImRaii.Disabled(!row.Importable))
            {
                var on = ticked.Contains(i);
                if (ImGui.Checkbox("##tick", ref on))
                {
                    if (on) ticked.Add(i);
                    else ticked.Remove(i);
                }
            }
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(row.Name);
            if (row.Section != null) Ui.Tip(row.Section);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(row.Quantity.ToString("N0"));
            ImGui.TableNextColumn();
            var (label, tone, tip) = row.Status switch
            {
                ImportStatus.Ready => ("Raw material", Tone.Good, "No recipe makes this, so it's ticked."),
                ImportStatus.Craftable => ("Craftable", Tone.Neutral, "You could craft this, so it isn't ticked. Tick it to buy it instead."),
                ImportStatus.NotMarketable => ("Not on market", Tone.Bad, "This can't be bought on the market board."),
                ImportStatus.TooMany => ("Too many", Tone.Bad, $"An entry holds at most {ShoppingList.MaxNeeded:N0}."),
                _ => ("Not recognised", Tone.Bad, "No item has exactly this name. Teamcraft set to another language won't match."),
            };
            Theme.Pill(label, tone);
            Ui.Tip(tip);
            ImGui.TableNextColumn();
            if (!ticked.Contains(i) || !byItem.TryGetValue(row.ItemId, out var change))
            {
                ImGui.TextDisabled("-");
                continue;
            }
            switch (change.Kind)
            {
                case ChangeKind.Add: ImGui.TextUnformatted("New"); break;
                case ChangeKind.Update: ImGui.TextUnformatted($"{change.Existing!.Needed:N0} > {change.Quantity:N0}"); break;
                default: ImGui.TextDisabled("Same"); break;
            }
        }
    }

    private void Reparse()
    {
        parsedText = text;
        parsed = TeamcraftText.Parse(text);
        rows = TeamcraftImport.Resolve(parsed, name => plugin.Items.FindExact(name) is { } info
            ? new ItemMatch(info.Id, info.Name, info.IsMarketable, plugin.Items.IsCraftable(info.Id))
            : null);
        ticked.Clear();
        for (var i = 0; i < rows.Count; i++)
            if (rows[i].TickedByDefault) ticked.Add(i);
    }

    private void Import(IReadOnlyList<ImportChange> changes)
    {
        var outcome = TeamcraftImport.Apply(plugin.Configuration.List, targetGroup, groupName, changes, removeMissing, DateTimeOffset.UtcNow);
        plugin.MarkDirty();
        Plugin.ChatGui.Print($"\"{outcome.Group.Name}\": {outcome.Added} added, {outcome.Updated} updated, " +
                             $"{outcome.Unchanged} unchanged, {outcome.Removed} removed.", "Bagcheck");
        IsOpen = false;
    }
}
