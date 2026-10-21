r"""Sizes shown to people are decimal (1 KB = 1,000 B, 1 MB = 1,000,000 B, 1 GB = 10^9 B, 1 TB = 10^12 B), computed from exact byte counts,
and no binary unit (KiB, MiB, GiB, TiB) reaches a user-facing surface. No browser; Node.js runs the page's own fmt().
  formatter   the page's fmt() (website and Windows app) and the backend's ccs_package.fmt_size() give the same, expected text for known
              byte counts (999 B ... 4,697,642,234 B), never "0" for a non-zero size, never "1,000 KB"
  install     the Install on NES Classic size display (install.js fmtB) uses the same decimal formatter
  storage     the NES Classic capacity estimate converts the documented MiB figure to decimal MB (300 MiB = 314.6 MB)
  leakage     no KiB/MiB/GiB/TiB in the built page (all of its markup, text and scripts), the Help guide, the desktop host's strings, the
              backend's user-facing strings (Python string literals, not comments) or the public documents
  python test_size_units.py [<built page or site folder>]      Exit 0 = PASS."""
from __future__ import annotations
import ast, json, os, re, subprocess, sys
HERE = os.path.dirname(os.path.abspath(__file__)); TOOLS = os.path.dirname(HERE); ROOT = os.path.dirname(TOOLS)
sys.path[:0] = [os.path.join(TOOLS, "companion"), HERE]
import ccs_package as cp
BIN = re.compile(r"\b[KMGT]iB\b")
EXPECT = {0: "0 B", 1: "1 B", 999: "999 B", 1000: "1 KB", 1500: "1.5 KB", 999_999: "1 MB", 1_000_000: "1 MB", 1_048_576: "1.05 MB", 16_400: "16.4 KB",
          100_000_000: "100 MB", 806_547_577: "806.55 MB", 999_999_999: "1 GB", 1_000_000_000: "1 GB", 4_697_642_234: "4.7 GB", 10**12: "1 TB", 1_073_741_824: "1.07 GB"}

def page_fmt_source() -> str:
    src = open(os.path.join(HERE, "build_html.py"), encoding="utf-8").read()
    m = re.search(r"const FMT_U=.*?\nfunction fmt\(n\)\{.*?\}\n", src, re.S); assert m, "fmt() not found in build_html.py"; return m.group(0)

