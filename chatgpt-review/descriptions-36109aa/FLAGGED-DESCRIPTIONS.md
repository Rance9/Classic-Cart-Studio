# Flagged descriptions: the six owner-review notes

These six Game overviews were flagged in the agent's editorial review (`REVIEW-VERDICTS.md`). **All six are currently in production:** each text below is the exact text in the public runtime data at commit `36109aa`. None is excluded, and none has been regenerated or edited since.

**What "verification" means here:** a second Gemini pass split each overview into claims and judged each claim against the same supplied sources (the encyclopedia sections listed, plus the official text where one exists). It does not check the games themselves. "Port mode" means the encyclopedia article is about an original arcade or computer game; the overview was then limited to core gameplay, and the checker applied the same rule.

---

## 1. Snow Brothers: `fbd7caadaf77c2b1`

**Release:** *Snow Brothers* (USA), Capcom, 1991, licensed NES release of Toaplan's 1990 arcade game *Snow Bros.*
**Official description:** none found ("No verified official description available." is shown).
**Source:** encyclopedia article "Snow Bros." (revision 1379434968), sections Lead and Gameplay. **Port mode:** yes (the article opens with the arcade game).

**Game overview (exact production text):**

> Players control the snowman twins Nick and Tom, hurling snow at hostile monsters to rescue captured princesses. Hurling snow repeatedly encases enemies until they transform into giant snowballs. Once fully covered, a snowball can be kicked to roll across platforms and rebound off walls, defeating any foes caught in its path and triggering other stationary snowballs into motion.
>
> Defeated monsters occasionally leave behind colored potion items that grant temporary enhancements. Red potions increase movement speed, blue potions boost the volume of snow thrown, yellow potions extend throwing distance, and green potions temporarily inflate the snowmen into flying forms that knock out adversaries. Taking excessive time to clear an area summons an invincible pumpkin hazard that produces lethal ghosts.

**Accuracy concern:** the article's Gameplay section describes the arcade original; the same section mentions inserting credits into the arcade machine. Its only NES-specific sentence covers the story: King Scorch turns princes Nick and Tom into snowmen and captures princesses Teri and Tina. The timeout hazard, the evil pumpkin head that spawns ghosts, is stated generally and is not tied to the NES port.

**Claims needing verification against the NES version:**
- "Taking excessive time to clear an area summons an invincible pumpkin hazard that produces lethal ghosts." This is the main concern.
- The four potion colors and their effects (stated generally in the same section).
- "rescue captured princesses": consistent with the NES-specific story sentence.

**Existing verification:** pass, 11 of 11 claims judged supported. For the pumpkin claim the checker cited "taking too much time summons an invincible evil pumpkin head that spawns ghosts", treating it as core gameplay shared by all versions.

**Arcade or other-version risk:** yes, possible for the timeout hazard and, to a lesser degree, the potion details.

**Recommendation:** confirm the pumpkin-head timeout in the NES version (manual or gameplay footage). If it cannot be confirmed, remove the last sentence by owner-approved edit. The remaining text stands on the NES-specific story and the shared core mechanics. Do not regenerate.

---

## 2. Faxanadu: `58f2ba801354a8e5`

**Release:** *Faxanadu* (USA) (Rev 1), Nintendo, 1989, licensed.
**Official description:** verified. The source is Nintendo's product page for the NES game in its Virtual Console (Wii) catalog; the page lists only the Nintendo Entertainment System. The excerpt in production:

> This action-RPG brings a unique twist to the world of classic fantasy! You are an adventurer who has just returned to his home town, the Elf town of Eolis, at the base of the World Tree. You find the village in peril - the Elven water supply has dried up, monsters are causing chaos and the Tree itself, once proud, is slowly wilting.

**Source:** encyclopedia article "Faxanadu" (revision 1379539408), sections Lead, Gameplay and Plot. **Port mode:** no (an NES game).

**Game overview (exact production text):**

