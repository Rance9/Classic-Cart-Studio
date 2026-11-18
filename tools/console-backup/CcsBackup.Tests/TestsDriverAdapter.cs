// Classic Cart Studio console backup and restore engine tests: Workbench's own USB driver setup and removal (the real adapter). Copyright (C) 2026 Rance9. GPL-3.0-or-later.
//   driver-package  the data-only INF and its catalog, made by the product code into this run's work folder and read back by Windows' own
//                   readers (CMS, catalog, Authenticode); the signing certificate's profile; the key never written to disk. Nothing is
//                   installed or trusted: the files stay in the work folder.
//   driver-adapter  DriverSetup -> DriverActionAdapter -> (the elevated run) -> DriverActionHost.Execute, all product code, with a fake of the
//                   system functions (FakeSystemApi: an in-memory PC, SimulatedDriverStore) and an in-process stand-in for Windows' runas.
//   driver-gate     the real path is compiled out behind DriverChanges (Gate.cs): read from the compiled code (IL) and the source; the
//                   Windows functions it would call exist in this PC's system DLLs (looked up, never called).
// No driver, certificate, certificate store, device or setting of this PC is changed by these groups, and nothing runs elevated.
using CcsBackup.Driver;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using static CcsBackup.Tests.Program;

namespace CcsBackup.Tests
{
    /// <summary>The system functions on an in-memory PC. DiInstallDriver is stood in for as Windows does it: the INF and its catalog must pass
    /// Windows' own catalog reader (CcsCatalog.Check), the signer must be in Root and Trusted Publishers, then the package is staged and used
    /// only on connected devices that have no driver; each outcome the tests need can be chosen.</summary>
    sealed class FakeSystemApi : ISystemDriverApi
    {
        public enum InstallMode { Success, RestartNeeded, Fails, StagesThenFails, CrashAfterStaging }
        // ReportsDoneLeavesPackage: Windows says done, the package stays with no restart flag; UnreadableAfter: removed, then the PC cannot be
        // read; CrashAfterRemoving: removed, then the elevated step ends before anything else (a crash or a kill)
        public enum UninstallMode { Success, RestartNeeded, Fails, ReportsDoneLeavesPackage, UnreadableAfter, CrashAfterRemoving }
        public readonly SimulatedDriverStore Pc; readonly string root;
        public bool IsElevated = true; public string Native;
        public InstallMode Install = InstallMode.Success; public int InstallError = unchecked((int)0xE0000247);
        public UninstallMode Uninstall = UninstallMode.Success;
        public CertStore? FailAdd; public bool FailRemoveCertificate; public int? TrustResult; public string CrashAt;
        public Action BeforeInstall;
        public readonly List<string> Calls = new List<string>(), Folders = new List<string>();
        public byte[] InstalledInf, InstalledCat; public CatalogCheck InstalledCheck; public bool AddedWithKey;
        public FakeSystemApi(SimulatedDriverStore pc, string root) { Pc = pc; this.root = root; }

        public string Kind => "fake";
        public bool Elevated => IsElevated;
        public string NotNative => Native;
        void Step(string s) { Calls.Add(s); if (CrashAt == s) throw new SimulatedCrashException("the elevated step ended at " + s); }

        public string NewWorkFolder() { var f = Path.Combine(root, "work-" + Guid.NewGuid().ToString("N").Substring(0, 8)); if (Directory.Exists(f)) throw new IOException("exists"); Directory.CreateDirectory(f); Folders.Add(f); Step("new-folder"); return f; }
        public void DeleteWorkFolder(string folder) { Step("delete-folder"); if (Directory.Exists(folder)) Directory.Delete(folder, true); }

