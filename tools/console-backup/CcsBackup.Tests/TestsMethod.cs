// Classic Cart Studio console backup and restore engine tests: CCS Mod and CCS Nucleus as independent add-ons placed through hakchi's
// transfer folder (Prepare Console adds the missing ones; Console Restore adds, removes and changes them and sets what opens at power-on),
// in the engine and through the worker's add-on requests. GPL-3.0-or-later. Simulated consoles only (files on this PC); the real-console
// boundaries (Gate.PreparationWrites, Gate.MethodWrites) stay closed. SimulatedConsole.NormalStart stands in for the console's next normal
// start (hakchi's p7080_hmods: the removal list in its order, then transfer/*.hmod, then transfer/hmod/*.hmod);
// tools/console-nucleus/test_nucleus.py runs hakchi's own p7080 with the real packages under qemu-arm.
using CcsBackup.Archive;
using CcsBackup.Device;
using CcsBackup.Engine;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using static CcsBackup.Tests.Program;

namespace CcsBackup.Tests
{
    static partial class Tests
    {
        static string Transfer(string c) => Path.Combine(c, "hakchi-transfer");
        /// <summary>what waits in hakchi's transfer folder: its own files (sorted), then those of its hmod/ subfolder as "hmod/NAME"</summary>
        static List<string> Waiting(string c)
        {
            if (!Directory.Exists(Transfer(c))) return new List<string>();
            var top = Directory.GetFiles(Transfer(c)).Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal);
            var hm = Path.Combine(Transfer(c), "hmod");
            var sub = Directory.Exists(hm) ? Directory.GetFiles(hm).Select(f => "hmod/" + Path.GetFileName(f)).OrderBy(x => x, StringComparer.Ordinal) : Enumerable.Empty<string>();
            return top.Concat(sub).ToList();
        }
        /// <summary>the hakchi packages installed on a simulated console (hakchi-packages.json), in the order it keeps them</summary>
        static List<string> Installed(string c)
        {
            var p = Path.Combine(c, "hakchi-packages.json"); if (!File.Exists(p)) return new List<string>();
            return (Json.Parse(File.ReadAllText(p)).A("packages") ?? new List<object>()).OfType<string>().ToList();
        }
        static void SetInstalled(string c, params string[] packages) => File.WriteAllText(Path.Combine(c, "hakchi-packages.json"), Json.Write(new JObj { { "packages", packages.Cast<object>().ToList() } }));
        /// <summary>CCS Nucleus's start setting on a simulated console (nucleus-start.txt; "none" without it)</summary>
        static string StartIn(string c) { var f = Path.Combine(c, "nucleus-start.txt"); return File.Exists(f) ? File.ReadAllText(f).Trim() : "none"; }
        static void SetStart(string c, string line) => File.WriteAllText(Path.Combine(c, "nucleus-start.txt"), line + "\n");
        /// <summary>CCS Nucleus's registry with one of the owner's own system images beside the console's own choices</summary>
        static void SetOwnImage(string c) => File.WriteAllText(Path.Combine(c, "nucleus-registry.json"), Json.Write(new JObj { { "options", new JObj {
            { "ccs-home", new JObj { { "kind", "ccs-home" } } }, { "my-snes", new JObj { { "kind", "owner-root" }, { "sha256", new string('a', 64) }, { "bytes", 1000L } } }, { "native", new JObj { { "kind", "native" } } } } } }));
        static bool OwnImageKept(string c) { var p = Path.Combine(c, "nucleus-registry.json"); return File.Exists(p) && Json.Parse(File.ReadAllText(p)).O("options")?.O("my-snes")?.S("kind") == "owner-root"; }
        static AddonChoice Choice(bool mod, string nucleus = "none", string start = "native", string auto = "menu") => new AddonChoice { Mod = mod, Nucleus = nucleus, Start = start, Auto = auto };
        static List<string> Sorted(IEnumerable<string> x) => x.OrderBy(v => v, StringComparer.Ordinal).ToList();
        static string Listed(IEnumerable<string> x) => x == null ? "none" : string.Join(", ", x);
        static string Flat(string t) => t?.Replace("\n", " / ");
        static RecoveryConsole OpenMethod(string c) => RecoverySessions.OpenSimulated(c, ImageKind.Method);
        static T Fn<T>(Func<T> f) => f();

