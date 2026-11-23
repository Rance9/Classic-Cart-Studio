// Classic Cart Studio console backup and restore window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Pictures for the Sega Mini menu (Console Composer, and the Storage view's row): the optional clean-up of the pictures Hakchi2 CE made for
// the Sega Mini menu (CcsBackup.Engine.SegaCleanup). Nothing happens unless the person reviews the exact list and confirms it: the console
// is backed up first, the list comes from that backup by the shared rule (CcsBackup.Browse.SegaPictures), and Put Them Back puts exactly
// those files back from it. Workbench never writes these pictures otherwise, and has no setting to include them.
using CcsBackup.Browse;
using CcsBackup.Engine;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;

namespace CcsBackup.Ui
{
    public sealed partial class BackupWorkspace
    {
        string segaOp;            // "remove" or "put-back": the clean-up running in this session
        string segaProtective;    // the backup made in this session before the change

        /// <summary>the removals recorded on this PC for the clean-up, newest first (the record files in the records folder)</summary>
        List<(string file, string made, int files, string console)> SegaRemovals()
        {
            var list = new List<(string, string, int, string)>();
            try
            {
                if (!Directory.Exists(settings.RecordsFolder)) return list;
                foreach (var f in Directory.GetFiles(settings.RecordsFolder, SegaCleanup.RemoveKind + "-*.json"))
                {
                    try
                    {
                        var d = Json.Parse(File.ReadAllText(f));
                        if (d.S("kind") != SegaCleanup.RemoveKind) continue;
                        int n = (d.A("changes") ?? new List<object>()).OfType<Dictionary<string, object>>().Count(x => x.S("state") == "removed-tombstone");
                        if (n > 0) list.Add((f, d.S("created_utc"), n, d.S("console_tag")));
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is FormatException) { }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
            return list.OrderByDescending(x => x.Item2, StringComparer.Ordinal).ToList();
        }

        void ShowSegaPictures()
        {
            var sp = SectionHead(SegaPictures.Title, SegaPictures.Explanation);
            var c = new System.Windows.Controls.StackPanel();
            c.Children.Add(T("Removing them is optional. When you start, Workbench first backs up the " + Con + " as it is now, then lists exactly the pictures it can remove, with their sizes. Nothing is removed until you confirm that list, and Put Them Back puts exactly those files back from that backup.", Ty.Text, false, true));
            c.Children.Add(T("Workbench lists only the pictures Hakchi2 CE made for a game or folder in hakchi's game folders. A picture a menu entry names, or one whose owner is not clear, stays and is listed with the reason.", Ty.Text, false, true, new Thickness(0, 8, 0, 0)));
            c.Children.Add(Note(SegaPictures.Recreated, "info"));
            c.Children.Add(Buttons(B("Review What Can Be Removed…", "Btn.Primary", () => StartSegaRemove())));
            sp.Children.Add(Card(c));
            var removals = SegaRemovals();
            var pb = new System.Windows.Controls.StackPanel();
            pb.Children.Add(T("Put back pictures removed earlier", Ty.Heading, true, false, new Thickness(0, 0, 0, 6)));
            if (removals.Count == 0) pb.Children.Add(T("No removal is recorded on this PC.", Ty.Text, false, true));
            foreach (var x in removals.Take(10))
                pb.Children.Add(ActionRow(When(x.made) + " · " + Units.Count(x.files, "picture") + (x.console != null ? " · console " + x.console : ""), "Put Them Back…", () => StartSegaPutBack(x.file)));
            sp.Children.Add(Card(pb));
            Show(sp, "sega-pictures");
        }

        System.Windows.Controls.Grid ActionRow(string text, string button, Action go)
        {
            var g = new System.Windows.Controls.Grid { Margin = new Thickness(0, 4, 0, 4) };
            g.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });
            var t = T(text, Ty.Text, false, true); t.VerticalAlignment = VerticalAlignment.Center; g.Children.Add(t);
            var b = B(button, "Btn", go); System.Windows.Controls.Grid.SetColumn(b, 1); g.Children.Add(b);
            return g;
        }

