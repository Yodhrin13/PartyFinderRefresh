using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;

namespace PartyFinderRefresher.Windows;

/// <summary>
/// Main settings window for the plugin.
/// </summary>
public sealed class MainWindow : Window, IDisposable
{
    private readonly Plugin Plugin;

    public MainWindow(Plugin plugin)
        : base("Party Finder Refresher##PFRMain",
               ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        Plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(320, 220),
            MaximumSize = new Vector2(420, 360),
        };
    }

    public void Dispose() { }

    public override void Draw()
    {
        var config = Plugin.Configuration;
        var changed = false;

        // --- Master toggle ---
        var enabled = config.Enabled;
        if (ImGui.Checkbox("Enable Auto-Refresh", ref enabled))
        {
            config.Enabled = enabled;
            changed = true;
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // --- Refresh interval ---
        var rate = config.RefreshRateMinutes;
        ImGui.SetNextItemWidth(180);
        if (ImGui.SliderInt("Refresh Interval (min)", ref rate, 5, 55))
        {
            config.RefreshRateMinutes = rate;
            changed = true;
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("How often to refresh your PF listing.\nPF listings expire after 60 minutes.\nRecommended: 25–30 minutes.");

        ImGui.Spacing();

        // --- Max refreshes ---
        var maxRefresh = config.MaxRefreshCount;
        ImGui.SetNextItemWidth(180);
        if (ImGui.InputInt("Max Refreshes (-1 = unlimited)", ref maxRefresh))
        {
            if (maxRefresh < -1) maxRefresh = -1;
            config.MaxRefreshCount = maxRefresh;
            changed = true;
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Maximum number of automatic refreshes per session.\nSet to -1 for unlimited.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // --- Options ---
        var chatNotifs = config.ChatNotifications;
        if (ImGui.Checkbox("Chat Notifications", ref chatNotifs))
        {
            config.ChatNotifications = chatNotifs;
            changed = true;
        }

        var preserveComment = config.PreserveComment;
        if (ImGui.Checkbox("Preserve Comment Text", ref preserveComment))
        {
            config.PreserveComment = preserveComment;
            changed = true;
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Cache and restore your PF comment if the\ngame clears it during refresh.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // --- Debug button ---
        if (ImGui.Button("Open Debug Window"))
        {
            Plugin.ToggleDebugUI();
        }

        // --- Save ---
        if (changed)
        {
            Plugin.SaveConfiguration();
        }
    }
}
