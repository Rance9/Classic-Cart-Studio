# Milestone 3.4: comprehensive NES description accuracy audit

- **Branch:** `home-visual-remediation`, starting from `4fcb3ee` (parent `26ea74a`). **Commit:** `b8f75864c3ca25e438ca723728b27f07bc650e96` (local only).
- **Not done:** no push, merge, deployment, release or review-branch update. Gate G1 is closed.
- **No model use:** no Gemini or other paid model request; no image generation. Every text was checked by independent auditors against NES-specific evidence and by deterministic rules.

## 1. Executive summary

- **Every one of the 773 Game overviews was independently audited** against its original US NES manual (page images, not OCR), the game's own on-screen text where needed, and NES-specific secondary sources. The planned main queue was 360 records; the recheck of previously audited records found the same error pattern in every earlier group, so the escalation rule extended the audit to all 773.
- **671 overviews changed** (87%):

  | Change | Records |
  |---|---:|
  | Substantive factual corrections | 455 |
  | Bounded rewrites (wrong game or another version) | 65 |
  | Unsupported specifics generalized | 134 |
  | Name-only corrections | 17 |
  | No change needed | 102 |

- **Coverage stays at 773 of 773.**
  - **Level D (fully audited and reconciled):** 685.
  - **Level C (source-verified, some claims only partly corroborated):** 87.
  - **Unresolved:** 1, Exploding Fist, kept for owner review.
  - **Withheld:** none.
- **Most serious findings:**
  - Texts describing a **different game**: Star Wars described the Japan-only Namco game; Indiana Jones' Action Game described Taito's game; Tengen's Tetris followed Nintendo's.
  - **Wrong manual matches** from M3.2, which tied four records to the wrong manual (two of them above).
  - **Names and numbers made from garbled OCR:** Conan's "Ry-lech", Jackie Chan's "40" Bonus Jade, Ferrari's "26-lap" races.
  - Systematic **other-version contamination** (162 records), including records the earlier model audit had "verified".
- **Official descriptions:** 101, unchanged. Adventure Island's approved excerpt and Ren & Stimpy's in-character excerpt are intact.
- **Verification:** standard run 37 of 37 suites passed under Python 3.13.15 (2,963 checks: 2,961 passed, 0 failed, 2 unsupported); description data proofs 11 of 11; HOME visual gate 48 of 48 after a documented baseline update.
- **Gemini:** no new requests; cumulative reported cost stays $5.22.

## 2. Starting baseline (`4fcb3ee`)

| Item | Value |
|---|---:|
| Game overviews | 773 of 773 |
| Official descriptions | 101 |
| Independently verified before M3.4 (levels C and D, from M3.3) | 48 |
| Standard verification | 37 suites, 2,956 checks |
| Visual regression | 48 screens |

## 3. Inventory reconciliation

Full detail: `M3-4-AUDIT-INVENTORY.md` and `audit-inventory.json` (one record per canonical game ID).

- **391 (M3.2 audit) + 263 (outside that audit) + 119 (recovery set) = 773.** No record is counted twice.
  - **M3.2 audit (391):**
    - 158 with contradictions (corrected in M3.2);
    - 145 medium risk (144 without contradiction, plus 1 counted under corrected);
    - 89 low risk.
  - **Outside the audit (263):** the 260 NES-native records (259 untouched, plus Cheetahmen II, which was polished) and 3 rejected drafts edited by hand in M3.2.
  - **Recovery set (119):**
    - 117 missing overviews: 105 recovered from manuals, 10 from researched facts, 2 unresolved and later recovered in M3.3;
    - 2 rebuilt rejected drafts.
  - **M3.3:** audited 44 of these (22 medium risk, 22 NES-native) and decided 4 texts.
- **Verification level before M3.4:**

  | Level | Records |
  |---|---:|
  | A, AI claim-checked only | 249 |
  | B, manual consulted by a model through OCR | 476 |
  | C, independently source-verified | 11 |
  | D, fully audited and reconciled | 37 |
  | **Requiring first independent verification (A + B)** | **725** |

- **Queues:**
  - **Main:** 360 records, exactly M3.3's recommended 122 medium-risk plus 238 NES-native, by canonical ID.
  - **Recheck:** 66 previously audited, corrected or recovered records.
  - **Escalation pool:** 347.

