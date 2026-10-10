"""Commit the allow-listed public tree (publish/repo) and push it to the public GitHub repository.

Safety: refuses unless scan_prohibited.py passes on the tree AND on the full git history of publish/repo; refuses when the
authenticated GitHub login is not the expected account; never prints tokens (gh keeps them in the OS keyring); never touches the
private development repository's history (publish/repo has its own .git).

  python publish_repo.py --account Rance9 --repo NES-Classic-Collection-Studio --message "..." [--create] [--push]
"""
from __future__ import annotations
import os, sys, json, subprocess, argparse
HERE = os.path.dirname(os.path.abspath(__file__)); PROJECT = os.path.normpath(os.path.join(HERE, "..", ".."))
TREE = os.environ.get("NES_PUBLIC_TREE") or os.path.join(PROJECT, "publish", "repo")   # the allow-listed tree built by build_public_repo.py (--repo)

def run(args, cwd=None, check=True, capture=True):
    r = subprocess.run(args, cwd=cwd or TREE, capture_output=capture, text=True, encoding="utf-8", errors="replace")
    if check and r.returncode != 0: raise SystemExit(f"command failed: {' '.join(args)}\n{r.stdout}\n{r.stderr}")
    return r

def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--account", required=True); ap.add_argument("--repo", required=True); ap.add_argument("--message", required=True)
    ap.add_argument("--tree", default=TREE); ap.add_argument("--create", action="store_true"); ap.add_argument("--push", action="store_true"); ap.add_argument("--description", default="An independent game collection manager for the Nintendo NES Classic Edition."); a = ap.parse_args()
    global TREE; TREE = os.path.abspath(a.tree)
    if not os.path.isdir(TREE) or not os.path.exists(os.path.join(TREE, "README.md")): raise SystemExit(f"public tree not found at {TREE}: run build_public_repo.py first")
    login = run(["gh", "api", "user", "--jq", ".login"], cwd=PROJECT).stdout.strip()
    if login != a.account: raise SystemExit(f"gh is authenticated as '{login}', expected '{a.account}': refusing")
    if not os.path.isdir(os.path.join(TREE, ".git")):
        run(["git", "init", "-b", "main"]); run(["git", "config", "user.name", a.account]); run(["git", "config", "user.email", f"{a.account}@users.noreply.github.com"])
    scan = run([sys.executable, os.path.join(HERE, "scan_prohibited.py"), TREE, "--allow", "site-data/brand/fonts/*"], check=False); print(scan.stdout[-800:])
    if scan.returncode != 0: raise SystemExit("prohibited content in the public tree: refusing to commit")
    run(["git", "add", "-A"]); st = run(["git", "status", "--porcelain"]).stdout
    if st.strip(): run(["git", "commit", "-m", a.message + "\n\nCo-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"]); print("committed")
    else: print("nothing to commit")
    hist = run([sys.executable, os.path.join(HERE, "scan_prohibited.py"), TREE, "--git-history", "--allow", "site-data/brand/fonts/*"], check=False); print(hist.stdout[-600:])
    if hist.returncode != 0: raise SystemExit("prohibited content in the public git history: refusing to push")
    full = f"{a.account}/{a.repo}"
    if a.create:
        exists = run(["gh", "repo", "view", full, "--json", "name"], cwd=PROJECT, check=False).returncode == 0
        if not exists:
            run(["gh", "repo", "create", full, "--public", "--description", a.description, "--disable-wiki"], cwd=PROJECT); print("created", full)
        else: print("repository exists", full)
        if run(["git", "remote", "get-url", "origin"], check=False).returncode != 0: run(["git", "remote", "add", "origin", f"https://github.com/{full}.git"])
    if a.push:
        remote = run(["git", "remote", "get-url", "origin"]).stdout.strip()
        if f"github.com/{full}" not in remote: raise SystemExit(f"origin is {remote}, not {full}: refusing to push")
        run(["git", "push", "-u", "origin", "main"], capture=False); print("pushed to", full)

if __name__ == "__main__":
    main()
