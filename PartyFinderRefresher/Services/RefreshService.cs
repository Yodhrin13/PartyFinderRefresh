using System;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using ECommons.Automation;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace PartyFinderRefresher.Services;

/// <summary>
/// Core refresh loop. Subscribes to IFramework.Update, accumulates elapsed time,
/// and fires an async UI automation sequence when the interval is reached.
/// </summary>
public sealed class RefreshService : IDisposable
{
    private readonly Configuration Config;
    private readonly IFramework Framework;
    private readonly ICondition Condition;
    private readonly IChatGui ChatGui;
    private readonly IPluginLog PluginLog;
    private readonly PartyFinderAgent Agent;

    // --- Public state (read-only for debug window) ---

    /// <summary>Minutes elapsed since last refresh or since enabling.</summary>
    public double ElapsedMinutes { get; private set; }

    /// <summary>Total number of refreshes completed this session.</summary>
    public int TotalRefreshes { get; private set; }

    /// <summary>Whether a refresh is currently in progress.</summary>
    public bool CurrentlyRefreshing { get; private set; }

    /// <summary>Timestamp of the last successful refresh.</summary>
    public DateTime? LastRefreshTime { get; private set; }

    /// <summary>Last error message, if any.</summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// Whether the local player currently has an active PF recruitment listing.
    ///
    /// Checked directly via AgentLookingForGroup.Instance()->IsAgentActive().
    /// There is no stable ConditionFlag for "currently recruiting" — do not use
    /// ConditionFlag here; the available ones only reflect UI window state.
    /// </summary>
    public unsafe bool IsListingActive
    {
        get
        {
            try
            {
                var agent = AgentLookingForGroup.Instance();
                return agent != null && agent->IsAgentActive();
            }
            catch
            {
                return false;
            }
        }
    }

    // Retry constants for addon polling
    private const int MaxAddonRetries = 60;   // 60 × 100 ms = 6 seconds max
    private const int AddonRetryDelayMs = 100;

    public RefreshService(
        Configuration config,
        IFramework framework,
        ICondition condition,
        IChatGui chatGui,
        IPluginLog pluginLog,
        PartyFinderAgent agent)
    {
        Config = config;
        Framework = framework;
        Condition = condition;
        ChatGui = chatGui;
        PluginLog = pluginLog;
        Agent = agent;

        Framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
    }

