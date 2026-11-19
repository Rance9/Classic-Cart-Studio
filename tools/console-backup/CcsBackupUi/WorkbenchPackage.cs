// Classic Cart Studio console backup and restore window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Console Composer's Private Restore Package: built here without a console from the files chosen in Console Composer (a ZIP labeled
// PRIVATE, for one console), then applied later to that console in the controlled worker (PackageComposer, strict policy: that console is
// backed up in the same session first). It is only for the console it was built for, never offered for sharing and never a restore point.
using CcsBackup.Compose;
using CcsBackup.Engine;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using CUnits = CcsBackup.Compose.Units;

namespace CcsBackup.Ui
{
    public sealed partial class BackupWorkspace
    {
        const string PackageFilter = "Private Restore Packages (*.zip)|*.zip";

        void ShowBuildPackage(string start, string policy, List<(string backup, string path)> items, string planName = null)
        {
            var backup = items.First().backup; bool migrate = start == "like-my-other-console";
            var sourceTag = History.Peek(backup)?.O("console")?.S("tag");
            var sp = SectionHead("Build a Private Restore Package", "Save the " + items.Count + (items.Count == 1 ? " file" : " files") + " you chose into one " + FileKinds.Package + " on this PC, to put on one console later with Console Composer. Building it only reads your backups: no console is needed, and nothing on a console changes.");
            void Back() => ShowComposerPlan(start, items, planName, policy);
            var c = new StackPanel();
            c.Children.Add(T("The console it is for", Ty.Heading, true, false, new Thickness(0, 0, 0, 8)));
            var dest = new ComboBox(); Button build = null; var why = T("", Ty.Small, false, true, new Thickness(0, 0, 0, 0));
            if (migrate)
            {
                c.Children.Add(T("Choose a backup of the console that will receive these files (not your other console, where they come from). The package takes that console's identity from the backup and can be applied only to it.", Ty.Text));
                try
                {
                    foreach (var e in History.List(settings).Where(x => x.Status == BackupStatus.Verified && !string.Equals(x.PrimaryPath, backup, StringComparison.OrdinalIgnoreCase) && (sourceTag == null || x.ConsoleTag != sourceTag)))
                        dest.Items.Add(new ComboBoxItem { Content = e.When + "  ·  Console " + e.ConsoleTag, Tag = e.PrimaryPath });
                }
                catch (Exception ex) { c.Children.Add(Note("Your backup location could not be read: " + ex.Message, "bad")); }
                c.Children.Add(Field("A backup of the console the package is for", dest));
                if (dest.Items.Count == 0)
                {
                    c.Children.Add(Note("There is no checked backup of another console in your backup location. Back up the console the package is for first, then build the package.", "info"));
                    c.Children.Add(Buttons(B("Back Up Console", "Btn", () => { Enter("Console Backup"); ShowBackup(); })));
                }
            }
            else c.Children.Add(T("Console " + (sourceTag ?? "unknown") + ", the console these backups came from. The package can be applied only to it.", Ty.Text));
            c.Children.Add(Note(PrivatePackage.Label + " It holds copies of the chosen files and the identity of the console it is for. It never holds startup software, bootloaders or the storage layout, and it is never a restore point.", "warn"));
            sp.Children.Add(Card(c));
            build = B("Build the Package…", "Btn.Primary", () =>
            {
                var destBackup = migrate ? (dest.SelectedItem as ComboBoxItem)?.Tag as string : backup;
                if (destBackup == null) return;
                var tag = History.Peek(destBackup)?.O("console")?.S("tag") ?? "unknown";
                var d = new Microsoft.Win32.SaveFileDialog { Title = "Save the Private Restore Package", Filter = PackageFilter, FileName = PrivatePackage.FileStem(DateTime.Now, tag) + ".zip", OverwritePrompt = false };
                if (d.ShowDialog(this) != true) return;
                var pv = new ProgressView(this, "Building the Private Restore Package", "Your backups are checked again and the chosen files copied into one file on this PC. Nothing in the backups changes, and no console is needed.", null, null); Show(pv.Panel, "progress");
                Task.Run(() =>
                {
                    try { return (PackageComposer.Build(items, destBackup, policy, start, d.FileName, opt.Version), (string)null); }
                    catch (Exception ex) when (ex is ComposeException || ex is IOException || ex is UnauthorizedAccessException) { return ((PrivatePackageInfo)null, ex.Message); }
                }).ContinueWith(t =>
                {
                    var (info, problem) = t.Result;
                    if (info == null)
                    {
                        var ep = SectionHead("The package was not built", "Nothing was saved, and your backups are unchanged.");
                        ep.Children.Add(Note(problem ?? "", "bad"));
                        ep.Children.Add(Buttons(B("Back to the Package", "Btn", () => ShowBuildPackage(start, policy, items, planName)), B("Done", "Btn.Ghost", () => Go("Console Composer"))));
                        Show(ep, "msg"); return;
                    }
                    long bytes = 0; try { bytes = new FileInfo(d.FileName).Length; } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    ShowPackageBuilt(d.FileName, bytes, info);
                }, TaskScheduler.FromCurrentSynchronizationContext());
            }, !migrate || dest.SelectedItem != null);
            if (migrate)
            {
                why.Text = "Choose the console the package is for to build it.";
                dest.SelectionChanged += (s, e) => { build.IsEnabled = dest.SelectedItem != null; why.Visibility = build.IsEnabled ? Visibility.Collapsed : Visibility.Visible; };
            }
            else why.Visibility = Visibility.Collapsed;
            sp.Children.Add(Buttons(build, B("Back", "Btn.Ghost", Back)));
            sp.Children.Add(why);
            Show(sp, "build-package");
        }

