# Default-catalog game description enrichment: report

**Branch:** `home-visual-remediation`, commit `36109aa` (parent `f97adba`).
**Status:** not pushed, not merged, not deployed. Gate G1 is closed. No console was used.

**Scope:**
- Every game in the website's default catalog got two description slots:
  - an **Official description**: verified official text only, never written by a model;
  - a **Game overview**: a neutral overview written by Gemini from supplied sources and then checked claim by claim.
- Both slots appear in:
  - the website's Game Details;
  - Game Details in all four Preview on TV HOME themes;
  - the shared HOME model that the future native HOME will use.
- Per the owner's addendum, no sources, links, citations or verification data appear anywhere in the product or in the public runtime data. They live only in private records.

## 1. Exact default game count

**773 games:**
- 773 records in `reports/catalog.json`, the curated library the website shows when the Extended Library switch is off;
- 773 distinct game identities, so each record is its own description target;
- 95 of them unlicensed.

**Snapshot:**
- Taken before generation: `<local work folder>\descriptions\catalog-snapshot.json`, catalog SHA-256 `722e23ff4ff2…`.
- The catalog file is unchanged: the data suite checks the hash.

**Identity rules:** see `IDENTITY-RULES.md`. The main rules:
- Only the NES release is described.
- Nintendo's *Tetris* and Tengen's *Tetris* are separate games with separate sources and texts.
- An article may be shared only between releases of the same NES game. Examples: *Gauntlet* licensed and unlicensed; the Namco and Tengen *Pac-Man*; *Punch-Out!!* with and without Mike Tyson.
- Sequels, series pages and compilation members that borrowed another game's article lost it.
- European retitles map only when the content is the same: *Street Gangs* maps to *River City Ransom*. *Probotector II* is not mapped.

## 2. Description data architecture

**Private dataset**
- Path: `reports/game-descriptions/game-descriptions.json`, schema `ccs-game-descriptions-private/1`, 773 records keyed by `game_id`. It is in the private workspace only and is not on the public allow-list.
- Each record holds:
  - **Identity:** title, No-Intro name, publisher, year, licensed and region.
  - **Official description:** status, text, text type, US-spelling changes, and the source's organization, title, URL, type, platform, verification note, retrieval date and page SHA-256.
  - **Game overview:** status, text, stage, port mode, evidence level, every draft with its checker verdicts, trim and grammar-correction records, model, generation and validation times.
  - **Research references:** encyclopedia article, revision, sections used.
  - **Review status and checksum.**
- The identity rules are also recorded.

**Research and generation records**
- Location: `<local work folder>\descriptions`, outside OneDrive.
- Contents:
  - source caches and catalog snapshot;
  - every request file (`jobs/`) and response;
  - per-stage results;
  - the spending ledger (`ledger.json`);
  - the log.

**Public runtime data**
- Path: `reports/descriptions.json`. It is published as `site-data/descriptions.json` and copied by the Pages build.
- Schema `ccs-descriptions/1`, with the shape `{"games": {game_id: {"official"?: text, "overview"?: text}}}`.
- Contents: 651 records, 584,629 bytes (214,883 gzip). A missing key means "not available".
- It holds nothing else: no URLs, sources, statuses, scores, prompts or model names. The data suite enforces this.

**Use at runtime (offline: no network call and no model call)**
- `build_html.py` embeds the file into the page as `DATA.descriptions`.
- Each default-catalog row gets `official_description` and `game_overview` (null when missing).
- The HOME model (`home-model.js`, `homeBuild`) gives every game a `descriptions: {official, overview}` field. This is the same field the future native HOME will read from the collection model.
- Personal games keep their own `description`. Fixtures and personal games carry `descriptions: null`.

## 3. Existing sources reused

**From the project:**
- **Catalog identity and metadata:** title, publisher, year, genre and player count. These are given to the model as context only; publisher and year are never written into the text.
- **Gemini infrastructure:** the hero-artwork campaign's Batch API pattern, namely reserving before sending, persisting before upload, reconciling by display name before resubmitting, and costing from usage metadata. The API key is read from the environment, as before.
- **Test harnesses:** the HOME controller harness (real key events), the visual gate, the deliberate-failure proof tool and the standard runner.

