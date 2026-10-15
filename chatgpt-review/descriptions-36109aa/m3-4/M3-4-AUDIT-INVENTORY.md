# M3.4 audit inventory

One record per canonical game ID: **773** (the default catalog; identities unchanged). Machine-readable: `m3-4/audit-inventory.json` (schema `ccs-description-audit-inventory/1`).

## Verification levels (owner definitions)

- **A, AI claim-checked:** a model checked the text against the sources it was written from.
- **B, NES manual consulted:** the original manual was used. In M3.2 this was the OCR text, read by a model; not necessarily every claim.
- **C, independently source-verified:** the important claims were checked by hand against evidence for this NES release.
- **D, fully audited and reconciled:** every material claim was reviewed and contradictions resolved; the text is approved or explicitly qualified.

Only C and D count as independently verified.

## Reconciliation of the earlier figures

| Earlier figure | Records | What it is in this inventory |
|---|---:|---|
| M3.2 manual audit | 391 | `m32-audit-*`: 158 with contradictions (corrected), 144 medium risk without contradiction, 89 low risk. The report's 145 medium included 1 record that also had a contradiction and is counted under corrected. |
| Outside the M3.2 audit | 260 | 259 untouched NES-native plus the polished Cheetahmen II (`m32-flagged-polish`). Another 3 records outside the audit are the previously rejected drafts edited by hand (`m32-rejected-edited`). |
| M3.2 recovery set | 119 | 117 missing overviews (105 from manuals, 10 from researched facts, 2 unresolved and later recovered in M3.3) plus 2 rejected drafts rebuilt from manuals. |
| M3.3 independent audit | 44 | 22 from the medium-risk group and 22 NES-native; plus 4 M3.3 decision texts (Racermate, Super Gun, Caesars Palace, BMX Simulator). |
| **Total** | **773** | 391 + 263 + 119 = 773; no record is counted twice. |

## Before M3.4

- **Independently verified (C or D):** 48. These are the M3.3 audit and decisions: D 37, C 11 (C means some claims rest on one secondary source).
- **Manual consulted through OCR by a model only (B):** 476.
- **AI claim-checked only (A):** 249.
- **Requiring first independent verification:** 725.

## M3.4 queues

