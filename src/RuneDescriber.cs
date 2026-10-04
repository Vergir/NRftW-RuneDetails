using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMoon.Forsaken;
using Il2CppPhoton.Deterministic;
using Il2CppQuantum;
using Math = System.Math;

namespace RuneDetails;

/// <summary>
/// What a rune really does, read from its Quantum action data (the rune's tooltip is a fixed text without numbers).
/// Mirrors tools/rune_extract.py; research in analysis/rune_numbers.md. Values are the rune's own numbers:
/// heals before the Healing stat, damage as a multiple of the weapon's Damage stat (runes have no level).
/// Assets are looked up with the game's context-free resolver AssetBase.Resolve.
/// </summary>
internal static class RuneDescriber
{
    private const float One = 65536f;
    /// <summary>What the mod says about one rune: the brief one-liner and the detailed mode's labelled lines.</summary>
    /// <summary>What the mod says about one rune: the brief one-liner, the detailed mode's labelled lines, and the
    /// cost as the detailed mode words it ("5 Focus (needs 25) + 32 Focus/s for up to 5s"). CostNotable: the cost says
    /// more than the game's own cost row (a drain, or a shown cost that is not all paid).</summary>
    internal sealed record RuneText(string? Brief, IReadOnlyList<string> Lines, string? Cost = null, bool CostNotable = false)
    {
        public static readonly RuneText None = new(null, Array.Empty<string>());
    }

    private static readonly Dictionary<string, RuneText> Cache = new();
    private static readonly HashSet<string> Failed = new();
    private static bool _selfTestDone;

    /// <summary>The rune's texts with live values (kick damage) filled in.</summary>
    public static RuneText Describe(Il2Cpp.HeroRuneDataAsset asset)
    {
        string key = asset.name ?? "";
        if (!Cache.TryGetValue(key, out var texts))
        {
            texts = RuneText.None;
            try { texts = Describe(asset.HeroItemData?.TryCast<HeroRuneData>()); }
            catch (Exception e)
            {
                if (Failed.Add(key)) RuneDetailsMod.Log.Warning($"Rune details for {key}: {e.Message}");
            }
            if (Prefs.Debug.Value) RuneDetailsMod.Log.Msg($"rune {key}: {texts.Brief ?? "(none)"} | {string.Join(" | ", texts.Lines)}");
            Cache[key] = texts;
        }
        return Live(texts);
    }

    private static RuneText Live(RuneText t) =>
        t with { Brief = t.Brief == null ? null : ResolveLive(t.Brief, false), Lines = t.Lines.Select(l => ResolveLive(l, true)).ToList() };

    private static RuneText Describe(HeroRuneData? rune)
    {
        if (rune?.Actions == null || rune.Actions.Length == 0) return RuneText.None;
        var action = Resolve<ActionData>(rune.Actions[0].Id);
        return action == null ? RuneText.None : new Walker().Action(action);
    }

