using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Bagcheck.Core.Retainers;
using Bagcheck.Game;

namespace Bagcheck.Windows;

/// <summary>Each retainer's gil and market listings, as last seen, and how the listings compare.</summary>
public sealed class RetainersTab(Plugin plugin)
{
    private readonly HashSet<ulong> expanded = [];
    private string filter = "";
    private bool showInactive = true;

    public void Draw()
    {
        var reader = plugin.Reader;
        if (reader.Problem is { } problem) ImGui.TextColored(Theme.Bad, problem);
        if (reader.Retainers.Error is { } error) ImGui.TextColored(Theme.Bad, error);
        if (plugin.CharacterId == 0)
        {
            ImGui.TextDisabled("Log in to see your retainers.");
            return;
        }

        var records = reader.Retainers.For(plugin.CharacterId);
        if (records.Count == 0)
        {
            ImGui.TextWrapped("No retainers yet. Open a summoning bell and Bagcheck will note your retainers and their gil. " +
                              "Open a retainer's market to see what it has listed.");
            return;
        }

        var totals = RetainerView.Totals(records);
        ImGui.TextUnformatted($"{totals.Gil:N0} gil   ·   {totals.Listed} of {totals.Slots} market slots in use   ·   " +
                              $"{totals.Active} of {totals.Total} retainers active");
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        Ui.Tip("Updated whenever you open a summoning bell, and each retainer's listings when you open its market.\n" +
               "Bagcheck only reads what you have open; it never opens anything itself.");

        DrawToolbar(records);
        ImGui.Separator();

        var own = records.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var world = Worlds.HomeName;
        var scale = ImGuiHelpers.GlobalScale;
        float gilColumn = 300 * scale, listedColumn = 430 * scale;
        ImGui.TextDisabled("Retainer");
        ImGui.SameLine(gilColumn);
        ImGui.TextDisabled("Gil");
        ImGui.SameLine(listedColumn);
        ImGui.TextDisabled("Market");

        using var child = ImRaii.Child("##retainers");
        if (!child.Success) return;
        foreach (var (record, listingMatched) in RetainerView.Filter(records, filter, showInactive))
        {
            using var id = ImRaii.PushId(record.RetainerId.ToString());
            if (!record.Available)
            {
                ImGui.TextDisabled($"      {record.Name} (inactive)");
                ImGui.SameLine(gilColumn);
                ImGui.TextDisabled(record.Gil.ToString("N0"));
                continue;
            }

            ImGui.SetNextItemOpen(listingMatched || expanded.Contains(record.RetainerId), ImGuiCond.Always);
            var open = ImGui.TreeNodeEx("##retainer", ImGuiTreeNodeFlags.SpanFullWidth, record.Name);
            if (ImGui.IsItemClicked() && !listingMatched)
            {
                if (!expanded.Remove(record.RetainerId)) expanded.Add(record.RetainerId);
            }
            Ui.Tip(record.ListingsSeenAt is { } seen
                ? $"Listings seen {Ui.Ago(seen)}."
                : "Listings not seen yet: open this retainer's market once.");

            ImGui.SameLine(gilColumn);
            ImGui.TextUnformatted(record.Gil.ToString("N0"));
            ImGui.SameLine(listedColumn);
            var count = record.Listings?.Length ?? record.MarketCount;
            ImGui.ProgressBar(count / 20f, new Vector2(130 * scale, ImGui.GetTextLineHeight()), $"{count} / 20");

            if (!open) continue;
            if (record.Listings == null) ImGui.TextDisabled("Open this retainer's market to see its listings.");
            else if (record.Listings.Length == 0) ImGui.TextDisabled("Nothing listed.");
            else DrawListings(record, own, world);
            ImGui.TreePop();
        }
    }

    private void DrawToolbar(IReadOnlyList<RetainerRecord> records)
    {
        var prices = plugin.ListingPrices;
        var world = Worlds.HomeName;
        var keys = records.Where(r => r.Available).SelectMany(r => r.Listings ?? []).Select(l => (l.ItemId, l.Hq)).ToList();
        using (ImRaii.Disabled(prices.Checking || keys.Count == 0 || world.Length == 0))
        {
            if (Theme.PrimaryButton("Check prices")) prices.Check(keys, world);
        }
        Ui.TipAlways(keys.Count == 0
            ? "Nothing listed yet. Open your retainers' markets first."
            : $"Ask Universalis for the current listings on {world} of everything your retainers are selling.");
        ImGui.SameLine();
        if (prices.Error is { } error) ImGui.TextColored(Theme.Bad, error);
        else ImGui.TextDisabled(prices.Checking ? "checking..." : prices.CheckedAt is { } at ? $"checked {Ui.Ago(at)}" : "not checked");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(220 * ImGuiHelpers.GlobalScale);
        ImGui.InputTextWithHint("##retainerFilter", "Find a retainer or item", ref filter, 64);
        ImGui.SameLine();
        ImGui.Checkbox("Show inactive", ref showInactive);
        ImGui.SameLine();
        if (ImGui.SmallButton("Expand all")) foreach (var r in records) expanded.Add(r.RetainerId);
        ImGui.SameLine();
        if (ImGui.SmallButton("Collapse all")) expanded.Clear();
    }

    private void DrawListings(RetainerRecord record, IReadOnlySet<string> own, string world)
    {
        using var table = ImRaii.Table("##listings", 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingFixedFit);
        if (!table.Success) return;
        var scale = ImGuiHelpers.GlobalScale;
        ImGui.TableSetupColumn("##icon", ImGuiTableColumnFlags.WidthFixed, 24 * scale);
        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 50 * scale);
        ImGui.TableSetupColumn("Price", ImGuiTableColumnFlags.WidthFixed, 90 * scale);
        ImGui.TableSetupColumn("Total", ImGuiTableColumnFlags.WidthFixed, 100 * scale);
        ImGui.TableSetupColumn("Market", ImGuiTableColumnFlags.WidthFixed, 150 * scale);
        ImGui.TableHeadersRow();

        foreach (var listing in record.Listings!)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            Ui.Icon(plugin.Items, listing.ItemId, listing.Hq, 20);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(listing.Hq ? listing.Name + " (HQ)" : listing.Name);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(listing.Quantity.ToString("N0"));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(listing.UnitPrice.ToString("N0"));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted((listing.UnitPrice * (ulong)listing.Quantity).ToString("N0"));
            ImGui.TableNextColumn();

            var market = plugin.ListingPrices.For(world, listing.ItemId, listing.Hq);
            var comparison = RetainerView.Compare(listing, market, own);
            var tone = comparison.Standing switch
            {
                Standing.Lowest => Tone.Good,
                Standing.Undercut => Tone.Warning,
                _ => Tone.Neutral,
            };
            Theme.Pill(comparison.ToString(), tone);
            Ui.Tip(market == null
                ? "Press Check prices to compare."
                : comparison.Cheapest is { } cheapest
                    ? $"Cheapest other {(listing.Hq ? "HQ" : "NQ")} listing on {world}: {cheapest:N0} gil. Checked {Ui.Ago(market.FetchedAt)}."
                    : $"No other {(listing.Hq ? "HQ" : "NQ")} listings on {world}. Checked {Ui.Ago(market.FetchedAt)}.");
        }
    }
}
