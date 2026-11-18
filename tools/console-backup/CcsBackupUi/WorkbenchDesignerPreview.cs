// Classic Cart Studio console backup and restore window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// HOME Designer's preview drawn by the console HOME itself: the design compiled by the one compiler for a real CCS HOME package's games
// (the HOME menu Classic Cart Studio for Windows prepared on this computer, the CCS HOME this program places with CCS Mod or CCS Nucleus
// (its Included Games, carried inside it), or a console's own installed HOME read from one of its backups), then every screen drawn by the native HOME renderer (in the standalone Workbench the one linked into it, in Classic Cart
// Studio for Windows its ccs-sim-core.exe) through the render check. Shown in the editor before anything is applied. Nothing is
// written anywhere but this program's own records folder, and nothing on a console changes. The layout sketch above it stays a sketch.
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
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CcsBackup.Ui
{
    public sealed partial class BackupWorkspace
    {
        RenderCheckResult designPreview; string designPreviewSha, designPreviewId, designPreviewFrom, designPreviewWhen; List<string> designPreviewLeftOut;
        int designPreviewSource;
        const string EmbeddedHome = "embedded:mod";   // the release-built CCS HOME this program carries (HomePackage.Embedded)

        /// <summary>the HOME packages a preview can be drawn with: (what the person sees, the package folder or a backup file, is a backup)</summary>
        List<(string label, string path, bool backup)> PreviewSources()
        {
            var l = new List<(string, string, bool)>();
            var app = opt.PreparedHomePackage;
            if (app != null && File.Exists(Path.Combine(app, "pack.json")) && File.Exists(Path.Combine(app, "games.json"))) l.Add(("The HOME menu Classic Cart Studio prepared on this PC (your collection's games)", app, false));
            try { if (HomePackage.Embedded("mod") != null) l.Add(("The CCS HOME this program places with CCS Mod or CCS Nucleus (the Included Games)", EmbeddedHome, false)); }
            catch (HomePackageException) { }
            try { foreach (var e in History.List(settings).Where(x => x.Status == BackupStatus.Verified || x.Status == BackupStatus.NeedsVerification).Take(12)) l.Add(("The CCS HOME on console " + e.ConsoleTag + ", from its backup of " + e.When, e.PrimaryPath, true)); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException) { }
            return l;
        }

        static readonly Dictionary<string, string> StateNames = new Dictionary<string, string>
        {
            ["all-games"] = "All Games", ["long-title"] = "The longest title", ["last-game"] = "The last game", ["details"] = "Game Details", ["details-long-title"] = "Game Details, long title",
            ["menu"] = "START menu", ["menu-focus"] = "START menu, another row", ["theme-picker"] = "Theme picker", ["browse"] = "Browse", ["browse-focus"] = "Browse, a tile in focus",
            ["folder"] = "A folder", ["folder-empty"] = "An empty folder", ["navigation-focus"] = "Navigation in focus", ["notice"] = "A notice", ["startup"] = "Startup picture",
        };

        UIElement DesignRealPreview(HomeDesign h)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
            sp.Children.Add(T("Drawn by the console HOME", 13.5, true, false, new Thickness(0, 0, 0, 4)));
            var renderer = HomeRenderers.Find(AppHomeRenderer()); var sources = PreviewSources();
            if (renderer == null) { sp.Children.Add(Note("The console's HOME program is not available in this copy, so the preview cannot be drawn here.", "warn")); return sp; }
            if (!h.CanApply) { sp.Children.Add(Note("The preview is drawn for a design the console can show. This one cannot yet: " + string.Join("; ", h.Unsupported) + ".", "info")); return sp; }
            if (sources.Count == 0)
            {
                sp.Children.Add(T("The preview needs a CCS HOME to draw with: " + (opt.HostedInFullApp ? "prepare your HOME menu in Classic Cart Studio (Prepare HOME Menu), or back up a console that has CCS HOME." : "back up a console that has CCS HOME (Back Up Console), or use HOME Designer inside Classic Cart Studio for Windows, which draws it with your collection's games."), 12.5, false, true));
                return sp;
            }
            sp.Children.Add(T("Your design is fitted to a real CCS HOME's games and every screen is drawn by the console's own HOME program, in the four themes; the screens of " + ThemeName(h.BaseTheme) + " are shown here. Nothing on a console changes.", 12.5, false, true, new Thickness(0, 0, 0, 8)));
            var pick = new ComboBox(); System.Windows.Automation.AutomationProperties.SetName(pick, "HOME to draw with");
            for (int i = 0; i < sources.Count; i++) pick.Items.Add(new ComboBoxItem { Content = sources[i].label, Tag = i, IsSelected = i == Math.Min(designPreviewSource, sources.Count - 1) });
            sp.Children.Add(Field("Draw it with", pick, B(designPreview == null ? "Draw the Preview" : "Draw It Again", "Btn.Primary", () => { designPreviewSource = pick.SelectedIndex < 0 ? 0 : pick.SelectedIndex; DrawDesignPreview(h, sources[designPreviewSource], renderer); })));
            if (designPreview != null && designPreviewId != h.Id) designPreview = null;   // drawn for another design
            if (designPreview == null) return sp;
            var r = designPreview; bool current = designPreviewSha == h.ContentSha256;
            sp.Children.Add(Note((current ? "Drawn " : "Drawn before your latest changes, ") + designPreviewWhen + " with " + designPreviewFrom + ", by " + r.Renderer + ". " + (r.Ok ? "Every screen passed the check: readable, within the console's memory." : "It did not pass the check: " + string.Join("; ", r.Problems.Take(4)) + (r.Problems.Count > 4 ? " (and " + (r.Problems.Count - 4) + " more)" : "") + "."), current ? (r.Ok ? "ok" : "warn") : "info"));
            foreach (var x in (designPreviewLeftOut ?? new List<string>()).Take(8)) sp.Children.Add(T("Left out on this HOME: " + x, 12, false, true));
            var shots = new CardPanel { MinItemWidth = 240, MaxColumns = 3, Gap = 12 };
            foreach (var f in r.Frames.Where(x => x.WithDesign && (x.Theme == r.DesignTheme || x.Theme == "startup")))
            {
                var cell = new StackPanel();
                try { var bi = new BitmapImage(); bi.BeginInit(); bi.StreamSource = new MemoryStream(f.Png()); bi.DecodePixelWidth = 480; bi.CacheOption = BitmapCacheOption.OnLoad; bi.EndInit(); bi.Freeze(); cell.Children.Add(new Image { Source = bi, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 0, 4) }); }
                catch (Exception) { cell.Children.Add(T("(this screen cannot be shown)", 12, false, true)); }
                var st = r.States.FirstOrDefault(s => s.Theme == f.Theme && s.State == f.State);
                cell.Children.Add(T((StateNames.TryGetValue(f.State, out var n) ? n : f.State) + (st != null && !st.Ok ? "  ·  hard to read" : ""), 11.5, false, true));
                shots.Children.Add(cell);
            }
            sp.Children.Add(shots);
            return sp;
        }

        void DrawDesignPreview(HomeDesign h, (string label, string path, bool backup) src, IHomeRenderer renderer)
        {
            var cts = new CancellationTokenSource(); var sha = h.ContentSha256;
            var pv = new ProgressView(this, "Drawing the Preview", "Your design is fitted to the HOME's games and every screen is drawn by the console's own HOME program. Nothing on a console changes.", "Cancel", () => cts.Cancel());
            Show(pv.Panel, "progress");
            var work = Path.Combine(settings.RecordsFolder, "home-design-preview", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            Task.Run(() =>
            {
                Directory.CreateDirectory(work);
                string pack = src.path;
                if (src.backup) { var st = DesignApplyEngine.ReadState(src.path); pack = DesignApplyEngine.HomePackage(src.path, st, null, Path.Combine(work, "pack")); }
                else if (src.path == EmbeddedHome) pack = EmbeddedHomeFolder(Path.Combine(work, "pack"));
                var games = Json.Parse(File.ReadAllText(Path.Combine(pack, "games.json"), Encoding.UTF8)).A("games").OfType<Dictionary<string, object>>().Select(g => new ConsoleGame { Id = g.S("id"), Title = g.S("title"), CatalogId = g.S("catalog") }).ToList();
                var compiled = DesignCompiler.Compile(h, games); var cdir = Path.Combine(work, "design"); compiled.WriteTo(cdir);
                var why = DesignApplyEngine.PackageRefuses(pack, compiled); if (why != null) throw new ComposeException("design.package", why);
                var r = renderer.Check(pack, cdir, new RenderCheckOptions { WorkDir = Path.Combine(work, "check"), Frames = true, Cancel = cts.Token });
                r.Frames.RemoveAll(f => !(f.WithDesign && (f.Theme == r.DesignTheme || f.Theme == "startup")));   // the base theme's screens and the startup picture are shown
                return (r, compiled.LeftOut);
            }).ContinueWith(t =>
            {
                try { Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                if (t.IsCanceled || t.IsFaulted && t.Exception.GetBaseException() is OperationCanceledException) { ShowDesignEditor(); return; }
                if (t.IsFaulted) { var ex = t.Exception.GetBaseException(); designProblem = "The preview could not be drawn: " + (ex is ComposeException || ex is IOException || ex is InvalidDataException ? ex.Message : ex.GetType().Name + ": " + ex.Message); ShowDesignEditor(); return; }
                designPreview = t.Result.r; designPreviewLeftOut = t.Result.LeftOut; designPreviewSha = sha; designPreviewId = h.Id; designPreviewFrom = src.backup ? "the console's own CCS HOME" : src.path == EmbeddedHome ? "the CCS HOME this program places" : "the HOME menu prepared on this PC"; designPreviewWhen = DateTime.Now.ToString("h:mm tt", En);
                ShowDesignEditor();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>the carried package's HOME folder written out for the render check, every file checked against the package's own list</summary>
        static string EmbeddedHomeFolder(string outDir)
        {
            var p = HomePackage.Embedded("mod") ?? throw new InvalidDataException("this copy does not carry a CCS HOME");
            foreach (var f in p.Files.Where(x => x.Rel.StartsWith("home/", StringComparison.Ordinal)))
            {
                var dst = Path.Combine(outDir, f.Rel.Substring(5).Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(dst));
                try { File.WriteAllBytes(dst, p.Read(f.Rel)); } catch (HomePackageException ex) { throw new InvalidDataException(ex.Message); }
            }
            return outDir;
        }
    }
}
