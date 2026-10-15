# Milestone 3.2: NES description research, accuracy corrections and editorial completion

**Branch:** `home-visual-remediation`, commit `26ea74a` (parent `36109aa`).
**Status:** not pushed, merged or deployed. Gate G1 is closed. No ROM, artwork, layout, HOME design or console work.

## A. Executive summary

The first campaign took its facts from Nintendo of Europe's pages and encyclopedia articles. M3.2 adds the strongest NES-specific evidence available: the **original US NES instruction manuals**. The archive.org "NESManuals" collection holds 784 scanned US manuals with OCR text, matched to the catalog by No-Intro name. It also adds targeted web research for games without a usable manual.

With that evidence:

1. **The six flagged overviews have dispositions.** The manual showed Snow Brothers' power-up details were arcade facts, and they are now corrected. The Touchdown Fever clause is removed. Three are retained. Cheetahmen II got a small wording polish.
2. **All five narrowly rejected drafts are accepted.** Three needed a wording or length edit; two were rebuilt from their NES manuals.
3. **115 of the 117 missing overviews are recovered.** Each was written only from NES-specific evidence and checked claim by claim. The two left unresolved have specific, documented reasons.
4. **An independent manual audit covered 391 accepted overviews** at risk of other-version content. It found 203 claims in 158 records that the NES manual contradicts; examples include Ikari Warriors' heroes (Paul and Vince), Bubble Bobble's level count, Toki's player count and Super Sprint's laps. 156 records were corrected (12 by hand); the other two are Snow Brothers and Touchdown Fever, already handled as flagged records.
5. **A deterministic name check** made 9 hand corrections where a model had turned garbled manual OCR into names that appear in no source.
6. **Official descriptions** rose from 96 to 101, using original manual text and one developer page.

**Coverage:** Game overviews 651 → **771 of 773** (99.7%); Official descriptions 96 → **101**.
**Cost:** $2.46, for a cumulative **$5.22 of the $25 cap**.
**Product code:** no presentation code changed. The public runtime file changed, and the data checks were extended.

## B. Starting coverage (commit `36109aa`)

773 default games; 96 official descriptions; 651 Game overviews; 122 without an overview. Of those 122, 94 had no usable source, 23 had sources that did not describe the NES game, and 5 drafts were rejected for narrow problems.

## C. Research-source expansion

**Original NES instruction manuals** (archive.org `NESManuals`):
- Matching: by normalized No-Intro name, preferring the `(USA)` manual. The 124 targets (117 missing records, the five rejected, Snow Brothers and Touchdown Fever) gave 113 matches with readable OCR; the audit set added 385 manuals.
- Download: only matched OCR text files (`_djvu.txt`), each hashed (`manuals-index.json`, private).
- Cleaning: legal, warranty and safety text removed. The model was told to ignore advertisements for other games that manuals often contain (the Beetlejuice manual carries a Bill & Ted ad).

**Identity (Layer A):**
- The manual file is the US release of the catalog record.
- Two catalog R.B.I. Baseball records share Tengen's manual (same game, licensed and unlicensed cartridges); their texts are separate.
- "King of Kings, The" is the manual of *The Early Years*.
- BMX Simulator was **rejected**: its sources describe the Quattro Sports cartridge, not the standalone release.

**Agent research for the 11 games without a matched manual:**
- **Sources:** scanned manuals of the same or compilation releases, read page by page; BootlegGames Wiki for unlicensed multicarts; NES database pages; the developer's own page (Wonderland Dizzy); encyclopedia articles where they discuss the NES release.
- **Rules:** each source was opened in full (never a search snippet), checked for the NES release, and recorded with facts, conflicts and an identity note.

**Web confirmation** of two name sets the manual OCR could not show: the Defenders of Dynatron City heroes and the King of Kings settings (encyclopedia articles on the NES games).

## D. The six flagged descriptions

| Game | ID | Disposition |
|---|---|---|
| Snow Brothers | `fbd7caadaf77c2b1` | **Corrected.** The NES manual contradicts the arcade-derived details. Power-ups are hot sauce jars lasting until a life is lost: red adds speed, blue larger globs, green throws farther; there is no yellow. Separate jars give an invincible flying giant or a treasure-room warp. The Flying Pumpkin Head is an ordinary chaser, with no timeout trigger and no ghosts. Paragraph 2 was rewritten from the manual, and paragraph 1 lost "kicked" and "triggering other snowballs". |
| Touchdown Fever | `115fcca17cb77d7c` | **Corrected.** The horse-collar clause is removed. The remaining claims were checked against the NES manual (12 teams, plays, kicking, two-point conversions) and the encyclopedia's NES Version section (field-goal range). |
| Faxanadu | `58f2ba801354a8e5` | **Retained as verified** (owner decision: the Virtual Console page is official text). |
| Starship Hector | `7b7ccdcda705c2c3` | **Retained as verified** (owner decision; not padded). |
| Cheetahmen II | `ebdd748cd488c4ac` | **Corrected (optional polish):** "manufactures" → "creates"; "the concluding adversary of the experience" → "the final boss". Facts unchanged. |
| Ultimate Basketball | `bd6797920755a1dc` | **Retained as verified** (owner decision). |

