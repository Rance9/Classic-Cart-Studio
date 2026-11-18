// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The USB driver, end to end. Offline work (browsing, exporting, repairing backup files) never comes here. On the first console action that
// needs USB:
//   Assess        reads the PC (only) and classifies what is bound to the console's identity. A compatible association already there is
//                 reused and recorded as pre-existing (RecordReuse): the pinned Hakchi association, or another WinUSB association with a
//                 registered interface (the transport finds any), including Workbench's own package from an earlier setup. A staged package
//                 counts even with no console connected. Anything else bound to the identity (libusb-win32, libusbK, WinUSB without an
//                 interface, another driver) is never replaced. Setup is offered only when nothing is there (and, for the pinned Hakchi
//                 helper's plan, not when it would replace an existing certificate by subject; Workbench's own package deletes nothing).
//   Run           after consent: the record is begun, the step written, the action run, and the PC read back. The result is what was read
//                 back, never the action's report alone; ownership is claimed only for exactly one new package with the planned identity
//                 (for Workbench's package: signed by the certificate this operation made, its subject naming the operation).
//   AtClose       offers removal only when Eligibility allows it; otherwise says what remains and why (or nothing, for a refused setup).
//   Keep/Remove   the close choice; Remove re-checks eligibility on a fresh reading first, then removes exactly the planned package and
//                 certificates, reads back, and never calls a removal waiting for a restart finished. Nothing here restarts the PC. The setup's
//                 certificates still on the PC after it (the step keeps them while the package's removal is not shown finished or anything
//                 may still use them) are recorded as a deferred cleanup.
//   Reconcile     at start: reads the records and the PC (both only) and explains what an interrupted or pending operation left, and whether
//                 a deferred cleanup may now be offered (TrustCleanup.cs).
//   CleanupOffer / KeepCertificate / CleanUp   that later question, the person's choice and the cleanup itself (the rules again on a fresh
//                 reading, exactly the plan through the same elevated step, the read-back).
using System;
using System.Collections.Generic;
using System.Linq;

namespace CcsBackup.Driver
{
    public enum AssociationKind { Unreadable, None, PinnedHakchi, CcsWinUsb, CompatibleWinUsb, WinUsbWithoutInterface, LibUsb0, LibUsbK, OtherDriver }

    public sealed class Assessment
    {
        public AssociationKind Kind;
        public bool Reusable => Kind == AssociationKind.PinnedHakchi || Kind == AssociationKind.CcsWinUsb || Kind == AssociationKind.CompatibleWinUsb;
        public bool SetupNeeded => Kind == AssociationKind.None && !SetupBlocked;
        public bool SetupBlocked;
        public DeviceBinding Device;            // the instance that decided (null when only a staged package did)
        public DriverPackage Package;
        public bool StagedOnly;                 // a package is there but no device instance is bound to it (the console has not been connected)
        public string Message;
        public List<string> Notes = new List<string>();
        public DriverSnapshot Snapshot;
    }

    public sealed class SetupContext
    {
        public IDriverStore Store;
        public IDriverActions Actions;
        public DriverLedger Ledger;
        public DriverSetupPlan Plan;            // null: Workbench's own package (DriverSetupPlan.CcsWinUsb)
        public ElevationState Elevation = ElevationState.Unknown;
        public HostKind Host = HostKind.Standalone;
        public string Tool = "CCS Console Workbench", ToolVersion = "";
    }

    public sealed class SetupOutcome { public SetupResult Result; public string Message; public DriverRecord Record; public Assessment Before, After; }
    public sealed class RemovalOutcome { public RemovalResult Result; public string Message; public RemovalDecision Decision; public DriverRecord Record; }
    public sealed class CleanupOutcome { public CleanupResult Result; public string Message; public CleanupDecision Decision; public DriverRecord Record; }

    public enum CloseKind { Nothing, Message, OfferRemoval }
    public sealed class CloseDecision
    {
        public CloseKind Kind; public string Message; public RemovalDecision Decision;
        public string Title, Body; public string[] Buttons; public int DefaultButton;
    }

    public sealed class ReconcileItem { public string File, OperationId, Status, Message; public bool DriverStillOnPc; public DriverRecord Record; }
    public sealed class ReconcileReport { public List<ReconcileItem> Items = new List<ReconcileItem>(); public DriverSnapshot Now; }

    public static class DriverSetup
    {
        // ------------------------------------------------------------------ assessing (reads only)
        public static Assessment Assess(IDriverStore store, DriverSetupPlan plan = null) => Assess(DriverSnapshot.Capture(store), plan);

