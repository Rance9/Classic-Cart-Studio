# SNES groundwork: identifiers, envelopes and closed boundaries

November 15, 2026. **Groundwork only.** Classic Cart Studio plays NES games. This change adds the names, file fields and checks that later
work needs, and makes sure nothing can switch the reserved parts on. It adds no SNES core, firmware, game file, screen, menu entry or
button. Full Classic Cart Studio SNES gameplay comes after December 1, in its own reviewed release. Backing up or restoring a physical SNES
Classic is a separate capability from SNES gameplay and needs its own hardware and storage-layout proof before it is enabled. A
user-supplied native SNES root on NES hardware is console software (a firmware environment), never a Classic Cart Studio SNES core.

## 1. The registry

`tools/platform/ccs-platform-registry.json` (`ccs-platform-registry/1`) is the one list. Readers:

| Where | Reader | How it gets the file |
|---|---|---|
| Console Backup & Restore engine, Workbench, both workers in C# | `CcsBackup.Compose.Capabilities` | embedded byte for byte (`CcsBackup.csproj`) |
| The Windows app's backend (Python) | `tools/companion/platform_ids.py` | the same text, verbatim (`test_platform_ids.py` compares the bytes) |
| The website and the app's page | `tools/rom-pipeline/studio/platform-ids.js` | put in by `build_html.py` (`_platform_registry`) |

All three decide `tools/platform/target-vectors.json` (37 shared cases) identically. Every entry has a `kind`, an `id` and a `status`:
**supported** (this version may use it; every gate, admission and approval still applies) or **reserved** (named so that files and code can
refer to it; denied everywhere). There is no way to change a status at run time: no setter, no setting, no environment variable, no URL
parameter, no request field, no imported file. The C# and Python readers hand out copies; the page's reader parses the text afresh on every
call and keeps nothing.

| Kind | Supported | Reserved |
|---|---|---|
| `game-system` | `nes` | `snes` |
| `core` | `fceumm` (bundled; GPL-2.0-or-later; pin `ac0d00a7…`; source and notices listed) | `snes-unselected` (not bundled; no license, pin, source or notices) |
| `physical-model` | `nes-classic-edition` (board `dp-nes`) | `snes-classic-edition` (no board listed: not identified on real hardware) |
| `firmware` | `nintendo-nes-classic` (stock, or with hakchi added) | `nintendo-snes-classic` |
| `method` | `ccs-home-autostart` (the current installation) | — (Mod and Nucleus register their own identifiers when they land) |
| `layout` | `nes-classic-nand-512m` (`2048 64 4096`, the known part labels) | `snes-classic-unproven` |
| `input` | `nes-pad`: up, down, left, right, a, b, select, start; 2 players | `snes-pad`: the same plus x, y, l, r; 2 players |
| `boot-option` | `ccs-home`, `native-menu` | `ccs-snes` (`visible: false`) |
| `save-adapter` | `ccs-fceumm-battery`, `nes-classic-stock-battery` (SAVE-FORMATS.md) | `snes-classic-stock-battery` (kept and exported as is) |
| `driver` | `clovershell-usb` (behind `Gate.ConsoleSessions`) | — |
| `capability` | `nes-gameplay`, `nes-classic-backup`, `nes-classic-restore`, `nes-classic-preparation`, `nes-classic-return` (each behind its own `Gate` constant, all closed in this version), `nes-stock-save-conversion` | `snes-gameplay`, `snes-classic-backup`, `snes-classic-restore`, `snes-stock-save-conversion` |

"Supported" never opens a physical path: the console session and write boundaries stay the compiled `Gate` constants.

## 2. The target envelope: `ccs_target` (`ccs-target/1`)

An optional object any of the documents below may carry:

```json
"ccs_target": {"schema": "ccs-target/1", "game_system": "nes", "core": "fceumm", "physical_model": "nes-classic-edition",
               "firmware": "nintendo-nes-classic", "method": "ccs-home-autostart", "layout": "nes-classic-nand-512m"}
```

