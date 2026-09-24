using System;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Bagcheck.Game;
using Bagcheck.Windows;

namespace Bagcheck;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IGameInventory GameInventory { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;

    private const string Command = "/bagcheck";
    private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(1);

    private readonly WindowSystem windows = new("Bagcheck");
    private DateTime? dirtySince;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Theme.Use(Configuration);

        Items = new ItemCatalog(DataManager);
        Market = new MarketService();
        Reader = new GameReader(this);
        ListingPrices = new ListingPrices(Market);

        MainWindow = new MainWindow(this);
        windows.AddWindow(MainWindow);

        CommandManager.AddHandler(Command, new CommandInfo(OnCommand) { HelpMessage = "Open Bagcheck." });
        PluginInterface.UiBuilder.Draw += windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainWindow;
        Framework.Update += OnUpdate;
    }

    public Configuration Configuration { get; }
    public ItemCatalog Items { get; }
    public MarketService Market { get; }
    public GameReader Reader { get; }
    public ListingPrices ListingPrices { get; }
    private MainWindow MainWindow { get; }

    /// <summary>The logged-in character's content id, or 0.</summary>
    public ulong CharacterId => PlayerState.IsLoaded ? PlayerState.ContentId : 0;

    /// <summary>Saves shortly after the last change, so typing into a field doesn't write the file every frame.</summary>
    public void MarkDirty() => dirtySince = DateTime.UtcNow;

    public void ToggleMainWindow() => MainWindow.Toggle();

    private void OnUpdate(IFramework framework)
    {
        Reader.Update();
        ListingPrices.Update();
        if (dirtySince is { } since && DateTime.UtcNow - since >= SaveDelay)
        {
            dirtySince = null;
            Configuration.Save();
        }
    }

    private void OnCommand(string command, string args) => ToggleMainWindow();

    public void Dispose()
    {
        Framework.Update -= OnUpdate;
        PluginInterface.UiBuilder.Draw -= windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainWindow;
        CommandManager.RemoveHandler(Command);
        windows.RemoveAllWindows();

        ListingPrices.Dispose();
        Market.Dispose();
        Reader.Save();
        if (dirtySince != null) Configuration.Save();
    }
}
