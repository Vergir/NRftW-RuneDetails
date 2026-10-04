"""Build docs/rune-tooltips.html: every rune as its tooltip looks with Rune Details, in Brief and in Detailed mode.

Inputs:
  - the workspace rune inventory (analysis/rune_inventory.csv: name, guid, slot, classes, costs, English template)
  - the mod's self-test output (<game>/UserData/RuneDetails.selftest.out.txt:
    "name<TAB>guid<TAB>brief ‖ detailed line ¦ detailed line …", detailed lines with their TMP colour tags)

Usage: python tools/rune_table.py [selftest.out.txt] [rune_inventory.csv]
"""
import csv
import html
import re
import sys
from collections import defaultdict
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parent
SELFTEST = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(
    r"C:\Games\Steam\steamapps\common\NoRestForTheWicked\UserData\RuneDetails.selftest.out.txt")
ANALYSIS = REPO.parent.parent / "analysis"
INVENTORY = Path(sys.argv[2]) if len(sys.argv) > 2 else ANALYSIS / "rune_inventory.csv"
# Which runes a player can get (CanBeDropped or a weapon's built-in special; see the analysis README section).
OBTAINABILITY = ANALYSIS / "rune_obtainability.csv"
OUT = REPO / "docs" / "rune-tooltips.html"

# Colours as the mod uses them (src/RuneLayout.cs, Prefs.DefaultHiddenFormat); GAME_TEXT approximates the tooltip's.
GREY, GOLD, GAME_TEXT = "#9A9A9A", "#F2E6BD", "#E6E1D6"

ONE_HANDED = {"Axe Club CurvedSword Dagger Hammer Mace Rapier Scepter Spear StraightSword Wakizashi Wand Whip",
              "Axe Club CurvedSword Dagger Hammer Wakizashi Mace Rapier StraightSword Spear Wand"}
TWO_HANDED = "BoStaff CurvedGreatSword GreatAxe GreatClub GreatHammer GreatSword Halberd Katana Nunchaku Scythe Staff"
STAFF_WAND = {"Wand Staff", "Staff Wand"}
SCHOOLS = ["Fire", "Ice", "Lightning", "Plague"]
# Staff/wand runes whose data school is missing or reads oddly; filed by what the game calls them.
SCHOOL_OVERRIDE = {"fireWalk": "Fire", "static": "Lightning", "curse": "Plague", "charredEarth": "Fire"}

# Footnotes: data oddities worth knowing when reading the mod's text (analysis/rune_numbers.md).
# Runes that share a text share one footnote number in their table.
SEGMENTS = "Spawns several segments that hit separately, so the /s is per segment."
KICK = ("Kick numbers are live, from the equipped weapon's item level (here an expected weapon damage of {live}). "
        "Outside a game they read \"N% base dmg\".")
NOTES = {
    "charredEarth": "A second \"Armageddon\": droppable from level 21, but its data is a Physical two-hit melee attack, not the inferno.",
    "plagueColumn": ("Each trap arms after 0.5s, shoots a homing bolt at an enemy within ~10m, then waits ~3s; it bursts "
                     "after 15s or when an enemy touches it. The bolt's direct hit is ~0 (fired without a weapon); only its "
                     "Plague payload counts: 9-10 per needle on the hub dummy in game, = 15% of 70 minus Plague resistance."),
    "arrowstorm": ("The skyward arrow does nothing; 10 arrows fall over ~2s onto a 2m circle 7m ahead, each with its own "
                   "explosion (no falloff): 24-25 on the hub dummy vs 29-30 for the Fungus bow's ×1.25 first shot."),
    "frostStream": SEGMENTS, "inferno": SEGMENTS,
    "static": "Fires a chain lightning (70% weapon dmg) every 2.5s through a periodic modifier; the mod shows only the duration.",
    "fireWalk": "Leaves a damaging fire trail through a periodic modifier; the mod shows only the duration.",
    "frontflipKick": KICK, "dropkick": KICK, "swipeKick": KICK, "turnbackKick": KICK,
}