        void StartSegaRemove()
        {
            segaOp = "remove"; segaProtective = null;
            var r = ConsoleReq("sega-pictures"); r.Add("op", "remove");
            StartOp("sega-pictures", "Preparing the Review", "The connected console is identified and backed up, and the pictures are listed from that backup. Nothing is removed until you confirm. Keep the USB cable connected.", r);
        }

        void StartSegaPutBack(string record)
        {
            segaOp = "put-back"; segaProtective = null;
            var r = ConsoleReq("sega-pictures"); r.Add("op", "put-back"); r.Add("record", record);
            StartOp("sega-pictures", "Preparing the Review", "The connected console is identified, the backup made before the removal is checked again, and the console is backed up as it is now. Nothing is written until you confirm. Keep the USB cable connected.", r);
        }

        void ShowSegaPlan(Dictionary<string, object> p)
        {
            bool remove = p.S("kind") == SegaCleanup.RemoveKind;
            var changes = (p.A("changes") ?? new List<object>()).OfType<Dictionary<string, object>>().ToList();
            var ev = (p.A("evidence") ?? new List<object>()).OfType<Dictionary<string, object>>().ToList();
            segaProtective = ev.LastOrDefault(x => (x.S("role") ?? "").StartsWith("backup made just now", StringComparison.Ordinal))?.S("path");
            var sp = new System.Windows.Controls.StackPanel();
            sp.Children.Add(PageHead(remove ? "Review: Remove Pictures for the Sega Mini Menu" : "Review: Put Back Pictures for the Sega Mini Menu", SegaPictures.Explanation));
            var c = new System.Windows.Controls.StackPanel();
            c.Children.Add(T(remove ? "What is removed from the console" : "What is put back on the console", Ty.Heading, true, false, new Thickness(0, 0, 0, 6)));
            if (changes.Count > 0) c.Children.Add(T(Units.Count(changes.Count, "picture") + ", " + Units.Gb(p.L("bytes")) + " in all.", Ty.Text, false, true));
            var table = ChangeTable(changes.Select(x => (remove ? "Remove" : "Put back", x.S("path"), Units.Gb(x.L("bytes"))))); SetNoTerms(table, true); c.Children.Add(table);
            if (remove && p.O("account") is Dictionary<string, object> a && a.L("removed_bytes") > 0)
            {
                long? av = a.TryGetValue("available_bytes", out var avo) && avo != null ? a.L("available_bytes") : (long?)null;
                c.Children.Add(Figures(("Space they take on the storage (each file once)", a.L("removed_bytes"), null),
                                       ("Available on the storage now (outside the reserve)", av, null),
                                       ("Available after removing them", av + a.L("removed_bytes"), null)));
            }
            var kept = (p.A("kept") ?? new List<object>()).OfType<string>().ToList();
            if (kept.Count > 0) { var kd = Details((remove ? "Pictures of this kind that stay" : "Files not put back") + " (" + kept.Count + ")", kept.Cast<object>().ToArray()); SetNoTerms(kd, true); c.Children.Add(kd); }
            foreach (var n in (p.A("notes") ?? new List<object>()).OfType<string>().Where(n => n != SegaPictures.Explanation)) c.Children.Add(T("• " + n, Ty.Text, false, true));
            if (p.S("refusal") != null) c.Children.Add(Note(p.S("refusal"), "info"));
            var tech = new List<object>();
            foreach (var x in ev) tech.Add(Cap(x.S("role") ?? "backup") + ": " + x.S("path") + "; SHA-256 " + x.S("sha256") + "; made " + (x.S("completed_utc") ?? "unknown") + "; console " + (x.S("console") ?? "unknown"));
            foreach (var x in changes) tech.Add((remove ? "Remove " : "Put back ") + x.S("path") + ": SHA-256 " + x.S("sha256") + ", " + x.L("bytes").ToString("N0", En) + " bytes");
            tech.Add(remove ? "Each file is checked to be exactly the one the backup holds, removed, and checked gone; the first failure stops every later file, and nothing is retried."
                            : "Each file is written only where the console has no file of that name, to a new name, renamed into place and read back; the first failure stops every later file.");
            if (p.S("digest") != null) tech.Add("Review digest " + p.S("digest") + "; the confirmation is good until " + (p.S("expires_utc") ?? "unknown") + " (UTC), once.");
            c.Children.Add(Details("Technical details", tech.ToArray()));
            sp.Children.Add(Card(c));
            if (p.S("refusal") != null || p.S("nonce") == null) { sp.Children.Add(Buttons(B("Done", "Btn", () => { worker?.Send(new JObj { { "cmd", "quit" } }); ShowSegaPictures(); }))); Show(sp, "sega-plan"); return; }
            sp.Children.Add(Buttons(B(remove ? "Remove These Pictures" : "Put These Back", "Btn.Primary", () =>
            {
                progress = new ProgressView(this, remove ? "Removing the Pictures" : "Putting the Pictures Back", "Do not unplug the console. Each file is changed once and checked. Stop After This File finishes the file being changed, then stops.", "Stop After This File", () => { worker?.Cancel(); progress.Say("Stopping after this file..."); });
                Show(progress.Panel, "progress");
                worker?.Send(new JObj { { "cmd", "sega-pictures-confirm" }, { "digest", p.S("digest") }, { "nonce", p.S("nonce") } });
            }), B("Cancel", "Btn.Ghost", () => { worker?.Send(new JObj { { "cmd", "quit" } }); ShowSegaPictures(); })));
            Show(sp, "sega-plan");
        }

