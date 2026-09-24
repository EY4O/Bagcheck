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
    [PluginService] internal static IContextMenu ContextMenu { get; private set; } = null!;

    private const string Command = "/bagcheck";
    private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(1);

    private readonly WindowSystem windows = new("Bagcheck");
    private DateTime? dirtySince;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.List.Tidy();
        Theme.Use(Configuration);

        Items = new ItemCatalog(DataManager);
        Vendors = new VendorCatalog();
        _ = Vendors.Index; // starts reading the vendor sheets in the background
        Market = new MarketService();
        Reader = new GameReader(this);
        ListingPrices = new ListingPrices(Market);
        Prices = new PriceChecker(this);

        MainWindow = new MainWindow(this);
        ImportWindow = new ImportWindow(this);
        SettingsWindow = new SettingsWindow(this);
        WelcomeWindow = new WelcomeWindow(this);
        windows.AddWindow(MainWindow);
        windows.AddWindow(ImportWindow);
        windows.AddWindow(SettingsWindow);
        windows.AddWindow(WelcomeWindow);
        itemMenu = new ItemContextMenu(ContextMenu, this);

        CommandManager.AddHandler(Command, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Bagcheck. Also: /bagcheck settings, /bagcheck welcome.",
        });
        PluginInterface.UiBuilder.Draw += windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainWindow;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleSettings;
        Framework.Update += OnUpdate;

        if (!Configuration.WelcomeSeen) WelcomeWindow.Open();
    }

    public Configuration Configuration { get; }
    public ItemCatalog Items { get; }
    public MarketService Market { get; }
    public GameReader Reader { get; }
    public ListingPrices ListingPrices { get; }
    public VendorCatalog Vendors { get; }
    public PriceChecker Prices { get; }
    private MainWindow MainWindow { get; }
    private ImportWindow ImportWindow { get; }
    private SettingsWindow SettingsWindow { get; }
    private WelcomeWindow WelcomeWindow { get; }
    private readonly ItemContextMenu itemMenu;

    /// <summary>The logged-in character's content id, or 0.</summary>
    public ulong CharacterId => PlayerState.IsLoaded ? PlayerState.ContentId : 0;

    /// <summary>Adds an item to the shopping list (outside any group) and shows it.</summary>
    public void AddToList(uint itemId, bool hq)
    {
        if (Items.Get(itemId) is not { IsMarketable: true } info) return;
        var entry = Configuration.List.Add(info.Id, info.Name, hq && info.CanBeHq, 1, null, out var added);
        MarkDirty();
        ChatGui.Print(added ? $"Added {info.Name} to your shopping list." : $"{info.Name} is already on your shopping list.", "Bagcheck");
        MainWindow.ShowListEntry(entry.Id);
    }

    public void OpenImport(Guid? groupId) => ImportWindow.Open(groupId);

    /// <summary>Saves shortly after the last change, so typing into a field doesn't write the file every frame.</summary>
    public void MarkDirty() => dirtySince = DateTime.UtcNow;

    public void ToggleMainWindow() => MainWindow.Toggle();
    public void ShowMainWindow() => MainWindow.IsOpen = true;
    public void ToggleSettings() => SettingsWindow.Toggle();
    public void OpenWelcome() => WelcomeWindow.Open();

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

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "":
                ToggleMainWindow();
                break;
            case "settings":
            case "config":
                ToggleSettings();
                break;
            case "welcome":
                OpenWelcome();
                break;
            default:
                ChatGui.Print("/bagcheck opens the window. /bagcheck settings opens the settings, /bagcheck welcome the guide.", "Bagcheck");
                break;
        }
    }

    public void Dispose()
    {
        Framework.Update -= OnUpdate;
        PluginInterface.UiBuilder.Draw -= windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainWindow;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleSettings;
        CommandManager.RemoveHandler(Command);
        itemMenu.Dispose();
        windows.RemoveAllWindows();

        Prices.Dispose();
        ListingPrices.Dispose();
        Market.Dispose();
        Reader.Save();
        if (dirtySince != null) Configuration.Save();
    }
}
