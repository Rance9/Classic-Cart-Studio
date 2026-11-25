// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The elevated driver step: this same executable (the single-file standalone Workbench or Classic Cart Studio for Windows) started again by
// Windows with administrator permission ("runas") and "--driver-action <request>", before any window, writing nothing of its own outside a
// working folder it makes and removes. Both hosts call Run first thing (ccs-backup-utility/Program.cs, ClassicCartStudio/Program.cs).
// The request travels in the command line itself, never in a file: Windows starts the elevated process with exactly the arguments the window
// gave, and a program without administrator permission cannot change them afterward, so nothing can be swapped between the window and this
// step (Windows' permission prompt shows them under its details). The step still trusts the request for nothing it can check itself:
//   setup    only Workbench's own package (CcsDriverPackage.cs: fixed INF bytes, a new certificate) and only while the PC still has nothing for
//            the console's USB identity (DriverSetup.Assess, read again here). Order: the package is made and checked and its key destroyed;
//            the certificate goes to Root and Trusted Publishers; Windows' own trust check of the package; DiInstallDriver (never forced). A
//            failure takes back what this step added (its certificate by exact thumbprint, and only while no package signed by it is on the
//            PC) and returns Windows' error.
//   removal  only a package that is still exactly the recorded one (Workbench's INF bytes, the recorded catalog, signed by this operation's
//            certificate, used by no device the setup did not bind, not already being removed). After Windows' uninstall the PC is read again
//            (TrustCleanup.Check): a certificate goes (exact thumbprint, only this operation's, keyless) only when the package is read back as
//            gone, nothing says its removal waits for a restart, every package catalog was read and none is signed with it. Otherwise both
//            certificates stay and the exit code says the cleanup is deferred; nothing is reported finished that is not.
//   cleanup  later, certificates a removal kept: the same reading again inside this step (the package gone, a restart seen when one is
//            required, no signer user, the certificate exactly this operation's), or nothing is touched.
// The exit code is the result (DriverExit); the window never trusts it alone and reads the PC back. Run stops at once if the DriverChanges
// boundary is ever closed (Gate.cs: open in this version), before reading the request or the PC.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;

namespace CcsBackup.Driver
{
    public sealed class DriverActionRequest
    {
        public const string Schema = "ccs-usb-driver-action/1";
        public const int MaxEncoded = 16000;
        public string Action, OperationId;      // "setup", "remove" or "cleanup"
        public DriverSetupPlan Plan;
        public RemovalPlan Removal;
        public CleanupPlan Cleanup;

        /// <summary>the plan without its display texts (the step needs only the identity)</summary>
        public static DriverActionRequest ForSetup(DriverSetupPlan p)
        {
            var copy = DriverSetupPlan.From(Json.Parse(Json.Write(p.ToJson()))); copy.Changes.Clear();
            return new DriverActionRequest { Action = "setup", OperationId = p.OperationId, Plan = copy };
        }
        public static DriverActionRequest ForRemoval(RemovalPlan r) => new DriverActionRequest { Action = "remove", OperationId = r.OperationId, Removal = r };
        public static DriverActionRequest ForCleanup(CleanupPlan c) => new DriverActionRequest { Action = "cleanup", OperationId = c.OperationId, Cleanup = c };

        public JObj ToJson() => new JObj { { "schema", Schema }, { "action", Action }, { "operation_id", OperationId },
            { "plan", Action == "setup" ? Plan?.ToJson() : null }, { "removal", Action == "remove" ? Removal?.ToJson() : null }, { "cleanup", Action == "cleanup" ? Cleanup?.ToJson() : null } };

