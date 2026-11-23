// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Opening a recovery session.
//   Images     the Classic Cart Studio recovery images (tools/console-backup/ram/build_images.py: ccs-backup, ccs-restore, ccs-prepare),
//              embedded in this assembly and loaded from memory (RamImage.FromSource): nothing is extracted to disk to use them.
//   Refusal    the boundaries a real console's session of a kind needs (Gate): ConsoleSessions, and for every kind but the backup image
//              (Back Up Console, the Console Browser check-up) its own write boundary as well. In this version ConsoleSessions is open and
//              every write boundary is closed, so only backup-image sessions open; the window asks the same function before it starts a
//              task (KindOf: the worker request's kind).
//   OpenReal   Refusal first, before any USB device is listed or opened. Then: exactly one console in
//              FEL mode, the image loaded into its RAM by the first-party FEL loader (CcsConsole.RamBoot, Hakchi2 CE's memboot sequence),
//              the clovershell link the image starts (the same USB identity and Windows driver as FEL), and the image's own id checked.
//              The physical step (USB in with POWER off, hold RESET, POWER on, hold about 5 s) is the owner's; nothing here can do it.
//   OpenSimulated   a SimulatedConsole folder "loaded" with the same image id and profile.
using CcsConsole.RamBoot;
using CcsConsole.Usb;
using CcsWorker.Shell;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace CcsBackup.Device
{
    public enum ImageKind { Backup, Restore, Prepare, Return, Rescue, Files, Method, Firmware, Community }   // Return, Method and Community: the preparation image (its ccsprep has the return and method steps, ccscomm the community steps), each under its own boundary; Rescue: ccs-rescue (ccsrescue)

    public static class RecoveryImages
    {
        public static string Variant(ImageKind k) => k == ImageKind.Backup ? "ccs-backup" : k == ImageKind.Restore ? "ccs-restore" : k == ImageKind.Rescue ? "ccs-rescue" : k == ImageKind.Files ? "ccs-files" : "ccs-prepare";
        public static SessionProfile ProfileOf(ImageKind k) => k == ImageKind.Backup ? SessionProfile.Backup : k == ImageKind.Restore ? SessionProfile.Restore : k == ImageKind.Rescue ? SessionProfile.Rescue : k == ImageKind.Files ? SessionProfile.Files : SessionProfile.Prepare;

        /// <summary>every console program comes from this shared engine's own resources (the same bytes in the standalone Workbench and in
        /// Classic Cart Studio); nothing is read from a host or from disk</summary>
        static byte[] Resource(string name)
        {
            var asm = typeof(RecoveryImages).Assembly;
            using (var s = asm.GetManifestResourceStream(name)) { if (s != null) { var m = new MemoryStream(); s.CopyTo(m); return m.ToArray(); } }
            return null;
        }
        public static bool Embedded(ImageKind k) => Resource("ram/" + Variant(k) + "/MANIFEST.json") != null;

        public static RamImage Load(ImageKind k)
        {
            var v = Variant(k);
            if (!Embedded(k)) throw new RamImageException("The " + v + " console program is not part of this build.");
            var img = RamImage.FromSource("embedded " + v, n => Resource("ram/" + v + "/" + n));
            if (img.Variant != v) throw new RamImageException("The embedded console program is not " + v + ".");
            return img;
        }

        /// <summary>the SHA-256 of an install-method package (ccs-mod, ccs-nucleus) the preparation image carries (its MANIFEST.json
        /// "methods"; build_images.py pins them); null when this build's image does not carry it</summary>
        public static string MethodSha(string package)
        {
            if (!Engine.Methods.IsPackage(package)) return null;
            var b = Resource("ram/ccs-prepare/MANIFEST.json"); if (b == null) return null;
            var s = Json.Parse(Encoding.UTF8.GetString(b)).O("methods")?.O(package)?.S("sha256");
            return Hex.IsHex(s, 64) ? s : null;
        }

        /// <summary>a file of the release-built CCS HOME package this build carries (ram/ccs-home/: tools/console-nucleus/build_home_package.py;
        /// Engine/HomePackage checks each against its layout's MANIFEST.json; sample.json lists HOME Designer's preview-only sample games, which
        /// no layout names, so they are never sent to a console); null when this build does not carry it</summary>
        public static byte[] HomeResource(string name) => System.Text.RegularExpressions.Regex.IsMatch(name ?? "", "^((mod|nucleus)\\.MANIFEST\\.json|[0-9a-f]{64}\\.blob|HOME\\.json|sample\\.json)$") ? Resource("ram/ccs-home/" + name) : null;

        /// <summary>the image's id (MANIFEST.json image_id), which the console reports back once the image runs</summary>
        public static (string id, string sha256) IdOf(ImageKind k)
        {
            var v = Variant(k);
            if (!Embedded(k)) return ("not-built:" + v, null);
            var m = Json.Parse(Encoding.UTF8.GetString(Resource("ram/" + v + "/MANIFEST.json")));
            var img = m.S("image"); var sha = m.O("outputs")?.O(img ?? "")?.S("sha256");
            return (m.S("image_id"), sha);
        }
    }

    public static class RecoverySessions
    {
        public const int ClovershellWaitMs = 150000;

        public static RecoveryConsole OpenSimulated(string folder, ImageKind kind)
        {
            var id = RecoveryImages.IdOf(kind).id;
            SimulatedConsole.Boot(folder, id, RecoveryImages.ProfileOf(kind));
            return new RecoveryConsole(new SimulatedConsole(folder), RecoveryImages.ProfileOf(kind), true);
        }

        /// <summary>the kind of console session a worker request opens (WorkerHost's dispatch; the window asks it too)</summary>
        public static ImageKind KindOf(string cmd) => cmd == "backup" || cmd == "browse" ? ImageKind.Backup : cmd == "prepare-plan" ? ImageKind.Prepare : cmd == "return-plan" ? ImageKind.Return
            : cmd == "method-plan" ? ImageKind.Method : cmd == "firmware-plan" ? ImageKind.Firmware : cmd == "community" ? ImageKind.Community : cmd == "rescue" ? ImageKind.Rescue
            : cmd == "files" || cmd == "compose" || cmd == "home-design" || cmd == "sega-pictures" ? ImageKind.Files : ImageKind.Restore;

        /// <summary>why a session of this kind cannot be opened on a real console in this version (the closed boundary's own sentence), or
        /// null when it can: ConsoleSessions, then the kind's own write boundary (a backup-image session needs none: its image has no write
        /// code). Reads nothing and touches no device.</summary>
        public static GateClosedException Refusal(ImageKind kind)
        {
            if (!CcsBackup.Gate.ConsoleSessions) return new GateClosedException(CcsBackup.Gate.SessionsText, "open a console session (" + RecoveryImages.Variant(kind) + ")");
            if (kind == ImageKind.Restore && !CcsBackup.Gate.RestoreWrites) return new GateClosedException(CcsBackup.Gate.RestoreText, "open a restore session");
            if (kind == ImageKind.Prepare && !CcsBackup.Gate.PreparationWrites) return new GateClosedException(CcsBackup.Gate.PreparationText, "open a preparation session");
            if (kind == ImageKind.Return && !CcsBackup.Gate.ReturnWrites) return new GateClosedException(CcsBackup.Gate.ReturnText, "open a return session");
            if (kind == ImageKind.Method && !CcsBackup.Gate.MethodWrites) return new GateClosedException(CcsBackup.Gate.MethodText, "open a method session");
            if (kind == ImageKind.Firmware && !CcsBackup.Gate.FirmwareWrites) return new GateClosedException(CcsBackup.Gate.FirmwareText, "open a session to add a system image");
            if (kind == ImageKind.Community && !CcsBackup.Gate.CommunityWrites) return new GateClosedException(CcsBackup.Gate.CommunityText, "open a community mods session");
            if (kind == ImageKind.Rescue && !CcsBackup.Gate.RescueWrites) return new GateClosedException(CcsBackup.Gate.RescueText, "open a rescue session");
            if (kind == ImageKind.Files && !CcsBackup.Gate.ExpertWrites && !CcsBackup.Gate.DeveloperWrites && !CcsBackup.Gate.ComposerWrites && !CcsBackup.Gate.CleanupWrites) return new GateClosedException(CcsBackup.Gate.ExpertText, "open a file session");
            if (kind != ImageKind.Backup && kind != ImageKind.Restore && kind != ImageKind.Prepare && kind != ImageKind.Return && kind != ImageKind.Method && kind != ImageKind.Firmware
                && kind != ImageKind.Community && kind != ImageKind.Rescue && kind != ImageKind.Files) return new GateClosedException(CcsBackup.Gate.SessionsText, "an unknown session kind");
            return null;
        }

        /// <summary>FEL RAM boot of the embedded image, then the clovershell link. say: progress text; cancelled: stop waiting.</summary>
        public static RecoveryConsole OpenReal(ImageKind kind, Action<string> say, Func<bool> cancelled)
        {
            var closed = Refusal(kind);
            if (closed != null) throw closed;   // before any USB device is listed or opened
            var img = RecoveryImages.Load(kind); var want = RecoveryImages.IdOf(kind).id;
            var loader = new RamBootLoader(new LibUsbDeviceList(), () => new LibUsbFelUsb());
            loader.Progress += p => { if (p.Message != null) say?.Invoke(p.Message); };
            var res = loader.Boot(img);
            if (res.Error != null) throw new ConsoleRefusedException("fel", "The console program could not be sent (" + res.Error + "). Nothing was read or written.");
            say?.Invoke("Waiting for the console program to start...");
            var until = DateTime.UtcNow.AddMilliseconds(ClovershellWaitMs); Exception last = null;
            while (DateTime.UtcNow < until)
            {
                if (cancelled != null && cancelled()) throw new OperationCanceledException();
                try
                {
                    var shell = new ClovershellShell(new LibUsbBulkPipe());
                    var con = new RecoveryConsole(shell, RecoveryImages.ProfileOf(kind), false);
                    var id = con.Identify();
                    if (id.Image != want) { con.Disconnect(); throw new ConsoleRefusedException("wrong-image", "The console is running a different program than the one just sent. Unplug the USB cable and start again."); }
                    return con;
                }
                catch (ShellException ex) { last = ex; Thread.Sleep(2000); }
            }
            throw new ConsoleRefusedException("no-link", "The console did not answer after its program was sent (" + (last?.Message ?? "no connection") + "). Unplug the USB cable: nothing was written.");
        }
    }
}
