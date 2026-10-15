# Classic Cart Studio M3.4: comprehensive NES description accuracy audit (summary)

- **Commit:** `b8f7586` on `home-visual-remediation`, from `4fcb3ee`. Not pushed, merged or deployed. Gate G1 is closed.
- **Scope:** description content and data checks only. No interface, artwork, ROM or console change.
- **No model use:** no Gemini or other paid model request. Cumulative Gemini cost stays $5.22.

## Coverage

- **Before:** 773 of 773 Game overviews, with 48 independently verified.
- **After:** 773 of 773.
  - 685 fully audited and reconciled (level D);
  - 87 source-verified with some claims only partly corroborated (level C);
  - 1 unresolved and kept for owner review (Exploding Fist).
- **Official descriptions:** 101, unchanged.

## What was audited

- **All 773 records:** each was checked against its original US NES manual (page images, not OCR), the game's own on-screen text where it settled a name or identity (read from the ROM in memory only), and NES-specific second sources.
- **Main queue:** 360 records (M3.3's recommended 122 + 238).
- **Recheck:** 66 previously audited records (seeded, with category minimums). 49 changed, including 10 of 10 M3.2 "low-risk" records and 10 of 10 records M3.3 had audited. Under the escalation rule, the other 347 previously audited records were audited in full too.

## What was found

- **671 overviews changed:** 455 substantive corrections, 65 bounded rewrites, 134 unsupported specifics generalized, 17 name-only corrections. 102 needed no change.
- **Error types:**

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

## Most significant corrections

- **Wrong game behind the text:**
  - Star Wars described the Japan-only Namco game; it now describes the JVC game (landspeeder, Death Star rescue, trench run).
  - Indiana Jones – The Action Game described Taito's game.
  - Tengen's Tetris followed Nintendo's game.
- **Wrong manual matches from M3.2:** Indiana Jones – The Action Game, Pac-Man (Namco), Ms. Pac-Man (Tengen) and Tetris (Tengen). All four are fixed; two were re-audited with the correct manual.
- **Names and numbers built from garbled OCR:**
  - Conan: "Ruins of Ry-lech".
  - Jackie Chan: "40" Bonus Jade, actually 30.
  - Ferrari Grand Prix Challenge: "26-lap" races, actually 5 laps.
  - ImageFight: "Boondoggle Galaxy", which M3.2 had removed, is restored from the page image.
- **Other-version contamination:** arcade, computer, Genesis and Japanese details, such as Gyruss's planet route, Commando's arcade gate and fleeing officer, and the Addams Family's plot from another version.
- **US release names:**
  - "Light Warriors" and "Orbs" (Final Fantasy, confirmed on screen);
  - "Rubies" and "Whistle" (Zelda);
  - "Necrosaro" and "Taloon" (Dragon Warrior IV);
  - "Marian" (Double Dragon II, from the on-screen text, not the manual's "Marion").

## Remaining exceptions

Details are in `M3-4-REMAINING-EXCEPTIONS.md`.

1. **Exploding Fist:** no NES-specific source. Keep it, generalize it, or withhold it.
2. **Naming questions:** for example "Racoon" against "Raccoon" Mario, Dr. Turner against Dr. Butler, and manual misprints kept as printed.
3. **Official text and overview use different names in 9 games**, for example Lolo 2's "Great Devil" against "King of Eggerland". Lolo 2 is the closest to the Adventure Island case.
4. **87 level-C records:** none is contradicted; their partly corroborated claims are listed.

## Verification

- **Standard run:** 37 of 37 suites passed under Python 3.13.15. 2,963 checks: 2,961 passed, 0 failed, 2 unsupported (the Windows-only simulator check on Linux and ARM).
- **Description data:** 50 of 50 checks passed, including the new M3.4 checks. Those checks fail on the 4fcb3ee data and pass now.
- **Deliberate-failure proofs:** 11 of 11, two of them new.
- **Visual gate:** 48 of 48 after a documented baseline update. 12 screens changed only inside description areas; the previous baseline is kept.
- **Scans:** clean.

## Recommendation

- **Ready for owner and ChatGPT review:** every record has an independent, documented audit, and every change keeps its previous text and evidence.
- **Not yet final editorial sign-off:** that should follow the owner's answers on Exploding Fist and the naming questions. None of them blocks use of the current data.

## Files

In `reports/game-descriptions/m3-4/`:
- `M3-4-COMPREHENSIVE-ACCURACY-AUDIT.md`
- `M3-4-AUDIT-INVENTORY.md` and `audit-inventory.json`
- `M3-4-EDITORIAL-CORRECTIONS.md`
- `M3-4-PRIOR-AUDIT-RECHECK.md`
- `M3-4-SOURCE-VERIFICATION.md`
- `M3-4-REMAINING-EXCEPTIONS.md`
- this summary
- `screenshots/`: 7 PNG files.
