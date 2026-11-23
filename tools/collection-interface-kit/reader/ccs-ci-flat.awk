# ccs-ci-flat.awk: a JSON document as one line per value, for shell scripts on small systems (POSIX awk: BusyBox awk, mawk, gawk).
# Part of the CCS Collection Interface kit. Copyright (c) 2026 Rance9. MIT License (see LICENSE).
#
#   awk -f ccs-ci-flat.awk index.json
#
# Each line: PATH <TAB> TYPE <TAB> VALUE
#   PATH   the names and array positions leading to the value, joined by "." (the document itself is "."); in a name, the characters
#          ".", "%", tab and newline are written %2E, %25, %09 and %0A
#   TYPE   string, number, true, false, null, object or array (an object's or array's own line comes after its members; its VALUE is
#          how many members it has)
#   VALUE  a string with \ written \\, newline \n, tab \t and carriage return \r; \uXXXX escapes become UTF-8 (run with LC_ALL=C)
# A document that is not valid JSON, or that nests deeper than 64 levels, prints a message on standard error and exits 2 (its lines up to
# there may already be printed: a reader uses the output only when the exit status is 0).
{ L[++NL] = $0 }
END {
  N = 0   # the document as one character per element (bytes, with LC_ALL=C)
  for (l = 1; l <= NL; l++) { n = length(L[l]); for (i = 1; i <= n; i++) C[++N] = substr(L[l], i, 1); C[++N] = "\n" }
  P = 1; HEX = "0123456789abcdef"
  if (N == 0) fail("the document is empty")
  value(".", 0); ws()
  if (P <= N) fail("text after the document")
  exit 0
}
function fail(m) { printf "ccs-ci-flat: not valid JSON: %s (character %d)\n", m, P > "/dev/stderr"; exit 2 }
function ws() { while (P <= N && (C[P] == " " || C[P] == "\t" || C[P] == "\n" || C[P] == "\r")) P++ }
function emit(path, type, v) { printf "%s\t%s\t%s\n", path, type, v }
function join(path, name) { return path == "." ? name : path "." name }
function keyesc(k,   o, i, n, c) {
  n = length(k); o = ""
  for (i = 1; i <= n; i++) { c = substr(k, i, 1); if (c == ".") o = o "%2E"; else if (c == "%") o = o "%25"; else if (c == "\t") o = o "%09"; else if (c == "\n") o = o "%0A"; else o = o c }
  return o
}
function hexval(h,   i, v, d) {
  v = 0; h = tolower(h)
  for (i = 1; i <= 4; i++) { d = index(HEX, substr(h, i, 1)); if (d == 0) fail("a \\u escape needs four hex digits"); v = v * 16 + d - 1 }
  return v
}
function utf8(cp) {
  if (cp < 128) return sprintf("%c", cp)
  if (cp < 2048) return sprintf("%c%c", 192 + int(cp / 64), 128 + cp % 64)
  if (cp < 65536) return sprintf("%c%c%c", 224 + int(cp / 4096), 128 + int(cp / 64) % 64, 128 + cp % 64)
  return sprintf("%c%c%c%c", 240 + int(cp / 262144), 128 + int(cp / 4096) % 64, 128 + int(cp / 64) % 64, 128 + cp % 64)
}
# a string at P (its opening quote): its value, escaped for the output as above
function str(   o, c, cp, lo) {
  P++; o = ""
  while (P <= N) {
    c = C[P]
    if (c == "\"") { P++; return o }
    if (c == "\\") {
      P++; c = C[P]
      if (c == "n") o = o "\\n"; else if (c == "t") o = o "\\t"; else if (c == "r") o = o "\\r"
      else if (c == "b") o = o "\\b"; else if (c == "f") o = o "\\f"
      else if (c == "\\") o = o "\\\\"; else if (c == "\"" || c == "/") o = o c
      else if (c == "u") {
        cp = hexval(C[P + 1] C[P + 2] C[P + 3] C[P + 4]); P += 4
        if (cp >= 55296 && cp < 56320 && C[P + 1] == "\\" && C[P + 2] == "u") {   # a surrogate pair: one character
          lo = hexval(C[P + 3] C[P + 4] C[P + 5] C[P + 6])
          if (lo >= 56320 && lo < 57344) { cp = 65536 + (cp - 55296) * 1024 + (lo - 56320); P += 6 }
        }
        if (cp == 92) o = o "\\\\"; else if (cp == 10) o = o "\\n"; else if (cp == 9) o = o "\\t"; else if (cp == 13) o = o "\\r"; else o = o utf8(cp)
      }
      else fail("an unknown escape")
      P++; continue
    }
    if (c == "\n" || c == "\t" || c == "\r") fail("a control character in a string")
    o = o c; P++
  }
  fail("a string without its closing quote")
}
function value(path, depth,   c, k, i, n, w) {
  if (depth > 64) fail("nested deeper than 64 levels")
  ws(); c = C[P]
  if (c == "{") {
    P++; ws(); n = 0
    if (C[P] == "}") { P++; emit(path, "object", 0); return }
    while (1) {
      ws(); if (C[P] != "\"") fail("a name was expected")
      k = str(); ws(); if (C[P] != ":") fail("':' was expected"); P++
      value(join(path, keyesc(k)), depth + 1); n++; ws()
      if (C[P] == ",") { P++; continue }
      if (C[P] == "}") { P++; emit(path, "object", n); return }
      fail("',' or '}' was expected")
    }
  }
  if (c == "[") {
    P++; ws(); i = 0
    if (C[P] == "]") { P++; emit(path, "array", 0); return }
    while (1) {
      value(join(path, i), depth + 1); i++; ws()
      if (C[P] == ",") { P++; continue }
      if (C[P] == "]") { P++; emit(path, "array", i); return }
      fail("',' or ']' was expected")
    }
  }
  if (c == "\"") { emit(path, "string", str()); return }
  if (c == "t" && C[P + 1] C[P + 2] C[P + 3] == "rue") { P += 4; emit(path, "true", "true"); return }
  if (c == "f" && C[P + 1] C[P + 2] C[P + 3] C[P + 4] == "alse") { P += 5; emit(path, "false", "false"); return }
  if (c == "n" && C[P + 1] C[P + 2] C[P + 3] == "ull") { P += 4; emit(path, "null", "null"); return }
  w = ""
  while (P <= N && index("-+0123456789.eE", C[P]) > 0) { w = w C[P]; P++ }
  if (w == "" || w !~ /^-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][-+]?[0-9]+)?$/) fail("a value was expected")
  emit(path, "number", w)
}