        public void AddCertificate(CertStore store, byte[] der, string friendly)
        {
            Step("add-cert " + store);
            if (FailAdd == store) throw new CryptographicException(unchecked((int)0x80070005));
            using (var c = new X509Certificate2(der))
            {
                if (c.HasPrivateKey) AddedWithKey = true;
                Pc.CertificateList.Add(new CertificateEntry { Store = store, Thumbprint = UsbId.NormThumbprint(c.Thumbprint), Subject = c.Subject, FriendlyName = friendly ?? "",
                    NotAfterUtc = c.NotAfter.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"), HasPrivateKey = c.HasPrivateKey });
            }
        }

        public bool RemoveCertificate(CertStore store, string thumbprint)
        {
            Step("remove-cert " + store);
            if (!UsbId.IsThumbprint(UsbId.NormThumbprint(thumbprint))) throw new ArgumentException("exact thumbprints only");
            if (FailRemoveCertificate) return false;
            return Pc.CertificateList.RemoveAll(c => c.Store == store && PinnedDriver.Eq(c.Thumbprint, thumbprint)) > 0;
        }

        public int VerifyCatalogTrust(string inf, string cat)
        {
            Step("verify-trust");
            if (TrustResult.HasValue) return TrustResult.Value;
            var k = CcsCatalog.Check(inf, cat);
            if (!k.Ok) return unchecked((int)0x80096010);
            return Pc.CertificateList.Any(c => c.Store == CertStore.Root && PinnedDriver.Eq(c.Thumbprint, k.SignerThumbprint)) ? 0 : unchecked((int)0x800B0109);
        }

        public SystemResult InstallDriverPackage(string inf)
        {
            Step("install");
            BeforeInstall?.Invoke();
            var info = Inf.Parse(Inf.Decode(File.ReadAllBytes(inf)));
            var cat = Path.Combine(Path.GetDirectoryName(inf), info.CatalogFile);
            if (!File.Exists(cat)) return SystemResult.Failed(unchecked((int)0xE000022F));                 // ERROR_NO_CATALOG_FOR_OEM_INF
            var k = CcsCatalog.Check(inf, cat);
            if (!k.Ok) return SystemResult.Failed(unchecked((int)0x80096010));
            if (!(new[] { CertStore.Root, CertStore.TrustedPublisher }).All(s => Pc.CertificateList.Any(c => c.Store == s && PinnedDriver.Eq(c.Thumbprint, k.SignerThumbprint))))
                return SystemResult.Failed(unchecked((int)0x800B0109));
            InstalledInf = File.ReadAllBytes(inf); InstalledCat = File.ReadAllBytes(cat); InstalledCheck = k;
            if (Install == InstallMode.Fails) return SystemResult.Failed(InstallError);
            var p = new DriverPackage { PublishedName = Pc.NextPublishedName(), OriginalName = Path.GetFileName(inf), Provider = info.Provider, ClassGuid = info.ClassGuid, DriverVersion = info.DriverVer,
                DeclaredIds = info.UsbIds, InterfaceGuids = info.InterfaceGuids, Services = info.Services, InfSha256 = Sha(InstalledInf), CatalogName = info.CatalogFile, CatalogSha256 = Sha(InstalledCat),
                SignerSubject = k.SignerSubject, SignerThumbprint = k.SignerThumbprint };
            Pc.PackageList.Add(p);
            if (Install == InstallMode.StagesThenFails) return SystemResult.Failed(InstallError);
            foreach (var d in Pc.DeviceList.Where(d => d.Present && !d.Bound && d.HardwareIds.Any(h => p.DeclaredIds.Any(x => UsbId.Matches(h, PinnedDriver.Vid, PinnedDriver.Pid)))))   // never over another driver
            { Pc.Bind(d, p); if (Install == InstallMode.RestartNeeded) d.NeedsRestart = true; }
            if (Install == InstallMode.CrashAfterStaging) throw new SimulatedCrashException("the elevated step ended after staging");
            return SystemResult.Done(Install == InstallMode.RestartNeeded);
        }

        public SystemResult UninstallDriverPackage(string published)
        {
            Step("uninstall " + published);
            if (!UsbId.IsPublishedName(published)) throw new ArgumentException("one published package only");
            var p = Pc.PackageList.FirstOrDefault(x => PinnedDriver.Eq(x.PublishedName, published));
            if (p == null) return SystemResult.Failed(2);
            if (Uninstall == UninstallMode.Fails) return SystemResult.Failed(5);
            if (Uninstall == UninstallMode.RestartNeeded)
            { p.PendingRemoval = true; foreach (var d in Pc.DeviceList.Where(d => PinnedDriver.Eq(d.InfPath, published))) d.NeedsRestart = true; return SystemResult.Done(true); }
            if (Uninstall == UninstallMode.ReportsDoneLeavesPackage) return SystemResult.Done(false);
            Pc.PackageList.Remove(p);
            foreach (var d in Pc.DeviceList.Where(d => PinnedDriver.Eq(d.InfPath, published))) { d.InfPath = ""; d.Service = ""; d.Provider = ""; d.InterfaceGuids.Clear(); }
            if (Uninstall == UninstallMode.UnreadableAfter) Pc.FailReads = true;
            if (Uninstall == UninstallMode.CrashAfterRemoving) throw new SimulatedCrashException("the elevated step ended after Windows removed the package, before its certificates");
            return SystemResult.Done(false);
        }
    }

    /// <summary>Windows' runas, in this process: the arguments go to DriverActionHost.Execute as the elevated program's command line;
    /// Refuse stands for the person refusing the permission prompt; a step that ends without finishing gives the exit code of a crashed process.</summary>
    sealed class InProcessRunner : IElevatedRunner
    {
        public FakeSystemApi Api; public IDriverStore Store; public bool Refuse; public int Runs; public string LastArguments; public Action BeforeStep; public int LastExit;
        public bool WindowEndsWithStep;   // a crash of the step ends the window too (nothing after it runs there either)
        public int Run(string arguments)
        {
            Runs++; LastArguments = arguments;
            if (Refuse) throw new ElevationCanceledException();
            BeforeStep?.Invoke();
            try { return LastExit = DriverActionHost.Execute(arguments.Split(' '), Api, Store); }
            catch (SimulatedCrashException) { LastExit = unchecked((int)0xE0434352); if (WindowEndsWithStep) throw; return LastExit; }
        }
    }

    static partial class Tests
    {
        sealed class AdPc { public SimulatedDriverStore Pc; public FakeSystemApi Api; public InProcessRunner Runner; public DriverActionAdapter Adapter; public SetupContext Ctx; public string Dir; }

        static AdPc NewAdapterPc(string name, bool consoleConnected = true)
        {
            var dir = NewDir("adapter-" + name);
            var pc = new SimulatedDriverStore();
            pc.AddCertificate(CertStore.Root, SimulatedDriverStore.FakeThumbprint("root-a"), "CN=Example Root CA");
            pc.AddCertificate(CertStore.TrustedPublisher, SimulatedDriverStore.FakeThumbprint("pub-a"), "CN=Example Publisher");
            pc.AddPackage("othermouse.inf", "Contoso", "mouhid", null, SimulatedDriverStore.FakeThumbprint("contoso"), @"USB\VID_045E&PID_0040");
            pc.InboxWinUsb();
            if (consoleConnected) pc.AddDevice("5&1a2b3c4d&0&2", true);
            var api = new FakeSystemApi(pc, dir);
            var runner = new InProcessRunner { Api = api, Store = pc };
            var ad = new DriverActionAdapter(runner);
            return new AdPc { Pc = pc, Api = api, Runner = runner, Adapter = ad, Dir = dir, Ctx = new SetupContext { Store = pc, Actions = ad, Ledger = new DriverLedger(dir), Elevation = ElevationState.CanElevate, ToolVersion = "test" } };
        }

        static string Unrelated(AdPc p) => string.Join("|", p.Pc.CertificateList.Where(c => c.Subject.StartsWith("CN=Example")).Select(c => c.Key)) + "|" + string.Join("|", p.Pc.PackageList.Where(x => x.OriginalName == "othermouse.inf" || x.IsInbox).Select(x => x.PublishedName + x.InfSha256));
        static string StoreDigest(DriverSnapshot s) => string.Join(" / ", s.StoreSummary.OrderBy(kv => kv.Key).Select(kv => kv.Key + " " + kv.Value));
        static DriverRecord SetUp(AdPc p) { var o = DriverSetup.Run(p.Ctx); if (o.Record == null || !o.Record.Installed) throw new Exception("setup did not install: " + o.Result + " " + o.Message); return o.Record; }

        // ================================================================== driver-adapter
        public static void DriverAdapter()
        {
            // ---------------------------------------------------------- nothing on the PC: setup, close offer, removal, all through the real adapter
            {
                var p = NewAdapterPc("absent");
                var before = DriverSnapshot.Capture(p.Pc); var unrelated = Unrelated(p);
                string seen = null;
                p.Runner.BeforeStep = () => { var r = p.Ctx.Ledger.ReadAll().First().Record; var s = r.Steps.Last(); seen = r.State + "/" + s.Name + "/" + (s.EndedUtc == null ? "open" : "ended") + "/" + (r.Plan?.OperationId == r.OperationId); };
                var o = DriverSetup.Run(p.Ctx);
                var rec = o.Record;
                Check(seen == "open/install/open/True", "the record, its install step and the plan's operation id are on disk before the elevated step starts (" + seen + ")");
                Check(o.Result == SetupResult.Installed && o.Message == DriverText.SetupDoneConnected && p.Runner.Runs == 1 && p.Runner.LastExit == DriverExit.Done,
                      "setup through the elevated step: installed and the console uses it (" + o.Result + ", exit " + p.Runner.LastExit + ")");
                var pkg = p.Pc.Package(rec.OwnedPackage);
                Check(pkg != null && CcsDriver.IsCcsPackage(pkg) && CcsDriver.IsCcsSubject(pkg.SignerSubject, rec.OperationId) && pkg.InfSha256 == CcsDriver.InfSha256 && pkg.Services.SequenceEqual(new[] { "WinUSB" })
                      && pkg.InterfaceGuids.SequenceEqual(new[] { CcsDriver.InterfaceGuid }) && p.Api.InstalledInf.SequenceEqual(CcsDriver.InfBytes()) && p.Api.InstalledCheck.Ok,
                      "the package staged is exactly Workbench's INF (fixed bytes), Windows' WinUSB with its interface GUID, signed by this operation's certificate");
                Check(rec.IntroducedCertificates.Count == 2 && rec.IntroducedCertificates.All(c => c.Thumbprint == pkg.SignerThumbprint && c.Subject == CcsDriver.SubjectFor(rec.OperationId) && !c.HasPrivateKey)
                      && rec.IntroducedCertificates.Select(c => c.Store).OrderBy(x => x).SequenceEqual(new[] { CertStore.Root, CertStore.TrustedPublisher }) && !p.Api.AddedWithKey,
                      "the record owns exactly its certificate in Root and Trusted Publishers, added without its private key, named for this operation");
                Check(rec.BoundByOperation.Count == 1 && rec.Ownership.Contains("this operation's own certificate") && rec.Plan.IsCcs && rec.Plan.CertificateSubject == CcsDriver.SubjectFor(rec.OperationId)
                      && rec.Reported.Reported == Reported.Success && rec.Steps.Select(s => s.Name).SequenceEqual(new[] { "install", "read-back" }),
                      "the record: the console instance it bound, why ownership holds, the plan, what the step reported, the steps");
                Check(p.Api.Calls.SequenceEqual(new[] { "new-folder", "add-cert Root", "add-cert TrustedPublisher", "verify-trust", "install", "delete-folder" }) && p.Api.Folders.All(f => !Directory.Exists(f)),
                      "the elevated step's order: a new working folder, the certificate in both stores, Windows' trust check, the install, the folder removed (" + string.Join(", ", p.Api.Calls) + ")");
                var arg = p.Runner.LastArguments.Split(' ');
                var req = DriverActionRequest.Decode(arg[1]);
                Check(arg.Length == 2 && arg[0] == DriverActionHost.Flag && arg[1].All(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_') && req.Action == "setup" && req.OperationId == rec.OperationId
                      && !Encoding.UTF8.GetString(Convert.FromBase64String(arg[1].Replace('-', '+').Replace('_', '/') + new string('=', (4 - arg[1].Length % 4) % 4))).Contains(":\\\\"),
                      "the command line: the flag and one encoded request (letters, digits, '-', '_'), the setup of this operation, with no file path in it");
                var after = rec.After; var diff = DriverSetup.Diff(before, after);
                Check(diff.UnexplainedStores.Count == 0 && diff.NewCertificates.Count == 2 && diff.RemovedCertificates.Count == 0 && diff.NewPackages.Count == 1 && Unrelated(p) == unrelated
                      && rec.Changes.Any(w => w.Contains(pkg.SignerThumbprint) && w.Contains("Trusted Root")) && rec.Changes.Any(w => w.Contains(pkg.SignerThumbprint) && w.Contains("Trusted Publishers")),
                      "trust audit: Root and Trusted Publishers each gained exactly the listed certificate; nothing unrelated changed (" + StoreDigest(before) + " -> " + StoreDigest(after) + ")");
                var a = DriverSetup.Assess(p.Pc);
                Check(a.Kind == AssociationKind.CcsWinUsb && a.Reusable && a.Message == DriverText.PreExisting, "after setup the PC has Workbench's association, which a later start reuses (" + a.Kind + ")");
                var close = DriverSetup.AtClose(rec, p.Pc, new DriverUsage(), ElevationState.CanElevate);
                Check(close.Kind == CloseKind.OfferRemoval && close.DefaultButton == 0 && close.Buttons[0] == DriverText.KeepButton && close.Decision.Plan.PublishedName == rec.OwnedPackage
                      && close.Decision.Plan.Certificates.Count == 2 && close.Decision.Plan.Devices.SequenceEqual(rec.BoundByOperation),
                      "close: removal offered, No - Keep Driver the default; the plan names one package, exact thumbprints and the devices it bound");
                var ro = DriverSetup.Remove(p.Ctx, rec, new DriverUsage());
                var removed = DriverSnapshot.Capture(p.Pc);
                Check(ro.Result == RemovalResult.Removed && p.Pc.Package(rec.OwnedPackage) == null && !p.Pc.CertificateList.Any(c => c.Thumbprint == pkg.SignerThumbprint) && p.Runner.Runs == 2
                      && ro.Message == DriverText.Removed + " " + DriverText.CertificateRemoved + " " + DriverText.RemovedLogsRemainCcs,
                      "removal through the elevated step: the package and its certificate are gone (read back), said plainly (" + ro.Message + ")");
                Check(StoreDigest(removed) == StoreDigest(before) && Unrelated(p) == unrelated && removed.Packages.Count(x => !x.IsInbox) == before.Packages.Count(x => !x.IsInbox)
                      && p.Api.Calls.Skip(6).SequenceEqual(new[] { "uninstall " + rec.OwnedPackage, "remove-cert Root", "remove-cert TrustedPublisher" }),
                      "trust audit after removal: both stores are back to their exact earlier contents (count and digest); the removal named one package and two exact thumbprints");
                var r2 = DriverLedger.Load(rec.File);
                Check(r2.Removal == RemovalResult.Removed && r2.RemovalState == "finished" && r2.RemovalReported.Reported == Reported.Success && DriverSetup.Reconcile(p.Ctx.Ledger, p.Pc).Items.Count == 0,
                      "the record keeps the removal; the next start has nothing to explain");
                var rq = DriverActionRequest.Decode(p.Runner.LastArguments.Split(' ')[1]);
                Check(rq.Action == "remove" && rq.Removal.PublishedName == rec.OwnedPackage && rq.Removal.Certificates.All(c => UsbId.IsThumbprint(c.Thumbprint)) && rq.Removal.Problems().Count == 0,
                      "the removal request names one published package and exact thumbprints only");
            }

            // ---------------------------------------------------------- console not connected: staged, still its own
            {
                var p = NewAdapterPc("noconsole", consoleConnected: false);
                var o = DriverSetup.Run(p.Ctx);
                Check(o.Result == SetupResult.Installed && o.Message == DriverText.SetupDone && o.Record.BoundByOperation.Count == 0
                      && DriverSetup.AtClose(o.Record, p.Pc, new DriverUsage(), ElevationState.CanElevate).Kind == CloseKind.OfferRemoval,
                      "no console connected: the package is staged for later, the plain done text, and it is still offered for removal at close");
            }

            // ---------------------------------------------------------- reuse: nothing is run over a compatible association
            {
                var p = NewAdapterPc("reuse-hakchi");
                var t = SimulatedDriverStore.FakeThumbprint("hakchi"); p.Pc.AddCertificate(CertStore.Root, t, PinnedDriver.CertificateSubject, "libwdi");
                var hk = p.Pc.AddPinnedPackage(t); p.Pc.Bind(p.Pc.DeviceList[0], hk);
                var o = DriverSetup.Run(p.Ctx);
                Check(o.Result == SetupResult.NotNeeded && o.Message == DriverText.PreExisting && p.Runner.Runs == 0 && p.Ctx.Ledger.ReadAll().Count == 0,
                      "the console already uses Hakchi's driver: reused, no elevated step, no setup record");
            }
            {
                var p = NewAdapterPc("reuse-own");
                var rec = SetUp(p); DriverSetup.Keep(p.Ctx, rec);
                var again = DriverSetup.Run(p.Ctx); var a = DriverSetup.Assess(p.Pc);
                var reuse = DriverSetup.RecordReuse(p.Ctx, a);
                Check(again.Result == SetupResult.NotNeeded && p.Runner.Runs == 1 && a.Kind == AssociationKind.CcsWinUsb && a.Notes.Any(n => n.Contains("from an earlier setup"))
                      && DriverSetup.AtClose(reuse, p.Pc, new DriverUsage(), ElevationState.CanElevate).Message == DriverText.PreExisting,
                      "Workbench's own package from an earlier setup (kept): reused next time as already on the PC; never offered for removal by the reusing session");
            }
            {
                var p = NewAdapterPc("reuse-zadig");
                var z = p.Pc.AddPackage("nes_fel.inf", "libwdi", "WinUSB", "{1B5D0E8A-0000-4000-8000-123456789ABC}", SimulatedDriverStore.FakeThumbprint("zadig"), PinnedDriver.HardwareId);
                p.Pc.Bind(p.Pc.DeviceList[0], z);
                Check(DriverSetup.Run(p.Ctx).Result == SetupResult.NotNeeded && p.Runner.Runs == 0 && p.Pc.DeviceList[0].InfPath == z.PublishedName, "another program's WinUSB association with an interface: reused as it is");
            }
            {
                var p = NewAdapterPc("staged-other", consoleConnected: false);
                var z = p.Pc.AddPackage("nes_fel.inf", "libwdi", "WinUSB", "{1B5D0E8A-0000-4000-8000-123456789ABC}", SimulatedDriverStore.FakeThumbprint("zadig2"), PinnedDriver.HardwareId);
                var o = DriverSetup.Run(p.Ctx);
                Check(o.Result == SetupResult.NotNeeded && p.Runner.Runs == 0 && p.Pc.Package(z.PublishedName) != null, "a package already staged for the console (console absent) is pre-existing: no setup over it");
            }

            // ---------------------------------------------------------- refusal: competing drivers are never replaced
            foreach (var (svc, guid, label) in new[] { ("libusbK", "{2B5D0E8A-0000-4000-8000-123456789ABC}", "libusbK"), ("libusb0", null, "libusb-win32"), ("usbccgp", null, "usbccgp") })
            {
                var p = NewAdapterPc("compete-" + svc);
                var k = p.Pc.AddPackage(svc + "_fel.inf", "libwdi", svc, guid, SimulatedDriverStore.FakeThumbprint(svc), PinnedDriver.HardwareId); p.Pc.Bind(p.Pc.DeviceList[0], k);
                var o = DriverSetup.Run(p.Ctx);
                Check(o.Result == SetupResult.Blocked && o.Message == string.Format(DriverText.IncompatibleFormat, label) && p.Runner.Runs == 0 && p.Api.Calls.Count == 0 && p.Pc.DeviceList[0].InfPath == k.PublishedName,
                      "a competing " + svc + " driver in use: refused before any elevated step, never replaced");
            }
            {
                var p = NewAdapterPc("compete-inbox");
                p.Pc.Bind(p.Pc.DeviceList[0], p.Pc.InboxWinUsb(), withInterface: false);
                Check(DriverSetup.Run(p.Ctx).Result == SetupResult.Blocked && p.Runner.Runs == 0, "Windows' WinUSB bound without an interface: refused, not changed");
            }
            {   // the PC changes between the window's check and the elevated step: the step checks again and changes nothing
                var p = NewAdapterPc("compete-late");
                DriverPackage k = null;
                p.Runner.BeforeStep = () => { k = p.Pc.AddPackage("libusbk_fel.inf", "libwdi", "libusbK", "{2B5D0E8A-0000-4000-8000-123456789ABC}", SimulatedDriverStore.FakeThumbprint("late"), PinnedDriver.HardwareId); p.Pc.Bind(p.Pc.DeviceList[0], k); };
                var o = DriverSetup.Run(p.Ctx);
                Check(o.Result == SetupResult.Blocked && p.Runner.LastExit == DriverExit.StateChanged && o.Record.Reported.Reported == Reported.Refused && p.Api.Calls.Count == 0
                      && o.Message == DriverText.SetupStateChanged + " " + string.Format(DriverText.IncompatibleFormat, "libusbK") && p.Pc.DeviceList[0].InfPath == k.PublishedName
                      && DriverSetup.AtClose(o.Record, p.Pc, new DriverUsage(), ElevationState.CanElevate).Kind == CloseKind.Nothing,
                      "a competing driver appears after the window's check: the elevated step refuses before changing anything (" + o.Message + ")");
            }

            // ---------------------------------------------------------- cancellation at the administrator prompt; no elevation; wrong processor
            {
                var p = NewAdapterPc("uac-cancel"); p.Runner.Refuse = true;
                var before = DriverSnapshot.Capture(p.Pc);
                var o = DriverSetup.Run(p.Ctx);
                Check(o.Result == SetupResult.ElevationRefused && o.Message == DriverText.SetupRefused + " " + DriverText.NothingChanged && o.Record.Reported.ExitCode == DriverExit.Canceled
                      && o.Record.Changes.Count == 0 && p.Api.Calls.Count == 0 && StoreDigest(DriverSnapshot.Capture(p.Pc)) == StoreDigest(before)
                      && DriverSetup.AtClose(o.Record, p.Pc, new DriverUsage(), ElevationState.CanElevate).Kind == CloseKind.Nothing,
                      "the person refuses Windows' administrator prompt: nothing changed, said plainly, nothing offered at close");
            }
            {
                var p = NewAdapterPc("not-elevated"); p.Api.IsElevated = false;
                var o = DriverSetup.Run(p.Ctx);
                Check(o.Result == SetupResult.ElevationRefused && p.Runner.LastExit == DriverExit.NotElevated && p.Api.Calls.Count == 0, "a step that somehow runs without administrator permission changes nothing");
                var q = NewAdapterPc("not-native"); q.Api.Native = "an x64 program on an Arm-based PC";
                var o2 = DriverSetup.Run(q.Ctx);
                Check(o2.Result == SetupResult.Failed && q.Runner.LastExit == DriverExit.NotNative && q.Api.Calls.Count == 0 && o2.Message.Contains("not made for this PC's processor") && o2.Message.EndsWith(DriverText.NothingChanged),
                      "a step built for another processor refuses before changing anything (" + o2.Message + ")");
            }

            // ---------------------------------------------------------- a failed install is taken back
            {
                var p = NewAdapterPc("install-fails"); p.Api.Install = FakeSystemApi.InstallMode.Fails;
                var before = DriverSnapshot.Capture(p.Pc);
                var o = DriverSetup.Run(p.Ctx);
                Check(o.Result == SetupResult.Failed && p.Runner.LastExit == unchecked((int)0xE0000247) && o.Message.StartsWith("The USB driver could not be set up (Windows: ") && o.Message.Contains("Windows: the driver package could not be added to the driver store (error 0xE0000247)")
                      && o.Message.EndsWith(DriverText.NothingChanged), "Windows refuses the install: its own words and number, and nothing changed (" + o.Message + ")");
                Check(p.Api.Calls.SequenceEqual(new[] { "new-folder", "add-cert Root", "add-cert TrustedPublisher", "verify-trust", "install", "remove-cert TrustedPublisher", "remove-cert Root", "delete-folder" })
                      && StoreDigest(DriverSnapshot.Capture(p.Pc)) == StoreDigest(before) && o.Record.Changes.Count == 0 && !p.Pc.CertificateList.Any(c => CcsDriver.IsCcsSubject(c.Subject)),
                      "the step takes back its certificate (exact thumbprint, both stores, reverse order); both stores are as before (" + string.Join(", ", p.Api.Calls) + ")");
            }
            {
                var p = NewAdapterPc("trust-fails"); p.Api.TrustResult = unchecked((int)0x800B0109);
                var o = DriverSetup.Run(p.Ctx);
                Check(o.Result == SetupResult.Failed && p.Runner.LastExit == unchecked((int)0x800B0109) && !p.Api.Calls.Contains("install") && o.Record.Changes.Count == 0 && o.Message.Contains("0x800B0109"),
                      "Windows does not trust the package even with its certificate added (a policy on the PC): no install, the certificate taken back (" + o.Message + ")");
            }
            {
                var p = NewAdapterPc("cert-add-fails"); p.Api.FailAdd = CertStore.TrustedPublisher;
                var o = DriverSetup.Run(p.Ctx);
                Check(o.Result == SetupResult.Failed && p.Runner.LastExit == unchecked((int)0x80070005) && o.Record.Changes.Count == 0 && !p.Api.Calls.Contains("install")
                      && p.Api.Calls.Contains("remove-cert Root") && !p.Pc.CertificateList.Any(c => CcsDriver.IsCcsSubject(c.Subject)),
                      "Trusted Publishers refuses the certificate: the Root copy is taken back, no install (" + o.Message + ")");
            }
            {
                var p = NewAdapterPc("stages-then-fails"); p.Api.Install = FakeSystemApi.InstallMode.StagesThenFails;
                var o = DriverSetup.Run(p.Ctx);
                Check(p.Runner.LastExit == DriverExit.TakeBackIncomplete && !p.Api.Calls.Any(c => c.StartsWith("remove-cert")) && o.Record.Reported.Reported == Reported.Failed
                      && o.Result == SetupResult.Installed && o.Record.OwnedPackage != null && o.Record.IntroducedCertificates.Count == 2,
                      "Windows staged the package but reported failure: the certificate is kept (the package needs it), and the read-back decides (" + o.Result + ")");
            }

            // ---------------------------------------------------------- restart pending
            {
                var p = NewAdapterPc("restart"); p.Api.Install = FakeSystemApi.InstallMode.RestartNeeded;
                var o = DriverSetup.Run(p.Ctx);
                var close = DriverSetup.AtClose(o.Record, p.Pc, new DriverUsage(), ElevationState.CanElevate);
                Check(o.Result == SetupResult.InstalledRestartPending && p.Runner.LastExit == DriverExit.DoneRestartNeeded && o.Message == DriverText.SetupRestart
                      && close.Kind == CloseKind.Message && close.Message == DriverText.Kept(DriverText.Because(RetainReason.RestartPending)),
                      "Windows needs a restart after setup: said plainly, never restarted, no removal offered meanwhile");
            }
            {   // the certificates around it: DriverTrustCleanup below
                var p = NewAdapterPc("removal-restart"); var rec = SetUp(p); p.Api.Uninstall = FakeSystemApi.UninstallMode.RestartNeeded;
                var ro = DriverSetup.Remove(p.Ctx, rec, new DriverUsage());
                var rc = DriverSetup.Reconcile(p.Ctx.Ledger, p.Pc);
                Check(ro.Result == RemovalResult.RemovedRestartPending && ro.Message == DriverText.RemovalRestart + " " + DriverText.CertificateKeptUntilFinished && p.Runner.LastExit == DriverExit.CertificateDeferredRestartNeeded
                      && rc.Items.Count == 1 && rc.Items[0].Status == "removal-waiting-for-restart", "a removal Windows finishes only after a restart is never called finished; the next start says so");
            }

            // ---------------------------------------------------------- removal refused or failed
            {
                var p = NewAdapterPc("remove-uac"); var rec = SetUp(p); p.Runner.Refuse = true;
                var ro = DriverSetup.Remove(p.Ctx, rec, new DriverUsage());
                Check(ro.Result == RemovalResult.ElevationRefused && ro.Message == DriverText.Kept(DriverText.Because(RetainReason.ElevationRefused)) && p.Pc.Package(rec.OwnedPackage) != null,
                      "the removal's administrator prompt refused: kept, said plainly");
                p.Runner.Refuse = false; p.Api.Uninstall = FakeSystemApi.UninstallMode.Fails;
                var rf = DriverSetup.Remove(p.Ctx, DriverLedger.Load(rec.File), new DriverUsage());
                Check(rf.Result == RemovalResult.Failed && rf.Message.StartsWith("The USB driver was kept because Windows could not remove it (Windows: ") && p.Pc.CertificateList.Count(c => CcsDriver.IsCcsSubject(c.Subject)) == 2,
                      "Windows refuses the removal: kept with Windows' words; the certificate stays with it (" + rf.Message + ")");
            }
            {
                var p = NewAdapterPc("remove-cert-fails"); var rec = SetUp(p); p.Api.FailRemoveCertificate = true;
                var ro = DriverSetup.Remove(p.Ctx, rec, new DriverUsage()); var r = DriverLedger.Load(rec.File);
                Check(ro.Result == RemovalResult.Partial && ro.Message == DriverText.Removed + " " + string.Format(DriverText.CertificateKeptFormat, DriverText.Because(RetainReason.RemovalFailed)) + " " + DriverText.CleanupLater
                      && p.Runner.LastExit == DriverExit.CertificateKept && p.Pc.Package(rec.OwnedPackage) == null && p.Pc.CertificateList.Count(c => CcsDriver.IsCcsSubject(c.Subject)) == 2
                      && r.CleanupState == "deferred" && r.DeferredCertificates.Count == 2 && !r.CleanupRestartFirst,
                      "the package went but Windows did not remove the certificate: said exactly so, and the cleanup is deferred (" + ro.Message + ")");
                p.Api.FailRemoveCertificate = false;
                var co = DriverSetup.CleanUp(p.Ctx, DriverLedger.Load(rec.File), new DriverUsage());
                Check(co.Result == CleanupResult.Removed && !p.Pc.CertificateList.Any(c => CcsDriver.IsCcsSubject(c.Subject)) && DriverLedger.Load(rec.File).CleanupState == "finished",
                      "a later cleanup, asked for, removes it once Windows allows (" + co.Message + ")");
            }

            // ---------------------------------------------------------- never offered: shared, active, uncertain, embedded parent, changed
            {
                var p = NewAdapterPc("shared"); var rec = SetUp(p);
                var other = p.Pc.AddDevice("5&99&0&4", false); p.Pc.Bind(other, p.Pc.Package(rec.OwnedPackage));
                var ro = DriverSetup.Remove(p.Ctx, rec, new DriverUsage());
                Check(ro.Result == RemovalResult.NotEligible && ro.Decision.Reasons.Contains(RetainReason.SharedWithOtherDevice) && p.Runner.Runs == 1, "another device instance uses the package: never removed, no elevated step");
            }
            {
                var p = NewAdapterPc("active"); var rec = SetUp(p);
                foreach (var (u, r) in new[] { (new DriverUsage { ConsoleOperationActive = true }, RetainReason.ConsoleOperationActive), (new DriverUsage { ConsoleWriteInProgress = true }, RetainReason.ConsoleWriteInProgress),
                    (new DriverUsage { Host = HostKind.Embedded, ParentAppNeedsDevice = true }, RetainReason.ParentAppNeedsIt), (new DriverUsage { OtherWorkbenchOperation = true }, RetainReason.AnotherWorkbenchOperation),
                    (new DriverUsage { OtherProgramsOpen = { "Hakchi" } }, RetainReason.OtherProgramMayUseIt) })
                {
                    var close = DriverSetup.AtClose(rec, p.Pc, u, ElevationState.CanElevate); var ro = DriverSetup.Remove(p.Ctx, rec, u);
                    Check(close.Kind == CloseKind.Message && close.Message == DriverText.Kept(DriverText.Because(r)) && ro.Result == RemovalResult.NotEligible && p.Runner.Runs == 1 && p.Pc.Package(rec.OwnedPackage) != null,
                          r + ": kept, and the embedded window's or any close never removes it (" + close.Message + ")");
                }
            }
            {   // two packages during the setup: the step's own is staged, but ownership cannot be shown
                var p = NewAdapterPc("concurrent");
                p.Api.BeforeInstall = () => p.Pc.AddPackage("someone_else.inf", "Contoso", "WinUSB", "{4B5D0E8A-0000-4000-8000-123456789ABC}", SimulatedDriverStore.FakeThumbprint("concurrent"), PinnedDriver.HardwareId);
                var o = DriverSetup.Run(p.Ctx);
                Check(o.Result == SetupResult.InstalledNotOwned && o.Record.OwnedPackage == null && DriverSetup.AtClose(o.Record, p.Pc, new DriverUsage(), ElevationState.CanElevate).Message == DriverText.Kept(DriverText.Because(RetainReason.OwnershipUncertain)),
                      "another package appeared during the setup: nothing is claimed, nothing offered (" + o.Result + ")");
            }
            {
                var p = NewAdapterPc("changed"); var rec = SetUp(p);
                p.Pc.Package(rec.OwnedPackage).CatalogSha256 = SimulatedDriverStore.FakeHash("re-signed by someone");
                Check(Eligibility.Evaluate(rec, DriverSnapshot.Capture(p.Pc), new DriverUsage()).Reasons.Contains(RetainReason.PackageChanged), "the package changed since setup (catalog): kept");
            }

            // ---------------------------------------------------------- the elevated step's own checks, given stale or crafted removal requests
            {
                var p = NewAdapterPc("crafted"); var rec = SetUp(p); var plan = Eligibility.Evaluate(rec, DriverSnapshot.Capture(p.Pc), new DriverUsage()).Plan;
                int Exec(RemovalPlan rp) => DriverActionHost.Execute(new[] { DriverActionHost.Flag, DriverActionRequest.ForRemoval(rp).Encode() }, p.Api, p.Pc);
                RemovalPlan Copy() => RemovalPlan.From(Json.Parse(Json.Write(plan.ToJson())));
                var t = SimulatedDriverStore.FakeThumbprint("hakchi-x"); var hk = p.Pc.AddPinnedPackage(t);
                var calls = p.Api.Calls.Count;
                var c1 = Copy(); c1.PublishedName = hk.PublishedName; c1.InfSha256 = hk.InfSha256; c1.CatalogSha256 = hk.CatalogSha256; c1.Certificates.Clear();
                var c2 = Copy(); c2.Certificates[0].Thumbprint = SimulatedDriverStore.FakeThumbprint("root-a");
                var e1 = Exec(c1); var e2 = Exec(c2);
                var c3 = Copy(); var extra = p.Pc.AddDevice("5&77&0&9", false); p.Pc.Bind(extra, p.Pc.Package(rec.OwnedPackage));
                var e3 = Exec(c3);
                Check(e1 == DriverExit.NotTheRecordedPackage && e2 == DriverExit.NotTheRecordedPackage && e3 == DriverExit.StateChanged && p.Api.Calls.Count == calls
                      && p.Pc.Package(hk.PublishedName) != null && p.Pc.Package(rec.OwnedPackage) != null && p.Pc.CertificateList.Any(c => c.Thumbprint == SimulatedDriverStore.FakeThumbprint("root-a")),
                      "the elevated step refuses a request naming Hakchi's package, an unrelated certificate, or a package another device now uses; nothing is touched");
                var bad = new[] { "", "not base64!", DriverActionRequest.ForRemoval(new RemovalPlan { OperationId = rec.OperationId, PublishedName = "winusb.inf", InfSha256 = "a", CatalogSha256 = "b" }).Encode(),
                    new DriverActionRequest { Action = "setup", OperationId = rec.OperationId, Plan = DriverSetupPlan.PinnedHakchi() }.Encode() };
                var pinned = DriverSetupPlan.CcsWinUsb(); pinned.OperationId = rec.OperationId; pinned.InfSha256 = SimulatedDriverStore.FakeHash("another INF");
                bad = bad.Concat(new[] { DriverActionRequest.ForSetup(pinned).Encode(), new DriverActionRequest { Action = "format", OperationId = rec.OperationId }.Encode() }).ToArray();
                Check(bad.All(b => DriverActionHost.Execute(new[] { DriverActionHost.Flag, b }, p.Api, p.Pc) == DriverExit.BadRequest) && p.Api.Calls.Count == calls
                      && DriverActionHost.Execute(new[] { "--something-else" }, p.Api, p.Pc) == DriverExit.BadRequest,
                      "malformed, inbox-INF, pinned-helper, other-INF and unknown requests: refused as bad requests before anything");
            }

            // ---------------------------------------------------------- the elevated step ends without finishing
            {
                var p = NewAdapterPc("crash-after-cert"); p.Api.CrashAt = "verify-trust";
                var o = DriverSetup.Run(p.Ctx);
                var rc = DriverSetup.Reconcile(p.Ctx.Ledger, p.Pc);
                Check(p.Runner.LastExit == unchecked((int)0xE0434352) && o.Result == SetupResult.Partial && o.Message.Contains("added to Trusted Root") && o.Record.OwnedPackage == null
                      && DriverSetup.AtClose(o.Record, p.Pc, new DriverUsage(), ElevationState.CanElevate).Message == DriverText.Kept(DriverText.Because(RetainReason.SetupNotConfirmed))
                      && rc.Items.Count == 1 && rc.Items[0].Status == "partial", "the step ends after adding its certificate: the window reads back and names it; kept and explained at the next start (" + o.Message + ")");
            }
            {
                var p = NewAdapterPc("crash-after-staging"); p.Api.Install = FakeSystemApi.InstallMode.CrashAfterStaging;
                var o = DriverSetup.Run(p.Ctx);
                Check(o.Record.Reported.Reported == Reported.Failed && o.Result == SetupResult.Installed && o.Record.OwnedPackage != null, "the step ends after Windows staged the package: the read-back finds it installed and Workbench's");
            }
            {   // the window itself closes during the elevated step
                var p = NewAdapterPc("window-crash");
                p.Runner.BeforeStep = () => throw new SimulatedCrashException("the window ended during the elevated step");
                try { DriverSetup.Run(p.Ctx); } catch (SimulatedCrashException) { }
                var rec = p.Ctx.Ledger.ReadAll().Single().Record;
                var rc = DriverSetup.Reconcile(p.Ctx.Ledger, p.Pc);
                Check(rec.SetupIncomplete && Eligibility.Evaluate(rec, DriverSnapshot.Capture(p.Pc), new DriverUsage()).Reasons.Contains(RetainReason.RecordIncomplete)
                      && rc.Items.Count == 1 && rc.Items[0].Status == "incomplete-setup", "the window ends during the step: its record stays open, never eligible, explained at the next start");
            }

            // ---------------------------------------------------------- exit codes and texts
            {
                var gate = DriverExit.Report(DriverExit.GateClosed);
                Check(gate.Reported == Reported.Unavailable && gate.Detail == CcsBackup.Gate.DriverText && DriverExit.Report(DriverExit.Done).Reported == Reported.Success
                      && DriverExit.Report(DriverExit.DoneRestartNeeded).Reported == Reported.SuccessRestartNeeded && DriverExit.Report(5).Detail.StartsWith("Windows: Access is denied")
                      && DriverExit.Report(unchecked((int)0xE0000248)).Detail == "Windows: a policy on this PC does not allow setting up this device (error 0xE0000248)"
                      && new[] { DriverExit.BadRequest, DriverExit.NotNative, DriverExit.PackageInvalid, DriverExit.TakeBackIncomplete, DriverExit.Unexpected }.All(c => DriverExit.Report(c).Reported == Reported.Failed)
                      && new[] { DriverExit.StateChanged, DriverExit.NotTheRecordedPackage, DriverExit.Unreadable, DriverExit.CertificateStillNeeded, DriverExit.NotTheRecordedCertificate }.All(c => DriverExit.Report(c).Reported == Reported.Refused)
                      && DriverExit.Report(DriverExit.CertificateDeferred).Reported == Reported.Success && DriverExit.Report(DriverExit.CertificateDeferredRestartNeeded).Reported == Reported.SuccessRestartNeeded
                      && DriverExit.Report(DriverExit.CertificateDeferred).Detail.Contains("certificate was kept") && DriverExit.Report(DriverExit.CertificateDeferredRestartNeeded).Detail.Contains("certificate was kept"),
                      "each exit code maps to its report; a Windows error carries Windows' own words (" + DriverExit.Report(5).Detail + ")");
                var all = DriverText.CcsPackageChanges.Concat(new[] { DriverText.SetupStateChanged, DriverText.SetupStateChangedReusable, DriverText.RemovedLogsRemainCcs, DriverText.SetupUnavailableNote })
                    .Concat(new[] { DriverExit.BadRequest, DriverExit.NotElevated, DriverExit.NotNative, DriverExit.StateChanged, DriverExit.PackageInvalid, DriverExit.NotTheRecordedPackage, DriverExit.TakeBackIncomplete, DriverExit.Unexpected, DriverExit.Unreadable,
                        DriverExit.CertificateDeferred, DriverExit.CertificateDeferredRestartNeeded, DriverExit.CertificateStillNeeded, DriverExit.NotTheRecordedCertificate }.Select(c => DriverExit.Report(c).Detail)).ToList();
                var uk = new[] { "colour", "behaviour", "recognise", "organise", "licence", "cancelled", "favourite", "analyse", "centre" };
                Check(all.All(x => !uk.Any(w => x.ToLowerInvariant().Contains(w))) && all.All(x => !x.Contains("ROM")), "the new driver texts are US English");
                var d = DriverText.SetupDetails(DriverSetupPlan.CcsWinUsb());
                Check(d.Any(x => x.Contains("ccs_console_usb.inf") && x.Contains("no driver program") && x.Contains("WinUSB")) && d.Any(x => x.Contains("Trusted Root Certification Authorities") && x.Contains("Trusted Publishers") && x.Contains("never saved"))
                      && d.Any(x => x.Contains("temporary folder") && x.Contains("logs")) && !d.Any(x => x.Contains("System32") || x.Contains("libwdi")),
                      "the details before consent name Workbench's actual PC changes, the trust certificate included, and nothing of Hakchi's helper");
            }

            DriverTrustCleanup();
        }

        // ================================================================== driver-adapter: the trust certificate after the package's removal
        // Every case runs the real elevated step (DriverActionHost.Execute through DriverActionAdapter and the in-process runas) on the
        // in-memory PC, and asserts the certificate invariant on the PC itself (Root and Trusted Publishers read directly), not only the words.
        static int CertsOf(AdPc p, string thumb) => p.Pc.CertificateList.Count(c => PinnedDriver.Eq(c.Thumbprint, thumb));
        static bool NoCertCall(AdPc p, int from) => !p.Api.Calls.Skip(from).Any(c => c.StartsWith("remove-cert"));
        static ReconcileItem OnlyItem(AdPc p) { var rc = DriverSetup.Reconcile(p.Ctx.Ledger, p.Pc); return rc.Items.Count == 1 ? rc.Items[0] : null; }
        static CleanupOutcome CleanUpNow(AdPc p, DriverRecord rec, DriverUsage u = null) => DriverSetup.CleanUp(p.Ctx, DriverLedger.Load(rec.File), u ?? new DriverUsage());
        static CloseDecision OfferNow(AdPc p, DriverRecord rec, DriverUsage u = null, bool available = true) => DriverSetup.CleanupOffer(DriverLedger.Load(rec.File), p.Pc, u ?? new DriverUsage(), ElevationState.CanElevate, available);
        /// <summary>Windows finishes removing a package on its own (outside any restart): the package and its device bindings go</summary>
        static void PackageLeaves(AdPc p, string pkg)
        {
            p.Pc.PackageList.RemoveAll(x => PinnedDriver.Eq(x.PublishedName, pkg));
            foreach (var d in p.Pc.DeviceList.Where(d => PinnedDriver.Eq(d.InfPath, pkg))) { d.InfPath = ""; d.Service = ""; d.Provider = ""; d.InterfaceGuids.Clear(); }
        }

        static void DriverTrustCleanup()
        {
            (AdPc p, DriverRecord rec, string thumb, string pkg) Ready(string name)
            {
                var p = NewAdapterPc(name); var rec = SetUp(p);
                return (p, rec, rec.IntroducedCertificates[0].Thumbprint, rec.OwnedPackage);
            }

            // ---------------------------------------------------------- normal removal: package, then both certificates; nothing deferred
            {
                var (p, rec, thumb, pkg) = Ready("trust-normal"); int from = p.Api.Calls.Count;
                var ro = DriverSetup.Remove(p.Ctx, rec, new DriverUsage()); var r = DriverLedger.Load(rec.File);
                Check(ro.Result == RemovalResult.Removed && p.Runner.LastExit == DriverExit.Done && p.Pc.Package(pkg) == null && CertsOf(p, thumb) == 0 && r.CleanupState == null && r.DeferredCertificates.Count == 0
                      && p.Api.Calls.Skip(from).SequenceEqual(new[] { "uninstall " + pkg, "remove-cert Root", "remove-cert TrustedPublisher" }) && OnlyItem(p) == null,
                      "normal removal: the package is read back gone before its certificate leaves Root and then Trusted Publishers; nothing is deferred or left to explain");
            }

            // ---------------------------------------------------------- pending restart, package present (the reviewed defect), then the later cleanup
            {
                var (p, rec, thumb, pkg) = Ready("trust-pending"); p.Api.Uninstall = FakeSystemApi.UninstallMode.RestartNeeded; int from = p.Api.Calls.Count;
                var unrelated = Unrelated(p);
                var ro = DriverSetup.Remove(p.Ctx, rec, new DriverUsage()); var r = DriverLedger.Load(rec.File);
                Check(p.Runner.LastExit == DriverExit.CertificateDeferredRestartNeeded && p.Pc.Package(pkg) != null && p.Pc.Package(pkg).PendingRemoval && CertsOf(p, thumb) == 2
                      && p.Pc.CertificateList.Any(c => c.Store == CertStore.Root && c.Thumbprint == thumb) && p.Pc.CertificateList.Any(c => c.Store == CertStore.TrustedPublisher && c.Thumbprint == thumb)
                      && p.Api.Calls.Skip(from).SequenceEqual(new[] { "uninstall " + pkg }),
                      "removal waiting for a restart, package still present: the step keeps the certificate in Root and in Trusted Publishers and makes no certificate call (exit 0x" + p.Runner.LastExit.ToString("X8") + ")");
                Check(ro.Result == RemovalResult.RemovedRestartPending && ro.Message == DriverText.RemovalRestart + " " + DriverText.CertificateKeptUntilFinished && !ro.Message.Contains(DriverText.Removed)
                      && r.CleanupState == "deferred" && r.DeferredCertificates.Count == 2 && r.DeferredCertificates.All(c => c.Thumbprint == thumb) && r.CleanupRestartFirst && r.CleanupAfterUtc == r.RemovalBefore.TakenUtc,
                      "never called finished, and the record defers the cleanup: both certificates, a restart of Windows first");
                var i1 = OnlyItem(p); var o1 = OfferNow(p, rec); var c1 = CleanUpNow(p, rec);
                Check(i1?.Status == "removal-waiting-for-restart" && i1.Message == DriverText.ReconcileWaitingForRestart + " " + DriverText.ReconcileCertificateUntilRestart
                      && o1.Kind == CloseKind.Message && c1.Result == CleanupResult.NotEligible && p.Runner.Runs == 2 && CertsOf(p, thumb) == 2,
                      "next start before the restart: explained, no cleanup offered, and a cleanup call is refused before any administrator step (" + i1?.Message + ")");
                p.Pc.SimulateRestart();
                var i2 = OnlyItem(p); var o2 = OfferNow(p, rec);
                Check(p.Pc.Package(pkg) == null && i2?.Status == "cleanup-ready" && i2.Message == DriverText.ReconcileCleanupReady && o2.Kind == CloseKind.OfferRemoval && o2.Title == DriverText.CleanupTitle
                      && o2.DefaultButton == 0 && o2.Buttons.SequenceEqual(new[] { DriverText.KeepCertificateButton, DriverText.RemoveCertificateButton, DriverText.LaterButton }) && CertsOf(p, thumb) == 2,
                      "after the restart finished the removal: the next start offers the cleanup, No - Keep Certificate the default (" + i2?.Message + ")");
                from = p.Api.Calls.Count;
                var c2 = CleanUpNow(p, rec); var r2 = DriverLedger.Load(rec.File);
                var rq = DriverActionRequest.Decode(p.Runner.LastArguments.Split(' ')[1]);
                Check(c2.Result == CleanupResult.Removed && c2.Message == DriverText.CleanupRemoved && p.Runner.LastExit == DriverExit.Done && CertsOf(p, thumb) == 0 && Unrelated(p) == unrelated
                      && p.Api.Calls.Skip(from).SequenceEqual(new[] { "remove-cert Root", "remove-cert TrustedPublisher" }) && r2.CleanupState == "finished" && r2.Cleanup == "Removed" && r2.DeferredCertificates.Count == 0
                      && r2.Steps.Select(s => s.Name).Skip(2).SequenceEqual(new[] { "removal", "removal-read-back", "removal-cleanup", "removal-cleanup-read-back" }) && OnlyItem(p) == null,
                      "the cleanup through the same administrator step: exactly this setup's certificate leaves both stores (read back), nothing unrelated, recorded, nothing left to explain");
                Check(rq.Action == "cleanup" && rq.Cleanup.PublishedName == pkg && rq.Cleanup.Certificates.Count == 2 && rq.Cleanup.Certificates.All(c => c.Thumbprint == thumb) && rq.Cleanup.RestartFirst && rq.Cleanup.Problems().Count == 0,
                      "the cleanup request names the removed package, exact thumbprints and the restart condition only");
            }

            // ---------------------------------------------------------- Windows says done, the package lingers with no restart flag
            {
                var (p, rec, thumb, pkg) = Ready("trust-linger"); p.Api.Uninstall = FakeSystemApi.UninstallMode.ReportsDoneLeavesPackage; int from = p.Api.Calls.Count;
                var ro = DriverSetup.Remove(p.Ctx, rec, new DriverUsage()); var r = DriverLedger.Load(rec.File);
                Check(p.Runner.LastExit == DriverExit.CertificateDeferred && p.Pc.Package(pkg) != null && !p.Pc.Package(pkg).PendingRemoval && CertsOf(p, thumb) == 2 && NoCertCall(p, from),
                      "the package is still there after Windows reported its removal, with no restart flag: both certificates stay");
                Check(ro.Result == RemovalResult.Unconfirmed && ro.Message == DriverText.RemovalNotFinished && r.CleanupState == "deferred" && r.DeferredCertificates.Count == 2 && r.CleanupRestartFirst,
                      "said exactly so (never 'removed'), and the cleanup is deferred (" + ro.Message + ")");
                var i1 = OnlyItem(p);
                Check(i1?.Status == "cleanup-deferred" && i1.Message == DriverText.ReconcileRemovalNotFinished && CleanUpNow(p, rec).Result == CleanupResult.NotEligible && CertsOf(p, thumb) == 2,
                      "next start: the driver is still there, the certificate kept, no cleanup");
                p.Pc.SimulateRestart();
                Check(OnlyItem(p)?.Status == "cleanup-deferred" && CleanUpNow(p, rec).Result == CleanupResult.NotEligible && CertsOf(p, thumb) == 2, "still there after a restart: still kept");
                PackageLeaves(p, pkg);
                var i3 = OnlyItem(p); var c = CleanUpNow(p, rec);
                Check(i3?.Status == "cleanup-ready" && c.Result == CleanupResult.Removed && CertsOf(p, thumb) == 0, "once the package is gone (after a restart): offered, and removed");
            }

            // ---------------------------------------------------------- the PC cannot be read after the uninstall
            {
                var (p, rec, thumb, pkg) = Ready("trust-unreadable"); p.Api.Uninstall = FakeSystemApi.UninstallMode.UnreadableAfter; int from = p.Api.Calls.Count;
                var ro = DriverSetup.Remove(p.Ctx, rec, new DriverUsage());
                Check(p.Runner.LastExit == DriverExit.CertificateDeferred && CertsOf(p, thumb) == 2 && NoCertCall(p, from) && ro.Result == RemovalResult.Unconfirmed && ro.Message == DriverText.RemovalUnconfirmed,
                      "the PC cannot be read after Windows' uninstall: the step and the window keep both certificates and say they cannot tell (" + ro.Message + ")");
                var r = DriverLedger.Load(rec.File);
                Check(r.CleanupState == "deferred" && r.DeferredCertificates.Count == 2 && r.CleanupRestartFirst && r.RemovalAfter.Error != null, "the record defers the cleanup with the unreadable reading kept");
                var o = OfferNow(p, rec); var c = CleanUpNow(p, rec); var rc = DriverSetup.Reconcile(p.Ctx.Ledger, p.Pc);
                Check(o.Kind == CloseKind.Message && o.Message == string.Format(DriverText.CleanupKeptFormat, DriverText.Because(RetainReason.StateUnreadable)) && c.Result == CleanupResult.NotEligible
                      && rc.Items.Single().Status == "pc-unreadable" && CertsOf(p, thumb) == 2, "still unreadable at the next start: nothing offered or removed");
                p.Pc.FailReads = false; p.Pc.SimulateRestart();
                Check(OnlyItem(p)?.Status == "cleanup-ready" && CleanUpNow(p, rec).Result == CleanupResult.Removed && CertsOf(p, thumb) == 0, "readable again after a restart, the package gone: offered, and removed");
            }

            // ---------------------------------------------------------- the signer scan is incomplete inside the step
            {
                var (p, rec, thumb, pkg) = Ready("trust-scan"); int from = p.Api.Calls.Count;
                p.Runner.BeforeStep = () => { p.Pc.IncompleteSignerScan = true; p.Runner.BeforeStep = null; };   // the window's own check was complete
                var ro = DriverSetup.Remove(p.Ctx, rec, new DriverUsage()); var r = DriverLedger.Load(rec.File);
                Check(p.Runner.LastExit == DriverExit.CertificateKept && p.Pc.Package(pkg) == null && CertsOf(p, thumb) == 2 && NoCertCall(p, from),
                      "the package is gone but not every package catalog could be read for the certificate: it stays in both stores");
                Check(ro.Result == RemovalResult.Partial && ro.Message == DriverText.Removed + " " + string.Format(DriverText.CertificateKeptFormat, DriverText.Because(RetainReason.CertificateSharingUnknown)) + " " + DriverText.CleanupLater
                      && r.CleanupState == "deferred" && !r.CleanupRestartFirst, "said exactly so; deferred with no restart needed (Windows and the reading agree the removal finished)");
                var i1 = OnlyItem(p);
                Check(i1?.Status == "cleanup-deferred" && i1.Message == string.Format(DriverText.ReconcileCleanupKeptFormat, DriverText.Because(RetainReason.CertificateSharingUnknown))
                      && CleanUpNow(p, rec).Result == CleanupResult.NotEligible && CertsOf(p, thumb) == 2, "next start, the scan still incomplete: kept, with the reason");
                p.Pc.IncompleteSignerScan = false;
                Check(OnlyItem(p)?.Status == "cleanup-ready" && CleanUpNow(p, rec).Result == CleanupResult.Removed && CertsOf(p, thumb) == 0, "every catalog read and none signed with it: offered, and removed");
            }

            // ---------------------------------------------------------- another package is signed with it (appears after the window's check)
            {
                var (p, rec, thumb, pkg) = Ready("trust-shared"); int from = p.Api.Calls.Count; DriverPackage other = null;
                p.Runner.BeforeStep = () => { other = p.Pc.AddPackage("ccs_copy.inf", CcsDriver.Provider, "WinUSB", CcsDriver.InterfaceGuid, thumb, @"USB\VID_1234&PID_5678"); p.Runner.BeforeStep = null; };
                var ro = DriverSetup.Remove(p.Ctx, rec, new DriverUsage());
                Check(p.Runner.LastExit == DriverExit.CertificateKept && p.Pc.Package(pkg) == null && CertsOf(p, thumb) == 2 && NoCertCall(p, from)
                      && ro.Result == RemovalResult.Partial && ro.Message.Contains(string.Format(DriverText.CertificateKeptFormat, DriverText.Because(RetainReason.CertificateShared))),
                      "another package is signed with the certificate: it stays in both stores, said so (" + ro.Message + ")");
                var plan = new CleanupPlan { OperationId = rec.OperationId, PublishedName = pkg, Certificates = p.Pc.CertificateList.Where(c => c.Thumbprint == thumb).ToList() };
                int calls = p.Api.Calls.Count;
                Check(OnlyItem(p)?.Status == "cleanup-deferred" && OnlyItem(p).Message == string.Format(DriverText.ReconcileCleanupKeptFormat, DriverText.Because(RetainReason.CertificateShared))
                      && DriverActionHost.Execute(new[] { DriverActionHost.Flag, DriverActionRequest.ForCleanup(plan).Encode() }, p.Api, p.Pc) == DriverExit.CertificateStillNeeded && p.Api.Calls.Count == calls && CertsOf(p, thumb) == 2,
                      "next start: kept while the other package is there; the elevated cleanup step refuses it on its own reading too");
                p.Pc.PackageList.Remove(other);
                Check(OnlyItem(p)?.Status == "cleanup-ready" && CleanUpNow(p, rec).Result == CleanupResult.Removed && CertsOf(p, thumb) == 0, "the other package gone: offered, and removed");
            }

            // ---------------------------------------------------------- interruption between the uninstall and the certificates
            {
                var (p, rec, thumb, pkg) = Ready("trust-crash-step"); p.Api.Uninstall = FakeSystemApi.UninstallMode.CrashAfterRemoving; int from = p.Api.Calls.Count;
                var ro = DriverSetup.Remove(p.Ctx, rec, new DriverUsage()); var r = DriverLedger.Load(rec.File);
                Check(p.Runner.LastExit == unchecked((int)0xE0434352) && p.Pc.Package(pkg) == null && CertsOf(p, thumb) == 2 && NoCertCall(p, from)
                      && ro.Result == RemovalResult.Partial && ro.Message == DriverText.Removed + " " + string.Format(DriverText.CertificateKeptFormat, DriverText.Because(RetainReason.StepInterrupted)) + " " + DriverText.CleanupLater
                      && r.CleanupState == "deferred" && r.CleanupRestartFirst,
                      "the elevated step ends right after Windows removed the package: both certificates stay, the window says so and defers the cleanup until a restart (" + ro.Message + ")");
                var i1 = OnlyItem(p);
                var crafted = new CleanupPlan { OperationId = rec.OperationId, PublishedName = pkg, RestartFirst = true, AfterUtc = r.CleanupAfterUtc, BootThenUtc = r.CleanupBootUtc, Certificates = r.DeferredCertificates.ToList() };
                Check(i1?.Status == "removal-waiting-for-restart" && i1.Message == DriverText.ReconcileCleanupAfterRestart && CleanUpNow(p, rec).Result == CleanupResult.NotEligible
                      && DriverActionHost.Execute(new[] { DriverActionHost.Flag, DriverActionRequest.ForCleanup(crafted).Encode() }, p.Api, p.Pc) == DriverExit.CertificateStillNeeded && CertsOf(p, thumb) == 2,
                      "next start without a restart: kept and explained; the elevated cleanup step itself also waits for the restart");
                p.Pc.SimulateRestart();
                Check(OnlyItem(p)?.Status == "cleanup-ready" && CleanUpNow(p, rec).Result == CleanupResult.Removed && CertsOf(p, thumb) == 0, "after a restart: offered, and removed");
            }
            {   // the window ends with the step: the record's removal stays open, and its plan bounds the later cleanup
                var (p, rec, thumb, pkg) = Ready("trust-crash-window"); p.Api.Uninstall = FakeSystemApi.UninstallMode.CrashAfterRemoving; p.Runner.WindowEndsWithStep = true;
                var ended = false; try { DriverSetup.Remove(p.Ctx, rec, new DriverUsage()); } catch (SimulatedCrashException) { ended = true; }
                var r = DriverLedger.Load(rec.File); var i1 = OnlyItem(p);
                Check(ended && r.RemovalState == "open" && r.CleanupState == null && p.Pc.Package(pkg) == null && CertsOf(p, thumb) == 2 && i1?.Status == "incomplete-removal"
                      && i1.Message == DriverText.ReconcileIncompleteRemoval + " " + DriverText.ReconcileRemovalFinished + " " + DriverText.CertificateNotRemoved + " " + DriverText.ReconcileOfferAfterRestart
                      && CleanUpNow(p, rec).Result == CleanupResult.NotEligible && CertsOf(p, thumb) == 2,
                      "the window ends during the removal: the record stays open, both certificates stay, the next start explains; no cleanup before a restart (" + i1?.Message + ")");
                p.Runner.WindowEndsWithStep = false; p.Pc.SimulateRestart();
                var i2 = OnlyItem(p); var c = CleanUpNow(p, rec); var r2 = DriverLedger.Load(rec.File);
                Check(i2?.Status == "cleanup-ready" && c.Result == CleanupResult.Removed && CertsOf(p, thumb) == 0 && r2.CleanupState == "finished" && r2.RemovalState == "open"
                      && Eligibility.Evaluate(r2, DriverSnapshot.Capture(p.Pc), new DriverUsage()).Reasons.Contains(RetainReason.RemovalIncomplete),
                      "after a restart: offered and removed; the open removal itself still never counts as finished");
            }
            {   // the cleanup itself ends part way (Root removed, Trusted Publishers not), then the window too
                var (p, rec, thumb, pkg) = Ready("trust-crash-cleanup"); p.Api.Uninstall = FakeSystemApi.UninstallMode.RestartNeeded;
                DriverSetup.Remove(p.Ctx, rec, new DriverUsage()); p.Pc.SimulateRestart();
                p.Api.CrashAt = "remove-cert TrustedPublisher";
                var c1 = CleanUpNow(p, rec); var r = DriverLedger.Load(rec.File);
                Check(p.Runner.LastExit == unchecked((int)0xE0434352) && c1.Result == CleanupResult.Partial
                      && c1.Message == string.Format(DriverText.CleanupPartialFormat, DriverSetup.StoreName(CertStore.Root), DriverSetup.StoreName(CertStore.TrustedPublisher)) && CertsOf(p, thumb) == 1
                      && r.CleanupState == "deferred" && r.DeferredCertificates.Single().Store == CertStore.TrustedPublisher,
                      "the cleanup step ends after Root: said exactly so, and the Trusted Publishers copy stays deferred (" + c1.Message + ") [" + p.Runner.LastExit.ToString("X8") + " " + c1.Result + " " + CertsOf(p, thumb) + " " + r.CleanupState + " " + string.Join("/", r.DeferredCertificates.Select(x => x.Store)) + "]");
                p.Runner.WindowEndsWithStep = true;
                var ended = false; try { CleanUpNow(p, rec); } catch (SimulatedCrashException) { ended = true; }
                var r2 = DriverLedger.Load(rec.File);
                Check(ended && r2.CleanupState == "open" && r2.Incomplete && CertsOf(p, thumb) == 1, "the window ends during the next cleanup: the record shows it open [" + ended + " " + r2.CleanupState + " " + r2.Incomplete + " " + CertsOf(p, thumb) + "]");
                p.Api.CrashAt = null; p.Runner.WindowEndsWithStep = false;
                Check(OnlyItem(p)?.Status == "cleanup-ready" && CleanUpNow(p, rec).Result == CleanupResult.Removed && CertsOf(p, thumb) == 0 && DriverLedger.Load(rec.File).CleanupState == "finished",
                      "the next start offers what is left, and the cleanup finishes");
            }

            // ---------------------------------------------------------- the cleanup refused whenever anything is uncertain or in use
            {
                var (p, rec, thumb, pkg) = Ready("trust-refuse"); p.Api.Uninstall = FakeSystemApi.UninstallMode.RestartNeeded;
                DriverSetup.Remove(p.Ctx, rec, new DriverUsage()); p.Pc.SimulateRestart();
                int runs = p.Runner.Runs;
                foreach (var (u, why) in new[] { (new DriverUsage { ConsoleOperationActive = true }, RetainReason.ConsoleOperationActive), (new DriverUsage { ConsoleWriteInProgress = true }, RetainReason.ConsoleWriteInProgress),
                    (new DriverUsage { UnresolvedConsoleResult = true }, RetainReason.UnresolvedConsoleResult), (new DriverUsage { Host = HostKind.Embedded, ParentAppNeedsDevice = true }, RetainReason.ParentAppNeedsIt),
                    (new DriverUsage { OtherWorkbenchOperation = true }, RetainReason.AnotherWorkbenchOperation), (new DriverUsage { OtherProgramsOpen = { "Hakchi" } }, RetainReason.OtherProgramMayUseIt) })
                {
                    var o = OfferNow(p, rec, u); var c = CleanUpNow(p, rec, u);
                    Check(o.Kind == CloseKind.Message && o.Message == string.Format(DriverText.CleanupKeptFormat, DriverText.Because(why)) && c.Result == CleanupResult.NotEligible && p.Runner.Runs == runs && CertsOf(p, thumb) == 2,
                          why + ": not offered, a cleanup call refused, no administrator step (" + o.Message + ")");
                }
                Check(OfferNow(p, rec, new DriverUsage { Host = HostKind.Embedded, ParentAppNeedsDevice = false }).Kind == CloseKind.OfferRemoval, "inside Classic Cart Studio once the app no longer needs the console (its own close): offered");
                var real = new SetupContext { Store = p.Pc, Actions = new WindowsDriverActions(), Ledger = p.Ctx.Ledger };
                var cu = DriverSetup.CleanUp(real, DriverLedger.Load(rec.File), new DriverUsage()); var ou = OfferNow(p, rec, null, available: false);
                Check(cu.Result == CleanupResult.Unavailable && cu.Message == string.Format(DriverText.CleanupKeptFormat, DriverText.Because(RetainReason.ActionUnavailable)) && ou.Kind == CloseKind.Message
                      && ou.Message == cu.Message && CertsOf(p, thumb) == 2 && DriverLedger.Load(rec.File).CleanupState == "deferred",
                      "this version's real adapter (DriverChanges closed): kept, said so, record unchanged");
                p.Pc.Pc = "sim-pc-0002";
                Check(OfferNow(p, rec).Message == string.Format(DriverText.CleanupKeptFormat, DriverText.Because(RetainReason.OtherPc)) && CleanUpNow(p, rec).Result == CleanupResult.NotEligible, "a record from another PC: never acted on");
                p.Pc.Pc = "sim-pc-0001";
                p.Runner.Refuse = true;
                var cr = CleanUpNow(p, rec);
                Check(cr.Result == CleanupResult.ElevationRefused && CertsOf(p, thumb) == 2 && DriverLedger.Load(rec.File).CleanupState == "deferred" && OfferNow(p, rec).Kind == CloseKind.OfferRemoval,
                      "the administrator prompt refused: kept, offered again later (" + cr.Message + ")");
                p.Runner.Refuse = false;
                var root = p.Pc.CertificateList.First(c => c.Thumbprint == thumb && c.Store == CertStore.Root); root.HasPrivateKey = true;
                var d = CleanupEligibility.Evaluate(DriverLedger.Load(rec.File), p.Pc, DriverSnapshot.Capture(p.Pc), new DriverUsage());
                var cc = CleanUpNow(p, rec);
                Check(d.Offer && d.Plan.Certificates.Single().Store == CertStore.TrustedPublisher && d.Kept.Single().Why == RetainReason.CertificateChanged && cc.Result == CleanupResult.Partial
                      && p.Pc.CertificateList.Count(c => c.Thumbprint == thumb) == 1 && p.Pc.CertificateList.Single(c => c.Thumbprint == thumb).Store == CertStore.Root
                      && OnlyItem(p)?.Status == "cleanup-deferred" && OnlyItem(p).Message == string.Format(DriverText.ReconcileCleanupKeptFormat, DriverText.Because(RetainReason.CertificateChanged)),
                      "the Root copy changed (it now has a private key): only the unchanged Trusted Publishers copy is removed; the changed one stays and is explained");
            }
            {   // the person keeps it
                var (p, rec, thumb, pkg) = Ready("trust-keep"); p.Api.Uninstall = FakeSystemApi.UninstallMode.RestartNeeded;
                DriverSetup.Remove(p.Ctx, rec, new DriverUsage()); p.Pc.SimulateRestart();
                DriverSetup.KeepCertificate(p.Ctx, DriverLedger.Load(rec.File));
                Check(DriverLedger.Load(rec.File).CleanupState == "kept" && OnlyItem(p) == null && OfferNow(p, rec).Kind == CloseKind.Nothing && CleanUpNow(p, rec).Result == CleanupResult.NotEligible && CertsOf(p, thumb) == 2,
                      "No - Keep Certificate: recorded, never offered again, nothing removed");
            }

            // ---------------------------------------------------------- the elevated cleanup step's own checks, given crafted requests
            {
                var (p, rec, thumb, pkg) = Ready("trust-crafted"); var saved = p.Pc.Package(pkg).Copy(); p.Api.Uninstall = FakeSystemApi.UninstallMode.RestartNeeded;
                DriverSetup.Remove(p.Ctx, rec, new DriverUsage()); p.Pc.SimulateRestart();
                var plan = CleanupEligibility.Evaluate(DriverLedger.Load(rec.File), p.Pc, DriverSnapshot.Capture(p.Pc), new DriverUsage()).Plan;
                int Exec(CleanupPlan cp) => DriverActionHost.Execute(new[] { DriverActionHost.Flag, DriverActionRequest.ForCleanup(cp).Encode() }, p.Api, p.Pc);
                CleanupPlan Copy() => CleanupPlan.From(Json.Parse(Json.Write(plan.ToJson())));
                var hk = SimulatedDriverStore.FakeThumbprint("hakchi-cert"); p.Pc.AddCertificate(CertStore.Root, hk, PinnedDriver.CertificateSubject, "libwdi");
                int calls = p.Api.Calls.Count; var before = Unrelated(p);
                var a = Copy(); a.OperationId = Guid.NewGuid().ToString("N");                                                                 // another operation's id
                var b = Copy(); b.Certificates = p.Pc.CertificateList.Where(c => c.Subject == "CN=Example Root CA").Select(c => CertificateEntry.From(Json.Parse(Json.Write(c.ToJson())))).ToList();   // an unrelated root
                var h = Copy(); h.Certificates = p.Pc.CertificateList.Where(c => c.Thumbprint == hk).ToList();                                // Hakchi's libwdi certificate
                var w = Copy(); w.AfterUtc = DateTime.UtcNow.AddMinutes(5).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"); w.RestartFirst = true;     // no restart seen since
                var ea = Exec(a); var eb = Exec(b); var eh = Exec(h); var ew = Exec(w);
                p.Pc.PackageList.Add(saved); var ep = Exec(Copy()); p.Pc.PackageList.Remove(saved);                                           // the package is back
                var bad = new[] { new CleanupPlan { OperationId = rec.OperationId, PublishedName = pkg }, new CleanupPlan { OperationId = rec.OperationId, PublishedName = "winusb.inf", Certificates = plan.Certificates },
                    new CleanupPlan { OperationId = rec.OperationId, PublishedName = pkg, Certificates = { new CertificateEntry { Store = CertStore.Root, Thumbprint = CcsDriver.SubjectFor(rec.OperationId) } } } };
                Check(ea == DriverExit.NotTheRecordedCertificate && eb == DriverExit.NotTheRecordedCertificate && eh == DriverExit.NotTheRecordedCertificate && ew == DriverExit.CertificateStillNeeded && ep == DriverExit.CertificateStillNeeded
                      && bad.All(x => Exec(x) == DriverExit.BadRequest) && p.Api.Calls.Count == calls && CertsOf(p, thumb) == 2 && CertsOf(p, hk) == 1 && Unrelated(p) == before,
                      "the elevated cleanup step refuses another operation's id, an unrelated root, Hakchi's certificate, a restart not seen, the package back, and plans with no certificate, an inbox INF or a subject for a thumbprint; nothing is touched ("
                      + string.Join(", ", new[] { ea, eb, eh, ew, ep }.Select(x => "0x" + x.ToString("X8"))) + ")");
                Check(Exec(Copy()) == DriverExit.Done && CertsOf(p, thumb) == 0 && CertsOf(p, hk) == 1 && Unrelated(p) == before, "the genuine plan: this setup's certificate only");
            }
            {   // a record listing a certificate the setup did not add is never acted on for it
                var (p, rec, thumb, pkg) = Ready("trust-provenance"); p.Api.Uninstall = FakeSystemApi.UninstallMode.RestartNeeded;
                DriverSetup.Remove(p.Ctx, rec, new DriverUsage()); p.Pc.SimulateRestart();
                var r = DriverLedger.Load(rec.File); var ex = p.Pc.CertificateList.First(c => c.Subject == "CN=Example Root CA");
                r.DeferredCertificates.Add(CertificateEntry.From(Json.Parse(Json.Write(ex.ToJson())))); p.Ctx.Ledger.Save(r);
                var d = CleanupEligibility.Evaluate(r, p.Pc, DriverSnapshot.Capture(p.Pc), new DriverUsage());
                Check(d.Offer && d.Plan.Certificates.All(c => c.Thumbprint == thumb) && d.Kept.Any(k => k.Certificate.Key == ex.Key && k.Why == RetainReason.CertificatePreExisting)
                      && CleanUpNow(p, rec).Result == CleanupResult.Removed && p.Pc.CertificateList.Any(c => c.Key == ex.Key) && CertsOf(p, thumb) == 0,
                      "a crafted record naming an unrelated root: only the setup's own certificate is ever planned or removed");
            }
        }

        // ================================================================== driver-package
        public static void DriverPackageFiles()
        {
            var dir = NewDir("driver-package");
            // ---- the INF: data only, Windows' WinUSB, fixed bytes
            var text = CcsDriver.InfText; var bytes = CcsDriver.InfBytes();
            var info = Inf.Parse(Inf.Decode(bytes), "NTamd64"); var arm = Inf.Parse(Inf.Decode(bytes), "NTarm64");
            Check(bytes.All(b => b >= 0x0A && b < 0x80) && Sha(bytes) == CcsDriver.InfSha256 && info.Provider == "Rance9 LLC" && info.Class == "USBDevice" && info.ClassGuid == PinnedDriver.ClassGuid
                  && info.CatalogFile == CcsDriver.CatalogName && arm.CatalogFile == CcsDriver.CatalogName && info.UsbIds.SequenceEqual(new[] { PinnedDriver.HardwareId })
                  && info.InterfaceGuids.SequenceEqual(new[] { CcsDriver.InterfaceGuid }) && info.Services.SequenceEqual(new[] { "WinUSB" }),
                  "the INF (ASCII, SHA-256 " + CcsDriver.InfSha256.Substring(0, 16) + "…): Rance9 LLC, USBDevice class, its catalog, only 1F3A:EFE8, the interface GUID, Windows' WinUSB service");
            var lines = text.Split(new[] { "\r\n" }, StringSplitOptions.None).Select(l => l.Split(';')[0].Trim()).Where(l => l.Length > 0).ToList();
            Check(lines.Contains("Include = winusb.inf") && lines.Contains("Needs   = WINUSB.NT") && lines.Contains("Needs   = WINUSB.NT.Services") && lines.Contains("PnpLockdown = 1")
                  && new[] { "[CcsConsole.NTx86]", "[CcsConsole.NTamd64]", "[CcsConsole.NTarm64]" }.All(lines.Contains)
                  && !lines.Any(l => Rx(@"^(CopyFiles|AddService|CoInstallers32|DestinationDirs|\[ClassInstall32|\[SourceDisks|DelReg|DelService|DelFiles|RenFiles|UpdateInis|RegisterDlls|AddProperty)", l)),
                  "data only: Windows' own WinUSB through Include/Needs, three processor sections, and no file copy, service, co-installer, class installer, deletion or registration");
            // ---- the certificate and the catalog, made by the product code into the work folder
            var keysBefore = KeyFileCount(); var opId = Guid.NewGuid().ToString("N");
            string inf, cat; CcsSigner signer;
            using (signer = CcsSigner.New(opId, DateTime.UtcNow)) (inf, cat) = CcsCatalog.Write(dir, signer);
            var cert = new X509Certificate2(signer.Der);
            Check(signer.Certificate == null && Directory.GetFiles(dir).Select(Path.GetFileName).OrderBy(x => x).SequenceEqual(new[] { CcsDriver.CatalogName, CcsDriver.InfName })
                  && File.ReadAllBytes(inf).SequenceEqual(bytes), "Write leaves exactly the INF (Workbench's bytes) and its catalog in the folder; the signer is disposed");
            var bc = cert.Extensions.OfType<X509BasicConstraintsExtension>().Single(); var ku = cert.Extensions.OfType<X509KeyUsageExtension>().Single(); var eku = cert.Extensions.OfType<X509EnhancedKeyUsageExtension>().Single();
            Check(!cert.HasPrivateKey && cert.Subject == CcsDriver.SubjectFor(opId) && cert.Issuer == cert.Subject && !bc.CertificateAuthority && bc.Critical && ku.KeyUsages == X509KeyUsageFlags.DigitalSignature && ku.Critical
                  && eku.Critical && eku.EnhancedKeyUsages.Count == 1 && eku.EnhancedKeyUsages[0].Value == "1.3.6.1.5.5.7.3.3" && cert.GetRSAPublicKey().KeySize == 3072 && cert.SignatureAlgorithm.Value == "1.2.840.113549.1.1.11"
                  && cert.NotAfter.ToUniversalTime() == CcsSigner.NotAfter.UtcDateTime && cert.NotBefore.ToUniversalTime() <= DateTime.UtcNow,
                  "the certificate: public part only, self-signed for this operation, CA=false (it can issue nothing), Digital Signature and Code Signing only, RSA 3072, SHA-256");
            Check(KeyFileCount() == keysBefore, "no key file was written: the private key lived in memory only (" + keysBefore + " key files in the user's and the machine's key folders, before and after)");
            var k = CcsCatalog.Check(inf, cat);
            Check(k.Ok && k.Signers == 1 && k.Members == 1 && k.SignerThumbprint == signer.Thumbprint && k.SignerSubject == CcsDriver.SubjectFor(opId) && k.DigestAlgorithm == "2.16.840.1.101.3.4.2.1" && k.MemberTag == CcsDriver.InfTag
                  && k.MemberAttributes["File"] == CcsDriver.InfName && k.MemberAttributes["OSAttr"] == CcsDriver.OsAttr && k.CatalogAttributes["HWID1"] == @"usb\vid_1f3a&pid_efe8" && k.CatalogAttributes["OS"] == CcsDriver.OsList,
                  "the catalog read back by Windows' CMS and catalog readers: one SHA-256 signer (this certificate), one entry (the INF's SHA-1 " + k.MemberTag + ", flat file), its attributes" + (k.Ok ? "" : ": " + string.Join("; ", k.Problems)));
            Check(WinCalcHash(inf) == CcsDriver.InfTag && WinEncodeObsoleteLink().SequenceEqual(CcsCatalogObsoleteLink()), "Windows computes the same INF hash (CryptCATAdminCalcHashFromFileHandle) and the same entry link (CryptEncodeObject)");
            var trust = CcsCatalog.VerifyTrust(inf, cat);
            Check(trust == unchecked((int)0x800B0109), "Windows' Authenticode check of the INF through the catalog reports only the untrusted root (0x" + trust.ToString("X8") + "): the hash and the signature verify, and this PC does not trust the certificate");
            var chain = ExclusiveRootAuthenticode(cert);
            Check(chain == "0x00000000/0x00000000", "with the certificate as the only trusted root (an in-memory chain engine, not this PC's stores), Windows' Authenticode chain policy accepts it (" + chain + ")");
            // ---- tampering is caught
            var t1 = NewDir("driver-package-tampered"); File.Copy(inf, Path.Combine(t1, CcsDriver.InfName)); File.Copy(cat, Path.Combine(t1, CcsDriver.CatalogName));
            File.AppendAllText(Path.Combine(t1, CcsDriver.InfName), "; changed\r\n");
            var vt = CcsCatalog.VerifyTrust(Path.Combine(t1, CcsDriver.InfName), Path.Combine(t1, CcsDriver.CatalogName)); var kt = CcsCatalog.Check(Path.Combine(t1, CcsDriver.InfName), Path.Combine(t1, CcsDriver.CatalogName));
            var cb = File.ReadAllBytes(cat); cb[cb.Length - 40] ^= 0xFF; var t2 = Path.Combine(t1, "flipped.cat"); File.WriteAllBytes(t2, cb);
            var kf = CcsCatalog.Check(inf, t2);
            Check(vt != 0 && vt != unchecked((int)0x800B0109) && !kt.Ok && kt.Problems.Any(x => x.Contains("hash")) && !kf.Ok, "a changed INF fails Windows' check (0x" + vt.ToString("X8") + ") and Check; a changed catalog fails Check (" + string.Join("; ", kf.Problems.Take(1)) + ")");
            // ---- each setup its own certificate
            using (var s2 = CcsSigner.New(Guid.NewGuid().ToString("N"), DateTime.UtcNow))
                Check(s2.Thumbprint != signer.Thumbprint && s2.Subject != signer.Subject && CcsDriver.IsCcsSubject(s2.Subject) && !CcsDriver.IsCcsSubject(PinnedDriver.CertificateSubject) && !CcsDriver.IsCcsSubject(CcsDriver.SubjectPrefix + "x"),
                      "a new certificate for every setup, recognizable only by Workbench's exact subject form");
            var bad = false; try { CcsSigner.New("not-an-id", DateTime.UtcNow).Dispose(); } catch (ArgumentException) { bad = true; }
            Check(bad, "a certificate is made only for a real operation id");
            // the evidence: the generated files stay in this run's work folder
            File.WriteAllBytes(Path.Combine(dir, "signer-public.cer"), signer.Der);
            File.WriteAllText(Path.Combine(dir, "package-check.json"), Json.Write(new JObj { { "inf_sha256", Sha(File.ReadAllBytes(inf)) }, { "cat_sha256", Sha(File.ReadAllBytes(cat)) }, { "cat_bytes", new FileInfo(cat).Length },
                { "signer_thumbprint", k.SignerThumbprint }, { "signer_subject", k.SignerSubject }, { "member_tag", k.MemberTag }, { "win_verify_trust", "0x" + trust.ToString("X8") }, { "exclusive_root_authenticode", chain } }));
            Console.WriteLine("      package files: " + dir);
        }

        static bool Rx(string pattern, string s) => System.Text.RegularExpressions.Regex.IsMatch(s, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        static byte[] CcsCatalogObsoleteLink() => (byte[])typeof(CcsCatalog).GetField("ObsoleteLink", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);

        /// <summary>files in the user's and the machine's CAPI/CNG key folders (a persisted key would add one)</summary>
        static int KeyFileCount()
        {
            int n = 0;
            void Walk(string d)
            {
                try { n += Directory.GetFiles(d).Length; foreach (var s in Directory.GetDirectories(d)) Walk(s); } catch (UnauthorizedAccessException) { } catch (IOException) { }
            }
            foreach (var d in new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Crypto"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Crypto") })
                if (Directory.Exists(d)) Walk(d);
            return n;
        }

        [DllImport("wintrust.dll", SetLastError = true)] static extern bool CryptCATAdminCalcHashFromFileHandle(IntPtr file, ref int cb, byte[] hash, int flags);
        static string WinCalcHash(string path)
        {
            using (var f = new FileStream(path, FileMode.Open, FileAccess.Read))
            { int cb = 20; var h = new byte[20]; return CryptCATAdminCalcHashFromFileHandle(f.SafeFileHandle.DangerousGetHandle(), ref cb, h, 0) ? string.Concat(h.Take(cb).Select(x => x.ToString("X2"))) : "error " + Marshal.GetLastWin32Error(); }
        }
        [StructLayout(LayoutKind.Sequential)] struct SPC_LINK { public uint Choice; public IntPtr Str; public IntPtr Pad; }
        [DllImport("crypt32.dll", SetLastError = true)] static extern bool CryptEncodeObject(uint enc, string type, ref SPC_LINK link, byte[] outb, ref int cb);
        static byte[] WinEncodeObsoleteLink()
        {
            var s = Marshal.StringToHGlobalUni("<<<Obsolete>>>");
            try { var l = new SPC_LINK { Choice = 3, Str = s }; int cb = 64; var o = new byte[64]; return CryptEncodeObject(1, "1.3.6.1.4.1.311.2.1.25", ref l, o, ref cb) ? o.Take(cb).ToArray() : new byte[0]; }
            finally { Marshal.FreeHGlobal(s); }
        }

        // an in-memory chain engine whose only trusted root is the given certificate (CERT_CHAIN_ENGINE_CONFIG.hExclusiveRoot): no store of this PC is read or changed
        [StructLayout(LayoutKind.Sequential)] struct ENGINE { public int cbSize; public IntPtr rr, rt, ro; public int cAdd; public IntPtr add; public uint flags, timeout, maxCached, cycle; public IntPtr exRoot, exPeople; public uint exFlags; }
        [StructLayout(LayoutKind.Sequential)] struct ENH { public int Count; public IntPtr Ids; }
        [StructLayout(LayoutKind.Sequential)] struct USAGE { public uint Type; public ENH Usage; }
        [StructLayout(LayoutKind.Sequential)] struct CHAIN_PARA { public int cbSize; public USAGE Usage; }
        [StructLayout(LayoutKind.Sequential)] struct POLICY_PARA { public int cbSize; public uint Flags; public IntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)] struct POLICY_STATUS { public int cbSize; public uint Error; public int Chain, Element; public IntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)] struct CHAIN_CONTEXT { public int cbSize; public uint TrustError, TrustInfo; }
        [DllImport("crypt32.dll", SetLastError = true)] static extern IntPtr CertOpenStore(IntPtr prov, uint enc, IntPtr h, uint flags, IntPtr para);
        [DllImport("crypt32.dll", SetLastError = true)] static extern bool CertAddCertificateContextToStore(IntPtr store, IntPtr cert, uint disp, IntPtr outp);
        [DllImport("crypt32.dll", SetLastError = true)] static extern bool CertCreateCertificateChainEngine(ref ENGINE c, out IntPtr engine);
        [DllImport("crypt32.dll", SetLastError = true)] static extern bool CertGetCertificateChain(IntPtr engine, IntPtr cert, IntPtr time, IntPtr add, ref CHAIN_PARA para, uint flags, IntPtr res, out IntPtr chain);
        [DllImport("crypt32.dll", SetLastError = true)] static extern bool CertVerifyCertificateChainPolicy(IntPtr oid, IntPtr chain, ref POLICY_PARA para, ref POLICY_STATUS st);
        [DllImport("crypt32.dll")] static extern void CertFreeCertificateChain(IntPtr c);
        [DllImport("crypt32.dll")] static extern void CertFreeCertificateChainEngine(IntPtr e);
        [DllImport("crypt32.dll")] static extern bool CertCloseStore(IntPtr s, uint f);
        static string ExclusiveRootAuthenticode(X509Certificate2 cert)
        {
            var mem = CertOpenStore(new IntPtr(2), 0, IntPtr.Zero, 0, IntPtr.Zero);   // CERT_STORE_PROV_MEMORY
            var oid = Marshal.StringToHGlobalAnsi("1.3.6.1.5.5.7.3.3"); var arr = Marshal.AllocHGlobal(IntPtr.Size); Marshal.WriteIntPtr(arr, oid);
            try
            {
                if (!CertAddCertificateContextToStore(mem, cert.Handle, 4, IntPtr.Zero)) return "add " + Marshal.GetLastWin32Error();
                var cfg = new ENGINE { cbSize = Marshal.SizeOf<ENGINE>(), exRoot = mem };
                if (!CertCreateCertificateChainEngine(ref cfg, out var eng)) return "engine " + Marshal.GetLastWin32Error();
                try
                {
                    var para = new CHAIN_PARA { cbSize = Marshal.SizeOf<CHAIN_PARA>(), Usage = new USAGE { Type = 0, Usage = new ENH { Count = 1, Ids = arr } } };
                    if (!CertGetCertificateChain(eng, cert.Handle, IntPtr.Zero, IntPtr.Zero, ref para, 0, IntPtr.Zero, out var ch)) return "chain " + Marshal.GetLastWin32Error();
                    try
                    {
                        var ctx = Marshal.PtrToStructure<CHAIN_CONTEXT>(ch);
                        var pp = new POLICY_PARA { cbSize = Marshal.SizeOf<POLICY_PARA>() }; var st = new POLICY_STATUS { cbSize = Marshal.SizeOf<POLICY_STATUS>() };
                        CertVerifyCertificateChainPolicy(new IntPtr(2), ch, ref pp, ref st);   // CERT_CHAIN_POLICY_AUTHENTICODE
                        return "0x" + ctx.TrustError.ToString("X8") + "/0x" + st.Error.ToString("X8");
                    }
                    finally { CertFreeCertificateChain(ch); }
                }
                finally { CertFreeCertificateChainEngine(eng); }
            }
            finally { CertCloseStore(mem, 0); Marshal.FreeHGlobal(arr); Marshal.FreeHGlobal(oid); }
        }

