// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Reading the PC's USB driver state. IDriverStore only reads: no member may install, remove, bind, stage or change a driver, a device, a
// certificate or a setting. WindowsDriverStore reads this PC (SetupAPI, the INF folder, the Local Machine certificate stores, the registry,
// all opened read-only); SimulatedDriverStore is an in-memory PC for the tests and the simulated console (it is changed only by
// SimulatedDriverActions and by the tests themselves).
using System;
using System.Collections.Generic;
using System.Linq;

namespace CcsBackup.Driver
{
    public interface IDriverStore
    {
        string Kind { get; }                               // "windows" or "simulated"
        string PcId { get; }                               // a stable, non-identifying id of this PC (records made elsewhere are never compared)
        /// <summary>every device instance Windows knows for that USB identity, connected or not, and what each is bound to</summary>
        IReadOnlyList<DeviceBinding> Devices(ushort vid, ushort pid);
        /// <summary>the published third-party driver packages (oemNN.inf) whose INF names that identity, staged or in use</summary>
        IReadOnlyList<DriverPackage> Packages(ushort vid, ushort pid);
        /// <summary>one package by its published name (oemNN.inf, or an inbox INF such as winusb.inf); null when it is not there</summary>
        DriverPackage Package(string publishedName);
        /// <summary>every device instance (any identity, connected or not) bound to that published package</summary>
        IReadOnlyList<string> DevicesUsing(string publishedName);
        /// <summary>the whole Local Machine store (Root or Trusted Publishers)</summary>
        IReadOnlyList<CertificateEntry> Certificates(CertStore store);
        /// <summary>every published package whose catalog is signed by that certificate (Complete false when some catalog could not be read)</summary>
        SignerScan PackagesSignedBy(string thumbprint);
        RestartState Restart();
    }

