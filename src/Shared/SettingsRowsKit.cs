// Shared settings-row kit for the No Rest for the Wicked mods of this workspace.
//
// Canonical copy: mods/Shared/SettingsRowsKit.cs. Every mod that uses it builds a vendored copy in src/Shared/, so each
// repo still builds on its own. Edit the canonical file, then run mods/Shared/sync-shared.ps1. A build warns when a mod's
// copy differs from the canonical one (only where mods/Shared exists).
//
// The class is internal, so the identical full name in several mod assemblies loaded in one game does not clash.

using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMoon.Forsaken;
using MelonLoader;
using UnityEngine;

namespace NrftwShared;

/// <summary>
/// A mod's rows in the game's settings screen (Options), built with the game's own SettingsScreenControls.Add*Item.
/// <list type="bullet">
/// <item>The game's bound rows need a PlayerSetting built on a ref-returning delegate, which a mod cannot supply; the rows
/// here use the overloads that take a plain callback (AddKeyboardAndMouseSchemeToggleItem for toggles,
/// AddActualDropDownItem, AddSliderItem with a null adapter, AddButtonItem).</item>
/// <item>Rows are found by GameObject name (the mod's prefix), not static state: the game builds a settings screen per scene
/// load, and the main menu and the game each have one.</item>
/// <item>m_categoryToContentToItem is keyed by the label's LocalizedMessage.Id; adding an Id twice throws after the row was
/// instantiated. So the ids are freed before adding, and on removal.</item>
/// <item>The controls keep direct references to rows (dropdown lists Esc / Back walk, cached selected and modal-previous
/// element). Rows are dropped from those before they are destroyed, or Back throws and Esc stops closing Options.</item>
/// <item>Mod Settings Tab may move the rows to its own tab, so removal looks in every tab's content root.</item>
/// </list>
/// </summary>
internal sealed class SettingsRowsKit
{
    /// <summary>Alternate1: the only scheme style that does not arm the game's "preview keyboard scheme" button while hovered.</summary>
    public const KeyboardAndMouseStyle ToggleStyle = KeyboardAndMouseStyle.Alternate1;

    /// <summary>The start of every row name of this mod (e.g. "QS_").</summary>
    public readonly string Prefix;

    private readonly string[] _ids;
    private readonly Func<MelonLogger.Instance> _log;
    private readonly Func<SettingsScreen, SettingsScreenControls?> _controlsOf;
    private readonly Dictionary<string, LocalizedMessage> _messages = new Dictionary<string, LocalizedMessage>();

    /// <summary>Also log a skipped add (rows already there), dropped references and the number of removed rows.</summary>
    public bool Verbose { get; set; }

    /// <param name="prefix">Start of every row name of this mod.</param>
    /// <param name="ids">Every row id (registry key) the mod uses or used: freed before adding and on removal.</param>
    /// <param name="log">The mod's logger (a delegate: the melon may not exist yet when the kit is created).</param>
    /// <param name="controlsOf">Which controls of a settings screen hold the rows: <see cref="GameplayTabControls"/> or
    /// <see cref="DisplayTabControls"/>.</param>
    public SettingsRowsKit(string prefix, string[] ids, Func<MelonLogger.Instance> log, Func<SettingsScreen, SettingsScreenControls?> controlsOf)
    {
        Prefix = prefix;
        _ids = ids;
        _log = log;
        _controlsOf = controlsOf;
    }

    private MelonLogger.Instance Log => _log();

    public static SettingsScreenControls? GameplayTabControls(SettingsScreen s) => s.m_gameplayTab != null ? s.m_gameplayTab.m_controls : null;

    public static SettingsScreenControls? DisplayTabControls(SettingsScreen s) => s.m_displayTab != null ? s.m_displayTab.m_controls : null;

    public bool IsOurs(string name) => name.StartsWith(Prefix, StringComparison.Ordinal);

    // ------------------------------------------------------------------ adding

    /// <summary>Add the rows to every settings screen that already exists (after a hot reload).</summary>
    public void AddToLiveScreens(Action<SettingsScreenControls?> addTo)
    {
        foreach (var s in Resources.FindObjectsOfTypeAll<SettingsScreen>())
        {
            if (s == null) continue;
            var controls = _controlsOf(s);
            if (controls != null) addTo(controls);
        }
    }

