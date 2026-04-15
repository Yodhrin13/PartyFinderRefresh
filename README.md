# PartyFinderRefresher

A [Dalamud](https://github.com/goatcorp/Dalamud) plugin for FFXIV that automatically refreshes your Party Finder listing on a configurable timer.

## Features

- **Auto-refresh** your active PF listing at a configurable interval (5–55 minutes)
- **Auto-open Party Finder window** — automatically opens the window at refresh start if closed
- **Comment preservation** — caches and restores your recruitment comment if the game clears it during refresh
- **Max refresh cap** — set a limit or run unlimited refreshes per session
- **Chat notifications** — optional status messages in game chat
- **Debug panel** — full state inspector with agent status, timer progress, and manual controls
- **Type-safe interop** — uses FFXIVClientStructs for automatic game update compatibility

## Installation

1. Open Dalamud Settings → Experimental
2. Add the following custom plugin repository URL:
   ```
   https://raw.githubusercontent.com/Yodhrin13/PartyFinderRefresh/main/repo.json
   ```
3. Save and search for "PartyFinderRefresher" in the plugin installer

## Usage

- `/pfr` — Open settings window
- `/pfrdebug` — Open debug inspector

### Basic Workflow

1. Create a Party Finder listing as normal (any state: recruiting, paused, etc.)
2. Open settings with `/pfr` and **enable auto-refresh**
3. The plugin will:
   - Automatically open the Party Finder window when needed
   - Click the Edit button on your listing
   - Confirm the recruitment to refresh the listing timestamp
4. Watch the timer progress in the debug window (`/pfrdebug`)

### Important Notes

- You **don't need to keep the PF window open** — the plugin will open/close it as needed
- The refresh only works on **active listings** (you must have created one first)
- If you're **in combat or in an instance**, the refresh will fail gracefully with a clear error message

## Building

### Prerequisites

- .NET 8.0 SDK
- Dalamud development environment (`DALAMUD_HOME` environment variable set)

### Build

```bash
dotnet restore
dotnet build --configuration Release
```

The built plugin will be in `PartyFinderRefresher/bin/x64/Release/`.

## Configuration

| Setting | Default | Description |
|---------|---------|-------------|
| Enabled | `false` | Master toggle for auto-refresh |
| Refresh Interval | `30 min` | Time between automatic refreshes (5–55 minutes) |
| Max Refreshes | `-1` | Maximum refreshes per session (-1 = unlimited) |
| Chat Notifications | `true` | Print status messages to game chat |
| Preserve Comment | `true` | Cache and restore your PF comment text |

## Architecture

```
Plugin.cs                 → Entry point, dependency injection, command handlers
Configuration.cs          → IPluginConfiguration with persistence to disk
Services/
  PartyFinderAgent.cs     → FFXIVClientStructs interop wrapper
                            - OpenListingByContentId (auto-generated, auto-updated)
                            - Comment caching
  RefreshService.cs       → Core refresh loop
                            - Framework.Update timer logic
                            - Window auto-open detection
                            - Async UI automation (click Edit, click Recruit)
Windows/
  MainWindow.cs           → Settings UI (ImGui)
  DebugWindow.cs          → Debug inspector: state, timers, addon info
```

## How It Works

### Refresh Sequence

1. **Window Check** — Ensures Party Finder window is open (auto-opens if needed)
2. **Comment Cache** — Reads and stores your current comment (if "Preserve Comment" enabled)
3. **Open Listing** — Calls `AgentLookingForGroup.OpenListingByContentId()` to show your listing details
4. **Edit Step** — Waits for and clicks the "Edit" button in LookingForGroupDetail addon
5. **Recruit Step** — Waits for and clicks the "Recruit Members" button in LookingForGroupCondition addon
6. **Complete** — Server registers the refresh, timestamp is updated

### Safety Features

- All game struct access runs on the **Framework thread** (thread-safe)
- Proper null checks and exception handling throughout
- Auto-detecting window open/close states (no hard-coded assume window is open)
- Graceful failure messages in chat if anything goes wrong

## Dependencies

- [Dalamud.NET.Sdk 14.0.2](https://github.com/goatcorp/Dalamud)
- [FFXIVClientStructs](https://github.com/FFXIVClientStructs/FFXIVClientStructs) — Auto-generated, auto-updated game struct definitions
- [ECommons 3.x](https://github.com/NightmareXIV/ECommons) — UI automation utilities (Callback.Fire)

## Why FFXIVClientStructs?

This plugin uses **FFXIVClientStructs' auto-generated interop** for the Party Finder agent instead of manual signature scanning. This means:

✅ **Automatic Compatibility** — When the game patches and FFXIVClientStructs updates, this plugin automatically works again  
✅ **Type Safety** — Full struct definitions with proper field layouts  
✅ **Maintainability** — No manual signature strings to hunt down and update  
✅ **Community-Driven** — FFXIVClientStructs is maintained by the community and updated within hours of game patches  

## Troubleshooting

### "Unable to complete party recruitment registration"

This error comes from the game, not the plugin. It means:
- Your listing doesn't exist or is inactive
- You're in a state where the game won't let you recruit (e.g., in combat, in an instance, wait timer active)
- The game's form validation failed

**Solution**: Try manually refreshing in the Party Finder UI first. If that fails, the plugin will also fail.

### Refresh only works with PF window open

**Not anymore!** The plugin now automatically opens the window. If it fails to open, you'll see an error in chat.

### Comment not being preserved

Make sure "Preserve Comment" is enabled in `/pfr` settings. If the comment is still empty:
- Your listing may not have a comment set
- Check the debug window (`/pfrdebug`) to see what was cached

## License

[AGPL-3.0](LICENSE)
