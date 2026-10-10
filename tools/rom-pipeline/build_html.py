"""Stage D — build the offline game-selection catalog (reports/nes-catalog.html).

Embeds reports/catalog.json (metadata only; no ROM bytes). Features: list and grid views sharing one selection,
search, filters (year, publisher, licensed, emulator, identification, built-in, genre, players, favorites, selection),
alphabetical jump, favorites (separate from selection), selection tray with warnings, details panel, lazy box-art
thumbnails from reports/art/, standardized hover preview, click-anywhere row/card selection, keyboard toggling,
localStorage persistence keyed by stable game_id (payload SHA-256 prefix), and JSON manifest export compatible with
import_selected.py (schema nes-classic-selection/1).

Visual theme (2026-10-09 redesign): dark "NES hardware" design system with Nintendo-red accents. Decorative artwork comes
only from local files: Nintendo's own pixel-art character renderings and UI atlas crops exported from the owner's verified
console backup (reports/brand/oem/, git-ignored, not redistributed) and the OFL-licensed Press Start 2P font
(reports/brand/fonts/). No CDN, no runtime network access. Rollback snapshot: reports/rollback/2026-10-09_catalog-v1/.
"""
from __future__ import annotations
import os, sys, json
sys.path.insert(0, os.path.dirname(__file__))
from common import REPORTS

BRAND = os.path.join(REPORTS, "brand", "oem")
WEB = os.path.join(REPORTS, "brand", "web")      # characters obtained online (provenance.json records every source)
# ---- PUBLIC build (--public --out <site>): the SAME approved visual presentation as the private build (owner directive 2026-10-10:
#      hero, 27-character parade, interface icons, wordmark, box art). Only the data source (sanitised copies in <site>), the page
#      title/meta and the legal paragraph differ. tools/publish/build_public_repo.py copies brand/oem, brand/web, fonts and art into <site>. ----
PUBLIC = "--public" in sys.argv
OUT = sys.argv[sys.argv.index("--out") + 1] if "--out" in sys.argv else REPORTS
SRC = OUT if PUBLIC else REPORTS
if PUBLIC: BRAND = os.path.join(OUT, "brand", "oem"); WEB = os.path.join(OUT, "brand", "web")
APP_NAME = "NES Classic Collection Studio"
TAGLINE = "An independent game collection manager for the Nintendo NES Classic Edition."
DISCLAIMER = "NES Classic Collection Studio is an independent project. It is not produced, licensed, sponsored, approved or endorsed by Nintendo. Nintendo, Nintendo Entertainment System, NES and NES Classic Edition are trademarks of Nintendo. Game titles and publisher names are used only to identify games. Console-side technology by the Hakchi2 CE project."

# Hero ensemble: Nintendo characters only (file stem in reports/brand/oem, pixel scale, css class). Order = left to right.
HERO = [   # (folder, file stem, pixel scale, css class) — compact hero, NES-era sprites only, 2x (Little Mac 1x)
    ("oem", "px-question-block-0", 2, "float f1"), ("oem", "px-mario-walk-1", 2, ""), ("oem", "px-luigi-walk-1", 2, ""), ("web", "toad", 2, ""), ("web", "peach", 2, ""),
    ("web", "goomba", 2, ""), ("web", "koopa-troopa", 2, ""), ("web", "bowser", 2, "lg"), ("web", "piranha-plant", 2, "lg"),
    ("oem", "px-link", 2, ""), ("web", "zelda", 2, ""), ("web", "octorok", 2, "lg"), ("web", "ganon", 2, "lg"),
    ("oem", "px-samus", 2, ""), ("web", "ridley", 2, "lg"), ("oem", "px-metroid", 2, "float f3"),
    ("oem", "px-kirby", 2, ""), ("web", "king-dedede", 2, "lg"), ("oem", "px-pit", 2, ""), ("web", "donkey-kong", 2, ""), ("oem", "px-donkey-kong-jr", 2, "lg"),
    ("oem", "px-little-mac", 1, ""), ("oem", "px-popo", 2, "lg"), ("oem", "px-nana", 2, "lg"), ("oem", "px-balloon-fighter", 2, "float f1 lg"),
    ("oem", "px-excitebike-rider-red", 2, "lg"), ("web", "yoshi", 2, "lg"),
]
# Secondary decorations: tray empty state, how-to panel, filters dialog (folder, stem, scale)
EMPTY_CAST = [("oem", "px-mario-walk-1", 3), ("web", "peach", 3), ("oem", "px-link", 3), ("web", "toad", 3), ("oem", "px-kirby", 3)]
HOWTO_CAST = [("web", "king-hippo", 1), ("web", "eggplant-wizard", 1), ("web", "mr-game-and-watch", 1)]
FILTERS_CAST = [("web", "rob", 2), ("web", "octorok", 2)]
# Character roster for the footer parade and the report: (file stem, display name, game).
ROSTER = [
    ("px-mario-walk-1", "Mario", "Super Mario Bros."), ("px-luigi-walk-1", "Luigi", "Super Mario Bros."), ("px-raccoon-mario", "Raccoon Mario", "Super Mario Bros. 3"),
    ("px-mario-kuribo-shoe", "Mario in Kuribo's Shoe", "Super Mario Bros. 3"), ("px-jumpman-mario-hammer", "Mario (Jumpman) with hammer", "Donkey Kong"),
    ("px-mario-bros-mario", "Mario", "Mario Bros."), ("px-super-mushroom", "Super Mushroom", "Super Mario Bros."), ("px-fire-flower", "Fire Flower", "Super Mario Bros."),
    ("px-question-block-0", "? Block", "Super Mario Bros."), ("px-shy-guy", "Shy Guy", "Super Mario Bros. 2"), ("px-birdo", "Birdo", "Super Mario Bros. 2"),
    ("px-shellcreeper", "Shellcreeper", "Mario Bros."), ("px-pauline", "Pauline", "Donkey Kong"), ("px-donkey-kong-jr", "Donkey Kong Jr.", "Donkey Kong Jr."),
    ("px-banana", "Banana", "Donkey Kong Jr."), ("px-link", "Link", "The Legend of Zelda"), ("px-zelda-old-man", "Old Man", "The Legend of Zelda"),
    ("px-link-zelda2", "Link", "Zelda II: The Adventure of Link"), ("px-zelda2-knight", "Iron Knuckle", "Zelda II: The Adventure of Link"),
    ("px-samus", "Samus Aran", "Metroid"), ("px-metroid", "Metroid", "Metroid"), ("px-kirby", "Kirby", "Kirby's Adventure"), ("px-parasol-waddle-dee", "Parasol Waddle Dee", "Kirby's Adventure"),
    ("px-pit", "Pit", "Kid Icarus"), ("px-kid-icarus-monoeye", "Monoeye", "Kid Icarus"), ("px-little-mac", "Little Mac", "Punch-Out!! Featuring Mr. Dream"),
    ("px-boxing-glove", "Boxing glove", "Punch-Out!! Featuring Mr. Dream"), ("px-popo", "Popo", "Ice Climber"), ("px-nana", "Nana", "Ice Climber"),
    ("px-balloon-fighter", "Balloon Fighter", "Balloon Fight"), ("px-balloon-bird", "Balloon Bird", "Balloon Fight"), ("px-excitebike-rider-red", "Excitebike rider", "Excitebike"),
    ("px-excitebike-rider-purple", "Excitebike rival", "Excitebike"), ("px-dr-mario-viruses", "Viruses", "Dr. Mario"), ("px-dr-mario-pills", "Megavitamins", "Dr. Mario"),
    ("px-mike-jones", "Mike Jones", "StarTropics"), ("px-startropics-enemy", "StarTropics creature", "StarTropics"),
    ("px-mega-man", "Mega Man", "Mega Man 2"), ("px-arthur", "Arthur", "Ghosts'n Goblins"), ("px-ff-fighter", "Fighter", "Final Fantasy"), ("px-bub", "Bub", "Bubble Bobble"),
    ("px-pac-man", "Pac-Man", "Pac-Man"), ("px-galaga-fighter", "Galaga fighter", "Galaga"), ("px-simon-belmont", "Simon Belmont", "Castlevania"), ("px-vic-viper", "Vic Viper", "Gradius"),
    ("px-super-c-soldier", "Bill Rizer", "Super C"), ("px-simon-belmont-2", "Simon Belmont", "Castlevania II: Simon's Quest"), ("px-ryu-hayabusa", "Ryu Hayabusa", "Ninja Gaiden"),
    ("px-tecmo-bowl-player", "Tecmo Bowl player", "Tecmo Bowl"), ("px-double-dragon-lee", "Billy Lee", "Double Dragon II: The Revenge"),
]
# Characters obtained online (reports/brand/web/provenance.json); shown in the footer parade with their documented origin.
WEB_ROSTER = [("peach", "Princess Peach"), ("bowser", "Bowser"), ("toad", "Toad"), ("goomba", "Goomba"), ("koopa-troopa", "Koopa Troopa"), ("piranha-plant", "Piranha Plant"),
    ("zelda", "Princess Zelda"), ("ganon", "Ganon"), ("octorok", "Octorok"), ("ridley", "Ridley"), ("donkey-kong", "Donkey Kong"), ("king-dedede", "King Dedede"),
    ("eggplant-wizard", "Eggplant Wizard"), ("king-hippo", "King Hippo"), ("duck-hunt-dog", "Duck Hunt dog"), ("duck-hunt-duck", "Duck Hunt duck"), ("rob", "R.O.B."),
    ("mr-game-and-watch", "Mr. Game & Watch"), ("yoshi", "Yoshi")]
NINTENDO_FIRST_PARTY = 37  # first N roster entries are Nintendo characters/items; the rest are third-party icons from the built-in library

