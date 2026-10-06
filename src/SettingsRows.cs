using Il2CppMoon.Forsaken;
using MelonLoader;
using NrftwShared;

namespace RuneDetails;

/// <summary>
/// Our Off / Brief / Detailed dropdown at the end of Options > Gameplay, after a divider and a heading. Built with the
/// shared kit (src/Shared/SettingsRowsKit.cs), which also removes it; same technique as the DailiesResetTimer mod (its
/// docs/internal.md, "Settings rows").
/// </summary>
internal static class SettingsRows
{
    public const string Prefix = "RD_";
    private const PlayerSettingCategory Category = PlayerSettingCategory.Gameplay;
    private const string SpacerId = "RD_Spacer", HeadingId = "RD_Heading", RunesId = "RD_Runes";

    private static readonly SettingsRowsKit Kit = new SettingsRowsKit(Prefix,
        new[] { SpacerId, HeadingId, RunesId },
        () => RuneDetailsMod.Log, SettingsRowsKit.GameplayTabControls);

    /// <summary>Add the rows to every settings screen that already exists (after a hot reload).</summary>
    public static void AddToLiveScreens() => Kit.AddToLiveScreens(AddTo);

    public static void AddTo(SettingsScreenControls? controls)
    {
        var rows = Kit.Begin(controls, Category, RunesId);
        if (rows == null) return;

        rows.Spacer(SpacerId);
        // A heading row as the game uses between its own groups, so the setting reads as this mod's.
        rows.Heading(HeadingId, "Rune Details");
        rows.Dropdown(RunesId, "Details Display Mode",
            "Show what a rune really does. Brief: one grey line with its damage, heal or buff. Detailed: cost, damage, poise, "
                + "cast time, lockout, invulnerability and efficiency on separate lines (Rune Details).",
            Prefs.LevelNames, (int)Prefs.Level,
            i => { Prefs.Mode.Value = Prefs.LevelNames[i]; MelonPreferences.Save(); });
        RuneDetailsMod.Log.Msg("Added the Rune Details row to Options > Gameplay");
    }

    /// <summary>Hot reload / unload: destroy our rows on every live settings screen and free their registry keys.</summary>
    public static void RemoveAll() => Kit.RemoveAll();
}
