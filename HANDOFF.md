# Rune Details: handoff (2026-09-29, naming updated 2026-09-30)

This repository (folder `mods/RuneDetails`, formerly `mods/RuneDetails`) started as a **clone of `mods/EnchantTooltip`** at
commit `71c2fd8` (EnchantTooltip 0.4.1). The rune feature grew into its own rabbit hole, so it moves here. The sibling mod
is now **Enchantment Details** (`mods/EnchantmentDetails`, `EnchantmentDetails.dll`, prefs `[EnchantmentDetails]`,
settings rows `ED_*`, 1.0.0 in preparation) and keeps only enchantments, gems and facets.
Your first job is the reverse strip: remove all the enchantment code and keep the runes.

**Do not build and deploy this repo before stripping it.** As cloned, it hooks the same enchantment methods as
Enchantment Details. This really happened on 2026-09-30, when two copies of the enchantment code were loaded at once
(the old `EnchantTooltip.dll` next to `EnchantmentDetails.dll`). Every tooltip line then got two appended notes, and
the other copy's `ET0` probe sentinels leaked into the text as `□ET0□`. Also check `<game>/Mods` for
leftover DLLs after renames.

---

## 1. The strip: what to keep, what to delete, what to rename

**Keep, since these are the rune feature:**

| File | What it is |
|---|---|
| `src/RuneDetails.cs` | The whole rune walker, live kick damage, re-hit wording and the self-test. |
| `src/Patches/RuneDescriptionPatches.cs` | A postfix on `HeroItemDataAsset.GetDescription()`, which is the only hook runes need. |
| `src/Prefs.cs` | Keep `Enabled`, `ShowRuneDetails`, `HiddenFormat` (the grey `(…)` wrapper), `Debug` and `AddSettingsRows`. |
| `src/SettingsRows.cs` + `src/Patches/SettingsPatches.cs` | Keep only the "Show Rune Details" row. |
| `src/EnchantTooltipMod.cs` | Keep it, renamed. It has init, `[assembly: HarmonyDontPatchAll]`, and the self-test call in `OnSceneWasLoaded`. |

**Delete, since these are enchantments:**
- `RangeMerger.cs`, `HiddenNumbers.cs`, `ModifierInfoReader.cs`
- `Showcase.cs`, `ShowcasePool.cs`, and the F10 key in `OnUpdate`
- `Patches/EnchantmentDescriptionPatches.cs`, `PacketCapturePatches.cs`, `KeywordTooltipPatches.cs`
- The prefs `Format`, `ShowRanges`, `ShowFacetNumbers`, `ShowDetailedInfo` and `ShowcaseKey`
- The three enchantment settings rows

**One dependency to cut:** `RuneDetails.Status()` calls `ModifierInfoReader.Label(statTypeName)` to turn a StatType
enum name into a label such as "Overall Damage Dealt". Before deleting `ModifierInfoReader.cs`, copy the small `Label`
function and its override dictionary into RuneDetails.

**Rename:**
- the assembly and namespace `EnchantTooltip` → `RuneDetails` (`RuneDetails.dll`)
- `MelonInfo`
- the preferences category `"EnchantTooltip"` → `"RuneDetails"` (display name "Rune Details")
- the settings-row prefix `ET_` → `RD_` (`SettingsRows.Prefix` and the ids)
- the csproj `AssemblyName`/`RootNamespace`
- the self-test file names `EnchantTooltip.selftest*.txt` → `RuneDetails.selftest*.txt`
- the README

Start the version at 0.1.0. The display name is **"Rune Details"**, the author in `MelonInfo` is lowercase `"vergir"`, and
the planned GitHub repo is `Vergir/NRftW-RuneDetails`. Mirror Enchantment Details' publishing layout:
`docs/internal.md`, `docs/nexus-description.bbcode`, `CHANGELOG.md`, `LICENSE` (MIT), `package.ps1`, and a player-facing
README.

**References:** the csproj can probably drop `Unity.TextMeshPro` and `UnityEngine.UI` after the strip. Check what the
rune settings row still needs: SettingsRows uses TMP through `SettingsItemGUIBase.SettingLabel`.

---

## 2. Workspace, build, test

The workspace root is `C:\Users\vergir\Downloads\nrftw`. Its README has the folder layout. Each mod folder is its own
git repo, and there is **no remote**.

- **Build and deploy:** `dotnet build -c Release`. A post-build step copies the DLL to `<game>/Mods`;
  `-p:DeployToGame=false` skips that. The game is at `C:\Games\Steam\steamapps\common\NoRestForTheWicked`, running
  MelonLoader 0.7.3 on IL2CPP. Interop assemblies are in `MelonLoader/Il2CppAssemblies`.