TEMPLATE = r"""<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>NES Classic Collection — Game Selection Catalog</title>
<meta name="nes-companion-token" content="">
<style>
@font-face{font-family:"Press Start 2P";src:url("brand/fonts/PressStart2P-Regular.ttf") format("truetype");font-display:swap}
:root{
 --bg0:#0a0a0c;--bg1:#121216;--bg2:#1a1a20;--s1:#1f1f26;--s2:#26262e;--s3:#2f2f38;--line:rgba(255,255,255,.08);--line2:rgba(255,255,255,.16);
 --ink:#f3f1ec;--ink2:#d8d6d0;--muted:#9c9a95;--dim:#6f6e6a;--silver:#c9c9ce;
 --red:#e4000f;--red2:#b8000c;--red3:#ff3b45;--glow:rgba(228,0,15,.38);--gold:#f2b632;--sky:#5c94fc;--pipe:#1fab4a;--info:#4a9bff;--warn:#f0a030;--bad:#ff5a5a;--ok:#39d37a;
 --r1:6px;--r2:10px;--r3:16px;--sh1:0 2px 6px rgba(0,0,0,.35);--sh2:0 10px 28px rgba(0,0,0,.5);--sh3:0 24px 60px rgba(0,0,0,.6);
 --font:"Segoe UI Variable Text","Segoe UI",system-ui,-apple-system,Roboto,sans-serif;--display:"Bahnschrift","Segoe UI Variable Display","Segoe UI",system-ui,sans-serif;--pixel:"Press Start 2P",monospace;
 --tb-h:160px;--maxw:4800px;--gap:12px;--ease:cubic-bezier(.2,.7,.2,1)}
*{box-sizing:border-box}[hidden]{display:none!important}html{scroll-behavior:smooth}body{margin:0;font:14px/1.45 var(--font);background:var(--bg0);color:var(--ink);-webkit-font-smoothing:antialiased}
a{color:var(--red3)}img{display:block}.px{image-rendering:pixelated;image-rendering:crisp-edges}
.skip{position:absolute;left:-9999px;top:8px;background:var(--red);color:#fff;padding:8px 12px;border-radius:var(--r1);z-index:200}.skip:focus{left:8px}
:focus-visible{outline:2px solid var(--red3);outline-offset:2px}
button,select,input{font:inherit;color:inherit}
.btn{display:inline-flex;align-items:center;gap:7px;padding:7px 12px;border:1px solid var(--line2);border-radius:var(--r2);background:linear-gradient(180deg,var(--s3),var(--s1));color:var(--ink);cursor:pointer;line-height:1.2;box-shadow:var(--sh1);transition:transform .08s,border-color .15s,background .15s,box-shadow .15s;white-space:nowrap}
.btn:hover{border-color:rgba(255,255,255,.3);box-shadow:var(--sh2)}.btn:active{transform:translateY(1px);box-shadow:none}.btn.active{background:linear-gradient(180deg,#3a3a44,#2a2a32);border-color:var(--silver);color:#fff}
.btn.primary{background:linear-gradient(180deg,#ff2a36,var(--red2));border-color:#ff5a63;color:#fff;font-weight:600;box-shadow:0 2px 10px var(--glow)}.btn.primary:hover{box-shadow:0 4px 18px var(--glow)}
.btn.ghost{background:transparent;box-shadow:none}.btn .ico{width:14px;height:14px}.btn.sm{padding:5px 9px;font-size:12.5px}
.seg{display:inline-flex;border:1px solid var(--line2);border-radius:var(--r2);overflow:hidden;box-shadow:var(--sh1)}.seg .btn{border:0;border-radius:0;box-shadow:none}.seg .btn+.btn{border-left:1px solid var(--line2)}
select,input[type=search]{padding:7px 10px;border:1px solid var(--line2);border-radius:var(--r2);background:var(--s1);color:var(--ink);box-shadow:inset 0 1px 2px rgba(0,0,0,.4)}
select{appearance:none;-webkit-appearance:none;padding-right:28px;background:var(--s1) url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='10' height='6'%3E%3Cpath d='M0 0l5 6 5-6z' fill='%239c9a95'/%3E%3C/svg%3E") no-repeat right 10px center;cursor:pointer}
select:hover,input[type=search]:hover{border-color:rgba(255,255,255,.3)}select:focus,input[type=search]:focus{border-color:var(--red3);outline:none;box-shadow:0 0 0 3px rgba(228,0,15,.25)}
input[type=search]{min-width:260px;flex:1;max-width:560px;background:var(--s1) url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='16' height='16' fill='none' stroke='%239c9a95' stroke-width='2'%3E%3Ccircle cx='7' cy='7' r='5'/%3E%3Cpath d='M11 11l4 4'/%3E%3C/svg%3E") no-repeat 10px center;padding-left:32px}
input[type=checkbox]{appearance:none;-webkit-appearance:none;width:18px;height:18px;border:2px solid var(--silver);border-radius:4px;background:var(--bg1);display:inline-grid;place-content:center;cursor:pointer;margin:0;vertical-align:middle;transition:background .12s,border-color .12s}
input[type=checkbox]::before{content:"";width:10px;height:10px;transform:scale(0);background:#fff;clip-path:polygon(14% 44%,0 65%,50% 100%,100% 16%,80% 0,43% 62%);transition:transform .12s}
input[type=checkbox]:checked{background:var(--red);border-color:var(--red3)}input[type=checkbox]:checked::before{transform:scale(1)}
.small{font-size:12px;color:var(--muted)}.muted{color:var(--muted)}.mono{font-family:Consolas,"Cascadia Mono",monospace;font-size:12px}
.tag{display:inline-flex;align-items:center;padding:2px 8px;border-radius:999px;font-size:11px;font-weight:600;letter-spacing:.2px;border:1px solid var(--line2);background:rgba(255,255,255,.04);color:var(--ink2);margin:1px 4px 1px 0;white-space:nowrap;line-height:1.5}
.tag.k{border-color:rgba(57,211,122,.45);color:var(--ok);background:rgba(57,211,122,.08)}.tag.r{border-color:rgba(240,160,48,.5);color:var(--warn);background:rgba(240,160,48,.08)}
.tag.u{border-color:rgba(255,90,90,.5);color:var(--bad);background:rgba(255,90,90,.08)}.tag.b{border-color:rgba(74,155,255,.5);color:var(--info);background:rgba(74,155,255,.1)}.tag.g{border-color:var(--line2);color:var(--silver)}
.warnline{color:var(--warn);font-size:12px}
/* ---------- hero ---------- */
.hero{position:relative;overflow:hidden;background:radial-gradient(1200px 500px at 18% -10%,rgba(228,0,15,.28),transparent 60%),radial-gradient(900px 400px at 85% 10%,rgba(92,148,252,.12),transparent 60%),linear-gradient(180deg,#1b1b21 0%,#111115 60%,#0b0b0e 100%);border-bottom:1px solid var(--line)}
.hero::before{content:"";position:absolute;inset:0;background-image:radial-gradient(rgba(255,255,255,.07) 1px,transparent 1.2px);background-size:22px 22px;opacity:.5;mask-image:linear-gradient(180deg,rgba(0,0,0,.9),transparent 85%);-webkit-mask-image:linear-gradient(180deg,rgba(0,0,0,.9),transparent 85%);pointer-events:none}
.hero::after{content:"";position:absolute;left:-10%;top:0;width:60%;height:100%;background:linear-gradient(100deg,transparent 40%,rgba(228,0,15,.07) 50%,transparent 60%);pointer-events:none}
.hero-inner{position:relative;max-width:min(var(--maxw),2200px);margin:0 auto;padding:14px 28px 4px;display:grid;grid-template-columns:minmax(0,1.5fr) minmax(300px,.9fr);gap:18px;align-items:center;z-index:1}
.wordmark{height:22px;width:auto;filter:drop-shadow(0 2px 0 rgba(0,0,0,.6))}
.hero h1{font-family:var(--display);font-weight:700;font-size:clamp(26px,2.3vw,40px);line-height:1;letter-spacing:-.3px;margin:6px 0 5px;text-transform:uppercase;color:#fff;text-shadow:0 2px 0 rgba(0,0,0,.5),0 12px 40px rgba(0,0,0,.6);white-space:nowrap}
.hero h1 span{color:var(--red3);font-weight:600;letter-spacing:3px;margin-left:8px}
.hero p{max-width:680px;color:var(--ink2);font-size:13px;line-height:1.4;margin:0 0 8px}
.pills{display:flex;flex-wrap:wrap;gap:6px}.pill{display:inline-flex;align-items:center;gap:6px;padding:3px 9px;border-radius:999px;background:rgba(255,255,255,.06);border:1px solid var(--line2);font-size:11.5px;color:var(--ink2);backdrop-filter:blur(6px)}
.pill b{font-family:var(--display);font-size:13px;color:#fff}.pill .led{width:8px;height:8px;border-radius:50%;background:var(--red3);box-shadow:0 0 8px var(--red3),0 0 2px #fff inset}
.pixlabel{font-family:var(--pixel);font-size:9px;letter-spacing:1px;color:var(--silver);text-transform:uppercase}
.hardware{position:relative;display:flex;flex-direction:row;align-items:flex-end;justify-content:flex-end;gap:12px;filter:drop-shadow(0 18px 24px rgba(0,0,0,.7))}
.hardware svg{width:100%;max-width:270px;height:auto}.hardware .pad{max-width:140px;margin:0 0 6px}
.parade{position:relative;height:86px;margin-top:0}
.parade .ground{position:absolute;left:0;right:0;bottom:0;height:22px;background:url("brand/oem/tile-ground.png") repeat-x;background-size:22px 22px;image-rendering:pixelated;border-top:2px solid rgba(0,0,0,.4);box-shadow:0 -12px 24px rgba(0,0,0,.35)}
.parade .cast{position:absolute;left:0;right:0;bottom:22px;display:flex;justify-content:center;align-items:flex-end;gap:clamp(8px,1.1vw,22px);padding:0 16px;overflow:hidden}
.parade .cast img{flex:none;filter:drop-shadow(0 3px 0 rgba(0,0,0,.35))}.parade .cast img.float{margin-bottom:18px}.parade .cast img.f2{margin-bottom:30px}.parade .cast img.f3{margin-bottom:10px}
@keyframes bob{0%,100%{transform:translateY(0)}50%{transform:translateY(-4px)}}.parade .cast img.float{animation:bob 3.2s ease-in-out infinite}.parade .cast img.f2{animation-duration:4.1s}.parade .cast img.f3{animation-duration:2.6s}
@media (max-width:1180px){.parade .cast img.lg{display:none}}
/* ---------- sticky toolbar ---------- */
.toolbar{position:sticky;top:0;z-index:30;background:rgba(18,18,22,.86);backdrop-filter:blur(14px) saturate(140%);border-bottom:1px solid var(--line2);box-shadow:0 8px 30px rgba(0,0,0,.45)}
.toolbar .in{max-width:var(--maxw);margin:0 auto;padding:10px 20px 8px;display:flex;flex-direction:column;gap:8px}
.row{display:flex;flex-wrap:wrap;gap:8px;align-items:center}
.brand{display:flex;align-items:center;gap:10px}.row.bar{justify-content:space-between;flex-wrap:nowrap}.actions{display:flex;align-items:center;gap:8px;flex-wrap:wrap;justify-content:flex-end}.row.tools{padding-top:2px}
.brand .led{width:9px;height:9px;border-radius:50%;background:var(--red3);box-shadow:0 0 10px var(--red3)}.brand .nm{font-family:var(--display);font-weight:700;letter-spacing:1.5px;font-size:15px;text-transform:uppercase;color:#fff}.brand .nm small{display:block;font-family:var(--pixel);font-size:7px;letter-spacing:1px;color:var(--muted);margin-top:3px}
.stats{font-weight:600;color:var(--ink2);padding:0 6px;white-space:nowrap}.stats b{color:#fff}.stats .over{color:var(--bad)}
.letters{gap:3px}.letters .lbl{font-family:var(--pixel);font-size:8px;color:var(--muted);margin-right:6px;letter-spacing:1px}.letters a{display:inline-block;min-width:24px;text-align:center;padding:3px 4px;border-radius:5px;color:var(--ink2);font-weight:600;font-size:12.5px;cursor:pointer;text-decoration:none;border:1px solid transparent}.letters a:hover{background:rgba(255,255,255,.08);border-color:var(--line2);color:#fff}
.notice{display:flex;align-items:center;gap:10px;font-size:12px;color:var(--ink2);white-space:nowrap;cursor:help}.notice b{color:#fff}.notice .meter{flex:none;width:120px;height:8px;border-radius:999px;background:rgba(255,255,255,.08);overflow:hidden;border:1px solid var(--line2)}.notice .meter i{display:block;height:100%;background:linear-gradient(90deg,var(--pipe),var(--gold) 70%,var(--red3));width:0%;transition:width .3s}.notice .meter.over i{background:var(--red)}

/* ---------- filters modal, select menu, chips, secondary line ---------- */
.modal{position:fixed;inset:0;background:rgba(0,0,0,.66);backdrop-filter:blur(5px);display:none;z-index:45;align-items:flex-start;justify-content:center;padding:70px 18px 18px}.modal.open{display:flex}
.mbox{background:linear-gradient(180deg,#22222a,#15151a);border:1px solid var(--line2);border-radius:var(--r3);width:min(960px,100%);box-shadow:var(--sh3);padding:18px 20px 20px;animation:pop .2s var(--ease);max-height:88vh;overflow:auto}
.mhd{display:flex;align-items:center;gap:12px;margin-bottom:14px;padding-bottom:10px;border-bottom:1px solid var(--line)}.mhd h2{font-family:var(--display);font-size:22px;margin:0;letter-spacing:1px;text-transform:uppercase;flex:1}
.fgrid{display:grid;grid-template-columns:repeat(auto-fit,minmax(260px,1fr));gap:12px 16px}.fgrid label{display:flex;flex-direction:column;gap:5px;font-size:11.5px;color:var(--muted);font-weight:600;letter-spacing:.4px;text-transform:uppercase}.fgrid select{width:100%;font-size:14px;text-transform:none;letter-spacing:0;font-weight:400}
.badge{display:inline-grid;place-content:center;min-width:18px;height:18px;padding:0 5px;border-radius:999px;background:var(--red);color:#fff;font-size:11px;font-weight:700;margin-left:2px}.badge.zero{background:rgba(255,255,255,.12);color:var(--silver)}
.menuwrap{position:relative}.menu{position:absolute;left:0;top:calc(100% + 6px);min-width:290px;background:linear-gradient(180deg,#26262e,#1a1a20);border:1px solid var(--line2);border-radius:var(--r2);box-shadow:var(--sh2);padding:6px;z-index:35;display:flex;flex-direction:column;gap:2px}
.menu .mi{display:flex;align-items:center;gap:8px;width:100%;text-align:left;padding:8px 10px;border:0;border-radius:7px;background:transparent;color:var(--ink);cursor:pointer;font:inherit}.menu .mi:hover{background:rgba(255,255,255,.08)}.menu .mi.danger{color:var(--red3)}.menu .mi.danger:hover{background:rgba(228,0,15,.15)}.menu .sep{height:1px;background:var(--line);margin:4px 2px}
.chips{gap:6px}.chip{display:inline-flex;align-items:center;gap:6px;padding:3px 8px 3px 10px;border-radius:999px;background:rgba(228,0,15,.12);border:1px solid rgba(255,59,69,.45);font-size:12px;color:var(--ink)}.chip b{color:var(--red3);font-weight:600}.chip .rm{cursor:pointer;color:var(--muted);font-weight:700;padding:0 2px;border-radius:50%}.chip .rm:hover{color:#fff;background:var(--red2)}.chips .lbl{font-family:var(--pixel);font-size:8px;color:var(--muted);letter-spacing:1px;margin-right:4px}
.row.sub{justify-content:space-between;align-items:center;gap:14px}.row.sub .letters{display:flex;flex-wrap:wrap;align-items:center;gap:2px;flex:1;min-width:0}

/* ---------- compatibility icons + tooltip, install labels, rankings, empty tray ---------- */
.ci{display:inline-grid;place-content:center;width:20px;height:20px;border-radius:50%;border:1px solid var(--line2);background:rgba(255,255,255,.05);color:var(--silver);cursor:help;padding:0;margin-left:5px;vertical-align:middle;font:inherit;line-height:1}
.ci svg{width:12px;height:12px}.ci:hover,.ci:focus-visible{border-color:currentColor;background:rgba(255,255,255,.1)}
.ci-info{color:var(--info)}.ci-alt{color:var(--warn);border-color:rgba(240,160,48,.5)}.ci-uncertain{color:var(--gold)}.ci-blocker{color:var(--bad);border-color:rgba(255,90,90,.6)}
#tip{position:fixed;z-index:80;display:none;max-width:380px;background:linear-gradient(180deg,#26262e,#17171c);border:1px solid var(--line2);border-radius:var(--r2);box-shadow:var(--sh3);padding:12px 14px;font-size:13px;color:var(--ink2);line-height:1.45}
#tip.open{display:block}#tip h4{margin:0 0 6px;font-family:var(--display);font-size:14px;letter-spacing:.3px;color:#fff;display:flex;align-items:center;gap:8px}#tip h4 i{display:inline-grid;place-content:center;width:18px;height:18px;border-radius:50%;border:1px solid currentColor}#tip h4 i svg{width:11px;height:11px}
#tip.alt h4{color:var(--warn)}#tip.uncertain h4{color:var(--gold)}#tip.blocker h4{color:var(--bad)}#tip.info h4{color:var(--info)}#tip p{margin:0 0 5px}#tip p:last-child{margin:0}#tip .ev{color:var(--muted);font-size:12px;margin-top:6px;padding-top:6px;border-top:1px solid var(--line)}
.inst{font-weight:700}.inst.i-builtin{border-color:rgba(74,155,255,.55);color:var(--info);background:rgba(74,155,255,.12)}.inst.i-revision{border-color:rgba(242,182,50,.55);color:var(--gold);background:rgba(242,182,50,.1)}.inst.i-review{border-color:rgba(255,90,90,.55);color:var(--bad);background:rgba(255,90,90,.1)}.inst.i-new{border-color:rgba(57,211,122,.45);color:var(--ok);background:rgba(57,211,122,.08)}
.rk{font-family:var(--display);font-weight:700;color:var(--silver);white-space:nowrap}.rk.top{color:var(--gold)}.rk .dash{color:var(--dim);font-weight:400}.rk small{display:block;font-family:var(--font);font-weight:400;font-size:10.5px;color:var(--muted)}
.rankbadge{position:absolute;right:8px;top:8px;z-index:2;padding:3px 7px;border-radius:999px;background:rgba(20,20,24,.85);border:1px solid var(--line2);color:var(--silver);font-family:var(--pixel);font-size:6.5px;letter-spacing:.5px;pointer-events:none;backdrop-filter:blur(4px)}.rankbadge.top{border-color:rgba(242,182,50,.7);color:var(--gold)}
.sortsel{max-width:220px}
#details .ranks{display:grid;grid-template-columns:repeat(auto-fit,minmax(150px,1fr));gap:8px;margin:6px 0 8px}#details .ranks .rb{background:rgba(255,255,255,.04);border:1px solid var(--line);border-radius:var(--r2);padding:8px 10px}#details .ranks .rb b{display:block;font-family:var(--display);font-size:20px;color:#fff}#details .ranks .rb.top b{color:var(--gold)}#details .ranks .rb span{font-size:11.5px;color:var(--muted)}
#tray li.empty{flex-direction:column;align-items:center;text-align:center;padding:26px 22px 22px;border-bottom:0;gap:6px}#tray li.empty .scene{position:relative;width:100%;max-width:330px;height:150px;margin:0 auto 6px;border-radius:var(--r3);background:radial-gradient(260px 120px at 50% 100%,rgba(228,0,15,.22),transparent 70%),linear-gradient(180deg,rgba(255,255,255,.04),rgba(255,255,255,.01));border:1px solid var(--line);overflow:hidden}
#tray li.empty .scene svg{position:absolute;left:50%;top:10px;transform:translateX(-50%);width:120px;height:auto;filter:drop-shadow(0 10px 16px rgba(0,0,0,.6))}#tray li.empty .scene .cast{position:absolute;left:0;right:0;bottom:16px;display:flex;justify-content:center;align-items:flex-end;gap:14px}#tray li.empty .scene .ground{position:absolute;left:0;right:0;bottom:0;height:16px;background:url("brand/oem/tile-ground.png") repeat-x;background-size:16px 16px;image-rendering:pixelated}
#tray li.empty h3{margin:4px 0 0;font-family:var(--display);font-size:18px;letter-spacing:1.5px;text-transform:uppercase;color:#fff}#tray li.empty p{margin:0;color:var(--ink2);font-size:13px;max-width:300px}#tray li.empty .hint{display:inline-flex;align-items:center;gap:8px;margin-top:8px;padding:6px 10px;border-radius:999px;border:1px solid var(--line2);background:rgba(255,255,255,.04);font-size:12px;color:var(--muted)}
#tray .dec{font-size:12px;padding:3px 24px 3px 8px;margin-top:4px;max-width:100%}
.howto .box{position:relative;padding-right:150px}.howto .deco{position:absolute;right:18px;bottom:14px;display:flex;gap:12px;align-items:flex-end;opacity:.9}.mhd .deco{display:flex;gap:8px;align-items:flex-end;margin-right:4px}
@media (max-width:900px){.howto .deco{display:none}.howto .box{padding-right:18px}}
.ci-input{color:#c58bff;border-color:rgba(197,139,255,.5)}.ci-input-opt{color:#8ab4ff}.ci-input-unk{color:var(--dim)}#tip.input h4{color:#c58bff}#tip.input-opt h4{color:#8ab4ff}#tip.input-unk h4{color:var(--silver)}
.policy{margin-top:6px;padding:7px 9px;border-radius:var(--r2);border:1px solid rgba(74,155,255,.35);background:rgba(74,155,255,.07);font-size:12px;color:var(--ink2)}.policy b{color:#fff}.policy .pend{display:block;margin-top:3px;color:var(--muted);font-size:11.5px}
.hwwarn{margin-top:6px;padding:6px 9px;border-radius:var(--r2);border:1px solid rgba(197,139,255,.45);background:rgba(197,139,255,.08);font-size:12px;color:var(--ink2)}
/* ---------- Open in Hakchi ---------- */
.btn.hk{background:linear-gradient(180deg,#ff2a36,var(--red2));border-color:#ff5a63;color:#fff;font-weight:700;letter-spacing:.3px;box-shadow:0 2px 12px var(--glow);padding:8px 14px}.btn.hk .ico{width:15px;height:15px}.btn.hk.off{background:linear-gradient(180deg,#3a3a44,#2a2a32);border-color:var(--line2);color:var(--silver);box-shadow:none}.btn.hk .cnt{font-family:var(--display);font-size:14px;margin:0 2px}
.btn.secondary{background:transparent;border-color:var(--line2);color:var(--ink2)}
#hkpanel .mbox{width:min(1100px,100%)}#hkpanel .sum{display:grid;grid-template-columns:repeat(auto-fit,minmax(150px,1fr));gap:8px;margin:6px 0 12px}#hkpanel .sum .sb{background:rgba(255,255,255,.04);border:1px solid var(--line);border-radius:var(--r2);padding:8px 10px}#hkpanel .sum .sb b{display:block;font-family:var(--display);font-size:22px;color:#fff}#hkpanel .sum .sb span{font-size:11.5px;color:var(--muted)}#hkpanel .sum .sb.warn b{color:var(--warn)}#hkpanel .sum .sb.bad b{color:var(--bad)}#hkpanel .sum .sb.ok b{color:var(--ok)}#hkpanel .sum .sb.info b{color:var(--info)}
#hkpanel table.pv{width:100%;border-collapse:collapse;font-size:12.5px;margin-top:6px}#hkpanel table.pv th{font-family:var(--display);font-size:11.5px;letter-spacing:.5px;text-transform:uppercase;color:var(--silver);text-align:left;padding:5px 8px;border-bottom:1px solid var(--line2);position:sticky;top:0;background:#1d1d24}#hkpanel table.pv td{padding:5px 8px;border-bottom:1px solid var(--line);vertical-align:top}
#hkpanel .pvwrap{max-height:38vh;overflow:auto;border:1px solid var(--line);border-radius:var(--r2)}
.st{display:inline-block;padding:1px 7px;border-radius:999px;font-size:11px;font-weight:600;border:1px solid var(--line2)}.st.ok{color:var(--ok);border-color:rgba(57,211,122,.45)}.st.info{color:var(--info);border-color:rgba(74,155,255,.5)}.st.warn{color:var(--warn);border-color:rgba(240,160,48,.5)}.st.bad{color:var(--bad);border-color:rgba(255,90,90,.5)}.st.dim{color:var(--muted)}
#hkpanel .msg{margin-top:10px;padding:9px 12px;border-radius:var(--r2);border:1px solid var(--line2);background:rgba(255,255,255,.04);font-size:13px;color:var(--ink2);line-height:1.5}#hkpanel .msg.bad{border-color:rgba(255,90,90,.5);background:rgba(255,90,90,.08)}#hkpanel .msg.ok{border-color:rgba(57,211,122,.45);background:rgba(57,211,122,.07)}#hkpanel .msg.warn{border-color:rgba(240,160,48,.5);background:rgba(240,160,48,.08)}#hkpanel .msg code{font-family:Consolas,monospace;font-size:12px;background:rgba(0,0,0,.35);padding:1px 5px;border-radius:4px}
#hkpanel .msg.envl{font-size:12px}#hkpanel .msg.envl .btn{padding:4px 9px;font-size:12px}#hkpanel .acts{display:flex;gap:8px;flex-wrap:wrap;align-items:center;margin-top:12px}#hkpanel .acts .sp{flex:1}
#hkpanel ol{margin:6px 0 0 18px;padding:0}#hkpanel li{margin:3px 0}
/* ---------- main ---------- */
main{max-width:var(--maxw);margin:0 auto;padding:16px 20px 30px}
.view{animation:fadein .25s var(--ease)}@keyframes fadein{from{opacity:0;transform:translateY(4px)}to{opacity:1;transform:none}}
table{width:100%;border-collapse:separate;border-spacing:0;background:var(--bg1);border:1px solid var(--line2);border-radius:var(--r2)}thead th:first-child{border-top-left-radius:var(--r2)}thead th:last-child{border-top-right-radius:var(--r2)}
th,td{padding:8px 10px;vertical-align:top;text-align:left}th{position:sticky;top:var(--tb-h);background:#202028;cursor:pointer;user-select:none;z-index:2;font-family:var(--display);font-weight:600;font-size:12.5px;letter-spacing:.6px;text-transform:uppercase;color:var(--silver);border-bottom:1px solid var(--line2)}th:hover{color:#fff}
tr.pick{cursor:pointer}tr.pick td{border-top:1px solid var(--line)}tbody tr.pick:nth-child(even):not(.sel):not(.review) td{background:rgba(255,255,255,.035)}
tr.pick:hover td{background:rgba(255,255,255,.05)}tr.sel td{background:rgba(228,0,15,.10)!important}tr.sel td:first-child{box-shadow:inset 4px 0 0 var(--red)}tr.review td{background:rgba(242,182,50,.07)}
td .ttl{font-weight:700;font-size:14.5px;color:#fff}.det{display:inline-flex;align-items:center;cursor:pointer;color:var(--silver);border:1px solid var(--line2);border-radius:999px;padding:1px 9px;font-size:11px;margin-left:8px;background:rgba(255,255,255,.04);line-height:1.5}.det:hover{color:#fff;border-color:var(--red3);background:rgba(228,0,15,.15)}
button.det,button.star{font:inherit}
.thumb{width:48px;height:68px;object-fit:cover;border-radius:4px;background:#000;box-shadow:var(--sh1);border:1px solid rgba(255,255,255,.1)}
.star{cursor:pointer;color:#55545a;font-size:19px;line-height:1;user-select:none;background:none;border:0;padding:2px 4px;border-radius:6px;transition:transform .12s,color .12s}.star:hover{transform:scale(1.2);color:#9a8a4a}.star.on{color:var(--gold);text-shadow:0 0 8px rgba(242,182,50,.6)}
.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(188px,1fr));gap:14px}
.card{background:linear-gradient(180deg,var(--s2),var(--s1));border:1px solid var(--line2);border-radius:var(--r3);position:relative;display:flex;flex-direction:column;cursor:pointer;overflow:hidden;box-shadow:var(--sh1);transition:transform .18s var(--ease),box-shadow .18s,border-color .18s;max-width:300px;width:100%}
.card:hover{transform:translateY(-4px);box-shadow:var(--sh2);border-color:rgba(255,255,255,.3)}.card.sel{border-color:var(--red3);box-shadow:0 0 0 1px var(--red),0 10px 30px var(--glow)}.card.review{border-color:rgba(242,182,50,.5)}
.card .cover{position:relative;aspect-ratio:3/4.3;background:#000;border-bottom:1px solid var(--line)}.card .cover img,.card .cover .ph{width:100%;height:100%;object-fit:cover;aspect-ratio:auto}
.card .cover::after{content:"";position:absolute;inset:0;background:linear-gradient(180deg,rgba(0,0,0,.15),transparent 30%,transparent 80%,rgba(0,0,0,.35));pointer-events:none}
.card .selbadge{position:absolute;left:8px;top:8px;z-index:2;display:none;align-items:center;gap:5px;padding:3px 8px;border-radius:999px;background:var(--red);color:#fff;font-family:var(--pixel);font-size:7px;letter-spacing:1px;box-shadow:0 2px 10px var(--glow);pointer-events:none}.card.sel .selbadge{display:inline-flex}
.card .body{padding:10px 11px 8px;display:flex;flex-direction:column;gap:5px;flex:1}
.card .t{font-weight:700;font-size:14px;line-height:1.25;color:#fff}.card .m{font-size:12px;color:var(--muted)}
.card .ctl{display:flex;justify-content:space-between;align-items:center;margin-top:auto;padding-top:6px;border-top:1px solid var(--line);gap:6px}.card .ctl label{display:inline-flex;align-items:center;gap:6px;font-size:12.5px;color:var(--ink2);cursor:pointer}
.ph{display:flex;align-items:center;justify-content:center;background:linear-gradient(135deg,#3a3a44,#17171b);color:#e6e6e6;font-family:var(--pixel);font-size:8px;text-align:center;padding:6px;border-radius:4px;aspect-ratio:3/4.3;cursor:pointer;letter-spacing:1px}
.ph span{border:2px solid var(--red);padding:6px 8px;border-radius:4px;line-height:1.8}
/* ---------- tray ---------- */
#tray{position:fixed;right:0;top:0;height:100vh;width:min(440px,100vw);background:linear-gradient(180deg,#1b1b21,#121216);border-left:1px solid var(--line2);box-shadow:-20px 0 60px rgba(0,0,0,.6);transform:translateX(102%);transition:transform .28s var(--ease);z-index:40;display:flex;flex-direction:column}
#tray.open{transform:none}#tray .hd{padding:14px 16px;border-bottom:1px solid var(--line2);display:flex;justify-content:space-between;align-items:center;gap:10px;background:rgba(228,0,15,.06)}
#tray .hd .ttl{font-family:var(--display);font-weight:700;font-size:18px;letter-spacing:1px;text-transform:uppercase;display:flex;align-items:center;gap:10px}
#tray .sub{padding:10px 16px;border-bottom:1px solid var(--line);font-size:12.5px;color:var(--ink2);display:flex;flex-direction:column;gap:6px}
#tray ul{list-style:none;margin:0;padding:0;overflow:auto;flex:1}#tray li{display:flex;gap:10px;padding:9px 16px;border-bottom:1px solid var(--line);align-items:flex-start}#tray li:hover{background:rgba(255,255,255,.03)}
#tray li img{width:36px;height:51px;object-fit:cover;border-radius:3px;background:#000;border:1px solid var(--line2)}#tray .tl{cursor:pointer;color:#fff}#tray .tl:hover{color:var(--red3)}
#tray .x{cursor:pointer;color:var(--muted);font-weight:700;margin-left:auto;border:1px solid transparent;border-radius:6px;padding:2px 7px;line-height:1.2}#tray .x:hover{color:#fff;background:var(--red2);border-color:var(--red3)}
#tray .ft{padding:12px 16px;border-top:1px solid var(--line2);display:flex;gap:8px;justify-content:space-between;align-items:center;background:var(--bg1)}
/* ---------- details ---------- */
#details{position:fixed;inset:0;background:rgba(0,0,0,.72);backdrop-filter:blur(6px);display:none;z-index:50;align-items:center;justify-content:center;padding:18px}
#details.open{display:flex}#details .box{background:linear-gradient(180deg,#22222a,#15151a);border:1px solid var(--line2);border-radius:var(--r3);max-width:1040px;width:100%;max-height:92vh;overflow:auto;padding:22px;display:grid;grid-template-columns:300px 1fr;gap:24px;box-shadow:var(--sh3);position:relative;animation:pop .22s var(--ease)}
@keyframes pop{from{opacity:0;transform:scale(.97) translateY(8px)}to{opacity:1;transform:none}}
#details img{width:100%;border-radius:var(--r2);background:#000;box-shadow:var(--sh2);border:1px solid var(--line2)}
#details h2{font-family:var(--display);font-size:30px;line-height:1.05;margin:0 0 4px;color:#fff;letter-spacing:-.3px}
#details .sec{margin-top:16px;padding-top:10px;border-top:1px solid var(--line)}#details .sec h3{margin:0 0 6px;font-family:var(--pixel);font-size:8px;letter-spacing:1.5px;color:var(--red3);text-transform:uppercase}
#details dl{display:grid;grid-template-columns:max-content 1fr;gap:4px 14px;font-size:13.5px;margin:0}#details dt{color:var(--muted)}#details dd{margin:0;color:var(--ink2)}
#details .close{position:absolute;right:16px;top:12px;font-size:20px;cursor:pointer;color:var(--muted);width:34px;height:34px;display:grid;place-content:center;border-radius:50%;border:1px solid transparent}#details .close:hover{color:#fff;background:rgba(255,255,255,.08);border-color:var(--line2)}
#details .side{display:flex;flex-direction:column;gap:10px}#details .side label{display:inline-flex;align-items:center;gap:8px;cursor:pointer}
/* ---------- hover preview ---------- */
#hover{position:fixed;z-index:60;pointer-events:none;display:none;background:linear-gradient(180deg,#26262e,#16161b);border:1px solid var(--line2);border-top:3px solid var(--red);border-radius:var(--r2);box-shadow:var(--sh3);padding:8px}#hover img{width:360px;max-width:80vw;height:auto;display:block;border-radius:6px}#hover .cap{font-size:12.5px;color:var(--ink2);margin-top:6px;max-width:360px;font-weight:600}
/* ---------- misc ---------- */
.toast{position:fixed;left:50%;bottom:22px;transform:translateX(-50%) translateY(10px);background:#1d1d23;color:#fff;padding:10px 16px;border-radius:var(--r2);font-size:13px;opacity:0;transition:opacity .2s,transform .2s;z-index:70;pointer-events:none;border:1px solid var(--line2);border-left:4px solid var(--red);box-shadow:var(--sh2);max-width:min(560px,90vw)}.toast.show{opacity:1;transform:translateX(-50%)}
.howto{max-width:var(--maxw);margin:0 auto;padding:0 20px 30px}.howto .box{background:var(--bg1);border:1px solid var(--line2);border-radius:var(--r3);padding:16px 18px}
.howto h2{font-family:var(--display);font-size:18px;margin:0 0 10px;letter-spacing:1px;text-transform:uppercase}.steps{display:grid;grid-template-columns:repeat(auto-fit,minmax(240px,1fr));gap:12px}.step{display:flex;gap:10px;font-size:13px;color:var(--ink2)}.step .n{flex:none;width:26px;height:26px;border-radius:50%;background:var(--red);color:#fff;display:grid;place-content:center;font-family:var(--pixel);font-size:9px}
footer{border-top:1px solid var(--line2);background:linear-gradient(180deg,#0f0f12,#070708);padding:22px 20px 26px}footer .in{max-width:var(--maxw);margin:0 auto}
footer .cast{display:flex;flex-wrap:wrap;align-items:flex-end;justify-content:center;gap:16px 18px;padding:10px 0 14px;border-bottom:1px solid var(--line)}footer .cast img{opacity:.9;transition:transform .15s,opacity .15s}footer .cast img:hover{transform:translateY(-4px);opacity:1}
footer .support{font-size:12.5px;color:var(--muted);max-width:1100px;margin:14px auto 0;text-align:center;line-height:1.6}footer .support a{color:var(--ink2);font-weight:600;text-decoration:none;display:inline-flex;align-items:center;gap:6px}footer .support a:hover{color:#fff;text-decoration:underline}footer .support .heart{image-rendering:pixelated;flex:none}
footer .legal{font-size:12px;color:var(--dim);max-width:1100px;margin:14px auto 0;text-align:center;line-height:1.6}
@media (prefers-reduced-motion:reduce){*,*::before,*::after{animation:none!important;transition:none!important;scroll-behavior:auto!important}}
@media (max-width:1400px){.row.sub{flex-direction:column;align-items:flex-start;gap:4px}.row.sub .letters{flex:none;width:100%}.notice{white-space:normal}.toolbar .in{padding:6px 12px 6px;gap:5px}.toolbar input[type=search]{min-width:180px}.toolbar .btn{padding:5px 8px;font-size:12.5px}.toolbar select,.toolbar input[type=search]{padding:5px 8px;font-size:13px}.brand .nm{font-size:14px}.stats{font-size:12.5px}.letters a{min-width:20px;padding:1px 3px;font-size:12px}.notice{font-size:11.5px}.notice>span:last-child{white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.notice .meter{width:110px}}
@media (max-width:1100px){.hero-inner{grid-template-columns:1fr}.hardware{display:none}.hero h1{font-size:clamp(24px,5vw,36px);white-space:normal}}
@media (max-width:900px){th:nth-child(6),td:nth-child(6),th:nth-child(8),td:nth-child(8),th:nth-child(11),td:nth-child(11){display:none}#details .box{grid-template-columns:1fr}.brand .nm small{display:none}.parade{height:64px}.parade .cast{gap:6px}}
@media (max-width:640px){th:nth-child(7),td:nth-child(7),th:nth-child(10),td:nth-child(10),th:nth-child(13),td:nth-child(13){display:none}.hero-inner{padding:22px 16px 6px}.toolbar .in{padding:8px 12px}main{padding:12px}.grid{grid-template-columns:repeat(auto-fill,minmax(150px,1fr));gap:10px}.howto,footer{padding-left:12px;padding-right:12px}}
__STUDIO_CSS__
</style></head><body>
<a class="skip" href="#main">Skip to catalog</a>
<div class="stickyhead" id="stickyHead">
<header class="hero" id="hero">
 <div class="hero-inner">
  <div>
   <img class="wordmark px" src="brand/oem/nes-wordmark.png" alt="Nintendo Entertainment System" width="137" height="15" onerror="this.style.display='none'">
   <h1>NES Classic<span>Collection Studio</span></h1>
   <p class="lede">Browse NES games, build <b>My Collection</b>, and prepare it for your NES Classic.</p>
   <div class="pills"><span class="pill"><span class="led"></span><b id="heroGames"></b> games</span><span class="pill"><b id="heroSel">0</b> selected</span><span class="pill"><b id="heroFav">0</b> favorites</span></div>
  </div>
  <div class="hardware" aria-hidden="true">
   <svg viewBox="0 0 520 210" xmlns="http://www.w3.org/2000/svg">
    <defs><linearGradient id="gTop" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#d9d9d9"/><stop offset="1" stop-color="#b9b9b9"/></linearGradient><linearGradient id="gBot" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#8e8e8e"/><stop offset="1" stop-color="#6d6d6d"/></linearGradient><linearGradient id="gDoor" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#2a2a2a"/><stop offset="1" stop-color="#111"/></linearGradient><radialGradient id="gLed"><stop offset="0" stop-color="#ff8a90"/><stop offset=".5" stop-color="#ff1f2d"/><stop offset="1" stop-color="#b8000c"/></radialGradient></defs>
    <rect x="10" y="40" width="500" height="150" rx="10" fill="url(#gBot)"/><rect x="10" y="40" width="500" height="88" rx="10" fill="url(#gTop)"/>
    <rect x="10" y="116" width="500" height="4" fill="#9a9a9a"/><rect x="10" y="128" width="500" height="2" fill="#5b5b5b"/>
    <g fill="#333"><rect x="34" y="52" width="4" height="30"/><rect x="42" y="52" width="4" height="30"/><rect x="50" y="52" width="4" height="30"/><rect x="58" y="52" width="4" height="30"/><rect x="66" y="52" width="4" height="30"/><rect x="74" y="52" width="4" height="30"/><rect x="82" y="52" width="4" height="30"/><rect x="90" y="52" width="4" height="30"/><rect x="98" y="52" width="4" height="30"/></g>
    <rect x="64" y="132" width="300" height="46" rx="4" fill="url(#gDoor)"/><rect x="64" y="132" width="300" height="6" fill="#3a3a3a"/>
    <rect x="400" y="52" width="92" height="76" rx="3" fill="#2d2d2d"/><g fill="#555"><rect x="406" y="60" width="80" height="3"/><rect x="406" y="68" width="80" height="3"/><rect x="406" y="76" width="80" height="3"/><rect x="406" y="84" width="80" height="3"/><rect x="406" y="92" width="80" height="3"/><rect x="406" y="100" width="80" height="3"/><rect x="406" y="108" width="80" height="3"/><rect x="406" y="116" width="80" height="3"/></g>
    <rect x="400" y="140" width="42" height="14" rx="3" fill="#2a2a2a"/><rect x="450" y="140" width="42" height="14" rx="3" fill="#2a2a2a"/>
    <circle cx="421" cy="168" r="4.5" fill="url(#gLed)"/><circle cx="421" cy="168" r="9" fill="rgba(255,40,50,.25)"/>
    <text x="434" y="171" font-family="Press Start 2P,monospace" font-size="6" fill="#444">POWER</text><text x="452" y="151" font-family="Press Start 2P,monospace" font-size="5" fill="#bbb">RESET</text>
    <rect x="140" y="186" width="110" height="10" rx="2" fill="#222"/><rect x="262" y="186" width="110" height="10" rx="2" fill="#222"/>
   </svg>
   <svg class="pad" viewBox="0 0 260 118" xmlns="http://www.w3.org/2000/svg">
    <defs><linearGradient id="gPad" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#6c6c6c"/><stop offset="1" stop-color="#4a4a4a"/></linearGradient><linearGradient id="gFace" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#dcdcdc"/><stop offset="1" stop-color="#c2c2c2"/></linearGradient></defs>
    <rect x="0" y="0" width="260" height="118" rx="8" fill="url(#gPad)"/><rect x="8" y="8" width="244" height="102" rx="4" fill="url(#gFace)"/>
    <rect x="8" y="8" width="244" height="20" fill="#1b1b1b"/><text x="92" y="22" font-family="Bahnschrift,Segoe UI,sans-serif" font-weight="700" font-size="11" fill="#e4000f">Nintendo</text>
    <g fill="#e4000f"><rect x="8" y="34" width="244" height="2"/><rect x="8" y="40" width="244" height="2"/><rect x="8" y="46" width="244" height="2"/><rect x="8" y="52" width="244" height="2"/></g>
    <rect x="18" y="30" width="78" height="76" fill="#cfcfcf"/><rect x="36" y="38" width="20" height="60" rx="2" fill="#111"/><rect x="16" y="58" width="60" height="20" rx="2" fill="#111"/><circle cx="46" cy="68" r="6" fill="#222"/>
    <rect x="104" y="60" width="64" height="30" rx="3" fill="#cfcfcf"/><rect x="108" y="74" width="24" height="9" rx="4.5" fill="#111"/><rect x="140" y="74" width="24" height="9" rx="4.5" fill="#111"/>
    <text x="108" y="70" font-family="Press Start 2P,monospace" font-size="5" fill="#e4000f">SELECT</text><text x="141" y="70" font-family="Press Start 2P,monospace" font-size="5" fill="#e4000f">START</text>
    <rect x="178" y="64" width="30" height="30" rx="3" fill="#cfcfcf"/><rect x="214" y="64" width="30" height="30" rx="3" fill="#cfcfcf"/><circle cx="193" cy="79" r="11" fill="#e4000f"/><circle cx="229" cy="79" r="11" fill="#e4000f"/>
    <text x="189" y="104" font-family="Press Start 2P,monospace" font-size="6" fill="#e4000f">B</text><text x="225" y="104" font-family="Press Start 2P,monospace" font-size="6" fill="#e4000f">A</text>
   </svg>
  </div>
 </div>
 <div class="parade" aria-hidden="true"><div class="cast">__HERO_CAST__</div><div class="ground"></div></div>
</header>
<div class="toolbar" id="toolbar"><div class="in">
<div class="row bar">
 <button class="btn howbtn" id="howBtn" title="Complete step-by-step guide to using NES Classic Collection Studio."><svg class="ico book" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linejoin="round"><path d="M2 3.5A1.5 1.5 0 0 1 3.5 2H8v12H3.5A1.5 1.5 0 0 1 2 12.5zM14 3.5A1.5 1.5 0 0 0 12.5 2H8v12h4.5a1.5 1.5 0 0 0 1.5-1.5z"/></svg>Help &amp; User Guide<span class="chc" id="howCount"></span></button>
 <span style="flex:1"></span>
 <div class="actions">
 <div class="menuwrap right"><button id="collBtn" class="btn" aria-haspopup="menu"><img class="px ico" src="brand/oem/icon-cartridge.png" alt="" onerror="this.remove()"><span id="collName">My Collection</span><svg class="ico" viewBox="0 0 10 6"><path d="M0 0l5 6 5-6z" fill="currentColor"/></svg></button>
  <div class="menu wide" id="collMenu" role="menu" hidden><div class="mhead"><div class="nm" id="collNameHead"></div><div class="st"><span class="stt" id="collStatus"><i></i>new</span></div></div>
   <button id="mNew" class="mi" role="menuitem"><b>New Collection</b><span class="d">Start again with an empty collection. The current one stays recoverable.</span></button>
   <button id="mLoadLocal" class="mi" role="menuitem"><b>Load Existing Collection — games already on this computer</b><span class="d">Continue from the games set up on this computer. Looking changes nothing.</span></button>
   <button id="mLoadFile" class="mi" role="menuitem"><b>Load Existing Collection — saved collection file</b><span class="d">Open a collection you saved earlier.</span></button>
   <div class="sep"></div>
   <button id="mOwn" class="mi" role="menuitem"><b>Add Your Own Game</b><span class="d">Bring in a game file you own that is not in the Game Library.</span></button>
   <button id="mRestore" class="mi" role="menuitem"><b>Restore a Previous Collection</b><span class="d">Automatic snapshots kept in this browser.</span></button>
   <button id="mInfo" class="mi" role="menuitem"><b>Collection information</b><span class="d">Name, counts, where it came from and how far it got.</span></button></div></div>
 <div class="menuwrap right"><button id="ioBtn" class="btn secondary" aria-haspopup="menu"><img class="px ico" src="brand/oem/icon-disk.png" alt="" onerror="this.remove()">Save / Load<svg class="ico" viewBox="0 0 10 6"><path d="M0 0l5 6 5-6z" fill="currentColor"/></svg></button>
  <div class="menu wide" id="ioMenu" role="menu" hidden>
   <button id="export" class="mi" role="menuitem"><b>Save Collection to File</b><span class="d">Keep a copy of your game choices to restore or share later.</span></button>
   <button id="importBtn" class="mi" role="menuitem"><b>Load Collection from File</b><span class="d">Open a collection you saved previously.</span></button>
   <button id="sdExport" class="mi" role="menuitem"><b>Export Studio Data</b><span class="d">Favorites, snapshots, settings and the current collection in one file — to move to another browser or to the website.</span></button>
   <button id="sdImport" class="mi" role="menuitem"><b>Import Studio Data</b><span class="d">Bring favorites, snapshots and settings back from a Studio data file.</span></button></div></div>
 <input type="file" id="sdFile" accept="application/json,.json" hidden>
 <button id="trayBtn" class="btn"><img class="px ico" src="brand/oem/icon-b.png" alt="" onerror="this.remove()">Tray<span class="badge" id="trayBadge">0</span></button>
 <button class="btn hk" id="hkBtn" title="Check and prepare your collection; console installation is shown honestly"><img class="px ico" src="brand/oem/icon-a.png" alt="" onerror="this.remove()"><span id="hkLabel">Install on NES Classic</span></button>
 </div>
 <input type="file" id="import" accept="application/json" hidden>
</div>
<div class="row tools">
 <input id="q" type="search" placeholder="Search title or No-Intro name…" aria-label="Search">
 <button id="filtersBtn" class="btn" aria-haspopup="dialog"><svg class="ico" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.8"><path d="M2 3h12M4 8h8M6 13h4"/></svg>Filter &amp; Sort<span class="badge" id="filtersBadge" hidden></span></button>
 <div class="menuwrap"><button id="selectBtn" class="btn" aria-haspopup="menu">Select<svg class="ico" viewBox="0 0 10 6"><path d="M0 0l5 6 5-6z" fill="currentColor"/></svg></button>
  <div class="menu" id="selectMenu" role="menu" hidden><button id="selVis" class="mi" role="menuitem">Select all visible</button><button id="unselVis" class="mi" role="menuitem">Unselect visible</button><button id="selKachi" class="mi" role="menuitem">Select visible kachikachi + verified</button><div class="sep"></div><button id="clear" class="mi danger" role="menuitem"><img class="px ico" src="brand/oem/icon-b.png" alt="" onerror="this.remove()">Clear selection</button></div></div>
 <div class="seg" role="group" aria-label="View"><button id="vList" class="btn active">List</button><button id="vGrid" class="btn">Grid</button></div>
 <span style="flex:1"></span>
 <span class="stats" id="stats"></span>
 <button class="conn" id="connPill" title="Connection to the companion on your computer"><span class="dot"></span>Checking the connection…</button>
</div>
<div class="row chips" id="chips" hidden></div>
<div class="row sub"><div class="letters" id="letters"></div><button class="notice" id="storage" type="button" title="NES Classic storage: how this estimate is made"></button></div>
</div></div>
</div>
<main id="main">
<div id="listView" class="view"><div class="tablewrap" id="tablewrap"><table id="t"><thead><tr><th data-k="sel" title="sort by selection">✓</th><th>Art</th><th data-k="display_title">Title</th><th data-k="rank:consensus" id="thRank" title="consensus ranking across verified Top 100 lists">Rank</th><th data-k="year">Year</th><th data-k="publisher">Publisher</th><th data-k="genre">Genre</th><th data-k="players">Players</th><th data-k="builtin_status">Built-in · Install</th><th data-k="mapper">Mapper</th><th>Identity · Header</th><th data-k="recommended_emulator">Emulator · Compat.</th><th data-k="payload_size">Size</th><th>★</th></tr></thead><tbody id="tb"></tbody></table></div></div>
<div id="gridView" class="grid view" hidden></div>
</main>
<section class="howto"><div class="box"><span class="deco" aria-hidden="true">__HOWTO_CAST__</span><h2>How it works</h2><div class="steps">
<div class="step"><span class="n">1</span><div><b>Choose.</b> Click any game to add it to <b>My Collection</b>; the 30 built-in games are in the Game Library like every other game, and a version chip lets you pick regions and revisions. Switch on the <b>Extended Library</b> for less-common titles.</div></div>
<div class="step"><span class="n">2</span><div><b>Bring in what you have.</b> <b>My Collection ▾</b> loads games already set up on this computer or a saved collection file; <b>Add Your Own Game</b> brings in a game file you own.</div></div>
<div class="step"><span class="n">3</span><div><b>Keep it.</b> Your draft is saved in this browser automatically; <b>Save / Load</b> keeps a portable file (no game files inside).</div></div>
<div class="step"><span class="n">4</span><div><b>Install on NES Classic</b> checks and prepares the collection on this computer and shows, honestly, what the console steps are. <button class="btn sm secondary" type="button" data-guide="start">Open the full guide</button></div></div>
</div><p class="small" style="margin:12px 0 0">Nothing here downloads games or changes your console. Hakchi2 CE provides the console-side technology — see <a href="#" data-guide="tech">About the Technology</a>.</p></div></section>
<footer><div class="in"><div class="cast" aria-label="Character parade">__FOOTER_CAST__</div>
<p class="support"><a href="#" data-guide="legal" class="legallink">Legal &amp; Privacy</a><span class="dotsep"> · </span><a href="https://github.com/sponsors/Rance9" rel="noopener" title="Optional support through GitHub Sponsors"><svg class="heart" viewBox="0 0 9 8" width="14" height="12" aria-hidden="true" shape-rendering="crispEdges"><path fill="#e4000f" d="M1 0h2v1h1v1h1V1h1V0h2v1h1v3H8v1H7v1H6v1H5v1H4V7H3V6H2V5H1V4H0V1h1z"/><path fill="#ff6b73" d="M1 1h1v1H1z"/></svg>Support this project</a><span class="small"> · NES Classic Collection Studio is free and open source. If you've found it useful, optional contributions through GitHub Sponsors are always appreciated and help support continued independent development.</span></p>
<p class="legal">Console-side technology by <a href="https://github.com/TeamShinkansen/Hakchi2-CE" rel="noopener">Hakchi2 CE</a> (GPL v3; see hakchi/LICENSE) — this Studio prepares collections for it and does not reimplement it. Personal, non-commercial fan project for one privately owned NES Classic Edition. Not affiliated with, sponsored or endorsed by Nintendo. Nintendo, Nintendo Entertainment System, NES and all character names and artwork are trademarks and copyrights of Nintendo or their respective owners; the pixel artwork, console lockup and interface icons shown here are Nintendo's own renderings read from this console's system backup and the box art is cached locally from community scans — none of it is redistributed with this file. Catalog <span id="gen"></span>.</p></div></footer>
<div id="hkpanel" class="modal" role="dialog" aria-modal="true" aria-labelledby="hkTitle"><div class="mbox"><div class="mhd"><h2 id="hkTitle">Install on NES Classic</h2><span class="small" id="hkSub"></span><button class="btn sm" id="hkClose">Close</button></div><div id="hkBody"></div></div></div>
<div id="filters" class="modal" role="dialog" aria-modal="true" aria-labelledby="filtersTitle"><div class="mbox">
<div class="mhd"><h2 id="filtersTitle">Filter &amp; Sort</h2><span class="deco" aria-hidden="true">__FILTERS_CAST__</span><span class="small" id="filtersCount"></span></div>
<div class="fsec"><h3>Sort games</h3><div class="fgrid">
<label>Sort by<select id="sortSel"></select></label>
<label>Direction<select id="sortDir"><option value="asc">Ascending (A–Z, oldest, smallest, best rank first)</option><option value="desc">Descending</option></select></label>
</div></div>
<div class="fsec"><h3>Library</h3><div class="fgrid">
<label class="ext" id="extLabel" title="Adds regional releases, alternate versions, translations, prototypes and other less-common ROMs to the catalog"><span>Catalog scope</span><span class="extrow"><input type="checkbox" id="extToggle"> Extended Library <button type="button" class="info" id="extInfo" aria-label="What is the Extended Library?">i</button></span><span class="small">Curated North American releases by default; the Extended Library adds every other known version. Selected games are never deselected by this switch.</span></label>
<label>Built-in games<select id="fBuilt"><option value="" selected>show all games (default)</option><option value="hideexact">hide exact factory matches</option><option value="exact">exact factory matches only</option><option value="other">other versions of factory titles</option><option value="not">not built-in only</option></select></label>
<label>Selection<select id="fSel"><option value="">any</option><option value="1">Selected</option><option value="0">Not selected</option><option value="fav">Favorites only</option></select></label>
</div></div>
<div class="fsec"><h3>Game details</h3><div class="fgrid">
<label>Genre<select id="fGenre"><option value="">any</option></select></label>
<label>Publisher<select id="fPub"><option value="">any</option></select></label>
<label>Year<select id="fYear"><option value="">any</option></select></label>
<label>Players<select id="fPlayers"><option value="">any</option><option value="1">1 player</option><option value="2">2 players (mode unknown unless noted)</option><option value="2sim">2 simultaneous (known)</option><option value="2alt">2 alternating (known)</option><option value="3+">3+ players</option><option value="?">unknown</option></select></label>
<label id="fRankLabel">Ranking<select id="fRank"><option value="">all games</option><option value="top10">consensus Top 10</option><option value="top25">consensus Top 25</option><option value="top50">consensus Top 50</option><option value="top100">consensus Top 100</option><option value="any">on any verified Top 100 list</option><option value="two">on at least two lists</option><option value="unranked">unranked games</option></select></label>
</div></div>
<div class="fsec"><h3>Technical filters</h3><div class="fgrid">
<label>Emulator<select id="fEmu"><option value="">any</option><option value="kachikachi">kachikachi (built-in)</option><option value="RetroArch">RetroArch</option><option value="unknown">unknown</option></select></label>
<label>ROM identity / header<select id="fVer"><option value="">any</option><option value="verified">No-Intro verified</option><option value="cartdb">Cartridge-DB only</option><option value="xval">Header cross-validated</option><option value="single">Header single-source</option><option value="review">Needs review</option></select></label>
<label>Licensed<select id="fLic"><option value="">any</option><option value="licensed">Licensed</option><option value="unlicensed">Unlicensed</option></select></label>
<label>Controllers<select id="fInput"><option value="">all games</option><option value="standard">playable with standard NES Classic controller(s)</option><option value="required">special accessory required</option><option value="optional">special accessory optional / some modes</option><option value="lightgun">light-gun games</option><option value="unknown">input compatibility unknown</option></select></label>
</div></div>
<div class="acts facts"><button class="btn sm ghost" id="filtersReset" title="Back to the defaults: no filters, Title A–Z, curated library">Reset filters &amp; sort</button><span class="small">Filtering and sorting change only what is shown, never what is selected. All 30 built-in games are shown by default.</span><span class="sp"></span><button class="btn sm secondary" id="filtersCancel">Cancel</button><button class="btn primary sm" id="filtersDone">Apply</button></div>
</div></div>
<aside id="tray" aria-label="Selection tray"><div class="hd"><span class="ttl"><img class="px" src="brand/oem/icon-cartridge.png" alt="" width="20" height="20" onerror="this.remove()"> My Collection</span><span id="trayCount" class="small"></span><button id="trayClose" class="btn sm">Close</button></div>
<div class="sub" id="traySub"></div><ul id="trayList"></ul><div class="ft"><span class="small">Stars are a shortlist only. <a href="#" data-guide="choose">How choosing works</a></span><button class="btn hk sm" id="trayHk">Install on NES Classic</button> <button class="btn secondary sm" id="trayExport">Save to File</button></div></aside>
<div id="details" role="dialog" aria-modal="true"><div class="box" id="detailsBox"></div></div>
__STUDIO_HTML__
<div class="toast" id="toast"></div>
<div id="hover"><img id="hoverImg" alt=""><div class="cap" id="hoverCap"></div></div>
<div id="tip" role="tooltip"></div>
<script>
const DATA = __DATA__;
let games = DATA.games; const byId = Object.fromEntries(games.map(g=>[g.game_id,g]));
const KEY='nes-classic-selection-v1', FKEY='nes-classic-favorites-v1', VKEY='nes-classic-view-v1', DKEY='nes-classic-decisions-v1';
let sel = new Set(JSON.parse(localStorage.getItem(KEY)||'[]')), fav = new Set(JSON.parse(localStorage.getItem(FKEY)||'[]'));
let decisions = JSON.parse(localStorage.getItem(DKEY)||'{}');   // game_id -> 'factory' | 'alternate' for different-revision / identity-only matches
const RK = DATA.rankings||{}, SRC = DATA.ranking_sources||[];
const EMPTY_CAST = `__EMPTY_CAST__`;
const FACTORY = DATA.factory_inventory||{codes:[]}; const FACTORY_CODES = FACTORY.codes||[]; const FACTORY_TITLES = Object.fromEntries(games.filter(g=>g.builtin_code).map(g=>[g.builtin_code, g.builtin_title]));
let view = localStorage.getItem(VKEY)||'list', sortK='display_title', sortDir=1;
const $=id=>document.getElementById(id); const esc=s=>String(s??'').replace(/[&<>"]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c]));
function persist(){ localStorage.setItem(KEY, JSON.stringify([...sel])); localStorage.setItem(FKEY, JSON.stringify([...fav])); localStorage.setItem(VKEY, view); localStorage.setItem(DKEY, JSON.stringify(decisions)); }
// ---- installation semantics (selection = the collection; installation is derived, never assumed) ----
function installClass(g){ const b=g.builtin_status||'NOT BUILT-IN'; return b==='BUILT-IN: EXACT MATCH'?'builtin':b==='BUILT-IN: DIFFERENT REVISION'?'revision':b==='BUILT-IN: IDENTITY ONLY'?'review':'new'; }
function comparisonStatus(g){ return {builtin:'exact-match',revision:'different-revision',review:'identity-only',new:'not-built-in'}[installClass(g)]; }
function decisionOf(g){ const c=installClass(g); return (c==='revision'||c==='review')?(decisions[g.game_id]||'factory'):null; }
function installRequired(g){ const c=installClass(g); if(c==='new') return true; if(c==='builtin') return false; return decisionOf(g)==='alternate'; }
const INST_LABEL={builtin:['BUILT-IN','BUILT-IN — NO INSTALL NEEDED'],revision:['BUILT-IN · REVISION','BUILT-IN — DIFFERENT REVISION'],review:['BUILT-IN · REVIEW','BUILT-IN — REVIEW REQUIRED'],new:['NEW','NEW GAME — INSTALL REQUIRED']};
function instTag(g, full){ const c=installClass(g); const t=INST_LABEL[c][full?1:0]; const tip=c==='builtin'?`factory game ${g.builtin_code} (${g.builtin_title}) contains this exact ROM. ${visibilityText(g)}`:c==='revision'?`the console has ${g.builtin_factory_version||g.builtin_title}; selecting keeps the factory version unless you choose the catalog version`:c==='review'?`the console has ${g.builtin_factory_version||g.builtin_title} — same title, different release; review before installing`:'not on the console; will be prepared for installation'; return (c==='new'&&!full)?'':`<span class="tag inst i-${c}" title="${esc(tip)}">${t}</span>`; }
// ---- rankings (consensus across verified Top 100 lists; absent = unranked, never a fabricated position) ----
function rankOf(g){ return RK[g.game_id]||null; }
function rankCell(g){ const r=rankOf(g); if(!r||!r.consensus) return '<span class="rk"><span class="dash">—</span></span>'; return `<span class="rk ${r.consensus<=10?'top':''}">#${r.consensus}<small>${r.count} of ${SRC.length} lists</small></span>`; }
function rankBadge(g){ const r=rankOf(g); if(!r||!r.consensus||r.consensus>100) return ''; return `<span class="rankbadge ${r.consensus<=10?'top':''}">${r.consensus<=10?'TOP 10':r.consensus<=25?'TOP 25':r.consensus<=50?'TOP 50':'TOP 100'} · #${r.consensus}</span>`; }
// ---- compatibility: one classification used by list icons, grid icons, tooltips, tray and details ----
function compatInfo(g){ const notes=(g.compat_notes||'').split(';').map(x=>x.trim()).filter(Boolean); const cap=x=>x.charAt(0).toUpperCase()+x.slice(1)+(/[.!?]$/.test(x)?'':'.'); let level='none', title='';
 if(g.recommended_emulator==='unknown'||g.compat_confidence==='none'){ level='blocker'; title='Emulator not determined'; }
 else if(g.recommended_emulator==='RetroArch'){ level='alt'; title='RetroArch required'; }
 else if(g.compat_confidence==='low'){ level='uncertain'; title='Compatibility uncertain'; }
 else if(notes.length){ level='info'; title='Compatibility note'; }
 const lines=[]; if(level==='alt') lines.push(`Recommended emulator: RetroArch (${g.core||'FCEUmm'} core), mapper ${g.mapper??'?'}.`); notes.forEach(n=>lines.push(cap(n)));
 return {level,title,lines,evidence:`Confidence ${g.compat_confidence||'unknown'} — ${g.compat_evidence||''}`}; }
const CI_ICON={info:'<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.8"><circle cx="8" cy="8" r="6.2"/><path d="M8 7.2v4M8 4.8v.2"/></svg>',alt:'<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.8"><path d="M8 2.2l6 11H2z"/><path d="M8 6.5v3.2M8 12v.2"/></svg>',uncertain:'<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.8"><circle cx="8" cy="8" r="6.2"/><path d="M6 6.3a2 2 0 1 1 3 1.7c-.7.4-1 .8-1 1.5M8 12v.2"/></svg>',blocker:'<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.8"><circle cx="8" cy="8" r="6.2"/><path d="M5.5 5.5l5 5M10.5 5.5l-5 5"/></svg>'};
// ---- controller / accessory requirements (separate from emulator compatibility) ----
const INPUT_SUPPORT_TEXT={supported:'Standard NES Classic controllers are sufficient.',partial:'Standard controllers cover the 1–2 player modes; the extra modes need hardware the NES Classic does not have.',unverified:'No compatible alternative has been verified for this console.',unsupported:'The NES Classic has no equivalent of this accessory; not playable with the standard controllers.',unknown:'Input requirements are not documented for this ROM; do not assume it is playable.'};
function inputInfo(g){ const L=g.input_level||'unknown'; if(L==='none') return {level:'none'}; const acc=g.input_special_accessory||'special accessory'; const req=g.input_accessory_requirement; const sup=g.input_nes_classic_support||'unknown';
 const title=L==='required'?`Requires ${acc}`:L==='partial'?`${acc} for some modes`:L==='optional'?`${acc} optional`:L==='unknown'?'Input requirements unknown':`Input note`;
 const lines=[]; if(L==='required') lines.push(`This game needs ${acc} input; it is not playable with a standard controller.`); if(L==='partial') lines.push(g.input_notes||`Some modes need ${acc}.`); if(L==='optional') lines.push(`${acc} is optional; the game plays with a standard controller.`); if(L==='unknown') lines.push(g.input_notes||'No input-device record for this ROM.');
 if((g.input_modes||[]).includes('light-gun')) lines.push('The original NES Zapper is not directly compatible with NES Classic hardware or modern HDMI displays.');
 lines.push(INPUT_SUPPORT_TEXT[sup]||''); const ev=[...new Set((g.input_evidence||[]).map(e=>e.source+(e.device?': '+e.device:'')))].join('; ');
 return {level:L,title,lines:lines.filter(Boolean),evidence:'NES Classic support: '+sup+(ev?' — evidence: '+ev:'')}; }
const INPUT_ICON={required:'<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.6"><path d="M2 6.5a2.5 2.5 0 0 1 2.5-2.5h7A2.5 2.5 0 0 1 14 6.5v3a2.5 2.5 0 0 1-2.5 2.5h-7A2.5 2.5 0 0 1 2 9.5z"/><path d="M5 7v3M3.5 8.5h3M10.5 7.5h.01M12 9h.01"/></svg>',partial:'<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.6"><path d="M2 6.5a2.5 2.5 0 0 1 2.5-2.5h7A2.5 2.5 0 0 1 14 6.5v3a2.5 2.5 0 0 1-2.5 2.5h-7A2.5 2.5 0 0 1 2 9.5z"/><path d="M5 7v3M3.5 8.5h3M10.5 7.5h.01M12 9h.01"/></svg>',optional:'<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.6"><path d="M2 6.5a2.5 2.5 0 0 1 2.5-2.5h7A2.5 2.5 0 0 1 14 6.5v3a2.5 2.5 0 0 1-2.5 2.5h-7A2.5 2.5 0 0 1 2 9.5z"/><path d="M5 7v3M3.5 8.5h3M10.5 7.5h.01M12 9h.01"/></svg>',unknown:'<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.6"><path d="M2 6.5a2.5 2.5 0 0 1 2.5-2.5h7A2.5 2.5 0 0 1 14 6.5v3a2.5 2.5 0 0 1-2.5 2.5h-7A2.5 2.5 0 0 1 2 9.5z"/><path d="M8 6.2v2.2M8 10.2v.1"/></svg>'};
function inBtn(g){ const c=inputInfo(g); if(c.level==='none') return ''; const cls=c.level==='required'||c.level==='partial'?'ci-input':c.level==='optional'?'ci-input-opt':'ci-input-unk'; return `<button type="button" class="ci ${cls}" data-in="${g.game_id}" aria-label="${esc(c.title)} — show controller requirements">${INPUT_ICON[c.level]}</button>`; }
function factoryVisible(g){ const c=installClass(g); return !!g.builtin_code && (c==='builtin' || ((c==='revision'||c==='review') && decisionOf(g)==='factory')); }
function needsHardware(g){ const sc=g.input_standard_controller_playable; return g.input_level==='required' || (sc!=='true'&&sc!=='partial'&&g.input_level!=='none'); }
function visibilityText(g){ const c=installClass(g); if(c==='new') return sel.has(g.game_id)?'Selected: will be installed and visible in the intended TV collection.':'Not selected: not installed.'; return sel.has(g.game_id)?'Selected: visible in the intended TV collection; uses the existing factory installation.':'Not selected: hidden from the intended TV collection; original factory game preserved (console-side enforcement pending).'; }
function ciBtn(g){ const c=compatInfo(g); if(c.level==='none') return ''; return `<button type="button" class="ci ci-${c.level}" data-ci="${g.game_id}" aria-label="${esc(c.title)} — show details">${CI_ICON[c.level]}</button>`; }
function toast(m){ const t=$('toast'); t.textContent=m; t.classList.add('show'); clearTimeout(t._h); t._h=setTimeout(()=>t.classList.remove('show'),3500); }
function fill(id, vals){ const s=$(id); vals.forEach(v=>{const o=document.createElement('option');o.value=v;o.textContent=v;s.appendChild(o);}); }
fill('fYear',[...new Set(DATA.games.map(g=>g.year).filter(Boolean))].sort());
fill('fPub',[...new Set(DATA.games.map(g=>g.publisher).filter(Boolean))].sort());
fill('fGenre',[...new Set(DATA.games.flatMap(g=>(g.genre||'').split(',').map(x=>x.trim()).filter(Boolean)))].sort());
const letters=[...new Set(games.map(g=>{const c=(g.display_title||'')[0].toUpperCase();return /[A-Z]/.test(c)?c:'#'}))].sort();
$('letters').innerHTML='<span class="lbl">Jump</span>'+letters.map(l=>`<a data-l="${l}" href="#" role="button" title="jump to ${l}">${l}</a>`).join('');
function stickyH(){ const h=$('stickyHead'); return getComputedStyle(h).position==='sticky'?h.offsetHeight:$('toolbar').offsetHeight; }
function jumpTo(letter){ if(sortK!=='display_title'){ sortK='display_title'; sortDir=1; render(); toast('Sorted by title so the letter jump lands correctly'); } const r=document.querySelector(`#${view==='list'?'tb':'gridView'} [data-l="${letter}"]`); if(!r){ toast(`No titles starting with "${letter}" in the current view`); return false; } const y=r.getBoundingClientRect().top+scrollY-stickyH()-(view==='list'?44:10); scrollTo({top:Math.max(0,y)}); return true; }
$('letters').onclick=e=>{ const a=e.target.closest('a[data-l]'); if(a){ e.preventDefault(); jumpTo(a.dataset.l); } };
const mibToMb=x=>Number(x)*1048576/1e6; const fmtMb=x=>mibToMb(x).toLocaleString('en-US',{maximumFractionDigits:1})+' MB';
// ---- NES Classic storage: source-aware. 'reference-estimate' = a documented stock-console model (never a visitor's measurement);
//      'measured' = a device-specific read-only measurement with its time (not produced by this version); 'unknown' = no usable figure. ----
let st=DATA.storage||{}; let capMiB=0;
function storageModel(){ const m=st.model||(st.conservative_usable_mib?'reference-estimate':'unknown'); return m; }
function storageNote(){ const m=storageModel(); return m==='measured'?`Measured game space on your console: ~${fmtMb(st.conservative_usable_mib)} (${st.measured_at||''}).`:m==='unknown'?'Free space on your console is unknown; only the size of your selection is shown.':`Reference estimate for a stock 2018 NES Classic: ~${fmtMb(st.conservative_usable_mib)} of game space. Not a measurement of your console; Hakchi2 CE shows the real figure before a sync.`; }
function storageTrayLine(bytes,mib,over){ const m=storageModel(); if(m==='unknown') return `<div class="small" style="margin-top:4px">~${fmt(bytes||0)} to install (factory games count as 0). ${esc(storageNote())}</div>`;
 const word=m==='measured'?'measured':'estimated'; return `<div style="display:flex;align-items:center;gap:10px;margin-top:4px"><span class="meter" style="flex:none;width:140px;height:8px;border-radius:999px;background:rgba(255,255,255,.08);overflow:hidden;border:1px solid var(--line2);display:inline-block"><i style="display:block;height:100%;width:${(capMiB?Math.min(100,mib/capMiB*100):0).toFixed(1)}%;background:${over?'var(--red)':'linear-gradient(90deg,var(--pipe),var(--gold))'}"></i></span><span>~${fmt(bytes||0)} to install of ~${fmtMb(capMiB)} ${word} game space${over?` <b style="color:var(--bad)">— exceeds the ${word} space</b>`:''} (factory games count as 0)</span></div><div class="small">${esc(storageNote())}</div>`; }
function renderStorage(){ st=DATA.storage||{}; const m=storageModel(); capMiB=(m==='unknown')?0:(st.conservative_usable_mib||0); const el=$('storage'); if(!el) return;
 if(m==='measured'){ el.innerHTML=`<span class="meter" id="meter"><i></i></span><span><b>NES Classic</b> · measured game space ~<b>${fmtMb(st.conservative_usable_mib)}</b> <span class="mut">(${esc(st.measured_at||'')})</span> · <span id="storageUse"></span></span>`; }
 else if(m==='unknown'){ el.innerHTML=`<span><b>NES Classic</b> · current free space unknown · <span id="storageUse"></span></span>`; }
 else { el.innerHTML=`<span class="meter" id="meter"><i></i></span><span><b>NES Classic</b> · estimated game space ~<b>${fmtMb(st.conservative_usable_mib)}</b> · <span id="storageUse"></span></span>`; }
 el.title=m==='measured'?'Measured on your console (read-only), see details':m==='unknown'?'No console measurement and no usable estimate: only your collection size is shown':'A reference estimate for a stock 2018 NES Classic Edition, not a measurement of your console. Click for the assumptions.'; }
function storageDetails(){ const m=storageModel(); const o=st.overhead_estimates_mib||{}; const rows=m==='unknown'?'':`<table class="pv"><tr><th>Figure</th><th>Value</th></tr><tr><td>Data partition (reference model)</td><td>${fmtMb(st.measured_partition_mib)}</td></tr><tr><td>Free before modification</td><td>${fmtMb(st.measured_free_mib_before_mod)}</td></tr><tr><td>Root-reserved blocks</td><td>${fmtMb(st.root_reserved_mib)}</td></tr><tr><td>Hakchi install allowance</td><td>${fmtMb(o.hakchi_install||0)}</td></tr><tr><td>RetroArch + cores allowance</td><td>${fmtMb(o.retroarch_plus_cores||0)}</td></tr><tr><td>Saves / save-states reserve</td><td>${fmtMb(o.saves_and_states_reserve||0)}</td></tr><tr><td>Filesystem slack</td><td>${o.filesystem_slack_pct||0}%</td></tr><tr><td><b>Estimated game space</b></td><td><b>${fmtMb(st.conservative_usable_mib)}</b></td></tr></table>`;
 const src=m==='measured'?`<p>Measured on your console by a read-only method at <b>${esc(st.measured_at||'')}</b> (${esc(st.measured_source||'')}).</p>`:m==='unknown'?'<p>No console measurement is available and no reference figure applies, so only the size of your selected collection is shown.</p>':'<p>This is a <b>reference estimate</b> derived from the documented storage layout of a stock 2018 NES Classic Edition (CLV-101), not a measurement of your console. Actual free space differs with the console modifications, modules, saves and previously installed games. Hakchi2 CE shows the real figure before a sync; this Studio never reads the console in this version.</p>';
 openColl('NES Classic storage','how the estimate is made', `<div class="msgbox">${src}</div>${rows}<p class="small" style="margin-top:8px">Figures are decimal megabytes (1 MB = 1,000,000 bytes), converted from the binary units the console reports. Your collection size counts each distinct ROM once and adds 16 bytes per header; it does not include emulator modules.</p>`); }
renderStorage(); $('storage').onclick=storageDetails;
function verClass(g){ return g.selection_status==='review'?'review':(g.nointro_match?'verified':'cartdb'); }
function playersClass(g){ const p=g.players||''; if(!p) return '?'; if(p==='1') return '1'; if(p==='2') return g.simultaneous==='simultaneous'?'2sim':g.simultaneous==='alternating'?'2alt':'2'; return '3+'; }
function visible(){ const q=$('q').value.toLowerCase(); const y=$('fYear').value,p=$('fPub').value,l=$('fLic').value,ge=$('fGenre').value,pl=$('fPlayers').value,e=$('fEmu').value,v=$('fVer').value,bi=$('fBuilt').value,s=$('fSel').value,rkf=$('fRank').value,inf=$('fInput').value;
 return games.filter(g=>{
  if(q && !(g.display_title+' '+g.nointro_name).toLowerCase().includes(q)) return false;
  if(y && g.year!==y) return false; if(p && g.publisher!==p) return false; if(l && g.licensed!==l) return false;
  if(ge && !(g.genre||'').split(',').map(x=>x.trim()).includes(ge)) return false;
  if(pl){ const pc=playersClass(g); if(pl==='2'){ if(!(pc==='2'||pc==='2sim'||pc==='2alt')) return false; } else if(pc!==pl) return false; }
  if(e && g.recommended_emulator!==e) return false;
  if(v){ if(v==='verified'&&!g.nointro_match) return false; if(v==='cartdb'&&g.nointro_match) return false; if(v==='xval'&&g.header_validation_short!=='cross-validated') return false; if(v==='single'&&g.header_validation_short!=='single-source') return false; if(v==='review'&&g.selection_status!=='review') return false; }
  const bs=g.builtin_status||'NOT BUILT-IN';
  if(bi==='hideexact'&&bs==='BUILT-IN: EXACT MATCH') return false; if(bi==='exact'&&bs!=='BUILT-IN: EXACT MATCH') return false; if(bi==='other'&&!(bs.startsWith('BUILT-IN')&&bs!=='BUILT-IN: EXACT MATCH')) return false; if(bi==='not'&&bs!=='NOT BUILT-IN') return false;
  if(s==='1'&&!sel.has(g.game_id)) return false; if(s==='0'&&sel.has(g.game_id)) return false; if(s==='fav'&&!fav.has(g.game_id)) return false;
  if(inf){ const L=g.input_level||'unknown', sc=g.input_standard_controller_playable; if(inf==='standard'&&!(sc==='true'||sc==='partial')) return false; if(inf==='required'&&g.input_accessory_requirement!=='required') return false; if(inf==='optional'&&!(g.input_accessory_requirement==='optional'||g.input_accessory_requirement==='mode-specific')) return false; if(inf==='lightgun'&&!(g.input_modes||[]).includes('light-gun')) return false; if(inf==='unknown'&&L!=='unknown') return false; }
  if(rkf){ const r=rankOf(g); const cons=r&&r.consensus||0, cnt=r&&r.count||0; if(rkf==='top10'&&!(cons&&cons<=10)) return false; if(rkf==='top25'&&!(cons&&cons<=25)) return false; if(rkf==='top50'&&!(cons&&cons<=50)) return false; if(rkf==='top100'&&!(cons&&cons<=100)) return false; if(rkf==='any'&&cnt<1) return false; if(rkf==='two'&&cnt<2) return false; if(rkf==='unranked'&&cnt>0) return false; }
  return true; }); }
// user-facing sizes are decimal (1 KB = 1,000 B; 1 MB = 1,000,000 B; 1 GB = 1e9 B) computed from exact byte counts; internal byte and *_mib fields are untouched
function fmt(n){ n=Number(n)||0; let v,u,d; if(n>=1e9){v=n/1e9;u='GB';d=2;} else if(n>=1e6){v=n/1e6;u='MB';d=2;} else {v=n/1e3;u='KB';d=1;} return v.toLocaleString('en-US',{minimumFractionDigits:0,maximumFractionDigits:d})+' '+u; }
function emuTag(g){ return g.recommended_emulator==='kachikachi'?`<span class="tag k">kachikachi</span>`:g.recommended_emulator==='RetroArch'?`<span class="tag r">RetroArch ${esc(g.core||'')}</span>`:'<span class="tag u">unknown</span>'; }
function idTag(g){ return g.nointro_match?'<span class="tag k" title="payload CRC32 found in the No-Intro database">identity: verified</span>':'<span class="tag r" title="matched only in a cartridge database">identity: cart-DB</span>'; }
function hdrTag(g){ const h=g.header_validation_short; return h==='cross-validated'?'<span class="tag k" title="NesCartDB and Mesen/NES 2.0 DB agree">header: 2 sources</span>':h==='single-source'?'<span class="tag r" title="only one database has this cartridge">header: 1 source</span>':h==='conflict'?'<span class="tag u">header: conflict</span>':'<span class="tag u">header: none</span>'; }
function compTag(g){ return `<span class="tag ${g.compat_confidence==='medium'?'g':'r'}" title="${esc(g.compat_evidence)}">confidence: ${esc(g.compat_confidence)} (documentation only)</span>`; }   // details panel only
function builtTag(g){ const b=g.builtin_status||'NOT BUILT-IN'; if(b==='NOT BUILT-IN') return ''; return `<span class="tag b" title="${esc(g.builtin_code||'')} ${esc(g.builtin_title||'')}">${esc(b)}</span>`; }
function artEl(g, cls){ if(g.art) return `<img class="${cls}" loading="lazy" src="${esc(g.art)}" alt="" data-id="${g.game_id}">`; return `<div class="ph ${cls}" data-id="${g.game_id}"><span>NES</span></div>`; }
function warnings(g){ const w=[]; const c=installClass(g); if(c==='builtin') w.push('already installed on the console — no ROM will be prepared'); if(c==='revision') w.push(decisionOf(g)==='alternate'?`catalog version chosen instead of factory ${g.builtin_factory_version||''} — needs approval at import`:`console has ${g.builtin_factory_version||'a different revision'} — factory version kept`); if(c==='review') w.push(decisionOf(g)==='alternate'?'catalog release chosen over the factory title — needs approval at import':`same title as factory ${g.builtin_factory_version||g.builtin_title} but a different release — review required`); const ii=inputInfo(g); if(ii.level==='required') w.push(`needs ${g.input_special_accessory} — NES Classic support ${g.input_nes_classic_support}`); else if(ii.level==='unknown') w.push('input requirements unknown'); const ci=compatInfo(g); if(ci.level==='alt') w.push('needs RetroArch'); if(ci.level==='uncertain') w.push('compatibility uncertain'); if(ci.level==='blocker') w.push('emulator not determined'); if(g.ambiguity) w.push('review: '+g.ambiguity); return w; }
function notesCell(g){ const w=warnings(g); const alt=g.group_size>1?`alternates: ${esc(g.alternates)}`:''; return [w.map(esc).join(' · '), alt].filter(Boolean).join(' · '); }
function starBtn(g){ return `<button type="button" class="star ${fav.has(g.game_id)?'on':''}" data-fav="${g.game_id}" title="favorite / shortlist" aria-label="favorite" aria-pressed="${fav.has(g.game_id)}">★</button>`; }
function detBtn(g){ return `<button type="button" class="det" data-det="${g.game_id}" title="open details">details</button>`; }
function rankKey(g,k){ const r=rankOf(g); if(!r) return null; if(k==='rank:consensus') return r.consensus||null; return (r.sources||{})[k.slice(5)]||null; }
function render(){ const vis=visible(); vis.sort((a,b)=>{ if(sortK.startsWith('rank:')){ const x=rankKey(a,sortK), y=rankKey(b,sortK); if(x==null&&y==null) return a.display_title.localeCompare(b.display_title); if(x==null) return 1; if(y==null) return -1; return (x-y)*sortDir; } let x=a[sortK],y=b[sortK]; if(sortK==='sel'){x=sel.has(a.game_id)?1:0;y=sel.has(b.game_id)?1:0;} if(typeof x==='string'){x=x.toLowerCase();y=(y||'').toLowerCase();} return (x>y?1:x<y?-1:0)*sortDir; });
 if(!$('filters').classList.contains('open')){ $('sortSel').value=sortK; $('sortDir').value=((sortK==='sel')?sortDir>0:sortDir<0)?'desc':'asc'; }
 let last=''; const anchor=g=>{const L=(g.display_title||'')[0].toUpperCase(); const l=/[A-Z]/.test(L)?L:'#'; const a=l!==last?` data-l="${l}"`:''; last=l; return a;};
 if(view==='list'){ $('listView').hidden=false; $('gridView').hidden=true; last='';
  $('tb').innerHTML=vis.map(g=>`<tr class="pick ${sel.has(g.game_id)?'sel ':''}${g.selection_status==='review'?'review':''}" data-g="${g.game_id}" tabindex="0" aria-selected="${sel.has(g.game_id)}"${anchor(g)}><td><input type="checkbox" data-id="${g.game_id}" ${sel.has(g.game_id)?'checked':''} aria-label="select"></td><td>${artEl(g,'thumb')}</td><td><span class="ttl">${esc(g.display_title)}</span>${window.verChip?verChip(g):''}${window.srcBadge?srcBadge(g):''}${detBtn(g)}<div class="small">${esc(g.rev||'')} ${esc(g.regions||'')} · ${esc(g.nointro_name)}</div></td><td>${rankCell(g)}</td><td>${esc(g.year||'')}</td><td>${esc(g.publisher||'')}</td><td class="small">${esc(g.genre||'unknown')}</td><td class="small">${esc(g.players||'?')}${g.simultaneous?' '+esc(g.simultaneous):''}</td><td>${instTag(g)||'<span class="small">new</span>'}${g.ambiguity?'<br><span class="tag u" title="'+esc(g.ambiguity)+'">review</span>':''}</td><td class="mono">${g.mapper??''}</td><td>${idTag(g)}<br>${hdrTag(g)}</td><td style="white-space:nowrap">${emuTag(g)}${ciBtn(g)}${inBtn(g)}</td><td class="mono" style="white-space:nowrap">${fmt(g.payload_size+16)}</td><td>${starBtn(g)}</td></tr>`).join('');
 } else { $('listView').hidden=true; $('gridView').hidden=false; last='';
  $('gridView').innerHTML=vis.map(g=>`<div class="card ${sel.has(g.game_id)?'sel ':''}${g.selection_status==='review'?'review':''}" data-g="${g.game_id}" tabindex="0" aria-selected="${sel.has(g.game_id)}"${anchor(g)}><div class="cover"><span class="selbadge">✓ Selected</span>${rankBadge(g)}${artEl(g,'')}</div><div class="body"><div class="t">${esc(g.display_title)}${window.srcBadge?srcBadge(g):''}</div>${window.verChip&&TITLES[g.title_id]&&TITLES[g.title_id].versions.length>1?`<div style="margin:2px 0 4px">${verChip(g)}</div>`:''}<div class="m">${esc(g.year||'')}${g.publisher?' · '+esc(g.publisher):''}${g.genre?' · '+esc(g.genre.split(',')[0]):''}</div><div>${g.licensed==='unlicensed'?'<span class="tag r">unlicensed</span>':''}${instTag(g)}${g.selection_status==='review'?'<span class="tag u">review</span>':''}${g.recommended_emulator==='RetroArch'?'<span class="tag r">RetroArch</span>':''}${ciBtn(g)}${inBtn(g)}</div><div class="ctl"><label><input type="checkbox" data-id="${g.game_id}" ${sel.has(g.game_id)?'checked':''}> Select</label><span>${detBtn(g)}${starBtn(g)}</span></div></div></div>`).join('');
 }
 const chosen=window.selectedGames?selectedGames():games.filter(g=>sel.has(g.game_id)); const toInstall=chosen.filter(installRequired); const bytes=toInstall.reduce((a,g)=>a+g.payload_size+16,0); const mib=bytes/1048576; const over=capMiB&&mib>capMiB;
 const selB=[...sel].filter(id=>{ const g=byId[id]; return g&&g.builtin_status&&g.builtin_status.startsWith('BUILT-IN'); }).length;
 $('stats').innerHTML=`${vis.length} shown · <b>${sel.size} selected</b>${selB?` <span class="mut">(${selB} built-in + ${sel.size-selB} added)</span>`:''} · <span class="mut">${window.libCountText?libCountText():games.length+' games'}</span> · ${toInstall.length} to install · ~${fmt(bytes)}${over?` <span class="over">(exceeds the ${storageModel()==='measured'?'measured':'estimated'} space)</span>`:''} · ${fav.size} ★`; $('trayBadge').textContent=sel.size; $('trayBadge').classList.toggle('zero',!sel.size);
 $('heroSel').textContent=sel.size; $('heroFav').textContent=fav.size;
 const m=$('meter'); if(m){ m.classList.toggle('over',!!over); m.firstElementChild.style.width=(capMiB?Math.min(100,mib/capMiB*100):0).toFixed(1)+'%'; }
 $('storageUse').textContent=capMiB?`to install ~${fmt(bytes)} (${(mib/capMiB*100).toFixed(1)}%)`:'';
 $('trayCount').textContent=`${sel.size} game(s)`; renderTray(bytes, mib, over); renderChips(); if(window.hkLabelUpdate) hkLabelUpdate(); }
function renderTray(bytes, mib, over){ const list=(window.selectedGames?selectedGames():games.filter(g=>sel.has(g.game_id))).sort((a,b)=>a.display_title.localeCompare(b.display_title));
 const cls=c=>list.filter(g=>installClass(g)===c); const nInst=list.filter(installRequired).length, nB=cls('builtin').length, nRev=cls('revision').filter(g=>!installRequired(g)).length, nRevAlt=cls('revision').filter(installRequired).length, nRv=cls('review').filter(g=>!installRequired(g)).length, nRvAlt=cls('review').filter(installRequired).length, nr=list.filter(g=>installRequired(g)&&g.recommended_emulator==='RetroArch').length;
 $('traySub').innerHTML=list.length?`<div class="small"><b style="color:#fff;font-size:13px">${list.length} game${list.length===1?'':'s'} in your collection</b> · <b style="color:var(--info)">${nB+nRev+nRv} already installed</b> · <b style="color:var(--ok)">${nInst} to add</b>${nRev?` · ${nRev} factory revision kept`:''}${nRv?` · ${nRv} need review`:''}${nRevAlt+nRvAlt?` · ${nRevAlt+nRvAlt} alternate${nRevAlt+nRvAlt===1?'':'s'} pending approval`:''}${nr?` · ${nr} need RetroArch`:''}</div>${(()=>{ const hw=list.filter(needsHardware); const hidden=FACTORY_CODES.filter(cd=>!list.some(g=>g.builtin_code===cd&&factoryVisible(g))).length; return `<div class="policy"><b>Desired TV collection: selected games only.</b> Only selected games will appear in your intended TV collection — ${hidden} of ${FACTORY_CODES.length} factory games would be hidden (never deleted).<span class="pend">Unselected factory games are intended to be hidden during deployment. Console-side enforcement has not yet been configured.</span></div>${hw.length?`<div class="hwwarn">⚠ ${hw.length} selected game${hw.length===1?'':'s'} require${hw.length===1?'s':''} special input hardware that has not been verified with this console: ${esc(hw.map(g=>g.display_title).join(', '))}.</div>`:''}`; })()}${storageTrayLine(bytes,mib,over)}`:`<div class="small">${esc(storageNote())}</div>`;
 const decSel=g=>{ const c=installClass(g); if(c!=='revision'&&c!=='review') return ''; const d=decisionOf(g); return `<select class="dec" data-dec="${g.game_id}" aria-label="revision decision"><option value="factory" ${d==='factory'?'selected':''}>Keep factory version (default)</option><option value="alternate" ${d==='alternate'?'selected':''}>Prepare this catalog version (needs approval)</option></select>`; };
 $('trayList').innerHTML=list.map(g=>`<li>${g.art?`<img loading="lazy" src="${esc(g.art)}" alt="" data-id="${g.game_id}">`:'<div class="ph" style="width:36px;height:51px;padding:0;font-size:7px"><span style="border:none">NES</span></div>'}<div style="min-width:0;flex:1"><b class="tl" data-id="${g.game_id}">${esc(g.display_title)}</b>${window.verChip?verChip(g):''}${window.srcBadge?srcBadge(g):''}<div class="small">${esc(g.year||'')}${g.publisher?' · '+esc(g.publisher):''} · ${installRequired(g)?fmt(g.payload_size+16):'0 KB to install'}${rankOf(g)&&rankOf(g).consensus?` · rank #${rankOf(g).consensus}`:''}</div><div class="small">${instTag(g,true)} ${emuTag(g)}${ciBtn(g)}${inBtn(g)}</div>${decSel(g)}${warnings(g).length?`<div class="warnline">${esc(warnings(g).join(' · '))}</div>`:''}</div><span class="x" data-rm="${g.game_id}" title="remove from selection" role="button" tabindex="0">✕</span></li>`).join('')||`<li class="empty"><div class="scene"><svg viewBox="0 0 120 150" xmlns="http://www.w3.org/2000/svg"><defs><linearGradient id="gc" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#bdbdbd"/><stop offset="1" stop-color="#8f8f8f"/></linearGradient></defs><rect x="4" y="4" width="112" height="142" rx="6" fill="url(#gc)"/><rect x="4" y="4" width="112" height="14" rx="6" fill="#d8d8d8"/><g fill="#6f6f6f"><rect x="12" y="22" width="4" height="60"/><rect x="20" y="22" width="4" height="60"/><rect x="28" y="22" width="4" height="60"/><rect x="36" y="22" width="4" height="60"/><rect x="44" y="22" width="4" height="60"/><rect x="52" y="22" width="4" height="60"/><rect x="60" y="22" width="4" height="60"/><rect x="68" y="22" width="4" height="60"/><rect x="76" y="22" width="4" height="60"/><rect x="84" y="22" width="4" height="60"/><rect x="92" y="22" width="4" height="60"/><rect x="100" y="22" width="4" height="60"/></g><rect x="14" y="88" width="92" height="48" rx="3" fill="#1c1c22"/><rect x="14" y="88" width="92" height="6" fill="#e4000f"/><text x="60" y="113" text-anchor="middle" font-family="Press Start 2P,monospace" font-size="7" fill="#f3f1ec">YOUR</text><text x="60" y="126" text-anchor="middle" font-family="Press Start 2P,monospace" font-size="7" fill="#ff3b45">COLLECTION</text></svg><div class="cast" aria-hidden="true">${EMPTY_CAST}</div><div class="ground"></div></div><h3>Your collection starts here</h3><p>Choose the games you want available on your NES Classic. Only selected games are meant to appear on the TV — including which of the 30 factory games stay visible.</p><span class="hint"><svg width="14" height="14" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.6"><path d="M5 2v9l2.3-2.2L9 13l1.8-.8-1.7-4.1H12z"/></svg>Click any game card or row to add it</span></li>`; }
function toggleSel(id,on){ const g=byId[id]; if(on){ sel.add(id); const c=installClass(g); if(c==='builtin') toast(`"${g.display_title}" is already installed on the console — added to your collection, no ROM will be prepared.`); else if(c==='revision') toast(`"${g.display_title}": the console has ${g.builtin_factory_version||'another revision'} — the factory version is kept unless you choose the catalog version in the tray.`); else if(c==='review') toast(`"${g.display_title}": same title as a factory game but a different release — review required before any install.`); } else sel.delete(id); persist(); render(); }
document.body.addEventListener('change',e=>{ if(e.target.matches('select.dec')){ decisions[e.target.dataset.dec]=e.target.value; persist(); render(); } });
document.body.addEventListener('change',e=>{ if(e.target.matches('input[type=checkbox][data-id]')) toggleSel(e.target.dataset.id, e.target.checked); });
document.body.addEventListener('click',e=>{ const t=e.target;
  if(t.dataset.fav){ fav.has(t.dataset.fav)?fav.delete(t.dataset.fav):fav.add(t.dataset.fav); persist(); render(); return; }
  if(t.dataset.rm){ sel.delete(t.dataset.rm); persist(); render(); return; }
  if(t.dataset.det){ openDetails(t.dataset.det); return; }
  if(t.closest('#tray')||t.closest('#details')){ if(t.classList.contains('tl')||((t.tagName==='IMG'||t.classList.contains('ph'))&&t.dataset.id)) openDetails(t.dataset.id); return; }
  if(t.closest('input,label,a,button,select,.star,.det')) return;           // their own handlers
  const row=t.closest('[data-g]'); if(row){ if(getSelection&&String(getSelection()).length) return; hv.style.display='none'; toggleSel(row.dataset.g, !sel.has(row.dataset.g)); } });
document.body.addEventListener('keydown',e=>{ const t=e.target; if((e.key==='Enter'||e.key===' ')&&t.matches&&t.matches('[data-g]')){ e.preventDefault(); toggleSel(t.dataset.g, !sel.has(t.dataset.g)); const n=document.querySelector(`[data-g="${t.dataset.g}"]`); if(n) n.focus(); } else if((e.key==='Enter'||e.key===' ')&&t.dataset&&t.dataset.rm){ e.preventDefault(); sel.delete(t.dataset.rm); persist(); render(); } });
function openDetails(id){ const g=byId[id]; if(!g) return; const w=warnings(g);
 $('detailsBox').innerHTML=`<span class="close" id="dClose" role="button" aria-label="close">✕</span><div class="side">${g.art?`<img src="${esc(g.art)}" alt="">`:'<div class="ph" style="aspect-ratio:3/4.3"><span>NES</span></div>'}<div class="small">${g.art_source==='libretro-thumbnails'?'Box art: libretro-thumbnails (community scans), cached locally':g.art_source==='oem-backup'?'Box art: Nintendo cover image from the console backup':'No reliable box art found'}</div><label><input type="checkbox" data-id="${g.game_id}" ${sel.has(g.game_id)?'checked':''}> Selected for my collection</label><div>${starBtn(g)} <span class="small">favorite / shortlist</span></div><div>${instTag(g,true)}</div><div>${emuTag(g)}${g.licensed==='unlicensed'?'<span class="tag r">unlicensed</span>':''}</div>${(installClass(g)==='revision'||installClass(g)==='review')?`<div class="small">Revision decision<br><select class="dec" data-dec="${g.game_id}" style="margin-top:4px;font-size:12px"><option value="factory" ${decisionOf(g)==='factory'?'selected':''}>Keep factory version (default)</option><option value="alternate" ${decisionOf(g)==='alternate'?'selected':''}>Prepare this catalog version (needs approval)</option></select></div>`:''}</div>
 <div><h2>${esc(g.display_title)}</h2><div class="small mono">${esc(g.nointro_name)}</div><div style="margin-top:8px">${idTag(g)}${hdrTag(g)}</div>${(()=>{ const r=rankOf(g); if(!SRC.length) return ''; return `<div class="sec"><h3>Rankings</h3>${r&&r.consensus?`<div class="ranks"><div class="rb ${r.consensus<=10?'top':''}"><b>#${r.consensus}</b><span>consensus of ${r.count} verified list${r.count===1?'':'s'}</span></div>${SRC.map(sc=>`<div class="rb"><b>${(r.sources||{})[sc.key]?'#'+(r.sources||{})[sc.key]:'—'}</b><span><a href="${esc(sc.url)}" rel="noopener">${esc(sc.name)}</a>${sc.date?' · '+esc(sc.date):''}</span></div>`).join('')}</div>`:`<div class="small">Not ranked on any of the ${SRC.length} verified Top 100 lists (${SRC.map(sc=>esc(sc.name)).join(', ')}). Absence from a list is not a judgement of quality.</div>`}<div class="small">${esc(DATA.ranking_method||'')}</div></div>`; })()}
 <div class="sec"><h3>Overview</h3><dl><dt>Year</dt><dd>${esc(g.year||'unknown')}${g.year_source?` <span class="small">(${esc(g.year_source)})</span>`:''}</dd><dt>Publisher</dt><dd>${esc(g.publisher||'unknown')}</dd><dt>Genre</dt><dd>${esc(g.genre||'unknown')}${g.genre_source?` <span class="small">(${esc(g.genre_source)})</span>`:''}</dd><dt>Players</dt><dd>${esc(g.players||'unknown')}${g.simultaneous?' — '+esc(g.simultaneous):(g.players==='2'?' — simultaneous/alternating not recorded':'')}</dd><dt>Description</dt><dd class="small">not available (no reliable local source)</dd><dt>Licensed</dt><dd>${esc(g.licensed)}</dd><dt>Built-in</dt><dd>${esc(g.builtin_status)}${g.builtin_code?` (${esc(g.builtin_code)} "${esc(g.builtin_title)}")`:''}${g.builtin_factory_version?`<br><span class="small">factory version: ${esc(g.builtin_factory_version)}</span>`:''}</dd><dt>Installation</dt><dd>${INST_LABEL[installClass(g)][1]}${installClass(g)==='revision'||installClass(g)==='review'?` — decision: ${decisionOf(g)}`:''}</dd><dt>TV collection</dt><dd>${esc(visibilityText(g))}</dd></dl></div>
 <div class="sec"><h3>Controllers &amp; accessories</h3>${(()=>{ const c=inputInfo(g); return `<dl><dt>Status</dt><dd>${c.level==='none'?'Standard NES Classic controller(s); no special accessory recorded.':`<span class="ci ${c.level==='required'||c.level==='partial'?'ci-input':c.level==='optional'?'ci-input-opt':'ci-input-unk'}" style="cursor:default">${INPUT_ICON[c.level]}</span> ${esc(c.title)}`}${(c.lines||[]).map(l=>'<br>'+esc(l)).join('')}</dd><dt>Primary input</dt><dd>${esc(g.input_primary||'standard-controller')}${g.input_modes&&g.input_modes.length>1?' (modes: '+esc(g.input_modes.join(', '))+')':''}</dd><dt>Controllers needed</dt><dd>${esc(String(g.input_min_controllers||1))}${g.players?` (supports ${esc(g.players)} player${g.players==='1'?'':'s'})`:''}</dd><dt>Accessory</dt><dd>${esc(g.input_special_accessory||'none')}${g.input_accessory_requirement&&g.input_accessory_requirement!=='none'?' — '+esc(g.input_accessory_requirement):''}</dd><dt>Standard controller</dt><dd>${esc(g.input_standard_controller_playable||'unknown')}</dd><dt>NES Classic support</dt><dd>${esc(g.input_nes_classic_support||'unknown')}</dd><dt>Evidence</dt><dd class="small">${(g.input_evidence||[]).map(e=>e.url?`<a href="${esc(e.url)}" rel="noopener">${esc(e.source)}${e.device?': '+esc(e.device):''}</a>`:esc(e.source)).join('<br>')||'none'}${g.input_notes?'<br>'+esc(g.input_notes):''}</dd></dl>`; })()}</div>
 <div class="sec"><h3>Cartridge &amp; verification</h3><dl><dt>ROM identity</dt><dd>${esc(g.identity_verification)}</dd><dt>Header</dt><dd>${esc(g.header_validation||'none')} — ${esc(g.header_status||'')}</dd><dt>Cartridge</dt><dd>mapper ${g.mapper??'?'}${g.submapper?'.'+g.submapper:''}, PRG ${g.prg?fmt(g.prg):'?'}, CHR ${g.chr!=null?fmt(g.chr):'?'}, ${g.four_screen?'four-screen':(g.vertical?'vertical':'horizontal')} mirroring${g.battery?', battery':''}; board ${esc(g.cart_board||g.mesen_board||'unknown')}</dd></dl></div>
 <div class="sec"><h3>Console &amp; compatibility</h3>${(()=>{ const c=compatInfo(g); return `<dl><dt>Emulator</dt><dd>${emuTag(g)}<div class="small mono">${esc(g.launch_command||'')}</div></dd><dt>Status</dt><dd>${c.level==='none'?'No specific compatibility note for this title.':`<span class="ci ci-${c.level}" style="cursor:default">${CI_ICON[c.level]}</span> ${esc(c.title)}`}${c.lines.map(l=>'<br>'+esc(l)).join('')}</dd><dt>Confidence</dt><dd>${compTag(g)}</dd><dt>Evidence</dt><dd>${esc(g.compat_evidence)}</dd><dt>Stored note</dt><dd class="small">${esc(g.compat_notes||'none')}</dd></dl>`; })()}${w.length?`<div class="warnline" style="margin-top:6px">⚠ ${esc(w.join(' · '))}</div>`:''}</div>
 <div class="sec"><h3>Source &amp; alternates</h3><dl><dt>Alternates</dt><dd class="small">${esc(g.alternates||'none')}</dd><dt>Source</dt><dd class="small mono">${g.archive?esc(g.archive.split('/').pop().split('\\').pop()):'source archive not published'} · payload SHA-256 ${esc(g.payload_sha256)}</dd></dl></div></div>`;
 $('details').classList.add('open'); $('dClose').onclick=()=>$('details').classList.remove('open'); }
$('details').onclick=e=>{ if(e.target.id==='details') $('details').classList.remove('open'); };
// Hover preview: any thumbnail (list, grid, tray) shows the cover at a standardized 360 px width near the pointer.
const hv=$('hover'), hvImg=$('hoverImg'), hvCap=$('hoverCap');
function placeHover(x,y){ const pad=16, w=hv.offsetWidth||380, h=hv.offsetHeight||540; let L=x+pad, T=y+pad; if(L+w>innerWidth) L=x-w-pad; if(T+h>innerHeight) T=Math.max(4,innerHeight-h-4); hv.style.left=L+'px'; hv.style.top=T+'px'; }
document.body.addEventListener('mouseover',e=>{ const t=e.target; if(t.tagName==='IMG'&&t.dataset.id&&!t.closest('#details')){ const g=byId[t.dataset.id]; if(!g||!g.art) return; hvImg.src=g.art; hvCap.textContent=g.display_title+(g.year?' ('+g.year+')':''); hv.style.display='block'; placeHover(e.clientX,e.clientY); } });
document.body.addEventListener('mousemove',e=>{ if(hv.style.display==='block') placeHover(e.clientX,e.clientY); });
document.body.addEventListener('mouseout',e=>{ const t=e.target; if(t.tagName==='IMG'&&t.dataset.id) hv.style.display='none'; });
document.addEventListener('scroll',()=>{ hv.style.display='none'; },true);
document.addEventListener('keydown',e=>{ if(e.key==='Escape'){ $('details').classList.remove('open'); $('tray').classList.remove('open'); $('filters').classList.remove('open'); $('hkpanel').classList.remove('open'); $('selectMenu').hidden=true; } });
// ---- filters modal, select menu, active-filter chips ----
const FILTERS=[['fYear','Year'],['fPub','Publisher'],['fLic','Licensed'],['fGenre','Genre'],['fPlayers','Players'],['fEmu','Emulator'],['fVer','Identity / header'],['fBuilt','Built-in'],['fSel','Selection'],['fInput','Controllers'],['fRank','Ranking']]; const FDEF={};
if(!SRC.length){ $('fRankLabel').hidden=true; $('thRank').hidden=true; }
function activeFilters(){ return FILTERS.filter(([id])=>$(id).value!==(FDEF[id]||'')); }
function sortLabel(){ const o=[...$('sortSel').options].find(o=>o.value===sortK); const base=o?o.textContent:sortK; const desc=(sortK==='sel')?sortDir>0:sortDir<0; return base+(desc?' (descending)':''); }
function renderChips(){ const a=activeFilters(); const sortActive=!(sortK==='display_title'&&sortDir===1); const n=a.length+(sortActive?1:0)+(extOn?1:0); $('filtersBadge').hidden=!n; $('filtersBadge').textContent=n; $('chips').hidden=!n;
 $('chips').innerHTML=n?'<span class="lbl">Active</span>'+(extOn?`<span class="chip ext"><b>Extended Library</b><span class="rm" data-cf="ext" title="back to the curated library" role="button" tabindex="0">✕</span></span>`:'')+(sortActive?`<span class="chip"><b>Sort:</b> ${esc(sortLabel())}<span class="rm" data-cf="sort" title="back to Title A–Z" role="button" tabindex="0">✕</span></span>`:'')+a.map(([id,l])=>`<span class="chip"><b>${l}:</b> ${esc($(id).selectedOptions[0].textContent)}<span class="rm" data-cf="${id}" title="remove filter" role="button" tabindex="0">✕</span></span>`).join('')+`<button class="btn sm ghost" id="chipsClear">Clear all</button>`:'';
 if($('filters').classList.contains('open')) updateFilterCount(); }
// ---- Filter & Sort modal: changes are staged (live count), Apply commits, Cancel/Escape/backdrop discard ----
let FSNAP=null;
function fsnapshot(){ return {f:Object.fromEntries(FILTERS.map(([id])=>[id,$(id).value])), sort:$('sortSel').value, dir:$('sortDir').value, ext:$('extToggle').checked}; }
function frestore(sn){ if(!sn) return; FILTERS.forEach(([id])=>{ $(id).value=sn.f[id]; }); $('sortSel').value=sn.sort; $('sortDir').value=sn.dir; $('extToggle').checked=sn.ext; }
function updateFilterCount(){ const scope=$('extToggle').checked?games.length:games.filter(g=>!g.ext).length; $('filtersCount').textContent=`${visible().length} of ${scope} games match`; }
function fapply(){ const ext=$('extToggle').checked; if(ext!==extOn) setExt(ext); const v=$('sortSel').value; sortK=v; sortDir=$('sortDir').value==='desc'?-1:1; if(v==='sel'&&$('sortDir').value==='asc') sortDir=-1; render(); }
$('filtersBtn').onclick=()=>{ FSNAP=fsnapshot(); $('sortSel').value=sortK; $('sortDir').value=(sortDir<0&&sortK!=='sel')||(sortK==='sel'&&sortDir>0)?'desc':'asc'; $('extToggle').checked=extOn; updateFilterCount(); $('filters').classList.add('open'); $('sortSel').focus(); };
$('filtersDone').onclick=()=>{ fapply(); FSNAP=null; $('filters').classList.remove('open'); };
function fcancel(){ frestore(FSNAP); FSNAP=null; $('filters').classList.remove('open'); render(); }
$('filtersCancel').onclick=fcancel;
$('filtersReset').onclick=()=>{ FILTERS.forEach(([id])=>$(id).value=FDEF[id]||''); $('sortSel').value='display_title'; $('sortDir').value='asc'; $('extToggle').checked=false; updateFilterCount(); };
$('filters').onclick=e=>{ if(e.target.id==='filters') fcancel(); };
document.addEventListener('keydown',e=>{ if(e.key==='Escape'&&$('filters').classList.contains('open')){ e.preventDefault(); fcancel(); } });
$('chips').addEventListener('click',e=>{ const t=e.target; if(t.dataset.cf==='sort'){ sortK='display_title'; sortDir=1; render(); } else if(t.dataset.cf==='ext'){ setExt(false); } else if(t.dataset.cf){ $(t.dataset.cf).value=FDEF[t.dataset.cf]||''; render(); } else if(t.id==='chipsClear'){ FILTERS.forEach(([id])=>$(id).value=FDEF[id]||''); sortK='display_title'; sortDir=1; render(); } });
$('selectBtn').onclick=e=>{ e.stopPropagation(); $('selectMenu').hidden=!$('selectMenu').hidden; };
document.addEventListener('click',e=>{ if(!e.target.closest('.menuwrap')) $('selectMenu').hidden=true; });
$('selectMenu').addEventListener('click',()=>{ $('selectMenu').hidden=true; });

$('q').oninput=render; ['fYear','fPub','fLic','fGenre','fPlayers','fEmu','fVer','fBuilt','fSel','fRank','fInput'].forEach(id=>$(id).oninput=updateFilterCount);
// ---- sort control (works for list and grid; column headers stay clickable) ----
(function(){ const opts=[['display_title','Title'],['year','Year'],['publisher','Publisher'],['sel','Selected first'],['payload_size','Size']]; if(SRC.length){ opts.push(['rank:consensus','Rank: consensus']); SRC.forEach(sc=>opts.push(['rank:'+sc.key,'Rank: '+sc.name])); }
 $('sortSel').innerHTML=opts.map(([v,l])=>`<option value="${v}">${esc(l)}</option>`).join(''); $('sortSel').onchange=$('sortDir').onchange=updateFilterCount; })();
// ---- compatibility tooltip: hover, keyboard focus, click/tap; never toggles selection ----
const tip=$('tip'); let tipFor=null, tipPinned=false;
function showTip(btn){ const g=byId[btn.dataset.ci||btn.dataset.in]; if(!g) return; const isIn=!!btn.dataset.in; const c=isIn?inputInfo(g):compatInfo(g); tip.className='open '+(isIn?(c.level==='required'||c.level==='partial'?'input':c.level==='optional'?'input-opt':'input-unk'):c.level); tip.innerHTML=`<h4><i>${isIn?INPUT_ICON[c.level]:CI_ICON[c.level]}</i>${esc(c.title)}</h4>${c.lines.map(l=>`<p>${esc(l)}</p>`).join('')}<div class="ev">${esc(c.evidence)}</div>`; tipFor=btn; btn.setAttribute('aria-describedby','tip'); tip.style.visibility='hidden'; tip.style.display='block';
 const r=btn.getBoundingClientRect(), w=tip.offsetWidth, h=tip.offsetHeight; let L=Math.min(Math.max(8,r.left+r.width/2-w/2),innerWidth-w-8), T=r.bottom+8; if(T+h>innerHeight-8) T=Math.max(8,r.top-h-8); tip.style.left=L+'px'; tip.style.top=T+'px'; tip.style.visibility=''; }
function hideTip(force){ if(tipPinned&&!force) return; tip.className=''; tip.style.display=''; if(tipFor) tipFor.removeAttribute('aria-describedby'); tipFor=null; tipPinned=false; }
document.body.addEventListener('mouseover',e=>{ const b=e.target.closest&&e.target.closest('button.ci'); if(b&&(b.dataset.ci||b.dataset.in)&&b!==tipFor&&!tipPinned) showTip(b); });
document.body.addEventListener('mouseout',e=>{ const b=e.target.closest&&e.target.closest('button.ci'); if(b&&!tipPinned&&!(e.relatedTarget&&e.relatedTarget.closest&&e.relatedTarget.closest('#tip'))) hideTip(); });
document.body.addEventListener('focusin',e=>{ const b=e.target.closest&&e.target.closest('button.ci'); if(b&&(b.dataset.ci||b.dataset.in)){ tipPinned=false; showTip(b); } });
document.body.addEventListener('focusout',e=>{ const b=e.target.closest&&e.target.closest('button.ci'); if(b&&!tipPinned) hideTip(); });
document.body.addEventListener('click',e=>{ const b=e.target.closest&&e.target.closest('button.ci'); if(b&&(b.dataset.ci||b.dataset.in)){ e.stopPropagation(); if(tipFor===b&&tipPinned){ hideTip(true); } else { showTip(b); tipPinned=true; } } else if(!e.target.closest('#tip')) hideTip(true); }, true);
document.addEventListener('scroll',()=>hideTip(true),true); document.addEventListener('keydown',e=>{ if(e.key==='Escape') hideTip(true); });
$('vList').onclick=()=>{view='list';$('vList').classList.add('active');$('vGrid').classList.remove('active');persist();render();};
$('vGrid').onclick=()=>{view='grid';$('vGrid').classList.add('active');$('vList').classList.remove('active');persist();render();};
if(view==='grid'){$('vGrid').classList.add('active');$('vList').classList.remove('active');}
$('selVis').onclick=()=>{ const v=visible(); v.forEach(g=>sel.add(g.game_id)); const b=v.filter(g=>(g.builtin_status||'').startsWith('BUILT-IN')).length; if(b) toast(`${b} of the selected games are already on the console (kept in selection).`); persist(); render(); };
$('unselVis').onclick=()=>{ visible().forEach(g=>sel.delete(g.game_id)); persist(); render(); };
$('selKachi').onclick=()=>{ visible().filter(g=>g.recommended_emulator==='kachikachi'&&g.nointro_match&&g.selection_status!=='review').forEach(g=>sel.add(g.game_id)); persist(); render(); };
$('clear').onclick=()=>{ sel.clear(); persist(); render(); };
$('trayBtn').onclick=()=>$('tray').classList.toggle('open'); $('trayClose').onclick=()=>$('tray').classList.remove('open');
document.querySelectorAll('th[data-k]').forEach(th=>th.onclick=()=>{ if(sortK===th.dataset.k) sortDir=-sortDir; else {sortK=th.dataset.k; sortDir=1;} render(); });
// ---- ONE authoritative manifest builder (schema 3). Export JSON and Open in Hakchi both call it. Collection-defining
// fields are serialized deterministically (games sorted by game_id, codes sorted); only `exported` varies between runs.
function buildManifest(){ const chosen=games.filter(g=>sel.has(g.game_id)).sort((a,b)=>a.game_id<b.game_id?-1:1); const visibleCodes=new Set(chosen.filter(factoryVisible).map(g=>g.builtin_code)); const codes=[...FACTORY_CODES].sort(); const hiddenFactory=codes.filter(cd=>!visibleCodes.has(cd)).map(cd=>({code:cd,title:FACTORY_TITLES[cd]||cd})); const hw=chosen.filter(needsHardware);
 return {schema:'nes-classic-selection/3',collection_mode:'exclusive',factory_game_policy:'hide_unselected',exported:new Date().toISOString(),catalog_generated:DATA.generated,factory_inventory:{file:FACTORY.file,sha256:FACTORY.sha256,count:FACTORY.count,codes:codes},count:chosen.length,
  hidden_factory_games:hiddenFactory, deployment:{required_capabilities:['hide_unselected_factory_games','install_additional_games','preserve_factory_roms','reversible'],console_enforcement:'pending',note:'Unselected factory games are intended to be hidden during deployment. Console-side enforcement has not yet been configured.'},
  summary:{collection:chosen.length,already_installed:chosen.filter(g=>installClass(g)==='builtin').length,factory_default:chosen.filter(g=>installClass(g)==='revision'&&!installRequired(g)).length,review:chosen.filter(g=>installClass(g)==='review'&&!installRequired(g)).length,alternates_pending_approval:chosen.filter(g=>installClass(g)!=='new'&&installRequired(g)).length,to_install:chosen.filter(installRequired).length,install_bytes:chosen.filter(installRequired).reduce((a,g)=>a+g.payload_size+16,0),factory_visible:visibleCodes.size,factory_hidden:hiddenFactory.length,input_hardware_warnings:hw.length},
  games:chosen.map(g=>({game_id:g.game_id,display_title:g.display_title,nointro_name:g.nointro_name,payload_sha256:g.payload_sha256,recommended_emulator:g.recommended_emulator,builtin_status:g.builtin_status,builtin_code:g.builtin_code||null,builtin_factory_version:g.builtin_factory_version||null,comparison_status:comparisonStatus(g),install_required:installRequired(g),revision_decision:decisionOf(g),consensus_rank:(rankOf(g)||{}).consensus||null,input_requirements:{primary:g.input_primary,modes:g.input_modes,min_controllers:g.input_min_controllers,special_accessory:g.input_special_accessory||null,requirement:g.input_accessory_requirement,standard_controller_playable:g.input_standard_controller_playable,nes_classic_support:g.input_nes_classic_support,level:g.input_level}}))}; }
function confirmExportWarnings(){ const chosen=window.selectedGames?selectedGames():games.filter(g=>sel.has(g.game_id)); const hw=chosen.filter(needsHardware);
 if(!chosen.length && !confirm('Your collection is EMPTY. An exclusive manifest with zero games would hide every factory game and leave the TV library empty. Continue anyway?')) return false;
 if(hw.length && !confirm(`${hw.length} selected game(s) require special input hardware that has not been verified with this console:\n\n${hw.map(g=>'• '+g.display_title+' — '+(g.input_special_accessory||'special input')).join('\n')}\n\nThey will stay in the manifest with their requirements recorded. Continue?`)) return false;
 return true; }
$('export').onclick=()=>{ if(!confirmExportWarnings()) return; const m=buildManifest();
 const b=new Blob([JSON.stringify(m,null,1)],{type:'application/json'}); const a=document.createElement('a'); a.href=URL.createObjectURL(b); a.download='selection.json'; a.click(); toast(`Exported ${m.count} game(s) to selection.json`); };
$('trayExport').onclick=()=>$('export').click(); $('trayHk').onclick=()=>$('hkBtn').click();
$('importBtn').onclick=()=>$('import').click();
$('import').onchange=e=>{ const f=e.target.files[0]; if(!f) return; f.text().then(t=>{ const m=JSON.parse(t); if(!['nes-classic-selection/1','nes-classic-selection/2','nes-classic-selection/3'].includes(m.schema)){ toast('Not a selection manifest (unknown schema)'); return; } let n=0; (m.games||[]).forEach(g=>{ if(byId[g.game_id]){ sel.add(g.game_id); n++; if(g.revision_decision==='alternate'||g.revision_decision==='factory') decisions[g.game_id]=g.revision_decision; } }); persist(); render(); toast(`Loaded ${n} game(s) from ${m.schema==='nes-classic-selection/3'?'exclusive ':m.schema==='nes-classic-selection/1'?'legacy ':''}manifest`); e.target.value=''; }); };
// ---- Open in Hakchi: talks only to the local companion that served this page (same origin; CSRF token embedded by the companion) ----
const COMPANION={ok:false,info:null,token:(document.querySelector('meta[name="nes-companion-token"]')||{}).content||'',served:location.protocol==='http:'&&/^(127\.0\.0\.1|localhost)$/.test(location.hostname)&&!!(document.querySelector('meta[name="nes-companion-token"]')||{}).content};
async function companionStatus(){ if(!COMPANION.served){ try{ await fetch('http://127.0.0.1:8765/api/ping',{mode:'no-cors',cache:'no-store'}); COMPANION.reachable=true; }catch(e){ COMPANION.reachable=false; } return; }
 try{ const r=await fetch('/api/status',{cache:'no-store'}); COMPANION.info=await r.json(); COMPANION.ok=!!COMPANION.info.ok; }catch(e){ COMPANION.ok=false; COMPANION.info=null; } }
function hkLabelUpdate(){ const n=sel.size; const b=$('hkBtn'); if(!COMPANION.served){ b.classList.add('off'); $('hkLabel').textContent='Open in Hakchi (set up)'; b.title='The local companion is not serving this page; click for setup instructions'; return; } b.classList.toggle('off',!COMPANION.ok||!!(COMPANION.info&&COMPANION.info.install&&COMPANION.info.install.state!=='ok')); $('hkLabel').textContent=n?`Open ${n} game${n===1?'':'s'} in Hakchi`:'Open in Hakchi'; b.title=COMPANION.ok?'Validate the collection, save the manifest and stage it into Hakchi for review':'Companion status unavailable — click for details'; }
function stTag(cls,txt){ return `<span class="st ${cls}">${esc(txt)}</span>`; }
const STATUS_CLS={'already installed':'info','factory game retained':'info','factory game hidden':'dim','ready to prepare':'ok','prepared for Hakchi':'ok','reused existing ROM':'ok','emulator prerequisite':'warn','revision approval required':'warn','controller warning':'warn','needs review':'warn','missing ROM payload':'bad','blocked':'bad','unchecked (no longer selected)':'dim'};
function renderPreview(pv, mode){ const S=pv.summary||{}; const sb=(v,l,c)=>`<div class="sb ${c||''}"><b>${v}</b><span>${l}</span></div>`;
 let h=`<div class="sum">${sb(S.collection,'selected games')}${sb(S.factory_visible,'factory games visible','info')}${sb(S.factory_hidden,'factory games hidden','')}${sb(S.to_prepare_total,'additional games to install','ok')}${sb(S.retroarch,'need RetroArch',S.retroarch?'warn':'')}${sb(S.controller_warnings,'controller warnings',S.controller_warnings?'warn':'')}${sb(S.revision_pending,'revision decisions pending',S.revision_pending?'warn':'')}${sb(S.install_size||fmt(S.install_bytes||0),'estimated install size')}${(()=>{ const G=pv.games||[]; const miss=G.filter(g=>(g.statuses||[]).includes('missing ROM payload')).length, rev=G.filter(g=>(g.statuses||[]).some(x=>x==='needs review'||x==='revision approval required')).length, avail=Math.max(0,(S.to_prepare_total||0)-miss); return sb(avail,'game files on this computer',avail?'ok':'')+sb(miss,'game files missing',miss?'bad':'')+sb(rev,'need your review',rev?'warn':''); })()}</div>`;
 if(pv.problems&&pv.problems.length) h+=`<div class="msg bad"><b>Cannot continue:</b><ol>${pv.problems.map(x=>`<li>${esc(x)}</li>`).join('')}</ol></div>`;
 if(pv.warnings&&pv.warnings.length) h+=`<div class="msg warn">${pv.warnings.map(x=>`<div>⚠ ${esc(x)}</div>`).join('')}</div>`;
 h+=`<div class="pvwrap"><table class="pv"><thead><tr><th>Game</th><th>Kind</th><th>Status</th><th>Emulator</th><th>Detail</th></tr></thead><tbody>${(pv.games||[]).map(g=>`<tr><td><b>${esc(g.title)}</b><div class="small">${esc(g.nointro_name||g.code||'')}</div></td><td class="small">${esc(g.kind)}</td><td>${(g.statuses||[]).map(st=>stTag(STATUS_CLS[st]||'dim',st)).join(' ')}</td><td class="small">${esc(g.emulator||'')}</td><td class="small">${esc(g.detail||'')}${(g.statuses||[]).includes('missing ROM payload')&&g.game_id&&typeof COMPANION!=='undefined'&&COMPANION.ok?`<div><button class="btn sm" data-locate="${esc(g.game_id)}">Locate Game File…</button></div>`:''}</td></tr>`).join('')}</tbody></table></div>`;
 return h; }
function hkEnvLine(env){ const i=env.install||{}, d=env.driver||{}; const drv=d.installed?`installed (${esc(d.inf||'')}${d.version?', WinUSB '+esc(d.version):''})`:d.state==='installer-unavailable'?'<b>not installed</b> (installer unavailable: set up Hakchi first)':'<b>not installed</b>';
 return `<div class="msg envl"><b>Environment</b> · Hakchi2 CE: ${i.state==='ok'?`<b>${esc(i.version||'3.9.3')}</b> verified (SHA-256 match)`:i.state==='different-version'?`<b>different build</b> (${esc(i.version||'unknown version')})`:'<b>not installed</b>'} · Console USB driver (${esc(d.hwid||'')}): ${drv}${d.simulated?' <span class="small">(simulated for rehearsal)</span>':''} · Hakchi running: ${env.hakchi_running?'<b>yes</b>':'no'} · Console attached: ${env.console_connected?'<b>yes</b>':'no'}${(!d.installed&&d.state==='missing')?` <button class="btn secondary" id="hkDrvBtn" style="margin-left:8px">Install console driver…</button>`:''}</div>`; }
function hkWireDriver(){ const b=$('hkDrvBtn'); if(!b) return; b.onclick=async()=>{ const NL=String.fromCharCode(10); if(!confirm(['Install the console USB driver now?','','This runs the installer bundled with Hakchi (driver/classic_driver.exe, a libwdi WinUSB driver for the console in FEL mode). Windows will show an administrator (UAC) prompt that you must accept.','','Nothing is sent to the console and it does not need to be connected.'].join(NL))) return;
  b.disabled=true; b.textContent='Installing… answer the Windows prompt'; const r=await hkPost('/api/install-driver',{}); const res=r.result||{}; const sp=document.createElement('span'); sp.className='small'; sp.innerHTML=(r.ok?'✓ ':'✗ ')+esc(res.message||r.error||''); b.replaceWith(sp); await companionStatus(); hkLabelUpdate(); }; }
function hkInstallPanel(inst, env){
 if(inst.state==='different-version'){ hkOpenPanel('Open in Hakchi — unexpected Hakchi build','', `<div class="msg bad">The file at the configured path is not the Hakchi2 CE 3.9.3 build this project is pinned to.<br><code>${esc(inst.path||'')}</code><br>File version: <b>${esc(inst.version||'unknown')}</b> · SHA-256 <code>${esc((inst.sha256||'').slice(0,16))}…</code> (expected <code>${esc((inst.expected_sha256||'').slice(0,16))}…</code>)</div>
  <div class="msg">The project pins Hakchi2 CE <b>3.9.3</b> (decision D-01): every verification in this workspace was done against that exact build, so there is no "minimum version", only the pinned one. The companion never replaces, updates or launches any other build. To use the pinned build, move the folder <code>${esc(inst.root||'')}</code> aside (keep it), then click Open in Hakchi again and choose <b>Set up Hakchi2 CE 3.9.3</b>. To adopt the build that is there instead, record that decision in <code>docs/decisions.md</code> and update the recorded hash in <code>tools/companion/hakchi_bridge.py</code>.</div>`+hkEnvLine(env)); hkWireDriver(); return; }
 hkOpenPanel('Open in Hakchi — Hakchi not installed','', `<div class="msg bad">Hakchi2 CE 3.9.3 was not found at the configured path.<br><code>${esc(inst.path||'(none)')}</code>${inst.root_nonempty?'<br><b>The folder exists but is not empty</b> and has no hakchi.exe: move it aside first (nothing is deleted automatically).':''}</div>
  <div class="msg">The companion can set it up from the <b>verified release package</b> <code>${esc(inst.package_name||'')}</code> (SHA-256 <code>${esc((inst.package_sha256||'').slice(0,16))}…</code>, ${fmt(inst.package_size||0)}): ${inst.package_local_verified?'a verified copy is already in <code>packages/</code>, so nothing is downloaded.':inst.package_local?'the copy in <code>packages/</code> does not match the recorded hash, so it is set aside and the official release asset is downloaded again and verified before use.':`it is downloaded from the official TeamShinkansen release (<code>${esc(inst.package_url||'')}</code>) into <code>packages/</code> and verified by size and SHA-256 before use.`} The portable build is then extracted to <code>${esc(inst.root||'')}</code>: no installer runs, nothing is registered in Windows, and no configuration is written (Hakchi creates its own on first run).</div>
  ${hkEnvLine(env)}
  <div class="acts"><span class="small">Nothing here touches the console. Gate G1 remains not approved.</span><span class="sp"></span><button class="btn secondary" id="hkCancel">Cancel</button><button class="btn hk" id="hkInstall" ${inst.root_nonempty?'disabled':''}>Set up Hakchi2 CE 3.9.3</button></div>`);
 hkWireDriver(); $('hkCancel').onclick=()=>$('hkpanel').classList.remove('open');
 $('hkInstall').onclick=async()=>{ $('hkInstall').disabled=true; $('hkInstall').textContent='Setting up… (download and extraction can take a minute)'; const r=await hkPost('/api/install-hakchi',{}); const res=r.result||{};
  $('hkTitle').textContent=r.ok?'Open in Hakchi — Hakchi set up':'Open in Hakchi — setup not done';
  $('hkBody').innerHTML=`<div class="msg ${r.ok?'ok':'bad'}"><b>${r.ok?'Done.':'Nothing was changed.'}</b> ${esc(res.message||r.error||'')}${res.package?`<br>Package: <code>${esc(res.package.path||'')}</code> (${esc(res.package.source||'')}, size and SHA-256 verified)`:''}${res.version?`<br>hakchi.exe file version <b>${esc(res.version)}</b>; ${res.entries||0} entries extracted.`:''}</div>`+(res.env?hkEnvLine(res.env):'')+`<div class="acts"><span class="sp"></span><button class="btn secondary" id="hkDone">Close</button>${r.ok?'<button class="btn hk" id="hkAgain">Continue to Open in Hakchi</button>':''}</div>`;
  hkWireDriver(); $('hkDone').onclick=()=>$('hkpanel').classList.remove('open'); if($('hkAgain')) $('hkAgain').onclick=()=>{ $('hkpanel').classList.remove('open'); companionStatus().then(()=>{ hkLabelUpdate(); $('hkBtn').click(); }); }; }; }
function hkOpenPanel(title, sub, body){ $('hkTitle').textContent=title; $('hkSub').textContent=sub||''; $('hkBody').innerHTML=body; $('hkpanel').classList.add('open'); }
$('hkClose').onclick=()=>$('hkpanel').classList.remove('open'); $('hkpanel').onclick=e=>{ if(e.target.id==='hkpanel') $('hkpanel').classList.remove('open'); };
async function hkPost(path, body){ const r=await fetch(path,{method:'POST',headers:{'Content-Type':'application/json','X-NES-Token':COMPANION.token},body:JSON.stringify(body),cache:'no-store'}); const j=await r.json().catch(()=>({ok:false,error:'bad response'})); if(!r.ok&&!j.error) j.error='HTTP '+r.status; return j; }
$('hkBtn').onclick=async()=>{
 if(!COMPANION.served){ await companionStatus(); hkOpenPanel('Open in Hakchi — setup needed','', `<div class="msg">This page was opened as a local file, so the browser cannot hand your collection to Hakchi from here. The catalog talks to Hakchi through a small <b>local companion</b> that runs only on this PC (no network, no background service) and serves this same catalog from <code>http://127.0.0.1:8765/</code>.</div>
  <ol><li>Double-click <code>Start-Catalog.cmd</code> in the project folder (<code>tools\\companion\\</code>). It starts the companion and opens the catalog in Edge.</li><li>Use that catalog window from now on; the Open in Hakchi button works there.</li><li>Your selection lives in this browser profile per page origin. To carry the current selection over, click <b>Export JSON</b> here, then <b>Select ▾ Load selection manifest…</b> in the companion catalog.</li></ol>
  <div class="msg ${COMPANION.reachable?'ok':''}">${COMPANION.reachable?'The companion appears to be running now: <a href="http://127.0.0.1:8765/nes-catalog.html">open the companion catalog</a>.':'The companion is not running at the moment.'}</div>
  <div class="small" style="margin-top:8px">Nothing is sent to the console by any of this. Gate G1 remains not approved.</div>`); return; }
 await companionStatus(); hkLabelUpdate();
 if(!COMPANION.ok){ hkOpenPanel('Open in Hakchi — companion unavailable','', `<div class="msg bad">The companion that served this page is not responding. Restart it with <code>Start-Catalog.cmd</code> and reload this page.</div>`); return; }
 const info=COMPANION.info; const inst=info.install||{state:info.hakchi_found?'ok':'missing',path:info.hakchi_path,root:info.hakchi_root};
 if(inst.state!=='ok'){ hkInstallPanel(inst, info); return; }
 if(!confirmExportWarnings()) return;
 const m=buildManifest();
 hkOpenPanel('Open in Hakchi — preview',`${m.count} selected`, `<div class="msg">Validating the collection against the factory inventory and ROM store…</div>`);
 const pv=await hkPost('/api/preview',{manifest:m});
 if(!pv.ok){ $('hkBody').innerHTML=`<div class="msg bad">${esc(pv.error||'validation failed')}</div>`+(pv.preview?renderPreview(pv.preview):''); return; }
 const can=pv.preview.can_apply; const env=pv.env||{};
 $('hkSub').textContent=`${m.count} selected · manifest saved as ${esc(pv.manifest_file||'')}`;
 $('hkBody').innerHTML=renderPreview(pv.preview)+hkEnvLine(env)+`<div class="msg ${can?'':'bad'}">${env.rehearsal?'<b>Rehearsal mode:</b> this companion is pointed at a disposable copy of the Hakchi tree.<br>':''}${env.hakchi_running&&!env.rehearsal?'<b>Hakchi is currently open.</b> Close it first — the companion never edits Hakchi\'s configuration while Hakchi is running and never closes it for you.<br>':''}${env.console_connected&&!env.rehearsal?'<b>The console is connected over USB.</b> Disconnect it first — the companion refuses to open Hakchi with the console attached so that nothing can be written to it.<br>':''}${can?`Applying will: back up Hakchi\'s PC-side configuration (SHA-256 recorded), set the NES original-game selection to exactly the ${pv.preview.summary.factory_visible} selected factory games, prepare ${pv.preview.summary.to_prepare} additional ROM file(s) into Hakchi\'s games library (reusing ${pv.preview.summary.reused} already prepared), uncheck entries no longer selected, verify the result, then open Hakchi for your review. <b>Nothing is sent to the console.</b>`:'Resolve the problems above, then try again.'}</div>
  <div class="acts"><span class="small">Manifest: <code>${esc(pv.manifest_file||'')}</code></span><span class="sp"></span><button class="btn secondary" id="hkCancel">Cancel</button><button class="btn hk" id="hkApply" ${can&&(env.rehearsal||(!env.hakchi_running&&!env.console_connected))?'':'disabled'}>Stage in Hakchi and open it</button></div>`;
 $('hkCancel').onclick=()=>$('hkpanel').classList.remove('open'); hkWireDriver();
 $('hkApply').onclick=async()=>{ $('hkApply').disabled=true; $('hkApply').textContent='Staging…'; const res=await hkPost('/api/apply',{manifest:m,manifest_file:pv.manifest_file});
  const r=res.result||{}; const ver=r.verification||{}; const okAll=res.ok&&ver.match===true;
  $('hkTitle').textContent=res.ok?'Open in Hakchi — staged for review':'Open in Hakchi — not applied'; $('hkSub').textContent=r.run_id?`run ${r.run_id}`:'';
  $('hkBody').innerHTML=(r.preview?renderPreview(r.preview):'')+`<div class="msg ${okAll?'ok':'bad'}"><b>${esc(res.ok?(okAll?'Staged and verified.':'Staged, but verification did not fully match.'):'Nothing was changed.')}</b> ${esc(res.error||r.message||'')}<br>
   Factory games enabled in Hakchi: <b>${ver.factory_enabled_count??'?'}</b> (manifest ${ver.factory_expected_count??'?'}) · additional games checked: <b>${ver.custom_checked_count??'?'}</b> (manifest ${ver.custom_expected_count??'?'}) · prepared now: ${r.prepared??0} · reused: ${r.reused??0} · unchecked (no longer selected): ${r.unchecked??0}<br>
   PC-side configuration match: <b>${ver.match===true?'yes':ver.match===false?'NO':'unverified'}</b>${ver.mismatches&&ver.mismatches.length?' — '+esc(ver.mismatches.join('; ')):''}<br>
   Hakchi launched: <b>${r.launched?'yes (PID '+r.hakchi_pid+')':'no'}</b> · Hakchi window game list: <b>${esc(r.ui_verification||'unverified')}</b><br>
   Rollback snapshot: <code>${esc(r.backup_dir||'none')}</code>${r.rolled_back?'<br><b>The previous configuration was restored.</b>':''}<br>
   <span class="small">Console deployment: NOT performed and NOT authorized (Gate G1). Review the collection in Hakchi; do not click Synchronize.</span></div>
   <div class="acts"><span class="sp"></span><button class="btn secondary" id="hkDone">Close</button></div>`; $('hkDone').onclick=()=>$('hkpanel').classList.remove('open'); };
};
$('gen').textContent=`generated ${DATA.generated} · ${DATA.games.length} curated games · Extended Library index ${DATA.lib_counts?DATA.lib_counts.titles+' titles / '+DATA.lib_counts.versions+' versions':''} · collection files: nes-classic-collection/4`;
$('heroGames').textContent=DATA.games.length;
// keep the sticky table header just below the sticky toolbar
const tb=$('stickyHead'); const setTb=()=>document.documentElement.style.setProperty('--tb-h',(getComputedStyle(tb).position==='sticky'?tb.offsetHeight:$('toolbar').offsetHeight)+'px'); if(window.ResizeObserver) new ResizeObserver(setTb).observe(tb); setTb(); addEventListener('resize',setTb);
__STUDIO_JS__
render();
</script></body></html>"""

