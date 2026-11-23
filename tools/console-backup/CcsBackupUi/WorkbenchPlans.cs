// Classic Cart Studio console backup and restore window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Console Composer's Shared Composition Plans (.ccsplan): a plan is choices and public references only (CcsBackup.Compose.Composition).
// Saving one shows exactly what is included and removed first; opening one shows a checklist of what the receiver supplies, what is ready
// and what this version cannot use. A plan never authorizes a write and is never a restore point: the receiver chooses their own backups
// and files, and every change is reviewed on the console like any other composition.
using CcsBackup.Compose;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace CcsBackup.Ui
{
    /// <summary>what this program can confirm as public. The standalone carries no game catalog, so every game is the receiver's own
    /// choice; a host with the catalog can supply its own registry.</summary>
    public sealed class LocalRegistry : IPublicRegistry
    {
        public string GameTitle(string catalogId) => null;
        public bool IsPublicGameHash(string catalogId, string contentSha256) => false;
        public bool IsPublicBuild(string kind, string name, string version, string sha256) => false;
    }

    public sealed partial class BackupWorkspace
    {
        public IPublicRegistry PublicRegistry { get; set; } = new LocalRegistry();
        const string PlanFilter = "Shared Composition Plans (*.ccsplan)|*.ccsplan";

        PrivateValues KnownPrivate()
        {
            var pv = new PrivateValues().Add(Environment.UserName, Environment.MachineName, settings.PrimaryFolder, settings.ArchiveFolder);
            try { pv.Add(History.List(settings).Select(e => e.ConsoleTag).ToArray()); } catch (Exception) { }
            return pv;
        }

        /// <summary>the private draft behind a shareable plan, from Composer's chosen backup and files (nothing is read beyond the backup's
        /// own description; game files are the person's own, so they become placeholders in the shared copy)</summary>
        CompositionDraft DraftFrom(string startingPoint, string policy, string title, string backup, IList<string> paths)
        {
            var d = new CompositionDraft { Title = title, StartingPoint = startingPoint, InstallPolicy = policy };
            var m = backup == null ? null : History.Peek(backup);
            d.SourceConsoleHwid = m?.O("console")?.S("hwid"); d.SourceConsoleTag = m?.O("console")?.S("tag");
            bool hakchi = paths.Any(p => p.StartsWith("hakchi/", StringComparison.Ordinal)), ccs = paths.Any(p => p.StartsWith("ccs/", StringComparison.Ordinal));
            d.Environments.Add(new DraftEnvironment { Id = "nintendo", Kind = "nintendo-original", Default = !ccs });
            if (hakchi) d.Environments.Add(new DraftEnvironment { Id = "hakchi", Kind = "hakchi-ce" });
            if (ccs) d.Environments.Add(new DraftEnvironment { Id = "ccs", Kind = "ccs-home", Default = true });
            foreach (var p in paths.Where(p => p.StartsWith("hakchi/games/", StringComparison.Ordinal) && p.EndsWith(".nes", StringComparison.OrdinalIgnoreCase)))
            {
                var parts = p.Split('/'); var code = parts.Length >= 2 ? parts[parts.Length - 2] : p;
                d.Games.Add(new DraftGame { PersonalId = code, FileName = parts[parts.Length - 1], Environment = "hakchi" });
            }
            if (backup != null)
            {
                d.Inputs.Add(new DraftInput { Role = "console-system", SourceBackupId = m?.S("backup_id"), LocalPath = backup, Environment = "nintendo" });
                if (paths.Any(p => p.StartsWith("clover/", StringComparison.Ordinal))) d.Inputs.Add(new DraftInput { Role = "console-saves", SourceBackupId = m?.S("backup_id"), Environment = "nintendo" });
            }
            return d;
        }

        void ShowSharePlan(CompositionDraft d, Action back)
        {
            var p = CompositionShare.Prepare(d, PublicRegistry, KnownPrivate());
            var sp = SectionHead("Save a Shared Composition Plan", "Save your choices as a " + FileKinds.Plan + " on this PC that you can give to someone else. It holds only choices and public references; check what it holds before you pass it on.");
            var c = new StackPanel(); c.Children.Add(T(Composition.Label, Ty.Small, false, true));
            c.Children.Add(T("Included", Ty.Body, true, false, new Thickness(0, 8, 0, 4))); foreach (var x in p.Included) c.Children.Add(T("• " + x, Ty.Text));
            if (p.Removed.Count > 0) { c.Children.Add(T("Removed from the shared copy", Ty.Body, true, false, new Thickness(0, 8, 0, 4))); foreach (var x in p.Removed) c.Children.Add(T("• " + x, Ty.Text)); }
            foreach (var x in p.Refusals) c.Children.Add(Note(x, "bad"));
            c.Children.Add(T("A plan never changes a console by itself and is never a restore point: whoever uses it chooses their own backups and files, and every change is reviewed on their console first.", Ty.Small, false, true, new Thickness(0, 8, 0, 0)));
            sp.Children.Add(Card(c));
            var problem = new ContentControl();
            sp.Children.Add(Buttons(p.CanShare ? B("Save the Shared Plan…", "Btn.Primary", () =>
            {
                var f = new Microsoft.Win32.SaveFileDialog { Title = "Save a Shared Composition Plan", Filter = PlanFilter, FileName = SafeFileName(string.IsNullOrWhiteSpace(d.Title) ? "My plan" : d.Title) + Composition.Extension, OverwritePrompt = false };
                if (f.ShowDialog(this) != true) return;
                try { CompositionShare.Write(p, f.FileName); }
                catch (Exception ex) when (ex is ComposeException || ex is IOException || ex is UnauthorizedAccessException) { problem.Content = Note("The plan was not saved: " + ex.Message, "bad"); return; }
                var r = SectionHead("Shared Composition Plan saved", "It was written and checked: it holds only your choices and public references, never your games, saves or console details.");
                r.Children.Add(Card(new StackPanel { Children = { T("Saved as:", Ty.Small, false, true, new Thickness(0, 0, 0, 2)), PathText(f.FileName, 13) } }));
                r.Children.Add(Buttons(B("Open Folder", "Btn", () => OpenFolder(f.FileName)), B("Back to Your Choices", "Btn.Ghost", back), B("Done", "Btn.Ghost", () => Go("Console Composer"))));
                Show(r, "plan-saved");
            }) : null, B("Back", "Btn.Ghost", back)));
            if (!p.CanShare) sp.Children.Add(T("This plan cannot be saved for sharing until the problems above are fixed.", Ty.Small, false, true));
            sp.Children.Add(problem);
            Show(sp, "plan-share");
        }

        void OpenPlan()
        {
            var f = new Microsoft.Win32.OpenFileDialog { Title = "Open a Shared Composition Plan", Filter = PlanFilter };
            if (f.ShowDialog(this) != true) return;
            Composition plan; string why = null;
            try
            {
                if (new FileInfo(f.FileName).Length > Composition.MaxBytes) throw new ComposeException("composition.size", "This file is larger than a shared plan can be.");
                var role = ShareGuard.Classify(f.FileName);
                if (role != FileRole.SharedComposition) throw new ComposeException("composition.role", "This is not a shared plan. " + ShareGuard.Describe(role));
                plan = Composition.Parse(File.ReadAllBytes(f.FileName));
            }
            catch (Exception ex) when (ex is ComposeException || ex is IOException || ex is UnauthorizedAccessException) { plan = null; why = ex.Message; }
            if (plan == null)
            {
                var ep = SectionHead("This plan could not be opened", "It is not a " + FileKinds.Plan + " this version can read. Nothing was changed.");
                ep.Children.Add(Note(why ?? "", "bad")); ep.Children.Add(PathText(f.FileName));
                ep.Children.Add(Buttons(B("Choose Another File…", "Btn", OpenPlan), B("Back", "Btn.Ghost", BackToSection)));
                Show(ep, "msg"); return;
            }
            ShowPlanChecklist(plan, System.IO.Path.GetFileName(f.FileName));
        }

        static string StartingPointName(string s) => s == "fresh-ccs" ? "Start Fresh with Classic Cart Studio" : s == "like-my-other-console" ? "Set Up Like My Other Console" : s == "rebuild-from-backups" ? "Rebuild from My Backups" : null;

        void ShowPlanChecklist(Composition plan, string file)
        {
            var sp = SectionHead("Shared Plan: " + Display.Line(plan.Title, 80), "What this " + FileKinds.Plan + " asks for, and what you would supply yourself. Opening a plan changes nothing, and a plan never changes a console by itself.");
            var c = new StackPanel();
            c.Children.Add(KeyValues(("File", file), ("Starting point", StartingPointName(plan.StartingPoint) ?? Display.Line(plan.StartingPoint, 40)),
                                     ("Add-ons", Composition.InstallPolicies.Contains(plan.InstallPolicy) ? Composition.PolicyName(plan.InstallPolicy) : Display.Line(plan.InstallPolicy, 40))));
            var items = plan.Checklist(PublicRegistry, id => false, (kind, name, version) => false, (id, sha) => false);
            string Label(string s) => s == "ready" ? "Ready" : s == "supply-your-own" ? "Your own" : s == "missing" ? "Missing" : s == "mismatched" ? "Does not match" : s == "unknown-build" ? "Not confirmed" : s == "unsupported" ? "Cannot be used" : s;
            c.Children.Add(T("What it asks for", Ty.Heading, true, false, new Thickness(0, 10, 0, 4)));
            foreach (var i in items)
                c.Children.Add(Note(Label(i.Status) + ": " + Display.Line(i.What, 80) + (i.Text != null ? ". " + Display.Line(i.Text, 240) : ""), i.Status == "ready" ? "ok" : i.Status == "supply-your-own" ? "info" : "warn"));
            if (items.Count == 0) c.Children.Add(T("This plan asks for nothing beyond its choices.", Ty.Text, false, true));
            c.Children.Add(T("A plan is never a restore point. To use it, choose your own backups and files in Console Composer; every change is reviewed on your console first, and the console is backed up before anything is written.", Ty.Small, false, true, new Thickness(0, 8, 0, 0)));
            if (!plan.CanApply) c.Children.Add(Note("This plan uses something this version does not know, so it can be read but not used: " + string.Join("; ", plan.Unsupported) + ".", "warn"));
            c.Children.Add(Details("Technical details", Composition.Label, plan.Id != null ? "Plan ID: " + plan.Id : null, "Starting point: " + plan.StartingPoint + "; add-ons: " + plan.InstallPolicy));
            sp.Children.Add(Card(c));
            var go = plan.CanApply ? StartingPointName(plan.StartingPoint) : null;
            sp.Children.Add(Buttons(go != null ? B(go, "Btn.Primary", () => { if (plan.StartingPoint == "fresh-ccs") ShowPrepare(); else ShowComposerPlan(plan.StartingPoint, null, null, plan.InstallPolicy); }) : null,
                                    B("Back", "Btn.Ghost", BackToSection)));
            Show(sp, "plan-checklist");
        }
    }
}