        public static Assessment Assess(DriverSnapshot s, DriverSetupPlan plan = null)
        {
            plan = plan ?? DriverSetupPlan.CcsWinUsb();
            var a = new Assessment { Snapshot = s };
            if (s.Error != null) { a.Kind = AssociationKind.Unreadable; a.Message = string.Format(DriverText.UnreadableFormat, s.Error); return a; }
            var bound = s.Devices.Where(d => d.Bound).ToList();
            var present = bound.Where(d => d.Present).ToList();
            var deciding = present.Count > 0 ? present : bound;
            if (deciding.Count > 0)
            {
                // several instances (USB ports) with different drivers: the least compatible decides, so nothing is ever replaced
                var ranked = deciding.Select(d => (d, k: Classify(d, s.Package(d.InfPath)))).OrderBy(x => Rank(x.k)).ToList();
                a.Kind = ranked[0].k; a.Device = ranked[0].d; a.Package = s.Package(a.Device.InfPath);
                if (ranked.Select(x => x.k).Distinct().Count() > 1) a.Notes.Add("Different USB ports on this PC use different drivers for the console.");
            }
            else
            {
                var staged = s.Packages.Where(p => !p.IsInbox && p.DeclaredIds.Any(h => UsbId.Matches(h, PinnedDriver.Vid, PinnedDriver.Pid))).ToList();
                if (staged.Count > 0)
                {
                    var ranked = staged.Select(p => (p, k: ClassifyPackage(p))).OrderBy(x => Rank(x.k)).ToList();
                    a.Kind = ranked[0].k; a.Package = ranked[0].p; a.StagedOnly = true;
                }
                else a.Kind = AssociationKind.None;
            }
            switch (a.Kind)
            {
                case AssociationKind.PinnedHakchi: a.Message = DriverText.PreExisting; break;
                case AssociationKind.CcsWinUsb:
                    a.Message = DriverText.PreExisting;
                    a.Notes.Add("This is the USB driver Workbench sets up (" + (a.Package?.PublishedName ?? a.Device?.InfPath ?? "") + "), from an earlier setup on this PC.");
                    break;
                case AssociationKind.CompatibleWinUsb:
                    a.Message = DriverText.PreExisting;
                    a.Notes.Add("This WinUSB association is not the one Hakchi2 CE 3.9.3 sets up (" + (a.Package?.OriginalName ?? a.Device?.InfPath ?? "unknown package") + "); the console transport finds any WinUSB device with a registered interface.");
                    break;
                case AssociationKind.WinUsbWithoutInterface: a.Message = DriverText.WinUsbWithoutInterface; break;
                case AssociationKind.LibUsb0: a.Message = string.Format(DriverText.IncompatibleFormat, "libusb-win32"); break;
                case AssociationKind.LibUsbK: a.Message = string.Format(DriverText.IncompatibleFormat, "libusbK"); break;
                case AssociationKind.OtherDriver: a.Message = string.Format(DriverText.IncompatibleFormat, a.Device?.Service ?? a.Package?.Services.FirstOrDefault() ?? "unknown"); break;
                case AssociationKind.None:
                    if (plan.DeletesCertificatesBySubject && s.Certificates.Any(c => PinnedDriver.Eq(c.Subject, PinnedDriver.CertificateSubject)))
                    { a.SetupBlocked = true; a.Message = DriverText.CertificateConflict; }   // libwdi deletes certificates by that subject before making its own (pki.c 940-941)
                    else a.Message = DriverText.SetupBody;
                    break;
            }
            return a;
        }

        static AssociationKind Classify(DeviceBinding d, DriverPackage p)
        {
            var svc = d.Service ?? "";
            if (PinnedDriver.Eq(svc, "WinUSB"))
            {
                if (d.InterfaceGuids.Count == 0) return AssociationKind.WinUsbWithoutInterface;
                if (CcsDriver.IsCcsPackage(p) && d.InterfaceGuids.Any(g => PinnedDriver.Eq(g, CcsDriver.InterfaceGuid))) return AssociationKind.CcsWinUsb;
                return PinnedDriver.IsPinned(p) && d.InterfaceGuids.Any(g => PinnedDriver.Eq(g, PinnedDriver.InterfaceGuid)) ? AssociationKind.PinnedHakchi : AssociationKind.CompatibleWinUsb;
            }
            if (PinnedDriver.Eq(svc, "libusb0")) return AssociationKind.LibUsb0;
            if (PinnedDriver.Eq(svc, "libusbK")) return AssociationKind.LibUsbK;
            if (svc == "" && p != null) return ClassifyPackage(p);
            return AssociationKind.OtherDriver;
        }

        static AssociationKind ClassifyPackage(DriverPackage p)
        {
            if (PinnedDriver.IsPinned(p)) return AssociationKind.PinnedHakchi;
            if (CcsDriver.IsCcsPackage(p)) return AssociationKind.CcsWinUsb;
            if (p.Services.Any(x => PinnedDriver.Eq(x, "WinUSB"))) return p.InterfaceGuids.Count > 0 ? AssociationKind.CompatibleWinUsb : AssociationKind.WinUsbWithoutInterface;
            if (p.Services.Any(x => PinnedDriver.Eq(x, "libusb0"))) return AssociationKind.LibUsb0;
            if (p.Services.Any(x => PinnedDriver.Eq(x, "libusbK"))) return AssociationKind.LibUsbK;
            return AssociationKind.OtherDriver;
        }

        static int Rank(AssociationKind k) => Array.IndexOf(new[] { AssociationKind.OtherDriver, AssociationKind.LibUsbK, AssociationKind.LibUsb0, AssociationKind.WinUsbWithoutInterface,
            AssociationKind.CompatibleWinUsb, AssociationKind.CcsWinUsb, AssociationKind.PinnedHakchi, AssociationKind.None, AssociationKind.Unreadable }, k);

