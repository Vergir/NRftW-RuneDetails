using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RuneDetails.Patches;

namespace RuneDetails;

/// <summary>
/// How the mod's text is put into the game's rune text.
/// Brief: one grey "(…)" appended in-line.
/// Detailed (layout A, tuned 2026-10-04): the game's effect text (the "Slot this Rune into a Bow to gain the X Rune
/// Attack." line dropped), an empty line, then our labelled lines. Where the game shows neither the rune's type nor its
/// cost (vendor, Runes menu) the lines start with the weapon classes (gold) and "Cost: …"; in the inventory views the
/// cost line appears only when it says more than the game's cost row. Labels grey, values light, numbers gold;
/// resources, elements and notable effects in their own colours.
/// </summary>
internal static class RuneLayout
{
    private const string LabelColor = "#9A9A9A", ValueColor = "#D9D9D9", NumberColor = "#F2E6BD";

    /// <summary>Words with a colour of their own. Elements and resources: placeholders until the game's own colours
    /// are confirmed. Knockdown and friendly fire stand out (rare effects).</summary>
    private static readonly Dictionary<string, string> WordColors = new()
    {
        ["Focus"] = "#E8C547", ["Stamina"] = "#7CC46A", ["Health"] = "#D9534F", ["HP"] = "#D9534F",
        ["Fire"] = "#F08A3C", ["Ice"] = "#8FD3F5", ["Lightning"] = "#F5E05A", ["Plague"] = "#A3C94A", ["Bleed"] = "#C0392B",
        ["Knockdown"] = "#E07B39", ["also hits allies"] = "#E05A4F",
    };

    // English only: other languages keep the game's line. "Slot this Rune into a <color=…>Wand</color> or a
    // <color=…>Staff</color> to gain the <color=…>Fireball</color> Rune Attack."
    private static readonly Regex SlotLine = new(@"^Slot this Rune into (?:an? )?(?<slot>.+?) to gain the .+$", RegexOptions.Compiled);
    private static readonly Regex Tags = new("<[^>]+>", RegexOptions.Compiled);

    // A label ("DMG:", "Poise DMG:" at a line start or after " · "), a coloured word, or a number with its sign, range
    // and unit: "+20", "−20%", "130/150/200%", "3–10", "0.93", "×2", "≈83".
    private static readonly Regex Token = new(
        @"(?<label>(?<=^|· )[A-Z][A-Za-z]*(?: [A-Z][A-Za-z]*)?:)"
        + "|(?<word>" + string.Join("|", WordColors.Keys.OrderByDescending(k => k.Length).Select(k => @"\b" + Regex.Escape(k) + @"\b")) + ")"
        + @"|[+−≈×]?\d[\d.]*(?:[–/]\d[\d.]*)*%?", RegexOptions.Compiled);

    /// <summary>The whole description (item tooltip, utility slot, vendor): brief appended in-line, or detailed.</summary>
    public static string Tooltip(string description, RuneDescriber.RuneText text, DetailLevel level, RuneView view)
    {
        if (level == DetailLevel.Detailed && text.Lines.Count > 0)
        {
            bool full = view == RuneView.Vendor; // the vendor's tooltip shows neither the type nor the cost
            return Effect(description).TrimEnd() + "\n\n" + Block(text, full ? Slot(description) : null, full || text.CostNotable);
        }
        return text.Brief == null ? description : description.TrimEnd() + Prefs.HiddenFormat.Value.Replace("{extra}", text.Brief);
    }

    /// <summary>The rune screen's sentence (the game shows only the effect's first sentence there, and no type or cost).</summary>
    public static string RuneScreen(string sentence, string? description, RuneDescriber.RuneText text, DetailLevel level)
    {
        if (level == DetailLevel.Detailed && text.Lines.Count > 0)
            return sentence.TrimEnd() + "\n\n" + Block(text, description == null ? null : Slot(description), true);
        return text.Brief == null ? sentence : sentence.TrimEnd() + Prefs.HiddenFormat.Value.Replace("{extra}", text.Brief);
    }

    /// <summary>The detailed lines with their colour tags; withCost puts "Cost: …" first (self-test, docs).</summary>
    public static IEnumerable<string> ColourLines(RuneDescriber.RuneText text, bool withCost)
    {
        if (withCost && text.Cost != null) yield return Colour("Cost: " + text.Cost);
        foreach (var line in text.Lines) yield return Colour(line);
    }

    private static string Block(RuneDescriber.RuneText text, string? slot, bool withCost)
    {
        var lines = ColourLines(text, withCost).ToList();
        if (slot != null) lines.Insert(0, Wrap(NumberColor, slot));
        return string.Join("\n", lines);
    }

    /// <summary>One detailed line as closed colour runs (TMP keeps a colour stack, so nothing is left open).</summary>
    private static string Colour(string line)
    {
        var sb = new StringBuilder();
        int pos = 0;
        foreach (Match m in Token.Matches(line))
        {
            if (m.Index > pos) sb.Append(Wrap(ValueColor, line.Substring(pos, m.Index - pos)));
            string color = m.Groups["label"].Success ? LabelColor : m.Groups["word"].Success ? WordColors[m.Value] : NumberColor;
            sb.Append(Wrap(color, m.Value));
            pos = m.Index + m.Length;
        }
        if (pos < line.Length) sb.Append(Wrap(ValueColor, line.Substring(pos)));
        return sb.ToString();
    }

    private static string Wrap(string color, string text) => $"<color={color}>{text}</color>";

    /// <summary>The game's text without its "Slot this Rune into a X to gain the Y …" line (English only).</summary>
    private static string Effect(string description)
    {
        int cut = description.IndexOf('\n');
        string first = cut < 0 ? description : description.Substring(0, cut);
        if (!SlotLine.IsMatch(Tags.Replace(first, "").Trim())) return description;
        return cut < 0 ? "" : description.Substring(cut).Trim();
    }

    /// <summary>"One-Handed Weapon", "Wand or Staff", "Any Weapon"; null for utility runes or other languages.</summary>
    private static string? Slot(string description)
    {
        int cut = description.IndexOf('\n');
        var m = SlotLine.Match(Tags.Replace(cut < 0 ? description : description.Substring(0, cut), "").Trim());
        if (!m.Success) return null;
        string slot = m.Groups["slot"].Value.Replace(" or a ", " or ").Replace(" or an ", " or ");
        return slot == "Utility Slot" ? null : slot == "Weapon" ? "Any Weapon" : slot;
    }
}
