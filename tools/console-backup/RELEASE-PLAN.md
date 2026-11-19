# CCS Console Workbench: source and release plan (for approval; nothing here has been created or pushed)

The current Workbench preview is **0.11.4**, matching the app and shared engine; Classic Cart Studio 0.11.4 installs this same program. The earlier 0.11.3, 0.11.2, 0.11.1, 0.11.0 and 1.0.0 previews are superseded (1.0.0 was not a production release). Product tags and feeds remain separate.

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
- **One program, the same bytes.** Since 0.11.4 Classic Cart Studio's installer carries the standalone release's single-file
  `CCS-Console-Workbench.exe` itself (x64 and Arm64; Setup installs the one for the PC) and Studio starts it as its own program. Both
  workflows build the managed program with `build_workbench_payload.ps1` and the hosts with `build_host.sh` from the same commit; the
  app's release check (`check_shared_engine.py`) proves the bundled copy is byte for byte the host it built and reports its
  `--engine-info`, and publication compares the released standalone files with the bundle's recorded hashes (`build-info.json`
  `bundle.workbench.assets`). One commit alone does not prove identical compiler settings; the compared hashes of the built artifacts do.
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
| Version series | the app's (`VERSION`) | its own (`tools/console-backup/WORKBENCH-VERSION`); plain N.N.N, no preview or rc suffix |
| Channel | `CHANNEL` (`preview` or `production`), in `build-info.json` | `WORKBENCH-CHANNEL`, in `build-record.json` |
| Update stream on the website | `release-streams.json`: `app/production`, `app/preview` | `release-streams.json`: `workbench/production`, `workbench/preview` |
| Earlier feeds (kept for earlier releases) | `release.json`: the app's newest preview | `workbench-release.json`: the Workbench's newest preview |
| GitHub release | a preview is a prerelease with latest turned off; a production release is latest | always latest turned off (`gh release create … --latest=false`); a preview is a prerelease |

- `release_notes.py --write-streams-json` (with `--write-release-json` and `--write-workbench-json`) reads the releases once and writes
  the four streams (`release_streams.py`): published, non-draft releases whose tag and main file both name one product with a plain
  version, never a release mixing both products' files and never an excluded tag (the superseded `workbench-v1.0.0`). The channel is the
  release's own metadata (`channel` in `build-info.json` or `build-record.json`), agreeing with GitHub's prerelease mark; a release whose
  metadata names no channel (up to 0.11.3) is a preview when GitHub marks it a prerelease. Each stream is newest first by numeric version.
  The website's download code (`connect.js` `ccsAppRelease`) never asks for GitHub's latest release. `test_release_separation.py` and
  `test_release_streams.py` check this offline.
- Update checks read only their own product's stream of their own channel: the app (`app_update.py`) its app stream, compared with
  both installed parts (the app and its bundled Workbench); the standalone Workbench (`WorkbenchUpdates.cs`) its Workbench stream; the
  Workbench started by the app the app's stream (one Classic Cart Studio update). A Workbench release is never offered as an app
  update. An unsigned preview is offered through the download page (https://classiccartstudio.com/app/), never installed by the app.
- Release notes are each product's own (a product description, files and hashes, build information, licenses; never a list of
  changes). No dates of future releases appear in source, README or release text.

## What needs the owner's approval before anything is created

1. Creating and pushing the branch `CCS-Console-Workbench` with `README.md`, `ENGINE-PIN.txt` and the release workflow.
2. The release workflow file itself (`.github/workflows/workbench-release.yml`), which builds from the pin, attaches the sources and
   creates a draft release with latest turned off.
3. Each publication: tags, releases, website feeds and the website itself, after the Pages preflight.