        // ------------------------------------------------------------------ reuse and setup
        /// <summary>a compatible association already on the PC is used as it is; the record says so (and nothing is ever offered for removal)</summary>
        public static DriverRecord RecordReuse(SetupContext c, Assessment a)
        {
            if (!a.Reusable) throw new InvalidOperationException("only a compatible association is reused");
            var r = c.Ledger.Begin("reuse", c.Host, c.Tool, c.ToolVersion, c.Elevation, null, a.Snapshot);
            r.Result = SetupResult.Reused; r.After = a.Snapshot; r.State = "finished";
            r.Changes.Add("none: " + a.Kind + (a.StagedOnly ? " (staged package " : " (") + (a.Package?.PublishedName ?? a.Device?.InfPath ?? "") + ")");
            c.Ledger.Save(r); return r;
        }

        /// <summary>after the person chose Set Up Driver. A record is written only when an action is actually attempted.</summary>
        public static SetupOutcome Run(SetupContext c, DriverSetupPlan plan = null)
        {
            plan = plan ?? c.Plan ?? DriverSetupPlan.CcsWinUsb();
            var a = Assess(c.Store, plan);
            if (!a.SetupNeeded) return new SetupOutcome { Result = a.Reusable ? SetupResult.NotNeeded : SetupResult.Blocked, Message = a.Message, Before = a };   // never over another association
            if (!c.Actions.Available) return new SetupOutcome { Result = SetupResult.Unavailable, Message = DriverActionUnavailableException.Text + " " + DriverText.NothingChanged, Before = a };
            var rec = c.Ledger.Begin("setup", c.Host, c.Tool, c.ToolVersion, c.Elevation, plan, a.Snapshot);
            plan.OperationId = rec.OperationId;
            if (plan.IsCcs) plan.CertificateSubject = CcsDriver.SubjectFor(rec.OperationId);
            var step = c.Ledger.BeginStep(rec, "install");
            ActionReport rep;
            try { rep = c.Actions.Install(plan, c.Elevation) ?? new ActionReport { Reported = Reported.Failed, Detail = "no report" }; }
            catch (DriverActionUnavailableException ex) { rep = new ActionReport { Reported = Reported.Unavailable, Detail = ex.Message }; }
            catch (Exception ex) when (!(ex is SimulatedCrashException)) { rep = new ActionReport { Reported = Reported.Failed, Detail = ex.GetType().Name + ": " + ex.Message }; }
            rec.Reported = rep; c.Ledger.EndStep(rec, step, rep.Reported.ToString());

            var check = c.Ledger.BeginStep(rec, "read-back");
            var after = DriverSnapshot.Capture(c.Store); rec.After = after;
            var aa = Assess(after, plan);
            var diff = Diff(a.Snapshot, after);
            rec.Changes = diff.Words;
            var fresh = diff.NewPackages;
            if (fresh.Count == 1 && plan.Matches(fresh[0]) && after.Error == null)
            {
                var p = fresh[0];
                rec.OwnedPackage = p.PublishedName; rec.IntroducedPackages = fresh.Select(x => x.Copy()).ToList();
                rec.BoundByOperation = after.UsersOf(p.PublishedName).ToList();
                rec.IntroducedCertificates = diff.NewCertificates.Where(x => plan.OwnsCertificate(x, p)).ToList();
                rec.OtherNewCertificates = diff.NewCertificates.Where(x => !plan.OwnsCertificate(x, p)).ToList();
                rec.Ownership = "one new package with the planned identity, absent before" + (plan.IsCcs ? ", signed by this operation's own certificate" : "");
            }
            else
            {
                rec.IntroducedPackages = fresh.Select(x => x.Copy()).ToList(); rec.OtherNewCertificates = diff.NewCertificates;
                rec.Ownership = after.Error != null ? "the PC could not be read afterward" : fresh.Count == 0 ? "no new package" : fresh.Count + " new package(s), not exactly one with the planned identity";
            }
            rec.RemovedCertificates = diff.RemovedCertificates;

            SetupResult res; string msg;
            bool oursAppeared = rec.OwnedPackage != null || plan.IsCcs && diff.NewCertificates.Any(x => CcsDriver.IsCcsSubject(x.Subject, rec.OperationId));
            if (after.Error != null) { res = SetupResult.Unconfirmed; msg = DriverText.SetupUnconfirmed; }
            else if (rep.Reported == Reported.Refused && !oursAppeared)
            {   // the elevated step found the PC changed and set up nothing; whatever changed is not Workbench's
                res = SetupResult.Blocked; msg = DriverText.SetupStateChanged + " " + (aa.Reusable ? DriverText.SetupStateChangedReusable : aa.Kind == AssociationKind.None ? DriverText.NothingChanged : aa.Message);
            }
            else if (aa.Kind == plan.ExpectedKind && rec.OwnedPackage != null)
            {
                var restart = rep.Reported == Reported.SuccessRestartNeeded || after.Devices.Any(d => d.NeedsRestart && PinnedDriver.Eq(d.InfPath, rec.OwnedPackage));
                res = restart ? SetupResult.InstalledRestartPending : SetupResult.Installed;
                msg = restart ? DriverText.SetupRestart : after.Devices.Any(d => d.Present && PinnedDriver.Eq(d.InfPath, rec.OwnedPackage)) ? DriverText.SetupDoneConnected : DriverText.SetupDone;
            }
            else if (aa.Reusable && !diff.Empty) { res = SetupResult.InstalledNotOwned; msg = DriverText.SetupDoneNotOwned; }   // usable, but not demonstrably this setup's
            else if (diff.Empty)
            {
                switch (rep.Reported)
                {
                    case Reported.ElevationRefused: res = SetupResult.ElevationRefused; msg = DriverText.SetupRefused + " " + DriverText.NothingChanged; break;
                    case Reported.Canceled: res = SetupResult.Canceled; msg = DriverText.SetupCanceled + " " + DriverText.NothingChanged; break;
                    case Reported.Unavailable: res = SetupResult.Unavailable; msg = DriverActionUnavailableException.Text + " " + DriverText.NothingChanged; break;
                    case Reported.Failed: res = SetupResult.Failed; msg = string.Format(DriverText.SetupFailedFormat, string.IsNullOrEmpty(rep.Detail) ? "no reason given" : rep.Detail) + " " + DriverText.NothingChanged; break;
                    default: res = SetupResult.ReportedButMissing; msg = DriverText.SetupReportedButMissing + " " + DriverText.NothingChanged; break;
                }
            }
            else { res = SetupResult.Partial; msg = string.Format(DriverText.SetupPartialFormat, string.Join("; ", diff.Words)); }
            rec.Result = res;
            c.Ledger.EndStep(rec, check, res.ToString());
            rec.State = "finished"; c.Ledger.Save(rec);
            return new SetupOutcome { Result = res, Message = msg, Record = rec, Before = a, After = aa };
        }

