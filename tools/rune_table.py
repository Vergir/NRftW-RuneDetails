"""Build docs/rune-tooltips.md: every rune's game text next to what Rune Details appends.

Inputs:
  - the workspace rune inventory (analysis/rune_inventory.csv: name, guid, slot, classes, costs, English template)
  - the mod's self-test output (<game>/UserData/RuneDetails.selftest.out.txt: "name<TAB>guid<TAB>text")

Usage: python tools/rune_table.py [selftest.out.txt] [rune_inventory.csv]
"""
import csv
import re
import sys
from collections import defaultdict
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parent
SELFTEST = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(
    r"C:\Games\Steam\steamapps\common\NoRestForTheWicked\UserData\RuneDetails.selftest.out.txt")
INVENTORY = Path(sys.argv[2]) if len(sys.argv) > 2 else REPO.parent.parent / "analysis" / "rune_inventory.csv"
OUT = REPO / "docs" / "rune-tooltips.md"

ONE_HANDED = {"Axe Club CurvedSword Dagger Hammer Mace Rapier Scepter Spear StraightSword Wakizashi Wand Whip",
              "Axe Club CurvedSword Dagger Hammer Wakizashi Mace Rapier StraightSword Spear Wand"}
TWO_HANDED = "BoStaff CurvedGreatSword GreatAxe GreatClub GreatHammer GreatSword Halberd Katana Nunchaku Scythe Staff"
STAFF_WAND = {"Wand Staff", "Staff Wand"}
SCHOOLS = ["Fire", "Ice", "Lightning", "Plague"]
# Staff/wand runes whose data school is missing or reads oddly; filed by what the game calls them.
SCHOOL_OVERRIDE = {"fireWalk": "Fire", "static": "Lightning", "curse": "Plague", "charredEarth": "Fire"}

# Footnotes: data oddities worth knowing when reading the mod column (analysis/rune_numbers.md).
# Runes that share a text share one footnote number in their table.
AFFLICTION = "All four Afflictions are one ×1 Ice hit in the data; the debuff from the text has no data."
AURA_CLONE = ("Deflect, Eagle Eye, Life Leech and Slow Aura reuse Heal Aura's action and have only the generic text: "
              "unfinished placeholders, probably not obtainable.")
SEGMENTS = "Spawns several segments that hit separately, so the /s is per segment."
KICK = ("Kick numbers are live, from the equipped weapon's item level (here expected weapon damage 5.2, an "
        "item-level-9 weapon). Outside a game they read \"N% base dmg\".")
NOTES = {
    "galeOfSpeed": "The data applies Damage Surge (+20% Overall Damage Dealt), no movement speed, although the text promises speed.",
    "coldAffliction": AFFLICTION, "electricAffliction": AFFLICTION, "heatAffliction": AFFLICTION, "plagueAffliction": AFFLICTION,
    "deflect": AURA_CLONE, "eagleEye": AURA_CLONE, "lifeLeech": AURA_CLONE, "slowAura": AURA_CLONE,
    "curse": "The text says plague beam; the damage school in the data is Fire.",
    "plagueColumn": "Not covered yet: a trap entity (BoneBolt projectile ×0.8/×2 by charge + 15% of typical enemy HP).",
    "frostStream": SEGMENTS, "inferno": SEGMENTS,
    "static": "Fires a chain lightning (70% weapon dmg) every 2.5s through a periodic modifier; the mod shows only the duration.",
    "fireWalk": "Leaves a damaging fire trail through a periodic modifier; the mod shows only the duration.",
    "frontflipKick": KICK, "dropkick": KICK, "swipeKick": KICK, "turnbackKick": KICK,
}


def cost(raw: str) -> str:
    """'Focus 5; Focus 20 | Cost=...' -> '25 Focus' (Cost + AdditionalCost, the total the rune spends)."""
    totals = defaultdict(float)
    for part in raw.split("|")[0].split(";"):
        m = re.match(r"\s*(\w+)\s+([\d.]+)", part)
        if m:
            totals[m.group(1)] += float(m.group(2))
    shown = [f"{v:g} {k}" for k, v in totals.items() if v > 0]
    return ", ".join(shown) or "–"


def game_text(template: str) -> str:
    """Drop the 'Slot this Rune into a … to gain the X Spell.' line; keep the effect paragraph(s)."""
    paragraphs = [p.strip() for p in template.replace("\ufffd", "’").split("\n") if p.strip()]
    if paragraphs and paragraphs[0].startswith("Slot this Rune"):
        paragraphs = paragraphs[1:]
    text = re.sub(r"<[^>]+>", "", " ".join(paragraphs))
    return text or "*(only the generic \"Slot this Rune…\" line)*"


def cell(s: str) -> str:
    return s.replace("|", "\\|")


