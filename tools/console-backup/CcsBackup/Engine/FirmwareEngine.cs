// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Console Composer's Browse for Firmware: an owner's own system image (a SquashFS root, .hsqs, chosen on the PC) added as a CCS Nucleus
// choice, or such a choice removed (the registry and Nucleus's image folder). Under the strict policy, in
// the preparation image's session (ccsprep rput, rimport, rdrop), behind Gate.FirmwareWrites (its own boundary):
//   1 check     the file on the PC really is a console system's root and which one (SystemImage.Identify: hakchi's own rules, so an
//               SNES Classic system is told from an NES Classic or Famicom one and a kernel file is refused by name; PrivateRoles.Check
//               "root-squashfs"); the console identified; hakchi 3.9.3 and
//               CCS Nucleus installed (its registry readable), what the registry lists and the data storage's free space (ccsprep mstate,
//               read-only); the image and 8,000,000 bytes more must fit
//   2 protect   a complete, verified backup of the console as it is now, in this session
//   3 plan      add: rput (the image to Nucleus's staging folder, checked and read back), rimport (this image's own pinned CCS Nucleus
//               program imports it: one rename into roots/ID.hsqs, recorded as Nucleus's data, a registry option, read back);
//               remove: rdrop (only Nucleus's own unchanged image file; the option, then the file). The file's size and SHA-256 are bound
//               in the plan; a one-time confirmation for exactly this plan
//   4 recheck   at the confirmation: the protective backup and the image file held open read-only and checked again (the image's
//               SHA-256 computed again); the console's registry state read again, unchanged; the record saved before the first write
//   5 write     each step once; the first failure stops every later step
//   6 verify    the registry read again: the option is there with the image's SHA-256 and size (or gone)
// A system image is a second system on the console, which starts only from CCS Nucleus's startup chooser: when the chooser is not installed
// yet, the same reviewed change adds it first (method:ccs-nucleus-chooser, its package placed in hakchi's transfer folder; nstart, the chooser
// shown at start, everything else of the start setting kept), then the image; hakchi installs the chooser at the console's next start.
// The image never goes into hakchi's firmware folders (hakchi starts the first image there by itself, without a choice). In the chooser it
// starts only when chosen, for one start; the console's own system stays the start without a choice and the way back.
using CcsBackup.Archive;
using CcsBackup.Compose;
using CcsBackup.Device;
using CcsBackup.Library;
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
    public sealed class FirmwarePlan
    {
        public string Op, Id, Label, Hwid, ConsoleTag, Session, Digest, Nonce, Text, ImagePath, ImageSha256; public long ImageBytes; public DateTime ExpiresUtc, ImageWriteUtc;
        /// <summary>CCS Nucleus's start setting the change writes when it adds the chooser ("DEFAULT always HOME SELECTION TIMEOUT"), else null</summary>
        public string StartLine;
        public BoundArchive ProtectiveArchive; public MethodState Before; public SystemImageInfo System;
        public string ProtectivePath => ProtectiveArchive?.Path;
        public List<string> Steps = new List<string>(), Notes = new List<string>();
        public JObj ToJson() => new JObj
        {
            { "operation", Op }, { "id", Id }, { "label", Label }, { "console_tag", ConsoleTag }, { "text", Text }, { "steps", Steps }, { "notes", Notes },
            { "start", StartLine }, { "image", new JObj { { "file", ImagePath }, { "sha256", ImageSha256 }, { "bytes", ImageBytes }, { "size", Compose.Units.Size(ImageBytes) } } }, { "system", System?.ToJson() },
            { "console_state", Before?.ToJson() }, { "protective_backup", ProtectivePath }, { "evidence", new List<object> { ProtectiveArchive?.ToJson() } },
            { "digest", Digest }, { "nonce", Nonce }, { "expires_utc", ExpiresUtc.ToString("o", CultureInfo.InvariantCulture) },
        };
    }

    public sealed class FirmwareOutcome { public string State, Problem, RecordFile; public List<string> Done = new List<string>(); public FirmwarePlan Plan; public bool Ok => State == "added" || State == "removed"; }

    public static class FirmwareEngine
    {
        public static readonly string[] Steps = { "rput", "rimport", "rdrop", "method:" + Methods.Chooser, "nstart" };
        static readonly Regex IdRe = new Regex("^[a-z0-9][a-z0-9-]{0,30}$"), LabelRe = new Regex("^[A-Za-z0-9 .,'!&()+:-]{1,60}$");
        public static bool IdOk(string id) => id != null && IdRe.IsMatch(id) && id != "native" && id != "ccs-home";
        public static string DefaultId(string sha256) => "owner-" + sha256.Substring(0, 8);
        public static string StepLabel(string s) => s == "rput" ? "the system image sent to the console and checked there" : s == "rimport" ? "the system image added to CCS Nucleus's choices"
            : s.StartsWith("method:", StringComparison.Ordinal) || s == "nstart" ? MethodEngine.StepLabel(s) : "the choice and its system image removed";

        static (string sha, long bytes, string why) Hash(string file)
        {
            try
            {
                using (var f = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var why = PrivateRoles.Check("root-squashfs", f); if (why != null) return (null, f.Length, why);
                    f.Position = 0; using (var h = SHA256.Create()) return (Hex.Of(h.ComputeHash(f)), f.Length, null);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return (null, -1, "could not be read (" + ex.Message + ")"); }
        }

        /// <summary>Phases 1 to 3 (nothing written to the console). add: file (on this PC), id (or null: owner-&lt;first 8 hex&gt;), label;
        /// remove: id.</summary>
        public static (FirmwarePlan plan, FirmwareOutcome refused) Prepare(RecoveryConsole con, EngineContext ctx, string op, string id, string label, string file)
        {
            var o = new FirmwareOutcome();
            FirmwareOutcome Refuse(string why) { o.State = "refused"; o.Problem = why; return o; }
            if (con.Profile != SessionProfile.Prepare) return (null, Refuse("The console is not in a preparation session."));
            if (op != "add" && op != "remove") return (null, Refuse("Choose whether to add a system image or remove one."));
            var p = new FirmwarePlan { Op = op, Session = con.SessionId };
            if (op == "add")
            {
                if (string.IsNullOrEmpty(file) || !File.Exists(file)) return (null, Refuse("The chosen file is not there."));
                var sys = SystemImage.Identify(file);
                if (!sys.Ok) return (null, Refuse("The chosen file " + sys.Why + "."));
                label = string.IsNullOrWhiteSpace(label) ? sys.DefaultLabel : label.Trim();
                if (!LabelRe.IsMatch(label)) return (null, Refuse("The name shown in the chooser can have letters, digits, spaces and . , ' ! & ( ) + : - (at most 60)."));
                p.System = sys;
                var (sha, bytes, why) = Hash(file);
                if (why != null) return (null, Refuse("The chosen file " + why + ". Only a system image (a SquashFS root file system, .hsqs) can be added."));
                id = string.IsNullOrEmpty(id) ? DefaultId(sha) : id;
                if (!IdOk(id)) return (null, Refuse("The choice's id can have lower-case letters, digits and - (at most 31)."));
                p.Id = id; p.Label = label; p.ImagePath = Path.GetFullPath(file); p.ImageSha256 = sha; p.ImageBytes = bytes; p.ImageWriteUtc = File.GetLastWriteTimeUtc(file);
            }
            else { if (!IdOk(id)) return (null, Refuse("Choose the system image to remove.")); p.Id = id; }
            ConsoleIdentity cid;
            try { cid = con.Identify(); Support.CheckForBackup(cid, ctx.RecoveryImageId, "firmware"); }
            catch (ConsoleRefusedException ex) { return (null, Refuse(ex.Message)); }
            var dw = ConsoleData.Refusal(cid); if (dw != null) return (null, Refuse(dw + " Nothing was changed."));
            if (p.System != null) p.System.Notes = SystemImage.NotesOn(p.System, cid.Model ?? ConsoleModels.Nes);
            p.Hwid = cid.Hwid; p.ConsoleTag = cid.ShortTag;
            var st = con.PrepState();
            if (!st.Ok) return (null, Refuse("The console's CCS Nucleus could not be read (" + st.Problem + "). Nothing was changed."));
            if (st.Hakchi != Preparation.PinnedHakchiVersion) return (null, Refuse(HakchiRequirement.Refusal(HakchiRequirement.Detected(st.Hakchi))));
            if (!st.Installed("ccs-nucleus") || st.Registry != "ok") return (null, Refuse(st.Installed("ccs-nucleus") ? "CCS Nucleus's list of choices cannot be read on this console. Nothing was changed." : "CCS Nucleus is not installed on this console. A system image is added as one of its choices: install CCS Nucleus first."));
            p.Before = st;
            if (op == "add")
            {
                if (st.Options.TryGetValue(p.Id, out var have)) return (null, Refuse(have.sha256 == p.ImageSha256 ? "This system image is already one of CCS Nucleus's choices (" + p.Id + "). Nothing to do." : "Another choice already has the id " + p.Id + "."));
                var same = st.Options.FirstOrDefault(x => x.Value.sha256 == p.ImageSha256).Key;
                if (same != null) return (null, Refuse("This system image is already one of CCS Nucleus's choices (" + same + "). Nothing to do."));
                if (st.Free < 0) return (null, Refuse("The console's free space could not be read. Nothing was changed."));
                // the shared storage accounting: the image in whole blocks, room kept for saves and the safety margin, against df's figure
                var room = StorageSpec.Account(new Dictionary<string, object>
                {
                    ["profile"] = cid.Model?.Id ?? StorageSpec.NesModel, ["storage"] = new Dictionary<string, object> { ["source"] = "measured", ["available_bytes"] = st.Free },
                    ["items"] = new object[] { new Dictionary<string, object> { ["category"] = "image", ["label"] = "the system image", ["files"] = new object[] { new Dictionary<string, object> { ["bytes"] = p.ImageBytes } } } },
                });
                if (!(bool)room["write_ok"]) return (null, Refuse(Capacity.Why(room, out _, out _) + " Nothing was changed."));
                // a second system starts only from the startup chooser: without it, the chooser comes in the same change, first
                if (!st.Installed(Methods.Chooser))
                {
                    var sp = st.StartParts;
                    if (sp == null || !Hex.IsHex(Methods.Sha256(Methods.Chooser), 64)) return (null, Refuse(sp == null ? "CCS Nucleus's start setting cannot be read on this console. Nothing was changed." : "This build does not carry the startup chooser's package. Nothing was changed."));
                    p.StartLine = sp[0] + " always " + sp[2] + " " + sp[3] + " " + sp[4];
                    var wp = MethodEngine.WaitingProblem(st, new Dictionary<string, string> { { Methods.Waiting(Methods.Chooser), Methods.Sha256(Methods.Chooser) } });
                    if (wp != null) return (null, Refuse("Adding a system image is refused: " + wp + ". Start the console normally once so hakchi applies it, then check again. Nothing was changed."));
                    p.Steps.Add("method:" + Methods.Chooser); if (st.Start != p.StartLine) p.Steps.Add("nstart");
                    p.Notes.Add("A system image starts only from CCS Nucleus's startup chooser, which is not on this console yet: it is added in the same change and shows at start. hakchi installs it at the console's next start; what opens when nothing is chosen stays as it is.");
                }
                p.Steps.Add("rput"); p.Steps.Add("rimport");
                p.Text = "Add the " + p.System.Text + " (" + Compose.Units.Size(p.ImageBytes) + ") as the choice \"" + p.Label + "\" in CCS Nucleus's chooser" + (p.StartLine != null ? ", with the chooser itself." : ".");
                p.Notes.Add("It goes to CCS Nucleus's own folder on the data storage, never to hakchi's firmware folder: it starts only when you choose it, for one start. When you don't choose it, the console starts its own system as before, and that stays the way back.");
                p.Notes.Add("The console's startup software, bootloader, own system, games and saves are not changed by adding it.");
                if (p.System.Version != null) p.Notes.Add("The image records its system version as " + p.System.Version + ".");
                p.Notes.AddRange(p.System.Notes);
                p.Notes.Add("Free on the console now: " + Compose.Units.Size(st.Free) + "; after adding it about " + Compose.Units.Size(st.Free - p.ImageBytes) + ".");
            }
            else
            {
                if (!st.Options.TryGetValue(p.Id, out var have) || have.kind != "owner-root" || !Hex.IsHex(have.sha256, 64)) return (null, Refuse("CCS Nucleus has no system image choice " + p.Id + ". Nothing to remove."));
                p.ImageSha256 = have.sha256; p.ImageBytes = have.bytes; p.Steps.Add("rdrop");
                p.Text = "Remove the choice " + p.Id + " and its system image from CCS Nucleus.";
                p.Notes.Add("Only CCS Nucleus's own, unchanged copy of the image is deleted; an image kept in hakchi's firmware folder is never touched (only its choice goes).");
            }
            ctx.Say("protect", "Backing up the console as it is now, before anything changes...");
            var prot = CaptureEngine.Run(con, ctx);
            if (!prot.Ok || prot.Verification == null || !prot.Verification.Ok) { o.State = "stopped"; o.Problem = "The backup of the console as it is now did not complete (" + (prot.Problem ?? prot.State) + "), so nothing was written."; return (null, o); }
            if (!ConsoleIdentity.SameConsole(prot.Verification.Hwid, cid.Hwid) || prot.Verification.Simulated != con.Simulated || !prot.Verification.RestoreReady) return (null, Refuse("The backup made just now is not a complete backup of this console, so nothing was written."));
            p.ProtectiveArchive = BoundArchive.From("backup made just now, before changing CCS Nucleus's choices", prot.PrimaryPath, prot.Verification, con.SessionId);
            p.Notes.Add("The backup just made" + (op == "add" ? " and the chosen file are" : " is") + " checked again when you confirm; if anything is missing or changed, nothing is written.");
            p.Nonce = Hex.Of(Guid.NewGuid().ToByteArray()); p.ExpiresUtc = DateTime.UtcNow + RestoreEngine.ConfirmWindow;
            p.Digest = DigestOf(p);
            return (p, null);
        }

        static string DigestOf(FirmwarePlan p)
        {
            using (var h = SHA256.Create())
                return Hex.Of(h.ComputeHash(Encoding.UTF8.GetBytes("ccs-firmware-plan/2\n" + ConsoleIdentity.NormalizeHwid(p.Hwid) + "\n" + p.Session + "\n" + p.Op + " " + p.Id + "\n" + p.Label + "\n" + p.System?.Text + "\n" + p.ImagePath + " " + p.ImageSha256 + " " +
                    p.ImageBytes.ToString(CultureInfo.InvariantCulture) + "\n" + p.Before?.Digest() + "\n" + p.ProtectiveArchive?.DigestLine() + "\n" + string.Join(",", p.Steps) + "\n" + p.StartLine + "\n" + p.Nonce)));
        }

        public static FirmwareOutcome Execute(RecoveryConsole con, FirmwarePlan plan, string digest, string nonce, EngineContext ctx)
        {
            var o = new FirmwareOutcome { Plan = plan };
            FirmwareOutcome Refuse(string why) { o.State = "refused"; o.Problem = why; return o; }
            if (plan == null) return Refuse("No plan.");
            if (digest != plan.Digest || nonce != plan.Nonce || plan.Nonce == null || DigestOf(plan) != plan.Digest) return Refuse("The confirmation does not match the reviewed change.");
            if (DateTime.UtcNow > plan.ExpiresUtc) return Refuse("The confirmation is more than 10 minutes old. Review the change again.");
            if (con.SessionId != plan.Session || plan.ProtectiveArchive?.Session != plan.Session) return Refuse("The console was connected again after the review. Review the change again. Nothing was changed.");
            var id = con.Identify();
            if (!ConsoleIdentity.SameConsole(id.Hwid, plan.Hwid)) return Refuse("A different console is connected now. Nothing was changed.");
            using (var hold = Evidence.Hold.Open(plan.ProtectivePath, plan.Op == "add" ? plan.ImagePath : null))
            {
                if (hold.Problem != null) return Refuse("A file the change relies on cannot be used (" + hold.Problem + "). Nothing was changed; review the change again.");
                ctx.Say("validate", "Checking the backup" + (plan.Op == "add" ? " and the system image" : "") + " again...");
                var why = Evidence.Recheck(plan.ProtectiveArchive, plan.Hwid, con.Simulated, con.SessionId, ctx.Cancelled);
                if (why != null) return Refuse(why + " Nothing was changed; review the change again.");
                if (plan.Op == "add")
                {
                    var (sha, bytes, bad) = Hash(plan.ImagePath);
                    if (bad != null || sha != plan.ImageSha256 || bytes != plan.ImageBytes) return Refuse("The chosen system image changed after the review" + (bad != null ? " (it " + bad + ")" : "") + ". Nothing was changed; review the change again.");
                }
                var now = con.PrepState();
                if (!now.Ok || now.Digest() != plan.Before.Digest()) return Refuse("CCS Nucleus's choices on the console changed since the review. Nothing was changed; review the change again.");
                Record rec;
                try { rec = Record.Start(ctx.Settings.RecordsFolder, Guid.NewGuid().ToString("N").Substring(0, 12), "firmware", new JObj { { "plan", plan.ToJson() }, { "console_tag", plan.ConsoleTag }, { "simulated", con.Simulated } }); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return Refuse("The change's record could not be saved in " + ctx.Settings.RecordsFolder + " (" + ex.Message + "), so nothing was written."); }
                plan.Nonce = null;   // used up only now
                o.RecordFile = rec.File; var phases = new List<object>();
                var auth = new WriteAuthority(plan.Digest, plan.Hwid, plan.Steps.Select(x => x.StartsWith("method:", StringComparison.Ordinal) ? "method" : x).Distinct().ToArray(), () => con.Simulated || Gate.FirmwareWrites, Gate.FirmwareText);
                rec.State("writing"); con.Card("writing");
                foreach (var s in plan.Steps)
                {
                    if (ctx.Cancelled()) { rec.State("stopped", "canceled between steps"); o.State = "stopped"; o.Problem = "Canceled. Steps done: " + (o.Done.Count == 0 ? "none" : string.Join("; ", o.Done.Select(StepLabel))) + ". Adding the same image again finishes it without sending it twice."; return o; }
                    ctx.Say("firmware", StepLabel(s) + "... (do not unplug the console)");
                    phases.Add(new JObj { { "step", s }, { "state", "began" }, { "at", Record.Now() } }); rec.Set("phases", phases).Save();
                    WriteResult r; string want;
                    try
                    {
                        if (s == "rput")
                            using (var f = new FileStream(plan.ImagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                                r = con.PrepStepStream("rput", auth, new[] { plan.Id, plan.ImageBytes.ToString(CultureInfo.InvariantCulture), plan.ImageSha256 }, f, plan.ImageBytes);
                        else if (s == "rimport") r = con.PrepStep("rimport", auth, new[] { plan.Id, plan.ImageSha256, plan.ImageBytes.ToString(CultureInfo.InvariantCulture), Convert.ToBase64String(Encoding.ASCII.GetBytes(plan.Label)) });
                        else if (s.StartsWith("method:", StringComparison.Ordinal)) r = con.PrepStep("method", auth, new[] { Methods.Chooser, Methods.Sha256(Methods.Chooser) });
                        else if (s == "nstart") r = con.PrepStep("nstart", auth, plan.StartLine.Split(' '));
                        else r = con.PrepStep("rdrop", auth, new[] { plan.Id, plan.ImageSha256 });
                    }
                    catch (GateClosedException ex) { rec.State("refused", ex.Message); o.State = "refused"; o.Problem = ex.Message; return o; }
                    want = s == "rput" ? "ok( already)? " + plan.ImageSha256 : s == "rimport" ? "ok " + plan.Id + " " + plan.ImageSha256 : "ok " + plan.Id;
                    var ok = s.StartsWith("method:", StringComparison.Ordinal) ? MethodEngine.Answer(r, "method", Methods.Sha256(Methods.Chooser))
                        : s == "nstart" ? MethodEngine.StartAnswer(r, plan.StartLine) : r.Ok && Regex.IsMatch(r.Line ?? "", "^result " + s + " " + want + "$");
                    phases.Add(new JObj { { "step", s }, { "state", ok ? (r.Line.Contains(" ok already ") ? "already-there" : "done-and-read-back") : "failed" }, { "detail", r.Line ?? r.Problem }, { "at", Record.Now() } }); rec.Set("phases", phases).Save();
                    ctx.At("firmware-" + s);
                    if (!ok)
                    {
                        auth.Revoke(s + ": " + (r.Problem ?? r.Line)); rec.State("stopped", r.Problem ?? r.Line); con.Card("stopped"); o.State = "stopped";
                        o.Problem = "Stopped at: " + StepLabel(s) + " (" + (r.Problem ?? r.Line) + "). Nothing more was written and nothing was retried. Steps done before: " + (o.Done.Count == 0 ? "none" : string.Join("; ", o.Done.Select(StepLabel))) + ". The backup made just now is unchanged.";
                        return o;
                    }
                    o.Done.Add(s);
                }
                var after = con.PrepState();
                bool good = after.Ok && (plan.Op == "add" ? after.Options.TryGetValue(plan.Id, out var a) && a.kind == "owner-root" && a.sha256 == plan.ImageSha256 && a.bytes == plan.ImageBytes : !after.Options.ContainsKey(plan.Id))
                    && (plan.StartLine == null || after.Start == plan.StartLine && after.Waiting.TryGetValue(Methods.Waiting(Methods.Chooser), out var cw) && cw == Methods.Sha256(Methods.Chooser));
                if (!good) { rec.State("stopped", "CCS Nucleus's registry does not read back as planned"); o.State = "stopped"; o.Problem = "Every step reported success, but CCS Nucleus's list of choices does not read back as planned. Its state is not verified; nothing more was changed."; return o; }
                rec.Set("after", new JObj { { "console_state", after.ToJson() } });
                o.State = plan.Op == "add" ? "added" : "removed"; rec.State(o.State); con.Card("done"); return o;
            }
        }
    }
}
