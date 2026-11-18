// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The trust certificate a Workbench setup added, once its driver package has been asked to leave this PC. The certificate leaves only when
// this PC, read again, shows every one of these: the package is gone (read by its own published name, and no device instance still names
// it); its removal is not waiting for a restart (Windows' restart flag, a device waiting for a restart, or a removal whose end was never
// confirmed until a restart of Windows has been seen since); every package catalog was read and none is signed with the certificate; and
// the certificate is still exactly the one the setup added (store and thumbprint, the same subject naming that setup's operation, the same
// expiry, no private key). Otherwise it stays, and the record says the cleanup is deferred (DriverRecord.CleanupState). One reading rule,
// TrustCleanup.Check, serves the elevated removal step right after Windows' uninstall (DriverActionHost), the window's read-back
// (DriverSetup.Remove), the startup check (DriverSetup.Reconcile) and a later cleanup (CleanupEligibility, DriverSetup.CleanUp and the
// elevated cleanup step). A certificate is never chosen by its subject, never one that was on the PC before the setup, never while unsure.
//   CleanupPlan         what a later cleanup may remove: the setup's own certificates by exact store and thumbprint, the package they signed
//                       (which must be gone), and whether a restart of Windows since the removal must be seen first
//   CleanupScope        which of a record's certificates wait for a cleanup, and on what condition
//   CleanupEligibility  whether a later cleanup may be offered now, with every reason it may not
using System;
using System.Collections.Generic;
using System.Linq;

namespace CcsBackup.Driver
{
    /// <summary>one certificate as read now: gone, or held for a reason, or removable</summary>
    public sealed class TrustItem
    {
        public CertificateEntry Certificate;
        public bool Gone;                     // not in that store now (nothing of it to remove)
        public RetainReason? Hold;            // why it stays; null with Gone false: it may be removed now
        public bool Removable => !Gone && Hold == null;
    }

    public sealed class TrustCheck
    {
        public bool PackageGone;              // the package is not on the PC (read by its name) and no device instance names it
        public RetainReason? PackageHold;     // null: the package's removal is finished
        public string Error;                  // the PC could not be read: everything is held
        public List<TrustItem> Items = new List<TrustItem>();
        public List<CertificateEntry> Removable => Items.Where(i => i.Removable).Select(i => i.Certificate).ToList();
        public List<CertificateEntry> Present => Items.Where(i => !i.Gone).Select(i => i.Certificate).ToList();
        public RetainReason? FirstHold => PackageHold ?? Items.FirstOrDefault(i => !i.Gone && i.Hold != null)?.Hold;
    }

    public static class TrustCleanup
    {
        /// <summary>reads only. signerSubject: the exact subject the setup's certificate has (Workbench's names the operation, CcsDriver.SubjectFor);
        /// a requirement on top of the exact thumbprint, never a way to find one. waiting: why the removal must be taken as unfinished whatever
        /// the PC shows (Windows said a restart is needed, or no restart has been seen since a removal whose end was not confirmed).</summary>
        public static TrustCheck Check(IDriverStore store, string signerSubject, string publishedName, IEnumerable<CertificateEntry> certificates, RetainReason? waiting)
        {
            var t = new TrustCheck(); var certs = (certificates ?? Enumerable.Empty<CertificateEntry>()).ToList();
            try
            {
                var p = store.Package(publishedName);
                var users = store.DevicesUsing(publishedName);
                bool deviceWaits = store.Devices(PinnedDriver.Vid, PinnedDriver.Pid).Any(d => d.NeedsRestart);
                t.PackageGone = p == null && users.Count == 0;
                if (p != null && p.PendingRemoval || deviceWaits) t.PackageHold = RetainReason.RestartPending;   // what the PC shows comes first
                else if (!t.PackageGone) t.PackageHold = RetainReason.PackageStillPresent;
                else if (waiting != null) t.PackageHold = waiting;
                var identity = store.Packages(PinnedDriver.Vid, PinnedDriver.Pid);
                foreach (var c in certs)
                {
                    var it = new TrustItem { Certificate = c }; t.Items.Add(it);
                    var thumb = UsbId.NormThumbprint(c.Thumbprint);
                    if (!UsbId.IsThumbprint(thumb)) { it.Hold = RetainReason.CertificateChanged; continue; }
                    var cur = store.Certificates(c.Store).FirstOrDefault(x => UsbId.NormThumbprint(x.Thumbprint) == thumb);
                    if (cur == null) { it.Gone = true; continue; }
                    if (cur.HasPrivateKey || string.IsNullOrEmpty(signerSubject) || !string.Equals(cur.Subject, signerSubject, StringComparison.Ordinal) || !cur.SameAs(c))
                    { it.Hold = RetainReason.CertificateChanged; continue; }   // not exactly the setup's
                    if (t.PackageHold != null) { it.Hold = t.PackageHold; continue; }
                    var scan = store.PackagesSignedBy(thumb);
                    if (!scan.Complete) it.Hold = RetainReason.CertificateSharingUnknown;
                    else if (scan.Packages.Count > 0 || identity.Any(x => UsbId.NormThumbprint(x.SignerThumbprint) == thumb)) it.Hold = RetainReason.CertificateShared;
                }
            }
            catch (Exception ex)
            {
                t.Error = ex.GetType().Name + ": " + ex.Message; t.PackageGone = false; t.PackageHold = RetainReason.StateUnreadable;
                t.Items = certs.Select(c => new TrustItem { Certificate = c, Hold = RetainReason.StateUnreadable }).ToList();
            }
            return t;
        }
    }

