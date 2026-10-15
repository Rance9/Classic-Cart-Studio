# M3.2 review verdicts (62-game sample)

**Reviewer:** the agent. Each text was read as displayed and compared with its NES-specific evidence. That is the NES manual's OCR text where one exists (the parts relevant to the claims were opened), otherwise the researched sources. These are not owner verdicts; **the owner's own review is still open**.

**What was checked for each game:**
1. the canonical game identity;
2. that it describes the NES release;
3. that every concrete claim is supported;
4. that nothing comes from another version;
5. neutral wording;
6. natural reading;
7. useful information rather than filler;
8. no identity mix-up.

**Beyond the sample**, the agent also read all 117 recovered and rebuilt overviews in full (`M3-2-RECOVERED-GAMES.md` lists them). It ran two systematic checks over every new text:
- A **name check** listed every proper name that does not appear in the record's sources. That led to 9 hand corrections, among them an invented "Anobian", a misread "Booglie Galaxy" and a garbled "Thundersanrior".
- A **grammar sweep**.

## Summary

| Verdict | Games |
|---|---|
| Accept | 51 |
| Accept, note | 9 |
| Unresolved (correctly not published) | 2 |
| Rejected | 0 |

No factual, identity, platform or neutrality error remains in the sample. The nine notes are listed below; three need an owner decision or spot-check:
- **Adventure Island:** regional naming conflict.
- **Racermate Challenge II:** thin evidence.
- **Super Gun:** thin evidence.

## Verdicts

