# Rune Details: internal notes

How the mod works, how to build and test it, the pitfalls found so far and the open ideas. Player-facing text lives in
`README.md`. Research data is in the workspace root (`analysis/rune_numbers.md`, `analysis/cascade_rehit.md`,
`analysis/rune_inventory.csv`, `tools/rune_extract.py`).

Code map: `src/RuneDetailsMod.cs` (init, `[assembly: HarmonyDontPatchAll]`, self-test call), `src/RuneDescriber.cs`
(the rune walker, live kick damage, re-hit wording, self-test), `src/Patches/RuneDescriptionPatches.cs` (the only
tooltip hook), `src/SettingsRows.cs` + `src/Patches/SettingsPatches.cs` (the `RD_Runes` toggle in Options > Gameplay),
`src/Prefs.cs` (`[RuneDetails]`).

## 1. Workspace, build, test

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
  1. Write `name guid` lines to `<game>/UserData/RuneDetails.selftest.txt`, generated from
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

## 2. Hard-won pitfalls (read these)

1. **Never Harmony-patch an IL2CPP method with a struct parameter passed by `ref`/`in`/`out`, and treat big by-value
   structs the same way.** A parameterless prefix on `InventoryItemInfoElement.PopulateEnchantmentsInfo(Frame, ref
   ItemDescription)` corrupted the argument inside Il2CppInterop's trampoline. That broke every item tooltip while the
   user was in co-op, and it survives unpatching until a restart. Rune hooks today: `HeroItemDataAsset.GetDescription()`
   has no parameters, which is safe.
2. **MelonLoader auto-applies every `[HarmonyPatch]` class in a mod.** Keep `[assembly: HarmonyDontPatchAll]` and call
   `PatchAll` explicitly. Otherwise an "inert/disabled" build still patches.
3. **The interop struct arrays for `DamageBalanceData[]` are wrong after element 0.** The native stride is 0x58; the
   interop struct is smaller. `RuneDescriber.DamageArray` reads the fields straight from native memory: base
   `array.Pointer + 0x20 + i*0x58`, Pct at +0x8, Comp at +0x10.
4. **Empty `FPCurve` means ×1, not ×0.** Check `curve.Count` before evaluating.
5. **Commits in mod repos:** no Claude co-author trailer and no Claude mention. The user's standing rule overrides the
   default attribution.
6. **Never push, create a GitHub repo, or make a release without the user's explicit confirmation.**
7. **Game cheats are compiled out.** `ExecuteCheatPlayerCommand` is a shared `ret` stub, so items can't be spawned for
   testing. Don't inject items into the Quantum sim either: it risks desync and a corrupted save.

## 3. How a rune tooltip is built

The research is in `analysis/rune_numbers.md` and the extractor in `tools/rune_extract.py`. The extractor imports
`enchant_extract.py` and `stats_extract.py`; it has a corrected qdb reader `SafeReader` and names qdb types by field
set. The data table is `analysis/rune_inventory.csv`, 262 rows.

- The rune text is a fixed localized string with no numbers and no packets, from `HeroItemDataAsset.GetDescription()`
  @0x8FB0900: "Slot this Rune into a Bow to gain the X Rune Attack.\n\n<effect>". Its callers are the three views below.