LEGAL_PRIVATE = TEMPLATE[TEMPLATE.index('<p class="legal">'):TEMPLATE.index("</p></div></footer>") + len("</p></div></footer>")]
LEGAL_PUBLIC = ('<p class="legal">' + DISCLAIMER + ' Console-side technology by <a href="https://github.com/TeamShinkansen/Hakchi2-CE" rel="noopener">Hakchi2 CE</a> (GPL v3, a separate download; this Studio prepares collections for it and does not bundle or reimplement it). '
    'The pixel artwork and interface icons are Nintendo\'s own renderings from the NES Classic Edition system software and documented community sprite rips (origins in the parade above), and the box art comes from community thumbnail collections; they are shown to identify and present the games, and all rights remain with their owners. No game files or console software are distributed with this website. '
    'Source code, releases and how to verify a download: <a href="https://github.com/Rance9/NES-Classic-Collection-Studio" rel="noopener">github.com/Rance9/NES-Classic-Collection-Studio</a>. Catalog <span id="gen"></span>.</p></div></footer>')

def sprite(folder: str, stem: str, scale: int, index: dict, cls: str = "", title: str = "") -> str:
    f = index.get((folder, stem + ".png"))
    if not f: return ""
    return f'<img class="px {cls}" src="brand/{folder}/{stem}.png" width="{f["w"]*scale}" height="{f["h"]*scale}" alt="" title="{title}" loading="lazy">'

