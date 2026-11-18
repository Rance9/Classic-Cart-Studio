// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// When removing the USB driver may even be offered. Removal is offered only for a package this operation demonstrably added: its
// record is complete, the setup's result was read back as installed, exactly one new package with the planned identity appeared and nothing
// like it existed before (a pre-staged package is pre-existing even when the console was absent), it is unchanged now, every device instance
// bound to it is one the setup itself bound, nothing is waiting for a restart, and nothing is using or about to use the console (a console
// operation, a write, an unsettled result, the parent app of an embedded Workbench, another Workbench operation, another program such as
// Hakchi: WinUSB serves one program at a time). Every other case keeps it and says why. Inbox Windows drivers, earlier Hakchi packages and
// other programs' packages are never removable. A certificate goes with the package only when the setup added it (exact thumbprint, absent
// before), it is unchanged and no other package is signed with it; it is never chosen by its subject name.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace CcsBackup.Driver
{
    public enum RetainReason
    {
        RecordIncomplete, RemovalIncomplete, SetupNotConfirmed, OwnershipUncertain, PreExisting, InboxDriver, OtherToolPackage, PackageMissing, PackageChanged,
        SharedWithOtherDevice, RestartPending, ConsoleOperationActive, ConsoleWriteInProgress, UnresolvedConsoleResult, ParentAppNeedsIt, AnotherWorkbenchOperation,
        OtherProgramMayUseIt, StateUnreadable, OtherPc, ElevationRefused, RemovalFailed, ActionUnavailable, RefusedAtLastCheck,
        CertificatePreExisting, CertificateShared, CertificateChanged, CertificateSharingUnknown, CertificateWithKeptDriver,
        PackageStillPresent, RestartNotSeen, StepInterrupted,
    }

    /// <summary>what still needs the console connection at the moment removal would be offered (the host fills it in)</summary>
    public sealed class DriverUsage
    {
        public HostKind Host = HostKind.Standalone;
        public bool ParentAppNeedsDevice;          // embedded: Classic Cart Studio still uses the console connection
        public bool OtherWorkbenchOperation;       // another Workbench operation (window or worker) still needs it
        public bool ConsoleOperationActive;        // a console operation is running (the console lock is held)
        public bool ConsoleWriteInProgress;        // a write to the console has started and not finished
        public bool UnresolvedConsoleResult;       // the last console operation's result is not settled (it must be checked before anything else)
        public List<string> OtherProgramsOpen = new List<string>();   // e.g. "Hakchi" when hakchi.exe is running

        /// <summary>other programs that open the console through the same driver (process names only; reads the process list)</summary>
        public static List<string> OtherProgramsFromProcesses()
        {
            var l = new List<string>();
            try { if (Process.GetProcessesByName("hakchi").Length > 0) l.Add("Hakchi"); } catch { }
            return l;
        }
    }

    public sealed class RemovalDecision
    {
        public List<RetainReason> Reasons = new List<RetainReason>();      // empty: removal may be offered
        public RemovalPlan Plan;                                           // set only when offered
        public List<(CertificateEntry Certificate, RetainReason Why)> CertificatesKept = new List<(CertificateEntry, RetainReason)>();
        public bool Offer => Reasons.Count == 0 && Plan != null;
        public RetainReason Primary => Reasons.Count > 0 ? Reasons[0] : RetainReason.OwnershipUncertain;
        /// <summary>"The USB driver was kept because ... No other driver was removed."</summary>
        public string KeptMessage => DriverText.Kept(DriverText.Because(Primary));
    }

    public static class Eligibility
    {
        public static RemovalDecision Evaluate(DriverRecord rec, DriverSnapshot now, DriverUsage usage)
        {
            var d = new RemovalDecision(); var r = d.Reasons;
            usage = usage ?? new DriverUsage();
            if (rec == null) { r.Add(RetainReason.OwnershipUncertain); return d; }
            if (now == null || now.Error != null) r.Add(RetainReason.StateUnreadable);
            else if (!PinnedDriver.Eq(rec.PcId, now.PcId) || string.IsNullOrEmpty(rec.PcId)) r.Add(RetainReason.OtherPc);
            if (rec.Kind == "reuse") r.Add(RetainReason.PreExisting);
            if (rec.SetupIncomplete) r.Add(RetainReason.RecordIncomplete);
            if (rec.RemovalState == "open") r.Add(RetainReason.RemovalIncomplete);
            if (rec.Kind == "setup" && !rec.SetupIncomplete && !rec.Installed) r.Add(rec.Result == SetupResult.InstalledNotOwned ? RetainReason.OwnershipUncertain : RetainReason.SetupNotConfirmed);

            // what the setup demonstrably added
            var owned = rec.OwnedPackage == null ? null : rec.IntroducedPackages.FirstOrDefault(p => PinnedDriver.Eq(p.PublishedName, rec.OwnedPackage));
            if (rec.Kind == "setup" && rec.Installed && owned == null) r.Add(RetainReason.OwnershipUncertain);
            if (owned != null)
            {
                if (owned.IsInbox || !UsbId.IsPublishedName(owned.PublishedName)) r.Add(RetainReason.InboxDriver);
                else if (rec.Plan == null || !rec.Plan.Matches(owned)) r.Add(RetainReason.OtherToolPackage);
                if (rec.Before == null || rec.Before.Error != null) r.Add(RetainReason.OwnershipUncertain);
                else if (rec.Before.Packages.Any(p => PinnedDriver.Eq(p.PublishedName, owned.PublishedName) || !p.IsInbox && PinnedDriver.Eq(p.InfSha256, owned.InfSha256)))
                    r.Add(RetainReason.PreExisting);    // the same package was there before (staged, even with no console connected)
                else if (rec.Before.Packages.Any(p => !p.IsInbox && rec.Plan != null && rec.Plan.MatchesKind(p)))
                    r.Add(RetainReason.OwnershipUncertain);   // an earlier package of the same kind was there: which one Windows uses is not ours to judge
            }

            // the PC now
            if (owned != null && now != null && now.Error == null)
            {
                var cur = now.Package(owned.PublishedName);
                if (cur == null) r.Add(RetainReason.PackageMissing);
                else
                {
                    if (cur.IsInbox) r.Add(RetainReason.InboxDriver);
                    if (!cur.SameContent(owned) || rec.Plan == null || !rec.Plan.Matches(cur)) r.Add(RetainReason.PackageChanged);
                    if (cur.PendingRemoval) r.Add(RetainReason.RestartPending);
                    var users = now.UsersOf(owned.PublishedName);
                    if (users.Any(u => !rec.BoundByOperation.Any(b => PinnedDriver.Eq(b, u)))) r.Add(RetainReason.SharedWithOtherDevice);
                    if (now.Devices.Any(x => x.NeedsRestart && (PinnedDriver.Eq(x.InfPath, owned.PublishedName) || users.Any(u => PinnedDriver.Eq(u, x.InstanceId))))) r.Add(RetainReason.RestartPending);
                }
            }
            if (rec.Result == SetupResult.InstalledRestartPending && now != null && now.Devices.Any(x => x.NeedsRestart)) { if (!r.Contains(RetainReason.RestartPending)) r.Add(RetainReason.RestartPending); }

            // what still needs the console connection
            if (usage.ConsoleWriteInProgress) r.Add(RetainReason.ConsoleWriteInProgress);
            if (usage.UnresolvedConsoleResult) r.Add(RetainReason.UnresolvedConsoleResult);
            if (usage.ConsoleOperationActive) r.Add(RetainReason.ConsoleOperationActive);
            if (usage.Host == HostKind.Embedded && usage.ParentAppNeedsDevice) r.Add(RetainReason.ParentAppNeedsIt);
            if (usage.OtherWorkbenchOperation) r.Add(RetainReason.AnotherWorkbenchOperation);
            if (usage.OtherProgramsOpen.Count > 0) r.Add(RetainReason.OtherProgramMayUseIt);

            d.Reasons = r.Distinct().ToList();
            // certificates: each on its own; never by subject; kept with a kept driver
            foreach (var c in rec.IntroducedCertificates)
            {
                if (d.Reasons.Count > 0 || owned == null) { d.CertificatesKept.Add((c, RetainReason.CertificateWithKeptDriver)); continue; }
                var why = CertificateProblem(rec, c, owned, now);
                if (why.HasValue) d.CertificatesKept.Add((c, why.Value));
            }
            if (d.Reasons.Count == 0 && owned != null)
            {
                var cur = now.Package(owned.PublishedName);
                d.Plan = new RemovalPlan { OperationId = rec.OperationId, PublishedName = cur.PublishedName, OriginalName = cur.OriginalName, InfSha256 = cur.InfSha256, CatalogSha256 = cur.CatalogSha256,
                    Devices = rec.BoundByOperation.ToList() };
                foreach (var c in rec.IntroducedCertificates)
                    if (!d.CertificatesKept.Any(k => k.Certificate.Key == c.Key)) d.Plan.Certificates.Add(now.Certificates.First(x => x.Key == c.Key));
                if (d.Plan.Problems().Count > 0) { d.Reasons.Add(RetainReason.OwnershipUncertain); d.Plan = null; }
            }
            return d;
        }

        static RetainReason? CertificateProblem(DriverRecord rec, CertificateEntry c, DriverPackage owned, DriverSnapshot now)
        {
            if (!UsbId.IsThumbprint(UsbId.NormThumbprint(c.Thumbprint))) return RetainReason.CertificateChanged;
            if (rec.Before == null || rec.Before.Error != null || rec.Before.HasCertificate(c.Store, c.Thumbprint)) return RetainReason.CertificatePreExisting;
            var cur = now.Certificates.FirstOrDefault(x => x.Key == c.Key);
            if (cur == null) return RetainReason.CertificateChanged;           // gone or moved: nothing of ours to remove there
            if (!cur.SameAs(c)) return RetainReason.CertificateChanged;
            if (!now.CertificateScanComplete) return RetainReason.CertificateSharingUnknown;
            if (!now.CertificateUsers.TryGetValue(UsbId.NormThumbprint(c.Thumbprint), out var users)) return RetainReason.CertificateSharingUnknown;
            if (users.Any(u => !PinnedDriver.Eq(u, owned.PublishedName))) return RetainReason.CertificateShared;
            return null;
        }
    }
}
