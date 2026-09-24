using System;
using Dalamud.Configuration;

namespace Bagcheck;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
