// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// CCS Mod and CCS Nucleus on a console Hakchi2 CE 3.9.3 prepared: independent add-ons, each added or removed through hakchi's own route.
//   CCS Mod             the CCS HOME tile in Original Menu (Nintendo's own menu) and CCS HOME's package; no power-on action of its own
//   CCS Nucleus         what opens at power-on: Original Menu or CCS HOME, started directly (package ccs-nucleus)
//   its chooser         a screen at start to choose, the owner's own system images and the menu entry (package ccs-nucleus-chooser;
//                       needs CCS Nucleus)
// The console's next normal start runs hakchi's p7080_hmods: it first uninstalls the packages named in $installpath/transfer/uninstall (in
// the order named; each with its own uninstall script, which removes only its own unchanged files), then installs transfer/*.hmod in
// sorted order and then transfer/hmod/*.hmod (the chooser waits there, so CCS Nucleus is installed before it), removes the folder and
// restarts. This engine places those files in the preparation image's session (ccsprep "method" and "unlist"), sets CCS Nucleus's start
// directly on the data storage with the image's own pinned CCS Nucleus program ("nstart"), and sends CCS HOME's package when CCS Mod is
// added, all under its own boundary Gate.MethodWrites; it belongs in Console Restore.
//   1 check     the console identified; hakchi 3.9.3 on its data storage; which add-ons are installed (hakchi's own package lists), CCS
//               Nucleus's start setting, and what already waits in the transfer folder (ccsprep mstate, read-only)
//   2 protect   a complete, verified backup of the console as it is now, in this session
//   3 plan      the add-ons to place (method, one per package), CCS Nucleus's start (nstart) when it stays or arrives, CCS HOME's package
//               when CCS Mod arrives (hput, huse; unless that exact package is in use), and one removal list for the add-ons to remove
//               (unlist: the chooser before CCS Nucleus). Anything already waiting must be exactly this plan's own files (an interrupted
//               earlier run is finished, not written twice); anything else waiting there is refused. A one-time confirmation
//   4 recheck   at the confirmation, before anything is used up or written: the protective backup held open read-only and verified again;
//               the console's state read again and still exactly what the plan saw; the operation record saved before the first write
//   5 write     each step once; each is read back from the storage; the first failure stops every later step
//   6 verify    the console's state read again: exactly the plan's files wait for the next start, and the start reads back as planned
// Nothing is retried or undone automatically. hakchi applies the add-ons at the console's next normal start (then restarts once).
// Update: an add-on installed in an earlier version (ccsprep mstate addon=NAME earlier) is brought to this build's package only when the
// person chooses it (AddonChoice.Update) and names it in the review: the same method step places this build's package, and hakchi's own
// pack_install installs it over the earlier one at the next start. Each package's install accepts only its own unchanged files, removes the
// files the new version no longer carries while unchanged, and keeps a valid registry: what opens at power-on, the last-used and countdown
// settings, the owner's system images and community menus in CCS Nucleus's list, games and saves stay as they are.
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
    /// <summary>the add-ons' packages (the preparation image carries them, pinned: its MANIFEST.json "methods")</summary>
    public static class Methods
    {
        public const string Mod = "ccs-mod", Nucleus = "ccs-nucleus", Chooser = "ccs-nucleus-chooser";
        public static readonly string[] Steps = { "method", "nstart", "unlist" };
        public static readonly string[] Packages = { Mod, Nucleus, Chooser };
        public static bool IsPackage(string n) => Array.IndexOf(Packages, n) >= 0;
        public static string Label(string package) => package == Mod ? "CCS Mod" : package == Nucleus ? "CCS Nucleus" : package == Chooser ? "CCS Nucleus's chooser" : "none";
        /// <summary>the package's SHA-256 as this build's preparation image carries it (null when the image does not carry it)</summary>
        public static string Sha256(string package) => RecoveryImages.MethodSha(package);
        /// <summary>the removal list hakchi reads (the names, in order, on one line), and its SHA-256</summary>
        public static string ListSha256(IEnumerable<string> packages) { using (var h = SHA256.Create()) return Hex.Of(h.ComputeHash(Encoding.ASCII.GetBytes(string.Join(" ", packages) + "\n"))); }
        /// <summary>where a package waits in hakchi's transfer folder (the chooser in hmod/, installed after CCS Nucleus)</summary>
        public static string Waiting(string package) => (package == Chooser ? "hmod/" : "") + package + ".hmod";
        /// <summary>the add-ons the console has in an earlier version than this build's (ccsprep mstate addon=NAME earlier), in install order</summary>
        public static List<string> Earlier(MethodState st) => Packages.Where(p => st.Installed(p) && st.AddonVersion(p) == "earlier").ToList();
    }

    /// <summary>the add-ons a console has, or should have: CCS Mod (the CCS HOME tile), CCS Nucleus (none, direct, or with its chooser) and
    /// what opens at power-on while CCS Nucleus is there (native: Original Menu; ccs-home: CCS HOME, which needs CCS Mod)</summary>
    public sealed class AddonChoice
    {
        public bool Mod; public string Nucleus = "none";
        /// <summary>with CCS Nucleus: what opens first (its registry's default): native (Original Menu), ccs-home (CCS HOME), or another option
        /// the console already has as its default (an owner's system image or a community menu, set on the console), which is kept as it is</summary>
        public string Start = "native";
        /// <summary>with CCS Nucleus: what starts by itself: "menu" (Start, after the countdown), "last" (the menu used last; with no countdown it is
        /// only selected first) or "wait" (no countdown: nothing starts until a choice)</summary>
        public string Auto = "menu";
        /// <summary>the countdown's seconds (CCS Nucleus's timeout, 1 to 99; 0 only with "last"). New choices count 10; a console's own value,
        /// as its startup chooser's settings set it, is kept unless the person changes that setting</summary>
        public int Seconds = Countdown;
        /// <summary>with its chooser: whether the chooser shows at every start (the chooser's own setting Show This Screen at Start); off, what
        /// opens first opens straight away and the chooser opens from CCS Nucleus in Original Menu or Choose Another Menu in CCS HOME</summary>
        public bool AtStart = true;
        /// <summary>the name of a kept default other than Original Menu and CCS HOME, as the console names it (shown, never written)</summary>
        public string StartName;
        /// <summary>bring the add-ons that stay, installed in an earlier version, to this build's packages (named in the review)</summary>
        public bool Update;
        public const int Countdown = 10;
        public static readonly string[] NucleusProfiles = { "none", "direct", "chooser" };
        static readonly Regex IdRe = new Regex("^[a-z0-9][a-z0-9-]{0,30}$");
        /// <summary>Original Menu or CCS HOME (the menus every page offers); anything else is a kept default</summary>
        public static bool Standard(string start) => start == "native" || start == "ccs-home";
        public string Selection => Auto == "last" ? "last" : "default";
        public int Timeout => Auto == "wait" ? 0 : Seconds;
        /// <summary>the startup chooser shows at power-on</summary>
        public bool Shown => Nucleus == "chooser" && AtStart;
        public AddonChoice Copy() => (AddonChoice)MemberwiseClone();
        public static AddonChoice FromState(MethodState st)
        {
            var sp = st.StartParts; bool nucleus = st.Installed(Methods.Nucleus);
            int t = sp == null ? Countdown : int.Parse(sp[4], CultureInfo.InvariantCulture);
            var c = new AddonChoice
            {
                Mod = st.Installed(Methods.Mod),
                Nucleus = !nucleus ? "none" : st.Installed(Methods.Chooser) ? "chooser" : "direct",
                Start = sp == null ? "native" : nucleus || Standard(sp[0]) ? sp[0] : "native",
                Auto = sp == null ? "menu" : sp[3] == "last" ? "last" : t == 0 ? "wait" : "menu",
                AtStart = sp == null || sp[1] == "always",
            };
            c.Seconds = sp == null || c.Auto == "wait" ? Countdown : t;
            if (!Standard(c.Start)) c.StartName = st.OptionName(c.Start);
            return c;
        }
        public static AddonChoice FromJson(Dictionary<string, object> j) => j == null ? null : new AddonChoice
        {
            Mod = j.B("mod"), Nucleus = j.S("nucleus") ?? "none", Start = j.S("start") ?? "native", Auto = j.S("auto") ?? "menu",
            Seconds = (int)Math.Max(-1, Math.Min(100, j.L("countdown", Countdown))), AtStart = !j.ContainsKey("at_start") || j.B("at_start"),
            StartName = j.S("start_name"), Update = j.B("update"),
        };
        /// <summary>null, or why this choice cannot be made</summary>
        public string Problem()
        {
            if (Array.IndexOf(NucleusProfiles, Nucleus) < 0) return "Choose whether CCS Nucleus is added, and how it starts.";
            if (Start == null || !IdRe.IsMatch(Start)) return "Choose what opens when the console is turned on.";
            if (Auto != "menu" && Auto != "last" && Auto != "wait") return "Choose what the startup chooser starts by itself.";
            if (Seconds < 0 || Seconds > 99 || Seconds == 0 && Auto == "menu") return "Choose how long the startup chooser counts down.";
            if (Nucleus != "none" && Start == "ccs-home" && !Mod) return "CCS HOME opens at power-on only with CCS Mod, which provides CCS HOME. Add CCS Mod, or choose Original Menu.";
            return null;
        }
        /// <summary>the start setting CCS Nucleus keeps for this choice: "DEFAULT CHOOSER HOME SELECTION TIMEOUT" (ccsprep nstart), null without
        /// CCS Nucleus. Every part comes from the choice, which starts from the console's own setting, so a part the person did not change is
        /// written exactly as the console has it</summary>
        public string StartLine => Nucleus == "none" ? null : Start + " " + (Shown ? "always" : "never") + " " + (Mod ? "on" : "off") + " " + Selection + " " + Timeout.ToString(CultureInfo.InvariantCulture);
        /// <summary>what opens first, by name</summary>
        public string StartMenuName => Start == "ccs-home" ? "CCS HOME" : Start == "native" ? "Original Menu" : StartName ?? "the menu set on the console";
        public static string SecondsText(int n) => n.ToString(CultureInfo.InvariantCulture) + (n == 1 ? " second" : " seconds");
        /// <summary>with CCS Nucleus: what happens at power-on after the chooser appears, or what opens straight away (the pages' words)</summary>
        public string AutoText
        {
            get
            {
                var name = StartMenuName;
                if (!Shown) return (Auto == "last" ? "The menu you used last opens straight away. Until you have used one, that is " + name + "." : name + " opens straight away.")
                    + (Nucleus == "chooser" ? " The startup chooser does not show at start; it opens from CCS Nucleus in Original Menu, or with Choose Another Menu in CCS HOME." : "");
                if (Timeout == 0) return Auto == "last" ? "Nothing starts until you choose. The menu you used last is selected first; until you have used one, that is " + name + "." : "Nothing starts until you choose.";
                return Auto == "last" ? "If you do nothing, the menu you used last starts in " + SecondsText(Seconds) + ". Until you have used one, that is " + name + "."
                    : "If you do nothing, " + name + " starts in " + SecondsText(Seconds) + ".";
            }
        }
        public string StartText
        {
            get
            {
                if (Nucleus == "none") return "When the console is turned on, Original Menu opens, as it does now." + (Mod ? " CCS HOME opens from its tile there." : "");
                var name = StartMenuName;
                if (!Shown) return "When the console is turned on, " + (Auto == "last" ? "the menu you used last opens straight away (" + name + " the first time)." : name + " opens straight away.")
                    + (Nucleus == "chooser" ? " The startup chooser does not show at start; it opens from CCS Nucleus in Original Menu, or with Choose Another Menu in CCS HOME." : " The startup chooser is off.");
                return "When the console is turned on, the startup chooser appears first. " + (Timeout == 0 ? (Auto == "last" ? "Nothing starts until you choose; the menu you used last is selected first (" + name + " the first time)." : "Nothing starts until you choose.")
                    : Auto == "last" ? "If you do nothing, the menu you used last starts in " + SecondsText(Seconds) + " (" + name + " the first time)." : "If you do nothing, " + name + " starts in " + SecondsText(Seconds) + ".");
            }
        }
        public JObj ToJson() => new JObj { { "mod", Mod }, { "nucleus", Nucleus }, { "start", Start }, { "auto", Auto }, { "countdown", Seconds }, { "at_start", AtStart }, { "start_name", StartName }, { "update", Update } };
        /// <summary>the add-on packages this choice keeps on the console</summary>
        public bool Keeps(string package) => package == Methods.Mod ? Mod : package == Methods.Nucleus ? Nucleus != "none" : package == Methods.Chooser && Nucleus == "chooser";
        /// <summary>the same add-ons and, with CCS Nucleus, exactly the same start setting</summary>
        public bool SameAs(AddonChoice o) => o != null && o.Mod == Mod && o.Nucleus == Nucleus && (Nucleus == "none" || o.StartLine == StartLine);
    }

    /// <summary>ccsprep mstate: hakchi's version, the hakchi packages installed, CCS Nucleus's start setting and what waits in hakchi's
    /// transfer folder</summary>
    public sealed class MethodState
    {
        public bool Ok; public string Problem, Hakchi, Registry; public bool TransferLink; public long Free = -1;
        /// <summary>CCS Nucleus's start setting (ccsprep mstate start): "DEFAULT CHOOSER HOME SELECTION TIMEOUT", none or invalid</summary>
        public string Start;
        public string[] StartParts => Start != null && Start.Split(' ').Length == 5 ? Start.Split(' ') : null;
        public string StartDefault => StartParts?[0];
        /// <summary>the registry's default option's label (ccsprep mstate start_label, base64; null when not reported)</summary>
        public string StartLabel64;
        /// <summary>an option's name as the console shows it: a community menu's label, or the default's label; null when unknown</summary>
        public string OptionName(string id)
        {
            if (Menus.TryGetValue(id, out var m)) return CommunityMenus.Label64Text(m.label64);
            if (id == StartDefault && !string.IsNullOrEmpty(StartLabel64))
                try { var l = Encoding.UTF8.GetString(Convert.FromBase64String(StartLabel64)).Trim(); return l.Length == 0 ? null : l; } catch (FormatException) { }
            return null;
        }
        /// <summary>CCS HOME's package on the console (ccsprep mstate home_*): the copy in use ("absent", "other" or a copy name), each recorded
        /// copy (its MANIFEST.json's SHA-256, whether its folder is there), each installation in progress (attempt -> copy), the saves'
        /// identity file, a link on the way (nothing is written through it), and the RAM free for a transfer (-1: not reported)</summary>
        public string HomePointer; public bool HomeLink; public byte[] HomeIdentity; public long Ram = -1;
        public SortedDictionary<string, (string manifest, bool dir)> HomeCopies = new SortedDictionary<string, (string, bool)>(StringComparer.Ordinal);
        public SortedDictionary<string, string> HomeAttempts = new SortedDictionary<string, string>(StringComparer.Ordinal);
        /// <summary>each recorded copy's details (mstate's home_copy_info): the copy in use before it, exact against its record, its files'
        /// bytes, the space it takes on the storage (KB), its collection name and games, and when it was installed (its record's installed_utc,
        /// this PC's time; null: not recorded), and whether an entry of Original Menu names its files (yes, no, unknown); for the
        /// earlier-versions review</summary>
        public SortedDictionary<string, (string previous, string state, long bytes, long kb, string label, long games, string installed, string named)> HomeInfo = new SortedDictionary<string, (string, string, long, long, string, long, string, string)>(StringComparer.Ordinal);
        /// <summary>the copy in use is a recorded copy of exactly this package (its MANIFEST.json's SHA-256)</summary>
        public bool HomeInUse(string manifestSha) => HomePointer != null && HomeCopies.TryGetValue(HomePointer, out var c) && c.manifest == manifestSha && c.dir;
        /// <summary>CCS Nucleus's registry options as the console reads them: id -> (kind, sha256, bytes)</summary>
        public SortedDictionary<string, (string kind, string sha256, long bytes)> Options = new SortedDictionary<string, (string, string, long)>(StringComparer.Ordinal);
        /// <summary>each community menu choice in CCS Nucleus's registry (ccsprep mstate menu=): id -> (its hakchi package, its program on the
        /// console, its label as base64)</summary>
        public SortedDictionary<string, (string package, string program, string label64)> Menus = new SortedDictionary<string, (string, string, string)>(StringComparer.Ordinal);
        /// <summary>each add-on on the console against this build's own package (ccsprep mstate addon=NAME STATE): "current" (its package record,
        /// and CCS Nucleus's program, are this build's), "earlier" (another version), "none" (hakchi does not list it)</summary>
        public SortedDictionary<string, string> AddonVersions = new SortedDictionary<string, string>(StringComparer.Ordinal);
        /// <summary>an add-on's state (null when the console did not say)</summary>
        public string AddonVersion(string package) => AddonVersions.TryGetValue(package, out var v) ? v : null;
        /// <summary>CCS Nucleus's: whether its installed program can read a community menu's choice ("current")</summary>
        public string NucleusProgram => AddonVersion(Methods.Nucleus);
        public List<string> Packages = new List<string>(); public SortedDictionary<string, string> Waiting = new SortedDictionary<string, string>(StringComparer.Ordinal);
        public bool Installed(string package) => Packages.Contains(package);
        public string Canonical() => "hakchi=" + Hakchi + "\n" + string.Join("", Packages.OrderBy(x => x, StringComparer.Ordinal).Select(p => "package=" + p + "\n")) + "start=" + Start + "\n"
            + string.Join("", Waiting.Select(w => "waiting=" + w.Key + " " + w.Value + "\n")) + (TransferLink ? "transfer=link\n" : "")
            + (StartLabel64 != null ? "start_label=" + StartLabel64 + "\n" : "") + "registry=" + Registry + "\n" + string.Join("", Options.Select(o => "option=" + o.Key + " " + o.Value.kind + " " + o.Value.sha256 + " " + o.Value.bytes.ToString(CultureInfo.InvariantCulture) + "\n"))
            + string.Join("", Menus.Select(m => "menu=" + m.Key + " " + m.Value.package + " " + m.Value.program + " " + m.Value.label64 + "\n")) + string.Join("", AddonVersions.Select(a => "addon=" + a.Key + " " + a.Value + "\n"))
            + (HomeLink ? "home=link\n" : "home_pointer=" + HomePointer + "\n" + string.Join("", HomeCopies.Select(c => "home_copy=" + c.Key + " " + c.Value.manifest + " " + (c.Value.dir ? "dir" : "nodir") + "\n"))
               + string.Join("", HomeInfo.Select(c => "home_copy_info=" + c.Key + " " + (c.Value.previous ?? "-") + " " + c.Value.state + " " + c.Value.bytes + " " + c.Value.kb + " " + (c.Value.label ?? "-") + " " + c.Value.games + " " + (c.Value.installed ?? "-") + " " + c.Value.named + "\n"))
               + string.Join("", HomeAttempts.Select(a => "home_attempt=" + a.Key + " " + a.Value + "\n")) + "home_identity=" + (HomeIdentity == null ? "absent" : Convert.ToBase64String(HomeIdentity)) + "\n");
        public string Digest() { using (var h = SHA256.Create()) return Hex.Of(h.ComputeHash(Encoding.UTF8.GetBytes(Canonical()))); }
        public JObj ToJson() => new JObj { { "hakchi", Hakchi }, { "packages", Packages.ToList() }, { "addons", Ok ? AddonChoice.FromState(this).ToJson() : null }, { "start", Start },
            { "waiting", Waiting.Select(w => (object)new JObj { { "name", w.Key }, { "sha256", w.Value } }).ToList() },
            { "free_bytes", Free }, { "registry", Registry }, { "options", Options.Select(o => (object)new JObj { { "id", o.Key }, { "kind", o.Value.kind }, { "sha256", o.Value.sha256 }, { "bytes", o.Value.bytes } }).ToList() },
            { "menus", Menus.Select(x => (object)new JObj { { "id", x.Key }, { "package", x.Value.package }, { "program", x.Value.program }, { "label", CommunityMenus.Label64Text(x.Value.label64) } }).ToList() }, { "addon_versions", AddonVersions.Select(a => (object)new JObj { { "name", a.Key }, { "state", a.Value } }).ToList() },
            { "home", new JObj { { "in_use", HomeLink ? "link" : HomePointer }, { "copies", HomeCopies.Select(c => (object)new JObj { { "copy", c.Key }, { "manifest_sha256", c.Value.manifest }, { "folder", c.Value.dir } }).ToList() },
                { "unfinished", HomeAttempts.Select(a => (object)new JObj { { "attempt", a.Key }, { "copy", a.Value } }).ToList() }, { "saves_identity", HomeIdentity != null }, { "ram_free_bytes", Ram } } },
            { "digest", Digest() } };
        static readonly Regex Name = new Regex("^(hmod/)?[A-Za-z0-9._+-]{1,120}$");
        public static MethodState Parse(string text, int code)
        {
            var m = new MethodState(); var lines = (text ?? "").Replace("\r", "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            if (code != 0 || lines.LastOrDefault() != "result mstate ok") { m.Problem = lines.LastOrDefault(l => l.StartsWith("result mstate ")) ?? "the console did not report its hakchi packages (status " + code + ")"; return m; }
            foreach (var l in lines)
            {
                var i = l.IndexOf('='); if (i <= 0) continue; var k = l.Substring(0, i); var v = l.Substring(i + 1);
                if (k == "hakchi") m.Hakchi = v;
                else if (k == "package") { if (!Name.IsMatch(v) || v.Contains("/")) { m.Problem = "an unexpected package name"; return m; } m.Packages.Add(v); }
                else if (k == "start") { if (!Regex.IsMatch(v, "^((native|ccs-home|[a-z0-9][a-z0-9-]{0,31}) (always|never) (on|off) (default|last) ([0-9]{1,2})|none|invalid)$")) { m.Problem = "an unexpected start setting"; return m; } m.Start = v; }
                else if (k == "start_label") { if (!Regex.IsMatch(v, "^[A-Za-z0-9+/]{0,160}={0,2}$")) { m.Problem = "an unexpected start setting name"; return m; } m.StartLabel64 = v; }
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
                else if (k == "menu")
                {
                    var s = v.Split(' ');
                    if (s.Length != 4 || !Regex.IsMatch(s[0], "^[a-z0-9][a-z0-9-]{0,31}$") || !CommunityCatalog.PackageRe.IsMatch(s[1]) || !Regex.IsMatch(s[2], "^/var/lib/hakchi/rootfs/[A-Za-z0-9._/-]{1,170}$") || !Regex.IsMatch(s[3], "^[A-Za-z0-9+/]{4,120}={0,2}$"))
                    { m.Problem = "an unexpected community menu choice"; return m; }
                    m.Menus[s[0]] = (s[1], s[2], s[3]);
                }
                else if (k == "addon") { var s2 = v.Split(' '); if (s2.Length != 2 || !Methods.IsPackage(s2[0]) || s2[1] != "current" && s2[1] != "earlier" && s2[1] != "none") { m.Problem = "an unexpected add-on version"; return m; } m.AddonVersions[s2[0]] = s2[1]; }
                else if (k == "home" && v == "link") m.HomeLink = true;
                else if (k == "home_pointer") { if (!(v == "absent" || v == "other" || HomePackage.CopyRe.IsMatch(v))) { m.Problem = "an unexpected HOME pointer"; return m; } m.HomePointer = v; }
                else if (k == "home_copy") { var s = v.Split(' '); if (s.Length != 3 || !HomePackage.CopyRe.IsMatch(s[0]) || !(Hex.IsHex(s[1], 64) || s[1] == "-") || s[2] != "dir" && s[2] != "nodir") { m.Problem = "an unexpected HOME copy record"; return m; } m.HomeCopies[s[0]] = (s[1] == "-" ? null : s[1], s[2] == "dir"); }
                else if (k == "home_copy_info")
                {
                    var s7 = v.Split(' '); long bb = -1, kk = -1, gg = -1; string lab = null;
                    bool ok7 = s7.Length == 9 && (s7[7] == "-" || HomeArchive.InstalledRe.IsMatch(s7[7])) && (s7[8] == "yes" || s7[8] == "no" || s7[8] == "unknown") && HomePackage.CopyRe.IsMatch(s7[0]) && (s7[1] == "-" || HomePackage.CopyRe.IsMatch(s7[1])) && (s7[2] == "exact" || s7[2] == "differs" || s7[2] == "nodir")
                        && (s7[3] == "-" || long.TryParse(s7[3], NumberStyles.None, CultureInfo.InvariantCulture, out bb)) && (s7[4] == "-" || long.TryParse(s7[4], NumberStyles.None, CultureInfo.InvariantCulture, out kk))
                        && (s7[6] == "-" || long.TryParse(s7[6], NumberStyles.None, CultureInfo.InvariantCulture, out gg));
                    if (!ok7) { m.Problem = "an unexpected HOME copy detail"; return m; }
                    if (s7[5] != "-") { try { lab = Json.Parse("{\"l\": \"" + Encoding.UTF8.GetString(Convert.FromBase64String(s7[5])) + "\"}").S("l"); } catch (Exception) { lab = null; } }
                    m.HomeInfo[s7[0]] = (s7[1] == "-" ? null : s7[1], s7[2], bb, kk, lab, gg, s7[7] == "-" ? null : s7[7], s7[8]);
                }
                else if (k == "home_attempt") { var s = v.Split(' '); if (s.Length != 2 || !Hex.IsHex(s[0], 8) || !(HomePackage.CopyRe.IsMatch(s[1]) || s[1] == "-")) { m.Problem = "an unexpected HOME installation record"; return m; } m.HomeAttempts[s[0]] = s[1]; }
                else if (k == "home_identity") { if (v == "absent") m.HomeIdentity = null; else try { m.HomeIdentity = Convert.FromBase64String(v); } catch (FormatException) { m.Problem = "an unreadable saves identity"; return m; } }
                else if (k == "ram") { if (!long.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out m.Ram)) m.Ram = -1; }
            }
            if (m.Hakchi == null || m.Start == null || m.Registry == null || m.HomePointer == null && !m.HomeLink) { m.Problem = "the console's answer was incomplete"; return m; }
            m.Ok = true; return m;
        }
    }

    public sealed class MethodPlan
    {
        public string Hwid, ConsoleTag, Session, Digest, Nonce, Refusal, Text; public DateTime ExpiresUtc;
        public AddonChoice Current, Target;
        /// <summary>the packages placed for hakchi to install (in this order), the removal list (in this order) and its SHA-256, and CCS Nucleus's
        /// start ("DEFAULT CHOOSER HOME") when it is set</summary>
        public List<string> Add = new List<string>(), Remove = new List<string>(); public string ListSha256, StartLine;
        /// <summary>the add-ons installed in an earlier version that this build's packages are placed over (hakchi installs them over the earlier ones)</summary>
        public List<string> Updated = new List<string>();
        public BoundArchive ProtectiveArchive; public MethodState Before;
        /// <summary>CCS HOME's package when CCS Mod is added, sent after the add-ons' files; null when that exact package is already in use</summary>
        public HomeArchive Home; public string HomePrior;
        /// <summary>earlier CCS HOME versions after the switch: the copies the setting keeps and the ones removed (hdrop:COPY steps)</summary>
        public EarlierPlan Earlier;
        public string ProtectivePath => ProtectiveArchive?.Path;
        /// <summary>the steps in order: "method:PACKAGE", "nstart", "unlist", then CCS HOME's (hput, huse)</summary>
        public List<string> Steps = new List<string>(), Notes = new List<string>();
        public JObj ToJson() => new JObj
        {
            { "operation", "addons" }, { "current", Current?.ToJson() }, { "target", Target?.ToJson() }, { "console_tag", ConsoleTag }, { "text", Text }, { "steps", Steps }, { "notes", Notes }, { "refusal", Refusal },
            { "add", Add.Select(p => (object)new JObj { { "name", p }, { "sha256", Methods.Sha256(p) } }).ToList() },
            { "update", Updated.Select(p => (object)new JObj { { "name", p }, { "label", Methods.Label(p) }, { "sha256", Methods.Sha256(p) } }).ToList() },
            { "removal_list", Remove.Count == 0 ? null : new JObj { { "names", string.Join(" ", Remove) }, { "sha256", ListSha256 } } }, { "start", StartLine },
            { "home", Home == null ? null : Home.ToJson().Set("replaces", HomePrior) }, { "earlier_home", Earlier?.ToJson() },
            { "console_state", Before?.ToJson() }, { "protective_backup", ProtectivePath }, { "evidence", new List<object> { ProtectiveArchive?.ToJson() } },
            { "digest", Digest }, { "nonce", Nonce }, { "expires_utc", ExpiresUtc.ToString("o", CultureInfo.InvariantCulture) },
        };
    }

    public sealed class MethodOutcome { public string State, Problem, RecordFile; public List<string> Done = new List<string>(); public MethodPlan Plan; public bool Ok => State == "scheduled"; }

    public static class MethodEngine
    {
        public const string Subject = "ccs-method";
        /// <summary>a step in plain words</summary>
        public static string StepLabel(string s, MethodPlan p = null)
        {
            if (s.StartsWith("method:", StringComparison.Ordinal)) return Methods.Label(s.Substring(7)) + "'s package placed for hakchi to install at the next start";
            if (s == "nstart") return "What opens at power-on set in CCS Nucleus";
            if (s == "unlist") return "Removal list placed for hakchi (" + (p == null ? "the add-ons to remove" : string.Join(", ", p.Remove.Select(Methods.Label))) + ")";
            return HomePackage.StepLabel(s);
        }

        /// <summary>the review's words about the earlier CCS HOME versions: how many the setting keeps and which ones go after the switch</summary>
        public static string EarlierNote(EarlierPlan e)
        {
            var go = e.Remove; var kept = e.Copies.Where(c => c.Copy != e.Current && !go.Contains(c.Copy)).ToList();
            return "Earlier HOME versions: this console keeps " + (e.Keep == 0 ? "none" : e.Keep == 1 ? "one" : e.Keep.ToString(System.Globalization.CultureInfo.InvariantCulture)) + " (Settings > CCS HOME > Earlier HOME versions). "
                + (kept.Count == 0 ? "No earlier version stays." : kept.Count + " earlier version" + (kept.Count == 1 ? " stays" : "s stay") + ". ")
                + (go.Count == 0 ? "" : go.Count + " earlier version" + (go.Count == 1 ? " is" : "s are") + " removed after the new one is in use and read back, each checked against its record first; the backup made just now keeps " + (go.Count == 1 ? "it" : "them") + ".");
        }

        /// <summary>the method steps' expected answers: "ok SHA" or "ok already SHA" (the same bytes were already waiting)</summary>
        internal static bool Answer(WriteResult r, string step, string sha) => r.Ok && Regex.IsMatch(r.Line ?? "", "^result " + step + " ok( already)? " + sha + "$");
        internal static bool StartAnswer(WriteResult r, string line) => r.Ok && (r.Line == "result nstart ok " + line || r.Line == "result nstart ok already " + line);

        /// <summary>what already waits in the transfer folder may be only this plan's own files (an interrupted earlier run): null, or the reason</summary>
        internal static string WaitingProblem(MethodState s, IDictionary<string, string> mine)
        {
            if (s.TransferLink) return "hakchi's transfer folder is a link";
            var other = s.Waiting.Where(w => !mine.TryGetValue(w.Key, out var sha) || sha != w.Value).Select(w => w.Key).ToList();
            return other.Count == 0 ? null : "something else already waits in hakchi's transfer folder for the next start (" + string.Join(", ", other) + ")";
        }
        static Dictionary<string, string> Mine(MethodPlan p)
        {
            var mine = p.Add.Concat(p.Updated).ToDictionary(Methods.Waiting, Methods.Sha256, StringComparer.Ordinal);
            if (p.Remove.Count > 0) mine["uninstall"] = p.ListSha256;
            return mine;
        }

        /// <summary>the console's add-ons as they are now, read before anything is chosen (phase 1 only: no write, no backup), so the add-on
        /// pages start from what the console has. refusal: why its add-ons cannot be changed (then st and id may be null)</summary>
        public static (MethodState st, ConsoleIdentity id, string refusal) Current(RecoveryConsole con, EngineContext ctx)
        {
            if (con.Profile != SessionProfile.Prepare) return (null, null, "The console is not in a preparation session.");
            ConsoleIdentity id;
            try { id = con.Identify(); Support.CheckForBackup(id, ctx.RecoveryImageId, "addons"); }
            catch (ConsoleRefusedException ex) { return (null, null, ex.Message); }
            var dw = ConsoleData.Refusal(id); if (dw != null) return (null, id, dw + " Nothing was changed.");
            var st = con.PrepState();
            if (!st.Ok) return (null, id, "The console's hakchi packages could not be read (" + st.Problem + "). Nothing was changed.");
            if (st.Hakchi != Preparation.PinnedHakchiVersion) return (st, id, HakchiRequirement.Refusal(HakchiRequirement.Detected(st.Hakchi)));
            return (st, id, null);
        }

        /// <summary>the add-on pages' starting point: the console's add-ons and what it starts now (operation "method" or "prepare";
        /// situation "hakchi": Hakchi2 CE 3.9.3's hakchi with these add-ons; "original": the original Nintendo software, no add-ons yet;
        /// "other": anything else, which the plan explains)</summary>
        public static JObj StateEvent(string operation, string situation, ConsoleIdentity id, MethodState st)
        {
            var cur = st != null && st.Ok ? AddonChoice.FromState(st) : new AddonChoice();
            var others = st == null ? new List<string>() : st.Packages.Where(p => !Methods.IsPackage(p)).ToList();
            return new JObj
            {
                { "event", "addon-state" }, { "operation", operation }, { "situation", situation }, { "console_tag", id?.ShortTag },
                { "current", cur.ToJson() }, { "start", st?.Start }, { "start_text", cur.Nucleus == "none" ? null : cur.StartText },
                { "start_kind", st != null && cur.Nucleus != "none" && !AddonChoice.Standard(cur.Start) && st.Options.TryGetValue(cur.Start, out var so) ? so.kind : null },
                { "installed", (st?.Packages ?? new List<string>()).Where(Methods.IsPackage).Cast<object>().ToList() }, { "other_packages", others.Count },
                { "own_images", st == null ? 0 : st.Options.Values.Count(x => x.kind == "owner-root") },
                { "community_menus", st == null ? 0 : st.Options.Values.Count(x => x.kind == "community") },
                { "earlier", (st == null ? new List<string>() : Methods.Earlier(st)).Select(x => (object)new JObj { { "name", x }, { "label", Methods.Label(x) } }).ToList() },
            };
        }

        /// <summary>Phases 1 to 3 (no write to the console; the protective backup on the PC). target: the add-ons the console should have;
        /// home: CCS HOME's package when CCS Mod is added (null: the one this build carries)</summary>
        public static (MethodPlan plan, MethodOutcome refused) Prepare(RecoveryConsole con, EngineContext ctx, AddonChoice target, HomePackage home = null)
        {
            var o = new MethodOutcome();
            MethodOutcome Refuse(string why) { o.State = "refused"; o.Problem = why; return o; }
            if (con.Profile != SessionProfile.Prepare) return (null, Refuse("The console is not in a preparation session."));
            if (target == null || target.Problem() != null) return (null, Refuse(target?.Problem() ?? "Choose the add-ons."));
            var (st, id, why0) = Current(con, ctx);
            if (why0 != null) return (null, Refuse(why0));
            var cur = AddonChoice.FromState(st);
            // what opens first other than Original Menu and CCS HOME: only the console's own default, kept as it is (the pages never offer another)
            if (target.Nucleus != "none" && !AddonChoice.Standard(target.Start))
            {
                if (cur.Nucleus == "none" || target.Start != cur.Start || !st.Options.TryGetValue(target.Start, out var ko)) return (null, Refuse("Choose what opens when the console is turned on: Original Menu or CCS HOME."));
                target.StartName = cur.StartName;
                if (ko.kind == "owner-root" && !target.Shown)
                    return (null, Refuse(target.StartMenuName + " opens first only from the startup chooser at start. Choose Original Menu or CCS HOME to open first, or keep the startup chooser showing at start."));
            }
            var earlier = Methods.Earlier(st).Where(target.Keeps).ToList();
            if (cur.SameAs(target) && (target.StartLine == null || st.Start == target.StartLine) && !(target.Update && earlier.Count > 0))
                return (null, Refuse("This console already has exactly these add-ons" + (earlier.Count > 0 ? ", but " + string.Join(" and ", earlier.Select(Methods.Label)) + (earlier.Count == 1 ? " is" : " are") + " an earlier version. Choose Update to this version to bring " + (earlier.Count == 1 ? "it" : "them") + " up to date." : ". Nothing to change.")));
            var p = new MethodPlan { Hwid = id.Hwid, ConsoleTag = id.ShortTag, Session = con.SessionId, Before = st, Current = cur, Target = target };
            // the add-ons to place, in the order hakchi installs them (CCS Nucleus before its chooser)
            if (target.Mod && !cur.Mod) p.Add.Add(Methods.Mod);
            if (target.Nucleus != "none" && cur.Nucleus == "none") p.Add.Add(Methods.Nucleus);
            if (target.Nucleus == "chooser" && cur.Nucleus != "chooser") p.Add.Add(Methods.Chooser);
            // an update: this build's packages placed over the earlier ones the person chose to update (hakchi installs them over)
            if (target.Update) foreach (var a in earlier) if (!p.Add.Contains(a)) p.Updated.Add(a);
            foreach (var a in p.Add.Concat(p.Updated)) if (!Hex.IsHex(Methods.Sha256(a), 64)) return (null, Refuse("This build does not carry " + Methods.Label(a) + "'s package."));
            // the removal list, in the order hakchi runs each package's own removal (the chooser before CCS Nucleus)
            if (cur.Nucleus == "chooser" && target.Nucleus != "chooser") p.Remove.Add(Methods.Chooser);
            if (cur.Nucleus != "none" && target.Nucleus == "none") p.Remove.Add(Methods.Nucleus);
            if (cur.Mod && !target.Mod) p.Remove.Add(Methods.Mod);
            foreach (var a in Methods.Packages.Where(x => p.Add.Contains(x) || p.Updated.Contains(x))) p.Steps.Add("method:" + a);
            if (!target.Update && earlier.Count > 0)
                p.Notes.Add(string.Join(" and ", earlier.Select(Methods.Label)) + " on this console " + (earlier.Count == 1 ? "is" : "are") + " an earlier version than this Workbench's and " + (earlier.Count == 1 ? "stays" : "stay") + " as " + (earlier.Count == 1 ? "it is" : "they are") + ". Choose Update to this version to bring " + (earlier.Count == 1 ? "it" : "them") + " up to date (a community menu needs this version of CCS Nucleus).");
            // what opens at power-on, set now in CCS Nucleus's registry (its install keeps it); not when CCS Nucleus is removed
            // (an update alone keeps the start setting exactly as it is: the page's choice cannot express every setting, such as Last Used with no countdown)
            if (target.Nucleus != "none" && st.Start != target.StartLine && !(target.Update && cur.SameAs(target))) { p.StartLine = target.StartLine; p.Steps.Add("nstart"); }
            if (p.Remove.Count > 0) { p.ListSha256 = Methods.ListSha256(p.Remove); p.Steps.Add("unlist"); }
            if (target.Nucleus != "chooser" && cur.Nucleus == "chooser" && st.Options.Values.Any(x => x.kind == "owner-root"))
                p.Notes.Add("Your own system images stay on the console as they are; without CCS Nucleus's chooser they do not start (only a choice on the TV starts one).");
            if (target.Nucleus == "direct" && cur.Nucleus == "chooser" && st.Menus.Count > 0)
                p.Notes.Add("Your community menus stay installed and stay listed in CCS Nucleus. Without its chooser, a menu starts only when it is what opens at power-on; leaving it opens Original Menu.");
            if (target.Nucleus == "none" && cur.Nucleus != "none" && st.Menus.Count > 0)
                p.Notes.Add("Your community menus stay installed, but without CCS Nucleus nothing starts them. Remove them in Community Mods, or keep CCS Nucleus.");
            // CCS HOME's package when CCS Mod arrives, after the add-ons' files (an interruption before it leaves CCS HOME as it was)
            if (target.Mod && !cur.Mod)
            {
                HomeSteps.Decision hd;
                try { hd = HomeSteps.Plan(home ?? HomePackage.Embedded(HomePackage.ModLayout), st, id.Hwid, id.Model?.Id ?? StorageSpec.NesModel); }
                catch (HomePackageException ex) { hd = new HomeSteps.Decision { Refusal = "CCS HOME's package cannot be used: " + ex.Message }; }
                if (hd.Refusal != null) return (null, Refuse(hd.Refusal));
                if (hd.Steps.Count > 0) { p.Home = hd.Archive; p.HomePrior = hd.Prior; p.Steps.AddRange(hd.Steps); }
                if (hd.Text != null) p.Notes.Add(hd.Text);
            }
            var wp = WaitingProblem(st, Mine(p));
            if (wp != null) return (null, Refuse("Changing the add-ons is refused: " + wp + ". Start the console normally once so hakchi applies it, then check again."));
            p.Text = string.Join(" ", new[] {
                p.Add.Count == 0 ? null : "Add " + string.Join(" and ", p.Add.Select(Methods.Label)) + ": hakchi installs " + (p.Add.Count == 1 ? "it" : "them") + " at the console's next start.",
                p.Updated.Count == 0 ? null : UpdateText(p.Updated),
                p.Remove.Count == 0 ? null : "Remove " + string.Join(" and ", p.Remove.Select(Methods.Label)) + ": hakchi runs " + (p.Remove.Count == 1 ? "its" : "each one's") + " own removal at that start, which removes only " + (p.Remove.Count == 1 ? "its" : "their") + " own unchanged files.",
                target.StartText }.Where(x => x != null));
            ctx.Say("protect", "Backing up the console as it is now, before anything changes...");
            var prot = CaptureEngine.Run(con, ctx);
            if (!prot.Ok || prot.Verification == null || !prot.Verification.Ok) { o.State = "stopped"; o.Problem = "The backup of the console as it is now did not complete (" + (prot.Problem ?? prot.State) + "), so nothing was written."; return (null, o); }
            if (!ConsoleIdentity.SameConsole(prot.Verification.Hwid, id.Hwid) || prot.Verification.Simulated != con.Simulated || !prot.Verification.RestoreReady) return (null, Refuse("The backup made just now is not a complete backup of this console, so nothing was written."));
            p.ProtectiveArchive = BoundArchive.From("backup made just now, before changing the add-ons", prot.PrimaryPath, prot.Verification, con.SessionId);
            if (p.Home != null)   // the earlier versions the setting keeps stay; the others go after the switch, each checked again on the console
            {
                p.Earlier = EarlierHome.Decide(EarlierHome.FromState(st), p.Home.Copy, p.HomePrior == "absent" ? null : p.HomePrior, ctx.Settings?.EarlierHomeKeep ?? StorageSpec.EarlierKeep(null));
                foreach (var c in p.Earlier.Remove) p.Steps.Add("hdrop:" + c);
                p.Notes.Add(EarlierNote(p.Earlier));
            }
            p.Notes.Add("Only files on the console's data storage are written: hakchi's transfer folder" + (p.StartLine != null ? ", CCS Nucleus's settings" : "") + (p.Home != null ? " and CCS HOME's folder" : "") + ". No kernel, bootloader or system is changed.");
            p.Notes.Add("Nothing else changes until the console starts normally: then hakchi applies the add-ons and restarts the console once.");
            p.Notes.Add("Games, saves, settings, other packages and system images are kept.");
            p.Notes.Add("The backup just made is checked again when you confirm and stays locked until the change ends; if it is missing or changed, nothing is written.");
            p.Nonce = Hex.Of(Guid.NewGuid().ToByteArray()); p.ExpiresUtc = DateTime.UtcNow + RestoreEngine.ConfirmWindow;
            p.Digest = DigestOf(p);
            return (p, null);
        }

        /// <summary>the review's words for an update of earlier add-ons to this build's packages</summary>
        public static string UpdateText(IList<string> updated) => "Update " + string.Join(" and ", updated.Select(Methods.Label)) + " to this version: hakchi installs " + (updated.Count == 1 ? "it" : "them") + " over the earlier one at the console's next start. "
            + "What opens at power-on, the last-used and countdown settings, your system images and community menus in CCS Nucleus's list, games and saves are kept.";

        static string DigestOf(MethodPlan p)
        {
            using (var h = SHA256.Create())
                return Hex.Of(h.ComputeHash(Encoding.UTF8.GetBytes("ccs-addon-plan/2\n" + ConsoleIdentity.NormalizeHwid(p.Hwid) + "\n" + p.Session + "\n" + Json.Write(p.Target.ToJson())
                    + string.Join(",", p.Add.Select(a => a + " " + Methods.Sha256(a))) + "\n" + string.Join(",", p.Updated.Select(a => a + " " + Methods.Sha256(a))) + "\n" + string.Join(" ", p.Remove) + " " + p.ListSha256 + "\n" + p.StartLine + "\n" + p.Before?.Digest() + "\n"
                    + p.ProtectiveArchive?.DigestLine() + "\n" + string.Join(",", p.Steps) + "\n" + (p.Home?.DigestLine() ?? "-") + " " + (p.HomePrior ?? "-") + "\n" + p.Nonce + "\n" + p.ExpiresUtc.Ticks)));
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
                if (!now.Ok || now.Digest() != plan.Before.Digest()) return Refuse("The console's hakchi packages, CCS Nucleus's settings, CCS HOME's copies or its transfer folder changed since the review" + (now.Ok ? "" : " (" + now.Problem + ")") + ". Nothing was changed; review the change again.");
                Record rec;
                try { rec = Record.Start(ctx.Settings.RecordsFolder, Guid.NewGuid().ToString("N").Substring(0, 12), "method", new JObj { { "plan", plan.ToJson() }, { "console_tag", plan.ConsoleTag }, { "simulated", con.Simulated } }); }
                catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException) { return Refuse("The change's record could not be saved in " + ctx.Settings.RecordsFolder + " (" + ex.Message + "), so nothing was written."); }
                plan.Nonce = null;   // used up only now
                o.RecordFile = rec.File; var phases = new List<object>();
                var targets = plan.Steps.Select(s => s.StartsWith("method:", StringComparison.Ordinal) ? "method" : HomeSteps.Target(s)).Distinct().ToArray();
                var auth = new WriteAuthority(plan.Digest, plan.Hwid, targets, () => con.Simulated || Gate.MethodWrites, Gate.MethodText);
                rec.State("writing"); con.Card("writing");
                foreach (var s in plan.Steps)
                {
                    if (ctx.Cancelled()) { rec.State("stopped", "canceled between steps"); o.State = "stopped"; o.Problem = "Canceled. Steps done: " + (o.Done.Count == 0 ? "none" : string.Join("; ", o.Done.Select(x => StepLabel(x, plan)))) + ". Nothing else was written."; return o; }
                    ctx.Say("method", StepLabel(s, plan) + "... (do not unplug the console)");
                    phases.Add(new JObj { { "step", s }, { "state", "began" }, { "at", Record.Now() } }); rec.Set("phases", phases).Save();
                    WriteResult r; bool ok;
                    try
                    {
                        if (s.StartsWith("method:", StringComparison.Ordinal)) { var pk = s.Substring(7); r = con.PrepStep("method", auth, new[] { pk, Methods.Sha256(pk) }); ok = Answer(r, "method", Methods.Sha256(pk)); }
                        else if (s == "nstart") { r = con.PrepStep("nstart", auth, plan.StartLine.Split(' ')); ok = StartAnswer(r, plan.StartLine); }
                        else if (s == "unlist") { r = con.PrepStep("unlist", auth, plan.Remove.ToArray()); ok = Answer(r, "unlist", plan.ListSha256); }
                        else { r = HomeSteps.Run(con, auth, s, plan.Home, plan.HomePrior); ok = HomeSteps.Answer(r, s, plan.Home); }
                    }
                    catch (GateClosedException ex) { rec.State("refused", ex.Message); o.State = "refused"; o.Problem = ex.Message; return o; }
                    phases.Add(new JObj { { "step", s }, { "state", ok ? ((r.Line ?? "").Contains(" ok already ") ? "already-done" : "done-and-read-back") : "failed" }, { "detail", r.Line ?? r.Problem }, { "at", Record.Now() } }); rec.Set("phases", phases).Save();
                    ctx.At("method-" + s);
                    if (!ok)
                    {
                        auth.Revoke(s + ": " + (r.Problem ?? r.Line)); rec.State("stopped", r.Problem ?? r.Line); con.Card("stopped"); o.State = "stopped";
                        o.Problem = "Changing the add-ons stopped at: " + StepLabel(s, plan) + " (" + (r.Problem ?? r.Line) + "). Nothing more was written and nothing was retried. Steps done before: " + (o.Done.Count == 0 ? "none" : string.Join("; ", o.Done.Select(x => StepLabel(x, plan)))) + ".";
                        return o;
                    }
                    o.Done.Add(s);
                }
                var after = con.PrepState();
                bool waits = after.Ok && plan.Add.Concat(plan.Updated).All(a => after.Waiting.TryGetValue(Methods.Waiting(a), out var sha) && sha == Methods.Sha256(a))
                             && (plan.Remove.Count == 0 || after.Waiting.TryGetValue("uninstall", out var ls) && ls == plan.ListSha256) && WaitingProblem(after, Mine(plan)) == null;
                if (!waits) { rec.State("stopped", "the transfer folder does not read back as planned"); o.State = "stopped"; o.Problem = "Every step reported success, but hakchi's transfer folder does not read back as planned. It was not changed again; check the console before its next start."; return o; }
                if (plan.StartLine != null && after.Start != plan.StartLine) { rec.State("stopped", "CCS Nucleus's start does not read back"); o.State = "stopped"; o.Problem = "Every step reported success, but CCS Nucleus's start setting does not read back as planned (" + after.Start + "). It was not changed again."; return o; }
                if (plan.Home != null && !HomeSteps.InUse(after, plan.Home)) { rec.State("stopped", "CCS HOME's package does not read back in use"); o.State = "stopped"; o.Problem = "Every step reported success, but the console does not report CCS HOME's new package in use. It was not changed again."; return o; }
                if (plan.Earlier != null && plan.Earlier.Remove.Any(c => after.HomeCopies.ContainsKey(c))) { rec.State("stopped", "an earlier CCS HOME copy still reads back"); o.State = "stopped"; o.Problem = "Every step reported success, but an earlier CCS HOME version that was removed still reads back on the console. Nothing more was changed."; return o; }
                rec.Set("after", new JObj { { "console_state", after.ToJson() }, { "applies", "at the console's next normal start (hakchi's p7080_hmods)" }, { "target", plan.Target.ToJson() },
                    { "earlier_home_removed", plan.Earlier == null ? null : plan.Earlier.Remove.Cast<object>().ToList() }, { "earlier_home_restore", plan.Earlier == null || plan.Earlier.Remove.Count == 0 ? null : EarlierHome.RestoreText } });
                rec.State("scheduled"); con.Card("done"); o.State = "scheduled"; return o;
            }
        }
    }
}