    /// <summary>An in-memory PC: devices, published packages, the two certificate stores and the restart flags. Nothing outside this object
    /// is ever touched. FailReads makes every read throw (a PC whose state cannot be read).</summary>
    public sealed class SimulatedDriverStore : IDriverStore
    {
        public readonly List<DeviceBinding> DeviceList = new List<DeviceBinding>();
        public readonly List<DriverPackage> PackageList = new List<DriverPackage>();
        public readonly List<CertificateEntry> CertificateList = new List<CertificateEntry>();
        public readonly RestartState RestartFlags = new RestartState { BootUtc = DateTime.UtcNow.AddHours(-2).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'") };
        public string Pc = "sim-pc-0001";
        public bool FailReads;
        public int Reads { get; private set; }
        public string Kind => "simulated";
        public string PcId { get { Read(); return Pc; } }

        void Read() { Reads++; if (FailReads) throw new InvalidOperationException("the simulated PC's driver list cannot be read"); }

        public IReadOnlyList<DeviceBinding> Devices(ushort vid, ushort pid) { Read(); return DeviceList.Where(d => d.HardwareIds.Any(h => UsbId.Matches(h, vid, pid))).ToList(); }
        public IReadOnlyList<DriverPackage> Packages(ushort vid, ushort pid) { Read(); return PackageList.Where(p => !p.IsInbox && p.DeclaredIds.Any(h => UsbId.Matches(h, vid, pid))).ToList(); }
        public DriverPackage Package(string publishedName) { Read(); return PackageList.FirstOrDefault(p => PinnedDriver.Eq(p.PublishedName, publishedName)); }
        public IReadOnlyList<string> DevicesUsing(string publishedName) { Read(); return DeviceList.Where(d => PinnedDriver.Eq(d.InfPath, publishedName)).Select(d => d.InstanceId).ToList(); }
        public IReadOnlyList<CertificateEntry> Certificates(CertStore store) { Read(); return CertificateList.Where(c => c.Store == store).ToList(); }
        public bool IncompleteSignerScan;               // some catalog "could not be read": sharing cannot be demonstrated
        public SignerScan PackagesSignedBy(string thumbprint)
        { Read(); return new SignerScan { Packages = PackageList.Where(p => !p.IsInbox && PinnedDriver.Eq(p.SignerThumbprint, thumbprint)).Select(p => p.PublishedName).ToList(), Complete = !IncompleteSignerScan }; }
        public RestartState Restart() { Read(); return new RestartState { SystemPending = RestartFlags.SystemPending, Reasons = RestartFlags.Reasons.ToList(), BootUtc = RestartFlags.BootUtc }; }

        /// <summary>Windows restarts (tests): removals waiting for it finish, devices stop waiting, and Windows' start time is now (a second
        /// restart of the same simulated PC within two minutes reads as the same start, as RestartState.RestartedSince allows for clock changes)</summary>
        public void SimulateRestart(bool finishRemovals = true)
        {
            foreach (var p in PackageList.Where(x => x.PendingRemoval && finishRemovals).ToList())
            {
                PackageList.Remove(p);
                foreach (var d in DeviceList.Where(d => PinnedDriver.Eq(d.InfPath, p.PublishedName))) { d.InfPath = ""; d.Service = ""; d.Provider = ""; d.InterfaceGuids.Clear(); }
            }
            foreach (var d in DeviceList) d.NeedsRestart = false;
            RestartFlags.SystemPending = false; RestartFlags.Reasons.Clear();
            RestartFlags.BootUtc = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
        }

        // ---- building a simulated PC (tests)
        int serial = 1;
        public string NextPublishedName() { var n = 100 + PackageList.Count(p => !p.IsInbox) + serial++; while (PackageList.Any(p => PinnedDriver.Eq(p.PublishedName, "oem" + n + ".inf"))) n++; return "oem" + n + ".inf"; }
        public static string FakeHash(string seed) { using (var h = System.Security.Cryptography.SHA256.Create()) return Hex.Of(h.ComputeHash(System.Text.Encoding.UTF8.GetBytes(seed))); }
        public static string FakeThumbprint(string seed) => FakeHash("cert:" + seed).Substring(0, 40).ToUpperInvariant();

        /// <summary>the package Hakchi2 CE 3.9.3's driver setup stages (libwdi WinUSB, nintendo_classic.inf, the pinned GUID), signed by signer</summary>
        public DriverPackage AddPinnedPackage(string signerThumbprint, string seed = null)
        {
            var name = NextPublishedName(); seed = seed ?? name;
            var p = new DriverPackage { PublishedName = name, OriginalName = PinnedDriver.InfName, Provider = PinnedDriver.Provider, ClassGuid = PinnedDriver.ClassGuid, DriverVersion = "6.1.7600.16385",
                DeclaredIds = { PinnedDriver.HardwareId }, InterfaceGuids = { PinnedDriver.InterfaceGuid }, Services = { PinnedDriver.Service }, InfSha256 = FakeHash("inf:" + seed),
                CatalogName = "nintendo_classic.cat", CatalogSha256 = FakeHash("cat:" + seed), SignerSubject = PinnedDriver.CertificateSubject, SignerThumbprint = signerThumbprint };
            PackageList.Add(p); return p;
        }
        public DriverPackage AddPackage(string original, string provider, string service, string guid, string signerThumbprint, params string[] ids)
        {
            var name = NextPublishedName();
            var p = new DriverPackage { PublishedName = name, OriginalName = original, Provider = provider, ClassGuid = PinnedDriver.ClassGuid, DriverVersion = "1.0.0.0",
                DeclaredIds = ids.ToList(), InterfaceGuids = guid == null ? new List<string>() : new List<string> { guid }, Services = { service }, InfSha256 = FakeHash("inf:" + name),
                CatalogName = System.IO.Path.ChangeExtension(original, ".cat"), CatalogSha256 = FakeHash("cat:" + name), SignerSubject = "CN=" + provider, SignerThumbprint = signerThumbprint };
            PackageList.Add(p); return p;
        }
        /// <summary>Windows' own winusb.inf (inbox: never a published oemNN.inf)</summary>
        public DriverPackage InboxWinUsb()
        {
            var p = PackageList.FirstOrDefault(x => PinnedDriver.Eq(x.PublishedName, "winusb.inf"));
            if (p == null) PackageList.Add(p = new DriverPackage { PublishedName = "winusb.inf", OriginalName = "winusb.inf", Provider = "Microsoft", ClassGuid = PinnedDriver.ClassGuid, Services = { "WinUSB" }, InfSha256 = FakeHash("inbox-winusb") });
            return p;
        }
        public CertificateEntry AddCertificate(CertStore store, string thumbprint, string subject, string friendly = "")
        {
            var c = new CertificateEntry { Store = store, Thumbprint = thumbprint, Subject = subject, FriendlyName = friendly, NotAfterUtc = "2100-01-01T01:00:00Z" };
            CertificateList.Add(c); return c;
        }
        public DeviceBinding AddDevice(string instanceSuffix, bool present, ushort vid = PinnedDriver.Vid, ushort pid = PinnedDriver.Pid)
        {
            var id = UsbId.Of(vid, pid);
            var d = new DeviceBinding { InstanceId = id + "\\" + instanceSuffix, HardwareIds = { id + "&REV_0100", id }, Present = present, Service = "", InfPath = "" };
            DeviceList.Add(d); return d;
        }
        public void Bind(DeviceBinding d, DriverPackage p, bool withInterface = true)
        {
            d.InfPath = p.PublishedName; d.Service = p.Services.FirstOrDefault() ?? ""; d.Provider = p.Provider; d.DriverVersion = p.DriverVersion;
            d.InterfaceGuids = withInterface ? p.InterfaceGuids.ToList() : new List<string>();
        }
    }
}