        // ================================================================== driver-gate
        public static void DriverGate()
        {
            Check(!CcsBackup.Gate.DriverChanges && CcsBackup.Gate.DriverText == "Setting up or removing the USB driver on this PC is not available in this version.", "the DriverChanges boundary is closed in code, with its plain sentence");
            var real = new WindowsDriverActions();
            string m1 = null, m2 = null, m3 = null;
            try { real.Install(DriverSetupPlan.CcsWinUsb(), ElevationState.Elevated); } catch (DriverActionUnavailableException ex) { m1 = ex.Message; }
            try { real.Remove(new RemovalPlan { OperationId = Guid.NewGuid().ToString("N"), PublishedName = "oem1.inf", InfSha256 = "a", CatalogSha256 = "b" }, ElevationState.Elevated); } catch (DriverActionUnavailableException ex) { m2 = ex.Message; }
            try { real.CleanUp(new CleanupPlan { OperationId = Guid.NewGuid().ToString("N"), PublishedName = "oem1.inf", Certificates = { new CertificateEntry { Store = CertStore.Root, Thumbprint = new string('A', 40) } } }, ElevationState.Elevated); }
            catch (DriverActionUnavailableException ex) { m3 = ex.Message; }
            Check(!real.Available && m1 != null && m1.StartsWith(CcsBackup.Gate.DriverText) && m2 != null && m2.StartsWith(CcsBackup.Gate.DriverText) && m3 != null && m3.StartsWith(CcsBackup.Gate.DriverText),
                  "the real setup, removal and later certificate cleanup refuse at once: " + m1);
            var g1 = false; var g2 = false;
            try { CallInternal(typeof(WindowsSystemDriverApi), "Open"); } catch (GateClosedException) { g1 = true; }
            try { CallInternal(typeof(RunasLauncher), "Open", @"C:\Windows\notepad.exe"); } catch (GateClosedException) { g2 = true; }
            Check(g1 && g2, "the only ways to the real system functions and to Windows' runas refuse while the boundary is closed");
            var req = DriverActionRequest.ForSetup(new Func<DriverSetupPlan>(() => { var x = DriverSetupPlan.CcsWinUsb(); x.OperationId = Guid.NewGuid().ToString("N"); return x; })()).Encode();
            var op = Guid.NewGuid().ToString("N");
            var creq = DriverActionRequest.ForCleanup(new CleanupPlan { OperationId = op, PublishedName = "oem1.inf", Certificates = { new CertificateEntry { Store = CertStore.Root, Thumbprint = new string('A', 40) } } }).Encode();
            Check(DriverActionHost.Run(new[] { DriverActionHost.Flag, req }) == DriverExit.GateClosed && DriverActionHost.Run(new[] { DriverActionHost.Flag, creq }) == DriverExit.GateClosed
                  && DriverActionHost.Run(new[] { DriverActionHost.Flag, "garbage" }) == DriverExit.GateClosed,
                  "the elevated mode itself stops with 'closed' before reading the request or the PC (setup and cleanup requests alike)");
            Check(DriverText.SetupUnavailableNote == CcsBackup.Gate.DriverText + " " + DriverText.NothingChanged, "the window's setup page says the truth while closed: " + DriverText.SetupUnavailableNote);

            // ---- the compiled code: nothing constructs the real functions or the runas launcher, nothing calls their factories, nothing else calls their Windows functions
            var asms = new List<Assembly> { typeof(DriverActionHost).Assembly };
            var ui = FindBuilt("CcsBackupUi", "CcsBackupUi.dll"); if (ui != null) asms.Add(Assembly.LoadFrom(ui));
            var api = typeof(WindowsSystemDriverApi); var run = typeof(RunasLauncher);
            var guarded = new HashSet<MethodBase>(api.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Concat<MethodBase>(run.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                .Concat(new[] { api.GetMethod("Open", BindingFlags.Static | BindingFlags.NonPublic), run.GetMethod("Open", BindingFlags.Static | BindingFlags.NonPublic) }));
            var pinvokes = api.GetMethods(BindingFlags.Static | BindingFlags.NonPublic).Where(x => (x.Attributes & MethodAttributes.PinvokeImpl) != 0).ToList();
            var siteList = new List<string>(); var pinvokeOutside = new List<string>(); int methods = 0; var executeCallers = new List<string>();
            foreach (var a in asms)
                foreach (var t in SafeTypes(a))
                    foreach (var mb in t.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Cast<MethodBase>()
                        .Concat(t.GetConstructors(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)))
                    {
                        methods++;
                        foreach (var target in Calls(mb))
                        {
                            if (guarded.Contains(target) && !(target.DeclaringType == mb.DeclaringType && target.IsConstructor && mb.Name == "Open")) siteList.Add(t.Name + "." + mb.Name + " -> " + target.DeclaringType.Name + "." + target.Name);
                            if (pinvokes.Contains(target) && mb.DeclaringType != api) pinvokeOutside.Add(t.Name + "." + mb.Name + " -> " + target.Name);
                            if (target.DeclaringType == typeof(DriverActionHost) && target.Name == "Execute" && mb.DeclaringType == typeof(DriverActionHost)) executeCallers.Add(mb.Name);
                        }
                    }
            var openIl = Calls(api.GetMethod("Open", BindingFlags.Static | BindingFlags.NonPublic)).ToList(); var runIl = Calls(typeof(DriverActionHost).GetMethod("Run")).ToList();
            Check(methods > 1000 && siteList.Count == 0 && !openIl.Any(x => x.IsConstructor && x.DeclaringType == api) && !runIl.Any(x => x.Name == "Execute" || x.Name == "Open" || x.DeclaringType == typeof(WindowsDriverStore)) && executeCallers.Count == 0,
                  "compiled code (" + methods + " methods in " + string.Join(", ", asms.Select(x => x.GetName().Name)) + "): no constructor of the real functions or of runas, and no call to their factories or to the elevated step's Execute, exists anywhere (" + string.Join("; ", siteList.Take(4)) + ")");
            Check(pinvokes.Count >= 7 && pinvokeOutside.Count == 0 && api.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).All(c => c.IsPrivate) && run.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).All(c => c.IsPrivate),
                  "the real functions' " + pinvokes.Count + " Windows calls are made only from its own (private-constructor, unconstructed) instance");
            // ---- the source: the factories are named only right after the closed check, in the three places that open the real path
            var srcDir = Path.GetFullPath(Path.Combine(Environment.GetEnvironmentVariable("CCS_FIXTURE_TOOLS") ?? "", "..", "CcsBackup", "Driver"));
            if (Directory.Exists(srcDir))
            {
                var hits = new List<string>(); var unguarded = new List<string>();
                foreach (var f in Directory.GetFiles(srcDir, "*.cs"))
                {
                    var ls = File.ReadAllLines(f);
                    for (int i = 0; i < ls.Length; i++)
                    {
                        if (ls[i].TrimStart().StartsWith("//")) continue;
                        if (!(ls[i].Contains("WindowsSystemDriverApi.Open(") || ls[i].Contains("RunasLauncher.Open(") || ls[i].Contains("new WindowsSystemDriverApi(") || ls[i].Contains("new RunasLauncher("))) continue;
                        hits.Add(Path.GetFileName(f) + ":" + (i + 1));
                        if (!ls.Skip(Math.Max(0, i - 2)).Take(Math.Min(i, 2)).Any(l => l.Contains("if (!Gate.DriverChanges)"))) unguarded.Add(Path.GetFileName(f) + ":" + (i + 1));
                    }
                }
                Check(hits.Count == 6 && unguarded.Count == 0, "source: the real functions and runas are reached in " + hits.Count + " places, each right after 'if (!Gate.DriverChanges)' (" + string.Join(", ", hits) + ")");
            }
            else Check(false, "the source folder for the gate check was not found (" + srcDir + "; set CCS_FIXTURE_TOOLS)");
            // ---- the compiled adapter exists, and every Windows function it would call is exported by this PC's system DLLs (looked up, never called)
            var all = pinvokes.Concat(typeof(CcsCatalog).GetMethods(BindingFlags.Static | BindingFlags.NonPublic).Where(x => (x.Attributes & MethodAttributes.PinvokeImpl) != 0)).ToList();
            var missing = new List<string>();
            foreach (var pm in all)
            {
                var di = pm.GetCustomAttribute<DllImportAttribute>(); var entry = string.IsNullOrEmpty(di.EntryPoint) ? pm.Name : di.EntryPoint;
                var h = LoadLibraryExW(di.Value, IntPtr.Zero, 0x800);   // LOAD_LIBRARY_SEARCH_SYSTEM32
                if (h == IntPtr.Zero || GetProcAddress(h, entry) == IntPtr.Zero) missing.Add(di.Value + "!" + entry);
            }
            var names = all.Select(x => x.Name).ToList();
            Check(missing.Count == 0 && new[] { "DiInstallDriverW", "DiUninstallDriverW", "SetupUninstallOEMInfW", "CryptCATOpen", "CryptCATPutMemberInfo", "WinVerifyTrust", "CreateDirectoryW" }.All(names.Contains)
                  && pinvokes.Single(x => x.Name == "DiInstallDriverW").GetCustomAttribute<DllImportAttribute>().Value == "newdev.dll",
                  "the compiled adapter's " + all.Count + " Windows functions (DiInstallDriverW, DiUninstallDriverW, the catalog and trust functions, ...) all exist in this PC's system DLLs" + (missing.Count > 0 ? ": missing " + string.Join(", ", missing) : ""));
            Check(Ldstrs(run.GetMethod("Run")).Contains("runas") && typeof(DriverActionAdapter).GetInterfaces().Contains(typeof(IDriverActions)) && typeof(WindowsSystemDriverApi).GetInterfaces().Contains(typeof(ISystemDriverApi)),
                  "the compiled launcher asks Windows for 'runas'; the adapter and the real functions implement the seams the tests drive");
        }

