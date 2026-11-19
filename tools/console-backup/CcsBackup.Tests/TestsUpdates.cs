// Classic Cart Studio console backup and restore engine tests: which published release an update check offers (Library/ReleaseStreams.cs,
// the CCS Console Workbench's decision; Classic Cart Studio's is tools/publish/test_release_streams.py, with the same cases).
// GPL-3.0-or-later. Offline: the release information here is made-up text; nothing opens a connection.
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.Linq;
using static CcsBackup.Tests.Program;

namespace CcsBackup.Tests
{
    static partial class Tests
    {
        static string UpdEntry(string product, string channel, string v, string wbVersion = null, string wbChannel = null, string tag = null, string file = null)
        {
            var t = tag ?? (product == "app" ? "v" : "workbench-v") + v;
            var f = file ?? (product == "app" ? "ClassicCartStudio-Setup-" : "CCS-Console-Workbench-") + v + ".exe";
            var bundle = wbVersion == null ? "" : ",\"bundle\":{\"workbench\":{\"version\":\"" + wbVersion + "\",\"channel\":\"" + (wbChannel ?? channel) + "\",\"assets\":[]},\"engine_source\":null}";
            return "{\"product\":\"" + product + "\",\"channel\":\"" + channel + "\",\"version\":\"" + v + "\",\"tag\":\"" + t + "\",\"prerelease\":" + (channel == "preview" ? "true" : "false")
                + ",\"asset\":{\"name\":\"" + f + "\",\"size\":1,\"url\":\"https://github.com/Rance9/Classic-Cart-Studio/releases/download/" + t + "/" + f + "\",\"sha256\":null},\"files\":[]" + bundle + "}";
        }

        /// <summary>a release-streams document: streams as (product, channel, entries); the four usual streams are added empty when not given</summary>
        static string UpdDoc(bool complete, params (string p, string c, string[] e)[] streams)
        {
            var all = streams.ToList();
            foreach (var p in new[] { "app", "workbench" }) foreach (var c in new[] { "production", "preview" }) if (!all.Any(s => s.p == p && s.c == c)) all.Add((p, c, new string[0]));
            return "{\"schema\":\"ccs-release-streams/1\",\"generated\":\"x\",\"complete\":" + (complete ? "true" : "false") + ",\"excluded\":[{\"tag\":\"workbench-v1.0.0\",\"reason\":\"superseded\"}],\"streams\":["
                + string.Join(",", all.Select(s => "{\"product\":\"" + s.p + "\",\"channel\":\"" + s.c + "\",\"releases\":[" + string.Join(",", s.e) + "]}")) + "]}";
        }