    /// <summary>Development audit: if UserData/RuneDetails.selftest.txt exists (lines "name guid", e.g. from
    /// analysis/rune_inventory.csv), describe every listed rune and write UserData/RuneDetails.selftest.out.txt.</summary>
    public static void SelfTest()
    {
        string input = System.IO.Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "RuneDetails.selftest.txt");
        if (_selfTestDone || !System.IO.File.Exists(input) || Il2Cpp.AssetBase.Resolve == null) return;
        var output = new List<string>();
        output.Add($"# live expected weapon damage: {LiveExpectedWeaponDamage()?.ToString() ?? "n/a"}");
        foreach (var line in System.IO.File.ReadAllLines(input))
        {
            int cut = line.LastIndexOf(' ');
            if (cut < 0 || !long.TryParse(line.Substring(cut + 1), out long guid)) continue;
            string text;
            // Both levels on one line: "brief ‖ line 1 ¦ line 2 ¦ ..." (the detailed lines when there are any).
            try
            {
                var texts = Live(Describe(Resolve<HeroRuneData>(new AssetGuid { Value = guid })));
                string brief = texts.Brief ?? "(none)";
                // Detailed as the vendor and the Runes menu show it (cost line first); see RuneLayout.ColourLines.
                text = texts.Lines.Count == 0 ? brief : $"{brief} ‖ {string.Join(" ¦ ", RuneLayout.ColourLines(texts, withCost: true))}";
            }
            catch (Exception e) { text = "ERROR " + e.Message; }
            output.Add($"{line.Substring(0, cut)}	{guid}	{text}");
        }
        int blank = output.Count(l => l.EndsWith("(none)"));
        if (blank == output.Count(l => !l.StartsWith("#"))) return; // asset database not ready yet: retry on the next scene
        _selfTestDone = true;
        output.Insert(0, $"# {blank} runes without details");
        System.IO.File.WriteAllLines(System.IO.Path.ChangeExtension(input, ".out.txt"), output);
        RuneDetailsMod.Log.Msg($"Rune self-test: {output.Count} runes written to RuneDetails.selftest.out.txt");
    }

    private const char Tok = '';

    private static string LevelDamageToken(float mult) => $"{Tok}{mult.ToString(CultureInfo.InvariantCulture)}{Tok}";

    /// <summary>Replace level-damage tokens with the live number for the local hero's equipped weapon
    /// (StatsSystem.ExpectedStats.GetExpectedWeaponDamage = 2 x (1 + 5.8 x (itemLevel-1)/29) in build 29466),
    /// or a generic text outside a game.</summary>
    private static string ResolveLive(string text, bool detailed)
    {
        if (text.IndexOf(Tok) < 0) return text;
        var expected = LiveExpectedWeaponDamage();
        var parts = text.Split(Tok);
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < parts.Length; i++)
        {
            if (i % 2 == 0) { sb.Append(parts[i]); continue; }
            float mult = float.Parse(parts[i], CultureInfo.InvariantCulture);
            // Brief "≈161 dmg, scales with weapon's level"; detailed "DMG: ≈161 (scales with weapon's level)".
            string amount = expected is { } e ? $"≈{Math.Round(mult * e)}" + (detailed ? "" : " dmg") : $"{Pct(mult)} base dmg";
            sb.Append(amount + (detailed ? " (scales with weapon's level)" : ", scales with weapon's level"));
        }
        return sb.ToString();
    }

    /// <summary>Expected weapon damage for the local hero: exact (the game's own hero path) while a weapon is drawn;
    /// in town the weapons are put away and the game has no mainhand, so every main-hand weapon set's item level is
    /// evaluated instead and averaged (tooltips stay short). Null outside a game.</summary>
    private static float? LiveExpectedWeaponDamage()
    {
        try
        {
            // HeroView.IsLocalPlayer gives the hero entity and the current verified frame. Only a hero entity takes the
            // weapon branch of GetExpectedWeaponDamage (an NPC or an unarmed hero falls back to character level).
            foreach (var view in UnityEngine.Object.FindObjectsOfType<HeroView>())
            {
                if (view == null || !view.IsLocalPlayer) continue;
                var frame = view.VerifiedFrame;
                if (frame == null) continue;
                var hero = view.EntityRef;
                if (EquipmentAPI.GetEquippedMainhand(frame, hero).Index != 0 || EquipmentAPI.GetEquippedOffhand(frame, hero).Index != 0)
                {
                    float v = F(StatsSystem.ExpectedStats.GetExpectedWeaponDamage(frame, hero));
                    return v > 0 ? v : null;
                }
                float sum = 0;
                int count = 0;
                foreach (var slot in new[] { EquipmentSlot.RightHand1, EquipmentSlot.RightHand2, EquipmentSlot.RightHand3 })
                {
                    var item = EquipmentAPI.GetItemEntity(frame, slot, hero);
                    if (item.Index == 0) continue;
                    float v = F(ItemStatsSystem.GetExpectedWeaponDamage(new IAssetResolutionContext(frame.Pointer), ItemsAPI.GetLevel(frame, item)));
                    if (v <= 0) continue;
                    sum += v;
                    count++;
                }
                return count > 0 ? sum / count : null;
            }
            return null;
        }
        catch (Exception e)
        {
            if (Failed.Add("live")) RuneDetailsMod.Log.Warning("Live expected weapon damage: " + e.Message);
            return null;
        }
    }

    private static T? Resolve<T>(AssetGuid guid) where T : Il2CppObjectBase
    {
        if (guid.Value == 0) return null;
        var resolve = Il2Cpp.AssetBase.Resolve;
        return resolve?.Invoke(guid)?.TryCast<T>();
    }

    private static float F(FP v) => v.RawValue / One;

    private static string N(float v) => Math.Round(v, 1).ToString(CultureInfo.InvariantCulture);

    /// <summary>Seconds: tick intervals go down to 0.02s, so keep two decimals below one second.</summary>
    private static string S(float v) => Math.Round(v, v < 1 ? 2 : 1).ToString(CultureInfo.InvariantCulture);

    /// <summary>DamageBalanceData arrays: the interop struct is smaller than the native one (0x58 bytes), so indexing the
    /// interop array reads garbage after element 0. Read the percentage fields straight from native memory instead.</summary>
    private const int DamageBalanceDataSize = 0x58, ArrayHeader = 0x20, PctOffset = 0x8, CompOffset = 0x10;

    private static (float Pct, float Comp)[] DamageArray(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<DamageBalanceData>? array)
    {
        if (array == null) return Array.Empty<(float, float)>();
        var result = new (float, float)[array.Length];
        IntPtr basePtr = array.Pointer + ArrayHeader;
        for (int i = 0; i < result.Length; i++)
        {
            IntPtr e = basePtr + i * DamageBalanceDataSize;
            result[i] = (System.Runtime.InteropServices.Marshal.ReadInt64(e + PctOffset) / One,
                         System.Runtime.InteropServices.Marshal.ReadInt64(e + CompOffset) / One);
        }
        return result;
    }

    private static string Pct(float mult) => Math.Round(mult * 100).ToString(CultureInfo.InvariantCulture) + "%";

    private static readonly Regex CamelSplit = new("(?<=[a-z])(?=[A-Z])", RegexOptions.Compiled);

    // Stat labels where the enum name reads badly (the rest is the enum name split at capitals).
    private static readonly Dictionary<string, string> Labels = new()
    {
        ["FocusGainOnHit"] = "Focus on Hit",
        ["FocusGainOnBlock"] = "Focus on Block",
        ["PoiseDamageOnBlock"] = "Poise on Block",
        ["StaminaRegen"] = "Stamina Recovery",
    };

    /// <summary>A StatType enum name as a label, e.g. OverallDamageDealt -> "Overall Damage Dealt".</summary>
    private static string Label(string enumName) =>
        Labels.TryGetValue(enumName, out var l) ? l : CamelSplit.Replace(enumName, " ");

    /// <summary>One damage source of a rune: "{Prefix}{Count}{Pct}[ weapon dmg]{Unit}{Tail}", e.g. "3 hits × " "150–200%",
    /// "up to " "350%" "/s" " for 4s". The parts are joined with " + " and only the first weapon-scaled one says
    /// "weapon dmg": "400% weapon dmg + 400% in 2.5m". Weapon = false: Pct is a complete text (kicks, % of enemy HP).
    /// Total = the weapon-damage multiple one cast deals when it is exactly known (for damage per Focus), else -1.</summary>
    private sealed record Dmg(string Pct, string Count = "", string Unit = "", string Tail = "", string Prefix = "", bool Weapon = true,
        float Total = -1);

    private sealed class Walker
    {
        private readonly List<string> _heals = new(), _buffs = new(), _costs = new();
        private readonly List<Dmg> _damage = new();
        private readonly HashSet<long> _seen = new();
        // Detailed-mode facts.
        private readonly HashSet<DamageSchool> _schools = new();
        private readonly List<string> _healsDetailed = new();
        private bool _knockdown, _melee;
        private int _meleeHits;
        private readonly List<string> _notes = new(); // detailed-only extra lines, e.g. "Mine: …"
        private const string KnockdownOnly = "knockdown, no damage";
        private float _restorePerSecond;
        private string? _restoreUnit;
        private float _firstHit = float.MaxValue;
        private float _healPerSecond, _healInstant;
        private DamageBalanceData _base;
        private bool _charged;
        private float _minCharge = 1, _maxCharge = 1;
        private bool _channelled; // ChargedMagicActionData that drains a resource while held (beams, auras)
        private bool _levelDamage; // DamageConfig.CustomDamageProvider = ExpectedWeaponDamageAmountProviderNode
        private string? _multishot; // "3–10": BowMultishotAttackData fires Min..MaxShots arrows depending on windup

        public RuneText Action(ActionData action)
        {
            if (action.DamageConfig != null)
            {
                _base = action.DamageConfig.Damage;
                // ResolveOverlapResult @0x05B8C659: a custom provider's amount is added to BaseDamage and the hit gets
                // DamageFlags.IgnoreEntityBaseDamage, so the weapon's Damage stat is not used (build 29466).
                _levelDamage = action.DamageConfig.CustomDamageProvider?.TryCast<ExpectedWeaponDamageAmountProviderNode>() != null;
            }
            var magic = action.TryCast<ChargedMagicActionData>();
            _charged = magic != null && magic.ChargeLevels > 1;
            _channelled = magic != null && F(magic.ChargingCostTime) > 0;
            // Blink: TeleportActionData.Distance (4m).
            var teleport = action.TryCast<TeleportActionData>();
            if (teleport != null && F(teleport.Distance) > 0) _buffs.Add($"Teleports {N(F(teleport.Distance))}m");
            var multishot = action.TryCast<BowMultishotAttackData>();
            if (multishot != null && multishot.MaxShots > 1)
                _multishot = multishot.MinShots == multishot.MaxShots ? $"{multishot.MaxShots}" : $"{multishot.MinShots}–{multishot.MaxShots}";
            if (magic != null && F(magic.MaxCharge) > 0)
            {
                _minCharge = F(magic.MinCharge);
                _maxCharge = F(magic.MaxCharge);
            }

            var tl = action.TimelineData;
            if (tl != null)
            {
                ChooseBranch(tl);
                MeleeHits(tl.WeaponColliders);
                if (tl.ProjectileEvents != null)
                    ProjectileEvents(tl.ProjectileEvents, tl.SpawnEntityEvents != null && tl.SpawnEntityEvents.Count > 0);
                if (tl.SpawnEntityEvents != null)
                {
                    // The same entity spawned several times (Plague Column: 3 traps) counts once per spawn.
                    var spawns = new List<(long Guid, int Count)>();
                    foreach (var e in tl.SpawnEntityEvents)
                    {
                        if (e == null || !InBranch(e.spawnTime)) continue;
                        long g = e.entityToSpawn.Id.Value;
                        int i = spawns.FindIndex(x => x.Guid == g);
                        if (i < 0) spawns.Add((g, 1)); else spawns[i] = (g, spawns[i].Count + 1);
                    }
                    foreach (var (g, n) in spawns) Entity(new AssetGuid { Value = g }, n);
                }
                if (tl.SpecialEffectEvents != null)
                    foreach (var e in tl.SpecialEffectEvents)
                        if (e != null) Payloads(e.Payloads, 0f, false);
            }
            if (magic != null)
            {
                if (magic.Cascades != null)
                    foreach (var c in magic.Cascades) Cascade(c, null);
                ChargingCost(magic);
            }

            // Brief: what it does, then what the channel costs to keep it up: "Heals 30 HP/s to you and allies for
            // 32 Focus/s, up to 5s". A drain with nothing before it (Rejuvenate) stands alone.
            var parts = _heals.Concat(_buffs).Append(DamageText(" weapon dmg")).OfType<string>().Distinct().ToList();
            string text = string.Join("; ", parts);
            if (_costs.Count > 0) text = parts.Count > 0 ? $"{text} for {string.Join(" + ", _costs)}" : "drains " + string.Join(" + ", _costs);
            var lines = DetailedLines(action, magic);
            var (costText, notable) = CostText(action, magic);
            return new RuneText(text.Length > 0 ? text : null, lines, costText, notable);
        }

        private string? DamageText(string weaponWord)
        {
            var texts = new List<string>();
            bool named = false;
            foreach (var d in _damage.Distinct())
            {
                string unit = d.Weapon && !named ? weaponWord + d.Unit : d.Unit;
                named |= d.Weapon;
                texts.Add($"{d.Prefix}{d.Count}{d.Pct}{unit}{d.Tail}");
            }
            return texts.Count > 0 ? string.Join(" + ", texts.Distinct()) : null;
        }

        // ---------------- detailed mode (layout A, 2026-10-04: labelled lines, to be tuned in game) ----------------

        private static readonly Dictionary<DamageSchool, string> SchoolNames = new()
        {
            [DamageSchool.Heat] = "Fire", [DamageSchool.Cold] = "Ice", [DamageSchool.Electric] = "Lightning",
            [DamageSchool.Plague] = "Plague", [DamageSchool.Bleed] = "Bleed",
        };

        private List<string> DetailedLines(ActionData action, ChargedMagicActionData? magic)
        {
            var lines = new List<string>();
            var tl = action.TimelineData;

            // Damage, in the weapon-damage shorthand; an element only when the rune sets its own (Physical runes hit
            // with the weapon's element: DamageAPI.GetDamageSchool keeps an elemental school, else the weapon's).
            string? damage = DamageText(" WPN");
            if (damage == KnockdownOnly) damage = null; // the detailed mode says "Knockdown" on its own line
            if (damage != null)
            {
                var own = _schools.Where(SchoolNames.ContainsKey).Select(x => SchoolNames[x]).Distinct().ToList();
                lines.Add("DMG: " + damage + (own.Count > 0 ? ", " + string.Join("/", own) : ""));
            }
            var effects = _healsDetailed.Concat(_buffs).Distinct().ToList();
            if (effects.Count > 0) lines.Add("Effect: " + string.Join("; ", effects));

            float perSecond = magic == null ? 0 : DrainTicksPerSecond(magic.ChargingCostTime.RawValue);

            // Hit: poise and knockback as formulas on the weapon's own values, "Poise: (WPN + 10) × 0.8",
            // "Knockback: ×2" (melee only: for projectiles and areas the action's values are one layer of several).
            // Poise offset in the game's display units (x10). Hit = (weapon poise + offset) x (1 + Poise%).
            if (damage != null && _melee)
            {
                var hit = new List<string>();
                float offset = F(_base.BasePoiseOffset) * 10, factor = 1 + F(_base.PoisePercentageModifier);
                bool hasOffset = Math.Abs(offset) >= 0.5f, hasFactor = Math.Abs(factor - 1) >= 0.005f;
                string plus = hasOffset ? $"WPN {(offset > 0 ? "+" : "−")} {N(Math.Abs(offset))}" : "WPN";
                if (hasOffset || hasFactor)
                    hit.Add("Poise DMG: " + (hasFactor ? $"{(hasOffset ? $"({plus})" : plus)} × {N(factor)}" : plus) + (_meleeHits > 1 ? " per hit" : ""));
                float kick = F(_base.KickbackMulti);
                if (_knockdown || _base.KnockDown) hit.Add("Knockdown");
                // Against the standard push, not the weapon's: a fixed curve x the hit's KickbackMulti x the target's
                // own multiplier (HitReactionResolverSystem.GetKickbackMulti @0x5C32C40). Normal attacks: 0.25-1.5.
                else if (Math.Abs(kick - 1) >= 0.005f) hit.Add($"Knockback: ×{N(kick)}");
                if (hit.Count > 0) lines.Add(string.Join(" · ", hit));
            }
            else if (_knockdown) lines.Add("Knockdown");
            lines.AddRange(_notes);

            // Timings (first hit, lockout, invulnerable windows) are left out until they are modelled: the timeline's
            // section times did not match the game (2026-10-04: Crushing Flurry and Swipe Kick cannot be dodged out of
            // where their Interruptible windows start; attack segments are rescaled at run time). See docs/internal.md.

            // Efficiency per point of what the press actually spends.
            var paid = Totals(action.Cost);
            foreach (var (resource, amount) in Totals(action.AdditionalCost))
                if (HasAdditionalCostSection(action)) paid[resource] = paid.TryGetValue(resource, out float v) ? v + amount : amount;
            if (paid.Count == 1)
            {
                var (resource, spent) = paid.First();
                var known = _damage.Distinct().ToList();
                // Only when every part is a known multiple of weapon damage (no rates, ranges or % of enemy HP).
                if (known.Count > 0 && known.All(d => d.Weapon && d.Total > 0 && !d.Tail.Contains("enemy HP")) && spent > 0)
                    lines.Add($"Efficiency: {N(known.Sum(d => d.Total) / spent * 100)}% WPN DMG per {resource}");
                else if (_healInstant > 0 && spent > 0 && known.Count == 0 && _buffs.Count == 0) // a buff is part of the price
                    lines.Add($"Efficiency: {N(_healInstant / spent)} HP per {resource}");
            }
            if (_healPerSecond > 0 && perSecond > 0 && magic?.ChargingCost != null)
            {
                float drain = Totals(magic.ChargingCost).Values.Sum() * perSecond;
                if (drain > 0) lines.Add($"Efficiency: {Math.Round(_healPerSecond / drain, 2).ToString(CultureInfo.InvariantCulture)} HP per Focus");
            }
            if (_restorePerSecond > 0 && perSecond > 0 && magic?.ChargingCost != null)
            {
                var drains = Totals(magic.ChargingCost);
                if (drains.Count == 1)
                {
                    var (resource, amount) = drains.First();
                    lines.Add($"Efficiency: {Math.Round(_restorePerSecond / (amount * perSecond), 2).ToString(CultureInfo.InvariantCulture)} {_restoreUnit} per {resource}");
                }
            }
            return lines;
        }

        /// <summary>"100 Focus", or "5 Focus (needs 25)" when the shown cost is not all paid, plus the drain "+ 32 Focus/s
        /// for up to 5s". Notable when it says more than the game's cost row. The game shows Cost + AdditionalCost and
        /// requires both to start (ActionData.CanAffordAction @0x5A1B1B0) but pays only Cost at the press
        /// (ActionData.Execute @0x5B84AC0); AdditionalCost is paid when an ApplyAdditionalCost (43) timeline section
        /// activates (ActionData.Update @0x5B84F60). Heal Aura, Channel, Frost Stream and Inferno have none.</summary>
        private (string? Text, bool Notable) CostText(ActionData action, ChargedMagicActionData? magic)
        {
            var shown = Totals(action.Cost);
            foreach (var (r, a) in Totals(action.AdditionalCost)) shown[r] = shown.TryGetValue(r, out float v) ? v + a : a;
            var paid = Totals(action.Cost);
            if (HasAdditionalCostSection(action))
                foreach (var (r, a) in Totals(action.AdditionalCost)) paid[r] = paid.TryGetValue(r, out float v) ? v + a : a;
            var parts = new List<string>();
            bool notable = false;
            foreach (var (resource, total) in shown)
            {
                paid.TryGetValue(resource, out float spent);
                if (Math.Abs(spent - total) < 0.01f) parts.Add($"{N(total)} {resource}");
                else { parts.Add($"{N(spent)} {resource} (needs {N(total)})"); notable = true; }
            }
            float perSecond = magic == null ? 0 : DrainTicksPerSecond(magic.ChargingCostTime.RawValue);
            float cap = _channelled ? ChannelCap(magic?.TimelineData) : 0;
            foreach (var (resource, amount) in Totals(magic?.ChargingCost))
            {
                parts.Add($"{Math.Round(amount * perSecond)} {resource}/s{(cap > 0 ? $" for up to {S(cap)}s" : "")}");
                notable = true;
            }
            return (parts.Count > 0 ? string.Join(" + ", parts) : null, notable);
        }

        private static bool HasAdditionalCostSection(ActionData action)
        {
            var sections = action.TimelineData?.Sections;
            if (sections != null)
                foreach (var sec in sections)
                    if (sec != null && sec.Id == QuantumActionSectionId.ApplyAdditionalCost) return true;
            return false;
        }

        private static float Mult(DamageBalanceData a, DamageBalanceData b) =>
            (1 + F(a.DamagePercentageModifier) + F(b.DamagePercentageModifier))
            * (1 + F(a.DamageCompoundingPercentageModifier) + F(b.DamageCompoundingPercentageModifier));

        private float Mult((float Pct, float Comp) layer) =>
            (1 + F(_base.DamagePercentageModifier) + layer.Pct) * (1 + F(_base.DamageCompoundingPercentageModifier) + layer.Comp);

        private void MeleeHits(Il2CppSystem.Collections.Generic.List<QuantumWeaponCollider>? colliders)
        {
            if (colliders == null || colliders.Count == 0) return;
            var mults = new List<float>();
            foreach (var c in colliders)
            {
                if (c == null || !InBranch(c.StartTime)) continue;
                // ActionData.ResolveWeaponColliderDamage: the collider's own strike data replaces the action's.
                var strike = c.UseAlternateStrikeData ? c.StrikeData : _base;
                mults.Add(Mult(strike, default));
                _schools.Add(strike.DamageSchool);
                _knockdown |= strike.KnockDown;
                _firstHit = Math.Min(_firstHit, F(c.StartTime));
                _melee = true;
                _meleeHits++;
            }
            if (mults.Count == 0) return;
            if (_levelDamage)
            {
                // Kicks: base = expected weapon damage for the weapon's item level, weapon Damage ignored.
                // Resolved per display (depends on the equipped weapon), see LevelDamageToken / Live.
                string each = LevelDamageToken(mults.Max());
                _damage.Add(new Dmg(mults.Count == 1 ? each : $"{mults.Count} hits × {each}", Weapon: false));
                return;
            }
            // Per hit only: a total next to it costs space and adds little.
            _damage.Add(new Dmg(Range(mults.Min(), mults.Max()), mults.Count == 1 ? "" : $"{mults.Count} hits × ", Total: mults.Sum()));
        }

        /// <summary>"150%" or "150–200%".</summary>
        private static string Range(float lo, float hi) =>
            Pct(lo) == Pct(hi) ? Pct(hi) : $"{Pct(lo).TrimEnd('%')}–{Pct(hi)}";

        private float[]? _stateEnds;
        private HashSet<int>? _branch;
        private List<int>? _path; // the chosen branch in order, for timeline -> real time

        /// <summary>A timeline is a row of states (StateInfos[i] ends at End; states lie back to back) linked by
        /// transitions. Some are alternatives: a bow shot's Windup goes to Resolve on release or to ChargedResolve after
        /// the full draw, each with its own arrow, so one press fires one arrow, not two. Follow every path from the
        /// starting state (StateIndex -1 ends the action) and keep only the events of the path with the most damage
        /// events. Single-state timelines are not filtered.</summary>
        private void ChooseBranch(TimelineActionData tl)
        {
            var infos = tl.StateInfos;
            if (infos == null || infos.Length < 2) return;
            _stateEnds = new float[infos.Length];
            for (int i = 0; i < infos.Length; i++) _stateEnds[i] = infos[i] == null ? 0 : F(infos[i].End);
            var paths = new List<List<int>>();
            void Walk(int state, List<int> path)
            {
                if (paths.Count > 64) return;
                if (state < 0 || state >= infos.Length || path.Contains(state)) { paths.Add(new List<int>(path)); return; }
                path.Add(state);
                var next = new List<int>();
                var transitions = infos[state]?.Transitions;
                if (transitions != null)
                    foreach (var t in transitions)
                        if (t != null && !next.Contains(t.StateIndex)) next.Add(t.StateIndex);
                if (next.Count == 0) paths.Add(new List<int>(path));
                foreach (var t in next) Walk(t, path);
                path.RemoveAt(path.Count - 1);
            }
            Walk(tl.StartingStateIndex, new List<int>());
            if (paths.Count < 2) return;
            _path = paths.OrderByDescending(p => DamageEvents(tl, p)).First();
            _branch = new HashSet<int>(_path);
        }

        private int StateAt(float time)
        {
            for (int i = 0; i < _stateEnds!.Length; i++)
                if (time < _stateEnds[i]) return i;
            return _stateEnds.Length - 1;
        }

        private bool InBranch(FP time) => _branch == null || _branch.Contains(StateAt(F(time)));

        /// <summary>Seconds from the press for a timeline time: states lie back to back on the timeline but a branch
        /// jumps (Fire Arrow's full draw goes from Windup, ending 2.17s, to ChargedResolve, starting 3.5s), so add up the
        /// lengths of the branch's earlier states. -1 if the time is not on the branch.</summary>
        private float Real(float time)
        {
            if (_stateEnds == null || _path == null) return time;
            int state = StateAt(time);
            int at = _path.IndexOf(state);
            if (at < 0) return -1;
            float offset = 0;
            for (int i = 0; i < at; i++)
            {
                int st = _path[i];
                offset += _stateEnds[st] - (st > 0 ? _stateEnds[st - 1] : 0);
            }
            return offset + time - (state > 0 ? _stateEnds[state - 1] : 0);
        }

        private int DamageEvents(TimelineActionData tl, List<int> path)
        {
            int n = 0;
            if (tl.WeaponColliders != null) foreach (var c in tl.WeaponColliders) if (c != null && path.Contains(StateAt(F(c.StartTime)))) n++;
            if (tl.ProjectileEvents != null) foreach (var e in tl.ProjectileEvents) if (e != null && path.Contains(StateAt(F(e.SpawnTime)))) n++;
            if (tl.SpawnEntityEvents != null) foreach (var e in tl.SpawnEntityEvents) if (e != null && path.Contains(StateAt(F(e.spawnTime)))) n++;
            return n;
        }

        private void ProjectileEvents(Il2CppSystem.Collections.Generic.List<ActionProjectileEvent> events, bool spawnsEntities)
        {
            int count = 0, ammoShots = 0;
            Dmg? text = null;
            foreach (var e in events)
            {
                if (e == null || !InBranch(e.SpawnTime)) continue;
                // A shot aimed steeply up from an action that also spawns something is a signal (Arrowstorm's skyward
                // arrow calls the rain): it hits nothing.
                var dir = e.SpawnParams.SpawnDirection;
                if (spawnsEntities && e.SpawnParams.UseSpawnDirection && F(dir.Y) >= 0.5f * Math.Abs(F(dir.Z))) continue;
                var p = Resolve<ProjectileData>(e.OverrideProjectile.Id);
                _firstHit = Math.Min(_firstHit, F(e.SpawnTime));
                _schools.Add(_base.DamageSchool);
                if (p == null) { ammoShots++; continue; } // fires the equipped ammo: only the action's own layer is known
                count++;
                text ??= ProjectileText(p);
            }
            if (text != null) _damage.Add(count > 1 ? text with { Count = $"{count} × ", Total = text.Total > 0 ? text.Total * count : -1 } : text);
            if (ammoShots > 0)
            {
                string shots = _multishot ?? ammoShots.ToString();
                float each = Mult(_base, default);
                _damage.Add(new Dmg(Pct(each), shots == "1" ? "" : $"{shots} shots × ", Total: _multishot == null ? each * ammoShots : -1));
            }
        }

        private Dmg? ProjectileText(ProjectileData p)
        {
            var strikes = DamageArray(p.StrikeDamageData);
            string? health = ExpectedHealthDamage(p.Payloads);
            var expl = DamageArray(p.ExplosionDamageData);
            // An explosion with radius 0 hits nothing (Regurgitate's +50%): OnCollisionWithDamageReceiver explodes only
            // with ExplodeOnHit and ExplosionRadius > 0.
            float blast = F(p.ExplosionRadius);
            bool explodes = expl.Length > 0 && blast > 0 && (p.ExplodeOnExpiration || (int)p.ExplodeOnHit != 0);
            string mine = Mine(p);
            if (strikes.Length == 0)
            {
                // Explosion only (Arrowstorm's falling arrows burst on the ground in 1.5m).
                if (explodes)
                {
                    float boom = Mult(expl[expl.Length - 1]);
                    return new Dmg(Pct(boom), Tail: (blast >= 1 ? $" in {N(blast)}m" : "") + (health == null ? "" : " + " + health) + mine, Total: boom);
                }
                return health == null ? null : new Dmg(health + mine, Weapon: false);
            }
            var mults = strikes.Select(Mult).ToList();
            string pct, tail = "";
            if (mults.Distinct().Count() == 1) pct = Pct(mults[0]);
            else if (_charged) { pct = $"{string.Join("/", mults.Select(m => Pct(m).TrimEnd('%')))}%"; tail = " by charge"; }
            else pct = Range(mults.Min(), mults.Max());
            float total = mults.Distinct().Count() == 1 ? mults[0] : -1;
            if (explodes)
            {
                float boom = Mult(expl[expl.Length - 1]);
                tail += $" + {Pct(boom)} in {N(blast)}m";
                if (total > 0) total += boom;
            }
            if (health != null) tail += " + " + health;
            return new Dmg(pct, Tail: tail + mine, Total: total);
        }

        /// <summary>A projectile that leaves a mine where it stops (Drone Trap: ProjectileData.OnStopInstantiation ->
        /// ExplosiveTrapData, one blast of 25% of base enemy HP in 2.5m when an enemy comes near, waits up to 240s,
        /// AllowFriendlyFire). Returns the brief tail; the detailed mode gets a "Mine:" line.</summary>
        private string Mine(ProjectileData p)
        {
            var trap = Resolve<ExplosiveTrapData>(p.OnStopInstantiation.Id);
            if (trap == null) return "";
            string? health = ExpectedHealthDamage(trap.PayloadData);
            float r = F(trap.ExplosionRadius), life = F(trap.MaxTimeActive);
            var facts = new List<string>();
            if (life > 0) facts.Add($"waits up to {S(life)}s");
            if (trap.AllowFriendlyFire) facts.Add("also hits allies");
            if (facts.Count > 0) _notes.Add("Mine: " + string.Join(", ", facts));
            return health == null ? "" : $" + {health.Replace(" of base enemy HP", "")} mine{(r >= 1 ? $" in {N(r)}m" : "")}";
        }

        /// <summary>DamagePayloads whose amount is ExpectedHealthAmountProvider: a fraction of the TARGET's expected
        /// health (StatsSystem.ExpectedStats.GetExpectedHealth(f, target) = the typical HP of an enemy of that level,
        /// 70 at level 1, 310 at 19, 950 at 30), independent of the weapon. Throw Axe 30%, Throw Knife 20%.</summary>
        private static string? ExpectedHealthDamage(PayloadData? data)
        {
            if (data?.Payloads == null) return null;
            float sum = 0;
            foreach (var p in data.Payloads)
            {
                var provider = p?.TryCast<DamagePayload>()?.Amount?.TryCast<ExpectedHealthAmountProvider>();
                var curve = provider?.ScalingData.Scaling;
                if (curve != null) sum += F(curve.Evaluate(new FP { RawValue = 0 }));
            }
            return sum > 0 ? $"{Pct(sum)} of base enemy HP" : null;
        }

        private void Entity(AssetGuid guid, int count = 1)
        {
            if (guid.Value == 0 || !_seen.Add(guid.Value)) return;
            var asset = Resolve<AssetObject>(guid);
            if (asset == null) return;
            var trap = asset.TryCast<MissileTrapData>();
            if (trap != null)
            {
                Trap(trap, count);
                return;
            }
            var cascade = asset.TryCast<CascadeStaticData>();
            if (cascade != null)
            {
                if (cascade.Events != null)
                    foreach (var ev in cascade.Events)
                        if (ev != null) Cascade(ev.Settings, cascade);
                return;
            }
            var projectile = asset.TryCast<ProjectileData>();
            if (projectile != null)
            {
                var text = ProjectileText(projectile);
                if (text != null) _damage.Add(count > 1 ? text with { Count = $"{count} × ", Total = text.Total > 0 ? text.Total * count : -1 } : text);
            }
        }

        /// <summary>A missile trap (Plague Column): armed after MinTimeActive, it shoots its projectile at an enemy it
        /// detects, FiringDelay after detecting, then stays in the fired state for FiringTime before it looks again
        /// (TrapData.OnUpdateEntityInstance @0x5B73950); gone after MaxTimeActive. Its projectiles are spawned without
        /// a weapon (MissileTrapData.OnBurst @0x5B711D0), so only their % of base enemy HP payload does damage.</summary>
        private void Trap(MissileTrapData trap, int count)
        {
            var projectile = Resolve<ProjectileData>(trap.ProjectileData.Id);
            string? health = projectile == null ? null : ExpectedHealthDamage(projectile.Payloads);
            if (health == null) return;
            float every = F(trap.FiringDelay) + F(trap.FiringTime) + Math.Max(0, F(trap.ResetCooldown));
            float life = F(trap.MaxTimeActive);
            int burst = Math.Max(1, trap.BurstCount);
            // "3 traps × 15% of base enemy HP every ~3.5s for 15s"
            string traps = count > 1 ? $"{count} traps × " : "trap: ";
            string shots = burst > 1 ? $"{burst} × {health}" : health;
            _damage.Add(new Dmg($"{traps}{shots}{(every > 0 ? $" every ~{S(every)}s" : "")}{(life > 0 ? $" for {S(life)}s" : "")}", Weapon: false));
        }

        private void Cascade(Il2Cpp.CascadeInstanceSettings s, CascadeStaticData? owner)
        {
            float repeat = CascadeEvery(s.ExecutionRepeatTime.RawValue); // seconds between ticks, 0 = once
            switch (s.Reaction)
            {
                case Il2Cpp.CascadeReactionType.DamageArea:
                case Il2Cpp.CascadeReactionType.DirectDamage:
                {
                    float m = Mult(_base, s.Damage.Damage);
                    _schools.Add(s.Damage.Damage.DamageSchool != DamageSchool.None ? s.Damage.Damage.DamageSchool : _base.DamageSchool);
                    _knockdown |= s.Damage.Damage.KnockDown;
                    float lo = 1, hi = 1;
                    var curve = s.Damage.DamageMultiplierDueToCharge;
                    if (curve != null && curve.Count > 0) // an empty curve means x1
                    {
                        // Sampled over the spell's charge range; uncharged actions release at full charge.
                        hi = F(curve.Evaluate(new FP { RawValue = (long)(_maxCharge * One) }));
                        lo = _charged || _minCharge < _maxCharge ? F(curve.Evaluate(new FP { RawValue = (long)(_minCharge * One) })) : hi;
                    }
                    if (m * hi < 0.005f)
                    {
                        if (s.Damage.Damage.KnockDown || _base.KnockDown)
                        {
                            _damage.Add(new Dmg(KnockdownOnly, Weapon: false)); // Scream
                            _knockdown = true;
                        }
                        break;
                    }
                    bool byCharge = Pct(m * lo) != Pct(m * hi);
                    var d = new Dmg(Range(m * lo, m * hi), Tail: byCharge ? " by charge" : "", Total: byCharge || repeat > 0 ? -1 : m * hi);
                    float duration = owner == null ? 0 : F(owner.InstanceDuration);
                    bool unique = s.Damage.UniqueDamageId || s.Reaction == Il2Cpp.CascadeReactionType.DirectDamage;
                    bool moving = owner != null && owner.MovementBehaviour == CascadeMovementBehaviour.UseVelocity
                        && Speed(owner.MovementSettings) >= WaveSpeed;
                    if (moving && !unique && repeat > 0)
                    {
                        // A fast travelling area (tremor waves: 20-30 m/s) with a shared damage id passes an enemy well
                        // within the 1s damage-id window, so it hits each enemy about once: a per-second rate would mislead.
                        // Slow ones (Rotwheel 1.25 m/s) keep the rate; Fire Wall is UseVelocity with empty curves.
                        _damage.Add(d with { Tail = d.Tail + " wave" });
                        break;
                    }
                    d = Repeat(d, m * hi, repeat, duration, unique, _channelled);
                    float radius = owner == null ? 0 : F(owner.InstanceRadius) * (s.DamageArea.Shape == null ? 1 : F(s.DamageArea.Shape.Radius));
                    if (radius >= 2) d = d with { Tail = d.Tail + $" in {N(radius)}m" };
                    _damage.Add(d);
                    break;
                }
                case Il2Cpp.CascadeReactionType.SpecialEffect:
                {
                    var fx = s.SpecialEffect;
                    if (fx == null) break;
                    bool allies = ((int)fx.Targeting & (int)Il2Cpp.CascadeTargetingMode.Friendlies) != 0;
                    float reach = owner == null ? 0 : F(owner.InstanceRadius) * (s.DamageArea.Shape == null ? 1 : F(s.DamageArea.Shape.Radius));
                    Payloads(fx.Payload, repeat, allies, reach);
                    break;
                }
                case Il2Cpp.CascadeReactionType.SpawnEntity:
                    Entity(s.EntityToSpawn.Id, owner == null ? 1 : Math.Max(1, owner.NumberOfInstances) * Math.Max(1, owner.NumberOfBranches));
                    break;
            }
        }

        /// <summary>How often a repeating cascade damages one enemy (analysis/cascade_rehit.md, traced 2026-09-29):
        /// the sim runs at 60 Hz and the repeat timer is reset, so ticks are ceil(repeat / frame) frames apart
        /// (0.05s -> 0.067s, 0.15s -> 0.167s). Without UniqueDamageId every tick reuses the cast's damage id and
        /// DamageResolverComponent.TryRegisterDamageID drops the same id on the same target for 60 frames, so the enemy
        /// is hit at most once per second. With UniqueDamageId (and DirectDamage) every tick is a new hit.
        /// IsContinuousDamage does not affect this (it only skips poise, lifesteal, focus gain...).</summary>
        private const float WaveSpeed = 5f;

        /// <summary>Peak speed (m/s) of a cascade's velocity curves, sampled over their range; empty curves are 0.</summary>
        private static float Speed(CascadeMovementSettings? settings)
        {
            var v = settings?.Velocity;
            if (v == null) return 0;
            float At(FPCurve? c, float t) => c == null || c.Count == 0 ? 0 : F(c.Evaluate(new FP { RawValue = (long)(t * One) }));
            float max = 0;
            for (int i = 0; i <= 4; i++)
            {
                float t = i / 4f, x = At(v.X, t), y = At(v.Y, t), z = At(v.Z, t);
                max = Math.Max(max, (float)Math.Sqrt(x * x + y * y + z * z));
            }
            return max;
        }

        /// <summary>Raw FP time a sim frame takes off a cascade's repeat timer (Frame.DeltaTime, 65536/60 truncated):
        /// CascadeInstanceComponent.Update @0x5A2B160 executes at &lt;= 0 and resets the timer to the interval, so a tick
        /// comes every ceil(interval / 1092) frames, at least one. Verified 2026-10-04 frame by frame in the user's 60 fps
        /// video (Heal Aura, no gear): a heal every 0.067s (4 frames, 15/s) for the 0.05s (3277 raw) timer.</summary>
        private const double CascadeFrameRaw = 1092;

        /// <summary>Seconds between a cascade's ticks: 0.05s -> 4 frames (15/s), 0.15s -> 10, 0.01s -> every frame. 0 = no repeat.</summary>
        private static float CascadeEvery(long raw) => raw <= 0 ? 0 : Math.Max(1, (int)Math.Ceiling(raw / CascadeFrameRaw)) / 60f;

        /// <summary>Ticks per second of a channelling drain, on a different clock than the heal: ChargedMagicActionData.
        /// UpdateCharge @0x5A1EC80 runs its timer on the action's segmented delta (1092 or 1093 raw, 65536 per 60 frames,
        /// ActionSystem.ResolveSegmentedActionDeltaTime @0x5B997C0), pays when it drops below 0 and resets it to
        /// ChargingCostTime: 0.05s (3276 raw) -> 3 or 4 frames, ~16.1/s. Measured 2026-10-03: Heal Aura drained 161 Focus
        /// over its 5s channel = 32/s (2 per tick), while healing 20 times a second. At attack speed 1 (it rescales).</summary>
        private static float DrainTicksPerSecond(long raw)
        {
            if (raw <= 0) return 0;
            long timer = 0;
            int ticks = 0;
            for (long i = 0; i < 600; i++)
            {
                timer -= (i + 1) * 65536 / 60 - i * 65536 / 60;
                if (timer < 0) { ticks++; timer = raw; }
            }
            return ticks / 10f;
        }

        private static Dmg Repeat(Dmg d, float mult, float every, float duration, bool unique, bool channelled)
        {
            // Without a duration or a channel nothing says how long it keeps ticking (Frigid Arc, Frost Step): per hit only.
            if (every <= 0 || (duration <= 0 && !channelled)) return d;
            string span = duration > 0 ? $" for {S(duration)}s" : ""; // no duration: channelled, the drain says so
            if (every >= 1f) return d with { Tail = d.Tail + $" every {S(every)}s{span}" };
            // Sub-second repeats as a rate. Shared damage id: capped at one hit per second per enemy, reached only
            // while the enemy stays inside ("up to"). Unique ids: every tick hits, exact.
            return unique
                ? new Dmg(Pct(mult / every), Unit: "/s", Tail: span)
                : new Dmg(Pct(mult), Unit: "/s", Tail: span, Prefix: "up to ");
        }

        private void Payloads(PayloadData? data, float repeat, bool allies, float reach = 0)
        {
            if (data?.Payloads == null) return;
            foreach (var p in data.Payloads)
            {
                if (p == null) continue;
                var status = p.TryCast<StatusPayload>();
                if (status != null) { Status(status); continue; }

                string? unit = p.TryCast<HealthPayload>() != null ? "HP"
                    : p.TryCast<FocusPayload>() != null ? "Focus"
                    : p.TryCast<StaminaPayload>() != null ? "Stamina"
                    : p.TryCast<DurabilityPayload>() != null ? "Durability" : null;
                if (unit == null) continue;
                // Only flat amounts: other providers are fractions of some stat and would need the live hero.
                var provider = p.TryCast<PayloadWithAmount>()?.Amount?.TryCast<DefaultAmountProvider>();
                var curve = provider?.ScalingData.Scaling;
                if (curve == null) continue;
                float v = F(curve.Evaluate(new FP { RawValue = 0 })); // runes are always requested at level 0
                // Each periodic amount lands as a whole number (measured 2026-10-04, no gear, Heal 40 exact: Heal Aura's
                // 1.5 HP and Channel's 1.9 Focus both arrive as +2 per tick). Where the game rounds is not traced, and
                // round-half-up vs ceiling is untested; both give 2 here.
                if (repeat > 0) v = (float)Math.Round(v, MidpointRounding.AwayFromZero);
                if (v <= 0) continue;
                string who = allies ? " to you and allies" : "";
                string verb = unit == "HP" ? "Heals" : "Restores";
                string amount = repeat > 0 ? $"{verb} {N(v / repeat)} {unit}/s" : $"{verb} {N(v)} {unit}"; // repeat = seconds per tick
                _heals.Add(amount + who);
                _healsDetailed.Add(amount + (allies && reach >= 1 ? $" to you and allies within {N(reach)}m" : who));
                if (unit == "HP") { if (repeat > 0) _healPerSecond += v / repeat; else _healInstant += v; }
                else if (repeat > 0) { _restorePerSecond += v / repeat; _restoreUnit = unit; }
            }
        }

        private void Status(StatusPayload status)
        {
            var md = Resolve<ModifierData>(status.ModifierRef.Id);
            if (md?.Modifiers == null) return;
            var effects = new List<string>();
            foreach (var m in md.Modifiers)
            {
                var stat = m?.TryCast<StatModifier>();
                var curve = stat?.ScalingData.Scaling;
                if (stat == null || curve == null) continue;
                float v = F(curve.Evaluate(new FP { RawValue = 0 }));
                if (Math.Abs(v) < 0.0001f) continue;
                string label = Label(stat.StatType.ToString());
                string sign = v > 0 ? "+" : "";
                effects.Add(stat.ModificationType == StatModificationType.Base ? $"{sign}{N(v)} {label}" : $"{sign}{Pct(v)} {label}");
            }
            var time = md.Duration?.TryCast<ModifierTimeDuration>();
            if (effects.Count == 0)
            {
                // Infusions, light, markers: nothing numeric, but how long it lasts is worth knowing.
                if (time != null) _buffs.Add($"lasts {N(F(time.Duration))}s");
                return;
            }
            _buffs.Add(string.Join(", ", effects) + (time != null ? $" for {N(F(time.Duration))}s" : ""));
        }

        private void ChargingCost(ChargedMagicActionData magic)
        {
            float perSecond = DrainTicksPerSecond(magic.ChargingCostTime.RawValue);
            var costs = magic.ChargingCost?.Costs;
            if (perSecond <= 0 || costs == null) return;
            float cap = ChannelCap(magic.TimelineData);
            foreach (var c in costs)
            {
                if (c?.Entries == null) continue;
                float sum = 0;
                foreach (var e in c.Entries) sum += F(e.Amount);
                // Whole numbers: the cadence makes the decimals look more exact than they are (attack speed rescales it).
                if (sum > 0) _costs.Add($"{Math.Round(sum * perSecond)} {c.Resource}/s{(cap > 0 ? $", up to {S(cap)}s" : "")}");
            }
        }

        /// <summary>How long a channel can be held: the state left on InputReleased that also ends by Duration (Heal Aura's
        /// Charge state, 300 frames = 5s). 0 = no such state.</summary>
        private static float ChannelCap(TimelineActionData? tl)
        {
            var infos = tl?.StateInfos;
            if (infos == null) return 0;
            for (int i = 0; i < infos.Length; i++)
            {
                var transitions = infos[i]?.Transitions;
                if (transitions == null) continue;
                bool released = false, timed = false;
                foreach (var t in transitions)
                {
                    if (t == null) continue;
                    if (t.Condition == ActionStateTransitionCondition.InputReleased) released = true;
                    if (t.Condition == ActionStateTransitionCondition.Duration) timed = true;
                }
                if (released && timed) return F(infos[i].End) - (i > 0 && infos[i - 1] != null ? F(infos[i - 1].End) : 0);
            }
            return 0;
        }

        private static Dictionary<string, float> Totals(Il2Cpp.MultiHeroActionCostHelper? helper)
        {
            var totals = new Dictionary<string, float>();
            if (helper?.Costs == null) return totals;
            foreach (var c in helper.Costs)
            {
                if (c?.Entries == null) continue;
                float sum = 0;
                foreach (var e in c.Entries) sum += F(e.Amount);
                if (sum <= 0) continue;
                string key = c.Resource.ToString();
                totals[key] = totals.TryGetValue(key, out float v) ? v + sum : sum;
            }
            return totals;
        }
    }
}
