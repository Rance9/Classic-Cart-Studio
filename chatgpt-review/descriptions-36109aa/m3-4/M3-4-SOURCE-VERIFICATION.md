# M3.4 source verification

How the evidence for the 773 Game overviews was gathered and weighed.

## Evidence used

| Evidence | Use | Records |
|---|---|---:|
| Original US NES instruction manual, page images (archive.org NESManuals, rendered at 2,000 px) | primary source for names, rules, counts, items, controls | 760 inspected (761 with a manual) |
| Manual OCR text (archive.org) | search aid only; never authoritative for names or numbers | 761 |
| Game's own on-screen text, read from the ROM in memory | identity and names where the manual differs or is missing | 7 checked (Double Dragon II, Mega Man 2, Mega Man 5, Final Fantasy, Cheetahmen II, Exploding Fist, Super Gun); others where an auditor needed it |
| NES-specific secondary sources (reviews, run notes, guides, nesdev, MobyGames NES entries) | claims a manual does not cover | 635 claims |
| Hardware documentation (nocash, nesdev) | Racermate Challenge II (M3.3, retained) | 1 |
| Encyclopedia articles | never sufficient alone; a claim found only there was generalized or removed | none as sole support |

## Reliability order

1. Direct evidence from the exact NES release: on-screen text, then the release's own US manual.
2. Original publisher documentation for that release.
3. Reliable independent NES-specific documentation, such as a review that describes the NES game or TASVideos analysis of the NES ROM.
4. General or cross-platform sources. These were not accepted on their own.

Manuals can be wrong too, so conflicts were resolved on the evidence:
- **On-screen text over the manual:** "Marian" (Double Dragon II), "Wily" (Mega Man 2).
- **NES sources over the manual's own text:**
  - Greg Norman's Golf Power: four courses, from the in-game screenshot and the box, against a manual text listing ten.
  - Dash Galaxy: the floor rule, where the manual and an NES speedrun write-up disagree, was left out.
- **Disputed numbers generalized:** where sources disagree on a number (Arkanoid's round count; Section-Z's level count), the number was left out.

## OCR limitations

The archive.org OCR misreads names, numbers and multi-column layouts. It also mixes in advertisements and legal pages. Every name or number a correction depends on was read from the page image. Errors this caught:
- **Names built from garbled OCR in earlier texts:** Conan's "Ruins of Ry-lech"; Castle of Deceit's "Cebo" and "Phfax" (the manual says Eebo and Phylax).
- **Numbers taken from bad OCR:** Jackie Chan's "40 Bonus Jade" (the image says 30); Ferrari's "26-lap" races (the image says 16 races of 5 laps).
- **OCR misreadings that did not reach the text:** To the Earth's "Ragossians" (the image says Raggosians); Star Soldier's stage numbers.
- **Names restored that M3.2 had removed because of garbled OCR:** ImageFight's "Boondoggle Galaxy".

Some scans are low resolution, sideways or missing pages: Star Wars – The Empire Strikes Back, Indiana Jones and the Temple of Doom, Gun Nac, Snake's Revenge, Pro Wrestling, Big Nose the Caveman, Loopz. Auditors rotated or enlarged the images, used another language section of the same booklet, or relied on NES-specific second sources, and recorded which.

## How version identity was established

- **Manual cover:** the cover, Nintendo seal page and controller page were compared with the No-Intro name. Where a revision (Rev 1, v1.1) cannot be told from the manual, the record says so. Manuals are not revision-specific; none of these differences affected a description.
- **Manual-match corrections:** four records had been tied to the wrong manual and are fixed in the cache index:
  - Indiana Jones – The Action Game: now the UBI Soft manual;
  - Pac-Man (Namco): now the Namco manual;
  - Ms. Pac-Man (Tengen): no manual exists for this release;
  - Tetris (Tengen): now Tengen's "Soviet Mind Game" manual.
  Two were re-audited with the correct manual; the auditors of the other two had already found and used the right sources.
- **Manuals filed under other titles:** 13, for example "Legend of the Ghost Lion", "T&C Surf Designs", "Race America", and "World Champ – Super Boxing Great Fight".
- **Cross-version manuals:** where the only manual covers another edition, the auditor checked that it is the same game and recorded it. Examples: Elite (European edition), Gargoyle's Quest II (UK booklet), Gauntlet (unlicensed Tengen booklet for the licensed cartridge), Indiana Jones and the Temple of Doom (Mindscape reissue).
- **ROM identity checks:** for Exploding Fist and Super Gun, the ROM text identifies the release (credits and copyright strings).

## Unreachable or untrusted sources

- **Unreachable:** StrategyWiki, GameFAQs live pages, MobyGames, Giant Bomb, NesCartDB and Fandom often returned 403 or 402, and web.archive.org was unreachable for some auditors. Archived copies were used where they could be fetched. A source that could not be reached was never treated as evidence against a claim.
- **Untrusted:** pages from TCRF and qix.wiki.gg contained injected instructions. Auditors ignored them and did not use those pages.

## Private records

Research references stay private:
- `m34-decisions.json`: claims, verdicts, sources and pages for each record.
- `name-evidence.json` and `name-attestations.json`.
- The batch results in `<local work folder>\descriptions\m34\`.

The public runtime (`ccs-descriptions/1`) holds only the two texts per game, and the data suite verifies that it carries no URLs or citation metadata.
