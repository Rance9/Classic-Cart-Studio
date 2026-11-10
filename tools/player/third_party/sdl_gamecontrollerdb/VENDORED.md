# SDL_GameControllerDB (vendored data)

- Source: https://github.com/mdqinc/SDL_GameControllerDB, commit `07af1d50187e94c861c9f9f5d8a55b148f11306b` (2026-11-02).
- `gamecontrollerdb.txt` SHA-256 `4ef169fcee14ef9563e36ebf731dc6950494e553bf8b56b5fc8aa70181f0d25c`; `LICENSE` (zlib) SHA-256
  `97f35b302b361680ec1e891e95d2d52097bb95abff361434916d99dc1305f127`, unchanged.
- Used only as data: `tools/player/scripts/gen_pad_db.py` turns the Windows USB entries into `tools/player/core/gp_pad_db.h` (the game
  player's known controllers) and `pad-db.json` (the browser player's). A controller listed there plays without setup; a setup the person
  saves always wins. No SDL code is used.
