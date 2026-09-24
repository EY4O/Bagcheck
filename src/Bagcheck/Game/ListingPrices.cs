using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagcheck.Core.Market;

namespace Bagcheck.Game;

/// <summary>Home-world market data for the items your retainers have listed, fetched when you ask for it.</summary>
public sealed class ListingPrices(MarketService market) : IDisposable
{
    private readonly Dictionary<(string World, uint Item, bool Hq), MarketSnapshot> results = [];
    private CancellationTokenSource? cancel;
    private Task<List<MarketSnapshot>>? pending;
    private string pendingWorld = "";
    private IReadOnlyList<(uint Item, bool Hq)> pendingKeys = [];

    public bool Checking => pending != null;
    public DateTimeOffset? CheckedAt { get; private set; }
    public string? Error { get; private set; }

    public MarketSnapshot? For(string world, uint itemId, bool hq) => results.GetValueOrDefault((world, itemId, hq));

    public void Check(IEnumerable<(uint Item, bool Hq)> keys, string world)
    {
        if (Checking || world.Length == 0) return;
        pendingKeys = keys.Distinct().ToList();
        if (pendingKeys.Count == 0) return;
        pendingWorld = world;
        Error = null;
        cancel = new CancellationTokenSource();
        var token = cancel.Token;
        var todo = pendingKeys;
        pending = Task.Run(async () =>
        {
            var found = new List<MarketSnapshot>();
            foreach (var (item, hq) in todo)
                found.Add(await market.Client.GetAsync(item, hq, world, 10, token).ConfigureAwait(false));
            return found;
        }, token);
    }

    /// <summary>Called every frame; picks up a finished check on the game thread.</summary>
    public void Update()
    {
        if (pending is not { IsCompleted: true } done) return;
        try
        {
            var found = done.GetAwaiter().GetResult();
            for (var i = 0; i < found.Count; i++)
                results[(pendingWorld, pendingKeys[i].Item, pendingKeys[i].Hq)] = found[i];
            CheckedAt = DateTimeOffset.UtcNow;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Error = "Price check failed: " + ex.GetBaseException().Message;
        }
        pending = null;
        cancel?.Dispose();
        cancel = null;
    }

    public void Dispose()
    {
        cancel?.Cancel();
        // Let a cancelled request finish on its own; don't touch it after unload.
        if (pending != null) _ = pending.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);
    }
}