        /// <summary>the built package: where it was saved, the console it is for, and how to apply it</summary>
        void ShowPackageBuilt(string file, long bytes, PrivatePackageInfo info)
        {
            var r = SectionHead("Private Restore Package saved", "It was opened again on this PC and every file in it checked. Nothing on a console has changed: apply it to console " + info.DestinationTag + " when that console is connected.");
            var c = new StackPanel();
            c.Children.Add(T("Saved as:", Ty.Small, false, true, new Thickness(0, 0, 0, 2))); c.Children.Add(PathText(file, 13));
            c.Children.Add(KeyValues(("For", "Console " + info.DestinationTag + " only"), ("Files", info.Members.Count.ToString(En)), ("Size", bytes > 0 ? CUnits.Size(bytes) : null)));
            c.Children.Add(Note(PrivatePackage.Label, "warn"));
            c.Children.Add(Details("Technical details", new object[] { "Package ID: " + info.PackageId }.Concat(info.Members.Take(40).Select(m => (object)(m.Role + " " + string.Join(", ", m.Places) + ": SHA-256 " + m.Sha256 + ", " + CUnits.Size(m.Size)))).ToArray()));
            r.Children.Add(Card(c));
            r.Children.Add(Buttons(B("Apply It to the Console…", "Btn.Primary", () => ShowApplyPackageReview(file, info)), B("Open Folder", "Btn", () => OpenFolder(file)), B("Done", "Btn.Ghost", () => Go("Console Composer"))));
            Show(r, "package-built");
        }

        void ShowApplyPackage()
        {
            var f = new Microsoft.Win32.OpenFileDialog { Title = "Open a Private Restore Package", Filter = PackageFilter };
            if (f.ShowDialog(this) != true) return;
            PrivatePackageInfo info = null; string why = null;
            try
            {
                var role = ShareGuard.Classify(f.FileName);
                if (role != FileRole.PrivateRestorePackage) throw new ComposeException("private.role", "This is not a private restore package. " + ShareGuard.Describe(role));
                info = PrivatePackage.Open(f.FileName, true);
            }
            catch (Exception ex) when (ex is ComposeException || ex is IOException || ex is UnauthorizedAccessException) { why = ex.Message; }
            if (info == null)
            {
                var ep = SectionHead("This file cannot be applied", "It did not pass the check for a " + FileKinds.Package + ". Nothing was changed.");
                ep.Children.Add(Note(why ?? "", "bad")); ep.Children.Add(PathText(f.FileName));
                ep.Children.Add(Buttons(B("Choose Another File…", "Btn", ShowApplyPackage), B("Back", "Btn.Ghost", BackToSection)));
                Show(ep, "msg"); return;
            }
            ShowApplyPackageReview(f.FileName, info);
        }

        /// <summary>a checked private package before a console is connected: the console it is for and the files it holds</summary>
        void ShowApplyPackageReview(string path, PrivatePackageInfo info)
        {
            var sp = SectionHead("Apply a Private Restore Package", "Put the files in this package on the console it was built for, console " + info.DestinationTag + ". This changes that console: you review every change once it is connected, and it is backed up before anything is written.");
            var c = new StackPanel();
            c.Children.Add(T("For console " + info.DestinationTag + " only", Ty.Heading, true, false, new Thickness(0, 0, 0, 6)));
            c.Children.Add(T("Any other console is refused. The console keeps its own startup software, bootloaders, storage layout and identity; only the files in the package are written.", Ty.Text));
            c.Children.Add(T("Package:", Ty.Small, false, true, new Thickness(0, 4, 0, 2))); c.Children.Add(PathText(path, 13));
            c.Children.Add(Note(PrivatePackage.Label, "warn"));
            sp.Children.Add(Card(c));
            var w = new StackPanel();
            w.Children.Add(T("What it holds", Ty.Heading, true, false, new Thickness(0, 0, 0, 6)));
            w.Children.Add(T(info.Members.Count + (info.Members.Count == 1 ? " file" : " files") + ", every one checked just now. Whether each is added or replaces a file is shown in the review, after the console is connected.", Ty.Small, false, true));
            w.Children.Add(ChangeTable(info.Members.Take(30).Select(m => ("File", string.Join(", ", m.Places.Select(p => p.StartsWith("data:") ? p.Substring(5) : p)), CUnits.Size(m.Size)))));
            if (info.Members.Count > 30) w.Children.Add(T("…and " + (info.Members.Count - 30) + " more.", 12.5, false, true));
            w.Children.Add(Details("Technical details", new object[] { "Package ID: " + info.PackageId }.Concat(info.Members.Take(30).Select(m => (object)(string.Join(", ", m.Places) + ": SHA-256 " + m.Sha256))).ToArray()));
            sp.Children.Add(Card(w));
            if (string.IsNullOrEmpty(opt.SimulatedConsole) || !profile.ConsoleTasks) sp.Children.Add(ConnectSteps("Nothing is written until you confirm the changes; the console is backed up first."));
            sp.Children.Add(Buttons(B(string.IsNullOrEmpty(opt.SimulatedConsole) ? "Connect and Review" : "Connect and Review (Simulated)", "Btn.Primary", () =>
            {
                composePlanKind = composeTag = composeProtective = null;
                var r = ConsoleReq("compose"); r.Add("kind", "package"); r.Add("package", path);
                StartOp("compose", "Preparing the Review", "The connected console is identified and backed up, and the package is checked again. Nothing is written until you confirm. Keep the USB cable connected.", r);
            }), B("Back", "Btn.Ghost", BackToSection)));
            Show(sp, "apply-package");
        }
    }
}