**What existed before:**
- No game descriptions existed. The catalog had no description field, and the website said "Description: not available (no reliable local source)".
- HOME Details already had a scrollable text box that no catalog game used. It is now the two-section box.

## 4. Official-source research method

**Search**
- Scope: Nintendo of Europe's official game catalog on nintendo.com, all 184 entries in its search index for "Nintendo Entertainment System".
- Each page's Overview text was read.
- Pages that list any other system were rejected. The index also returns Super NES pages.

**Matching**
- Normalized titles: subtitles, "The" and trademark marks are handled.
- Two pure European retitles are mapped.
- When several catalog records match, only a publisher match decides.
- Ambiguous matches are left out.

**Excerpts**
- Whole opening sentences, at most 75 words.
- Sentences about a re-release service are dropped, such as Wii Remote controls or "an import of the original US version".
- UK spellings are changed to US spellings to follow the owner's US English rule: "colour" becomes "color", "centre" becomes "center". Each change is recorded per game, and all words are otherwise verbatim.

**Results**
- 96 verified official descriptions; all 96 excerpts were read in review.
- 0 publisher-sourced summaries: no paraphrase was needed.
- 677 games are `not_found` and show "No verified official description available.". Most catalog games have no official page in that source; many remaining NES pages there are for PAL/Japan-only releases.
- No other publisher site was verified, and none was used.

Provenance (private): `OFFICIAL-SOURCE-PROVENANCE.md`.

## 5. Gemini model, configuration and Batch API

**Model and pricing**
- Model `gemini-3.8-flash` (text only), confirmed live on the account.
- Pricing checked on Google's pricing page on 2026-10-14: Batch input $0.375 and output $1.875 per million tokens; the standard tier costs twice that.
- Requests had thinking level "low", a JSON response schema, and system instructions.
- No grounding, no images, no tools.

**Inputs per game:** catalog metadata, the official page text (when found) and the encyclopedia sections verified for that NES game. The encyclopedia sections are capped at 9,000 characters and limited to the lead, gameplay and plot; reception, legacy and sales sections are excluded.

**Encyclopedia coverage:** 679 of 773 games.

| Source type | Games |
|---|---|
| NES games list link | 592 |
| Title form | 69 |
| Strict exact-title search | 7 |
| The game's own section of another article | 6 |
| Compilation members | 3 |
| Identity override | 2 |

Every article had to be in a Nintendo Entertainment System games category.

**Pipeline, one bounded pass per stage:**
1. **Pilot:** 3 games at the standard tier ($0.014). It confirmed the request format, schema and token use.
2. **Draft (gen1):** 676 games. The rules:
   - facts only from the sources;
   - no praise, reception or marketing; no "you";
   - 90–150 words in two paragraphs;
   - other versions are excluded;
   - the opening style varies by game.
3. **Claim check (ver1):** a second request split each draft into claims and judged each one against the same sources. It also flagged evaluative wording and other platforms.
4. **One regeneration (gen2/ver2)** with the list of problems, for drafts that failed the check or the language lint.
5. **Port mode (gen3/ver3, then gen4/ver4):** 212 games whose article is about an arcade or computer original. Review of the first pass found that such drafts could carry details of the other version; the first Pac-Man draft described the arcade intermissions. These games were redone so that only defining core gameplay is used, plus anything the official NES text states. The checker applied the same rule. Each got one regeneration.
6. **Trim (trim/vtrim):** the 63 verified overviews above 150 words were shortened by removing detail only, then checked again. 61 were accepted; for 2, the shortened text was rejected and the verified longer text kept.
7. **Deterministic steps:**
   - US spelling normalization;
   - the language lint (exclamation marks, second person, markup, URLs, source mentions, other platforms, evaluative words);
   - two grammar-only corrections from review.

