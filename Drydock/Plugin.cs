using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Game.Gui.Dtr;
using Drydock.Core.Fleet;
using Drydock.Core.Plugins;
using Drydock.Windows;

namespace Drydock;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IDtrBar DtrBar { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;

    private const string CommandName = "/drydock";
    private static readonly TimeSpan BackgroundRefresh = TimeSpan.FromMinutes(5);

    private IDtrBarEntry infoBar = null!;
    private readonly InfoBarState infoBarState = new();
    private readonly SetupAutoOpen setupAutoOpen = new();
    private bool disposed;
    private DateTime lastTick = DateTime.MinValue;
    private DateTime lastBackgroundRefresh = DateTime.UtcNow;

    internal Configuration Configuration { get; private set; } = null!;
    internal PluginStatusService Status { get; private set; } = null!;

    private readonly WindowSystem windowSystem = new("Drydock");
    private MainWindow mainWindow = null!;
    private SetupWindow setupWindow = null!;
    private ConfigWindow configWindow = null!;
    private bool setupOpenedByUs;

    public Plugin()
    {
        try
        {
            // winsqlite3 from System32: nothing native in the plugin folder to lock it against updates.
            SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_winsqlite3());

            Configuration = LoadConfiguration();
            Status = new PluginStatusService(PluginInterface, Configuration);

            setupWindow = new SetupWindow(Status);
            mainWindow = new MainWindow(Status, Configuration, () => setupWindow.IsOpen = true, OpenConfig, UpdateInfoBar);
            configWindow = new ConfigWindow(Configuration, Status, UpdateInfoBar);
            windowSystem.AddWindow(setupWindow);
            windowSystem.AddWindow(mainWindow);
            windowSystem.AddWindow(configWindow);

            CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Open Drydock. Subcommands: setup (check the other plugins), refresh (read again), config (settings).",
            });

            BigFont.Init(PluginInterface.UiBuilder);
            PluginInterface.UiBuilder.Draw += windowSystem.Draw;
            PluginInterface.UiBuilder.OpenMainUi += OpenMain;
            PluginInterface.UiBuilder.OpenConfigUi += OpenConfig;

            infoBar = DtrBar.Get("Drydock");
            infoBar.Tooltip = FleetSummary.DefaultTooltip;
            infoBar.OnClick = _ => OpenMain();
            infoBar.Shown = false;

            // The setup window waits for the plugins to load after a login; a plugin installed mid-session starts the wait now.
            if (ClientState.IsLoggedIn) setupAutoOpen.NoteLogin(DateTime.UtcNow);
            ClientState.Login += OnLogin;
            ClientState.Logout += OnLogout;
            Framework.Update += OnFrameworkUpdate;

            Status.Refreshed += OnRefreshed;
            Status.Refresh();
        }
        catch
        {
            Teardown();
            throw;
        }
    }

    // A config that cannot be read or holds out-of-range values falls back to defaults instead of stopping the plugin.
    private static Configuration LoadConfiguration()
    {
        Configuration config;
        try
        {
            config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Drydock could not read its configuration, using the defaults");
            config = new Configuration();
        }
        config.Sanitize();
        return config;
    }

    // info bar off the cached fleet; also the only thing that refreshes while every window is closed
    private void OnFrameworkUpdate(IFramework framework)
    {
        var now = DateTime.UtcNow;
        if (now - lastTick < TimeSpan.FromSeconds(1)) return;
        lastTick = now;

        if (now - lastBackgroundRefresh >= BackgroundRefresh)
        {
            lastBackgroundRefresh = now;
            Status.Refresh();
        }
        UpdateInfoBar();
        CheckSetup();
    }

    private string? lastInfoBarError;

    // Runs on the game's framework tick: whatever goes wrong here is logged once and hides the entry, never thrown.
    private void UpdateInfoBar()
    {
        if (disposed) return;
        try
        {
            var view = FleetSummary.InfoBar(Configuration.ShowInfoBar, Status.Results, Status.Fleet, Configuration.ExcludedCharacters, DateTime.UtcNow);
            var change = infoBarState.Next(view);
            if (change.SetText) infoBar.Text = view.Text!;
            if (change.SetTooltip) infoBar.Tooltip = view.Tooltip;
            if (change.SetShown) infoBar.Shown = change.Shown;
        }
        catch (Exception ex)
        {
            if (lastInfoBarError != ex.Message)
            {
                lastInfoBarError = ex.Message;
                Log.Error(ex, "Drydock info bar failed");
            }
            infoBarState.Reset();
            infoBar.Shown = false;
        }
    }

    private void OnLogin() => setupAutoOpen.NoteLogin(DateTime.UtcNow);

    private void OnLogout(int type, int code) => setupAutoOpen.NoteLogout();

    // Opens the setup window by itself once per session, after the plugins had time to load; closes it again
    // when everything is ready and it was opened that way. The main window is never opened from here.
    private void CheckSetup()
    {
        if (disposed) return;
        var ready = Status.RequiredReady;
        if (setupAutoOpen.ShouldOpen(DateTime.UtcNow, Status.Results.Count > 0, ready))
        {
            setupWindow.IsOpen = true;
            setupOpenedByUs = true;
        }
        else if (setupOpenedByUs && ready)
        {
            setupOpenedByUs = false;
            setupWindow.IsOpen = false;
        }
    }

    // Fires on the refresh worker: whatever touches windows or the bar goes to the framework thread, without waiting for it.
    private void OnRefreshed()
    {
        if (disposed) return;
        Framework.RunOnFrameworkThread(() =>
        {
            UpdateInfoBar();
            CheckSetup();
        });
    }

    private void OnCommand(string command, string args)
    {
        var sub = args.Trim().ToLowerInvariant();
        switch (sub)
        {
            case "":
                OpenMain();
                break;
            case "setup":
                setupOpenedByUs = false;
                setupWindow.IsOpen = true;
                Status.Refresh();
                break;
            case "refresh":
                Status.Refresh();
                break;
            case "config":
                OpenConfig();
                break;
            default:
                ChatGui.Print($"Drydock: unknown subcommand \"{sub}\". Use /drydock, /drydock setup, /drydock refresh or /drydock config.");
                break;
        }
    }

    private void OpenMain() => mainWindow.Toggle();
    private void OpenConfig() => configWindow.Toggle();

    public void Dispose() => Teardown();

    // Also runs on a constructor that threw partway, so every step tolerates what was never built.
    private void Teardown()
    {
        if (disposed) return;
        disposed = true;

        if (Status is not null) Status.Refreshed -= OnRefreshed;
        Framework.Update -= OnFrameworkUpdate;
        ClientState.Login -= OnLogin;
        ClientState.Logout -= OnLogout;
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= OpenMain;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenConfig;

        CommandManager.RemoveHandler(CommandName);
        windowSystem.RemoveAllWindows();
        BigFont.Dispose();
        infoBar?.Remove();
        Status?.Dispose();
        configWindow?.Flush(false);
    }
}
