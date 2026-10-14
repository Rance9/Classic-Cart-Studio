# Description enrichment: review materials for commit 36109aa

**Purpose:** these files make the results of the Classic Cart Studio game description enrichment readable one file at a time, for an editorial and implementation review.

**This branch is temporary.**
- It is based on the public `main` branch (`ff887aa`) and adds only this folder.
- It contains no application code from the development branch, no artwork archives, ROMs, credentials, research databases or the description dataset.
- No deployment, release or merge is involved.

**Development baseline:** commit `36109aa` on the local development branch `home-visual-remediation`. It is not pushed.

## Results in brief

| | |
|---|---|
| Default catalog | **773** games |
| Verified official description | **96** games (short excerpts from Nintendo's official NES game pages); 677 show "No verified official description available." |
| Game overview shown | **651** games; 122 show "No game overview available." (94 with no usable source, 28 excluded after verification) |
| Gemini spending | **$2.76** of the $25.00 authorized (2,168 requests, `gemini-3.8-flash`, almost all Batch API) |
| Standard verification | 37 of 37 suites passed (2,944 checks) |
| Deliberate-failure proofs | 52 of 52 behaved as intended |
| HOME visual gate | 48 of 48 against a documented new baseline. 14 screens changed intentionally, and every changed pixel lies inside a description area. |

**Product rule (unchanged):** the website and HOME show no source links, citations or attribution next to either description. The source references inside these review files are for this editorial review only.

## Files

Base URL: `https://github.com/Rance9/Classic-Cart-Studio/blob/review/description-enrichment-36109aa/chatgpt-review/descriptions-36109aa/`

| File | Contents |
|---|---|
| [README.md](https://github.com/Rance9/Classic-Cart-Studio/blob/review/description-enrichment-36109aa/chatgpt-review/descriptions-36109aa/README.md) | This index |
| [DEFAULT-CATALOG-DESCRIPTION-ENRICHMENT-REPORT.md](https://github.com/Rance9/Classic-Cart-Studio/blob/review/description-enrichment-36109aa/chatgpt-review/descriptions-36109aa/DEFAULT-CATALOG-DESCRIPTION-ENRICHMENT-REPORT.md) | The full report (19 sections) |
| [CHATGPT-SUMMARY.md](https://github.com/Rance9/Classic-Cart-Studio/blob/review/description-enrichment-36109aa/chatgpt-review/descriptions-36109aa/CHATGPT-SUMMARY.md) | Short handoff summary |
| [REVIEW-SAMPLE.md](https://github.com/Rance9/Classic-Cart-Studio/blob/review/description-enrichment-36109aa/chatgpt-review/descriptions-36109aa/REVIEW-SAMPLE.md) | 53-game stratified sample: both texts as displayed, check results, research sources |
| [REVIEW-VERDICTS.md](https://github.com/Rance9/Classic-Cart-Studio/blob/review/description-enrichment-36109aa/chatgpt-review/descriptions-36109aa/REVIEW-VERDICTS.md) | The agent's verdict for each sampled game |
| [EDITORIAL-EXCEPTIONS.md](https://github.com/Rance9/Classic-Cart-Studio/blob/review/description-enrichment-36109aa/chatgpt-review/descriptions-36109aa/EDITORIAL-EXCEPTIONS.md) | Games without an overview and why; short overviews |
| [FLAGGED-DESCRIPTIONS.md](https://github.com/Rance9/Classic-Cart-Studio/blob/review/description-enrichment-36109aa/chatgpt-review/descriptions-36109aa/FLAGGED-DESCRIPTIONS.md) | The six flagged overviews: exact text, concern, claims, verification results, other-version risk, recommendation |
| [COVERAGE-AND-EXCEPTIONS.md](https://github.com/Rance9/Classic-Cart-Studio/blob/review/description-enrichment-36109aa/chatgpt-review/descriptions-36109aa/COVERAGE-AND-EXCEPTIONS.md) | Status of all 773 games; a row for each of the 122 without an overview; the 96 official texts; the full index |

## Screenshots

Captured from the generated GitHub Pages build of the development branch (offline, local), at original resolution. The implementation was not changed for them.

| File | Shows |
|---|---|
| [01-website-both-descriptions.png](https://github.com/Rance9/Classic-Cart-Studio/blob/review/description-enrichment-36109aa/chatgpt-review/descriptions-36109aa/screenshots/01-website-both-descriptions.png) | Website Game Details for Adventure Island: Official description, then Game overview |
| [02-website-official-unavailable.png](https://github.com/Rance9/Classic-Cart-Studio/blob/review/description-enrichment-36109aa/chatgpt-review/descriptions-36109aa/screenshots/02-website-official-unavailable.png) | Website Game Details for Contra: the missing-official message, with the overview |
| [03-box-art-showcase-overview.png](https://github.com/Rance9/Classic-Cart-Studio/blob/review/description-enrichment-36109aa/chatgpt-review/descriptions-36109aa/screenshots/03-box-art-showcase-overview.png) | Box Art Showcase: two-line overview preview under the title |
| [04-adventure-map-overview.png](https://github.com/Rance9/Classic-Cart-Studio/blob/review/description-enrichment-36109aa/chatgpt-review/descriptions-36109aa/screenshots/04-adventure-map-overview.png) | Adventure Map: two-line overview preview in the info panel |
| [05-home-details-both-descriptions.png](https://github.com/Rance9/Classic-Cart-Studio/blob/review/description-enrichment-36109aa/chatgpt-review/descriptions-36109aa/screenshots/05-home-details-both-descriptions.png) | HOME Game Details (Arcade Grid): both labeled sections in the scrollable box |
| [06-home-details-long-scroll.png](https://github.com/Rance9/Classic-Cart-Studio/blob/review/description-enrichment-36109aa/chatgpt-review/descriptions-36109aa/screenshots/06-home-details-long-scroll.png) | HOME Game Details (Box Art Showcase): the text box focused with the controller and scrolled to its last line |

## Not included (and why)

- **The description dataset and the public runtime file** (`game-descriptions.json`, `descriptions.json`). They are unpublished development data. The texts needed for review are in the sample, flagged and coverage files.
- **The official-source provenance report, source caches and request/response files.** They are private research records.
- **The before/after comparison images, the verification logs and the proof logs.** They are summarized in the report.
- **Sanitization.** Only local machine paths were replaced: 7 occurrences in the main report now read `<local work folder>` or `<local public dev tree>`. No editorial content, finding or description text was removed or changed.
