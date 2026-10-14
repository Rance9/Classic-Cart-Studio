# Classic Cart Studio: game description enrichment summary

Commit `36109aa` on `home-visual-remediation` (parent `f97adba`). It is not pushed, merged or deployed. Gate G1 is closed. No artwork, ROM, catalog record, HOME design or console runtime changed.

## What was built

All 773 games in the website's default catalog now have two description slots, shown in the website's Game Details and in Game Details of all four Preview on TV HOME themes:

- **Official description:** verified official text only, never written by a model.
  - Source: Nintendo's official NES game pages, as short verbatim excerpts (US spelling applied).
  - Found for **96** games. The other 677 show "No verified official description available."
- **Game overview:** a neutral, factual overview (premise, controls, objective, structure, modes, players).
  - Written by Gemini only from supplied sources: the official text and the game's verified encyclopedia article.
  - Each one was then checked claim by claim by a second Gemini pass.
  - Shown for **651** games. The other 122 show "No game overview available." (94 have no usable source; 28 are held for editorial review). Nothing was invented to fill gaps.

Per the owner's addendum, **no sources, links or citations** appear anywhere in the product. The public data file holds only the two texts per game. Full provenance is kept in private records.

## How it appears

- **Website Details:** two sections at the top of the panel. Search still matches titles only.
- **HOME Details (all four themes):**
  - One scrollable box with the two labeled sections.
  - The controller path is Play → Favorite → text. Scrolling stops at the last line.
  - Title, metadata, buttons, Favorite, Play and B are unchanged.
- **Box Art Showcase:** a two-line overview preview under the title.
- **Adventure Map:** a two-line preview in the info panel.
- **Arcade Grid and Featured Shelf:** no new text outside Details.

## Quality controls

- **Identity:**
  - Nintendo's and Tengen's *Tetris* use separate sources and texts.
  - Only releases of the same NES game share sources.
  - For 208 ports of arcade or computer games, overviews keep core gameplay only, so details of the original version cannot slip in.
- **Checks:**
  - Every displayed overview passed the claim check and a language check: no praise or marketing, no "you", no other platforms, US spelling.
  - 61 long overviews were shortened and checked again.
- **Review:** the agent reviewed a 53-game stratified sample. There were no factual errors; there were two grammar fixes and six notes for the owner. **The owner's own review of that sample is still open.**

## Cost

**$2.76** of the $25 cap: 2,168 requests to `gemini-3.8-flash`, almost all through the Batch API. Every batch was estimated and reserved before upload. There were no retries and no unresolved reservations.

## Verification

- **Standard run:** 37 of 37 suites passed, 0 failed, 0 blocked, 0 not executed (2944 checks; 2942 passed, the other 2 are the unsupported Windows-only check).
- **Deliberate-failure proofs:** 52 of 52 behave as intended: 50 in the full run, and 2 rerun individually. `descmodel-preview-mid-word` was rerun after its test was strengthened: the original fixed-length check could pass a mid-word cut, and it now tests every cut length from 150 to 170. `m25-map-results-up-to-hidden-tabs` was blocked by one slow tap during a concurrent run and behaved on an idle rerun. The proofs include 15 new description proofs, each with a clean control: 4 model, 5 data and 6 browser (official text hidden, HTML not escaped, unbounded scroll, Box Art preview not clamped, Map preview missing). Logs: `evidence/proofs/`.
- **Visual gate:**
  - 14 screens changed intentionally (Details in 4 themes; Box Art and Map preview screens).
  - A DOM-measured check proved every changed pixel lies inside the description areas.
  - The baseline was replaced with a recorded reason (the old one is kept). The gate now passes 48 of 48.
- **Scans:** privacy scans are clean, and there is no API key in any client asset.

## Open items for the owner

1. Review the 53-game sample (`REVIEW-SAMPLE.md`, `REVIEW-VERDICTS.md`), especially the six notes.
2. Decide whether more official sources (manual or box text) should be pursued; only 96 games have one.
3. The 122 games without an overview are listed with reasons in `EDITORIAL-EXCEPTIONS.md`.
