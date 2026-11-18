r"""The public pre-launch splash's sound (public_entry.py): its MP3 fallback and the timing record of both of its files.

The splash plays the CCS HOME theme (assets/home-music/music.json), the whole 62.016-second song, looped. Its primary file is the HOME's own
browser AAC, published unchanged at brand/home/music/ccs-home-theme-<hash>.m4a (assets/home/music); a browser that cannot decode AAC gets
this MP3, made here from the song's native PCM (assets/home-music/ccs-home-theme.wav):

  every one of the PCM's 2,976,768 frames encoded once (LAME, CBR 192 kbit/s, 48,000 Hz, stereo): nothing trimmed, no fade, no level change,
  no tags; the encoder's delay and padding are written in the file's own LAME tag (the "Info" frame), so a decoder that honors it outputs
  exactly the song's frames

It writes assets/splash-music/ccs-home-theme-<first 12 hex of its SHA-256>.mp3 and assets/splash-music/splash-music.json: the MP3's bytes
and SHA-256, its LAME tag, the AAC's edit list (its 1,024 priming frames) and the frames each holds before and after the song, which the
splash uses to keep exactly the song when a browser's decoder leaves priming or padding in. Checks before anything is written: the PCM is
the recorded one (SHA-256 and frame count), the AAC is the recorded one, the new MP3 decodes (FFmpeg, honoring its LAME tag) to exactly the
song's frame count, aligned with the PCM at its start and its end, and a second encoding is byte-identical (the result is reproducible).

The site build needs none of this (public_entry.py only checks the committed MP3 against this record); making or checking it needs PyAV
(it carries FFmpeg with LAME): the versions that made the committed file are in the record.

  python make_splash_music.py            make the MP3 and the record (replaces an earlier MP3 of this folder)
  python make_splash_music.py --check    the committed MP3 and record are what this script makes now (exit 0 = yes)
Part of Classic Cart Studio: GNU GPL v3 or later, like the rest of this repository. The song itself is not (see music.json)."""
from __future__ import annotations
import hashlib, io, json, os, struct, sys, wave
HERE = os.path.dirname(os.path.abspath(__file__))
HM_DIR = os.path.join(HERE, "assets", "home-music"); HOME_DIR = os.path.join(HERE, "assets", "home")
OUT_DIR = os.path.join(HERE, "assets", "splash-music"); RECORD = os.path.join(OUT_DIR, "splash-music.json")
PUBLISHED_DIR = "brand/splash"   # where public_entry.py publishes the MP3 (prelaunch only; never in the service worker's files)
RATE, BITRATE = 48000, 192000
MP3_SPF, MP3_DECODER_DELAY = 1152, 529   # MPEG-1 Layer III: frames of 1,152 samples; every decoder's own delay (the LAME tag's delay excludes it)

def sha(b: bytes) -> str: return hashlib.sha256(b).hexdigest()

def pcm():
    m = json.load(open(os.path.join(HM_DIR, "music.json"), encoding="utf-8")); nat = m["native"]
    raw = open(os.path.join(HM_DIR, nat["file"]), "rb").read()
    if sha(raw) != nat["sha256"]: raise SystemExit("the HOME theme's PCM is not the recorded file")
    w = wave.open(io.BytesIO(raw)); p = w.getparams()
    if (p.nchannels, p.sampwidth, p.framerate, p.nframes) != (2, 2, RATE, nat["frames"]): raise SystemExit(f"unexpected PCM {p}")
    return m, w.readframes(p.nframes), p.nframes

def aac_edit_list(data: bytes) -> dict:
    """the M4A's one track: its edit list (priming to skip, the song's duration) and its packets"""
    out = {}
    def walk(o, end):
        while o < end:
            size, typ = struct.unpack(">I4s", data[o:o + 8]); hdr = 8
            if size == 1: size = struct.unpack(">Q", data[o + 8:o + 16])[0]; hdr = 16
            body = data[o + hdr:o + size]
            if typ in (b"moov", b"trak", b"edts", b"mdia", b"minf", b"stbl"): walk(o + hdr, o + size)
            elif typ == b"elst":
                n = struct.unpack(">I", body[4:8])[0]
                if n != 1: raise SystemExit(f"the M4A's edit list has {n} entries (one expected)")
                seg, mt = struct.unpack(">QQ", body[8:24]) if body[0] == 1 else struct.unpack(">II", body[8:16])
                out["edit_media_time"], out["edit_duration"] = int(mt), int(seg)
            elif typ == b"mdhd":
                ts, dur = struct.unpack(">IQ", body[20:32]) if body[0] == 1 else struct.unpack(">II", body[12:20]); out["timescale"], out["media_frames"] = ts, dur
            elif typ == b"stts":
                n = struct.unpack(">I", body[4:8])[0]; out["packets"] = sum(struct.unpack(">I", body[8 + 8 * i:12 + 8 * i])[0] for i in range(n))
            o += size
    walk(0, len(data)); return out