def main():
    data = json.load(open(os.path.join(SRC, "catalog.json"), encoding="utf-8"))
    bp = os.path.join(SRC, "builtin.json")
    if os.path.exists(bp): data["builtin_total"] = len(json.load(open(bp, encoding="utf-8"))["games"])   # 30 factory titles from the verified backup
    rp = os.path.join(SRC, "rankings.json")
    if os.path.exists(rp):
        rk = json.load(open(rp, encoding="utf-8"))
        data["rankings"] = rk["games"]; data["ranking_sources"] = rk["sources"]; data["ranking_method"] = rk.get("method_summary", "")
    index = {}
    ip = os.path.join(BRAND, "index.json")
    if PUBLIC: assert os.path.exists(ip), "public site is missing brand/oem: run tools/publish/build_public_repo.py (it copies the approved artwork into the site)"
    if os.path.exists(ip):
        for f in json.load(open(ip, encoding="utf-8"))["files"]: index[("oem", f["file"])] = f
    web_prov = {}
    wp = os.path.join(WEB, "provenance.json")
    if os.path.exists(wp):
        for f in json.load(open(wp, encoding="utf-8")):
            if os.path.exists(os.path.join(WEB, f["slug"] + ".png")):
                index[("web", f["slug"] + ".png")] = {"w": f["width"], "h": f["height"]}; web_prov[f["slug"]] = f
    hero = "".join(sprite(d, st, sc, index, cls) for d, st, sc, cls in HERO)
    # ---- Studio partials + compact library index (titles + non-curated versions; curated rows already carry full data) ----
    sdir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "studio")
    part = lambda n: open(os.path.join(sdir, n), encoding="utf-8").read()
    libp = os.path.join(SRC, "library.json"); lib = json.load(open(libp, encoding="utf-8")) if os.path.exists(libp) else {"generated": "", "titles": [], "versions": [], "counts": {}}
    tkeep = ("id", "title", "names", "publisher", "licensed", "versions", "recommended", "identity", "year", "genre", "art", "builtin_code", "curated")
    vkeep = ("id", "sha256", "name", "title_id", "regions", "rev", "kind", "identity", "size", "emulator", "core", "input_level", "input_accessory", "input_support", "input_requirement", "input_modes", "builtin_code", "builtin_status", "year", "genre", "art", "header_ok", "licensed", "publisher")
    LIBC = {"generated": lib.get("generated", ""), "counts": lib.get("counts", {}), "titles": [{k: t[k] for k in tkeep if t.get(k) not in ("", None, [])} for t in lib["titles"]], "versions": [{k: v[k] for k in vkeep if v.get(k) not in ("", None, [], False)} for v in lib["versions"] if not v["curated"]]}
    for t in LIBC["titles"]: t.setdefault("curated", False)
    data["lib_counts"] = {"titles": len(lib["titles"]), "versions": len(lib["versions"]), "curated": sum(1 for t in lib["titles"] if t["curated"])}
    welcome_cast = "".join(sprite(d, st, sc, index) for d, st, sc in [("oem", "px-mario-walk-1", 2), ("oem", "px-link", 2), ("oem", "px-samus", 2), ("oem", "px-kirby", 2), ("oem", "px-pit", 2), ("web", "toad", 2)])
    g_start = "".join(sprite(d, st, sc, index) for d, st, sc in [("oem", "px-mario-walk-1", 2), ("oem", "px-question-block-0", 2), ("web", "goomba", 2)])
    studio_js = part("studio.js").replace("__LIB__", json.dumps(LIBC, separators=(",", ":")).replace("</", "<\\/")) + "\n" + part("connect.js")
    studio_html = part("studio.html").replace("__WELCOME_CAST__", welcome_cast).replace("__GUIDE_SECTIONS__", part("guide.html").replace("__G_START__", g_start))
    foot = "".join(sprite("oem", st, 2, index, "", f"{n} — {g}") for st, n, g in ROSTER)
    foot += "".join(sprite("web", st, 2, index, "", f"{n} — {web_prov.get(st, {}).get('game_or_origin', '')}") for st, n in WEB_ROSTER)
    empty = "".join(sprite(d, st, sc, index) for d, st, sc in EMPTY_CAST)
    howto = "".join(sprite(d, st, sc, index) for d, st, sc in HOWTO_CAST)
    filt = "".join(sprite(d, st, sc, index) for d, st, sc in FILTERS_CAST)
    html = (TEMPLATE.replace("__DATA__", json.dumps(data).replace("</", "<\\/")).replace("__HERO_CAST__", hero).replace("__FOOTER_CAST__", foot)
            .replace("__EMPTY_CAST__", empty).replace("__HOWTO_CAST__", howto).replace("__FILTERS_CAST__", filt)
            .replace("__STUDIO_CSS__", part("studio.css")).replace("__STUDIO_HTML__", studio_html).replace("__STUDIO_JS__", studio_js))
    if PUBLIC:
        html = (html.replace("<title>NES Classic Collection — Game Selection Catalog</title>", f"<title>{APP_NAME}</title><meta name=\"description\" content=\"{TAGLINE}\"><meta name=\"robots\" content=\"index,follow\"><meta property=\"og:title\" content=\"{APP_NAME}\"><meta property=\"og:description\" content=\"{TAGLINE}\">")
                .replace(LEGAL_PRIVATE, LEGAL_PUBLIC))
        # regression guard (owner directive 2026-10-10): the public page must carry the approved visuals — 27-sprite hero, wordmark, box art
        assert LEGAL_PUBLIC in html and hero.count("<img") == len(HERO) == 27 and "brand/oem/nes-wordmark.png" in html and sum(1 for g in data["games"] if g.get("art")) >= 745, "public build lost the approved visuals"
    out = os.path.join(OUT, "nes-catalog.html")
    open(out, "w", encoding="utf-8").write(html)
    print("wrote", out, "games:", len(data["games"]), "bytes:", len(html), "hero sprites:", hero.count("<img"), "footer sprites:", foot.count("<img"), "web sprites:", len(web_prov), "rankings:", len(data.get("rankings", {})))

if __name__ == "__main__":
    main()
