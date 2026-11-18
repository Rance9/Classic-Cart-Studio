// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// This PC's USB driver state, read only. Every call here is a query: SetupDiGetClassDevs / SetupDiEnumDeviceInfo / SetupDiGetDevice*Property
// (device instances, present or not, their service and bound INF), SetupDiOpenDevRegKey with KEY_READ (Device Parameters\DeviceInterfaceGUIDs;
// it opens, never creates), CM_Get_DevNode_Status (present, problem, waiting for restart), the INF folder's oemNN.inf files and
// SetupGetInfDriverStoreLocation (each package's original name, its catalog), the catalog's signer read as data (SignedCms; no trust decision,
// nothing installed), the Local Machine Root and Trusted Publishers stores opened ReadOnly | OpenExistingOnly, three registry values for a
// pending restart and GetTickCount64 (when Windows last started). No elevation is needed or asked for. Nothing is installed, staged, bound,
// removed or written.
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace CcsBackup.Driver
{
    public sealed class WindowsDriverStore : IDriverStore
    {
        public const int MaxCatalogBytes = 32 * 1000 * 1000;
        public string Kind => "windows";
        static readonly string InfDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "INF");
        static readonly string CatRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "CatRoot", "{F750E6C3-38EE-11D1-85E5-00C04FC295EE}");

        string pcId;
        /// <summary>a hash of this PC's MachineGuid (never the value itself), so a record carried to another PC is never compared with it</summary>
        public string PcId
        {
            get
            {
                if (pcId != null) return pcId;
                using (var b = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var k = b.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography", false))
                {
                    var g = k?.GetValue("MachineGuid") as string;
                    if (string.IsNullOrEmpty(g)) throw new InvalidOperationException("this PC's identity could not be read");
                    using (var h = SHA256.Create()) return pcId = "pc-" + Hex.Of(h.ComputeHash(Encoding.UTF8.GetBytes("ccs-usb-driver-record/1:" + g.ToLowerInvariant()))).Substring(0, 24);
                }
            }
        }

        // ------------------------------------------------------------------ devices
        public IReadOnlyList<DeviceBinding> Devices(ushort vid, ushort pid)
        {
            var list = new List<DeviceBinding>();
            ForEachDevice("USB", (set, data) =>
            {
                var ids = MultiSz(set, ref data, SPDRP_HARDWAREID);
                if (!ids.Any(h => UsbId.Matches(h, vid, pid))) return;
                var d = new DeviceBinding { InstanceId = InstanceId(set, ref data), HardwareIds = ids, Service = Sz(set, ref data, SPDRP_SERVICE) ?? "",
                    InfPath = Prop(set, ref data, DriverInfPath) ?? "", Provider = Prop(set, ref data, DriverProvider) ?? "", DriverVersion = Prop(set, ref data, DriverVersion) ?? "" };
                var cr = CM_Get_DevNode_Status(out var status, out var problem, data.DevInst, 0);
                d.Present = cr == CR_SUCCESS;
                if (d.Present) { d.ProblemCode = (status & DN_HAS_PROBLEM) != 0 ? (int)problem : 0; d.NeedsRestart = (status & DN_NEED_RESTART) != 0 || problem == CM_PROB_NEED_RESTART && (status & DN_HAS_PROBLEM) != 0; }
                d.InterfaceGuids = InterfaceGuids(set, ref data);
                list.Add(d);
            });
            return list;
        }

        public IReadOnlyList<string> DevicesUsing(string publishedName)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(publishedName)) return list;
            ForEachDevice(null, (set, data) => { if (PinnedDriver.Eq(Prop(set, ref data, DriverInfPath), publishedName)) list.Add(InstanceId(set, ref data)); });
            return list;
        }

        // ------------------------------------------------------------------ packages
        public IReadOnlyList<DriverPackage> Packages(ushort vid, ushort pid)
        {
            var list = new List<DriverPackage>();
            foreach (var f in Directory.GetFiles(InfDir, "oem*.inf"))
            {
                var name = Path.GetFileName(f); if (!UsbId.IsPublishedName(name)) continue;
                InfInfo info; byte[] bytes;
                try { bytes = ReadBounded(f, Inf.MaxBytes); info = Inf.Parse(Inf.Decode(bytes)); } catch (FormatException) { continue; } catch (IOException) { continue; } catch (UnauthorizedAccessException) { continue; }
                if (info.UsbIds.Any(h => UsbId.Matches(h, vid, pid))) list.Add(Describe(name, info));
            }
            return list;
        }

        public DriverPackage Package(string publishedName)
        {
            if (string.IsNullOrEmpty(publishedName) || publishedName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !publishedName.EndsWith(".inf", StringComparison.OrdinalIgnoreCase)) return null;
            var f = Path.Combine(InfDir, publishedName);
            if (!File.Exists(f)) return null;
            var info = Inf.Parse(Inf.Decode(ReadBounded(f, Inf.MaxBytes)));
            return Describe(publishedName, info);
        }

        DriverPackage Describe(string published, InfInfo info)
        {
            var p = new DriverPackage { PublishedName = published, Provider = info.Provider, ClassGuid = info.ClassGuid, DriverVersion = info.DriverVer, DeclaredIds = info.UsbIds,
                InterfaceGuids = info.InterfaceGuids, Services = info.Services, CatalogName = info.CatalogFile };
            var published_ = Path.Combine(InfDir, published);
            string storeInf = UsbId.IsPublishedName(published) ? DriverStorePath(published_) : null;
            p.OriginalName = storeInf != null ? Path.GetFileName(storeInf) : published;
            p.InfSha256 = Sha(storeInf ?? published_, Inf.MaxBytes);
            var cat = Catalog(published, storeInf, info.CatalogFile);
            if (cat != null)
            {
                p.CatalogSha256 = Sha(cat, MaxCatalogBytes);
                var (subject, thumb) = Signer(cat);
                p.SignerSubject = subject ?? ""; p.SignerThumbprint = thumb ?? "";
            }
            return p;
        }

        static string Catalog(string published, string storeInf, string catalogName)
        {
            if (storeInf != null && !string.IsNullOrEmpty(catalogName) && catalogName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
            { var c = Path.Combine(Path.GetDirectoryName(storeInf), catalogName); if (File.Exists(c)) return c; }
            if (UsbId.IsPublishedName(published)) { var c = Path.Combine(CatRoot, Path.ChangeExtension(published, ".cat")); if (File.Exists(c)) return c; }
            return null;
        }

        static string DriverStorePath(string publishedPath)
        {
            var sb = new StringBuilder(1024);
            return SetupGetInfDriverStoreLocationW(publishedPath, IntPtr.Zero, null, sb, sb.Capacity, out _) ? sb.ToString() : null;
        }

        /// <summary>the catalog's first signer certificate, read from the PKCS #7 data (null when it cannot be read)</summary>
        static (string subject, string thumbprint) Signer(string cat)
        {
            try
            {
                var cms = new SignedCms(); cms.Decode(ReadBounded(cat, MaxCatalogBytes));
                var c = cms.SignerInfos.Count > 0 ? cms.SignerInfos[0].Certificate : null;
                return c == null ? (null, null) : (c.Subject, UsbId.NormThumbprint(c.Thumbprint));
            }
            catch { return (null, null); }
        }

        public SignerScan PackagesSignedBy(string thumbprint)
        {
            var scan = new SignerScan(); thumbprint = UsbId.NormThumbprint(thumbprint);
            foreach (var f in Directory.GetFiles(InfDir, "oem*.inf"))
            {
                var name = Path.GetFileName(f); if (!UsbId.IsPublishedName(name)) continue;
                try
                {
                    var info = Inf.Parse(Inf.Decode(ReadBounded(f, Inf.MaxBytes)));
                    var cat = Catalog(name, DriverStorePath(f), info.CatalogFile);
                    if (cat == null) { if (!string.IsNullOrEmpty(info.CatalogFile)) scan.Complete = false; continue; }
                    var (_, t) = Signer(cat);
                    if (t == null) { scan.Complete = false; continue; }
                    if (t == thumbprint) scan.Packages.Add(name);
                }
                catch { scan.Complete = false; }
            }
            return scan;
        }

        // ------------------------------------------------------------------ certificates and restart
        public IReadOnlyList<CertificateEntry> Certificates(CertStore store)
        {
            var list = new List<CertificateEntry>();
            using (var s = new X509Store(store == CertStore.Root ? StoreName.Root : StoreName.TrustedPublisher, StoreLocation.LocalMachine))
            {
                s.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
                foreach (var c in s.Certificates)
                {
                    try { list.Add(new CertificateEntry { Store = store, Thumbprint = UsbId.NormThumbprint(c.Thumbprint), Subject = c.Subject, FriendlyName = c.FriendlyName ?? "",
                        NotAfterUtc = c.NotAfter.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"), HasPrivateKey = c.HasPrivateKey }); }
                    finally { c.Reset(); }
                }
            }
            return list;
        }

        public RestartState Restart()
        {
            var r = new RestartState();
            using (var b = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            {
                using (var k = b.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending", false)) if (k != null) r.Reasons.Add("Windows component servicing");
                using (var k = b.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired", false)) if (k != null) r.Reasons.Add("Windows Update");
                using (var k = b.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager", false))
                    if (k?.GetValue("PendingFileRenameOperations") is string[] ops && ops.Any(o => !string.IsNullOrEmpty(o))) r.Reasons.Add("files waiting to be replaced");
            }
            r.SystemPending = r.Reasons.Count > 0;
            r.BootUtc = (DateTime.UtcNow - TimeSpan.FromMilliseconds(GetTickCount64())).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");   // a restart gives a later one
            return r;
        }

        // ------------------------------------------------------------------ helpers (queries only)
        static byte[] ReadBounded(string f, int max)
        {
            using (var s = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (s.Length > max) throw new FormatException(Path.GetFileName(f) + " is larger than " + max + " bytes");
                var b = new byte[s.Length]; int got = 0; while (got < b.Length) { int n = s.Read(b, got, b.Length - got); if (n <= 0) break; got += n; }
                return b;
            }
        }
        static string Sha(string f, int max) { try { using (var h = SHA256.Create()) return Hex.Of(h.ComputeHash(ReadBounded(f, max))); } catch { return ""; } }

        static void ForEachDevice(string enumerator, Action<IntPtr, SP_DEVINFO_DATA> each)
        {
            var set = SetupDiGetClassDevsW(IntPtr.Zero, enumerator, IntPtr.Zero, DIGCF_ALLCLASSES);
            if (set == INVALID_HANDLE) throw new InvalidOperationException("the device list could not be read (error " + Marshal.GetLastWin32Error() + ")");
            try
            {
                for (int i = 0; i < 100000; i++)
                {
                    var data = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf(typeof(SP_DEVINFO_DATA)) };
                    if (!SetupDiEnumDeviceInfo(set, i, ref data)) break;
                    each(set, data);
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
        }

        static string InstanceId(IntPtr set, ref SP_DEVINFO_DATA data) { var sb = new StringBuilder(512); return SetupDiGetDeviceInstanceIdW(set, ref data, sb, sb.Capacity, out _) ? sb.ToString() : ""; }

        static byte[] RegProp(IntPtr set, ref SP_DEVINFO_DATA data, int prop)
        {
            var buf = new byte[2048];
            if (SetupDiGetDeviceRegistryPropertyW(set, ref data, prop, out _, buf, buf.Length, out var need)) return Trim(buf, need);
            if (need <= buf.Length || need > 65536) return null;
            buf = new byte[need];
            return SetupDiGetDeviceRegistryPropertyW(set, ref data, prop, out _, buf, buf.Length, out need) ? Trim(buf, need) : null;
        }
        static byte[] Trim(byte[] b, int n) { var r = new byte[Math.Max(0, Math.Min(n, b.Length))]; Array.Copy(b, r, r.Length); return r; }
        static List<string> MultiSz(IntPtr set, ref SP_DEVINFO_DATA data, int prop) { var b = RegProp(set, ref data, prop); return b == null ? new List<string>() : Encoding.Unicode.GetString(b).Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries).ToList(); }
        static string Sz(IntPtr set, ref SP_DEVINFO_DATA data, int prop) { var b = RegProp(set, ref data, prop); return b == null ? null : Encoding.Unicode.GetString(b).TrimEnd('\0'); }

        static string Prop(IntPtr set, ref SP_DEVINFO_DATA data, DEVPROPKEY key)
        {
            var buf = new byte[1024];
            if (!SetupDiGetDevicePropertyW(set, ref data, ref key, out var type, buf, buf.Length, out var need, 0))
            {
                if (need <= buf.Length || need > 65536) return null;
                buf = new byte[need];
                if (!SetupDiGetDevicePropertyW(set, ref data, ref key, out type, buf, buf.Length, out need, 0)) return null;
            }
            return type == DEVPROP_TYPE_STRING ? Encoding.Unicode.GetString(buf, 0, Math.Min(need, buf.Length)).TrimEnd('\0') : null;
        }

        static List<string> InterfaceGuids(IntPtr set, ref SP_DEVINFO_DATA data)
        {
            var list = new List<string>();
            var h = SetupDiOpenDevRegKey(set, ref data, DICS_FLAG_GLOBAL, 0, DIREG_DEV, KEY_READ);   // opens Device Parameters; never creates it
            if (h == INVALID_HANDLE || h == IntPtr.Zero) return list;
            using (var k = RegistryKey.FromHandle(new SafeRegistryHandle(h, true)))
            {
                var v = k.GetValue("DeviceInterfaceGUIDs");
                var vals = v is string[] a ? a : v is string s1 ? new[] { s1 } : k.GetValue("DeviceInterfaceGUID") is string s2 ? new[] { s2 } : new string[0];
                foreach (var g in vals) if (Guid.TryParse(g, out var gg)) list.Add(gg.ToString("B").ToUpperInvariant());
            }
            return list;
        }

        // ------------------------------------------------------------------ declarations
        [StructLayout(LayoutKind.Sequential)] struct SP_DEVINFO_DATA { public int cbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }
        [StructLayout(LayoutKind.Sequential)] struct DEVPROPKEY { public Guid fmtid; public uint pid; public DEVPROPKEY(string g, uint p) { fmtid = new Guid(g); pid = p; } }
        static readonly DEVPROPKEY DriverVersion = new DEVPROPKEY("a8b865dd-2e3d-4094-ad97-e593a70c75d6", 3), DriverInfPath = new DEVPROPKEY("a8b865dd-2e3d-4094-ad97-e593a70c75d6", 5),
            DriverProvider = new DEVPROPKEY("a8b865dd-2e3d-4094-ad97-e593a70c75d6", 9);
        const int DIGCF_ALLCLASSES = 0x4, SPDRP_HARDWAREID = 0x1, SPDRP_SERVICE = 0x4, DICS_FLAG_GLOBAL = 1, DIREG_DEV = 1, KEY_READ = 0x20019;
        const uint DEVPROP_TYPE_STRING = 0x12, DN_NEED_RESTART = 0x100, DN_HAS_PROBLEM = 0x400, CM_PROB_NEED_RESTART = 14;
        const int CR_SUCCESS = 0;
        static readonly IntPtr INVALID_HANDLE = new IntPtr(-1);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr SetupDiGetClassDevsW(IntPtr classGuid, string enumerator, IntPtr parent, int flags);
        [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiEnumDeviceInfo(IntPtr set, int index, ref SP_DEVINFO_DATA data);
        [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool SetupDiGetDeviceInstanceIdW(IntPtr set, ref SP_DEVINFO_DATA data, StringBuilder id, int size, out int need);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool SetupDiGetDeviceRegistryPropertyW(IntPtr set, ref SP_DEVINFO_DATA data, int prop, out int regType, byte[] buf, int size, out int need);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool SetupDiGetDevicePropertyW(IntPtr set, ref SP_DEVINFO_DATA data, ref DEVPROPKEY key, out uint type, byte[] buf, int size, out int need, int flags);
        [DllImport("setupapi.dll", SetLastError = true)] static extern IntPtr SetupDiOpenDevRegKey(IntPtr set, ref SP_DEVINFO_DATA data, int scope, int profile, int keyType, int access);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool SetupGetInfDriverStoreLocationW(string file, IntPtr altPlatform, string locale, StringBuilder ret, int size, out int need);
        [DllImport("cfgmgr32.dll")] static extern int CM_Get_DevNode_Status(out uint status, out uint problem, uint devInst, int flags);
        [DllImport("kernel32.dll")] static extern ulong GetTickCount64();
    }
}
