// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// A read-only decrypted view of the console's encrypted system part, for the Backup Browser: the part's exact bytes (a stream over the
// backup) read through the cipher the console's own startup opens it with. hakchi 3.9.3's /init (pinned): cryptsetup open <part>
// root-crypt --type plain --cipher aes-xts-plain --key-file key-file. That is plain dm-crypt (no header) with:
//   - AES in XTS mode (IEEE 1619): the 32-byte key file is the cipher key as it is (plain mode takes a key file without hashing it) and its
//     default 256-bit size, i.e. two AES-128 keys: the first 16 bytes encrypt the data, the last 16 the tweak;
//   - 512-byte sectors, each its own XTS data unit, numbered from the start of the part;
//   - the "plain" IV: the sector number's low 32 bits, little-endian, in a 16-byte block of zeros.
// These rules were checked against the owner's own console data (see the freeze 9 handoff); the browser still checks what every key
// decrypts to (a SquashFS superblock and a readable file list) before it lists anything, so a wrong key or another cipher is never
// presented as the system's files. AES itself is Windows' own (System.Security.Cryptography); the XTS steps around it are below. Nothing
// is written: the key stays in this object's memory and is cleared when it is disposed.
using System;
using System.IO;
using System.Security.Cryptography;

namespace CcsBackup.Device
{
    public sealed class XtsView : Stream
    {
        public const int Sector = 512;
        const int Chunk = 64 * Sector;   // decrypted 32 KB at a time; the last chunk is kept for the SquashFS reader's small reads
        readonly Stream inner; readonly long length; long pos;
        ICryptoTransform data, tweak; readonly byte[] cbuf = new byte[Chunk], pbuf = new byte[Chunk]; long cached = -1; int cachedLen;

        /// <summary>key: the 32-byte key file (copied; the caller keeps its own copy private). inner: the part's exact bytes, seekable, a whole
        /// number of sectors.</summary>
        public XtsView(Stream inner, byte[] key)
        {
            if (key == null || key.Length != 32) throw new ArgumentException("the system part's key is 32 bytes");
            if (!inner.CanSeek || inner.Length % Sector != 0) throw new InvalidDataException("the system part is not a whole number of 512-byte sectors");
            this.inner = inner; length = inner.Length;
            data = Ecb(key, 0, false); tweak = Ecb(key, 16, true);
        }

        static ICryptoTransform Ecb(byte[] key, int at, bool encrypt)
        {
            var k = new byte[16]; Buffer.BlockCopy(key, at, k, 0, 16);
            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.ECB; aes.Padding = PaddingMode.None; aes.KeySize = 128;
                try { return encrypt ? aes.CreateEncryptor(k, null) : aes.CreateDecryptor(k, null); }
                finally { Array.Clear(k, 0, k.Length); }
            }
        }

        /// <summary>One XTS data unit (a whole number of 16-byte blocks, no ciphertext stealing: the console's sectors never need it), in
        /// place. unit: the full IV value (the "plain" IV takes the sector number's low 32 bits). Public for the tests' known-answer vectors
        /// and their own synthetic encrypted parts; the browser only decrypts.</summary>
        public static void Transform(ICryptoTransform dataCipher, ICryptoTransform tweakCipher, byte[] buf, int off, int len, ulong unit)
        {
            if (len % 16 != 0) throw new ArgumentException("an XTS unit here is whole 16-byte blocks");
            var t = new byte[16]; for (int i = 0; i < 8; i++) t[i] = (byte)(unit >> (8 * i));
            var T = new byte[16]; tweakCipher.TransformBlock(t, 0, 16, T, 0);
            var ts = new byte[len];
            for (int j = 0; j < len; j += 16)
            {
                Buffer.BlockCopy(T, 0, ts, j, 16);
                // T times alpha in GF(2^128), the little-endian byte order IEEE 1619 uses
                int carry = 0; for (int i = 0; i < 16; i++) { int b = T[i]; T[i] = (byte)((b << 1) | carry); carry = b >> 7; }
                if (carry != 0) T[0] ^= 0x87;
            }
            for (int i = 0; i < len; i++) buf[off + i] ^= ts[i];
            var o = new byte[len]; dataCipher.TransformBlock(buf, off, len, o, 0);
            for (int i = 0; i < len; i++) buf[off + i] = (byte)(o[i] ^ ts[i]);
        }

        /// <summary>AES-128 ECB transforms for a 32-byte XTS key (data, tweak); encrypt for the tests' synthetic parts</summary>
        public static (ICryptoTransform data, ICryptoTransform tweak) Ciphers(byte[] key, bool encrypt) => (Ecb(key, 0, encrypt), Ecb(key, 16, true));

        void Load(long chunkAt)
        {
            if (cached == chunkAt) return;
            int n = (int)Math.Min(Chunk, length - chunkAt);
            inner.Position = chunkAt; int got = 0;
            while (got < n) { int k = inner.Read(cbuf, got, n - got); if (k <= 0) throw new IOException("the system part ends early"); got += k; }
            Buffer.BlockCopy(cbuf, 0, pbuf, 0, n);
            for (int s = 0; s < n; s += Sector) Transform(data, tweak, pbuf, s, Sector, (uint)((chunkAt + s) / Sector));
            cached = chunkAt; cachedLen = n;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (data == null) throw new ObjectDisposedException(nameof(XtsView));
            if (pos >= length || count <= 0) return 0;
            count = (int)Math.Min(count, length - pos); int done = 0;
            while (done < count)
            {
                long at = (pos / Chunk) * Chunk; Load(at);
                int from = (int)(pos - at), k = Math.Min(count - done, cachedLen - from);
                Buffer.BlockCopy(pbuf, from, buffer, offset + done, k); done += k; pos += k;
            }
            return done;
        }

        public override bool CanRead => true; public override bool CanSeek => true; public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => pos; set { if (value < 0) throw new IOException("before the start"); pos = value; } }
        public override long Seek(long offset, SeekOrigin origin) { Position = origin == SeekOrigin.Begin ? offset : origin == SeekOrigin.Current ? pos + offset : length + offset; return pos; }
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) { data?.Dispose(); tweak?.Dispose(); data = tweak = null; Array.Clear(pbuf, 0, pbuf.Length); cached = -1; inner.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