- **Which runes exist for players:** `analysis/rune_obtainability.csv`. 40 of the 262 assets can't be obtained
  (`HeroItemData.CanBeDropped` false and no weapon has them as its built-in special; loot = `SelectionSystem.
  GetLootSelectionPool` @0x5E13BA0, weapon specials = `RunesAPI.Weapon.InitializeStaticRunes` @0x5D8D880). Don't spend
  effort on them: the Afflictions, the aura clones, Gale of Speed, Arrow (the bow's basic attack), Bolt, Curse, Frost
  Blade, Evade (Jump), Rejuvenate, Spectre, legacy Skyfall Shot / bow Throw copies, unnamed test assets.

### The three rune views (traced 2026-10-01)

| View | Where | Text shown | Hook |
|---|---|---|---|
| A. Item tooltip | hovering a rune item: inventory, vendor, rewards, the rune list in the Runes menu | everything, incl. the "Slot this Rune…" line (leading chars trimmed, input tokens processed); panel grows | `InventoryItemInfoElement.PopulateItemDescription(in ItemDescription, …)`: **never hook it** (pitfall 1), so it is the default context |
| B. Utility slot hover | hovering a utility slot on the inventory character panel (utility runes only) | everything; panel grows | `InventoryItemInfoElement.PopulateRuneData(HeroRuneDataAsset, Frame)`: prefix/postfix set the context |
| C. Runes menu inspect page | Runes menu → a weapon (or Utility) → one of its 4 slots | `RuneScreen.SetRuneNameText()` splits the text on `". "` and `"\n"` and shows `parts[1]`: the effect's **first sentence**, without its period. Anything appended to the text is cut (it always follows `". "`) | prefix sets the context (the description postfix then leaves the text alone and keeps the details), postfix appends them to `RuneSlotDescription` |

Both A and B show the name, a Weapon/Utility rune-type row, **one** cost (Focus if any, else Stamina, else Health:
`ActionData.Cost + AdditionalCost` summed per resource) and a price. Compatible weapon classes are never shown (the
prefab's `RuneCompatibleWithSection` is dead), only through the "Slot this Rune into a Bow" line. The weapon tooltip's
Skills row shows slotted runes by name only. `RuneViewContext` (in `RuneDescriptionPatches.cs`) tracks the view, tied
to the frame so a missed reset cannot leak.
- **Asset lookup without a context:** `Il2Cpp.AssetBase.Resolve` is a static `Func<AssetGuid, AssetObject>`.
  `HeroRuneData.Actions[0]` is the whole behaviour.
- **The walker (`RuneDescriber.Walker.Action`) covers:**
  - **state branches first:** `TimelineData.StateInfos[i]` (states back to back, ending at `End`) with `Transitions`
    (`StateIndex`, -1 = end). Bow shots branch: Windup → Resolve on release or → ChargedResolve after the full draw,
    each with its own arrow event, so one press = one arrow. `ChooseBranch` follows every path from
    `StartingStateIndex` and keeps the events of the path with the most damage events.
  - melee `TimelineData.WeaponColliders` (one hit per collider; no re-hit fields exist, dedupe per SubDamageId)
  - `ProjectileEvents` (`OverrideProjectile` → `ProjectileData.StrikeDamageData[]` by charge level; no override means
    the ammo decides). A shot aimed steeply up (`SpawnParams.UseSpawnDirection`, Y ≥ Z/2) from an action that also
    spawns entities is a signal and skipped (Arrowstorm's skyward arrow). Explosion-only projectiles show their
    explosion and radius (Arrowstorm's falling arrows).
  - `SpawnEntityEvents` (cascade / projectile / `MissileTrapData`), counted per spawn (Plague Column: 3 traps). A
    cascade that spawns entities spawns one per instance (`NumberOfInstances × NumberOfBranches`: Arrowstorm 10,
    Converging Flame 6).
  - traps (`Trap()`): only their projectile's ExpectedHealth payload counts, because `MissileTrapData.OnBurst`
    @0x5B711D0 spawns it without a weapon (weapon damage 0). Cadence = FiringDelay + FiringTime (+ ResetCooldown).
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

### "Typical enemy HP" = ExpectedHealth (traced 2026-10-02)

- `ExpectedHealthAmountProvider` → `StatsSystem.ExpectedStats.GetExpectedHealth(f, target)` @0x5E026B0: hero target →
  hero curve; NPC target (any `NpcComponent`: enemies of every type, critters, the training dummy) →
  `GetEnemyStatAtLevel(MaxHealth, LevelSystem.GetLevel(target))` = `BalanceConfigData.EnemyStats.EnemyStatsList`
  MaxHealth curve, linear between keys 1:70, 5:100, 11:160, 15:210, 21:360, 26:650, 30:950 (clamped); else 0.
- That curve is exactly the **base** HP of every NPC (`StatsSystem.Npc.InitializeStats` @0x5E07240 →
  `ExtractNpcBaseStats` adds it as Base). Real HP = (curve + ΣBase) × (1 + ΣPct) × Π(1 + Comp) with affixes:
  Elite +25%, Giant +50%, Boss +300%, bounty elites ×4.5, big bosses ×4–14, shielded normals ×0.67, critters ×0.1;
  co-op Comp +20/25/35% (bosses +25/50/75%) for 2/3/4 players; Path of Resolve ×0.5, Torment ×1.2. **ExpectedHealth
  ignores all of it**: the payload is a % of a plain normal enemy's HP at the target's level, the same against a boss.
- Enemy level = node danger level (most world areas Base 21 / Max 30 in the data, plague/quest driven at runtime;
  town 0 → 1) + difficulty offset (−5…+7); no player-level scaling.
- The payload goes through armor/resistance like a hit (`DamagePayload.Request` @0x5DC54B0 → `DamageAPI.GetDamage`).
  Training dummy (`npcDummyBase`): Normal, affix +10000 HP, level low (L1–3), Plague armor 180 + 20·L. In-game
  2026-10-02: Plague Column needles hit the dummy for 9–10 = 0.15 × 70–85 after ~20% resistance, so the bolt's direct
  hit adds nothing (confirmed).

### Rune hits are SpecialAttack: Rune Damage, not Attack Damage (traced 2026-10-02)

- Projectiles get `DamageFlags.SpecialAttack` when the hero is executing a weapon special (`ProjectileUtils.Spawn`
  @0x5B55F90 reads the hero flags; `DamageAPI.ApplyMetaData` @0x5C09950 ORs it in at hit time). With SpecialAttack,
  `GetWeaponDamageAmount` @0x5C0A890 applies no Attack / Normal Attack Damage (stats 96/97) and `GetDamage` reads
  Rune Damage (99) instead. Charged Attack Damage (98) replaces 97 only with `DamageFlags.ChargeAttack`: melee
  windups (`MeleeAttackWindupData`, hero flag 0x10) and full-draw auto-fire of `BowAttackData` bows
  (`ModifyProjectileOnRelease` @0x5A198B0 sets 0x2000 in state 2, ChargedResolve). Armor Penetration (110) applies to
  every hit with a hero proxy, runes included (`PrecalculateDamage` @0x5C0F9D0 has no flag check).
- In-game 2026-10-02: normal arrows 29–30, Arrowstorm arrows 24–25 on the dummy. Explained (2026-10-03) by the bow,
  not by stats: the player's Fungus Strung Bow's first normal shot is `lobbedShot` with ×1.25 (follow-ups
  `bowAttackFollowUp` ×0.75, run `bowRollAttackA` ×1.0), Arrowstorm's arrows ×1.0; 29.5/24.5 ≈ 1.20 is 1.25 within
  integer display. (An earlier guess blamed an Attack Damage bonus; the character sheet shows none.) Arrowstorm's arrows: real `ProjectileUtils.Spawn` with the bow as weapon, explosion r 1.5 m with
  no falloff from the centre, own damage id per arrow (all 10 can hit). They spawn after the action ends and keep the
  SpecialAttack flag until the hero starts another action.
- Bow distance falloff (`ProjectileData.GetDamageFalloffMultiplier` @0x5B51FA0, bows only): ×1 to 6 m, then
  −10% per metre (additive %), from `BalanceConfigData.Bow.ProjectileDamageOverDistanceCurve` (0:1, 6:1, 15:0.1).
- Quivers add no damage (`QuiverCoreStatType`: AttackStaminaCost, FocusGainOnHit, Weight, Durability).

### Repeating cascades (traced; `analysis/cascade_rehit.md`)

**Timing and dedupe:**
- The sim runs at 60 Hz (`quantumDeterministicConfig UpdateFPS = 60`; frame = 1092 raw FP). The cascade repeat timer
  is reset after each tick, so ticks are `ceil(repeat / frame)` frames apart: 0.05 → 0.067 s, 0.15 → 0.167 s.
  **Verified 2026-10-04 frame by frame** (user's 60 fps videos, no gear), **two clocks plus rounding**:
  - **Cascade repeats** (heals, damage areas): `CascadeInstanceComponent.Update` @0x5A2B160 subtracts
    `Frame.DeltaTime` (1092 raw) and executes at ≤ 0, so a tick every `ceil(raw / 1092)` frames (`CascadeEvery`):
    0.05 s → 4 frames (seen: HP rises at 0.067 / 0.133 / 0.2 / 0.267 s), 0.15 s → 10, 0.25 s → 16, 0.01 s → 1.
  - **Periodic amounts arrive as whole numbers:** Heal Aura's 1.5 HP and Channel's 1.9 Focus both land as +2 per tick
    (plain Heal = exactly 40, so no hidden Healing bonus). Not traced where (not in `HealthPayload.GetAmount`
    @0x5DC7300 nor `HealthComponent.Heal`); the mod rounds half away from zero (ceiling untested).
  - **Channel drains** (`ChargedMagicActionData.UpdateCharge` @0x5A1EC80): the action's segmented delta (1092 or 1093
    raw, 65536 per 60 frames), pays when the timer drops **below** 0, resets to `ChargingCostTime`: 0.05 s → ~16.1/s
    (`DrainTicksPerSecond`). 0.01 s → every frame.
  - Measured: Heal Aura 2 HP × 15/s = 30 HP/s, 32 Focus/s (161 Focus per 5 s channel); Channel 2 Focus × 15/s = 30
    Focus/s (124 in 4.1 s), 48 HP/s (246 HP), ends when it can't take 3 more HP or at 5 s; Frost Stream and Inferno
    30 Focus/s, no cap (ran 6 s until Focus was gone), damage ticks ~50% weapon dmg, crits apply; Frost Stream 6
    numbers per second (0.15 s → 10 frames).
  - History: 30/40 (right rate by accident), 22.5/32 (right clock, missing the rounding), 30/40 (misread video),
    30/32 (1093 step), now 30/32 with the 1092 step + rounding, which also fits Channel.
- **Cost vs AdditionalCost:** the game shows and requires Cost + AdditionalCost (`ActionData.CanAffordAction`
  @0x5A1B1B0) but pays only Cost at the press (`ActionData.Execute` @0x5B84AC0). AdditionalCost is paid when an
  `ApplyAdditionalCost` (43) timeline section activates (`ActionData.Update` @0x5B84F60): 108 of 116 actions with one
  have it. Heal Aura, Channel, Frost Stream and Inferno don't: 5 of the shown 25 is spent (`UnpaidCost`). Focus costs
  and drains of Spell/Special actions go through the Focus Cost stat (`StatsSystem.Hero.GetActionCost` @0x5E03AE0).
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
| moving (`MovementBehaviour` UseVelocity, peak velocity ≥ 5 m/s), not unique | `X% wave`: it passes an enemy within the 1 s dedupe window, so about one hit each (Tremor Slam 30 m/s, Plague Launch 20 m/s). Rotwheel (1.25 m/s) and Fire Wall (empty velocity curves) keep the rate |

### Other data facts (verified from data; not seen in game)

- **Heal / Heal Aura / Pulse of Health:**
  - Heal: +40 HP instantly, 50 Focus.
  - Heal Aura (traced + measured 2026-10-03): 5 Focus at the press (needs 25 to start; the 20 is never paid; seen
    in game: 185 → 180). Nothing heals until `ReleaseMagic` at 0.93 s releases the cascade; then 1.5 HP every 3 frames
    → +2 HP every 4 frames (30 HP/s, × the target's Healing stat) to you and allies within 5 m, while the drain takes
    2 Focus ~16 times a second (32/s, × Focus Cost): ~0.94 HP per Focus. The channel ends on release, at 5 s, or when a drain tick can't
    be paid. In game: 93 → 247 HP, 180 → 19 Focus over one full channel (154 HP for 161 Focus).
  - Channel: 5 Health at the press (needs 25), drains 3 Health per drain tick (~48/s), restores 1.9 → +2 Focus
    every 4 frames (30/s) from 0.93 s; ends on release, at 5 s, or when Focus is full. The Health drain can't kill you (needs HP > cost).
  - Pulse of Health: +25 HP and +20% Max Health for 120 s.
- Unobtainable, so moot (re-verified 2026-10-01): **Gale of Speed** is a byte copy of Damage Surge (+20% Overall
  Damage Dealt, no speed). The four **Afflictions** are identical: one ×1 hit with DamageSchool Cold (Heat Affliction
  too; `DamageAPI.GetDamageSchool` keeps an elemental school), no debuff anywhere in the data. **Deflect, Eagle Eye,
  Life Leech and Slow Aura** share Heal Aura's action.
- **Charred Earth** is droppable (level 21) but named and described as "Armageddon" while its data is a Physical
  two-hit melee attack.
- **Crushing Flurry** text says seven swings; the data has 6 colliders × 80%.
- **Plague Column:** 3 traps, 15 s; each arms after 0.5 s, detects an enemy within ~10 m, fires one homing bolt 0.5 s
  later, stays fired 3 s, repeats; bursts (r 2 m, ~0 damage) after 15 s or when an enemy touches it. The bolt's
  payload: Plague damage = 15% of a typical enemy's HP at the target's level.
- **Scream** = ×0 damage, knockdown only.
- **Charge curves:** an empty curve is ×1; real ones are sampled over `MinCharge..MaxCharge` (Fire Nova 320–800%,
  Chain Lightning 100–200%).
- **Charged Bolt, Fire Wall, Plague Launch and Frost Stream** drop from level 21 (staff/wand), so the user couldn't
  test them at level 19.

## 4. Wording conventions

- **Concise tooltips.** Tooltip space matters.
  - Rune text is appended in grey via `HiddenFormat` (` <color=#9A9A9A>({extra})</color>`).
  - **Never put parentheses inside it**, because everything is already inside one pair.
  - Round distances to whole metres.
  - Show a mean rather than a range where a range would be long (kicks in town).
- **Brief style (2026-10-01):** per-hit damage only, no totals (`3 hits × 150–200% weapon dmg`); parts joined with
  ` + ` and "weapon dmg" said once (`400% weapon dmg + 400% in 2.5m`); fast moving areas as `wave`
  (`200% weapon dmg + 200% wave`); drop `1 shot ×`.
- **Wording they chose or approved:**
  - `≈161 dmg, grows with weapon LVL, not weapon DMG`
  - `70% weapon dmg + 30% of base enemy HP` (was "typical enemy HP"; chosen 2026-10-03)
  - `up to 350% weapon dmg/s for 4s`
  - `Heals 40 HP`
  - `Heals 30 HP/s to you and allies; drains 40 Focus/s while channelling` (numbers corrected 2026-10-03 to
    `Heals 22.5 HP/s to you and allies; drains 32 Focus/s while channelling, up to 5s; needs 25 Focus, spends 5`,
    then shortened on request to `Heals 30 HP/s to you and allies for 32 Focus/s, up to 5s` (measured in game): a channel's drain is
    joined with " for "; "needs 25, spends 5" kept for detailed mode in `_details`)
  - "weapon dmg", not "weapon damage"
- **They like data-backed comparisons**, such as the kick and throw tables, and in-game verification. They will buy
  runes and test on the hub dummy if asked, and they expect a clear test protocol.
- **They want it tested before shipping.** Screenshots are fine when they're not playing something else.

## 5. Open items and ideas

1. **Nexus FAQ (must have):** explain "base enemy HP": the HP of a normal (non-elite) enemy at the *target's* level,
   from the balance curve (70 at level 1, 160 at 11, 360 at 21, 950 at 30), before elite/giant/boss multipliers, co-op
   scaling and realm difficulty. So it is the same number against a boss as against a normal enemy of its level, and it
   does not grow in co-op although enemy HP does (+20/25/35% for 2/3/4 players); armor and resistance still apply.
   Also: should tooltips say rune hits use Rune Damage, not Attack Damage?
2. **Frost Stream / Inferno** spawn several segments. Each segment touching an enemy hits separately, so `/s` is per
   segment. Either trace the overlap geometry or add "per segment".
3. **Continuous side-effects:** optionally add `no stagger` for continuous hits (Fire Wall, Charged Bolt, the beams).
4. **Absolute numbers for weapon-damage runes:** `≈ N dmg` instead of a %, using the equipped weapon's Damage stat
   (`HeroStatsAPI.TryGetCombinedActiveItemStat(f, hero, weapon, ItemStatType.Damage, out FP, …)` @0x5DEB620) and the
   hero's Healing stat for heals.
5. **Heal Aura:** done (traced + measured in game 2026-10-03).
6. **Status-driven runes:** Static and Fire Walk show only `lasts 60s`. Static fires a chain lightning (70%
   ExpectedDamage) every 2.5 s through a `PeriodicModifier` → `CascadePayload`.
7. **Performance:** `LiveExpectedWeaponDamage` runs `FindObjectsOfType<HeroView>()` per kick tooltip. That's fine, but
   it could be cached per frame.
8. **In-game settings row text** for the new mod, and a README written for players.
10. **Lightning Leap** (measured 2026-10-04 on the dummy, normal hit 8): hits 28, 25, 17 (merged into one rising
   number: the game appends hits on the same target while its number is fresh, `DamageNumberView.SetDamageValue(append)`
   via `PlayerControllerView.GetDamageNumberView`, `EntityDamageNumber.CreateTime/LastUpdateTime`), then 23, 23.
   25/23/23 = the three 300% parts (all reach one enemy; strike +1 flat). 17 = a Shock proc (20% base enemy HP, dummy
   ~L3). 28 = not explained: maybe the 100% lightning zone (`ContinuousAreaDamageData` r 3 m, linger 1 s) hitting
   more than once, or a crit. The zone is not in the tooltip yet.
9. **Brief / Detailed modes** (decided 2026-10-03): a setting `Mode` = Off / Brief / Detailed (dropdown row
   `RD_Runes`), shown in **all three views** (the rune screen's `runeSlotDescription` has a ContentSizeFitter and
   grows; only its ". " split was the problem, and we set its text ourselves after the split). Detailed list: per-hit
   damage and element, poise / knockdown, reach / radius, first-hit timing, immunity windows, damage per Focus, the
   real cost ("needs 25, spends 5"), and replacing the "Slot this Rune into a Bow…" line with something like
   `Bow · Rune Attack` (English only). NOT wanted: absolute "base enemy HP" numbers by enemy level.
   **At release time, remind the user:** "hold a key (e.g. Shift) to see the detailed text" as an option for 1.1.
