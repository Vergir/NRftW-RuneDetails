# Rune Details

MelonLoader mod for No Rest for the Wicked: appends what a rune really does to its tooltip, e.g. `Heals 40 HP`,
`up to 350% weapon dmg/s for 4s`, `70% weapon dmg + 30% of base enemy HP`. Display only; the Quantum simulation is
untouched. Sibling of Enchantment Details (enchantments, gems, facets).

## How it works

Rune texts are fixed strings without numbers (`HeroItemDataAsset.GetDescription()`, no packets). A postfix on that
parameterless method appends what the rune's `HeroRuneData.Actions[0]` really does, walked at runtime with the game's
context-free resolver `AssetBase.Resolve` (`RuneDescriber`, mirrors `tools/rune_extract.py`):

| Rune kind | Shown |
|---|---|
| instant heal / restore | `Heals 40 HP`, `Restores 25 Durability` |
| channelled aura | `Heals 22.5 HP/s to you and allies; drains 32 Focus/s while channelling, up to 5s; needs 25 Focus, spends 5` (the game shows and requires 25 but takes only 5; the 20 is never paid) |
| self buff | `+20% Overall Damage Dealt for 120s`, or `lasts 60s` for infusions |
| melee rune attack | `350% weapon dmg`, `4 hits × 100% weapon dmg`, `3 hits × 150–200% weapon dmg` (per hit, no totals) |
| projectiles / spells | `130/150/200% weapon dmg by charge`, `320–800% weapon dmg by charge`, `1100% weapon dmg in 6m`, `3–10 shots × 80% weapon dmg` (ammo fired) |
| damage over time | `up to 350% weapon dmg/s for 4s` (repeating area, shared damage id: max one hit per second per enemy while inside), `270% weapon dmg every 1.5s for 5s` (repeat ≥ 1s); beams `120% weapon dmg/s; drains 20 Focus/s while channelling` (unique ids, every 1/60-rounded tick hits); no rate without a duration or channel |
| throws | `70% weapon dmg + 30% of base enemy HP` (Throw Axe), `100% weapon dmg + 20% of base enemy HP` (Throw Knife): projectile `DamagePayload` with `ExpectedHealthAmountProvider` = fraction of the target's expected health (typical HP for its level: 70 at 1, 310 at 19, 950 at 30) |
| waves and traps | `200% weapon dmg + 200% wave` (Tremor Slam: a fast moving area hits each enemy about once), `3 traps for 15s, each: 15% of base enemy HP every ~3.5s` (Plague Column) |
| no damage | `knockdown, no damage` (Scream) |
| kicks (Swipe/Turnback/Frontflip Kick, Dropkick) | `≈161 dmg, grows with weapon LVL, not weapon DMG` with a weapon drawn; `≈153 dmg, …` in town (weapons put away: mean over the main-hand weapon sets); `3100% base dmg, …` outside a game |

Heals are before your Healing stat and damage is a multiple of the weapon's Damage stat (runes have no level). Item and
utility-slot tooltips get the details appended in-line; the Runes menu shows only the effect's first sentence (it
splits the text on ". "), so there the details are added after the game has cut the text (`RuneScreen.SetRuneNameText` postfix).
Details: empty charge curves count as ×1 and real ones are sampled over the spell's Min..MaxCharge; `DamageBalanceData`
arrays are read from native memory (0x58-byte stride; the interop struct is smaller); repeats follow
`analysis/cascade_rehit.md`: without `UniqueDamageId` a repeating area hits one enemy at most once per second (damage-id
dedupe, 60 frames), with it every tick hits; tick times are rounded up to 1/60 s frames (0.15s -> 0.17s).

Kicks: their `DamageConfig.CustomDamageProvider` is `ExpectedWeaponDamageAmountProviderNode`; `ResolveOverlapResult`
(@0x05B8C659) adds its amount to BaseDamage and sets `DamageFlags.IgnoreEntityBaseDamage`, so the hit is
multiplier × `StatsSystem.ExpectedStats.GetExpectedWeaponDamage(frame, hero)` = 2 × (1 + 5.8 × (weaponItemLevel − 1) / 29)
(`BalanceConfigData.Weapon.CoreStatScaling[Damage]`). The weapon's Damage stat, facets, attributes and upgrades don't
apply. Verified in game 2026-09-28: Swipe Kick 88 vs 32 for a 60-Damage item-level-9 spear (161 / 60 = 2.7x).
The live value comes from the local `HeroView` (`IsLocalPlayer`: `EntityRef` + `VerifiedFrame`), no hook. In town the
game reports no mainhand (weapons put away) and its own function would fall back to character level (1760 for Frontflip
Kick at level 19), so the mod then evaluates `ItemStatsSystem.GetExpectedWeaponDamage(ctx, itemLevel)` for the items in
`EquipmentSlot.RightHand1..3` and shows their mean.

Research: `analysis/rune_numbers.md` in the workspace root; internals in `docs/internal.md`.

### Self-test

Put `name guid` lines (from `analysis/rune_inventory.csv`) in `UserData/RuneDetails.selftest.txt`; on load the mod
writes every rune's text to `RuneDetails.selftest.out.txt` once the asset database is ready (first scene load; the first
line counts runes without details). Last run 2026-09-29: 262 runes, 10 blank (movement and minion runes, Plague Column),
none odd. Delete both files afterwards.

## Preferences (`UserData/MelonPreferences.cfg`, `[RuneDetails]`)

| Key | Default | |
|---|---|---|
| `Enabled` | `true` | Master switch. |
| `ShowRuneDetails` | `true` | In game: Options > Gameplay > **Show Rune Details**. |
| `AddSettingsRows` | `true` | Add that toggle to Options > Gameplay (after a divider, rows named `RD_*`). |
| `HiddenFormat` | ` <color=#9A9A9A>({extra})</color>` | Appended to the rune text. |
| `Debug` | `false` | Log every rune's details when first shown. |

## Build

`dotnet build -c Release` (post-build copies the DLL to `<game>/Mods`, `-p:DeployToGame=false` to skip;
`-p:GameDir=...` for another install). With HotReload in `<game>/Plugins` the running game picks up new builds.