def mp3_info(data: bytes) -> dict:
    """the MP3's frames and its LAME tag (in the first frame, an "Info" frame that carries no audio)"""
    BR = [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320]; o = 0; frames = []
    if data[:3] == b"ID3": raise SystemExit("the MP3 has an ID3 tag (none expected)")
    while o < len(data):
        h = struct.unpack(">I", data[o:o + 4])[0]
        if (h >> 21) & 0x7FF != 0x7FF or (h >> 19) & 3 != 3 or (h >> 17) & 3 != 1 or (h >> 10) & 3 != 1: raise SystemExit(f"the MP3 is not plain MPEG-1 Layer III at 48,000 Hz (byte {o})")
        n = 144 * BR[(h >> 12) & 15] * 1000 // RATE + ((h >> 9) & 1); frames.append((o, (h >> 6) & 3)); o += n
    if o != len(data): raise SystemExit("the MP3 ends inside a frame")
    f0, mode = frames[0]; x = f0 + 4 + (17 if mode == 3 else 32)
    if data[x:x + 4] != b"Info": raise SystemExit("the MP3's first frame is not its Info (LAME) frame")
    flags = struct.unpack(">I", data[x + 4:x + 8])[0]; q = x + 8
    if flags & 15 != 15: raise SystemExit("the MP3's Info frame lacks its frame count, byte count, table of contents or quality")
    nfr, nby = struct.unpack(">II", data[q:q + 8]); q += 8 + 100 + 4
    dp = int.from_bytes(data[q + 21:q + 24], "big"); delay, padding = dp >> 12, dp & 0xFFF
    if nfr != len(frames) - 1 or nby != len(data): raise SystemExit("the MP3's Info frame does not describe the file")
    return {"frames": nfr, "lame_delay": delay, "lame_padding": padding}

def encode(pcm_bytes: bytes, n: int) -> tuple[bytes, dict]:
    import av, numpy as np
    a = np.frombuffer(pcm_bytes, dtype="<i2").reshape(-1, 2); buf = io.BytesIO()
    out = av.open(buf, "w", format="mp3", options={"id3v2_version": "0", "write_xing": "1", "fflags": "+bitexact"})
    st = out.add_stream("libmp3lame", rate=RATE, options={"flags": "+bitexact"})
    st.codec_context.bit_rate = BITRATE; st.codec_context.layout = "stereo"; st.codec_context.format = "s16p"
    for i in range(0, n, MP3_SPF):
        f = av.AudioFrame.from_ndarray(np.ascontiguousarray(a[i:i + MP3_SPF].T), format="s16p", layout="stereo"); f.sample_rate = RATE; f.pts = i
        for p in st.encode(f): out.mux(p)
    for p in st.encode(None): out.mux(p)
    out.close()
    tool = {"pyav": av.__version__, "ffmpeg": av.ffmpeg_version_info, "libavcodec": ".".join(str(x) for x in av.library_versions["libavcodec"]), "encoder": "libmp3lame"}
    return buf.getvalue(), tool

def decoded_check(mp3: bytes, pcm_bytes: bytes, n: int) -> dict:
    """FFmpeg's own decoding of the MP3 (it honors the LAME tag): exactly the song's frames, aligned with the PCM at its start and its end"""
    import av, numpy as np
    c = av.open(io.BytesIO(mp3)); out = [fr.to_ndarray() for fr in c.decode(c.streams.audio[0])]; c.close()
    d = np.concatenate(out, axis=1).astype(np.float64); ref = np.frombuffer(pcm_bytes, dtype="<i2").reshape(-1, 2).T.astype(np.float64) / 32768
    if d.shape != ref.shape: raise SystemExit(f"the MP3 decodes to {d.shape[1]} frames, the song has {n}")
    def lag(x, y): return max(range(-300, 301), key=lambda k: float(np.dot(x[400:-400], np.roll(y, k)[400:-400])))
    start, end = lag(ref[0, :16384], d[0, :16384]), lag(ref[0, -16384:], d[0, -16384:])
    if start or end: raise SystemExit(f"the MP3 is not aligned with the song (start {start}, end {end} frames)")
    return {"decoded_frames": int(d.shape[1]), "alignment_lag_start": start, "alignment_lag_end": end,
            "snr_db": round(float(10 * np.log10((ref ** 2).sum() / ((d - ref) ** 2).sum())), 1)}

def trims(lead: int, tail: int, extra_frame: int = 0) -> list[list[int]]:
    """[start, extra]: the frames a decoder may leave before the song (start) and in all (extra), for each way it may honor the file's
    priming and padding (both, only the start, only the end, neither; an MP3 decoder may also output the Info frame as silence)"""
    c = [[0, 0], [0, tail], [lead, lead], [lead, lead + tail]] + ([[lead + extra_frame, lead + tail + extra_frame]] if extra_frame else [])
    seen = {}; out = []
    for s, e in c:
        if seen.get(e, s) != s: raise SystemExit(f"two ways leave {e} extra frames at different starts: the record cannot tell them apart")
        if e not in seen: seen[e] = s; out.append([s, e])
    return out