    /// <summary>
    /// Get ready to add rows at the end of a tab: drop references to destroyed rows and free our registry keys.
    /// Null when the controls or the tab's content root are missing (logged), or when the row named guardId is already
    /// there (the rows were added before).
    /// </summary>
    /// <param name="tabLabel">The tab's name in the log; default: the category.</param>
    public Builder? Begin(SettingsScreenControls? controls, PlayerSettingCategory category, string guardId, string? tabLabel = null)
    {
        if (controls == null) { Log.Warning($"{category}SettingsTab.m_controls is null"); return null; }
        var content = ContentOf(controls, category);
        if (content == null) { Log.Warning($"{tabLabel ?? category.ToString()} tab has no content root yet"); return null; }

        // Drop references to destroyed rows (left by an older build).
        ForgetRows(controls, oursToo: false);
        if (content.Find(guardId) != null)
        {
            if (Verbose) Log.Msg("Settings rows already present, skipping");
            return null;
        }
        RemoveRegistryEntries(controls, category);
        return new Builder(this, controls, content, category);
    }

    // ------------------------------------------------------------------ removing

    /// <summary>Hot reload / unload: destroy our rows on every live settings screen, in every tab, and free their registry keys.</summary>
    public int RemoveAll()
    {
        int removed = 0;
        foreach (var s in Resources.FindObjectsOfTypeAll<SettingsScreen>())
        {
            if (s == null) continue;
            var controls = _controlsOf(s);
            if (controls == null) continue;
            // Unregister before destroying, or Back throws on the destroyed dropdown.
            ForgetRows(controls, oursToo: true);
            var roots = controls.m_nameToContentRoot;
            if (roots == null) continue;
            foreach (var kv in roots)
            {
                var content = kv.Value;
                if (content == null) continue;
                for (int i = content.childCount - 1; i >= 0; i--)
                {
                    var child = content.GetChild(i);
                    if (child != null && IsOurs(child.name)) { UnityEngine.Object.DestroyImmediate(child.gameObject); removed++; }
                }
                RemoveRegistryEntries(controls, kv.Key);
            }
        }
        if (Verbose && removed > 0) Log.Msg("Removed " + removed + " settings rows");
        return removed;
    }

    /// <summary>Drop references the controls hold to destroyed rows (and, with oursToo, to our live rows): the dropdown
    /// instance lists (Esc / Back call IsOpen on each through IsAnyDropDownOpen) and the cached selected / modal-previous element.</summary>
    private void ForgetRows(SettingsScreenControls controls, bool oursToo)
    {
        int dropped = 0;
        var actual = controls.m_actualDropDownInstances;
        if (actual != null)
            for (int i = actual.Count - 1; i >= 0; i--)
                if (IsDeadOrOurs(actual[i], oursToo)) { actual.RemoveAt(i); dropped++; }
        var bound = controls.m_boundDropDownInstances;
        if (bound != null)
            for (int i = bound.Count - 1; i >= 0; i--)
                if (bound[i] == null) { bound.RemoveAt(i); dropped++; }
        if (IsDeadOrOurs(controls.m_cachedSelectedItemGUI, oursToo)) { controls.m_cachedSelectedItemGUI = null; dropped++; }
        if (IsDeadOrOurs(controls.m_modalPreviousElement, oursToo)) { controls.m_modalPreviousElement = null; dropped++; }
        if (Verbose && dropped > 0)
            Log.Msg("Dropped " + dropped + " settings-screen reference(s) to " + (oursToo ? "our rows" : "destroyed rows"));
    }

    private bool IsDeadOrOurs(SettingsItemGUIBase? item, bool oursToo)
    {
        if (item is null) return false;                       // no reference at all
        if (item == null) return true;                        // Unity-destroyed object
        return oursToo && IsOurs(item.gameObject.name);
    }

    private void RemoveRegistryEntries(SettingsScreenControls controls, PlayerSettingCategory category)
    {
        if (controls.m_categoryToContentToItem == null || !controls.m_categoryToContentToItem.ContainsKey(category)) return;
        var items = controls.m_categoryToContentToItem[category];
        if (items == null) return;
        foreach (var id in _ids) items.Remove(id);
    }