        internal static void Updates()
        {
            // the published releases as of 0.11.3: app and Workbench 0.11.0 to 0.11.3 previews, app production up to 0.10.1, no production Workbench;
            // the superseded workbench-v1.0.0 is only in "excluded"
            string[] V(string p, string c, params string[] vs) => vs.Select(v => UpdEntry(p, c, v)).ToArray();
            var published = UpdDoc(true, ("app", "preview", V("app", "preview", "0.11.3", "0.11.2", "0.11.1", "0.11.0")), ("app", "production", V("app", "production", "0.10.1", "0.10.0", "0.9.0")),
                ("workbench", "preview", V("workbench", "preview", "0.11.3", "0.11.2", "0.11.1", "0.11.0")));
            UpdateAnswer P(string doc, string product, string channel, string running, string wb = null) => ReleaseStreams.Plan(doc, product, channel, running, wb);

            var a = P(published, "workbench", "preview", "0.11.3");
            Check(a.State == UpdateState.UpToDate && a.Newest == "0.11.3" && !a.Ahead, "current: Workbench 0.11.3 preview is up to date (" + a.State + " " + a.Newest + ")");
            Check(P(published, "workbench", "preview", "0.11.2").State == UpdateState.Available && P(published, "workbench", "preview", "0.11.2").Newest == "0.11.3", "newer: Workbench 0.11.2 is offered 0.11.3");
            a = P(published, "workbench", "preview", "0.11.5");
            Check(a.State == UpdateState.UpToDate && a.Ahead && a.Newest == "0.11.3", "running ahead of the feed: up to date, never a lower version, and marked ahead");
            Check(P(published, "workbench", "preview", "0.9.0").Newest == "0.11.3", "the superseded workbench-v1.0.0 (only in \"excluded\") is never offered");
            Check(P(published, "workbench", "production", "0.11.3").State == UpdateState.UpToDate && P(published, "workbench", "production", "0.11.3").Newest == null, "a production Workbench is never offered a preview (workbench/production lists nothing)");
            Check(P(published, "app", "preview", "0.11.3").State == UpdateState.UpToDate, "legacy app 0.11.3 is a preview and up to date in the preview stream");
            Check(P(published, "app", "preview", "0.9.0").Newest == "0.11.3", "a preview Studio is never offered production v0.10.1");
            Check(P(published, "app", "production", "0.10.0").State == UpdateState.Available && P(published, "app", "production", "0.10.0").Newest == "0.10.1", "a production Studio is offered production v0.10.1");
            Check(P(published, "app", "production", "0.10.1").State == UpdateState.UpToDate, "production v0.10.1 is never offered the 0.11 previews");

            var tens = UpdDoc(true, ("workbench", "preview", V("workbench", "preview", "0.11.9", "0.11.10", "0.11.4")));
            Check(P(tens, "workbench", "preview", "0.11.9").Newest == "0.11.10" && P(tens, "workbench", "preview", "0.11.9").State == UpdateState.Available, "0.11.10 is newer than 0.11.9 (numbers, not text or list order)");
            Check(ReleaseStreams.Version("0.011.3") == null && ReleaseStreams.Version("0.11.3-preview.1") == null && ReleaseStreams.Version("v0.11.3") == null && ReleaseStreams.Version("0.11.3") != null, "only plain versions are versions");

            var mixed = UpdDoc(true, ("workbench", "preview", new[] { UpdEntry("workbench", "production", "9.9.9"), UpdEntry("app", "preview", "9.9.8"), UpdEntry("workbench", "preview", "9.9.7", tag: "v9.9.7"), UpdEntry("workbench", "preview", "9.9.6", file: "ClassicCartStudio-Setup-9.9.6.exe"), UpdEntry("workbench", "preview", "0.11.3") }));
            Check(P(mixed, "workbench", "preview", "0.11.3").State == UpdateState.UpToDate, "entries of another channel or product, a wrong tag or another product's file inside the stream are never offered");
            var appBig = UpdDoc(true, ("app", "preview", V("app", "preview", "5.0.0")), ("workbench", "preview", V("workbench", "preview", "0.11.3")));
            Check(P(appBig, "workbench", "preview", "0.11.3").State == UpdateState.UpToDate, "the app's numbers are never compared with the Workbench's");

            foreach (var (label, text) in new[] { ("malformed JSON", "{not json"), ("an HTML page", "<html></html>"), ("empty", ""), ("another document", "{\"schema\":\"something-else\",\"complete\":true,\"streams\":[]}"),
                ("incomplete", UpdDoc(false)), ("no such stream", "{\"schema\":\"ccs-release-streams/1\",\"complete\":true,\"streams\":[]}"),
                ("only entries not well formed", UpdDoc(true, ("workbench", "preview", new[] { UpdEntry("workbench", "preview", "0.11", tag: "workbench-v0.11", file: "CCS-Console-Workbench-0.11.exe") }))) })
            {
                var x = P(text, "workbench", "preview", "0.11.3");
                Check(x.State == UpdateState.CouldNotCheck && x.Problem != null, label + ": could not check, never up to date");
            }
            Check(P(published, "workbench", "preview", "0.11").State == UpdateState.CouldNotCheck && P(published, "workbench", "beta", "0.11.3").State == UpdateState.CouldNotCheck && P(published, "studio", "preview", "0.11.3").State == UpdateState.CouldNotCheck,
                "an unreadable running version, channel or product: could not check");

            // installed with Classic Cart Studio: the app stream of Studio's channel, the installed Workbench in the bundle
            var bundles = UpdDoc(true, ("app", "preview", new[] { UpdEntry("app", "preview", "0.11.4", "0.11.4"), UpdEntry("app", "preview", "0.11.3") }),
                ("app", "production", new[] { UpdEntry("app", "production", "0.10.2", "0.11.4", "preview"), UpdEntry("app", "production", "0.10.1") }));
            Check(P(bundles, "app", "preview", "0.11.3", "0.11.3").State == UpdateState.Available && P(bundles, "app", "preview", "0.11.3", "0.11.3").Newest == "0.11.4", "Studio 0.11.3 with Workbench 0.11.3 is offered the 0.11.4 bundle");
            Check(P(bundles, "app", "preview", "0.11.3", "0.11.5").State == UpdateState.UpToDate, "a bundle whose Workbench is older than the installed one is not offered");
            Check(P(bundles, "app", "production", "0.10.1", "0.11.3").State == UpdateState.UpToDate, "a production bundle carrying a preview Workbench is never offered");
            Check(P(bundles, "app", "preview", "0.11.3").State == UpdateState.Available, "the installed Workbench unknown: the bundle is offered on Studio's version");
        }
    }
}
