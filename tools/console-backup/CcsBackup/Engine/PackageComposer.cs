// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Console Composer's private restore package: the chosen files of a composition, built OFFLINE into one self-contained private ZIP
// (Compose/PrivatePackage: "PRIVATE" label, the destination console's identity, the exact target plan, every file's SHA-256 and where it
// came from), then applied to that console later under the STRICT policy, exactly like a composition from backups:
//   build     each chosen file read from its verified source backup's data storage (checked against that backup's own file list) and
//             stored as role "data-file" with its exact place "data:<path>" (a path Console Composer may write); the source backups are
//             never changed. The destination: the source console (rebuild) or the console a chosen backup of it names (migrate).
//   plan      the package opened and every member checked (archive safety, manifest, SHA-256, role) and bound by its own SHA-256; it must
//             name THIS console; each target read (stat); a complete backup of the console as it is now, in this session; one-time
//             confirmation. A game file target is also checked as an NES game file.
//   execute   the package and the protective backup held read-only and checked again; the record saved first; then each file once through
//             the files image (scope "compose"), read back; the first failure stops every later change. Gate.ComposerWrites.
// A private package is never shareable (ShareGuard) and never a restore point; startup software, bootloaders, storage table and identity
// are never part of what it writes.
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

namespace CcsBackup.Engine
{
    public sealed class PackagePlan
    {
        public string Hwid, ConsoleTag, Session, Digest, Nonce, Refusal, PackagePath, PackageSha256, PackageId; public long PackageBytes; public DateTime ExpiresUtc; public bool Simulated;
        public BoundArchive Protective;
        public List<PlannedFileChange> Changes = new List<PlannedFileChange>(); public List<string> Skipped = new List<string>(), Notes = new List<string>();
        public JObj ToJson() => new JObj
        {
            { "kind", "package" }, { "console_tag", ConsoleTag }, { "package", new JObj { { "path", PackagePath }, { "sha256", PackageSha256 }, { "bytes", PackageBytes }, { "id", PackageId } } },
            { "evidence", Protective == null ? new List<object>() : new List<object> { Protective.ToJson() } },
            { "changes", Changes.Select(c => (object)new JObj { { "op", c.Op }, { "path", c.Path }, { "new_sha256", c.NewSha256 }, { "bytes", c.Bytes }, { "old", c.OldState } }).ToList() },
            { "skipped", Skipped }, { "notes", Notes }, { "refusal", Refusal }, { "digest", Digest }, { "nonce", Nonce }, { "expires_utc", ExpiresUtc.ToString("o", CultureInfo.InvariantCulture) },
        };
    }

    public static class PackageComposer
    {
        static string Sha(byte[] b) { using (var h = SHA256.Create()) return Hex.Of(h.ComputeHash(b)); }

