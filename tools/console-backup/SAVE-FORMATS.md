# Battery saves: the NES Classic's own format and Classic Cart Studio's

November 15, 2026. **No real NES Classic save has been examined.** Everything below marked *believed* is confirmed or corrected by the
owner's own save in hardware run W02 (section 9). Until then the converter refuses every real conversion except a clearly labeled
comparison copy, and nothing it makes is imported into saves or written to a console.

Code: `CcsBackup/Saves` (StockSave, BatteryTable, SaveImport). It is the one implementation: the Backup Browser's battery-save export and
conversion and Expert's typed save drop both call it; no other part of Classic Cart Studio converts stock saves. Tests: `CcsBackup.Tests
--only saves` (synthetic vectors only).

**Independent implementation.** The converter was written from the documented format facts listed here. The ClassicEditionTools converter
scripts carry no license grant; their code was not read, copied or adapted. Only the published description of what that converter does
(as summarized in the project's private save-format research brief of November 14, 2026) is used as a source, and it is labeled *believed*.

Labels: **Verified** — read in pinned source code, or measured on Classic Cart Studio's own files. **Believed** — from documentation or
another project's described behavior, not checked against a real console file. **Unknown** — not known; the code refuses rather than guess.

## 1. Where the console keeps its saves

| Fact | Status | Source |
|---|---|---|
| Each game's saves are in its profile folder `/var/lib/clover/profiles/0/<game code>/` (The Legend of Zelda, North America: `CLV-P-NAANE`) | Verified | Hakchi2 CE 3.9.3 `data/desktop_entries/nes-usa/CLV-P-NAANE.desktop`, `Path=/var/lib/clover/profiles/0//CLV-P-NAANE` ([source](https://github.com/TeamShinkansen/Hakchi2-CE/blob/v3.9.3/data/desktop_entries/nes-usa/CLV-P-NAANE.desktop)); `SaveStateManager.cs` archives that folder ([source](https://github.com/TeamShinkansen/Hakchi2-CE/blob/v3.9.3/SaveStateManager.cs)) |
| In a console backup the same folder is `/clover/profiles/0/<game code>/` inside the data storage image (the console mounts that storage at `/var/lib`) | Believed (layout); the test fixtures that use it are synthetic | `tests/make_fixtures.py`; the engine's own data-storage inventory |
| Hakchi2 CE 3.9.3 looks for a battery save as `save.sram`, or `cartridge.sram` when that file is larger than 20 bytes | Verified (what Hakchi checks) | `SaveStateManager.cs` `LoadSaveStatesList` |
| The NES Classic writes its battery save as `save.sram` | Believed | research brief |
| Which console software writes `cartridge.sram`, and what a 20-byte-or-smaller one holds | Unknown | — |
| Suspend points are `suspendpoint1`–`suspendpoint4` folders and `1.state`–`4.state` files beside the battery save | Verified (what Hakchi checks) | `SaveStateManager.cs` |
| `/var/saves` is the path Hakchi's save manager uses to reach these folders | Verified (Hakchi's path); its relation to every installation's physical place is not assumed | `SaveStateManager.cs` |
| The Zelda entry says `SaveCount=3` | Verified (the entry's text); its meaning is not used | desktop entry above |

A suspend point is never a battery save, and a native suspend point is never claimed to equal a Classic Cart Studio saved state. Suspend
points are kept and exported exactly as they are; nothing converts them.

## 2. The stock battery-save wrapper

Identifier `nes-classic-stock-battery` (the registry's save format, `tools/platform/ccs-platform-registry.json`), version 1, described in
code by `StockWrapper.Describe()` (`ccs-stock-save-wrapper/1`).

| Offset | Bytes | Field | Status |
|---|---|---|---|
| 0 | 40 | Check value: the SHA-1 of the payload (every byte after this field), written as 40 ASCII hexadecimal characters | Believed (research brief's description of the published converter: 40 hexadecimal SHA-1 characters checked against the entire payload; not a 20-byte binary digest) |
| 40 | the rest of the file | Payload: the game's battery memory, then zero bytes up to the payload length | Believed (the brief: the reverse converter zero-pads, then prefixes the SHA-1 text) |
| — | — | Letter case of the check value on a real console | **Unknown.** Reading accepts all lowercase or all uppercase and records which; mixed case is not a check value. Writing uses only a case confirmed for that game, or the case of that console's own file for the same game (a comparison copy) |
| — | — | Payload length for each game, and whether it ever differs from the battery size | **Unknown.** Never assumed (no 32 KiB or other universal length) |
| — | — | Anything after the payload (a newline, a terminator) | **Unknown**; none is expected. Such a file fails the check value and is refused, never trimmed |
| — | — | Byte order inside the payload | Not applicable: the payload is the game's own memory, copied as bytes; nothing is swapped or reinterpreted |

## 3. What `StockSave.Inspect` decides

`Inspect(bytes, source)` only reads; it never changes its input and never guesses. It reports the source (name, path, game code from the
path, date, origin), the file's size and SHA-256, the check value and its case, the payload length and where the save data ends (the last
non-zero byte).

| Kind | Code | When |
|---|---|---|
| `stock-battery` | `ok` | 40 hexadecimal characters in one case, and the SHA-1 of the rest equals them |
| `suspend-point` | `suspend-point` | the path has a `suspendpointN` folder, or the file is `N.state` / `.ccsstate` (decided by where it was found, before its bytes) |
| `ccs-state` | `ccs-state` | the bytes start with `CCSSTATE` (Classic Cart Studio's saved state, `tools/player/core/gp_save.c`) |
| `empty` | `empty` | no bytes |
| `not-recognized` | `too-short` / `no-check-value` | 40 bytes or fewer; or the first 40 bytes are not hexadecimal in one case |
| `damaged` | `check-value-mismatch` | the check value does not match the payload |

## 4. Conversion (`ConvertToRaw`, `ConvertToStock`)

Both directions need: a supported game system (NES; a save of the reserved SNES system is refused, `unsupported-system`), the exact game
file (`GameIdentity.ContentSha256`, the SHA-256 of the game file after its 16-byte iNES header, as Classic Cart Studio identifies games:
`tools/companion/CCS-FORMAT.md`; a title or code alone is `no-game-file`), and a battery-size entry for that game (`no-battery-size`
otherwise). A save offered for another game's code, found in another game's folder or with another game file is `wrong-game`.

**Stock → raw (cart.sav).** The input must inspect as `stock-battery` (a suspend point or saved state is refused first). The payload must be
at least the battery size (`short`: nothing is added), must equal the console's payload length when that is recorded
(`unexpected-length`), and every byte after the battery size must be zero (`nonzero-tail`: nothing is removed). The output is exactly the
first battery-size bytes of the payload.

**Raw → stock.** The input must be exactly the battery size (`wrong-size`: nothing is cut off or added, not even a zero byte) and must not be
a saved state (`ccs-state`). The payload length and letter case come from a confirmed entry; for an entry not confirmed yet, only from that
console's own file for the same game (`template`; another game's file is `template-other-game`, a damaged one `template-not-recognized`),
and otherwise the conversion is refused (`not-confirmed`) — never made after a warning. The result is read back with the reverse
conversion and must give the same battery save (`self-check`).

Every input is left unchanged (outputs are new arrays; the tests compare SHA-256 before and after). Every conversion returns a report.

| Entry status | Result | May be exported | May be imported into saves | May be written to a console |
|---|---|---|---|---|
| `confirmed` (exact game file recorded, both directions confirmed by play) | `Confirmed` | yes | yes (SaveImport rules) | only with that write's own approval (`ForConsole`) |
| `needs-real-console-confirmation` | `Trial`: a comparison copy | yes, labeled | **no** (`trial`) | **no** |
| `synthetic-test` (tests only, never in the table) | `Synthetic` | — | in tests | **no** |

## 5. Battery sizes (`BatteryTable`)

A battery size is the number of bytes the pinned core keeps as the game's battery memory **for that exact game file**:

- FCEUmm `ac0d00a7` (`tools/player/third_party/fceumm`): `drivers/libretro/libretro.c` `retro_get_memory_size(RETRO_MEMORY_SAVE_RAM)`
  returns the cartridge's `SaveGameLen[0]` — **verified**. The length depends on the file's header and mapper: for mapper 1,
  `boards/mmc1.c` `Mapper1_Init`/`DetectMMC1WRAMSize` give 8 KiB for an iNES 1.0 file with the battery flag, while an NES 2.0 header's
  PRG-NVRAM field decides instead — **verified**. So there is no general size, and none is guessed.
- Classic Cart Studio's player loads `cart.sav` only when its size equals that number (`tools/player/core/gp_save.c` `gp_sram_load`) —
  **verified**.

| Entry | Game code | Exact game file | Battery bytes | Console payload length | Case | Status | Basis |
|---|---|---|---|---|---|---|---|
| `clv-p-naane` | `CLV-P-NAANE` (folder verified) | not recorded | 8,192 (believed) | unknown | unknown | needs real-console confirmation | the original cartridge is documented as MMC1 with 8 KiB of battery-backed memory (community documentation, e.g. the [NESdev Wiki MMC1](https://www.nesdev.org/wiki/MMC1) and [SxROM](https://www.nesdev.org/wiki/SxROM) pages), and the core keeps 8 KiB for such an iNES 1.0 file; the console's own game file (its header, its exact identity) has not been examined |

No other game is in the table: no other size can be justified for an exact file yet. Because the Zelda entry has no exact game file, it is
matched only by the save's game code and only ever gives a comparison copy. An entry becomes `confirmed` only by a reviewed code change
after W02.

## 6. Classic Cart Studio's own saves

| Fact | Status | Source |
|---|---|---|
| `cart.sav` is the core's battery memory, exact bytes, no header | Verified | `tools/player/core/gp_save.c` (`gp_sram_save`, `gp_sram_load`) |
| On the console: `/var/lib/ccs/data/saves/<content SHA-256>/cart.sav` | Verified | `tools/companion/console_engine/ccs_install.py` (`DATA_DIR`), its tests |
| On a PC: the Windows app's `saves/<content SHA-256>/cart.sav`; inside a Full Studio Backup `progress/<content SHA-256>/cart.sav` | Verified | `tools/companion/saves_transfer.py`, `tools/companion/CCS-FORMAT.md` |
| Saved states (`slot1`–`slot4.ccsstate`) begin with `CCSSTATE` and are never battery saves | Verified | `gp_save.c`, `studio_backup.py` |
| A Classic Cart Studio state is not a native suspend point, and no conversion between them exists or is claimed | — | — |

## 7. Import rules (`SaveImport`)

`Decide` is the rule; `Plan`/`Apply` apply it to a folder on the PC, and a store of another kind applies the same decision.

- Nothing there: added (with the save's own date as the file date).
- The same bytes: nothing changes.
- A different save there: kept unless the person chooses. If the save there is **older**, Replace or Replace Newer Save replaces it. If it
  is **newer**, or which is newer cannot be told (no date, or the same date), only the explicit **Replace Newer Save** replaces it.
- Before any replacement the save there is kept first, write-once and read back, in `replaced-<UTC date>-<time>-<id>/<its name>` beside it,
  with its own file date and a record `replaced.json` (`ccs-replaced-save/1`: name, original size/SHA-256/date, when it was replaced, by
  which save, the choice). A kept copy is never written over; each replacement gets its own folder.
- `ListReplaced` lists the kept saves with an intact check; `Recover` turns one back into an incoming save with its own name and date, which
  is imported by these same rules (so the save it replaces is kept in turn). The kept files are ordinary files for export.
- Refused, nothing changed: a battery save of a different size (another game version or format); a saved state named as a battery save; a
  suspend-point file; a name with a path; a comparison copy (`trial`); bytes other than the conversion shown; a save folder that changed
  since the plan.

## 8. The conversion report (`ccs-save-conversion/1`)

`direction`, `status` (`confirmed` | `trial` | `synthetic`); `source` (the file: name, path, game code, title, date, origin; its size and
SHA-256; "kept: unchanged"); `game` (system, code, content SHA-256, how it was established); `core` (`fceumm`, its pin, the battery size);
`wrapper` (identifier, version, check value and case, payload length, padding removed, all zero); `output` (name, size, SHA-256); the entry
with its status, basis and sources; plain notes (the original is unchanged; whether play has confirmed it; suspend points are not battery
saves).

## 9. The owner's sample needed in W02

W02 is the owner-console, stock read-only baseline run of the owner's W01–W16 hardware order (private planning record): no write to the
console. Needed, in this order:

1. **The save.** One genuine battery save the owner recognizes on the stock console, preferably The Legend of Zelda (North America,
   `CLV-P-NAANE`), made before W02 by ordinary play (not created for the test). Note what the game's own file screen shows (slot names,
   hearts, items, location). If Zelda has no save, choose another battery-saving title and record its game code; it then needs its own
   table entry before any conversion.
2. **Taken from the verified W02 backup** (not a separate live copy): the folder listing of `/clover/profiles/0/CLV-P-NAANE/` with every
   file's name, size, date and SHA-256, and the battery file's bytes (`save.sram` or `cartridge.sram`, whichever is there; both if both).
   Keep the originals and their hashes; nothing from them goes into this repository or a public place.
3. **The console's own game file** for `CLV-P-NAANE`, from the same backup's system storage: its SHA-256 after the iNES header (the exact
   game identity) and its header (mapper, battery flag, iNES 1.0 or NES 2.0 and its PRG-NVRAM field).
4. **Measured on the PC:** `StockSave.Inspect` of the battery file (check value match, letter case, payload length, where the data ends);
   the battery size Classic Cart Studio's player uses for that exact game file (the size of the `cart.sav` it writes after an in-game save
   in a separate test data folder).
5. **Converted and played (stock → Classic Cart Studio):** the comparison copy from `ConvertToRaw`, played in Classic Cart Studio for
   Windows with the same game file, cold start, no saved state loaded: does the game's own file screen show the same save? Owner's
   observation, recorded.
6. **Reverse, offline:** `ConvertToStock` following the console's own file must give that file back byte for byte (the tests show the
   method on synthetic files). Reverse **through play** (a converted save written to the console and loaded there) is a console write: it
   needs its own approved run after W02 and is not part of W02.
7. **Then, by reviewed code change:** the exact game file's content SHA-256, the battery size, the console's payload length, the letter
   case and the file name go into the entry; `confirmed` only after both directions are confirmed by play.
