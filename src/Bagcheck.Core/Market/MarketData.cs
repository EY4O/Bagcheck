using System.Text.Json.Serialization;

namespace Bagcheck.Core.Market;

// Field names follow the Universalis v2 schema: https://docs.universalis.app/api/schema/v2
public sealed record MarketListing(
    [property: JsonRequired] int PricePerUnit,
    [property: JsonRequired] int Quantity,
    [property: JsonRequired] bool Hq,
    int? WorldID = null,
    string? RetainerName = null);

public sealed record MarketSale(
    [property: JsonRequired] int PricePerUnit,
    [property: JsonRequired] int Quantity,
    [property: JsonRequired] bool Hq,
    [property: JsonRequired] long Timestamp);

public sealed record MarketData
{
    [JsonRequired] public uint ItemID { get; init; }
    [JsonRequired] public long LastUploadTime { get; init; }
    public MarketListing[]? Listings { get; init; }
    public MarketSale[]? RecentHistory { get; init; }
}

/// <param name="Scope">The world or data centre the data was asked for.</param>
public sealed record MarketSnapshot(string Scope, DateTimeOffset FetchedAt, MarketData Data)
{
    public DateTimeOffset? UploadedAt => Data.LastUploadTime > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(Data.LastUploadTime) : null;
}