    /// <summary>A later cleanup: only the setup's own certificates (exact store and thumbprint), only once the package they signed is gone.
    /// No valid plan can name an inbox INF, a hardware ID, a wildcard or a subject.</summary>
    public sealed class CleanupPlan
    {
        public string OperationId, PublishedName;
        public List<CertificateEntry> Certificates = new List<CertificateEntry>();
        public bool RestartFirst;               // a restart of Windows after AfterUtc must be seen first (and not the start read then, BootThenUtc)
        public string AfterUtc, BootThenUtc;

        public List<string> Problems()
        {
            var p = new List<string>();
            if (!Hex.IsHex(OperationId, 32)) p.Add("the cleanup names no operation");
            if (!UsbId.IsPublishedName(PublishedName)) p.Add("the package the certificates signed is not one published package name (oemNN.inf): " + (PublishedName ?? "(none)"));
            if (Certificates.Count == 0) p.Add("the cleanup names no certificate");
            foreach (var c in Certificates) if (!UsbId.IsThumbprint(UsbId.NormThumbprint(c.Thumbprint))) p.Add("a certificate is not named by an exact thumbprint: " + (c.Thumbprint ?? "(none)"));
            if (RestartFirst && string.IsNullOrEmpty(AfterUtc)) p.Add("the cleanup needs a restart but names no moment after which it must be");
            return p;
        }
        public JObj ToJson() => new JObj { { "operation_id", OperationId }, { "published_name", PublishedName }, { "certificates", Certificates.Select(c => c.ToJson()).ToList() },
            { "restart_first", RestartFirst }, { "after_utc", AfterUtc }, { "boot_then_utc", BootThenUtc } };
        public static CleanupPlan From(Dictionary<string, object> d)
        {
            if (d == null) return null;
            var r = new CleanupPlan { OperationId = d.S("operation_id"), PublishedName = d.S("published_name"), RestartFirst = d.B("restart_first"), AfterUtc = d.S("after_utc"), BootThenUtc = d.S("boot_then_utc") };
            foreach (var o in d.A("certificates") ?? new List<object>()) { var c = CertificateEntry.From(o as Dictionary<string, object>); if (c != null) r.Certificates.Add(c); }
            return r;
        }
    }

    public sealed class CleanupScope
    {
        public string PublishedName; public List<CertificateEntry> Certificates; public bool RestartFirst; public string AfterUtc, BootThenUtc;

        /// <summary>the record's certificates waiting for a cleanup, or null: a removal kept them (CleanupState deferred, or a cleanup cut
        /// short), or the window ended during the removal itself (then everything the setup added, and only after a restart)</summary>
        public static CleanupScope Of(DriverRecord r)
        {
            if (r == null || r.Kind != "setup" || r.RemovalPlan == null || !UsbId.IsPublishedName(r.RemovalPlan.PublishedName)) return null;
            if (r.CleanupState == "deferred" || r.CleanupState == "open")
                return new CleanupScope { PublishedName = r.RemovalPlan.PublishedName, Certificates = r.DeferredCertificates.ToList(), RestartFirst = r.CleanupRestartFirst, AfterUtc = r.CleanupAfterUtc, BootThenUtc = r.CleanupBootUtc };
            if (r.CleanupState == null && r.RemovalState == "open" && r.IntroducedCertificates.Count > 0)
                return new CleanupScope { PublishedName = r.RemovalPlan.PublishedName, Certificates = r.IntroducedCertificates.ToList(), RestartFirst = true,
                    AfterUtc = r.RemovalBefore?.TakenUtc ?? r.Steps.LastOrDefault(s => s.Name == "removal")?.BegunUtc, BootThenUtc = r.RemovalBefore?.Restart?.BootUtc };
            return null;
        }
    }

