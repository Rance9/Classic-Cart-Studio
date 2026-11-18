// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The system functions the elevated driver step (DriverActionHost) uses to change this PC, behind one interface so the step's own code runs
// unchanged in the tests against an in-memory PC. WindowsSystemDriverApi is the real one:
//   InstallDriverPackage    newdev DiInstallDriverW with flags 0: the package goes to the driver store and is used only where it is the
//                           better match (never DIIRFLAG_FORCE_INF); Windows' restart flag is returned, never acted on
//   UninstallDriverPackage  newdev DiUninstallDriverW on %windir%\INF\oemNN.inf (one published package; flags 0); before Windows 10 version
//                           1703, SetupUninstallOEMInfW without SUOI_FORCEDELETE (Windows refuses while a device uses it)
//   Add/RemoveCertificate   the Local Machine Root or Trusted Publishers store: one certificate added (public part only), or removed by its
//                           exact thumbprint; never a search by subject
//   VerifyCatalogTrust      WinVerifyTrust on the package (CcsCatalog.VerifyTrust; reads only)
//   NewWorkFolder           %windir%\Temp\ccs-usb-driver-<random>, created new (never an existing one) with access for Administrators and
//                           SYSTEM only, so nothing without administrator permission can change the package between its check and its install
// It can be made only through Open, which the closed DriverChanges boundary stops (Gate.cs): with the constant false the compiler leaves out
// every path that would construct it, and nothing else calls its Windows functions (the driver-gate test reads the compiled code to show it).
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;

namespace CcsBackup.Driver
{
    public sealed class SystemResult
    {
        public bool Ok; public int Error; public bool NeedsRestart;
        public static SystemResult Done(bool restart) => new SystemResult { Ok = true, NeedsRestart = restart };
        public static SystemResult Failed(int error) => new SystemResult { Ok = false, Error = error };
    }

    public interface ISystemDriverApi
    {
        string Kind { get; }                 // "windows" or "fake"
        bool Elevated { get; }               // this process has administrator permission now
        string NotNative { get; }            // null, or why this process cannot set up drivers on this PC (a 32-bit or emulated process)
        string NewWorkFolder();
        void DeleteWorkFolder(string folder);
        void AddCertificate(CertStore store, byte[] der, string friendlyName);
        bool RemoveCertificate(CertStore store, string thumbprint);
        int VerifyCatalogTrust(string infPath, string catalogPath);
        SystemResult InstallDriverPackage(string infPath);
        SystemResult UninstallDriverPackage(string publishedName);
    }

    public sealed class WindowsSystemDriverApi : ISystemDriverApi
    {
        WindowsSystemDriverApi() { }

        /// <summary>the only way to the real functions; refused while the DriverChanges boundary is closed</summary>
        internal static ISystemDriverApi Open()
        {
            if (!Gate.DriverChanges) throw new GateClosedException(Gate.DriverText, "the system driver functions");
            return new WindowsSystemDriverApi();
        }

        public string Kind => "windows";
        static string WinDir => Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        const string FolderPrefix = "ccs-usb-driver-";

        public bool Elevated
        {
            get
            {
                using (var id = WindowsIdentity.GetCurrent())
                    return GetTokenInformation(id.Token, TokenElevation, out var e, 4, out _) && e != 0;
            }
        }

        public string NotNative
        {
            get
            {
                ushort process, native;
                try { if (!IsWow64Process2(GetCurrentProcess(), out process, out native)) throw new EntryPointNotFoundException(); }
                catch (EntryPointNotFoundException) { return Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess ? "a 32-bit program on 64-bit Windows" : null; }   // before Windows 10 version 1511
                if (process != 0) return "a 32-bit program on 64-bit Windows";
                var mine = RuntimeInformation.ProcessArchitecture;
                if (native == IMAGE_FILE_MACHINE_ARM64 && mine != Architecture.Arm64) return "an x64 program on an Arm-based PC";
                if (native == IMAGE_FILE_MACHINE_AMD64 && mine != Architecture.X64) return "a program for another processor";
                return null;
            }
        }

        public string NewWorkFolder()
        {
            var path = Path.Combine(WinDir, "Temp", FolderPrefix + Guid.NewGuid().ToString("N"));
            if (!ConvertStringSecurityDescriptorToSecurityDescriptorW("O:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)", 1, out var sd, out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                var sa = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(), lpSecurityDescriptor = sd };
                if (!CreateDirectoryW(path, ref sa)) throw new Win32Exception(Marshal.GetLastWin32Error(), "the working folder could not be created new");
            }
            finally { LocalFree(sd); }
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("the working folder is not a plain folder");
            return path;
        }