**Spending guards:**
- Every batch is estimated and reserved at its worst case before upload.
- The reservation is persisted before anything leaves the machine.
- A batch whose worst case would pass $23.50 is refused.
- An unconfirmed submission is reconciled by display name, never resubmitted blindly.
- A stage runs at most once per game.
- No retry was needed: 0 responses were unreadable or missing in any batch.

## 6. Actual API costs

| Item | Value |
|---|---|
| Requests | 2,168 (6 pilot requests at the standard tier, 2,162 in 10 batch jobs) |
| Tokens | 2,649,505 input, 937,019 output (1,527 of the output were thinking) |
| **Actual cost** | **$2.76** (pilot $0.014, batches $2.746), from the usage metadata |
| Remaining of the $25.00 cap | $22.24 |
| Unresolved reservations | 0 |

Per-batch details: `GEMINI-LEDGER.md`.

## 7. Description coverage

| | Games | Share |
|---|---|---|
| Default catalog | 773 | 100% |
| Verified official description | 96 | 12.4% |
| Publisher-sourced summary | 0 | 0% |
| Official not found (missing message shown) | 677 | 87.6% |
| Game overview shown (passed every check) | 651 | 84.2% |
| ↳ first draft accepted | 551 | |
| ↳ after one regeneration | 100 | |
| ↳ port mode (core gameplay only) | 208 | |
| ↳ shortened and re-checked | 61 | |
| ↳ 40–89 words (sparse sources, not padded) | 94 | |
| Overview held for editorial review | 28 | 3.6% |
| No usable source (no overview attempted) | 94 | 12.2% |
| Neither text (both missing messages shown) | 122 | 15.8% |

**By license:** licensed games 594 of 678 have an overview; unlicensed games 57 of 95.

**Overview length:** median 125 words; 554 within 90–150; 3 between 151 and 159; 94 below 90.

## 8. Source-verification coverage

- **Official texts:** 96 of 96 trace to a Nintendo page with complete provenance: organization, title, URL, type, platform, verification note, retrieval date and page hash. The data suite checks every one.
- **Overviews:** all 651 displayed overviews passed the claim checker with every claim supported, no evaluative wording and no other platform. They also passed the deterministic lint. All have a recorded research source, and none uses a source shared across a game identity boundary.
- **Claim-check results:**
  - The checker failed 55 of 656 first drafts. With the language lint, 118 drafts were regenerated once.
  - In port mode, the checker failed 18 of 208 drafts; with the lint, 26 were regenerated once.
  - Typical failures were details more specific than the source ("rooftop snipers", "battery-backed"), facts tied to another version, or an unsupported mechanic.
- **The checker's limits:** it compares the text with the supplied sources, not with the games themselves. An error inside a source would pass. This is why the human review sample matters.

## 9. Editorial-quality findings

- **Agent review of the 53-game stratified sample** (`REVIEW-SAMPLE.md`, `REVIEW-VERDICTS.md`):
  - 42 accepted as is, 6 accepted with a note, 2 grammar-only corrections, 3 exceptions confirmed.
  - No factual, identity, platform or neutrality error found.
  - The strata are: both Tetris releases; other same-title releases; unlicensed games; sports, RPG, puzzle, shooter, racing and platformer games; games with official text; compilations and dedicated sections; regenerated, short and missing records.
- **Fixed during the work:**
  - **Port details:** arcade or computer details in ports (port mode, above).
  - **Length:** 63 overviews above 150 words were trimmed.
  - **Re-release sentences:** removed from 5 official excerpts: *Duck Hunt*, *Hogan's Alley* and *Wild Gunman* (Wii Remote), *Final Fantasy* (Virtual Console import note) and *Crash 'n' the Boys*.
  - **Excerpt splitting:** a sentence split at "Dr." (Mega Man 2), and a missing space after a sentence end in page markup.
  - **Grammar:** "a adequate", "free children".
- **Repetition:** the most common two-word opening ("Players control") starts 22 of 651 overviews (3.4%); openings rotate across five instructed styles.
- **Official texts keep their own voice.** They are promotional ("one of the most popular video games ever made!"), as published by Nintendo. Neutrality applies to the overview only, as the prompt specifies.

