// Classic Cart Studio console backup and restore window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The USB driver flow (CcsBackup.Driver; texts in DriverText): before the first real console operation of a window, this PC's driver state
// is read (reads only). A compatible driver already there is reused and left in place. With none, the setup page names every PC change of
// Workbench's own driver package and asks first; setup is never silent, and another program's driver is never replaced. The setup is one
// run of this same program with administrator permission (DriverActionHost), behind the closed DriverChanges boundary (Gate.cs): in this
// version the page says so and offers no setup. When the window closes, removal is offered only for a driver this window set up and only
// when the rules allow it (Eligibility); "No - Keep Driver" is the default. Inside Classic Cart Studio the window never removes it (the app
// may still use the console connection). Every step is recorded in the backup folder's "USB Driver Records" before it happens; when the
// window first shows, those records and the PC are read (only) to explain anything an earlier run left unfinished, and a certificate an
// earlier removal kept is offered for removal only once that is shown safe (TrustCleanup.cs). Simulated consoles never touch any of this.
// The pages and questions say in plain words why the driver is needed and what changes on this PC; package, catalog and certificate names,
// stores, thumbprints, Windows' codes and raw errors are under Technical details. The setup and close texts (DriverText) are shown as
// they are.
using CcsBackup.Driver;
using CcsBackup.Engine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace CcsBackup.Ui
{
    public sealed partial class BackupWorkspace
    {
        bool driverReady; DriverRecord driverRecord;
        // a driver a Workbench window inside Classic Cart Studio set up in this run of the app: that window's own close never offers its
        // removal (the app may still use the console), so the app's own close asks (DriverAtAppClose). The same holds for a certificate an
        // earlier removal kept: inside the app its cleanup is offered only when the app closes.
        static string appRunDriverFile;
        static readonly List<string> appCleanupFiles = new List<string>();
        public static bool AppRunDriverPending => appRunDriverFile != null || appCleanupFiles.Count > 0;

        /// <summary>why the driver is needed and what Workbench promises about it, in the words every driver page uses</summary>
        string DriverWhy => "Windows needs a USB driver to talk to your " + Con + ". Workbench uses it only for console tasks: backing up, checking, restoring or changing the console. Opening, browsing, checking and repairing backup files on this PC never need it.";
        const string DriverPromise = "Workbench asks before it sets up a driver and never installs or removes one by itself. It never replaces or removes another program's driver, such as Hakchi's.";

        SetupContext DriverContext(IDriverStore store) => new SetupContext
        {
            Store = store, Actions = new WindowsDriverActions(), Ledger = new DriverLedger(settings.PrimaryFolder), Elevation = Elevation.Current(), Plan = DriverSetupPlan.CcsWinUsb(),
            Host = opt.HostedInFullApp ? HostKind.Embedded : HostKind.Standalone, Tool = ProductTitle, ToolVersion = opt.Version,
        };

        /// <summary>runs go once the PC has a usable USB driver for the console (at most one check per window); simulated consoles go at once</summary>
        void WithDriver(Action go)
        {
            if (!string.IsNullOrEmpty(opt.SimulatedConsole) || driverReady) { go(); return; }
            var pv = new ProgressView(this, "Checking This PC's USB Driver", "Workbench reads Windows' list of drivers to find the one for your " + Con + ". Nothing is changed. This can take a few seconds.", null, null); Show(pv.Panel, "progress");
            var store = new WindowsDriverStore();
            Task.Run(() => DriverSetup.Assess(store)).ContinueWith(t =>
            {
                if (t.IsFaulted) { ShowDriverReadFailed(t.Exception.GetBaseException().Message); return; }
                var a = t.Result; var ctx = DriverContext(store);
                if (a.Reusable) { try { driverRecord = DriverSetup.RecordReuse(ctx, a); } catch (Exception) { } driverReady = true; go(); return; }
                if (a.SetupNeeded) { ShowDriverSetup(ctx, go); return; }
                ShowDriverProblem(a);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>Windows' driver list could not be read: the task did not start (Windows' own words under Technical details)</summary>
        void ShowDriverReadFailed(string error) =>
            Show(Message("This PC's USB drivers could not be read", "Workbench could not read Windows' list of USB drivers, so the console task did not start. Nothing on this PC or the console was changed. Try again in a moment.", "bad", error), "msg");

        /// <summary>a driver Workbench cannot use (another program's, one without the setting it needs, or a setup that would replace a
        /// certificate): explained, and nothing changed</summary>
        void ShowDriverProblem(Assessment a)
        {
            bool unreadable = a.Kind == AssociationKind.Unreadable, blocked = a.SetupBlocked;
            var sp = PageHead(unreadable ? "This PC's USB drivers could not be read" : blocked ? "The USB driver cannot be set up" : "This PC's USB driver for the console cannot be used",
                unreadable ? "The console task did not start: Workbench could not read Windows' list of USB drivers to find the one for your " + Con + ". Nothing on this PC or the console was changed."
                : blocked ? "The console task did not start: this PC has no USB driver for your " + Con + " yet, and Workbench cannot set one up here as things are. Nothing on this PC or the console was changed."
                : "The console task did not start: Workbench needs a USB driver on this PC to talk to your " + Con + ", and it cannot use the one this PC has for the console now. Nothing on this PC or the console was changed.");
            var c = new StackPanel();
            c.Children.Add(Note(a.Message ?? "This PC's USB driver for the console cannot be used as it is.", "warn"));
            foreach (var n in a.Notes) c.Children.Add(T(n, Ty.Small, false, true, new Thickness(0, 4, 0, 0)));
            if (!unreadable) c.Children.Add(T("Workbench never replaces or removes another program's driver, so it changed nothing.", Ty.Text, false, false, new Thickness(0, 8, 0, 0)));
            c.Children.Add(T("Backup files on this PC can still be opened, browsed, checked and repaired: they never need the USB driver.", Ty.Text, false, true, new Thickness(0, 8, 0, 0)));
            c.Children.Add(Details("Technical details", AssessmentDetails(a).Cast<object>().ToArray()));
            sp.Children.Add(Card(c));
            sp.Children.Add(Buttons(B("Back", "Btn", BackToSection)));
            Show(sp, "driver");
        }

        /// <summary>what Windows reported for the console's USB identity, for Technical details</summary>
        static IEnumerable<string> AssessmentDetails(Assessment a)
        {
            yield return "USB identity: " + PinnedDriver.HardwareId + "; finding: " + a.Kind + (a.StagedOnly ? " (a package only, no console connected with it)" : "");
            var d = a.Device;
            if (d != null)
            {
                yield return "Device: " + d.InstanceId + (d.Present ? " (connected now)" : "");
                yield return "Service: " + (string.IsNullOrEmpty(d.Service) ? "none" : d.Service) + "; package: " + (string.IsNullOrEmpty(d.InfPath) ? "none" : d.InfPath) + (d.Provider != null ? "; provider: " + d.Provider : "") + (d.DriverVersion != null ? "; version: " + d.DriverVersion : "");
                if (d.InterfaceGuids.Count > 0) yield return "Device interface GUIDs: " + string.Join(", ", d.InterfaceGuids);
                if (d.ProblemCode != 0) yield return "Windows' device problem code: " + d.ProblemCode;
                if (d.NeedsRestart) yield return "Windows marked this device as waiting for a restart.";
            }
            var p = a.Package;
            if (p != null)
            {
                yield return "Driver package: " + p.PublishedName + (p.OriginalName != null ? " (" + p.OriginalName + ")" : "") + (p.Provider != null ? "; provider: " + p.Provider : "") + (p.Services.Count > 0 ? "; services: " + string.Join(", ", p.Services) : "");
                if (p.SignerSubject != null) yield return "Catalog signer: " + p.SignerSubject + (p.SignerThumbprint != null ? " (" + p.SignerThumbprint + ")" : "");
                if (p.PendingRemoval) yield return "Windows has not finished removing this package (it waits for a restart).";
            }
        }

        static string StoreName(CertStore s) => s == CertStore.Root ? "Trusted Root Certification Authorities" : "Trusted Publishers";
        static string CertText(CertificateEntry c) => (string.IsNullOrEmpty(c.Subject) ? "a certificate" : "\"" + c.Subject + "\"") + " in " + StoreName(c.Store) + ", thumbprint " + c.Thumbprint;

        void ShowDriverSetup(SetupContext ctx, Action go)
        {
            // the title and sentence (DriverText), then why, what changes and what Workbench promises, in plain words; the exact list of
            // PC changes, as the driver audit gives it, is under Technical details
            var sp = PageHead(DriverText.SetupTitle, DriverText.SetupBodyFor(ctx.Elevation));
            var why = new StackPanel();
            why.Children.Add(T("Why this PC needs it", Ty.Heading, true, false, new Thickness(0, 0, 0, 8)));
            why.Children.Add(T(DriverWhy, Ty.Text));
            why.Children.Add(T(DriverPromise, Ty.Text));
            sp.Children.Add(Card(why));
            var c = new StackPanel();
            c.Children.Add(T(DriverText.SetupDetailsHeading, Ty.Heading, true, false, new Thickness(0, 0, 0, 8)));
            var plan = ctx.Plan;
            if (plan != null && plan.IsCcs)
                c.Children.Add(Numbered(
                    "A small driver package that tells Windows to use its own WinUSB driver for your " + Con + " whenever it is connected. It contains no driver program.",
                    "A new certificate, made for this setup only, added to this PC's Trusted Root Certification Authorities and Trusted Publishers lists so that Windows accepts that package. It can sign nothing else, and it stays on this PC with the driver.",
                    "Nothing else is installed, nothing is installed on the console, and no other driver or certificate is replaced or removed.",
                    "Windows keeps its own record of the setup in its logs. Removing the driver later does not erase that record."));
            else c.Children.Add(Numbered(DriverText.SetupDetails(plan).ToArray()));
            c.Children.Add(T(DriverText.SetupKeepOrRemove, Ty.Text, false, false, new Thickness(0, 6, 0, 6)));
            c.Children.Add(T("If you choose to remove it, Workbench removes only the driver it set up, and only when nothing on this PC still needs it. A removal Windows can finish only after a restart is left for that restart, and the certificate stays until then. Workbench never restarts your PC.", Ty.Small, false, true));
            var tech = new List<object>();
            if (plan != null && plan.IsCcs) tech.AddRange(DriverText.SetupDetails(plan));
            if (plan != null)
            {
                tech.Add("USB identity: " + plan.HardwareId + "; driver package: " + plan.InfName + (plan.CatalogName != null ? " with its catalog " + plan.CatalogName : "") + "; service: " + plan.Service + (plan.Provider != null ? "; provider: " + plan.Provider : ""));
                if (plan.InterfaceGuid != null) tech.Add("Device interface GUID: " + plan.InterfaceGuid);
                if (plan.InfSha256 != null) tech.Add("SHA-256 of the driver package's INF: " + plan.InfSha256);
                if (plan.CertificateSubject != null) tech.Add("Certificate subject: " + plan.CertificateSubject);
            }
            tech.Add("Each step is recorded before it happens, in " + (ctx.Ledger?.Folder ?? DriverLedger.FolderName) + ".");
            c.Children.Add(Details("Technical details", tech.ToArray()));
            sp.Children.Add(Card(c));
            if (!ctx.Actions.Available)
            {   // closed in this version (Gate.DriverChanges): the truth, and no button that would only fail
                sp.Children.Add(Note(DriverText.SetupUnavailableNote, "warn"));
                sp.Children.Add(Buttons(B("Back", "Btn.Ghost", BackToSection)));
                Show(sp, "driver-setup"); return;
            }
            sp.Children.Add(Buttons(B(DriverText.SetupButton, "Btn.Primary", () =>
            {
                var pv = new ProgressView(this, DriverText.SetupTitle, DriverText.AdminSentence(ctx.Elevation) + " Workbench sets up its driver package, then checks this PC again.", null, null); Show(pv.Panel, "progress");
                Task.Run(() => DriverSetup.Run(ctx)).ContinueWith(t =>
                {
                    var o = t.IsFaulted ? new SetupOutcome { Result = SetupResult.Failed, Message = t.Exception.GetBaseException().Message } : t.Result;
                    driverRecord = o.Record ?? driverRecord;
                    if (opt.HostedInFullApp && o.Record != null) appRunDriverFile = o.Record.File;
                    bool ok = o.Result == SetupResult.Installed || o.Result == SetupResult.InstalledNotOwned || o.Result == SetupResult.Reused || o.Result == SetupResult.NotNeeded;
                    if (ok) { driverReady = true; go(); return; }
                    var main = t.IsFaulted ? "The USB driver setup stopped with a problem, so Workbench cannot say whether it finished. Workbench checks this PC again before its next console task." : o.Message ?? "The USB driver setup did not finish.";
                    Show(Message(o.Result == SetupResult.InstalledRestartPending ? "Restart Windows to finish the driver setup" : "The USB driver was not set up", main,
                        o.Result == SetupResult.Canceled || o.Result == SetupResult.ElevationRefused ? "info" : "bad", "Result: " + o.Result + (t.IsFaulted ? "; " + o.Message : "") + (o.Record?.File != null ? "; record: " + o.Record.File : "")), "msg");
                }, TaskScheduler.FromCurrentSynchronizationContext());
            }), B(DriverText.SetupCancelButton, "Btn.Ghost", BackToSection)));
            Show(sp, "driver-setup");
        }

        /// <summary>what still needs the console connection now. countIdle: a worker that has finished its operation but still holds the
        /// connection (the console waits in its RAM program) counts as running.</summary>
        DriverUsage CurrentUsage(bool countIdle)
        {
            bool running = worker != null && worker.Running;
            var u = new DriverUsage { Host = opt.HostedInFullApp ? HostKind.Embedded : HostKind.Standalone, ParentAppNeedsDevice = opt.HostedInFullApp,
                ConsoleOperationActive = running && (!gotResult || countIdle), ConsoleWriteInProgress = writing };
            try { u.OtherProgramsOpen = DriverUsage.OtherProgramsFromProcesses(); } catch (Exception) { }
            try { u.OtherWorkbenchOperation = !running && ConsoleLock.IsBusy(); } catch (Exception) { }   // the console lock held by another window or program
            return u;
        }

        /// <summary>the exact driver package and certificates a removal would take away (and any certificate it would keep), for the removal
        /// question's Technical details</summary>
        static IEnumerable<string> RemovalDetails(CloseDecision d)
        {
            var r = d?.Decision; var p = r?.Plan;
            if (p != null)
            {
                yield return "Driver package to remove: " + p.PublishedName + (p.OriginalName != null ? " (" + p.OriginalName + ")" : "");
                foreach (var c in p.Certificates) yield return "Certificate to remove once the package is gone: " + CertText(c);
                if (p.Devices.Count > 0) yield return "Devices set up with it: " + string.Join(", ", p.Devices);
            }
            if (r != null) foreach (var (c, why) in r.CertificatesKept) yield return "Certificate kept: " + CertText(c) + ", because " + DriverText.Because(why);
            if (r != null && r.Reasons.Count > 1) yield return "Every reason it is kept: " + string.Join("; ", r.Reasons.Select(DriverText.Because)) + ".";
        }
        /// <summary>the certificates a later cleanup would remove, for its question's Technical details</summary>
        static IEnumerable<string> CleanupDetails(DriverRecord rec)
        {
            foreach (var c in rec?.DeferredCertificates ?? new List<CertificateEntry>()) yield return "Certificate: " + CertText(c);
            if (rec?.File != null) yield return "Record: " + rec.File;
        }

        /// <summary>at close: the removal question, only for a driver this Workbench set up and only when the rules allow; false = keep the window open</summary>
        bool DriverAtClose()
        {
            if (driverRecord == null || driverRecord.CloseChoice != null) return true;
            var store = new WindowsDriverStore();
            CloseDecision d;
            try { d = DriverSetup.AtClose(driverRecord, store, CurrentUsage(false), Elevation.Current()); } catch (Exception) { return true; }
            if (d.Kind == CloseKind.Nothing) return true;
            if (d.Kind == CloseKind.Message) { DriverDialog("The USB Driver", d.Message, new[] { "OK" }, 0, details: RemovalDetails(d)); return true; }
            int chosen = DriverDialog(d.Title, d.Body, d.Buttons, d.DefaultButton, cancelIndex: 2, details: RemovalDetails(d));
            var ctx = DriverContext(store);
            if (chosen == 0) { DriverSetup.Keep(ctx, driverRecord); return true; }
            if (chosen != 1) return false;   // Cancel: the window stays open
            if (worker != null && worker.Running && gotResult && !writing)
            {   // the finished operation's worker still holds the connection: end it first (it exits when its input closes)
                worker.Detach();
                for (int i = 0; i < 150 && worker.Running; i++) System.Threading.Thread.Sleep(100);
            }
            RemovalOutcome o; string error = null;
            try { o = DriverSetup.Remove(ctx, driverRecord, CurrentUsage(true)); }
            catch (Exception ex) { error = ex.Message; o = new RemovalOutcome { Result = RemovalResult.Failed, Message = DriverText.Kept(DriverText.Because(RetainReason.RemovalFailed)) }; }
            DriverDialog(d.Title, o.Message ?? "Workbench could not show what happened to the USB driver; it checks again when it next starts.", new[] { "OK" }, 0, details: new[] { "Result: " + o.Result, error, driverRecord.File != null ? "Record: " + driverRecord.File : null });
            return true;
        }

        /// <summary>Classic Cart Studio's own close: the removal question the standalone asks at its close, by the same rules and in the same
        /// words, for a driver a Workbench window inside the app set up in this run (once the app closes it no longer needs the console);
        /// nothing is asked otherwise. Removal stays behind Gate.DriverChanges. false = keep the app open (Cancel).</summary>
        public static bool DriverAtAppClose(Window owner, string version)
        {
            var file = appRunDriverFile;
            if (file != null && !AppCloseRemoval(owner, version, file)) return false;   // Cancel: the app stays open
            foreach (var f in appCleanupFiles.ToList()) AppCloseCleanup(owner, version, f);
            appCleanupFiles.Clear();
            return true;
        }

        /// <summary>the app no longer needs the console once it closes; anything else using it still counts</summary>
        static DriverUsage AppCloseUsage()
        {
            var usage = new DriverUsage { Host = HostKind.Embedded, ParentAppNeedsDevice = false };
            try { usage.OtherProgramsOpen = DriverUsage.OtherProgramsFromProcesses(); } catch (Exception) { }
            try { usage.OtherWorkbenchOperation = ConsoleLock.IsBusy(); } catch (Exception) { }
            return usage;
        }

        static SetupContext AppCloseContext(IDriverStore store, string file, string version) => new SetupContext
        {
            Store = store, Actions = new WindowsDriverActions(), Ledger = new DriverLedger(Path.GetDirectoryName(Path.GetDirectoryName(file))), Elevation = Elevation.Current(),
            Plan = DriverSetupPlan.CcsWinUsb(), Host = HostKind.Embedded, Tool = "Classic Cart Studio", ToolVersion = version,
        };

        static bool AppCloseRemoval(Window owner, string version, string file)
        {
            DriverRecord rec; try { rec = DriverLedger.Load(file); } catch (Exception) { return true; }
            if (rec.CloseChoice != null) return true;
            var store = new WindowsDriverStore(); var usage = AppCloseUsage();
            CloseDecision d;
            try { d = DriverSetup.AtClose(rec, store, usage, Elevation.Current()); } catch (Exception) { return true; }
            if (d.Kind == CloseKind.Nothing) return true;
            if (d.Kind == CloseKind.Message) { AppDialog(owner, "The USB Driver", d.Message, new[] { "OK" }, 0, -1, RemovalDetails(d)); return true; }
            int chosen = AppDialog(owner, d.Title, d.Body, d.Buttons, d.DefaultButton, 2, RemovalDetails(d));
            var ctx = AppCloseContext(store, file, version);
            if (chosen == 0) { DriverSetup.Keep(ctx, rec); return true; }
            if (chosen != 1) return false;   // Cancel: the app stays open
            RemovalOutcome o; string error = null;
            try { o = DriverSetup.Remove(ctx, rec, usage); }
            catch (Exception ex) { error = ex.Message; o = new RemovalOutcome { Result = RemovalResult.Failed, Message = DriverText.Kept(DriverText.Because(RetainReason.RemovalFailed)) }; }
            AppDialog(owner, d.Title, o.Message ?? "Classic Cart Studio could not show what happened to the USB driver; Workbench checks again when it next starts.", new[] { "OK" }, 0, -1, new[] { "Result: " + o.Result, error, "Record: " + file });
            return true;
        }

        /// <summary>a certificate an earlier removal kept, found when a Workbench window inside the app started: the same question the
        /// standalone asks at its start, by the same rules, now that the app no longer needs the console</summary>
        static void AppCloseCleanup(Window owner, string version, string file)
        {
            DriverRecord rec; try { rec = DriverLedger.Load(file); } catch (Exception) { return; }
            var store = new WindowsDriverStore(); var usage = AppCloseUsage(); var ctx = AppCloseContext(store, file, version);
            CloseDecision d;
            try { d = DriverSetup.CleanupOffer(rec, store, usage, ctx.Elevation, ctx.Actions.Available); } catch (Exception) { return; }
            if (d.Kind == CloseKind.Nothing) return;
            if (d.Kind == CloseKind.Message) { AppDialog(owner, "The USB Driver", d.Message, new[] { "OK" }, 0, -1, CleanupDetails(rec)); return; }
            int chosen = AppDialog(owner, d.Title, d.Body, d.Buttons, d.DefaultButton, DriverText.CleanupLaterButton, CleanupDetails(rec));
            if (chosen == 0) { try { DriverSetup.KeepCertificate(ctx, rec); } catch (Exception) { } return; }
            if (chosen != 1) return;   // Ask Me Later
            CleanupOutcome o; string error = null;
            try { o = DriverSetup.CleanUp(ctx, rec, usage); }
            catch (Exception ex) { error = ex.Message; o = new CleanupOutcome { Result = CleanupResult.Failed, Message = string.Format(DriverText.CleanupKeptFormat, DriverText.Because(RetainReason.RemovalFailed)) }; }
            AppDialog(owner, d.Title, o.Message ?? "Classic Cart Studio could not show what happened to the certificate; Workbench checks again when it next starts.", new[] { "OK" }, 0, -1, new[] { "Result: " + o.Result, error, "Record: " + file });
        }

        /// <summary>DriverDialog's look for the app's own close (no Workbench window is needed): the Workbench theme, a dark title bar,
        /// wrapping text, a closed Technical details area when there are details, and buttons; returns the chosen button (cancelIndex, or the
        /// last button, when the window is closed)</summary>
        static int AppDialog(Window owner, string title, string body, string[] buttons, int defaultIndex, int cancelIndex, IEnumerable<string> details = null)
        {
            bool shown = owner != null && owner.IsLoaded;
            var dlg = new Window { Title = title, Owner = shown ? owner : null, SizeToContent = SizeToContent.Height, Width = 560, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = shown ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen, ShowInTaskbar = false };
            try { dlg.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/CcsBackupUi;component/Workbench.xaml", UriKind.Absolute) }); } catch { }
            var ink = dlg.TryFindResource("B.Ink") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.White;
            dlg.Background = dlg.TryFindResource("B.Bg") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Black;
            dlg.SourceInitialized += (s, e) => Dark(dlg);
            var sp = new StackPanel { Margin = new Thickness(24, 20, 24, 14) };
            sp.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.Bold, Foreground = ink, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
            foreach (var para in (body ?? "").Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries)) sp.Children.Add(new TextBlock { Text = para, FontSize = 13.5, Foreground = ink, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            var lines = (details ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrEmpty(x)).ToList();
            if (lines.Count > 0) sp.Children.Add(AppDetails(dlg, ink, lines));
            int chosen = cancelIndex >= 0 ? cancelIndex : buttons.Length - 1; var row = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            for (int i = 0; i < buttons.Length; i++)
            {
                int k = i; var b = new Button { Content = buttons[i], Style = dlg.TryFindResource(i == defaultIndex ? "Btn.Primary" : "Btn.Ghost") as Style, IsDefault = i == defaultIndex, IsCancel = i == cancelIndex, MinHeight = 34, Margin = new Thickness(0, 0, 10, 8) };
                System.Windows.Automation.AutomationProperties.SetName(b, buttons[i]); b.Click += (s, e) => { chosen = k; dlg.Close(); }; row.Children.Add(b);
            }
            sp.Children.Add(row); dlg.Content = sp; dlg.ShowDialog();
            return chosen;
        }

        /// <summary>the app close dialog's closed "Technical details" area (the same look as the window's own, built from the dialog's theme)</summary>
        static FrameworkElement AppDetails(Window dlg, System.Windows.Media.Brush ink, List<string> lines)
        {
            var body = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(26, 4, 0, 4) };
            foreach (var s in lines) body.Children.Add(new TextBox { Text = s, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4), FontSize = Ty.Small, Foreground = dlg.TryFindResource("B.Ink2") as System.Windows.Media.Brush ?? ink, MinHeight = 0 });
            var copy = new Button { Content = "Copy Details", Style = dlg.TryFindResource("Btn.Ghost") as Style, MinHeight = 30, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
            System.Windows.Automation.AutomationProperties.SetName(copy, "Copy Details");
            copy.Click += (s, e) => { try { Clipboard.SetText(string.Join(Environment.NewLine, lines)); } catch (Exception) { } };
            body.Children.Add(copy);
            var glyph = new TextBlock { Text = "", FontFamily = dlg.TryFindResource("F.Icons") as System.Windows.Media.FontFamily, FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 10, 0), Foreground = dlg.TryFindResource("B.Muted") as System.Windows.Media.Brush ?? ink };
            var head = new System.Windows.Controls.Primitives.ToggleButton { Style = dlg.TryFindResource("Wb.Disclosure") as Style, Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { glyph, new TextBlock { Text = "Technical details", FontSize = Ty.Text, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center } } } };
            System.Windows.Automation.AutomationProperties.SetName(head, "Technical details");
            head.Checked += (s, e) => { body.Visibility = Visibility.Visible; glyph.Text = ""; };
            head.Unchecked += (s, e) => { body.Visibility = Visibility.Collapsed; glyph.Text = ""; };
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 8) }; sp.Children.Add(head); sp.Children.Add(body);
            return sp;
        }

        /// <summary>the driver questions' and notices' content in the Workbench's own look: the title, the text wrapping, a closed Technical
        /// details area when there are details, and the buttons wrapping under it; chosen(i) when a button is pressed</summary>
        StackPanel DriverDialogPanel(string title, string body, string[] buttons, int defaultIndex, int cancelIndex, IEnumerable<string> details, Action<int> chosen)
        {
            var sp = new StackPanel { Margin = new Thickness(24, 20, 24, 14) }; sp.Children.Add(T(title, 17, true, false, new Thickness(0, 0, 0, 10)));
            foreach (var para in (body ?? "").Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries)) sp.Children.Add(T(para, 13.5, false, false, new Thickness(0, 0, 0, 8)));
            var lines = (details ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrEmpty(x)).Cast<object>().ToArray();
            if (lines.Length > 0) sp.Children.Add(Details("Technical details", lines));
            var row = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            for (int i = 0; i < buttons.Length; i++) { int k = i; var b = B(buttons[i], i == defaultIndex ? "Btn.Primary" : "Btn.Ghost", () => chosen(k)); b.IsDefault = i == defaultIndex; b.IsCancel = i == cancelIndex; row.Children.Add(b); }
            sp.Children.Add(row);
            return sp;
        }

        /// <summary>the driver questions and notices as a dialog of this window; returns the chosen button (cancelIndex, or the last button,
        /// when the window is closed)</summary>
        int DriverDialog(string title, string body, string[] buttons, int defaultIndex, int cancelIndex = -1, IEnumerable<string> details = null)
        {
            var dlg = new Window { Title = title, Owner = IsLoaded ? this : null, SizeToContent = SizeToContent.Height, Width = 560, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = IsLoaded ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen, Background = Res<System.Windows.Media.Brush>("B.Bg"), ShowInTaskbar = false };
            foreach (var md in Resources.MergedDictionaries) dlg.Resources.MergedDictionaries.Add(md);
            dlg.SourceInitialized += (s, e) => Dark(dlg);
            int chosen = cancelIndex >= 0 ? cancelIndex : buttons.Length - 1;
            dlg.Content = DriverDialogPanel(title, body, buttons, defaultIndex, cancelIndex, details, k => { chosen = k; dlg.Close(); });
            dlg.ShowDialog();
            return chosen;
        }

        /// <summary>the lead of the start-of-window notice about earlier driver changes (the records and the PC are only read)</summary>
        const string DriverRecordsLead = "Workbench read its records of earlier USB driver changes and this PC's list of drivers. Nothing was changed.";

        /// <summary>When the window first shows: if the backup folder has USB driver records, they and the PC are read (only, off the window's
        /// thread) and anything an earlier run left unfinished is explained: an interrupted setup or removal, a removal waiting for a restart, a
        /// setup that stopped part way, a certificate an earlier removal kept, an unreadable record. Nothing is changed, and nothing is shown
        /// when there is nothing to explain. A kept certificate whose removal is now shown safe (TrustCleanup.cs) is then offered, with
        /// "No - Keep Certificate" the default, through the same administrator step as every driver change (closed in this version by
        /// Gate.DriverChanges, and said so); inside Classic Cart Studio only when the app closes (DriverAtAppClose).</summary>
        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            if (!string.IsNullOrEmpty(opt.SimulatedConsole)) return;
            DriverLedger ledger;
            try { ledger = new DriverLedger(settings.PrimaryFolder); if (!Directory.Exists(ledger.Folder) || Directory.GetFiles(ledger.Folder, "usb-driver-*.json").Length == 0) return; }
            catch (Exception) { return; }
            var store = new WindowsDriverStore(); var ctx = DriverContext(store); var usage = CurrentUsage(false); bool hosted = opt.HostedInFullApp;
            Task.Run(() =>
            {
                var rep = DriverSetup.Reconcile(ledger, store);
                var offers = hosted ? new List<(DriverRecord rec, CloseDecision d)>()
                    : rep.Items.Where(i => i.Status == "cleanup-ready" && i.Record != null).Select(i => (i.Record, DriverSetup.CleanupOffer(i.Record, store, usage, ctx.Elevation, ctx.Actions.Available))).ToList();
                return (rep, offers);
            }).ContinueWith(t =>
            {
                if (t.IsFaulted || !IsLoaded) return;
                var (rep, offers) = t.Result;
                var shown = new[] { "incomplete-setup", "incomplete-removal", "removal-waiting-for-restart", "partial", "unreadable", "cleanup-deferred", "cleanup-ready" };
                var items = rep.Items.Where(i => shown.Contains(i.Status) && !(i.Status == "cleanup-ready" && !hosted)).ToList();   // the standalone asks about those below
                if (hosted) foreach (var i in rep.Items.Where(i => i.Status == "cleanup-ready")) if (!appCleanupFiles.Contains(i.File)) appCleanupFiles.Add(i.File);
                if (items.Count > 0) DriverDialog("The USB Driver on This PC", DriverRecordsLead + "\n\n" + string.Join("\n\n", items.Select(i => i.Message + (hosted && i.Status == "cleanup-ready" ? " " + DriverText.CleanupAtAppClose : "")).Distinct()), new[] { "OK" }, 0,
                    details: items.Select(i => "Record " + (i.File ?? "(unknown)") + ": " + i.Status).Concat(new[] { "Records folder: " + ledger.Folder }));
                OfferCleanups(ctx, offers, 0, usage);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>the later cleanup question, one record after another (the standalone, at its start); each cleanup runs off the window's thread</summary>
        void OfferCleanups(SetupContext ctx, List<(DriverRecord rec, CloseDecision d)> offers, int i, DriverUsage usage)
        {
            if (i >= offers.Count || !IsLoaded) return;
            var (rec, d) = offers[i];
            void Next() => OfferCleanups(ctx, offers, i + 1, usage);
            if (d.Kind == CloseKind.Nothing) { Next(); return; }
            if (d.Kind == CloseKind.Message) { DriverDialog("The USB Driver", d.Message, new[] { "OK" }, 0, details: CleanupDetails(rec)); Next(); return; }
            int chosen = DriverDialog(d.Title, d.Body, d.Buttons, d.DefaultButton, cancelIndex: DriverText.CleanupLaterButton, details: CleanupDetails(rec));
            if (chosen == 0) { try { DriverSetup.KeepCertificate(ctx, rec); } catch (Exception) { } Next(); return; }
            if (chosen != 1) { Next(); return; }   // Ask Me Later: asked again at the next start
            Task.Run(() => DriverSetup.CleanUp(ctx, rec, usage)).ContinueWith(t =>
            {
                DriverDialog(d.Title, t.IsFaulted ? string.Format(DriverText.CleanupKeptFormat, DriverText.Because(RetainReason.RemovalFailed)) : t.Result.Message ?? "Workbench could not show what happened to the certificate; it checks again when it next starts.", new[] { "OK" }, 0,
                    details: new[] { "Result: " + (t.IsFaulted ? CleanupResult.Failed : t.Result.Result), t.IsFaulted ? t.Exception.GetBaseException().Message : null, rec.File != null ? "Record: " + rec.File : null });
                Next();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
    }
}