> Faxanadu combines side-scrolling platforming with action role-playing gameplay. Controlling an adventurer named Nello, the player returns to the troubled Elven town of Eolis at the base of the World Tree. The protagonist sets out to climb the tree, discover why the water supply has stopped, and defeat The Evil One. Progression moves screen by screen through fields, towns, and dungeons across four primary overworld regions: the buttress, the trunk, the branches, and a mountain stronghold.
>
> The character walks, jumps, climbs ladders, strikes foes with bladed weapons, and casts offensive magic projectiles. Defeated adversaries leave behind experience, currency, health-restoring bread, or other items. Experience raises the hero's rank through church Gurus, who also provide password mantras to resume play. Players speak with villagers for information, spend collected gold in shops on usable gear or armor, and monitor status meters for health, magic power, items, and experience.

**Accuracy concern:** none in the overview. The note concerns the official text's source: a Virtual Console product page, not a page from the original NES era. Its excerpt has no sentence about the download service.

**Claims needing verification:** "an adventurer named Nello". The article says this is the name in all non-Japanese versions, so it applies to this USA release.

**Existing verification:** pass, 11 of 11 claims supported.

**Arcade or other-version risk:** no.

**Recommendation:** keep both texts. The owner decides whether a Virtual Console catalog page counts as an acceptable official source (it is Nintendo's own copy, describing the NES game). If not, the official text becomes "not found" and the overview stays.

---

## 3. Touchdown Fever: `115fcca17cb77d7c`

**Release:** *Touchdown Fever* (USA), SNK, 1991, licensed NES port of SNK's 1987 arcade game.
**Official description:** none found.
**Source:** encyclopedia article "Touchdown Fever" (revision 1370865248), sections Lead, Gameplay and NES Version. **Port mode:** yes.

**Game overview (exact production text):**

> Touchdown Fever depicts the sport of American football across matches that support two players. Teams represent real-world football cities, operating under standard football rules while excluding horse-collar tackles. Play encompasses standard running and passing maneuvers alongside special teams kicking.
>
> On offense, human competitors choose from a pool of five basic plays: short passes, long passes, quarterback sneaks, hand-offs to backs, and punts or field goals. A field goal attempt is available only when the line of scrimmage rests within the opponent fifteen-yard line, succeeding automatically unless the opposing defense blocks the kick. Players can also opt to run a two-point conversion.

**Accuracy concern:** "operating under standard football rules while excluding horse-collar tackles" comes from the article's general Gameplay section. That section describes the arcade game (it mentions the arcade's rotating joysticks). Everything in the second paragraph, and the team cities, comes from the article's NES Version section.

**Claims needing verification against the NES version:**
- "standard football rules"
- "excluding horse-collar tackles"
- "Play encompasses ... special teams kicking": consistent with the NES section's punts and field goals.

**Existing verification:** pass, 9 of 9 claims supported. The checker accepted the horse-collar rule as stated in the Gameplay section. The two-player claim was supported by the catalog's player count.

**Arcade or other-version risk:** yes, for the rules clause in the first paragraph.

**Recommendation:** remove "operating under standard football rules while excluding horse-collar tackles", by owner-approved edit, unless the NES manual confirms it. The rest is NES-specific.

---

## 4. Starship Hector: `7b7ccdcda705c2c3`

**Release:** *Starship Hector* (USA), Hudson Soft, 1990, licensed.
**Official description:** none found.
**Source:** encyclopedia article "Starship Hector" (revision 1291229773), Lead only (the article has no gameplay section). **Port mode:** no.

**Game overview (exact production text):**

> Segments in this scrolling shooter feature both horizontally and vertically moving perspectives. The player pilots a starship through each section while facing hostile targets.
>
> Two distinct firing options are available to the vessel. One weapon configuration targets airborne foes, while the second mode addresses adversaries stationed on the ground.

**Accuracy concern:** none found. The note is about length: 49 words, the shortest in the review sample, because the source is a short lead paragraph. The model judged the evidence "limited".

