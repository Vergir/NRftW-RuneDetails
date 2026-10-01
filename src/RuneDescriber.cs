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
    private static readonly Dictionary<string, string?> Cache = new();
    private static readonly HashSet<string> Failed = new();
    private static bool _selfTestDone;

    public static string? Describe(Il2Cpp.HeroRuneDataAsset asset)
    {
        string key = asset.name ?? "";
        if (Cache.TryGetValue(key, out var cached)) return cached == null ? null : ResolveLive(cached);
        string? text = null;
        try { text = Describe(asset.HeroItemData?.TryCast<HeroRuneData>()); }
        catch (Exception e)
        {
            if (Failed.Add(key)) RuneDetailsMod.Log.Warning($"Rune details for {key}: {e.Message}");
        }
        if (Prefs.Debug.Value) RuneDetailsMod.Log.Msg($"rune {key}: {text ?? "(none)"}");
        Cache[key] = text;
        return text == null ? null : ResolveLive(text);
    }

    private static string? Describe(HeroRuneData? rune)
    {
        if (rune?.Actions == null || rune.Actions.Length == 0) return null;
        var action = Resolve<ActionData>(rune.Actions[0].Id);
        return action == null ? null : new Walker().Action(action);
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
            try { text = ResolveLive(Describe(Resolve<HeroRuneData>(new AssetGuid { Value = guid })) ?? "(none)"); }
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
    private static string ResolveLive(string text)
    {
        if (text.IndexOf(Tok) < 0) return text;
        var expected = LiveExpectedWeaponDamage();
        var parts = text.Split(Tok);
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < parts.Length; i++)
        {
            if (i % 2 == 0) { sb.Append(parts[i]); continue; }
            float mult = float.Parse(parts[i], CultureInfo.InvariantCulture);
            string amount = expected is { } e ? $"≈{Math.Round(mult * e)} dmg" : $"{Pct(mult)} base dmg";
            sb.Append(amount + ", grows with weapon LVL, not weapon DMG");
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

    private sealed class Walker
    {
        private readonly List<string> _heals = new(), _buffs = new(), _damage = new(), _costs = new();
        private readonly HashSet<long> _seen = new();
        private DamageBalanceData _base;
        private bool _charged;
        private float _minCharge = 1, _maxCharge = 1;
        private bool _channelled; // ChargedMagicActionData that drains a resource while held (beams, auras)
        private bool _levelDamage; // DamageConfig.CustomDamageProvider = ExpectedWeaponDamageAmountProviderNode
        private string? _multishot; // "3–10": BowMultishotAttackData fires Min..MaxShots arrows depending on windup

        public string? Action(ActionData action)
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
                MeleeHits(tl.WeaponColliders);
                if (tl.ProjectileEvents != null)
                    ProjectileEvents(tl.ProjectileEvents);
                if (tl.SpawnEntityEvents != null)
                    foreach (var e in tl.SpawnEntityEvents)
                        if (e != null) Entity(e.entityToSpawn.Id);
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

            var parts = _heals.Concat(_buffs).Concat(_damage).Concat(_costs).Distinct().ToList();
            return parts.Count > 0 ? string.Join("; ", parts) : null;
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
                if (c == null) continue;
                // ActionData.ResolveWeaponColliderDamage: the collider's own strike data replaces the action's.
                mults.Add(c.UseAlternateStrikeData ? Mult(c.StrikeData, default) : Mult(_base, default));
            }
            if (mults.Count == 0) return;
            if (_levelDamage)
            {
                // Kicks: base = expected weapon damage for the weapon's item level, weapon Damage ignored.
                // Resolved per display (depends on the equipped weapon), see LevelDamageToken / Live.
                string each = LevelDamageToken(mults.Max());
                _damage.Add(mults.Count == 1 ? each : $"{mults.Count} hits × {each}");
                return;
            }
            if (mults.All(m => Math.Abs(m - mults[0]) < 0.001f))
                _damage.Add(mults.Count == 1 ? $"{Pct(mults[0])} weapon dmg" : $"{mults.Count} hits × {Pct(mults[0])} weapon dmg");
            else
                _damage.Add($"{mults.Count} hits, {Pct(mults.Min()).TrimEnd('%')}–{Pct(mults.Max())} each, {Pct(mults.Sum())} total weapon dmg");
        }

        private void ProjectileEvents(Il2CppSystem.Collections.Generic.List<ActionProjectileEvent> events)
        {
            int count = 0, ammoShots = 0;
            string? text = null;
            foreach (var e in events)
            {
                if (e == null) continue;
                var p = Resolve<ProjectileData>(e.OverrideProjectile.Id);
                if (p == null) { ammoShots++; continue; } // fires the equipped ammo: only the action's own layer is known
                count++;
                text ??= ProjectileText(p);
            }
            if (text != null) _damage.Add(count > 1 ? $"{count} × {text}" : text);
            if (ammoShots > 0)
            {
                string each = $"{Pct(Mult(_base, default))} weapon dmg";
                string shots = _multishot ?? ammoShots.ToString();
                _damage.Add($"{shots} {(_multishot != null || ammoShots > 1 ? "shots" : "shot")} × {each}");
            }
        }

        private string? ProjectileText(ProjectileData p)
        {
            var strikes = DamageArray(p.StrikeDamageData);
            string? health = ExpectedHealthDamage(p.Payloads);
            if (strikes.Length == 0) return health;
            var mults = strikes.Select(Mult).ToList();
            string text;
            if (mults.Distinct().Count() == 1) text = $"{Pct(mults[0])} weapon dmg";
            else if (_charged) text = $"{string.Join("/", mults.Select(m => Pct(m).TrimEnd('%')))}% weapon dmg by charge";
            else text = $"{Pct(mults.Min()).TrimEnd('%')}–{Pct(mults.Max())} weapon dmg";
            var expl = DamageArray(p.ExplosionDamageData);
            if (expl.Length > 0 && (p.ExplodeOnExpiration || (int)p.ExplodeOnHit != 0))
                text += $" + {Pct(Mult(expl[expl.Length - 1]))} explosion";
            return health == null ? text : text + " + " + health;
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
            return sum > 0 ? $"{Pct(sum)} of typical enemy HP" : null;
        }

        private void Entity(AssetGuid guid)
        {
            if (guid.Value == 0 || !_seen.Add(guid.Value)) return;
            var asset = Resolve<AssetObject>(guid);
            if (asset == null) return;
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
                if (text != null) _damage.Add(text);
            }
        }

        private void Cascade(Il2Cpp.CascadeInstanceSettings s, CascadeStaticData? owner)
        {
            float repeat = F(s.ExecutionRepeatTime);
            switch (s.Reaction)
            {
                case Il2Cpp.CascadeReactionType.DamageArea:
                case Il2Cpp.CascadeReactionType.DirectDamage:
                {
                    float m = Mult(_base, s.Damage.Damage);
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
                        if (s.Damage.Damage.KnockDown || _base.KnockDown) _damage.Add("knockdown, no damage");
                        break;
                    }
                    string text = Math.Abs(hi - lo) < 0.001f
                        ? $"{Pct(m * hi)} weapon dmg"
                        : $"{Pct(m * lo).TrimEnd('%')}–{Pct(m * hi)} weapon dmg by charge";
                    float duration = owner == null ? 0 : F(owner.InstanceDuration);
                    text = RepeatText(text, m * hi, repeat, duration, s.Damage.UniqueDamageId || s.Reaction == Il2Cpp.CascadeReactionType.DirectDamage, _channelled);
                    float radius = owner == null ? 0 : F(owner.InstanceRadius) * (s.DamageArea.Shape == null ? 1 : F(s.DamageArea.Shape.Radius));
                    if (radius >= 2) text += $" in {N(radius)}m";
                    _damage.Add(text);
                    break;
                }
                case Il2Cpp.CascadeReactionType.SpecialEffect:
                {
                    var fx = s.SpecialEffect;
                    if (fx == null) break;
                    bool allies = ((int)fx.Targeting & (int)Il2Cpp.CascadeTargetingMode.Friendlies) != 0;
                    Payloads(fx.Payload, repeat, allies);
                    break;
                }
                case Il2Cpp.CascadeReactionType.SpawnEntity:
                    Entity(s.EntityToSpawn.Id);
                    break;
            }
        }

        /// <summary>How often a repeating cascade damages one enemy (analysis/cascade_rehit.md, traced 2026-09-29):
        /// the sim runs at 60 Hz and the repeat timer is reset, so ticks are ceil(repeat / frame) frames apart
        /// (0.05s -> 0.067s, 0.15s -> 0.167s). Without UniqueDamageId every tick reuses the cast's damage id and
        /// DamageResolverComponent.TryRegisterDamageID drops the same id on the same target for 60 frames, so the enemy
        /// is hit at most once per second. With UniqueDamageId (and DirectDamage) every tick is a new hit.
        /// IsContinuousDamage does not affect this (it only skips poise, lifesteal, focus gain...).</summary>
        private static string RepeatText(string text, float mult, float repeat, float duration, bool unique, bool channelled)
        {
            // Without a duration or a channel nothing says how long it keeps ticking (Frigid Arc, Frost Step): per hit only.
            if (repeat <= 0 || (duration <= 0 && !channelled)) return text;
            const float frame = 1092f / 65536f; // FP(1/60) as the sim computes it
            int k = (int)Math.Ceiling(repeat / frame - 0.001f);
            float every = k * frame;
            string span = duration > 0 ? $" for {S(duration)}s" : ""; // no duration: channelled, the drain says so
            if (every >= 1f) return $"{text} every {S(every)}s{span}";
            // Sub-second repeats as a rate. Shared damage id: capped at one hit per second per enemy, reached only
            // while the enemy stays inside ("up to"). Unique ids: every tick hits, exact.
            return unique ? $"{Pct(mult / every)} weapon dmg/s{span}" : $"up to {Pct(mult)} weapon dmg/s{span}";
        }

        private void Payloads(PayloadData? data, float repeat, bool allies)
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
                if (v <= 0) continue;
                string who = allies ? " to you and allies" : "";
                string verb = unit == "HP" ? "Heals" : "Restores";
                _heals.Add(repeat > 0 ? $"{verb} {N(v / repeat)} {unit}/s{who}" : $"{verb} {N(v)} {unit}{who}");
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
            float every = F(magic.ChargingCostTime);
            var costs = magic.ChargingCost?.Costs;
            if (every <= 0 || costs == null) return;
            foreach (var c in costs)
            {
                if (c?.Entries == null) continue;
                float sum = 0;
                foreach (var e in c.Entries) sum += F(e.Amount);
                if (sum > 0) _costs.Add($"drains {N(sum / every)} {c.Resource}/s while channelling");
            }
        }
    }
}
