# Security policy

## Reporting a vulnerability

Please open a private security advisory on GitHub (Security → Report a vulnerability) or, if that is not possible, an issue that says only "security — please contact me" without details. Reports are read by the maintainer. There is no bug bounty.

## What the companion does and does not do

- The Windows companion listens **only on 127.0.0.1** (loopback). It never opens a port on your network and never contacts the console.
- A website can use it only after you click **Allow** in the pairing window the companion opens. Access is tied to that exact website origin, expires after 12 hours, and is forgotten when the companion closes.
- The only website allowed by default is `https://rance9.github.io`. Loopback development origins can be added in `companion.json` (`allowed_origins`); non-loopback `http://` origins are never accepted.
- Every cross-origin request goes through CORS with an exact-origin allow list, a preflight (including Chrome/Edge Private Network Access), a `Host` check against DNS rebinding and a bearer token sent in a header (never in a URL).
- The companion downloads exactly one third-party program on request, Hakchi2 CE 3.9.3, from its official GitHub release, and refuses it unless size and SHA-256 match the values pinned in `tools/companion/hakchi_bridge.py`.
- The companion never writes to the console. Installing a collection on the console is done by Hakchi2 CE under the user's control; this project only prepares the files and settings on the PC.

## Verifying a download

Each release lists `SHA256SUMS.txt`. On Windows:

```powershell
Get-FileHash -Algorithm SHA256 "$env:USERPROFILE\Downloads\NESClassicCollectionStudio-Setup-<version>.exe"
```

The printed hash must equal the line for that file in `SHA256SUMS.txt`. Releases are built by GitHub Actions from the tagged source; the workflow run is linked from the release notes. The installer is **not** code-signed (see the README for why), so Windows SmartScreen will warn on first run.

## Supported versions

Only the latest release is supported.
