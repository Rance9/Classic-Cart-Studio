// Derived from FelLib (https://github.com/TeamShinkansen/FelLib, commit e28ddfe, Fel.cs), part of Hakchi2 CE.
// Copyright (C) the FelLib / Hakchi2 CE authors (Cluster, madmonkey, and contributors). GPL-3.0.
// Modified 2026-10-18 for Classic Cart Studio by Rance9 (GPL-3.0-or-later):
//   - USB access goes through IFelUsb (CcsConsole.Usb) instead of LibUsbDotNet directly, so the protocol can be exercised
//     against a simulated FEL device; device opening and the endpoint check (0x82 in / 0x01 out) moved to LibUsbFelUsb.
//   - ReadFlash and WriteFlash (sunxi_flash phy_read / phy_write) are removed: this loader never touches flash.
//   - RunUbootCmd accepts only "boota <hex address>" and only the no-return form (the RAM-boot command).
//   - InitDram keeps upstream's behavior (skip when fes1 is already resident; otherwise upload, run, wait 2 s).
//   - ReadSid (2026-11-15): the SoC id registers read with FEL_UPLOAD, for Developer Mode's test boot to name the console before sending.
// The protocol, addresses, chunk sizes and timeouts are unchanged.
using CcsConsole.Usb;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace FelLib
{
    public class Fel : IDisposable
    {
        public byte[] Fes1Bin;
        byte[] uBootBin;

        public enum CurrentAction { RunningCommand, ReadingMemory, WritingMemory }
        public delegate void OnFelProgress(CurrentAction action, string command);

        readonly IFelUsb usb;
        const int ReadTimeout = 1000;
        const int WriteTimeout = 1000;
        public const int MaxBulkSize = 0x10000;
        UInt16 vid = 0x1F3A, pid = 0xEFE8;
        bool DramInitDone = false;
        public int DramInitSleepMs = 2000;   // upstream: Thread.Sleep(2000) after running fes1 (tests shorten it)

        int cmdOffset = -1;
        public const UInt32 fes1_base_m = 0x2000;
        public const UInt32 dram_base = 0x40000000;
        public const UInt32 uboot_base_m = dram_base + 0x7000000u;
        public const UInt32 sector_size = 0x20000u;
        public const UInt32 transfer_base_m = dram_base + 0x7400000u;
        public const UInt32 transfer_max_size = sector_size * 0x100;
        public const UInt32 ExpectedBoard = 0x00166700;
        static readonly Regex AllowedUbootCommand = new Regex("^boota [0-9a-f]{1,8}$");

        public Fel(IFelUsb usb) { this.usb = usb; }

        public byte[] UBootBin
        {
            get { return uBootBin; }
            set
            {
                uBootBin = value;
                cmdOffset = -1;
                var prefix = "bootcmd=";
                for (int i = 0; i < uBootBin.Length - prefix.Length; i++)
                    if (Encoding.ASCII.GetString(uBootBin, i, prefix.Length) == prefix)
                    {
                        cmdOffset = i + prefix.Length;
                        break;
                    }
            }
        }
        public int UbootCommandOffset => cmdOffset;

        /// <summary>opens the FEL device and verifies the board id; why explains a refusal</summary>
        public bool Open(out string why, UInt16 vid = 0x1F3A, UInt16 pid = 0xEFE8)
        {
            this.vid = vid; this.pid = pid;
            Close();
            if (!usb.Open(vid, pid, out why)) return false;
            uint board;
            try { board = VerifyDevice().Board; }
            catch (Exception ex) { Close(); why = "the device did not answer FEL_VERIFY_DEVICE: " + ex.Message; return false; }
            if (board != ExpectedBoard)
            {
                Close(); why = string.Format("board id 0x{0:X8} is not the NES Classic's (0x{1:X8})", board, ExpectedBoard); return false;
            }
            why = null; return true;
        }

        public void Close() { usb.Close(); }

        private void WriteToUSB(byte[] buffer)
        {
            int pos = 0;
            while (pos < buffer.Length)
            {
                int l = usb.Write(buffer, pos, buffer.Length - pos, WriteTimeout);
                if (l > 0) pos += l;
                else throw new Exception("Can't write to USB");
            }
        }

        private byte[] ReadFromUSB(UInt32 length)
        {
            var result = new byte[length];
            int pos = 0;
            while (pos < length)
                pos += usb.Read(result, pos, (int)(length - pos), ReadTimeout);
            return result;
        }

        private void FelWrite(byte[] buffer)
        {
            var req = new AWUSBRequest();
            req.Cmd = AWUSBRequest.RequestType.AW_USB_WRITE;
            req.Len = (uint)buffer.Length;
            WriteToUSB(req.Data);
            WriteToUSB(buffer);
            var resp = new AWUSBResponse(ReadFromUSB(13));
            if (resp.CswStatus != 0) throw new FelException("FEL write error");
        }

        private byte[] FelRead(UInt32 length)
        {
            var req = new AWUSBRequest();
            req.Cmd = AWUSBRequest.RequestType.AW_USB_READ;
            req.Len = length;
            WriteToUSB(req.Data);
            var result = ReadFromUSB(length);
            var resp = new AWUSBResponse(ReadFromUSB(13));
            if (resp.CswStatus != 0) throw new FelException("FEL read error");
            return result;
        }

        private void FelRequest(AWFELStandardRequest.RequestType command)
        {
            var req = new AWFELStandardRequest();
            req.Cmd = command;
            FelWrite(req.Data);
        }

        private void FelRequest(AWFELStandardRequest.RequestType command, UInt32 address, UInt32 length)
        {
            var req = new AWFELMessage();
            req.Cmd = command;
            req.Address = address;
            req.Len = length;
            FelWrite(req.Data);
        }

        public AWFELVerifyDeviceResponse VerifyDevice()
        {
            FelRequest(AWFELStandardRequest.RequestType.FEL_VERIFY_DEVICE);
            byte[] resp;
            try { resp = FelRead(32); }
            catch { resp = new byte[32]; }
            var status = new AWFELStatusResponse(FelRead(8));
            return new AWFELVerifyDeviceResponse(resp);
        }

        public void WriteMemory(UInt32 address, byte[] buffer, OnFelProgress callback = null)
        {
            if (address >= dram_base)
                InitDram();

            UInt32 length = (UInt32)buffer.Length;
            if (length != (length & ~((UInt32)3)))
            {
                length = (length + 3) & ~((UInt32)3);
                var newBuffer = new byte[length];
                Array.Copy(buffer, 0, newBuffer, 0, buffer.Length);
                buffer = newBuffer;
            }

            int pos = 0;
            while (pos < buffer.Length)
            {
                callback?.Invoke(CurrentAction.WritingMemory, null);
                var buf = new byte[Math.Min(buffer.Length - pos, MaxBulkSize)];
                Array.Copy(buffer, pos, buf, 0, buf.Length);
                FelRequest(AWFELStandardRequest.RequestType.FEL_DOWNLOAD, (UInt32)(address + pos), (uint)buf.Length);
                FelWrite(buf);
                var status = new AWFELStatusResponse(FelRead(8));
                if (status.State != 0) throw new FelException("FEL write error");
                pos += buf.Length;
            }
        }

        private byte[] ReadMemory(UInt32 address, UInt32 length, OnFelProgress callback = null)
        {
            if (address >= dram_base)
                InitDram();

            length = (length + 3) & ~((UInt32)3);

            var result = new List<byte>();
            while (length > 0)
            {
                callback?.Invoke(CurrentAction.ReadingMemory, null);
                var l = Math.Min(length, MaxBulkSize);
                FelRequest(AWFELStandardRequest.RequestType.FEL_UPLOAD, address, l);
                var r = FelRead((UInt32)l);
                result.AddRange(r);
                var status = new AWFELStatusResponse(FelRead(8));
                if (status.State != 0) throw new FelException("FEL read error");
                length -= l;
                address += l;
            }
            return result.ToArray();
        }

        /// <summary>Classic Cart Studio addition (Developer Mode's test boot): the SoC's 128-bit SID, read with FEL_UPLOAD from its fixed
        /// register address 0x01C23800 (the same 16 bytes the console's own "sntool devmem 0x01c23800" reports as its hardware id). Reads
        /// only; below DRAM, so nothing (not even fes1) is written or run first.</summary>
        public const UInt32 sid_base = 0x01C23800;
        public byte[] ReadSid() => ReadMemory(sid_base, 16);

        /// <summary>true when fes1 was already resident (upstream treats DRAM as initialized then)</summary>
        public bool DramWasResident { get; private set; }

        public bool InitDram(bool force = false)
        {
            if (DramInitDone && !force) return true;
            if (DramInitDone) return true;
            const UInt32 testSize = 0x80;
            if (Fes1Bin == null || Fes1Bin.Length < testSize)
                throw new FelException("Can't init DRAM, incorrect Fes1 binary");
            var buf = ReadMemory((UInt32)(fes1_base_m + Fes1Bin.Length - testSize), testSize);
            var buf2 = new byte[testSize];
            Array.Copy(Fes1Bin, Fes1Bin.Length - buf.Length, buf2, 0, testSize);
            if (buf.SequenceEqual(buf2))
            {
                DramWasResident = true;
                return DramInitDone = true;
            }
            WriteMemory(fes1_base_m, Fes1Bin);
            Exec(fes1_base_m);
            Thread.Sleep(DramInitSleepMs);
            return DramInitDone = true;
        }

        public void Exec(UInt32 address)
        {
            FelRequest(AWFELStandardRequest.RequestType.FEL_RUN, address, 0);
            var status = new AWFELStatusResponse(FelRead(8));
            if (status.State != 0) throw new FelException("FEL run error");
        }

        /// <summary>
        /// writes u-boot (unless already resident), sets its bootcmd to the command and runs it. Only the RAM-boot command
        /// "boota &lt;address&gt;" is accepted, and the device is not expected to answer afterwards (upstream noreturn=true).
        /// </summary>
        public void RunUbootCmd(string command, OnFelProgress callback = null)
        {
            if (command == null || !AllowedUbootCommand.IsMatch(command))
                throw new FelException("refused u-boot command (only \"boota <address>\" is allowed): " + command);
            callback?.Invoke(CurrentAction.RunningCommand, command);
            if (cmdOffset < 0) throw new Exception("Invalid Uboot binary, command variable not found");
            const UInt32 testSize = 0x20;
            if (UBootBin == null || UBootBin.Length < testSize)
                throw new FelException("Can't init Uboot, incorrect Uboot binary");
            var buf = ReadMemory(uboot_base_m, testSize);
            var buf2 = new byte[testSize];
            Array.Copy(UBootBin, 0, buf2, 0, testSize);
            if (!buf.SequenceEqual(buf2))
                WriteMemory(uboot_base_m, UBootBin);
            var cmdBuff = Encoding.ASCII.GetBytes(command + "\0");
            WriteMemory((uint)(uboot_base_m + cmdOffset), cmdBuff);
            Exec((uint)uboot_base_m);
        }

        public void Dispose() { Close(); }
    }
}
