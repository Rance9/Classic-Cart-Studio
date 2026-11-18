# Hakchi2 CE upstream recheck

Checked November 15, 2026 against GitHub (`git ls-remote`, a fresh clone, and the GitHub releases API). Read-only: nothing is adopted,
pinned or changed by this note. Baseline: the review of November 14, 2026 (`_temp/backup-dec1-planning-20261114/UPSTREAM-51-COMMIT-REVIEW.md`
and `upstream-dispositions.json` in the owner workspace), which covered `25bfcfd04b8ed12f7d513dc87c2a0f7096d38ebc` (3.9.3) to mainline
`4fb3092c29cd39a85504bd75889ffb18e749a704`.

## Result

| Item | Today | Same as the review? |
|---|---|---|
| Latest release | `v3.9.3` (published 2022-03-15; GitHub `releases/latest`); tag commit `25bfcfd04b8ed12f7d513dc87c2a0f7096d38ebc` | Yes, and it is the pinned version |
| Mainline head | `4fb3092c29cd39a85504bd75889ffb18e749a704` (2025-11-20, "Use System.IO.Compress.GZipStream to decompress mod repo pack") | Yes |
| Commits `v3.9.3..mainline` | 51; the set of hashes equals the 51 `sha` entries in `upstream-dispositions.json` exactly (none missing, none extra) | Yes |
| Pinned hakchi.hmod (`supplemental` release asset `hakchi-v1.0.4-126…hmod`) | 9,340,382 bytes, SHA-256 `003994b46504959895f40b0b9e91b16951ef4a638feb6312314c8fc4fdbff592` | Yes |
| Pinned driver helper (TeamShinkansen/libwdi release `classic_driver_1`, `classic_driver.exe`) | 5,841,920 bytes, SHA-256 `88a2c6bc3e296c81ca3544557a2e254e894c867ceb0d60e14c2fc2e5d610462d`, equal to `hakchi2-ce-3.9.3\driver\classic_driver.exe`; no newer release of it | Yes |

**There are no new mainline commits since the review.** Nothing new needs a disposition on the release line CCS pins.

## Commits outside mainline (not new to mainline, listed for completeness)

These branches exist upstream and were outside the mainline-scoped review. All are dated before the review; none is in a release.
One-line relevance for CCS, most relevant first:

| Branch | Commit | Date | What it does | Relevance / disposition |
|---|---|---|---|---|
| `copilot/update-net-and-cancellation-tokens` | `91e3efbd14e91b0a56a4ef70c3c4191f0df2d229` | 2026-03-01 | .NET 9; replaces `Thread.Abort` with cancellation in `Clovershell/ClovershellConnection.cs`, `ExecConnection.cs`, `ShellConnection.cs` (closes the device to stop the reader) | Related to the USB shell CCS derived its clovershell pipe from, but an unreleased automated branch; CCS has its own worker cancellation. **Skip; note only.** |
| `copilot/update-net-and-cancellation-tokens` | `22c07485455a30c74ef36e014022989ea8a52247`, `9c0931a5a22921ae7cc28435c830e1790b44f5ef`, `daaca65eb59f94158d5e7cda3039c980512f9bab`, `e1c1b18938e03ebd9116a00bd7d0d501640bf043` | 2026-03-01 | CI fixes, removal of an accidentally committed API-key folder, plan | No CCS path. **Skip.** |
| `net-10` | `37f27179dbe061ff03640b4030a7ee992df3b0d0` | 2026-06-08 | Preliminary .NET 10 upgrade; deletes `hakchi_gui/Clovershell/*` from the GUI project and moves submodules (SSH.NET and others) | Restructures the clovershell source CCS cites; unreleased. **Skip; recheck source locations if it ever reaches a release.** |
| `net-10` | `9168b845571e41d183a103f83b7dbaedcfa19269`, `bb70cc7c0317734ca1e34321a9fa156160a41efb` | 2026-06-08/09 | Cancellation instead of `Thread.Abort` (SSH client wrapper); handle thread-interrupted exceptions in GUI tasks | Not the USB path CCS uses. **Skip.** |
| `net-10` | `594f97af59f9ec31523c9064d9a860978bb8bd73`, `25b895ae996cb885412a1a48267dab4880d17b15`, `50774dfd2ef5a6e494c0d12f3c1191dcef37aad6`, `f3179c38c768d7a40cd7bc6589b20fabc32e3a21`, `a71a6984dbd174b4221f4d4e8cc0117c35fabb1b` | 2026-06-09/10 | Host builder, path helpers (touches `Tasks/MembootTasks.cs` path joins only), dependency-injection registration | Architecture refactor; no storage, USB or driver fix. **Skip.** |
| `copilot/update-hakchi-gui-to-net-10` | `d70da6a34dd45c59a801ce1edfdfd973983c1099` | 2026-06-08 | Retarget to `net10.0-windows` | **Skip.** |
| `copilot/update-hakchi-gui-dotnet-lts` | `60c26ab675dcda553544e5f330608f98e0843e43`, `51fbb94ad36ac04d1f42410bb8a3aadcd127cbb9`, `3d6f4c6a5adb8e80269e77f36d300d7b09d32cde` | 2026-02-28 | CI: busybox download, .NET SDK 8, artifact paths | **Skip.** |
| `LullyBeef/mainline` | `fe7e1f6a2d7649a7806a4fbb10614999213fa283` | 2025-11-20 | Region code tidy-up in game import | **Skip.** |
| `update-fel` | `6d680faa71315a4421b44c700e719dc4479a11e5` | 2022-11-12 | Burn-mode handshake (same idea as reviewed row 21) | Already covered by row 21's disposition. **Skip.** |
| `10-minute-timeout` | `4de19bc0e48d92343fb1f63a8394ef5233bf5a7c` | 2022-07-22 | Longer timeout in `MembootTasks.cs` | Unreleased; CCS's RAM boot has its own bounded waits. **Skip.** |
| `game-genie` | `c0e6da018f672cfe92273e9131a221a5365d89b2` | 2022-05-25 | Re-enable Game Genie | **Skip.** |

