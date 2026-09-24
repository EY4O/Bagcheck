using System.Linq;
using Dalamud.Game;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace Bagcheck.Game;

/// <summary>
/// World and data centre names for the logged-in character. English sheet names are used because that's how
/// Universalis spells them, whatever language the client runs in.
/// </summary>
public static class Worlds
{
    public static uint Home => Plugin.PlayerState.HomeWorld.RowId;
    public static uint Current => Plugin.PlayerState.CurrentWorld.RowId;

    public static string HomeName => Name(Home);
    public static string CurrentName => Name(Current);

    /// <summary>The data centre of the world the character is standing on.</summary>
    public static string DataCentre =>
        DataCentreOf(Current) is var id and not 0 &&
        Plugin.DataManager.GetExcelSheet<WorldDCGroupType>(ClientLanguage.English).GetRowOrDefault(id) is { } row
            ? row.Name.ExtractText()
            : "";

    public static string Name(uint world) => world == 0 ? ""
        : Sheet.GetRowOrDefault(world) is { } row ? row.Name.ExtractText() : "#" + world;

    public static string Name(int world) => world <= 0 ? "" : Name((uint)world);

    private static uint DataCentreOf(uint world) => Sheet.GetRowOrDefault(world)?.DataCenter.RowId ?? 0;

    private static ExcelSheet<World> Sheet => Plugin.DataManager.GetExcelSheet<World>(ClientLanguage.English);
}
