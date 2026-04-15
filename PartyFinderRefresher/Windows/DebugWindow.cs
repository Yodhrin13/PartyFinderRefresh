using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using PartyFinderRefresher.Services;
using Dalamud.Bindings.ImGui;

namespace PartyFinderRefresher.Windows;

/// <summary>
/// Debug window exposing full internal state and sig-scanning tools.
/// </summary>
public sealed class DebugWindow : Window, IDisposable
{
    private readonly Plugin Plugin;
    private readonly RefreshService RefreshService;
    private readonly PartyFinderAgent PartyFinderAgent;
    private readonly ISigScanner SigScanner;

    public DebugWindow(Plugin plugin, RefreshService refreshService, PartyFinderAgent partyFinderAgent, ISigScanner sigScanner)
        : base("PFR Debug##PFRDebug", ImGuiWindowFlags.NoCollapse)
    {
        Plugin = plugin;
        RefreshService = refreshService;
        PartyFinderAgent = partyFinderAgent;
        SigScanner = sigScanner;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(440, 420),
            MaximumSize = new Vector2(680, 700),
        };
    }

    public void Dispose() { }

    public override void Draw()
    {
        // =====================================================================
        // Agent Status
        // =====================================================================
        if (ImGui.CollapsingHeader("Agent Status", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Indent();

            // Signature status — now always resolved since we use FFXIVClientStructs
            ImGui.TextColored(new Vector4(0.2f, 1.0f, 0.2f, 1.0f), "✓ Using FFXIVClientStructs Interop");
            ImGui.TextWrapped("Method: AgentLookingForGroup.OpenListingByContentId (auto-updated with game patches)");

            ImGui.Text($"Agent Pointer: 0x{PartyFinderAgent.GetAgentPointer():X}");

            // Cached comment (not live game memory — see PartyFinderAgent.cs note)
            var comment = PartyFinderAgent.GetStoredComment();
            ImGui.Text("Cached Comment:");
            ImGui.SameLine();
            ImGui.TextWrapped(string.IsNullOrEmpty(comment) ? "(empty)" : comment);

            ImGui.Unindent();
        }

        ImGui.Spacing();

        // =====================================================================
        // Refresh State
        // =====================================================================
        if (ImGui.CollapsingHeader("Refresh State", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Indent();

            DrawStatusRow("Enabled", Plugin.Configuration.Enabled);
            DrawStatusRow("PF Listing Active (AgentActive)", RefreshService.IsListingActive);
            DrawStatusRow("Currently Refreshing", RefreshService.CurrentlyRefreshing);

            ImGui.Spacing();

            ImGui.Text($"Elapsed:  {RefreshService.ElapsedMinutes:F2} / {Plugin.Configuration.RefreshRateMinutes} min");

            var progress = Plugin.Configuration.RefreshRateMinutes > 0
                ? (float)(RefreshService.ElapsedMinutes / Plugin.Configuration.RefreshRateMinutes)
                : 0f;
            progress = Math.Clamp(progress, 0f, 1f);
            ImGui.ProgressBar(progress, new Vector2(-1, 0),
                $"{RefreshService.ElapsedMinutes:F1} / {Plugin.Configuration.RefreshRateMinutes} min");

            ImGui.Spacing();

            var maxStr = Plugin.Configuration.MaxRefreshCount < 0
                ? "unlimited"
                : Plugin.Configuration.MaxRefreshCount.ToString();
            ImGui.Text($"Refreshes: {RefreshService.TotalRefreshes} / {maxStr}");
            ImGui.Text($"Last Refresh: {(RefreshService.LastRefreshTime.HasValue ? RefreshService.LastRefreshTime.Value.ToString("HH:mm:ss") : "—")}");

            if (!string.IsNullOrEmpty(RefreshService.LastError))
                ImGui.TextColored(new Vector4(1.0f, 0.4f, 0.4f, 1.0f), $"Error: {RefreshService.LastError}");

            ImGui.Unindent();
        }

        ImGui.Spacing();

        // =====================================================================
        // Controls
        // =====================================================================
        if (ImGui.CollapsingHeader("Controls", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Indent();

            if (ImGui.Button("Force Refresh Now"))
                RefreshService.ForceRefresh();

            ImGui.SameLine();

            if (ImGui.Button("Reset Counters"))
                RefreshService.Reset();

            ImGui.SameLine();

            var toggleLabel = Plugin.Configuration.Enabled ? "Disable" : "Enable";
            if (ImGui.Button(toggleLabel))
            {
                Plugin.Configuration.Enabled = !Plugin.Configuration.Enabled;
                Plugin.SaveConfiguration();
            }

            ImGui.Unindent();
        }

        ImGui.Spacing();

        // =====================================================================
        // About
        // =====================================================================
        //if (ImGui.CollapsingHeader("About"))
        //{
        //    ImGui.Indent();

        //    ImGui.TextWrapped(
        //        "PartyFinderRefresher uses FFXIVClientStructs' auto-generated party finder methods. " +
        //        "This approach is safer and more maintainable than manual signature scanning, " +
        //        "and automatically adapts to game patches.");

        //    ImGui.Unindent();
        //}
    }

    private static void DrawStatusRow(string label, bool value)
    {
        ImGui.Text($"{label}:");
        ImGui.SameLine();
        var color = value
            ? new Vector4(0.2f, 1.0f, 0.2f, 1.0f)
            : new Vector4(0.6f, 0.6f, 0.6f, 1.0f);
        ImGui.TextColored(color, value ? "YES" : "NO");
    }
}