- **Hot reload:** the dev plugin `Plugins/HotReload.dll` reloads a rebuilt mod in the running game. The log line is
  `[HotReload] Reloaded X (Harmony: N unpatched, M patched)`. The log is `<game>/MelonLoader/Latest.log`.
- **Before launching or closing the game:** check `Get-Process NoRestForTheWicked`. The user plays with friends, and
  other sessions share the game.
- **Rune self-test:** the audit that caught most of the bugs.
  1. Write `name guid` lines to `<game>/UserData/EnchantTooltip.selftest.txt`, generated from
     `analysis/rune_inventory.csv` (all 262 runes).
  2. On the first scene load once the asset database is ready, the mod writes every rune's text to
     `…selftest.out.txt`. Line 1 is `# N runes without details`.
  3. Grep the output for `0–0`, 6-digit numbers, `every 0s`, `ERROR` and parentheses.
  4. Delete both files afterwards.

  The last run (2026-09-29) was clean, with 10 runes blank: movement and minion runes plus Plague Column.
- **Interop naming:**
  - Quantum types live under `Il2CppQuantum`.
  - `Moon.Forsaken` types live under `Il2CppMoon.Forsaken`.
  - **Namespace-less** types live under `Il2Cpp`: `HeroItemDataAsset`, `HeroRuneDataAsset`, `AssetBase`,
    `CascadeInstanceSettings`, `CascadeReactionType`, `CascadeTargetingMode`.
  - `Math` is ambiguous (`Il2CppMoon.Forsaken.Math`), so `RuneDetails` aliases `Math = System.Math`.
  - `EntityView` is ambiguous; use `Il2CppMoon.Forsaken.EntityView`.
  - To use a `Frame` as an `IAssetResolutionContext` parameter, wrap it: `new IAssetResolutionContext(frame.Pointer)`.

## 3. Hard-won pitfalls (read these)

1. **Never Harmony-patch an IL2CPP method with a struct parameter passed by `ref`/`in`/`out`, and treat big by-value
   structs the same way.** A parameterless prefix on `InventoryItemInfoElement.PopulateEnchantmentsInfo(Frame, ref
   ItemDescription)` corrupted the argument inside Il2CppInterop's trampoline. That broke every item tooltip while the
   user was in co-op, and it survives unpatching until a restart. Rune hooks today: `HeroItemDataAsset.GetDescription()`
   has no parameters, which is safe.
2. **MelonLoader auto-applies every `[HarmonyPatch]` class in a mod.** Keep `[assembly: HarmonyDontPatchAll]` and call
   `PatchAll` explicitly. Otherwise an "inert/disabled" build still patches.
3. **The interop struct arrays for `DamageBalanceData[]` are wrong after element 0.** The native stride is 0x58; the
   interop struct is smaller. `RuneDetails.DamageArray` reads the fields straight from native memory: base
   `array.Pointer + 0x20 + i*0x58`, Pct at +0x8, Comp at +0x10.
4. **Empty `FPCurve` means ×1, not ×0.** Check `curve.Count` before evaluating.
5. **Commits in mod repos:** no Claude co-author trailer and no Claude mention. The user's standing rule overrides the
   default attribution.
6. **Never push, create a GitHub repo, or make a release without the user's explicit confirmation.**
7. **Game cheats are compiled out.** `ExecuteCheatPlayerCommand` is a shared `ret` stub, so items can't be spawned for
   testing. Don't inject items into the Quantum sim either: it risks desync and a corrupted save.

## 4. How a rune tooltip is built

The research is in `analysis/rune_numbers.md` and the extractor in `tools/rune_extract.py`. The extractor imports
`enchant_extract.py` and `stats_extract.py`; it has a corrected qdb reader `SafeReader` and names qdb types by field
set. The data table is `analysis/rune_inventory.csv`, 262 rows.

- The rune text is a fixed localized string with no numbers and no packets, from `HeroItemDataAsset.GetDescription()`.
  It has 4 callers: the item tooltip ×2, `PopulateRuneData` and `RuneScreen.SetRuneNameText`.
- `RuneScreen` shows only the paragraph after the first newline. So always append in-line; never add `\n`.
- **Asset lookup without a context:** `Il2Cpp.AssetBase.Resolve` is a static `Func<AssetGuid, AssetObject>`.
  `HeroRuneData.Actions[0]` is the whole behaviour.
- **The walker (`RuneDetails.Walker.Action`) covers:**
  - melee `TimelineData.WeaponColliders`
  - `ProjectileEvents` (`OverrideProjectile` → `ProjectileData.StrikeDamageData[]` by charge level; no override means
    the ammo decides)
  - `SpawnEntityEvents` (cascade / projectile)
  - `SpecialEffectEvents` (heal / focus / durability / status payloads)
  - `ChargedMagicActionData.Cascades`
  - `CascadeStaticData.Events[].Settings`
  - `BowMultishotAttackData.MinShots/MaxShots`
  - `ChargingCost / ChargingCostTime` (drain)