## 10. Records requiring manual review

All lists are in `EDITORIAL-EXCEPTIONS.md`.

- **28 editorial-review records**, with no overview shown:
  - 23 where the model reported that the sources did not describe the NES game. Examples: *R.B.I. Baseball*, *James Bond Jr*, *Lethal Weapon*; their articles describe other versions or the license.
  - 5 whose second draft still had a problem: *John Elway's Quarterback* (an unsupported claim), *Teenage Mutant Ninja Turtles III* (161 words), *Bram Stoker's Dracula* (a source mention), *Faria* and *WWF WrestleMania* (one evaluative word each).
- **94 games with no usable source:** mostly game shows (*Jeopardy!*, *Wheel of Fortune*), educational titles (*Sesame Street*, *Fisher-Price*) and small unlicensed releases. A future source could fill these. Nothing was invented.
- **94 short overviews** (40–89 words): accurate but brief.
- **Owner human review of the 53-game sample is still open.** The verdicts above are the agent's. Six notes are flagged for the owner: Snow Brothers' timeout hazard, Faxanadu's official page, Touchdown Fever's unusual rules detail, and three short or stiff texts (Starship Hector, Cheetahmen II, Ultimate Basketball). The Bubble Bath Babes content note sits with its grammar correction.

## 11. Website implementation

- **Game Details** (`build_html.py` `descSections`) now opens, after the title and tags, with two labeled sections: **Official description**, then **Game overview**. Paragraphs are separated, and a missing text shows "No verified official description available." or "No game overview available.".
- **No links, sources, citations or icons.**
- **The former "Overview" block is renamed "Game facts".** It no longer carries a Description line, except for personal games, whose own description is still listed there.
- **Unchanged:**
  - The cards, list rows, filters, selection, favorites, folders and the Studio.
  - Search: it matches titles only. A test removes every description and confirms identical results.
- **Phone width:** the Details panel's narrow-screen column now has `minmax(0,1fr)`, so long text and IDs wrap at 390 px. This fixes an existing horizontal overflow that the new test exposed.
- **Pages output:** `build_site_ci.py` and the public build copy `descriptions.json` into the site data.

## 12. HOME implementation

**Model (`home-model.js`)**
- New functions: `homeDescriptions(g)` (texts only), `homeOverviewPreview(g, max)` (the first paragraph, cut at a word boundary with "…"), and the two missing-text messages.

**Game Details, all four themes (`home-themes.js`, `home.css`)**
- One scrollable box with small uppercase headings "Official description" and "Game overview", in each theme's own fonts and frame.
- The box fills the column below the buttons. The renderer sizes it, so the title, version, metadata and buttons do not move: the visual gate shows them pixel-identical.

**Controller (`home-state.js`, `tv.js`)**
- The path is Play → Favorite → text box, using DOWN.
- DOWN scrolls four lines at a time. The renderer measures the overflow (`homeDescMax`), so scrolling stops exactly at the last line; it used to scroll without limit.
- UP scrolls back. From the top, UP returns to Favorite.
- Play, Favorite and B (return to the same game) are unchanged.

**Previews**
- **Box Art Showcase:** the previously empty line area under the title shows two lines of the overview. The carousel geometry is unchanged.
- **Adventure Map:** two lines in the info panel, inside its frame.
- **Arcade Grid and Featured Shelf:** no new text outside Details. All their browsing screens are pixel-identical in the gate.

**Not changed:** artwork, hero images, title positions and long-title scrolling, Favorites/History sync, repeat timing, navigation, the native console runtime. No boot or launcher work.

## 13. Responsive and controller accessibility

**Website**
- At 390 px, both sections wrap inside the panel with no horizontal page or panel scrolling (tested).
- HTML in a text is shown as text and never runs (tested on the website and in HOME).

