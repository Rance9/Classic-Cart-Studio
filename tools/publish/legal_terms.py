"""The Installation Terms: one maintained source, three copies. The source is tools/publish/assets/legal/INSTALLATION-TERMS.md (Markdown,
edited only there). This writes, from it:
  tools/publish/assets/legal/INSTALLATION-TERMS.txt   the installer's and the release's copy: plain text, UTF-8 with a byte-order mark (the
                                                       setup page's text box reads the encoding from it); headings and paragraphs as lines
  tools/rom-pipeline/studio/guide.html                 the Help & User Guide chapter "Installation Terms (EULA)", between its two markers
  tools/publish/assets/legal/terms-manifest.json       the document's identity and every copy's SHA-256
Formatting is the only difference between the copies: Markdown's **bold** and `code` marks become plain text (txt) or <b>/<code> (Help),
headings lose their # marks, and every copy keeps the same words in the same order. Each copy's "text identity" is the SHA-256 of its words
with all markup removed and whitespace collapsed; --check fails unless all three equal the source's.
  python tools/publish/legal_terms.py            write the copies
  python tools/publish/legal_terms.py --check    check them (tests, the release build)"""
from __future__ import annotations
import hashlib, html, json, os, re, sys
HERE = os.path.dirname(os.path.abspath(__file__)); REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
SRC = os.path.join(REPO, "tools", "publish", "assets", "legal", "INSTALLATION-TERMS.md")
TXT = os.path.join(REPO, "tools", "publish", "assets", "legal", "INSTALLATION-TERMS.txt")
MAN = os.path.join(REPO, "tools", "publish", "assets", "legal", "terms-manifest.json")
GUIDE = os.path.join(REPO, "tools", "rom-pipeline", "studio", "guide.html")
DOC_ID, VERSION = "CCS-INSTALLATION-TERMS", "1.0"
BEGIN, END = "<!-- installation-terms:begin -->", "<!-- installation-terms:end -->"

def sha(b: bytes) -> str: return hashlib.sha256(b).hexdigest()
def rtext(p: str) -> str:
    with open(p, encoding="utf-8") as f: return f.read()
def rbytes(p: str) -> bytes:
    with open(p, "rb") as f: return f.read()
def wbytes(p: str, b: bytes):
    with open(p, "wb") as f: f.write(b)
def wtext(p: str, s: str):
    with open(p, "w", encoding="utf-8", newline="\n") as f: f.write(s)
def words(text: str) -> str: return " ".join(text.split())
def identity(text: str) -> str: return sha(words(text).encode("utf-8"))
def md_plain(s: str) -> str: return re.sub(r"`([^`]*)`", r"\1", s.replace("**", ""))
def md_html(s: str) -> str:
    s = html.escape(s, quote=False)
    s = re.sub(r"\*\*(.+?)\*\*", r"<b>\1</b>", s); s = re.sub(r"`([^`]*)`", r"<code>\1</code>", s)
    return re.sub(r"(https://[A-Za-z0-9./_-]+[A-Za-z0-9/_-])", r'<a href="\1" rel="noopener">\1</a>', s)

def blocks(md: str):
    """(kind, text) for every non-empty line of the source: title (# / ## at the top), heading (## N.), paragraph"""
    out = []
    for line in md.split("\n"):
        if not line.strip(): continue
        if line.startswith("# "): out.append(("title", line[2:]))
        elif line.startswith("## "): out.append(("heading", line[3:]))
        else: out.append(("para", line))
    return out

def make_txt(md: str) -> bytes:
    return ("﻿" + "\n\n".join(md_plain(t) for _, t in blocks(md)) + "\n").encode("utf-8")