        // ------------------------------------------------------------------ closing
        public static CloseDecision AtClose(DriverRecord rec, IDriverStore store, DriverUsage usage, ElevationState elevation)
        {
            if (rec == null) return new CloseDecision { Kind = CloseKind.Nothing };
            if (rec.Kind == "reuse") return new CloseDecision { Kind = CloseKind.Message, Message = DriverText.PreExisting };
            if (rec.Kind == "setup" && !rec.SetupIncomplete && !rec.Installed && (rec.Changes.Count == 0 || rec.Result == SetupResult.Blocked)) return new CloseDecision { Kind = CloseKind.Nothing };
            var now = DriverSnapshot.Capture(store);
            var d = Eligibility.Evaluate(rec, now, usage);
            if (d.Offer)
                return new CloseDecision { Kind = CloseKind.OfferRemoval, Decision = d, Title = DriverText.CloseTitle, Body = DriverText.CloseBodyFor(elevation), Buttons = DriverText.CloseButtons, DefaultButton = DriverText.CloseDefaultButton };
            if (d.Reasons.Count == 1 && d.Primary == RetainReason.PackageMissing) return new CloseDecision { Kind = CloseKind.Message, Decision = d, Message = DriverText.RemovalGone };
            return new CloseDecision { Kind = CloseKind.Message, Decision = d, Message = d.KeptMessage };
        }

        /// <summary>"No — Keep Driver": recorded; nothing else happens</summary>
        public static void Keep(SetupContext c, DriverRecord rec) { rec.CloseChoice = "kept"; c.Ledger.Save(rec); }