    // ------------------------------------------------------------------ helpers

    public static RectTransform? ContentOf(SettingsScreenControls controls, PlayerSettingCategory category)
    {
        var roots = controls.m_nameToContentRoot;
        if (roots == null || !roots.ContainsKey(category)) return null;
        return roots[category];
    }

    /// <summary>A tab whose label contains "mod" ("Mods", "Modifications"...: a mod-added tab) if the screen of these
    /// controls has one, else <paramref name="fallback"/> (also when controls is null).</summary>
    public static PlayerSettingCategory ModTabOr(SettingsScreenControls? controls, PlayerSettingCategory fallback, out string label)
    {
        label = fallback.ToString();
        if (controls == null) return fallback;
        foreach (var screen in Resources.FindObjectsOfTypeAll<SettingsScreen>())
        {
            if (screen == null || screen.m_controls == null || screen.m_controls.Pointer != controls.Pointer) continue;
            foreach (var tab in screen.GetComponentsInChildren<SettingsTabGUI>(true))
            {
                string text = tab?.label?.text?.Trim() ?? "";
                if (text.Length == 0 || text.IndexOf("mod", StringComparison.OrdinalIgnoreCase) < 0 || ContentOf(controls, tab!.Category) == null) continue;
                label = text;
                return tab.Category;
            }
        }
        return fallback;
    }

    /// <summary>Slider position (0..1) of a value in min..max.</summary>
    public static float Normalize(float value, float min, float max) => Mathf.Clamp01((value - min) / (max - min));

    /// <summary>A LocalizedMessage is a ScriptableObject holding one string per language; fill every language with the same
    /// text. Cached by id.</summary>
    public LocalizedMessage Msg(string id, string text)
    {
        if (_messages.TryGetValue(id, out var cached) && cached != null) return cached;
        var so = ScriptableObject.CreateInstance(Il2CppType.Of<LocalizedMessage>());
        var m = so.Cast<LocalizedMessage>();
        m.name = id;
        m.Id = id;
        SetText(m, text);
        m.hideFlags = HideFlags.HideAndDontSave;
        _messages[id] = m;
        return m;
    }

    /// <summary>A message made earlier by <see cref="Msg"/> (e.g. to rewrite a description in place).</summary>
    public bool TryGetMessage(string id, out LocalizedMessage message)
    {
        if (_messages.TryGetValue(id, out var cached) && cached != null) { message = cached; return true; }
        message = null!;
        return false;
    }

    public static void SetText(LocalizedMessage m, string text)
    {
        m.English = text; m.French = text; m.Italian = text; m.German = text; m.Spanish = text;
        m.BrazilianPortuguese = text; m.TraditionalChinese = text; m.SimplifiedChinese = text;
        m.Korean = text; m.Russian = text; m.Japanese = text; m.Polish = text;
    }

    // ------------------------------------------------------------------ rows

    /// <summary>
    /// Adds rows at the end of one tab, each named after its id. A row's label message has the id as its Id, its
    /// description the id + "_Desc". For a row the kit has no helper for: note <c>Content.childCount</c>, call the game,
    /// then <see cref="NameNewRow"/>.
    /// </summary>
    internal sealed class Builder
    {
        private readonly SettingsRowsKit _kit;
        public readonly SettingsScreenControls Controls;
        public readonly RectTransform Content;
        public readonly PlayerSettingCategory Category;

        public Builder(SettingsRowsKit kit, SettingsScreenControls controls, RectTransform content, PlayerSettingCategory category)
        {
            _kit = kit;
            Controls = controls;
            Content = content;
            Category = category;
        }

        public LocalizedMessage Msg(string id, string text) => _kit.Msg(id, text);

        /// <summary>Name the row added since the content had <paramref name="before"/> children; returns it (null if none was added).</summary>
        public Transform? NameNewRow(int before, string id)
        {
            if (Content.childCount <= before) return null;
            var row = Content.GetChild(Content.childCount - 1);
            row.name = id;
            return row;
        }

