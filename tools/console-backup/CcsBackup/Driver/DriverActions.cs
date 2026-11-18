// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Changing the PC's USB driver state: the one place that can, and in this version it cannot. WindowsDriverActions is the real one: each
// change is one run of this same program with administrator permission in its --driver-action mode (DriverActionAdapter, RunasLauncher,
// DriverActionHost), and it stands behind the closed DriverChanges boundary (Gate.cs): with the constant false every path to that run is left
// out by the compiler, Available is false and each change throws DriverActionUnavailableException before anything starts.
// SimulatedDriverActions changes a SimulatedDriverStore only, the way the pinned Hakchi2 CE 3.9.3 driver setup changes a real PC
// (DRIVER-AUDIT.md), with every outcome the ledger tests need: refusal, cancel, failure, restart, a crash part way, a misleading report.
//   DriverSetupPlan   what a setup would do, shown in plain words before consent and kept in the record: Workbench's own package
//                     (CcsWinUsb, what the real adapter sets up) or the pinned Hakchi helper's effect (PinnedHakchi, the simulation only)
//   RemovalPlan       one published package by name and exact certificate thumbprints, never a hardware ID, a wildcard or a subject name
//   CleanupPlan       a later cleanup of certificates a removal kept (TrustCleanup.cs): exact thumbprints, and the package that must be gone
//   ActionReport      what the action itself said (never trusted: the result is always read back from the store)
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace CcsBackup.Driver
{
    public sealed class DriverActionUnavailableException : Exception
    {
        public const string Text = Gate.DriverText;
        public DriverActionUnavailableException(string what) : base(Text + " (" + what + ")") { }
    }

    /// <summary>Refused: the elevated step checked the PC again and found it was no longer as the window saw it, so it changed nothing.</summary>
    public enum Reported { Success, SuccessRestartNeeded, ElevationRefused, Canceled, Failed, Unavailable, Refused }

    public sealed class ActionReport
    {
        public Reported Reported;
        public int? ExitCode;
        public string Detail;
        public JObj ToJson() => new JObj { { "reported", Reported.ToString() }, { "exit_code", ExitCode.HasValue ? (object)ExitCode.Value : null }, { "detail", Detail ?? "" } };
        public static ActionReport From(Dictionary<string, object> d)
        {
            if (d == null) return null;
            Enum.TryParse(d.S("reported") ?? "", out Reported r);
            var e = d.L("exit_code", long.MinValue);
            return new ActionReport { Reported = r, ExitCode = e == long.MinValue ? (int?)null : (int)e, Detail = d.S("detail") ?? "" };
        }
    }

    public sealed class DriverSetupPlan
    {
        public string Method;                  // "ccs-winusb-inf" (Workbench's own package) or "pinned-hakchi-helper" (the effect of Hakchi2 CE 3.9.3's driver\classic_driver.exe)
        public string HardwareId, InfName, Service, InterfaceGuid, HelperSha256, Provider, InfSha256, CatalogName;
        public bool AddsTrustCertificate, DeletesCertificatesBySubject;
        public string CertificateSubject;
        public string OperationId;             // set when the setup's record is begun: the certificate's subject names it
        public List<string> Changes = new List<string>();   // the PC changes, in plain words, shown before consent

        /// <summary>Workbench's own data-only package (CcsDriverPackage.cs): Windows' WinUSB, a new certificate of its own per setup, nothing deleted by subject</summary>
        public static DriverSetupPlan CcsWinUsb() => new DriverSetupPlan
        {
            Method = CcsDriver.Method, HardwareId = PinnedDriver.HardwareId, InfName = CcsDriver.InfName, Service = CcsDriver.Service, InterfaceGuid = CcsDriver.InterfaceGuid,
            Provider = CcsDriver.Provider, InfSha256 = CcsDriver.InfSha256, CatalogName = CcsDriver.CatalogName, AddsTrustCertificate = true, DeletesCertificatesBySubject = false,
            CertificateSubject = CcsDriver.SubjectPrefix + "<operation id>", Changes = DriverText.CcsPackageChanges.ToList(),
        };
        /// <summary>what Hakchi2 CE 3.9.3's own driver setup does (the simulation and the coexistence tests; Workbench never runs that helper)</summary>
        public static DriverSetupPlan PinnedHakchi() => new DriverSetupPlan
        {
            Method = "pinned-hakchi-helper", HardwareId = PinnedDriver.HardwareId, InfName = PinnedDriver.InfName, Service = PinnedDriver.Service, InterfaceGuid = PinnedDriver.InterfaceGuid,
            HelperSha256 = PinnedDriver.HelperSha256, Provider = PinnedDriver.Provider, AddsTrustCertificate = true, DeletesCertificatesBySubject = true, CertificateSubject = PinnedDriver.CertificateSubject,
            Changes = DriverText.PinnedHelperChanges.ToList(),
        };
        public bool IsCcs => Method == CcsDriver.Method;
        /// <summary>the association this plan leaves when it worked</summary>
        public AssociationKind ExpectedKind => IsCcs ? AssociationKind.CcsWinUsb : AssociationKind.PinnedHakchi;
        /// <summary>a package of this plan's kind (any operation's)</summary>
        public bool MatchesKind(DriverPackage p) => IsCcs ? CcsDriver.IsCcsPackage(p) : PinnedDriver.IsPinned(p);
        /// <summary>this operation's own package: of this kind and, for Workbench's package, signed by this operation's certificate</summary>
        public bool Matches(DriverPackage p) => MatchesKind(p) && (!IsCcs || OperationId == null || CcsDriver.IsCcsSubject(p.SignerSubject, OperationId));
        /// <summary>a certificate this operation made (for Workbench's package: its subject names the operation)</summary>
        public bool OwnsCertificate(CertificateEntry c, DriverPackage owned) => c != null && owned != null && PinnedDriver.Eq(c.Thumbprint, owned.SignerThumbprint)
            && (!IsCcs || CcsDriver.IsCcsSubject(c.Subject, OperationId) && !c.HasPrivateKey);

        public JObj ToJson() => new JObj { { "method", Method }, { "hardware_id", HardwareId }, { "inf", InfName }, { "service", Service }, { "interface_guid", InterfaceGuid },
            { "helper_sha256", HelperSha256 ?? "" }, { "provider", Provider ?? "" }, { "inf_sha256", InfSha256 ?? "" }, { "catalog", CatalogName ?? "" }, { "adds_trust_certificate", AddsTrustCertificate },
            { "deletes_certificates_by_subject", DeletesCertificatesBySubject }, { "certificate_subject", CertificateSubject ?? "" }, { "operation_id", OperationId }, { "changes", Changes } };
        public static DriverSetupPlan From(Dictionary<string, object> d) => d == null ? null : new DriverSetupPlan
        {
            Method = d.S("method"), HardwareId = d.S("hardware_id"), InfName = d.S("inf"), Service = d.S("service"), InterfaceGuid = d.S("interface_guid"), HelperSha256 = d.S("helper_sha256"),
            Provider = d.S("provider"), InfSha256 = d.S("inf_sha256"), CatalogName = d.S("catalog"), AddsTrustCertificate = d.B("adds_trust_certificate"),
            DeletesCertificatesBySubject = d.B("deletes_certificates_by_subject") || d.S("method") == "pinned-hakchi-helper", CertificateSubject = d.S("certificate_subject"),
            OperationId = d.S("operation_id"), Changes = DeviceBinding.Strs(d.A("changes")),
        };
    }

    public sealed class RemovalPlan
    {
        public string OperationId, PublishedName, OriginalName, InfSha256, CatalogSha256;
        public List<CertificateEntry> Certificates = new List<CertificateEntry>();   // exact store + thumbprint, each recorded as added by the setup
        public List<string> Devices = new List<string>();                              // the device instances the setup bound (no other may use the package)

        /// <summary>empty when the plan names exactly one published package and exact certificates: no hardware ID, wildcard, subject, inbox INF
        /// or forced removal can ever be expressed by a valid plan</summary>
        public List<string> Problems()
        {
            var p = new List<string>();
            if (!UsbId.IsPublishedName(PublishedName)) p.Add("the package to remove is not one published package name (oemNN.inf): " + (PublishedName ?? "(none)"));
            if (string.IsNullOrEmpty(InfSha256) || string.IsNullOrEmpty(CatalogSha256)) p.Add("the package's recorded content (INF and catalog SHA-256) is missing");
            foreach (var c in Certificates) if (!UsbId.IsThumbprint(UsbId.NormThumbprint(c.Thumbprint))) p.Add("a certificate is not named by an exact thumbprint: " + (c.Thumbprint ?? "(none)"));
            return p;
        }
        public JObj ToJson() => new JObj { { "operation_id", OperationId }, { "published_name", PublishedName }, { "original_name", OriginalName ?? "" }, { "inf_sha256", InfSha256 ?? "" },
            { "catalog_sha256", CatalogSha256 ?? "" }, { "certificates", Certificates.Select(c => c.ToJson()).ToList() }, { "devices", Devices } };
        public static RemovalPlan From(Dictionary<string, object> d)
        {
            if (d == null) return null;
            var r = new RemovalPlan { OperationId = d.S("operation_id"), PublishedName = d.S("published_name"), OriginalName = d.S("original_name"), InfSha256 = d.S("inf_sha256"), CatalogSha256 = d.S("catalog_sha256"),
                Devices = DeviceBinding.Strs(d.A("devices")) };
            foreach (var o in d.A("certificates") ?? new List<object>()) { var c = CertificateEntry.From(o as Dictionary<string, object>); if (c != null) r.Certificates.Add(c); }
            return r;
        }
    }

    public interface IDriverActions
    {
        bool Available { get; }
        /// <summary>set up the planned driver (needs administrator permission). The report is what the action said; callers read the result back.</summary>
        ActionReport Install(DriverSetupPlan plan, ElevationState elevation);
        /// <summary>remove exactly the planned package and certificates (needs administrator permission; Windows may finish only after a restart)</summary>
        ActionReport Remove(RemovalPlan plan, ElevationState elevation);
        /// <summary>a later cleanup: remove exactly the planned certificates, which a removal kept, once their package is gone (needs administrator permission)</summary>
        ActionReport CleanUp(CleanupPlan plan, ElevationState elevation);
    }

    /// <summary>The real PC, behind the DriverChanges boundary. With the boundary closed (this version) Available is false and each change throws
    /// before anything starts; the compiler leaves out the elevated run that would follow.</summary>
    public sealed class WindowsDriverActions : IDriverActions
    {
        public bool Available => Gate.DriverChanges;
        public ActionReport Install(DriverSetupPlan plan, ElevationState elevation)
        {
            if (!Gate.DriverChanges) throw new DriverActionUnavailableException("set up the USB driver");
            return new DriverActionAdapter(RunasLauncher.Open(Self())).Install(plan, elevation);
        }
        public ActionReport Remove(RemovalPlan plan, ElevationState elevation)
        {
            if (!Gate.DriverChanges) throw new DriverActionUnavailableException("remove the USB driver");
            return new DriverActionAdapter(RunasLauncher.Open(Self())).Remove(plan, elevation);
        }
        public ActionReport CleanUp(CleanupPlan plan, ElevationState elevation)
        {
            if (!Gate.DriverChanges) throw new DriverActionUnavailableException("remove the USB driver's certificate");
            return new DriverActionAdapter(RunasLauncher.Open(Self())).CleanUp(plan, elevation);
        }
        /// <summary>this program's own file: the single-file standalone (its native host) or Classic Cart Studio for Windows; never a copy</summary>
        static string Self() => System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
    }

    /// <summary>the person refused Windows' administrator prompt, or could not give an administrator's name and password</summary>
    public sealed class ElevationCanceledException : Exception { public ElevationCanceledException() : base("Windows did not give administrator permission (the prompt was refused).") { } }

    /// <summary>starts this same program once with administrator permission and the given arguments, waits, and returns its exit code;
    /// ElevationCanceledException when the permission prompt was refused</summary>
    public interface IElevatedRunner { int Run(string arguments); }

    /// <summary>The real adapter, window side: one driver change = one elevated run of this program in its --driver-action mode, with the
    /// request in the command line (DriverActionHost). Only Workbench's own package can be asked for. The exit code becomes the report; the
    /// caller (DriverSetup) then reads the PC back and decides from that, never from the report alone.</summary>
    public sealed class DriverActionAdapter : IDriverActions
    {
        readonly IElevatedRunner runner;
        public int Runs { get; private set; }
        public DriverActionAdapter(IElevatedRunner runner) { this.runner = runner ?? throw new ArgumentNullException(nameof(runner)); }
        public bool Available => true;

        public ActionReport Install(DriverSetupPlan plan, ElevationState elevation)
        {
            if (plan == null || !plan.IsCcs || !Hex.IsHex(plan.OperationId, 32))
                return new ActionReport { Reported = Reported.Failed, Detail = "only Workbench's own driver package, with its record begun, can be set up" };
            return Launch(DriverActionRequest.ForSetup(plan));
        }

        public ActionReport Remove(RemovalPlan plan, ElevationState elevation)
        {
            var problems = plan?.Problems() ?? new List<string> { "no removal plan" };
            if (problems.Count > 0) throw new ArgumentException(string.Join("; ", problems));
            if (!Hex.IsHex(plan.OperationId, 32)) throw new ArgumentException("the removal plan names no operation");
            return Launch(DriverActionRequest.ForRemoval(plan));
        }

        public ActionReport CleanUp(CleanupPlan plan, ElevationState elevation)
        {
            var problems = plan?.Problems() ?? new List<string> { "no cleanup plan" };
            if (problems.Count > 0) throw new ArgumentException(string.Join("; ", problems));
            return Launch(DriverActionRequest.ForCleanup(plan));
        }

        ActionReport Launch(DriverActionRequest r)
        {
            int code; Runs++;
            try { code = runner.Run(DriverActionHost.Flag + " " + r.Encode()); }
            catch (ElevationCanceledException ex) { return new ActionReport { Reported = Reported.ElevationRefused, ExitCode = DriverExit.Canceled, Detail = ex.Message }; }
            catch (Win32Exception ex) { return new ActionReport { Reported = Reported.Failed, ExitCode = ex.NativeErrorCode, Detail = "Workbench could not start its administrator step: " + ex.Message }; }
            return DriverExit.Report(code);
        }
    }

    /// <summary>thrown by SimulatedDriverActions to stand for the process ending part way (nothing after it runs, as after a real crash)</summary>
    public sealed class SimulatedCrashException : Exception { public SimulatedCrashException(string where) : base("simulated crash: " + where) { } }

    public enum InstallBehavior { Success, SuccessRestartPending, ElevationRefused, Canceled, Fails, FailsAfterCertificate, CrashAfterChanges, ReportsSuccessNothingChanged, ReportsFailureButInstalled }
    public enum RemoveBehavior { Success, RestartPending, ElevationRefused, Fails, CrashAfterPackage }
    public enum CleanupBehavior { Success, ElevationRefused, Fails, CrashAfterFirst }

    /// <summary>The pinned setup's effect on a SimulatedDriverStore (libwdi: drop earlier certificates with the same subject, create a new one in
    /// Root and Trusted Publishers, stage the package signed by it, force it onto connected devices with the identity), and a removal by name.</summary>
    public sealed class SimulatedDriverActions : IDriverActions
    {
        readonly SimulatedDriverStore pc;
        public InstallBehavior InstallMode = InstallBehavior.Success;
        public RemoveBehavior RemoveMode = RemoveBehavior.Success;
        public CleanupBehavior CleanupMode = CleanupBehavior.Success;
        public Action BeforeInstall, BeforeRemove, BeforeCleanup;   // the tests look at the record on disk at the moment the change starts
        public int Installs, Removals, Cleanups;
        public string LastThumbprint;
        public SimulatedDriverActions(SimulatedDriverStore pc) { this.pc = pc; }
        public bool Available => true;

        public ActionReport Install(DriverSetupPlan plan, ElevationState elevation)
        {
            Installs++; BeforeInstall?.Invoke();
            switch (InstallMode)
            {
                case InstallBehavior.ElevationRefused: return new ActionReport { Reported = Reported.ElevationRefused, ExitCode = 1223, Detail = "The operation was canceled by the user." };
                case InstallBehavior.Canceled: return new ActionReport { Reported = Reported.Canceled, ExitCode = -12, Detail = "operation canceled by the user (WDI_ERROR_USER_CANCEL)" };
                case InstallBehavior.Fails: return new ActionReport { Reported = Reported.Failed, ExitCode = -5, Detail = "WDI_ERROR_NOT_FOUND" };
                case InstallBehavior.ReportsSuccessNothingChanged: return new ActionReport { Reported = Reported.Success, ExitCode = 0 };
            }
            var t = SimulatedDriverStore.FakeThumbprint("setup-" + Installs + "-" + pc.PackageList.Count + "-" + Guid.NewGuid().ToString("N")); LastThumbprint = t;
            pc.CertificateList.RemoveAll(c => PinnedDriver.Eq(c.Subject, PinnedDriver.CertificateSubject));   // SelfSignFile: RemoveCertFromStore by subject (pki.c 940-941)
            pc.AddCertificate(CertStore.Root, t, PinnedDriver.CertificateSubject, PinnedDriver.CertificateFriendlyName);
            pc.AddCertificate(CertStore.TrustedPublisher, t, PinnedDriver.CertificateSubject, PinnedDriver.CertificateFriendlyName);
            if (InstallMode == InstallBehavior.FailsAfterCertificate) return new ActionReport { Reported = Reported.Failed, ExitCode = -1, Detail = "WDI_ERROR_IO" };
            var p = pc.AddPinnedPackage(t);
            foreach (var d in pc.DeviceList.Where(d => d.Present && d.HardwareIds.Any(h => UsbId.Matches(h, PinnedDriver.Vid, PinnedDriver.Pid))))
            { pc.Bind(d, p); if (InstallMode == InstallBehavior.SuccessRestartPending) d.NeedsRestart = true; }
            if (InstallMode == InstallBehavior.CrashAfterChanges) throw new SimulatedCrashException("after the driver package was staged");
            if (InstallMode == InstallBehavior.ReportsFailureButInstalled) return new ActionReport { Reported = Reported.Failed, ExitCode = -99, Detail = "WDI_ERROR_OTHER" };
            return new ActionReport { Reported = InstallMode == InstallBehavior.SuccessRestartPending ? Reported.SuccessRestartNeeded : Reported.Success, ExitCode = 0 };
        }

        public ActionReport Remove(RemovalPlan plan, ElevationState elevation)
        {
            Removals++;
            var problems = plan.Problems(); if (problems.Count > 0) throw new ArgumentException(string.Join("; ", problems));
            BeforeRemove?.Invoke();
            if (RemoveMode == RemoveBehavior.ElevationRefused) return new ActionReport { Reported = Reported.ElevationRefused, ExitCode = 1223 };
            if (RemoveMode == RemoveBehavior.Fails) return new ActionReport { Reported = Reported.Failed, ExitCode = 5, Detail = "Access is denied." };
            var p = pc.PackageList.FirstOrDefault(x => PinnedDriver.Eq(x.PublishedName, plan.PublishedName));
            if (p == null) return new ActionReport { Reported = Reported.Failed, ExitCode = 2, Detail = "the driver package is not there" };
            if (RemoveMode == RemoveBehavior.RestartPending)
            {
                p.PendingRemoval = true;
                foreach (var d in pc.DeviceList.Where(d => PinnedDriver.Eq(d.InfPath, p.PublishedName))) d.NeedsRestart = true;
                return new ActionReport { Reported = Reported.SuccessRestartNeeded, ExitCode = 3010 };
            }
            pc.PackageList.Remove(p);
            foreach (var d in pc.DeviceList.Where(d => PinnedDriver.Eq(d.InfPath, p.PublishedName))) { d.InfPath = ""; d.Service = ""; d.Provider = ""; d.InterfaceGuids.Clear(); }
            if (RemoveMode == RemoveBehavior.CrashAfterPackage) throw new SimulatedCrashException("after the package was removed, before its certificate");
            foreach (var c in plan.Certificates) pc.CertificateList.RemoveAll(x => x.Store == c.Store && PinnedDriver.Eq(x.Thumbprint, c.Thumbprint));
            return new ActionReport { Reported = Reported.Success, ExitCode = 0 };
        }

        /// <summary>the planned certificates leave the simulated PC by exact store and thumbprint (the window's checks decide whether it is asked)</summary>
        public ActionReport CleanUp(CleanupPlan plan, ElevationState elevation)
        {
            Cleanups++;
            var problems = plan.Problems(); if (problems.Count > 0) throw new ArgumentException(string.Join("; ", problems));
            BeforeCleanup?.Invoke();
            if (CleanupMode == CleanupBehavior.ElevationRefused) return new ActionReport { Reported = Reported.ElevationRefused, ExitCode = 1223 };
            if (CleanupMode == CleanupBehavior.Fails) return new ActionReport { Reported = Reported.Failed, ExitCode = 5, Detail = "Access is denied." };
            foreach (var c in plan.Certificates)
            {
                pc.CertificateList.RemoveAll(x => x.Store == c.Store && PinnedDriver.Eq(x.Thumbprint, c.Thumbprint));
                if (CleanupMode == CleanupBehavior.CrashAfterFirst) throw new SimulatedCrashException("after the first certificate was removed");
            }
            return new ActionReport { Reported = Reported.Success, ExitCode = 0 };
        }
    }
}