## 4. Records independently audited

- **Total:** 773.
  - 360 main.
  - 66 recheck.
  - 347 escalation, covering the remaining previously audited records and all 48 M3.3 records.
- **Batches:** 12 main batches of 30, 2 recheck batches of 33 and 12 escalation batches of 28 or 29. Each was split among three auditors working independently, with resumable result files.
- **Re-audits:** 2 records were re-audited after their manual mapping was corrected.

## 5. Primary-source coverage

- **NES manual available:** 761 of 773 records (archive.org NESManuals). 13 were found under other titles (for example "T&C Surf Designs", "Legend of the Ghost Lion", "Race America"), and M3.2's manual-match mistakes were corrected for 4 records (section 8).
- **Page images inspected:** 760 records. Every manual was rendered to 2,000-pixel page images; names and numbers were read from the images, not the OCR.
- **Claims checked:** 7,114.
  - 4,567 supported by the manual.
  - 635 supported by an NES-specific second source.
  - 748 contradicted.
  - 1,164 uncertain in the original texts; these were removed or generalized unless corroborated.
- **On-screen text:** the game's own text was read from the ROM (in memory only, never extracted or modified) where it settled a name or an identity:
  - Double Dragon II ("MARIAN");
  - Mega Man 2 ("WILY");
  - Mega Man 5 ("PROTOMAN");
  - Final Fantasy ("LIGHT WARRIORS");
  - Cheetahmen II (plural "CHEETAHMEN" only);
  - Exploding Fist (credits Beam Software and Piko Interactive);
  - Super Gun (rechecked).

## 6. Secondary-source coverage

- **Use:** where a manual is silent, important claims needed a second NES-specific source, such as:
  - NES reviews (Hardcore Gaming 101, Take on the NES Library, Nintendo Life, NES Files);
  - TASVideos run notes;
  - nesdev;
  - GameFAQs NES guides;
  - MobyGames NES entries.
- **Circular sources rejected:** two sites repeating the same encyclopedia sentence did not count as corroboration.
- **Records without a manual in their batch packet (12):** they rest on secondary sources and the ROM.
  - **Level C:** 7.
  - **Level D:** 4. Race America's manual was found by its auditor under another title. Racermate's facts rest on hardware documentation and ROM text. Magic Carpet 1001 uses the Caltron 6-in-1 manual for the same game. Hot Slots was generalized to supported facts.
  - **Unresolved:** 1 (Exploding Fist).
- **Limits:** several sites were blocked or returned 403 (StrategyWiki, GameFAQs live pages, MobyGames, Giant Bomb, NesCartDB, web.archive.org for some auditors). A blocked source was treated as no evidence either way, never as evidence against a claim. TCRF and qix.wiki.gg pages returned injected instructions; they were ignored and never used as evidence.

## 7. Confirmed factual discrepancies

Error types across the 671 changed records (a record can have several):

| Error type | Records |
|---|---:|
| Unsupported specific | 459 |
| Wrong name | 230 |
| Wrong rule | 203 |
| Invented detail | 199 |
| Other-version detail | 162 |
| Wrong item effect | 90 |
| Wrong count | 45 |
| Wrong player mode | 28 |
| Wrong control | 28 |

The full before-and-after text, evidence and verification for each record is in `M3-4-EDITORIAL-CORRECTIONS.md`.

## 8. Naming corrections