- **The damage multiplier** is `(1 + Σ DamagePercentageModifier) × (1 + Σ DamageCompoundingPercentageModifier)` over
  the layers (the action's `DamageConfig.Damage` plus the collider, strike or cascade layer), × your **weapon Damage
  stat**. Runes have **no level**. Heals are flat before your Healing stat (payloads are requested with
  `ScalingMeta.Default`).

### Kicks (verified in game)

**Mechanism:**
- Dropkick, Frontflip Kick, Swipe Kick and Turnback Kick are the only runes with `DamageConfig.CustomDamageProvider` =
  `ExpectedWeaponDamageAmountProviderNode`. This is not a weapon-damage multiplier.
- `ResolveOverlapResult` (@0x05B8C659) adds that provider's amount to BaseDamage and sets
  `DamageFlags.IgnoreEntityBaseDamage`.
- So a hit = multiplier (×16/×16/×31/×21) × expected weapon damage = `2 × (1 + 5.8 × (weaponItemLevel − 1)/29)`, from
  `BalanceConfigData.Weapon.CoreStatScaling[Damage]`.
- Weapon Damage, facets, attributes and upgrades don't apply.

**In-game test:** Swipe Kick 88 vs a normal hit of 32 on the hub dummy, using a 60-Damage Moonshaft at item level 9.
31 × 5.2 = 161 vs 60, a ratio of 2.7×.

**Live value in the mod:**
- It finds the local `HeroView` (`IsLocalPlayer`) and uses its `EntityRef` and `VerifiedFrame`.
- **Weapon drawn:** `StatsSystem.ExpectedStats.GetExpectedWeaponDamage(frame, hero)`.
- **In town (weapons put away):** `GetEquippedMainhand` is 0, and the game's own function would fall back to character
  level (it gave 1760 for Frontflip at the vendor). So the mod averages
  `ItemStatsSystem.GetExpectedWeaponDamage(ctx, ItemsAPI.GetLevel(item))` over `EquipmentSlot.RightHand1..3`.
- **Outside a game:** `3100% base dmg`.
- `RuntimeCharacterResolver.Asset` is null at runtime, and `PlayerMenuInteractionView` exists only in menus.
- The current weapon-set index lives only inside the `EquipmentComponent` struct (`CurrentRightHandItemIndex`); reaching
  it through the interop is awkward.

**Comparison:**

| Kick | Cost | Multiplier | Damage per Stamina | Notes |
|---|---|---|---|---|
| Frontflip | 20 | ×16 | 0.80 | best per Stamina, earliest hit (0.43 s) |
| Swipe | 40 | ×31 | 0.78 | burst, narrowest window |
| Turnback | 30 | ×21 | 0.70 | knockback 2.0 |
| Dropkick | 30 | ×16 | 0.53 | 0.62–1.52 s active window, poise +2 |

### Throws

Each projectile carries a second `DamagePayload` with `ExpectedHealthAmountProvider`: a fraction of the **target's**
expected health, i.e. typical enemy HP for its level (`EnemyStats.EnemyStatsList` MaxHealth: 70 @1, 140 @9, 310 @19,
950 @30).
- Throw Axe: 50 Focus, 70% weapon damage + 30% typical enemy HP, releases at 0.33 s, poise 2.
- Throw Knife: 25 Focus, 100% weapon damage + 20%, releases at 0.72 s, poise 1.

The knife is about twice as Focus-efficient; the axe is faster and front-loaded. Drone Trap does 60% of typical enemy
HP.

Two points are **inferred:** that the NPC expected health is exactly that curve, and that the payload goes through
armor.

### Repeating cascades (traced; `analysis/cascade_rehit.md`)

**Timing and dedupe:**
- The sim runs at 60 Hz (`quantumDeterministicConfig UpdateFPS = 60`; frame = 1092 raw FP). The cascade repeat timer
  is reset after each tick, so ticks are `ceil(repeat / frame)` frames apart: 0.05 → 0.067 s, 0.15 → 0.167 s.
- **Re-hits depend on `CascadeDamageSettings.UniqueDamageId`, not on `IsContinuousDamage`:**
  - **Not unique:** every tick reuses the cast's damage id, and `DamageResolverComponent.TryRegisterDamageID`
    (@0x5C0B9B0, 10 slots per target) drops the same id on the same target for 60 frames. That means at most one hit
    per second per enemy.
  - **Unique** (and DirectDamage): every tick hits.
- `IsContinuousDamage` only skips poise/stagger build-up, lifesteal, thorns and Focus gain.
- The player's render FPS doesn't matter.

**Tooltip wording:**

| Case | Wording |
|---|---|
| not unique, sub-second repeat | `up to X% weapon dmg/s for Ns` |
| unique | `X%/s` exact, e.g. Bolt 20% per 0.167 s = `120% weapon dmg/s` |
| repeat of 1 s or more | `X% every Ys for Ns` |
| no duration and not channelled (Frigid Arc, Frost Step) | per-hit only |

### Other data facts (verified from data; not seen in game)

- **Heal / Heal Aura / Pulse of Health:**
  - Heal: +40 HP instantly, 50 Focus.
  - Heal Aura: 1.5 HP per 0.05 s (≈30 HP/s) to you and allies within about 5 m, drains 2 Focus per 0.05 s
    (≈40/s); the tooltip cost of 25 is 5 + 20. The split between start and release, and whether healing only happens
    while channelling, are NOT verified.
  - Pulse of Health: +25 HP and +20% Max Health for 120 s.
- **Gale of Speed** applies Damage Surge (+20% Overall Damage Dealt), not speed. The **Affliction** runes are one ×1
  Ice hit with no debuff. **Deflect, Eagle Eye, Life Leech and Slow Aura** share Heal Aura's action (placeholders).
- **Scream** = ×0 damage, knockdown only.
- **Charge curves:** an empty curve is ×1; real ones are sampled over `MinCharge..MaxCharge` (Fire Nova 320–800%,
  Chain Lightning 100–200%).
- **Charged Bolt, Fire Wall, Plague Launch and Frost Stream** drop from level 21 (staff/wand), so the user couldn't
  test them at level 19.

## 5. The user's preferences for this mod

- **Concise tooltips.** Tooltip space matters.
  - Rune text is appended in grey via `HiddenFormat` (` <color=#9A9A9A>({extra})</color>`).
  - **Never put parentheses inside it**, because everything is already inside one pair.
  - Round distances to whole metres.
  - Show a mean rather than a range where a range would be long (kicks in town).
- **Wording they chose or approved:**
  - `≈161 dmg, grows with weapon LVL, not weapon DMG`
  - `70% weapon dmg + 30% of typical enemy HP`
  - `up to 350% weapon dmg/s for 4s`
  - `Heals 40 HP`
  - `Heals 30 HP/s to you and allies; drains 40 Focus/s while channelling`
  - "weapon dmg", not "weapon damage"
- **They like data-backed comparisons**, such as the kick and throw tables, and in-game verification. They will buy
  runes and test on the hub dummy if asked, and they expect a clear test protocol.
- **They want it tested before shipping.** Screenshots are fine when they're not playing something else.

## 6. Open items and ideas

1. **Plague Column** shows nothing. It's a trap entity (`plagueColumnTrapData`) with a BoneBolt projectile (×0.8/×2 by
   charge) and a 0.15 ExpectedHealth payload. The walker's `Entity()` handles cascades and projectiles, not traps.
