using System.Net;
using Bagcheck.Core.Market;
using Xunit;

namespace Bagcheck.Core.Tests;

public class UniversalisClientTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private const string Body = """{"itemID":5057,"lastUploadTime":1,"listings":[{"pricePerUnit":12,"quantity":99,"hq":false,"worldID":34}],"recentHistory":[]}""";

    private static (UniversalisClient Client, FakeHandler Handler, List<TimeSpan> Waits) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new FakeHandler(respond);
        var waits = new List<TimeSpan>();
        var time = DateTimeOffset.UnixEpoch;
        var client = new UniversalisClient(new HttpClient(handler), "Bagcheck/test", () => time,
            (span, _) => { waits.Add(span); time += span; return Task.CompletedTask; });
        return (client, handler, waits);
    }

    [Fact]
    public async Task QualityIsOnlyFilteredWhenAsked()
    {
        var (client, handler, _) = Create(_ => Json(Body));
        await client.GetAsync(5057, null, "Aether", cancellationToken: TestContext.Current.CancellationToken);
        await client.GetAsync(5057, true, "Aether", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("https://universalis.app/api/v2/Aether/5057?entries=20", handler.Requests[0].RequestUri!.ToString());
        Assert.EndsWith("&hq=true", handler.Requests[1].RequestUri!.ToString());
        Assert.Contains("Bagcheck/test", handler.Requests[0].Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task RepeatedRequestsComeFromTheCache()
    {
        var (client, handler, _) = Create(_ => Json(Body));
        var first = await client.GetAsync(5057, null, "Aether", cancellationToken: TestContext.Current.CancellationToken);
        var second = await client.GetAsync(5057, null, "Aether", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(handler.Requests);
        Assert.Same(first, second);
        Assert.Equal(34, first.Data.Listings![0].WorldID);
    }

    [Fact]
    public async Task RequestsAreSpacedOneSecondApart()
    {
        var (client, handler, waits) = Create(r => Json(r.RequestUri!.AbsolutePath.EndsWith("5058") ? Body.Replace("5057", "5058") : Body));
        await client.GetAsync(5057, null, "Aether", cancellationToken: TestContext.Current.CancellationToken);
        await client.GetAsync(5058, null, "Aether", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal([TimeSpan.FromSeconds(1)], waits);
    }

    [Fact]
    public async Task AnAnswerForAnotherItemIsRejected()
    {
        var (client, _, _) = Create(_ => Json(Body.Replace("5057", "5058")));
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() =>
            client.GetAsync(5057, null, "Aether", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BusyAnswersAreRetried()
    {
        var calls = 0;
        var (client, handler, _) = Create(_ => ++calls == 1 ? new HttpResponseMessage(HttpStatusCode.TooManyRequests) : Json(Body));
        var snapshot = await client.GetAsync(5057, null, "Aether", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(5057u, snapshot.Data.ItemID);
    }

    [Fact]
    public async Task BadScopesAreRefused()
    {
        var (client, _, _) = Create(_ => Json(Body));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetAsync(5057, null, " ", cancellationToken: TestContext.Current.CancellationToken));
    }
}
