using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMoon.Forsaken;
using MelonLoader;
using UnityEngine;

namespace RuneDetails;

/// <summary>
/// Our Off / Brief / Detailed dropdown at the end of Options > Gameplay, after a divider. Same technique as the
/// DailiesResetTimer mod (its docs/internal.md, "Settings rows"); the dropdown row is AddActualDropDownItem with a plain
/// Action&lt;int&gt;, as in DisplayUiTweaks (the game's bound rows need a PlayerSetting a mod cannot supply).
/// </summary>
internal static class SettingsRows
{
    public const string Prefix = "RD_";
    private const PlayerSettingCategory Category = PlayerSettingCategory.Gameplay;
    private const string SpacerId = "RD_Spacer", RunesId = "RD_Runes";
    private static readonly string[] AllIds = { SpacerId, RunesId };

    private static readonly Dictionary<string, LocalizedMessage> _messages = new Dictionary<string, LocalizedMessage>();

    /// <summary>Add the rows to every settings screen that already exists (after a hot reload).</summary>
    public static void AddToLiveScreens()
    {
        foreach (var s in Resources.FindObjectsOfTypeAll<SettingsScreen>())
            if (s != null && s.m_gameplayTab != null) AddTo(s.m_gameplayTab.m_controls);
    }

    public static void AddTo(SettingsScreenControls? controls)
    {
        if (controls == null) { RuneDetailsMod.Log.Warning("GameplaySettingsTab.m_controls is null"); return; }
        var content = GameplayContent(controls);
        if (content == null) { RuneDetailsMod.Log.Warning("Gameplay tab has no content root yet"); return; }

        ForgetRows(controls, oursToo: false);
        if (content.Find(RunesId) != null) return;
        RemoveRegistryEntries(controls);

        AddSpacer(controls, content);
        AddDropdown(controls, content, RunesId, "Rune Details",
            "Show what a rune really does. Brief: one grey line with its damage, heal or buff. Detailed: cost, damage, poise, "
                + "cast time, lockout, invulnerability and efficiency on separate lines (Rune Details).",
            Prefs.LevelNames, (int)Prefs.Level,
            i => { Prefs.Mode.Value = Prefs.LevelNames[i]; MelonPreferences.Save(); });
        RuneDetailsMod.Log.Msg("Added the Rune Details row to Options > Gameplay");
    }

    /// <summary>Hot reload / unload: destroy our rows on every live settings screen and free their registry keys.</summary>
    public static void RemoveAll()
    {
        foreach (var s in Resources.FindObjectsOfTypeAll<SettingsScreen>())
        {
            var controls = s != null && s.m_gameplayTab != null ? s.m_gameplayTab.m_controls : null;
            if (controls == null) continue;
            ForgetRows(controls, oursToo: true);
            var content = GameplayContent(controls);
            if (content != null)
                for (int i = content.childCount - 1; i >= 0; i--)
                {
                    var child = content.GetChild(i);
                    if (child != null && child.name.StartsWith(Prefix)) UnityEngine.Object.DestroyImmediate(child.gameObject);
                }
            RemoveRegistryEntries(controls);
        }
    }

    /// <summary>Drop the controls' cached selected / modal-previous element when it is destroyed (or, with oursToo, ours).</summary>
    private static void ForgetRows(SettingsScreenControls controls, bool oursToo)
    {
        if (IsDeadOrOurs(controls.m_cachedSelectedItemGUI, oursToo)) controls.m_cachedSelectedItemGUI = null;
        if (IsDeadOrOurs(controls.m_modalPreviousElement, oursToo)) controls.m_modalPreviousElement = null;
    }

    private static bool IsDeadOrOurs(SettingsItemGUIBase? item, bool oursToo)
    {
        if (item is null) return false;                       // no reference at all
        if (item == null) return true;                        // Unity-destroyed object
        return oursToo && item.gameObject.name.StartsWith(Prefix);
    }

    private static RectTransform? GameplayContent(SettingsScreenControls controls)
    {
        var roots = controls.m_nameToContentRoot;
        if (roots == null || !roots.ContainsKey(Category)) return null;
        return roots[Category];
    }

    private static void RemoveRegistryEntries(SettingsScreenControls controls)
    {
        if (controls.m_categoryToContentToItem == null || !controls.m_categoryToContentToItem.ContainsKey(Category)) return;
        var items = controls.m_categoryToContentToItem[Category];
        if (items == null) return;
        foreach (var id in AllIds) items.Remove(id);
    }

    private static void AddDropdown(SettingsScreenControls controls, RectTransform content, string id, string name, string desc,
        string[] options, int current, Action<int> onChanged)
    {
        var arr = new Il2CppStringArray(options.Length);
        for (int i = 0; i < options.Length; i++) arr[i] = options[i];
        int before = content.childCount;
        controls.AddActualDropDownItem(Category, Msg(id, name), arr, current, onChanged, Msg(id + "_Desc", desc), true, false);
        NameNewRow(content, before, id);
    }

    /// <summary>The same empty divider row the game uses between its own groups.</summary>
    private static void AddSpacer(SettingsScreenControls controls, RectTransform content)
    {
        int before = content.childCount;
        controls.AddDividerItem(Category, SpacerId);
        NameNewRow(content, before, SpacerId);
        if (content.childCount > before)
        {
            var row = content.GetChild(content.childCount - 1).GetComponent<SettingsItemGUIBase>();
            if (row != null && row.SettingLabel != null) row.SettingLabel.text = "";
        }
    }

    private static void NameNewRow(RectTransform content, int before, string id)
    {
        if (content.childCount > before) content.GetChild(content.childCount - 1).name = id;
    }

    /// <summary>A LocalizedMessage is a ScriptableObject holding one string per language; fill every language with the same text.</summary>
    private static LocalizedMessage Msg(string id, string text)
    {
        if (_messages.TryGetValue(id, out var cached) && cached != null) return cached;
        var so = ScriptableObject.CreateInstance(Il2CppType.Of<LocalizedMessage>());
        var m = so.Cast<LocalizedMessage>();
        m.name = id;
        m.Id = id;
        m.English = text; m.French = text; m.Italian = text; m.German = text; m.Spanish = text;
        m.BrazilianPortuguese = text; m.TraditionalChinese = text; m.SimplifiedChinese = text;
        m.Korean = text; m.Russian = text; m.Japanese = text; m.Polish = text;
        m.hideFlags = HideFlags.HideAndDontSave;
        _messages[id] = m;
        return m;
    }
}