        void SegaResult(Dictionary<string, object> r)
        {
            var st = r.S("state"); bool remove = segaOp != "put-back"; var sp = new System.Windows.Controls.StackPanel();
            var done = (r.A("done") ?? new List<object>()).OfType<string>().ToList();
            var (failTitle, failText) = NotDone(st, done.Count, "file");
            sp.Children.Add(PageHead(st == "changed" ? (remove ? "Pictures removed and checked" : "Pictures put back and checked") : failTitle,
                st == "changed" ? (remove ? Units.Count(done.Count, "picture") + " for the Sega Mini menu " + (done.Count == 1 ? "was" : "were") + " removed from your " + Con + "; each was checked gone." : Units.Count(done.Count, "picture") + " " + (done.Count == 1 ? "was" : "were") + " put back on your " + Con + " and read back.") : failText));
            if (st != "changed") sp.Children.Add(Note(r.S("problem") ?? "", st == "nothing-to-do" ? "info" : st == "canceled" ? "warn" : "bad"));
            else if (remove) sp.Children.Add(Note(SegaPictures.Recreated + " Put Them Back puts exactly these files back.", "info"));
            if (segaProtective != null && st != "refused") { sp.Children.Add(T("The backup of the console made just before is kept:", Ty.Small, false, true)); sp.Children.Add(PathText(segaProtective)); }
            sp.Children.Add(Details("Technical details", done.Count > 0 ? (remove ? "Removed: " : "Put back: ") + string.Join(", ", done) : null, r.S("record") != null ? "Record: " + r.S("record") : null, "Result: " + st));
            var rec = r.S("record");
            var buttons = new List<UIElement>();
            if (remove && rec != null && done.Count > 0) buttons.Add(B("Put Them Back…", "Btn", () => { worker?.Send(new JObj { { "cmd", "quit" } }); StartSegaPutBack(rec); }));
            buttons.Add(B("Done", st == "changed" ? "Btn.Primary" : "Btn", () => { worker?.Send(new JObj { { "cmd", "quit" } }); ShowSegaPictures(); }));
            sp.Children.Add(Buttons(buttons.ToArray()));
            Show(sp, "result");
        }
    }
}
