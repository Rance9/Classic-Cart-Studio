# Changelog

## 0.1.6 — 2026-10-10

- Website now lives at **https://classiccartstudio.com/** (GitHub Pages custom domain; the old github.io address redirects there). The companion allows the new origin (and www) in addition to the github.io origin, so pairing works on the new address; links in the companion window, the installer and the README point at the new domain.
- Top bar: every button has the same height; when the bar wraps on narrower windows the buttons are centered on every row; the title list never scrolls sideways or bleeds past the right edge at any width (fixed column layout between 641 and 1000 px).

## 0.1.5 — 2026-10-10

- Website: toolbar buttons recolored (Save / Load matches My Collection; Tray is blue with the red B; Install on NES Classic is green with the red A); identity and emulator pills in the list are faint so the titles and art lead; a collapsible **Search & Filters** section holds search, Filter & Sort, Select, List/Grid, stats, the connection pill, the alphabet strip and the storage chip (collapsed on narrow screens until opened; on wide screens it folds away when you scroll into the list and returns at the top); a real phone layout (stacked task buttons, the list becomes a card list, no sideways scrolling anywhere, dialogs fit the screen).
- Help & User Guide: new showcase layout with a sprite band and chapter number on every chapter, grouped chapter list, reading progress bar, equal-height step cards on a dashed path (a timeline on phones), styled callouts, disclosures and tables, previous/next chapter cards, and ← / → keys to move between chapters.
- US English throughout the website, the guide, the companion and the repository text.
- Companion 0.1.5: file-picker replies use the `canceled` key (the website accepts both spellings, so a 0.1.4 companion keeps working); dropped client connections no longer print tracebacks in the companion log.

## 0.1.4 — 2026-10-10

- Website: compact hero (about 140 px) that stays on screen together with the toolbar; the duplicate brand block in the toolbar is gone; "How It Works" is now **Help & User Guide** (book icon, chapter count); one **Filter & Sort** dialog holds every filter, the sort order and the Extended Library switch, with Apply / Cancel / Reset, staged changes, an active-filter badge and chips; the toolbar is grouped by task; the search icon no longer overlaps typed text; no page-level horizontal scroll from 360 px to 3440 px wide (also at 125 % zoom).
- Connection: the website reports **Connected** as soon as the companion answers the version handshake (about 10 ms on a warm companion instead of 4 s); the console-setup check (Hakchi2 CE, driver, console) runs in the background with bounded timeouts and shows "Checking console setup…" / "Setup needs attention"; states shown: Checking for companion…, Companion not running, Permission needed, Waiting for approval, Connected.
- Companion 0.1.4: `/api/status` answers from a cached snapshot immediately and refreshes diagnostics in a background thread (`?wait=` for callers that need a fresh result); every probe is time-limited (process list 10 s, device enumeration 12 s, driver query 25 s); file hashes and version lookups are cached by size and modification time; the status payload carries the measured timings. Protocol stays 1: the 0.1.3 companion still works with this website (its status call is slower and shows no timings); updating is recommended.
- Storage: the header chip reads "NES Classic · estimated game space ~261 MB" and names its source (a reference estimate for a stock 2018 console, a read-only measurement, or unknown); the collection tray and footer stats use the same wording; no visitor free space is ever claimed and no private path is published.
- Legal: new guide chapter **Legal, Privacy & Disclaimers**, `PRIVACY.md`, `THIRD_PARTY_NOTICES.md`, a **Legal & Privacy** link in the footer and links from the README.

## 0.1.3 — 2026-10-10

- Restored the approved visual presentation on the website and in the companion's bundled site: the Nintendo hero and 27-character parade, the footer character roster with origins, the firmware interface icons and wordmark, and the game box art in grid, list, details and the Collection Tray (745 of 773 curated games). The public build no longer substitutes generic artwork or strips art references; ROM, console-image, secret and private-path exclusions are unchanged.
- Install button keeps its glow in every state; hover lift, glow and sheen on all buttons.

## 0.1.2 — 2026-10-10

- How It Works guide: content pass across all 24 chapters (shorter, direct wording that names Hakchi2 CE, FEL mode, mappers and hmods; stale artwork note removed), step boxes no longer stretch to the tallest step, verification command shown as a code block.

## 0.1.1 — 2026-10-10

- Installer: the post-uninstall note is no longer shown during a silent uninstall (`/VERYSILENT`), which blocked scripted removal in 0.1.0.
- Website: requests to the companion declare the loopback target address space (Microsoft Edge / Chrome Local Network Access); the connection dialog explains the browser's local-network permission when the companion cannot be reached.

## 0.1.0 — 2026-10-10

First public release.

- Website (GitHub Pages): the complete Studio — curated library of 773 North American releases plus the extended index, version chips, Technical Details, collections with save/load/snapshots, How It Works guide, original pixel art.
- Windows companion (per-user installer): loopback server with CORS pairing for the public website, local-library reading, bulk import of game files and folders (ZIP archives, exact-content identification, duplicates, safety limits), personal games with artwork, collection preparation for Hakchi2 CE 3.9.3 (verified download), console USB driver setup, status window, `nesstudio://` link.
- Honest limits: installing on the console is done by Hakchi2 CE under the user's control; this version prepares everything on the PC and does not write to the console. The installer is not code-signed; the SHA-256 and a build-provenance attestation are published instead.
