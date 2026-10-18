using System;
using Dalamud.Configuration;

namespace TradeTracker;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    /// <summary>Print a one-line summary in chat whenever a trade is logged.</summary>
    public bool PrintToChat { get; set; } = true;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