        static object CallInternal(Type t, string name, params object[] args)
        {
            try { return t.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).Invoke(null, args); }
            catch (TargetInvocationException ex) { throw ex.InnerException; }
        }
        static string FindBuilt(string project, string file)
        {
            var b = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", project));
            return Directory.Exists(b) ? Directory.GetFiles(b, file, SearchOption.AllDirectories).FirstOrDefault() : null;
        }
        static IEnumerable<Type> SafeTypes(Assembly a) { try { return a.GetTypes(); } catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null); } }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibraryExW(string name, IntPtr file, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)] static extern IntPtr GetProcAddress(IntPtr module, string name);

        // ---- a small IL reader: the methods a method body calls or constructs (call, callvirt, newobj, ldftn, ldvirtftn), and its string constants
        static readonly Dictionary<short, OpCode> opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (OpCode)f.GetValue(null)).ToDictionary(o => o.Value);
        static IEnumerable<(OpCode op, int token)> Il(MethodBase m)
        {
            byte[] il; try { il = m.GetMethodBody()?.GetILAsByteArray(); } catch { il = null; }
            if (il == null) yield break;
            for (int i = 0; i < il.Length;)
            {
                short v = il[i] == 0xFE && i + 1 < il.Length ? (short)(0xFE00 | il[i + 1]) : il[i];
                if (!opcodes.TryGetValue(v, out var op)) yield break;
                i += op.Size; int token = 0;
                switch (op.OperandType)
                {
                    case OperandType.InlineNone: break;
                    case OperandType.ShortInlineBrTarget: case OperandType.ShortInlineI: case OperandType.ShortInlineVar: i += 1; break;
                    case OperandType.InlineVar: i += 2; break;
                    case OperandType.InlineI8: case OperandType.InlineR: i += 8; break;
                    case OperandType.InlineSwitch: { int n = BitConverter.ToInt32(il, i); i += 4 + 4 * n; break; }
                    default: token = BitConverter.ToInt32(il, i); i += 4; break;
                }
                yield return (op, token);
            }
        }
        static IEnumerable<MethodBase> Calls(MethodBase m)
        {
            var mod = m.Module; var ta = m.DeclaringType != null && m.DeclaringType.IsGenericType ? m.DeclaringType.GetGenericArguments() : null; var ma = m.IsGenericMethod ? m.GetGenericArguments() : null;
            foreach (var (op, token) in Il(m))
            {
                if (op != OpCodes.Call && op != OpCodes.Callvirt && op != OpCodes.Newobj && op != OpCodes.Ldftn && op != OpCodes.Ldvirtftn) continue;
                MethodBase t = null; try { t = mod.ResolveMethod(token, ta, ma); } catch { }
                if (t != null) yield return t.DeclaringType != null && t.DeclaringType.IsGenericType && !t.DeclaringType.IsGenericTypeDefinition ? t : t;
            }
        }
        static List<string> Ldstrs(MethodBase m) { var l = new List<string>(); foreach (var (op, token) in Il(m)) if (op == OpCodes.Ldstr) try { l.Add(m.Module.ResolveString(token)); } catch { } return l; }
    }
}