def build() -> dict:
    m, raw, n = pcm(); br = m["browser"]
    m4a_rel = br["file"].split("/", 2)[2] if br["file"].startswith("brand/home/") else None
    if not m4a_rel: raise SystemExit("the HOME theme's browser file is not under brand/home/")
    m4a = open(os.path.join(HOME_DIR, *m4a_rel.split("/")), "rb").read()
    if sha(m4a) != br["sha256"]: raise SystemExit("the HOME theme's AAC is not the recorded file")
    el = aac_edit_list(m4a)
    if el.get("timescale") != RATE or el.get("edit_duration") != n or el["media_frames"] != el["packets"] * 1024 or el["packets"] != m["source"]["packets"]: raise SystemExit(f"unexpected AAC timing {el}")
    aac_lead = el["edit_media_time"]; aac_tail = el["media_frames"] - aac_lead - n
    if aac_tail < 0: raise SystemExit("the AAC's edit list runs past its packets")
    mp3, tool = encode(raw, n); again, _ = encode(raw, n)
    if mp3 != again: raise SystemExit("two encodings differ: the MP3 would not be reproducible")
    info = mp3_info(mp3); lead = info["lame_delay"] + MP3_DECODER_DELAY; tail = info["frames"] * MP3_SPF - n - lead
    if tail < 0: raise SystemExit("the MP3's LAME tag does not fit its frames")
    dec = decoded_check(mp3, raw, n); h = sha(mp3); name = f"ccs-home-theme-{h[:12]}.mp3"
    rec = {"schema": "ccs-splash-music-asset/1",
           "purpose": "The public pre-launch splash's sound control (tools/publish/public_entry.py): the CCS HOME theme, whole and looped, only after a visitor turns it on. Not part of HOME, the app or its offline files.",
           "song": {"record": "../home-music/music.json", "pcm_sha256": m["native"]["sha256"], "frames": n, "rate": RATE, "seconds": m["source"]["seconds"]},
           "aac": {"published": br["file"], "sha256": br["sha256"], "bytes": len(m4a), "packets": el["packets"], "edit_list": {"media_time": aac_lead, "duration": el["edit_duration"]},
                   "lead_frames": aac_lead, "tail_frames": aac_tail, "trims": trims(aac_lead, aac_tail),
                   "made": "unchanged: the HOME theme's own browser file (music.json)"},
           "mp3": {"file": name, "published": f"{PUBLISHED_DIR}/{name}", "sha256": h, "bytes": len(mp3), "frames": info["frames"],
                   "lame_tag": {"delay": info["lame_delay"], "padding": info["lame_padding"]}, "decoder_delay": MP3_DECODER_DELAY,
                   "lead_frames": lead, "tail_frames": tail, "trims": trims(lead, tail, MP3_SPF), **dec,
                   "settings": "MPEG-1 Layer III, CBR 192 kbit/s, 48,000 Hz, joint stereo, bit-exact, no ID3 tags, LAME Info frame", "tool": tool,
                   "made": "every frame of ccs-home-theme.wav encoded once (make_splash_music.py): nothing trimmed, no fade, no level change"}}
    return {"record": rec, "mp3": mp3}

def main(argv: list[str]) -> int:
    got = build(); rec, mp3 = got["record"], got["mp3"]
    if "--check" in argv:
        old = json.load(open(RECORD, encoding="utf-8")); cur = open(os.path.join(OUT_DIR, old["mp3"]["file"]), "rb").read()
        same = old == rec and cur == mp3
        print("splash music:", "the committed MP3 and record are reproduced exactly" if same else "the committed MP3 or record differs from what this script makes now")
        return 0 if same else 1
    os.makedirs(OUT_DIR, exist_ok=True)
    for f in os.listdir(OUT_DIR):
        if f.startswith("ccs-home-theme-") and f.endswith(".mp3") and f != rec["mp3"]["file"]: os.remove(os.path.join(OUT_DIR, f))
    open(os.path.join(OUT_DIR, rec["mp3"]["file"]), "wb").write(mp3)
    json.dump(rec, open(RECORD, "w", encoding="utf-8", newline="\n"), indent=1); open(RECORD, "a", encoding="utf-8", newline="\n").write("\n")
    print(f"splash music: {rec['mp3']['published']} {rec['mp3']['bytes']:,} bytes, sha256 {rec['mp3']['sha256']}; decodes to {rec['mp3']['decoded_frames']:,} frames (the song: {rec['song']['frames']:,})")
    return 0

if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
