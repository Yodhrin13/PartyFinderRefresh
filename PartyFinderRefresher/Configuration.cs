using System;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace PartyFinderRefresher;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    /// <summary> Whether automatic refreshing is enabled.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary> Interval in minutes between automatic refreshes. Range: 5–55.</summary>
    public int RefreshRateMinutes { get; set; } = 30;

    /// <summary> Maximum number of automatic refreshes. -1 = unlimited.</summary>
    public int MaxRefreshCount { get; set; } = -1;

    /// <summary> Whether to print status messages in game chat.</summary>
    public bool ChatNotifications { get; set; } = true;

    /// <summary> Whether to preserve the PF comment text across refreshes.</summary>
    public bool PreserveComment { get; set; } = true;

    // ---

    [NonSerialized]
    private IDalamudPluginInterface? PluginInterface;

    public void Initialize(IDalamudPluginInterface pluginInterface)
    {
        PluginInterface = pluginInterface;
    }

    public void Save()
    {
        PluginInterface?.SavePluginConfig(this);
    }
}
