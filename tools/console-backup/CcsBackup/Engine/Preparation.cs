// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// First-time console preparation, Classic Cart Studio's own (Classic Cart Studio for Windows only; the Utility has no preparation image and
// its worker refuses the request): Hakchi2 CE 3.9.3's installation, from its pinned files, as checked steps (ram/ccsprep), with the backup
// prerequisite first and no separately installed Hakchi PC program.
//   1 admit     a verified recovery baseline of this console (Admission "prepare", bound to this session)
//   2 check     the console identified in the preparation session: supported, the same console, its startup software classified
//   2b protect  a complete, verified backup of the console as it is now, captured in this same session (CaptureEngine): the exact
//               pre-preparation snapshot, with every save made since any earlier backup; no capture, no preparation. The plan is bound to
//               it and to this session (a reconnection is a new session and needs a new plan)
//   3 plan      already prepared with the pinned hakchi (its files and kernel are there, the original kernel in the spare slot): nothing to
//               do; the original Nintendo software: the four steps; anything else (another modification, an unknown kernel): refused.
//               With add-ons chosen (Engine/MethodEngine.cs AddonChoice: CCS Mod; CCS Nucleus direct or with its chooser; what opens
//               at power-on): each missing package (carried by the preparation image, pinned) is placed in hakchi's transfer folder by a
//               "method" step, and CCS Nucleus's start is set by "nstart", both after hakchi's files and before the kernel (so the kernel
//               stays last); hakchi itself installs the packages at the console's next normal start. On a console already prepared with
//               the pinned hakchi those are the only steps (no kernel, bootloader or system write); the console's hakchi packages and
//               transfer folder are read first (ccsprep mstate) and must be read the same at the confirmation. CCS Mod's CCS HOME package
//               (Engine/HomePackage: the one this build carries, or the caller's) follows in the same session, hput then huse (sent after
//               the image started, checked file by file on the console, then switched to), unless that exact package is already in use.
//               Preparation only adds; changing or removing add-ons is in Console Restore (MethodEngine)
//   4 confirm   a one-time confirmation for exactly this plan
//   5 steps     files, uboot, slot68, kernel, in hakchi's own order (the kernel last: until it is written the console starts its original
//               system); the first failure stops every later step; nothing is burned twice
//   6 verify    the console identified again: the hakchi kernel running state recorded, the original kernel in the spare slot
//   7 record    the preparation record keeps the pre-preparation snapshot and the exact state preparation left (bootloader, startup
//               software, spare slot, storage table): what Return to Original Software (ReturnEngine) requires before it changes anything
// A failure is never undone automatically; recovery is its own approved operation.
using CcsBackup.Device;
using CcsBackup.Ext4;
using CcsBackup.Library;
using CcsWorker.Shell;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CcsBackup.Engine
{
    public sealed class PrepPlan
    {
        public string Hwid, ConsoleTag, State, Text, Digest, Nonce, Session; public DateTime ExpiresUtc;
        /// <summary>the add-ons chosen (null: hakchi only), the packages this preparation places (in the order hakchi installs them), CCS
        /// Nucleus's start ("DEFAULT CHOOSER HOME") when it is set, and the console's hakchi packages and transfer folder as the review read them</summary>
        public AddonChoice Addons; public List<string> Add = new List<string>(); public string StartLine; public MethodState MethodBefore;
        /// <summary>CCS HOME's package for the method (Engine/HomePackage): the archive the plan sends (its size and SHA-256 bound in the
        /// digest) and the copy in use it replaces ("absent": none); null when no HOME step is needed</summary>
        public HomeArchive Home; public string HomePrior; public EarlierPlan Earlier;
        /// <summary>the console's baseline (admitted) and the backup made in this session just before the plan, as the review saw them</summary>
        public BoundArchive BaselineArchive, CurrentArchive;
        public string CurrentBackup => CurrentArchive?.Path; public string CurrentSha256 => CurrentArchive?.Sha256; public string BaselineBackup => BaselineArchive?.Path; public string BaselineSha256 => BaselineArchive?.Sha256;
        public List<string> Steps = new List<string>(); public List<string> Notes = new List<string>(); public string Refusal;
        public JObj ToJson() => new JObj { { "console_tag", ConsoleTag }, { "state", State }, { "text", Text }, { "steps", Steps }, { "notes", Notes }, { "refusal", Refusal }, { "digest", Digest }, { "nonce", Nonce }, { "current_backup", CurrentBackup }, { "current_sha256", CurrentSha256 }, { "baseline_backup", BaselineBackup },
            { "addons", Addons?.ToJson() }, { "add", Add.Select(a => (object)new JObj { { "name", a }, { "sha256", Methods.Sha256(a) } }).ToList() }, { "start", StartLine }, { "console_state", MethodBefore?.ToJson() },
            { "home", Home == null ? null : Home.ToJson().Set("replaces", HomePrior) }, { "earlier_home", Earlier?.ToJson() },
            { "evidence", new List<object> { BaselineArchive?.ToJson(), CurrentArchive?.ToJson() } } };
    }

    public sealed class PrepOutcome { public string State, Problem; public List<string> Done = new List<string>(); public PrepPlan Plan; public string RecordFile; public bool Ok => State == "prepared" || State == "already-prepared" || State == "method-scheduled" || State == "home-placed"; }

    public static class Preparation
    {
        public static readonly string[] Order = { "files", "uboot", "slot68", "kernel" };
        public const string PinnedHakchiVersion = "v1.0.4-126";
        /// <summary>the change record's list of the earlier CCS HOME versions removed (null: none placed), and how to put them back</summary>
        static List<object> EarlierRemoved(PrepPlan plan) => plan.Earlier == null ? null : plan.Earlier.Remove.Cast<object>().ToList();
        static string EarlierRestore(PrepPlan plan) => plan.Earlier == null || plan.Earlier.Remove.Count == 0 ? null : EarlierHome.RestoreText;

        public static string StepLabel(string s) => s == "files" ? "hakchi's files on the data storage" : s == "uboot" ? "hakchi's bootloader (u-boot)" : s == "slot68" ? "a copy of the original Nintendo startup software in the spare startup slot" : s.StartsWith("method:", StringComparison.Ordinal) || s == "nstart" ? MethodEngine.StepLabel(s)
            : HomeSteps.IsStep(s) ? HomePackage.StepLabel(s) : "hakchi's kernel (the startup software)";

        /// <summary>Phases 1 to 3 (no write but the protective backup on the PC). admission: this console's baseline, for this session. home: the
        /// CCS HOME package CCS Mod gets (null: the one this build carries). addons: the add-ons to add (null or none: hakchi only); adding
        /// is done here, changing or removing add-ons is in Console Restore.</summary>
        public static (PrepPlan plan, PrepOutcome refused) Prepare(RecoveryConsole con, EngineContext ctx, AdmissionResult admission, AddonChoice addons = null, HomePackage home = null)
        {
            var o = new PrepOutcome();
            if (con.Profile != SessionProfile.Prepare) { o.State = "refused"; o.Problem = "The console is not in a preparation session."; return (null, o); }
            if (addons != null && !addons.Mod && addons.Nucleus == "none") addons = null;
            if (addons != null && addons.Problem() != null) { o.State = "refused"; o.Problem = addons.Problem(); return (null, o); }
            foreach (var pk in new[] { Methods.Mod, Methods.Nucleus, Methods.Chooser })
                if (addons != null && (pk == Methods.Mod ? addons.Mod : pk == Methods.Nucleus ? addons.Nucleus != "none" : addons.Nucleus == "chooser") && !Hex.IsHex(Methods.Sha256(pk), 64))
                { o.State = "refused"; o.Problem = "This build does not carry " + Methods.Label(pk) + "'s package, so it cannot be installed."; return (null, o); }
            if (admission == null || !admission.Ok || admission.BaselinePath == null) { o.State = "refused"; o.Problem = "Back up the console first: preparation needs a verified backup of its original state."; return (null, o); }
            ConsoleIdentity id;
            try { id = con.Identify(); Support.CheckForBackup(id, ctx.RecoveryImageId, "preparation"); }
            catch (ConsoleRefusedException ex) { o.State = "refused"; o.Problem = ex.Message; return (null, o); }
            // 2b protect: the console as it is now, in this session (the same capture a backup makes; a new file, never over another)
            ctx.Say("protect", "Backing up the console as it is now, before anything changes...");
            var cap = CaptureEngine.Run(con, ctx);
            if (!cap.Ok || cap.Verification == null || !cap.Verification.Ok) { o.State = "stopped"; o.Problem = "The backup of the console as it is now did not complete (" + (cap.Problem ?? cap.State) + "), so nothing was prepared and nothing was written."; return (null, o); }
            var cur = cap.Verification;
            if (!ConsoleIdentity.SameConsole(cur.Hwid, id.Hwid) || cur.Simulated != con.Simulated || !cur.RestoreReady) { o.State = "refused"; o.Problem = "The backup made for this preparation is not a complete backup of this console."; return (null, o); }
            var bv = Archive.Verifier.VerifyFile(admission.BaselinePath, null, ctx.Cancelled);
            if (!bv.Ok || !ConsoleIdentity.SameConsole(bv.Hwid, id.Hwid) || bv.Simulated != con.Simulated || !bv.RecoveryBaseline) { o.State = "refused"; o.Problem = "The console's baseline backup no longer passes its check, so nothing was prepared and nothing was written."; return (null, o); }
            var p = new PrepPlan { Hwid = id.Hwid, ConsoleTag = id.ShortTag, Session = con.SessionId,
                CurrentArchive = BoundArchive.From("backup made just now, before preparing", cap.PrimaryPath, cur, con.SessionId), BaselineArchive = BoundArchive.From("baseline backup", admission.BaselinePath, bv) };
            var cls = Support.Classify(id);
            var inv = HakchiFiles(cap.PrimaryPath);
            if (cls.ActiveIsStock && !id.Slot68Valid && inv == null)
            { p.State = "original"; p.Text = "Original Nintendo software: Classic Cart Studio prepares it with Hakchi2 CE 3.9.3's kernel and files."; p.Steps.AddRange(Order); }
            else if (!cls.ActiveIsStock && cls.OriginalSource == "slot68" && inv == PinnedHakchiVersion && id.ActiveHakchiEntries > 0)
            { p.State = "already"; p.Text = "Already prepared with Hakchi2 CE 3.9.3 (hakchi " + inv + "): nothing needs to be installed again."; }
            else
            {
                p.State = "unsupported"; p.Refusal = cls.ActiveIsStock && (id.Slot68Valid || inv != null)
                    ? "This console has leftovers of an earlier modification (" + (id.Slot68Valid ? "a kernel in the spare slot" : "hakchi files") + ") with its original kernel running. Preparing over that is not something this version does."
                    : "This console's startup software was modified by other software (" + (inv ?? "an unknown version") + "). Preparing over it is not something this version does; Workbench can still back it up and restore it.";
            }
            if (p.State == "original") p.Notes.Add("This changes the console's startup software and bootloader and adds hakchi's files to its data storage, exactly as Hakchi2 CE 3.9.3 does (including its adjustment of the data storage's table). Nintendo's HOME, its 30 games and their saves stay.");
            if (addons != null && p.Refusal == null)
            {
                // the add-ons: the console's hakchi packages and transfer folder as they are now (read only), then each missing package placed
                // (before the kernel, after hakchi's files, so the kernel stays last), CCS Nucleus's start, and CCS HOME's package for CCS Mod
                p.Addons = addons;
                var ms = con.PrepState();
                if (!ms.Ok) { o.State = "refused"; o.Problem = "The console's hakchi packages could not be read (" + ms.Problem + "), so nothing was prepared and nothing was written."; return (null, o); }
                p.MethodBefore = ms;
                var have = AddonChoice.FromState(ms);
                if (have.Mod && !addons.Mod || have.Nucleus == "chooser" && addons.Nucleus != "chooser" || have.Nucleus == "direct" && addons.Nucleus == "none")
                    p.Refusal = "This console already has add-ons this choice would remove. Changing or removing add-ons is in Console Restore.";
                else
                {
                    if (addons.Mod && !have.Mod) p.Add.Add(Methods.Mod);
                    if (addons.Nucleus != "none" && have.Nucleus == "none") p.Add.Add(Methods.Nucleus);
                    if (addons.Nucleus == "chooser" && have.Nucleus != "chooser") p.Add.Add(Methods.Chooser);
                    var mine = p.Add.ToDictionary(Methods.Waiting, Methods.Sha256, StringComparer.Ordinal);
                    var wp = MethodEngine.WaitingProblem(ms, mine);
                    if (wp != null) p.Refusal = "Adding the add-ons is refused: " + wp + ". Start the console normally once so hakchi applies it, then prepare again.";
                    else
                    {
                        var steps = p.Add.Select(a2 => "method:" + a2).ToList();
                        if (addons.Nucleus != "none" && ms.Start != addons.StartLine) { p.StartLine = addons.StartLine; steps.Add("nstart"); }
                        if (p.State == "original") p.Steps.InsertRange(p.Steps.IndexOf("kernel"), steps); else p.Steps.AddRange(steps);
                        if (p.State == "already" && steps.Count > 0)
                            p.Text = "Already prepared with Hakchi2 CE 3.9.3 (hakchi " + inv + "): " + (p.Add.Count > 0 ? string.Join(" and ", p.Add.Select(Methods.Label)) + " added" : "the start changes") + ", without changing the kernel, bootloader or system.";
                        if (p.Add.Count > 0) p.Notes.Add(string.Join(" and ", p.Add.Select(Methods.Label)) + (p.Add.Count == 1 ? "'s package is" : "'s packages are") + " placed in hakchi's transfer folder on the data storage; hakchi installs " + (p.Add.Count == 1 ? "it" : "them") + " at the console's next normal start and then restarts the console once. That writes files on the data storage only.");
                        p.Notes.Add(addons.StartText);
                    }
                }
                // CCS HOME's package, which CCS Mod provides: sent in this same session (after the add-ons' packages, the kernel still last)
                if (p.Refusal == null && addons.Mod)
                {
                    HomeSteps.Decision hd;
                    try { hd = HomeSteps.Plan(home ?? HomePackage.Embedded(HomePackage.ModLayout), ms, id.Hwid); }
                    catch (HomePackageException ex) { hd = new HomeSteps.Decision { Refusal = "CCS HOME's package cannot be used: " + ex.Message }; }
                    if (hd.Refusal != null) p.Refusal = hd.Refusal;
                    else if (hd.Steps.Count > 0)
                    {
                        p.Home = hd.Archive; p.HomePrior = hd.Prior;
                        // the earlier versions the setting keeps stay; the others go right after the switch, each checked again on the console
                        var hsteps = hd.Steps.ToList();
                        p.Earlier = EarlierHome.Decide(EarlierHome.FromState(ms), hd.Archive.Copy, hd.Prior == "absent" ? null : hd.Prior, ctx.Settings?.EarlierHomeKeep ?? StorageSpec.EarlierKeep(null));
                        if (p.Earlier != null) { hsteps.AddRange(p.Earlier.Remove.Select(c => "hdrop:" + c)); p.Notes.Add(MethodEngine.EarlierNote(p.Earlier)); }
                        if (p.State == "original") p.Steps.InsertRange(p.Steps.IndexOf("kernel"), hsteps); else p.Steps.AddRange(hsteps);
                        if (p.State == "already" && p.Add.Count == 0 && p.StartLine == null) p.Text = "Already prepared with Hakchi2 CE 3.9.3 (hakchi " + inv + ") with CCS Mod: only CCS HOME's package is placed, without changing the kernel, bootloader or system.";
                        p.Notes.Add(hd.Text + " It writes files on the data storage only, in Classic Cart Studio's own folder.");
                    }
                    else if (hd.Text != null) p.Notes.Add(hd.Text);
                }
                if (p.Refusal != null) p.Steps.Clear();
            }
            p.Nonce = Hex.Of(Guid.NewGuid().ToByteArray()); p.ExpiresUtc = DateTime.UtcNow + RestoreEngine.ConfirmWindow;
            p.Digest = DigestOf(p);
            return (p, null);
        }

        static string DigestOf(PrepPlan p)
        {
            using (var h = SHA256.Create()) return Hex.Of(h.ComputeHash(Encoding.UTF8.GetBytes("ccs-prepare-plan/7\n" + ConsoleIdentity.NormalizeHwid(p.Hwid) + "\n" + p.State + "\n" + string.Join(",", p.Steps) + "\n" + p.CurrentArchive?.DigestLine() + "\n" + p.Session + "\n" + p.BaselineArchive?.DigestLine() + "\n" +
                    (p.Addons == null ? "-" : Json.Write(p.Addons.ToJson())) + string.Join(",", p.Add.Select(a => a + " " + Methods.Sha256(a))) + " " + (p.MethodBefore?.Digest() ?? "-") + "\n" + (p.Home?.DigestLine() ?? "-") + " " + (p.HomePrior ?? "-") + "\nstart=" + (p.StartLine ?? "-") + "\n" + p.Nonce)));
        }

        /// <summary>hakchi's version from a backup's captured data storage (null when hakchi's files are not there)</summary>
        public static string HakchiFiles(string backup)
        {
            try
            {
                using (var z = Archive.ZipReader.Open(backup, Archive.BackupFormat.Allowed))
                {
                    var m = Library.History.Peek(backup); var dev = (m?.O("layout")?.A("partitions") ?? new List<object>()).OfType<Dictionary<string, object>>().FirstOrDefault(x => x.S("label") == "data" || x.S("label") == "rootfs_data")?.S("device");
                    var e = dev == null ? null : z.Find("restore/" + dev + ".img"); if (e == null || e.Method != 0) return null;
                    var fs = new Ext4Reader(z.StoredWindow(e));
                    var d = fs.Lookup("/hakchi/rootfs/etc/preinit.d/b0000_defines"); if (d == null) return fs.Lookup("/hakchi") != null ? "unknown" : null;
                    var t = Encoding.UTF8.GetString(fs.ReadAll(d.Value, 100000));
                    var mm = Regex.Match(t, "hakchiVersion='([^']{1,40})'"); return mm.Success ? mm.Groups[1].Value : "unknown";
                }
            }
            catch (Exception) { return null; }
        }

        /// <summary>use: the console worker's admission check (Admission.Use with this session's capture); it runs under the evidence hold,
        /// after every plan check, so a refused confirmation never uses up the admission. Every check that can refuse runs before the
        /// confirmation is used up and before the first step.</summary>
        public static PrepOutcome Execute(RecoveryConsole con, PrepPlan plan, string digest, string nonce, EngineContext ctx, Func<AdmissionResult> use = null)
        {
            var o = new PrepOutcome { Plan = plan };
            PrepOutcome Refuse(string why) { o.State = "refused"; o.Problem = why; return o; }
            if (plan == null || plan.Refusal != null) return Refuse(plan?.Refusal ?? "No plan.");
            if (digest != plan.Digest || nonce != plan.Nonce || plan.Nonce == null || DigestOf(plan) != plan.Digest) return Refuse("The confirmation does not match the reviewed preparation.");
            if (con.SessionId != plan.Session || plan.CurrentArchive?.Session != plan.Session) return Refuse("The console was connected again after the review, so its backup from that review no longer proves its state. Review the preparation again. Nothing was changed.");
            if (DateTime.UtcNow > plan.ExpiresUtc) return Refuse("The confirmation is more than 10 minutes old. Review the preparation again.");
            if (plan.State == "already" && plan.Steps.Count == 0) { plan.Nonce = null; o.State = "already-prepared"; return o; }
            var id = con.Identify();
            if (!ConsoleIdentity.SameConsole(id.Hwid, plan.Hwid)) return Refuse("A different console is connected now. Nothing was changed.");
            using (var hold = Evidence.Hold.Open(plan.BaselineBackup, plan.CurrentBackup))
            {
                if (hold.Problem != null) return Refuse("A backup the preparation relies on cannot be used (" + hold.Problem + "). Nothing was changed; review the preparation again.");
                ctx.Say("validate", "Checking the backups again...");
                var why = Evidence.Recheck(plan.BaselineArchive, plan.Hwid, con.Simulated, null, ctx.Cancelled) ?? Evidence.Recheck(plan.CurrentArchive, plan.Hwid, con.Simulated, con.SessionId, ctx.Cancelled);
                if (why != null) return Refuse(why + " Nothing was changed; review the preparation again.");
                if (plan.Addons != null && (plan.Add.Count > 0 || plan.Home != null || plan.StartLine != null))
                {
                    var ms = con.PrepState();
                    if (!ms.Ok || ms.Digest() != plan.MethodBefore?.Digest()) return Refuse("The console's hakchi packages, CCS HOME's copies or its transfer folder changed since the review" + (ms.Ok ? "" : " (" + ms.Problem + ")") + ". Nothing was changed; review the preparation again.");
                }
                if (use == null) return Refuse("The preparation's backup admission was not checked, so nothing was changed.");
                var adm = use();
                if (adm == null || !adm.Ok) return Refuse(adm?.Text ?? "The backup admission was refused. Nothing was changed.");
                Record rec;
                try
                {
                    rec = Record.Start(ctx.Settings.RecordsFolder, Guid.NewGuid().ToString("N").Substring(0, 12), "prepare", new JObj
                    {
                        { "plan", plan.ToJson() }, { "console_tag", plan.ConsoleTag }, { "simulated", con.Simulated },
                        { "before", new JObj { { "snapshot", plan.CurrentBackup }, { "snapshot_sha256", plan.CurrentSha256 }, { "baseline", plan.BaselineBackup }, { "baseline_sha256", plan.BaselineSha256 }, { "parttable", id.Get("parttable") }, { "active_md5", id.ActiveMd5 }, { "slot68_md5", id.Slot68Valid ? id.Slot68Md5 : null } } },
                    });
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return Refuse("The preparation's record could not be saved in " + ctx.Settings.RecordsFolder + " (" + ex.Message + "), so nothing was written."); }
                plan.Nonce = null;   // used up only now
                return ExecuteHeld(con, plan, ctx, o, rec);
            }
        }

        static PrepOutcome ExecuteHeld(RecoveryConsole con, PrepPlan plan, EngineContext ctx, PrepOutcome o, Record rec)
        {
            o.RecordFile = rec.File; var phases = new List<object>();
            var auth = new WriteAuthority(plan.Digest, plan.Hwid, plan.Steps.Select(x => x.StartsWith("method:", StringComparison.Ordinal) ? "method" : HomeSteps.Target(x)).Distinct().ToArray(), () => con.Simulated || CcsBackup.Gate.PreparationWrites, CcsBackup.Gate.PreparationText); string ubootMd5 = null;
            rec.State("writing"); con.Card("writing");
            foreach (var s in plan.Steps)
            {
                if (ctx.Cancelled()) { rec.State("stopped", "canceled between steps"); o.State = "stopped"; o.Problem = "Canceled. Steps done: " + (o.Done.Count == 0 ? "none" : string.Join(", ", o.Done)) + ". The console's state is not verified."; return o; }
                ctx.Say("prepare", "Preparing: " + StepLabel(s) + "... (do not unplug the console)");
                phases.Add(new JObj { { "step", s }, { "state", "began" }, { "at", Record.Now() } }); rec.Set("phases", phases).Save();
                WriteResult r;
                try { r = s.StartsWith("method:", StringComparison.Ordinal) ? con.PrepStep("method", auth, new[] { s.Substring(7), Methods.Sha256(s.Substring(7)) }) : s == "nstart" ? con.PrepStep("nstart", auth, plan.StartLine.Split(' ')) : HomeSteps.IsStep(s) ? HomeSteps.Run(con, auth, s, plan.Home, plan.HomePrior) : con.PrepStep(s, auth); }
                catch (GateClosedException ex) { rec.State("refused", ex.Message); o.State = "refused"; o.Problem = ex.Message; return o; }
                phases.Add(new JObj { { "step", s }, { "state", r.Ok ? (r.Line != null && r.Line.Contains(" ok already ") ? "already-there" : "done-and-read-back") : "failed" }, { "detail", r.Line ?? r.Problem }, { "at", Record.Now() } }); rec.Set("phases", phases).Save();
                ctx.At("prepare-" + s);
                if (!r.Ok)
                {
                    rec.State("stopped", r.Problem); con.Card("stopped"); o.State = "stopped";
                    o.Problem = "Preparing stopped at " + StepLabel(s) + " (" + r.Problem + "). Nothing more was changed and nothing was retried. Steps done before: " + (o.Done.Count == 0 ? "none" : string.Join(", ", o.Done.Select(StepLabel))) + ". Your backups are unchanged."
                        + (s == "hput" || s == "huse" ? " The HOME the console used before stays in use; preparing again with the same package finishes this installation without sending what is already there again." : "");
                    return o;
                }
                if (s.StartsWith("method:", StringComparison.Ordinal) && !MethodEngine.Answer(r, "method", Methods.Sha256(s.Substring(7))) || s == "nstart" && !MethodEngine.StartAnswer(r, plan.StartLine) || HomeSteps.IsStep(s) && !HomeSteps.Answer(r, s, plan.Home))
                {
                    rec.State("stopped", "the step reads back as " + r.Line); con.Card("stopped"); o.State = "stopped";
                    o.Problem = "Preparing stopped at " + StepLabel(s) + ": the console reports '" + r.Line + "', not the reviewed package. Nothing more was changed and nothing was retried.";
                    return o;
                }
                o.Done.Add(s); if (s == "uboot") { var mm = Regex.Match(r.Line ?? "", "^result uboot ok ([0-9a-f]{32})"); ubootMd5 = mm.Success ? mm.Groups[1].Value : null; }
            }
            JObj method = null, home = null;
            if (plan.Add.Count > 0 || plan.Home != null || plan.StartLine != null)
            {
                var ms = con.PrepState();
                if (plan.Add.Count > 0 && (!ms.Ok || !plan.Add.All(a => ms.Waiting.TryGetValue(Methods.Waiting(a), out var ws) && ws == Methods.Sha256(a)))) { rec.State("stopped", "the package does not read back in the transfer folder"); o.State = "stopped"; o.Problem = "Every step reported success, but " + string.Join(" and ", plan.Add.Select(Methods.Label)) + " does not read back in hakchi's transfer folder. Its state is not verified; nothing more was changed."; return o; }
                if (plan.Earlier != null && plan.Earlier.Remove.Any(c => ms.HomeCopies.ContainsKey(c))) { rec.State("stopped", "an earlier CCS HOME copy still reads back"); o.State = "stopped"; o.Problem = "Every step reported success, but an earlier CCS HOME version that was removed still reads back on the console. Nothing more was changed."; return o; }
                if (plan.Home != null && !HomeSteps.InUse(ms, plan.Home)) { rec.State("stopped", "CCS HOME's package does not read back in use"); o.State = "stopped"; o.Problem = "Every step reported success, but the console does not read back CCS HOME's new package as the one in use. Its state is not verified; nothing more was changed."; return o; }
                if (plan.StartLine != null && ms.Start != plan.StartLine) { rec.State("stopped", "CCS Nucleus's start does not read back"); o.State = "stopped"; o.Problem = "Every step reported success, but CCS Nucleus's start setting does not read back as chosen. Its state is not verified; nothing more was changed."; return o; }
                if (plan.Add.Count > 0 || plan.StartLine != null) method = new JObj { { "addons", plan.Addons.ToJson() }, { "add", plan.Add.Select(a => (object)new JObj { { "name", a }, { "sha256", Methods.Sha256(a) } }).ToList() }, { "start", plan.StartLine }, { "state", plan.Add.Count > 0 ? "waiting for the next start (hakchi's p7080_hmods installs them)" : "set" } };
                if (plan.Home != null) home = plan.Home.ToJson().Set("replaced", plan.HomePrior).Set("state", "in use");
                if (plan.State == "already")
                {   // only the method and/or the HOME were added: this console's preparation record (the way back) stays the earlier one
                    var st = method != null ? "method-scheduled" : "home-placed";
                    rec.Set("after", new JObj { { "method", method }, { "home", home }, { "earlier_home_removed", EarlierRemoved(plan) }, { "earlier_home_restore", EarlierRestore(plan) } }); rec.State(st); con.Card("done"); o.State = st; return o;
                }
            }
            var after = con.Identify();
            bool ok = ConsoleIdentity.SameConsole(after.Hwid, plan.Hwid) && after.Slot68Valid && KnownKernels.IsStockFor(after.Model, after.Region, after.Slot68Md5) && after.ActiveHakchiEntries > 0;
            if (!ok) { rec.State("stopped", "the console does not read back as prepared"); o.State = "stopped"; o.Problem = "Every step reported success, but the console does not read back as prepared. Its state is not verified; nothing more was changed."; return o; }
            // the exact state this preparation left: what the way back (ReturnEngine) must find before it changes anything
            rec.Set("after", new JObj { { "parttable", after.Get("parttable") }, { "active_md5", after.ActiveMd5 }, { "slot68_md5", after.Slot68Md5 }, { "boot1_md5", ubootMd5 ?? con.Digest("boot1").md5 }, { "active_hakchi_entries", (long)after.ActiveHakchiEntries }, { "method", method }, { "home", home }, { "earlier_home_removed", EarlierRemoved(plan) }, { "earlier_home_restore", EarlierRestore(plan) } });
            rec.State("prepared"); con.Card("done"); o.State = "prepared"; return o;
        }
    }
}