def category(x) -> str:
    name, stem, slot, classes = x["name"], x["stem"], x["slot"], x["classes"]
    if not name or (name == "Skyfall Shot" and stem != "skyfallShot"):
        return "legacy"
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


SECTIONS = [
    ("utility", "Utility runes", "Utility slot. Heals, buffs, auras and movement. The four Afflictions are Staff/Wand only."),
    ("one", "One-handed weapon runes",
     "Axe, Club, Curved Sword, Dagger, Hammer, Mace, Rapier, Spear, Straight Sword, Wakizashi, Wand, and Scepter/Whip "
     "except where marked ¹."),
    ("two", "Two-handed weapon runes",
     "Bo Staff, Curved Greatsword, Great Axe, Great Club, Great Hammer, Greatsword, Halberd, Katana, Nunchaku, Scythe, Staff."),
    ("daggers", "Dual dagger runes", "Double Dagger only."),
    ("gauntlet", "Gauntlet runes", "Gauntlets only."),
    ("bow", "Bow runes", "Bow and Greatbow."),
    ("staff:Fire", "Staff & Wand runes: Fire", "Staff and Wand."),
    ("staff:Ice", "Staff & Wand runes: Ice", "Staff and Wand."),
    ("staff:Lightning", "Staff & Wand runes: Lightning", "Staff and Wand."),
    ("staff:Plague", "Staff & Wand runes: Plague", "Staff and Wand."),
    ("any", "Any-weapon runes", "Kicks, throws and evades: every melee weapon, gauntlets, dual daggers and the greatbow."),
    ("legacy", "Appendix: unnamed and legacy rune assets",
     "In the database but probably not obtainable: assets without a name (the game would show an empty title) and 11 "
     "old stems that all reuse the name \"Skyfall Shot\". Listed by their internal stem."),
]


def main():
    inventory = list(csv.DictReader(INVENTORY.open(encoding="utf-8")))
    mod = {}
    header = []
    for line in SELFTEST.open(encoding="utf-8"):
        line = line.rstrip("\n")
        if line.startswith("#"):
            header.append(line.lstrip("# "))
            continue
        _, guid, text = line.split("\t", 2)
        mod[guid] = text

    rows = defaultdict(list)
    for x in inventory:
        rows[category(x)].append(x)

    out = [
        "# Rune tooltips: game text vs Rune Details",
        "",
        "Generated by `tools/rune_table.py` from the mod's in-game self-test (Rune Details 0.1.0, game build 29466, "
        f"{len(inventory)} rune assets; {header[0] if header else ''}) and the English rune texts in "
        "`analysis/rune_inventory.csv`.",
        "",
        "- **Game text**: the rune's description without its first line (\"Slot this Rune into a … to gain the X "
        "Spell.\"). The rune screen shows only this part.",
        "- **Rune Details adds**: what the mod appends to that text, in grey parentheses: "
        "`… on impact. (130/150/200% weapon dmg by charge)`. *(nothing)* = the mod adds nothing.",
        "- **Cost**: from the rune's action data (cost + additional cost). The mod does not show it; it is here for context.",
        "",
        "Contents: " + " · ".join(f"[{title}](#{re.sub(r'[^a-z0-9 -]', '', title.lower()).replace(' ', '-')})"
                                   for key, title, _ in SECTIONS if rows[key]),
        "",
    ]
    for key, title, blurb in SECTIONS:
        items = rows[key]
        if not items:
            continue
        notes = []
        names = defaultdict(int)  # names used twice in one table get their stem
        for x in items:
            names[x["name"]] += 1
        out += [f"## {title}", "", f"{blurb} {len(items)} runes.", "",
                "| Rune | Cost | Game text | Rune Details adds |", "|---|---|---|---|"]
        for x in sorted(items, key=lambda x: (x["name"] or "~", x["stem"])):
            name = x["name"] if key != "legacy" else f"{x['name'] or '(no name)'} · `{x['stem']}`"
            if key != "legacy" and names[x["name"]] > 1:
                name += f" · `{x['stem']}`"
            if key == "one" and "Scepter" not in x["classes"]:
                name += " ¹"
            note = NOTES.get(x["stem"])
            if note:
                if note not in notes:
                    notes.append(note)
                name += f" [{notes.index(note) + 1}]"
            added = mod.get(x["guid"], "(none)")
            added = "*(nothing)*" if added == "(none)" else added
            out.append(f"| {cell(name)} | {cost(x['cost_raw'])} | {cell(game_text(x['template']))} | {cell(added)} |")
        if notes:
            out.append("")
            out += [f"[{i + 1}] {n}  " for i, n in enumerate(notes)]
        out.append("")
    OUT.write_text("\n".join(out), encoding="utf-8")
    print(f"{OUT} ({sum(len(v) for v in rows.values())} runes)")


if __name__ == "__main__":
    main()
