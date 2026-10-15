# Milestone 3.4: comprehensive NES description accuracy audit (review materials)

- **Development commit:** `b8f7586` on `home-visual-remediation` (from `4fcb3ee`). The development branch is not pushed.
- **This folder:** only the review documents and screenshots. No deployment, release, merge or pull request. Gate G1 is closed.

| Result | Value |
|---|---:|
| Game overviews independently audited | 773 of 773 |
| Overviews corrected | 671 |
| Fully reconciled (level D) | 685 |
| Partly corroborated, at least one claim backed by a single secondary source (level C) | 87 |
| Unresolved | 1 (Exploding Fist) |
| Official descriptions | 101 (unchanged) |
| Standard verification | 37 of 37 suites passing |
| Gemini spending in M3.4 | none (cumulative stays $5.22) |

## Reports

| Document | Contents |
|---|---|
| [M3-4-COMPREHENSIVE-ACCURACY-AUDIT.md](https://github.com/Rance9/Classic-Cart-Studio/blob/review/description-enrichment-36109aa/chatgpt-review/descriptions-36109aa/m3-4/M3-4-COMPREHENSIVE-ACCURACY-AUDIT.md) | Full audit report |
| [M3-4-CHATGPT-SUMMARY.md](https://github.com/Rance9/Classic-Cart-Studio/blob/review/description-enrichment-36109aa/chatgpt-review/descriptions-36109aa/m3-4/M3-4-CHATGPT-SUMMARY.md) | Short summary |
| [M3-4-AUDIT-INVENTORY.md](https://github.com/Rance9/Classic-Cart-Studio/blob/review/description-enrichment-36109aa/chatgpt-review/descriptions-36109aa/m3-4/M3-4-AUDIT-INVENTORY.md) | All 773 records: reconciliation, levels before and after |
| [audit-inventory.json](https://github.com/Rance9/Classic-Cart-Studio/blob/review/description-enrichment-36109aa/chatgpt-review/descriptions-36109aa/m3-4/audit-inventory.json) | The same inventory, machine-readable |
| [M3-4-EDITORIAL-CORRECTIONS.md](https://github.com/Rance9/Classic-Cart-Studio/blob/review/description-enrichment-36109aa/chatgpt-review/descriptions-36109aa/m3-4/M3-4-EDITORIAL-CORRECTIONS.md) | Every changed overview: before, after, error type, evidence (large file; may need the raw view) |
| [M3-4-PRIOR-AUDIT-RECHECK.md](https://github.com/Rance9/Classic-Cart-Studio/blob/review/description-enrichment-36109aa/chatgpt-review/descriptions-36109aa/m3-4/M3-4-PRIOR-AUDIT-RECHECK.md) | The 66-record recheck and the escalation decision |
| [M3-4-SOURCE-VERIFICATION.md](https://github.com/Rance9/Classic-Cart-Studio/blob/review/description-enrichment-36109aa/chatgpt-review/descriptions-36109aa/m3-4/M3-4-SOURCE-VERIFICATION.md) | Evidence types, OCR limits, version identity |
| [M3-4-REMAINING-EXCEPTIONS.md](https://github.com/Rance9/Classic-Cart-Studio/blob/review/description-enrichment-36109aa/chatgpt-review/descriptions-36109aa/m3-4/M3-4-REMAINING-EXCEPTIONS.md) | Open decisions and the level-C records |

## Screenshots

1. [01-website-substantial-correction-star-wars.png](https://github.com/Rance9/Classic-Cart-Studio/blob/review/description-enrichment-36109aa/chatgpt-review/descriptions-36109aa/m3-4/screenshots/01-website-substantial-correction-star-wars.png)
2. [02-home-details-corrected-star-wars.png](https://github.com/Rance9/Classic-Cart-Studio/blob/review/description-enrichment-36109aa/chatgpt-review/descriptions-36109aa/m3-4/screenshots/02-home-details-corrected-star-wars.png)
3. [03-box-art-showcase-overview.png](https://github.com/Rance9/Classic-Cart-Studio/blob/review/description-enrichment-36109aa/chatgpt-review/descriptions-36109aa/m3-4/screenshots/03-box-art-showcase-overview.png)
4. [04-adventure-map-overview.png](https://github.com/Rance9/Classic-Cart-Studio/blob/review/description-enrichment-36109aa/chatgpt-review/descriptions-36109aa/m3-4/screenshots/04-adventure-map-overview.png)
5. [05-home-controller-scroll-final-line.png](https://github.com/Rance9/Classic-Cart-Studio/blob/review/description-enrichment-36109aa/chatgpt-review/descriptions-36109aa/m3-4/screenshots/05-home-controller-scroll-final-line.png)
6. [06-website-wrong-manual-rewrite-indiana-jones.png](https://github.com/Rance9/Classic-Cart-Studio/blob/review/description-enrichment-36109aa/chatgpt-review/descriptions-36109aa/m3-4/screenshots/06-website-wrong-manual-rewrite-indiana-jones.png)
7. [07-website-rechecked-no-change-super-mario-bros.png](https://github.com/Rance9/Classic-Cart-Studio/blob/review/description-enrichment-36109aa/chatgpt-review/descriptions-36109aa/m3-4/screenshots/07-website-rechecked-no-change-super-mario-bros.png)

## Topics for owner review

1. **Exploding Fist** (`a3049fe0daabefe4`): its exact NES identity and an appropriate overview. See `M3-4-REMAINING-EXCEPTIONS.md` §1.
2. **Original US manual terminology versus apparent typographical errors**, for example "Racoon" against "Raccoon" Mario, and "Abatros". See §2.
3. **Nine naming conflicts between the Official description and the Game overview**, for example Lolo 2's "Great Devil" against "King of Eggerland". See §3.
4. **The 87 records relying on a secondary source** (level C). See §4 and the table there.
5. **A representative selection of the 65 rewritten descriptions**, for example:
   - Star Wars, Indiana Jones and the Last Crusade – The Action Game, Tetris (Tengen);
   - Advanced Dungeons & Dragons – DragonStrike, Rocket Ranger;
   - Commando, Fist of the North Star, Werewolf – The Last Warrior.

   All are in `M3-4-EDITORIAL-CORRECTIONS.md` (search by title).

## Notes

- **Sanitization:** six local machine paths (in the audit report and the source-verification report) were replaced with `<local work folder>` and `<local public dev tree>`. Nothing else was changed.
- **Not included:** private files the reports mention, such as the decisions file, the name-evidence records, the manual scans and caches, and ROMs. The research URLs and manual names in these reports are for editorial review only; the product shows no sources.