## Driver-related upstream (libwdi), for the driver audit

The pinned helper is built from TeamShinkansen/libwdi `6d379a32e93e8a8f27e6205cf2c45d26ab98a68d` (2018). Mainline Hakchi moved the
submodule to `f6ee18622ecf434818136af67f4d71451c010c63` (2019-11-07, "Update vendor and device description") in reviewed row 33, but no
new `classic_driver` binary was released. The fork's branch `copilot/merge-upstream-changes` (`1ef20e17748ddcaa659f7fb840765f987173098f`,
`96161684bf4a0986b06fc469d4a87843d04fd354`, `e35860c0a9e30f90b67df02f220b01fa88865f21`, 2026-06-08) merges upstream libwdi and fixes the
build, unreleased. Upstream pbatard/libwdi (latest tag `v1.5.1`, `9b23b82a2dd1cbffc16d46c212f92c6bf8c0c602`) has since fixed, among
others:

| Commit | Date | Fix | Bearing on the pinned helper |
|---|---|---|---|
| `3d5d0292294c5b833709e66752aca6f60fca5ba8` | 2021-10-28 | Stop the subject-name certificate deletion loop when a delete fails | The pinned helper can hang there (`pki.c:566-570`) |
| `e665d769c43067de1cca20917e38941b7fb4155b` | 2021-02-01 | Work around Windows corrupting key containers | Pinned helper reuses `libwdi key container` if present |
| `37841c236177a54a79bd8ac60a9fd9d5a023f099` | 2021-09-27 | Do not continue when catalog signing fails (unless test signing) | Pinned helper continues after a signing failure |
| `bd17edc3e23d1c7c67dbf78daecc7d3012cae048` | 2023-09-15 | Correct the UAC test before starting the installer | Pinned test is inverted (`libwdi.c:1561`) |
| `66d282e6bf0315c51744ad21c18c52016277e782` | 2025-02-28 | Fix a crash when `SignerSignEx` fails | Pinned helper has that code path |
| `13dd5dfdc9904963cfdcad96de48a3613c777bfb` | 2025-03-04 | Prevent DLL side-loading (`/DEPENDENTLOADFLAG:0x800`) | Pinned helper and its installer predate it |

Disposition: **report only.** None changes the pinned bytes; adopting a rebuilt helper would be a new, separately reviewed driver pin.
