r"""A collection's carried HOME design, compiled for a HOME package by the ONE compiler.

A collection carries its design as the .ccshome file the Workbench's HOME Designer wrote (design_embed.py). Before the package builder
puts it into a package, it is compiled by CcsBackup.Compose.DesignCompiler, the C# compiler both Windows hosts use: this module runs
that program's --design-compile (ClassicCartStudio.exe, the program that started this backend and hosts the Workbench window; its path
comes in CCS_DESIGN_COMPILER, or it is the program installed beside this one). Python never compiles, edits or re-writes a design: the
compiled folder is used exactly as the compiler wrote it (design_ingest.py checks it). A design that cannot be compiled is never left
out silently: the build stops with the reason in words for the person.
Copyright (C) 2026 Rance9, GPL-3.0-or-later."""
from __future__ import annotations
import json, os, re, subprocess, sys, tempfile

RESULT_SCHEMA = "ccs-home-design-compile-result/1"; TIMEOUT = 600
CATALOG = re.compile(r"^[0-9a-f]{16}$")

class CompileRefused(ValueError):
    """the design cannot be used on the console: str(e) says why and what to do, in words for the person"""

def compiler() -> str | None:
    """the program that answers --design-compile: CCS_DESIGN_COMPILER (set by Classic Cart Studio for Windows for the backend it starts,
    and by the tests), else the one installed beside this frozen backend; None when there is none"""
    p = os.environ.get("CCS_DESIGN_COMPILER")
    if p and os.path.isfile(p): return p
    if getattr(sys, "frozen", False):
        p = os.path.join(os.path.dirname(os.path.abspath(sys.executable)), "ClassicCartStudio.exe")
        if os.path.isfile(p): return p
    return None

def _what(ref: dict) -> str:
    n = str((ref or {}).get("name") or "").strip()[:60]
    return f'The HOME design "{n}"' if n else "This collection's HOME design"

def compile_carried(embed: dict, games: list, out: str, placeholders: dict | None = None) -> dict:
    """compiles the carried design (home.design, with its bytes) for these HOME games (dicts with id, title) into out (a new folder).
    Returns the compiler's result (id, schema_name, theme, name, manifest, left_out, not_shown). Raises CompileRefused when the design
    cannot be used, with the reason and what to do."""
    from design_embed import check
    ccshome, why = check(embed)
    what = _what(embed)
    if why: raise CompileRefused(f"{what} cannot be used: {why}. Open the collection's HOME Settings and add the design again, or remove it.")
    exe = compiler()
    if not exe: raise CompileRefused(f"{what} cannot be prepared here: the HOME Designer compiler that comes with Classic Cart Studio for Windows is not available. Use Classic Cart Studio for Windows, or remove the design from this collection's HOME Settings.")
    if os.path.exists(out): raise ValueError(f"{out} exists; a compiled design is always written into a new folder")
    tmp = tempfile.mkdtemp(prefix="ccs-design-compile-")
    try:
        dp, gp, rp = os.path.join(tmp, "design.ccshome"), os.path.join(tmp, "games.json"), os.path.join(tmp, "result.json")
        with open(dp, "wb") as f: f.write(ccshome)
        gl = [{"id": str(g.get("id") or ""), "title": str(g.get("title") or "")} for g in games if g.get("id")]
        for g, src in zip(gl, [x for x in games if x.get("id")]):
            c = src.get("catalog")
            if isinstance(c, str) and CATALOG.match(c): g["catalog"] = c
        with open(gp, "w", encoding="utf-8") as f: json.dump({"games": gl}, f, ensure_ascii=False)
        args = [exe, "--design-compile", "--design", dp, "--games", gp, "--out", out, "--result", rp]
        if placeholders:
            mp = os.path.join(tmp, "placeholders.json")
            with open(mp, "w", encoding="utf-8") as f: json.dump({str(int(k)): str(v) for k, v in placeholders.items()}, f)
            args += ["--placeholders", mp]
        try: p = subprocess.run(args, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=TIMEOUT, creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
        except (OSError, subprocess.TimeoutExpired) as e: raise CompileRefused(f"{what} could not be prepared ({e.__class__.__name__}). Try again; your collection is unchanged.")
        try: r = json.load(open(rp, encoding="utf-8"))
        except (OSError, ValueError): r = None
        if not isinstance(r, dict) or r.get("schema") != RESULT_SCHEMA:
            raise CompileRefused(f"{what} could not be prepared: the compiler stopped without a result (status {p.returncode}). Try again; your collection is unchanged.")
        if p.returncode != 0 or not r.get("ok"):
            err = str(r.get("error") or "it could not be compiled").strip().rstrip(".")
            if r.get("code") == "design.inspect-only":
                raise CompileRefused(f"{what} cannot be used on the console: {err}. Update Classic Cart Studio for Windows to a version that knows it, or remove the design from this collection's HOME Settings; nothing was prepared.")
            if r.get("code") == "design.not-applicable":
                raise CompileRefused(f"{what} cannot be used on the console: {err}. Open it in HOME Designer (CCS Console Workbench, in Classic Cart Studio for Windows) to change it, or remove it from this collection's HOME Settings; nothing was prepared.")
            raise CompileRefused(f"{what} cannot be used: {err}. Nothing was prepared.")
        return r
    finally:
        import shutil; shutil.rmtree(tmp, ignore_errors=True)