- **Main queue:** 360. Records never independently audited and never part of an M3.2 correction or recovery: the remaining 122 medium-risk and 238 NES-native records (M3.3's recommended 122 + 238).
- **Recheck of previously audited, corrected or recovered records:** 66, seeded selection; category counts {'arcade port': 11, 'computer or cross-platform port': 10, 'previously corrected': 34, 'medium risk': 10, 'low risk': 10, 'NES-native or secondary-source': 18}.
- **Escalation pool:** 347. The remaining previously audited, corrected or recovered records plus the 48 M3.3 records, audited in full only where the recheck shows a systematic error pattern (escalation rule); see the audit report for the decision.

## After M3.4

- **Records independently audited in M3.4:** 773.
- **Verdicts:** {'rewritten': 65, 'corrected': 606, 'accurate': 78, 'accurate-with-minor-uncertainty': 23, 'unresolved': 1}.
- **Texts changed:** 671.
- **Page images inspected:** 760.
- **Level after M3.4:** {'D': 685, 'C': 87, 'unresolved': 1}.
- **Records still resting on a secondary-only or uncertain claim:** 610 (claims listed in the machine-readable file).

## Records

| Game | ID | Group | Level before | Queue | M3.4 verdict | Changed | Level after |
|---|---|---|---|---|---|---|---|
| 10-Yard Fight | `8e844d77da01f86a` | m32-audit-medium-risk | B | main | rewritten | yes | D |
| 1942 | `e868400409c70876` | m32-audit-low-risk | B | recheck | corrected | yes | C |
| 1943 - The Battle of Midway | `9f54aafa367247b9` | m32-audit-corrected | B | escalation | corrected | yes | D |
| 3-D WorldRunner | `c457644ccfb93f89` | nes-native-untouched | A | main | corrected | yes | D |
| 720 Degrees | `df696ce2af90b18c` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| 8 Eyes | `3493f7621d964af7` | nes-native-untouched | D | escalation | corrected | yes | D |
| Abadox - The Deadly Inner War | `c4d4ff0bd283656c` | nes-native-untouched | A | main | corrected | yes | D |
| Action 52 | `96861646aadc9534` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Addams Family, The | `005c48a5984e3b75` | m32-audit-medium-risk | B | main | rewritten | yes | D |
| Addams Family, The - Pugsley's Scavenger Hunt | `b3e09c94e33e0293` | m32-audit-corrected | B | escalation | accurate |  | D |
| Advanced Dungeons & Dragons - DragonStrike | `22e92af92aed2d8d` | nes-native-untouched | A | main | rewritten | yes | D |
| Advanced Dungeons & Dragons - Heroes of the Lance | `df2609c80e818bb9` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Advanced Dungeons & Dragons - Hillsfar | `60061c0042741f7c` | m32-audit-medium-risk | D | escalation | corrected | yes | D |
| Advanced Dungeons & Dragons - Pool of Radiance | `38e5253131d26439` | m32-audit-low-risk | B | escalation | corrected | yes | C |
| Adventure Island | `53bfc94fce46a251` | m32-audit-corrected | B | escalation | corrected | yes | C |
| Adventure Island 3 | `e8dc8c0441c54d09` | m32-audit-corrected | B | escalation | corrected | yes | C |
| Adventure Island II | `5ee479ef04d80366` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Adventures in the Magic Kingdom | `185e4adb2cbcb71e` | nes-native-untouched | C | recheck | corrected | yes | D |
| Adventures of Bayou Billy, The | `0e862077f390348b` | nes-native-untouched | D | escalation | corrected | yes | D |
| Adventures of Dino Riki | `58d0f0e504b8450e` | nes-native-untouched | A | main | corrected | yes | C |
| Adventures of Gilligan's Island, The | `a0bc241936acf199` | nes-native-untouched | A | main | corrected | yes | D |
| Adventures of Lolo | `c5e47c0479f500b8` | m32-audit-corrected | B | recheck | corrected | yes | D |
| Adventures of Lolo 2 | `12d3119e25f7b40a` | nes-native-untouched | A | main | corrected | yes | C |
| Adventures of Lolo 3 | `24f7bb802a0ddce0` | nes-native-untouched | A | main | corrected | yes | D |
| Adventures of Rad Gravity, The | `322d6be47ed539c3` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Adventures of Rocky and Bullwinkle and Friends, The | `03db3d003eaa91e1` | m32-audit-medium-risk | B | main | rewritten | yes | D |
| Adventures of Tom Sawyer | `227e8a900206d0f8` | nes-native-untouched | A | main | corrected | yes | D |
| After Burner | `69300586af39342f` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Air Fortress | `6d0215929f2dfeee` | nes-native-untouched | A | main | corrected | yes | D |
| Airwolf | `b82c1234165cd725` | m32-audit-low-risk | B | escalation | accurate |  | D |
| Al Unser Jr. Turbo Racing | `4c95afeda9a92842` | m32-recovered-manual | B | recheck | accurate |  | D |
| Alex DeMeo's Race America | `5cb8d03a90732d01` | nes-native-untouched | A | main | rewritten | yes | D |
| Alfred Chicken | `4b088e1e78981308` | m32-audit-corrected | B | escalation | corrected | yes | C |
| Alien 3 | `d73a9a1fae739675` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Alien Syndrome | `d676ef6f7aa3b042` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| All-Pro Basketball | `1c811b67d3e211f9` | nes-native-untouched | C | escalation | corrected | yes | D |
| Alpha Mission | `3addebdec132929e` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Amagon | `a978de5d5f343eac` | nes-native-untouched | A | main | corrected | yes | D |
| American Gladiators | `be12cff13a06e0b5` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Anticipation | `427876021e6c0774` | m32-audit-corrected | B | recheck | corrected | yes | C |
| Arch Rivals - A Basketbrawl! | `bb834eb82f0ac531` | m32-recovered-manual | B | escalation | accurate |  | D |
| Archon | `751d3bf4722ce117` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Arkanoid | `a3763e702f8ae081` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Arkista's Ring | `381fcbe2b714c38f` | nes-native-untouched | A | main | corrected | yes | D |
| Asder - 20-in-1 | `19a0cae21557cb7f` | m32-recovered-research | A | recheck | corrected | yes | C |
| Astyanax | `a08e6b53f1fa593e` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Athena | `2137d1621d29df50` | m32-audit-medium-risk | B | main | rewritten | yes | D |
| Athletic World | `9d01730c16c2cfd2` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Attack of the Killer Tomatoes | `3b687bf0a8ccd237` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Baby Boomer | `fb20d9562088cfa6` | nes-native-untouched | A | main | corrected | yes | D |
| Back to the Future | `2bd744ff0d76653b` | nes-native-untouched | A | main | corrected | yes | D |
| Back to the Future Part II & III | `b265adba4a6f751f` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Bad Dudes | `56382fac9104b267` | m32-audit-medium-risk | D | recheck | corrected | yes | D |
| Bad News Baseball | `467a2e53c7af4c60` | nes-native-untouched | A | main | corrected | yes | D |
| Bad Street Brawler | `98658d5c8e530994` | m32-audit-corrected | B | escalation | corrected | yes | C |
| Balloon Fight | `7328b2eb1c9fe06b` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Bandai Golf - Challenge Pebble Beach | `2a92f666ce73177e` | nes-native-untouched | A | main | rewritten | yes | D |
| Bandit Kings of Ancient China | `1ca05fb702996a11` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Barbie | `3f8a65278efb20df` | m32-audit-corrected | B | escalation | corrected | yes | C |
| Bard's Tale, The | `e88de987a5b79833` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Barker Bill's Trick Shooting | `4da0d4900b297274` | nes-native-untouched | A | main | corrected | yes | D |
| Baseball | `f22944452be3259a` | m32-audit-medium-risk | B | main | accurate-with-minor-uncertainty |  | D |
| Baseball Simulator 1.000 | `8bb20791eb3f4fd2` | nes-native-untouched | A | main | corrected | yes | D |
| Baseball Stars | `b035ec9e2a7fc408` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Baseball Stars II | `5f8807999205f380` | m32-audit-low-risk | B | recheck | corrected | yes | D |
| Bases Loaded | `85de365c6e538bc7` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Bases Loaded 3 | `5d84d61e7e4c2b7d` | nes-native-untouched | A | main | rewritten | yes | D |
| Bases Loaded 4 | `e4aa19e0fd2800b5` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Bases Loaded II - Second Season | `b9af9efdf490e148` | nes-native-untouched | A | main | accurate |  | D |
| Batman - Return of the Joker | `183ede1115b428b0` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Batman - The Video Game | `f28821df0114eb6c` | nes-native-untouched | A | main | corrected | yes | D |
| Batman Returns | `8b7363e037883aaa` | nes-native-untouched | A | main | corrected | yes | D |
| Battle Chess | `f4d57c83d8b3af0f` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Battle of Olympus, The | `d095eab5376c2b7c` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Battleship | `c49a5d7f565646d7` | nes-native-untouched | A | main | corrected | yes | D |
| Battletoads | `56d25e05dde2048c` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Battletoads-Double Dragon | `4f67a81cc978e5aa` | m32-audit-medium-risk | D | escalation | corrected | yes | D |
| Bee 52 | `f93163a51f4a671c` | m32-audit-medium-risk | B | main | rewritten | yes | D |
| Beetlejuice | `b9434d2f359f6e46` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Best of the Best - Championship Karate | `2366af9fc0512d39` | m32-audit-low-risk | B | recheck | rewritten | yes | D |
| Bible Adventures | `e455d60550150a93` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Bible Buffet | `9229e5f67a285d72` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Big Nose Freaks Out | `87b9c94b5e9144eb` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Big Nose the Caveman | `d290ac95ca512a37` | m32-audit-corrected | B | escalation | accurate-with-minor-uncertainty |  | C |
| Bigfoot | `32fa00d52f39b053` | nes-native-untouched | A | main | rewritten | yes | D |
| Bill & Ted's Excellent Video Game Adventure | `b183c0952994cadc` | nes-native-untouched | A | main | corrected | yes | D |
| Bill Elliott's NASCAR Challenge | `ddf58f90fa596613` | m32-audit-medium-risk | D | recheck | corrected | yes | D |
| Bionic Commando | `aeb61fd5cf5a5ed7` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Black Bass, The | `99806081ba58c0a1` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Blackjack | `d430afcb9d0af1a8` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Blades of Steel | `abde6fc87b2d90e0` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Blaster Master | `7a26c62a9b1605cb` | nes-native-untouched | A | main | corrected | yes | D |
| Blue Marlin, The | `e311f33f98a4461f` | nes-native-untouched | A | main | corrected | yes | D |
| Blues Brothers, The | `46fb05f80167bd18` | m32-audit-corrected | B | escalation | rewritten | yes | D |
| BMX Simulator | `85f7be325689329e` | m32-unresolved | D | escalation | accurate-with-minor-uncertainty |  | C |
| Bo Jackson Baseball | `7e59dcd2576d2e83` | m32-audit-corrected | B | recheck | corrected | yes | D |
| Bomberman | `2967746c2a434c97` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Bomberman II | `83cb47fda376900e` | nes-native-untouched | D | escalation | corrected | yes | D |
| Bonk's Adventure | `b3d82e2818aea6ca` | m32-audit-corrected | B | recheck | corrected | yes | D |
| Boulder Dash | `0e58f270e7b11678` | m32-audit-corrected | B | escalation | accurate |  | D |
| Bram Stoker's Dracula | `a8b6829d8d1e17cc` | m32-rebuilt-rejected | B | recheck | accurate |  | D |
| Break Time - The National Pool Tour | `fde81fc5af055819` | nes-native-untouched | A | main | corrected | yes | D |
| BreakThru | `e1487d23800aa2c2` | m32-recovered-manual | B | escalation | accurate-with-minor-uncertainty |  | D |
| Bubble Bath Babes | `3bcce1ff03b55c20` | m32-audit-low-risk | B | escalation | accurate-with-minor-uncertainty |  | C |
| Bubble Bobble | `31523322db8f94e7` | m32-audit-corrected | B | escalation | corrected | yes | C |
| Bubble Bobble Part 2 | `382699d54308423c` | m32-audit-medium-risk | D | escalation | corrected | yes | D |
| Bucky O'Hare | `d02b24e4ee8e639b` | nes-native-untouched | A | main | corrected | yes | D |
| Bugs Bunny Birthday Blowout, The | `f01fe9436ca7b509` | nes-native-untouched | A | main | corrected | yes | D |
| Bugs Bunny Crazy Castle, The | `7882e99e08deb1c2` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Bump 'n' Jump | `ba8c9990f378b941` | m32-audit-corrected | B | escalation | rewritten | yes | D |
| Burai Fighter | `abbddcb7c85a9956` | m32-audit-corrected | B | escalation | corrected | yes | D |
| BurgerTime | `8f349b0ed7d31a07` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Cabal | `47dfae941b3c660b` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Caesars Palace | `cfea236e0d0fe0e4` | m32-unresolved | D | escalation | accurate-with-minor-uncertainty |  | C |
| California Games | `be20222704df7d5c` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Caltron - 6 in 1 | `67e2a8ab48af329e` | nes-native-untouched | A | main | rewritten | yes | D |
| Capcom's Gold Medal Challenge '92 | `4ec881462687e084` | nes-native-untouched | A | main | rewritten | yes | D |
| Captain America and the Avengers | `6d694349435603c0` | m32-audit-corrected | B | escalation | accurate |  | D |
| Captain Comic - The Adventure | `935aa637ca8fb0ec` | m32-audit-low-risk | B | recheck | corrected | yes | C |
| Captain Planet and the Planeteers | `41dd396fbd9b0883` | nes-native-untouched | A | main | corrected | yes | D |
| Captain Skyhawk | `ffcce36b7ccbec82` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Casino Kid | `f76f1779454a8a98` | nes-native-untouched | A | main | rewritten | yes | D |
| Casino Kid II | `87e479b14421fc4b` | m32-recovered-manual | B | escalation | accurate |  | D |
| Castelian | `98ba2353111a0b2c` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Castle of Deceit | `3668454e1904ada7` | nes-native-untouched | A | main | rewritten | yes | D |
| Castle of Dragon | `6887230077d8eefa` | m32-audit-medium-risk | B | main | rewritten | yes | D |
| Castlequest | `9abebd837287a38a` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Castlevania | `7eba1637cd2fdbc4` | m32-audit-corrected | B | escalation | corrected | yes | C |
| Castlevania II - Simon's Quest | `9575ec31c1c658fd` | nes-native-untouched | A | main | corrected | yes | D |
| Castlevania III - Dracula's Curse | `6e8d289635ac3947` | nes-native-untouched | A | main | corrected | yes | D |
| Caveman Games | `61bab3612f96371b` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Challenge of the Dragon | `c20a8cc1b2dacb8a` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Championship Bowling | `b012733f6be75e9a` | m32-recovered-manual | B | escalation | accurate |  | D |
| Championship Pool | `d5b039637a231545` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Cheetahmen II | `ebdd748cd488c4ac` | m32-flagged-polish | A | main | corrected | yes | C |
| Chessmaster, The | `66d4beb992a64632` | m32-recovered-manual | B | escalation | accurate |  | D |
| Chiller | `f64ce816453d826d` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Chip 'n Dale - Rescue Rangers | `e84d90fefb92f2b6` | m32-audit-medium-risk | D | recheck | corrected | yes | D |
| Chip 'n Dale - Rescue Rangers 2 | `afa359b53e90781e` | nes-native-untouched | D | recheck | corrected | yes | D |
| Chris Evert & Ivan Lendl in Top Players' Tennis | `68a6f5ef79892e49` | m32-recovered-research | A | escalation | corrected | yes | D |
| Chubby Cherub | `21a7f7a5a043ddc2` | nes-native-untouched | D | recheck | corrected | yes | D |
| Circus Caper | `fa701601d39819ef` | nes-native-untouched | A | main | corrected | yes | D |
| City Connection | `590535e8cda84cb4` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Clash at Demonhead | `cf226f0d9486103b` | nes-native-untouched | A | main | corrected | yes | D |
| Classic Concentration | `69d26e6e849a103c` | m32-recovered-manual | B | escalation | accurate |  | D |
| Cliffhanger | `79969ab6741823e5` | m32-audit-low-risk | B | escalation | rewritten | yes | D |
| Clu Clu Land | `ed0a1a5ca7cf4041` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Cobra Command | `711eee6690af76a5` | m32-audit-low-risk | B | escalation | rewritten | yes | D |
| Cobra Triangle | `db26936868427b5b` | nes-native-untouched | A | main | corrected | yes | D |
| Code Name - Viper | `2be0bd6e64cf2cb4` | nes-native-untouched | A | main | corrected | yes | D |
| Color a Dinosaur | `e8bb5be4ada0b982` | nes-native-untouched | A | main | rewritten | yes | D |
| Commando | `0512b4aa2220f74e` | m32-audit-medium-risk | B | main | rewritten | yes | C |
| Conan | `8ae9b624c08b86f6` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Conflict | `18ca062abee6bf3b` | nes-native-untouched | A | main | corrected | yes | D |
| Conquest of the Crystal Palace | `a5b8e24589539b0b` | nes-native-untouched | A | main | corrected | yes | D |
| Contra | `d41e28b1a33b3b67` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Contra Force | `0c190a8a7bfb57d7` | nes-native-untouched | A | main | corrected | yes | D |
| Cool World | `22ea833fd9466795` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Cowboy Kid | `528d501320ed0276` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Crash 'n' the Boys - Street Challenge | `fda4fd839497634d` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Crystal Mines | `9357e9233f65413e` | m32-recovered-manual | B | escalation | accurate |  | D |
| Crystalis | `5ad644d368f70b61` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Cyber Stadium Series - Base Wars | `231701f8c463184d` | nes-native-untouched | A | main | corrected | yes | D |
| Cyberball | `e239709320fca149` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Cybernoid - The Fighting Machine | `ad1e14d08657d99c` | m32-audit-corrected | B | recheck | corrected | yes | D |
| Dance Aerobics | `3c4ef00443a1675e` | nes-native-untouched | D | recheck | accurate |  | D |
| Danny Sullivan's Indy Heat | `4cbf1c1d0be22712` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Darkman | `9b6d967f493a565e` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Darkwing Duck | `f97f05a4f8747d81` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Dash Galaxy in the Alien Asylum | `227e789fef94eabc` | nes-native-untouched | D | escalation | corrected | yes | D |
| David Crane's A Boy and His Blob - Trouble on Blobolonia | `911fb75ec1f900a5` | nes-native-untouched | A | main | corrected | yes | D |
| Day Dreamin' Davey | `41f949cfedf71679` | nes-native-untouched | C | escalation | corrected | yes | D |
| Days of Thunder | `9371fab4f58fa6d6` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Deadly Towers | `0115356b0791cc8d` | nes-native-untouched | A | main | corrected | yes | D |
| Death Race | `1536b8418d0643ae` | m32-audit-medium-risk | B | main | rewritten | yes | D |
| Deathbots | `32aeef6bae170d26` | m32-recovered-manual | B | escalation | accurate |  | D |
| Defender II | `bf58512dc3bc427d` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Defender of the Crown | `9e31d3d918a352c0` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Defenders of Dynatron City | `167bc1a0125f12d9` | m32-recovered-manual | B | escalation | accurate |  | D |
| Deja Vu | `87850226dc6c4c52` | m32-audit-medium-risk | B | main | corrected | yes | C |
| Demon Sword | `c408dd19ca49e551` | nes-native-untouched | A | main | corrected | yes | D |
| Desert Commander | `281ad7bdb4f94e23` | nes-native-untouched | A | main | rewritten | yes | D |
| Destination Earthstar | `44cd6b2aa05286c3` | nes-native-untouched | A | main | corrected | yes | D |
| Destiny of an Emperor | `6d082c801942ce67` | nes-native-untouched | A | main | rewritten | yes | D |
| Dick Tracy | `163479e2b1571538` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Die Hard | `e060fd43ef58d6e0` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Dig Dug II - Trouble in Paradise | `1bf92138500e24a4` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Digger T. Rock - The Legend of the Lost City | `721c464598519132` | nes-native-untouched | A | main | rewritten | yes | D |
| Dirty Harry | `5aaa8382ec79c170` | nes-native-untouched | A | main | rewritten | yes | D |
| Donkey Kong | `aa408f5a6b97c0d7` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Donkey Kong 3 | `b15b298dc37692d0` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Donkey Kong Classics | `234c2f62cbc3c72e` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Donkey Kong Jr. | `950ebe68e7f74219` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Donkey Kong Jr. Math | `07dd16464d769429` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Double Dare | `56cb5897c539b687` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Double Dragon | `8f2f9ba926280edd` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Double Dragon II - The Revenge | `c564704993cf79c9` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Double Dragon III - The Sacred Stones | `9f5891d333ea410c` | m32-audit-medium-risk | C | recheck | corrected | yes | D |
| Double Dribble | `eb0e5f318e84e931` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Double Strike - Aerial Attack Force | `588a31fdcdefcef9` | m32-recovered-manual | B | recheck | accurate |  | D |
| Dr. Chaos | `a0b2ec9b44033fc4` | nes-native-untouched | A | main | corrected | yes | D |
| Dr. Jekyll and Mr. Hyde | `5860e217030f9da9` | nes-native-untouched | A | main | corrected | yes | C |
| Dr. Mario | `d2fbe8b10f762099` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Dragon Fighter | `af505b8530b3303e` | nes-native-untouched | A | main | corrected | yes | D |
| Dragon Power | `038f2241fd6e5860` | m32-recovered-manual | B | escalation | accurate |  | D |
| Dragon Spirit - The New Legend | `901b7a9b3e8c0256` | m32-audit-medium-risk | D | escalation | corrected | yes | D |
| Dragon Warrior | `d968a138b19d39da` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Dragon Warrior II | `c15ab051ff066f01` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Dragon Warrior III | `f91a8bfc25bd267f` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Dragon Warrior IV | `e49cb745370065a4` | nes-native-untouched | A | main | corrected | yes | D |
| Dragon's Lair | `5564e54943deddc6` | nes-native-untouched | A | main | corrected | yes | D |
| Duck Hunt | `7026334a7e8742b6` | m32-audit-medium-risk | B | main | corrected | yes | C |
| DuckTales | `8ba8baed01a9fbaf` | m32-audit-medium-risk | B | main | corrected | yes | C |
| DuckTales 2 | `54c70628739c9cfa` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Dudes with Attitude | `99bba29bd92942f6` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Dungeon Magic - Sword of the Elements | `c613c10e32b93dbc` | nes-native-untouched | A | main | rewritten | yes | D |
| Dusty Diamond's All-Star Softball | `35ac08a055cbaa13` | nes-native-untouched | A | main | corrected | yes | D |
| Dynowarz - The Destruction of Spondylus | `690ec2de233486d9` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Elevator Action | `e2824a71c7410632` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Eliminator Boat Duel | `9d0396286f2c0273` | nes-native-untouched | A | main | corrected | yes | C |
| Elite | `9b547db136e4b756` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Empereur, L' | `a899f4f8b7ded48a` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Excitebike | `e9f4c9d1b7c66c6a` | m32-audit-low-risk | B | recheck | corrected | yes | D |
| Exodus - Journey to the Promised Land | `6d7b7315fe0aee27` | m32-audit-medium-risk | D | recheck | corrected | yes | D |
| Exploding Fist | `a3049fe0daabefe4` | nes-native-untouched | A | main | unresolved |  | unresolved |
| F-117A Stealth Fighter | `69bc9f0a2f0cd50d` | nes-native-untouched | A | main | rewritten | yes | D |
| F-15 City War | `3fd39ba2cdd6e220` | m32-recovered-manual | B | escalation | accurate |  | D |
| F-15 Strike Eagle | `bf4b9f8706814e91` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Family Feud | `0de5af54fd433bc6` | m32-recovered-manual | B | recheck | accurate |  | D |
| Fantastic Adventures of Dizzy, The | `e0b124ddd8ac7be9` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Fantasy Zone | `ad14301747e0ae0f` | m32-audit-medium-risk | B | main | rewritten | yes | D |
| Faria - A World of Mystery & Danger! | `6f23f245b9edc5af` | m32-rejected-edited | A | recheck | corrected | yes | D |
| Faxanadu | `58f2ba801354a8e5` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Felix the Cat | `884b46ad4eb5160a` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Ferrari Grand Prix Challenge | `9bc9b891cb8da7f4` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Fester's Quest | `c0c98471130cd09c` | nes-native-untouched | A | main | corrected | yes | D |
| Final Fantasy | `fa456d852372173e` | nes-native-untouched | A | main | corrected | yes | D |
| Fire 'n Ice | `db16f0a5cb05dfa9` | nes-native-untouched | A | main | corrected | yes | D |
| Firehawk | `3f5ed8b5207ff10e` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Fisher-Price - Firehouse Rescue | `eb3955503b404a5e` | m32-recovered-manual | B | escalation | accurate |  | D |
| Fisher-Price - I Can Remember | `f79c8ddedbf0b580` | m32-recovered-manual | B | recheck | accurate |  | D |
| Fisher-Price - Perfect Fit | `c31d4984544dfee4` | m32-recovered-manual | B | escalation | accurate |  | D |
| Fist of the North Star | `04264565cf33f4bd` | nes-native-untouched | A | main | rewritten | yes | D |
| Flight of the Intruder | `6e5bdfe7ee4cc4d9` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Flintstones, The - The Rescue of Dino & Hoppy | `3036a59bb7bf16a3` | nes-native-untouched | A | main | rewritten | yes | D |
| Flintstones, The - The Surprise at Dinosaur Peak! | `8da82a28be164453` | nes-native-untouched | A | main | rewritten | yes | D |
| Flying Dragon - The Secret Scroll | `fd7523b5ec5769e4` | nes-native-untouched | A | main | corrected | yes | D |
| Flying Warriors | `1568e77e6533087e` | nes-native-untouched | A | main | corrected | yes | D |
| Formula One - Built to Win | `758ea7bee928136a` | nes-native-untouched | A | main | rewritten | yes | D |
| Fox's Peter Pan & the Pirates - The Revenge of Captain Hook | `e9fe69c87a9ab118` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Frankenstein - The Monster Returns | `682a0629d25275a9` | nes-native-untouched | A | main | corrected | yes | D |
| Freedom Force | `995b57f7d2c68a56` | nes-native-untouched | C | recheck | corrected | yes | C |
| Friday the 13th | `7e94b4fe8c334397` | nes-native-untouched | A | main | corrected | yes | D |
| Fun House | `6280ffb211b03ccc` | m32-audit-corrected | B | escalation | corrected | yes | D |
| G.I. Joe - A Real American Hero | `d077b282b751a246` | m32-recovered-manual | B | escalation | corrected | yes | D |
| G.I. Joe - The Atlantis Factor | `9e65e4d55123612c` | nes-native-untouched | A | main | corrected | yes | D |
| Galactic Crusader | `a636a947acf1ef0b` | nes-native-untouched | A | main | rewritten | yes | D |
| Galaga - Demons of Death | `df49cc788fff3688` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Galaxy 5000 - Racing in the 51st Century | `c019750cc439810d` | nes-native-untouched | A | main | corrected | yes | D |
| Gargoyle's Quest II | `055fb73baaed0f3c` | nes-native-untouched | A | main | corrected | yes | D |
| Garry Kitchen's Battletank | `a2039efb5b5b8d49` | nes-native-untouched | A | main | corrected | yes | D |
| Gauntlet | `fd2a8520314fb183` | m32-audit-corrected | B | recheck | corrected | yes | D |
| Gauntlet | `67b8a39744807dd7` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Gauntlet II | `ffa61d9f7bfb1d60` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Gemfire | `c7dae9170fcd7cbc` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Genghis Khan | `2a2f75c2be5f6952` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| George Foreman's KO Boxing | `cf517940496d6085` | nes-native-untouched | A | main | corrected | yes | D |
| Ghost Lion | `1849bb687d038a43` | nes-native-untouched | A | main | rewritten | yes | D |
| Ghostbusters | `43221ae8a386e8dd` | m32-audit-corrected | B | escalation | accurate |  | D |
| Ghostbusters II | `1ea36ebd81692d3a` | nes-native-untouched | A | main | corrected | yes | D |
| Ghosts'n Goblins | `eea66f7bcc90d114` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Ghoul School | `48aaef58dee3ad37` | nes-native-untouched | A | main | corrected | yes | D |
| Goal! | `bf22e6aff97bb442` | nes-native-untouched | A | main | corrected | yes | D |
| Goal! Two | `7911375ab98da4ac` | nes-native-untouched | A | main | corrected | yes | D |
| Godzilla - Monster of Monsters! | `c2383e5cd8670c71` | nes-native-untouched | A | main | rewritten | yes | D |
| Godzilla 2 - War of the Monsters | `37b5a1c9d9bc1747` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Golf | `c41555b61617e52c` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Golf Grand Slam | `af24262bc78865b8` | nes-native-untouched | A | main | accurate |  | D |
| Golgo 13 - Top Secret Episode | `9f559f83b9b51791` | nes-native-untouched | A | main | corrected | yes | C |
| Goonies II, The | `16c7de15b7dc72c5` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Gotcha! - The Sport! | `15685dee8bc1c588` | nes-native-untouched | A | main | corrected | yes | D |
| Gradius | `6918d7cbb81bfcd2` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Great Waldo Search, The | `60a7d102deac7491` | nes-native-untouched | A | main | corrected | yes | D |
| Greg Norman's Golf Power | `978eb3b632927b65` | nes-native-untouched | C | escalation | accurate-with-minor-uncertainty |  | D |
| Gremlins 2 - The New Batch | `2b77da430b08e6a9` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Guardian Legend, The | `d18ad8b76f9d0678` | nes-native-untouched | A | main | corrected | yes | D |
| Guerrilla War | `a2033c3b3d9f54b3` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Gumshoe | `4628f32db9b826d1` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Gun Nac | `d22a0c390dfc47c9` | nes-native-untouched | A | main | corrected | yes | D |
| Gun.Smoke | `f39421a126f3b93c` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Gyromite | `0d895a031dd38f26` | nes-native-untouched | A | main | corrected | yes | D |
| Gyruss | `c6e275929764f795` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Harlem Globetrotters | `07bfd5bf6d3e5cea` | m32-audit-medium-risk | B | main | rewritten | yes | D |
| Hatris | `3616e10dac2c7e0d` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Heavy Barrel | `fd3c19b0339bf2b3` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Heavy Shreddin' | `0a23312c8e07b9af` | nes-native-untouched | A | main | corrected | yes | D |
| High Speed | `710e907230bbd821` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Hogan's Alley | `8e4a04076b6a728a` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Hollywood Squares | `54fb22e081d95b80` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Home Alone | `e719d71881096345` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Home Alone 2 - Lost in New York | `3f761f529d40cf42` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Hook | `f3ff0c50c05aa5d4` | m32-audit-low-risk | B | escalation | accurate |  | D |
| Hoops | `0a3459d0b8955bed` | nes-native-untouched | A | main | corrected | yes | D |
| Hot Slots | `9e552c628e4e2ab6` | nes-native-untouched | A | main | corrected | yes | D |
| Hudson Hawk | `3991a761116131dc` | m32-audit-corrected | B | recheck | corrected | yes | D |
| Hunt for Red October, The | `bafe68d5e6bbebb0` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Hydlide | `770abf58074764db` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Ice Climber | `002bb62441c16250` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Ice Hockey | `b59b46aaaa6b382d` | nes-native-untouched | A | main | corrected | yes | D |
| Ikari III - The Rescue | `181163b590a581b6` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Ikari Warriors | `3775c1184419c078` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Ikari Warriors II - Victory Road | `119f865684be58f3` | m32-audit-medium-risk | D | escalation | accurate-with-minor-uncertainty |  | C |
| ImageFight | `7032a94d140339f9` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Immortal, The | `8bc6a252778c2909` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Impossible Mission II | `a47f338a88835424` | m32-recovered-manual | B | escalation | corrected | yes | C |
| Incredible Crash Dummies, The | `c42fc592821b474b` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Indiana Jones and the Last Crusade | `5348f7f88695867d` | nes-native-untouched | A | main | corrected | yes | D |
| Indiana Jones and the Last Crusade - The Action Game | `79c03fec3f459ac6` | m32-audit-corrected | B | escalation | rewritten | yes | D |
| Indiana Jones and the Temple of Doom | `884765e5df860422` | m32-audit-medium-risk | D | escalation | corrected | yes | D |
| Indiana Jones and the Temple of Doom | `3a7726a787a6d8ea` | m32-audit-medium-risk | D | recheck | corrected | yes | C |
| Infiltrator | `0bb401bbd0cae275` | nes-native-untouched | A | main | rewritten | yes | C |
| Iron Tank - The Invasion of Normandy | `6fac8ea87f7eea5e` | nes-native-untouched | A | main | corrected | yes | D |
| IronSword - Wizards & Warriors II | `aab5e0ecc46e575b` | nes-native-untouched | A | main | accurate |  | D |
| Isolated Warrior | `81311ff2507c6522` | nes-native-untouched | A | main | corrected | yes | D |
| Ivan 'Ironman' Stewart's Super Off Road | `acc3b89bcbe3ccc2` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Jack Nicklaus' Greatest 18 Holes of Major Championship Golf | `0516f35bf14eb5c0` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Jackal | `6928e8c589d6d9cc` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Jackie Chan's Action Kung Fu | `628d6696c7ac84c3` | m32-audit-corrected | B | escalation | corrected | yes | D |
| James Bond Jr | `e0d4463066d9985d` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Jaws | `8db2cb94dc9caec6` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Jeopardy! | `7f28fa007f219e4e` | m32-recovered-manual | B | escalation | accurate |  | D |
| Jeopardy! 25th Anniversary Edition | `0244b219abbde241` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Jeopardy! Junior Edition | `5e2aac273c63b389` | m32-recovered-manual | B | recheck | accurate |  | D |
| Jetsons, The - Cogswell's Caper | `c9a3c9873a859fcb` | nes-native-untouched | A | main | corrected | yes | D |
| Jimmy Connors Tennis | `69cf1773c23606cd` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Joe & Mac | `509f04b745d11b8d` | m32-audit-corrected | B | escalation | accurate |  | D |
| John Elway's Quarterback | `8d21e1e256f82579` | m32-rebuilt-rejected | B | recheck | corrected | yes | D |
| Jordan vs Bird - One on One | `2d0b789f04621e4a` | m32-audit-corrected | B | escalation | corrected | yes | C |
| Joshua & the Battle of Jericho | `ec54ed32302aaeb2` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Journey to Silius | `0417d7caec593852` | nes-native-untouched | A | main | corrected | yes | D |
| Joust | `8471a6b7c7c7d6c7` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Jungle Book, The | `e5bcb8838f567f48` | m32-audit-medium-risk | D | escalation | accurate-with-minor-uncertainty |  | C |
| Jurassic Park | `49fe0c49cd0a2841` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Kabuki - Quantum Fighter | `2ae0a99b457a6d00` | nes-native-untouched | A | main | corrected | yes | D |
| Karate Champ | `d67817e44f3a421a` | m32-audit-corrected | B | escalation | accurate-with-minor-uncertainty |  | C |
| Karate Kid, The | `8aa5c70100080adf` | nes-native-untouched | A | main | corrected | yes | D |
| Karnov | `4ece74fde0b86ece` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Kickle Cubicle | `4a9544bb41869e88` | m32-audit-low-risk | B | escalation | accurate |  | D |
| KickMaster | `23bf669afb3125d7` | nes-native-untouched | A | main | corrected | yes | D |
| Kid Icarus | `ee8b884dec3b2c5c` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Kid Klown in Night Mayor World | `a11eec5762a029c9` | m32-audit-corrected | B | escalation | accurate-with-minor-uncertainty |  | C |
| Kid Kool and the Quest for the Seven Wonder Herbs | `f85a689b4ff7a5e5` | nes-native-untouched | A | main | corrected | yes | D |
| Kid Niki - Radical Ninja | `81a77623adf819f1` | m32-audit-medium-risk | B | main | corrected | yes | D |
| King Neptune's Adventure | `6cc3a973288a4c3c` | nes-native-untouched | A | main | corrected | yes | D |
| King of Kings, The - The Early Years | `3249d267ca170980` | m32-recovered-manual | B | escalation | corrected | yes | D |
| King's Knight | `be97bca71e1bf099` | nes-native-untouched | A | main | corrected | yes | D |
| King's Quest V | `997ddca52ac72448` | nes-native-untouched | A | main | corrected | yes | D |
| Kings of the Beach - Professional Beach Volleyball | `913c5eb7d4390d0f` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Kirby's Adventure | `1250a80faf865aa5` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Kiwi Kraze - A Bird-Brained Adventure! | `9caa01a2b81ce1f9` | m32-recovered-manual | B | escalation | accurate |  | D |
| KlashBall | `ef63df6bcbc7e13d` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Klax | `28b3f1dde1488dd8` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Knight Rider | `b66538543d3c4c74` | nes-native-untouched | A | main | corrected | yes | D |
| Krazy Kreatures | `c46f20c85e2f3958` | m32-recovered-manual | B | escalation | accurate |  | D |
| Krion Conquest, The | `e5673075301b0c84` | nes-native-untouched | A | main | corrected | yes | D |
| Krusty's Fun House | `5f981ab1988614a9` | m32-audit-medium-risk | C | escalation | corrected | yes | D |
| Kung Fu | `0ba5e69962f6576d` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Kung-Fu Heroes | `323d330cb50e0a4c` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Laser Invasion | `764b80a6291f138f` | nes-native-untouched | A | main | corrected | yes | D |
| Last Action Hero | `f4b7d8d31c5bbfee` | m32-audit-corrected | B | escalation | corrected | yes | C |
| Last Ninja, The | `f15382be46e474c5` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Last Starfighter, The | `54dd62754f48e39a` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Lee Trevino's Fighting Golf | `ac9d9f60f68c020c` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Legacy of the Wizard | `203dc9486ba58e65` | nes-native-untouched | A | main | corrected | yes | D |
| Legend of Kage, The | `9d83796d8feba971` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Legend of Zelda, The | `ec0d4ebf6d2fcecd` | nes-native-untouched | A | main | corrected | yes | D |
| Legendary Wings | `d2a585ff6febf594` | m32-audit-corrected | B | escalation | rewritten | yes | C |
| Legends of the Diamond - The Baseball Championship Game | `3b5627bc1ebaa7a8` | m32-recovered-manual | B | escalation | accurate |  | D |
| Lemmings | `494aa7d49d3f4d66` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Lethal Weapon | `97c67809c952bc25` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Life Force | `dbd530e6ca07cc4b` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Linus Spacehead's Cosmic Crusade | `54a1949ea7d609bd` | m32-audit-corrected | B | escalation | corrected | yes | C |
| Little League Baseball - Championship Series | `475c35c18b5e9581` | nes-native-untouched | A | main | corrected | yes | D |
| Little Mermaid, The | `68e98b1b8dc5da61` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Little Nemo - The Dream Master | `e95d3557fa60baca` | nes-native-untouched | A | main | corrected | yes | D |
| Little Ninja Brothers | `50badb618accf0ca` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Little Samson | `a5165565263eaf8b` | nes-native-untouched | D | escalation | corrected | yes | C |
| Lode Runner | `6c17c7bf2f7466eb` | m32-audit-corrected | B | recheck | corrected | yes | D |
| Lone Ranger, The | `622ef597b328a204` | nes-native-untouched | A | main | corrected | yes | D |
| Loopz | `fbf9426155adf3b8` | m32-audit-low-risk | B | escalation | corrected | yes | C |
| Low G Man - The Low Gravity Man | `4fb6c5c8359dcbab` | nes-native-untouched | A | main | corrected | yes | D |
| Lunar Pool | `9cdcac614a14e0ae` | nes-native-untouched | A | main | corrected | yes | D |
| M.C. Kids | `52812b55986f96fa` | m32-audit-corrected | B | escalation | corrected | yes | D |
| M.U.L.E. | `345fe1e466b3e8d7` | m32-audit-medium-risk | B | main | corrected | yes | D |
| M.U.S.C.L.E. - Tag Team Match | `d10e4280b6ca8681` | m32-recovered-manual | B | recheck | corrected | yes | D |
| Mach Rider | `944105c6dcc57314` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Mad Max | `80757aa85fe40011` | nes-native-untouched | A | main | corrected | yes | D |
| Mafat Conspiracy, The | `ab0a43bc7c33f0cd` | nes-native-untouched | A | main | corrected | yes | D |
| Magic Carpet 1001 | `df96b560b5cd059c` | m32-recovered-research | A | recheck | corrected | yes | D |
| Magic Darts | `fe622610cd889b35` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Magic Johnson's Fast Break | `e6e010824dad945d` | nes-native-untouched | A | main | corrected | yes | D |
| Magic of Scheherazade, The | `527faba646cd9e95` | nes-native-untouched | A | main | corrected | yes | D |
| Magician | `caae56599689d429` | nes-native-untouched | A | main | corrected | yes | D |
| Magmax | `daa1c7263a804920` | m32-audit-corrected | B | escalation | accurate |  | D |
| Major League Baseball | `6e017c8619f900c0` | nes-native-untouched | A | main | corrected | yes | C |
| Maniac Mansion | `84f5377980d2fd44` | m32-audit-medium-risk | B | main | corrected | yes | C |
| Mappy-Land | `7d07d335a8732059` | m32-audit-corrected | B | escalation | accurate |  | D |
| Marble Madness | `d77ed527cf081019` | m32-audit-low-risk | B | escalation | corrected | yes | C |
| Mario Bros. | `0cc334007d3eae69` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Mario is Missing! | `9babea1d40007f93` | m32-audit-medium-risk | B | main | corrected | yes | C |
| Mario's Time Machine | `c9348c4e30ef0bc6` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Master Chu and the Drunkard Hu | `a385f0bed8d9afd1` | nes-native-untouched | D | recheck | corrected | yes | D |
| Maxi 15 | `2a16f9ebf40db89e` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Mechanized Attack | `c8486cd1a3595000` | m32-recovered-manual | B | recheck | corrected | yes | D |
| Mega Man | `5314ad0c40616119` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Mega Man 2 | `1e588d435e75d80c` | nes-native-untouched | A | main | corrected | yes | C |
| Mega Man 3 | `5b85c1ff632c6ac3` | nes-native-untouched | A | main | corrected | yes | C |
| Mega Man 4 | `ed8bc89c9cd44bf5` | nes-native-untouched | A | main | corrected | yes | D |
| Mega Man 5 | `4ddb728c3a007f1a` | nes-native-untouched | A | main | corrected | yes | D |
| Mega Man 6 | `2037babe50fed7a1` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Menace Beach | `741fbfeeec83073c` | nes-native-untouched | A | main | corrected | yes | C |
| Mendel Palace | `1152961edf8d484f` | nes-native-untouched | A | main | corrected | yes | D |
| Mermaids of Atlantis - The Riddle of the Magic Bubble | `b6be14808b32b300` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Metal Fighter | `1e538078c3c92587` | nes-native-untouched | A | main | corrected | yes | D |
| Metal Gear | `f6d29afbd7ddad33` | nes-native-untouched | A | main | corrected | yes | C |
| Metal Storm | `2fbbc465fc239b48` | nes-native-untouched | C | escalation | corrected | yes | C |
| MetalMech - Man & Machine | `a00c77c313031662` | m32-recovered-manual | B | escalation | accurate |  | D |
| Metroid | `649db8035018f251` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Michael Andretti's World GP | `77c9100f4d3f291d` | m32-audit-corrected | B | escalation | corrected | yes | C |
| Mickey Mousecapade | `989243c99d6e58c4` | nes-native-untouched | A | main | corrected | yes | D |
| Mickey's Adventure in Numberland | `d3b0627673e6dea5` | nes-native-untouched | A | main | rewritten | yes | D |
| Mickey's Safari in Letterland | `e958d5556bdcc873` | nes-native-untouched | A | main | accurate |  | D |
| Micro Machines | `1df64e9554fb9997` | nes-native-untouched | A | main | corrected | yes | C |
| MiG 29 - Soviet Fighter | `5327792d8b5392cd` | m32-audit-corrected | B | escalation | accurate |  | D |
| Might & Magic - Secret of the Inner Sanctum | `c0dea150d9f39127` | nes-native-untouched | A | main | corrected | yes | C |
| Mighty Bomb Jack | `a2b4b0fd54028f1b` | m32-audit-corrected | B | recheck | accurate |  | D |
| Mighty Final Fight | `ef8ccb38760604f5` | m32-audit-medium-risk | C | recheck | corrected | yes | D |
| Mike Tyson's Punch-Out!! | `2ebab487204c42b0` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Millipede | `bd84b27f752aa568` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Milon's Secret Castle | `6eea421f6c0738ff` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Miracle Piano Teaching System, The | `de66c8d971cadb53` | m32-recovered-manual | B | recheck | accurate-with-minor-uncertainty |  | D |
| Mission - Impossible | `6b04b87ab30e8859` | nes-native-untouched | A | main | corrected | yes | D |
| Mission Cobra | `61e4e0a38f23eec3` | nes-native-untouched | A | main | corrected | yes | D |
| Monopoly | `f997fe34edf1d998` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Monster in My Pocket | `08615e9e339bd2df` | m32-recovered-manual | B | escalation | accurate |  | D |
| Monster Party | `219c94a1f0801ee2` | nes-native-untouched | A | main | corrected | yes | D |
| Monster Truck Rally | `9f04b8e867aa8569` | nes-native-untouched | A | main | accurate |  | D |
| Moon Ranger | `66cb74efa264a285` | nes-native-untouched | A | main | rewritten | yes | D |
| Motor City Patrol | `ed219a1b03be92e3` | nes-native-untouched | A | main | corrected | yes | D |
| Ms. Pac-Man | `f7cc35a736ffd780` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Ms. Pac-Man | `3ec56ffa686a0e45` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Muppet Adventure - Chaos at the Carnival | `80f6f2b9cb2669e8` | m32-audit-corrected | B | escalation | accurate |  | D |
| Mutant Virus, The - Crisis in a Computer World | `ac2374bd4a5f7c87` | nes-native-untouched | A | main | corrected | yes | D |
| Mystery Quest | `61cb18d11771cffa` | m32-recovered-manual | B | escalation | accurate |  | D |
| NARC | `dc71dadc3f4eae03` | m32-audit-corrected | B | recheck | corrected | yes | D |
| NES Open Tournament Golf | `55efed163edb02ab` | nes-native-untouched | A | main | accurate |  | D |
| NES Play Action Football | `4b382baa70cbc529` | m32-audit-medium-risk | B | main | corrected | yes | C |
| NFL | `aa0f7f60a6cff146` | nes-native-untouched | A | main | corrected | yes | D |
| Nigel Mansell's World Championship Racing | `c03ffd0d5fb5439e` | nes-native-untouched | A | main | corrected | yes | D |
| Nightmare on Elm Street, A | `58be6a811ee33708` | nes-native-untouched | A | main | corrected | yes | D |
| Nightshade | `02caaf66cc43a4c5` | nes-native-untouched | A | main | corrected | yes | D |
| Ninja Crusaders | `b1c2ae757c5ec76f` | nes-native-untouched | A | main | corrected | yes | D |
| Ninja Gaiden | `6799437d4122b81c` | m32-audit-low-risk | B | recheck | corrected | yes | D |
| Ninja Gaiden II - The Dark Sword of Chaos | `21c51dc47a458a7d` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Ninja Gaiden III - The Ancient Ship of Doom | `ba5968f14a02f1ad` | m32-audit-medium-risk | B | main | corrected | yes | C |
| Ninja Kid | `622c62d48aa244fb` | nes-native-untouched | A | main | rewritten | yes | D |
| Nintendo World Cup | `b0e4d88db0b21db4` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Nobunaga's Ambition | `9d35bccd23f8caf0` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Nobunaga's Ambition II | `ac192342f14b41fe` | nes-native-untouched | A | main | corrected | yes | D |
| North and South | `55c2d10ae1b034b3` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Operation Secret Storm | `106a9cd2acc3373b` | nes-native-untouched | A | main | corrected | yes | D |
| Operation Wolf | `36666e3314ee9e0a` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Orb-3D | `4d2b5339703d2300` | m32-audit-corrected | B | recheck | accurate |  | D |
| Othello | `4a2c4a082e0b735e` | m32-audit-medium-risk | D | escalation | accurate |  | D |
| Overlord | `b3a798ab7f2f237c` | m32-audit-medium-risk | B | main | corrected | yes | D |
| P'Radikus Conflict | `fdafaa5c95e86168` | m32-recovered-manual | B | escalation | corrected | yes | D |
| P.O.W. - Prisoners of War | `ae2dddda1f90b8f8` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Pac-Man | `fa12a61eb787bf83` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Pac-Man | `1e60a181e1f89f22` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Pac-Man | `25506ac6d6413a73` | m32-audit-low-risk | B | recheck | corrected | yes | D |
| Pac-Mania | `acc561f57cfd0490` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Palamedes | `270a759c64b56e72` | nes-native-untouched | A | main | corrected | yes | D |
| Panic Restaurant | `6c47c73eb510fb0f` | nes-native-untouched | A | main | corrected | yes | D |
| Paperboy | `f1c9c4723190d6be` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Paperboy 2 | `dec09033f68850bd` | m32-audit-medium-risk | B | main | rewritten | yes | D |
| Peek-A-Boo Poker | `2503f943a82c59c9` | nes-native-untouched | A | main | corrected | yes | C |
| Pesterminator - The Western Exterminator | `f1762c9f40e6e45a` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Phantom Fighter | `1c598fe0b58811b1` | nes-native-untouched | A | main | rewritten | yes | D |
| Pictionary - The Game of Video Quick Draw | `c701cffa5315b437` | m32-recovered-manual | B | escalation | accurate |  | D |
| Pin Bot | `f4ddb0f1a02f823e` | nes-native-untouched | A | main | corrected | yes | D |
| Pinball | `910ba4505b46a99b` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Pinball Quest | `836a6df9f6885b2b` | nes-native-untouched | A | main | corrected | yes | D |
| Pipe Dream | `a57f873ccb2c8842` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Pirates! | `ae8684d9075b6665` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Platoon | `ac1215af2a3315f8` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Popeye | `97f56ee3bcb05429` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Power Blade | `4986c3862a04fcf5` | nes-native-untouched | A | main | corrected | yes | D |
| Power Blade 2 | `6c462c3fa07aab70` | nes-native-untouched | A | main | corrected | yes | D |
| Power Punch II | `ae360d56679f1791` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Predator | `76cdd991b85e4a15` | nes-native-untouched | A | main | accurate-with-minor-uncertainty |  | D |
| Prince of Persia | `7b0899bfde9661d1` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Princess Tomato in the Salad Kingdom | `99f3ca1657ad48f9` | m32-audit-medium-risk | C | recheck | corrected | yes | D |
| Pro Sport Hockey | `3910ae7495a71e6a` | m32-audit-corrected | B | escalation | corrected | yes | C |
| Pro Wrestling | `e144020f37416f80` | nes-native-untouched | A | main | corrected | yes | C |
| Punch-Out!! | `635271fe654636ec` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Punisher, The | `ea81de1a6d901d8d` | nes-native-untouched | A | main | corrected | yes | D |
| Puss 'n Boots - Pero's Great Adventure | `691ef0807cacd703` | nes-native-untouched | A | main | rewritten | yes | D |
| Puzzle | `88ebfd7a124182cc` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Puzznic | `0cf2fc17a59a0932` | m32-audit-medium-risk | B | main | rewritten | yes | D |
| Pyramid | `8e91630bd2a9f2f2` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Q-bert | `5fa346174b6b5a9d` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| QIX | `745050dec23a692e` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Quattro Adventure | `85d1b9bcfa9e0ffc` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Quattro Sports | `5305f0d6d38893ee` | m32-recovered-manual | B | escalation | corrected | yes | D |
| R.B.I. Baseball | `d4f1650059a01145` | m32-recovered-manual | B | escalation | corrected | yes | D |
| R.B.I. Baseball | `3d725939dfc50523` | m32-recovered-manual | B | recheck | accurate |  | D |
| R.B.I. Baseball 2 | `0df01b1d7b6efe38` | m32-recovered-manual | B | escalation | accurate |  | D |
| R.B.I. Baseball 3 | `0a5ccea3fb83a670` | m32-audit-medium-risk | B | main | corrected | yes | D |
| R.C. Pro-Am | `3f9e3d3b48a897d9` | m32-audit-medium-risk | B | main | corrected | yes | D |
| R.C. Pro-Am II | `730399227c156663` | nes-native-untouched | A | main | corrected | yes | D |
| Racermate Challenge II | `97e84b6cbe198710` | m32-recovered-research | D | recheck | corrected |  | D |
| Racket Attack | `a868544e22d00eac` | nes-native-untouched | A | main | corrected | yes | C |
| Rad Racer | `2e14f4481b5b762b` | nes-native-untouched | A | main | accurate |  | D |
| Rad Racer II | `bcc8a24ab99f8593` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Rad Racket - Deluxe Tennis II | `c6507f7040a61e7b` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Raid 2020 | `d79be89ff83550ba` | nes-native-untouched | A | main | corrected | yes | C |
| Raid on Bungeling Bay | `b7b5fdf2b31c4b8c` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Rainbow Islands | `0a3f0c3795700df2` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Rally Bike | `9866b51b16b503aa` | m32-audit-corrected | B | recheck | corrected | yes | D |
| Rambo | `2596cf1e1a9b09da` | nes-native-untouched | A | main | accurate |  | D |
| Rampage | `bbe150f50bd11f5a` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Rampart | `85e29454e42aecf1` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Remote Control | `2f07815e80fcf807` | m32-recovered-manual | B | escalation | accurate |  | D |
| Ren & Stimpy Show, The - Buckeroo$! | `501e921616931e47` | m32-recovered-research | A | escalation | corrected | yes | D |
| Renegade | `b4856061c9310015` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Rescue - The Embassy Mission | `711dd795f6e24699` | m32-audit-medium-risk | B | main | corrected | yes | C |
| Ring King | `f17df83a3e714c9f` | m32-audit-corrected | B | escalation | corrected | yes | D |
| River City Ransom | `54cdc8b6fab80433` | m32-audit-medium-risk | D | escalation | corrected | yes | D |
| Road Runner | `46a38baf9067869e` | m32-recovered-manual | B | escalation | accurate |  | D |
| RoadBlasters | `46361c5b4007e6ef` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Robin Hood - Prince of Thieves | `687e4129be3b8e22` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Robo Warrior | `5431ed49f22ee818` | m32-audit-medium-risk | D | escalation | corrected | yes | D |
| RoboCop | `d04b445ac57cccb3` | m32-audit-low-risk | B | escalation | accurate |  | D |
| RoboCop 2 | `8ed3cb0046caecbe` | m32-audit-corrected | B | escalation | corrected | yes | D |
| RoboCop 3 | `a0f18e729a7ee6be` | m32-audit-low-risk | B | recheck | corrected | yes | D |
| Robodemons | `d0e5899cf6b756d6` | nes-native-untouched | A | main | corrected | yes | D |
| Rock 'n' Ball | `2bd31df34f925d03` | nes-native-untouched | A | main | corrected | yes | D |
| Rocket Ranger | `a0d60fe16b09d201` | nes-native-untouched | A | main | rewritten | yes | D |
| Rocketeer, The | `e1a3d949b9bf258b` | m32-audit-low-risk | B | escalation | accurate |  | D |
| Rockin' Kats | `683c49e4ea3f4b17` | nes-native-untouched | A | main | corrected | yes | D |
| Roger Clemens' MVP Baseball | `1a35607e218d6810` | m32-audit-corrected | B | escalation | corrected | yes | C |
| Rollerball | `e82f63d59b896c4c` | m32-audit-corrected | B | escalation | accurate-with-minor-uncertainty |  | C |
| Rollerblade Racer | `942c802c34627c96` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Rollergames | `7ddc56bf2b8d8f3f` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Rolling Thunder | `e91bb35c7a95388c` | m32-audit-corrected | B | escalation | corrected | yes | C |
| Romance of the Three Kingdoms | `62fa1b7ec8e92638` | m32-audit-low-risk | B | escalation | accurate |  | D |
| Romance of the Three Kingdoms II | `99c6775ac55cb243` | nes-native-untouched | A | main | corrected | yes | D |
| Roundball - 2-on-2 Challenge | `644291444ae16a02` | nes-native-untouched | A | main | corrected | yes | D |
| Rumble Station - 15 in 1 | `6ce321d32e524eae` | m32-recovered-research | A | escalation | accurate-with-minor-uncertainty |  | C |
| Rush'n Attack | `fade44d9c76173af` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Rygar | `9bef1813dbcfa003` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| S.C.A.T. - Special Cybernetic Attack Team | `9579a2b5a4083420` | nes-native-untouched | D | escalation | accurate |  | D |
| Secret Scout in the Temple of Demise | `7ee2b81ef8ab8e9c` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Section-Z | `a98e48a26c104375` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Seicross | `afc7ce7262cc0303` | m32-audit-medium-risk | B | main | rewritten | yes | D |
| Sesame Street - 123 | `bf24ceb607e46f38` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Sesame Street - ABC | `0d1603daf31ec66c` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Sesame Street - ABC & 123 | `d1d15eca4bf1732e` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Sesame Street - Big Bird's Hide & Speak | `63e867e7ea89b5bf` | m32-recovered-manual | B | escalation | accurate |  | D |
| Sesame Street - Countdown | `c03acbd23ae90b05` | nes-native-untouched | A | main | corrected | yes | D |
| Shadow of the Ninja | `2395a8a102bfc403` | m32-audit-medium-risk | D | recheck | corrected | yes | D |
| Shadowgate | `fbc69cdaf7c14199` | m32-audit-medium-risk | B | main | corrected | yes | C |
| Shatterhand | `c3f361c4a1441101` | nes-native-untouched | D | escalation | corrected | yes | D |
| Shingen the Ruler | `28210e19699c118f` | m32-recovered-manual | B | recheck | corrected | yes | D |
| Shinobi | `2b85e6411f42597c` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Shockwave | `53477d979c71429a` | nes-native-untouched | D | escalation | corrected | yes | D |
| Shooting Range | `6c705f7980a08b7a` | nes-native-untouched | A | main | corrected | yes | D |
| Short Order + Eggsplode! | `ac2b69cf35ece7f0` | m32-recovered-manual | B | recheck | corrected | yes | D |
| Side Pocket | `648f68ae58f34200` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Silent Assault | `69399633df5ea1ba` | nes-native-untouched | A | main | corrected | yes | C |
| Silent Service | `c387805a48a897fc` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Silk Worm | `28f0444e178830ed` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Silver Surfer | `718dce0398dba4f8` | nes-native-untouched | A | main | corrected | yes | C |
| Simpsons, The - Bart vs. the Space Mutants | `7e8a3accbd57c9fe` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Simpsons, The - Bart vs. the World | `678565338a9efa2d` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Simpsons, The - Bartman Meets Radioactive Man | `ce1fd1fcf89d2d63` | nes-native-untouched | A | main | corrected | yes | D |
| Skate or Die | `fe4043430276177b` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Skate or Die 2 - The Search for Double Trouble | `3626bb5ce3035b39` | nes-native-untouched | A | main | corrected | yes | D |
| Ski or Die | `a29461c583ec0390` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Skull & Crossbones | `06fddd300a6d1cbe` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Sky Kid | `51958d12a19e7c57` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Sky Shark | `f845ee19cbe21f5b` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Slalom | `f7f6d714c7758f67` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Smash T.V. | `6dde1137253e7e41` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Snake Rattle n Roll | `98691aea357072f9` | m32-audit-low-risk | B | escalation | accurate |  | D |
| Snake's Revenge | `c85ed147e6da0da5` | nes-native-untouched | A | main | corrected | yes | D |
| Snoopy's Silly Sports Spectacular! | `e16d48f1d703b51c` | m32-audit-corrected | B | recheck | accurate |  | D |
| Snow Brothers | `fbd7caadaf77c2b1` | m32-audit-corrected | B | escalation | accurate |  | D |
| Soccer | `6a0c8be0f6445d9d` | m32-audit-corrected | B | escalation | accurate-with-minor-uncertainty |  | D |
| Solar Jetman - Hunt for the Golden Warpship | `45a0fd35f3141c14` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Solitaire | `abd9923fd2f3ac2b` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Solomon's Key | `842478f0d9f01c6c` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Solstice - The Quest for the Staff of Demnos | `e437dbafa7e20dd7` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Space Shuttle Project | `1c3a2806b546230c` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Spelunker | `6f58c6fb427cf18b` | m32-audit-corrected | B | escalation | accurate-with-minor-uncertainty |  | D |
| Spider-Man - Return of the Sinister Six | `1104d574711a1037` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Spiritual Warfare | `3c74b191d7d4cb6c` | m32-audit-corrected | B | escalation | corrected | yes | C |
| Spot - The Video Game | `299910cc0ddbaa77` | m32-audit-corrected | B | recheck | accurate |  | D |
| Spy Hunter | `17da401495807145` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Spy vs Spy | `91c61066a9b6b5ed` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Sqoon | `687963e7fd128345` | nes-native-untouched | A | main | corrected | yes | D |
| Stack-Up | `af7a31a57dd701f5` | nes-native-untouched | A | main | corrected | yes | D |
| Stadium Events | `ef55073037f94c42` | nes-native-untouched | D | escalation | accurate |  | D |
| Stanley - The Search for Dr. Livingston | `d9a666f7b122bff2` | nes-native-untouched | A | main | corrected | yes | D |
| Star Force | `581851e22e8d499a` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Star Soldier | `025abe5ae4e1610f` | m32-audit-corrected | B | recheck | accurate |  | D |
| Star Trek - 25th Anniversary | `a3d132f118c0d955` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Star Trek - The Next Generation | `8271542561a71830` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Star Voyager | `c4c139d2c349a513` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Star Wars | `b9116f11828e39d3` | nes-native-untouched | A | main | rewritten | yes | D |
| Star Wars - The Empire Strikes Back | `01b1bc93f72286a1` | m32-audit-corrected | B | escalation | rewritten | yes | D |
| Starship Hector | `7b7ccdcda705c2c3` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| StarTropics | `69de2c7552fa81ca` | nes-native-untouched | A | main | corrected | yes | D |
| Stealth ATF | `8093144834f1153e` | nes-native-untouched | A | main | corrected | yes | D |
| Stinger | `5ca7cb3c4168bb02` | nes-native-untouched | A | main | corrected | yes | D |
| Street Cop | `d8a12772ddcb35d6` | m32-recovered-manual | B | recheck | accurate |  | D |
| Street Fighter 2010 - The Final Fight | `885cfd4396e0ade5` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Strider | `d4fc5610f1355c54` | m32-audit-medium-risk | B | main | corrected | yes | C |
| Stunt Kids | `f3a140c16012c4e4` | m32-recovered-manual | B | escalation | rewritten | yes | D |
| Sunday Funday - The Ride | `4d16627301ff5def` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Super C | `3edd803db1cc8815` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Super Cars | `92aa617d6254a7aa` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Super Dodge Ball | `28ce7c337755f30a` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Super Glove Ball | `20de11cda85d7e4b` | nes-native-untouched | A | main | corrected | yes | D |
| Super Gun | `9930cf033982f378` | m32-recovered-research | D | escalation | accurate-with-minor-uncertainty | yes | C |
| Super Jeopardy! | `cf9b320f493edac5` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Super Mario Bros. | `fcb6a0ef3a20c19b` | m32-audit-medium-risk | D | escalation | accurate |  | D |
| Super Mario Bros. + Duck Hunt | `5dde385041aa7364` | m32-audit-medium-risk | D | recheck | corrected | yes | D |
| Super Mario Bros. + Duck Hunt + World Class Track Meet | `26977a6c51a6f1e1` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Super Mario Bros. 2 | `728d0ca6751b0c03` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Super Mario Bros. 3 | `959fdd32c71735d6` | m32-audit-low-risk | B | escalation | accurate-with-minor-uncertainty |  | D |
| Super Pitfall | `fe019a7da7fb7ecd` | m32-audit-low-risk | B | recheck | corrected | yes | D |
| Super Robin Hood | `096b8ef672ff215e` | nes-native-untouched | A | main | rewritten | yes | C |
| Super Spike V'Ball | `3387595598af933f` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Super Spike V'Ball + Nintendo World Cup | `8951dccb6640b48c` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Super Sprint | `d1defc7a6c6f96f8` | m32-audit-corrected | B | recheck | corrected | yes | D |
| Super Spy Hunter | `7806ee6afcdf88ef` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Super Team Games | `54112120e3a5d0a1` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Superman | `9ffc36dfc772e102` | nes-native-untouched | A | main | rewritten | yes | C |
| Swamp Thing | `d138aba5eb5c8cf1` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Sword Master | `553d6db3959c8ae3` | nes-native-untouched | A | main | corrected | yes | D |
| Swords and Serpents | `cdd148ebf43ed891` | nes-native-untouched | A | main | corrected | yes | D |
| Taboo - The Sixth Sense | `798888ffa76d0e52` | nes-native-untouched | A | main | corrected | yes | D |
| Tag Team Wrestling | `b002d1ebee16a5fd` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Tagin' Dragon | `9f63cec07fb4b01c` | nes-native-untouched | A | main | corrected | yes | D |
| TaleSpin | `6d10e95fcbf4cd11` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Target - Renegade | `f6e88113b5f64cf7` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Tecmo Baseball | `044015f2933671f2` | nes-native-untouched | A | main | corrected | yes | C |
| Tecmo Bowl | `d088f4b91a03dd6a` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Tecmo Cup - Soccer Game | `a334d4545d8262de` | nes-native-untouched | A | main | corrected | yes | D |
| Tecmo NBA Basketball | `0b3afd6b8e4fc12f` | m32-audit-corrected | B | recheck | accurate-with-minor-uncertainty |  | C |
| Tecmo Super Bowl | `cd5ad84e6bba35c1` | nes-native-untouched | A | main | corrected | yes | D |
| Tecmo World Wrestling | `daf07341c80a8663` | nes-native-untouched | A | main | corrected | yes | D |
| Teenage Mutant Ninja Turtles | `892468d05a109776` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Teenage Mutant Ninja Turtles - Tournament Fighters | `10e2091a1c5e3ff3` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Teenage Mutant Ninja Turtles II - The Arcade Game | `368d7ea3a066d4dc` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Teenage Mutant Ninja Turtles III - The Manhattan Project | `9f671090ffd2bb1d` | m32-rejected-edited | A | recheck | corrected | yes | D |
| Tennis | `8da7129bba8c61a5` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Terminator 2 - Judgment Day | `6f66d14e5f17ca24` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Terminator, The | `dd89b8e08738243d` | m32-recovered-manual | B | escalation | accurate |  | D |
| Terra Cresta | `667bcf3ca1ebcf6f` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Tetris | `2ae5fb18a1bf8410` | m32-audit-medium-risk | B | main | accurate-with-minor-uncertainty |  | D |
| Tetris | `9daa4bec3013ae7e` | m32-audit-corrected | B | escalation | rewritten | yes | D |
| Tetris 2 | `dd77dc88d3808079` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Three Stooges, The | `700264cfa786a6be` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Thunder & Lightning | `e3be0290f198fcee` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Thunderbirds | `c0c3d7c2682dd2e0` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Thundercade | `c56af43761cb601a` | m32-audit-low-risk | B | escalation | corrected | yes | C |
| Tiger-Heli | `403fe1c4454dc1e2` | m32-audit-medium-risk | B | main | corrected | yes | C |
| Tiles of Fate | `5b0b097beb19f220` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Time Lord | `13353e4e94633eba` | nes-native-untouched | A | main | corrected | yes | D |
| Times of Lore | `d0807dad4f79c978` | m32-audit-medium-risk | B | main | corrected | yes | C |
| Tiny Toon Adventures | `c47a01c9b32642ad` | nes-native-untouched | A | main | corrected | yes | D |
| Tiny Toon Adventures - Cartoon Workshop | `4a991ac38eb235b6` | nes-native-untouched | A | main | corrected | yes | D |
| Tiny Toon Adventures 2 - Trouble in Wackyland | `d4a94fd07859485f` | nes-native-untouched | A | main | corrected | yes | D |
| To the Earth | `44569cef71a0b859` | nes-native-untouched | D | escalation | rewritten | yes | D |
| Toki | `e580f51b06217b10` | m32-audit-corrected | B | recheck | corrected | yes | D |
| Tom & Jerry - The Ultimate Game of Cat and Mouse! | `b49e3b3c2a307e9c` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Tombs & Treasure | `bffa6183b2cff753` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Toobin' | `fe052f7f6b4b41dc` | m32-audit-corrected | B | escalation | corrected | yes | C |
| Top Gun | `e788ae1dff1b8687` | m32-audit-corrected | B | escalation | accurate |  | D |
| Top Gun - The Second Mission | `55375474cdebdb05` | nes-native-untouched | A | main | corrected | yes | D |
| Total Recall | `45067f85328eabb3` | m32-audit-low-risk | B | escalation | rewritten | yes | D |
| Totally Rad | `0bd29c5b583570e1` | nes-native-untouched | A | main | rewritten | yes | D |
| Touchdown Fever | `115fcca17cb77d7c` | m32-audit-corrected | B | recheck | corrected | yes | D |
| Town & Country Surf Designs - Thrilla's Surfari | `cfa1cc0f40cf139b` | nes-native-untouched | A | main | corrected | yes | D |
| Town & Country Surf Designs - Wood & Water Rage | `f3955370fe7ecc99` | nes-native-untouched | A | main | corrected | yes | D |
| Toxic Crusaders | `c3fec0650f170991` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Track & Field | `a2812d80b3057aac` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Track & Field II | `b7c0c863084e6924` | nes-native-untouched | A | main | corrected | yes | D |
| Treasure Master | `58d2f6abe4445dad` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Trog! | `6b2004f30e7de67d` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Trojan | `a5ae39359aa5b169` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Trolls on Treasure Island | `0b5d1465b23e31f2` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Twin Cobra | `e2b26560d326cc62` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Twin Eagle | `8eb6aa7818f1cac8` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Ultima - Exodus | `af5ac55d6283b709` | m32-audit-medium-risk | B | main | corrected | yes | C |
| Ultima - Quest of the Avatar | `bb625b8df6ddbeb9` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Ultima - Warriors of Destiny | `1e8966fd8c9baf57` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Ultimate Air Combat | `8dda0b6bcb5d66fd` | nes-native-untouched | A | main | corrected | yes | D |
| Ultimate Basketball | `bd6797920755a1dc` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Ultimate League Soccer | `4ab711a804e0cfd0` | m32-recovered-manual | B | escalation | accurate |  | D |
| Ultimate Stuntman, The | `f4de2a91acb2eb30` | nes-native-untouched | A | main | corrected | yes | D |
| Uncanny X-Men, The | `c7ad19e926809adc` | nes-native-untouched | A | main | corrected | yes | D |
| Uncharted Waters | `1f368ff0f1d03bfd` | nes-native-untouched | A | main | corrected | yes | D |
| Uninvited | `955abe39d0a45dab` | m32-audit-medium-risk | B | main | corrected | yes | C |
| Untouchables, The | `018f8a4f35d15af7` | m32-audit-corrected | B | escalation | accurate |  | D |
| Urban Champion | `7255ab27932f7c07` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Vegas Dream | `cfbb785f7227e87e` | nes-native-untouched | A | main | corrected | yes | D |
| Venice Beach Volleyball | `10d7e7b387a8960c` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Vice - Project Doom | `433f6bcbd59a9d59` | m32-audit-corrected | B | recheck | corrected | yes | D |
| Videomation | `26ff208f2b133e66` | nes-native-untouched | A | main | corrected | yes | D |
| Vindicators | `8104dc7acdaea42b` | m32-audit-medium-risk | B | main | rewritten | yes | D |
| Volleyball | `2e312fc327c1e569` | nes-native-untouched | A | main | corrected | yes | D |
| Wacky Races | `8589b1dc55e04d23` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Wall Street Kid | `60667bc0ed1c0c63` | nes-native-untouched | A | main | corrected | yes | D |
| Wally Bear and the No! Gang | `50b6f1f87fb14c8f` | nes-native-untouched | A | main | corrected | yes | C |
| Wario's Woods | `c12771e8155b030e` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Wayne Gretzky Hockey | `7ecff2d3e6d34171` | nes-native-untouched | A | main | rewritten | yes | D |
| Wayne's World | `059409954035963f` | m32-recovered-manual | B | escalation | corrected | yes | D |
| WCW World Championship Wrestling | `a00db4c89bfd1270` | nes-native-untouched | A | main | corrected | yes | D |
| Werewolf - The Last Warrior | `ab6f1bbcda6f0c0b` | nes-native-untouched | A | main | rewritten | yes | D |
| Wheel of Fortune | `7157801d42484e58` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Wheel of Fortune - Family Edition | `9178fba5cbb412cc` | m32-recovered-manual | B | escalation | accurate |  | D |
| Wheel of Fortune - Junior Edition | `84e3a018cbaaf231` | m32-recovered-manual | B | escalation | corrected | yes | D |
| Wheel of Fortune Featuring Vanna White | `0dcd6d458d26cda8` | m32-recovered-research | A | escalation | corrected | yes | D |
| Where in Time Is Carmen Sandiego | `768867bd0dc79c85` | m32-audit-corrected | B | escalation | corrected | yes | D |
| Where's Waldo | `a66195e11e38bee9` | nes-native-untouched | A | main | corrected | yes | D |
| Who Framed Roger Rabbit | `700ebc3c2dd27420` | m32-audit-medium-risk | B | main | rewritten | yes | D |
| Whomp 'Em | `b9a4542417439619` | nes-native-untouched | A | main | corrected | yes | D |
| Widget | `9d875583dbdf80a0` | m32-recovered-manual | B | escalation | accurate |  | D |
| Wild Gunman | `62aec65696ecf24a` | m32-audit-corrected | B | escalation | accurate |  | D |
| Willow | `adb1a1a9e853c239` | m32-audit-medium-risk | B | main | accurate-with-minor-uncertainty |  | C |
| Win, Lose or Draw | `818c7648979f86af` | m32-recovered-manual | B | escalation | accurate |  | D |
| Winter Games | `554c74ff0b7ce3c1` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Wizardry - Knight of Diamonds - The Second Scenario | `de5e01c6b46aac2c` | nes-native-untouched | A | main | corrected | yes | D |
| Wizardry - Proving Grounds of the Mad Overlord | `c3136379fc4e9401` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Wizards & Warriors | `d30e480e7a99b5c3` | m32-audit-low-risk | B | escalation | corrected | yes | C |
| Wizards & Warriors III - Kuros...Visions of Power | `2fa3ee5f9ef17dcd` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Wolverine | `2d62114f3f139e8d` | nes-native-untouched | A | main | corrected | yes | D |
| Wonderland Dizzy | `8a604198ba3e2437` | m32-recovered-research | A | escalation | corrected | yes | C |
| World Champ | `05684e63aa981006` | m32-recovered-research | A | escalation | accurate |  | D |
| World Class Track Meet | `ac766a8d99bfd8e9` | nes-native-untouched | D | escalation | corrected | yes | D |
| World Games | `6449c6d072fd1df1` | m32-recovered-manual | B | escalation | accurate |  | D |
| Wrath of the Black Manta | `3950d679fd6bbd5b` | nes-native-untouched | A | main | corrected | yes | D |
| Wrecking Crew | `f1eb29dd1c7b2b29` | m32-audit-low-risk | B | recheck | corrected | yes | D |
| Wurm - Journey to the Center of the Earth! | `a0354fb9c1c29e25` | nes-native-untouched | A | main | corrected | yes | D |
| WWF King of the Ring | `e5de4656729ffa37` | m32-audit-low-risk | B | escalation | accurate-with-minor-uncertainty |  | D |
| WWF WrestleMania | `a6f98e57ffb45441` | m32-rejected-edited | A | recheck | corrected | yes | D |
| WWF WrestleMania - Challenge | `89df74e7b275929c` | nes-native-untouched | A | main | corrected | yes | D |
| WWF WrestleMania - Steel Cage Challenge | `b0e4bcb63416c32f` | m32-audit-low-risk | B | escalation | corrected | yes | D |
| Xenophobe | `4b11689be770a7dd` | m32-audit-medium-risk | B | main | rewritten | yes | D |
| Xevious - The Avenger | `9112f5b30c8a8d0d` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Xexyz | `2508e4eada73f233` | nes-native-untouched | A | main | corrected | yes | C |
| Yo! Noid | `62a3551ce546fa7d` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Yoshi | `56a169bb3a610105` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Yoshi's Cookie | `24b117a0830fcb91` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Young Indiana Jones Chronicles, The | `fd4884c98d9412eb` | nes-native-untouched | C | escalation | corrected | yes | D |
| Zanac | `bdc9dfed1b03db47` | m32-audit-medium-risk | B | main | corrected | yes | D |
| Zelda II - The Adventure of Link | `4cfc55e1521e5803` | nes-native-untouched | A | main | accurate |  | D |
| Zen - Intergalactic Ninja | `939dcb88fce04f9c` | nes-native-untouched | A | main | rewritten | yes | D |
| Zoda's Revenge - StarTropics II | `d0850075065ecbd1` | m32-audit-corrected | B | escalation | corrected | yes | C |
| Zombie Nation | `91eae4e0e59dadd5` | nes-native-untouched | A | main | corrected | yes | C |
