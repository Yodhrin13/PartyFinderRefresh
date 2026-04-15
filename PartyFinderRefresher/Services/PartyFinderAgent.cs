using System;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace PartyFinderRefresher.Services;

/// <summary>
/// Native interop for Party Finder agent functions.
/// Uses FFXIVClientStructs-generated method <see cref="AgentLookingForGroup.OpenListingByContentId"/>
/// instead of manual signature scanning for safety and maintainability.
/// </summary>
public sealed class PartyFinderAgent : IDisposable
{
    private readonly IPluginLog PluginLog;

    /// <summary>
    /// Cached comment text. Set externally before a refresh so we can restore it
    /// if the game clears it. NOT read from game memory (that field path is unstable).
    /// </summary>
    private string _cachedComment = string.Empty;

    public PartyFinderAgent(ISigScanner sigScanner, IPluginLog pluginLog)
    {
        PluginLog = pluginLog;
        // Note: sigScanner parameter kept for interface compatibility but no longer needed
        PluginLog.Info("[PartyFinderAgent] Initialized with FFXIVClientStructs interop (no sig-scanning required)");
    }

    /// <summary>
    /// Opens the player's own PF listing detail window via FFXIVClientStructs interop.
    /// This uses the official <see cref="AgentLookingForGroup.OpenListingByContentId"/> method,
    /// which is automatically kept in sync with game updates by FFXIVClientStructs.
    /// 
    /// Note: This may fail silently if:
    /// - You don't have an active PF listing
    /// - The Party Finder window is closed (and needs to be opened first)
    /// - You're in a state where you can't edit listings (e.g., in combat)
    /// </summary>
    public unsafe bool TryOpenOwnListing()
    {
        try
        {
            var agent = AgentLookingForGroup.Instance();
            if (agent == null)
            {
                PluginLog.Error("[PartyFinderAgent] AgentLookingForGroup instance is null.");
                return false;
            }

            var playerState = PlayerState.Instance();
            if (playerState == null)
            {
                PluginLog.Error("[PartyFinderAgent] PlayerState instance is null.");
                return false;
            }

            var contentId = playerState->ContentId;
            PluginLog.Info($"[PartyFinderAgent] Opening own listing (ContentId: {contentId})");
            
            // Check if the agent thinks it has an active listing
            if (!agent->IsAgentActive())
            {
                PluginLog.Warning("[PartyFinderAgent] Agent IsAgentActive returned false - you may not have an active Party Finder listing.");
                PluginLog.Info("[PartyFinderAgent] Attempting to open anyway - you may need to have the Party Finder window open with an active listing.");
            }
            
            // Call the official FFXIVClientStructs method
            var result = agent->OpenListingByContentId(contentId);
            
            if (!result)
            {
                PluginLog.Warning("[PartyFinderAgent] OpenListingByContentId returned false (listing may not be active or window may need to be open).");
            }
            
            return result;
        }
        catch (Exception ex)
        {
            PluginLog.Error($"[PartyFinderAgent] Error opening listing: {ex.Message}\n{ex.StackTrace}");
            return false;
        }
    }

    /// <summary>
    /// Caches a comment string for later restoration.
    /// Call this before triggering a refresh.
    /// </summary>
    public void CacheComment(string comment)
    {
        _cachedComment = comment ?? string.Empty;
        PluginLog.Debug($"[PartyFinderAgent] Cached comment: \"{_cachedComment}\"");
    }

    /// <summary>
    /// Returns the cached comment (set via <see cref="CacheComment"/>).
    /// NOTE: Reading the comment directly from AgentLookingForGroup memory is intentionally
    /// NOT done here — the field layout changes frequently with FFXIVClientStructs updates.
    /// If you need live memory reading, find the correct field path in the current
    /// AgentLookingForGroup.cs in FFXIVClientStructs and add it here with a FieldOffset guard.
    /// </summary>
    public string GetStoredComment() => _cachedComment;

    /// <summary>Returns the raw agent pointer address for debug display.</summary>
    public unsafe nint GetAgentPointer()
    {
        try
        {
            var agent = AgentLookingForGroup.Instance();
            return agent != null ? (nint)agent : nint.Zero;
        }
        catch
        {
            return nint.Zero;
        }
    }

    /// <summary>
    /// Scans the entire .text section for ALL matches of a signature pattern.
    /// DEPRECATED: This method is no longer needed with FFXIVClientStructs interop.
    /// Kept for backwards compatibility with debug window if needed.
    /// </summary>
    [Obsolete("Use FFXIVClientStructs OpenListingByContentId instead of manual sig-scanning")]
    public nint[] ScanAllMatches(ISigScanner sigScanner)
    {
        PluginLog.Warning("[PartyFinderAgent] ScanAllMatches called but is deprecated. Using FFXIVClientStructs interop instead.");
        return Array.Empty<nint>();
    }

    public void Dispose()
    {
        // No resources to clean up
    }
}