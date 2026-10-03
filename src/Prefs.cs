using System;
using MelonLoader;

namespace RuneDetails;

internal enum DetailLevel { Off, Brief, Detailed }

/// <summary>All user-tunable values. Stored in UserData/MelonPreferences.cfg under [RuneDetails].</summary>
internal static class Prefs
{
    public const string DefaultHiddenFormat = " <color=#9A9A9A>({extra})</color>";
    public static readonly string[] LevelNames = { "Off", "Brief", "Detailed" };

    private static MelonPreferences_Category _cat = null!;

    public static MelonPreferences_Entry<bool> Enabled = null!;
    public static MelonPreferences_Entry<string> Mode = null!;
    public static MelonPreferences_Entry<bool> AddSettingsRows = null!;
    public static MelonPreferences_Entry<string> HiddenFormat = null!;
    public static MelonPreferences_Entry<bool> Debug = null!;

    /// <summary>The Mode preference as a level; anything unrecognised reads as Brief.</summary>
    public static DetailLevel Level =>
        Enum.TryParse(Mode.Value, true, out DetailLevel level) && Enum.IsDefined(typeof(DetailLevel), level) ? level : DetailLevel.Brief;

    public static void Init()
    {
        _cat = MelonPreferences.CreateCategory("RuneDetails", "Rune Details");

        Enabled = _cat.CreateEntry("Enabled", true, description: "Master switch.");
        Mode = _cat.CreateEntry("Mode", "Brief",
            description: "Off, Brief (one line: heal amounts, buffs, damage as % of weapon damage, channelling drain) or Detailed "
                + "(more numbers). Also in Options > Gameplay.");
        HiddenFormat = _cat.CreateEntry("HiddenFormat", DefaultHiddenFormat,
            description: "Appended to the rune text. {extra} = the details, e.g. \"Heals 40 HP\".");
        AddSettingsRows = _cat.CreateEntry("AddSettingsRows", true,
            description: "Add the Rune Details setting to Options > Gameplay.");
        Debug = _cat.CreateEntry("Debug", false,
            description: "Log the details of every rune when first shown (MelonLoader console).");
    }
}