Each correction was re-checked claim by claim against its NES manual plus the original sources, and all pass. The before and after texts are in `M3-2-EDITORIAL-CORRECTIONS.md`.

## E. The five previously rejected records

| Game | Problem at `36109aa` | Resolution | Status |
|---|---|---|---|
| John Elway's Quarterback | unsupported receiver-sprint claim | The arcade lineage and an unconfirmed mechanic made a patch unsafe, so the overview was **rebuilt from the NES manual** (play-calling, cursor passing, tackles, linebacker control) | accepted (checked) |
| Teenage Mutant Ninja Turtles III | 161 words | **Trimmed** to 147 words (two minor details removed) | accepted (checked) |
| Bram Stoker's Dracula | source mention; thin trivia | **Rebuilt from the NES manual** (Harker, Mina, weapon boxes, Dracula's forms) | accepted (checked) |
| Faria | "expansive" | **Word removed** | accepted (checked) |
| WWF WrestleMania | "unique" | **Sentence removed**: its health-item claim is not in the NES manual. The other claims match the manual. | accepted (checked) |

## F. Missing-overview recovery

Every one of the original 122 records has its own row in `M3-2-RECOVERED-GAMES.md`: original reason, new evidence, disposition, acceptance and limitation.

- **Recovered: 115 of 117.**
  - 105 written from the game's own NES manual; 10 from researched NES facts.
  - The two rebuilt drafts (Elway, Dracula) count in E.
  - 83 passed the claim check on the first pass. 32 needed one regeneration for length, an evaluative word or an unsupported claim; their unsupported claims included Super Spy Hunter's lives, Magic Darts' Round the Clock rule and Secret Scout's capture. Of those 32, 3 then needed a deterministic wording or length edit after a passing draft.
  - 6 recovered texts received a name-check edit.
- **Unresolved: 2.**
  - **BMX Simulator:** ambiguous release identification.
  - **Caesars Palace:** the manual OCR is unreadable, and the model provider's content filter blocked generation twice.
- **Categories used for the 2 unresolved:** "Ambiguous release identification" and "Inadequate source detail". "No Wikipedia article" is no longer used as a reason.

## G. Official-description expansion

**101 verified (+5).** The new ones are World Champ, Wheel of Fortune Featuring Vanna White, Ren & Stimpy: Buckeroo$! and Chris Evert & Ivan Lendl in Top Players' Tennis (original NES manual scans, read page by page), plus Wonderland Dizzy (the developer's own page).
- All are verbatim. World Champ uses only one contiguous sentence, and one OCR normalization is recorded.
- Two candidates were declined (identity).
- A systematic pass over manual story text was not attempted. The OCR is too noisy to quote verbatim without reading each scan by hand, and the milestone ranks official text third. See `M3-2-OFFICIAL-COVERAGE.md`.

## H. Editorial changes to previously accepted overviews