**HOME, tested with real key events in all four themes**
- Games with both texts, without an official text, and the longest texts.
- Results:
  - The text box is reachable.
  - It scrolls to the exact end and no further.
  - The last line is visible at the end, and the top aligns after scrolling back.
  - Focus returns to Favorite, then Play.
  - Favorite toggles; Play launches; B returns to the same game.
- The previews stay within two lines and inside their frames. The visual state stays consistent.

## 14. Performance findings

**Timing:**
- Website Details opens in 0.5 ms on average (1.0 ms worst, 40 opens).
- HOME Details draws in 4.7 ms on average (5.7 ms worst, 20 draws).
- Previews use the first paragraph only.

**Size:**
- Page: the generated Pages page grows from 5,231,713 to 5,822,428 bytes (+590,715, +11%), or +217,975 bytes gzip (822,627 to 1,040,602); almost all of it is the embedded description text.
- The description data is embedded: 581k characters (215 KB gzip for the data file).
- No runtime fetch, no network call, no model call.

**Security:** no API key, model endpoint or source endpoint appears in any client asset. This is tested on the workspace page and on the generated Pages site.

## 15. Automated test results

37 of 37 suites passed, 0 failed, 0 blocked, 0 not executed (2944 checks; 2942 passed, the other 2 are the unsupported Windows-only check).

**New suites:**
- `descriptions-model` (node, 22 checks): model fields, texts only, preview cut, scroll clamp, controller path.
- `descriptions-data` (37 checks, workspace): the public file's shape and safety; neutral language; US spelling; uniqueness; opening variety; traceability of every text to a verified private record; catalog unchanged; identity rules; Tetris distinct; no key in client assets.
- `descriptions-presentation` (194 checks, browser): the website and the four themes as described above, including phone width and performance.
- `pages-site-descriptions`: the data suite on the generated GitHub Pages site.

| Suite | Status | Checks | Passed | Failed | Notes |
|---|---|---|---|---|---|
| home-engine | PASSED | 164 | 164 | 0 |  |
| home-m25-navigation | PASSED | 305 | 305 | 0 |  |
| home-multitab | PASSED | 21 | 21 | 0 |  |
| home-favorites-tabs | PASSED | 29 | 29 | 0 |  |
| home-time-zones | PASSED | 39 | 39 | 0 |  |
| descriptions-model | PASSED | 22 | 22 | 0 | new |
| descriptions-data | PASSED | 37 | 37 | 0 | new |
| descriptions-presentation | PASSED | 194 | 194 | 0 | new |
| home-m1-regressions | PASSED | 111 | 111 | 0 |  |
| home-controller | PASSED | 715 | 715 | 0 |  |
| home-repeat | PASSED | 35 | 35 | 0 |  |
| home-persistence | PASSED | 55 | 55 | 0 |  |
| tv-preview | PASSED | 102 | 102 | 0 |  |
| tv-search | PASSED | 21 | 21 | 0 |  |
| catalog | PASSED | 185 | 185 | 0 | 178 assertions + 7 informational; Details check now requires the two sections |
| studio-page | PASSED | 52 | 52 | 0 |  |
| install-journey | PASSED | 23 | 23 | 0 |  |
| home-api | PASSED | 22 | 22 | 0 |  |
| home-sync | PASSED | 14 | 14 | 0 |  |
| folder-art | PASSED | 9 | 9 | 0 |  |
| console-engine | PASSED | 69 | 69 | 0 |  |
| console-api | PASSED | 30 | 30 | 0 |  |
| manifest-v4 | PASSED | 37 | 37 | 0 |  |
| manifest-v4-pure | PASSED | 17 | 17 | 0 |  |
| storage-states | PASSED | 22 | 22 | 0 |  |
| genre-overrides | PASSED | 7 | 7 | 0 |  |
| hero-guard | PASSED | 5 | 5 | 0 |  |
| hero-artwork-integrity | PASSED | 102 | 102 | 0 |  |
| artwork-integrity | PASSED | 10 | 10 | 0 |  |
| visual-regression | PASSED | 48 | 48 | 0 | screens, against the documented new baseline |
| pages-site-home-assets | PASSED | 17 | 17 | 0 |  |
| pages-site-catalog | PASSED | 185 | 185 | 0 | 178 assertions + 7 informational; Details check now requires the two sections |
| pages-site-descriptions | PASSED | 23 | 23 | 0 | new |
| public-site | PASSED | 31 | 31 | 0 |  |
| console-simulator-windows | PASSED | 62 | 62 | 0 |  |
| console-simulator-linux | PASSED | 62 | 61 | 0 | 1 unsupported on this path (Windows-only check) |
| console-simulator-arm | PASSED | 62 | 61 | 0 | 1 unsupported on this path (Windows-only check) |
| **Total** | **37 passed, 0 failed, 0 blocked, 0 not executed** | 2944 | 2942 | 0 | |

