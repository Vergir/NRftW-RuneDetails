using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using HarmonyLib;
using Il2CppMoon.Forsaken;

namespace RuneDetails;

/// <summary>
/// Detailed mode shows a rune's type and cost exactly once: the description always carries our Type and plain Cost
/// lines inside a TMP link marker (RuneLayout.MarkOpen), and after the UI has updated, every open item info panel that
/// shows the game's own type/cost row (InventoryItemInfoElement.RuneTypeElement: inventory, utility slots) gets the
/// marked lines removed. Panels without that row (vendor, …) keep ours. The Runes menu builds its own text.
/// </summary>
internal static class TypeCostDedup
{
    private static readonly List<InventoryItemInfoElement> Panels = new();
    private static readonly Regex Marked = new(Regex.Escape(RuneLayout.MarkOpen) + ".*?" + Regex.Escape(RuneLayout.MarkClose) + @"\n?",
        RegexOptions.Compiled | RegexOptions.Singleline);

    public static void Register(InventoryItemInfoElement panel)
    {
        if (!Panels.Contains(panel)) Panels.Add(panel);
    }

    /// <summary>Called from OnLateUpdate: a few frames after a detailed rune tooltip was built.</summary>
    public static void Tick()
    {
        if (RuneLayout.PendingDedupFrames <= 0) return;
        RuneLayout.PendingDedupFrames--;
        for (int i = Panels.Count - 1; i >= 0; i--)
        {
            var panel = Panels[i];
            if (panel == null) { Panels.RemoveAt(i); continue; }
            try
            {
                var text = panel.DescriptionText;
                var row = panel.RuneTypeElement;
                if (text == null || row == null || !row.activeInHierarchy || !panel.gameObject.activeInHierarchy) continue;
                string current = text.text;
                if (current == null || !current.Contains(RuneLayout.MarkOpen)) continue;
                text.text = Marked.Replace(current, "");
            }
            catch (Exception e)
            {
                RuneDetailsMod.Log.Warning("Type/cost dedup: " + e.Message);
                Panels.RemoveAt(i);
            }
        }
    }
}

/// <summary>InventoryItemInfoElement.OnEnable() (no parameters): collect the item info panels.</summary>
[HarmonyPatch(typeof(InventoryItemInfoElement), "OnEnable")]
internal static class InfoPanelEnablePatch
{
    static void Postfix(InventoryItemInfoElement __instance) => TypeCostDedup.Register(__instance);
}