**159 previously accepted overviews changed**, each with its previous text kept and its reason and evidence recorded (`M3-2-EDITORIAL-CORRECTIONS.md`):
- **Flagged:** 3 (Snow Brothers, Touchdown Fever, Cheetahmen II).
- **Audit, automated correction:** 141, each checked against the NES manual plus the original sources.
- **Audit, corrected by hand:** 12, where the automated correction failed its check: Star Wars: The Empire Strikes Back, Vice: Project Doom, Jurassic Park, Terra Cresta, Lode Runner, Arkanoid, Cybernoid, Bump 'n' Jump, Super Sprint, Toki, Ghosts'n Goblins and Last Action Hero.
  - Where sources conflicted, nothing was guessed. The disputed detail was generalized: the Arkanoid round count, the Bump 'n' Jump jump speed, the Empire Strikes Back ending.
  - The audit itself was wrong once (Vice's gears; the manual says three), and the text stayed as it was.
- **Name-check edits:** 3 on audit corrections (Castelian, Image Fight, Hudson Hawk).

## I. Verification methodology

| Layer | How it was applied |
|---|---|
| A. Identity | Manual matched to the catalog's US release by No-Intro name; research sources checked for platform and release; the ambiguous release (BMX Simulator) was rejected. |
| B. Claim to source | Each overview was split into claims by a second Gemini pass and judged against the supplied evidence. This covers all 115 recoveries, all 141 automated audit corrections, and all 30 texts written or edited by hand. |
| C. NES version | The NES manual is the version-specific source. The 391-record audit classified 3,466 claims as confirmed (2,269), not in the manual (994) or contradicted (203). |
| D. Neutrality and readability | A deterministic lint covers evaluative words, exclamation marks, second person, markup, other platforms, source mentions, US spelling and length. Hand edits removed residual words such as "unique", "great" and "beloved". |
| E. Contradictions | Conflicts were investigated in the manual, not settled by the fuller text. The agent did this for Vice, Arkanoid, Bump 'n' Jump, the Star Wars ending and Last Action Hero; the audit's misreading of Vice was overruled. The **Adventure Island** regional conflict is documented for the owner. |
| F. Final editorial review | The agent read all 117 recovered and rebuilt texts and a 62-game stratified sample. A deterministic **name check** flagged every proper name absent from the record's sources, leading to 9 hand corrections. Grammar sweep: "four or matching" was fixed. |

**A weakness found and fixed:** the claim checker was told to tolerate OCR letter errors. That let a model's "reading" of garbled OCR pass as a name ("Anobian", "Booglie", "Thundersanrior"). The name check now catches these, and a data-suite regression check keeps them out.

## J. Editorial quality review

`M3-2-REVIEW-SAMPLE.md` (62 games, stratified) and `M3-2-REVIEW-VERDICTS.md`:
- **Verdicts:** 51 accepted as is, 9 accepted with a note, 2 unresolved and correctly unpublished, 0 rejected.
- **Strata:**
  - both corrected flagged games and the five rejected records;
  - new official texts;
  - recoveries: game shows, educational, casino and tabletop, sports, compilations, unlicensed, obscure licensed, limited evidence, researched facts;
  - audit corrections of arcade ports, computer ports and hand-corrected records;
  - regional differences, short texts and the unresolved records.

**Owner items:**
- the Adventure Island regional naming;
- a spot-check of Racermate Challenge II and Super Gun (thin, non-manual evidence);
- the official excerpt chosen for Ren & Stimpy (in-character dialogue).

## K. Updated coverage

| | Before (`36109aa`) | After |
|---|---|---|
| Default games | 773 | 773 |
| Official descriptions | 96 | **101** |
| Game overviews | 651 | **771** (99.7%) |
| No overview | 122 | **2** |
| Neither text | 122 | 2 |

The public runtime file `site-data/descriptions.json` (`ccs-descriptions/1`, texts only) now holds 771 records, 712,657 bytes. Every record without an overview has a category and a specific reason in the private dataset.

## L. Gemini costs

| | |
|---|---|
| Previous campaign (ledger, from usage metadata) | $2.7604 |
| M3.2 requests | 1,037 in 12 batch jobs (`gemini-3.8-flash`, Batch API, text only) |
| M3.2 tokens | 4,115,287 input; 491,294 output |
| M3.2 charges (from usage metadata) | **$2.4643** |
| **Cumulative** | **$5.2247 of $25.00** |
| Remaining authorized | $19.7753 |

**Spending controls:**
- Every batch was reserved at its worst case before upload, against the same ledger and the same $23.50 stop.
- Stages never re-sent a completed request. One unreadable response (Caesars Palace) got its single allowed retry; it was blocked again and not retried.
- No auto-reload or limit change; the API key appears in no file.

**Billing:** reconciliation used the per-request usage metadata recorded in the ledger. Google's billing console cannot be read with an API key, so the account-side total was not independently confirmed.

## M. Tests

37 of 37 suites passed, 0 failed, 0 blocked, 0 not executed (2948 checks; 2946 passed, the other 2 are the unsupported Windows-only check).

| Suite | Status | Checks | Passed | Failed |
|---|---|---|---|---|
| home-engine | PASSED | 164 | 164 | 0 |
| home-m25-navigation | PASSED | 305 | 305 | 0 |
| home-multitab | PASSED | 21 | 21 | 0 |
| home-favorites-tabs | PASSED | 29 | 29 | 0 |
| home-time-zones | PASSED | 39 | 39 | 0 |
| descriptions-model | PASSED | 22 | 22 | 0 |
| descriptions-data | PASSED | 40 | 40 | 0 |
| descriptions-presentation | PASSED | 194 | 194 | 0 |
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
| pages-site-descriptions | PASSED | 24 | 24 | 0 |
| public-site | PASSED | 31 | 31 | 0 |
| console-simulator-windows | PASSED | 62 | 62 | 0 |
| console-simulator-linux | PASSED | 62 | 61 | 0 |
| console-simulator-arm | PASSED | 62 | 61 | 0 |
| **Total** | **37 passed, 0 failed, 0 blocked, 0 not executed** | 2948 | 2946 | 0 |

- **Skipped:** none.
- **Unsupported:** 2 (the Windows-only check on the Linux and ARM simulator paths).
- **Known defects counted as passes:** none.

Run directory: `<local work folder>\logs\descriptions-m32\verification\20261014-190551`.

**New or changed checks (data suite, 40 checks):**
- **M3.2 provenance:** official texts from manuals or the developer's page need full source records; M3.2 overviews need their own passing claim check and evidence.
- **Corrections keep their previous text.**
- **Unresolved records are classified,** each with an allowed category and a specific reason.
- **Corrected claims stay out:** Snow Brothers' arcade power-ups, the horse-collar clause, Arkanoid's count, Toki's two players, Super Sprint's helicopter, Lode Runner's computer figures, the invented names. This check also runs on the generated Pages site.

**Failing-first evidence:** against the published `36109aa` data, the regression check fails. It finds Snow Brothers' "yellow", "pumpkin" and "potion", Touchdown Fever's "horse-collar" and Arkanoid's "36 stages". It passes on the M3.2 data.

**Deliberate-failure proofs:** 17 of 17 description proofs behave as intended (model 4, data 6 including the new `descdata-arcade-detail-returns`, browser 6, plus the leftover-browser check). The other proofs cover code M3.2 did not change; their last full run, at `36109aa`, was 52 of 52.

## N. Visual integrity

The protected HOME visual gate passes **48 of 48 screens unchanged** (largest difference 5, which is noise) against the existing baseline. None of the gate's fixed screens shows a game whose text changed, so **no rebaseline was needed**.

Description changes stay inside the existing text areas. The presentation suite (194 checks, all four themes) verifies:
- controller scrolling to the last line;
- previews within two lines and inside their frames;
- the phone-width website layout.

No art, title position, metadata, button or focus visual changed, and no rendering code changed.

## O. Remaining exceptions and limitations

- **Unresolved:**
  - **BMX Simulator** (`85f7be325689329e`): ambiguous release. Next action: confirm what the standalone release is; if it is the Quattro Sports game, write it from that manual's BMX section.
  - **Caesars Palace** (`cfea236e0d0fe0e4`): unreadable OCR and a provider content block. Next action: a legible scan, with the overview written by hand.
- **Owner decisions and spot-checks:**
  - Adventure Island (Leilani versus Tina).
  - Racermate Challenge II and Super Gun (thin evidence).
- **Residual audit risk:**
  - 994 audited claims are "not in the manual". These are usually details a manual does not cover, not errors, and were left in place.
  - 145 audited records are rated medium risk on that basis.
  - The 260 accepted overviews outside the audit set (NES-native games) were not re-audited.
- **OCR dependence:** manual text is OCR, so names and numbers are only as good as the scan. The name check and the generalization of disputed numbers limit this; a misread number that the model and the checker both accepted cannot be ruled out entirely.
- **Model provider filter:** casino and gambling content can be blocked (Caesars Palace).

## P. Final commit

`26ea74a` on `home-visual-remediation` in the public dev tree (`<local public dev tree>`). It is not pushed.

**Files changed:**
- `site-data/descriptions.json` (content)
- `tools/rom-pipeline/test_descriptions_data.py` (M3.2 checks)
- `tools/rom-pipeline/m2_failure_proofs.py` (regression proof)

**Development only (workspace):**
- `tools/rom-pipeline/desc_m32.py`
- `tools/rom-pipeline/desc_report_m32.py`
- changes to `game_descriptions.py` (overrides in assembly; excerpt sentence handling)

**Private records:**
- `reports/game-descriptions/editorial-overrides.json`
- `reports/game-descriptions/game-descriptions.json`
- `<local work folder>\descriptions\m32\` (manuals, research, results)

## Review files

- `M3-2-EDITORIAL-COMPLETION-REPORT.md` (this file)
- `M3-2-CHATGPT-SUMMARY.md`
- `M3-2-RECOVERED-GAMES.md`
- `M3-2-UNRESOLVED-GAMES.md`
- `M3-2-EDITORIAL-CORRECTIONS.md`
- `M3-2-REVIEW-SAMPLE.md`
- `M3-2-REVIEW-VERDICTS.md`
- `M3-2-OFFICIAL-COVERAGE.md`

**Screenshots (`m3-2-screenshots/`):**
1. `01-website-both-descriptions.png` (Adventure Island)
2. `02-website-recovered-game.png` (Beetlejuice)
3. `03-website-official-unavailable.png` (Snow Brothers, corrected)
4. `04-box-art-showcase-overview.png`
5. `05-adventure-map-overview.png`
6. `06-home-details-longer-overview.png` (Wheel of Fortune Featuring Vanna White)
7. `07-home-controller-scroll-final-line.png`
