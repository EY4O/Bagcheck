using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagcheck.Core.Shopping;

namespace Bagcheck.Game;

/// <summary>
/// Checks data-centre prices for everything the shopping list still needs, when asked. Only reads prices from
/// Universalis; nothing in game is touched.
/// </summary>
public sealed class PriceChecker(Plugin plugin) : IDisposable
{
    private sealed record Job(ItemKey Key, string Name, long Wanted, uint Target, NpcOffer? Npc);

    private readonly ConcurrentDictionary<ItemKey, PriceCheck> results = new();
    private readonly ConcurrentQueue<string> failures = new();
    private CancellationTokenSource? cancel;
    private Task? running;
    private int total;
    private int done;

    public bool Running => running is { IsCompleted: false };
    public int Total => total;
    public int Done => done;
    public DateTimeOffset? CheckedAt { get; private set; }
    public string DataCentre { get; private set; } = "";
    public IReadOnlyCollection<string> Failures => failures.ToArray();

    public PriceCheck? For(ItemKey key) => results.GetValueOrDefault(key);
    public IEnumerable<PriceCheck> All => results.Values;

    /// <summary>Starts a check. Everything read from the game is read here, on the game thread.</summary>
    public void Start(IReadOnlyCollection<Need> needs)
    {
        if (Running) return;
        var scope = Worlds.DataCentre;
        if (scope.Length == 0) return;

        var jobs = needs.Where(n => !n.Done)
            .Select(n => new Job(n.Key, n.Name, n.Short, n.TargetPrice, Npc(n.Key.ItemId)))
            .ToList();
        results.Clear();
        failures.Clear();
        total = jobs.Count;
        done = 0;
        DataCentre = scope;
        CheckedAt = null;
        if (jobs.Count == 0) return;

        var world = (int)Worlds.Current;
        cancel = new CancellationTokenSource();
        var token = cancel.Token;
        running = Task.Run(async () =>
        {
            foreach (var job in jobs)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var market = await plugin.Market.Client
                        .GetAsync(job.Key.ItemId, job.Key.HqOnly ? true : null, scope, 20, token).ConfigureAwait(false);
                    results[job.Key] = Prices.Check(job.Key, job.Wanted, job.Target, market, world, job.Npc);
                }
                catch (Exception ex) when (!token.IsCancellationRequested)
                {
                    failures.Enqueue($"{job.Name}: {ex.GetBaseException().Message}");
                }
                Interlocked.Increment(ref done);
            }
            CheckedAt = DateTimeOffset.UtcNow;
        }, token);
    }

    private NpcOffer? Npc(uint itemId)
    {
        var offer = plugin.Vendors.Index.For(itemId).FirstOrDefault();
        return offer == null ? null : new NpcOffer(offer.Npc, offer.Place.Zone, offer.Price);
    }

    public void Dispose()
    {
        cancel?.Cancel();
        if (running != null) _ = running.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);
    }
}