        /// <summary>The same empty divider row the game uses between its own groups.</summary>
        public void Spacer(string id)
        {
            int before = Content.childCount;
            Controls.AddDividerItem(Category, id);
            var row = NameNewRow(before, id)?.GetComponent<SettingsItemGUIBase>();
            if (row != null && row.SettingLabel != null) row.SettingLabel.text = "";
        }

        /// <summary>A heading row, like the Controls tab's "Keyboard &amp; Mouse" (SeparatorSettingsItemGUI).</summary>
        public void Heading(string id, string text)
        {
            int before = Content.childCount;
            Controls.AddSeparatorItem(Category, Msg(id, text));
            NameNewRow(before, id);
        }

        /// <summary>A checkbox row bound to a bool preference (saved on change); <paramref name="after"/> runs after the save.</summary>
        public void Toggle(string id, string name, string desc, MelonPreferences_Entry<bool> pref, Action<bool>? after = null)
        {
            Action<bool> onChanged = v => { pref.Value = v; MelonPreferences.Save(); after?.Invoke(v); };
            int before = Content.childCount;
            Controls.AddKeyboardAndMouseSchemeToggleItem(Category, Msg(id, name), pref.Value, onChanged, ToggleStyle, Msg(id + "_Desc", desc), false);
            NameNewRow(before, id);
        }

        /// <summary>A dropdown row; <paramref name="current"/> is clamped to the options.</summary>
        public void Dropdown(string id, string name, string desc, string[] options, int current, Action<int> onChanged)
        {
            var arr = new Il2CppStringArray(options.Length);
            for (int i = 0; i < options.Length; i++) arr[i] = options[i];
            int before = Content.childCount;
            Controls.AddActualDropDownItem(Category, Msg(id, name), arr, Mathf.Clamp(current, 0, options.Length - 1), onChanged, Msg(id + "_Desc", desc), true, false);
            NameNewRow(before, id);
        }

        /// <summary>
        /// A slider row over min..max in steps of <paramref name="step"/> (the game's slider works on 0..1 with a fixed
        /// increment). With snapToStep a drag lands on the same values the arrow keys do. A held key repeats no faster than
        /// every <paramref name="holdRepeatSeconds"/>, so a normal key tap is exactly one step. Returns the row.
        /// </summary>
        public Transform? Slider(string id, string name, string desc, float min, float max, float step, float current,
            Func<float, string> display, Action<float> onChanged, float holdRepeatSeconds = 0.25f, bool snapToStep = true)
        {
            int steps = Mathf.Max(1, Mathf.RoundToInt((max - min) / step));
            float increment = 1f / steps;
            float ToValue(float normalized) => snapToStep
                ? min + Mathf.Round(Mathf.Clamp01(normalized) * steps) / steps * (max - min)
                : min + Mathf.Clamp01(normalized) * (max - min);
            Func<float, string> displayNormalized = n => display(ToValue(n));
            Action<float> changedNormalized = n => onChanged(ToValue(n));

            int before = Content.childCount;
            Controls.AddSliderItem(
                Category,
                null!,                              // IPlayerSettingAdapter<float>: stored, never read
                Msg(id, name),
                Normalize(current, min, max),
                changedNormalized,
                increment,
                displayNormalized,
                Msg(id + "_Desc", desc),
                10,                                 // maxScrollMultiplier: a held key speeds up to 10 steps per repeat
                false,                              // invokeCallbackOnStart
                false,                              // canSelectForFader
                false);                             // showOffOnZero
            var row = NameNewRow(before, id);
            var slider = row?.GetComponent<SliderSettingsItemGUI>();
            if (slider != null && slider.m_scrollHoldThreshold < holdRepeatSeconds) slider.m_scrollHoldThreshold = holdRepeatSeconds;
            return row;
        }

        /// <summary>A button row, like the game's Reset Tutorials.</summary>
        public ButtonSettingsItemGUI? Button(string id, string name, string desc, Action onClick)
        {
            int before = Content.childCount;
            var row = Controls.AddButtonItem(id, Category, Msg(id, name), onClick, Msg(id + "_Desc", desc));
            NameNewRow(before, id);
            return row;
        }
    }
}
