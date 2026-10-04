using HarmonyLib;
using Il2CppMoon.Forsaken;

namespace RuneDetails.Patches;

/// <summary>Which of the game's three rune views is asking for the text (all of them call GetDescription).</summary>
internal enum RuneView
{
    /// <summary>InventoryItemInfoElement.PopulateItemDescription: hovering a rune item (inventory, vendor, rune list).
    /// Full text. Cannot be hooked itself (an `in ItemDescription` struct parameter), so it is the default.</summary>
    Tooltip,
    /// <summary>InventoryItemInfoElement.PopulateRuneData: hovering a utility rune slot. Full text.</summary>
    UtilitySlot,
    /// <summary>RuneScreen.SetRuneNameText: a weapon's or the utility rune slots in the Runes menu. Shows only the
    /// second piece of the text split on ". " and "\n" (the effect's first sentence), so anything appended is cut.</summary>
    RuneScreen,
}

internal static class RuneViewContext
{
    private static RuneView _view;
    private static int _frame = -1;

    /// <summary>The view set by a prefix in this frame, else the item tooltip. Tied to the frame so a reset missed
    /// because of an exception cannot leak into later calls.</summary>
    public static RuneView Current => _frame == UnityEngine.Time.frameCount ? _view : RuneView.Tooltip;

    public static void Enter(RuneView view) { _view = view; _frame = UnityEngine.Time.frameCount; }
    public static void Exit() => _frame = -1;

    /// <summary>The rune screen's details, captured during SetRuneNameText and appended after its split.</summary>
    public static RuneDescriber.RuneText? RuneScreenExtra;
}

/// <summary>
/// Every rune text comes from HeroItemDataAsset.GetDescription() (no parameters, not virtual; callers: the item tooltip,
/// PopulateRuneData and RuneScreen.SetRuneNameText, build 29466). The tooltip views show the whole text, so the details
/// are appended in-line; the rune screen gets them in RuneScreenPatch instead.
/// </summary>
[HarmonyPatch(typeof(Il2Cpp.HeroItemDataAsset), nameof(Il2Cpp.HeroItemDataAsset.GetDescription))]
internal static class RuneDescriptionPatch
{
    static void Postfix(Il2Cpp.HeroItemDataAsset __instance, ref string __result)
    {
        if (!Prefs.Enabled.Value || Prefs.Level == DetailLevel.Off || string.IsNullOrEmpty(__result)) return;
        try
        {
            var rune = __instance.TryCast<Il2Cpp.HeroRuneDataAsset>();
            if (rune == null) return;
            var text = RuneDescriber.Describe(rune);
            if (RuneViewContext.Current == RuneView.RuneScreen)
            {
                // Leave the text alone: the screen splits it on ". " and would drop the details.
                RuneViewContext.RuneScreenExtra = text;
                return;
            }
            __result = RuneLayout.Tooltip(__result, text, Prefs.Level);
        }
        catch (System.Exception e)
        {
            RuneDetailsMod.Log.Warning("Rune description postfix: " + e.Message);
        }
    }
}

[HarmonyPatch(typeof(InventoryItemInfoElement), nameof(InventoryItemInfoElement.PopulateRuneData))]
internal static class UtilitySlotPatch
{
    static void Prefix() => RuneViewContext.Enter(RuneView.UtilitySlot);
    static void Postfix() => RuneViewContext.Exit();
}

/// <summary>RuneScreen.SetRuneNameText() (no parameters) fills RuneSlotDescription with the effect's first sentence.
/// Append the details after it has done so.</summary>
[HarmonyPatch(typeof(RuneScreen), "SetRuneNameText")]
internal static class RuneScreenPatch
{
    static void Prefix()
    {
        RuneViewContext.RuneScreenExtra = null;
        RuneViewContext.Enter(RuneView.RuneScreen);
    }

    static void Postfix(RuneScreen __instance)
    {
        RuneViewContext.Exit();
        var extra = RuneViewContext.RuneScreenExtra;
        RuneViewContext.RuneScreenExtra = null;
        if (extra == null || !Prefs.Enabled.Value || Prefs.Level == DetailLevel.Off) return;
        try
        {
            var text = __instance.RuneSlotDescription;
            if (text == null || !text.gameObject.activeInHierarchy || string.IsNullOrEmpty(text.text)) return;
            text.text = RuneLayout.RuneScreen(text.text, extra, Prefs.Level);
        }
        catch (System.Exception e)
        {
            RuneDetailsMod.Log.Warning("Rune screen postfix: " + e.Message);
        }
    }
}
