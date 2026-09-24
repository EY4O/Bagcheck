using System;
using Dalamud.Configuration;
using Bagcheck.Core.Shopping;
using Bagcheck.Core.Stock;
using Bagcheck.Windows;

namespace Bagcheck;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public ShoppingList List { get; set; } = new();

    public bool CountSaddlebag { get; set; } = true;
    public bool CountRetainers { get; set; } = true;

    /// <summary>Retainer contents seen longer ago than this are shown but not counted.</summary>
    public int RetainerMaxAgeHours { get; set; } = 72;

    public bool ShowContextMenu { get; set; } = true;

    /// <summary>The welcome guide has been shown once, so it no longer opens by itself.</summary>
    public bool WelcomeSeen { get; set; }

    public bool UseTheme { get; set; } = true;
    public AccentChoice Accent { get; set; } = AccentChoice.GilGold;

    /// <summary>0xRRGGBB, used when <see cref="Accent"/> is Custom.</summary>
    public uint CustomAccent { get; set; } = 0xE0A63A;

    public StockOptions GetStockOptions() => new(CountSaddlebag, CountRetainers, RetainerMaxAgeHours);

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
