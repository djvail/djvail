"""Regenerates docs/05-Rule-Catalog.md from samples/Default.profile.json.

    dotnet run --project src/VailITCodeClean.Cli -- export-profile samples/Default.profile.json
    python scripts/Update-RuleCatalog.py
"""
import json
import pathlib

root = pathlib.Path(__file__).resolve().parent.parent
profile = json.loads((root / "samples" / "Default.profile.json").read_text(encoding="utf-8-sig"))
rules = profile["Rules"]
categories = list(dict.fromkeys(r["Category"] for r in rules))


def anchor(name: str) -> str:
    return name.lower().replace(" & ", "--").replace(" ", "-").replace("(", "").replace(")", "")


out = [
    "# Rule catalog", "",
    "Every rule that ships with VailIT Code Clean (the `Default` profile). "
    "**On** = enabled by default. Rules marked *off* are noisy in most code bases or are templates you must edit first.", "",
    "The source of these rules is `src/VailITCodeClean.Core/Rules/DefaultRules.*.cs`; to regenerate this page run "
    "`vailit-codeclean export-profile samples/Default.profile.json` and the script in `scripts/Update-RuleCatalog.py`.", "",
    "| Category | Rules | On by default |", "|---|---:|---:|",
]
for c in categories:
    rs = [r for r in rules if r["Category"] == c]
    out.append(f"| [{c}](#{anchor(c)}) | {len(rs)} | {sum(r['Enabled'] for r in rs)} |")
out.append(f"| **Total** | **{len(rules)}** | **{sum(r['Enabled'] for r in rules)}** |")
for c in categories:
    out += ["", f"## {c}", "", "| On | Severity | Id | What it finds | Placeholder |", "|---|---|---|---|---|"]
    for r in (r for r in rules if r["Category"] == c):
        text = (r["Name"] + " - " + r["Description"]).replace("|", "\\|").replace("\n", " ")
        label = r["TokenLabel"] or "category"
        out.append(f"| {'on' if r['Enabled'] else '*off*'} | {r['Severity']} | `{r['Id']}` | {text} | `VAILIT_{label}_0001` |")
(root / "docs" / "05-Rule-Catalog.md").write_text("\n".join(out) + "\n", encoding="utf-8")
print(f"Wrote {len(rules)} rules.")
