// Classic Cart Studio console backup and restore window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// HOME Designer's preview: the actual HOME, drawn on this PC by the console HOME's own program (in the standalone Workbench the one linked
// into it, otherwise Classic Cart Studio's ccs-sim-core.exe) with real games. The design is compiled by the one compiler for a CCS HOME
// package, and the HOME starts with it the way the console starts (HomeBoot): its first screen is the large preview, drawn again within
// moments of each accepted change. A little later the full render check draws every other screen of the design's theme in the background
// (the same check Check and Apply runs), and the wizard shows the screens that matter for its step (Game Details, the START menu, Browse,
// the startup picture) under the large one, with that check's findings in short sentences. By default the package is the CCS HOME this
// program carries (its Included Games), so no console and no backup are needed; Classic Cart Studio's prepared HOME menu (the person's own
// games) and the CCS HOME in a backup are choices under "Preview with". The last pictures stay, marked, while new ones are drawn; a picture
// that does not show the latest changes says so. A preview that cannot be drawn says why, with Retry where trying again can help: never a
// sketch or an imitation in its place. Nothing is written anywhere but this window's own folder under the PC's temporary folder (removed
// when the window closes), and nothing on a console changes.
using CcsBackup.Compose;
using CcsBackup.Engine;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace CcsBackup.Ui
{
    public sealed partial class BackupWorkspace
    {
        const string EmbeddedHome = "embedded:mod";   // the release-built CCS HOME this program carries (HomePackage.Embedded)

        /// <summary>a HOME package a preview can be drawn with</summary>
        sealed class PreviewSource { public string Key, Label, Short, Path; public bool Backup; }

        string pvWork;                 // this window's preview folder (created when first needed)
        string pvPick;                 // the chosen source's key (null: the first one available)
        List<PreviewSource> pvBackupSources;   // backups listed once "Preview with" is opened
        readonly Dictionary<string, (string dir, List<ConsoleGame> games)> pvPacks = new Dictionary<string, (string, List<ConsoleGame>)>();
        BitmapSource pvImage; string pvImageKey;          // the first screen as last drawn, and the design and source it shows
        bool pvBusy, pvFailed; string pvFailDetail;       // drawing now; the last try failed (Retry)
        string pvBlocked, pvBlockedDetail;               // why the design cannot be drawn as it is (the person can change that)
        int pvRound;
        // the other screens (the full render check), drawn after the first screen
        Dictionary<string, BitmapSource> pvShots; string pvShotsKey; RenderCheckResult pvShotsCheck;
        bool pvShotsBusy, pvShotsFailed; string pvShotsFailDetail; CancellationTokenSource pvShotsCancel; DispatcherTimer pvShotsTimer;
        bool designGuides;
        Action pvRefresh;
        /// <summary>a test can make the drawings fail (returns the exception to throw, or null)</summary>
#pragma warning disable CS0649
        internal static Func<string, Exception> PreviewFault;
#pragma warning restore CS0649

        /// <summary>the render check's screens, in the order the HOME is used, with the names the wizard gives them</summary>
        static readonly Dictionary<string, string> StateNames = new Dictionary<string, string>
        {
            ["all-games"] = "All Games", ["long-title"] = "The longest title", ["last-game"] = "The last game", ["details"] = "Game Details", ["details-long-title"] = "Game Details, long title",
            ["menu"] = "START menu", ["menu-focus"] = "START menu, another row", ["theme-picker"] = "Theme picker", ["browse"] = "Browse", ["browse-focus"] = "Browse, a tile in focus",
            ["folder"] = "A folder", ["folder-empty"] = "An empty folder", ["navigation-focus"] = "Navigation in focus", ["notice"] = "A notice", ["startup"] = "Startup picture",
        };
        static int StateOrder(string s) { int i = 0; foreach (var k in StateNames.Keys) { if (k == s) return i; i++; } return i; }

        /// <summary>the HOME packages a preview can be drawn with: the CCS HOME this program carries, Classic Cart Studio's prepared HOME menu,
        /// and (when asked) the CCS HOME in each recent backup</summary>
        List<PreviewSource> PreviewSources(bool withBackups)
        {
            var l = new List<PreviewSource>();
            try { if (HomePackage.Embedded("mod") != null) l.Add(new PreviewSource { Key = EmbeddedHome, Label = "The Included Games (the CCS HOME that comes with Workbench)", Short = "the Included Games" }); }
            catch (HomePackageException) { }
            var app = opt.PreparedHomePackage;
            if (app != null && File.Exists(System.IO.Path.Combine(app, "pack.json")) && File.Exists(System.IO.Path.Combine(app, "games.json")))
                l.Add(new PreviewSource { Key = "prepared", Label = "Your collection's games (the HOME menu Classic Cart Studio prepared on this PC)", Short = "your collection's games", Path = app });
            if (withBackups)
            {
                if (pvBackupSources == null)
                {
                    pvBackupSources = new List<PreviewSource>();
                    try { foreach (var e in History.List(settings).Where(x => x.Status == BackupStatus.Verified || x.Status == BackupStatus.NeedsVerification).Take(12)) pvBackupSources.Add(new PreviewSource { Key = "backup:" + e.PrimaryPath, Label = "The CCS HOME on console " + e.ConsoleTag + ", from its backup of " + e.When, Short = "the games in a backup of console " + e.ConsoleTag, Path = e.PrimaryPath, Backup = true }); }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException) { }
                }
                l.AddRange(pvBackupSources);
            }
            else if (pvPick != null && pvBackupSources != null) l.AddRange(pvBackupSources.Where(x => x.Key == pvPick));
            return l;
        }
        PreviewSource PreviewSourceNow() { var l = PreviewSources(false); return l.FirstOrDefault(x => x.Key == pvPick) ?? l.FirstOrDefault(); }
        static IHomeRenderer PreviewRenderer() => HomeRenderers.Find(AppHomeRenderer());

        /// <summary>a new or another design: nothing drawn for the earlier one is shown for it</summary>
        void PreviewForget()
        {
            pvImage = null; pvImageKey = null; pvFailed = false; pvFailDetail = pvBlocked = pvBlockedDetail = null;
            pvShotsCancel?.Cancel(); pvShots = null; pvShotsKey = null; pvShotsCheck = null; pvShotsFailed = false; pvShotsFailDetail = null;
        }

        string PreviewKey() { var h = design; var s = PreviewSourceNow(); return h == null || s == null ? null : h.ContentSha256 + "|" + s.Key; }

        /// <summary>why the open design cannot be drawn now (null: it can), in a short sentence and the model's words</summary>
        (string text, string detail)? PreviewCannot(HomeDesign h)
        {
            if (h.InspectOnly) return ("A design from a newer version of Classic Cart Studio cannot be drawn here.", string.Join("; ", h.Unsupported));
            if (!h.CanApply) { var p = PlainIssues(h.Unsupported); return (p.FirstOrDefault() ?? "This design cannot be drawn as it is.", "Check: " + string.Join("; ", h.Unsupported)); }
            return null;
        }

        /// <summary>after a change (or when the wizard opens): draws the design's first screen as it is now, unless that picture is already
        /// shown or being drawn (a drawing under way picks up the latest design when it ends); a check of the other screens under way for an
        /// earlier design stops, and starts again shortly after</summary>
        void RequestPreview()
        {
            if (design == null) return;
            if (pvShotsBusy && pvShotsCancel != null && !pvShotsCancel.IsCancellationRequested && PreviewKey() != pvShotsKeyDrawing) pvShotsCancel.Cancel();
            if (!pvBusy) DrawPreview(); else pvRefresh?.Invoke();
        }

        async void DrawPreview()
        {
            if (pvBusy) return;
            pvBusy = true; pvFailed = false; pvFailDetail = null; pvRefresh?.Invoke();
            try
            {
                for (int guard = 0; guard < 100; guard++)
                {
                    var h = design; if (h == null) break;
                    var cannot = PreviewCannot(h);
                    if (cannot != null) { pvBlocked = cannot.Value.text; pvBlockedDetail = cannot.Value.detail; break; }
                    var src = PreviewSourceNow(); var renderer = PreviewRenderer();
                    if (src == null || renderer == null) { pvBlocked = null; break; }   // the panel says what is missing
                    var key = h.ContentSha256 + "|" + src.Key;
                    if (key == pvImageKey) { pvBlocked = null; break; }   // already shown
                    var work = PreviewFolder();
                    var pack = await Task.Run(() => PreviewPack(src, work));
                    if (design != h || h.ContentSha256 + "|" + src.Key != key) continue;   // changed meanwhile: the newest is drawn
                    CompiledDesign compiled;
                    try { compiled = DesignCompiler.Compile(h, pack.games); }   // here, on the window's thread: the design exactly as it is now
                    catch (ComposeException ex) { var (t, d) = Plain(ex); pvBlocked = t; pvBlockedDetail = d ?? ex.Message; break; }
                    int n = ++pvRound; var fault = PreviewFault;
                    var (res, bmp) = await Task.Run(() =>
                    {
                        var f = fault?.Invoke(key); if (f != null) throw f;
                        var root = System.IO.Path.Combine(work, "design-root"); HomeBoot.Place(compiled, root);
                        var w = System.IO.Path.Combine(work, "start-" + n);
                        try { var r = HomeBoot.Draw(renderer, pack.dir, root, w); return (r, r.UsedDesign ? Bitmap(r.Frame) : null); }
                        finally { try { Directory.Delete(w, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
                    });
                    if (design != h) continue;
                    if (bmp == null) { pvBlocked = "The HOME could not use this design as it is."; pvBlockedDetail = res.Why ?? res.Report; break; }
                    pvImage = bmp; pvImageKey = key; pvBlocked = pvBlockedDetail = null;
                    pvRefresh?.Invoke();
                }
            }
            catch (Exception ex)
            {
                var b = ex is AggregateException ae ? ae.GetBaseException() : ex;
                pvFailed = true; pvFailDetail = b.GetType().Name + ": " + b.Message;
            }
            finally { pvBusy = false; pvRefresh?.Invoke(); ScheduleShots(); }
        }

        static BitmapSource Bitmap(RenderFrame f) { var b = BitmapSource.Create(f.Width, f.Height, 96, 96, PixelFormats.Rgb24, null, f.Rgb, f.Width * 3); b.Freeze(); return b; }
        static BitmapSource Scaled(RenderFrame f, double scale) { var b = new TransformedBitmap(Bitmap(f), new ScaleTransform(scale, scale)); b.Freeze(); return b; }

        // ------------------------------------------------------------------ the other screens: the full render check, in the background
        string pvShotsKeyDrawing;
        void ScheduleShots()
        {
            if (design == null || pvBlocked != null || pvFailed || pvImageKey == null || PreviewKey() != pvImageKey || pvShotsKey == pvImageKey && !pvShotsFailed) return;
            if (pvShotsTimer == null) { pvShotsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) }; pvShotsTimer.Tick += (s, e) => { pvShotsTimer.Stop(); DrawShots(); }; }
            pvShotsTimer.Stop(); pvShotsTimer.Start();
        }

        async void DrawShots()
        {
            if (pvShotsBusy || pvBusy) { ScheduleShots(); return; }
            var h = design; if (h == null || PreviewCannot(h) != null) return;
            var src = PreviewSourceNow(); var renderer = PreviewRenderer(); if (src == null || renderer == null) return;
            var key = h.ContentSha256 + "|" + src.Key; if (key == pvShotsKey && !pvShotsFailed) return;
            pvShotsBusy = true; pvShotsFailed = false; pvShotsFailDetail = null; pvShotsKeyDrawing = key; var cts = pvShotsCancel = new CancellationTokenSource(); pvRefresh?.Invoke();
            try
            {
                var work = PreviewFolder();
                var pack = await Task.Run(() => PreviewPack(src, work));
                if (design != h || h.ContentSha256 + "|" + src.Key != key || cts.IsCancellationRequested) return;
                var compiled = DesignCompiler.Compile(h, pack.games);
                var (r, shots) = await RunCheck(renderer, pack.dir, compiled, key, work, cts.Token, 0.5);
                if (cts.IsCancellationRequested || design != h) return;
                pvShots = shots; pvShotsCheck = r; pvShotsKey = key;
            }
            catch (Exception ex) when (ex is OperationCanceledException || cts.IsCancellationRequested) { }
            catch (Exception ex) { var b = ex is AggregateException ae ? ae.GetBaseException() : ex; if (!(b is OperationCanceledException)) { pvShotsFailed = true; pvShotsFailDetail = b.GetType().Name + ": " + b.Message; } }
            finally
            {
                pvShotsBusy = false; pvShotsKeyDrawing = null; pvRefresh?.Invoke();
                if (design != null && PreviewKey() != pvShotsKey && !pvShotsFailed) ScheduleShots();
            }
        }

        /// <summary>the full render check of a compiled design, keeping the design theme's screens and the startup picture as pictures at
        /// scale (on a background thread); the check's report stays whole</summary>
        Task<(RenderCheckResult, Dictionary<string, BitmapSource>)> RunCheck(IHomeRenderer renderer, string pack, CompiledDesign compiled, string key, string work, CancellationToken cancel, double scale)
        {
            var fault = PreviewFault;
            return Task.Run(() =>
            {
                var f = fault?.Invoke(key); if (f != null) throw f;
                var dir = System.IO.Path.Combine(work, "screens-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                try
                {
                    compiled.WriteTo(System.IO.Path.Combine(dir, "design"));
                    var res = renderer.Check(pack, System.IO.Path.Combine(dir, "design"), new RenderCheckOptions { WorkDir = System.IO.Path.Combine(dir, "check"), Frames = true, Cancel = cancel, KeepFrame = (th, st, with) => with && (th == compiled.Theme || th == "startup") });
                    cancel.ThrowIfCancellationRequested();
                    var shots = new Dictionary<string, BitmapSource>();
                    foreach (var fr in res.Frames.Where(x => x.WithDesign && (x.Theme == res.DesignTheme || x.Theme == "startup"))) shots[fr.State] = Scaled(fr, scale);
                    res.Frames.Clear();
                    return (res, shots);
                }
                finally { try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
            }, cancel);
        }

        /// <summary>this window's preview folder under the PC's temporary folder (HOME package data, compiled designs and drawn frames, never
        /// a program); removed when the window closes, and folders older than a day left by an earlier window are removed when it is made</summary>
        string PreviewFolder()
        {
            if (pvWork != null) return pvWork;
            var top = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CCS Workbench HOME Preview");
            try { if (Directory.Exists(top)) foreach (var d in Directory.GetDirectories(top)) if (Directory.GetLastWriteTimeUtc(d) < DateTime.UtcNow.AddDays(-1)) try { Directory.Delete(d, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
            pvWork = System.IO.Path.Combine(top, System.Diagnostics.Process.GetCurrentProcess().Id.ToString(En) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(pvWork);
            Closed += (s, e) => { pvShotsCancel?.Cancel(); var w = pvWork; pvWork = null; try { if (w != null) Directory.Delete(w, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } };
            return pvWork;
        }

        /// <summary>a source's HOME package folder and its games, made once per window (the carried package and a backup's are written out,
        /// every file checked against the package's own list; Classic Cart Studio's prepared folder is only read)</summary>
        (string dir, List<ConsoleGame> games) PreviewPack(PreviewSource src, string work)
        {
            lock (pvPacks)
            {
                if (pvPacks.TryGetValue(src.Key, out var have) && Directory.Exists(have.dir)) return have;
                string dir;
                if (src.Key == EmbeddedHome) dir = EmbeddedHomeFolder(System.IO.Path.Combine(work, "pack-included-" + Guid.NewGuid().ToString("N").Substring(0, 6)));
                else if (src.Backup) { var st = DesignApplyEngine.ReadState(src.Path); dir = DesignApplyEngine.HomePackage(src.Path, st, null, System.IO.Path.Combine(work, "pack-" + Guid.NewGuid().ToString("N").Substring(0, 8))); }
                else dir = src.Path;
                var games = Json.Parse(File.ReadAllText(System.IO.Path.Combine(dir, "games.json"), Encoding.UTF8)).A("games").OfType<Dictionary<string, object>>().Select(g => new ConsoleGame { Id = g.S("id"), Title = g.S("title"), CatalogId = g.S("catalog") }).ToList();
                pvPacks[src.Key] = (dir, games);
                return (dir, games);
            }
        }

        /// <summary>the carried package's HOME folder written out for the HOME program, every file checked against the package's own list</summary>
        static string EmbeddedHomeFolder(string outDir)
        {
            var p = HomePackage.Embedded("mod") ?? throw new InvalidDataException("this copy does not carry a CCS HOME");
            foreach (var f in p.Files.Where(x => x.Rel.StartsWith("home/", StringComparison.Ordinal)))
            {
                var dst = System.IO.Path.Combine(outDir, f.Rel.Substring(5).Replace('/', System.IO.Path.DirectorySeparatorChar)); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dst));
                try { File.WriteAllBytes(dst, p.Read(f.Rel)); } catch (HomePackageException ex) { throw new InvalidDataException(ex.Message); }
            }
            return outDir;
        }

        // ------------------------------------------------------------------ the preview panel
        /// <summary>the screens each step shows under the large first screen (only those the check drew for this design)</summary>
        static string[] StepScreens(int step) =>
            step == StepPictures ? new[] { "details", "startup", "menu" } : step == StepText ? new[] { "menu", "details", "browse" } : step == StepLayout ? new[] { "browse", "folder", "details" } : new[] { "details", "menu", "browse" };

        /// <summary>the preview as the wizard shows it: the HOME's first screen, a status line (with Retry when drawing failed), more of the
        /// HOME's screens for the step, the optional placement guides (Layout &amp; Folders) and, closed, the choice of games it is drawn with</summary>
        FrameworkElement PreviewPanel(int step)
        {
            bool layoutStep = step == StepLayout;
            var sp = new StackPanel();
            var img = new Image { Width = 1280, Height = 720, Stretch = Stretch.Fill }; RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            var waitText = new TextBlock { FontSize = 40, Foreground = new SolidColorBrush(Color.FromRgb(0xb0, 0xb0, 0xb8)), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(120, 0, 120, 0) };
            var placeholder = new Border { Background = new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x1c)), Child = waitText };
            var guides = new Canvas { Width = 1280, Height = 720, IsHitTestVisible = false };
            var pill = new Border { Background = new SolidColorBrush(Color.FromArgb(0xe0, 0x10, 0x10, 0x14)), CornerRadius = new CornerRadius(26), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 26, 26, 0), Visibility = Visibility.Collapsed,
                                    Child = new TextBlock { Text = "Updating…", FontSize = 30, Foreground = Brushes.White, Margin = new Thickness(22, 8, 22, 10) } };
            var stage = new Grid { Width = 1280, Height = 720 }; stage.Children.Add(placeholder); stage.Children.Add(img); stage.Children.Add(guides); stage.Children.Add(pill);
            var frame = new Border { BorderBrush = Res<Brush>("B.Line2"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Background = Brushes.Black, Padding = new Thickness(1), Child = new Viewbox { Child = stage, Stretch = Stretch.Uniform } };
            var open = new Button { Style = Res<Style>("Wb.Plain"), Content = frame, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(0) };
            open.Click += (s, e) => { if (pvImage != null) { ShowPicture(pvImage, "Your HOME", PreviewCaption(), "Drawn on this PC by the console HOME's own program. Nothing on a console changed."); open.Focus(); } };
            sp.Children.Add(open);

            var status = PictureCaption(null); status.VerticalAlignment = VerticalAlignment.Center; status.Margin = new Thickness(2, 6, 10, 0);   // the preview's caption: what it shows, or why it is not up to date
            AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
            var retry = Small("Retry", () => { pvFailed = false; pvShotsFailed = false; RequestPreview(); }, true, "Retry the preview"); retry.Visibility = Visibility.Collapsed;
            var line = new DockPanel(); DockPanel.SetDock(retry, Dock.Right); line.Children.Add(retry); line.Children.Add(status);
            sp.Children.Add(line);
            var tech = new ContentControl { Focusable = false }; sp.Children.Add(tech);

            // more of the HOME's screens for this step, from the full check
            var more = new UniformGrid { Rows = 1, Columns = 3, Margin = new Thickness(-4, 10, -4, 0) };
            var cells = new List<(string state, Image im, TextBlock cap, Button b)>();
            foreach (var st0 in StepScreens(step))
            {
                var st = st0; var name = StateNames[st];
                var im = new Image { Stretch = Stretch.Uniform }; RenderOptions.SetBitmapScalingMode(im, BitmapScalingMode.HighQuality);
                var box = new Border { BorderBrush = Res<Brush>("B.Line2"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x1c)), Child = new Viewbox { Child = new Grid { Width = 640, Height = 360, Children = { im } }, Stretch = Stretch.Uniform } };
                var cap = PictureCaption(name); cap.Margin = new Thickness(2, 3, 0, 0);
                var cell = new StackPanel(); cell.Children.Add(box); cell.Children.Add(cap);
                var b = new Button { Style = Res<Style>("Wb.Plain"), Content = cell, Margin = new Thickness(4, 0, 4, 0), Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
                b.Click += (s, e) => { if (pvShots != null && pvShots.TryGetValue(st, out var pic)) { ShowPicture(pic, name, "Your design on the HOME's " + name + " screen.", "Drawn on this PC by the console HOME's own program. Nothing on a console changed."); b.Focus(); } };
                cells.Add((st, im, cap, b)); more.Children.Add(b);
            }
            sp.Children.Add(more);
            var moreStatus = new TextBlock { FontSize = Ty.Small, Foreground = Res<Brush>("B.Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
            AutomationProperties.SetLiveSetting(moreStatus, AutomationLiveSetting.Polite);
            sp.Children.Add(moreStatus);
            var findings = new ContentControl { Focusable = false }; sp.Children.Add(findings);

            if (layoutStep)
            {
                var g = new CheckBox { Content = "Show where the HOME's controls are", IsChecked = designGuides, Margin = new Thickness(0, 10, 0, 0) };
                g.Click += (s, e) => { designGuides = g.IsChecked == true; pvRefresh?.Invoke(); };
                sp.Children.Add(g);
            }
            var with = PreviewWith(); sp.Children.Add(with);

            string lastStatus = null, lastTech = null, lastFindings = null;
            void Refresh()
            {
                var h = design; if (h == null) return;
                var renderer = PreviewRenderer(); var src = PreviewSourceNow(); var key = PreviewKey();
                bool current = pvImage != null && pvImageKey == key;
                string say, techText = null; bool showRetry = false, stale = pvImage != null && !current;
                if (renderer == null) { say = "The HOME preview is not available in this copy of Workbench: it does not have the console HOME's program."; stale = pvImage != null; }
                else if (src == null) say = "The HOME preview needs the CCS HOME that comes with Workbench, and this copy does not have it.";
                else if (pvBusy) say = pvImage == null ? "Drawing your HOME…" : "Updating…";
                else if (pvFailed) { say = "The preview could not be drawn." + (pvImage != null ? " The picture shows your design before your latest change." : ""); showRetry = true; techText = pvFailDetail; }
                else if (pvBlocked != null) { say = pvBlocked + (pvImage != null ? " The picture shows your design before your latest change." : ""); techText = pvBlockedDetail; }
                else if (current) say = PreviewCaption();
                else say = "Updating…";
                waitText.Text = pvImage != null ? "" : renderer == null || src == null ? "No preview in this copy" : pvBusy ? "Drawing your HOME…" : pvFailed ? "The preview could not be drawn" : pvBlocked != null ? "Not drawn" : "Drawing your HOME…";
                img.Source = pvImage; img.Opacity = stale ? 0.4 : 1;
                pill.Visibility = pvBusy && pvImage != null ? Visibility.Visible : Visibility.Collapsed;
                status.Text = say;
                if (techText != lastTech) { tech.Content = string.IsNullOrEmpty(techText) ? null : Details("Technical details", techText); lastTech = techText; }
                open.IsEnabled = pvImage != null;
                AutomationProperties.SetName(open, pvImage == null ? "Preview of your HOME: " + waitText.Text : (current ? "Preview of your HOME's first screen, as the console HOME draws it" : "Preview of your HOME, before your latest change") + ". Select to see it larger.");
                DrawGuides(guides, h, layoutStep && designGuides);

                // the other screens
                bool shotsCurrent = pvShots != null && pvShotsKey == key;
                bool anyShown = false;
                foreach (var (st, im, cap, b) in cells)
                {
                    BitmapSource pic = null; bool has = pvShots != null && pvShots.TryGetValue(st, out pic);
                    var ok = pvShotsCheck?.States.FirstOrDefault(x => x.Theme == pvShotsCheck.DesignTheme && x.State == st)?.Ok ?? true;
                    b.Visibility = has || pvShots == null ? Visibility.Visible : Visibility.Collapsed; if (has) anyShown = true;
                    im.Source = pic; im.Opacity = shotsCurrent ? 1 : 0.4; b.IsEnabled = has;
                    cap.Text = StateNames[st] + (has && shotsCurrent && !ok ? "  ·  hard to read" : "");
                    AutomationProperties.SetName(b, StateNames[st] + (has ? (shotsCurrent ? "" : ", before your latest change") + (has && shotsCurrent && !ok ? ", hard to read" : "") + ". Select to see it larger." : ": not drawn yet"));
                }
                more.Visibility = renderer == null || src == null || pvBlocked != null && pvShots == null ? Visibility.Collapsed : Visibility.Visible;
                more.Columns = Math.Max(1, cells.Count(c => c.b.Visibility == Visibility.Visible));
                string moreSay = null, find = null;
                if (renderer == null || src == null || PreviewCannot(h) != null) moreSay = null;
                else if (pvShotsBusy || (!shotsCurrent && !pvShotsFailed && (pvBusy || current))) moreSay = "More screens: " + (anyShown ? "updating…" : "drawing…");
                else if (pvShotsFailed) { moreSay = "The other screens could not be drawn. Retry draws them again."; showRetry = true; techText = pvShotsFailDetail; }
                else if (shotsCurrent) { moreSay = "More of your HOME's screens. Review & Save can show every screen."; if (!pvShotsCheck.Ok) find = string.Join("\n", PlainCheck(pvShotsCheck)); }
                moreStatus.Text = moreSay ?? ""; moreStatus.Visibility = moreSay == null ? Visibility.Collapsed : Visibility.Visible;
                if (find != lastFindings) { findings.Content = find == null ? null : Note(find, "warn"); lastFindings = find; }
                retry.Visibility = showRetry ? Visibility.Visible : Visibility.Collapsed;
                if (pvShotsFailed && techText != lastTech) { tech.Content = string.IsNullOrEmpty(techText) ? null : Details("Technical details", techText); lastTech = techText; }
                with.Visibility = PreviewCannot(h) != null || renderer == null ? Visibility.Collapsed : Visibility.Visible;
                if (say != lastStatus) { lastStatus = say; try { System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(status)?.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged); } catch (Exception) { } }
            }
            pvRefresh = () => { if (Within(page, sp)) Refresh(); };
            Refresh();
            return sp;
        }

        string PreviewCaption()
        {
            var s = PreviewSourceNow();
            return "Your HOME's first screen with " + (s?.Short ?? "games") + ", drawn by the console HOME's own program on this PC.";
        }

        /// <summary>"Preview with": the games the preview is drawn with, closed at first (the Included Games unless the person chooses other games)</summary>
        UIElement PreviewWith()
        {
            var body = new StackPanel();
            var pick = new ComboBox { MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left }; AutomationProperties.SetName(pick, "Games to preview with");
            body.Children.Add(T("The HOME is drawn with these games. This changes only the preview, never your design.", Ty.Small, false, true, new Thickness(0, 0, 0, 6)));
            body.Children.Add(pick);
            bool filled = false;
            void Fill()
            {
                if (filled) return; filled = true;
                var now = PreviewSourceNow();
                foreach (var s in PreviewSources(true)) pick.Items.Add(new ComboBoxItem { Content = s.Label, Tag = s.Key, IsSelected = s.Key == now?.Key });
                pick.SelectionChanged += (s, e) =>
                {
                    var k = (pick.SelectedItem as ComboBoxItem)?.Tag as string; if (k == null || k == PreviewSourceNow()?.Key) return;
                    pvPick = k; pvFailed = false; pvBlocked = null; RequestPreview(); pvRefresh?.Invoke();
                };
            }
            var d = Disclosure("Preview with: " + (PreviewSourceNow()?.Short ?? "no games available"), body, false);
            if (d is StackPanel sp && sp.Children[0] is ToggleButton tb) tb.Checked += (s, e) => Fill();
            return d;
        }

        /// <summary>placement guides over the actual HOME (Layout &amp; Folders, when asked): the screen's safe area, the theme's controls that
        /// stay, and each group of controls where the design puts it; never the preview itself</summary>
        void DrawGuides(Canvas cv, HomeDesign h, bool on)
        {
            cv.Children.Clear(); if (!on) return;
            void Put(UIElement e, double x, double y) { Canvas.SetLeft(e, x); Canvas.SetTop(e, y); cv.Children.Add(e); }
            Put(new Rectangle { Width = 1280 - 2 * DesignProfile.Margin, Height = 720 - 2 * DesignProfile.Margin, Stroke = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), StrokeDashArray = new DoubleCollection { 6, 6 }, StrokeThickness = 2 }, DesignProfile.Margin, DesignProfile.Margin);
            Border Box(double w, double hh, Brush line, string text) => new Border { Width = w, Height = hh, BorderBrush = line, BorderThickness = new Thickness(3), Background = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)),
                Child = new Border { Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Child = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 17, Margin = new Thickness(6, 0, 6, 1) } } };
            foreach (var f in DesignProfile.FixedControls(h.BaseTheme)) Put(Box(f.box[2] - f.box[0], f.box[3] - f.box[1], Brushes.Gray, Cap(f.name) + " (stays)"), f.box[0], f.box[1]);
            var placed = h.ControlRegions; var ap = h.Document["appearance"] as JObj ?? new JObj();
            foreach (var ui in DesignProfile.UiOrder)
            {
                var b = DesignProfile.RegionBox(h.BaseTheme, ui); if (b == null) continue;
                double x = b[0], y = b[1]; if (placed.TryGetValue(ui, out var r) && r["x"] is long rx && r["y"] is long ry) { x = rx; y = ry; }
                Put(Box(b[2] - b[0], b[3] - b[1], Hex(ap["accent_color"] as string ?? "#e4000f"), Cap(DesignProfile.UiName(ui))), x, y);
            }
        }

        // ------------------------------------------------------------------ every screen (Review & Save)
        async void ShowEveryScreen()
        {
            var h = design; if (h == null) { ShowDesigner(); return; }
            var cannot = PreviewCannot(h); if (cannot != null) { Tell("Every screen cannot be shown yet", cannot.Value.text); return; }
            var renderer = PreviewRenderer(); var src = PreviewSourceNow();
            if (renderer == null || src == null) { Tell("Every screen cannot be shown here", renderer == null ? "This copy of Workbench does not have the console HOME's program." : "This copy of Workbench does not have the CCS HOME to draw with."); return; }
            var key = h.ContentSha256 + "|" + src.Key;
            if (pvShots != null && pvShotsKey == key && pvShotsCheck != null) { EveryScreenPage(h, src, pvShotsCheck, pvShots); return; }
            pvShotsCancel?.Cancel();
            var cts = new CancellationTokenSource(); bool canceled = false;
            var pv = new ProgressView(this, "Drawing Every Screen", "Your design is drawn on every screen of the HOME with " + src.Short + ", here on this PC. Nothing on a console changes.", "Cancel", () => { canceled = true; cts.Cancel(); ShowDesignEditor(); });
            Show(pv.Panel, "progress");
            RenderCheckResult r; Dictionary<string, BitmapSource> shots;
            try
            {
                var work = PreviewFolder();
                var pack = await Task.Run(() => PreviewPack(src, work));
                if (canceled || design != h) return;
                var compiled = DesignCompiler.Compile(h, pack.games);
                (r, shots) = await RunCheck(renderer, pack.dir, compiled, key, work, cts.Token, 0.5);
            }
            catch (Exception ex) when (ex is OperationCanceledException || canceled) { return; }
            catch (Exception ex)
            {
                if (canceled) return;
                var b = ex is AggregateException ae ? ae.GetBaseException() : ex;
                var ep = SectionHead("Every screen could not be drawn", "Nothing changed, and your design is as it was.");
                ep.Children.Add(Note(b is ComposeException ce ? Plain(ce).text : "Something went wrong while the screens were being drawn. Try again.", "bad"));
                ep.Children.Add(Details("Technical details", b.GetType().Name + ": " + b.Message));
                ep.Children.Add(Buttons(B("Try Again", "Btn", ShowEveryScreen), B("Back to the Design", "Btn.Ghost", () => ShowDesignEditor())));
                Show(ep, "design-screens"); return;
            }
            if (canceled || design != h) return;
            if (h.ContentSha256 + "|" + src.Key == key) { pvShots = shots; pvShotsCheck = r; pvShotsKey = key; }
            EveryScreenPage(h, src, r, shots);
        }

        void EveryScreenPage(HomeDesign h, PreviewSource src, RenderCheckResult r, Dictionary<string, BitmapSource> shots)
        {
            var sp = SectionHead("Every Screen", r.Ok ? "Every screen of " + ThemeName(r.DesignTheme ?? h.BaseTheme) + " with your design, drawn on this PC by the console HOME's own program. Every screen passed its checks." : "Some screens need a change before this design can go on a console.");
            if (!r.Ok) sp.Children.Add(Note(string.Join("\n", PlainCheck(r).Select(x => "• " + x)), "warn"));
            var grid = new CardPanel { MinItemWidth = 240, MaxColumns = 3, Gap = 12 };
            foreach (var kv in shots.OrderBy(x => StateOrder(x.Key)))
            {
                var st = r.States.FirstOrDefault(s => s.Theme == r.DesignTheme && s.State == kv.Key); bool ok = st == null || st.Ok;
                var name = StateNames.TryGetValue(kv.Key, out var nm) ? nm : kv.Key; var pic = kv.Value;
                var cell = new StackPanel();
                var im = new Image { Source = pic, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 0, 4) }; RenderOptions.SetBitmapScalingMode(im, BitmapScalingMode.HighQuality);
                var b = new Button { Style = Res<Style>("Wb.Plain"), Content = im, Padding = new Thickness(0) };
                AutomationProperties.SetName(b, name + (ok ? "" : ", hard to read") + ". Select to see it larger.");
                b.Click += (s, e) => { ShowPicture(pic, name, "Your design on the HOME's " + name + " screen.", "Drawn on this PC by the console HOME's own program. Nothing on a console changed."); b.Focus(); };
                cell.Children.Add(b);
                cell.Children.Add(PictureCaption(name + (ok ? "" : "  ·  hard to read")));   // the cell is as wide as its picture
                grid.Children.Add(cell);
            }
            if (grid.Children.Count > 0) sp.Children.Add(Card(grid, 12));
            sp.Children.Add(Details("Technical details", "Drawn by " + r.Renderer + " with " + src.Label + ".",
                r.AddedPeakBytes > 0 ? "Picture memory the design adds at its worst moment, as measured by the HOME program on this PC: " + CcsBackup.Engine.Units.Gb(r.AddedPeakBytes) + " (at most " + CcsBackup.Engine.Units.Gb(r.Budget) + "). The check before applying measures it again; neither is a test on console hardware." : null,
                r.Problems.Count > 0 ? "Problems: " + string.Join("; ", r.Problems) : null, r.ReportSha256 != null ? "Check report SHA-256: " + r.ReportSha256 : null));
            sp.Children.Add(Buttons(B("Back to the Design", "Btn.Primary", () => ShowDesignEditor())));
            Show(sp, "design-screens");
        }

        /// <summary>the render check's findings in short sentences (its own words stay in Technical details)</summary>
        internal static List<string> PlainCheck(RenderCheckResult r)
        {
            var l = new List<string>(); void Add(string s) { if (!l.Contains(s)) l.Add(s); }
            var hard = r.States.Where(s => !s.Ok && (r.DesignTheme == null || s.Theme == r.DesignTheme)).OrderBy(s => StateOrder(s.State)).Select(s => StateNames.TryGetValue(s.State, out var n) ? n : s.State).Distinct().ToList();
            foreach (var p in r.Problems)
            {
                if (p.Contains("hard to read")) Add(hard.Count > 0 ? "Some text is hard to read over your pictures on: " + string.Join(", ", hard.Take(6)) + "." : "Some text is hard to read over your pictures.");
                else if (p.Contains("decoded pictures") || p.Contains("memory budget") || p.Contains("decoded memory")) Add("These pictures are too large. Choose smaller pictures.");
                else if (p.Contains("has no character")) Add("A label uses a letter its font does not have. Change the label's text.");
                else if (p.Contains("does not fit its panel") || p.Contains("panel reaches the screen's edge")) Add("Some menu text does not fit with these fonts. Choose a smaller font.");
                else if (p.Contains("startup picture")) Add("The startup picture cannot be shown. Choose another startup picture.");
                else if (p.Contains("set the design aside") || p.Contains("does not check out")) Add("The HOME could not use this design as it is.");
                else Add("Some screens did not pass the HOME's checks.");
            }
            if (l.Count == 0 && !r.Ok) Add("Some screens did not pass the HOME's checks.");
            return l;
        }
    }
}