Every field except `schema` is optional; a missing one takes the document kind's default. Items of a document's lists (a collection's
`selections`, a Game Saves file's `games`) may each carry `game_system`.

The decision, in this order (`ResolveTarget` / `resolve_target` / `ccsTargetResolve`):

1. No `ccs_target` at all: the document kind's NES defaults (migration M1 below). Nothing else is ever assumed to be NES.
2. `ccs_target` not an object → `damaged`; `schema` not exactly `ccs-target/1` (missing, or a newer version) → `target-schema`; any other
   key (for example `capabilities`, or `__proto__`) → `unknown-field`; a field that is not a non-empty string → `damaged`.
3. Each identifier, in the order game system, core, model, firmware, method, layout: not in the registry → `unknown-id`; reserved →
   `reserved`.
4. Consistency: the core's game system, and the firmware's, method's and layout's model must match → `inconsistent`.
5. Each list item's `game_system`: a non-empty string, registered, supported and the document's own system.

Every refusal has one plain message with no SNES wording: "This file is for a game system or console that this version of Classic Cart
Studio does not support, so it was not used. Nothing was changed." (requests: "This request names a game system, console or feature that
this version does not support. Nothing was done.").

## 3. Versioned envelopes and their NES defaults

| Document | Schema (unchanged) | Where `ccs_target` may be | Default target (M1) |
|---|---|---|---|
| Collection | `nes-classic-collection/4` | top level; `selections[].game_system` | `nes`, `fceumm` |
| `.ccs` package | `ccs-package/2` | `ccs-manifest.json` and its `collection.json` / `studio.json` | `nes`, `fceumm` |
| Game Saves file | `ccs-game-saves/1` | `ccs-game-saves.json`; `games[].game_system` | `nes`, `fceumm` |
| Console package | `ccs-console-package/1` | `MANIFEST.json` | `nes`, `fceumm`, `nes-classic-edition`, `ccs-home-autostart` |
| Console backup | `ccs-console-backup/1` | `ccs-console-backup.json` (its `console.board` must also be a supported model's board) | `nes-classic-edition`, `nintendo-nes-classic`, `nes-classic-nand-512m` |
| Stock save conversion | `ccs-save-conversion/1`, `ccs-replaced-save/1` (SAVE-FORMATS.md) | the game identity's `game_system` | `nes` |
| Worker request | the workers' protocols | `ccs_target`, or the fields directly, or `capability`, `boot_option`, `input`, `save_adapter`, `driver`, `provider: {kind, id}` | `nes`, `nes-classic-edition` |
| Provider | `ccs-platform-registry/1` entries | — (the entry itself: kind, id, status; a core adds license, upstream, pin, source, notices, bundled) | — |

The Game Saves file's existing `platform` field (`web-wasm32`, `windows-x86_64`) names the computer a save was made on; it is unrelated to
these identifiers and unchanged.

## 4. Migrations

**M1 (every schema above):** a document without `ccs_target` is its kind's NES default. No schema version changes, and **no writer adds the
field**: every file Classic Cart Studio writes for NES is byte for byte what it wrote before, so every existing file and every new one
loads and round-trips as before. An explicit NES target is accepted as the same thing. Anything else is refused, never read as NES.
Tests: a new console backup carries no `ccs_target` and verifies, and with an explicit NES target re-zipped in, still verifies; a written
`.ccs` package and Game Saves export carry none; packages and Game Saves files with and without an explicit NES target read as before.

## 5. Where the reserved entries are denied

| Boundary | Code | Test |
|---|---|---|
| Page (website and the app's page) | `ccs.js` `ccsInspect` (package manifest and collection), `studio.js` `applyManifest` (collection files), `collections.js` `csImportObject` (collection objects), `saves-transfer.js` `gxRead` (Game Saves files) | `rom-pipeline/test_platform_ids_web.py` (the vectors inside the built page; `ccsInspect` and `gxRead` refusing SNES files, NES files as before), `test_platform_ids.py` (the built `platform-ids.js` in Node.js; every page reader calls the rule; no name clash between page parts) |
| Shared engine, C# | `Archive/Verifier` (backup manifest target and board), `Ext4/CcsExport` (the console's HOME package), `Device/ConsoleFacts` `CheckForBackup` (the board of every recovery session), `Saves/StockSave` (only supported game systems convert) | `CcsBackup.Tests --only platform,saves` |
| Shared engine, Python | `ccs_package.inspect`, `native_home/config.resolve`, `nes_companion.handoff_validate`, `console_engine/ccs_install.build_plan`, `saves_transfer.read` | `test_platform_ids.py` |
| Workers | `Engine/WorkerHost` (backup/restore/preparation worker), `CcsWorker/Protocol/WorkerServer` (transport worker), `console_engine/worker.py` | `--only platform`; CcsWorker `protocol`; `test_platform_ids.py` |
| RAM (recovery sessions) | The PC refuses any board that is not a supported model's before the first read or write of every session (`CheckForBackup`, used by backup, restore, preparation and return). The recovery programs (`ram/ccsbr`, `ccsprep`, `ccs-init`, `ccs-idle`) take only fixed verbs and fixed targets: there is no input through which a system, provider or capability could be named. No recovery image was rebuilt for this change. | `test_platform_ids.py` (the programs name no system, provider or capability; the fixed target list; the board check asks the registry) |

## 6. Controllers

`nes-pad` is exactly the eight actions the players already use, in their order (`ccs-play.js` `CP_ACT`, `play.js` `PLAY_ACTIONS`); no
mapping, key or controller default changes. `snes-pad` (reserved) defines x, y, l and r in addition; nothing reads it yet.

## 7. Licensing and build interface

A core entry names its license, upstream, pinned commit, vendored source folder and notice files, and whether it is bundled. Tests require:
only the supported core is bundled; its pin is the commit in `tools/player/third_party/fceumm/VENDORED.md`; its notice files exist;
`tools/player/third_party` holds only the registered core and its two non-core companions (a new folder needs a registry entry and review
first); the reserved core carries no license, pin or source; no SNES core, firmware or game file is in the source tree.

## 8. What is not done, and what needs hardware

- No SNES core is chosen, licensed, built or bundled; nothing plays SNES games.
- Physical SNES Classic backup or restore: needs a real SNES Classic (its board identity, storage layout, kernel list and a proved layout)
  and its own reviewed release. Until then any board other than `dp-nes` is refused before anything is read.
- The installation methods being added elsewhere (Mod, Nucleus) register their own `method` identifiers when they land; until then the
  registry refuses any other method.
- No screen lists boot options or capabilities from the registry yet; when one does, it shows only supported entries.
