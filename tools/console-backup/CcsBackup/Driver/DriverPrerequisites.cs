// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// What this PC needs before Workbench's own USB driver package can be set up, read before the setup page is shown (reads only: this
// process's own permission and processor, and Windows' policy settings in the registry; nothing is changed). Each need says plainly whether
// it is met, will be asked for, may stop the setup, or is not met; a need that is not met is a real requirement of this PC, named exactly,
// and the page offers no setup that could only fail. Workbench never changes a policy, a trust setting or driver signature enforcement.
//   Administrator permission  this person's account: already elevated, asked to confirm, or an administrator's name and password needed
//   This PC's processor       Windows sets up drivers only from a program made for the PC's own processor (a 32-bit or emulated copy cannot)
//   Device installation       Windows' Device Installation Restrictions policy (HKLM\SOFTWARE\Policies\Microsoft\Windows\DeviceInstall\
//                             Restrictions): a rule that names the console's USB identity, its device class (USBDevice), removable devices,
//                             every device, or every device no other rule describes, stops the setup unless administrators may override it
//   Trust in the package      the package is signed by a certificate the setup makes for this PC and adds to its Trusted Root Certification
//                             Authorities and Trusted Publishers lists. When this PC lets only its organization's administrators decide which
//                             publishers are trusted (Trusted Publisher management, AuthenticodeFlags 2, under HKLM\SOFTWARE\Policies\Microsoft\
//                             SystemCertificates\TrustedPublisher\Safer), Windows does not accept that certificate: not met. When this PC
//                             trusts only root certificates from its organization's policy (ProtectedRoots Flags 0x8), Windows may refuse it.
//   Signature enforcement     not changed and not needed: the package holds no driver program; Windows' own WinUSB driver does the work
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CcsBackup.Driver
{
    public enum NeedState { Met, Asked, MayStop, NotMet }

    public sealed class Need
    {
        public string Title, Text; public NeedState State;
        public override string ToString() => Title + ": " + State + ": " + Text;
    }

    public sealed class Needs
    {
        public readonly List<Need> Items = new List<Need>();
        /// <summary>the first need that is not met: the setup is not offered, and this says why</summary>
        public Need Blocker => Items.FirstOrDefault(i => i.State == NeedState.NotMet);
    }

    /// <summary>Windows' policy settings, read only (the tests give their own)</summary>
    public interface IPolicyReader
    {
        bool KeyExists(string key);
        /// <summary>a DWORD value under HKLM\key, or null when it is not there</summary>
        int? Dword(string key, string name);
        /// <summary>every string value under HKLM\key (the lists of device IDs and classes are kept that way), or an empty list</summary>
        List<string> Strings(string key);
    }

    public sealed class RegistryPolicyReader : IPolicyReader
    {
        public static readonly RegistryPolicyReader Instance = new RegistryPolicyReader();
        public bool KeyExists(string key) { try { using (var k = Registry.LocalMachine.OpenSubKey(key, false)) return k != null; } catch (Exception) { return false; } }
        public int? Dword(string key, string name)
        {
            try { using (var k = Registry.LocalMachine.OpenSubKey(key, false)) { var v = k?.GetValue(name); return v is int i ? i : (int?)null; } }
            catch (Exception) { return null; }
        }
        public List<string> Strings(string key)
        {
            try { using (var k = Registry.LocalMachine.OpenSubKey(key, false)) return k == null ? new List<string>() : k.GetValueNames().Select(n => k.GetValue(n) as string).Where(x => x != null).ToList(); }
            catch (Exception) { return new List<string>(); }
        }
    }

    public static class DriverPrerequisites
    {
        public const string RestrictionsKey = @"SOFTWARE\Policies\Microsoft\Windows\DeviceInstall\Restrictions";
        public const string PublisherKey = @"SOFTWARE\Policies\Microsoft\SystemCertificates\TrustedPublisher\Safer";
        public const string RootsKey = @"SOFTWARE\Policies\Microsoft\SystemCertificates\Root\ProtectedRoots";
        /// <summary>the device setup class of Workbench's package (USBDevice)</summary>
        public const string PackageClass = "{88BAE032-5A81-49f0-BC3D-A4FF138216D6}";

        /// <summary>every need of this PC, read now (this process's permission and processor, Windows' policy settings)</summary>
        public static Needs ForThisPc(ElevationState e) => Check(e, WindowsSystemDriverApi.NativeProblem(), RegistryPolicyReader.Instance);

        /// <summary>the plain words for a need's state, as the setup page shows them</summary>
        public static string StateWords(NeedState s) => s == NeedState.Met ? "Met" : s == NeedState.Asked ? "Asked for during setup" : s == NeedState.MayStop ? "May stop the setup" : "Not met";

        /// <summary>every need, read now; notNative: why this process cannot set up drivers (null when it can)</summary>
        public static Needs Check(ElevationState e, string notNative, IPolicyReader pol)
        {
            var n = new Needs();
            void Add(string title, NeedState st, string text) => n.Items.Add(new Need { Title = title, State = st, Text = text });
            switch (e)
            {
                case ElevationState.Elevated: Add("Administrator permission", NeedState.Met, "Workbench already has administrator permission, so Windows does not ask again."); break;
                case ElevationState.StandardUser: Add("Administrator permission", NeedState.Asked, "Your Windows account is not an administrator: Windows asks for an administrator's name and password. Without them the setup does not run."); break;
                case ElevationState.CanElevate: Add("Administrator permission", NeedState.Asked, "Windows asks you to confirm, once, in its permission prompt."); break;
                default: Add("Administrator permission", NeedState.Asked, "Windows asks for administrator permission once."); break;
            }
            if (notNative == null) Add("A copy of Workbench made for this PC", NeedState.Met, "This copy of Workbench is made for this PC's processor.");
            else Add("A copy of Workbench made for this PC", NeedState.NotMet, "Windows sets up drivers only from a program made for the PC's own processor, and this copy of Workbench is " + notNative + ". Use the CCS Console Workbench download made for this PC (Arm64 for an Arm-based PC, x64 for others).");
            Add("Device installation allowed", Devices(pol, out var dt), dt);
            Add("Windows accepts the setup's certificate", Trust(pol, out var tt), tt);
            Add("Driver signature enforcement and Secure Boot", NeedState.Met, "Not changed and not needed: the package holds no driver program of its own. Windows' own WinUSB driver, which Microsoft signs, does the work.");
            return n;
        }

        static bool Listed(IEnumerable<string> list, string id) => list.Any(x =>
        {
            var v = (x ?? "").Trim();
            return string.Equals(v, id, StringComparison.OrdinalIgnoreCase) || v.StartsWith(id + "&REV_", StringComparison.OrdinalIgnoreCase);
        });

        static NeedState Devices(IPolicyReader p, out string text)
        {
            const string K = RestrictionsKey;
            if (!p.KeyExists(K)) { text = "No Windows policy on this PC restricts setting up devices."; return NeedState.Met; }
            bool admins = p.Dword(K, "AllowAdminInstall") == 1;
            var why = new List<string>();
            if (p.Dword(K, "DenyAll") == 1) why.Add("it does not allow setting up any device");
            if (p.Dword(K, "DenyDeviceIDs") == 1 && Listed(p.Strings(K + @"\DenyDeviceIDs"), PinnedDriver.HardwareId)) why.Add("it names the console's USB identity (" + PinnedDriver.HardwareId + ")");
            if (p.Dword(K, "DenyDeviceClasses") == 1 && p.Strings(K + @"\DenyDeviceClasses").Any(x => string.Equals((x ?? "").Trim(), PackageClass, StringComparison.OrdinalIgnoreCase)))
                why.Add("it names the class of USB devices Workbench's driver package belongs to");
            if (p.Dword(K, "DenyRemovableDevices") == 1) why.Add("it does not allow setting up removable devices, and the console is one");
            if (p.Dword(K, "DenyUnspecified") == 1 && !(p.Dword(K, "AllowDeviceIDs") == 1 && Listed(p.Strings(K + @"\AllowDeviceIDs"), PinnedDriver.HardwareId)))
                why.Add("it allows only devices it names, and it does not name the console");
            if (why.Count > 0 && !admins)
            {
                text = "A device installation policy set by whoever manages this PC stops this setup: " + string.Join("; ", why) + ". Workbench changes no policy. Someone who manages this PC can allow the console's USB identity " + PinnedDriver.HardwareId + ", or set up a driver for it that this PC allows.";
                return NeedState.NotMet;
            }
            if (why.Count > 0) { text = "A device installation policy on this PC would stop it (" + string.Join("; ", why) + "), but it lets administrators set up devices anyway. Windows may still refuse the console when it is connected; if it does, Workbench says what Windows reported."; return NeedState.MayStop; }
            text = "This PC has device installation rules set by whoever manages it. None of them names the console, but Windows may still refuse the setup; if it does, Workbench says what Windows reported and changes nothing else.";
            return NeedState.MayStop;
        }

        static NeedState Trust(IPolicyReader p, out string text)
        {
            if ((p.Dword(PublisherKey, "AuthenticodeFlags") ?? 0) == 2)
            {
                text = "Not met: this PC lets only its organization's administrators decide which publishers Windows trusts (its Trusted Publisher management setting). Windows would not accept the certificate Workbench's setup makes for its driver package, so the setup is not offered. Workbench changes no trust setting. Someone who manages this PC can set up a driver for the console that this PC accepts.";
                return NeedState.NotMet;
            }
            if (((p.Dword(RootsKey, "Flags") ?? 0) & 0x8) != 0)
            {
                text = "This PC may trust only root certificates its organization's policy provides. The setup adds a certificate it makes for this PC; if Windows does not accept it, Workbench takes it back and says what Windows reported.";
                return NeedState.MayStop;
            }
            text = "The setup adds a certificate it makes for this PC to its Trusted Root Certification Authorities and Trusted Publishers lists, and Windows checks the package with it before setting it up. If Windows does not accept it, Workbench takes the certificate back and says what Windows reported.";
            return NeedState.Asked;
        }
    }
}