        /// <summary>"Yes — Remove Driver": eligibility again on a fresh reading, then exactly the plan, then the read-back</summary>
        public static RemovalOutcome Remove(SetupContext c, DriverRecord rec, DriverUsage usage)
        {
            var now = DriverSnapshot.Capture(c.Store);
            var d = Eligibility.Evaluate(rec, now, usage);
            if (!d.Offer) return new RemovalOutcome { Result = RemovalResult.NotEligible, Decision = d, Record = rec, Message = d.Reasons.SequenceEqual(new[] { RetainReason.PackageMissing }) ? DriverText.RemovalGone : d.KeptMessage };
            if (!c.Actions.Available) return new RemovalOutcome { Result = RemovalResult.Unavailable, Decision = d, Record = rec, Message = DriverText.Kept(DriverText.Because(RetainReason.ActionUnavailable)) };
            rec.CloseChoice = "remove"; rec.RemovalPlan = d.Plan; rec.RemovalBefore = now; rec.RemovalState = "open"; c.Ledger.Save(rec);
            var step = c.Ledger.BeginStep(rec, "removal");
            ActionReport rep;
            try { rep = c.Actions.Remove(d.Plan, c.Elevation) ?? new ActionReport { Reported = Reported.Failed, Detail = "no report" }; }
            catch (DriverActionUnavailableException ex) { rep = new ActionReport { Reported = Reported.Unavailable, Detail = ex.Message }; }
            catch (Exception ex) when (!(ex is SimulatedCrashException)) { rep = new ActionReport { Reported = Reported.Failed, Detail = ex.GetType().Name + ": " + ex.Message }; }
            rec.RemovalReported = rep; c.Ledger.EndStep(rec, step, rep.Reported.ToString());

            var check = c.Ledger.BeginStep(rec, "removal-read-back");
            var after = DriverSnapshot.Capture(c.Store); rec.RemovalAfter = after;
            var pkg = after.Package(d.Plan.PublishedName);
            bool pkgGone = after.Error == null && pkg == null;
            bool accepted = rep.Reported == Reported.Success || rep.Reported == Reported.SuccessRestartNeeded;   // Windows took the uninstall (its word only)
            bool pending = rep.Reported == Reported.SuccessRestartNeeded || pkg != null && pkg.PendingRemoval
                || after.Devices.Any(x => x.NeedsRestart && rec.BoundByOperation.Any(b => PinnedDriver.Eq(b, x.InstanceId)));
            bool unchanged = after.Error == null && pkg != null && pkg.SameContent(now.Package(d.Plan.PublishedName)) && !pkg.PendingRemoval
                && d.Plan.Certificates.All(x => after.HasCertificate(x.Store, x.Thumbprint));
            // the setup's certificates still here: those that stay for good (on the PC before, or changed since) are not waited for
            var forGood = d.CertificatesKept.Where(k => k.Why == RetainReason.CertificatePreExisting || k.Why == RetainReason.CertificateChanged).Select(k => k.Certificate.Key).ToList();
            var left = rec.IntroducedCertificates.Where(x => !forGood.Contains(x.Key) && (after.Error != null || after.HasCertificate(x.Store, x.Thumbprint))).ToList();
            bool planLeft = d.Plan.Certificates.Any(x => after.Error != null || after.HasCertificate(x.Store, x.Thumbprint));
            RemovalResult res; string msg; bool defer = left.Count > 0;
            var certNote = d.CertificatesKept.Count > 0 ? " " + string.Format(DriverText.CertificateKeptFormat, DriverText.Because(d.CertificatesKept[0].Why))
                : d.Plan.Certificates.Count > 0 && !planLeft ? " " + DriverText.CertificateRemoved : "";
            if (after.Error != null)
            {   // unknown: kept for a later check whenever the step may have changed something (it ran, or stopped part way)
                res = RemovalResult.Unconfirmed; msg = accepted ? DriverText.RemovalUnconfirmed : DriverText.Kept(DriverText.Because(RetainReason.StateUnreadable));
                defer = left.Count > 0 && (accepted || rep.Reported == Reported.Failed);
            }
            else if (unchanged && !accepted)
            {
                defer = false;
                if (rep.Reported == Reported.Refused) { res = RemovalResult.NotEligible; msg = DriverText.Kept(DriverText.Because(RetainReason.RefusedAtLastCheck)); }
                else
                {
                    res = rep.Reported == Reported.ElevationRefused ? RemovalResult.ElevationRefused : rep.Reported == Reported.Unavailable ? RemovalResult.Unavailable : RemovalResult.Failed;
                    var why = res == RemovalResult.ElevationRefused ? RetainReason.ElevationRefused : res == RemovalResult.Unavailable ? RetainReason.ActionUnavailable : RetainReason.RemovalFailed;
                    msg = DriverText.Kept(DriverText.Because(why) + (res == RemovalResult.Failed && !string.IsNullOrEmpty(rep.Detail) ? " (" + rep.Detail + ")" : ""));
                }
            }
            else if (pending && (pkgGone || pkg.PendingRemoval)) { res = RemovalResult.RemovedRestartPending; msg = DriverText.RemovalRestart + (left.Count > 0 ? " " + DriverText.CertificateKeptUntilFinished : ""); }
            else if (pkgGone && !planLeft) { res = RemovalResult.Removed; msg = DriverText.Removed + certNote + (left.Count > 0 ? " " + DriverText.CleanupLater : "") + " " + DriverText.RemovedLogsRemainFor(rec.Plan); }
            else if (pkgGone)
            {   // the package went but a certificate the step was to remove is still here: why, from the reading just taken
                res = RemovalResult.Partial;
                var t = d.Plan.Certificates.First(x => after.HasCertificate(x.Store, x.Thumbprint)).Thumbprint;
                var why = !after.CertificateScanComplete ? RetainReason.CertificateSharingUnknown : after.CertificateUsers.TryGetValue(UsbId.NormThumbprint(t), out var u) && u.Count > 0 ? RetainReason.CertificateShared
                    : rep.Reported != Reported.Success ? RetainReason.StepInterrupted : RetainReason.RemovalFailed;
                msg = DriverText.Removed + " " + string.Format(DriverText.CertificateKeptFormat, DriverText.Because(why)) + " " + DriverText.CleanupLater;
            }
            else if (accepted) { res = RemovalResult.Unconfirmed; msg = DriverText.RemovalNotFinished; }   // Windows said removed, and it is still here
            else { res = RemovalResult.Partial; msg = DriverText.Kept(DriverText.Because(RetainReason.RemovalIncomplete)); }
            if (defer)
            {   // kept until removing them is shown safe; a restart since the removal must be seen first unless Windows and the reading agree it finished
                rec.CleanupState = "deferred"; rec.DeferredCertificates = left.Select(x => CertificateEntry.From(Json.Parse(Json.Write(x.ToJson())))).ToList();
                rec.CleanupRestartFirst = !(rep.Reported == Reported.Success && pkgGone && !pending); rec.CleanupAfterUtc = now.TakenUtc; rec.CleanupBootUtc = now.Restart?.BootUtc;
            }
            rec.Removal = res;
            c.Ledger.EndStep(rec, check, res.ToString());
            rec.RemovalState = "finished"; c.Ledger.Save(rec);
            return new RemovalOutcome { Result = res, Message = msg, Decision = d, Record = rec };
        }

