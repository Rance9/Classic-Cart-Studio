# M3.4 remaining exceptions

All 773 records were independently audited. None of the items below is a known factual error in a published text. They are open decisions or claims that are only partly corroborated.

## 1. Unresolved: Exploding Fist (`a3049fe0daabefe4`)

- **Release:** the ROM's own text identifies it as Beam Software's NES conversion, released on cartridge by Piko Interactive. Its credits name both, and the catalog's mapper 268 image matches a modern cartridge.
- **Evidence:** there is no NES manual in the collection, and no NES-specific review or guide was reachable. Every gameplay claim in the current overview comes from the article about the computer original: the referee, the yin-yang scoring and the move list. The ROM text shows only generic words (BONUS, WINS, LOSE, GAME OVER, LEVEL).
- **Current state:** the text is kept unchanged, marked unresolved and not counted as verified. It is not known to be wrong, but its specific mechanics are unconfirmed for this release.
- **Owner decision:** keep it as is, generalize it to "a one-on-one martial arts fighting game" (about 40 words), or withhold it (772 of 773 shown) until an NES source is found.

## 2. Naming questions (owner decision)

The audit followed the US NES manual, and the game's on-screen text where the two differ. These cases are worth a decision:

- **Super Mario Bros. 3:** the manual spells "Racoon Mario". The text keeps "Raccoon Mario"; the owner may prefer the manual's spelling.
- **Mega Man 2:** "Dr. Wily" follows the game's own text, while the manual prints "Dr. Wiley". Mega Man 5 uses "Protoman", as both the manual and the game spell it.
- **Destiny of an Emperor:** "Liu Bei", from a Nintendo Power-based guide; the manual's "Lui Bei" appears to be a misprint.
- **The Flintstones – The Rescue of Dino & Hoppy:** the manual's "Dr. Turner". A review and the encyclopedia say "Dr. Butler", but the game's text could not be read (compressed), so the manual name stays.
- **Manual spellings that look like misprints but are kept:**
  - "Abatros" (Bionic Commando);
  - "Wiley Coyote" (Bugs Bunny Crazy Castle);
  - "Centurians" (Kid Icarus);
  - "Michaelangelo" (TMNT);
  - "Medussa" (Skull & Crossbones);
  - "Morica" (Venice Beach Volleyball; also on screen).
- **Castlevania:** "Simon Belmont" and "Dracula" are kept from an NES review; the US manual says only "the Evil Count".

## 3. Official description and overview use different names (owner decision)

Following the Adventure Island precedent, the overview now uses the US release's names where the Nintendo of Europe official excerpt uses others. None of these is a factual contradiction, but readers see both names on one page:

| Game | Official excerpt | Game overview |
|---|---|---|
| Adventures of Lolo 2 | Great Devil | King of Eggerland (US manual) |
| Ghosts'n Goblins | Arthur | the Knight (US manual) |
| Final Fantasy | Warriors of Light | Light Warriors (US manual and on-screen text) |
| Mega Man 5 | Proto Man | Protoman (US manual and on-screen text) |
| Metroid | Varia Suit | Varia (US manual) |
| Donkey Kong | Jumpman | Mario only (US manual) |
| The Adventures of Bayou Billy | Billy West | Bayou Billy (US manual) |
| Flying Warriors | Rick Stalker | Rick (US manual) |
| Faxanadu | Elven | the US manual's terms |

**Proposed default:** leave these as they are. Lolo 2 is the closest to the Adventure Island case, because the villain's name differs; the owner may want the same treatment there, omitting the official sentence that names the Great Devil.

## 4. Claims kept with partial corroboration (level C, 87 records)

None is contradicted. The table at the end lists each record and its partly corroborated claims. Typical examples:
- a scroll direction or count supported by one NES review or run note;
- a record without a manual that rests on NES-specific web sources;
- a low-resolution scan where one detail could not be read with certainty.