        /// <summary>disk: each simulated console and each capture of it is about 1 GB; what a check no longer needs is deleted at once</summary>
        static void Gone(params string[] files) { foreach (var f in files) if (f != null && File.Exists(f)) File.Delete(f); }
        static void GoneConsole(string c) { try { Directory.Delete(Path.GetDirectoryName(c), true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        static void GoneBackups(Settings s) { foreach (var d in new[] { s.PrimaryFolder, s.ArchiveFolder }) if (d != null && Directory.Exists(d)) foreach (var f in Directory.GetFiles(d)) File.Delete(f); }
        /// <summary>how many files the backup folder holds (a check compares it before and after: no backup was made)</summary>
        static int BackupFiles(Settings s) => Directory.Exists(s.PrimaryFolder) ? Directory.GetFiles(s.PrimaryFolder).Length : 0;
        /// <summary>a smaller simulated console (its data storage the 64 MB "modern" fixture), for the cases that do not read hakchi's files
        /// from a backup; hakchi: the preparation's marker of hakchi's files on its data storage (what ccsprep mstate reports)</summary>
        static string SmallConsole(string kind, bool hakchi)
        {
            var lay = Layout.Select(x => x.dev == "nandc" ? (x.dev, x.label, 65536L * 1024) : x).ToArray();
            var c = Console_(kind, HwidA, Path.Combine(Fx, "data-modern.img"), lay);
            if (hakchi) File.WriteAllText(Path.Combine(c, "prepared-files.txt"), "hakchi v1.0.4-126 files installed (simulated)\n");
            return c;
        }

        /// <summary>a preparation plan with these add-ons (null: hakchi only); home: CCS HOME's package for CCS Mod (null: the one this build carries)</summary>
        static (PrepPlan plan, PrepOutcome refused, RecoveryConsole con, EngineContext ctx, AdmissionResult adm) PrepPlanWith(string console, Settings s, AddonChoice addons, HomePackage home = null)
        {
            var con = RecoverySessions.OpenSimulated(console, ImageKind.Prepare); var ctx = Ctx(s, ImageKind.Prepare);
            var id = con.Identify();
            var adm = Admission.Admit(s, id.Hwid, "prepare", Subj, null, true, con.SessionId);
            if (!adm.Ok) return (null, new PrepOutcome { State = "refused", Problem = adm.Code + ": " + adm.Text }, con, ctx, adm);
            var (p, r) = Preparation.Prepare(con, ctx, adm, addons, home);
            return (p, r, con, ctx, adm);
        }
        static string HomeIn(string c) => File.Exists(Path.Combine(c, "ccs-data", "pkg", "current")) ? File.ReadAllText(Path.Combine(c, "ccs-data", "pkg", "current")).Trim() : null;

        /// <summary>the engine's own digest of a Console Restore plan, computed again after a test changed the plan's expiry time</summary>
        static string Resign(MethodPlan p) => typeof(MethodEngine).GetMethod("DigestOf", BindingFlags.NonPublic | BindingFlags.Static)?.Invoke(null, new object[] { p }) as string;

        /// <summary>one part of a group: an exception in it is a failed check (with where it came from) and the group's later parts still run;
        /// a skip (no genuine stock kernel given) still skips the group</summary>
        static void Part(string what, Action run)
        {
            try { run(); }
            catch (SkipException) { throw; }
            catch (Exception ex) { Check(false, what + ": stopped with " + ex.GetType().Name + ": " + ex.Message + " " + (ex.StackTrace ?? "").Split('\n').FirstOrDefault()?.Trim()); }
        }

        /// <summary>the worker in this process, driven as both Workbench hosts drive it: the request line, then for each event the answer
        /// (null: none), until its result or error; another program holding the console lock is waited for ("busy")</summary>
        static List<Dictionary<string, object>> WorkerTalk(JObj request, Settings s, Func<Dictionary<string, object>, JObj> answer = null)
        {
            for (int attempt = 0; ; attempt++)
            {
                var input = new QueueStream(); var output = new QueueStream(); var enc = new UTF8Encoding(false);
                var th = new System.Threading.Thread(() => { try { WorkerHost.RunOn(input, output, "tests", "CcsBackup.Tests", "test", s.Path); } finally { output.End(); } }) { IsBackground = true }; th.Start();
                void Line(JObj j) { var b = enc.GetBytes(WorkerHost.RequestLine(j) + "\n"); input.Write(b, 0, b.Length); }
                Line(request);
                var evs = new List<Dictionary<string, object>>(); string line; var rd = new StreamReader(output, enc);
                while ((line = rd.ReadLine()) != null)
                {
                    Dictionary<string, object> ev; try { ev = Json.Parse(line); } catch (FormatException) { continue; }
                    evs.Add(ev);
                    if (ev.S("event") == "result" || ev.S("event") == "error") break;
                    var a = answer?.Invoke(ev); if (a != null) Line(a);
                }
                input.End(); th.Join(120000);
                if (evs.LastOrDefault()?.S("state") != "busy" || attempt >= 90) return evs;
                System.Threading.Thread.Sleep(2000);
            }
        }
        static Dictionary<string, object> Ev(List<Dictionary<string, object>> evs, string name) => evs.FirstOrDefault(e => e.S("event") == name);
        static Dictionary<string, object> Last(List<Dictionary<string, object>> evs) => evs.LastOrDefault();
        static List<string> Strings(Dictionary<string, object> d, string k) => (d?.A(k) ?? new List<object>()).OfType<string>().ToList();
        static JObj Target(string cmd, AddonChoice a) => new JObj { { "cmd", cmd }, { "mod", a.Mod }, { "nucleus", a.Nucleus }, { "start", a.Start }, { "auto", a.Auto } };
        static JObj Confirm(string cmd, Dictionary<string, object> plan) => new JObj { { "cmd", cmd }, { "digest", plan?.S("digest") }, { "nonce", plan?.S("nonce") } };
        static bool SameChoice(Dictionary<string, object> j, AddonChoice a) => j != null && j.TryGetValue("mod", out var m) && m is bool mb && mb == a.Mod && j.S("nucleus") == a.Nucleus && j.S("start") == a.Start && j.S("auto") == a.Auto;

        /// <summary>CCS Nucleus, started directly, then CCS Mod with CCS HOME at power-on, then the chooser, added by Prepare Console to a
        /// console already prepared with the pinned hakchi (Preparation reads hakchi's files from the backup made in the session, so this
        /// group takes the full-size "modified" data fixture: about 3 GB of disk while it runs)</summary>
        public static void MethodAlready()
        {
            var s = SettingsIn(NewDir("method-already"), false);
            var c = Console_("modified"); Backup(c, s);

            Part("the worker's add-on pages on a prepared console", () =>
            {
                // ---- the worker: the console's add-ons reported first (situation hakchi), then the chosen add-ons planned; not confirmed
                int f0 = BackupFiles(s), filesAtState = -1, writesAtState = -1;
                var evs = WorkerTalk(new JObj { { "cmd", "prepare-plan" }, { "console", "simulated" }, { "folder", c }, { "choose", true } }, s, ev =>
                    ev.S("event") == "addon-state" ? Fn(() => { filesAtState = BackupFiles(s); writesAtState = Writes(c).Count; return Target("prepare-target", Choice(false, "direct")); })
                    : ev.S("event") == "prep-plan" ? new JObj { { "cmd", "quit" } } : null);
                var st = Ev(evs, "addon-state"); var plan = Ev(evs, "prep-plan")?.O("plan"); var res = Last(evs);
                Check(st != null && st.S("operation") == "prepare" && st.S("situation") == "hakchi" && SameChoice(st.O("current"), Choice(false)) && st.S("start") == "none" && st.S("start_text") == null
                      && Strings(st, "installed").Count == 0 && st.L("other_packages") == 0 && st.L("own_images") == 0 && !string.IsNullOrEmpty(st.S("console_tag")) && filesAtState == f0 && writesAtState == 0,
                      "worker, Prepare Console with the add-on pages on a console prepared with the pinned hakchi: it reports that situation and no add-ons yet, before any backup or write (" + Flat(st?.S("situation") ?? res?.S("problem") ?? res?.S("text")) + ")");
                Check(plan != null && Strings(plan, "steps").SequenceEqual(new[] { "method:ccs-nucleus", "nstart" }) && plan.S("state") == "already" && SameChoice(plan.O("addons"), Choice(false, "direct"))
                      && plan.S("start") == "native never off default 10" && res?.S("state") == "canceled" && Writes(c).Count == 0,
                      "the chosen add-on (CCS Nucleus, started directly) is planned after the backup: its package and its start; without a confirmation nothing is written (" + Listed(Strings(plan, "steps")) + "; " + res?.S("state") + ")");
                Gone(plan?.S("current_backup"));
            });

            Part("CCS Nucleus added to a prepared console", () =>
            {
                var (p, r, con, ctx, adm) = PrepPlanWith(c, s, Choice(false, "direct"));
                Check(p != null && p.State == "already" && p.Refusal == null && p.Steps.SequenceEqual(new[] { "method:ccs-nucleus", "nstart" }) && p.Add.SequenceEqual(new[] { "ccs-nucleus" }) && p.StartLine == "native never off default 10" && p.Home == null
                      && p.Text.Contains("CCS Nucleus added, without changing the kernel, bootloader or system") && p.Notes.Contains(Choice(false, "direct").StartText),
                      "already prepared, CCS Nucleus started directly: its package and its start (Original Menu straight away); no kernel, bootloader, system or CCS HOME step (" + Flat(p?.Text ?? r?.Problem) + ")");
                // the console's hakchi state changes after the review (something else placed in its transfer folder): refused, nothing written
                Directory.CreateDirectory(Transfer(c)); File.WriteAllText(Path.Combine(Transfer(c), "other.hmod"), "another program's package\n");
                var m = Preparation.Execute(con, p, p.Digest, p.Nonce, ctx, () => UsePrep(s, adm, con, p));
                Check(m.State == "refused" && m.Problem.Contains("changed since the review") && Writes(c).Count == 0 && p.Nonce != null && !File.Exists(Path.Combine(s.RecordsFolder, "admission-" + adm.TicketId + ".used")),
                      "the transfer folder changed after the review: refused before anything, neither the confirmation nor the admission used (" + m.Problem + ")");
                Gone(p.CurrentBackup);
                var (pw, _, _, _, _) = PrepPlanWith(c, s, Choice(false, "direct")); Gone(pw?.CurrentBackup);
                Check(pw != null && pw.Refusal != null && pw.Refusal.Contains("other.hmod") && pw.Steps.Count == 0, "with another package waiting there for the next start, adding an add-on is refused, with the reason (" + pw?.Refusal + ")");
                Directory.Delete(Transfer(c), true);
                (p, r, con, ctx, adm) = PrepPlanWith(c, s, Choice(false, "direct"));
                var o = Preparation.Execute(con, p, p.Digest, p.Nonce, ctx, () => UsePrep(s, adm, con, p));
                var rec = Record.Load(o.RecordFile);
                Check(o.State == "method-scheduled" && Writes(c).SequenceEqual(new[] { "method ccs-nucleus", "nstart native never off default 10" }) && Waiting(c).SequenceEqual(new[] { "ccs-nucleus.hmod" }) && StartIn(c) == "native never off default 10"
                      && rec.S("state") == "method-scheduled" && rec.O("after")?.O("method")?.S("start") == "native never off default 10" && rec.O("after")?.O("home") == null && rec.O("after")?.S("parttable") == null,
                      "only CCS Nucleus's package and its start setting were written (no kernel, bootloader or table); the package waits for the next start; the record is not a preparation record (" + o.Problem + "; " + Listed(Writes(c)) + ")");
                Gone(p.CurrentBackup);
                var st = SimulatedConsole.NormalStart(c);
                Check(st.SequenceEqual(new[] { "install ccs-nucleus", "transfer folder removed; restart" }) && Installed(c).SequenceEqual(new[] { "ccs-nucleus" }) && StartIn(c) == "native never off default 10",
                      "the next normal start installs CCS Nucleus, which keeps the start setting already made (" + Listed(st) + ")");
                var (p2, _, _, _, _) = PrepPlanWith(c, s, Choice(false, "direct")); Gone(p2?.CurrentBackup);
                Check(p2 != null && p2.State == "already" && p2.Refusal == null && p2.Steps.Count == 0 && p2.Text.Contains("nothing needs to be installed again"), "preparing again with the same add-ons: nothing to do (" + Flat(p2?.Text) + ")");
                var (px, _, _, _, _) = PrepPlanWith(c, s, Choice(true)); Gone(px?.CurrentBackup);
                Check(px != null && px.Refusal != null && px.Refusal.Contains("Changing or removing add-ons is in Console Restore") && px.Steps.Count == 0 && Writes(c).Count == 2,
                      "CCS Nucleus is installed and a choice without it is made: refused here, nothing written; removing an add-on is Console Restore's (" + px?.Refusal + ")");
            });

            var home = HomePackage.Folder(MakeHome("mod", "a1"), "mod");
            Part("CCS Mod with CCS HOME at power-on added to a prepared console", () =>
            {
                var n0 = Writes(c).Count;
                var (p, r, con, ctx, adm) = PrepPlanWith(c, s, Choice(true, "direct", "ccs-home"), home);
                Check(p != null && p.State == "already" && p.Refusal == null && p.Steps.SequenceEqual(new[] { "method:ccs-mod", "nstart", "hput", "huse" }) && p.Add.SequenceEqual(new[] { "ccs-mod" }) && p.StartLine == "ccs-home never on default 10"
                      && p.Home != null && p.Home.Layout == "mod" && p.HomePrior == "absent" && p.Text.Contains("CCS Mod added"),
                      "already prepared with CCS Nucleus, CCS Mod and CCS HOME at power-on chosen: CCS Mod's package, CCS Nucleus's start, then CCS HOME's package (sent, then switched to) (" + Listed(p?.Steps) + "; " + Flat(p?.Text ?? r?.Problem) + ")");
                var o = Preparation.Execute(con, p, p.Digest, p.Nonce, ctx, () => UsePrep(s, adm, con, p));
                Check(o.State == "method-scheduled" && Writes(c).Skip(n0).SequenceEqual(new[] { "method ccs-mod", "nstart ccs-home never on default 10", "hput " + p.Home.Copy, "huse " + p.Home.Copy }) && HomeIn(c) == p.Home.Copy
                      && StartIn(c) == "ccs-home never on default 10" && Record.Load(o.RecordFile).O("after")?.O("home")?.S("copy") == p.Home.Copy,
                      "written in that order and read back: CCS HOME is in use at once, CCS Mod waits for the next start, CCS Nucleus opens CCS HOME (" + o.Problem + "; " + Listed(Writes(c).Skip(n0)) + ")");
                Gone(p.CurrentBackup);
                var st = SimulatedConsole.NormalStart(c);
                Check(st.SequenceEqual(new[] { "install ccs-mod", "transfer folder removed; restart" }) && Sorted(Installed(c)).SequenceEqual(new[] { "ccs-mod", "ccs-nucleus" }) && StartIn(c) == "ccs-home never on default 10",
                      "the next normal start installs CCS Mod beside CCS Nucleus; the start stays CCS HOME (" + Listed(st) + ")");
                var (p3, _, _, _, _) = PrepPlanWith(c, s, Choice(true, "direct", "ccs-home"), home); Gone(p3?.CurrentBackup);
                Check(p3 != null && p3.State == "already" && p3.Refusal == null && p3.Steps.Count == 0 && p3.Notes.Any(x => x.Contains("already this package")), "the same add-ons and the same CCS HOME package again: nothing to do");
                // the HOME package's pointer removed from the console by someone: the add-ons stay, only CCS HOME is placed again
                File.Delete(Path.Combine(c, "ccs-data", "pkg", "current"));
                var (ph, rh, conh, ctxh, admh) = PrepPlanWith(c, s, Choice(true, "direct", "ccs-home"), home);
                var oh = ph == null ? null : Preparation.Execute(conh, ph, ph.Digest, ph.Nonce, ctxh, () => UsePrep(s, admh, conh, ph)); Gone(ph?.CurrentBackup);
                Check(ph != null && ph.Steps.SequenceEqual(new[] { "hput", "huse" }) && ph.Text.Contains("only CCS HOME's package") && oh?.State == "home-placed" && HomeIn(c) != null && Record.Load(oh.RecordFile).S("state") == "home-placed",
                      "CCS Mod installed but no HOME in use: only CCS HOME's package is placed (sent again under a new attempt, never written over) (" + Flat(ph?.Text ?? rh?.Problem) + "; " + oh?.State + ")");
            });

            Part("the chooser added to CCS Nucleus on a prepared console", () =>
            {
                var (p, r, con, ctx, adm) = PrepPlanWith(c, s, Choice(true, "chooser", "ccs-home", "last"), home);
                Check(p != null && p.State == "already" && p.Refusal == null && p.Steps.SequenceEqual(new[] { "method:ccs-nucleus-chooser", "nstart" }) && p.StartLine == "ccs-home always on last 10",
                      "already prepared with CCS Mod and CCS Nucleus, the chooser chosen (the menu used last after its countdown): the chooser's package and CCS Nucleus's start; CCS HOME is already in use ("
                      + (p != null ? Listed(p.Steps) : Flat(r?.Problem)) + ")");
                if (p == null || p.Refusal != null || p.Steps.Count == 0) { Gone(p?.CurrentBackup); return; }   // the check above failed with the reason
                var o = Preparation.Execute(con, p, p.Digest, p.Nonce, ctx, () => UsePrep(s, adm, con, p)); Gone(p.CurrentBackup);
                Check(o.State == "method-scheduled" && Waiting(c).SequenceEqual(new[] { "hmod/ccs-nucleus-chooser.hmod" }) && StartIn(c) == "ccs-home always on last 10",
                      "the chooser's package waits in the transfer folder's hmod/ subfolder; the start reads back (" + o.Problem + ")");
                var st = SimulatedConsole.NormalStart(c);
                Check(st.SequenceEqual(new[] { "install ccs-nucleus-chooser", "transfer folder removed; restart" }) && Sorted(Installed(c)).SequenceEqual(new[] { "ccs-mod", "ccs-nucleus", "ccs-nucleus-chooser" }),
                      "the next normal start installs the chooser (CCS Nucleus is there) (" + Listed(st) + ")");
            });

            Part("earlier CCS HOME versions: one kept by default, none, a changed copy kept, a failure stops the clean-up; dates, Original Menu's entries, a change after the review, the record", () =>
            {
                string Pkg(string x) => Path.Combine(c, "ccs-data", "pkg", x);
                List<string> Copies() => Directory.GetDirectories(Path.Combine(c, "ccs-data", "pkg")).Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal).ToList();
                var save = Path.Combine(c, "ccs-data", "data", "saves", "x", "slot1.ccsstate"); Directory.CreateDirectory(Path.GetDirectoryName(save)); File.WriteAllText(save, "my slot");
                var choice = Choice(true, "chooser", "ccs-home", "last");
                // a package whose MANIFEST.json is written as the release pipeline writes it (one space per level), which the console reads line by line
                HomePackage HomeOf(string tag)
                {
                    var d = MakeHome("mod", tag); var mf = Path.Combine(d, "MANIFEST.json");
                    File.WriteAllText(mf, System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(mf), "(?m)^( +)", m => new string(' ', m.Groups[1].Length / 2)));
                    return HomePackage.Folder(d, "mod");
                }
                (PrepPlan p, PrepOutcome o) Place(string tag)
                {
                    var (pp, rr, cc, cx, ad) = PrepPlanWith(c, s, choice, HomeOf(tag));
                    var oo = pp == null || pp.Refusal != null ? null : Preparation.Execute(cc, pp, pp.Digest, pp.Nonce, cx, () => UsePrep(s, ad, cc, pp)); Gone(pp?.CurrentBackup);
                    return (pp, oo ?? rr);
                }
                // the default (one earlier version): the copy in use before stays, every other earlier copy of this program's goes
                var before = Copies(); var prior = HomeIn(c); s.EarlierHomeVersions = null;
                var (p1, o1) = Place("e1"); var n1 = Copies();
                var drop1 = before.Where(x => x != prior).ToList();
                Check(p1 != null && o1?.State == "home-placed" && p1.Steps.SequenceEqual(new[] { "hput", "huse" }.Concat(drop1.Select(x => "hdrop:" + x))) && n1.Contains(prior) && drop1.All(x => !n1.Contains(x))
                      && n1.Contains(p1.Home.Copy) && HomeIn(c) == p1.Home.Copy && p1.Notes.Any(x => x.StartsWith("Earlier HOME versions: this console keeps one", StringComparison.Ordinal)),
                      "the default keeps one earlier version: the copy in use before stays, the older ones go after the switch (" + Listed(p1?.Steps) + "; kept " + Listed(n1) + "; " + o1?.Problem + ")");
                var rec1 = o1?.RecordFile == null ? null : Record.Load(o1.RecordFile);
                Check(rec1 != null && File.ReadAllText(save) == "my slot", "the saves are untouched by the clean-up");
                var vr1 = p1 == null ? null : Json.Parse(File.ReadAllText(Path.Combine(c, "ccs-data", "install", "v-" + p1.Home.Copy + ".json")));
                // the copies placed in this group before this part came from packages written with two spaces per level: their names are
                // not read (the console reads the release format), which the review shows as not known; the new copy's own details are read
                var (p1b, _, _, _, _) = PrepPlanWith(c, s, choice, HomeOf("e1b")); Gone(p1b?.CurrentBackup);
                var mine1 = p1b?.Earlier.Copies.FirstOrDefault(x => x.Copy == p1?.Home.Copy);
                Check(vr1 != null && HomeArchive.InstalledRe.IsMatch(vr1.S("installed_utc") ?? "") && mine1 != null && mine1.Installed == vr1.S("installed_utc") && mine1.Label == "Test e1" && mine1.Games == 1
                      && mine1.Bytes > 0 && mine1.Allocated >= mine1.Bytes && p1b.Earlier.Copies.Where(x => x.Copy != p1b.Home.Copy).All(x => x.Bytes > 0 && x.Allocated >= x.Bytes),
                      "each copy's record says when it was installed (this PC's time); the next review lists it with its name, games, that date, its size and the space it takes ("
                      + vr1?.S("installed_utc") + "; " + mine1?.Label + ", " + mine1?.Games + " game, " + mine1?.Bytes + " B, " + mine1?.Allocated + " B on the storage)");
                var after1 = rec1?.O("after"); var removed1 = (after1?.A("earlier_home_removed") ?? new List<object>()).OfType<string>().ToList();
                Check(removed1.SequenceEqual(drop1) && after1?.S("earlier_home_restore") == EarlierHome.RestoreText,
                      "the record names exactly the earlier versions removed and how to put them back from the backup made before (" + Listed(removed1) + ")");
                // keep none: the earlier copy goes once the new one is in use; a copy that differs from its record is kept whole
                var prior2 = HomeIn(c); s.EarlierHomeVersions = 0;
                var changed = Path.Combine(Pkg(prior), "session.json"); var orig = File.ReadAllBytes(changed); File.AppendAllText(changed, " ");
                var (p2, o2) = Place("e2"); var n2 = Copies();
                Check(p2 != null && o2?.State == "home-placed" && p2.Steps.SequenceEqual(new[] { "hput", "huse", "hdrop:" + prior2 }) && !n2.Contains(prior2) && n2.Contains(prior) && File.Exists(changed)
                      && p2.Earlier.Why(prior) == "changed since it was installed",
                      "keep none: the copy in use before goes after the switch; a changed earlier copy is kept whole (" + Listed(p2?.Steps) + "; kept " + Listed(n2) + ")");
                // a failure stops every later removal; the copy it was removing is no longer what its record names, so it is kept afterwards
                File.WriteAllBytes(changed, orig); var prior3 = HomeIn(c);
                File.WriteAllText(Path.Combine(c, "faults.json"), "{\"prep\": {\"step\": \"hdrop\"}}");
                var n0 = Writes(c).Count; var (p3, o3) = Place("e3"); File.Delete(Path.Combine(c, "faults.json")); var n3 = Copies(); var w3 = Writes(c).Skip(n0).ToList();
                Check(p3 != null && p3.Steps.Count(x => x.StartsWith("hdrop:", StringComparison.Ordinal)) == 2 && o3?.State == "stopped" && w3.Count(x => x.StartsWith("hdrop", StringComparison.Ordinal)) == 1
                      && n3.Contains(prior) && Directory.Exists(Pkg(prior3)) && HomeIn(c) == p3.Home.Copy,
                      "a failed removal stops the clean-up: the next earlier copy is not touched (" + Listed(w3) + "; " + o3?.Problem + ")");
                var (p4, _) = (PrepPlanWith(c, s, choice, HomePackage.Folder(MakeHome("mod", "e4"), "mod")).plan, (PrepOutcome)null); Gone(p4?.CurrentBackup);
                Check(p4 != null && p4.Earlier.Why(prior3) == "changed since it was installed" && !p4.Steps.Contains("hdrop:" + prior3),
                      "the copy whose removal stopped no longer matches its record: it is kept, never removed in part again");
                // an earlier version that an entry of Original Menu names is kept, and so is every one while entries are recorded on a USB drive
                // or SD card (they cannot be read in this session); the console refuses to remove one too
                var cur6 = HomeIn(c); var named6 = p4?.Earlier.Copies.Where(x => x.Unchanged && x.Copy != cur6 && Copies().Contains(x.Copy)).Select(x => x.Copy).FirstOrDefault();   // an earlier copy the setting could otherwise remove
                var desk = Path.Combine(c, "hakchi-data", "games", "nes-usa", "000", "CLV-Z-00001", "CLV-Z-00001.desktop"); Directory.CreateDirectory(Path.GetDirectoryName(desk));
                File.WriteAllText(desk, "[Desktop Entry]\nExec=/bin/clover-kachikachi-wr /var/lib/ccs/pkg/" + named6 + "/roms/x.nes\n");
                s.EarlierHomeVersions = 0;
                var (p6, _, _, _, _) = PrepPlanWith(c, s, choice, HomeOf("e6")); Gone(p6?.CurrentBackup);
                var conD = RecoverySessions.OpenSimulated(c, ImageKind.Prepare); conD.Identify();
                var drop6 = conD.PrepStep("hdrop", new WriteAuthority("t", HwidA, new[] { "hdrop" }, () => true, "t"), new[] { named6, cur6 });
                Check(p6 != null && p6.Earlier.Why(named6) == "something on the console still names it" && !p6.Steps.Contains("hdrop:" + named6) && p6.Earlier.Copies.First(x => x.Copy == named6).Named == "yes"
                      && !drop6.Ok && (drop6.Line ?? "").Contains("Original Menu") && Copies().Contains(named6),
                      "an earlier version an entry of Original Menu names is kept; the console itself refuses to remove it (" + drop6.Line + ")");
                File.Delete(desk);
                var own = Path.Combine(c, "ccs-data", "owned", "published.own"); Directory.CreateDirectory(Path.GetDirectoryName(own));
                File.WriteAllText(own, "schema=ccs-ownership/1\nmethod=published\npackage=collections\nfile=" + new string('a', 64) + " 0644 /media/hakchi/games/nes-usa/000/CLV-Z-00001/CLV-Z-00001.desktop\n");
                var (p7, _, _, _, _) = PrepPlanWith(c, s, choice, HomeOf("e7")); Gone(p7?.CurrentBackup);
                Check(p7 != null && p7.Earlier.Remove.Count == 0 && !p7.Steps.Any(x => x.StartsWith("hdrop:", StringComparison.Ordinal)) && p7.Earlier.Copies.Where(x => x.Copy != p7.Home.Copy).All(x => x.Named == "unknown"),
                      "while entries are recorded on a USB drive or SD card, no earlier version is removed (" + Listed(p7?.Steps) + ")");
                File.Delete(own);
                // a copy changed between the review and the confirmation: refused, nothing written, the confirmation not used
                var (p8, _, con8, ctx8, adm8) = PrepPlanWith(c, s, choice, HomeOf("e8"));
                var goes8 = p8?.Earlier?.Remove.FirstOrDefault(); var f8 = goes8 == null ? null : Path.Combine(Pkg(goes8), "session.json"); var b8 = f8 == null ? null : File.ReadAllBytes(f8);
                if (f8 != null) File.AppendAllText(f8, " ");
                var n8 = Writes(c).Count; var o8 = p8 == null ? null : Preparation.Execute(con8, p8, p8.Digest, p8.Nonce, ctx8, () => UsePrep(s, adm8, con8, p8));
                if (f8 != null) File.WriteAllBytes(f8, b8); Gone(p8?.CurrentBackup);
                Check(goes8 != null && o8?.State == "refused" && (o8.Problem ?? "").Contains("CCS HOME's copies") && Writes(c).Count == n8 && Copies().Contains(goes8),
                      "an earlier version changed after the review: refused at the confirmation, nothing written (" + o8?.Problem + ")");
                s.EarlierHomeVersions = 0;
                // none never removes the only fallback before the switch succeeds: a failed switch removes nothing
                File.WriteAllText(Path.Combine(c, "faults.json"), "{\"prep\": {\"step\": \"huse\"}}");
                var cur5 = HomeIn(c); var b5 = Copies(); var (p5, o5) = Place("e5"); File.Delete(Path.Combine(c, "faults.json")); var n5 = Copies();
                Check(p5 != null && o5?.State == "stopped" && HomeIn(c) == cur5 && b5.All(n5.Contains), "keep none: when the switch fails, the copy in use and every earlier copy stay (" + o5?.Problem + ")");
                s.EarlierHomeVersions = null;
            });
            GoneConsole(c); GoneBackups(s);
        }

        public static void Method()
        {
            // ---- what the add-ons are, without a console
            var modSha = Methods.Sha256(Methods.Mod); var nucSha = Methods.Sha256(Methods.Nucleus); var choSha = Methods.Sha256(Methods.Chooser);
            string Pin(string n, string sha) => n + " " + (Hex.IsHex(sha, 64) ? sha.Substring(0, 12) : "NOT CARRIED");
            Check(Hex.IsHex(modSha, 64) && Hex.IsHex(nucSha, 64) && Hex.IsHex(choSha, 64) && new[] { modSha, nucSha, choSha }.Distinct().Count() == 3,
                  "the preparation image carries the three add-on packages, pinned (its MANIFEST.json: " + Pin("ccs-mod", modSha) + ", " + Pin("ccs-nucleus", nucSha) + ", " + Pin("ccs-nucleus-chooser", choSha) + ")");
            Part("the add-on choice", () =>
            {
                Check(Choice(false).StartLine == null && Choice(true).StartLine == null && Choice(true, "direct", "ccs-home").StartLine == "ccs-home never on default 10" && Choice(false, "direct").StartLine == "native never off default 10"
                      && Choice(true, "chooser", "native", "last").StartLine == "native always on last 10" && Choice(false, "chooser", "native", "wait").StartLine == "native always off default 0",
                      "CCS Nucleus's start setting for each choice: what opens, the chooser always or never, CCS HOME offered with CCS Mod, what the chooser starts by itself and after how long; none without CCS Nucleus");
                Check(Choice(false, "maybe").Problem() != null && Choice(true, "direct", "Elsewhere!").Problem() != null && Choice(true, "chooser", "native", "soon").Problem() != null
                      && (Choice(false, "direct", "ccs-home").Problem() ?? "").Contains("only with CCS Mod") && Choice(false, "chooser", "ccs-home").Problem() != null && Choice(true, "chooser", "ccs-home", "wait").Problem() == null,
                      "an unknown CCS Nucleus profile, start or chooser selection is refused, and so is CCS HOME at power-on without CCS Mod (" + Choice(false, "direct", "ccs-home").Problem() + ")");
                string State(string body) => "hakchi=v1.0.4-126\n" + body + "free=1\nregistry=ok\nhome_pointer=absent\nhome_identity=absent\nram=1\nresult mstate ok\n";
                var full = MethodState.Parse(State("package=ccs-mod\npackage=ccs-nucleus\npackage=ccs-nucleus-chooser\nstart=ccs-home always on last 10\n"), 0);
                var wait = MethodState.Parse(State("package=ccs-nucleus\npackage=ccs-nucleus-chooser\nstart=native always off default 0\n"), 0);
                var direct = MethodState.Parse(State("package=ccs-nucleus\nstart=native never off default 10\n"), 0);
                var alone = MethodState.Parse(State("package=ccs-nucleus-chooser\nstart=none\n"), 0);
                Check(full.Ok && AddonChoice.FromState(full).SameAs(Choice(true, "chooser", "ccs-home", "last")) && wait.Ok && AddonChoice.FromState(wait).SameAs(Choice(false, "chooser", "native", "wait"))
                      && direct.Ok && AddonChoice.FromState(direct).SameAs(Choice(false, "direct")) && !AddonChoice.FromState(direct).SameAs(Choice(true, "direct")) && alone.Ok && AddonChoice.FromState(alone).Nucleus == "none"
                      && full.Digest() != wait.Digest(),
                      "the console's add-ons are read from its hakchi packages and CCS Nucleus's start setting (the chooser counts only with CCS Nucleus itself)");
                var old = MethodState.Parse("hakchi=v1.0.4-126\npackage=ccs-mod\npolicy=mod\nmodstart=on ours\nfree=1\nregistry=none\nhome_pointer=absent\nhome_identity=absent\nram=1\nresult mstate ok\n", 0);
                var bad = MethodState.Parse(State("start=maybe\n"), 0);
                Check(!old.Ok && (old.Problem ?? "").Contains("incomplete") && !bad.Ok && bad.Problem != null,
                      "an answer without CCS Nucleus's start line (an earlier preparation image's policy and modstart lines) or with a start line in another form is refused, never read as a choice (" + old.Problem + "; " + bad.Problem + ")");
                Check(Methods.Steps.SequenceEqual(new[] { "method", "nstart", "unlist" }) && Methods.Waiting(Methods.Chooser) == "hmod/ccs-nucleus-chooser.hmod" && Methods.Waiting(Methods.Nucleus) == "ccs-nucleus.hmod" && Methods.Waiting(Methods.Mod) == "ccs-mod.hmod"
                      && Methods.ListSha256(new[] { Methods.Chooser, Methods.Nucleus }) == Sha(Encoding.ASCII.GetBytes("ccs-nucleus-chooser ccs-nucleus\n")) && !Methods.IsPackage("mod") && !Methods.IsPackage("nucleus"),
                      "the add-on steps are method, nstart and unlist (CCS Mod's former start step is not one); the chooser waits in the transfer folder's hmod/ subfolder; the removal list is the package names in order on one line");
                Check(MethodEngine.StepLabel("nstart").Contains("power-on") && MethodEngine.StepLabel("method:" + Methods.Chooser).StartsWith("CCS Nucleus's chooser") && Preparation.StepLabel("nstart") == MethodEngine.StepLabel("nstart"),
                      "the review names the steps in words: " + MethodEngine.StepLabel("nstart") + "; " + MethodEngine.StepLabel("method:" + Methods.Chooser));
            });

            Part("the worker's add-on request shapes", () =>
            {
                var sw = SettingsIn(NewDir("method-worker-shapes"), false);
                var none = Path.Combine(Work, "no-such-console-" + Guid.NewGuid().ToString("N").Substring(0, 6));
                List<Dictionary<string, object>> Ask(JObj extra) { var r = new JObj { { "console", "simulated" }, { "folder", none } }; foreach (var kv in extra) r.Set(kv.Key, kv.Value); return WorkerTalk(r, sw); }
                bool Unknown(List<Dictionary<string, object>> evs) => Last(evs)?.S("event") == "error" && Last(evs)?.S("text") == "unknown add-on request";
                var bare = Ask(new JObj { { "cmd", "method-plan" } });
                var both = Ask(new JObj { { "cmd", "method-plan" }, { "choose", true }, { "mod", true } });
                var both2 = Ask(new JObj { { "cmd", "prepare-plan" }, { "choose", true }, { "nucleus", "direct" } });
                var old = Ask(new JObj { { "cmd", "method-plan" }, { "op", "remove" }, { "method", "mod" }, { "mod_start", true } });
                var noMod = Ask(new JObj { { "cmd", "prepare-plan" }, { "nucleus", "direct" }, { "start", "ccs-home" } });
                var odd = Ask(new JObj { { "cmd", "method-plan" }, { "mod", true }, { "nucleus", "sometimes" } });
                Check(Unknown(bare) && Unknown(both) && Unknown(both2) && !Directory.Exists(none),
                      "worker: a Console Restore request naming no add-ons and not asking for the add-on pages, or a request asking for the pages and naming add-ons too (Console Restore or Prepare Console), is refused before any console is opened");
                Check(Unknown(old) && Unknown(noMod) && Unknown(odd) && !Directory.Exists(none),
                      "worker: a request in the earlier form (op, method, mod_start), CCS HOME at power-on without CCS Mod, or an unknown CCS Nucleus profile is refused before any console is opened");
            });

            // ---- Prepare Console on the original software (stock consoles)
            Part("Prepare Console on the original software: CCS Mod", () =>
            {
                var s = SettingsIn(NewDir("method-1"), false);
                var c = Console_("stock"); Backup(c, s);   // (the 413 MB stock data fixture: the small one carries hakchi's files)
                var f0 = BackupFiles(s);
                var cur = MethodEngine.Current(OpenMethod(c), Ctx(s, ImageKind.Method));
                var (sp, sr) = MethodEngine.Prepare(OpenMethod(c), Ctx(s, ImageKind.Method), Choice(true));
                Check(cur.refusal != null && cur.refusal.Contains("Hakchi2 CE 3.9.3") && cur.st != null && cur.st.Hakchi == "none" && sp == null && sr.Problem.Contains("Hakchi2 CE 3.9.3") && Writes(c).Count == 0 && BackupFiles(s) == f0,
                      "Console Restore's add-ons on a console without hakchi: its state read, then refused before any backup or write (" + Flat(sr?.Problem)?.Split('/')[0].Trim() + ")");
                var (b1, br1, _, _, _) = PrepPlanWith(c, s, Choice(false, "something-else"));
                var (b2, br2, _, _, _) = PrepPlanWith(c, s, Choice(false, "direct", "ccs-home"));
                Check(b1 == null && br1.State == "refused" && b2 == null && br2.State == "refused" && br2.Problem.Contains("only with CCS Mod") && Writes(c).Count == 0 && BackupFiles(s) == f0,
                      "Prepare Console with an unknown CCS Nucleus profile, or CCS HOME at power-on without CCS Mod: refused before the console is backed up or written (" + br1?.Problem + "; " + br2?.Problem + ")");

                // the worker's add-on pages on the original software: the console's state reported (situation original), the choice planned
                var homeW = MakeHome("mod", "w1");
                int filesAtState = -1;
                var evs = WorkerTalk(new JObj { { "cmd", "prepare-plan" }, { "console", "simulated" }, { "folder", c }, { "choose", true }, { "home", homeW } }, s, ev =>
                    ev.S("event") == "addon-state" ? Fn(() => { filesAtState = BackupFiles(s); return Target("prepare-target", Choice(true)); })
                    : ev.S("event") == "prep-plan" ? new JObj { { "cmd", "quit" } } : null);
                var ws = Ev(evs, "addon-state"); var wp = Ev(evs, "prep-plan")?.O("plan"); var wr = Last(evs);
                Check(ws != null && ws.S("operation") == "prepare" && ws.S("situation") == "original" && SameChoice(ws.O("current"), Choice(false)) && ws.S("start") == "none" && ws.S("start_text") == null
                      && Strings(ws, "installed").Count == 0 && ws.L("own_images") == 0 && filesAtState == f0,
                      "worker, Prepare Console with the add-on pages on the original software: it reports that situation and no add-ons, before any backup (" + (ws?.S("situation") ?? Flat(wr?.S("problem") ?? wr?.S("text"))) + ")");
                Check(wp != null && Strings(wp, "steps").SequenceEqual(new[] { "files", "uboot", "slot68", "method:ccs-mod", "hput", "huse", "kernel" }) && SameChoice(wp.O("addons"), Choice(true)) && wp.O("home")?.S("layout") == "mod"
                      && wr?.S("state") == "canceled" && Writes(c).Count == 0,
                      "the chosen add-on (CCS Mod, with the request's CCS HOME package) is planned after the backup with hakchi's steps, the kernel last; quitting there writes nothing (" + Listed(Strings(wp, "steps")) + "; " + wr?.S("state") + ")");
                Gone(wp?.S("current_backup"));

                // the engine: CCS Mod only (no CCS Nucleus, so no start setting)
                var homeMod = HomePackage.Folder(MakeHome("mod", "m1"), "mod");
                var (p, r, con, ctx, adm) = PrepPlanWith(c, s, Choice(true), homeMod);
                Check(p != null && p.State == "original" && p.Steps.SequenceEqual(new[] { "files", "uboot", "slot68", "method:ccs-mod", "hput", "huse", "kernel" }) && p.Add.SequenceEqual(new[] { "ccs-mod" }) && p.StartLine == null
                      && p.MethodBefore != null && p.MethodBefore.Hakchi == "none" && p.Home != null && p.Home.ManifestSha256 == homeMod.ManifestSha256 && p.HomePrior == "absent",
                      "the original software, CCS Mod chosen: hakchi's steps with CCS Mod's package and CCS HOME's package (sent, then switched to) before the kernel; no start step without CCS Nucleus (" + Listed(p?.Steps) + "; " + Flat(r?.Problem) + ")");
                var pj = Json.Parse(Json.Write(p.ToJson())); var add0 = (pj.A("add") ?? new List<object>()).OfType<Dictionary<string, object>>().FirstOrDefault();
                Check(SameChoice(pj.O("addons"), Choice(true)) && add0?.S("name") == "ccs-mod" && add0?.S("sha256") == modSha && pj.S("start") == null && pj.O("home")?.S("archive_sha256") == p.Home.Sha256
                      && p.Notes.Any(n => n.Contains("transfer folder") && n.Contains("next normal start")) && p.Notes.Any(n => n.Contains("CCS HOME") && n.Contains("checked file by file")) && p.Notes.Contains(Choice(true).StartText),
                      "the plan names the add-ons chosen, CCS Mod's package by its SHA-256 and CCS HOME's by its archive's, where the package goes and when hakchi installs it, and what opens at power-on (" + Choice(true).StartText + ")");
                var d0 = p.Digest; var hs = p.Home;
                p.Home = HomeArchive.Build(HomePackage.Folder(MakeHome("mod", "other"), "mod"), hs.Attempt, HwidA, "absent"); var t1 = Preparation.Execute(con, p, d0, p.Nonce, ctx).State; p.Home = hs;
                var ad = p.Addons; p.Addons = Choice(true, "direct"); var t2 = Preparation.Execute(con, p, d0, p.Nonce, ctx).State; p.Addons = ad;
                p.Add.Add(Methods.Nucleus); var t3 = Preparation.Execute(con, p, d0, p.Nonce, ctx).State; p.Add.Remove(Methods.Nucleus);
                Check(t1 == "refused" && t2 == "refused" && t3 == "refused" && Writes(c).Count == 0 && p.Nonce != null,
                      "CCS HOME's archive, the add-ons chosen and the packages placed are part of the plan's digest: a plan changed after the review is refused, nothing written");
                AdmissionResult use = null;
                var o = Preparation.Execute(con, p, p.Digest, p.Nonce, ctx, () => use = UsePrep(s, adm, con, p));
                Check(use != null && use.Ok && o.State == "prepared" && Writes(c).SequenceEqual(new[] { "prep files", "prep uboot", "prep slot68", "method ccs-mod", "hput " + p.Home.Copy, "huse " + p.Home.Copy, "prep kernel" }),
                      "prepared: every step once, in order, CCS Mod's package and CCS HOME's package placed before the kernel (" + o.Problem + "; " + Listed(Writes(c)) + ")");
                var rec = Record.Load(o.RecordFile); var rm = rec.O("after")?.O("method"); var radd = (rm?.A("add") ?? new List<object>()).OfType<Dictionary<string, object>>().ToList();
                Check(Waiting(c).SequenceEqual(new[] { "ccs-mod.hmod" }) && rec.S("state") == "prepared" && radd.Count == 1 && radd[0].S("name") == "ccs-mod" && radd[0].S("sha256") == modSha && SameChoice(rm.O("addons"), Choice(true)) && rm.S("start") == null
                      && HomeIn(c) == p.Home.Copy && rec.O("after")?.O("home")?.S("manifest_sha256") == homeMod.ManifestSha256 && rec.O("before")?.S("snapshot") == p.CurrentBackup && StartIn(c) == "none",
                      "CCS Mod's package waits in hakchi's transfer folder; CCS HOME's package is the one in use; the preparation record names both and keeps the pre-preparation snapshot (which Return to Original Software looks for)");
                Gone(p.CurrentBackup);
                var started = SimulatedConsole.NormalStart(c);
                Check(started.SequenceEqual(new[] { "install ccs-mod", "transfer folder removed; restart" }) && Installed(c).SequenceEqual(new[] { "ccs-mod" }) && StartIn(c) == "none" && Waiting(c).Count == 0,
                      "the next normal start: hakchi installs CCS Mod; without CCS Nucleus there is no start setting and Original Menu opens as before (" + Listed(started) + ")");
                GoneConsole(c); GoneBackups(s);
            });

            Part("Prepare Console on the original software: CCS Mod and CCS Nucleus", () =>
            {
                var s = SettingsIn(NewDir("method-2"), false);
                var c = Console_("stock"); Backup(c, s);
                var home = HomePackage.Folder(MakeHome("mod", "m2"), "mod");
                var (p, r, con, ctx, adm) = PrepPlanWith(c, s, Choice(true, "chooser", "ccs-home", "last"), home);
                Check(p != null && p.Refusal == null && p.Steps.SequenceEqual(new[] { "files", "uboot", "slot68", "method:ccs-mod", "method:ccs-nucleus", "method:ccs-nucleus-chooser", "nstart", "hput", "huse", "kernel" })
                      && p.StartLine == "ccs-home always on last 10" && p.Add.SequenceEqual(new[] { "ccs-mod", "ccs-nucleus", "ccs-nucleus-chooser" }),
                      "the original software, CCS Mod and CCS Nucleus with its chooser: the three packages in the order hakchi installs them (the chooser after CCS Nucleus), CCS Nucleus's start, then CCS HOME, the kernel last ("
                      + (p != null ? Listed(p.Steps) : Flat(r?.Problem)) + ")");
                var add = new List<string> { Methods.Mod, Methods.Nucleus, Methods.Chooser }; var how = "CCS Nucleus with its chooser";
                if (p == null)
                {   // this build does not carry the chooser's package (the check above failed with the reason): CCS Nucleus started directly
                    // instead, so the rest of the first-time preparation with both add-ons is still checked
                    add.Remove(Methods.Chooser); how = "CCS Nucleus started directly, as this build does not carry the chooser's package";
                    (p, r, con, ctx, adm) = PrepPlanWith(c, s, Choice(true, "direct", "ccs-home"), home);
                    Check(p != null && p.Steps.SequenceEqual(new[] { "files", "uboot", "slot68", "method:ccs-mod", "method:ccs-nucleus", "nstart", "hput", "huse", "kernel" }) && p.StartLine == "ccs-home never on default 10",
                          "the original software, CCS Mod and CCS Nucleus started directly: both packages, CCS Nucleus's start, then CCS HOME, the kernel last (" + Listed(p?.Steps) + "; " + Flat(r?.Problem) + ")");
                }
                var line = p.StartLine;
                var o = Preparation.Execute(con, p, p.Digest, p.Nonce, ctx, () => UsePrep(s, adm, con, p));
                var want = new[] { "prep files", "prep uboot", "prep slot68" }.Concat(add.Select(a => "method " + a)).Concat(new[] { "nstart " + line, "hput " + p.Home.Copy, "huse " + p.Home.Copy, "prep kernel" });
                var rm = Record.Load(o.RecordFile).O("after")?.O("method");
                Check(o.State == "prepared" && Writes(c).SequenceEqual(want) && Waiting(c).SequenceEqual(add.Select(Methods.Waiting)) && StartIn(c) == line && rm?.S("start") == line && HomeIn(c) == p.Home.Copy,
                      "prepared (" + how + "): the packages wait in the transfer folder (the chooser in hmod/), CCS Nucleus's start is set before the kernel and recorded (" + o.Problem + "; " + Listed(Writes(c)) + ")");
                Gone(p.CurrentBackup);
                var st = SimulatedConsole.NormalStart(c);
                Check(st.SequenceEqual(add.Select(a => "install " + a).Concat(new[] { "transfer folder removed; restart" })) && Installed(c).SequenceEqual(add) && StartIn(c) == line,
                      "the next normal start installs them in that order (" + how + "), and CCS Nucleus keeps the start already made: CCS HOME (" + Listed(st) + ")");
                GoneConsole(c); GoneBackups(s);
            });

            Part("Prepare Console: a failure of CCS Nucleus's start setting", () =>
            {
                var s = SettingsIn(NewDir("method-3"), false);
                var c = Console_("stock"); Backup(c, s);
                File.WriteAllText(Path.Combine(c, "faults.json"), "{\"prep\":{\"step\":\"nstart\"}}");
                var (p, r, con, ctx, adm) = PrepPlanWith(c, s, Choice(false, "direct"));
                Check(p != null && p.Steps.SequenceEqual(new[] { "files", "uboot", "slot68", "method:ccs-nucleus", "nstart", "kernel" }) && p.Home == null && p.StartLine == "native never off default 10",
                      "the original software, CCS Nucleus alone: its package and its start before the kernel; no CCS HOME without CCS Mod (" + Listed(p?.Steps) + "; " + Flat(r?.Problem) + ")");
                var o = Preparation.Execute(con, p, p.Digest, p.Nonce, ctx, () => UsePrep(s, adm, con, p));
                Check(o.State == "stopped" && o.Done.SequenceEqual(new[] { "files", "uboot", "slot68", "method:ccs-nucleus" }) && Writes(c).SequenceEqual(new[] { "prep files", "prep uboot", "prep slot68", "method ccs-nucleus" }) && StartIn(c) == "none"
                      && File.ReadAllBytes(SimulatedConsole.Dev(c, "boot2")).SequenceEqual(StockKernel()) && o.Problem.Contains("nothing was retried"),
                      "CCS Nucleus's start setting fails: stopped there; the kernel is NOT written, so the console still starts its original system; nothing retried (" + o.Problem + ")");
                GoneConsole(c); GoneBackups(s);
            });

            Part("Prepare Console: a failure of CCS HOME's transfer", () =>
            {
                var s = SettingsIn(NewDir("method-4"), false);
                var c = Console_("stock"); Backup(c, s);
                File.WriteAllText(Path.Combine(c, "faults.json"), "{\"prep\":{\"step\":\"hput\"}}");
                var (p, r, con, ctx, adm) = PrepPlanWith(c, s, Choice(true), HomePackage.Folder(MakeHome("mod", "m4", 512), "mod"));
                var o = Preparation.Execute(con, p, p.Digest, p.Nonce, ctx, () => UsePrep(s, adm, con, p));
                Check(o.State == "stopped" && o.Done.SequenceEqual(new[] { "files", "uboot", "slot68", "method:ccs-mod" }) && Writes(c).Count == 5 && Writes(c)[4] == "hput " + p.Home.Copy + " INTERRUPTED" && HomeIn(c) == null
                      && File.ReadAllBytes(SimulatedConsole.Dev(c, "boot2")).SequenceEqual(StockKernel()) && o.Problem.Contains("finishes this installation"),
                      "CCS HOME's transfer fails: stopped there; no HOME is switched to and the kernel is NOT written; the result says how it is finished (" + o.Problem + ")");
                GoneConsole(c); GoneBackups(s);
            });

            // ---- Console Restore: the add-ons added, removed and changed (MethodEngine), on smaller consoles prepared with hakchi
            var sM = SettingsIn(NewDir("method-restore"), false); var mctx = Ctx(sM, ImageKind.Method);
            Part("Console Restore: CCS Mod removed, then CCS Nucleus added", () =>
            {
                var c = SmallConsole("modified", true); SetInstalled(c, Methods.Mod);
                var f0 = BackupFiles(sM);
                var cur = MethodEngine.Current(OpenMethod(c), mctx);
                Check(cur.refusal == null && cur.st != null && cur.st.Installed(Methods.Mod) && AddonChoice.FromState(cur.st).SameAs(Choice(true)) && cur.id != null && Writes(c).Count == 0 && BackupFiles(sM) == f0,
                      "the console's add-ons read before anything is chosen: CCS Mod installed; no backup, no write (" + Flat(cur.refusal) + ")");
                var (n1, nr1) = MethodEngine.Prepare(OpenMethod(c), mctx, null);
                var (n2, nr2) = MethodEngine.Prepare(OpenMethod(c), mctx, Choice(false, "direct", "ccs-home"));
                var (n3, nr3) = MethodEngine.Prepare(OpenMethod(c), mctx, Choice(true));
                Check(n1 == null && nr1.Problem == "Choose the add-ons." && n2 == null && nr2.Problem.Contains("only with CCS Mod") && n3 == null && nr3.Problem.Contains("Nothing to change") && Writes(c).Count == 0 && BackupFiles(sM) == f0,
                      "no choice, CCS HOME at power-on without CCS Mod, or exactly the add-ons the console has: refused before any backup or write (" + nr2?.Problem + "; " + nr3?.Problem + ")");
                var au = new WriteAuthority("t", HwidA, new[] { "mstart" }, () => true, "t");
                Check(Throws<ArgumentException>(() => OpenMethod(c).PrepStep("mstart", au, new[] { "on" })) && Writes(c).Count == 0 && !File.Exists(Path.Combine(c, "ccs-mod-config.txt")),
                      "CCS Mod's former start step (mstart) is not a preparation step: refused before the console is asked");
                // something else waiting for the next start: refused, it is not this change's own
                Directory.CreateDirectory(Transfer(c)); File.WriteAllText(Path.Combine(Transfer(c), "uninstall"), "someone-else\n");
                var (w1, wr1) = MethodEngine.Prepare(OpenMethod(c), mctx, Choice(false));
                Directory.Delete(Transfer(c), true);
                Directory.CreateDirectory(Path.Combine(Transfer(c), "hmod")); File.WriteAllText(Path.Combine(Transfer(c), "hmod", "other.hmod"), "another program's package\n");
                var (w2, wr2) = MethodEngine.Prepare(OpenMethod(c), mctx, Choice(false));
                Directory.Delete(Transfer(c), true);
                Check(w1 == null && wr1.Problem.Contains("already waits") && wr1.Problem.Contains("(uninstall)") && w2 == null && wr2.Problem.Contains("hmod/other.hmod") && Writes(c).Count == 0 && BackupFiles(sM) == f0,
                      "another removal list, or another package in the transfer folder's hmod/ subfolder, already waiting for the next start: refused before any backup (" + wr1?.Problem + "; " + wr2?.Problem + ")");

                var con = OpenMethod(c);
                var (p, r) = MethodEngine.Prepare(con, mctx, Choice(false));
                var pv = p == null ? null : Verifier.VerifyFile(p.ProtectivePath); var pj = p == null ? null : Json.Parse(Json.Write(p.ToJson()));
                Check(r == null && p.Steps.SequenceEqual(new[] { "unlist" }) && p.Remove.SequenceEqual(new[] { "ccs-mod" }) && p.Add.Count == 0 && p.StartLine == null && p.ListSha256 == Methods.ListSha256(new[] { "ccs-mod" })
                      && pv != null && pv.Ok && p.ProtectiveArchive.Session == con.SessionId && p.Before.Installed("ccs-mod") && p.Current.Mod && !p.Target.Mod && p.Text.Contains("Remove CCS Mod")
                      && pj.S("operation") == "addons" && pj.O("removal_list")?.S("names") == "ccs-mod" && pj.O("removal_list")?.S("sha256") == p.ListSha256,
                      "remove CCS Mod: one step (hakchi's removal list naming it), after a complete backup made in this same session (" + Flat(r?.Problem ?? p.Text) + ")");
                var nonce = p.Nonce;
                Check(MethodEngine.Execute(con, p, Other(p.Digest), nonce, mctx).State == "refused" && MethodEngine.Execute(con, p, p.Digest, Other(nonce), mctx).State == "refused" && Writes(c).Count == 0 && p.Nonce == nonce,
                      "a confirmation for another plan, or with another one-time code: refused, nothing written, the confirmation not used");
                Check(MethodEngine.Execute(OpenMethod(c), p, p.Digest, nonce, mctx).State == "refused" && Writes(c).Count == 0, "the console connected again after the review: refused, nothing written");
                var exp = p.ExpiresUtc; var dg = p.Digest;
                p.ExpiresUtc = DateTime.UtcNow.AddMinutes(-1); p.Digest = Resign(p);
                var me = MethodEngine.Execute(con, p, p.Digest, nonce, mctx);
                p.ExpiresUtc = exp; p.Digest = dg;
                Check(me.State == "refused" && (me.Problem ?? "").Contains("more than 10 minutes old") && me.RecordFile == null && Writes(c).Count == 0 && p.Nonce == nonce,
                      "a confirmation after the plan's ten minutes (the plan signed again for that time): refused, nothing written, no record (" + me.Problem + ")");
                var prot = p.ProtectivePath;
                File.Move(prot, prot + ".withheld");
                var mm = MethodEngine.Execute(con, p, p.Digest, nonce, mctx);
                Check(mm.State == "refused" && mm.Problem.Contains("missing") && Writes(c).Count == 0 && p.Nonce == nonce && mm.RecordFile == null, "the backup made for the change moved aside: refused, nothing written, no record, the confirmation not used (" + mm.Problem + ")");
                File.Move(prot + ".withheld", prot);
                SetInstalled(c, Methods.Mod, "other-package");
                var mc = MethodEngine.Execute(con, p, p.Digest, nonce, mctx);
                Check(mc.State == "refused" && mc.Problem.Contains("changed since the review") && Writes(c).Count == 0 && p.Nonce == nonce, "the console's hakchi packages changed after the review: refused, nothing written (" + mc.Problem + ")");
                SetInstalled(c, Methods.Mod);
                var o = MethodEngine.Execute(con, p, p.Digest, nonce, mctx);
                var rec = Record.Load(o.RecordFile);
                Check(o.State == "scheduled" && Writes(c).SequenceEqual(new[] { "unlist ccs-mod" }) && Waiting(c).SequenceEqual(new[] { "uninstall" }) && File.ReadAllText(Path.Combine(Transfer(c), "uninstall")) == "ccs-mod\n"
                      && rec.S("state") == "scheduled" && rec.O("plan") != null && rec.O("after")?.O("target")?.S("nucleus") == "none",
                      "removal scheduled: hakchi's removal list naming ccs-mod waits for the next start; the record (saved before the write) shows it (" + o.Problem + ")");
                Check(MethodEngine.Execute(con, p, p.Digest, p.Nonce, mctx).State == "refused" && Writes(c).Count == 1, "the same confirmation cannot run twice");
                Gone(p.ProtectivePath);
                var st = SimulatedConsole.NormalStart(c);
                Check(st.SequenceEqual(new[] { "uninstall ccs-mod", "transfer folder removed; restart" }) && Installed(c).Count == 0 && StartIn(c) == "none", "the next normal start: hakchi runs CCS Mod's own removal (" + Listed(st) + ")");

                // CCS Nucleus with its chooser added together: the chooser waits in hmod/, so hakchi installs it after CCS Nucleus
                var n0 = Writes(c).Count;
                var con2 = OpenMethod(c);
                var (q, qr) = MethodEngine.Prepare(con2, mctx, Choice(false, "chooser", "native", "wait"));
                Check(q != null && q.Steps.SequenceEqual(new[] { "method:ccs-nucleus", "method:ccs-nucleus-chooser", "nstart" }) && q.StartLine == "native always off default 0" && q.Home == null && q.Remove.Count == 0,
                      "add CCS Nucleus with its chooser: CCS Nucleus's package, then the chooser's, then CCS Nucleus's start (the chooser always, nothing starting by itself); no CCS HOME without CCS Mod ("
                      + (q != null ? Listed(q.Steps) : Flat(qr?.Problem)) + ")");
                var add = new List<string> { Methods.Nucleus, Methods.Chooser }; var how = "with its chooser";
                if (q == null)
                {   // this build does not carry the chooser's package (the check above failed with the reason): CCS Nucleus started directly
                    add.Remove(Methods.Chooser); how = "started directly, as this build does not carry the chooser's package";
                    (q, qr) = MethodEngine.Prepare(con2, mctx, Choice(false, "direct"));
                    Check(q != null && q.Steps.SequenceEqual(new[] { "method:ccs-nucleus", "nstart" }) && q.StartLine == "native never off default 10" && q.Home == null,
                          "add CCS Nucleus started directly: its package, then its start (" + Listed(q?.Steps) + "; " + Flat(qr?.Problem) + ")");
                }
                var qo = MethodEngine.Execute(con2, q, q.Digest, q.Nonce, mctx); Gone(q.ProtectivePath);
                Check(qo.State == "scheduled" && Writes(c).Skip(n0).SequenceEqual(add.Select(a => "method " + a).Concat(new[] { "nstart " + q.StartLine })) && Waiting(c).SequenceEqual(add.Select(Methods.Waiting)) && StartIn(c) == q.StartLine,
                      "CCS Nucleus " + how + ": the packages wait (the chooser in hmod/), the start is set and read back (" + qo.Problem + "; " + Listed(Waiting(c)) + ")");
                st = SimulatedConsole.NormalStart(c);
                Check(st.SequenceEqual(add.Select(a => "install " + a).Concat(new[] { "transfer folder removed; restart" })) && Installed(c).SequenceEqual(add) && StartIn(c) == q.StartLine,
                      "the next normal start installs CCS Nucleus first, then what waits in hmod/; CCS Nucleus keeps the start already made (" + Listed(st) + ")");
                GoneConsole(c); GoneBackups(sM);
            });

            Part("Console Restore through the worker's add-on pages, the start alone, and CCS Mod removed while CCS Nucleus stays", () =>
            {
                var c = SmallConsole("modified", true); SetInstalled(c, Methods.Mod, Methods.Nucleus, "other-package"); SetStart(c, "native never on default 10"); SetOwnImage(c);
                var req = new JObj { { "cmd", "method-plan" }, { "console", "simulated" }, { "folder", c }, { "choose", true } };
                // the worker: the console's add-ons reported (read only), then the chosen ones planned after the backup, then confirmed
                int f0 = BackupFiles(sM), filesAtState = -1, writesAtState = -1; Dictionary<string, object> plan = null;
                var evs = WorkerTalk(req, sM, ev =>
                    ev.S("event") == "addon-state" ? Fn(() => { filesAtState = BackupFiles(sM); writesAtState = Writes(c).Count; return Target("method-target", Choice(true, "direct", "ccs-home")); })
                    : ev.S("event") == "method-plan" ? Confirm("method-confirm", plan = ev.O("plan")) : null);
                var ws = Ev(evs, "addon-state"); var wr = Last(evs);
                Check(ws != null && ws.S("operation") == "method" && ws.S("situation") == "hakchi" && SameChoice(ws.O("current"), Choice(true, "direct")) && ws.S("start") == "native never on default 10"
                      && ws.S("start_text") == Choice(true, "direct").StartText && Strings(ws, "installed").SequenceEqual(new[] { "ccs-mod", "ccs-nucleus" }) && ws.L("other_packages") == 1 && ws.L("own_images") == 1
                      && !string.IsNullOrEmpty(ws.S("console_tag")) && filesAtState == f0 && writesAtState == 0 && evs.IndexOf(ws) < evs.FindIndex(e => e.S("event") == "method-plan"),
                      "worker, Console Restore with the add-on pages: the console's add-ons, its start, its other packages and its own system images are reported first, before any backup or write ("
                      + Flat(ws == null ? wr?.S("problem") ?? wr?.S("text") : Listed(Strings(ws, "installed"))) + ")");
                Check(plan != null && Strings(plan, "steps").SequenceEqual(new[] { "nstart" }) && SameChoice(plan.O("current"), Choice(true, "direct")) && SameChoice(plan.O("target"), Choice(true, "direct", "ccs-home"))
                      && plan.S("start") == "ccs-home never on default 10" && wr?.S("state") == "scheduled" && Writes(c).SequenceEqual(new[] { "nstart ccs-home never on default 10" }) && StartIn(c) == "ccs-home never on default 10" && Waiting(c).Count == 0,
                      "the method-target is planned after the backup and confirmed with the plan's own code: only the start changes, set directly in CCS Nucleus, nothing waits for hakchi (" + Listed(Strings(plan, "steps")) + "; " + wr?.S("state") + " " + wr?.S("problem") + ")");
                Gone(wr?.S("protective"));
                Check(SimulatedConsole.NormalStart(c).Count == 0 && StartIn(c) == "ccs-home never on default 10", "with nothing in the transfer folder the next normal start changes nothing: CCS HOME opens at power-on");
                // the add-on pages left without a choice, or with a choice that cannot be made: nothing backed up, nothing written
                var f1 = BackupFiles(sM);
                var quit = WorkerTalk(req, sM, ev => ev.S("event") == "addon-state" ? new JObj { { "cmd", "quit" } } : null);
                var cancel = WorkerTalk(req, sM, ev => ev.S("event") == "addon-state" ? new JObj { { "cmd", "cancel" } } : null);
                var invalid = WorkerTalk(req, sM, ev => ev.S("event") == "addon-state" ? Target("method-target", Choice(false, "direct", "ccs-home")) : null);
                Check(Ev(quit, "addon-state") != null && Last(quit)?.S("state") == "canceled" && Ev(cancel, "addon-state") != null && Last(cancel)?.S("state") == "canceled" && Ev(quit, "method-plan") == null && Ev(cancel, "method-plan") == null
                      && Writes(c).Count == 1 && BackupFiles(sM) == f1,
                      "worker: quitting or canceling on the add-on pages ends with canceled; nothing backed up or written (" + Last(quit)?.S("problem") + "; " + Last(cancel)?.S("problem") + ")");
                Check(Ev(invalid, "addon-state") != null && Last(invalid)?.S("state") == "refused" && (Last(invalid)?.S("problem") ?? "").Contains("only with CCS Mod") && Ev(invalid, "method-plan") == null && Writes(c).Count == 1 && BackupFiles(sM) == f1,
                      "worker: a target that cannot be made (CCS HOME at power-on without CCS Mod) is refused with the reason; nothing backed up or written (" + Last(invalid)?.S("problem") + ")");

                // the engine: CCS Mod cannot go while CCS Nucleus opens CCS HOME; with Original Menu at power-on it can
                var (b, br) = MethodEngine.Prepare(OpenMethod(c), mctx, Choice(false, "direct", "ccs-home"));
                Check(b == null && br.Problem.Contains("only with CCS Mod") && Writes(c).Count == 1 && BackupFiles(sM) == f1, "removing CCS Mod while CCS Nucleus opens CCS HOME at power-on: refused before anything (" + br?.Problem + ")");
                var con = OpenMethod(c);
                var (p, r) = MethodEngine.Prepare(con, mctx, Choice(false, "direct"));
                Check(r == null && p.Steps.SequenceEqual(new[] { "nstart", "unlist" }) && p.Remove.SequenceEqual(new[] { "ccs-mod" }) && p.Add.Count == 0 && p.StartLine == "native never off default 10" && p.Home == null
                      && p.Text.Contains("Remove CCS Mod") && p.Text.Contains(Choice(false, "direct").StartText),
                      "removing CCS Mod with Original Menu at power-on: CCS Nucleus's start first (CCS HOME no longer offered), then the removal list (" + Flat(r?.Problem ?? p.Text) + ")");
                var o = MethodEngine.Execute(con, p, p.Digest, p.Nonce, mctx); Gone(p.ProtectivePath);
                Check(o.State == "scheduled" && Writes(c).Skip(1).SequenceEqual(new[] { "nstart native never off default 10", "unlist ccs-mod" }) && StartIn(c) == "native never off default 10" && Waiting(c).SequenceEqual(new[] { "uninstall" }),
                      "written in that order and read back (" + o.Problem + "; " + Listed(Writes(c).Skip(1)) + ")");
                var st = SimulatedConsole.NormalStart(c);
                Check(st.SequenceEqual(new[] { "uninstall ccs-mod", "transfer folder removed; restart" }) && Installed(c).SequenceEqual(new[] { "ccs-nucleus", "other-package" }) && StartIn(c) == "native never off default 10" && OwnImageKept(c),
                      "the next normal start removes CCS Mod; CCS Nucleus, the other package and the owner's own system image stay; Original Menu opens (" + Listed(st) + ")");
                var f2 = BackupFiles(sM);
                var (n, nr) = MethodEngine.Prepare(OpenMethod(c), mctx, Choice(false, "direct"));
                Check(n == null && nr.Problem.Contains("Nothing to change") && BackupFiles(sM) == f2, "the same add-ons and the same start again: nothing to change (" + nr?.Problem + ")");
                GoneConsole(c); GoneBackups(sM);
            });

            Part("Console Restore: the chooser's start, the chooser removed and added, CCS Nucleus removed with its chooser", () =>
            {
                var c = SmallConsole("modified", true); SetInstalled(c, Methods.Nucleus, Methods.Chooser); SetStart(c, "native always off default 10"); SetOwnImage(c);
                // the start alone, with the chooser: nothing starts by itself any more
                var con = OpenMethod(c);
                var (p, r) = MethodEngine.Prepare(con, mctx, Choice(false, "chooser", "native", "wait"));
                Check(r == null && p.Steps.SequenceEqual(new[] { "nstart" }) && p.Add.Count == 0 && p.Remove.Count == 0 && p.StartLine == "native always off default 0" && p.Notes.Any(x => x.Contains("No kernel, bootloader or system is changed")),
                      "the chooser's selection changed (nothing starts until a choice): only CCS Nucleus's start (" + Flat(r?.Problem) + ")");
                var o = MethodEngine.Execute(con, p, p.Digest, p.Nonce, mctx); Gone(p.ProtectivePath);
                Check(o.State == "scheduled" && Writes(c).SequenceEqual(new[] { "nstart native always off default 0" }) && StartIn(c) == "native always off default 0" && Waiting(c).Count == 0, "set and read back; nothing waits for hakchi (" + o.Problem + ")");
                // the chooser removed: CCS Nucleus stays and starts Original Menu directly
                con = OpenMethod(c);
                (p, r) = MethodEngine.Prepare(con, mctx, Choice(false, "direct"));
                Check(r == null && p.Steps.SequenceEqual(new[] { "nstart", "unlist" }) && p.Remove.SequenceEqual(new[] { "ccs-nucleus-chooser" }) && p.StartLine == "native never off default 10" && p.Text.Contains("Remove CCS Nucleus's chooser")
                      && p.Notes.Any(x => x.Contains("Your own system images stay on the console")),
                      "remove the chooser only: CCS Nucleus's start (the chooser never), then the removal list naming the chooser; the review says the owner's own system images stay (" + Flat(r?.Problem ?? p.Text) + ")");
                o = MethodEngine.Execute(con, p, p.Digest, p.Nonce, mctx); Gone(p.ProtectivePath);
                var st = SimulatedConsole.NormalStart(c);
                Check(o.State == "scheduled" && Writes(c).Skip(1).SequenceEqual(new[] { "nstart native never off default 10", "unlist ccs-nucleus-chooser" }) && st.SequenceEqual(new[] { "uninstall ccs-nucleus-chooser", "transfer folder removed; restart" })
                      && Installed(c).SequenceEqual(new[] { "ccs-nucleus" }) && StartIn(c).Split(' ')[1] == "never" && StartIn(c) == "native never off default 10" && OwnImageKept(c),
                      "the next normal start removes the chooser; CCS Nucleus stays, its start's chooser word is never, the owner's own system image is kept (" + o.Problem + "; " + Listed(st) + ")");
                // the chooser added back to CCS Nucleus
                var n0 = Writes(c).Count;
                con = OpenMethod(c);
                (p, r) = MethodEngine.Prepare(con, mctx, Choice(false, "chooser"));
                Check(p != null && p.Steps.SequenceEqual(new[] { "method:ccs-nucleus-chooser", "nstart" }) && p.Add.SequenceEqual(new[] { "ccs-nucleus-chooser" }) && p.Remove.Count == 0 && p.StartLine == "native always off default 10",
                      "add the chooser to CCS Nucleus: its package, then CCS Nucleus's start (the chooser always) (" + (p != null ? Listed(p.Steps) : Flat(r?.Problem)) + ")");
                if (p != null)
                {
                    o = MethodEngine.Execute(con, p, p.Digest, p.Nonce, mctx); Gone(p.ProtectivePath);
                    Check(o.State == "scheduled" && Writes(c).Skip(n0).SequenceEqual(new[] { "method ccs-nucleus-chooser", "nstart native always off default 10" }) && Waiting(c).SequenceEqual(new[] { "hmod/ccs-nucleus-chooser.hmod" }),
                          "the chooser's package waits in the transfer folder's hmod/ subfolder (" + o.Problem + ")");
                    st = SimulatedConsole.NormalStart(c);
                    Check(st.SequenceEqual(new[] { "install ccs-nucleus-chooser", "transfer folder removed; restart" }) && Installed(c).SequenceEqual(new[] { "ccs-nucleus", "ccs-nucleus-chooser" }) && StartIn(c) == "native always off default 10",
                          "the next normal start installs the chooser beside CCS Nucleus (" + Listed(st) + ")");
                }
                // CCS Nucleus removed with its chooser, CCS Mod kept (the console set to that state directly, so this case runs the same whether
                // or not the chooser could be added above)
                SetInstalled(c, Methods.Mod, Methods.Nucleus, Methods.Chooser); SetStart(c, "native always on default 10");
                n0 = Writes(c).Count;
                con = OpenMethod(c);
                (p, r) = MethodEngine.Prepare(con, mctx, Choice(true));
                Check(r == null && p.Steps.SequenceEqual(new[] { "unlist" }) && p.Remove.SequenceEqual(new[] { "ccs-nucleus-chooser", "ccs-nucleus" }) && p.Add.Count == 0 && p.StartLine == null && p.Home == null
                      && p.ListSha256 == Methods.ListSha256(new[] { "ccs-nucleus-chooser", "ccs-nucleus" }) && p.Text.Contains("Remove CCS Nucleus's chooser and CCS Nucleus"),
                      "remove CCS Nucleus with its chooser: one removal list naming the chooser first, then CCS Nucleus; no start step and no CCS HOME step (CCS Mod stays) (" + Flat(r?.Problem ?? p.Text) + ")");
                o = MethodEngine.Execute(con, p, p.Digest, p.Nonce, mctx); Gone(p.ProtectivePath);
                Check(o.State == "scheduled" && Writes(c).Skip(n0).SequenceEqual(new[] { "unlist ccs-nucleus-chooser ccs-nucleus" }) && File.ReadAllText(Path.Combine(Transfer(c), "uninstall")) == "ccs-nucleus-chooser ccs-nucleus\n",
                      "the removal list waits with both names in that order (" + o.Problem + ")");
                st = SimulatedConsole.NormalStart(c);
                Check(st.SequenceEqual(new[] { "uninstall ccs-nucleus-chooser", "uninstall ccs-nucleus", "transfer folder removed; restart" }) && Installed(c).SequenceEqual(new[] { "ccs-mod" }),
                      "the next normal start runs the chooser's own removal, then CCS Nucleus's; CCS Mod stays (" + Listed(st) + ")");
                GoneConsole(c); GoneBackups(sM);
            });

            Part("Console Restore: an interrupted change finished by a new connection", () =>
            {
                var c = SmallConsole("modified", true); SetInstalled(c, Methods.Nucleus, Methods.Chooser); SetStart(c, "native always off default 10");
                File.WriteAllText(Path.Combine(c, "faults.json"), "{\"prep\":{\"step\":\"unlist\"}}");
                var home = HomePackage.Folder(MakeHome("mod", "x1"), "mod");
                var con = OpenMethod(c);
                var (p, r) = MethodEngine.Prepare(con, mctx, Choice(true, "direct", "ccs-home"), home);
                Check(r == null && p.Steps.SequenceEqual(new[] { "method:ccs-mod", "nstart", "unlist", "hput", "huse" }) && p.Add.SequenceEqual(new[] { "ccs-mod" }) && p.Remove.SequenceEqual(new[] { "ccs-nucleus-chooser" })
                      && p.StartLine == "ccs-home never on default 10" && p.Home?.Layout == "mod",
                      "add CCS Mod, start CCS HOME directly and remove the chooser: CCS Mod's package, the start, the removal list, then CCS HOME (" + Listed(p?.Steps) + "; " + Flat(r?.Problem) + ")");
                var o = MethodEngine.Execute(con, p, p.Digest, p.Nonce, mctx);
                Check(o.State == "stopped" && o.Done.SequenceEqual(new[] { "method:ccs-mod", "nstart" }) && Waiting(c).SequenceEqual(new[] { "ccs-mod.hmod" }) && HomeIn(c) == null && Record.Load(o.RecordFile).S("state") == "stopped",
                      "the removal list's write fails: stopped there, nothing retried; what was already written waits, recorded (" + o.Problem + ")");
                File.Delete(Path.Combine(c, "faults.json")); Gone(p.ProtectivePath);
                var con2 = OpenMethod(c);
                var (p2, r2) = MethodEngine.Prepare(con2, mctx, Choice(true, "direct", "ccs-home"), home);
                Check(r2 == null && p2 != null && p2.Before.Waiting.ContainsKey("ccs-mod.hmod") && p2.Steps.SequenceEqual(new[] { "method:ccs-mod", "unlist", "hput", "huse" }),
                      "a new connection: CCS Mod's package waiting there is this change's own and the start is already set, so the change can be finished (" + Listed(p2?.Steps) + "; " + Flat(r2?.Problem) + ")");
                var o2 = MethodEngine.Execute(con2, p2, p2.Digest, p2.Nonce, mctx);
                var phases = Record.Load(o2.RecordFile).A("phases").OfType<Dictionary<string, object>>().ToList();
                Check(o2.State == "scheduled" && Writes(c).SequenceEqual(new[] { "method ccs-mod", "nstart ccs-home never on default 10", "unlist ccs-nucleus-chooser", "hput " + p2.Home.Copy, "huse " + p2.Home.Copy })
                      && phases.Any(x => x.S("step") == "method:ccs-mod" && x.S("state") == "already-done") && Waiting(c).SequenceEqual(new[] { "ccs-mod.hmod", "uninstall" }) && HomeIn(c) == p2.Home.Copy,
                      "finished: CCS Mod's package is not written again (ok already), the removal list is written once, CCS HOME is in use (" + o2.Problem + "; " + Listed(Writes(c)) + ")");
                Gone(p2.ProtectivePath);
                var st = SimulatedConsole.NormalStart(c);
                Check(st.SequenceEqual(new[] { "uninstall ccs-nucleus-chooser", "install ccs-mod", "transfer folder removed; restart" }) && Sorted(Installed(c)).SequenceEqual(new[] { "ccs-mod", "ccs-nucleus" }) && StartIn(c) == "ccs-home never on default 10",
                      "the next normal start removes the chooser first, then installs CCS Mod; CCS Nucleus opens CCS HOME (" + Listed(st) + ")");
                var (n, nr) = MethodEngine.Prepare(OpenMethod(c), mctx, Choice(true, "direct", "ccs-home"), home);
                Check(n == null && nr.Problem.Contains("Nothing to change"), "the same add-ons again: nothing to change (" + nr?.Problem + ")");
                GoneConsole(c); GoneBackups(sM);
            });

            Part("Console Restore: the console's own start setting kept as it is through another change and an update (Last Used with no countdown, another countdown, the chooser not shown at start, its own system image first)", () =>
            {
                const string OldNucleusPin = "0074f2ec6bba1f7f329a3eafdfac4beaef3681e87802680e38aa435043eef2b7";   // CCS Nucleus as 0.11.11 published it
                var c = SmallConsole("modified", true); SetInstalled(c, Methods.Nucleus, Methods.Chooser); SetStart(c, "native always off last 0");
                var home = HomePackage.Folder(MakeHome("mod", "k1"), "mod");
                AddonChoice Now(RecoveryConsole k) { var (st0, _, why0) = MethodEngine.Current(k, mctx); return why0 == null ? AddonChoice.FromState(st0) : null; }
                // read as it is (the pages start from this choice, sent back and forth as JSON)
                var con = OpenMethod(c); var cur = Now(con); var back = AddonChoice.FromJson(Json.Parse(Json.Write(cur?.ToJson())));
                Check(cur != null && cur.Auto == "last" && cur.Seconds == 0 && cur.AtStart && cur.StartLine == "native always off last 0" && back?.StartLine == cur.StartLine && back.SameAs(cur)
                      && cur.StartText.Contains("Nothing starts until you choose; the menu you used last is selected first") && cur.AutoText.StartsWith("Nothing starts until you choose."),
                      "Last Used with no countdown (set on the console) is read as it is, also through the pages' JSON: " + Flat(cur?.StartText));
                // another startup change made from it (CCS Mod added: CCS HOME offered): every other part of the start setting written back as it was
                var t1 = cur.Copy(); t1.Mod = true;
                var (p, r) = MethodEngine.Prepare(con, mctx, t1, home);
                Check(r == null && p.StartLine == "native always on last 0" && p.Steps.SequenceEqual(new[] { "method:ccs-mod", "nstart", "hput", "huse" }) && p.Text.Contains("the menu you used last is selected first"),
                      "CCS Mod added: the start setting changes only in offering CCS HOME; Last Used with no countdown stays (" + (p?.StartLine ?? Flat(r?.Problem)) + ")");
                var o = MethodEngine.Execute(con, p, p.Digest, p.Nonce, mctx); Gone(p.ProtectivePath); var ns = SimulatedConsole.NormalStart(c);
                Check(o.State == "scheduled" && Writes(c).Contains("nstart native always on last 0") && StartIn(c) == "native always on last 0" && Sorted(Installed(c)).SequenceEqual(new[] { "ccs-mod", "ccs-nucleus", "ccs-nucleus-chooser" }),
                      "written, read back and still so after the next start (" + o.Problem + "; " + StartIn(c) + ")");
                // the update alone (CCS Nucleus from 0.11.11): no start step at all, the setting stays through hakchi's install over it
                SimulatedConsole.SetAddonInstalled(c, "ccs-nucleus", OldNucleusPin); var n1 = Writes(c).Count;
                con = OpenMethod(c); var t2 = Now(con); t2.Update = true;
                (p, r) = MethodEngine.Prepare(con, mctx, t2, home);
                Check(r == null && p.Updated.SequenceEqual(new[] { "ccs-nucleus" }) && p.StartLine == null && p.Steps.SequenceEqual(new[] { "method:ccs-nucleus" }),
                      "an update alone: this version's CCS Nucleus placed, CCS Nucleus's start not written (" + (p != null ? Listed(p.Steps) : Flat(r?.Problem)) + ")");
                o = MethodEngine.Execute(con, p, p.Digest, p.Nonce, mctx); Gone(p.ProtectivePath); ns = SimulatedConsole.NormalStart(c);
                var after = Now(OpenMethod(c));
                Check(o.State == "scheduled" && Writes(c).Skip(n1).SequenceEqual(new[] { "method ccs-nucleus" }) && ns.Contains("install ccs-nucleus") && StartIn(c) == "native always on last 0"
                      && after?.StartLine == "native always on last 0",
                      "the next start installs this version over the earlier one; Last Used with no countdown is unchanged (" + Listed(ns) + "; " + StartIn(c) + ")");
                // the same with another change in the same review: a countdown of 20 seconds and the chooser not shown at start (both set on the
                // console), CCS Mod removed while CCS Nucleus is updated
                SetStart(c, "native never on default 20"); SimulatedConsole.SetAddonInstalled(c, "ccs-nucleus", OldNucleusPin); n1 = Writes(c).Count;
                con = OpenMethod(c); var t3 = Now(con);
                Check(t3 != null && !t3.AtStart && t3.Seconds == 20 && t3.Nucleus == "chooser" && t3.StartLine == "native never on default 20" && t3.StartText.Contains("does not show at start"),
                      "a 20-second countdown and the chooser not shown at start are read as they are: " + Flat(t3?.StartText));
                t3.Update = true; t3.Mod = false;
                (p, r) = MethodEngine.Prepare(con, mctx, t3, home);
                Check(r == null && p.StartLine == "native never off default 20" && p.Updated.SequenceEqual(new[] { "ccs-nucleus" }) && p.Remove.SequenceEqual(new[] { "ccs-mod" })
                      && p.Steps.SequenceEqual(new[] { "method:ccs-nucleus", "nstart", "unlist" }),
                      "CCS Mod removed and CCS Nucleus updated in one review: only CCS HOME's offer changes in the start setting (" + (p?.StartLine ?? Flat(r?.Problem)) + ")");
                o = MethodEngine.Execute(con, p, p.Digest, p.Nonce, mctx); Gone(p.ProtectivePath); ns = SimulatedConsole.NormalStart(c);
                Check(o.State == "scheduled" && Writes(c).Skip(n1).SequenceEqual(new[] { "method ccs-nucleus", "nstart native never off default 20", "unlist ccs-mod" }) && StartIn(c) == "native never off default 20",
                      "written and read back; after the next start the countdown and the chooser's setting are as before (" + o.Problem + "; " + StartIn(c) + ")");
                // its own system image as what opens first (set on the console): named, kept through another change
                File.WriteAllText(Path.Combine(c, "nucleus-registry.json"), Json.Write(new JObj { { "options", new JObj {
                    { "ccs-home", new JObj { { "kind", "ccs-home" } } }, { "my-snes", new JObj { { "kind", "owner-root" }, { "label", "My Other System" }, { "sha256", new string('a', 64) }, { "bytes", 1000L } } }, { "native", new JObj { { "kind", "native" } } } } } }));
                SetStart(c, "my-snes always off default 10"); n1 = Writes(c).Count;
                con = OpenMethod(c); var t4 = Now(con);
                Check(t4 != null && t4.Start == "my-snes" && t4.StartName == "My Other System" && t4.StartText.Contains("If you do nothing, My Other System starts in 10 seconds."),
                      "its own system image as what opens first is read and named as the console names it: " + Flat(t4?.StartText));
                var t5 = t4.Copy(); t5.Mod = true;
                (p, r) = MethodEngine.Prepare(con, mctx, t5, home);
                Check(r == null && p.StartLine == "my-snes always on default 10", "CCS Mod added: what opens first stays the console's own system image (" + (p?.StartLine ?? Flat(r?.Problem)) + ")");
                o = MethodEngine.Execute(con, p, p.Digest, p.Nonce, mctx); Gone(p.ProtectivePath);
                Check(o.State == "scheduled" && Writes(c).Skip(n1).Contains("nstart my-snes always on default 10") && StartIn(c) == "my-snes always on default 10", "written and read back (" + o.Problem + ")");
                // refused before anything: that image straight away without the chooser, and a menu the console does not have as what opens first
                int b0 = BackupFiles(sM); n1 = Writes(c).Count;
                var t6 = t4.Copy(); t6.Mod = true; t6.Nucleus = "direct";
                var (q, qr) = MethodEngine.Prepare(OpenMethod(c), mctx, t6, home);
                var t7 = t4.Copy(); t7.Mod = true; t7.Start = "other-image";
                var (q2, qr2) = MethodEngine.Prepare(OpenMethod(c), mctx, t7, home);
                Check(q == null && qr.Problem.Contains("opens first only from the startup chooser") && q2 == null && qr2.Problem.Contains("Choose what opens when the console is turned on") && BackupFiles(sM) == b0 && Writes(c).Count == n1,
                      "refused before any backup or write: its own system image straight away without the chooser (" + Flat(qr?.Problem) + "), a menu the console does not have (" + Flat(qr2?.Problem) + ")");
                GoneConsole(c); GoneBackups(sM);
            });

            // ---- the real console stays behind its own closed boundaries
            Check(CcsBackup.Gate.ConsoleSessions && CcsBackup.Gate.MethodWrites && RecoverySessions.Refusal(ImageKind.Method) == null, "a real console: the add-on boundary is open in this version (asked of the boundary itself: no test opens a real session or lists USB devices)");
            Check(CcsBackup.Gate.ConsoleSessions && CcsBackup.Gate.PreparationWrites && RecoverySessions.Refusal(ImageKind.Prepare) == null, "a real console: preparation's boundary is open in this version (asked of the boundary itself: no test opens a real session or lists USB devices)");
            var real = new WriteAuthority("x", HwidA, Methods.Steps, () => CcsBackup.Gate.MethodWrites, CcsBackup.Gate.MethodText);
            foreach (var step in Methods.Steps) { try { real.Require(step, HwidA); Check(true, "an add-on write (" + step + ") on a real console: admitted by its open boundary for the plan's own step and console"); } catch (Exception ex) { Check(false, "an add-on write (" + step + ") was refused: " + ex.Message); } }
            try { real.Require("no-such-step", HwidA); Check(false, "a step outside the plan was admitted"); } catch (InvalidOperationException) { Check(true, "an add-on write: a step outside the reviewed plan is refused"); }
            try { real.Require(Methods.Steps[0], HwidB); Check(false, "another console was admitted"); } catch (InvalidOperationException) { Check(true, "an add-on write: another console is refused"); }
            real.Revoke("an earlier step failed"); try { real.Require(Methods.Steps[0], HwidA); Check(false, "a revoked authority was admitted"); } catch (InvalidOperationException) { Check(true, "an add-on write: nothing more after an earlier failure"); }
        }
    }
}