2. **Frost Stream / Inferno** spawn several segments. Each segment touching an enemy hits separately, so `/s` is per
   segment. Either trace the overlap geometry or add "per segment".
3. **Continuous side-effects:** optionally add `no stagger` for continuous hits (Fire Wall, Charged Bolt, the beams).
4. **Absolute numbers for weapon-damage runes:** `≈ N dmg` instead of a %, using the equipped weapon's Damage stat
   (`HeroStatsAPI.TryGetCombinedActiveItemStat(f, hero, weapon, ItemStatType.Damage, out FP, …)` @0x5DEB620) and the
   hero's Healing stat for heals.
5. **Heal Aura timing:** verify when the 5 and the 20 are charged, and whether it heals after release. The user can
   test with low HP, full Focus, and 1 s vs 2 s channels.
6. **Status-driven runes:** Static and Fire Walk show only `lasts 60s`. Static fires a chain lightning (70%
   ExpectedDamage) every 2.5 s through a `PeriodicModifier` → `CascadePayload`.
7. **Performance:** `LiveExpectedWeaponDamage` runs `FindObjectsOfType<HeroView>()` per kick tooltip. That's fine, but
   it could be cached per frame.
8. **In-game settings row text** for the new mod, and a README written for players.

Memory notes the user's Claude setup keeps (under `~/.claude/projects/C--Users-vergir-Downloads-nrftw/memory/`) cover
these topics too: `nrftw-enchant-tooltip-range.md` (mixed rune + enchant history), `nrftw-melonloader-harmony-pitfalls.md`,
`no-claude-attribution.md`, `confirm-before-publishing.md`.