        /// <summary>builds the private package (no console). items: each chosen file and the verified backup it comes from (a rebuild may
        /// take several backups of one console; a migration takes one backup of the other console); destinationBackup: a verified backup
        /// of the console the package is for. Returns the opened, checked package.</summary>
        public static PrivatePackageInfo Build(IList<(string backup, string path)> items, string destinationBackup, string policy, string startingPoint, string outPath, string toolVersion, Func<bool> cancelled = null)
        {
            if (items == null || items.Count == 0) throw new ComposeException("private.empty", "Choose at least one file.");
            var dv = Verifier.VerifyFile(destinationBackup, null, cancelled);
            if (!dv.Ok) throw new ComposeException("private.destination", Path.GetFileName(destinationBackup) + " did not pass its check: " + dv.Problems[0]);
            var verified = new Dictionary<string, Verification>(StringComparer.OrdinalIgnoreCase);
            foreach (var b in items.Select(i => i.backup).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var vb = Verifier.VerifyFile(b, null, cancelled);
                if (!vb.Ok) throw new ComposeException("private.source", Path.GetFileName(b) + " did not pass its check: " + vb.Problems[0]);
                if (vb.Simulated != dv.Simulated) throw new ComposeException("private.destination", "A simulated console's backup and a real console's are never mixed.");
                bool same = ConsoleIdentity.SameConsole(vb.Hwid, dv.Hwid);
                if (startingPoint == "rebuild-from-backups" && !same) throw new ComposeException("private.destination", "A rebuild package is for the console its backups came from.");
                if (startingPoint == "like-my-other-console" && same) throw new ComposeException("private.destination", "Choose a backup of the OTHER console the package is for.");
                if (vb.Model == null || vb.Model != dv.Model) throw new ComposeException("private.destination", Path.GetFileName(b) + " is a backup of " + (vb.Model?.Name ?? "a console this version does not support") + ", not of the model of the console the package is for (" + (dv.Model?.Name ?? "unknown") + "): one kind of console's data is never put on another kind.");
                var rw = same ? null : ConsoleData.RegionRefusal(vb.Model, vb.Region, dv.Region); if (rw != null) throw new ComposeException("private.destination", rw);
                verified[b] = vb;
            }
            if (startingPoint == "like-my-other-console" && verified.Count != 1) throw new ComposeException("private.source", "A migration package takes one backup of the other console.");
            var hw = ConsoleIdentity.NormalizeHwid(dv.Hwid);
            if (hw.Length != 32) throw new ComposeException("private.destination", "The backup does not record its console's identity completely.");
            var spec = new PrivatePackageSpec { DestinationHwid = string.Join(" ", Enumerable.Range(0, 4).Select(i => hw.Substring(i * 8, 8))), InstallPolicy = policy, StartingPoint = startingPoint, ToolName = "CCS Console Workbench", ToolVersion = toolVersion ?? "" };
            spec.Environments.Add(("console", "nintendo-original"));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (backup, p) in items)
            {
                var v = verified[backup]; var m = v.Manifest;
                if (!FilesEngine.PathOk(p) || !FilesEngine.InScope("compose", p)) throw new ComposeException("private.place", p + " is not something Console Composer moves.");
                if (!seen.Add(p)) throw new ComposeException("private.place", p + " is chosen twice.");
                var bytes = ComposerEngine.FileOf(backup, p, out var why);
                if (bytes == null) throw new ComposeException("private.source", p + ": " + why);
                spec.Members.Add(new PrivateMemberSource
                {
                    Role = "data-file", Place = "data:" + p, Bytes = bytes,
                    Provenance = new JObj { { "kind", "console-backup" }, { "backup_id", m?.S("backup_id") }, { "console_tag", m?.O("console")?.S("tag") }, { "archive_sha256", v.ArchiveSha256 }, { "path", p }, { "simulated", v.Simulated } },
                });
            }
            return PrivatePackage.Write(outPath, spec);
        }

