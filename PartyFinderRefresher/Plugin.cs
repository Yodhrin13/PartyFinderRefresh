using System;
using Dalamud.Game;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;
using PartyFinderRefresher.Services;
using PartyFinderRefresher.Windows;

namespace PartyFinderRefresher;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/pfr";
    private const string DebugCommandName = "/pfrdebug";

    private readonly IDalamudPluginInterface PluginInterface;
    private readonly ICommandManager CommandManager;
    private readonly IFramework Framework;
    private readonly ICondition Condition;
    private readonly IChatGui ChatGui;
    private readonly ISigScanner SigScanner;
    private readonly IPluginLog PluginLog;

    public readonly Configuration Configuration;
    public readonly WindowSystem WindowSystem = new("PartyFinderRefresher");

    private readonly PartyFinderAgent PartyFinderAgent;
    private readonly RefreshService RefreshService;

    private readonly MainWindow MainWindow;
    private readonly DebugWindow DebugWindow;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IFramework framework,
        ICondition condition,
        IChatGui chatGui,
        ISigScanner sigScanner,
        IPluginLog pluginLog)
    {
        PluginInterface = pluginInterface;
        CommandManager = commandManager;
        Framework = framework;
        Condition = condition;
        ChatGui = chatGui;
        SigScanner = sigScanner;
        PluginLog = pluginLog;

        // ECommons init — must be first
        ECommonsMain.Init(pluginInterface, this);

        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(PluginInterface);

        PartyFinderAgent = new PartyFinderAgent(SigScanner, PluginLog);
        RefreshService = new RefreshService(Configuration, Framework, Condition, ChatGui, PluginLog, PartyFinderAgent);

        MainWindow = new MainWindow(this);
        DebugWindow = new DebugWindow(this, RefreshService, PartyFinderAgent, SigScanner);

        WindowSystem.AddWindow(MainWindow);
        WindowSystem.AddWindow(DebugWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Party Finder Refresher settings."
        });
        CommandManager.AddHandler(DebugCommandName, new CommandInfo(OnDebugCommand)
        {
            HelpMessage = "Open Party Finder Refresher debug window."
        });

        PluginInterface.UiBuilder.Draw += DrawUI;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleMainUI;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUI;
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= DrawUI;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleMainUI;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUI;

        WindowSystem.RemoveAllWindows();

        CommandManager.RemoveHandler(CommandName);
        CommandManager.RemoveHandler(DebugCommandName);

        RefreshService.Dispose();
        PartyFinderAgent.Dispose();

        // ECommons dispose — must be last
        ECommonsMain.Dispose();
    }

    private void OnCommand(string command, string args) => ToggleMainUI();
    private void OnDebugCommand(string command, string args) => DebugWindow.Toggle();

    private void DrawUI() => WindowSystem.Draw();
    public void ToggleMainUI() => MainWindow.Toggle();
    public void ToggleDebugUI() => DebugWindow.Toggle();

    public void SaveConfiguration() => Configuration.Save();
}