// Classic Cart Studio console backup engine tests: the DATA-ONLY admission. GPL-3.0-or-later. Simulated
// consoles only; nothing here opens a console session of any kind. A console whose backups hold no original Nintendo material (the
// "modified-noorig" fixture: a Hakchi setup without the original kernel copy) can have Classic Cart Studio's data changed with its NEWEST
// complete backup alone, checked against the console's data at the write; preparation is never admitted that way, and no forged or stale
// ticket passes.
using CcsBackup.Engine;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static CcsBackup.Tests.Program;

namespace CcsBackup.Tests
{
    static partial class Tests
    {
        public static void DataOnly()
        {
            var dir = NewDir("data-only"); var s = SettingsIn(dir, false);
            var con = Console_("modified-noorig");
            var b = RealFlagged(con, s);
            Check(b.Ok && b.Verification != null && !b.Verification.RecoveryBaseline && b.Verification.RestoreReady && b.Verification.StateKind == "modified",
                "a complete backup of a Hakchi console without its original material: restore-ready, modified, not a recovery baseline (" + b.State + " " + b.Problem + ")");
            if (!b.Ok) return;
            var saved = Admission.CcsDataOf(b.PrimaryPath, out var why);
            Check(saved != null && saved.Keys.Any(k => k.StartsWith("/ccs/", StringComparison.Ordinal)), "the backup holds the console's Classic Cart Studio data list (" + (saved?.Count.ToString() ?? why) + ")");
            if (saved == null) return;
            LiveTree Live(Action<Dictionary<string, string>> change = null) { var l = new LiveTree(); foreach (var kv in saved) l.Entries[kv.Key] = kv.Value; change?.Invoke(l.Entries); return l; }
            Dictionary<string, object> Ticket(AdmissionResult a) => Json.Parse(File.ReadAllText(a.TicketFile)) as Dictionary<string, object>;
            // each admitted write records a change (an early hint that refuses later admissions until a newer backup): cleared between cases
            void NoHistory() { foreach (var h in Directory.GetFiles(s.RecordsFolder, "console-*-changes.json")) File.Delete(h); }

            // admitted, as its own type, with the notice
            var a1 = Admission.Admit(s, HwidA, "install", "home1");
            var t1 = a1.Ok ? Ticket(a1) : null;
            Check(a1.Ok && a1.DataOnly && a1.Code == "admitted-data-only" && a1.BaselinePath == null && a1.CurrentPath == b.PrimaryPath
                  && t1.S("policy") == "data-only-current" && t1["baseline"] == null && t1.S("current_proof") == "console-data-list",
                "install with only a current backup: admitted as data-only, naming that backup and the console's data list as its proof (" + a1.Text + ")");
            Check(a1.Text != null && a1.Text.Contains(Admission.OriginalUnavailableTitle) && a1.Text.Contains(Admission.OriginalUnavailableText),
                "its text says returning to Nintendo's original software is unavailable, in the approved words");
            var u1 = Admission.Use(s, a1.TicketId, "install", "home1", HwidA, null, false, Live(), null);
            Check(u1.Ok && u1.DataOnly && u1.Code == "admitted-data-only", "the worker's check with the console's data equal to the backup: admitted (" + u1.Text + ")");
            Check(!Admission.Use(s, a1.TicketId, "install", "home1", HwidA, null, false, Live(), null).Ok, "the same ticket again (a replay): refused");

            // the console's data must equal the backup at the write
            foreach (var (label, live, code) in new[] {
                ("a file changed since the backup", Live(e => e[e.Keys.First(k => e[k].StartsWith("f:", StringComparison.Ordinal))] = "f:" + new string('0', 64)), "current-backup-required"),
                ("a new save since", Live(e => e["/ccs/data/saves/new-since.sav"] = "f:" + new string('1', 64)), "current-backup-required"),
                ("the list could not be read", LiveTree.Unreadable("the listing did not finish"), "current-unchecked"),
                ("no list given", (LiveTree)null, "current-unchecked") })
            {
                NoHistory(); var a = Admission.Admit(s, HwidA, "update", "home2");
                var u = Admission.Use(s, a.TicketId, "update", "home2", HwidA, null, false, live, null);
                Check(a.Ok && a.DataOnly && !u.Ok && u.Code == code, label + ": refused before any write (" + u.Code + ")");
            }

            // never preparation; another console; a forged type or operation
            NoHistory(); var p = Admission.Admit(s, HwidA, "prepare", "prep", null, false, "session-1");
            Check(!p.Ok && p.Code == "backup-required", "preparation with only a current backup: refused (no data-only admission for firmware work) (" + p.Text + ")");
            var a2 = Admission.Admit(s, HwidA, "install", "home3");
            Check(!Admission.Use(s, a2.TicketId, "install", "home3", HwidB, null, false, Live(), null).Ok, "the ticket on another console: refused");
            var a3 = Admission.Admit(s, HwidA, "install", "home4"); var t3 = Ticket(a3); t3["policy"] = "recovery-baseline"; File.WriteAllText(a3.TicketFile, Json.Write(t3));
            Check(!Admission.Use(s, a3.TicketId, "install", "home4", HwidA, null, false, Live(), null).Ok, "a data-only ticket edited to claim the recovery-baseline type: refused (it names no baseline)");
            var a4 = Admission.Admit(s, HwidA, "install", "home5"); var t4 = Ticket(a4); t4["operation"] = "prepare"; File.WriteAllText(a4.TicketFile, Json.Write(t4));
            var u4 = Admission.Use(s, a4.TicketId, "prepare", "home5", HwidA, null, false, Live(), null);
            Check(!u4.Ok, "a data-only ticket edited to name preparation: refused (" + u4.Code + ")");
            var a5 = Admission.Admit(s, HwidA, "install", "home6"); var t5 = Ticket(a5); t5["baseline"] = t5["current"]; File.WriteAllText(a5.TicketFile, Json.Write(t5));
            Check(!Admission.Use(s, a5.TicketId, "install", "home6", HwidA, null, false, Live(), null).Ok, "a data-only ticket with a baseline added: refused");

            // stale: a change after the newest backup
            NoHistory(); Admission.NoteChange(s, HwidA, "install");
            var a6 = Admission.Admit(s, HwidA, "install", "home7");
            Check(!a6.Ok && a6.Code == "current-backup-required", "a recorded change after the newest backup: refused at admission (" + a6.Text + ")");
        }
    }
}
