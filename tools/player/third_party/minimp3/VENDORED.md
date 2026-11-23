# minimp3 (vendored source)

- Source: https://github.com/lieff/minimp3, commit `142823796ab8efa55e1bf11406028f3be77ecceb` (2026-11-18), files unchanged:
  - `minimp3.h` SHA-256 `f9592be69e8b94044e9da6f45f993c9708000abae38cf6f44b70c699de55b509`
  - `minimp3_ex.h` SHA-256 `bdda836f5ea2de782bce7cf122a41a3d76fad57d960dfb47b6765dc631779de0`
  - `LICENSE` (CC0 1.0 Universal) SHA-256 `6a1ee543e5282cd9061881edf462e6fdab181f328da71fc2c9a6950a80e94d01`
- Used by `tools/player/core/gp_mp3.c` only in builds made with `-DGP_HOME_MP3` (HOME Music as MP3 in the console player): its extended
  API (`mp3dec_ex_open_buf`, `mp3dec_ex_read`, `mp3dec_ex_seek` with `MP3D_SEEK_TO_SAMPLE`) over the song held in memory, compiled with
  `MINIMP3_NO_STDIO`. No other build compiles it. This version builds that player but does not ship it: the console's HOME Music stays the
  PCM file until the MP3 is approved.
