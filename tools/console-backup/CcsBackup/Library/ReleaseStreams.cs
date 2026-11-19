// Classic Cart Studio console backup and restore engine: which published release an update check offers. Copyright (C) 2026 Rance9.
// GPL-3.0-or-later.
// The website's release streams (release-streams.json, written from this project's GitHub releases by tools/publish/release_streams.py)
// list the published releases of Classic Cart Studio ("app") and of CCS Console Workbench ("workbench"), each in two channels
// ("production", "preview"). A check reads only its own product's stream of its own channel, checks every entry (its product, channel,
// tag and file) before comparing versions, compares versions as numbers (0.11.10 is newer than 0.11.9), offers only a newer version and
// never a lower one, and answers "could not check" whenever the answer cannot be used: never "up to date". An app release is a Studio
// bundle: when it names its bundled Workbench, a Workbench older than the installed one, or a preview Workbench in a production release, is
// not offered. Pure: no network, no files (the Workbench window reads the answer, WorkbenchUpdates.cs).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace CcsBackup.Library
{
    public enum UpdateState { CouldNotCheck, UpToDate, Available }

    /// <summary>an update check's answer: State, the running version, the newest version this program may take (Newest; null when the
    /// stream lists none), Ahead when the running version is newer than every release listed, and Problem (why it could not check)</summary>
    public sealed class UpdateAnswer
    {
        public UpdateState State; public string Product, Channel, Running, Newest, Problem; public bool Ahead;
        internal static UpdateAnswer Fail(string product, string channel, string running, string why) =>
            new UpdateAnswer { State = UpdateState.CouldNotCheck, Product = product, Channel = channel, Running = running, Problem = why };
    }

    public static class ReleaseStreams
    {
        public const string Schema = "ccs-release-streams/1";
        public static readonly string[] Channels = { "production", "preview" };

        /// <summary>a plain version N.N.N as numbers (each part 1 to 4 digits, written without leading zeros), else null</summary>
        public static int[] Version(string s)
        {
            if (s == null || !Regex.IsMatch(s, @"^\d{1,4}\.\d{1,4}\.\d{1,4}$")) return null;
            var v = s.Split('.').Select(x => int.Parse(x, CultureInfo.InvariantCulture)).ToArray();
            return string.Join(".", v.Select(x => x.ToString(CultureInfo.InvariantCulture))) == s ? v : null;
        }

        public static int Compare(int[] a, int[] b)
        {
            for (int i = 0; i < 3; i++) if (a[i] != b[i]) return a[i].CompareTo(b[i]);
            return 0;
        }

        static string TagPrefix(string product) => product == "app" ? "v" : "workbench-v";
        static string MainFile(string product, string v) => product == "app" ? "ClassicCartStudio-Setup-" + v + ".exe" : "CCS-Console-Workbench-" + v + ".exe";

        /// <summary>the answer for one program: product "app" or "workbench", its channel and running version; installedWorkbench: for the
        /// app stream, the version of the Workbench installed with Classic Cart Studio (null when not known)</summary>
        public static UpdateAnswer Plan(string json, string product, string channel, string running, string installedWorkbench = null)
        {
            var cur = Version(running);
            if ((product != "app" && product != "workbench") || !Channels.Contains(channel) || cur == null)
                return UpdateAnswer.Fail(product, channel, running, "this program's version or channel is not known");
            Dictionary<string, object> doc;
            try { doc = Json.Parse(json); } catch (Exception) { return UpdateAnswer.Fail(product, channel, running, "the answer is not readable"); }
            if (!(doc.TryGetValue("schema", out var sc) && sc as string == Schema)) return UpdateAnswer.Fail(product, channel, running, "the answer is not release information");
            if (!(doc.TryGetValue("complete", out var cp) && cp is bool b && b)) return UpdateAnswer.Fail(product, channel, running, "the release information is incomplete");
            var streams = (doc.TryGetValue("streams", out var so) ? so as object[] : null);
            if (streams == null) return UpdateAnswer.Fail(product, channel, running, "the release information lists no streams");
            var mine = streams.OfType<Dictionary<string, object>>().Where(s => S(s, "product") == product && S(s, "channel") == channel).ToList();
            if (mine.Count != 1 || !(mine[0].TryGetValue("releases", out var ro) && ro is object[] listed)) return UpdateAnswer.Fail(product, channel, running, "the release information has no " + product + "/" + channel + " stream");

            var ok = new List<(int[] v, Dictionary<string, object> e)>();
            foreach (var e in listed.OfType<Dictionary<string, object>>())
            {
                if (S(e, "product") != product || S(e, "channel") != channel) continue;   // checked before any version is compared
                var vs = S(e, "version"); var v = Version(vs);
                if (v == null || S(e, "tag") != TagPrefix(product) + vs) continue;
                if (!(e.TryGetValue("asset", out var ao) && ao is Dictionary<string, object> a && S(a, "name") == MainFile(product, vs))) continue;
                ok.Add((v, e));
            }
            if (listed.Length > 0 && ok.Count == 0) return UpdateAnswer.Fail(product, channel, running, "no release in the answer is well formed");
            var fit = ok.Where(x => product != "app" || BundleOk(x.e, channel, Version(installedWorkbench))).ToList();
            var best = fit.Count == 0 ? default : fit.Aggregate((x, y) => Compare(y.v, x.v) > 0 ? y : x);
            var r = new UpdateAnswer { Product = product, Channel = channel, Running = running, Newest = best.e != null ? S(best.e, "version") : null, Ahead = ok.Count > 0 && ok.All(x => Compare(x.v, cur) < 0) };
            r.State = best.e != null && Compare(best.v, cur) > 0 ? UpdateState.Available : UpdateState.UpToDate;
            return r;
        }

        /// <summary>an app release this installation may take: one that names no bundle (every release up to 0.11.3), or whose bundled
        /// Workbench has a plain version and a channel, is a production Workbench in a production release, and is not older than the
        /// installed Workbench</summary>
        static bool BundleOk(Dictionary<string, object> e, string channel, int[] installed)
        {
            if (!e.TryGetValue("bundle", out var bo) || bo == null) return true;
            if (!(bo is Dictionary<string, object> bd && bd.TryGetValue("workbench", out var wo) && wo is Dictionary<string, object> wb)) return false;
            var wc = S(wb, "channel"); var wv = Version(S(wb, "version"));
            if (wv == null || !Channels.Contains(wc)) return false;
            if (channel == "production" && wc != "production") return false;
            return installed == null || Compare(wv, installed) >= 0;
        }

        static string S(Dictionary<string, object> d, string k) => d != null && d.TryGetValue(k, out var v) ? v as string : null;
    }
}