        public void DeleteWorkFolder(string folder)
        {
            var full = Path.GetFullPath(folder ?? "");
            if (!PinnedDriver.Eq(Path.GetDirectoryName(full), Path.Combine(WinDir, "Temp")) || !Path.GetFileName(full).StartsWith(FolderPrefix, StringComparison.OrdinalIgnoreCase)) return;   // only its own folders
            if (Directory.Exists(full)) Directory.Delete(full, true);
        }

        public void AddCertificate(CertStore store, byte[] der, string friendlyName)
        {
            using (var s = new X509Store(store == CertStore.Root ? StoreName.Root : StoreName.TrustedPublisher, StoreLocation.LocalMachine))
            using (var c = new X509Certificate2(der))
            {
                if (c.HasPrivateKey) throw new InvalidOperationException("a certificate with a private key is never added");
                s.Open(OpenFlags.ReadWrite | OpenFlags.OpenExistingOnly);
                c.FriendlyName = friendlyName ?? "";
                s.Add(c);
            }
        }

        public bool RemoveCertificate(CertStore store, string thumbprint)
        {
            thumbprint = UsbId.NormThumbprint(thumbprint);
            if (!UsbId.IsThumbprint(thumbprint)) throw new ArgumentException("a certificate is removed only by its exact thumbprint");
            using (var s = new X509Store(store == CertStore.Root ? StoreName.Root : StoreName.TrustedPublisher, StoreLocation.LocalMachine))
            {
                s.Open(OpenFlags.ReadWrite | OpenFlags.OpenExistingOnly);
                var found = s.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false);
                try
                {
                    if (found.Count != 1 || UsbId.NormThumbprint(found[0].Thumbprint) != thumbprint) return false;
                    s.Remove(found[0]); return true;
                }
                finally { foreach (var c in found) c.Reset(); }
            }
        }

        public int VerifyCatalogTrust(string infPath, string catalogPath) => CcsCatalog.VerifyTrust(infPath, catalogPath);

        public SystemResult InstallDriverPackage(string infPath)
        {
            if (DiInstallDriverW(IntPtr.Zero, infPath, 0, out var reboot)) return SystemResult.Done(reboot);
            return SystemResult.Failed(Marshal.GetLastWin32Error());
        }

        public SystemResult UninstallDriverPackage(string publishedName)
        {
            if (!UsbId.IsPublishedName(publishedName)) throw new ArgumentException("only one published package (oemNN.inf) can be removed");
            var inf = Path.Combine(WinDir, "INF", publishedName);
            try
            {
                if (DiUninstallDriverW(IntPtr.Zero, inf, 0, out var reboot)) return SystemResult.Done(reboot);
                return SystemResult.Failed(Marshal.GetLastWin32Error());
            }
            catch (EntryPointNotFoundException)
            {   // before Windows 10 version 1703: refused by Windows while a device uses the package (never SUOI_FORCEDELETE)
                if (SetupUninstallOEMInfW(publishedName, 0, IntPtr.Zero)) return SystemResult.Done(false);
                return SystemResult.Failed(Marshal.GetLastWin32Error());
            }
        }

        // ------------------------------------------------------------------ declarations
        const int TokenElevation = 20;
        const ushort IMAGE_FILE_MACHINE_AMD64 = 0x8664, IMAGE_FILE_MACHINE_ARM64 = 0xAA64;
        [StructLayout(LayoutKind.Sequential)] struct SECURITY_ATTRIBUTES { public int nLength; public IntPtr lpSecurityDescriptor; public int bInheritHandle; }

        [DllImport("newdev.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool DiInstallDriverW(IntPtr hwndParent, string infPath, uint flags, [MarshalAs(UnmanagedType.Bool)] out bool needReboot);
        [DllImport("newdev.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool DiUninstallDriverW(IntPtr hwndParent, string infPath, uint flags, [MarshalAs(UnmanagedType.Bool)] out bool needReboot);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool SetupUninstallOEMInfW(string infFileName, uint flags, IntPtr reserved);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool GetTokenInformation(IntPtr token, int infoClass, out int info, int length, out int returned);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string sddl, int revision, out IntPtr sd, out int size);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CreateDirectoryW(string path, ref SECURITY_ATTRIBUTES sa);
        [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr p);
        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool IsWow64Process2(IntPtr process, out ushort processMachine, out ushort nativeMachine);
    }
}
