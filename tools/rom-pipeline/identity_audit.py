"""Audit of uncertain identities in reports/library.json: which 'filename-only' versions sit inside titles that also contain verified
versions (could a hack/translation/pirate be mislabelled as the original?), which titles mix licensed classes, which share a normalised
name across publishers (possible unintended merges or splits), and where a grouping relies on an empty publisher. Writes
reports/identity-audit.{md,json}. Read-only; it never changes the index — unresolved cases stay unresolved."""
from __future__ import annotations
import os, sys, json, re
from collections import defaultdict, Counter
sys.path.insert(0, os.path.dirname(__file__)); from common import REPORTS

def main():
    lib = json.load(open(os.path.join(REPORTS, "library.json"), encoding="utf-8")); T = {t["id"]: t for t in lib["titles"]}; V = {v["id"]: v for v in lib["versions"]}
    norm = lambda s: re.sub(r"[^a-z0-9]+", " ", s.lower()).strip()
    rep = {"generated": lib["generated"], "counts": {}, "A_filename_only_in_verified_titles": [], "B_filename_only_titles": [], "C_same_name_several_titles": [], "D_mixed_kinds_in_title": [], "E_empty_publisher_groups": [], "F_hack_or_translation_grouped_with_retail": []}
    fo = [v for v in V.values() if v["identity"] == "filename"]
    rep["counts"]["filename_only_versions"] = len(fo); rep["counts"]["filename_only_kinds"] = dict(Counter(v["kind"] for v in fo))
    for t in lib["titles"]:
        vs = [V[x] for x in t["versions"]]; ids = Counter(v["identity"] for v in vs); kinds = Counter(v["kind"] for v in vs)
        if ids.get("filename") and (ids.get("verified") or ids.get("cartridge-db")):
            rep["A_filename_only_in_verified_titles"].append({"title": t["title"], "title_id": t["id"], "curated": t["curated"], "filename_only": [v["name"] for v in vs if v["identity"] == "filename"][:6], "verified": [v["name"] for v in vs if v["identity"] != "filename"][:3]})
        if ids.get("filename") and not (ids.get("verified") or ids.get("cartridge-db")):
            rep["B_filename_only_titles"].append({"title": t["title"], "title_id": t["id"], "versions": [v["name"] for v in vs][:4], "kinds": dict(kinds)})
        if len(kinds) > 1 and "retail" in kinds and (kinds.get("hack") or kinds.get("translation")):
            rep["F_hack_or_translation_grouped_with_retail"].append({"title": t["title"], "title_id": t["id"], "kinds": dict(kinds), "examples": [v["name"] for v in vs if v["kind"] in ("hack", "translation")][:3]})
        if len(kinds) > 2: rep["D_mixed_kinds_in_title"].append({"title": t["title"], "title_id": t["id"], "kinds": dict(kinds)})
        if not t["publisher"] and len(vs) > 1: rep["E_empty_publisher_groups"].append({"title": t["title"], "title_id": t["id"], "versions": len(vs), "licensed": t["licensed"]})
    by_name = defaultdict(list)
    for t in lib["titles"]: by_name[norm(t["title"])].append(t)
    for n, ts in by_name.items():
        if len(ts) > 1: rep["C_same_name_several_titles"].append({"name": ts[0]["title"], "titles": [{"id": t["id"], "publisher": t["publisher"], "licensed": t["licensed"], "curated": t["curated"], "versions": len(t["versions"])} for t in ts]})
    rep["counts"].update({k: len(v) for k, v in rep.items() if isinstance(v, list)})
    json.dump(rep, open(os.path.join(REPORTS, "identity-audit.json"), "w", encoding="utf-8"), indent=1, ensure_ascii=False)
    c = rep["counts"]
    md = ["# Identity audit — uncertain and sensitive groupings", "", f"Generated from `reports/library.json` ({lib['generated']}). Read-only review; nothing in the index was changed. Unknown stays unknown.", "",
          "| Measure | Count |", "|---|---|", f"| Versions identified by file name only | {c['filename_only_versions']} |", f"| … by kind | {', '.join(f'{k} {n}' for k, n in sorted(c['filename_only_kinds'].items(), key=lambda x: -x[1]))} |",
          f"| A. Titles mixing file-name-only versions with verified/cartridge-DB versions | {c['A_filename_only_in_verified_titles']} |", f"| B. Titles with no verified version at all (file-name-only titles) | {c['B_filename_only_titles']} |",
          f"| C. Normalised names shared by several titles (split by publisher / licensed class — intended, listed for review) | {c['C_same_name_several_titles']} |", f"| D. Titles mixing three or more version kinds | {c['D_mixed_kinds_in_title']} |",
          f"| E. Multi-version titles grouped without a known publisher | {c['E_empty_publisher_groups']} |", f"| F. Hacks or translations grouped under a title that also has retail versions | {c['F_hack_or_translation_grouped_with_retail']} |", "",
          "## How to read this", "", "- A: the file-name-only versions are shown with that confidence and are never promoted; they are listed here because a mislabelled hack would look exactly like this. Spot checks below.",
          "- C: the grouping key includes publisher and licensed class, so same-named games from different publishers (Tetris: Nintendo vs Tengen vs Bullet-Proof) stay apart. Titles with an empty publisher could not be merged safely and stay separate — a known limitation, not a defect.",
          "- F: hacks/translations are versions of the game they modify (kind = hack/translation). They are never presented as the retail release; the version chip and Technical Details show the kind.", "",
          "## A. Spot list (first 40)", ""] + [f"- **{x['title']}**{' (curated)' if x['curated'] else ''}: file-name-only {x['filename_only']} · verified {x['verified']}" for x in rep["A_filename_only_in_verified_titles"][:40]] + ["", "## C. Same name, several titles (first 40)", ""] + [f"- **{x['name']}**: " + "; ".join(f"{t['publisher'] or '(no publisher)'} / {t['licensed']} / {t['versions']} version(s){' / curated' if t['curated'] else ''}" for t in x["titles"]) for x in rep["C_same_name_several_titles"][:40]] + ["", "## F. Hacks / translations with retail siblings (first 40)", ""] + [f"- **{x['title']}**: {x['kinds']} · e.g. {x['examples']}" for x in rep["F_hack_or_translation_grouped_with_retail"][:40]] + ["", "## E. Empty-publisher multi-version titles (first 30)", ""] + [f"- {x['title']} ({x['versions']} versions, {x['licensed']})" for x in rep["E_empty_publisher_groups"][:30]]
    open(os.path.join(REPORTS, "identity-audit.md"), "w", encoding="utf-8").write("\n".join(md) + "\n")
    print(json.dumps(c))

if __name__ == "__main__":
    main()