SECTIONS = [
    ("utility", "Utility runes", "Utility slot. Heals, buffs, auras and movement."),
    ("one", "One-handed weapon runes",
     "Axe, Club, Curved Sword, Dagger, Hammer, Mace, Rapier, Spear, Straight Sword, Wakizashi, Wand, and Scepter/Whip "
     "except where marked ¹."),
    ("two", "Two-handed weapon runes",
     "Bo Staff, Curved Greatsword, Great Axe, Great Club, Great Hammer, Greatsword, Halberd, Katana, Nunchaku, Scythe, Staff."),
    ("daggers", "Dual dagger runes", "Double Dagger only."),
    ("gauntlet", "Gauntlet runes", "Gauntlets only."),
    ("bow", "Bow runes", "Bow and Greatbow."),
    ("staff:Fire", "Staff & Wand: Fire", "Staff and Wand."),
    ("staff:Ice", "Staff & Wand: Ice", "Staff and Wand."),
    ("staff:Lightning", "Staff & Wand: Lightning", "Staff and Wand."),
    ("staff:Plague", "Staff & Wand: Plague", "Staff and Wand."),
    ("any", "Any-weapon runes", "Kicks, throws and evades: every melee weapon, gauntlets, dual daggers and the greatbow."),
    ("unobtainable", "Appendix: unobtainable rune assets",
     "In the database, but no player can get them: CanBeDropped is off, no weapon has them built in, and no vendor, "
     "quest or loot table lists them (analysis/rune_obtainability.csv). Placeholders (the aura clones, Afflictions, "
     "Gale of Speed), the bow's basic Arrow attack, 10 legacy copies of Skyfall Shot, unnamed test assets. The mod's "
     "output here is not maintained."),
]

SLOT_LINE = re.compile(r"^Slot this Rune into (?:an? )?(?P<slot>.+?) to gain the .+$")
TMP_TAG = re.compile(r"<color=(#[0-9A-Fa-f]{6})>|</color>|<[^>]+>")


def cost(raw: str) -> str:
    """'Focus 5; Focus 20 | Cost=...' -> '25 Focus' (Cost + AdditionalCost, what the game's tooltip shows)."""
    totals = defaultdict(float)
    for part in raw.split("|")[0].split(";"):
        m = re.match(r"\s*(\w+)\s+([\d.]+)", part)
        if m:
            totals[m.group(1)] += float(m.group(2))
    shown = [f"{v:g} {k}" for k, v in totals.items() if v > 0]
    return ", ".join(shown) or "–"


def tmp_html(text: str) -> str:
    """TextMeshPro rich text -> HTML: <color=#…> becomes a span, other tags are dropped, the rest escaped."""
    out, pos, depth = [], 0, 0
    for m in TMP_TAG.finditer(text):
        out.append(html.escape(text[pos:m.start()]))
        if m.group(1):
            out.append(f'<span style="color:{m.group(1)}">')
            depth += 1
        elif m.group(0) == "</color>" and depth:
            out.append("</span>")
            depth -= 1
        pos = m.end()
    out.append(html.escape(text[pos:]))
    out.append("</span>" * depth)
    return "".join(out).replace("\n", "<br>")


def split_template(template: str):
    """(full game text, effect paragraph(s), slot) from the English template; slot None for utility runes."""
    full = template.replace("\ufffd", "’").strip()
    first, _, rest = full.partition("\n")
    m = SLOT_LINE.match(re.sub(r"<[^>]+>", "", first).strip())
    if not m:
        return full, full, None
    slot = m.group("slot").replace(" or a ", " or ").replace(" or an ", " or ")
    if slot == "Utility Slot":
        slot = None
    elif slot == "Weapon":
        slot = "Any Weapon"
    return full, rest.strip(), slot


