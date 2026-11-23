# Classic Cart Studio console library: the parts CCS Mod and CCS Nucleus share (each package carries its own byte-identical copy at
# /etc/ccs/<method>/lib.sh in hakchi's overlay). BusyBox ash, as hakchi 3.9.3's own scripts (its BusyBox 1.29). Sourced: it defines
# ccs_* names and the path variables below, and runs nothing. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
#
# CCS_ROOT is empty on a running console. The start stage (hakchi's preinit, before the console's system starts) passes hakchi's
# $mountpoint (/newroot); the tests pass a stand-in folder. Every path below starts with it.
#
# Files this library writes are written whole to a new name, synced and renamed over the old one (one rename: an interrupted update
# leaves the old file in force). Records that must not be half-read ("sealed": the registry, the ownership journals, the method in
# charge) end with a line end=<SHA-256 of every line before it>; a record whose seal does not match counts as absent.

ccs_paths(){
  R0="${CCS_ROOT:-}"
  HKR="$R0/var/lib/hakchi/rootfs"       # hakchi's overlay ("rootfs"), where hakchi packages install their files
  HKI="$R0/var/lib/hakchi"              # hakchi's own folder
  CCSD="$R0/var/lib/ccs"                # Classic Cart Studio's folder on the data storage (shared by both methods)
  OWNED="$CCSD/owned"                   # ownership journals: <method>.own (ccs-ownership/1)
  TMPC="$R0/tmp/ccs"                    # RAM: links, logs and markers of this power-on
}

ccs_log(){
  mkdir -p "$TMPC/logs" 2>/dev/null
  echo "$*" >> "$TMPC/logs/ccs-method.log" 2>/dev/null
  echo "$*" >&2
}

ccs_sha(){ sha256sum "$1" 2>/dev/null | cut -d' ' -f1; }
ccs_size(){ wc -c < "$1" 2>/dev/null | tr -d ' '; }

