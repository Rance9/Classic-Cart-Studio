# Security policy

## Reporting a vulnerability

Please open a private security advisory on GitHub (Security → Report a vulnerability) or, if that is not possible, an issue that says only "security — please contact me" without details. Reports are read by the maintainer. There is no bug bounty.

## What Classic Cart Studio for Windows does and does not do

- The application is a WebView2 window that shows the interface installed with it and a backend (the Background Helper) that listens **only on 127.0.0.1** (loopback). Neither opens a port on your network, and nothing contacts the console.
- Only the application's own interface may load inside its window; any other link opens in your normal browser. Web content never gets a general bridge to Windows: the only message channel is the bundled recovery screen's four buttons.
- The backend accepts privileged requests from exactly one caller: the application window (a random session token created at startup, handed to the backend through its environment, injected only into pages of the approved website origin, and discarded at exit). The public website never pairs with the backend (there are no pairing endpoints); the only requests it may send are a collection handoff and a `.ccs` package handoff (below).
- The only website origin allowed is `https://classiccartstudio.com`. Loopback development origins can be added in `companion.json` (`allowed_origins`); non-loopback `http://` origins are never accepted.
- Every cross-origin request goes through CORS with an exact-origin allow list, a preflight (including Chrome/Edge Private Network Access), a `Host` check against DNS rebinding and a bearer token sent in a header (never in a URL).
- The website-to-application collection handoff accepts a collection only from an allowed origin, validates its schema, limits its size (32 MB) and rate, keeps at most 8 pending offers for 20 minutes, deduplicates by offer id and content, and never changes the application's current collection without the user's confirmation. A `.ccs` package handed over by the website is only staged on this computer and checked; the window shows it before anything is imported. The `classiccartstudio://` link (and the older `nesstudio://`) carries only an identifier (letters, digits and dashes); any other form is ignored. No command, path or data is accepted from a link.
- A request whose body stops arriving for 120 seconds is abandoned: the connection is closed and anything partly received is deleted. This is an inactivity limit, not a time or size limit; a transfer that keeps arriving is never cut off.
- ROM files and `.ccs` packages never leave your computer through Classic Cart Studio: packages are made and checked on the device, and nothing is uploaded to a Classic Cart Studio service.
- The backend downloads nothing. Hakchi2 CE 3.9.3 is included in the installer, and the backend refuses it unless its size and SHA-256 match the values pinned in `tools/companion/hakchi_bridge.py`.
- The backend never writes to the console. Installing a collection on the console is done by Hakchi2 CE under the user's control; this project only prepares the files and settings on the PC.

## Verifying a download

Each release lists `SHA256SUMS.txt`. On Windows:

```powershell
Get-FileHash -Algorithm SHA256 "$env:USERPROFILE\Downloads\ClassicCartStudio-Setup-<version>.exe"
```

The printed hash must equal the line for that file in `SHA256SUMS.txt`. Releases are built by GitHub Actions from the tagged source; the workflow run is linked from the release notes and a build-provenance attestation is attached (`gh attestation verify <file> --owner Rance9`). A matching hash shows the file is the one the workflow built; the attestation and the public workflow are what tie it to this repository. The installer is **not** code-signed (see the README for why), so Windows SmartScreen will warn on first run.

## Supported versions

Only the latest release is supported.