        /// <summary>one command-line argument (base64url of the JSON: letters, digits, '-' and '_' only, so no quoting can change it)</summary>
        public string Encode() => Convert.ToBase64String(Encoding.UTF8.GetBytes(Json.Write(ToJson()))).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        public static DriverActionRequest Decode(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length > MaxEncoded || s.Any(c => !(c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '-' || c == '_')))
                throw new FormatException("the driver request is not one encoded argument");
            var b = s.Replace('-', '+').Replace('_', '/'); b += new string('=', (4 - b.Length % 4) % 4);
            var d = Json.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(b)));
            if (d.S("schema") != Schema) throw new FormatException("not a driver request (" + (d.S("schema") ?? "no schema") + ")");
            var r = new DriverActionRequest { Action = d.S("action"), OperationId = d.S("operation_id"), Plan = DriverSetupPlan.From(d.O("plan")), Removal = RemovalPlan.From(d.O("removal")), Cleanup = CleanupPlan.From(d.O("cleanup")) };
            var p = r.Problems(); if (p.Count > 0) throw new FormatException(string.Join("; ", p));
            return r;
        }

        public List<string> Problems()
        {
            var p = new List<string>();
            if (!Hex.IsHex(OperationId, 32)) p.Add("the request names no operation");
            if (Action == "setup")
            {
                if (Plan == null || !Plan.IsCcs) p.Add("only Workbench's own driver package can be set up");
                else if (Plan.OperationId != OperationId) p.Add("the plan belongs to another operation");
                else if (!PinnedDriver.Eq(Plan.InfSha256, CcsDriver.InfSha256) || !PinnedDriver.Eq(Plan.InterfaceGuid, CcsDriver.InterfaceGuid) || !PinnedDriver.Eq(Plan.HardwareId, PinnedDriver.HardwareId))
                    p.Add("the plan is not this version's driver package");
            }
            else if (Action == "remove")
            {
                if (Removal == null) p.Add("the request has no removal plan");
                else { p.AddRange(Removal.Problems()); if (Removal.OperationId != OperationId) p.Add("the removal plan belongs to another operation"); }
            }
            else if (Action == "cleanup")
            {
                if (Cleanup == null) p.Add("the request has no cleanup plan");
                else { p.AddRange(Cleanup.Problems()); if (Cleanup.OperationId != OperationId) p.Add("the cleanup plan belongs to another operation"); }
            }
            else p.Add("unknown driver action: " + (Action ?? "(none)"));
            return p;
        }
    }

    /// <summary>The step's exit codes: 0 and 3010 (ERROR_SUCCESS_REBOOT_REQUIRED) when done; 0x4343xxxx for its own refusals and stops; any other
    /// value is the Windows error that stopped it (after taking back what it added).</summary>
    public static class DriverExit
    {
        public const int Done = 0, DoneRestartNeeded = 3010, Canceled = 1223;
        public const int GateClosed = 0x43430001, BadRequest = 0x43430002, NotElevated = 0x43430003, NotNative = 0x43430004, StateChanged = 0x43430005,
            PackageInvalid = 0x43430006, NotTheRecordedPackage = 0x43430007, TakeBackIncomplete = 0x43430008, CertificateKept = 0x43430009,
            CertificateKeptRestartNeeded = 0x4343000A, Unexpected = 0x4343000B, Unreadable = 0x4343000C,
            CertificateDeferred = 0x4343000D, CertificateDeferredRestartNeeded = 0x4343000E, CertificateStillNeeded = 0x4343000F, NotTheRecordedCertificate = 0x43430010;

        public static ActionReport Report(int code)
        {
            ActionReport R(Reported r, string detail) => new ActionReport { Reported = r, ExitCode = code, Detail = detail };
            switch (code)
            {
                case Done: return R(Reported.Success, "");
                case DoneRestartNeeded: return R(Reported.SuccessRestartNeeded, "Windows needs a restart to finish");
                case GateClosed: return R(Reported.Unavailable, Gate.DriverText);
                case BadRequest: return R(Reported.Failed, "Workbench's administrator step did not accept the request");
                case NotElevated: return R(Reported.ElevationRefused, "the administrator step ran without administrator permission");
                case NotNative: return R(Reported.Failed, "this copy of Workbench is not made for this PC's processor, and Windows sets up drivers only from a program made for it");
                case StateChanged: return R(Reported.Refused, "this PC's USB drivers changed while Workbench was getting ready");
                case PackageInvalid: return R(Reported.Failed, "Workbench's driver package did not pass its own check, so nothing was changed");
                case NotTheRecordedPackage: return R(Reported.Refused, "the driver on this PC is no longer exactly the one Workbench set up");
                case TakeBackIncomplete: return R(Reported.Failed, "Windows did not finish, and Workbench could not take back every step");
                case CertificateKept: return R(Reported.Success, "the certificate was kept");
                case CertificateKeptRestartNeeded: return R(Reported.SuccessRestartNeeded, "the certificate was kept; Windows needs a restart to finish");
                case Unexpected: return R(Reported.Failed, "the administrator step stopped unexpectedly");
                case Unreadable: return R(Reported.Refused, "the administrator step could not read this PC's driver list");
                case CertificateDeferred: return R(Reported.Success, "Windows accepted the removal, but it is not shown finished; the certificate was kept");
                case CertificateDeferredRestartNeeded: return R(Reported.SuccessRestartNeeded, "Windows needs a restart to finish; the certificate was kept until then");
                case CertificateStillNeeded: return R(Reported.Refused, "the certificate may still be needed on this PC, so it was kept");
                case NotTheRecordedCertificate: return R(Reported.Refused, "the certificate on this PC is no longer exactly the one Workbench's setup added");
            }
            return R(Reported.Failed, WindowsError(code));
        }

        /// <summary>Windows' device-setup errors (setupapi.h; Windows has no message text for them), in plain words</summary>
        static readonly Dictionary<int, string> DeviceSetupErrors = new Dictionary<int, string>
        {
            { 0x22F, "the driver package has no signature Windows accepts" },                      // ERROR_NO_CATALOG_FOR_OEM_INF
            { 0x235, "a 32-bit program cannot set up drivers on 64-bit Windows" },                 // ERROR_IN_WOW64
            { 0x23D, "a device is still using the driver package" },                               // ERROR_INF_IN_USE_BY_DEVICES
            { 0x240, "this PC's settings do not allow driver packages signed this way" },           // ERROR_AUTHENTICODE_DISALLOWED
            { 0x242, "trust in the driver package's signer was not established" },                 // ERROR_AUTHENTICODE_TRUST_NOT_ESTABLISHED
            { 0x243, "this PC is set not to trust the driver package's signer" },                  // ERROR_AUTHENTICODE_PUBLISHER_NOT_TRUSTED
            { 0x244, "the driver package's signature is not valid for this version of Windows" },   // ERROR_SIGNATURE_OSATTRIBUTE_MISMATCH
            { 0x246, "device setup is not ready; try again later" },                               // ERROR_DEVICE_INSTALLER_NOT_READY
            { 0x247, "the driver package could not be added to the driver store" },                // ERROR_DRIVER_STORE_ADD_FAILED
            { 0x248, "a policy on this PC does not allow setting up this device" },                // ERROR_DEVICE_INSTALL_BLOCKED
            { 0x249, "a policy on this PC does not allow this driver" },                           // ERROR_DRIVER_INSTALL_BLOCKED
            { 0x24B, "the driver package does not match its signature" },                          // ERROR_FILE_HASH_NOT_IN_CATALOG
            { 0x24C, "the driver package could not be deleted from the driver store" },            // ERROR_DRIVER_STORE_DELETE_FAILED
        };

        /// <summary>Windows' trust check's errors for a package signed by a certificate this PC does not trust (WinVerifyTrust, wintrust.h)</summary>
        static readonly Dictionary<int, string> TrustErrors = new Dictionary<int, string>
        {
            { unchecked((int)0x800B0109), "Windows did not trust the certificate Workbench's setup made for its driver package" },      // CERT_E_UNTRUSTEDROOT
            { unchecked((int)0x800B0112), "Windows did not trust the certificate Workbench's setup made for its driver package" },      // CERT_E_UNTRUSTEDCA
            { unchecked((int)0x800B010A), "Windows could not connect the package's signature to a certificate it trusts" },             // CERT_E_CHAINING
            { unchecked((int)0x800B0111), "this PC is set to distrust the package's signer" },                                       // TRUST_E_EXPLICIT_DISTRUST
            { unchecked((int)0x800B0004), "this PC's settings do not let Windows trust the package's signer" },                     // TRUST_E_SUBJECT_NOT_TRUSTED
            { unchecked((int)0x80092026), "this PC's security settings do not allow it" },                                           // CRYPT_E_SECURITY_SETTINGS
            { unchecked((int)0x800B0100), "the driver package has no signature Windows accepts" },                                   // TRUST_E_NOSIGNATURE
        };

        /// <summary>the sentence that names an unmet requirement of this PC behind a refusal (trust in the setup's certificate, a policy, a copy
        /// for another processor), or null when the code is not one</summary>
        public static string Requirement(int? code)
        {
            if (code == null) return null;
            int c = code.Value;
            if (TrustErrors.ContainsKey(c) || c == unchecked((int)0xE0000242) || c == unchecked((int)0xE0000243) || c == unchecked((int)0xE0000240))
                return "This PC does not trust a driver package signed by a certificate made on it for one setup, as Workbench's package is. Workbench took that certificate back and changes no trust setting. Someone who manages this PC can set up a driver for the console that this PC accepts.";
            if (c == unchecked((int)0xE0000248) || c == unchecked((int)0xE0000249))
                return "A policy set by whoever manages this PC does not allow setting up this device or driver. Workbench changes no policy.";
            if (c == NotNative) return "Use the CCS Console Workbench download made for this PC's processor.";
            return null;
        }

        /// <summary>Windows' own words for an error (FormatMessage, or the device-setup table above), with its number</summary>
        public static string WindowsError(int code)
        {
            string text;
            if ((code & unchecked((int)0xFFFFF000)) == unchecked((int)0xE0000000) && DeviceSetupErrors.TryGetValue(code & 0xFFF, out var t)) text = t;
            else if (TrustErrors.TryGetValue(code, out var tt)) text = tt;
            else try { text = new Win32Exception(code).Message; } catch { text = null; }
            if (string.IsNullOrWhiteSpace(text) || text.StartsWith("Unknown error", StringComparison.OrdinalIgnoreCase)) text = "Windows reported an error";
            return "Windows: " + text.Trim().TrimEnd('.') + " (error 0x" + code.ToString("X8") + ")";
        }
    }

    public static class DriverActionHost
    {
        public const string Flag = "--driver-action";

        /// <summary>the elevated mode's entry, called by both hosts before anything else when Flag is on the command line</summary>
        public static int Run(string[] args)
        {
            if (!Gate.DriverChanges) return DriverExit.GateClosed;   // closed in code: nothing is read and nothing is changed
            return Execute(args, WindowsSystemDriverApi.Open(), new WindowsDriverStore());
        }

        /// <summary>the step itself, on any system functions and any PC reader (the tests give an in-memory PC)</summary>
        public static int Execute(string[] args, ISystemDriverApi api, IDriverStore store)
        {
            DriverActionRequest r;
            try { int i = Array.IndexOf(args ?? new string[0], Flag); r = DriverActionRequest.Decode(i >= 0 && i + 1 < args.Length ? args[i + 1] : null); }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentException) { return DriverExit.BadRequest; }
            if (!api.Elevated) return DriverExit.NotElevated;
            if (api.NotNative != null) return DriverExit.NotNative;
            return r.Action == "setup" ? Setup(r, api, store) : r.Action == "remove" ? Remove(r, api, store) : CleanUp(r, api, store);
        }

        static int Setup(DriverActionRequest r, ISystemDriverApi api, IDriverStore store)
        {
            var before = DriverSetup.Assess(store, r.Plan);
            if (before.Kind == AssociationKind.Unreadable) return DriverExit.Unreadable;
            if (before.Kind != AssociationKind.None || before.SetupBlocked) return DriverExit.StateChanged;   // never over anything already there
            string folder = null, thumb = null; var added = new List<CertStore>(); CcsSigner signer = null;
            try
            {
                folder = api.NewWorkFolder();
                signer = CcsSigner.New(r.OperationId, DateTime.UtcNow);
                var (inf, cat) = CcsCatalog.Write(folder, signer);
                thumb = signer.Thumbprint; var der = signer.Der;
                signer.Dispose(); signer = null;                     // the private key is gone before anything on the PC changes
                var check = CcsCatalog.Check(inf, cat);
                if (!check.Ok || check.SignerThumbprint != thumb || !CcsDriver.IsCcsSubject(check.SignerSubject, r.OperationId)) return DriverExit.PackageInvalid;
                foreach (var s in new[] { CertStore.Root, CertStore.TrustedPublisher }) { added.Add(s); api.AddCertificate(s, der, CcsDriver.FriendlyName); }
                var trust = api.VerifyCatalogTrust(inf, cat);
                if (trust != 0) return TakeBack(api, store, thumb, added) ? trust : DriverExit.TakeBackIncomplete;
                var res = api.InstallDriverPackage(inf);
                if (!res.Ok) return TakeBack(api, store, thumb, added) ? (res.Error != 0 ? res.Error : DriverExit.Unexpected) : DriverExit.TakeBackIncomplete;
                return res.NeedsRestart ? DriverExit.DoneRestartNeeded : DriverExit.Done;
            }
            catch (Exception ex) when (!(ex is SimulatedCrashException))
            {
                if (thumb != null && added.Count > 0 && !TakeBack(api, store, thumb, added)) return DriverExit.TakeBackIncomplete;
                return ErrorOf(ex);
            }
            finally
            {
                signer?.Dispose();
                if (folder != null) try { api.DeleteWorkFolder(folder); } catch (Exception) { }
            }
        }

        /// <summary>after a failed setup: this step's certificate leaves each store it was added to (exact thumbprint), but only while no package
        /// signed by it is on the PC (such a package would lose its trust); true when nothing of this step is left</summary>
        static bool TakeBack(ISystemDriverApi api, IDriverStore store, string thumb, List<CertStore> added)
        {
            try
            {
                var scan = store.PackagesSignedBy(thumb);
                if (!scan.Complete || scan.Packages.Count > 0 || store.Packages(PinnedDriver.Vid, PinnedDriver.Pid).Any(p => PinnedDriver.Eq(p.SignerThumbprint, thumb))) return false;
                bool all = true;
                foreach (var s in Enumerable.Reverse(added)) { try { api.RemoveCertificate(s, thumb); } catch (Exception) { } if (store.Certificates(s).Any(c => PinnedDriver.Eq(c.Thumbprint, thumb))) all = false; }
                return all;
            }
            catch (Exception) { return false; }
        }

        static int Remove(DriverActionRequest r, ISystemDriverApi api, IDriverStore store)
        {
            var plan = r.Removal;
            try
            {
                var p = store.Package(plan.PublishedName);
                if (p == null) return DriverExit.StateChanged;
                if (!CcsDriver.IsCcsPackage(p) || !CcsDriver.IsCcsSubject(p.SignerSubject, plan.OperationId) || !PinnedDriver.Eq(p.InfSha256, plan.InfSha256) || !PinnedDriver.Eq(p.CatalogSha256, plan.CatalogSha256)
                    || p.PendingRemoval) return DriverExit.NotTheRecordedPackage;
                if (store.DevicesUsing(plan.PublishedName).Any(u => !plan.Devices.Any(d => PinnedDriver.Eq(d, u)))) return DriverExit.StateChanged;   // shared since the decision
                foreach (var c in plan.Certificates)
                {
                    var cur = store.Certificates(c.Store).FirstOrDefault(x => PinnedDriver.Eq(x.Thumbprint, c.Thumbprint));
                    if (cur == null || cur.HasPrivateKey || !CcsDriver.IsCcsSubject(cur.Subject, plan.OperationId) || !PinnedDriver.Eq(c.Thumbprint, p.SignerThumbprint)) return DriverExit.NotTheRecordedPackage;
                }
                var res = api.UninstallDriverPackage(plan.PublishedName);
                if (!res.Ok) return res.Error != 0 ? res.Error : DriverExit.Unexpected;
                // Windows' answer is not the result: the package and every user of each certificate are read again before any certificate is
                // touched, and while the package is there, waits for a restart or cannot be read, both certificates stay (cleanup deferred)
                var t = TrustCleanup.Check(store, CcsDriver.SubjectFor(plan.OperationId), plan.PublishedName, plan.Certificates, res.NeedsRestart ? RetainReason.RestartPending : (RetainReason?)null);
                if (t.PackageHold != null) return res.NeedsRestart || t.PackageHold == RetainReason.RestartPending ? DriverExit.CertificateDeferredRestartNeeded : DriverExit.CertificateDeferred;
                bool kept = false;
                foreach (var i in t.Items)
                {
                    if (i.Gone) continue;
                    if (!i.Removable) { kept = true; continue; }   // its signer scan is incomplete, or another package is signed with it
                    try { if (!api.RemoveCertificate(i.Certificate.Store, i.Certificate.Thumbprint)) kept = true; } catch (Exception ex) when (!(ex is SimulatedCrashException)) { kept = true; }
                }
                return kept ? DriverExit.CertificateKept : DriverExit.Done;
            }
            catch (Exception ex) when (!(ex is SimulatedCrashException)) { return ErrorOf(ex); }
        }

        /// <summary>a later cleanup of certificates a removal kept: everything is read again here, and any doubt touches nothing</summary>
        static int CleanUp(DriverActionRequest r, ISystemDriverApi api, IDriverStore store)
        {
            var plan = r.Cleanup;
            try
            {
                var waiting = plan.RestartFirst && !RestartState.RestartedSince(store.Restart(), plan.AfterUtc, plan.BootThenUtc) ? RetainReason.RestartNotSeen : (RetainReason?)null;
                var t = TrustCleanup.Check(store, CcsDriver.SubjectFor(plan.OperationId), plan.PublishedName, plan.Certificates, waiting);
                if (t.Error != null) return DriverExit.Unreadable;
                if (t.Items.Any(i => !i.Gone && i.Hold == RetainReason.CertificateChanged)) return DriverExit.NotTheRecordedCertificate;
                if (t.PackageHold != null || t.Items.Any(i => !i.Gone && i.Hold != null)) return DriverExit.CertificateStillNeeded;
                bool kept = false;
                foreach (var c in t.Removable) { try { if (!api.RemoveCertificate(c.Store, c.Thumbprint)) kept = true; } catch (Exception ex) when (!(ex is SimulatedCrashException)) { kept = true; } }
                return kept ? DriverExit.CertificateKept : DriverExit.Done;
            }
            catch (Exception ex) when (!(ex is SimulatedCrashException)) { return ErrorOf(ex); }
        }

        /// <summary>a Windows error carried by an exception (a certificate store or file refusal), else Unexpected</summary>
        static int ErrorOf(Exception ex) => ex is Win32Exception w ? w.NativeErrorCode
            : (ex is System.Security.Cryptography.CryptographicException || ex is UnauthorizedAccessException || ex is System.IO.IOException) && ex.HResult != 0 ? ex.HResult : DriverExit.Unexpected;
    }
}
