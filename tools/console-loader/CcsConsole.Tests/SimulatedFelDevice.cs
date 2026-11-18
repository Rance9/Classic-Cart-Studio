// Classic Cart Studio console engine tests. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// A simulated Allwinner FEL device speaking the protocol FelLib uses (AWUC/AWUS USB wrapper; FEL_VERIFY_DEVICE,
// FEL_DOWNLOAD, FEL_RUN, FEL_UPLOAD). It is a PC-side model of the protocol, not of the console: it proves what the loader
// sends, in which order and to which addresses, and how it reacts to refusals and errors.
using CcsConsole.Usb;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CcsConsole.Tests
{
    public sealed class SimulatedFelDevice : IFelUsb
    {
        public uint Board = 0x00166700;
        public bool OpenSucceeds = true; public string OpenRefusal = "unexpected endpoints in 0x81 / out 0x02 (FEL uses 0x82 / 0x01)";
        public int FailDownloadAtChunk = -1;          // FEL_DOWNLOAD number (0-based) that returns an error status
        public bool DramReady;                        // set when fes1 (0x2000) is executed, or preset by a test
        public bool Gone;                             // after u-boot runs the device leaves FEL
        public readonly List<string> Log = new List<string>();
        public readonly List<uint> Executed = new List<uint>();
        public readonly List<(uint addr, int len)> Downloads = new List<(uint, int)>();
        public readonly List<string> Violations = new List<string>();
        public string BootCommand;
        public int Opens;

        readonly Dictionary<uint, byte[]> pages = new Dictionary<uint, byte[]>();   // 64 KiB pages
        readonly Queue<byte> outQ = new Queue<byte>();
        int pendingWrite = -1; readonly List<byte> payload = new List<byte>();
        enum Next { None, VerifyData, Status, UploadData }
        Next next = Next.None; uint dlAddr; int dlLen = -1; uint upAddr; int upLen; int downloads; byte pendingState;
        bool open;

        public byte ReadByte(uint a) => pages.TryGetValue(a & ~0xFFFFu, out var p) ? p[a & 0xFFFF] : (byte)0;
        public byte[] Read(uint a, int n) { var r = new byte[n]; for (int i = 0; i < n; i++) r[i] = ReadByte((uint)(a + i)); return r; }
        public void Poke(uint a, byte[] data) { for (int i = 0; i < data.Length; i++) { uint x = (uint)(a + i); if (!pages.TryGetValue(x & ~0xFFFFu, out var p)) pages[x & ~0xFFFFu] = p = new byte[0x10000]; p[x & 0xFFFF] = data[i]; } }

        public bool Open(ushort vid, ushort pid, out string why)
        {
            Opens++;
            if (Gone || !OpenSucceeds || vid != 0x1F3A || pid != 0xEFE8) { why = Gone ? "device gone" : OpenRefusal; return false; }
            open = true; why = null; Log.Add("open"); return true;
        }
        public void Close() { if (open) Log.Add("close"); open = false; }
        public void Dispose() => Close();

        public int Write(byte[] buffer, int offset, int count, int timeoutMs)
        {
            if (!open || Gone) return 0;
            var data = buffer.Skip(offset).Take(count).ToArray();
            if (pendingWrite >= 0)
            {
                payload.AddRange(data);
                if (payload.Count >= pendingWrite) { var p = payload.ToArray(); payload.Clear(); pendingWrite = -1; OnPayload(p); Respond(0); }
                return count;
            }
            if (count != 32 || Encoding.ASCII.GetString(data, 0, 4) != "AWUC") { Violations.Add("bad USB request"); return count; }
            uint len = BitConverter.ToUInt32(data, 8); byte cmd = data[16];
            if (cmd == 0x12) pendingWrite = (int)len;
            else if (cmd == 0x11) OnRead((int)len);
            else Violations.Add("unknown AW request " + cmd);
            return count;
        }

        public int Read(byte[] buffer, int offset, int count, int timeoutMs)
        {
            if (!open) throw new Exception("USB read error: not open");
            if (outQ.Count == 0) throw new Exception("USB read error: IoTimedOut");
            int n = Math.Min(count, outQ.Count); for (int i = 0; i < n; i++) buffer[offset + i] = outQ.Dequeue(); return n;
        }

        void Respond(byte csw) { var r = new byte[13]; Encoding.ASCII.GetBytes("AWUS").CopyTo(r, 0); r[12] = csw; foreach (var b in r) outQ.Enqueue(b); }
        void Status(byte state) { var s = new byte[8]; s[0] = 0xFF; s[1] = 0xFF; s[4] = state; foreach (var b in s) outQ.Enqueue(b); Respond(0); }

        void OnPayload(byte[] p)
        {
            if (dlLen >= 0)
            {   // data phase of FEL_DOWNLOAD
                if (p.Length != dlLen) Violations.Add("download length mismatch");
                bool dram = dlAddr >= 0x40000000;
                if (dram && !DramReady) { Violations.Add($"DRAM written at 0x{dlAddr:X8} before fes1 ran"); pendingState = 1; }
                else if (downloads == FailDownloadAtChunk) pendingState = 1;
                else { Poke(dlAddr, p); pendingState = 0; }
                Downloads.Add((dlAddr, p.Length)); downloads++; dlLen = -1; next = Next.Status; return;
            }
            ushort cmd = BitConverter.ToUInt16(p, 0);
            if (cmd == 0x1) { Log.Add("verify"); next = Next.VerifyData; return; }
            uint addr = BitConverter.ToUInt32(p, 4); int len = (int)BitConverter.ToUInt32(p, 8);
            if (cmd == 0x101) { dlAddr = addr; dlLen = len; if (len > 0x10000) Violations.Add("download chunk larger than 64 KiB"); return; }
            if (cmd == 0x103) { if (addr >= 0x40000000 && !DramReady) Violations.Add($"DRAM read at 0x{addr:X8} before fes1 ran"); upAddr = addr; upLen = len; next = Next.UploadData; return; }
            if (cmd == 0x102)
            {
                Executed.Add(addr); Log.Add($"run 0x{addr:X8}"); pendingState = 0; next = Next.Status;
                if (addr == 0x2000) DramReady = true;
                else if (addr == 0x47000000)
                {   // u-boot runs its bootcmd: read it back from memory (as u-boot would), then the device leaves FEL
                    var region = Read(0x47000000, 0xC0000); var s = Encoding.ASCII.GetString(region);
                    int k = s.IndexOf("bootcmd=", StringComparison.Ordinal); if (k < 0) Violations.Add("u-boot without bootcmd");
                    else { int e = s.IndexOf('\0', k); BootCommand = s.Substring(k + 8, e - k - 8); }
                }
                else Violations.Add($"execution of unexpected address 0x{addr:X8}");
                return;
            }
            Violations.Add("unexpected FEL request 0x" + cmd.ToString("X"));
        }

        void OnRead(int len)
        {
            switch (next)
            {
                case Next.VerifyData:
                    var v = new byte[32]; Encoding.ASCII.GetBytes("AWUSBFEX").CopyTo(v, 0); BitConverter.GetBytes(Board).CopyTo(v, 8); BitConverter.GetBytes((ushort)0).CopyTo(v, 16);
                    foreach (var b in v.Take(len)) outQ.Enqueue(b); Respond(0); next = Next.Status; pendingState = 0; return;
                case Next.Status:
                    var s = new byte[8]; s[0] = 0xFF; s[1] = 0xFF; s[4] = pendingState; foreach (var b in s.Take(len)) outQ.Enqueue(b); Respond(0); next = Next.None;
                    if (Executed.LastOrDefault() == 0x47000000 && BootCommand != null) Gone = true;
                    return;
                case Next.UploadData:
                    foreach (var b in Read(upAddr, len)) outQ.Enqueue(b); Respond(0); next = Next.Status; pendingState = 0; return;
                default:
                    Violations.Add("read with nothing pending"); Respond(1); return;
            }
        }
    }

    public sealed class FakeDeviceList : IUsbDeviceList
    {
        public List<UsbDeviceEntry> Items = new List<UsbDeviceEntry>();
        public IEnumerable<UsbDeviceEntry> Devices() => Items;
        public static UsbDeviceEntry Dev(ushort vid, ushort pid, string name) => new UsbDeviceEntry { Vid = vid, Pid = pid, Name = name, Path = $"usb#{vid:x4}{pid:x4}" };
    }
}