# a stable option or method id: lower-case letters, digits and "-", 1 to 32 characters, not starting with "-"
ccs_id_ok(){
  case "$1" in ""|-*|*[!a-z0-9-]*) return 1 ;; esac
  [ ${#1} -le 32 ]
}
# a console path this program records: absolute, plain names only (letters, digits . _ - /), no "..", at most 200 characters
ccs_path_ok(){
  case "$1" in /*) ;; *) return 1 ;; esac
  case "$1" in *[!A-Za-z0-9._/-]*|*/../*|*/..|*//*|*/) return 1 ;; esac
  [ ${#1} -le 200 ]
}
# a label shown on the TV: plain printable words, 1 to 60 characters
ccs_label_ok(){
  [ -n "$1" ] && [ ${#1} -le 60 ] && [ -z "$(printf '%s' "$1" | tr -d "A-Za-z0-9 .,'!&()+:-")" ]
}
ccs_hex64_ok(){ case "$1" in *[!0-9a-f]*) return 1 ;; esac; [ ${#1} -eq 64 ]; }
ccs_num_ok(){ case "$1" in ""|*[!0-9]*) return 1 ;; esac; [ ${#1} -le 12 ]; }

# ccs_put DEST < content: written to DEST.new.<pid>, synced, renamed over DEST
ccs_put(){
  local d="$1" t="$1.new.$$"
  mkdir -p "${d%/*}" || return 1
  cat > "$t" || { rm -f "$t"; return 1; }
  sync
  mv -f "$t" "$d" || { rm -f "$t"; return 1; }
  sync
}

# ccs_put_sealed DEST < body: the body (whole lines) and its seal line, written as ccs_put does
ccs_put_sealed(){
  local d="$1" t="$1.new.$$" s
  mkdir -p "${d%/*}" || return 1
  cat > "$t.body" || { rm -f "$t.body"; return 1; }
  s="$(sha256sum < "$t.body" | cut -d' ' -f1)"
  { cat "$t.body"; echo "end=$s"; } > "$t" || { rm -f "$t" "$t.body"; return 1; }
  rm -f "$t.body"
  sync
  mv -f "$t" "$d" || { rm -f "$t"; return 1; }
  sync
}

# ccs_sealed FILE SCHEMA: the file is a sealed record of that schema (its first line schema=..., its seal matches)
ccs_sealed(){
  local f="$1" e
  [ -f "$f" ] && [ ! -L "$f" ] || return 1
  [ "$(head -n 1 "$f")" = "schema=$2" ] || return 1
  e="$(tail -n 1 "$f")"
  case "$e" in end=*) ;; *) return 1 ;; esac
  e="${e#end=}"
  ccs_hex64_ok "$e" || return 1
  [ "$(sed '$d' "$f" | sha256sum | cut -d' ' -f1)" = "$e" ]
}

# ccs_get FILE KEY: the value of the first line KEY=... (the key compared exactly)
ccs_get(){ awk -v k="$2=" 'index($0, k) == 1 { print substr($0, length(k) + 1); exit }' "$1" 2>/dev/null; }

# ---------------------------------------------------------------- ownership journal (ccs-ownership/1)
# One journal per method, $OWNED/<method>.own:
#   schema=ccs-ownership/1
#   method=<mod|nucleus>
#   package=<the package's content id>
#   file=<sha256> <mode> <console path>   a program, setting or menu entry this method put there; removed only while unchanged
#   data=<sha256> <console path>          the owner's data this method keeps (an imported system image); removed only on request
#   dir=<console path>                    a folder this method made; removed only when empty
#   end=<seal>
# Something counts as this method's only while a valid journal names it AND its bytes still have the recorded SHA-256.
ccs_own_file(){ echo "$OWNED/$1.own"; }
ccs_own_valid(){ ccs_sealed "$(ccs_own_file "$1")" ccs-ownership/1 && [ "$(ccs_get "$(ccs_own_file "$1")" method)" = "$1" ]; }
ccs_own_entries(){   # the entry lines (file=, data=, dir=) of a valid journal; nothing when there is none
  ccs_own_valid "$1" || return 0
  grep -E '^(file|data|dir)=' "$(ccs_own_file "$1")"
}
ccs_own_path(){ local x="${1##* }"; echo "${x#dir=}"; }   # the console path an entry line names
# ccs_own_sha METHOD PATH: the SHA-256 the journal records for PATH (file= or data=), or nothing
ccs_own_sha(){
  ccs_own_entries "$1" | awk -v p="$2" '
    /^file=/ { split(substr($0, 6), a, " "); if (a[3] == p) { print a[1]; exit } }
    /^data=/ { split(substr($0, 6), a, " "); if (a[2] == p) { print a[1]; exit } }'
}
# ccs_own_mine METHOD PATH: PATH is named by the journal and unchanged
ccs_own_mine(){
  local s
  s="$(ccs_own_sha "$1" "$2")"
  [ -n "$s" ] && [ -f "$R0$2" ] && [ ! -L "$R0$2" ] && [ "$(ccs_sha "$R0$2")" = "$s" ]
}
# ccs_own_free METHOD PATH [NEWSHA]: PATH may be written by METHOD: nothing is there, or it is METHOD's own unchanged file, or it already
# holds exactly NEWSHA (writing it changes nothing). Anything else belongs to someone else, or was changed, and is never overwritten.
ccs_own_free(){
  [ -e "$R0$2" ] || [ -L "$R0$2" ] || return 0
  [ -f "$R0$2" ] && [ ! -L "$R0$2" ] || return 1
  ccs_own_mine "$1" "$2" && return 0
  [ -n "$3" ] && [ "$(ccs_sha "$R0$2")" = "$3" ]
}
# ccs_own_write METHOD PACKAGE < entries: the journal replaced by these entries (sorted, each line once)
ccs_own_write(){
  local m="$1" p="$2"
  { printf 'schema=ccs-ownership/1\nmethod=%s\npackage=%s\n' "$m" "$p"; grep . | sort -u; } | ccs_put_sealed "$(ccs_own_file "$m")"
}
ccs_own_others(){   # METHOD PATH: the journal's entries for every other path
  ccs_own_entries "$1" | awk -v p="$2" '{ n = split($0, a, " "); x = a[n]; sub(/^dir=/, "", x); if (x != p) print }'
}
# ccs_own_add METHOD KIND PATH [MODE]: (re)record PATH as it is now (KIND file, data or dir)
ccs_own_add(){
  local m="$1" k="$2" p="$3" mode="${4:-0644}" pkg
  ccs_path_ok "$p" || return 1
  pkg="$(ccs_own_valid "$m" && ccs_get "$(ccs_own_file "$m")" package)"
  case "$k" in
    file) [ -f "$R0$p" ] || return 1; { ccs_own_others "$m" "$p"; echo "file=$(ccs_sha "$R0$p") $mode $p"; } | ccs_own_write "$m" "${pkg:-none}" ;;
    data) [ -f "$R0$p" ] || return 1; { ccs_own_others "$m" "$p"; echo "data=$(ccs_sha "$R0$p") $p"; } | ccs_own_write "$m" "${pkg:-none}" ;;
    dir)  { ccs_own_others "$m" "$p"; echo "dir=$p"; } | ccs_own_write "$m" "${pkg:-none}" ;;
    *) return 1 ;;
  esac
}
ccs_own_forget(){   # METHOD PATH: no longer recorded
  local m="$1" p="$2" pkg
  ccs_own_valid "$m" || return 0
  pkg="$(ccs_get "$(ccs_own_file "$m")" package)"
  ccs_own_others "$m" "$p" | ccs_own_write "$m" "$pkg"
}
# ccs_own_remove METHOD keep|remove-data: every recorded file still unchanged is removed; a changed one is kept and reported; data is
# removed only with remove-data (and only while unchanged); recorded folders are removed when empty. Prints one line per path:
# "removed PATH", "kept-changed PATH", "kept-data PATH", "gone PATH", "kept-folder PATH/". The journal afterwards names only what was
# kept (and is gone when nothing was).
ccs_own_remove(){
  local m="$1" how="$2" line kind rest sha path pkg
  mkdir -p "$TMPC" || return 1
  ccs_own_valid "$m" || { echo "no-journal $m"; return 0; }
  pkg="$(ccs_get "$(ccs_own_file "$m")" package)"
  ccs_own_entries "$m" > "$TMPC/own.$$"
  : > "$TMPC/kept.$$"
  while read -r line; do
    kind="${line%%=*}"; rest="${line#*=}"
    case "$kind" in
      file|data)
        sha="${rest%% *}"; path="${rest##* }"
        if [ ! -e "$R0$path" ] && [ ! -L "$R0$path" ]; then echo "gone $path"
        elif [ "$kind" = data ] && [ "$how" != remove-data ]; then echo "kept-data $path"; echo "$line" >> "$TMPC/kept.$$"
        elif [ -f "$R0$path" ] && [ ! -L "$R0$path" ] && [ "$(ccs_sha "$R0$path")" = "$sha" ]; then rm -f "$R0$path" && echo "removed $path"
        else echo "kept-changed $path"; echo "$line" >> "$TMPC/kept.$$"
        fi ;;
    esac
  done < "$TMPC/own.$$"
  # folders, deepest first, only when empty (a folder still holding anything kept or someone else's stays)
  grep '^dir=' "$TMPC/own.$$" | sed 's/^dir=//' | sort -r > "$TMPC/dirs.$$"
  while read -r path; do
    if [ -d "$R0$path" ] && rmdir "$R0$path" 2>/dev/null; then echo "removed $path/"
    elif [ -d "$R0$path" ]; then echo "kept-folder $path/"; echo "dir=$path" >> "$TMPC/kept.$$"; fi
  done < "$TMPC/dirs.$$"
  if [ -s "$TMPC/kept.$$" ]; then ccs_own_write "$m" "$pkg" < "$TMPC/kept.$$"
  else rm -f "$(ccs_own_file "$m")"; sync; fi
  rm -f "$TMPC/own.$$" "$TMPC/kept.$$" "$TMPC/dirs.$$"
}

# ---------------------------------------------------------------- the package's own file list (etc/ccs/<method>/FILES, made by the builder)
# Lines "<sha256> <mode> <path relative to hakchi's overlay>": what the package copies into the overlay.
# ccs_pkg_preflight METHOD FILESLIST: every target may be written (ccs_own_free); prints each refusal; 1 when there is any
ccs_pkg_preflight(){
  local m="$1" sha mode rel bad=0
  while read -r sha mode rel; do
    [ -n "$rel" ] || continue
    ccs_path_ok "/var/lib/hakchi/rootfs/$rel" || { echo "refused not-a-plain-path $rel"; bad=1; continue; }
    ccs_own_free "$m" "/var/lib/hakchi/rootfs/$rel" "$sha" || { echo "refused belongs-to-someone-else /var/lib/hakchi/rootfs/$rel"; bad=1; }
  done < "$2"
  return $bad
}
ccs_pkg_path(){   # METHOD PATH: a path the method's package itself carries (not one made later: menu entries, the optional start script)
  case "$2" in */etc/ccs/"$1"/FILES) return 1 ;; esac   # the list itself is recorded on its own, after the others
  case "$2" in /var/lib/hakchi/rootfs/etc/ccs/"$1"/*|/var/lib/hakchi/rootfs/bin/ccs-"$1") return 0 ;; esac
  [ "$1" = nucleus ] || return 1
  case "$2" in /var/lib/hakchi/rootfs/etc/preinit.d/p????_ccsnucleus|/var/lib/hakchi/rootfs/etc/init.d/S81ccs-nucleus) return 0 ;; esac
  return 1
}
# ccs_pkg_record METHOD FILESLIST PACKAGE: every listed file is in the overlay with its listed SHA-256 (else nothing is recorded and
# the copy is reported); an earlier package's files that this one no longer carries are removed while unchanged; the journal then names
# the listed files plus everything else it named that is still there (menu entries, the optional start script, data, folders)
ccs_pkg_record(){
  local m="$1" list="$2" pkg="$3" sha mode rel bad=0 line rest s p
  while read -r sha mode rel; do
    [ -n "$rel" ] || continue
    [ "$(ccs_sha "$HKR/$rel")" = "$sha" ] || { echo "failed copy /var/lib/hakchi/rootfs/$rel"; bad=1; }
  done < "$list"
  [ $bad -eq 0 ] || return 1
  mkdir -p "$TMPC" || return 1
  awk '{ if ($3 != "") print "/var/lib/hakchi/rootfs/" $3 }' "$list" > "$TMPC/new.$$"
  ccs_own_entries "$m" > "$TMPC/old.$$"
  : > "$TMPC/keep.$$"
  while read -r line; do
    p="$(ccs_own_path "$line")"
    grep -qxF "$p" "$TMPC/new.$$" && continue
    case "$line" in
      file=*)
        if ccs_pkg_path "$m" "$p"; then
          rest="${line#file=}"; s="${rest%% *}"
          if [ -f "$R0$p" ] && [ ! -L "$R0$p" ] && [ "$(ccs_sha "$R0$p")" = "$s" ]; then rm -f "$R0$p"; echo "removed-old $p"; continue; fi
        fi
        if [ -e "$R0$p" ]; then echo "$line" >> "$TMPC/keep.$$"; fi ;;
      *) echo "$line" >> "$TMPC/keep.$$" ;;
    esac
  done < "$TMPC/old.$$"
  { cat "$TMPC/keep.$$"; awk '{ if ($3 != "") print "file=" $1 " " $2 " /var/lib/hakchi/rootfs/" $3 }' "$list"; } | ccs_own_write "$m" "$pkg"
  rm -f "$TMPC/new.$$" "$TMPC/old.$$" "$TMPC/keep.$$"
}

# ---------------------------------------------------------------- CCS HOME (the shared package: /var/lib/ccs/pkg/<version>.<attempt>, named by pkg/current)
# The same checks as the existing installer's start script: the pointer is a file naming one copy, the copy is a real folder with the
# player and its session. Sets HOME_DIR.
# ccs_chooser_installed: CCS Nucleus's startup chooser (its full profile, the ccs-nucleus-chooser package) is installed: its own journal is
# valid and the chooser program it carries is there. Used by every menu's "Choose Another Menu": the chooser when it is installed,
# otherwise Original Menu.
ccs_chooser_installed(){ ccs_own_valid nucleus-chooser && [ -f "$R0/var/lib/hakchi/rootfs/etc/ccs/nucleus/chooser/bin/ccs-home-armv7" ]; }

ccs_home_dir(){
  local P="$CCSD/pkg" V=""
  HOME_DIR=""
  if [ -f "$P/current" ] && [ ! -L "$P/current" ]; then read -r V < "$P/current"; fi
  case "$V" in ????????????????.????????) ;; *) return 1 ;; esac
  case "$V" in *[!0-9a-f.]*) return 1 ;; esac
  [ -d "$P/$V" ] && [ ! -L "$P/$V" ] && [ -x "$P/$V/bin/ccs-player-armv7" ] && [ -f "$P/$V/session.json" ] || return 1
  HOME_DIR="$P/$V"
}

# ccs_screen_free [TENTHS]: wait until no program holds the screen (as hakchi's chmenu waits after uistop); nothing is stopped by force.
# 1 when it is still held at the end.
ccs_screen_free(){
  local n=0 max="${1:-30}"
  while [ "$n" -lt "$max" ]; do
    lsof -n 2>/dev/null | grep -qF "/dev/fb0" || return 0
    usleep 100000; n=$((n + 1))
  done
  lsof -n 2>/dev/null | grep -qF "/dev/fb0" && return 1
  return 0
}

# ccs_run_home startup|menu: CCS HOME on the TV until it is left; its exit code. "startup": shows the startup picture (the console just
# started); "menu": started from the console's menu (keeps hakchi's game-hang watch fed while it runs: /tmp/.heartbeat, as a game does).
# The player writes its saves itself before it ends; everything is synced before the console's own menu gets the screen back.
ccs_run_home(){
  local how="$1" pid hb rc
  ccs_home_dir || { ccs_log "home: no complete CCS HOME package (pkg/current)"; return 20; }
  mkdir -p "$TMPC/scratch" "$TMPC/logs" "$TMPC/run" "$CCSD/data/saves" "$CCSD/data/state" "$CCSD/data/game-settings" || return 21
  rm -rf "$TMPC/pkg" && ln -s "$HOME_DIR" "$TMPC/pkg" || return 21
  set -- --session "$TMPC/pkg/session.json" --trace "$TMPC/logs/trace.jsonl" --report "$TMPC/logs/report.json" --map "$TMPC/pkg/config/controller-map.txt"
  [ "$how" = startup ] && set -- "$@" --startup "$TMPC/pkg/startup/startup.json"
  "$TMPC/pkg/bin/ccs-player-armv7" "$@" > "$TMPC/logs/player.txt" 2>&1 &
  pid=$!
  echo "$pid" > "$TMPC/run/home.pid"
  hb=""
  if [ "$how" = menu ]; then
    ( while kill -0 "$pid" 2>/dev/null; do touch "$R0/tmp/.heartbeat" 2>/dev/null; sleep 1; done ) &
    hb=$!
  fi
  wait "$pid"; rc=$?
  [ -n "$hb" ] && kill "$hb" 2>/dev/null
  rm -f "$TMPC/run/home.pid"
  sync
  return $rc
}

# ccs_home_stop: the console is shutting down while CCS HOME runs: it is asked to end (it saves, then ends); never killed by force
ccs_home_stop(){
  local pid n=0
  [ -f "$TMPC/run/home.pid" ] || return 0
  read -r pid < "$TMPC/run/home.pid"
  case "$pid" in ""|*[!0-9]*) return 0 ;; esac
  kill -TERM "$pid" 2>/dev/null || return 0
  while kill -0 "$pid" 2>/dev/null && [ $n -lt 50 ]; do usleep 100000; n=$((n + 1)); done
  sync
}

# ---------------------------------------------------------------- the console's menu entry (a folder in hakchi's game storage)
# ccs_menu_dir: where the console's menu lists games now, found with hakchi's own findGameStorage (its root menu "000" when there is
# one); with no hakchi game storage, the console's built-in games folder in hakchi's overlay. Sets MENU_DIR (a console path).
# findGameStorage looks first in games/<type>-<region> (Hakchi2 CE's own storage, e.g. games/nes-usa) and needs hakchi's sftype and
# sfregion, which sourcing hakchi's defines resets: they are found here read only, by the same files and rules as hakchi's softwareCheck
# (b0010_functions), whose other work (moving the menu's save files) must not run here. hakchi's containsGames needs filename globbing.
ccs_menu_dir(){
  MENU_DIR=""
  mkdir -p "$TMPC" || return 1
  (
    set +f
    mountpoint="$R0"; temppath="$R0/tmp"
    . "$HKR/etc/preinit" 2>/dev/null && source_base "$HKR/etc/preinit.d" >/dev/null 2>&1 || exit 1
    if [ -d "$squashfs/etc/clover" ]; then
      sftype=nes; sfregion=usa
      [ "$(cat "$squashfs/etc/clover/boardtype" 2>/dev/null)" = dp-shvc ] && sftype=snes
      grep -qF dp-hvcj "$squashfs/etc/issue" 2>/dev/null && sftype=hvcj
      case "$(cat "$squashfs/etc/clover/REGION" 2>/dev/null)" in JPN) sfregion=jpn ;; EUR) sfregion=eur ;; esac
    fi
    gs="$(findGameStorage 2>/dev/null)" || gs=""
    gs="${gs%/}"
    builtin="$HKR/usr/share/games"; [ "$sftype" = snes ] || builtin="$builtin/nes/kachikachi"
    if [ -n "$gs" ]; then if [ -d "$gs/000" ]; then echo "$gs/000"; else echo "$gs"; fi; else echo "$builtin"; fi
  ) > "$TMPC/menudir.$$" 2>/dev/null
  read -r MENU_DIR < "$TMPC/menudir.$$"; rm -f "$TMPC/menudir.$$"
  [ -n "$MENU_DIR" ] || MENU_DIR="$HKR/usr/share/games/nes/kachikachi"
  MENU_DIR="${MENU_DIR#$R0}"
  ccs_path_ok "$MENU_DIR"
}

# ccs_entry_add METHOD CODE NAME EXEC ICON: the entry folder MENU_DIR/CODE with CODE.desktop, CODE.png and CODE_small.png (written only
# where nothing else is; recorded as METHOD's). EXEC is what the console's menu runs (with /bin/sh) when the entry is chosen.
ccs_entry_add(){
  local m="$1" code="$2" name="$3" exe="$4" icon="$5" d f
  ccs_menu_dir || { echo "refused no-menu-folder"; return 1; }
  d="$MENU_DIR/$code"
  for f in "$d/$code.desktop" "$d/$code.png" "$d/${code}_small.png"; do
    ccs_own_free "$m" "$f" || { echo "refused belongs-to-someone-else $f"; return 1; }
  done
  if [ ! -d "$R0$d" ]; then mkdir -p "$R0$d" && ccs_own_add "$m" dir "$d" || return 1; fi
  cat > "$TMPC/entry.$$" <<EOF
[Desktop Entry]
Type=Application
Exec=/bin/sh $exe
Path=/var/lib/clover/profiles/0/$code
Name=$name
Icon=/var/games/$code/$code.png

[X-CLOVER Game]
Code=$code
TestID=777
ID=0
Players=1
Simultaneous=0
ReleaseDate=2026-12-01
SaveCount=0
SortRawTitle=$(echo "$name" | tr 'A-Z' 'a-z')
SortRawPublisher=CLASSIC CART STUDIO
Copyright=Classic Cart Studio 2026
EOF
  ccs_put "$R0$d/$code.desktop" < "$TMPC/entry.$$" || return 1
  rm -f "$TMPC/entry.$$"
  ccs_put "$R0$d/$code.png" < "$icon" || return 1
  ccs_put "$R0$d/${code}_small.png" < "${icon%.png}-small.png" || return 1
  for f in "$d/$code.desktop" "$d/$code.png" "$d/${code}_small.png"; do ccs_own_add "$m" file "$f" || return 1; done
  echo "entry $d"
}
# ccs_entry_remove METHOD CODE: the recorded entry files removed while unchanged (a changed one is kept and reported), then its folder
# when empty
ccs_entry_remove(){
  local m="$1" code="$2" line p
  mkdir -p "$TMPC" || return 1
  ccs_own_entries "$m" > "$TMPC/ent.$$"
  while read -r line; do
    p="$(ccs_own_path "$line")"
    case "$line" in file=*) ;; *) continue ;; esac
    case "$p" in */"$code"/"$code".desktop|*/"$code"/"$code".png|*/"$code"/"$code"_small.png) ;; *) continue ;; esac
    if ccs_own_mine "$m" "$p"; then rm -f "$R0$p"; echo "removed $p"; ccs_own_forget "$m" "$p"
    elif [ ! -e "$R0$p" ]; then echo "gone $p"; ccs_own_forget "$m" "$p"
    else echo "kept-changed $p"; fi
  done < "$TMPC/ent.$$"
  while read -r line; do
    case "$line" in dir=*/"$code") ;; *) continue ;; esac
    p="${line#dir=}"
    if [ ! -d "$R0$p" ]; then ccs_own_forget "$m" "$p"
    elif rmdir "$R0$p" 2>/dev/null; then echo "removed $p/"; ccs_own_forget "$m" "$p"
    else echo "kept-folder $p/"; fi
  done < "$TMPC/ent.$$"
  rm -f "$TMPC/ent.$$"
}
