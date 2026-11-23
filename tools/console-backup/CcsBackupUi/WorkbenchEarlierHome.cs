// Classic Cart Studio console backup and restore window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The earlier CCS HOME versions in a review that places CCS HOME (Add-ons and Startup, Review the Preparation), in four groups: the copy in
// use now, the update (sent first beside it, checked before the switch), the copies kept as earlier versions after the update and the ones
// removed then (each checked against its record on the console first), each with its collection name, games, date, size and the space its
// files take; and Change… (Settings > CCS HOME > Earlier HOME versions).
// A change of the setting applies when the change is reviewed again.
using CcsBackup.Engine;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace CcsBackup.Ui
{
    public sealed partial class BackupWorkspace
    {
        /// <summary>the review's earlier-versions block from the plan's "earlier_home" (EarlierPlan.ToJson), in four groups: the copy in use now,
        /// the update (sent first beside it and checked before the switch), the copies kept as earlier versions after the switch and the
        /// ones removed then; null when the plan places no HOME. newSize: the new copy's size as the plan's "home" gives it</summary>
        UIElement EarlierHomeReview(Dictionary<string, object> e, Action back, string backLabel, string newSize = null)
        {
            if (e == null) return null;
            var ret = e.O("retention"); var remove = new HashSet<string>((ret?.A("remove") ?? new List<object>()).OfType<string>(), StringComparer.Ordinal);
            var why = ret?.O("why") ?? new Dictionary<string, object>(); var cur = e.S("current"); var prior = e.S("prior"); long keep = e.L("keep", 1);
            var copies = (e.A("copies") ?? new List<object>()).OfType<Dictionary<string, object>>().Where(x => x.S("copy") != cur).ToList();
            var c = new StackPanel { Margin = new Thickness(0, 6, 0, 0) }; System.Windows.Automation.AutomationProperties.SetName(c, "Earlier HOME versions");
            c.Children.Add(T("Earlier HOME versions", Ty.Body, true, false, new Thickness(0, 0, 0, 4)));
            c.Children.Add(T("This console keeps " + (keep == 0 ? "no earlier version" : keep == 1 ? "one earlier version" : keep.ToString(CultureInfo.InvariantCulture) + " earlier versions") + " when CCS HOME is updated, so you can go back to one. Your backups on this PC are kept separately.", Ty.Text, false, true));
            void Group(string title, IEnumerable<string> rows)
            {
                c.Children.Add(T(title, Ty.Text, true, false, new Thickness(0, 8, 0, 2)));
                var list = rows.ToList(); if (list.Count == 0) list.Add("None");
                foreach (var r in list) c.Children.Add(T("• " + r, Ty.Text));
            }
            void Change() => c.Children.Add(LinkRow(("Change…", () => { settingsBack = back; settingsBackLabel = backLabel; try { Go("Settings"); } finally { settingsBack = null; settingsBackLabel = null; } })));
            if (prior == null && copies.Count == 0)   // a first installation: nothing earlier to keep or remove
            {
                c.Children.Add(T("This console has no CCS HOME yet, so there is no earlier version to keep or remove.", Ty.Text, false, true, new Thickness(0, 4, 0, 0)));
                Change(); return c;
            }
            var pc = copies.FirstOrDefault(x => x.S("copy") == prior);
            Group("In use now", pc != null ? new[] { EarlierRow(pc) } : new[] { prior == null ? "No CCS HOME yet" : "A copy whose details are not known (" + prior + ")" });
            Group("The update: sent first, beside the copy in use, and checked before the switch", new[] { "The new CCS HOME copy" + (newSize != null ? ", " + newSize : "") + "; in use after the switch (" + cur + ")" });
            Group("Kept on the console as earlier versions after the update", copies.Where(x => !remove.Contains(x.S("copy"))).Select(x =>
            {
                var w = why.TryGetValue(x.S("copy"), out var o) ? o as string : null;
                var head = x.S("copy") == prior ? "The copy in use now (" + (x.S("label") ?? "name not known") + ")" : EarlierRow(x);
                return head + (w == null || w == "an earlier version kept" ? "" : " · Kept: " + w);
            }));
            Group("Removed after the new copy is in use and read back", copies.Where(x => remove.Contains(x.S("copy"))).Select(x => x.S("copy") == prior ? "The copy in use now (" + (x.S("label") ?? "name not known") + "), after the switch" : EarlierRow(x)));
            Change();
            c.Children.Add(T("A change applies when you review this again. Each version is checked against its record on the console again just before it is removed; a version that differs, or that an entry of Original Menu names, is kept.", Ty.Small, false, true));
            if (remove.Count > 0) c.Children.Add(T(EarlierHome.RestoreText, Ty.Small, false, true));
            return c;
        }

        /// <summary>one copy in the review: its collection name and games, when it was installed, its size and the space it takes</summary>
        static string EarlierRow(Dictionary<string, object> x)
        {
            long b = x.L("bytes"), al = x.L("allocated_bytes"), g = x.L("games");
            var name = (x.S("label") ?? "A copy whose name is not known") + (g >= 0 ? ", " + g.ToString(CultureInfo.InvariantCulture) + " game" + (g == 1 ? "" : "s") : "");
            var when = DateTime.TryParseExact(x.S("installed_utc") ?? "", "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t)
                ? "installed " + t.ToLocalTime().ToString("MMMM d, yyyy 'at' h:mm tt", CultureInfo.InvariantCulture) : "date not known";
            var size = b >= 0 ? Units.Gb(b) + (al >= 0 ? " (" + Units.Gb(al) + " on the storage)" : " (space on the storage not known)") : "size not known";
            return name + " · " + when + " · " + size;
        }
    }
}