def brief_html(full: str, brief: str | None) -> str:
    """Brief mode: the game's whole text with the grey "(…)" appended in-line."""
    text = tmp_html(full)
    return text + (f' <span style="color:{GREY}">({html.escape(brief)})</span>' if brief else "")


def detailed_html(effect: str, slot: str | None, lines: list[str], brief: str | None, full: str) -> str:
    """Detailed mode: effect text, the slot in gold, an empty line, the coloured lines (falls back to brief)."""
    if not lines:
        return brief_html(full, brief)
    top = tmp_html(effect) + (f'<br><span style="color:{GOLD}">{html.escape(slot)}</span>' if slot else "")
    return top + "<br><br>" + "<br>".join(tmp_html(l) for l in lines)


def category(x) -> str:
    name, stem, slot, classes = x["name"], x["stem"], x["slot"], x["classes"]
    if x["obtainable"] != "1":
        return "unobtainable"
    if slot == "utility":
        return "utility"
    if classes == "Bow Greatbow" or name == "Arrow":
        return "bow"
    if classes in STAFF_WAND:
        school = SCHOOL_OVERRIDE.get(stem) or next((s for s in SCHOOLS if s in x["schools"].split()), "Other")
        return "staff:" + school
    if classes == "Gauntlet":
        return "gauntlet"
    if classes == "DoubleDagger":
        return "daggers"
    if classes == TWO_HANDED:
        return "two"
    if classes in ONE_HANDED:
        return "one"
    return "any"


CSS = """
:root { color-scheme: dark; }
body { margin: 0; background: #12110e; color: #d6d2c8; font: 14px/1.45 "Segoe UI", system-ui, sans-serif; }
header { padding: 20px 24px 8px; }
h1 { margin: 0 0 6px; font-size: 22px; color: #f2e6bd; }
.meta { color: #9a9a9a; max-width: 1100px; }
nav { position: sticky; top: 0; z-index: 2; background: #12110eee; border-bottom: 1px solid #2c2922;
      padding: 8px 24px; display: flex; flex-wrap: wrap; gap: 6px 14px; }
nav a { color: #c9bf9f; text-decoration: none; font-size: 13px; }
nav a:hover { color: #f2e6bd; }
section { padding: 8px 24px 24px; }
h2 { color: #f2e6bd; font-size: 18px; margin: 18px 0 4px; }
.blurb { color: #9a9a9a; margin: 0 0 10px; }
table { border-collapse: collapse; width: 100%; table-layout: fixed; }
th { text-align: left; color: #9a9a9a; font-weight: 600; padding: 6px 10px; border-bottom: 1px solid #2c2922; }
td { vertical-align: top; padding: 10px; border-bottom: 1px solid #23211b; }
col.rune { width: 170px; } col.mode { width: calc(50% - 85px); }
.name { color: #f2e6bd; font-weight: 600; }
.cost, .stem { color: #8a857a; font-size: 12px; }
.tip { background: #1d1b16; border: 1px solid #34302676; border-radius: 4px; padding: 8px 10px; color: """ + GAME_TEXT + """; }
.notes { color: #9a9a9a; font-size: 12.5px; margin-top: 8px; }
.notes p { margin: 2px 0; }
details > summary { cursor: pointer; color: #f2e6bd; font-size: 18px; margin: 18px 0 4px; }
@media (max-width: 760px) { col.rune { width: 110px; } td, th { padding: 6px; } header, section, nav { padding-left: 12px; padding-right: 12px; } }
"""