        // ------------------------------------------------------------------ the certificate a removal kept (TrustCleanup.cs)
        /// <summary>whether the later cleanup may be offered now (reads only): the question, a message saying why not, or nothing</summary>
        public static CloseDecision CleanupOffer(DriverRecord rec, IDriverStore store, DriverUsage usage, ElevationState elevation, bool actionsAvailable)
        {
            var d = CleanupEligibility.Evaluate(rec, store, DriverSnapshot.Capture(store), usage);
            if (!d.Applicable || d.Present.Count == 0 && d.Reasons.Count == 0 || rec.CleanupState == "kept") return new CloseDecision { Kind = CloseKind.Nothing };
            if (!d.Offer) return new CloseDecision { Kind = CloseKind.Message, Message = string.Format(DriverText.CleanupKeptFormat, DriverText.Because(d.Primary)) };
            if (!actionsAvailable) return new CloseDecision { Kind = CloseKind.Message, Message = string.Format(DriverText.CleanupKeptFormat, DriverText.Because(RetainReason.ActionUnavailable)) };
            return new CloseDecision { Kind = CloseKind.OfferRemoval, Title = DriverText.CleanupTitle, Body = DriverText.CleanupBodyFor(elevation), Buttons = DriverText.CleanupButtons, DefaultButton = DriverText.CleanupDefaultButton };
        }

        /// <summary>"No — Keep Certificate": recorded; never offered again</summary>
        public static void KeepCertificate(SetupContext c, DriverRecord rec) { rec.CleanupState = "kept"; c.Ledger.Save(rec); }

        /// <summary>"Yes — Remove Certificate": the rules again on a fresh reading, then exactly the plan, then the read-back</summary>
        public static CleanupOutcome CleanUp(SetupContext c, DriverRecord rec, DriverUsage usage)
        {
            var now = DriverSnapshot.Capture(c.Store);
            var d = CleanupEligibility.Evaluate(rec, c.Store, now, usage);
            if (!d.Offer) return new CleanupOutcome { Result = CleanupResult.NotEligible, Decision = d, Record = rec, Message = string.Format(DriverText.CleanupKeptFormat, DriverText.Because(d.Primary)) };
            if (!c.Actions.Available) return new CleanupOutcome { Result = CleanupResult.Unavailable, Decision = d, Record = rec, Message = string.Format(DriverText.CleanupKeptFormat, DriverText.Because(RetainReason.ActionUnavailable)) };
            var scope = CleanupScope.Of(rec);
            if (rec.CleanupState == null)
            {   // the window ended during the removal: what it planned becomes the deferred cleanup (only after a restart)
                rec.DeferredCertificates = scope.Certificates.ToList(); rec.CleanupRestartFirst = scope.RestartFirst; rec.CleanupAfterUtc = scope.AfterUtc; rec.CleanupBootUtc = scope.BootThenUtc;
            }
            rec.CleanupPlan = d.Plan; rec.CleanupBefore = now; rec.CleanupState = "open"; c.Ledger.Save(rec);
            var step = c.Ledger.BeginStep(rec, "removal-cleanup");
            ActionReport rep;
            try { rep = c.Actions.CleanUp(d.Plan, c.Elevation) ?? new ActionReport { Reported = Reported.Failed, Detail = "no report" }; }
            catch (DriverActionUnavailableException ex) { rep = new ActionReport { Reported = Reported.Unavailable, Detail = ex.Message }; }
            catch (Exception ex) when (!(ex is SimulatedCrashException)) { rep = new ActionReport { Reported = Reported.Failed, Detail = ex.GetType().Name + ": " + ex.Message }; }
            rec.CleanupReported = rep; c.Ledger.EndStep(rec, step, rep.Reported.ToString());

            var check = c.Ledger.BeginStep(rec, "removal-cleanup-read-back");
            var after = DriverSnapshot.Capture(c.Store); rec.CleanupAfter = after;
            var removed = d.Plan.Certificates.Where(x => after.Error == null && !after.HasCertificate(x.Store, x.Thumbprint)).ToList();
            var left = rec.DeferredCertificates.Where(x => after.Error != null || after.HasCertificate(x.Store, x.Thumbprint)).ToList();
            CleanupResult res; string msg;
            if (after.Error != null) { res = CleanupResult.Unconfirmed; msg = DriverText.CleanupUnconfirmed; }
            else if (left.Count == 0) { res = CleanupResult.Removed; msg = DriverText.CleanupRemoved; }
            else if (removed.Count > 0)
            {
                res = CleanupResult.Partial;
                msg = string.Format(DriverText.CleanupPartialFormat, string.Join(" and ", removed.Select(x => StoreName(x.Store)).Distinct()), string.Join(" and ", left.Select(x => StoreName(x.Store)).Distinct()));
            }
            else
            {
                res = rep.Reported == Reported.ElevationRefused ? CleanupResult.ElevationRefused : rep.Reported == Reported.Unavailable ? CleanupResult.Unavailable
                    : rep.Reported == Reported.Refused ? CleanupResult.NotEligible : CleanupResult.Failed;
                var why = res == CleanupResult.ElevationRefused ? RetainReason.ElevationRefused : res == CleanupResult.Unavailable ? RetainReason.ActionUnavailable
                    : res == CleanupResult.NotEligible ? RetainReason.RefusedAtLastCheck : RetainReason.RemovalFailed;
                msg = string.Format(DriverText.CleanupKeptFormat, DriverText.Because(why) + (res == CleanupResult.Failed && !string.IsNullOrEmpty(rep.Detail) ? " (" + rep.Detail + ")" : ""));
            }
            rec.Cleanup = res.ToString();
            c.Ledger.EndStep(rec, check, res.ToString());
            if (after.Error == null) rec.DeferredCertificates = left;
            rec.CleanupState = after.Error == null && left.Count == 0 ? "finished" : "deferred"; c.Ledger.Save(rec);
            return new CleanupOutcome { Result = res, Message = msg, Decision = d, Record = rec };
        }

