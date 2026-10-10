# Changelog

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