- **Skipped:** none.
- **Unsupported:** 2 (the Windows-only port check on the Linux and ARM simulator paths).
- **Known defects counted as passes:** none.

Run directory: `<local work folder>\logs\descriptions\verification\20261014-155349`.

**The first two full runs each ended 34 passed, 2 failed, 1 blocked.** Their logs are kept in `<local work folder>\logs\descriptions\verification-run1` and `verification-run2`. The causes were found and fixed before the final run:
- **`catalog`:** the check `6_details_fields` required a "Description" row in Details, which this work replaces with the two sections. The assertion now requires the two labeled sections in order, plus every other field. That is stricter than before.
- **`pages-site-home-assets` and `pages-site-descriptions`:** the local GitHub Pages build could not delete its previous output, so the generated-site suites were blocked or failed.
  - **First, an empty, read-only `art/hero/candidates` folder** was in the local build trees. Its three empty copies were removed (the private workspace folder is untouched).
  - **The second full run then failed the same way on 110 other empty, read-only folders.** The public build copies the private art folder skeleton, whose folders are read-only (pinned), and prunes only the files. Those empty folders flow through `site-data` into the Pages output.
  - **Root cause:** `shutil.rmtree` cannot delete read-only folders on Windows. Every second local Pages build therefore failed; M2.6 passed only because `pages-site` did not exist yet. Git does not track empty folders, so CI is not affected.
  - **Fix:** `build_site_ci.py` now clears the read-only flag and retries when it deletes its own previous output. The failure was reproduced in isolation before the fix. After the fix, two consecutive builds over the read-only output succeed.
  - The second run's logs are kept in `verification-run2`.

**Deliberate-failure proofs:** 52 of 52 behave as intended: 50 in the full run, and 2 rerun individually. `descmodel-preview-mid-word` was rerun after its test was strengthened: the original fixed-length check could pass a mid-word cut, and it now tests every cut length from 150 to 170. `m25-map-results-up-to-hidden-tabs` was blocked by one slow tap during a concurrent run and behaved on an idle rerun. The proofs include 15 new description proofs, each with a clean control: 4 model, 5 data and 6 browser (official text hidden, HTML not escaped, unbounded scroll, Box Art preview not clamped, Map preview missing). Logs: `evidence/proofs/`.

## 16. Visual comparison results

**Before replacing the baseline**, the protected HOME visual gate against the f97adba baseline:
- 34 of 48 screens unchanged (largest difference 5, which is noise).
- 14 screens changed, all intentional content additions:
  - Game Details in each theme (4);
  - Box Art Showcase All Games, folder, recent, navigation and START-menu screens (5), which show the preview;
  - the same five on Adventure Map.

**Scope proof:** `desc_visual_scope.py` reproduced each changed screen and measured the description elements' boxes from the DOM. **Every changed pixel lies inside them** (14 of 14; result in `evidence/visual-scope.json`). A first attempt showed a 4 px shift of the Details metadata and buttons, caused by a flex layout. It was fixed (renderer sizing) before acceptance.

**The baseline was then replaced explicitly**, with this reason recorded in its manifest. The previous baseline is kept at `<local work folder>\home-visual\baseline.replaced-20261014-144731`. The gate now passes 48 of 48.