        // ------------------------------------------------------------------ the startup check (reads only: no record and nothing on the PC is changed)
        public static ReconcileReport Reconcile(DriverLedger ledger, IDriverStore store)
        {
            var rep = new ReconcileReport();
            var entries = ledger.ReadAll();
            if (entries.Count == 0) return rep;
            var now = rep.Now = DriverSnapshot.Capture(store);
            foreach (var e in entries)
            {
                var it = new ReconcileItem { File = e.File };
                if (e.Record == null) { it.Status = "unreadable"; it.Message = string.Format(DriverText.ReconcileUnreadableFormat, e.Error); rep.Items.Add(it); continue; }
                var r = e.Record; it.OperationId = r.OperationId;
                if (now.Error != null) { it.Status = "pc-unreadable"; it.Message = string.Format(DriverText.UnreadableFormat, now.Error); rep.Items.Add(it); continue; }
                if (!PinnedDriver.Eq(r.PcId, now.PcId)) { it.Status = "other-pc"; it.Message = DriverText.ReconcileOtherPc; rep.Items.Add(it); continue; }
                if (r.Kind == "reuse") continue;
                if (r.SetupIncomplete)
                {
                    var d = Diff(r.Before, now);
                    it.DriverStillOnPc = d.NewPackages.Any(p => r.Plan?.Matches(p) ?? PinnedDriver.IsPinned(p));
                    it.Status = "incomplete-setup";
                    it.Message = DriverText.ReconcileIncompleteSetup + " " + (it.DriverStillOnPc ? DriverText.ReconcileDriverPresent + " " + DriverText.Kept(DriverText.Because(RetainReason.RecordIncomplete))
                        : DriverText.ReconcileDriverAbsent + (d.NewCertificates.Count > 0 ? " " + DriverText.ReconcileCertificateRemains : ""));
                    rep.Items.Add(it); continue;
                }
                var owned = r.OwnedPackage == null ? null : now.Package(r.OwnedPackage);
                it.DriverStillOnPc = owned != null; it.Record = r;
                var cleanup = CleanupEligibility.Evaluate(r, store, now, null);   // the certificates a removal kept, if any (reads only)
                if (r.RemovalState == "open")
                {
                    it.Status = "incomplete-removal";
                    var certLeft = r.IntroducedCertificates.Concat(r.RemovalPlan?.Certificates ?? new List<CertificateEntry>()).Any(x => now.HasCertificate(x.Store, x.Thumbprint));
                    if (owned != null) it.Message = DriverText.ReconcileIncompleteRemoval + " " + DriverText.Kept(DriverText.Because(RetainReason.RemovalIncomplete));
                    else
                    {
                        it.Message = DriverText.ReconcileIncompleteRemoval + " " + DriverText.ReconcileRemovalFinished + " ";
                        if (!certLeft) it.Message += DriverText.ReconcileMayNeedRestart;
                        else if (cleanup.Offer) { it.Status = "cleanup-ready"; it.Message += DriverText.CertificateNotRemoved + " " + DriverText.ReconcileNothingUsesIt; }
                        else if (cleanup.Applicable && (cleanup.Primary == RetainReason.RestartNotSeen || cleanup.Primary == RetainReason.RestartPending))
                            it.Message += DriverText.CertificateNotRemoved + " " + DriverText.ReconcileOfferAfterRestart;
                        else if (cleanup.Applicable) it.Message += DriverText.CertificateNotRemoved + " " + string.Format(DriverText.CertificateKeptFormat, DriverText.Because(cleanup.Primary));
                        else it.Message += DriverText.CertificateNotRemoved;   // the person chose to keep it
                    }
                }
                else if (cleanup.Applicable)
                {   // certificates a removal kept until removing them is shown safe
                    if (cleanup.Present.Count == 0 && cleanup.Reasons.Count == 0) continue;   // none left: nothing to explain
                    if (cleanup.Offer) { it.Status = "cleanup-ready"; it.Message = DriverText.ReconcileCleanupReady; }
                    else if (cleanup.Primary == RetainReason.RestartPending && owned != null) { it.Status = "removal-waiting-for-restart"; it.Message = DriverText.ReconcileWaitingForRestart + " " + DriverText.ReconcileCertificateUntilRestart; }
                    else if (cleanup.Primary == RetainReason.RestartNotSeen || cleanup.Primary == RetainReason.RestartPending) { it.Status = "removal-waiting-for-restart"; it.Message = DriverText.ReconcileCleanupAfterRestart; }
                    else if (cleanup.Primary == RetainReason.PackageStillPresent) { it.Status = "cleanup-deferred"; it.Message = DriverText.ReconcileRemovalNotFinished; }
                    else { it.Status = "cleanup-deferred"; it.Message = string.Format(DriverText.ReconcileCleanupKeptFormat, DriverText.Because(cleanup.Primary)); }
                }
                else if (r.Removal == RemovalResult.RemovedRestartPending)
                {
                    if (owned != null || now.Devices.Any(x => x.NeedsRestart)) { it.Status = "removal-waiting-for-restart"; it.Message = DriverText.ReconcileWaitingForRestart; }
                    else continue;   // finished after the restart: nothing to explain
                }
                else if (r.Removal == RemovalResult.Removed) continue;
                else if (r.Installed)
                {
                    if (owned != null) { it.Status = "installed"; it.Message = string.Format(DriverText.ReconcileInstalledFormat, (r.StartedUtc ?? "").Split('T')[0]); }
                    else { it.Status = "gone"; it.Message = DriverText.RemovalGone; }
                }
                else if (r.Result == SetupResult.Partial || r.Result == SetupResult.Unconfirmed)
                {
                    var d = Diff(r.Before, now);
                    if (d.Empty) continue;
                    it.Status = "partial"; it.Message = string.Format(DriverText.ReconcilePartialFormat, string.Join("; ", d.Words));
                }
                else continue;
                rep.Items.Add(it);
            }
            return rep;
        }