    /// <summary>
    /// Core tick handler. Accumulates elapsed time and triggers refresh at interval.
    /// </summary>
    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!Config.Enabled || !IsListingActive)
        {
            if (!IsListingActive && (ElapsedMinutes > 0 || TotalRefreshes > 0))
            {
                PluginLog.Debug("[RefreshService] Listing no longer active — resetting state.");
                Reset();
            }
            return;
        }

        if (CurrentlyRefreshing)
            return;

        ElapsedMinutes += framework.UpdateDelta.TotalMinutes;

        if (ElapsedMinutes >= Config.RefreshRateMinutes)
        {
            if (Config.MaxRefreshCount >= 0 && TotalRefreshes >= Config.MaxRefreshCount)
            {
                PluginLog.Info("[RefreshService] Max refresh count reached — stopping.");
                if (Config.ChatNotifications)
                    ChatGui.Print("[PFR] Max refresh count reached. Auto-refresh paused.");
                Config.Enabled = false;
                return;
            }

            ElapsedMinutes = 0;
            ExecuteRefreshAsync();
        }
    }

    /// <summary>
    /// Runs the refresh sequence asynchronously.
    ///
    /// Flow:
    ///   1. Call native agent to open our own PF listing detail window.
    ///   2. Poll for LookingForGroupDetail addon, fire Callback with value 0 (Edit button).
    ///   3. Poll for LookingForGroupCondition addon, fire Callback with value 0 (Recruit button).
    ///
    /// BUTTON NODE IDs: Determined by inspecting the addon tree in /xldev → Addon Inspector.
    /// If clicks stop working after a patch, re-check the node IDs there.
    ///   LookingForGroupDetail  → Edit button          = node 8
    ///   LookingForGroupCondition → Recruit Members    = node 10
    ///
    /// THREAD SAFETY: All AtkUnitBase access is dispatched to the framework thread via
    /// Framework.RunOnFrameworkThread(). Never touch game structs from the thread pool.
    /// </summary>
    private void ExecuteRefreshAsync()
    {
        CurrentlyRefreshing = true;
        LastError = null;

        Task.Run(async () =>
        {
            try
            {
                PluginLog.Info("[RefreshService] Starting refresh...");

                // 0. Ensure the Party Finder window is open
                if (!await EnsurePartyFinderWindowOpen())
                {
                    LastError = "Could not open Party Finder window. You may be in combat or in an instance.";
                    PluginLog.Warning($"[RefreshService] {LastError}");
                    if (Config.ChatNotifications)
                        ChatGui.PrintError($"[PFR] {LastError}");
                    return;
                }

                // 1. Cache the current comment BEFORE we do anything (if enabled)
                if (Config.PreserveComment)
                {
                    try
                    {
                        await Framework.RunOnFrameworkThread(() =>
                        {
                            unsafe
                            {
                                var agent = AgentLookingForGroup.Instance();
                                if (agent != null)
                                {
                                    // Read the comment from the agent's memory
                                    var commentStr = agent->StoredRecruitmentInfo.Comment.ToString();
                                    Agent.CacheComment(commentStr);
                                    PluginLog.Debug($"[RefreshService] Cached current comment: \"{commentStr}\"");
                                }
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        PluginLog.Warning($"[RefreshService] Could not cache comment: {ex.Message}");
                    }
                }

                // 2. Open own PF listing via native method
                if (!Agent.TryOpenOwnListing())
                {
                    LastError = "Failed to open PF listing (agent call failed).";
                    PluginLog.Error($"[RefreshService] {LastError}");
                    if (Config.ChatNotifications)
                        ChatGui.PrintError($"[PFR] {LastError}");
                    return;
                }

                // 3. Wait for LookingForGroupDetail → click Edit (Callback value 0)
                if (!await WaitAndFireCallback("LookingForGroupDetail", 0, "Edit"))
                {
                    LastError = "Timed out waiting for LookingForGroupDetail.";
                    PluginLog.Error($"[RefreshService] {LastError}");
                    if (Config.ChatNotifications)
                        ChatGui.PrintError($"[PFR] {LastError}");
                    return;
                }

                // Wait for the Condition window to fully load and be interactive
                // This delay is critical — the game needs time to populate form data before accepting recruitment
                PluginLog.Debug("[RefreshService] Waiting 1000ms for LookingForGroupCondition window to fully initialize and load form data...");
                await Task.Delay(1000);

                // 4. Wait for LookingForGroupCondition → click Recruit Members (Callback value 0)
                // The game validates the recruitment data server-side. If it rejects with
                // "Unable to complete party recruitment registration", the form state may be invalid.
                if (!await WaitAndFireCallback("LookingForGroupCondition", 0, "Recruit Members"))
                {
                    LastError = "Timed out waiting for LookingForGroupCondition.";
                    PluginLog.Error($"[RefreshService] {LastError}");
                    if (Config.ChatNotifications)
                        ChatGui.PrintError($"[PFR] {LastError}");
                    return;
                }

                TotalRefreshes++;
                LastRefreshTime = DateTime.Now;
                PluginLog.Info($"[RefreshService] Refresh #{TotalRefreshes} completed.");

                if (Config.ChatNotifications)
                    ChatGui.Print($"[PFR] Party Finder listing refreshed (#{TotalRefreshes}).");
            }
            catch (Exception ex)
            {
                LastError = $"Refresh failed: {ex.Message}";
                PluginLog.Error($"[RefreshService] {LastError}\n{ex.StackTrace}");
                if (Config.ChatNotifications)
                    ChatGui.PrintError($"[PFR] {LastError}");
            }
            finally
            {
                CurrentlyRefreshing = false;
            }
        });
    }

    /// <summary>
    /// Ensures the Party Finder window is open and visible.
    /// If it's not visible, attempts to open it and waits for it to load.
    /// </summary>
    private async Task<bool> EnsurePartyFinderWindowOpen()
    {
        PluginLog.Debug("[RefreshService] Ensuring Party Finder window is open...");

        // Try up to 10 times to get/open the window (each retry is 100ms)
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var isWindowVisible = false;

            await Framework.RunOnFrameworkThread(() =>
            {
                unsafe
                {
                    var addon = Svc.GameGui.GetAddonByName<AtkUnitBase>("LookingForGroup");
                    if (addon != null && addon->IsVisible)
                    {
                        isWindowVisible = true;
                        PluginLog.Debug("[RefreshService] Party Finder window is already visible.");
                        return;
                    }

                    // Window not visible, try to open it via the agent
                    var agent = AgentLookingForGroup.Instance();
                    if (agent != null)
                    {
                        PluginLog.Debug($"[RefreshService] Party Finder window not visible on attempt {attempt + 1}. Attempting to show it...");
                        agent->Show();
                    }
                }
            });

            if (isWindowVisible)
                return true;

            // Wait a bit for the window to appear
            await Task.Delay(100);
        }

        PluginLog.Warning("[RefreshService] Could not open Party Finder window after 10 attempts.");
        return false;
    }

    /// <summary>
    /// Polls for a visible addon then fires ECommons Callback.Fire on it.
    /// All game-struct access runs on the framework thread.
    ///
    /// Callback.Fire(addon, true, callbackValue) simulates the UI event the game
    /// fires when a button in that addon is pressed. callbackValue = 0 is the
    /// primary/confirm action for most buttons, but verify with /xldev.
    ///
    /// IMPORTANT: If you're getting errors like "Unable to complete party recruitment registration",
    /// the callback value is likely wrong for that button. Use:
    ///   /xldev → Addon Inspector → click on the addon → Events tab → manually click the button
    ///   and note the callback value shown in the events list.
    /// </summary>
    private async Task<bool> WaitAndFireCallback(string addonName, int callbackValue, string actionLabel)
    {
        PluginLog.Debug($"[RefreshService] Waiting for '{addonName}' addon to become visible...");

        for (var i = 0; i < MaxAddonRetries; i++)
        {
            var fired = false;

            await Framework.RunOnFrameworkThread(() =>
            {
                unsafe
                { 
                    var addon = Svc.GameGui.GetAddonByName<AtkUnitBase>(addonName);
                    if (addon == null)
                    {
                        PluginLog.Debug($"[RefreshService] Addon '{addonName}' not found (attempt {i + 1}/{MaxAddonRetries}).");
                        return;
                    }
                    
                    if (!addon->IsVisible)
                    {
                        PluginLog.Debug($"[RefreshService] Addon '{addonName}' not visible yet (attempt {i + 1}/{MaxAddonRetries}).");
                        return;
                    }

                    PluginLog.Info($"[RefreshService] Found '{addonName}' - firing callback value {callbackValue} for '{actionLabel}'.");
                    Callback.Fire(addon, true, callbackValue);
                    fired = true;
                    PluginLog.Debug($"[RefreshService] Callback fired successfully.");
                }
            });

            if (fired)
                return true;

            await Task.Delay(AddonRetryDelayMs);
        }

        PluginLog.Warning($"[RefreshService] '{addonName}' not visible after {MaxAddonRetries} retries.");
        return false;
    }

    /// <summary>Manually trigger a refresh (for debug window button).</summary>
    public void ForceRefresh()
    {
        if (CurrentlyRefreshing)
        {
            PluginLog.Warning("[RefreshService] Refresh already in progress.");
            return;
        }

        if (!IsListingActive)
        {
            PluginLog.Warning("[RefreshService] No active PF listing.");
            if (Config.ChatNotifications)
                ChatGui.PrintError("[PFR] No active Party Finder listing to refresh.");
            return;
        }

        ElapsedMinutes = 0;
        ExecuteRefreshAsync();
    }

    /// <summary>Reset all counters and state.</summary>
    public void Reset()
    {
        ElapsedMinutes = 0;
        TotalRefreshes = 0;
        CurrentlyRefreshing = false;
        LastRefreshTime = null;
        LastError = null;
    }
}