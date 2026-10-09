# Development

Bagcheck is a Dalamud API 15 plugin (.NET 10, `Dalamud.NET.Sdk` 15.0.0). Everything that doesn't need the game lives
in `Bagcheck.Core`, so it can be tested without Dalamud.

```
src/Bagcheck.Core/       shopping list model, Teamcraft import, price maths, Universalis client,
                         retainer and stock stores, vendor index (no Dalamud)
src/Bagcheck/Game/       game reads (bags, crystals, saddlebag, retainers), item and vendor sheets, context menu
src/Bagcheck/Windows/    main window (Shopping List, Retainers, About), settings, import, welcome guide, theme
tests/Bagcheck.Core.Tests/   xUnit tests for Bagcheck.Core
images/                  icon.png (plugin list, README), logo.png (About tab, welcome guide), README screenshots
```

## Run it locally

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download) and XIVLauncher with Dalamud installed (the SDK finds
Dalamud's libraries from there).

```
dotnet build Bagcheck.slnx -c Debug
```

Add `src/Bagcheck/bin/Debug/Bagcheck.dll` under **`/xlsettings` → Experimental → Dev Plugin Locations**, then enable
it in `/xlplugins`. Rebuild and reload the plugin to try changes. In game: `/bagcheck`, `/bagcheck settings`,
`/bagcheck welcome`.

## Tests

```
dotnet test tests/Bagcheck.Core.Tests
```

| Suite | Covers |
|---|---|
| `ShoppingListTests` | adding entries, needs across groups, paused groups, removing groups |
| `TeamcraftTests` | parsing "Copy as text" lists, sorting raw/craftable/unknown, updating a group |
| `StockTests` | adding up bags, crystals, saddlebag and retainers; HQ only; expiry; saving |
| `PriceTests` | cheapest fill across worlds, tax, sales median, NPC prices, totals |
| `RetainerTests` | retainer roster per character, saved listings, filter, undercut checks |
| `VendorTests` | NPC vendor offers, reachable zones first |
| `UniversalisClientTests` | quality filter, caching, request spacing, retries |

Anything that needs the game (reading inventories, the right-click menu, windows) is checked by hand in game.

## Releasing

1. Bump `<Version>` in `src/Bagcheck/Bagcheck.csproj`.
2. `dotnet build Bagcheck.slnx -c Release` — the packager writes `src/Bagcheck/bin/Release/Bagcheck/latest.zip`.
3. Commit, push to `main`, and attach `latest.zip` to a GitHub release tagged with the version.
4. Update the Bagcheck entry in the [XIV-Plugins](https://github.com/EY4O/XIV-Plugins) `repo.json` so the custom
   repository serves the new version.
