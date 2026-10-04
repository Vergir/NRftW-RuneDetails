using RuneDetails;
using System;
using MelonLoader;

[assembly: MelonInfo(typeof(RuneDetailsMod), "Rune Details", "1.0.0", "vergir")]
[assembly: MelonGame("Moon Studios", "NoRestForTheWicked")]
// MelonLoader would otherwise apply every [HarmonyPatch] in this assembly by itself, ignoring Enabled (and the INERT build).
[assembly: HarmonyDontPatchAll]

namespace RuneDetails;

/// <summary>
/// No Rest for the Wicked: append what a rune really does (heal amounts, buffs, damage as % of weapon damage, channelling
/// drain) to its fixed tooltip text. Display only; the simulation is untouched.
/// </summary>
public class RuneDetailsMod : MelonMod
{
    public static RuneDetailsMod Instance { get; private set; } = null!;
    public static MelonLogger.Instance Log => Instance.LoggerInstance;

    public override void OnInitializeMelon()
    {
        Instance = this;
#if INERT
        LoggerInstance.Msg("Inert build: no patches, the game's rune text is untouched.");
        return;
#endif
        Prefs.Init();
        if (!Prefs.Enabled.Value)
        {
            LoggerInstance.Msg("Disabled via preferences.");
            return;
        }

        HarmonyInstance.PatchAll(typeof(RuneDetailsMod).Assembly);
        LoggerInstance.Msg("Patches applied.");
        try { RuneDescriber.SelfTest(); } catch (Exception e) { LoggerInstance.Warning("Rune self-test: " + e); }

        // After a hot reload the info panels already exist; their OnEnable postfix has not run.
        try
        {
            foreach (var panel in UnityEngine.Resources.FindObjectsOfTypeAll<Il2CppMoon.Forsaken.InventoryItemInfoElement>())
                if (panel != null) TypeCostDedup.Register(panel);
        }
        catch (Exception e) { LoggerInstance.Warning("Registering info panels: " + e.Message); }

        // After a hot reload the settings screens already exist; the Initialize postfix will not run for them.
        if (Prefs.AddSettingsRows.Value)
        {
            try { SettingsRows.AddToLiveScreens(); }
            catch (Exception e) { LoggerInstance.Warning("Adding rows to live settings screens: " + e.Message); }
        }
    }

    /// <summary>After the UI has updated: drop our Type/Cost lines where the game shows its own row (TypeCostDedup).</summary>
    public override void OnLateUpdate()
    {
        if (Prefs.Enabled is null || !Prefs.Enabled.Value) return;
        TypeCostDedup.Tick();
    }

    /// <summary>Development audit (see RuneDescriber.SelfTest): the asset database is ready only after the first scenes.</summary>
    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        if (Prefs.Enabled is null || !Prefs.Enabled.Value) return;
        try { RuneDescriber.SelfTest(); } catch (Exception e) { LoggerInstance.Warning("Rune self-test: " + e.Message); }
    }

    /// <summary>Unload / hot reload: take our rows back off the game's settings screens.</summary>
    public override void OnDeinitializeMelon()
    {
        try { SettingsRows.RemoveAll(); }
        catch (Exception e) { LoggerInstance.Warning("SettingsRows.RemoveAll: " + e.Message); }
    }
}
