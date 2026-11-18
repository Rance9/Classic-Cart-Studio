# CCS Console Workbench: source and release plan (for approval; nothing here has been created or pushed)

The current Workbench preview is **0.11.0**, matching the app and shared engine. The earlier 1.0.0 preview is superseded and was not a production release. Product tags and feeds remain separate.

> Current development previews use separate app and Workbench releases. The standalone's public branch is `CCS-Console-Workbench`; its ENGINE-PIN.txt identifies the exact app/engine source commit, tree and archive hash. Workflow dispatch can rebuild that pinned source without changing release tags. The templates in branch-root mirror the deployed Workbench workflow. All console/driver boundaries remain closed, and upstream source/redistribution gaps remain disclosed rather than resolved.


## Structure

- **One repository, one engine.** The public branch **`CCS-Console-Workbench`** in the existing repository carries a developer-facing
  `README.md` at its root (draft: `branch-root/README.md`) and the Workbench's own release files. It does not carry a second copy of
  the engine.
- **Pinned engine source.** The Workbench branch records the full commit of `main` whose `tools/console-backup/` (engine, window,
  console programs, `ccsboot`, recovery-image builder) it builds, in `ENGINE-PIN.txt`:
  `commit <40 hex> tree <40 hex> sha256-of-source-archive <64 hex>`. Its build:
  1. checks out exactly that commit (never a floating `main`);
  2. checks `git rev-parse <commit>^{tree}` and the source archive hash;
  3. builds with the pinned .NET SDK and the pinned zig toolchain;
  4. fails on any difference.

  Changing the engine means a new commit on `main`, a reviewed update of `ENGINE-PIN.txt`, and a rebuild of both products.
- **Same bytes in both hosts.** Both the standalone and Classic Cart Studio embed `CcsBackup.dll`, `CcsBackupUi.dll`, `CcsConsole.dll`
  and the recovery images built from that pin. The release check compares their SHA-256 values (`check_shared_engine.py`), and any
  difference fails the release. One commit alone does not prove identical compiler settings; the compared hashes of the built
  artifacts do.
- **Offline reproducibility.** Each Workbench release attaches the source archive of every file of the pinned commit its build reads
  (`ccsboot`'s source and build script and the toolchain pins among them), the PAR2 library source and the LibUsbDotNet source (the commit
  its NuGet package names); the app's release attaches its own source tag, the LibUsbDotNet source and the Hakchi2 CE 3.9.3 program source.
  With the pinned toolchains and the official Hakchi2 CE 3.9.3 package (which the build downloads and checks by SHA-256), these rebuild the
  engine and the console programs. They are not the source of the prebuilt hakchi programs the console programs carry (the kernel and its
  modules, u-boot, fes1, sntool, BusyBox and the others): `SOURCE-AUDIT.md` records what has and has not been identified, and no release
  may claim more (its release gates).
- **Notices.** `NOTICE` (embedded in the Workbench window, both hosts) lists every component the console programs, the engine and the
  standalone's native host carry, with the notices their licenses require and the license texts; `THIRD_PARTY_NOTICES.md` covers the app
  and points to it. Actual licenses are kept as they are.
- **A later split.** Folders and the pin file are arranged so `tools/console-backup/` can move to its own repository with history
  (`git filter-repo --path tools/console-backup`) without a redesign. No split now.

## Releases (GitHub releases are repository-wide; product separation is explicit)

| | Classic Cart Studio | CCS Console Workbench |
|---|---|---|
| Tag | `v<version>` | `workbench-v<version>` |
| Main file | `ClassicCartStudio-Setup-<version>.exe` | `CCS-Console-Workbench-<version>.exe` |
| Version series | the app's | its own, starting at 1.0.0 |
| Feed on the website | `release.json` (product `app`) | `workbench-release.json` (product `workbench`) |
| GitHub "latest" | may be latest | created with **latest turned off** (`gh release create … --latest=false`) |
| Release candidates | draft/prerelease, tag `v<version>-rc<n>` | draft/prerelease, tag `workbench-v<version>-rc<n>` |

- `release_notes.py --write-release-json` and `--write-workbench-json` list releases and take only their own product's newest
  published, non-draft, non-prerelease release. Its tag and file must both match, and a release mixing both products' files is
  refused. The website's download code (`connect.js` `ccsAppRelease`) applies the same rule and never asks for GitHub's latest
  release. `test_release_separation.py` checks all of this offline.
- The app's in-app update and download paths read only `release.json` (product `app`), so a Workbench release can never be offered as
  an app update.
- Release notes are each product's own (a product description, files and hashes, build information, licenses; never a list of
  changes). No dates of future releases appear in source, README or release text.

## What needs the owner's approval before anything is created

1. Creating and pushing the branch `CCS-Console-Workbench` with `README.md`, `ENGINE-PIN.txt` and the release workflow.
2. The release workflow file itself (`.github/workflows/workbench-release.yml`), which builds from the pin, attaches the sources and
   creates a draft release with latest turned off.
3. Each publication: tags, releases, website feeds and the website itself, after the Pages preflight.
