using HarmonyLib;

namespace RuneDetails.Patches;

/// <summary>
/// Every rune text comes from HeroItemDataAsset.GetDescription() (no parameters, not virtual; callers: the item tooltip,
/// PopulateRuneData and RuneScreen.SetRuneNameText, build 29466). The rune screen splits the text on newlines and shows
/// only the second paragraph, so the numbers are appended to the last paragraph, never on a new line.
/// </summary>
[HarmonyPatch(typeof(Il2Cpp.HeroItemDataAsset), nameof(Il2Cpp.HeroItemDataAsset.GetDescription))]
internal static class RuneDescriptionPatch
{
    static void Postfix(Il2Cpp.HeroItemDataAsset __instance, ref string __result)
    {
        if (!Prefs.Enabled.Value || !Prefs.ShowRuneDetails.Value || string.IsNullOrEmpty(__result)) return;
        try
        {
            var rune = __instance.TryCast<Il2Cpp.HeroRuneDataAsset>();
            if (rune == null) return;
            var extra = RuneDescriber.Describe(rune);
            if (extra != null) __result = __result.TrimEnd() + Prefs.HiddenFormat.Value.Replace("{extra}", extra);
        }
        catch (System.Exception e)
        {
            RuneDetailsMod.Log.Warning("Rune description postfix: " + e.Message);
        }
    }
}