def make_html(md: str) -> str:
    parts = []
    for kind, t in blocks(md):
        if kind == "title": parts.append(f"<p class=\"lt\"><b>{md_html(t)}</b></p>")
        elif kind == "heading": parts.append(f"<h4>{md_html(t)}</h4>")
        else: parts.append(f"<p>{md_html(t)}</p>")
    doc = "\n".join(parts)
    return (f'{BEGIN}\n<section data-ch="terms"><h3>Installation Terms (EULA) <span>· version {VERSION}</span></h3>\n'
            '<p class="gintro">These are the terms the Windows installer shows before it installs anything. Reading them here is not agreeing to them, '
            'and neither the website nor the Studio asks you to agree again. The Windows app keeps a copy in its program folder, in <b>legal\\INSTALLATION-TERMS.txt</b>, '
            'beside the GNU General Public License and the third-party notices.</p>\n'
            f'<div class="terms-doc" data-doc="{DOC_ID}" data-version="{VERSION}">\n{doc}\n</div>\n'
            '<p>Open-source licenses: <a href="#" data-guide="credits">Credits</a> lists every component and its license; the full texts are '
            '<a href="https://github.com/Rance9/Classic-Cart-Studio/blob/main/LICENSE" rel="noopener">LICENSE</a> and '
            '<a href="https://github.com/Rance9/Classic-Cart-Studio/blob/main/THIRD_PARTY_NOTICES.md" rel="noopener">THIRD_PARTY_NOTICES.md</a>.</p>\n'
            '<div class="gft"><button class="btn sm" data-prev="legal">← Legal, Privacy &amp; Disclaimers</button><span class="sp"></span><button class="btn sm" data-next="credits">Next: Credits →</button></div>\n'
            f'</section>\n{END}')

def html_doc_text(fragment: str) -> str:
    """the words of the document inside the Help chapter (its terms-doc block only: not the chapter's own introduction)"""
    m = re.search(r'<div class="terms-doc"[^>]*>(.*?)\n</div>', fragment, re.S); assert m, "no terms-doc block"
    return html.unescape(re.sub(r"<[^>]+>", "", m.group(1)))   # blocks are on lines of their own: removing the tags keeps the words apart

def current_fragment(guide: str) -> str | None:
    i, j = guide.find(BEGIN), guide.find(END)
    return guide[i:j + len(END)] if i >= 0 and j > i else None

def manifest(md: str, txt: bytes, frag: str) -> dict:
    return {"schema": "ccs-legal-terms/1", "document": DOC_ID, "version": VERSION, "source": "tools/publish/assets/legal/INSTALLATION-TERMS.md",
            "source_sha256": sha(md.encode("utf-8")), "text_identity_sha256": source_identity(md),
            "copies": {"installer_and_release_txt": {"path": "tools/publish/assets/legal/INSTALLATION-TERMS.txt", "sha256": sha(txt), "text_identity_sha256": identity(txt.decode("utf-8-sig"))},
                       "help_chapter": {"path": "tools/rom-pipeline/studio/guide.html (between its markers)", "sha256": sha(frag.encode("utf-8")), "text_identity_sha256": identity(html_doc_text(frag))}},
            "normalization": "Markdown marks removed (**, `, heading #); Help adds <b>, <code>, links on https addresses and <h4> headings; whitespace collapsed for the text identity"}

def source_identity(md: str) -> str: return identity("\n".join(md_plain(t) for _, t in blocks(md)))

def main(check: bool) -> int:
    md = rtext(SRC); txt = make_txt(md); frag = make_html(md); guide = rtext(GUIDE)
    want = source_identity(md); problems = []
    if check:
        have_txt = rbytes(TXT) if os.path.exists(TXT) else b""; have_frag = current_fragment(guide) or ""
        if have_txt != txt: problems.append("INSTALLATION-TERMS.txt is not the source's copy (run legal_terms.py)")
        if have_frag != frag: problems.append("the Help chapter is not the source's copy (run legal_terms.py)")
        for name, t in (("txt", have_txt.decode("utf-8-sig", "replace")), ("help", html_doc_text(have_frag) if have_frag else "")):
            if identity(t) != want: problems.append(f"the {name} copy's words differ from the source")
        m = json.loads(rtext(MAN)) if os.path.exists(MAN) else {}
        if m.get("copies") != manifest(md, txt, frag)["copies"] or m.get("source_sha256") != sha(md.encode("utf-8")): problems.append("terms-manifest.json is out of date")
        for p in problems: print("FAIL", p)
        print("RESULT", "OK: the installer, release and Help copies match the source" if not problems else f"{len(problems)} problem(s)", "| text identity", want[:16])
        return 1 if problems else 0
    wbytes(TXT, txt)
    cur = current_fragment(guide)
    if cur is None: raise SystemExit("guide.html has no installation-terms markers")
    open(GUIDE, "w", encoding="utf-8", newline="\n").write(guide.replace(cur, frag))
    m = manifest(md, txt, frag)
    json.dump(m, open(MAN, "w", encoding="utf-8", newline="\n"), indent=1)
    print("written: txt", m["copies"]["installer_and_release_txt"]["sha256"][:16], "help", m["copies"]["help_chapter"]["sha256"][:16], "identity", want[:16]); return 0

if __name__ == "__main__": sys.exit(main("--check" in sys.argv[1:]))