**Before/after comparisons** are in `screenshots/before-after/`: the website Details, the four HOME Details screens, and the Box Art and Map screens. "Before" is the f97adba Pages build; "after" is this build.

## 17. Remaining limitations

- **Official coverage is low (96 games).** No other verified official source was practical. Manuals and box text exist for many games, but no authentic, verifiable digital copy was available to cite.
- **122 games have no overview** (94 without sources, 28 held back). They show the missing message.
- **The claim checker only verifies against the supplied sources.** An encyclopedia error, or an arcade detail that also exists in the NES port, can pass. The owner's sample review and the six notes are the safeguard.
- **Port-mode overviews (208) are deliberately general.**
- **Official texts are Nintendo of Europe's English copy** with US spelling applied. Wording otherwise follows the European page, for example "football" in *Soccer*.
- **A pre-existing `SyntaxWarning`** (invalid escape `\/` in a `build_html.py` string, unchanged since f97adba) is printed when the file is compiled fresh. It is harmless and left as is.

## 18. Exact files modified

**Product (public tree):**
- `tools/rom-pipeline/build_html.py`: data embedding, `descSections`, Game facts, Details column fix.
- `tools/rom-pipeline/studio/home-model.js`: `homeDescriptions`, `homeOverviewPreview`, messages.
- `tools/rom-pipeline/studio/home-state.js`: details focus with descriptions, scroll clamp, `homeDescSteps`.
- `tools/rom-pipeline/studio/home-themes.js`: `hmDescSections`, two-section Details, `hmDescFit`/`homeDescMax`, Box Art and Map previews.
- `tools/rom-pipeline/studio/home.css`: section headings, previews, Featured Shelf heading size.
- `tools/rom-pipeline/studio/tv.js`: passes the measured scroll limit.
- `tools/publish/build_public_repo.py`: `descriptions.json` in the site data; test allow-list.
- `tools/publish/build_site_ci.py`: copies `descriptions.json`; a read-only-safe deletion of its previous output.
- `site-data/descriptions.json` (new, generated).

**Tests (public tree):**
- `tools/rom-pipeline/test_descriptions_data.py` (new)
- `tools/rom-pipeline/test_home_descriptions.py` (new)
- `tools/rom-pipeline/test_home_descriptions.js` (new)
- `tools/rom-pipeline/test_public_site.py`: requires `descriptions.json`.
- `tools/rom-pipeline/test_catalog_html.py`: Details check updated to the two sections.
- `tools/rom-pipeline/m2_failure_proofs.py`: description proofs.
- `tools/run_verification.py`: 4 new suites.

**Development only (workspace, not public):**
- `tools/rom-pipeline/game_descriptions.py`: research, generation, verification, ledger, assembly, export.
- `tools/rom-pipeline/desc_report.py`: reports.
- `tools/rom-pipeline/desc_visual_scope.py`: visual scope check.
- `tools/rom-pipeline/shot_descriptions.py`: screenshots.

**Data and reports:**
- `reports/descriptions.json`
- `reports/game-descriptions/*`
- `docs/execution-log.md`, `docs/phase-status.md`

## 19. Final commit SHA

`36109aa` on `home-visual-remediation` in the public dev tree (`<local public dev tree>`). It is not pushed.

## Deliverables in this folder

- `game-descriptions.json`: the complete machine-readable dataset (private).
- `coverage.json`
- `../descriptions.json`: the public runtime file.
- `OFFICIAL-SOURCE-PROVENANCE.md`
- `EDITORIAL-EXCEPTIONS.md`
- `IDENTITY-RULES.md`
- `REVIEW-SAMPLE.md`, `REVIEW-VERDICTS.md`, `review-sample.json`
- `GEMINI-LEDGER.md`
- `screenshots/`: website, HOME, before/after.
- `evidence/`: the verification summary, proofs and the visual scope.
- `CHATGPT-SUMMARY.md`

**Review archive:** `<local work folder>\logs\descriptions\description-enrichment-review.zip`. It has no API key, no source caches and no third-party documents.