## 5. Future compatibility note (from M3.3, unchanged)

Racermate Challenge II needs the RacerMate/CompuTrainer hardware. This is recorded only; installability and launch behavior are unchanged.

## Records with remaining uncertainty (generated)

98 records keep at least one claim that is only partly corroborated, or are unresolved. None of these claims is contradicted by NES evidence.

| Game | ID | Verdict | Level | Remaining uncertainty |
|---|---|---|---|---|
| 1942 | `e868400409c70876` | corrected | C | Midair collision costing a life is corroborated only by one NES review ('avoiding collisions and enemy fire'), not the manual. 'Pacific Theater' rests on Tokyo objective plus HG101's US-to-Japan route description. |
| Advanced Dungeons & Dragons - Pool of Radiance | `38e5253131d26439` | corrected | C | 'Forgotten Realms' rests on the manual's references to 'the Realms' rather than the full name; the council recruitment framing and the final encounter with the evil leader rest on one NES review (Flying Omelette). |
| Adventure Island | `53bfc94fce46a251` | corrected | C | The stronger weapon is called 'Fireballs' by Nintendo Life but 'Rocks' in the Dalez GameFAQs guide; the manual does not list it. Extra lives in hidden eggs and the standing-still bonus stages rest on the single Dalez FAQ. |
| Adventure Island 3 | `e8dc8c0441c54d09` | corrected | C | Losing the dinosaur when hit rests on an inference from one GameFAQs NES guide; the manual does not describe it. |
| Adventures of Dino Riki | `58d0f0e504b8450e` | corrected | C | 'Vertically scrolling' rests on a search snippet and a review rather than the manual; 'taking hits downgrades weapons' rests on one review. |
| Adventures of Lolo 2 | `12d3119e25f7b40a` | corrected | C | The four Pro rooms are confirmed only by a StrategyWiki search snippet (page itself returned 403). |
| Alfred Chicken | `4b088e1e78981308` | corrected | C | 'giant flower' and 'bouncing' bombs rest on a manual screenshot and an NES review rather than manual text. |
| Anticipation | `427876021e6c0774` | corrected | C | 25-second answer time and the four-color requirement rest on one NES review site (raregamer.co.uk), not the manual. 'Die counts down from six' is inferred from the manual's die and countdown. |
| Asder - 20-in-1 | `19a0cae21557cb7f` | corrected | C | No manual exists; every claim rests on one fan wiki (BootlegGames), with regvault.org repeating it. Region/edition match between the wiki's Famicom-listed cart and the No-Intro USA dump is assumed. |
| Bad Street Brawler | `98658d5c8e530994` | corrected | C | Pre-stage quotes rest on one NES review (GamesBeat) plus the encyclopedia. 'Yellow outfit' rests on the GamesBeat 'yellow leotard' and the orange-yellow cover art; the in-game sprite was not inspected. The 'Duke Dunnegan' box name was not verified. |
| Barbie | `3f8a65278efb20df` | corrected | C | Soda Shop world and end-of-section bosses come from NES articles (Time Extension, GoCollect) read through fetched summaries, not from the manual. The Rev 1 cartridge differences are not documented. |
| Baseball | `f22944452be3259a` | accurate-with-minor-uncertainty | D | The 'only by uniform color' detail rests on one NES review's 'appear to be no real differences in how they perform'; teams are distinguished on screen by initials and colors. |
| Big Nose the Caveman | `d290ac95ca512a37` | accurate-with-minor-uncertainty | C | The How To Play pages (booklet pp.3-4) are missing from the scan, so the hazard list, one-hit rule and checkpoint rule rest on a single NES-specific source (TASVideos submission text, which the encyclopedia article appears to share); island names rest on a code-guide page, not the manual. |
| BMX Simulator | `85f7be325689329e` | accurate-with-minor-uncertainty | C | Documentation is for the Quattro Sports multicart, not the standalone (World) image; the one-or-two-player limit and the high score name entry rest on the earlier ROM/byte-comparison evidence recorded in history, not re-verified here. |
| BreakThru | `e1487d23800aa2c2` | accurate-with-minor-uncertainty | D | The manual says landmines must be avoided and does not state explicitly that they can be jumped; the text lists them among hazards cleared by jumping. |
| Bubble Bath Babes | `3bcce1ff03b55c20` | accurate-with-minor-uncertainty | C | Chain-reaction rise and the bottom-of-screen game-over rule rest only on the encyclopedia article; the instruction sheet is titled 'The Megica Bubble' and does not name this release. |
| Bubble Bobble | `31523322db8f94e7` | corrected | C | The bottom-to-top wraparound and the 'foes move faster' part of the time limit rest on StrategyWiki series pages plus its statement that the NES conversion is very close to the arcade original, not on an NES-only source. The 'two worlds of 113 levels each' wording is the manual's; StrategyWiki's NES page describes 113 levels with a second 'Super' mode, which is consistent but worded differently. |
| Caesars Palace | `cfea236e0d0fe0e4` | accurate-with-minor-uncertainty | C | The manual scan is low resolution; small words were read from blurred page images. Ride-home outcomes other than the bus were not directly confirmed in a source I could open (RetroAchievements live page 403; archived copy read). The credits page heading appears to mention the Game Boy, likely a reused heading; the rest of the manual is NES-specific. |
| Captain Comic - The Adventure | `935aa637ca8fb0ec` | corrected | C | 'side-scrolling' rests on a weak TASVideos description and the left/right controls; no NES-specific guide was reachable (web.archive.org fetch blocked). |
| Castlevania | `7eba1637cd2fdbc4` | corrected | C | Simon Belmont and Dracula are not named in the US manual (it says 'the Evil Count'); kept on an NES review source. Spiked ceilings rest on a speedrun guide's 'spiked crushers'. |
| Cheetahmen II | `ebdd748cd488c4ac` | corrected | C | No manual. Claims rest on TASVideos pages and one review; Dr. Morbis being the Apollo boss is inferred from the TAS level notes (Level 2) plus the stated character order. TCRF not consulted. |
| Commando | `0512b4aa2220f74e` | rewritten | C | Alternating two-player play is inferred from the manual's statement that Controller 1 moves Super Joe in both 1- and 2-player games. Whether the NES version keeps the arcade's end-of-stage gate and upward-only grenades was not verified, so those details were removed. |
| Deja Vu | `87850226dc6c4c52` | corrected | C | Second source is a StrategyWiki walkthrough that covers all versions with explicit NES notes; story details (Chicago, framing, kidnapping) are assumed shared by the NES version, which StrategyWiki says keeps the same storyline. 'Boxing skills' rests on boxer pictures and the HIT command rather than an explicit statement. |
| Dr. Jekyll and Mr. Hyde | `5860e217030f9da9` | corrected | C | The six-stage count and stage settings rest on one review site (classic-games.net), which may draw on the same encyclopedia article. TCRF appeared in search results but was not used. |
| Duck Hunt | `7026334a7e8742b6` | corrected | C | Kill-screen sentence rests on one secondary article that does not distinguish Game A, B and C behavior after round 99. Ten-target count for Game C not stated explicitly in the manual. |
| DuckTales | `8ba8baed01a9fbaf` | corrected | C | Ending-by-earnings and final-confrontation details rest on a single TASVideos submission; 'hinder progress' and 'reach high platforms' are only loosely supported. |
| Eliminator Boat Duel | `9d0396286f2c0273` | corrected | C | Which rivals are faced on Normal and Expert is not verified (text says "such as"). 'Primarily overhead' and 'multidirectional scrolling' are only loosely supported by manual screenshots. |
| Exploding Fist | `a3049fe0daabefe4` | unresolved | unresolved | Every gameplay claim is unverified for the NES. Needs the Piko Interactive manual or an NES longplay/screenshots to confirm the referee, yin-yang scoring, move set and stage progression. Release identity of the No-Intro 'Exploding Fist (USA)' dump (leaked prototype vs. finished version) is also unconfirmed. |
| Freedom Force | `995b57f7d2c68a56` | corrected | C | Bonus-round timing (after every two levels), automatic scrolling and the lower-right position of the item box rest on one NES review (nintendoisgreat.substack.com) and are not stated in the manual. |
| Golgo 13 - Top Secret Episode | `9f559f83b9b51791` | corrected | C | 'Several acts' rests on an NES cheat-code page quoting act titles rather than the manual. Rio de Janeiro and Alexander Island may well be correct but were removed for lack of independent NES evidence. |
| Greg Norman's Golf Power | `978eb3b632927b65` | accurate-with-minor-uncertainty | D | The NES manual's COURSE section lists ten courses (USA, Japan, England, Scotland, Germany, Spain, France, Australia, Hawaii, User Course), while its own in-game screenshot shows only U.S.A, Japan, England, Scotland and User Course; the ROM was not checked directly. The Four Score is not named in the manual. |
| Ikari Warriors II - Victory Road | `119f865684be58f3` | accurate-with-minor-uncertainty | C | 'alien' rests on a search-result snippet of an NES game page (the page returned 403) and a review's wording; 'mysterious storm' paraphrases the manual's blackened sky and time warp. |
| Impossible Mission II | `a47f338a88835424` | corrected | C | The manual says its robot pictures come from the Commodore version and that robots in other versions may vary, so the exact robot roster of the NES release is not independently confirmed. |
| Indiana Jones and the Temple of Doom | `3a7726a787a6d8ea` | corrected | C | The manual scan is too low resolution to read with certainty; the loss of items collected on the wave at time-out is read from two blurry manual passages only (Take on the NES Library mentions only the lost life and restarting the wave). Manual is the Mindscape reissue, not the Tengen V1.1 booklet. |
| Infiltrator | `0bb401bbd0cae275` | rewritten | C | Manual scan is very low resolution; disguise, missiles/flares/chaff, three missions, time limit and password were read from blurry page images and only partly cross-checked (arcade-history confirms helicopter, protagonist, Mad Leader and timed ground missions). Mission-specific names (scientist, chemical canisters) were unreadable and left out. |
| Jordan vs Bird - One on One | `2d0b789f04621e4a` | corrected | C | 'Bulls' and 'Celtics' are real-world facts; the manual gives only the drafting cities. |
| Jungle Book, The | `e5bcb8838f567f48` | accurate-with-minor-uncertainty | C | Single-player and 'climbing vines' are not stated explicitly (manual: jump from a vine; Options page has no player-count option). |
| Karate Champ | `d67817e44f3a421a` | accurate-with-minor-uncertainty | C | The game-over-on-loss rule rests on StrategyWiki's statement that the NES conversion follows the arcade rules; the manual is silent on it. Manual does not say 'gi' explicitly (only white and red players). Clock and referee rules on manual page 5 were read through OCR (clear text). |
| Kid Klown in Night Mayor World | `a11eec5762a029c9` | accurate-with-minor-uncertainty | C | Night Mayor as the final opponent rests on StrategyWiki (text close to the encyclopedia) plus the manual's story and stage map; no independent NES walkthrough was read. Controller and special-move claims (strike, float over wide gaps, balloon bounce) rest on legible OCR of pages 7-13; those page images were not rendered. |
| Last Action Hero | `f4b7d8d31c5bbfee` | corrected | C | Hamlet stage as 'Danny Madigan's interpretation' rests on the encyclopedia plus the manual's Claudius scene. Scene order: manual lists Death as Scene 7 'The Finale' while the TAS commentary puts the Death level before a final theater level; text avoids claiming either is last. |
| Legendary Wings | `d2a585ff6febf594` | rewritten | C | 'A dragon appears as a recurring boss' rests on an NES review ('a dragon in every stage') and the encyclopedia's NES-differences note; the manual only lists Dragon among enemies. |
| Linus Spacehead's Cosmic Crusade | `54a1949ea7d609bd` | corrected | C | Detroitica, the bumper car event, fake license, teleporter side effects and the ten-candy rule rest on one NES TAS submission write-up (tasvideos.org/2965S), not the manual. 'Avoid falling' in arcade sections is inferred from platform play, not stated. |
| Little Samson | `a5165565263eaf8b` | corrected | C | Exact stage count (HG101 says approximately twenty) and the extent of branching paths rest on review/TAS notes; password timing unconfirmed (one cheat page says codes appear at the start of most levels). 'Across linear progression' is loosely worded but kept. |
| Loopz | `fbf9426155adf3b8` | corrected | C | The scan lacks the manual's pages 2-3; the corner-tile description rests on the hint about straight pieces plus the logic of closed loops, not an explicit NES statement. |
| Major League Baseball | `6e017c8619f900c0` | corrected | C | That players are shown by number and statistics rather than by name rests on the in-game screenshot (number and batting average) and an encyclopedia summary; the manual's player-selection text mentions a player's name. No independent NES review reachable confirmed it. |
| Maniac Mansion | `84f5377980d2fd44` | corrected | C | Surname 'Miller' and the count of five endings rest on one NES review (infinityretro), not the manual. strategywiki returned 403 and web.archive.org was unreachable. |
| Marble Madness | `d77ed527cf081019` | corrected | C | Independent per-player clocks in two-player mode are inferred from NES speedrun notes about the winner's five-second bonus, not stated outright. |
| Mario is Missing! | `9babea1d40007f93` | corrected | C | Pokeys and the disguised-Koopa final battle rest on one NES-specific source (Mario Wiki NES page); the manual does not mention them. 'To prevent interference' is an inference from the manual's story text. |
| Mega Man 2 | `1e588d435e75d80c` | corrected | C | 'Dr. Wily' kept from the release's on-screen intro although the US manual prints 'Dr. Wiley'. 'Robot Master' is not a US-manual term (manual: enemy robots). Intro text and password details come from search excerpts; megamanwiki and the Fandom wiki were unreachable (403/402). |
| Mega Man 3 | `5b85c1ff632c6ac3` | corrected | C | Break Man, the Doc Robot stages and the tank effect rest on web sources, not the manual; strategywiki, megamanwiki and Fandom pages were seen only as search excerpts. 'Periodic duels ... open passageways' is confirmed for the Break Man stage before the fortress; that the earlier encounters also open paths is less certain. |
| Menace Beach | `741fbfeeec83073c` | corrected | C | The clothing deterioration is not in the manual. It rests on Wikipedia plus one differently worded search excerpt; mobygames and tvtropes returned 403. |
| Metal Gear | `f6d29afbd7ddad33` | corrected | C | Alert details and resistance contacts come from StrategyWiki search excerpts (the pages returned 403). The NES game never shows Metal Gear itself; 'nuclear-armed bipedal mecha' rests on Take on the NES Library's description. The count of five strongholds is the manual's and was not checked against gameplay. |
| Metal Storm | `2fbbc465fc239b48` | corrected | C | The two-acts-per-stage structure rests on search snippets (stage numbering 1-2, 2-2, 4-2) rather than a fully read page; the manual pictures two screens for each stage but does not state it. On-screen name of the expert mode not verified. |
| Michael Andretti's World GP | `77c9100f4d3f291d` | corrected | C | That the rival drivers are pseudonyms of real drivers is inferred from the altered spellings in the manual, not stated outright. |
| Micro Machines | `1df64e9554fb9997` | corrected | C | 'Top-down' view not explicitly stated in the manual (generic, not contradicted). The off-course reset rests on one NES review that describes being sent back after leaving the track too long. |
| Might & Magic - Secret of the Inner Sanctum | `c0dea150d9f39127` | corrected | C | The impostor King rests on a TASVideos NES submission ('Fake Alamar'); castles rest on the same source. Corak and Sheltem may well appear in the NES script but no NES-specific source was reached. |
| Miracle Piano Teaching System, The | `de66c8d971cadb53` | accurate-with-minor-uncertainty | D | Most checks used clean OCR of a 197-page typeset manual; the count (36 Sections) and the age screen were confirmed on the page image. 'explain musical concepts' for chalkboards is a paraphrase. |
| NES Play Action Football | `4b382baa70cbc529` | corrected | C | Nester's role in announcing the winner at the end of a game could not be confirmed; the manual says only 'ending scenes will be shown', and the second sources could not be opened. The first-down/touchdown voice rests on a search-result quote from nesguide.com. |
| Ninja Gaiden III - The Ancient Ship of Doom | `ba5968f14a02f1ad` | corrected | C | Losing a life by falling into a pit is not stated in the manual and no NES-specific source was reached (StrategyWiki 403, archive.org blocked); it is genre-standard and not contradicted. |
| Peek-A-Boo Poker | `2503f943a82c59c9` | corrected | C | No manual; on-screen spelling of 'Debbie' not seen directly (three web sources agree on 'Debbie', Wikipedia says 'Debby'). The $500 start and per-$1000 pictures rest on one NES Files forum review. |
| Predator | `76cdd991b85e4a15` | accurate-with-minor-uncertainty | D | The manual does not say whether Dutch starts each jungle stage unarmed or only the first; 'initial stages' is consistent but not explicitly confirmed. The text omits the Big Mode/Ethereal Zone sections, which is an omission, not an error. |
| Pro Sport Hockey | `3910ae7495a71e6a` | corrected | C | 1992-93 season rests on one NES review (questicle.net), consistent with the (c) 1993 manual. Booklet pages 22-27 are missing from the scan, so the full FOULS section (possibly misconducts or penalty lengths) could not be read. |
| Pro Wrestling | `e144020f37416f80` | corrected | C | The referee running over and lying on the mat is supported only by NESguide text that repeats the encyclopedia; the manual scan lacks the Controlling Wrestlers pages (8-11), so move details rest on StrategyWiki. Rev 1 versus Rev 0 is not shown by the manual. |
| Racket Attack | `a868544e22d00eac` | corrected | C | The view description rests on two small MobyGames screenshots; the view shift during serves is inferred from serve versus rally framing. The line-judge protest may exist in the game but could not be confirmed independently. |
| Raid 2020 | `d79be89ff83550ba` | corrected | C | Whether Technopolis and New L.A. are actual stage locations is inferred from the manual's item text, not stated. Shadow may be able to jump (encyclopedia says so) but no readable NES-specific source confirmed it. |
| Rescue - The Embassy Mission | `711dd795f6e24699` | corrected | C | Page images were not inspected (OCR is clean and no disputed name or number depends on it). The end-of-mission screen rests on a single NES review (Just Games Retro). |
| Roger Clemens' MVP Baseball | `1a35607e218d6810` | corrected | C | Fictional-name modeling rests on the manual roster image plus an NES-site search snippet (page itself returned 403). Season counts and 26 teams are from the manual. |
| Rollerball | `e82f63d59b896c4c` | accurate-with-minor-uncertainty | C | The ocean backdrop, the Empire State Building identification and the 'two real tables' comparison rest on the StrategyWiki NES page, which shares wording with the encyclopedia article. |
| Rolling Thunder | `e91bb35c7a95388c` | corrected | C | The hero's name Albatross is not in the NES manual; it is supported by two NES reviews but may originate from the franchise; on-screen use in the NES game not verified. |
| Rumble Station - 15 in 1 | `6ce321d32e524eae` | accurate-with-minor-uncertainty | C | Only one source (BootlegGames Wiki); no manual. Menu titles in the ROM itself not verified. |
| Shadowgate | `fbc69cdaf7c14199` | corrected | C | Lakmir and the Staff of Ages are not in the manual; they rest on one NES review (VGJUNK), with TotNL silent on them. Whether Lakmir is called wizard or druid in the NES text was not confirmed. |
| Silent Assault | `69399633df5ea1ba` | corrected | C | Stage count: the manual digit is blurred (6 or 8); 8 rests on TASVideos. Whether every stage ends in a boss is unconfirmed (text says only that bosses appear). |
| Silver Surfer | `718dce0398dba4f8` | corrected | C | That a single final stage follows the five worlds rests on the manual's 'sixth piece' hint plus one NES review stating six levels; its content was not confirmed. |
| Soccer | `6a0c8be0f6445d9d` | accurate-with-minor-uncertainty | D | The exact half lengths (15/30/45) are unreadable in the manual screenshot; they rest on the screenshot's three options plus the NES review's 15-minute half and 6-20-minute match range. |
| Spelunker | `6f58c6fb427cf18b` | accurate-with-minor-uncertainty | D | Steam vents are not in the manual; supported by a NES (Virtual Console) review. Starting at the top is implied by the manual, not stated. |
| Spiritual Warfare | `3c74b191d7d4cb6c` | corrected | C | 'A Christian' is implied, not stated. Location list is corroborated by TASVideos forum route notes and a review, not by the manual. Second-source pages were read through a summarizing fetch. |
| Strider | `d4fc5610f1355c54` | corrected | C | Level-up effects (more energy) rest on one NES review (HG101); Kazakh as the first area rests on TASVideos; web.archive.org and StrategyWiki were unreachable. |
| Super Gun | `9930cf033982f378` | accurate-with-minor-uncertainty | C | Power-ups rest on two fan wikis only. Automatic scrolling rests on M3.3 code analysis and a fan wiki, not emulator observation. Whether the radio exchange plays as the opening intro (versus an attract sequence) was not observed. Fan-wiki gameplay details may describe the NTDEC cartridge rather than this Caltron dump. |
| Super Mario Bros. 3 | `959fdd32c71735d6` | accurate-with-minor-uncertainty | D | The US manual spells the form 'Racoon Mario' throughout; the text keeps the standard spelling 'Raccoon Mario' (same name, also used in the official description). The owner may prefer the manual's literal spelling. The princess's capture and rescue rest on Super Mario Wiki, not the manual. |
| Super Robin Hood | `096b8ef672ff215e` | rewritten | C | No manual; all claims rest on a single TASVideos NES discussion plus the developers' site. Exact on-screen spelling of Marian and the full enemy roster are unconfirmed. |
| Superman | `9ffc36dfc772e102` | rewritten | C | Boss naming conflict: US manual character page shows 'Zoara' and 'Zadda Trio' while web sources say Ursa, Non and General Zod; the mapping is unconfirmed, so names were omitted. Kryptonian-exile framing rests on Den of Geek and encyclopedia summaries. |
| Tecmo Baseball | `044015f2933671f2` | corrected | C | That player names and statistics parallel real major leaguers is stated only by the encyclopedia article; the manual's rosters and stats are consistent with it but do not state it. |
| Tecmo NBA Basketball | `0b3afd6b8e4fc12f` | accurate-with-minor-uncertainty | C | Cinema-style cutscenes are not in the manual; corroborated only by an NES user forum post (other web hits describe the SNES sequel or repeat the encyclopedia). 'Challenge the Eastern or Western Conference rosters' is loose phrasing for choosing a conference side in an East-West match. |
| Tetris | `2ae5fb18a1bf8410` | accurate-with-minor-uncertainty | D | 'Every ten cleared lines advances the level' holds from low starting levels; from a high starting level the first advance takes more lines (10 x target level total). |
| Thundercade | `c56af43761cb601a` | corrected | C | 'Vertically scrolling' is not stated in the manual; it rests on a search-result summary and the manual's overhead art and movement description. |
| Tiger-Heli | `403fe1c4454dc1e2` | corrected | C | The hidden continue (hold A and B on the Game Over screen) is supported only by the encyclopedia text; kept in general form. |
| Times of Lore | `d0807dad4f79c978` | corrected | C | 'Uncovering a conspiracy' rests on an NES walkthrough and review (assassination of the High King) rather than the manual. Treela's description as a town rests on one NES walkthrough. StrategyWiki was unreachable. |
| Toobin' | `fe052f7f6b4b41dc` | corrected | C | 'Vertically scrolling' rests on a search snippet of an NES review (page blocked) plus the manual's left/right can throws; very likely correct but not read from a full page. NES enemy types were not confirmed in detail. One NES review (questicle.net) lists 1-2 simultaneous players, but the manual states two-player games alternate turns, with simultaneous play only in the shared-control special mode |
| Ultima - Exodus | `af5ac55d6283b709` | corrected | C | Bribing and the shrine cards rest on one NES walkthrough (mikesrpgcenter); exact role of Lord British in leveling differs between manual (max HP) and walkthrough (level), so text kept general. |
| Uninvited | `955abe39d0a45dab` | corrected | C | Manual covers only the opening story and commands (p03 image checked); Dracan, locations, monsters and the ruby rule rest on NES reviews (HG101, honestgamers, questicle). StrategyWiki 403. |
| Wally Bear and the No! Gang | `50b6f1f87fb14c8f` | corrected | C | Level settings rest on one NES review plus manual mentions (subway, parking structure, sewer); 'members of his group to talk about hazards' rests on review summaries. Page images not opened because OCR was clean; names checked in OCR. |
| Willow | `adb1a1a9e853c239` | accurate-with-minor-uncertainty | C | 'Willow locates all necessary equipment ... rather than purchasing it through a currency system' is consistent with the manual (no money on any status screen, no shops described) but only explicitly stated by the encyclopedia article the text came from. |
| Wizards & Warriors | `d30e480e7a99b5c3` | corrected | C | End-of-stage bosses rest on one NES review plus the manual's Black Magic description; the final defeat of Malkil rests on a StrategyWiki search snippet and the manual's points table (the page itself returned 403). Manual is not revision-specific. |
| Wonderland Dizzy | `8a604198ba3e2437` | corrected | C | No manual exists for this release. 'Evil wizard' for Zaks is supported by series sources (Magicland Dizzy) rather than a page about this exact NES release. Platforming is generic and not stated explicitly on the developer page. |
| WWF King of the Ring | `e5de4656729ffa37` | accurate-with-minor-uncertainty | D | 'Identical maneuvers' is inferred from the manual's single shared move chart; 'exhibition' is a descriptive term, not the manual's mode name. |
| Xexyz | `2508e4eada73f233` | corrected | C | The manual scan is low resolution; names were read from the images but small text is hard to read. 'Auto-scrolling side-view' for the even stages is not explicitly confirmed by HG101 (it says pure shoot-em-up stages outdoors). |
| Zoda's Revenge - StarTropics II | `d0850075065ecbd1` | corrected | C | Tetrads, clones, nine chapters, eras and movement rest on fan and review sites (HG101, startropics.com, Flying Omelette, HonestGamers), not the manual. 'Eight-directional' is inferred from 'walk and leap diagonally'. |
| Zombie Nation | `91eae4e0e59dadd5` | corrected | C | Being crushed by the scrolling screen is reported only in a Giant Bomb search snippet (page 403) and the encyclopedia article; kept because no contradiction was found. |
