# Runtime libraries the pinned zig links into the game players

The game players are compiled by zig 0.17.0 (`tools/console-simulator/toolchain.lock.json`: version, URL and SHA-256 of the archive).
zig links parts of the C runtime it carries into each program:

- `ccs-player.exe` (Windows, `x86_64-windows-gnu`): the mingw-w64 runtime (`lib/libc/mingw` in the zig distribution) and compiler-rt.
- `play/ccs-player.wasm` (the website's game player, `wasm32-wasi`): wasi-libc (`lib/libc/wasi`, with its musl, cloudlibc and dlmalloc
  parts) and compiler-rt.

The license files below are copied unchanged from the zig 0.17.0 distribution (paths inside it in parentheses):

| File | From | SHA-256 |
|---|---|---|
| `zig-LICENSE` | `LICENSE` (zig: MIT) | `5c537d6853e005298a285d508cff9ac7192cea23576c840d485b2b586a7ff177` |
| `wasi-libc-LICENSE` | `lib/libc/wasi/LICENSE` | `673f577e363e80e0058bd78214683f045d1d0c63930969a87f01a1d87d7cf1d6` |
| `wasi-libc-LICENSE-MIT` | `lib/libc/wasi/LICENSE-MIT` | `23f18e03dc49df91622fe2a76176497404e46ced8a715d9d2b67a7446571cca3` |
| `wasi-libc-LICENSE-APACHE` | `lib/libc/wasi/LICENSE-APACHE` | `a60eea817514531668d7e00765731449fe14d059d3249e0bc93b36de45f759f2` |
| `wasi-libc-LICENSE-APACHE-LLVM` | `lib/libc/wasi/LICENSE-APACHE-LLVM` | `268872b9816f90fd8e85db5a28d33f8150ebb8dd016653fb39ef1f94f2686bc5` |
| `musl-COPYRIGHT` | `lib/libc/wasi/libc-top-half/musl/COPYRIGHT` (the same bytes as `lib/libc/musl/COPYRIGHT`) | `f9bc4423732350eb0b3f7ed7e91d530298476f8fec0c6c427a1c04ade22655af` |
| `cloudlibc-LICENSE` | `lib/libc/wasi/libc-bottom-half/cloudlibc/LICENSE` | `c8b789cf5a746611e6300a0cc7750dbf92b61912a709d04e639245f7290656d0` |
| `mingw-w64-COPYING` | `lib/libc/mingw/COPYING` | `99a69660981156c21336fdb5661f89341b013c94e4bf9e1c7467b4745718397f` |

compiler-rt is under the Apache License v2.0 with LLVM Exceptions (the exception covers code compiled into a program; the text is in
`wasi-libc-LICENSE-APACHE-LLVM`). dlmalloc is CC0 (its notice is in wasi-libc's `malloc.c`).
