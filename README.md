# Rune Details

A [MelonLoader](https://github.com/LavaGang/MelonLoader) mod for **No Rest for the Wicked** that adds what a rune
really does to its text: damage as a multiple of weapon damage, heals, buffs, costs and timings. Display only; the
Quantum simulation is untouched.

Download: [Nexus Mods](https://www.nexusmods.com/norestforthewicked/mods/107) · [GitHub releases](https://github.com/vergir/NRftW-RuneDetails/releases/latest)

What players see is described on the Nexus page ([docs/nexus-description.bbcode](docs/nexus-description.bbcode)); every
rune's output in both modes is in [docs/rune-tooltips.html](docs/rune-tooltips.html); how each number is found, the
traced game code and the in-game measurements are in [docs/internal.md](docs/internal.md).

## Build

* `dotnet build -c Release` builds and copies `RuneDetails.dll` to `<game>/Mods` (`-p:DeployToGame=false` to skip,
  `-p:GameDir=...` for another install). With HotReload in `<game>/Plugins` the running game picks up new builds.
* `pwsh ./package.ps1` builds the release zip into `dist/RuneDetails.zip` (`Mods/RuneDetails.dll`, README, LICENSE,
  CHANGELOG).
* Needs MelonLoader 0.7.3's generated interop assemblies in the game folder (start the game once with MelonLoader).

## Layout

| Path | What |
|---|---|
| `src/RuneDescriber.cs` | Walks a rune's Quantum action data into the brief text and the detailed lines (`RuneText`). |
| `src/RuneLayout.cs` | Puts that text into the game's: brief as a grey `(…)`, detailed as coloured lines. |
| `src/Patches/RuneDescriptionPatches.cs` | `HeroItemDataAsset.GetDescription` postfix; which view asks (utility slot, Runes menu). |
| `src/TypeCostDedup.cs` | Removes our type/cost lines where the game shows its own row. |
| `src/SettingsRows.cs`, `src/Prefs.cs` | The Off / Brief / Detailed dropdown in Options > Gameplay; `[RuneDetails]` preferences. |
| `tools/rune_table.py` | Builds `docs/rune-tooltips.html` from the in-game self-test. |
| `tools/timing/` | Scripts used to trace rune timings (need the workspace's qdb reader). |

## Preferences

`UserData/MelonPreferences.cfg`, section `[RuneDetails]`:

| Key | Default | |
|---|---|---|
| `Enabled` | `true` | Master switch. |
| `Mode` | `Brief` | `Off`, `Brief` or `Detailed`; also in Options > Gameplay. |
| `HiddenFormat` | ` <color=#9A9A9A>({extra})</color>` | How the brief note is appended. |
| `AddSettingsRows` | `true` | Add the dropdown to Options > Gameplay. |
| `Debug` | `false` | Log every rune's text when first shown. |

## Self-test

Put `name guid` lines (from the workspace's `analysis/rune_inventory.csv`) in `UserData/RuneDetails.selftest.txt`. On
the first scene load the mod writes every rune's brief and detailed text to `RuneDetails.selftest.out.txt`;
`python tools/rune_table.py <out file>` turns it into the HTML table. Delete both files afterwards.

## License

MIT, see [LICENSE](LICENSE).
