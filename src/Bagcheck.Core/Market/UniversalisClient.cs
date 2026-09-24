using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Bagcheck.Core.Market;

/// <summary>
/// Reads market data from Universalis. Requests go out one at a time and at most one per second, retries included,
/// and answers are cached for two minutes. The caller owns the <see cref="HttpClient"/>.
/// </summary>
public sealed class UniversalisClient(
    HttpClient http,
    string userAgent,
    Func<DateTimeOffset>? clock = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(2);

    private readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly Func<TimeSpan, CancellationToken, Task> wait = delay ?? Task.Delay;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, MarketSnapshot> cache = [];
    private DateTimeOffset nextRequest;

    /// <param name="scope">A world, data centre or region name, as Universalis spells them.</param>
    /// <param name="hq">True for HQ only, false for NQ only, null for both.</param>
    public async Task<MarketSnapshot> GetAsync(uint itemId, bool? hq, string scope, int historyCount = 20,
        CancellationToken cancellationToken = default)
    {
        if (itemId == 0) throw new ArgumentOutOfRangeException(nameof(itemId));
        if (historyCount is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(historyCount));
        if (string.IsNullOrWhiteSpace(scope) || scope.Length > 64 || scope.Any(char.IsControl))
            throw new ArgumentException("Expected a world or data centre name.", nameof(scope));

        var path = $"{Uri.EscapeDataString(scope)}/{itemId}?entries={historyCount}" +
                   (hq is { } q ? $"&hq={q.ToString().ToLowerInvariant()}" : "");
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (cache.TryGetValue(path, out var cached) && now() - cached.FetchedAt < CacheLifetime)
                return cached;

            for (var attempt = 0; ; attempt++)
            {
                var pause = nextRequest - now();
                if (pause > TimeSpan.Zero) await wait(pause, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                nextRequest = now().AddSeconds(1);
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, "https://universalis.app/api/v2/" + path);
                    request.Headers.UserAgent.ParseAdd(userAgent);
                    using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                    if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout ||
                        (int)response.StatusCode >= 500)
                    {
                        var retryAfter = response.Headers.RetryAfter;
                        var backoff = retryAfter?.Delta ?? (retryAfter?.Date - now()) ?? TimeSpan.FromSeconds(1 << attempt);
                        nextRequest = now() + (backoff > TimeSpan.FromSeconds(1) ? backoff : TimeSpan.FromSeconds(1));
                        if (attempt < 2) continue;
                    }
                    response.EnsureSuccessStatusCode();
                    var data = await response.Content.ReadFromJsonAsync<MarketData>(cancellationToken).ConfigureAwait(false);
                    if (data == null || data.ItemID != itemId || data.LastUploadTime < 0 ||
                        data.Listings?.Any(l => l is null) == true || data.RecentHistory?.Any(s => s is null) == true)
                        throw new JsonException("Universalis sent an unexpected response.");

                    var snapshot = new MarketSnapshot(scope, now(), data);
                    cache.Remove(path);
                    if (cache.Count >= 128) cache.Remove(cache.MinBy(entry => entry.Value.FetchedAt).Key);
                    cache[path] = snapshot;
                    return snapshot;
                }
                catch (HttpRequestException ex) when (ex.StatusCode == null && attempt < 2)
                {
                    nextRequest = now().AddSeconds(1 << attempt);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < 2)
                {
                    nextRequest = now().AddSeconds(1 << attempt);
                }
            }
        }
        finally
        {
            gate.Release();
        }
    }
}
