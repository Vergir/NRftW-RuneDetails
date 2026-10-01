using MelonLoader;

namespace RuneDetails;

/// <summary>All user-tunable values. Stored in UserData/MelonPreferences.cfg under [RuneDetails].</summary>
internal static class Prefs
{
    public const string DefaultHiddenFormat = " <color=#9A9A9A>({extra})</color>";

    private static MelonPreferences_Category _cat = null!;

    public static MelonPreferences_Entry<bool> Enabled = null!;
    public static MelonPreferences_Entry<bool> ShowRuneDetails = null!;
    public static MelonPreferences_Entry<bool> AddSettingsRows = null!;
    public static MelonPreferences_Entry<string> HiddenFormat = null!;
    public static MelonPreferences_Entry<bool> Debug = null!;

    public static void Init()
    {
        _cat = MelonPreferences.CreateCategory("RuneDetails", "Rune Details");

        Enabled = _cat.CreateEntry("Enabled", true, description: "Master switch.");
        ShowRuneDetails = _cat.CreateEntry("ShowRuneDetails", true,
            description: "Append what a rune really does: heal amounts, buffs, damage as % of weapon damage, channelling drain. Also in Options > Gameplay.");
        HiddenFormat = _cat.CreateEntry("HiddenFormat", DefaultHiddenFormat,
            description: "Appended to the rune text. {extra} = the details, e.g. \"Heals 40 HP\".");
        AddSettingsRows = _cat.CreateEntry("AddSettingsRows", true,
            description: "Add the Show Rune Details toggle to Options > Gameplay.");
        Debug = _cat.CreateEntry("Debug", false,
            description: "Log the details of every rune when first shown (MelonLoader console).");
    }
}