def main():
    checks = []
    def ok(k, v, n=""): checks.append((k, bool(v))); print(f"{k}={'PASS' if v else 'FAIL'}" + (f" ({str(n)[:400]})" if n and not v else ""), flush=True)
    site = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "reports", "nes-catalog.html")
    page = site if site.endswith(".html") else os.path.join(site, "nes-catalog.html")
    # ---- the formatter: page (Node) and backend (Python) ----
    js = page_fmt_source() + "\nconsole.log(JSON.stringify(%s.map(fmt)));" % json.dumps(list(EXPECT))
    r = subprocess.run(["node", "-e", js], capture_output=True, text=True); page_out = json.loads(r.stdout or "[]")
    py_out = [cp.fmt_size(n) for n in EXPECT]
    ok("page_fmt_decimal_values", page_out == list(EXPECT.values()), [(n, a) for n, a, e in zip(EXPECT, page_out, EXPECT.values()) if a != e] or r.stderr[:200])
    ok("backend_fmt_size_decimal_values", py_out == list(EXPECT.values()), [(n, a) for n, a, e in zip(EXPECT, py_out, EXPECT.values()) if a != e])
    ok("page_and_backend_agree", page_out == py_out)
    many = [int(1.37 ** k) for k in range(1, 90)] + [10**k - 1 for k in range(1, 15)] + [10**k for k in range(1, 15)]
    r = subprocess.run(["node", "-e", page_fmt_source() + "\nconsole.log(JSON.stringify(%s.map(fmt)));" % json.dumps(many)], capture_output=True, text=True); pm = json.loads(r.stdout)
    bad = [(n, s) for n, s in zip(many, pm) if re.match(r"^1,000(\.0+)? (KB|MB|GB)$", s) or (n > 0 and re.match(r"^0(\.0+)? ", s)) or BIN.search(s)]
    ok("no_zero_no_1000_of_a_unit_no_binary", not bad, bad[:5])
    ok("page_and_backend_agree_on_400_sizes", pm == [cp.fmt_size(n) for n in many], [(n, a, cp.fmt_size(n)) for n, a in zip(many, pm) if a != cp.fmt_size(n)][:5])
    # ---- install sizes and the storage estimate ----
    inst = open(os.path.join(HERE, "studio", "install.js"), encoding="utf-8").read()
    ok("install_sizes_use_the_decimal_formatter", re.search(r"function fmtB\(n\)\{ return n==null\?'unknown':fmt\(n\); \}", inst) and "/=1024" not in inst)
    b = open(os.path.join(HERE, "build_html.py"), encoding="utf-8").read()
    m = re.search(r"const mibToMb=x=>Number\(x\)\*1048576/1e6;", b)
    r = subprocess.run(["node", "-e", "const mibToMb=x=>Number(x)*1048576/1e6; const fmtMb=x=>mibToMb(x).toLocaleString('en-US',{maximumFractionDigits:1})+' MB'; console.log(fmtMb(300))"], capture_output=True, text=True)
    ok("storage_estimate_converts_mib_to_decimal_mb", m and r.stdout.strip() == "314.6 MB", r.stdout)
    # ---- leakage ----
    leaks = []
    def scan(label, text):
        for mm in BIN.finditer(text): leaks.append(f"{label}: ...{text[max(0, mm.start() - 50):mm.end() + 10]!r}")
    scan(os.path.basename(page), open(page, encoding="utf-8", errors="replace").read())
    scan("guide.html", open(os.path.join(HERE, "studio", "guide.html"), encoding="utf-8").read())
    for d, exts in ((os.path.join(TOOLS, "desktop", "ClassicCartStudio"), (".cs", ".html", ".xaml")), (os.path.join(TOOLS, "publish", "repo-files"), (".md",)), (os.path.join(TOOLS, "publish", "site-static"), (".html", ".json", ".txt"))):
        if not os.path.isdir(d): continue
        for dp, _, fs in os.walk(d):
            if os.sep + "bin" in dp or os.sep + "obj" in dp: continue
            for f in fs:
                if f.endswith(exts):
                    t = open(os.path.join(dp, f), encoding="utf-8", errors="replace").read()
                    if f.endswith(".cs"): t = "\n".join(re.findall(r'"(?:[^"\\]|\\.)*"', t))   # the host's strings, not its comments
                    scan(os.path.relpath(os.path.join(dp, f), TOOLS), t)
    for f in sorted(os.listdir(os.path.join(TOOLS, "companion"))):   # backend: string literals (messages people see), not comments
        if not f.endswith(".py") or f.startswith(("test_", "check_", "shot_", "dbg_", "e2e_")): continue
        tree = ast.parse(open(os.path.join(TOOLS, "companion", f), encoding="utf-8").read())
        docs = {id(n.body[0].value) for n in ast.walk(tree) if isinstance(n, (ast.Module, ast.FunctionDef, ast.ClassDef, ast.AsyncFunctionDef)) and n.body and isinstance(n.body[0], ast.Expr) and isinstance(getattr(n.body[0], "value", None), ast.Constant)}
        for n in ast.walk(tree):
            if isinstance(n, ast.Constant) and isinstance(n.value, str) and id(n) not in docs: scan("companion/" + f, n.value)
    ok("no_binary_units_on_user_facing_surfaces", not leaks, leaks[:6])
    bad = [k for k, v in checks if not v]; print("RESULT:", "PASS" if not bad else "FAIL " + str(bad), f"({len(checks)} checks)"); return 0 if not bad else 1

if __name__ == "__main__": sys.exit(main())