    public sealed class CleanupDecision
    {
        public bool Applicable;                                            // the record has certificates waiting for a cleanup
        public List<RetainReason> Reasons = new List<RetainReason>();      // why nothing may be removed now
        public List<(CertificateEntry Certificate, RetainReason Why)> Kept = new List<(CertificateEntry, RetainReason)>();   // still on the PC and staying, each with its reason
        public List<CertificateEntry> Present = new List<CertificateEntry>();   // the record's certificates still on the PC (all of them when the PC cannot be read)
        public CleanupPlan Plan;                                           // set only when offered
        public TrustCheck Check;
        public bool Offer => Reasons.Count == 0 && Plan != null && Plan.Certificates.Count > 0;
        public RetainReason Primary => Reasons.Count > 0 ? Reasons[0] : Kept.Count > 0 ? Kept[0].Why : RetainReason.OwnershipUncertain;
    }

    public static class CleanupEligibility
    {
        /// <summary>reads only. usage: what still needs the console connection (null at the startup check, which offers nothing itself)</summary>
        public static CleanupDecision Evaluate(DriverRecord rec, IDriverStore store, DriverSnapshot now, DriverUsage usage)
        {
            var d = new CleanupDecision(); var scope = CleanupScope.Of(rec);
            if (scope == null) return d;
            d.Applicable = true;
            if (now == null || now.Error != null) { d.Reasons.Add(RetainReason.StateUnreadable); d.Present = scope.Certificates.ToList(); return d; }
            if (string.IsNullOrEmpty(rec.PcId) || !PinnedDriver.Eq(rec.PcId, now.PcId)) { d.Reasons.Add(RetainReason.OtherPc); return d; }
            // only certificates this setup demonstrably added: listed as introduced by it, absent from its own before-reading
            bool Added(CertificateEntry c) => rec.IntroducedCertificates.Any(x => x.Key == c.Key) && rec.Before != null && rec.Before.Error == null && !rec.Before.HasCertificate(c.Store, c.Thumbprint);
            bool OnPc(CertificateEntry c) { try { return store.Certificates(c.Store).Any(x => UsbId.NormThumbprint(x.Thumbprint) == UsbId.NormThumbprint(c.Thumbprint)); } catch (Exception) { return true; } }
            var certs = scope.Certificates.Where(Added).ToList();
            foreach (var c in scope.Certificates.Where(c => !Added(c) && OnPc(c))) { d.Present.Add(c); d.Kept.Add((c, RetainReason.CertificatePreExisting)); }   // never ours to remove
            var waiting = scope.RestartFirst && !RestartState.RestartedSince(now.Restart, scope.AfterUtc, scope.BootThenUtc) ? RetainReason.RestartNotSeen : (RetainReason?)null;
            var subject = rec.Plan != null && !rec.Plan.IsCcs ? rec.Plan.CertificateSubject : CcsDriver.SubjectFor(rec.OperationId);   // the pinned plan: the simulation only
            var t = d.Check = TrustCleanup.Check(store, subject, scope.PublishedName, certs, waiting);
            d.Present.AddRange(t.Present);
            if (t.Error != null) d.Reasons.Add(RetainReason.StateUnreadable);
            else if (t.PackageHold != null && t.Present.Count > 0) d.Reasons.Add(t.PackageHold.Value);
            foreach (var i in t.Items) if (!i.Gone && i.Hold != null && i.Hold != t.PackageHold) d.Kept.Add((i.Certificate, i.Hold.Value));
            if (usage != null)
            {
                if (usage.ConsoleWriteInProgress) d.Reasons.Add(RetainReason.ConsoleWriteInProgress);
                if (usage.UnresolvedConsoleResult) d.Reasons.Add(RetainReason.UnresolvedConsoleResult);
                if (usage.ConsoleOperationActive) d.Reasons.Add(RetainReason.ConsoleOperationActive);
                if (usage.Host == HostKind.Embedded && usage.ParentAppNeedsDevice) d.Reasons.Add(RetainReason.ParentAppNeedsIt);
                if (usage.OtherWorkbenchOperation) d.Reasons.Add(RetainReason.AnotherWorkbenchOperation);
                if (usage.OtherProgramsOpen.Count > 0) d.Reasons.Add(RetainReason.OtherProgramMayUseIt);
            }
            d.Reasons = d.Reasons.Distinct().ToList();
            var go = t.Removable;
            if (d.Reasons.Count == 0 && go.Count > 0)
            {
                d.Plan = new CleanupPlan { OperationId = rec.OperationId, PublishedName = scope.PublishedName, RestartFirst = scope.RestartFirst, AfterUtc = scope.AfterUtc, BootThenUtc = scope.BootThenUtc };
                foreach (var c in go) d.Plan.Certificates.Add(now.Certificates.FirstOrDefault(x => x.Key == c.Key) ?? c);
                if (d.Plan.Problems().Count > 0) { d.Reasons.Add(RetainReason.OwnershipUncertain); d.Plan = null; }
            }
            return d;
        }
    }
}
