# Source request to the Hakchi maintainers: draft for the owner and the marketing agent

Internal working record. **Not sent; not final.** The marketing agent refines the wording, the owner decides whether, when and how to send
it. Nothing here authorizes sending, filing an issue, posting, or contacting Nintendo. It follows `docs/technical-forum-drafts.md`'s
structure (facts, owner prerequisites, draft, open questions, risks). It is a private message to project maintainers, not a forum post,
and it is consolidated with the M5 courtesy note (marketing R5, section M5), so it names the product the way M5 does.

## 1. Facts

| Fact | Label | Evidence |
|---|---|---|
| Hakchi2 CE 3.9.3's build downloads `hakchi-v1.0.4-126.Fri.03.Apr.2020.11_12_31.AM.GMT.hmod` from its own `supplemental` release; SHA-256 `003994b46504959895f40b0b9e91b16951ef4a638feb6312314c8fc4fdbff592`, 9,340,382 bytes | verified | `SOURCE-AUDIT.md` §2 |
| Its kernel: `Linux version 3.4.113.29-madmonkey (madmonkey@hakchi-buildbot) (gcc version 4.9.3 (Buildroot 2015.11.1)) #1 SMP PREEMPT Fri Mar 20 11:15:28 GMT 2020`; `boot.img` SHA-256 `9e921c326be65aa4a3100270e86fdb15fe4b8563d586941b9f6c640eef064e4c`; no embedded `.config` | verified | §3.1 |
| The newest public kernel tree found is `madmonkey1907/sun-nontendocm-kernel` at `f8c6e0b` (3.4.113.24, May 2019) | verified | §3.1 |
| `nand.ko` (SHA-256 `5d67ca0977370a04f46edbf1c02aa1dcbb2fc964781775d906125c40eb7d29d5`) defines 42 symbols found in no examined source | verified | §3.2 |
| `rtl8188fu.ko` is vendor version `v4.3.23.6_20964.20170110`; its source was not found in any examined tree | verified | §3.2 |
| u-boot: `U-Boot 2011.09-rc1-00000-g32c3df5-dirty (Nov 19 2019 - 02:26:33)`; `uboot.bin` SHA-256 `5cfacb297b962a388d947e3be553692bb1e9df9edc1125ca5a769166d3a89564`; hash slot `32c3df5dff315fd5bdca115aa85e5c4d6befa46b`; its boot command differs from the public `r16-uboot.diff` | verified | §3.4 |
| hakchi's u-boot recipe takes the board-configuration block (bytes 0x90000-0xBA000) from the console's stock u-boot | believed (hakchi's readme) | §3.4 |
| fes1: SHA-256 `d78688b70e022c53e6a49a18994f59946fda7791d22bc0e6409ff630553b491c`, embeds the r16-uboot commit `fc3061df…`, links Allwinner's prebuilt `libdram-riot` object, which has no license text | verified | §3.5 |
| sntool: `sntool 5833d00 (dirty) 2020-03-30 01:41:34 UTC`, SHA-256 `f7eeecdeb94863364593fe9da55f499bfa13915a74fa2b2049e18209a65f57e9`; the public `sntool.c` (`fd525ac`, March 2019) has no `sunxi-part`, `decodepng`, `clover-mcp` or `m2engage` | verified | §3.6 |
| BusyBox: `BusyBox v1.29.0.git (2018-04-10 20:38:05 GMT)`, SHA-256 `d5c96520b02920b331ec8b7f232c662e53f97add208741f65500b0512bf87d44`; commit and `.config` not found | verified | §3.7 |
| The public repositories' last pushes (`madmonkey1907/hakchi` May 2019, the kernel May 2019) predate these 2019-2020 builds | verified (GitHub API) | §3 |
| Whether the maintainers still have these trees, configurations or build environments | unknown | |
| CCS's console programs and its preparation carry these files (both products) | verified | §1, §7 |

Nothing in the facts says anyone failed to publish anything: the files may exist somewhere not searched, or privately.

## 2. Owner prerequisites (before sending)

1. Decide: one consolidated message with M5 (as drafted below), or M5 first and this request separately. The draft assumes consolidated.
2. Choose the channel and recipients. M5 says "the project's own contact channel, not as a public issue". Suggested recipients: the
   Hakchi2 CE maintainers (Team Shinkansen), with madmonkey (hakchi: kernel, sntool, u-boot) where a channel exists. No public issue, no
   forum thread, no Discord channel post, no contact with Nintendo.
3. Have Codex confirm each M5 bullet against the release on the day it is sent, as M5 requires; remove any bullet that is not true then.
   In particular, M5's source bullet must not say the complete corresponding source is published while the gates in `SOURCE-AUDIT.md`
   section 11 are open; the wording below avoids that.
4. Fill the bracketed placeholders (names, timing, links). Release timing follows the owner's current rule for what may be said; this file
   carries no date.
5. Marketing agent review of the whole message; the implementation draft is never the final text.

## 3. Draft (first person, Rance9; awaiting marketing review)

Subject: Courtesy note and a source question: Classic Cart Studio is built on Hakchi2 CE

> Hi [Name or team],
>
> I'm Rance9, the developer of Classic Cart Studio, a free, open-source app that builds game collections for the NES Classic and also
> plays them in a browser or on Windows, with or without the console. I'm preparing its 1.0 release [timing per the owner's current rule],
> and I wanted you to hear about it from me directly, because it leans heavily on your work.
>
> [M5's bullets on how it uses Hakchi2 CE, unchanged from marketing R5 and confirmed on the day of sending; the source bullet reads:]
> - The source of my own code and build files is published with each release, with my changes identified and upstream licenses and
>   credits kept: [exact release source link].
>
> I also have one question, and any help is appreciated. I'd like each release to include the complete source for everything it
> carries. For a few prebuilt files in the hakchi package Hakchi2 CE 3.9.3 downloads (`hakchi-v1.0.4-126…hmod`, SHA-256
> `003994b46504959895f40b0b9e91b16951ef4a638feb6312314c8fc4fdbff592`), I couldn't find the exact source or build inputs, and I'd rather ask than guess:
>
> 1. Kernel `3.4.113.29-madmonkey` (built March 20, 2020; `boot.img` SHA-256 `9e921c326be65aa4a3100270e86fdb15fe4b8563d586941b9f6c640eef064e4c`): the tree, its `.config` and the build
>    steps, including the modules, especially `nand.ko` (SHA-256 `5d67ca0977370a04f46edbf1c02aa1dcbb2fc964781775d906125c40eb7d29d5`) and `rtl8188fu.ko`. The newest public tree I found is
>    `sun-nontendocm-kernel` at `f8c6e0b` (3.4.113.24).
> 2. U-Boot `2011.09-rc1-g32c3df5-dirty` (built November 19, 2019; `uboot.bin` SHA-256 `5cfacb297b962a388d947e3be553692bb1e9df9edc1125ca5a769166d3a89564`): commit
>    `32c3df5dff315fd5bdca115aa85e5c4d6befa46b` and its local changes, and where its board-configuration block comes from.
> 3. fes1 (SHA-256 `d78688b70e022c53e6a49a18994f59946fda7791d22bc0e6409ff630553b491c`): anything you know about the source or licensing of the `libdram-riot` object it links from the
>    r16-uboot tree.
> 4. sntool `5833d00` (March 30, 2020; SHA-256 `f7eeecdeb94863364593fe9da55f499bfa13915a74fa2b2049e18209a65f57e9`): its source. The public `sntool.c` at `fd525ac` doesn't have
>    `sunxi-part` or `decodepng` yet.
> 5. BusyBox `v1.29.0.git` (built April 10, 2018; SHA-256 `d5c96520b02920b331ec8b7f232c662e53f97add208741f65500b0512bf87d44`): the commit and the `.config`.
>
> If any of these are already public somewhere I missed, a link is all I need. If it's easier to share them another way, that works too,
> and if some of them aren't available anymore, that's useful to know as well.
>
> [M5's development-method sentence, unchanged from marketing R5.]
>
> I'm not asking for endorsement, promotion or support, and I won't suggest you're involved. If a credit is wrong, or you'd like something
> described differently, tell me and I'll fix it.
>
> Thank you for Hakchi2 CE. This project wouldn't exist without it.
>
> Rance9
> [site]

## 4. Open questions

- Whether the 2020 kernel, u-boot, sntool and BusyBox builds came from trees that still exist (only the maintainers can answer).
- Whether the `libdram-riot` object has any license or source statement from its supplier that the maintainers know of.
- Whether the board-configuration block in hakchi's u-boot was taken from a particular stock firmware version.

## 5. Risks

- **Reads as a compliance accusation.** Mitigated: it asks for files, says "I couldn't find", offers that they may be public already, and
  makes no legal statement. Keep any revision in that register.
- **Overclaiming in M5.** M5's source bullet must not claim complete corresponding source while the release gates are open (owner
  prerequisite 3).
- **Disclosure.** Names the product (as M5 does), the hakchi versions and hashes; no private paths, no owner backup content, no console
  identifiers, no personal or employer details. Do not attach files from the owner's backups or this repository's private records.
- **Timing.** No date in this file; the owner's current rule decides what timing, if any, the message states.
- **Scope creep.** It asks only for the G1-G8 material. It does not ask for endorsement, support, a pin update or permission.
- **No answer.** That is an acceptable outcome; `SOURCE-AUDIT.md` section 8 gives each item's routes without an answer.
