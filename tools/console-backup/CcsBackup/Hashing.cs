// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// CRC-32 (the ZIP polynomial), and a pass-through stream that counts and hashes everything written through it (CRC-32, SHA-256, MD5),
// so a member is hashed while it streams, in bounded memory; and one that hashes it in fixed-size chunks (the repair map).
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace CcsBackup
{
    public sealed class Crc32
    {
        static readonly uint[] Table = Make();
        uint crc = 0xFFFFFFFF;
        static uint[] Make()
        {
            var t = new uint[256];
            for (uint i = 0; i < 256; i++) { uint c = i; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1; t[i] = c; }
            return t;
        }
        public void Update(byte[] b, int o, int n) { uint c = crc; for (int i = o; i < o + n; i++) c = Table[(c ^ b[i]) & 0xFF] ^ (c >> 8); crc = c; }
        public uint Value => crc ^ 0xFFFFFFFF;
    }

    public static class Hex
    {
        public static string Of(byte[] h) { var c = new char[h.Length * 2]; const string x = "0123456789abcdef"; for (int i = 0; i < h.Length; i++) { c[2 * i] = x[h[i] >> 4]; c[2 * i + 1] = x[h[i] & 15]; } return new string(c); }
        public static bool IsHex(string s, int len) { if (s == null || s.Length != len) return false; foreach (var ch in s) if (!(ch >= '0' && ch <= '9' || ch >= 'a' && ch <= 'f')) return false; return true; }
    }

    /// <summary>Everything written goes to the inner stream (unless it is null: hashing only) and into CRC-32, SHA-256 and MD5.</summary>
    public sealed class HashingSink : Stream
    {
        readonly Stream inner; readonly Crc32 crc = new Crc32();
        readonly SHA256 sha = SHA256.Create(); readonly MD5 md5 = MD5.Create();
        readonly Action<long> progress; readonly Func<bool> cancelled;
        bool done; string shaHex, md5Hex;
        public long Count { get; private set; }
        public HashingSink(Stream inner, Action<long> progress = null, Func<bool> cancelled = null) { this.inner = inner; this.progress = progress; this.cancelled = cancelled; }
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (done) throw new InvalidOperationException("already finished");
            if (cancelled != null && cancelled()) throw new OperationCanceledException("canceled");
            if (count <= 0) return;
            crc.Update(buffer, offset, count); sha.TransformBlock(buffer, offset, count, null, 0); md5.TransformBlock(buffer, offset, count, null, 0);
            inner?.Write(buffer, offset, count); Count += count; progress?.Invoke(Count);
        }
        public void Finish()
        {
            if (done) return; done = true;
            sha.TransformFinalBlock(new byte[0], 0, 0); md5.TransformFinalBlock(new byte[0], 0, 0);
            shaHex = Hex.Of(sha.Hash); md5Hex = Hex.Of(md5.Hash);
        }
        public uint Crc => crc.Value;
        public string Sha256 { get { Finish(); return shaHex; } }
        public string Md5 { get { Finish(); return md5Hex; } }
        public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
        public override long Length => Count; public override long Position { get => Count; set => throw new NotSupportedException(); }
        public override void Flush() { inner?.Flush(); }
        public override int Read(byte[] b, int o, int c) => throw new NotSupportedException();
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) { sha.Dispose(); md5.Dispose(); } base.Dispose(disposing); }
    }

    public static class FastHash
    {
        /// <summary>SHA-256 by Windows' own implementation (CNG: the same digest, several times faster than the managed one that
        /// SHA256.Create gives on .NET Framework), or the managed one where CNG is not available</summary>
        public static SHA256 Sha256()
        {
            try { return new SHA256Cng(); }
            catch (Exception ex) when (ex is PlatformNotSupportedException || ex is CryptographicException) { return SHA256.Create(); }
        }
    }

    /// <summary>The SHA-256 of every fixed-size chunk of what is written (the last chunk may be shorter; nothing written: no chunk), passed
    /// on to an optional inner stream: the repair map's chunk hashes (Repair/RepairMap.cs), computed while a member streams.</summary>
    public sealed class ChunkHasher : Stream
    {
        readonly Stream inner; readonly int size; readonly SHA256 sha = FastHash.Sha256(); int filled; bool done;
        public readonly List<string> Chunks = new List<string>();
        public long Count { get; private set; }
        public ChunkHasher(int chunkSize, Stream inner = null) { if (chunkSize <= 0) throw new ArgumentException("chunk size"); size = chunkSize; this.inner = inner; }
        public override void Write(byte[] b, int o, int c)
        {
            if (done) throw new InvalidOperationException("already finished");
            inner?.Write(b, o, c); Count += c;
            while (c > 0)
            {
                int n = Math.Min(c, size - filled);
                sha.TransformBlock(b, o, n, null, 0); filled += n; o += n; c -= n;
                if (filled == size) Close1();
            }
        }
        void Close1() { sha.TransformFinalBlock(new byte[0], 0, 0); Chunks.Add(Hex.Of(sha.Hash)); sha.Initialize(); filled = 0; }
        /// <summary>the chunk hashes, the last (shorter) chunk included</summary>
        public List<string> Finish() { if (!done) { done = true; if (filled > 0) Close1(); } return Chunks; }
        public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
        public override long Length => Count; public override long Position { get => Count; set => throw new NotSupportedException(); }
        public override void Flush() { inner?.Flush(); }
        public override int Read(byte[] b, int o, int c) => throw new NotSupportedException();
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) sha.Dispose(); base.Dispose(disposing); }
    }

    /// <summary>A read-only window [start, start+length) of a seekable stream (a stored ZIP member, a partition image inside a file).</summary>
    public sealed class SubStream : Stream
    {
        readonly Stream s; readonly long start, len; long pos;
        public SubStream(Stream s, long start, long length) { this.s = s; this.start = start; len = length; }
        public override int Read(byte[] b, int o, int c)
        {
            if (pos >= len) return 0;
            if (c > len - pos) c = (int)(len - pos);
            s.Position = start + pos; int n = s.Read(b, o, c);
            if (n <= 0) throw new EndOfStreamException("the file ended before the member did");
            pos += n; return n;
        }
        public override bool CanRead => true; public override bool CanSeek => true; public override bool CanWrite => false;
        public override long Length => len;
        public override long Position { get => pos; set { if (value < 0 || value > len) throw new IOException("outside the member"); pos = value; } }
        public override long Seek(long o, SeekOrigin origin) { Position = origin == SeekOrigin.Begin ? o : origin == SeekOrigin.Current ? pos + o : len + o; return pos; }
        public override void Flush() { }
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }

    public static class Streams
    {
        public const int Buffer = 1 << 20;
        /// <summary>copies exactly n bytes (or everything when n &lt; 0); returns the count; a short source is an EndOfStreamException</summary>
        public static long Copy(Stream from, Stream to, long n = -1, Func<bool> cancelled = null)
        {
            var b = new byte[Buffer]; long total = 0;
            while (n < 0 || total < n)
            {
                if (cancelled != null && cancelled()) throw new OperationCanceledException("canceled");
                int want = n < 0 ? b.Length : (int)Math.Min(b.Length, n - total);
                int got = from.Read(b, 0, want);
                if (got <= 0) { if (n >= 0) throw new EndOfStreamException("the data ended after " + total + " of " + n + " bytes"); break; }
                to.Write(b, 0, got); total += got;
            }
            return total;
        }
    }
}
