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
    private const string InvulnerableColor = "#5B9968"; // the game's "Positive" style: a rare effect worth spotting

    /// <summary>Words with a colour of their own, taken from the game (2026-10-04):
    /// elements = the TMP "Default Style Sheet" styles the game wraps element words and damage numbers in ([!sFire]);
    /// Focus = the rune slots' Focus-cost text; Stamina = the bright end of the HUD stamina ring texture; Health and the
    /// friendly-fire warning = the sheet's "Negative" style; Knockdown = its "Heavy" style.</summary>
    private static readonly Dictionary<string, string> WordColors = new()
    {
        ["Focus"] = "#F3EC04", ["Stamina"] = "#44A11D", ["Health"] = "#F15E4B", ["HP"] = "#F15E4B",
        ["Fire"] = "#F09000", ["Ice"] = "#80C0F0", ["Lightning"] = "#D0C000", ["Plague"] = "#9060F0", ["Bleed"] = "#F07070",
        ["Knockdown"] = "#B79052",
    };

    // English only: other languages keep the game's line. "Slot this Rune into a <color=…>Wand</color> or a
    // <color=…>Staff</color> to gain the <color=…>Fireball</color> Rune Attack."
    private static readonly Regex SlotLine = new(@"^Slot this Rune into (?:an? )?(?<slot>.+?) to gain the .+$", RegexOptions.Compiled);
    private static readonly Regex Tags = new("<[^>]+>", RegexOptions.Compiled);

    // A label ("DMG:", "Poise DMG:" at a line start or after " · "), a coloured word, or a number with its sign, range
    // and unit: "+20", "-20%", "130/150/200%", "3–10", "0.93", "×2", "~83".
    private static readonly Regex Token = new(
        @"(?<label>(?<=^|· )[A-Z][A-Za-z]*(?: [A-Z][A-Za-z]*)?:)"
        + "|(?<word>" + string.Join("|", WordColors.Keys.OrderByDescending(k => k.Length).Select(k => @"\b" + Regex.Escape(k) + @"\b")) + ")"
        + @"|[+\-~×]?\d[\d.]*(?:[–/]\d[\d.]*)*%?", RegexOptions.Compiled);

    /// <summary>Our Type and plain Cost lines as last written ("…\n"): TypeCostDedup removes exactly this text where the game
    /// shows its own type/cost row. (A TMP link tag around them added a gap below in the info panels.)</summary>
    public static readonly HashSet<string> MarkedBlocks = new();

    /// <summary>Set when a detailed tooltip was built: TypeCostDedup then checks the open info panels for a few frames.</summary>
    public static int PendingDedupFrames;

    /// <summary>The whole description (item tooltip, utility slot, vendor, …): brief appended in-line, or detailed.</summary>
    public static string Tooltip(string description, RuneDescriber.RuneText text, DetailLevel level)
    {
        if (level == DetailLevel.Detailed && text.Lines.Count > 0)
        {
            PendingDedupFrames = 3;
            // One line break and a half-height empty line: the info panels' text adds paragraph spacing at each break,
            // so the plain empty line the Runes menu uses looks too tall there.
            return Effect(description).TrimEnd() + "\n<size=50%> </size>\n" + Block(text, Slot(description));
        }
        return text.Brief == null ? description : description.TrimEnd() + Prefs.HiddenFormat.Value.Replace("{extra}", text.Brief);
    }

    /// <summary>The rune screen's sentence (the game shows only the effect's first sentence there, and no type or cost).</summary>
    public static string RuneScreen(string sentence, string? description, RuneDescriber.RuneText text, DetailLevel level)
    {
        if (level == DetailLevel.Detailed && text.Lines.Count > 0)
            return sentence.TrimEnd() + "\n\n" + Block(text, description == null ? null : Slot(description));
        return text.Brief == null ? sentence : sentence.TrimEnd() + Prefs.HiddenFormat.Value.Replace("{extra}", text.Brief);
    }

    /// <summary>The detailed lines with their colour tags; withCost puts "Cost: …" first (self-test, docs).</summary>
    public static IEnumerable<string> ColourLines(RuneDescriber.RuneText text, bool withCost)
    {
        if (withCost && text.Cost != null) yield return Colour("Cost: " + text.Cost);
        foreach (var line in text.Lines) yield return Colour(line);
    }

    /// <summary>Type and cost first, then the lines. The type and a plain cost are the facts the game's own type/cost
    /// row shows, so they are marked for TypeCostDedup; a cost that says more (a drain, "5 Focus (needs 25)") stays.</summary>
    private static string Block(RuneDescriber.RuneText text, string? slot)
    {
        var marked = new List<string>();
        var always = new List<string>();
        if (slot != null) marked.Add(Wrap(NumberColor, slot));
        if (text.Cost != null) (text.CostNotable ? always : marked).Add(Colour("Cost: " + text.Cost));
        var lines = new List<string>();
        if (marked.Count > 0)
        {
            string block = string.Join("\n", marked);
            MarkedBlocks.Add(block + "\n");
            lines.Add(block);
        }
        lines.AddRange(always);
        lines.AddRange(ColourLines(text, withCost: false));
        return string.Join("\n", lines);
    }

    private static readonly HashSet<string> Resources = new() { "Focus", "Stamina", "Health", "HP" };

    /// <summary>One detailed line as closed colour runs (TMP keeps a colour stack, so nothing is left open). Resource
    /// words get their colour only in the Cost line, to keep the rest calm.</summary>
    private static string Colour(string line)
    {
        bool cost = line.StartsWith("Cost:");
        var sb = new StringBuilder();
        int pos = 0;
        foreach (Match m in Token.Matches(line))
        {
            if (m.Index > pos) sb.Append(Wrap(ValueColor, line.Substring(pos, m.Index - pos)));
            string color = m.Groups["label"].Success ? (m.Value == "Invulnerable:" ? InvulnerableColor : LabelColor)
                : m.Groups["word"].Success ? (cost || !Resources.Contains(m.Value) ? WordColors[m.Value] : ValueColor)
                : NumberColor;
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

    /// <summary>"One-Handed Weapon", "Wand or Staff", "Any Weapon", "Utility Rune"; null in other languages.</summary>
    private static string? Slot(string description)
    {
        int cut = description.IndexOf('\n');
        var m = SlotLine.Match(Tags.Replace(cut < 0 ? description : description.Substring(0, cut), "").Trim());
        if (!m.Success) return null;
        string slot = m.Groups["slot"].Value.Replace(" or a ", " or ").Replace(" or an ", " or ");
        return slot == "Utility Slot" ? "Utility Rune" : slot == "Weapon" ? "Any Weapon" : slot;
    }
}