        public static (PackagePlan plan, string why) Plan(RecoveryConsole con, EngineContext ctx, string package)
        {
            if (con.Profile != SessionProfile.Files) return (null, "The console is not in a file session.");
            if (string.IsNullOrEmpty(package) || !File.Exists(package)) return (null, "The private package was not found.");
            PrivatePackageInfo info; string sha; long bytes;
            try { info = PrivatePackage.Open(package, true); using (var f = File.OpenRead(package)) using (var h = SHA256.Create()) { bytes = f.Length; sha = Hex.Of(h.ComputeHash(f)); } }
            catch (ComposeException ex) { return (null, "The private package cannot be used: " + ex.Message); }
            catch (IOException ex) { return (null, "The private package could not be read: " + ex.Message); }
            var id = con.Identify();
            var mw = ConsoleModels.RefusalFor(id, "composition") ?? ConsoleData.Refusal(id); if (mw != null) return (null, mw + " Nothing was changed.");
            if (!ConsoleIdentity.SameConsole(info.DestinationHwid, id.Hwid)) return (null, "This private package is for console " + info.DestinationTag + ", not the console connected now (" + id.ShortTag + "). Nothing was changed.");
            if (info.Members.Any(x => x.Provenance?["simulated"] is bool s && s != con.Simulated)) return (null, "A simulated console's files and a real console are never mixed.");
            var p = new PackagePlan { Hwid = id.Hwid, ConsoleTag = id.ShortTag, Session = con.SessionId, Simulated = con.Simulated, PackagePath = package, PackageSha256 = sha, PackageBytes = bytes, PackageId = info.PackageId };
            var plan = info.Manifest["target_plan"] as JObj;
            var targets = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in (Doc.Arr(plan?["components"]) ?? new List<object>()).OfType<JObj>())
                {
                    string role = c["role"] as string, place = c["place"] as string, csha = c["sha256"] as string;
                    if (role != "data-file" || place == null || !place.StartsWith("data:", StringComparison.Ordinal)) { p.Skipped.Add(PrivateRoles.All.TryGetValue(role ?? "", out var r0) ? Cap(r0.Text) + ": not written by Console Composer (it never writes startup software, bootloaders, the storage table or system images)" : "an unknown component"); continue; }
                    var target = place.Substring(5);
                    if (!FilesEngine.PathOk(target) || !FilesEngine.InScope("compose", target)) { p.Skipped.Add(target + ": not something Console Composer moves"); continue; }
                    if (SegaCleanup.NeverWritten(target)) { p.Skipped.Add(target + ": " + SegaCleanup.NotWritten); continue; }
                    if (!targets.Add(target)) return (null, target + " is named twice in the package.");
                    byte[] data;
                    try { data = PrivatePackage.ReadMember(package, info.Members.First(x => x.Name == PrivateRoles.MemberName(role, csha))); }
                    catch (ComposeException) { return (null, "The private package changed while it was being read. Nothing was changed."); }
                    if (target.EndsWith(".nes", StringComparison.OrdinalIgnoreCase)) { var why = PrivateRoles.Check("game-nes", new MemoryStream(data, false)); if (why != null) { p.Skipped.Add(target + ": " + why); continue; } }
                    var (state, n) = con.FileStat("compose", target);
                    if (state == "folder" || state == "link" || state == "special") { p.Skipped.Add(target + ": a " + state + " on this console"); continue; }
                    if (state == csha) { p.Notes.Add(target + " is already the same on this console."); continue; }
                    p.Changes.Add(new PlannedFileChange { Op = "put", Path = target, Data = data, Bytes = data.LongLength, NewSha256 = csha, OldState = state, OldBytes = n });
                }
            if (p.Changes.Count == 0) { p.Refusal = "Nothing needs to change on this console." + (p.Skipped.Count > 0 ? " Some parts of the package could not be used." : ""); return (p, null); }
            p.Notes.Add("This console keeps its own startup software, bootloaders, storage table and identity; only the files listed are written.");
            if (p.Changes.Any(c => c.OldState != "absent")) p.Notes.Add(Units.Count(p.Changes.Count(c => c.OldState != "absent"), "file") + " on this console will be replaced; the backup made just now keeps them.");
            ctx.Say("protect", "Backing up the console as it is now, before anything changes...");
            var prot = CaptureEngine.Run(con, ctx);
            if (!prot.Ok || prot.Verification == null || !prot.Verification.Ok || !ConsoleIdentity.SameConsole(prot.Verification.Hwid, id.Hwid) || !prot.Verification.RestoreReady)
                return (null, "The backup of the console as it is now did not complete (" + (prot.Problem ?? prot.State) + "), so nothing was written.");
            p.Protective = BoundArchive.From("backup made just now, before applying the package", prot.PrimaryPath, prot.Verification, con.SessionId);
            var room = Capacity.Check(prot.PrimaryPath, p.Changes, out var need, out var have); if (room != null) return (null, room + " Nothing was written.");
            p.Notes.Add("Space: about " + Units.Gb(need) + " of the " + Units.Gb(have) + " available on the console's data storage.");
            p.Nonce = Hex.Of(Guid.NewGuid().ToByteArray()); p.ExpiresUtc = DateTime.UtcNow + RestoreEngine.ConfirmWindow; p.Digest = DigestOf(p);
            return (p, null);
        }

        static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        static string DigestOf(PackagePlan p)
        {
            var sb = new StringBuilder("ccs-package-plan/1\n").Append(ConsoleIdentity.NormalizeHwid(p.Hwid)).Append('\n').Append(p.Session).Append('\n');
            sb.Append(p.PackageSha256).Append(' ').Append(p.PackageBytes.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(p.PackageId).Append('\n');
            sb.Append(p.Protective?.DigestLine()).Append('\n');
            foreach (var c in p.Changes) sb.Append(c.Path).Append(' ').Append(c.NewSha256).Append(' ').Append(c.Bytes.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(c.OldState).Append('\n');
            sb.Append(p.Nonce);
            using (var h = SHA256.Create()) return Hex.Of(h.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())));
        }

        /// <summary>the package as the review saw it: same bytes, still a valid private package for this console</summary>
        static string RecheckPackage(PackagePlan p)
        {
            try
            {
                if (!File.Exists(p.PackagePath)) return "The private package is missing (" + p.PackagePath + ").";
                string sha; long n; using (var f = new FileStream(p.PackagePath, FileMode.Open, FileAccess.Read, FileShare.Read)) using (var h = SHA256.Create()) { n = f.Length; sha = Hex.Of(h.ComputeHash(f)); }
                if (sha != p.PackageSha256 || n != p.PackageBytes) return "The private package changed after the review.";
                var info = PrivatePackage.Open(p.PackagePath, true);
                if (info.PackageId != p.PackageId || !ConsoleIdentity.SameConsole(info.DestinationHwid, p.Hwid)) return "The private package does not match what the review saw.";
                return null;
            }
            catch (ComposeException ex) { return "The private package no longer passes its check (" + ex.Message + ")."; }
            catch (IOException ex) { return "The private package could not be read (" + ex.Message + ")."; }
        }

        public static FileOutcome Execute(RecoveryConsole con, PackagePlan plan, string digest, string nonce, EngineContext ctx)
        {
            var o = new FileOutcome();
            FileOutcome Refuse(string why) { o.State = "refused"; o.Problem = why; return o; }
            if (plan == null || plan.Refusal != null) return Refuse(plan?.Refusal ?? "No plan.");
            if (digest != plan.Digest || nonce != plan.Nonce || plan.Nonce == null || DigestOf(plan) != plan.Digest) return Refuse("The confirmation does not match the reviewed package.");
            if (DateTime.UtcNow > plan.ExpiresUtc) return Refuse("The confirmation is more than 10 minutes old. Review the package again.");
            if (con.SessionId != plan.Session || plan.Protective?.Session != plan.Session) return Refuse("The console was connected again after the review. Review the package again. Nothing was changed.");
            var id = con.Identify();
            if (!ConsoleIdentity.SameConsole(id.Hwid, plan.Hwid)) return Refuse("A different console is connected now. Nothing was changed.");
            using (var hold = Evidence.Hold.Open(new[] { plan.PackagePath, plan.Protective.Path }))
            {
                if (hold.Problem != null) return Refuse("A file the package step relies on cannot be used (" + hold.Problem + "). Nothing was changed.");
                var pk = RecheckPackage(plan); if (pk != null) return Refuse(pk + " Nothing was changed.");
                var pw = Evidence.Recheck(plan.Protective, plan.Hwid, con.Simulated, con.SessionId, ctx.Cancelled); if (pw != null) return Refuse(pw + " Nothing was changed.");
                Record rec;
                try { rec = Record.Start(ctx.Settings.RecordsFolder, Guid.NewGuid().ToString("N").Substring(0, 12), "compose-package", new JObj { { "plan", plan.ToJson() }, { "console_tag", plan.ConsoleTag }, { "simulated", con.Simulated } }); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return Refuse("The record could not be saved (" + ex.Message + "), so nothing was written."); }
                plan.Nonce = null;
                o.RecordFile = rec.File; var log = new List<object>();
                var auth = new WriteAuthority(plan.Digest, plan.Hwid, plan.Changes.Select(c => "put:" + c.Path).ToArray(), () => con.Simulated || Gate.ComposerWrites, Gate.ComposerText);
                rec.State("writing"); con.Card("writing");
                foreach (var c in plan.Changes)
                {
                    if (ctx.Cancelled()) { rec.State("stopped", "canceled between files"); o.State = "stopped"; o.Problem = "Canceled. Files written: " + (o.Done.Count == 0 ? "none" : string.Join(", ", o.Done)) + "."; return o; }
                    ctx.Say("compose", "Writing " + c.Path + "...");
                    WriteResult r;
                    try { r = con.FileStep("put", "compose", c.Path, c.NewSha256, c.OldState, c.Data, auth); }
                    catch (GateClosedException ex) { rec.State("refused", ex.Message); o.State = "refused"; o.Problem = ex.Message; return o; }
                    log.Add(new JObj { { "path", c.Path }, { "state", r.Ok ? "written-and-read-back" : "failed" }, { "detail", r.Line ?? r.Problem }, { "at", Record.Now() } }); rec.Set("changes", log).Save();
                    if (!r.Ok) { rec.State("stopped", r.Problem); con.Card("stopped"); o.State = "stopped"; o.Problem = "Stopped at " + c.Path + " (" + r.Problem + "). Nothing more was changed and nothing was retried. The backup made just now holds the console as it was."; return o; }
                    o.Done.Add(c.Path);
                }
                rec.State("changed"); con.Card("done"); o.State = "changed"; return o;
            }
        }
    }
}
