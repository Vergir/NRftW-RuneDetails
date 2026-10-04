using System.Text.RegularExpressions;

namespace RuneDetails;

/// <summary>
/// How the mod's text is put into the game's rune text. Brief: one grey "(…)" appended in-line. Detailed (layout A,
/// 2026-10-04, to be tuned in game): the "Slot this Rune into a Bow to gain the X Rune Attack." line becomes just the
/// weapon classes ("Bow"; removed for utility runes, whose tooltip already says Utility), and labelled grey lines
/// follow the game's text ("Damage: …", "Timing: …").
/// </summary>
internal static class RuneLayout
{
    private const string DetailColor = "#9A9A9A";

    // English only: other languages keep the game's line. "Slot this Rune into a <color=…>Wand</color> or a
    // <color=…>Staff</color> to gain the <color=…>Fireball</color> Rune Attack."
    private static readonly Regex SlotLine = new(@"^Slot this Rune into (?:an? )?(?<slot>.+?) to gain the .+$", RegexOptions.Compiled);
    private static readonly Regex Tags = new("<[^>]+>", RegexOptions.Compiled);

    /// <summary>The whole description (tooltip views): brief appended in-line, or detailed with the slot header.</summary>
    public static string Tooltip(string description, RuneDescriber.RuneText text, DetailLevel level)
    {
        if (level == DetailLevel.Detailed && text.Lines.Count > 0)
            return Header(description).TrimEnd() + "\n" + Block(text);
        return text.Brief == null ? description : description.TrimEnd() + Prefs.HiddenFormat.Value.Replace("{extra}", text.Brief);
    }

    /// <summary>The rune screen's sentence (the game shows only the effect's first sentence there).</summary>
    public static string RuneScreen(string sentence, RuneDescriber.RuneText text, DetailLevel level)
    {
        if (level == DetailLevel.Detailed && text.Lines.Count > 0) return sentence.TrimEnd() + "\n" + Block(text);
        return text.Brief == null ? sentence : sentence.TrimEnd() + Prefs.HiddenFormat.Value.Replace("{extra}", text.Brief);
    }

    private static string Block(RuneDescriber.RuneText text) =>
        $"<color={DetailColor}>{string.Join("\n", text.Lines)}</color>";

    /// <summary>Replace the first paragraph's "Slot this Rune into a X to gain the Y …" with "X" (grey).</summary>
    private static string Header(string description)
    {
        int cut = description.IndexOf('\n');
        string first = cut < 0 ? description : description.Substring(0, cut);
        var m = SlotLine.Match(Tags.Replace(first, "").Trim());
        if (!m.Success) return description;
        string rest = cut < 0 ? "" : description.Substring(cut).TrimStart('\n', '\r');
        string slot = m.Groups["slot"].Value.Replace(" or a ", " or ").Replace(" or an ", " or ");
        if (slot == "Utility Slot") return rest;
        if (slot == "Weapon") slot = "Any Weapon";
        return $"<color={DetailColor}>{slot}</color>\n{rest}";
    }
}
