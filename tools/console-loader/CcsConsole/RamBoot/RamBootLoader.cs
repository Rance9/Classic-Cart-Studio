// Classic Cart Studio console engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The sequence is Hakchi2 CE 3.9.3's FEL memboot (Tasks/MembootTasks.cs: WaitForFelOrMembootableShell + MembootFel):
//   open the FEL device, verify board 0x00166700, run fes1 (DRAM init), write the boot image at 0x47400000 in 64 KiB
//   FEL_DOWNLOAD chunks, then write u-boot at 0x47000000 with bootcmd="boota 47400000" and run it. Nothing else is sent.
using CcsConsole.Usb;
using FelLib;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CcsConsole.RamBoot
{
    public enum RamBootStage { Discover, Open, DramInit, Transfer, BootCommand, Done, Failed }

    public sealed class RamBootProgress
    {
        public RamBootStage Stage; public string Message; public long Done, Total;
        public double Percent => Total > 0 ? 100.0 * Done / Total : 0;
    }

    public sealed class RamBootResult
    {
        public bool Transferred, BootCommandSent; public string Error; public RamBootStage FailedAt; public string Device;
        public TimeSpan Elapsed;
        public const string NotProof = "The image is in the console's RAM and the boot command was sent. This does not prove the console booted: only the TV shows that.";
    }

    public static class FelDiscovery
    {
        public const ushort FelVid = 0x1F3A, FelPid = 0xEFE8;
        /// <summary>exactly the console's boot ROM identity; anything else (a phone, a hub, another gadget) is ignored</summary>
        public static List<UsbDeviceEntry> FindFel(IUsbDeviceList list) => list.Devices().Where(d => d.Vid == FelVid && d.Pid == FelPid).ToList();
    }

    public sealed class RamBootLoader
    {
        readonly IUsbDeviceList devices; readonly Func<IFelUsb> transport;
        public event Action<RamBootProgress> Progress;
        public int DramInitSleepMs = 2000;
        public RamBootLoader(IUsbDeviceList devices, Func<IFelUsb> transport) { this.devices = devices; this.transport = transport; }

        void Report(RamBootStage s, string m, long done = 0, long total = 0) => Progress?.Invoke(new RamBootProgress { Stage = s, Message = m, Done = done, Total = total });

        /// <summary>the image must already be validated (RamImage.Load). Never throws: errors come back in the result.</summary>
        public RamBootResult Boot(RamImage image)
        {
            var res = new RamBootResult(); var t0 = DateTime.UtcNow; var stage = RamBootStage.Discover;
            try
            {
                Report(stage, "looking for the console in FEL mode (USB 1F3A:EFE8)");
                var fel = FelDiscovery.FindFel(devices);
                if (fel.Count == 0) return Fail(res, stage, "no console in FEL mode was found (USB 1F3A:EFE8 with its driver)", t0);
                if (fel.Count > 1) return Fail(res, stage, "more than one device in FEL mode is connected; connect only the NES Classic", t0);
                res.Device = fel[0].ToString();
                stage = RamBootStage.Open; Report(stage, "opening " + res.Device);
                using (var usb = transport())
                using (var f = new Fel(usb) { Fes1Bin = image.Fes1, UBootBin = image.UbootSd, DramInitSleepMs = DramInitSleepMs })
                {
                    if (!f.Open(out var why)) return Fail(res, stage, why, t0);
                    if (f.UbootCommandOffset < 0) return Fail(res, stage, "u-boot has no bootcmd variable", t0);
                    stage = RamBootStage.DramInit; Report(stage, "initializing the console's memory (fes1)");
                    f.InitDram(true);
                    Report(stage, f.DramWasResident ? "memory was already initialized" : "memory initialized");
                    stage = RamBootStage.Transfer; long total = image.Image.Length, done = 0;
                    Report(stage, "transferring the image to RAM at 0x47400000", 0, total);
                    f.WriteMemory(Fel.transfer_base_m, image.Image, (a, c) => { done = Math.Min(total, done + Fel.MaxBulkSize); Report(RamBootStage.Transfer, null, done, total); });
                    res.Transferred = true;
                    stage = RamBootStage.BootCommand; Report(stage, "starting the image (" + RamImage.BootCommand + ")");
                    f.RunUbootCmd(RamImage.BootCommand);
                    res.BootCommandSent = true;
                }
                res.Elapsed = DateTime.UtcNow - t0; Report(RamBootStage.Done, RamBootResult.NotProof);
                return res;
            }
            catch (Exception ex) { return Fail(res, stage, ex.Message, t0); }
        }

        RamBootResult Fail(RamBootResult r, RamBootStage s, string why, DateTime t0)
        {
            r.Error = why; r.FailedAt = s; r.Elapsed = DateTime.UtcNow - t0; Report(RamBootStage.Failed, why); return r;
        }
    }
}
