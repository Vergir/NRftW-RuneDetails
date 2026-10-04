# Rune Details

A [MelonLoader](https://github.com/LavaGang/MelonLoader) mod for **No Rest for the Wicked** that shows what every rune
really does: its damage as a multiple of your weapon's, heals, buffs, costs and timings, right in the rune's tooltip.
Nothing is changed, only shown.

Download: [Nexus Mods](https://www.nexusmods.com/norestforthewicked/mods/) · [GitHub releases](https://github.com/vergir/NRftW-RuneDetails/releases/latest)

## Features

* **Brief** (the default): one grey note at the end of the rune text.
  * `(3 hits × 150–200% weapon dmg)`, `(300% weapon dmg + 200% wave)`, `(10 × 100% weapon dmg in 1.5m)`
  * `(Heals 40 HP)`, `(Heals 30 HP/s to you and allies for 32 Focus/s, up to 5s)`, `(+20% Overall Damage Dealt for 120s)`
  * `(70% weapon dmg + 30% of base enemy HP)`, `(~161 dmg, scales with weapon's level)`, `(Teleports 4m)`
* **Detailed**: separate lines under the rune text.
  * `Cost: 25 Focus to cast, 32 Focus/s to channel`
  * `DMG: 300% WPN + 300% WPN in 2.5m (hits allies), Fire`
  * `Poise DMG: (WPN + 10) × 0.8 per hit · Knockback: ×0.5`
  * `Cast Time: 0.5s · Lockout: 2.2s (combo: 2.3s)`, `Invulnerable: 0–0.73s`, `+10 Poise while casting`
  * `Efficiency: 4.8% WPN DMG per Focus`
* **Every screen that shows runes:** item tooltips, vendors, utility slots and the Runes menu. Where the game shows no
  rune type or cost (vendors, the Runes menu), Detailed adds them first.
* **Live numbers:** kick damage follows your weapon's item level, and a few runes follow your character level.

Choose **Off**, **Brief** or **Detailed** at the end of **Options > Gameplay** (*Rune Details*).

## Good to know

* **WPN** is your weapon's damage. Rune hits take your weapon's damage and element, unless the rune has its own element.
* **Base enemy HP** (throws, Drone Trap, Plague Column): the health a normal enemy of the target's level has (70 at
  level 1, 160 at 11, 360 at 21, 950 at 30), before elite, boss and co-op multipliers. The hit is the same against a
  boss as against a normal enemy of that level.
* **Lockout:** when you can dodge out of the rune. After normal attacks the rune counts as a combo finisher and locks
  you in longer: the *combo* time.
* **Friendly fire:** in co-op every hit can damage a partner (heavily reduced, never lethal). Area damage says whether
  it hits allies.
* Times assume attack speed 1. The numbers are read from the game data when the tooltip is drawn, so they follow
  balance patches.

## Installation

1. Install [MelonLoader](https://github.com/LavaGang/MelonLoader/releases) 0.7.3 or newer and start the game once.
2. Extract `RuneDetails.zip` into the game folder, so that `RuneDetails.dll` ends up in the `Mods` folder.

Uninstall: delete `Mods\RuneDetails.dll`. The settings stay in `UserData\MelonPreferences.cfg` under `[RuneDetails]`.

## Settings

`UserData\MelonPreferences.cfg`, section `[RuneDetails]`:

| Key | Default | |
|---|---|---|
| `Enabled` | `true` | Master switch. |
| `Mode` | `Brief` | `Off`, `Brief` or `Detailed`. Also in Options > Gameplay. |
| `HiddenFormat` | ` <color=#9A9A9A>({extra})</color>` | How the brief note is appended. |
| `AddSettingsRows` | `true` | Add the setting to Options > Gameplay. |
| `Debug` | `false` | Log every rune's details when first shown. |

## Build

`dotnet build -c Release` builds and copies the DLL to `<game>/Mods` (`-p:DeployToGame=false` to skip,
`-p:GameDir=...` for another install). `pwsh ./package.ps1` builds the release zip into `dist/`. How the numbers are
found: [docs/internal.md](docs/internal.md); every rune's text in both modes: [docs/rune-tooltips.html](docs/rune-tooltips.html).

## License

MIT, see [LICENSE](LICENSE).
