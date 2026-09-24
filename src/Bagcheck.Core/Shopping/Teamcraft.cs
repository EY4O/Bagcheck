using System.Globalization;
using System.Text.RegularExpressions;

namespace Bagcheck.Core.Shopping;

/// <param name="Section">The Teamcraft panel the line came from, or null for lines before any heading.</param>
public sealed record TeamcraftLine(int Quantity, string Name, string? Section);

/// <summary>
/// Text from Teamcraft's "Copy as text" button: a panel heading (usually ending in " :"), then one "3x Item name" line
/// per item. Several panels can be pasted at once, and an item named twice is added together.
/// </summary>
public sealed record TeamcraftText(IReadOnlyList<TeamcraftLine> Lines, IReadOnlyList<string> Rejected)
{
    private static readonly Regex Row = new(@"^(\d+)\s*x\s+(.+)$", RegexOptions.CultureInvariant);

    public static TeamcraftText Parse(string text)
    {
        var raw = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(l => l.Trim()).ToArray();
        var lines = new List<TeamcraftLine>();
        var rejected = new List<string>();
        var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        string? section = null;

        for (var i = 0; i < raw.Length; i++)
        {
            var line = raw[i];
            if (line.Length == 0) continue;

            var match = Row.Match(line);
            if (match.Success)
            {
                if (!int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var quantity) || quantity < 1)
                {
                    rejected.Add($"{line} (the amount must be a whole number from 1)");
                    continue;
                }
                var name = match.Groups[2].Value.Trim();
                if (byName.TryGetValue(name, out var index))
                    lines[index] = lines[index] with { Quantity = (int)Math.Min((long)lines[index].Quantity + quantity, int.MaxValue) };
                else
                {
                    byName[name] = lines.Count;
                    lines.Add(new(quantity, name, section));
                }
                continue;
            }

            // A heading ends in a colon, or is followed by an item line.
            var next = raw.Skip(i + 1).FirstOrDefault(l => l.Length > 0);
            if (line.EndsWith(':') || (next != null && Row.IsMatch(next)))
            {
                section = line.TrimEnd(':').TrimEnd();
                continue;
            }
            rejected.Add($"{line} (not a \"3x Item name\" line)");
        }
        return new(lines, rejected);
    }
}

public enum ImportStatus { Ready, Craftable, NotMarketable, Unknown, TooMany }

/// <summary>A pasted line matched against the game's items.</summary>
public sealed record ImportRow(string Name, int Quantity, string? Section, uint ItemId, ImportStatus Status)
{
    public bool Importable => Status is ImportStatus.Ready or ImportStatus.Craftable;

    /// <summary>Raw materials are ticked to start with; things you'd normally craft are not.</summary>
    public bool TickedByDefault => Status == ImportStatus.Ready;
}

/// <summary>What the game's item sheet says about a pasted name.</summary>
public sealed record ItemMatch(uint ItemId, string Name, bool Marketable, bool Craftable);

public enum ChangeKind { Add, Update, Unchanged, Remove }

public sealed record ImportChange(ChangeKind Kind, uint ItemId, string Name, int Quantity, ShoppingItem? Existing);

public sealed record ImportOutcome(ShoppingGroup Group, int Added, int Updated, int Unchanged, int Removed);

/// <summary>Turns a Teamcraft paste into a shopping list group, or updates the group it came from.</summary>
public static class TeamcraftImport
{
    public static IReadOnlyList<ImportRow> Resolve(TeamcraftText text, Func<string, ItemMatch?> find) => text.Lines
        .Select(line => find(line.Name) is not { } item
            ? new ImportRow(line.Name, line.Quantity, line.Section, 0, ImportStatus.Unknown)
            : new ImportRow(item.Name, line.Quantity, line.Section, item.ItemId,
                !item.Marketable ? ImportStatus.NotMarketable
                : line.Quantity > ShoppingList.MaxNeeded ? ImportStatus.TooMany
                : item.Craftable ? ImportStatus.Craftable
                : ImportStatus.Ready))
        .ToList();

    /// <summary>
    /// What importing <paramref name="picked"/> would change. Into a new group everything is added. Into an existing
    /// group, entries are matched by item, and entries the paste no longer has are offered for removal.
    /// </summary>
    public static IReadOnlyList<ImportChange> Plan(ShoppingList list, Guid? groupId, IEnumerable<ImportRow> picked)
    {
        var current = groupId is { } id
            ? list.Items.Where(i => i.GroupId == id && !i.HqOnly).GroupBy(i => i.ItemId).ToDictionary(g => g.Key, g => g.First())
            : [];
        var changes = new List<ImportChange>();
        var seen = new HashSet<uint>();
        foreach (var row in picked.Where(r => r.Importable))
        {
            if (!seen.Add(row.ItemId)) continue;
            changes.Add(current.TryGetValue(row.ItemId, out var existing)
                ? new(existing.Needed == row.Quantity ? ChangeKind.Unchanged : ChangeKind.Update, row.ItemId, row.Name, row.Quantity, existing)
                : new(ChangeKind.Add, row.ItemId, row.Name, row.Quantity, null));
        }
        changes.AddRange(current.Values.Where(e => !seen.Contains(e.ItemId))
            .Select(e => new ImportChange(ChangeKind.Remove, e.ItemId, e.Name, e.Needed, e)));
        return changes;
    }

    /// <param name="removeMissing">Remove entries the paste no longer has; otherwise they're left alone.</param>
    public static ImportOutcome Apply(ShoppingList list, Guid? groupId, string name, IReadOnlyList<ImportChange> changes,
        bool removeMissing, DateTimeOffset now)
    {
        var group = groupId is { } id ? list.Groups.FirstOrDefault(g => g.Id == id) : null;
        if (group == null)
        {
            group = new ShoppingGroup { Name = name.Trim(), Source = ShoppingGroup.Teamcraft };
            list.Groups.Add(group);
        }
        group.ImportedAt = now;

        int added = 0, updated = 0, unchanged = 0, removed = 0;
        foreach (var change in changes)
        {
            switch (change.Kind)
            {
                case ChangeKind.Add:
                    list.Items.Add(new ShoppingItem { ItemId = change.ItemId, Name = change.Name, Needed = change.Quantity, GroupId = group.Id });
                    added++;
                    break;
                case ChangeKind.Update:
                    change.Existing!.Needed = change.Quantity;
                    updated++;
                    break;
                case ChangeKind.Unchanged:
                    unchanged++;
                    break;
                case ChangeKind.Remove when removeMissing:
                    list.Items.Remove(change.Existing!);
                    removed++;
                    break;
            }
        }
        return new(group, added, updated, unchanged, removed);
    }
}
