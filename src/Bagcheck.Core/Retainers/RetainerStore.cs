using System.Text.Json;

namespace Bagcheck.Core.Retainers;

public sealed record RetainerListing(uint ItemId, string Name, bool Hq, int Quantity, ulong UnitPrice, uint Slot);

/// <summary>One retainer as last seen. <see cref="Listings"/> is null until its market has been opened once.</summary>
public sealed record RetainerRecord(
    ulong CharacterId,
    ulong RetainerId,
    string Name,
    int Order,
    bool Available,
    uint Gil,
    int MarketCount,
    DateTimeOffset SeenAt,
    RetainerListing[]? Listings = null,
    DateTimeOffset? ListingsSeenAt = null);

/// <summary>Every character's retainers as last seen, kept in retainers.json.</summary>
public sealed class RetainerStore
{
    private static readonly TimeSpan KeepFor = TimeSpan.FromDays(60);

    private readonly string path;
    private readonly Func<DateTimeOffset> clock;
    private readonly List<RetainerRecord> records = [];
    private bool canWrite = true;

    private sealed record Document(int Version, List<RetainerRecord> Retainers);

    public string? Error { get; private set; }
    public bool Dirty { get; private set; }

    public RetainerStore(string path, Func<DateTimeOffset>? clock = null)
    {
        this.path = path;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        if (!File.Exists(path)) return;
        try
        {
            var document = JsonSerializer.Deserialize<Document>(File.ReadAllText(path));
            if (document is not { Version: 1, Retainers: not null } ||
                document.Retainers.Any(r => r is null || r.CharacterId == 0 || r.RetainerId == 0 || string.IsNullOrWhiteSpace(r.Name)))
                throw new JsonException("The file is not a Bagcheck retainer list.");
            var cutoff = this.clock() - KeepFor;
            records.AddRange(document.Retainers.Where(r => r.SeenAt >= cutoff));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Leave a file we can't read alone rather than overwrite it.
            canWrite = false;
            Error = "Saved retainers could not be read, so they won't be saved this session: " + ex.Message;
        }
    }

    public IReadOnlyList<RetainerRecord> For(ulong characterId) =>
        records.Where(r => r.CharacterId == characterId).OrderBy(r => r.Order).ToList();

    /// <summary>Names, order, gil and market counts from the game's retainer list. Dismissed retainers are dropped.</summary>
    public void UpdateRoster(ulong characterId,
        IReadOnlyList<(ulong Id, string Name, bool Available, uint Gil, int MarketCount)> roster)
    {
        if (characterId == 0 || roster.Count == 0) return;
        var now = clock();
        for (var order = 0; order < roster.Count; order++)
        {
            var (id, name, available, gil, count) = roster[order];
            var index = records.FindIndex(r => r.CharacterId == characterId && r.RetainerId == id);
            if (index < 0)
            {
                records.Add(new(characterId, id, name, order, available, gil, count, now));
                Dirty = true;
                continue;
            }
            var old = records[index];
            if (old.Name != name || old.Order != order || old.Available != available || old.Gil != gil || old.MarketCount != count)
                Dirty = true;
            records[index] = old with { Name = name, Order = order, Available = available, Gil = gil, MarketCount = count, SeenAt = now };
        }
        var ids = roster.Select(r => r.Id).ToHashSet();
        if (records.RemoveAll(r => r.CharacterId == characterId && !ids.Contains(r.RetainerId)) > 0) Dirty = true;
    }

    /// <summary>The listings of a retainer whose market was just read.</summary>
    public void UpdateListings(ulong characterId, ulong retainerId, string name, RetainerListing[] listings)
    {
        if (characterId == 0 || retainerId == 0) return;
        var now = clock();
        var index = records.FindIndex(r => r.CharacterId == characterId && r.RetainerId == retainerId);
        if (index < 0)
        {
            var order = records.Where(r => r.CharacterId == characterId).Select(r => r.Order + 1).DefaultIfEmpty(0).Max();
            records.Add(new(characterId, retainerId, name, order, true, 0, listings.Length, now, listings, now));
            Dirty = true;
            return;
        }
        var old = records[index];
        if (old.Listings == null || !old.Listings.SequenceEqual(listings)) Dirty = true;
        records[index] = old with { Listings = listings, ListingsSeenAt = now };
    }

    public void Save()
    {
        if (!canWrite) { Dirty = false; return; }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new Document(1, records)));
            File.Move(path + ".tmp", path, true);
            Error = null;
            Dirty = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = "Couldn't save retainers: " + ex.Message;
        }
    }
}