| Game | ID | Stratum | Verdict | Notes |
|---|---|---|---|---|
| Snow Brothers | `fbd7caadaf77c2b1` | flagged | Accept | Rewritten from the NES manual: hot sauce jars (red speed, blue larger snow, green farther), the flying-giant and treasure-room jars, a boss every tenth floor, the slot-machine bonus round, SNOW letters. The arcade details are gone (yellow potion, pumpkin timeout, ghosts). |
| Touchdown Fever | `115fcca17cb77d7c` | flagged | Accept, note | The horse-collar clause is removed. The field-goal rule (inside the 15, automatic unless blocked) comes from the encyclopedia's NES Version section; the manual neither confirms nor contradicts it. |
| Bram Stoker's Dracula | `a8b6829d8d1e17cc` | rejected five | Accept | Rebuilt from the manual (Harker, Mina, the weapon boxes, Dracula's forms). It replaces the thin trivia draft. |
| Faria | `6f23f245b9edc5af` | rejected five | Accept | "expansive" removed; no other change. |
| John Elway's Quarterback | `8d21e1e256f82579` | rejected five | Accept | Rebuilt from the NES manual. The unsupported receiver-sprint claim is gone, and play-calling and cursor passing are described as in the manual. |
| Teenage Mutant Ninja Turtles III | `9f671090ffd2bb1d` | rejected five | Accept | Trimmed from 161 to 147 words; the claims are a subset of the checked draft. |
| WWF WrestleMania | `a6f98e57ffb45441` | rejected five | Accept, note | The unsupported health-item sentence is removed. "Only Hulk Hogan can bodyslam André" comes from the encyclopedia article on the NES game; the manual neither confirms nor contradicts it. |
| Ren & Stimpy: Buckeroo$! | `501e921616931e47` | new official | Accept, note | The official excerpt is in-character dialogue from the manual (Stimpy introducing the Gametron). It is authentic manual text, but owner taste may prefer a different passage. |
| Wheel of Fortune Featuring Vanna White | `0dcd6d458d26cda8` | new official | Accept | The official text is promotional, as published; the overview is neutral. |
| Wonderland Dizzy | `8a604198ba3e2437` | new official | Accept, note | A one-line official excerpt from the developer's page. The catalog has no publisher or year for this record (catalog data unchanged). |
| Classic Concentration | `69d26e6e849a103c` | game show | Accept | |
| Remote Control | `2f07815e80fcf807` | game show | Accept | |
| Wheel of Fortune | `7157801d42484e58` | game show | Accept | It describes the NES game's own rules (timer, round three speed-up, bonus round), not the TV show's. |
| Win, Lose or Draw | `818c7648979f86af` | game show | Accept | |
| Fisher-Price: Firehouse Rescue | `eb3955503b404a5e` | educational | Accept | |
| Fisher-Price: I Can Remember | `f79c8ddedbf0b580` | educational | Accept | |
| Sesame Street ABC & 123 | `d1d15eca4bf1732e` | educational | Accept | |
| Sesame Street: Big Bird's Hide & Speak | `63e867e7ea89b5bf` | educational | Accept | |
| Blackjack | `d430afcb9d0af1a8` | casino | Accept | |
| Casino Kid II | `87e479b14421fc4b` | casino | Accept | |
| Puzzle | `88ebfd7a124182cc` | tabletop | Accept | Rock Paper Scissors and Roll the Dice are clean readings of the manual's OCR ("Aock Paper Scissor", "Rall the Dina"). |
| Days of Thunder | `9371fab4f58fa6d6` | sports | Accept | |
| Magic Darts | `fe622610cd889b35` | sports | Accept | The Round the Clock rule was corrected by regeneration to match the manual. |
| R.B.I. Baseball (unlicensed release) | `d4f1650059a01145` | sports | Accept | "unique statistics" was reworded. Its text is distinct from the licensed R.B.I. Baseball record, which uses the same manual. |
| Super Team Games | `54112120e3a5d0a1` | sports | Accept | |
| Thunderbirds | `c0c3d7c2682dd2e0` | sports stratum (shooter) | Accept | |
| Asder 20-in-1 | `19a0cae21557cb7f` | compilation | Accept | From BootlegGames Wiki and NES database pages (no manual). The game list matches its sources. |
| Rumble Station 15 in 1 | `6ce321d32e524eae` | compilation | Accept | Plug-and-play hardware facts were left out as not NES-specific. |
| Sunday Funday: The Ride | `4d16627301ff5def` | compilation | Accept | |
| Big Nose Freaks Out | `87b9c94b5e9144eb` | unlicensed | Accept | |
| Double Strike | `588a31fdcdefcef9` | unlicensed | Accept | |
| P'Radikus Conflict | `fdafaa5c95e86168` | unlicensed | Accept | 159 words, at the edge of the range. |
| Pesterminator | `f1762c9f40e6e45a` | unlicensed | Accept | |
| Road Runner | `46a38baf9067869e` | unlicensed | Accept | Tengen's NES game (Wile E. Coyote and the seed meter), not the arcade description. |
| The Adventures of Rad Gravity | `322d6be47ed539c3` | licensed | Accept | |
| Al Unser Jr. Turbo Racing | `4c95afeda9a92842` | licensed | Accept | |
| BreakThru | `e1487d23800aa2c2` | licensed | Accept | |
| The Chessmaster | `66d4beb992a64632` | licensed | Accept | It describes the NES program's own features, not generic chess. |
| Family Feud | `0de5af54fd433bc6` | licensed | Accept | |
| Godzilla 2 | `37b5a1c9d9bc1747` | licensed | Accept | |
| Impossible Mission II | `a47f338a88835424` | limited | Accept, note | 84 words from a thin OCR manual. Accurate as far as the manual reads; not padded. |
| Stunt Kids | `f3a140c16012c4e4` | limited | Accept | |
| Top Players' Tennis | `68a6f5ef79892e49` | researched | Accept | From the scanned NES manual, read page by page by the research agent. |
| Racermate Challenge II | `97e84b6cbe198710` | researched | Accept, note | **Owner spot-check suggested.** No manual exists online; the facts come from articles that mention the cartridge, with two riders and the on-screen data. |
| Super Gun | `9930cf033982f378` | researched | Accept, note | **Owner spot-check suggested.** A single fan-wiki source (BootlegGames Wiki); "approximately twelve stages" is stated as approximate. |
| 1943: The Battle of Midway | `9f54aafa367247b9` | arcade port | Accept | The audit corrected it to the NES manual. |
| Rally Bike | `9866b51b16b503aa` | arcade port | Accept | |
| Skull & Crossbones | `06fddd300a6d1cbe` | arcade port | Accept | Crossbones are walked over (manual), not dug up with a sword (arcade article). |
| Track & Field | `a2812d80b3057aac` | arcade port | Accept | The NES event list follows the manual. |
| Alien 3 | `d73a9a1fae739675` | computer port | Accept | |
| Hudson Hawk | `3991a761116131dc` | computer port | Accept | Softballs (manual), not tennis balls. "Eddie" was removed by the name check. |
| Muppet Adventure | `80f6f2b9cb2669e8` | computer port | Accept | |
| Jurassic Park | `49fe0c49cd0a2841` | hand-corrected | Accept, note | The T. rex can be fired upon (manual). 158 words: above the 150 target, inside the 160 tolerance. |
| Last Action Hero | `f4b7d8d31c5bbfee` | hand-corrected | Accept | Seven scenes per the manual; the car-chase scene is described without claiming the player drives. |
| Lode Runner | `6c17c7bf2f7466eb` | hand-corrected | Accept | 50 stages per the NES manual. |
| Terra Cresta | `667bcf3ca1ebcf6f` | hand-corrected | Accept | |
| Adventure Island | `53bfc94fce46a251` | regional | Accept, note | **Owner decision.** The overview follows the US manual (Princess Leilani); the official Nintendo of Europe text says Tina. Both are correct for their region and appear together. |
| Astyanax | `a08e6b53f1fa593e` | regional | Accept | NES story (Remlia, Princess Rosebud, Cutie) per the US manual, not the arcade or Japanese version. |
| Loopz | `fbf9426155adf3b8` | short | Accept | |
| Monster Truck Rally | `9f04b8e867aa8569` | short | Accept | |
| BMX Simulator | `85f7be325689329e` | unresolved | Unresolved | Ambiguous release; correctly not published. |
| Caesars Palace | `cfea236e0d0fe0e4` | unresolved | Unresolved | Unreadable manual OCR; the provider blocked generation. Correctly not published. |
