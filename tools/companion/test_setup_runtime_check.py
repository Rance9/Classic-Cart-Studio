r"""Setup's WebView2 Runtime check starts from Setup's own few files (installer.iss). The published 0.11.0 to 0.11.3 installers failed
this way in a clean Windows setup: the program's entry point named another assembly, which Setup's temporary folder does not hold, so
the check never ran.

  python test_setup_runtime_check.py <dist folder: ...\ClassicCartStudioBackend> [--out <result.json>]      Exit 0 = PASS.

Before anything is installed, Setup extracts a few files of the program folder into its temporary folder and runs
ClassicCartStudio.exe --runtime-check there (installer.iss: the [Files] entries from {#SourceDir} marked dontcopy, each extracted with
ExtractTemporaryFile). This check reads that list from installer.iss, copies exactly those files, and nothing else, from the release
folder into a new empty folder, and runs the program there the way Setup does (that folder as its working folder), with the check's own
simulated absence (CCS_RUNTIME_CHECK_SIMULATE=absent): nothing is looked for, installed or started, so the expected result is always the
same, exit 3 ("none found") with the check's JSON saying simulated. A program whose start needs any other file of the installation (an
assembly its entry point names, for example) never gets that far and fails this check. It changes nothing on the PC: the folder it makes
is removed again."""
from __future__ import annotations
import hashlib, json, os, re, shutil, subprocess, sys, tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
ISS = os.path.join(HERE, "installer.iss")
EXPECTED_EXIT = 3   # Runtime.cs: 3 = none found (here: the simulated absence)


def setup_files(iss_text: str) -> tuple[list[str], str]:
    """the program folder's files Setup extracts for its runtime check, as installer.iss names them, and the program's name"""
    defines = dict(re.findall(r'^#define\s+(\w+)\s+"([^"]*)"', iss_text, re.M))
    def expand(s: str) -> str:
        return re.sub(r"\{#(\w+)\}", lambda m: defines[m.group(1)] if m.group(1) in defines else m.group(0), s)
    staged = [expand(m) for m in re.findall(r'^Source:\s*"\{#SourceDir\}\\([^"]+)";\s*Flags:\s*dontcopy\s*$', iss_text, re.M)]
    extracted = {expand(m) for m in re.findall(r"ExtractTemporaryFile\('([^']+)'\)", iss_text)}
    app = defines.get("AppExe")
    if not staged or app not in staged:
        raise SystemExit("FAIL installer.iss: no program-folder files marked dontcopy, or the program is not among them: %r" % staged)
    for n in staged:
        if any(c in n for c in "*?\\/") or "{" in n:
            raise SystemExit("FAIL installer.iss: not a plain file name: %r" % n)
        if n not in extracted:
            raise SystemExit("FAIL installer.iss: %s is carried for the temporary folder but never extracted" % n)
    if not re.search(r"Exec\(ExpandConstant\('\{tmp\}\\\{#AppExe\}'\),\s*'--runtime-check ", iss_text) or "ExpandConstant('{tmp}'), SW_HIDE" not in iss_text:
        raise SystemExit("FAIL installer.iss: Setup no longer runs {tmp}\\{#AppExe} --runtime-check from {tmp}; this check must follow it")
    return staged, app


def sha256(p: str) -> str:
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for b in iter(lambda: f.read(1 << 20), b""):
            h.update(b)
    return h.hexdigest()


def main() -> int:
    args = sys.argv[1:]
    out_path = None
    if "--out" in args:
        i = args.index("--out"); out_path = args[i + 1]; del args[i:i + 2]
    if len(args) != 1:
        print(__doc__); return 2
    dist = os.path.abspath(args[0])
    names, app = setup_files(open(ISS, encoding="utf-8-sig").read())
    result = {"schema": "ccs-setup-runtime-check-guard/1", "ok": False, "installer": "tools/companion/installer.iss",
              "staged": [], "expected_exit": EXPECTED_EXIT, "simulation": "CCS_RUNTIME_CHECK_SIMULATE=absent"}
    stage = tempfile.mkdtemp(prefix="ccs-setup-tmp-")
    try:
        for n in names:
            src = os.path.join(dist, n)
            if not os.path.isfile(src):
                raise SystemExit("FAIL the release folder has no %s (Setup carries it for its check)" % n)
            shutil.copyfile(src, os.path.join(stage, n))
            result["staged"].append({"name": n, "bytes": os.path.getsize(src), "sha256": sha256(src)})
        present = sorted(os.listdir(stage))
        if present != sorted(names):
            raise SystemExit("FAIL the staged folder holds other files: %r" % present)
        env = dict(os.environ)
        for k in ("CCS_RUNTIME_CHECK_MARKER", "CCS_RUNTIME_CHECK_SKIP_DISCOVERY"):
            env.pop(k, None)
        env["CCS_RUNTIME_CHECK_SIMULATE"] = "absent"
        report = os.path.join(stage, "ccs-runtime-guard.json")
        p = subprocess.run([os.path.join(stage, app), "--runtime-check", report],
                           cwd=stage, env=env, capture_output=True, timeout=180)
        code = p.returncode - (1 << 32) if p.returncode >= (1 << 31) else p.returncode
        result["exit"] = code
        check = None
        if os.path.isfile(report):
            try:
                check = json.loads(open(report, encoding="utf-8").read())
            except ValueError:
                check = {"unreadable": True}
        result["check"] = None if check is None else {k: check.get(k) for k in ("schema", "code", "kind", "simulated", "detail", "error")}
        problems = []
        if code != EXPECTED_EXIT:
            problems.append("exit %d, not %d%s" % (code, EXPECTED_EXIT, " (0xE0434352: .NET stopped the program before the check, for example an assembly the start names is missing)" if code == -532462766 else ""))
        if check is None:
            problems.append("the check wrote no result: the program did not get to it")
        elif not (check.get("schema") == "ccs-runtime-check/1" and check.get("code") == EXPECTED_EXIT and check.get("simulated") is True and check.get("kind") == "none"):
            problems.append("the check's result is not the simulated none found: %r" % result["check"])
        result["ok"] = not problems
        result["problems"] = problems
    finally:
        shutil.rmtree(stage, ignore_errors=True)
    text = json.dumps(result, indent=1)
    if out_path:
        os.makedirs(os.path.dirname(os.path.abspath(out_path)), exist_ok=True)
        open(out_path, "w", encoding="utf-8", newline="\n").write(text + "\n")
    print(text)
    if result["ok"]:
        print("PASS Setup's WebView2 Runtime check starts from Setup's own %d files (%s) and reports the simulated absence (exit %d)" % (len(names), ", ".join(names), EXPECTED_EXIT))
        return 0
    print("FAIL Setup's WebView2 Runtime check from Setup's own files: " + "; ".join(result["problems"]))
    return 1


if __name__ == "__main__":
    sys.exit(main())
