using System.Text.Json;
using Bagcheck.Core.Shopping;

namespace Bagcheck.Core.Stock;

public sealed record StockItem(uint ItemId, bool Hq, long Quantity)
{
    public bool Matches(ItemKey key) => ItemId == key.ItemId && (Hq || !key.HqOnly);
}

/// <summary>What the saddlebag (owner 0) or one retainer held when last seen. Market listings are not included.</summary>
public sealed record StockSnapshot(ulong CharacterId, string Kind, ulong OwnerId, string Name, DateTimeOffset At, StockItem[] Items)
{
    public const string Saddlebag = "Saddlebag";
    public const string Retainer = "Retainer";
}

/// <summary>Saddlebag and retainer contents per character as last seen, kept in stock.json.</summary>
public sealed class StockStore
{
    private static readonly TimeSpan KeepFor = TimeSpan.FromDays(60);

    // The saddlebag is read every second while it's open; an unchanged reading only needs saving now and then.
    private static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(10);

    private readonly string path;
    private readonly Func<DateTimeOffset> clock;
    private readonly List<StockSnapshot> snapshots = [];
    private readonly Dictionary<(ulong, string, ulong), DateTimeOffset> saved = [];
    private bool canWrite = true;

    private sealed record Document(int Version, List<StockSnapshot> Snapshots);

    public string? Error { get; private set; }
    public bool Dirty { get; private set; }

    public StockStore(string path, Func<DateTimeOffset>? clock = null)
    {
        this.path = path;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        if (!File.Exists(path)) return;
        try
        {
            var document = JsonSerializer.Deserialize<Document>(File.ReadAllText(path));
            if (document is not { Version: 1, Snapshots: not null } || document.Snapshots.Any(s =>
                    s is null || s.CharacterId == 0 || s.Items is null || s.Kind is not (StockSnapshot.Saddlebag or StockSnapshot.Retainer)))
                throw new JsonException("The file is not a Bagcheck stock file.");
            var cutoff = this.clock() - KeepFor;
            snapshots.AddRange(document.Snapshots.Where(s => s.At >= cutoff));
            foreach (var s in snapshots) saved[Key(s)] = s.At;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            canWrite = false;
            Error = "Saved saddlebag and retainer contents could not be read, so they won't be saved this session: " + ex.Message;
        }
    }

    public IReadOnlyList<StockSnapshot> For(ulong characterId) => snapshots.Where(s => s.CharacterId == characterId).ToList();

    /// <summary>Replaces the last reading of the same saddlebag or retainer.</summary>
    public void Record(StockSnapshot snapshot)
    {
        var key = Key(snapshot);
        var index = snapshots.FindIndex(s => Key(s) == key);
        var changed = index < 0 || snapshots[index].Name != snapshot.Name || !Same(snapshots[index].Items, snapshot.Items);
        if (index < 0) snapshots.Add(snapshot);
        else snapshots[index] = snapshot;
        if (changed || !saved.TryGetValue(key, out var at) || snapshot.At - at >= RefreshEvery) Dirty = true;
    }

    /// <summary>Forgets a retainer that has been dismissed.</summary>
    public void Forget(ulong characterId, IReadOnlySet<ulong> keepRetainers)
    {
        if (snapshots.RemoveAll(s => s.CharacterId == characterId && s.Kind == StockSnapshot.Retainer &&
                                     !keepRetainers.Contains(s.OwnerId)) > 0) Dirty = true;
    }

    public void Save()
    {
        if (!canWrite) { Dirty = false; return; }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new Document(1, snapshots)));
            File.Move(path + ".tmp", path, true);
            foreach (var s in snapshots) saved[Key(s)] = s.At;
            Error = null;
            Dirty = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = "Couldn't save saddlebag and retainer contents: " + ex.Message;
        }
    }

    private static (ulong, string, ulong) Key(StockSnapshot s) => (s.CharacterId, s.Kind, s.OwnerId);

    private static bool Same(StockItem[] a, StockItem[] b) =>
        a.Length == b.Length &&
        a.OrderBy(i => i.ItemId).ThenBy(i => i.Hq).SequenceEqual(b.OrderBy(i => i.ItemId).ThenBy(i => i.Hq));
}

/// <param name="NotCounted">Why this source is shown but left out of the total, or null when it counts.</param>
public sealed record StockSource(string Label, long Quantity, DateTimeOffset? SeenAt, string? NotCounted);

public sealed record Holding(IReadOnlyList<StockSource> Sources)
{
    public long Total => Sources.Where(s => s.NotCounted == null).Sum(s => s.Quantity);
}

public sealed record StockOptions(bool CountSaddlebag, bool CountRetainers, int RetainerMaxAgeHours);

/// <summary>
/// How many of an item you hold. Bags and crystals are live. The saddlebag and retainers are as last seen, and
/// retainer contents older than the chosen age are listed but not counted, since they may have changed since.
/// </summary>
public static class StockCount
{
    public static Holding Of(ItemKey key, IEnumerable<StockItem> bags, IEnumerable<StockItem> crystals,
        IEnumerable<StockSnapshot> stored, StockOptions options, DateTimeOffset now)
    {
        var sources = new List<StockSource> { new("Bags", bags.Where(i => i.Matches(key)).Sum(i => i.Quantity), null, null) };
        var crystal = crystals.Where(i => i.Matches(key)).Sum(i => i.Quantity);
        if (crystal > 0) sources.Add(new("Crystals", crystal, null, null));

        var maxAge = TimeSpan.FromHours(Math.Max(1, options.RetainerMaxAgeHours));
        foreach (var snapshot in stored.OrderBy(s => s.Kind == StockSnapshot.Saddlebag ? 0 : 1).ThenBy(s => s.Name))
        {
            var quantity = snapshot.Items.Where(i => i.Matches(key)).Sum(i => i.Quantity);
            if (quantity <= 0) continue;
            if (snapshot.Kind == StockSnapshot.Saddlebag)
            {
                sources.Add(new("Saddlebag", quantity, snapshot.At, options.CountSaddlebag ? null : "saddlebag not counted (Settings)"));
                continue;
            }
            var why = !options.CountRetainers ? "retainers not counted (Settings)"
                : now - snapshot.At > maxAge ? $"seen over {maxAge.TotalHours:0} h ago; visit the retainer to refresh"
                : null;
            sources.Add(new(snapshot.Name, quantity, snapshot.At, why));
        }
        return new(sources);
    }
}
