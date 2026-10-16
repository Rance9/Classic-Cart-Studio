# Native HOME N1.1 screenshots (console simulator, Arcade Grid)

All images are actual output of the native C renderer in the Classic Cart Studio console simulator. There are no mockups.
- **01–11:** frames the simulator core wrote during a scripted run (`fixtures/scripts/home-n1-1.txt`), 1280 × 720.
- **12–17:** the same states rendered by the N1 code (commit 43b2193, left) and the N1.1 code (right), with the same data.
- **live-window-\*:** captures of the real Windows simulator window, driven with real keyboard input (SendInput) through the same keyboard adapter the owner uses.

| File | Shows |
| --- | --- |
| 01-grid-selected-game | The selected game has the red focus border; the large card previews the same game |
| 02-focus-moved-right | Right ×3: the border moves one tile at a time, and the tiles stay in place |
| 03-boundary-row-under-card | The first tile of the bottom row, under the card, is the left edge |
| 04-later-page | Held Down paged forward (page 10 of 49, game 150 of 773) |
| 05-details-focused-action | Game Details opens with Play Game focused |
| 06-details-after-right | Right moves the focus to Add Favorite; the legend states what A does |
| 07-details-long-text-scrolled | The description box has focus and is scrolled; the legend says Scroll |
| 08-back-selection-kept | B returns to the same page and game, pixel-identical to 04 |
| 09-tabs-focused | Up from the first row enters the tabs (white ring); the game keeps a quiet marker |
| 10-launch-not-available | A shows that launching is not available yet; nothing runs |
| 11-after-notice | Any button closes the notice; focus and selection are unchanged |
| 12-compare-selected-game-border | Before: no tile marked the selected game |
| 13-compare-moving-right | Before: every move reshuffled the tiles |
| 14-compare-caption-overflow | Before: long caption metadata wrapped out of its plate under a tile; now one centered line |
| 15-compare-card-frame-and-caption-plate | Before: the frame's corner bracket arms, right neon edge and caption plate border were covered by a black box |
| 16-compare-whole-covers | Before: covers were cropped to fill the tiles; now each whole box art is shown |
| 17-compare-details-right | Before: Right did nothing in Details |
| live-window-a/b/c | The live window after Right, Details after Right, and back on the same game |
