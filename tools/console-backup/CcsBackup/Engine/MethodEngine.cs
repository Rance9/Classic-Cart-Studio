// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// CCS Mod and CCS Nucleus on a console Hakchi2 CE 3.9.3 prepared (tools/console-nucleus/DESIGN.md): removing one, or changing from one to
// the other, through hakchi's own route. The console's next normal start runs hakchi's p7080_hmods, which first uninstalls the packages
// named in $installpath/transfer/uninstall (each with its own uninstall script: CCS's removes only its own unchanged files), then
// installs every *.hmod there with hakchi's pack_install, removes the folder and restarts. This engine only places those files, in the
// preparation image's session (ccsprep "method" and "unlist"), under its own boundary Gate.MethodWrites; it belongs in Console Restore.
//   1 check     the console identified; hakchi 3.9.3 on its data storage; which method is installed (hakchi's own package lists), which one
//               is in charge, and what already waits in the transfer folder (ccsprep mstate, read-only)
//   2 protect   a complete, verified backup of the console as it is now, in this session
//   3 plan      remove:  a removal list naming the method's package (unlist)
//               change:  the other method's package (method), THEN the removal list for the one installed (unlist): an interruption
//                        between the two leaves both installed with the old one still in charge, never neither; then CCS HOME's
//                        package in the new method's layout (Engine/HomePackage: hput, huse), unless that exact package is in use. To
//                        CCS Mod, the owner's choice of CCS HOME at power-on (off by default) is recorded first as CCS Mod's own setting
//                        (mstart), which CCS Mod's install applies at that start with its own autostart command
//               anything already waiting must be exactly this plan's own files (an interrupted earlier run is finished, not written
//               twice); anything else waiting there is refused. A one-time confirmation for exactly this plan
//   4 recheck   at the confirmation, before anything is used up or written: the protective backup held open read-only and verified again;
//               the console's method state read again and still exactly what the plan saw; the operation record saved before the
//               first write
//   5 write     each step once; each is read back from the storage; the first failure stops every later step
//   6 verify    the transfer folder read again: exactly the plan's files wait there for the next start
// Nothing is retried or undone automatically. The console applies the change at its next normal start (then restarts once).
using CcsBackup.Archive;
using CcsBackup.Device;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CcsBackup.Engine
{
    /// <summary>the two install methods' names and their packages (the preparation image carries them, pinned: its MANIFEST.json "methods")</summary>
    public static class Methods
    {
        public static readonly string[] Steps = { "mstart", "method", "unlist" };
        public static readonly string[] Policies = { "mod", "nucleus" };
        public static bool IsPolicy(string p) => p == "mod" || p == "nucleus";
        public static string Package(string policy) => policy == "mod" ? "ccs-mod" : policy == "nucleus" ? "ccs-nucleus" : null;
        public static bool IsPackage(string n) => n == "ccs-mod" || n == "ccs-nucleus";
        public static string Label(string policy) => policy == "mod" ? "CCS Mod" : policy == "nucleus" ? "CCS Nucleus" : "none";
        public static string Other(string policy) => policy == "mod" ? "nucleus" : "mod";
        /// <summary>the package's SHA-256 as this build's preparation image carries it (null when the image does not carry it)</summary>
        public static string Sha256(string package) => RecoveryImages.MethodSha(package);
        /// <summary>the removal list hakchi reads ("NAME\n"), and its SHA-256</summary>
        public static string ListSha256(string package) { using (var h = SHA256.Create()) return Hex.Of(h.ComputeHash(Encoding.ASCII.GetBytes(package + "\n"))); }
        public static string Waiting(string package) => package + ".hmod";
        /// <summary>CCS Mod's start at power-on in words, for a review (plain: what the console does when it is turned on)</summary>
        public static string StartText(string modStart) => modStart == "on"
            ? "When the console is turned on, CCS HOME opens first; leaving it, the console's own start continues to its own menu (CCS Mod's own optional start). Its CCS HOME entry in that menu stays. To turn this off later, prepare the console again with the box unchecked."
            : "The console starts in its own menu; CCS HOME opens from its entry there.";
        /// <summary>the mstart step for a chosen start when CCS Mod's package is placed (installed or installed again): needed when its setting,
        /// which CCS Mod's install applies, is not already the choice (no setting counts as off)</summary>
        public static bool NeedsStartSetting(MethodState st, string modStart) => modStart == "on" ? st.ModStartSetting != "on" : st.ModStartSetting == "on";
        /// <summary>the answer of mstart: "ok on|off" or "ok already on|off"</summary>
        internal static bool StartAnswer(WriteResult r, string modStart) => r.Ok && Regex.IsMatch(r.Line ?? "", "^result mstart ok( already)? " + modStart + "$");
    }

    /// <summary>ccsprep mstate: hakchi's version, the hakchi packages installed, CCS's method in charge and what waits in hakchi's transfer folder</summary>
    public sealed class MethodState
    {
        public bool Ok; public string Problem, Hakchi, Policy, Registry; public bool TransferLink; public long Free = -1;
        /// <summary>CCS HOME's package on the console (ccsprep mstate home_*): the copy in use ("absent", "other" or a copy name), each recorded
        /// copy (its MANIFEST.json's SHA-256, whether its folder is there), each installation in progress (attempt -> copy), the saves'
        /// identity file, a link on the way (nothing is written through it), and the RAM free for a transfer (-1: not reported)</summary>
        public string HomePointer; public bool HomeLink; public byte[] HomeIdentity; public long Ram = -1;
        /// <summary>CCS Mod's start at power-on (ccsprep mstate modstart): its setting (on, off, none, invalid) and its start script in hakchi's
        /// overlay (ours: exactly the package's template; other: a file CCS Mod did not write; absent). null: not reported</summary>
        public string ModStartSetting, ModStartScript;
        /// <summary>CCS HOME opens at power-on now: CCS Mod installed, its setting on and its own start script there</summary>
        public bool ModStartOn => Installed("ccs-mod") && ModStartSetting == "on" && ModStartScript == "ours";
        public SortedDictionary<string, (string manifest, bool dir)> HomeCopies = new SortedDictionary<string, (string, bool)>(StringComparer.Ordinal);
        public SortedDictionary<string, string> HomeAttempts = new SortedDictionary<string, string>(StringComparer.Ordinal);
        /// <summary>the copy in use is a recorded copy of exactly this package (its MANIFEST.json's SHA-256)</summary>
        public bool HomeInUse(string manifestSha) => HomePointer != null && HomeCopies.TryGetValue(HomePointer, out var c) && c.manifest == manifestSha && c.dir;
        /// <summary>CCS Nucleus's registry options as the console reads them: id -> (kind, sha256, bytes)</summary>
        public SortedDictionary<string, (string kind, string sha256, long bytes)> Options = new SortedDictionary<string, (string, string, long)>(StringComparer.Ordinal);
        public List<string> Packages = new List<string>(); public SortedDictionary<string, string> Waiting = new SortedDictionary<string, string>(StringComparer.Ordinal);
        public bool Installed(string package) => Packages.Contains(package);
        public string InstalledPolicy => Installed("ccs-mod") && Installed("ccs-nucleus") ? "both" : Installed("ccs-mod") ? "mod" : Installed("ccs-nucleus") ? "nucleus" : "none";
        public string Canonical() => "hakchi=" + Hakchi + "\n" + string.Join("", Packages.OrderBy(x => x, StringComparer.Ordinal).Select(p => "package=" + p + "\n")) + "policy=" + Policy + "\n" + (ModStartSetting == null ? "" : "modstart=" + ModStartSetting + " " + ModStartScript + "\n") + (TransferLink ? "transfer=link\n" : "") + string.Join("", Waiting.Select(w => "waiting=" + w.Key + " " + w.Value + "\n"))
            + "registry=" + Registry + "\n" + string.Join("", Options.Select(o => "option=" + o.Key + " " + o.Value.kind + " " + o.Value.sha256 + " " + o.Value.bytes.ToString(CultureInfo.InvariantCulture) + "\n"))
            + (HomeLink ? "home=link\n" : "home_pointer=" + HomePointer + "\n" + string.Join("", HomeCopies.Select(c => "home_copy=" + c.Key + " " + c.Value.manifest + " " + (c.Value.dir ? "dir" : "nodir") + "\n"))
               + string.Join("", HomeAttempts.Select(a => "home_attempt=" + a.Key + " " + a.Value + "\n")) + "home_identity=" + (HomeIdentity == null ? "absent" : Convert.ToBase64String(HomeIdentity)) + "\n");
        public string Digest() { using (var h = SHA256.Create()) return Hex.Of(h.ComputeHash(Encoding.UTF8.GetBytes(Canonical()))); }
        public JObj ToJson() => new JObj { { "hakchi", Hakchi }, { "packages", Packages.ToList() }, { "policy", Policy }, { "installed", InstalledPolicy }, { "waiting", Waiting.Select(w => (object)new JObj { { "name", w.Key }, { "sha256", w.Value } }).ToList() },
            { "mod_start", ModStartSetting == null ? null : new JObj { { "setting", ModStartSetting }, { "script", ModStartScript }, { "on", ModStartOn } } },
            { "free_bytes", Free }, { "registry", Registry }, { "options", Options.Select(o => (object)new JObj { { "id", o.Key }, { "kind", o.Value.kind }, { "sha256", o.Value.sha256 }, { "bytes", o.Value.bytes } }).ToList() },
            { "home", new JObj { { "in_use", HomeLink ? "link" : HomePointer }, { "copies", HomeCopies.Select(c => (object)new JObj { { "copy", c.Key }, { "manifest_sha256", c.Value.manifest }, { "folder", c.Value.dir } }).ToList() },
                { "unfinished", HomeAttempts.Select(a => (object)new JObj { { "attempt", a.Key }, { "copy", a.Value } }).ToList() }, { "saves_identity", HomeIdentity != null }, { "ram_free_bytes", Ram } } },
            { "digest", Digest() } };
        static readonly Regex Name = new Regex("^[A-Za-z0-9._+-]{1,120}$");
        public static MethodState Parse(string text, int code)
        {
            var m = new MethodState(); var lines = (text ?? "").Replace("\r", "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            if (code != 0 || lines.LastOrDefault() != "result mstate ok") { m.Problem = lines.LastOrDefault(l => l.StartsWith("result mstate ")) ?? "the console did not report its hakchi packages (status " + code + ")"; return m; }
            foreach (var l in lines)
            {
                var i = l.IndexOf('='); if (i <= 0) continue; var k = l.Substring(0, i); var v = l.Substring(i + 1);
                if (k == "hakchi") m.Hakchi = v;
                else if (k == "package") { if (!Name.IsMatch(v)) { m.Problem = "an unexpected package name"; return m; } m.Packages.Add(v); }
                else if (k == "policy") m.Policy = v;
                else if (k == "modstart") { var s = v.Split(' '); if (s.Length != 2 || !new[] { "on", "off", "none", "invalid" }.Contains(s[0]) || !new[] { "ours", "other", "absent" }.Contains(s[1])) { m.Problem = "an unexpected CCS Mod start state"; return m; } m.ModStartSetting = s[0]; m.ModStartScript = s[1]; }
                else if (k == "transfer" && v == "link") m.TransferLink = true;
                else if (k == "free") { if (!long.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out m.Free)) m.Free = -1; }
                else if (k == "registry") m.Registry = v;
                else if (k == "option")
                {
                    var o = v.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (o.Length < 2 || !Regex.IsMatch(o[0], "^[a-z0-9][a-z0-9-]{0,31}$")) { m.Problem = "an unexpected registry option"; return m; }
                    long n = 0; if (o.Length > 3) long.TryParse(o[3], NumberStyles.None, CultureInfo.InvariantCulture, out n);
                    m.Options[o[0]] = (o[1], o.Length > 2 && Hex.IsHex(o[2], 64) ? o[2] : null, n);
                }
                else if (k == "waiting") { var s = v.Split(' '); if (s.Length != 2 || !Name.IsMatch(s[0]) || !(Hex.IsHex(s[1], 64) || s[1] == "other")) { m.Problem = "an unexpected file in hakchi's transfer folder"; return m; } m.Waiting[s[0]] = s[1]; }
                else if (k == "home" && v == "link") m.HomeLink = true;
                else if (k == "home_pointer") { if (!(v == "absent" || v == "other" || HomePackage.CopyRe.IsMatch(v))) { m.Problem = "an unexpected HOME pointer"; return m; } m.HomePointer = v; }
                else if (k == "home_copy") { var s = v.Split(' '); if (s.Length != 3 || !HomePackage.CopyRe.IsMatch(s[0]) || !(Hex.IsHex(s[1], 64) || s[1] == "-") || s[2] != "dir" && s[2] != "nodir") { m.Problem = "an unexpected HOME record"; return m; } m.HomeCopies[s[0]] = (s[1] == "-" ? null : s[1], s[2] == "dir"); }
                else if (k == "home_attempt") { var s = v.Split(' '); if (s.Length != 2 || !Hex.IsHex(s[0], 8) || !(HomePackage.CopyRe.IsMatch(s[1]) || s[1] == "-")) { m.Problem = "an unexpected HOME installation record"; return m; } m.HomeAttempts[s[0]] = s[1]; }
                else if (k == "home_identity") { if (v == "absent") m.HomeIdentity = null; else try { m.HomeIdentity = Convert.FromBase64String(v); } catch (FormatException) { m.Problem = "an unreadable saves identity"; return m; } }
                else if (k == "ram") { if (!long.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out m.Ram)) m.Ram = -1; }
            }
            if (m.Hakchi == null || m.Policy == null || m.Registry == null || m.HomePointer == null && !m.HomeLink) { m.Problem = "the console's answer was incomplete"; return m; }
            m.Ok = true; return m;
        }
    }

    public sealed class MethodPlan
    {
        public string Op, Method, From, Hwid, ConsoleTag, Session, Digest, Nonce, Refusal, Text; public DateTime ExpiresUtc;
        /// <summary>a change to CCS Mod: CCS HOME at power-on, the owner's choice ("on" or "off"); null otherwise</summary>
        public string ModStart;
        public BoundArchive ProtectiveArchive; public MethodState Before;
        public string Package, PackageSha256, ListPackage, ListSha256;
        /// <summary>a change: CCS HOME's package in the new method's layout (its leave item names where it goes back to), sent after the
        /// method's two files; null when that exact package is already in use</summary>
        public HomeArchive Home; public string HomePrior;
        public string ProtectivePath => ProtectiveArchive?.Path;
        public List<string> Steps = new List<string>(), Notes = new List<string>();
        public JObj ToJson() => new JObj
        {
            { "operation", Op }, { "method", Method }, { "from", From }, { "console_tag", ConsoleTag }, { "text", Text }, { "steps", Steps }, { "notes", Notes }, { "refusal", Refusal }, { "mod_start", ModStart },
            { "package", Package == null ? null : new JObj { { "name", Package }, { "sha256", PackageSha256 } } }, { "removal_list", ListPackage == null ? null : new JObj { { "names", ListPackage }, { "sha256", ListSha256 } } },
            { "home", Home == null ? null : Home.ToJson().Set("replaces", HomePrior) },
            { "console_state", Before?.ToJson() }, { "protective_backup", ProtectivePath }, { "evidence", new List<object> { ProtectiveArchive?.ToJson() } },
            { "digest", Digest }, { "nonce", Nonce }, { "expires_utc", ExpiresUtc.ToString("o", CultureInfo.InvariantCulture) },
        };
    }

    public sealed class MethodOutcome { public string State, Problem, RecordFile; public List<string> Done = new List<string>(); public MethodPlan Plan; public bool Ok => State == "scheduled"; }

    public static class MethodEngine
    {
        public const string Subject = "ccs-method";
        public static string StepLabel(string s, MethodPlan p) => StepLabel(s, p.Op, p.Method, p.From);
        /// <summary>the same words from the plan as the window receives it (operation, method, from)</summary>
        public static string StepLabel(string s, string op, string method, string from) => s == "method" ? Methods.Label(method) + "'s package placed for hakchi to install at the next start" : s == "mstart" ? Preparation.StepLabel(s) : HomePackage.Steps.Contains(s) ? HomePackage.StepLabel(s) : Methods.Label(op == "remove" ? method : from) + "'s removal list placed for hakchi at the next start";

        /// <summary>the method steps' expected answers: "ok SHA" or "ok already SHA" (the same bytes were already waiting)</summary>
        internal static bool Answer(WriteResult r, string step, string sha) => r.Ok && Regex.IsMatch(r.Line ?? "", "^result " + step + " ok( already)? " + sha + "$");

        /// <summary>what already waits in the transfer folder may be only this plan's own files (an interrupted earlier run): null, or the reason</summary>
        internal static string WaitingProblem(MethodState s, IDictionary<string, string> mine)
        {
            if (s.TransferLink) return "hakchi's transfer folder is a link";
            var other = s.Waiting.Where(w => !mine.TryGetValue(w.Key, out var sha) || sha != w.Value).Select(w => w.Key).ToList();
            return other.Count == 0 ? null : "something else already waits in hakchi's transfer folder for the next start (" + string.Join(", ", other) + ")";
        }

        /// <summary>Phases 1 to 3 (no write to the console; the protective backup on the PC). op: "remove" (method: the one to remove) or
        /// "change" (method: the one to change to; home: its CCS HOME package, null: the one this build carries for its layout). modStart:
        /// a change to CCS Mod only, CCS HOME at power-on (the owner's explicit choice; off by default).</summary>
        public static (MethodPlan plan, MethodOutcome refused) Prepare(RecoveryConsole con, EngineContext ctx, string op, string method, HomePackage home = null, bool modStart = false)
        {
            var o = new MethodOutcome();
            MethodOutcome Refuse(string why) { o.State = "refused"; o.Problem = why; return o; }
            if (con.Profile != SessionProfile.Prepare) return (null, Refuse("The console is not in a preparation session."));
            if (op != "remove" && op != "change" || !Methods.IsPolicy(method)) return (null, Refuse("Choose CCS Mod or CCS Nucleus, and whether to remove it or change to it."));
            if (modStart && !(op == "change" && method == "mod")) return (null, Refuse("CCS HOME at power-on is chosen with CCS Mod only (CCS Nucleus's start chooser decides what starts)."));
            ConsoleIdentity id;
            try { id = con.Identify(); Support.CheckForBackup(id, ctx.RecoveryImageId); }
            catch (ConsoleRefusedException ex) { return (null, Refuse(ex.Message)); }
            var st = con.PrepState();
            if (!st.Ok) return (null, Refuse("The console's hakchi packages could not be read (" + st.Problem + "). Nothing was changed."));
            if (st.Hakchi != Preparation.PinnedHakchiVersion) return (null, Refuse("This console does not run Hakchi2 CE 3.9.3's hakchi (" + (st.Hakchi == "none" ? "no hakchi files on its data storage" : "hakchi " + st.Hakchi) + "). CCS Mod and CCS Nucleus are only changed on a console prepared with it."));
            var p = new MethodPlan { Op = op, Method = method, Hwid = id.Hwid, ConsoleTag = id.ShortTag, Session = con.SessionId, Before = st };
            var mine = new Dictionary<string, string>(StringComparer.Ordinal);
            if (op == "remove")
            {
                p.ListPackage = Methods.Package(method);
                if (!st.Installed(p.ListPackage)) return (null, Refuse(Methods.Label(method) + " is not installed on this console (hakchi does not list it). Nothing to remove."));
                p.ListSha256 = Methods.ListSha256(p.ListPackage); mine["uninstall"] = p.ListSha256; p.Steps.Add("unlist");
                p.Text = "Remove " + Methods.Label(method) + ": hakchi runs its own removal at the console's next start. Only its unchanged files are removed; its settings, CCS HOME and every save stay.";
            }
            else
            {
                p.From = Methods.Other(method); p.Package = Methods.Package(method); p.ListPackage = Methods.Package(p.From);
                if (st.Installed(p.Package)) return (null, Refuse(Methods.Label(method) + " is already installed on this console."));
                if (!st.Installed(p.ListPackage)) return (null, Refuse(Methods.Label(p.From) + " is not installed on this console, so there is nothing to change from. Preparing the console installs " + Methods.Label(method) + "."));
                p.PackageSha256 = Methods.Sha256(p.Package);
                if (!Hex.IsHex(p.PackageSha256, 64)) return (null, Refuse("This build does not carry " + Methods.Label(method) + "'s package."));
                p.ListSha256 = Methods.ListSha256(p.ListPackage);
                mine[Methods.Waiting(p.Package)] = p.PackageSha256; mine["uninstall"] = p.ListSha256; p.Steps.Add("method"); p.Steps.Add("unlist");
                p.Text = "Change from " + Methods.Label(p.From) + " to " + Methods.Label(method) + ": at the console's next start hakchi removes " + Methods.Label(p.From) + " (only its unchanged files) and installs " + Methods.Label(method) + ".";
                if (method == "mod")
                {   // CCS HOME at power-on: recorded first as CCS Mod's own setting, which its install applies (its own autostart command)
                    p.ModStart = modStart ? "on" : "off";
                    if (modStart && st.ModStartScript == "other") return (null, Refuse("A start script named S81ccs-mod that CCS Mod did not write is on the console; it is kept, so CCS HOME at power-on cannot be turned on. Nothing was changed."));
                    if (Methods.NeedsStartSetting(st, p.ModStart)) p.Steps.Insert(0, "mstart");
                    p.Notes.Add(Methods.StartText(p.ModStart));
                }
                // CCS HOME in the new method's layout, after the method's two files (an interruption before it leaves the HOME as it was: it
                // still starts; finishing the change sends it)
                HomeSteps.Decision hd;
                try { hd = HomeSteps.Plan(home ?? HomePackage.Embedded(method), st, id.Hwid); }
                catch (HomePackageException ex) { hd = new HomeSteps.Decision { Refusal = "CCS HOME's package cannot be used: " + ex.Message }; }
                if (hd.Refusal != null) return (null, Refuse(hd.Refusal));
                if (hd.Steps.Count > 0) { p.Home = hd.Archive; p.HomePrior = hd.Prior; p.Steps.AddRange(hd.Steps); }
                if (hd.Text != null) p.Notes.Add(hd.Text);
            }
            var wp = WaitingProblem(st, mine);
            if (wp != null) return (null, Refuse("Changing the method is refused: " + wp + ". Start the console normally once so hakchi applies it, then check again."));
            ctx.Say("protect", "Backing up the console as it is now, before anything changes...");
            var prot = CaptureEngine.Run(con, ctx);
            if (!prot.Ok || prot.Verification == null || !prot.Verification.Ok) { o.State = "stopped"; o.Problem = "The backup of the console as it is now did not complete (" + (prot.Problem ?? prot.State) + "), so nothing was written."; return (null, o); }
            if (!ConsoleIdentity.SameConsole(prot.Verification.Hwid, id.Hwid) || prot.Verification.Simulated != con.Simulated || !prot.Verification.RestoreReady) return (null, Refuse("The backup made just now is not a complete backup of this console, so nothing was written."));
            p.ProtectiveArchive = BoundArchive.From("backup made just now, before changing the method", prot.PrimaryPath, prot.Verification, con.SessionId);
            p.Notes.Add("Only files on the console's data storage are written: hakchi's transfer folder" + (p.Home != null ? " and CCS HOME's folder" : "") + ". No kernel, bootloader or system is changed.");
            p.Notes.Add("Nothing changes until the console starts normally: then hakchi applies it and restarts the console once.");
            if (op == "change") p.Notes.Add("Games, saves, settings, other packages and system images are kept. " + Methods.Label(p.From) + "'s settings stay on the console as data.");
            p.Notes.Add("The backup just made is checked again when you confirm and stays locked until the change ends; if it is missing or changed, nothing is written.");
            p.Nonce = Hex.Of(Guid.NewGuid().ToByteArray()); p.ExpiresUtc = DateTime.UtcNow + RestoreEngine.ConfirmWindow;
            p.Digest = DigestOf(p);
            return (p, null);
        }

        static string DigestOf(MethodPlan p)
        {
            using (var h = SHA256.Create())
                return Hex.Of(h.ComputeHash(Encoding.UTF8.GetBytes("ccs-method-plan/3\n" + ConsoleIdentity.NormalizeHwid(p.Hwid) + "\n" + p.Session + "\n" + p.Op + " " + p.Method + " " + p.From + "\n" + p.Package + " " + p.PackageSha256 + "\n" +
                    p.ListPackage + " " + p.ListSha256 + "\n" + p.Before?.Digest() + "\n" + p.ProtectiveArchive?.DigestLine() + "\n" + string.Join(",", p.Steps) + "\n" + (p.Home?.DigestLine() ?? "-") + " " + (p.HomePrior ?? "-") + "\nmodstart=" + (p.ModStart ?? "-") + "\n" + p.Nonce)));
        }

        /// <summary>Phases 4 to 6: only with the plan's own digest and nonce, in its session, before it expires, once. Every check that can
        /// refuse runs before the confirmation is used up and before the first write.</summary>
        public static MethodOutcome Execute(RecoveryConsole con, MethodPlan plan, string digest, string nonce, EngineContext ctx)
        {
            var o = new MethodOutcome { Plan = plan };
            MethodOutcome Refuse(string why) { o.State = "refused"; o.Problem = why; return o; }
            if (plan == null || plan.Refusal != null) return Refuse(plan?.Refusal ?? "No plan.");
            if (digest != plan.Digest || nonce != plan.Nonce || plan.Nonce == null || DigestOf(plan) != plan.Digest) return Refuse("The confirmation does not match the reviewed change.");
            if (DateTime.UtcNow > plan.ExpiresUtc) return Refuse("The confirmation is more than 10 minutes old. Review the change again.");
            if (con.SessionId != plan.Session || plan.ProtectiveArchive?.Session != plan.Session) return Refuse("The console was connected again after the review. Review the change again. Nothing was changed.");
            var id = con.Identify();
            if (!ConsoleIdentity.SameConsole(id.Hwid, plan.Hwid)) return Refuse("A different console is connected now. Nothing was changed.");
            using (var hold = Evidence.Hold.Open(plan.ProtectivePath))
            {
                if (hold.Problem != null) return Refuse("The backup the change relies on cannot be used (" + hold.Problem + "). Nothing was changed; review the change again.");
                ctx.Say("validate", "Checking the backup again...");
                var why = Evidence.Recheck(plan.ProtectiveArchive, plan.Hwid, con.Simulated, con.SessionId, ctx.Cancelled);
                if (why != null) return Refuse(why + " Nothing was changed; review the change again.");
                var now = con.PrepState();
                if (!now.Ok || now.Digest() != plan.Before.Digest()) return Refuse("The console's hakchi packages or its transfer folder changed since the review" + (now.Ok ? "" : " (" + now.Problem + ")") + ". Nothing was changed; review the change again.");
                Record rec;
                try { rec = Record.Start(ctx.Settings.RecordsFolder, Guid.NewGuid().ToString("N").Substring(0, 12), "method", new JObj { { "plan", plan.ToJson() }, { "console_tag", plan.ConsoleTag }, { "simulated", con.Simulated } }); }
                catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException) { return Refuse("The change's record could not be saved in " + ctx.Settings.RecordsFolder + " (" + ex.Message + "), so nothing was written."); }
                plan.Nonce = null;   // used up only now
                o.RecordFile = rec.File; var phases = new List<object>();
                var auth = new WriteAuthority(plan.Digest, plan.Hwid, plan.Steps.ToArray(), () => con.Simulated || Gate.MethodWrites, Gate.MethodText);
                rec.State("writing"); con.Card("writing");
                foreach (var s in plan.Steps)
                {
                    if (ctx.Cancelled()) { rec.State("stopped", "canceled between steps"); o.State = "stopped"; o.Problem = "Canceled. Steps done: " + (o.Done.Count == 0 ? "none" : string.Join("; ", o.Done.Select(x => StepLabel(x, plan)))) + ". Starting the console again runs this connection's steps once more and finishes it."; return o; }
                    ctx.Say("method", StepLabel(s, plan) + "... (do not unplug the console)");
                    phases.Add(new JObj { { "step", s }, { "state", "began" }, { "at", Record.Now() } }); rec.Set("phases", phases).Save();
                    WriteResult r;
                    try { r = s == "method" ? con.PrepStep("method", auth, new[] { plan.Package, plan.PackageSha256 }) : s == "unlist" ? con.PrepStep("unlist", auth, new[] { plan.ListPackage }) : s == "mstart" ? con.PrepStep("mstart", auth, new[] { plan.ModStart }) : HomeSteps.Run(con, auth, s, plan.Home, plan.HomePrior); }
                    catch (GateClosedException ex) { rec.State("refused", ex.Message); o.State = "refused"; o.Problem = ex.Message; return o; }
                    var ok = HomePackage.Steps.Contains(s) ? HomeSteps.Answer(r, s, plan.Home) : s == "mstart" ? Methods.StartAnswer(r, plan.ModStart) : Answer(r, s, s == "method" ? plan.PackageSha256 : plan.ListSha256);
                    phases.Add(new JObj { { "step", s }, { "state", ok ? (r.Line.Contains(" ok already ") ? "already-waiting" : "done-and-read-back") : "failed" }, { "detail", r.Line ?? r.Problem }, { "at", Record.Now() } }); rec.Set("phases", phases).Save();
                    ctx.At("method-" + s);
                    if (!ok)
                    {
                        auth.Revoke(s + ": " + (r.Problem ?? r.Line)); rec.State("stopped", r.Problem ?? r.Line); con.Card("stopped"); o.State = "stopped";
                        o.Problem = "Changing the method stopped at: " + StepLabel(s, plan) + " (" + (r.Problem ?? r.Line) + "). Nothing more was written and nothing was retried. Steps done before: " + (o.Done.Count == 0 ? "none" : string.Join("; ", o.Done.Select(x => StepLabel(x, plan)))) + ". The backup made just now is unchanged.";
                        return o;
                    }
                    o.Done.Add(s);
                }
                var after = con.PrepState();
                bool waits = after.Ok && (plan.Package == null || after.Waiting.TryGetValue(Methods.Waiting(plan.Package), out var ps) && ps == plan.PackageSha256) && after.Waiting.TryGetValue("uninstall", out var ls) && ls == plan.ListSha256
                             && WaitingProblem(after, new Dictionary<string, string> { { "uninstall", plan.ListSha256 }, { Methods.Waiting(plan.Package ?? "-"), plan.PackageSha256 } }) == null;
                if (!waits) { rec.State("stopped", "the transfer folder does not read back as planned"); o.State = "stopped"; o.Problem = "Every step reported success, but hakchi's transfer folder does not read back as planned. Its state is not verified; nothing more was changed."; return o; }
                if (plan.Home != null && !HomeSteps.InUse(after, plan.Home)) { rec.State("stopped", "CCS HOME's package does not read back in use"); o.State = "stopped"; o.Problem = "Every step reported success, but the console does not read back CCS HOME's new package as the one in use. Its state is not verified; nothing more was changed."; return o; }
                if (plan.Steps.Contains("mstart") && after.ModStartSetting != plan.ModStart) { rec.State("stopped", "CCS Mod's start setting does not read back"); o.State = "stopped"; o.Problem = "Every step reported success, but CCS Mod's start setting does not read back as chosen. Its state is not verified; nothing more was changed."; return o; }
                rec.Set("after", new JObj { { "console_state", after.ToJson() }, { "applies", "at the console's next normal start (hakchi's p7080_hmods)" }, { "mod_start", plan.ModStart }, { "home", plan.Home == null ? null : plan.Home.ToJson().Set("replaced", plan.HomePrior).Set("state", "in use") } });
                rec.State("scheduled"); con.Card("done"); o.State = "scheduled"; return o;
            }
        }
    }
}
