using Bagcheck.Core.Market;

namespace Bagcheck.Core.Retainers;

public enum Standing { Unknown, Lowest, Undercut }

/// <summary>How one listing compares with the cheapest listing that isn't yours.</summary>
public sealed record Comparison(Standing Standing, ulong By, int? Cheapest)
{
    public override string ToString() => Standing switch
    {
        Standing.Lowest => "Lowest",
        Standing.Undercut => $"Undercut by {By:N0}",
        _ => "Not checked",
    };
}

public sealed record RetainerTotals(int Active, int Total, int Listed, ulong Gil)
{
    public int Slots => Active * 20;
}

/// <summary>Sorting, filtering and totals for the Retainers tab.</summary>
public static class RetainerView
{
    public static RetainerTotals Totals(IReadOnlyCollection<RetainerRecord> records) => new(
        records.Count(r => r.Available),
        records.Count,
        records.Where(r => r.Available).Sum(r => r.Listings?.Length ?? r.MarketCount),
        (ulong)records.Sum(r => (long)r.Gil));

    /// <summary>
    /// Retainers whose name, or any listed item's name or id, contains <paramref name="filter"/>. The flag says the
    /// match was on a listing, so the retainer can be shown expanded.
    /// </summary>
    public static IReadOnlyList<(RetainerRecord Record, bool ListingMatched)> Filter(IEnumerable<RetainerRecord> records,
        string filter, bool includeInactive)
    {
        var result = new List<(RetainerRecord, bool)>();
        foreach (var record in records)
        {
            if (!record.Available && !includeInactive) continue;
            if (filter.Length == 0) { result.Add((record, false)); continue; }
            var listing = record.Listings?.Any(l => l.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                                                    l.ItemId.ToString() == filter) == true;
            if (listing || record.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)) result.Add((record, listing));
        }
        return result;
    }

    /// <summary>
    /// Compares a listing with the market. Listings from any of <paramref name="ownRetainers"/> are ignored, so your
    /// own retainers never count as competition.
    /// </summary>
    public static Comparison Compare(RetainerListing listing, MarketSnapshot? market, IReadOnlySet<string> ownRetainers)
    {
        if (market == null) return new(Standing.Unknown, 0, null);
        var cheapest = (market.Data.Listings ?? [])
            .Where(l => l.Hq == listing.Hq && l.PricePerUnit > 0 && !ownRetainers.Contains(l.RetainerName ?? ""))
            .Select(l => (int?)l.PricePerUnit)
            .Min();
        if (cheapest is not { } price || listing.UnitPrice <= (ulong)price) return new(Standing.Lowest, 0, cheapest);
        return new(Standing.Undercut, listing.UnitPrice - (ulong)price, cheapest);
    }
}