def main():
    inventory = list(csv.DictReader(INVENTORY.open(encoding="utf-8")))
    obtainable = {r["guid"]: r["obtainable"] for r in csv.DictReader(OBTAINABILITY.open(encoding="utf-8"))}
    for x in inventory:
        x["obtainable"] = obtainable.get(x["guid"], "1")
    mod = {}
    header = []
    for line in SELFTEST.open(encoding="utf-8"):
        line = line.rstrip("\n")
        if line.startswith("#"):
            header.append(line.lstrip("# "))
            continue
        _, guid, text = line.split("\t", 2)
        brief, _, detailed = text.partition(" ‖ ")
        mod[guid] = (None if brief == "(none)" else brief, detailed.split(" ¦ ") if detailed else [])

    rows = defaultdict(list)
    for x in inventory:
        rows[category(x)].append(x)

    live = next((f"{float(h.split(': ')[1]):.1f}" for h in header if h.startswith("live expected weapon damage")), "?")
    anchor = lambda key: "s-" + re.sub(r"[^a-z0-9]+", "-", key.lower())
    out = [
        "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">",
        "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">",
        "<title>Rune Tooltips</title>", f"<style>{CSS}</style></head><body>",
        "<header><h1>Rune tooltips: Brief vs Detailed</h1>",
        f"<p class=\"meta\">Every rune's tooltip text with Rune Details, as the item tooltip shows it. Generated by "
        f"<code>tools/rune_table.py</code> from the mod's in-game self-test (game build 29466, {len(inventory)} rune "
        f"assets; {html.escape(header[0]) if header else ''}) and the English rune texts in "
        f"<code>analysis/rune_inventory.csv</code>. The cost under each name is what the game's tooltip shows "
        f"(cost + additional cost).</p></header>",
        "<nav>" + "".join(f'<a href="#{anchor(k)}">{html.escape(t)}</a>' for k, t, _ in SECTIONS if rows[k]) + "</nav>",
    ]
    for key, title, blurb in SECTIONS:
        items = rows[key]
        if not items:
            continue
        notes = []
        names = defaultdict(int)  # names used twice in one table get their stem
        for x in items:
            names[x["name"]] += 1
        body = []
        for x in sorted(items, key=lambda x: (x["name"] or "~", x["stem"])):
            name = html.escape(x["name"] or "(no name)")
            if key == "unobtainable" or names[x["name"]] > 1:
                name += f' <span class="stem">{html.escape(x["stem"])}</span>'
            if key == "one" and "Scepter" not in x["classes"]:
                name += " ¹"
            note = NOTES.get(x["stem"]) if key != "unobtainable" else None
            if note:
                note = note.replace("{live}", live)
                if note not in notes:
                    notes.append(note)
                name += f" <sup>[{notes.index(note) + 1}]</sup>"
            brief, lines = mod.get(x["guid"], (None, []))
            full, effect, slot = split_template(x["template"])
            body.append(f'<tr><td><div class="name">{name}</div><div class="cost">{html.escape(cost(x["cost_raw"]))}</div></td>'
                        f'<td><div class="tip">{brief_html(full, brief)}</div></td>'
                        f'<td><div class="tip">{detailed_html(effect, slot, lines, brief, full)}</div></td></tr>')
        table = ('<table><colgroup><col class="rune"><col class="mode"><col class="mode"></colgroup>'
                 "<thead><tr><th>Rune</th><th>Brief</th><th>Detailed</th></tr></thead><tbody>"
                 + "".join(body) + "</tbody></table>")
        foot = ('<div class="notes">' + "".join(f"<p>[{i + 1}] {html.escape(n)}</p>" for i, n in enumerate(notes)) + "</div>"
                if notes else "")
        intro = f'<p class="blurb">{html.escape(blurb)} {len(items)} runes.</p>'
        if key == "unobtainable":
            out.append(f'<section id="{anchor(key)}"><details><summary>{html.escape(title)}</summary>{intro}{table}{foot}</details></section>')
        else:
            out.append(f'<section id="{anchor(key)}"><h2>{html.escape(title)}</h2>{intro}{table}{foot}</section>')
    out.append("</body></html>")
    OUT.write_text("\n".join(out), encoding="utf-8")
    print(f"{OUT} ({sum(len(v) for v in rows.values())} runes)")


if __name__ == "__main__":
    main()
