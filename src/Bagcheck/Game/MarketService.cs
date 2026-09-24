using System;
using System.Net.Http;
using Bagcheck.Core.Market;

namespace Bagcheck.Game;

/// <summary>One Universalis client for the whole plugin, so its one-request-a-second limit covers every caller.</summary>
public sealed class MarketService : IDisposable
{
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public MarketService()
    {
        Client = new UniversalisClient(http, $"Bagcheck/{Plugin.PluginInterface.Manifest.AssemblyVersion}");
    }

    public UniversalisClient Client { get; }

    public void Dispose() => http.Dispose();
}
