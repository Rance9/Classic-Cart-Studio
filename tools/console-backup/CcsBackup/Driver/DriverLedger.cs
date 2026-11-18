// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The USB driver records: one JSON file per driver operation in "USB Driver Records" inside the workspace the person chose. Each step is
// written (Durable.WriteAtomic) BEFORE the action it names starts and again when it ends, so a record whose step has no end, or whose state
// is still "open", is an operation that was cut short: nothing it touched is ever offered for removal (Eligibility), and the next start
// explains what remains (DriverSetup.Reconcile, reads only). A record carries: the operation id, the program and host, this PC's id, the
// elevation state, the plan, the PC state before and after, what the action reported, what was actually introduced (packages, certificates,
// device instances bound), the close choice, any removal with its own before/after, and the setup's certificates a removal kept until
// removing them is shown safe (deferred cleanup) with any later cleanup's own before/after. Records are never deleted by this code.
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CcsBackup.Driver
{
    public enum HostKind { Standalone, Embedded }
    public enum SetupResult { NotNeeded, Blocked, Unavailable, Installed, InstalledRestartPending, InstalledNotOwned, ElevationRefused, Canceled, Failed, ReportedButMissing, Partial, Unconfirmed, Reused }
    public enum RemovalResult { NotEligible, Removed, RemovedRestartPending, ElevationRefused, Failed, Partial, Unconfirmed, Unavailable }
    public enum CleanupResult { NotEligible, Removed, Partial, ElevationRefused, Failed, Unconfirmed, Unavailable }

    public sealed class DriverStep
    {
        public int Seq; public string Name, BegunUtc, EndedUtc, Result;
        public JObj ToJson() => new JObj { { "seq", Seq }, { "name", Name }, { "begun_utc", BegunUtc }, { "ended_utc", EndedUtc }, { "result", Result } };
        public static DriverStep From(Dictionary<string, object> d) => new DriverStep { Seq = (int)d.L("seq", 0), Name = d.S("name"), BegunUtc = d.S("begun_utc"), EndedUtc = d.S("ended_utc"), Result = d.S("result") };
    }

    public sealed class DriverRecord
    {
        public const string Schema = "ccs-usb-driver-record/1";
        public string File;                                   // where it is (not stored)
        public string OperationId, Kind, Host, Tool, ToolVersion, PcId, StartedUtc, Elevation;
        public DriverSetupPlan Plan;
        public DriverSnapshot Before, After;
        public List<DriverStep> Steps = new List<DriverStep>();
        public string State = "open";                         // "open" while anything is under way, "finished" after the read-back
        public SetupResult? Result;
        public ActionReport Reported;
        public List<DriverPackage> IntroducedPackages = new List<DriverPackage>();     // new published packages found after (copies of their after-state)
        public List<CertificateEntry> IntroducedCertificates = new List<CertificateEntry>();   // new certificates that signed the owned package
        public List<CertificateEntry> OtherNewCertificates = new List<CertificateEntry>();     // new relevant certificates it cannot claim
        public List<CertificateEntry> RemovedCertificates = new List<CertificateEntry>();      // relevant certificates that disappeared during the setup
        public List<string> BoundByOperation = new List<string>();  // device instances bound to the owned package right after the setup
        public string OwnedPackage;                           // published name, only when exactly one new package with the planned identity appeared
        public string Ownership;                              // why ownership is or is not shown
        public List<string> Changes = new List<string>();     // the changes found, in plain words
        public string CloseChoice;                            // null, "kept" or "remove"
        public RemovalPlan RemovalPlan;
        public string RemovalState;                           // null, "open" or "finished"
        public RemovalResult? Removal;
        public ActionReport RemovalReported;
        public DriverSnapshot RemovalBefore, RemovalAfter;
        // the setup's own certificates kept after its package's removal until removing them is shown safe (TrustCleanup.cs)
        public string CleanupState;                           // null, "deferred" (kept, waiting), "open" (a cleanup under way), "finished" (none left), "kept" (the person chose to keep them)
        public List<CertificateEntry> DeferredCertificates = new List<CertificateEntry>();   // exact store and thumbprint, each one the setup added
        public bool CleanupRestartFirst;                      // the removal's end was not confirmed: a restart of Windows since the removal must be seen first
        public string CleanupAfterUtc, CleanupBootUtc;        // the removal's start, and Windows' start time read then
        public CleanupPlan CleanupPlan;
        public string Cleanup;                                // the last cleanup's result (CleanupResult)
        public ActionReport CleanupReported;
        public DriverSnapshot CleanupBefore, CleanupAfter;

        public bool Incomplete => State != "finished" || RemovalState == "open" || CleanupState == "open" || Steps.Any(s => s.EndedUtc == null);
        public bool SetupIncomplete => State != "finished" || Steps.Any(s => s.EndedUtc == null && !s.Name.StartsWith("removal"));
        public bool Installed => Result == SetupResult.Installed || Result == SetupResult.InstalledRestartPending;

        public JObj ToJson() => new JObj
        {
            { "schema", Schema }, { "operation_id", OperationId }, { "kind", Kind }, { "host", Host }, { "tool", Tool }, { "tool_version", ToolVersion ?? "" }, { "pc", PcId ?? "" },
            { "started_utc", StartedUtc }, { "elevation", Elevation }, { "state", State }, { "result", Result?.ToString() }, { "plan", Plan?.ToJson() },
            { "steps", Steps.Select(s => s.ToJson()).ToList() }, { "reported", Reported?.ToJson() },
            { "owned_package", OwnedPackage }, { "ownership", Ownership ?? "" }, { "bound_by_operation", BoundByOperation },
            { "introduced_packages", IntroducedPackages.Select(p => p.ToJson()).ToList() }, { "introduced_certificates", IntroducedCertificates.Select(c => c.ToJson()).ToList() },
            { "other_new_certificates", OtherNewCertificates.Select(c => c.ToJson()).ToList() }, { "removed_certificates", RemovedCertificates.Select(c => c.ToJson()).ToList() },
            { "changes", Changes }, { "close_choice", CloseChoice }, { "removal_state", RemovalState }, { "removal", Removal?.ToString() }, { "removal_plan", RemovalPlan?.ToJson() },
            { "removal_reported", RemovalReported?.ToJson() }, { "before", Before?.ToJson() }, { "after", After?.ToJson() },
            { "removal_before", RemovalBefore?.ToJson() }, { "removal_after", RemovalAfter?.ToJson() },
            { "cleanup_state", CleanupState }, { "deferred_certificates", DeferredCertificates.Select(c => c.ToJson()).ToList() }, { "cleanup_restart_first", CleanupRestartFirst },
            { "cleanup_after_utc", CleanupAfterUtc }, { "cleanup_boot_utc", CleanupBootUtc }, { "cleanup_plan", CleanupPlan?.ToJson() }, { "cleanup", Cleanup },
            { "cleanup_reported", CleanupReported?.ToJson() }, { "cleanup_before", CleanupBefore?.ToJson() }, { "cleanup_after", CleanupAfter?.ToJson() },
        };

        public static DriverRecord From(Dictionary<string, object> d, string file)
        {
            if (d.S("schema") != Schema) throw new FormatException("not a USB driver record (" + (d.S("schema") ?? "no schema") + ")");
            var r = new DriverRecord
            {
                File = file, OperationId = d.S("operation_id"), Kind = d.S("kind"), Host = d.S("host"), Tool = d.S("tool"), ToolVersion = d.S("tool_version"), PcId = d.S("pc"),
                StartedUtc = d.S("started_utc"), Elevation = d.S("elevation"), State = d.S("state") ?? "open", Plan = DriverSetupPlan.From(d.O("plan")), Reported = ActionReport.From(d.O("reported")),
                OwnedPackage = d.S("owned_package"), Ownership = d.S("ownership"), BoundByOperation = DeviceBinding.Strs(d.A("bound_by_operation")), Changes = DeviceBinding.Strs(d.A("changes")),
                CloseChoice = d.S("close_choice"), RemovalState = d.S("removal_state"), RemovalPlan = RemovalPlan.From(d.O("removal_plan")), RemovalReported = ActionReport.From(d.O("removal_reported")),
                Before = DriverSnapshot.From(d.O("before")), After = DriverSnapshot.From(d.O("after")), RemovalBefore = DriverSnapshot.From(d.O("removal_before")), RemovalAfter = DriverSnapshot.From(d.O("removal_after")),
                CleanupState = d.S("cleanup_state"), CleanupRestartFirst = d.B("cleanup_restart_first"), CleanupAfterUtc = d.S("cleanup_after_utc"), CleanupBootUtc = d.S("cleanup_boot_utc"),
                CleanupPlan = CleanupPlan.From(d.O("cleanup_plan")), Cleanup = d.S("cleanup"), CleanupReported = ActionReport.From(d.O("cleanup_reported")),
                CleanupBefore = DriverSnapshot.From(d.O("cleanup_before")), CleanupAfter = DriverSnapshot.From(d.O("cleanup_after")),
            };
            if (r.OperationId == null || r.Kind == null) throw new FormatException("the record has no operation id or kind");
            if (Enum.TryParse(d.S("result") ?? "", out SetupResult sr)) r.Result = sr;
            if (Enum.TryParse(d.S("removal") ?? "", out RemovalResult rr)) r.Removal = rr;
            foreach (var o in (d.A("steps") ?? new List<object>()).OfType<Dictionary<string, object>>()) r.Steps.Add(DriverStep.From(o));
            foreach (var o in (d.A("introduced_packages") ?? new List<object>()).OfType<Dictionary<string, object>>()) r.IntroducedPackages.Add(DriverPackage.From(o));
            foreach (var o in (d.A("introduced_certificates") ?? new List<object>()).OfType<Dictionary<string, object>>()) r.IntroducedCertificates.Add(CertificateEntry.From(o));
            foreach (var o in (d.A("other_new_certificates") ?? new List<object>()).OfType<Dictionary<string, object>>()) r.OtherNewCertificates.Add(CertificateEntry.From(o));
            foreach (var o in (d.A("removed_certificates") ?? new List<object>()).OfType<Dictionary<string, object>>()) r.RemovedCertificates.Add(CertificateEntry.From(o));
            foreach (var o in (d.A("deferred_certificates") ?? new List<object>()).OfType<Dictionary<string, object>>()) r.DeferredCertificates.Add(CertificateEntry.From(o));
            return r;
        }
    }

    public sealed class LedgerEntry { public string File; public DriverRecord Record; public string Error; }

    public sealed class DriverLedger
    {
        public const string FolderName = "USB Driver Records";
        public readonly string Folder;

        /// <summary>workspace: the folder the person chose for Workbench (backups and records); the records go in its "USB Driver Records"</summary>
        public DriverLedger(string workspace)
        {
            if (string.IsNullOrWhiteSpace(workspace)) throw new ArgumentException("a workspace folder is needed for the USB driver records");
            Folder = Path.Combine(Path.GetFullPath(workspace), FolderName);
        }

        public DriverRecord Begin(string kind, HostKind host, string tool, string version, ElevationState elevation, DriverSetupPlan plan, DriverSnapshot before)
        {
            Directory.CreateDirectory(Folder);
            var id = Guid.NewGuid().ToString("N");
            var r = new DriverRecord
            {
                OperationId = id, Kind = kind, Host = host == HostKind.Embedded ? "embedded" : "standalone", Tool = tool, ToolVersion = version, PcId = before?.PcId,
                StartedUtc = Record.Now(), Elevation = elevation.ToString(), Plan = plan, Before = before,
                File = Path.Combine(Folder, "usb-driver-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + id.Substring(0, 8) + ".json"),
            };
            if (System.IO.File.Exists(r.File)) throw new IOException("a USB driver record with that name already exists; nothing was replaced");
            Save(r); return r;
        }

        public void Save(DriverRecord r) => Durable.WriteAtomic(r.File, Json.Write(r.ToJson()));

        /// <summary>written to disk before the step's action starts</summary>
        public DriverStep BeginStep(DriverRecord r, string name)
        {
            var s = new DriverStep { Seq = r.Steps.Count + 1, Name = name, BegunUtc = Record.Now() };
            r.Steps.Add(s); Save(r); return s;
        }

        public void EndStep(DriverRecord r, DriverStep s, string result) { s.EndedUtc = Record.Now(); s.Result = result; Save(r); }

        public static DriverRecord Load(string file) => DriverRecord.From(Json.Parse(System.IO.File.ReadAllText(file)), file);

        /// <summary>every record, newest first; one that cannot be read is listed with its error (and never acted on)</summary>
        public List<LedgerEntry> ReadAll()
        {
            var list = new List<LedgerEntry>();
            if (!Directory.Exists(Folder)) return list;
            foreach (var f in Directory.GetFiles(Folder, "usb-driver-*.json").OrderByDescending(x => Path.GetFileName(x), StringComparer.Ordinal))
            {
                try { list.Add(new LedgerEntry { File = f, Record = Load(f) }); }
                catch (Exception ex) { list.Add(new LedgerEntry { File = f, Error = ex.GetType().Name + ": " + ex.Message }); }
            }
            return list;
        }
    }
}