**Claims needing verification:** none beyond the source. Every statement maps to the lead: scrolling shooter, horizontal and vertical segments, two fire modes for air and ground.

**Existing verification:** pass, 6 of 6 claims supported.

**Arcade or other-version risk:** no.

**Recommendation:** keep. If the owner wants a fuller text, a richer source is needed first (the manual). It should not be padded.

---

## 5. Cheetahmen II: `ebdd748cd488c4ac`

**Release:** *Cheetahmen II* (USA) (Unl), unlicensed, 1992. The catalog has no publisher for this record.
**Official description:** none found.
**Source:** the "Cheetahmen II" section of the encyclopedia article "The Cheetahmen" (revision 1357693796); the game has no article of its own. **Port mode:** no.

**Game overview (exact production text):**

> Guiding the Cheetahmen to defeat Dr. Morbis and his creations forms the primary goal of this title. At the start of the adventure, Dr. Morbis manufactures the Ape-Man to oppose the group. During the Apollo portion, players encounter Dr. Morbis directly as an opponent who continuously dashes toward the right side until beaten.
>
> Progression involves controlling specific characters across designated stages against particular adversaries. Aries navigates two areas where Cygore appears at the end, running back and forth until bested. Hercules subsequently engages the Ape-Man as the concluding adversary of the experience.

**Accuracy concern:** no factual concern found. The note is about readability: the text closely follows a terse source section, and the boss-movement details read stiffly.

**Claims needing verification:** the boss order (Dr. Morbis in the Apollo stages, Cygore in both Aries stages, the Ape-Man last with Hercules) and the bosses' movement patterns, which come from one short section.

**Existing verification:** pass, 6 of 6 claims supported.

**Arcade or other-version risk:** no (a single NES release).

**Recommendation:** keep. Optionally, a light owner-approved wording edit that changes no facts.

---

## 6. Ultimate Basketball: `bd6797920755a1dc`

**Release:** *Ultimate Basketball* (USA), Sammy, 1990, licensed.
**Official description:** none found.
**Source:** encyclopedia article "Ultimate Basketball" (revision 1337658247), sections Lead and Gameplay. **Port mode:** no.

**Game overview (exact production text):**

> Modes of play include a championship mode and a single game mode. Players select a squad from a roster of seven available teams, which feature entirely fictional rosters and fictional statistics rather than real-world professional or college athletes.
>
> On the court, the user directs five squad members, directly maneuvering one teammate at a time. The rules allow team members to foul out, which can leave a squad with only three or four competitors remaining on the floor by the end of the match.

**Accuracy concern:** none found. The note is about length: 83 words, below the 90-word target, because the sources say little.

**Claims needing verification:** the seven teams, the fictional rosters, and the possibility of finishing with three or four players after foul-outs. Each is a direct paraphrase of the article.

**Existing verification:** pass, 5 of 5 claims supported.

**Arcade or other-version risk:** no.

**Recommendation:** keep.

---

## Summary

| Game | ID | In production | Concern | Arcade or other-version risk | Recommendation |
|---|---|---|---|---|---|
| Snow Brothers | `fbd7caadaf77c2b1` | yes | pumpkin-head timeout (and potion colors) not tied to the NES version | yes | verify; otherwise remove the last sentence |
| Faxanadu | `58f2ba801354a8e5` | yes | the official text's source is a Virtual Console catalog page | no | keep; owner decides on the source type |
| Touchdown Fever | `115fcca17cb77d7c` | yes | the rules clause comes from the arcade gameplay section | yes | remove the clause unless the manual confirms it |
| Starship Hector | `7b7ccdcda705c2c3` | yes | short (49 words) | no | keep |
| Cheetahmen II | `ebdd748cd488c4ac` | yes | stiff wording | no | keep (optional wording edit) |
| Ultimate Basketball | `bd6797920755a1dc` | yes | short (83 words) | no | keep |

No text was regenerated or edited for this document, and no model was called.
