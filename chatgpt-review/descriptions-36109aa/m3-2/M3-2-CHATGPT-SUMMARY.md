# Classic Cart Studio M3.2: description research and editorial completion

Commit `26ea74a` on `home-visual-remediation` (parent `36109aa`). It is not pushed, merged or deployed. Gate G1 is closed. No interface, artwork, ROM or console change; only description content and its data checks changed.

## What changed

- **New evidence:** the original US NES instruction manuals (archive.org "NESManuals", OCR text), matched to the catalog by No-Intro name, plus targeted web research for 11 games without a manual.
- **Coverage:** Game overviews went from 651 to **771 of 773**; Official descriptions from 96 to **101**.
- **Flagged six:**
  - **Snow Brothers:** corrected. The NES manual shows the old power-up and pumpkin details were arcade facts.
  - **Touchdown Fever:** the horse-collar clause is removed.
  - **Faxanadu, Starship Hector, Ultimate Basketball:** retained.
  - **Cheetahmen II:** lightly polished.
- **Rejected five:** all accepted. Three were edited; Elway and Dracula were rebuilt from their manuals.
- **Recovered:** 115 of the 117 missing overviews, each written only from NES-specific evidence and checked claim by claim.
  - **Unresolved:** BMX Simulator (ambiguous release) and Caesars Palace (unreadable manual scan; the provider blocked generation).
- **Audit:** 391 accepted overviews at risk of other-version content were checked against their NES manuals.
  - **Contradictions:** 203, in 158 records.
  - **Corrected:** 156 records, 12 of them by hand where sources conflicted or the automated fix failed. Disputed numbers were generalized rather than guessed. The other two contradicted records were Snow Brothers and Touchdown Fever, already corrected as flagged games.
  - **Examples:** the Ikari Warriors heroes are Paul and Vince; Toki is single-player; Super Sprint races are five laps.
- **Name check:** every proper name must appear in the record's sources. It caught names a model had made from garbled OCR ("Anobian", "Booglie Galaxy", "Thundersanrior"); 9 were hand-corrected, and a regression check keeps them out.

## Verification

- **Standard run:** 37 of 37 suites passed, 0 failed, 0 blocked, 0 not executed (2948 checks; 2946 passed, the other 2 are the unsupported Windows-only check).
- **Deliberate-failure proofs:** 17 of 17 description proofs behave as intended (model 4, data 6 including the new `descdata-arcade-detail-returns`, browser 6, plus the leftover-browser check). The other proofs cover code M3.2 did not change; their last full run, at `36109aa`, was 52 of 52.
- **Failing-first:** the new data regression check fails on the `36109aa` data and passes now.
- **Visual gate:** 48 of 48 unchanged; no rebaseline.
- **Scans:** privacy scans clean; no API key in any client asset.

## Cost

**M3.2:** $2.46 for 1,037 requests. **Cumulative:** **$5.22 of $25**, leaving $19.78. Every batch was reserved before upload; no retry was needed beyond one blocked request.

## For the owner

1. **Adventure Island:** the official European text says the princess is Tina; the US manual, which the overview follows, says Leilani. Keep both, or adjust?
2. **Spot-check** Racermate Challenge II and Super Gun (thin, non-manual evidence).
3. **The 62-game review sample** (`M3-2-REVIEW-SAMPLE.md`, `M3-2-REVIEW-VERDICTS.md`) awaits your review.