        // ------------------------------------------------------------------ what changed between two readings
        public sealed class Changes
        {
            public List<DriverPackage> NewPackages = new List<DriverPackage>(), ChangedPackages = new List<DriverPackage>(), GonePackages = new List<DriverPackage>();
            public List<CertificateEntry> NewCertificates = new List<CertificateEntry>(), RemovedCertificates = new List<CertificateEntry>();
            public List<string> Rebound = new List<string>();
            public List<CertStore> UnexplainedStores = new List<CertStore>();   // a store whose count or contents changed beyond the certificates listed
            public List<string> Words = new List<string>();
            public bool Empty => Words.Count == 0;
        }

        public static Changes Diff(DriverSnapshot before, DriverSnapshot after)
        {
            var c = new Changes();
            if (before == null || after == null) { c.Words.Add("the PC could not be compared (a reading is missing)"); return c; }
            foreach (var p in after.Packages.Where(p => !p.IsInbox))
            {
                var b = before.Package(p.PublishedName);
                if (b == null) c.NewPackages.Add(p); else if (!b.SameContent(p) || b.PendingRemoval != p.PendingRemoval) c.ChangedPackages.Add(p);
            }
            c.GonePackages = before.Packages.Where(p => !p.IsInbox && after.Package(p.PublishedName) == null).ToList();
            c.NewCertificates = after.Certificates.Where(x => !before.HasCertificate(x.Store, x.Thumbprint)).ToList();
            c.RemovedCertificates = before.Certificates.Where(x => !after.HasCertificate(x.Store, x.Thumbprint)).ToList();
            foreach (var d in after.Devices)
            {
                var b = before.Devices.FirstOrDefault(x => PinnedDriver.Eq(x.InstanceId, d.InstanceId));
                if (b == null ? d.Bound : !PinnedDriver.Eq(b.InfPath, d.InfPath) || !PinnedDriver.Eq(b.Service, d.Service)) c.Rebound.Add(d.InstanceId);
            }
            foreach (var p in c.NewPackages) c.Words.Add("driver package " + p.PublishedName + " (" + p.OriginalName + ") added");
            foreach (var p in c.ChangedPackages) c.Words.Add("driver package " + p.PublishedName + " changed");
            foreach (var p in c.GonePackages) c.Words.Add("driver package " + p.PublishedName + " (" + p.OriginalName + ") removed");
            foreach (var x in c.NewCertificates) c.Words.Add("certificate " + x.Thumbprint + " added to " + StoreName(x.Store));
            foreach (var x in c.RemovedCertificates) c.Words.Add("certificate " + x.Thumbprint + " removed from " + StoreName(x.Store));
            foreach (var i in c.Rebound) c.Words.Add("device " + i + " now uses " + (after.Devices.First(x => PinnedDriver.Eq(x.InstanceId, i)).InfPath is string s && s != "" ? s : "no driver"));
            // the trust-store audit: every certificate added to or taken from Root or Trusted Publishers must be one of those listed above
            foreach (CertStore cs in Enum.GetValues(typeof(CertStore)))
            {
                var (n0, d0) = Summary(before, cs); var (n1, d1) = Summary(after, cs);
                if (n0 < 0 || n1 < 0) continue;
                var listed = c.NewCertificates.Count(x => x.Store == cs) - c.RemovedCertificates.Count(x => x.Store == cs);
                if (n1 - n0 != listed || listed == 0 && !c.NewCertificates.Concat(c.RemovedCertificates).Any(x => x.Store == cs) && d0 != d1)
                { c.UnexplainedStores.Add(cs); c.Words.Add("other certificates in " + StoreName(cs) + " changed (" + n0 + " before, " + n1 + " after)"); }
            }
            return c;
        }

        /// <summary>a store's certificate count and digest from a reading (count -1 when the reading has none)</summary>
        static (int count, string digest) Summary(DriverSnapshot s, CertStore cs)
        {
            if (s?.StoreSummary == null || !s.StoreSummary.TryGetValue(cs.ToString(), out var v) || v == null) return (-1, null);
            var parts = v.Split(' '); int n = -1;
            foreach (var p in parts) if (p.StartsWith("count:")) int.TryParse(p.Substring(6), out n);
            return (n, parts.FirstOrDefault(p => p.StartsWith("sha256:")));
        }

        public static string StoreName(CertStore s) => s == CertStore.Root ? "Trusted Root Certification Authorities" : "Trusted Publishers";
    }
}