- **Rule applied:** names follow the US NES release: its manual, and the game's own on-screen text where the two differ (direct evidence from the release ranks first). Examples:
  - "Magic Mushroom", "Bubby", "Castle of Ruth" (M3.3, retained);
  - "Light Warriors" and "Orbs" (Final Fantasy, confirmed in the ROM);
  - "Rubies" and "Whistle" (The Legend of Zelda);
  - "Necrosaro" and "Taloon" (Dragon Warrior IV);
  - "Dr. Gangrene", "Boralis", "Centurians", "Michaelangelo", "Morica";
  - "Marian" (Double Dragon II, on-screen text over the manual's "Marion");
  - "Dr. Wily" (Mega Man 2, on-screen text over the manual's "Wiley").
- **Proper-name evidence:** every proper name in all 773 overviews now has recorded evidence, either in the record's sources or attested by an auditor from a named manual page or the ROM text (`name-evidence.json`, `name-attestations.json`). The 15 names left after the audit were checked by hand on the page images; five wordings were corrected (Bigfoot, Gun.Smoke, Rockin' Kats, ImageFight, Cheetahmen II).
- **Manual-match corrections:**

  | Record | Matched before | Corrected to |
  |---|---|---|
  | Indiana Jones and the Last Crusade – The Action Game | Taito's game | UBI Soft manual |
  | Pac-Man (Namco) | Tengen booklet | Pac-Man (USA) manual |
  | Ms. Pac-Man (Tengen) | Namco's 1993 booklet | no manual exists for this release |
  | Tetris (Tengen) | Nintendo's booklet | Tetris – The Soviet Mind Game manual |

## 9. Unsupported claims removed

134 records changed only by generalizing or removing specifics that no NES-specific source supports, for example:
- exact stage or room counts;
- character surnames;
- unconfirmed bosses or modes.

Many more corrections also generalized one or more claims; that is the "unsupported specific" count of 459 in section 7.

## 10. Full rewrites

**65** records needed a bounded rewrite from NES evidence, because the text described another version or another game. Examples:
- Star Wars (Japan-only Namco game);
- Indiana Jones – The Action Game (Taito's game);
- Tetris (Tengen);
- Advanced Dungeons & Dragons: DragonStrike, Rocket Ranger and Wayne Gretzky Hockey (computer versions);
- Commando, Vindicators and Xenophobe (arcade);
- Fist of the North Star (anime story);
- Werewolf: The Last Warrior (Japanese story).

The full list is in the corrections log.

## 11. Previously audited records rechecked

See `M3-4-PRIOR-AUDIT-RECHECK.md`. **66** records were drawn by seeded selection with the owner's category minimums:

| Category | Records |
|---|---:|
| Arcade ports | 11 |
| Computer or cross-platform | 10 |
| Previously corrected | 34 |
| Medium risk | 10 |
| Low risk | 10 |
| NES-native or secondary-source | 18 |

**49** changed, 32 of them substantively:
- every M3.2 low-risk record checked (10 of 10) and every M3.3-audited medium-risk record (10 of 10);
- 14 of 20 M3.2 audit corrections;
- 4 of 12 M3.2 manual recoveries.

## 12. Results by risk category

| Group (from M3.2/M3.3) | Records | Substantive | Rewrite | Generalized | Name only | No change |
|---|---:|---:|---:|---:|---:|---:|
| M3.2 audit, corrected | 158 | 93 | 6 | 29 | 5 | 25 |
| M3.2 audit, medium risk | 144 | 91 | 16 | 27 | 3 | 7 |
| M3.2 audit, low risk | 89 | 56 | 4 | 17 | 2 | 10 |
| NES-native (never audited before M3.3) | 260 | 162 | 38 | 42 | 3 | 15 |
| M3.2 recovered from manuals | 105 | 44 | 1 | 17 | 4 | 39 |
| Other M3.2 groups (research, rebuilt, edited, unresolved) | 17 | 9 | 0 | 2 | 0 | 6 |

By port type:

| Port type | Substantive or rewrite | Records |
|---|---:|---:|
| Arcade ports | 104 | 135 |
| Computer ports | 36 | 67 |
| NES-native or none | 373 | 561 |

**Lowest error rate:** the M3.2 manual recoveries, because they were written from the NES manual. They still had errors in 63% of records.

## 13. Systematic error patterns

1. **Encyclopedia text about other versions:** arcade, computer, Genesis or Japanese releases (162 records). The earlier model checks accepted these claims because they were in the source the text was written from.
2. **Wrong game identity:** a different game with the same title (Star Wars, Indiana Jones – The Action Game, Tengen's Tetris), or a manual for another release.
3. **Later or regional names** instead of the US release's names (230 records with a wrong name).
4. **OCR artifacts** turned into names and numbers (Conan's "Ry-lech", Jackie Chan's "40", Ferrari's "26-lap"). In the other direction, ImageFight's "Boondoggle Galaxy", removed in M3.2 because of garbled OCR, was restored from the page image.
5. **Invented specifics** to fill out the text: counts, surnames, item colors and endings (199 invented details, 459 unsupported specifics).

Because the pattern held in every group, the escalation rule extended the audit to all 773 records. No unrelated architectural change was needed.

## 14. Remaining uncertainty

See `M3-4-REMAINING-EXCEPTIONS.md`.
- **87 records at level C:** none contradicted. The partly corroborated claims are listed per record.
- **Exploding Fist:** unresolved; no NES manual or NES-specific source.
- **Naming and terminology questions for the owner:** see that file.

## 15. Final description coverage

| | Records |
|---|---:|
| Game overviews | **773 of 773** |
| Fully accepted (level D) | 685 |
| Material claims partly unverified (level C) | 87 |
| Unresolved, needing owner review | 1 (Exploding Fist) |
| Withheld | 0 |
| **Effective coverage** | **773 shown; 772 independently source-verified (C or D)** |

## 16. Automated verification

Python 3.13.15, run `<local work folder>\logs\descriptions-m34\verification\20261015-004918`.

| Suite | Result | Checks | Passed | Failed |
|---|---|---:|---:|---:|
| home-engine | PASSED | 164 | 164 | 0 |
| home-m25-navigation | PASSED | 305 | 305 | 0 |
| home-multitab | PASSED | 21 | 21 | 0 |
| home-favorites-tabs | PASSED | 29 | 29 | 0 |
| home-time-zones | PASSED | 39 | 39 | 0 |
| descriptions-model | PASSED | 22 | 22 | 0 |
| descriptions-data | PASSED | 50 | 50 | 0 |
| descriptions-presentation | PASSED | 193 | 193 | 0 |
| home-m1-regressions | PASSED | 111 | 111 | 0 |
| home-controller | PASSED | 715 | 715 | 0 |
| home-repeat | PASSED | 35 | 35 | 0 |
| home-persistence | PASSED | 55 | 55 | 0 |
| tv-preview | PASSED | 102 | 102 | 0 |
| tv-search | PASSED | 21 | 21 | 0 |
| catalog | PASSED | 185 | 185 | 0 |
| studio-page | PASSED | 52 | 52 | 0 |
| install-journey | PASSED | 23 | 23 | 0 |
| home-api | PASSED | 22 | 22 | 0 |
| home-sync | PASSED | 14 | 14 | 0 |
| folder-art | PASSED | 9 | 9 | 0 |
| console-engine | PASSED | 69 | 69 | 0 |
| console-api | PASSED | 30 | 30 | 0 |
| manifest-v4 | PASSED | 37 | 37 | 0 |
| manifest-v4-pure | PASSED | 17 | 17 | 0 |
| storage-states | PASSED | 22 | 22 | 0 |
| genre-overrides | PASSED | 7 | 7 | 0 |
| hero-guard | PASSED | 5 | 5 | 0 |
| hero-artwork-integrity | PASSED | 102 | 102 | 0 |
| artwork-integrity | PASSED | 10 | 10 | 0 |
| visual-regression | PASSED | 48 | 48 | 0 |
| pages-site-home-assets | PASSED | 17 | 17 | 0 |
| pages-site-catalog | PASSED | 185 | 185 | 0 |
| pages-site-descriptions | PASSED | 30 | 30 | 0 |
| public-site | PASSED | 31 | 31 | 0 |
| console-simulator-windows | PASSED | 62 | 62 | 0 |
| console-simulator-linux | PASSED | 62 | 61 | 0 |
| console-simulator-arm | PASSED | 62 | 61 | 0 |
| **Total** | **37 passed, 0 failed, 0 blocked, 0 not executed** | **2,963** | **2,961** | **0** |

- **Skipped:** none. **Unsupported:** 2 (the Windows-only check on the Linux and ARM simulator paths). **Known defects counted as passes:** none. **Leaked test browsers:** 0.

**First full run (`20261015-002517`):** 36 passed and 1 failed. The failure was the visual gate: 12 of 48 screens changed because their overview texts changed. The scope check (`desc_visual_scope.py`) found 0 changed pixels outside the description areas on all 12: the Details text box, the Box Art Showcase preview line and the Adventure Map preview. A visual review confirmed that geometry, art, titles, buttons and focus visuals are unchanged. The baseline was then replaced with that recorded reason (`home_visual_gate.py baseline --replace --reason …`). The previous baseline is kept at `<local work folder>\home-visual\baseline.replaced-20261015-004748`. The complete run was repeated and passed 37 of 37.

**New checks (description data suite, now 50 checks):**
- `m34_corrected_claims_do_not_return` and `m34_corrected_facts_present`: per game ID, for the wrong-game, wrong-manual, OCR and naming patterns.
- `m34_proper_names_have_evidence`: every proper name in every overview has recorded evidence.
- `m34_only_decided_texts_changed`: against the 4fcb3ee baseline.

**Failing-first:** on the 4fcb3ee data, all three content checks fail. For example, the name check finds "Abigail Craven" (The Addams Family) and "Devil of Darkness" (Deadly Towers). All three pass on the M3.4 data.

**Deliberate-failure proofs (description data):** 11 of 11 behaved as intended. Two are new: `descdata-invented-name` and `descdata-wrong-game-returns`. The other proofs cover code M3.4 did not change.

**Privacy and credential scans:** the repository and site scans found 0 prohibited items and 0 private-path references. No API key appears in any client asset. The public runtime holds only the two texts per game (data suite).

## 17. Gemini spending

- **No new requests:** no Gemini or other paid model request, and no image generation, in M3.4.
- **Ledger:** `<local work folder>\descriptions\ledger.json` was last written on 2026-10-14 at 18:52, during M3.2. Cumulative actual cost: $5.2247.
- **Not checked:** account-side billing.

## 18. Files modified

**Committed in the public dev tree** (`<local public dev tree>`, branch `home-visual-remediation`):
- `site-data/descriptions.json`: the M3.4 texts (schema `ccs-descriptions/1`, texts only).
- `tools/rom-pipeline/test_descriptions_data.py`: the M3.4 checks above, and the shared proper-name extractor.
- `tools/rom-pipeline/m2_failure_proofs.py`: two new proofs.

**Development workspace only** (not on the public allow list):
- New tools:
  - `desc_m34_manuals.py`: manual cache, page rendering, aliases and match corrections.
  - `desc_m34_inventory.py`: the 773-record inventory, queues, seeded recheck and merge.
  - `desc_m34_batches.py`: resumable batches.
  - `desc_m34_decide.py`: audit results and hand adjustments into decisions.
  - `desc_m34_names.py`: proper-name evidence.
  - `desc_m34_review.py`: review flags.
  - `desc_m34_romtext.py`: read-only ROM text search.
  - `desc_report_m34.py`: the generated reports.
- Changed tools:
  - `desc_m33.py`: generalized to the M3.4 decisions file and milestone; quality rules aligned with the data suite.
  - `desc_m32.py`: the M3.2 stages never touch M3.4 decisions.
  - `game_descriptions.py`: M3.4 fields carried into the private record.
- `reports/descriptions.json` (workspace copy of the public data) and the rebuilt `reports/nes-catalog.html`.

**Private records** (`reports/game-descriptions/`):
- `m3-4/m34-decisions.json`: all 773 audit records, claims, sources and pages.
- `m3-4/audit-inventory.json`, `m3-4/editorial-adjustments.json`, `m3-4/name-evidence.json`, `m3-4/name-attestations.json`, `m3-4/m34-baseline.json`, `m3-4/m34-stats.json`.
- `editorial-overrides.json` and `game-descriptions.json` (updated).
- Research and batch results in `<local work folder>\descriptions\m34\`: manual cache and page images, batches, results, and backups of the 4fcb3ee state.

**Review files:** the seven `M3-4-*.md` reports and `m3-4/screenshots/` (7 PNG files).

**Not changed:** no artwork, hero illustration or master, HOME or website code, ROM, ROM header, catalog identity or metadata, official description, or console file. The visual baseline was replaced as documented in section 16.

## 19. Final commit

`b8f75864c3ca25e438ca723728b27f07bc650e96` on `home-visual-remediation` in the public dev tree, local only. The starting commit was `4fcb3ee`, the branch was confirmed before committing, and the 773 canonical IDs are unchanged (`catalog_unchanged_since_research`).

## 20. Recommendation

- **Ready for owner and ChatGPT review:** the description dataset. Every record has an independent, documented audit, and the corrections are traceable.
- **Not yet final editorial sign-off:** that should follow the owner's answers on the naming questions and on Exploding Fist (`M3-4-REMAINING-EXCEPTIONS.md`). None of them blocks use of the current data.
