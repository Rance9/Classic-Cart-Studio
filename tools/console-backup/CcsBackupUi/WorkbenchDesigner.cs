// Classic Cart Studio console backup and restore window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// HOME Designer (inside Console Composer; the same window in the standalone Workbench and when Classic Cart Studio for Windows opens it): a
// guided wizard with five steps (Theme, Pictures & Startup, Text & Colors, Layout & Folders, Review & Save), Back and Next, the controls of
// the current step only, and the actual HOME beside them, drawn by the console HOME's own program on this PC (WorkbenchDesignerPreview.cs).
// Every change goes through the one design model (CcsBackup.Compose.HomeDesign), which validates a copy before it replaces the design and
// keeps what a newer version added; the wizard shows the model's findings in short, plain sentences and keeps the model's own words in
// Technical details. A design this version can only inspect is shown, never changed, and its original file can be exported as it was
// received. The open design and its step stay while the person visits other pages; questions are the Workbench's own (Ask, Tell).
using CcsBackup.Compose;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CUnits = CcsBackup.Compose.Units;

namespace CcsBackup.Ui
{
    public sealed partial class BackupWorkspace
    {
        HomeDesign design, designOpened; string designPath; bool designDirty;
        /// <summary>the open wizard's short message (null: none) and the model's own words for its Technical details</summary>
        string designProblem, designProblemDetail;
        /// <summary>the wizard's step (0 to 4): kept with the design while the person visits other pages</summary>
        int designStep;
        /// <summary>the person's own picture files chosen in this window, per picture place, so a picture's fit can change without choosing
        /// it again; never saved (a design keeps only the fitted picture)</summary>
        readonly Dictionary<string, byte[]> designSources = new Dictionary<string, byte[]>();
        readonly Dictionary<string, string> designFits = new Dictionary<string, string>();
        /// <summary>the open wizard page's parts that change in place</summary>
        Border designNote; TextBlock designState; ScrollViewer designControls;
        /// <summary>what is typed but not added yet (a label's text, a folder's name, a credit), by its box's name, and the new folder's place
        /// (its parent folder's id; null: at the top). Kept with the open wizard, never in the design, so a step change, the page drawn again
        /// after a change, or a visit to Share or See Every Screen keeps it. Let go only by the Add that used it, by the person's own
        /// answer when leaving the page or closing the design, or when another design starts.</summary>
        readonly Dictionary<string, string> designDrafts = new Dictionary<string, string>();
        string designDraftParent;
        /// <summary>a box for text that its own Add button adds: it shows its kept draft, and what is typed in it is kept</summary>
        TextBox Pending(string name, bool ro)
        {
            var t = new TextBox { IsReadOnly = ro }; AutomationProperties.SetName(t, name);
            if (designDrafts.TryGetValue(name, out var was)) t.Text = was;
            t.TextChanged += (s, e) => { if (t.Text.Length > 0) designDrafts[name] = t.Text; else designDrafts.Remove(name); };
            return t;
        }
        /// <summary>the leave question's and the close review's words for typed text not added yet (null: none)</summary>
        internal string DesignDraftWords() => designDrafts.Values.Any(v => v.Trim().Length > 0) ? "What you typed in HOME Designer but did not add (a label, a folder or a credit)" : null;
        void ForgetDesignDrafts() { designDrafts.Clear(); designDraftParent = null; }
        const string DesignFilter = "Shared HOME Designs (*.ccshome)|*.ccshome";
        const string PictureFilter = "Pictures (PNG or JPEG)|*.png;*.jpg;*.jpeg";
        /// <summary>the theme a new design starts from (by its stable id, never by its place in a list): Featured Shelf</summary>
        internal const string DefaultTheme = "featured-shelf";
        static readonly string[] StepNames = { "Theme", "Pictures & Startup", "Text & Colors", "Layout & Folders", "Review & Save" };
        const int StepTheme = 0, StepPictures = 1, StepText = 2, StepLayout = 3, StepReview = 4;

        // ------------------------------------------------------------------ the start page
        void ShowDesigner()
        {
            if (design != null) { ShowDesignEditor(); return; }
            var sp = SectionHead("HOME Designer", "Design how CCS HOME looks on your " + Con + ": your own pictures, colors, folders and game order. You see the real HOME as you work. Nothing on your console changes while you design.");
            if (designProblem != null) sp.Children.Add(ProblemNote(designProblem, designProblemDetail));
            sp.Children.Add(CardGrid(280, 2,
                ActionCard("New Design", "Start from Featured Shelf, then choose the theme you like best.", "Btn.Primary", NewDesign),
                ActionCard("Open a Shared HOME Design", "Keep working on a " + FileKinds.HomeDesign + " file (.ccshome) on this PC.", "Btn", OpenDesign, "Open a Shared HOME Design…")));
            sp.Children.Add(T("A " + FileKinds.HomeDesign + " holds pictures and appearance settings only: no games, saves or console files.", Ty.Small, false, true, new Thickness(0, 0, 0, 10)));
            sp.Children.Add(Buttons(B("Back", "Btn.Ghost", BackToSection)));
            Show(sp, "designer");
        }

        void NewDesign()
        {
            design = HomeDesign.New("My HOME", DefaultTheme); designOpened = null; designPath = null; designDirty = false; designStep = StepTheme;
            DesignStarted();
        }

        /// <summary>a design was started or opened: its wizard opens on the first step, with nothing carried over from another design</summary>
        void DesignStarted()
        {
            designProblem = designProblemDetail = null; designSources.Clear(); designFits.Clear(); PreviewForget(); ForgetDesignDrafts();
            ShowDesignEditor();
        }

        void OpenDesign()
        {
            if (!DesignLeaveOk("Save First", "Open Without Saving", "Open Without It")) return;
            var d = new Microsoft.Win32.OpenFileDialog { Title = "Open a Shared HOME Design", Filter = DesignFilter };
            if (d.ShowDialog(this) != true) return;
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                var len = new FileInfo(d.FileName).Length;
                if (len > DesignProfile.MaxArchiveBytes) throw new ComposeException("design.size", "This file is larger than a Shared HOME Design can be (" + CUnits.Size(DesignProfile.MaxArchiveBytes) + ").");
                var opened = HomeDesign.Open(d.FileName);
                design = opened; designOpened = HomeDesign.Open(d.FileName); designPath = d.FileName; designDirty = false; designStep = StepTheme;
            }
            catch (Exception ex) when (ex is ComposeException || ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException)
            {
                design = null; designProblem = "This file could not be opened as a HOME design."; designProblemDetail = ex.Message; ShowDesigner(); return;
            }
            finally { Mouse.OverrideCursor = null; }
            DesignStarted();
        }

        // ------------------------------------------------------------------ leaving and closing a design
        /// <summary>before the open design with changes that are not saved is closed or replaced on its own page (Close Design, Open a Shared
        /// HOME Design…): the Workbench's own question with Save (Save As when it has no file yet), leave without saving, or Cancel. True: go
        /// on. Closing the window asks in its one close review instead (BackupWorkspace.CloseReview, with DesignUnsavedName). Text typed but
        /// not added is named in the same one question, never in a second one: it is not part of the design, so saving does not keep
        /// it; with only such text, the question offers Keep Designing or leaving without it.</summary>
        bool DesignLeaveOk(string saveAnd, string without, string withoutTyped)
        {
            var name = DesignUnsavedName(); bool typed = DesignDraftWords() != null;
            if (name == null && !typed) return true;
            if (name == null) return Ask("Let go of what you typed?", "You typed a label, a folder or a credit but did not add it to the design.", new[] { "Keep Designing", withoutTyped }, 0, 0) == 1;
            var k = Ask("Save your design?", "\"" + name + "\" has changes that are not saved." + (typed ? "\n\nWhat you typed but did not add (a label, a folder or a credit) is not part of the design, so it is let go either way." : ""),
                        new[] { saveAnd, without, "Cancel" }, 0, 2);
            if (k == 0) return SaveDesign(false);
            return k == 1;
        }

        /// <summary>the window's close review (WB-UX-20): the open design's name when it has changes that are not saved, else null</summary>
        internal string DesignUnsavedName() => design != null && designDirty && !design.InspectOnly ? (string.IsNullOrWhiteSpace(design.Name) ? "My HOME" : design.Name) : null;
        /// <summary>the close review's Save &amp; Close for the design: Save (Save As when it has no file yet), without opening any page; false
        /// when the person canceled the file choice (designProblem null) or it could not be saved (designProblem says why, for the review's
        /// notice), with the design kept as it was</summary>
        internal bool SaveDesignForClose() { designProblem = designProblemDetail = null; return SaveDesign(false, true) && !designDirty; }
        /// <summary>the close review's Close Without Saving, for the design it named</summary>
        internal void DiscardDesignForClose() { designDirty = false; }

        void CloseDesign()
        {
            if (!DesignLeaveOk("Save and Close", "Close Without Saving", "Close Without It")) return;
            design = designOpened = null; designPath = null; designDirty = false; designProblem = designProblemDetail = null; designStep = StepTheme;
            designSources.Clear(); designFits.Clear(); PreviewForget(); ForgetDesignDrafts();
            ShowDesigner();
        }

        /// <summary>Save, or Save As (also when the design has no file yet). A canceled file choice changes nothing. atClose (the window's close
        /// review): no page is drawn or opened and a failure is only kept in designProblem for the review's own notice; otherwise a failure
        /// is shown on the wizard's page.</summary>
        bool SaveDesign(bool saveAs, bool atClose = false)
        {
            var h = design; if (h == null || h.InspectOnly) return false;
            var path = designPath;
            if (saveAs || path == null)
            {
                var d = new Microsoft.Win32.SaveFileDialog { Title = "Save the Shared HOME Design", Filter = DesignFilter, FileName = SafeFileName(h.Name ?? "My HOME") + DesignProfile.Extension, OverwritePrompt = true };
                if (d.ShowDialog(this) != true) return false; path = d.FileName;
            }
            return SaveDesignTo(path, atClose);
        }

        /// <summary>the save itself, to the file chosen (written, read back the same, then put in place by the model)</summary>
        bool SaveDesignTo(string path, bool atClose = false)
        {
            var h = design; if (h == null || h.InspectOnly) return false;
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                if (designOpened != null && h.ContentSha256 != designOpened.ContentSha256) h.NextRevision();
                h.Save(path, overwrite: true); designPath = path; designOpened = HomeDesign.Open(path); designDirty = false;
                designProblem = designProblemDetail = null;
            }
            catch (Exception ex) when (ex is ComposeException || ex is IOException || ex is UnauthorizedAccessException)
            {
                var (text, detail) = ex is ComposeException ce ? Plain(ce) : ("The design could not be saved there. Choose another folder, or try again.", ex.Message);
                if (atClose) { designProblem = text; designProblemDetail = detail; }
                else SetDesignProblem(text, detail);
                return false;
            }
            finally { Mouse.OverrideCursor = null; }
            if (IsDesignPage() && !atClose) RefreshDesigner(); else UpdateDesignState();
            return true;
        }

        static string SafeFileName(string s) { foreach (var ch in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(ch, '-'); s = s.Trim().TrimEnd('.'); return s.Length == 0 ? "My HOME" : s.Length > 60 ? s.Substring(0, 60) : s; }

        // ------------------------------------------------------------------ changes
        /// <summary>one change to the open design: the model validates it on a copy first. Accepted: the design is marked changed, the
        /// page follows (rebuilt, keeping the focus, or only its status) and the preview draws again. Refused: nothing changes and a short
        /// message says what to do.</summary>
        bool DesignEdit(Action change, bool rebuild = true)
        {
            if (design == null || design.InspectOnly) return false;
            try { Mouse.OverrideCursor = Cursors.Wait; change(); designDirty = true; designProblem = designProblemDetail = null; }
            catch (ComposeException ex) { var (t, d) = Plain(ex); SetDesignProblem(t, d); return false; }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { SetDesignProblem("That file could not be used. Choose another one.", ex.Message); return false; }
            finally { Mouse.OverrideCursor = null; }
            if (rebuild) RefreshDesigner(); else { SetDesignProblem(null, null); UpdateDesignState(); }
            RequestPreview();
            return true;
        }

        bool IsDesignPage() => (page.Content as FrameworkElement)?.Tag as string == "design-editor";

        /// <summary>the wizard again, as it is now: the same step, the focus on the control that had it and the step's controls scrolled as
        /// before (typed text not added yet comes back from designDrafts, as on every drawing of the wizard)</summary>
        void RefreshDesigner()
        {
            if (!IsDesignPage()) return;
            string focus = null;
            if (Keyboard.FocusedElement is DependencyObject f && Within(page, f)) focus = AutomationProperties.GetName(f);
            double off = designControls?.VerticalOffset ?? 0;
            ShowDesignEditor();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                designControls?.ScrollToVerticalOffset(off);
                if (!string.IsNullOrEmpty(focus)) FocusNamed(focus);
            }), DispatcherPriority.Loaded);
        }

        /// <summary>is d inside root (by the visual tree, or the logical tree for a part not laid out yet)</summary>
        static bool Within(DependencyObject root, DependencyObject d)
        {
            for (var p = d; p != null; p = (p is Visual || p is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(p) : null) ?? LogicalTreeHelper.GetParent(p)) if (p == root) return true;
            return false;
        }
        /// <summary>moves the keyboard focus to the page's control with this accessible name (the first that can take it)</summary>
        bool FocusNamed(string name)
        {
            var q = new Queue<DependencyObject>(); q.Enqueue(page);
            while (q.Count > 0)
            {
                var d = q.Dequeue();
                if (d is UIElement u && u.Focusable && u.IsEnabled && u.IsVisible && AutomationProperties.GetName(d) == name) { u.Focus(); return true; }
                int n = d is Visual ? VisualTreeHelper.GetChildrenCount(d) : 0; for (int i = 0; i < n; i++) q.Enqueue(VisualTreeHelper.GetChild(d, i));
            }
            return false;
        }

        void GoStep(int k, string focus = null)
        {
            designStep = Math.Max(0, Math.Min(StepNames.Length - 1, k)); designProblem = designProblemDetail = null;
            ShowDesignEditor();
            Dispatcher.BeginInvoke(new Action(() => { if (focus == null || !FocusNamed(focus)) FocusNamed(StepTitle(designStep)); }), DispatcherPriority.Loaded);
        }
        static string StepTitle(int i) => "Step " + (i + 1) + " of " + StepNames.Length + ": " + StepNames[i];

        // ------------------------------------------------------------------ plain words for the model's findings
        /// <summary>a refused change or file in a short sentence for the page, and the model's own words for Technical details (null when the
        /// sentence already is them)</summary>
        internal static (string text, string detail) Plain(ComposeException ex)
        {
            var c = ex.Code ?? ""; var m = ex.Message;
            switch (c)
            {
                case "image.size": case "image.dimensions": case "design.slot-bytes": case "design.slot-size": return ("This picture is too large. Choose a smaller picture.", m);
                case "image.type": return ("Choose a PNG or JPEG picture.", m);
                case "image.animated": return ("Choose a still picture. Moving pictures cannot be used.", m);
                case "design.contrast": return ("The text color is too close to the panel color to read. Choose a lighter text color or a darker panel color.", m);
                case "design.layout-place": return (m.StartsWith("That place cannot be used: ", StringComparison.Ordinal) ? "It cannot go there: " + m.Substring(27) : m, null);
                case "design.folder-parent": return (m.StartsWith("Move or remove", StringComparison.Ordinal) ? m : "That folder is not in this design.", m.StartsWith("Move or remove", StringComparison.Ordinal) ? null : m);
                case "design.inspect-only": return ("This design was made by a newer version of Classic Cart Studio, so it cannot be changed here.", null);
                case "design.length": return ("That text is too long. Use fewer words.", m);
                case "design.url": return ("Text cannot hold a web or file link.", m);
                case "design.path": return ("Text cannot hold a file or folder location.", m);
                case "design.markup": return ("Use plain text only.", m);
                case "design.console-id": case "design.fingerprint": return ("That text looks like a console's identity or a file's code. Use other words.", m);
                case "design.control": case "design.encoding": return ("That text has a character that cannot be used.", m);
                case "design.bounds": return ("The design cannot hold more of these.", m);
                case "design.exists": return ("A file with that name is already there. Choose another name.", null);
                case "design.size": case "zip.too-large": return ("This file is too large to be a HOME design.", m);
            }
            if (c.StartsWith("image.", StringComparison.Ordinal)) return ("This picture could not be read. Choose another picture.", m);
            if (c.StartsWith("design.layout", StringComparison.Ordinal)) return ("That does not fit on the screen. Make it smaller or move it.", m);
            if (c == "design.type" && m.EndsWith(" is empty.", StringComparison.Ordinal)) return ("Give it a name.", m);
            if (c == "design.type" && !m.StartsWith("This HOME design", StringComparison.Ordinal)) return (m, null);   // the wizard's own (Use whole numbers ...)
            return ("That change could not be made.", m);
        }

        /// <summary>why a design cannot go on a console (HomeDesign.Unsupported), each in a short sentence; the model's words go in Technical details</summary>
        internal static List<string> PlainIssues(IEnumerable<string> unsupported)
        {
            var l = new List<string>();
            foreach (var u in unsupported ?? Enumerable.Empty<string>())
            {
                string s;
                if (u.StartsWith("Memory:", StringComparison.Ordinal) || u.Contains("decoding")) s = "These pictures are too large. Choose smaller pictures.";
                else if (u.Contains("accent_color") && u.Contains("too light")) s = "The highlight color is too light for the HOME's white lettering. Choose a darker highlight color.";
                else if (u.StartsWith("appearance.title_font", StringComparison.Ordinal)) s = "The title font is too large for the HOME's menus. Choose a smaller title font.";
                else if (u.StartsWith("appearance.text_font", StringComparison.Ordinal)) s = "The text font does not fit the HOME's menus. Choose another text font.";
                else if (u.StartsWith("layout: moving the HOME's controls needs", StringComparison.Ordinal)) s = "To move the HOME's controls, choose your own HOME background (Pictures & Startup).";
                else if (u.StartsWith("layout:", StringComparison.Ordinal)) s = "Some of the HOME's controls are in places this theme cannot show. Put them back in the theme's place (Layout & Folders).";
                else if (u.Contains("newer HOME design format") || u.Contains("capability this version does not have")) s = "This design was made by a newer version of Classic Cart Studio.";
                else if (u.Contains("another screen profile")) s = "This design was made for a different screen.";
                else s = "This design uses something this version of Workbench does not have.";
                if (!l.Contains(s)) l.Add(s);
            }
            return l;
        }

        /// <summary>a short message with the model's own words in Technical details under it</summary>
        StackPanel ProblemContent(string text, string detail)
        {
            var sp = new StackPanel(); sp.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 19 });
            if (!string.IsNullOrEmpty(detail)) { var d = Details("Technical details", detail); if (d != null) { d.Margin = new Thickness(-6, 4, 0, -6); sp.Children.Add(d); } }
            return sp;
        }
        Border ProblemNote(string text, string detail) { var n = Note("", "bad"); n.Child = ProblemContent(text, detail); return n; }

        /// <summary>the wizard's message in place (null: none), without rebuilding the page</summary>
        void SetDesignProblem(string text, string detail)
        {
            designProblem = text; designProblemDetail = detail;
            if (designNote == null || !IsDesignPage()) { if (IsDesignPage()) RefreshDesigner(); return; }
            if (text == null) { designNote.Visibility = Visibility.Collapsed; designNote.Child = null; return; }
            designNote.Child = ProblemContent(text, detail); designNote.Visibility = Visibility.Visible;
            AutomationProperties.SetName(designNote, text);
            try { System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(designNote)?.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged); } catch (Exception) { }
        }

        string DesignStateText()
        {
            var h = design; if (h == null) return "";
            var name = string.IsNullOrWhiteSpace(h.Name) ? "My HOME" : h.Name;
            if (h.InspectOnly) return name + "  ·  can be looked at, not changed";
            return name + "  ·  " + (designPath == null ? (designDirty ? "not saved yet" : "new, not saved yet") : designDirty ? "changes not saved" : "saved as " + Path.GetFileName(designPath));
        }
        void UpdateDesignState() { if (designState != null) designState.Text = DesignStateText(); }

        // ------------------------------------------------------------------ the wizard page
        void ShowDesignEditor()
        {
            var h = design; if (h == null) { ShowDesigner(); return; }
            bool ro = h.InspectOnly;
            designStep = Math.Max(0, Math.Min(StepNames.Length - 1, designStep));

            // the head: title, the design's name and state, the steps, a message
            var head = new StackPanel();
            var top = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 2) };
            designState = NoTerms(new TextBlock { Text = DesignStateText(), FontSize = Ty.Small, Foreground = Res<Brush>("B.Muted"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 4, 0, 0) });
            DockPanel.SetDock(designState, Dock.Right); designState.HorizontalAlignment = HorizontalAlignment.Right; designState.MaxWidth = 420;
            top.Children.Add(designState);
            var title = PageTitle("HOME Designer"); title.Margin = new Thickness(0, 0, 0, 4); top.Children.Add(title);
            head.Children.Add(top);
            head.Children.Add(StepBar());
            if (ro) head.Children.Add(Note("This design was made by a newer version of Classic Cart Studio. You can look at it here, but not change, share or apply it. Review & Save can export its original file.", "warn"));
            designNote = Note("", "bad"); designNote.Child = null; designNote.Visibility = Visibility.Collapsed; designNote.Margin = new Thickness(0, 0, 0, 8);
            AutomationProperties.SetLiveSetting(designNote, AutomationLiveSetting.Polite);
            if (designProblem != null) { designNote.Child = ProblemContent(designProblem, designProblemDetail); designNote.Visibility = Visibility.Visible; }
            head.Children.Add(designNote);

            // the step's own controls, and the preview
            var controls = new StackPanel { Margin = new Thickness(0, 0, 12, 12) };
            switch (designStep)
            {
                case StepTheme: ThemeStep(controls, h, ro); break;
                case StepPictures: PicturesStep(controls, h, ro); break;
                case StepText: TextStep(controls, h, ro); break;
                case StepLayout: LayoutStep(controls, h, ro); break;
                default: ReviewStep(controls, h, ro); break;
            }
            var preview = PreviewPanel(designStep);

            // the foot: Back, Next and Close Design
            var foot = new DockPanel { Margin = new Thickness(0, 10, 0, 0), LastChildFill = false };
            var back = B("Back", "Btn", () => GoStep(designStep - 1, "Back"), designStep > 0); back.Margin = new Thickness(0, 0, 10, 0); foot.Children.Add(back);
            if (designStep < StepNames.Length - 1) { var next = B("Next: " + StepNames[designStep + 1], "Btn.Primary", () => GoStep(designStep + 1, "Next: " + StepNames[Math.Min(StepNames.Length - 1, designStep + 2)])); next.Margin = new Thickness(0); foot.Children.Add(next); }
            var close = B("Close Design", "Btn.Ghost", CloseDesign); close.Margin = new Thickness(0, 0, 0, 0); DockPanel.SetDock(close, Dock.Right); close.HorizontalAlignment = HorizontalAlignment.Right; foot.Children.Add(close);
            var footLine = new StackPanel(); footLine.Children.Add(new Border { Height = 1, Background = Res<Brush>("B.Line") }); footLine.Children.Add(foot);

            // wide: the controls and the preview side by side, the preview always in view; narrow: the preview above the controls
            var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.Children.Add(head);
            bool wide = DesignWide();
            designControls = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
            FrameworkElement body;
            if (wide)
            {
                var g = new Grid { Margin = new Thickness(0, 4, 0, 0) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 300 });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star), MaxWidth = 760 });
                designControls.Content = controls; g.Children.Add(designControls);
                var pv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, Content = preview, Margin = new Thickness(12, 0, 0, 0) };
                Grid.SetColumn(pv, 1); g.Children.Add(pv);
                body = g;
            }
            else
            {
                var sp = new StackPanel(); preview.Margin = new Thickness(0, 0, 0, 16); sp.Children.Add(preview); sp.Children.Add(controls);
                designControls.Content = sp; body = designControls;
            }
            Grid.SetRow(body, 1); root.Children.Add(body);
            Grid.SetRow(footLine, 2); root.Children.Add(footLine);
            Show(root, "design-editor");
            // another page may be opened: the design stays open with its changes (the close review names it); only typed text that was not
            // added yet is let go by leaving, so leaving asks about that alone, and Leave Without It lets it go (Stay keeps it)
            LeaveGuard(DesignDraftWords,
                       "You typed a label, a folder or a credit in HOME Designer but did not add it. Your design stays open either way; only what you typed is let go.",
                       ForgetDesignDrafts);
            PinToViewport(root, wide);
            RequestPreview();
        }

        /// <summary>side by side when the page is wide enough for both (the window's default size and larger)</summary>
        bool DesignWide() { double w = page.ActualWidth > 0 ? page.ActualWidth : scroller?.ViewportWidth ?? 0; return w <= 0 || w >= 760; }

        /// <summary>the wizard fills the page column's height, so its head, its foot and (when wide) the preview stay in view while the step's
        /// controls scroll; at a very low window height the whole page scrolls instead. Crossing the wide/narrow width rebuilds it.</summary>
        void PinToViewport(Grid root, bool wide)
        {
            if (scroller == null) return;
            void Fit() { if (page.Content != root) return; var vh = scroller.ViewportHeight; root.Height = vh >= 300 ? Math.Floor(vh) : double.NaN; }
            ScrollChangedEventHandler handler = null;
            handler = (s, e) =>
            {
                if (page.Content != root) { scroller.ScrollChanged -= handler; return; }
                if (e.ViewportHeightChange != 0) Fit();
                if (e.ViewportWidthChange != 0 && DesignWide() != wide) { scroller.ScrollChanged -= handler; Dispatcher.BeginInvoke(new Action(RefreshDesigner), DispatcherPriority.Background); }
            };
            scroller.ScrollChanged += handler;
            Fit(); root.Loaded += (s, e) => Fit();
        }

        UIElement StepBar()
        {
            var wp = new WrapPanel { Margin = new Thickness(-6, 2, 0, 8) };
            AutomationProperties.SetName(wp, "Steps");
            for (int i = 0; i < StepNames.Length; i++)
            {
                int k = i; bool cur = i == designStep;
                var num = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1.5), BorderBrush = Res<Brush>(cur ? "B.Red" : "B.Line2"), Background = cur ? Res<Brush>("B.Red") : Brushes.Transparent,
                                       Child = new TextBlock { Text = (i + 1).ToString(En), FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = cur ? Brushes.White : Res<Brush>("B.Ink2") } };
                var label = new TextBlock { Text = StepNames[i], FontSize = Ty.Text, FontWeight = cur ? FontWeights.SemiBold : FontWeights.Normal, Foreground = Res<Brush>(cur ? "B.Ink" : "B.Ink2"), Margin = new Thickness(8, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
                var row = new StackPanel { Orientation = Orientation.Horizontal }; row.Children.Add(num); row.Children.Add(label);
                if (i >= StepPictures && i <= StepLayout) row.Children.Add(new TextBlock { Text = "optional", FontSize = Ty.Fine, Foreground = Res<Brush>("B.Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 2, 0) });
                var b = new Button { Style = Res<Style>("Wb.Row"), Content = row, Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(0) };
                AutomationProperties.SetName(b, StepTitle(i) + (cur ? ", this step" : ""));
                if (cur) AutomationProperties.SetItemStatus(b, "this step");
                b.Click += (s, e) => GoStep(k);
                wp.Children.Add(b);
            }
            return wp;
        }

        /// <summary>a step's heading and its one short sentence; the heading is named for the step so the keyboard focus can land on it</summary>
        void StepHead(StackPanel sp, string title, string text, bool first = true)
        {
            var t = new TextBlock { Text = title, FontSize = Ty.Heading, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, first ? 2 : 18, 0, text == null ? 8 : 3) };
            if (first) { t.Focusable = true; t.FocusVisualStyle = null; AutomationProperties.SetName(t, StepTitle(designStep)); KeyboardNavigation.SetIsTabStop(t, false); }
            sp.Children.Add(t);
            if (text != null) sp.Children.Add(T(text, Ty.Small, false, true, new Thickness(0, 0, 0, 10)));
        }

        // ------------------------------------------------------------------ step 1: theme
        void ThemeStep(StackPanel sp, HomeDesign h, bool ro)
        {
            StepHead(sp, "Choose a theme", "Each theme arranges the HOME its own way. Your pictures, colors and folders go on top of it, and you can change the theme later.");
            var grid = new CardPanel { MinItemWidth = 150, MaxColumns = 2, Gap = 12, Margin = new Thickness(0, 0, 0, 8) };
            foreach (var t in new[] { DefaultTheme }.Concat(DesignProfile.Themes.Where(x => x != DefaultTheme)))
            {
                var ex = ThemeExamples().FirstOrDefault(x => x.Theme == t); var pic = ThemePicture(ex, true);
                var body = new StackPanel();
                body.Children.Add(new TextBlock { Text = ThemeName(t), FontSize = Ty.Body, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
                if (pic != null) { var im = new Image { Source = pic, Stretch = Stretch.Uniform, MaxHeight = 140, HorizontalAlignment = HorizontalAlignment.Left }; RenderOptions.SetBitmapScalingMode(im, BitmapScalingMode.HighQuality); body.Children.Add(new Border { CornerRadius = new CornerRadius(4), ClipToBounds = true, Child = im }); }
                var rb = new RadioButton { GroupName = "design-theme", Content = body, IsChecked = t == h.BaseTheme, IsEnabled = !ro, VerticalContentAlignment = VerticalAlignment.Top };
                AutomationProperties.SetName(rb, ThemeName(t));
                var card = new Border { BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(8), Padding = new Thickness(10), Background = Res<Brush>("B.Surface"), BorderBrush = Res<Brush>(t == h.BaseTheme ? "B.Red" : "B.Line"), Child = rb };
                var tt = t; rb.Checked += (s, e) => { if (design != null && design.BaseTheme != tt) DesignEdit(() => design.SetBaseTheme(tt)); };
                grid.Children.Add(card);
            }
            sp.Children.Add(grid);
            sp.Children.Add(T("The small pictures are examples of each theme, filled with games. The preview shows your own design.", Ty.Small, false, true));
        }

        // ------------------------------------------------------------------ step 2: pictures and startup
        static readonly Dictionary<string, (string title, string what)> PictureText = new Dictionary<string, (string, string)>
        {
            ["scene"] = ("HOME background", "Fills the screen behind the HOME."),
            ["details-backdrop"] = ("Game Details background", "Behind a game's details, on the Game Details screen."),
            ["plate"] = ("Frame", "Drawn over the background, under the HOME's controls. Clear parts of a PNG picture stay clear."),
            ["icon-games"] = ("All Games icon", null), ["icon-folder"] = ("Folder icon", null), ["icon-star"] = ("Favorites icon", null), ["icon-clock"] = ("Recently Played icon", null), ["icon-gear"] = ("Settings icon", null),
        };
        static string PictureTitle(string slot) => PictureText.TryGetValue(slot, out var p) ? p.title : Cap(DesignProfile.Slots.TryGetValue(slot, out var r) ? r.Text : slot);

        void PicturesStep(StackPanel sp, HomeDesign h, bool ro)
        {
            StepHead(sp, "Pictures", "Use pictures of your own, or keep the theme's. Each picture is fitted to its place for you.");
            var theme = h.BaseTheme;
            foreach (var slot in new[] { "scene", "details-backdrop", "plate" }.Where(s => DesignProfile.ThemeDraws(theme, s) || h.ImageFor(s) != null)) sp.Children.Add(PictureRow(h, slot, ro));
            var icons = DesignProfile.SlotIds.Where(s => s.StartsWith("icon-", StringComparison.Ordinal) && (DesignProfile.ThemeDraws(theme, s) || h.ImageFor(s) != null)).ToList();
            if (icons.Count > 0)
            {
                var ip = new StackPanel(); foreach (var s in icons) ip.Children.Add(PictureRow(h, s, ro));
                sp.Children.Add(Disclosure("Icons (" + icons.Count + ")", ip, icons.Any(s => h.ImageFor(s) != null)));
            }

            StepHead(sp, "Startup picture", "What your console shows while CCS HOME starts.", false);
            var st = h.Document["startup"] as JObj; var mode = st?["mode"] as string ?? "random";
            var random = new RadioButton { GroupName = "design-startup", Content = "A different built-in picture each time", IsChecked = mode == "random", IsEnabled = !ro, Margin = new Thickness(0, 2, 0, 6) };
            random.Checked += (s, e) => { if (mode != "random") DesignEdit(() => design.SetStartup("random")); };
            sp.Children.Add(random);
            var same = new RadioButton { GroupName = "design-startup", Content = "The same built-in picture every time", IsChecked = mode == "builtin", IsEnabled = !ro, Margin = new Thickness(0, 2, 0, 4) };
            var builtin = new ComboBox { IsEnabled = !ro, MaxWidth = 280, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(26, 0, 0, 8) };
            foreach (var b0 in DesignProfile.StartupBuiltins) builtin.Items.Add(new ComboBoxItem { Content = StartupName(b0), Tag = b0, IsSelected = b0 == st?["builtin"] as string });
            if (builtin.SelectedIndex < 0) builtin.SelectedIndex = 0;
            AutomationProperties.SetName(builtin, "Built-in startup picture");
            same.Checked += (s, e) => { if (mode != "builtin") DesignEdit(() => design.SetStartup("builtin", (string)((ComboBoxItem)builtin.SelectedItem).Tag)); };
            builtin.SelectionChanged += (s, e) => { var id = (builtin.SelectedItem as ComboBoxItem)?.Tag as string; if (id != null && (mode != "builtin" || id != st?["builtin"] as string)) DesignEdit(() => design.SetStartup("builtin", id)); };
            sp.Children.Add(same); sp.Children.Add(builtin);
            var own = new RadioButton { GroupName = "design-startup", Content = "A picture of your own", IsChecked = mode == "image", IsEnabled = !ro, Margin = new Thickness(0, 2, 0, 4) };
            own.Checked += (s, e) => { if (mode != "image") { if (!PickPicture("startup", DesignProfile.Slots["startup"], bytes => design.SetStartup("image", null, bytes))) RefreshDesigner(); } };
            sp.Children.Add(own);
            if (mode == "image")
            {
                var g = new StackPanel { Margin = new Thickness(26, 0, 0, 6) };
                if (st["image"] is string ss && h.Images.TryGetValue(ss, out var sb)) g.Children.Add(PictureFrame(Thumb(sb, 240, 135)));
                var row = new WrapPanel(); row.Children.Add(Small("Choose Another…", () => PickPicture("startup", DesignProfile.Slots["startup"], bytes => design.SetStartup("image", null, bytes)), !ro, "Choose another startup picture…"));
                g.Children.Add(row); var fit = FitChoice("startup", DesignProfile.Slots["startup"], bytes => design.SetStartup("image", null, bytes), ro); if (fit != null) g.Children.Add(fit);
                sp.Children.Add(g);
            }
        }

        UIElement PictureRow(HomeDesign h, string slot, bool ro)
        {
            var rule = DesignProfile.Slots[slot]; var title = PictureTitle(slot); var what = PictureText.TryGetValue(slot, out var pt) ? pt.what : null;
            var sha = h.ImageFor(slot); bool mine = sha != null && h.Images.ContainsKey(sha);
            var g = new Grid { Margin = new Thickness(0, 0, 0, 12) }; g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition());
            bool icon = slot.StartsWith("icon-", StringComparison.Ordinal);
            UIElement pic = mine ? PictureFrame(Thumb(h.Images[sha], icon ? 56 : 128, icon ? 56 : 72))
                                 : new Border { Width = icon ? 56 : 128, Height = icon ? 56 : 72, CornerRadius = new CornerRadius(6), BorderBrush = Res<Brush>("B.Line2"), BorderThickness = new Thickness(1), Background = Res<Brush>("B.Surface2"),
                                                Child = new TextBlock { Text = "The theme's own", FontSize = Ty.Fine, Foreground = Res<Brush>("B.Muted"), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6) } };
            ((FrameworkElement)pic).VerticalAlignment = VerticalAlignment.Top; ((FrameworkElement)pic).Margin = new Thickness(0, 2, 14, 0); g.Children.Add(pic);
            var info = new StackPanel(); Grid.SetColumn(info, 1); g.Children.Add(info);
            info.Children.Add(new TextBlock { Text = title, FontSize = Ty.Body, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            if (what != null) info.Children.Add(T(what, Ty.Small, false, true, new Thickness(0, 2, 0, 2)));
            if (!DesignProfile.ThemeDraws(h.BaseTheme, slot)) info.Children.Add(T(ThemeName(h.BaseTheme) + " does not show this picture.", Ty.Small, false, true, new Thickness(0, 2, 0, 2)));
            var row = new WrapPanel();
            row.Children.Add(Small(mine ? "Choose Another…" : "Choose…", () => PickPicture(slot, rule, bytes => design.SetSlot(slot, bytes)), !ro, "Choose a picture for the " + title + "…"));
            if (mine) row.Children.Add(Small("Use the Theme's Own", () => DesignEdit(() => { design.SetSlot(slot, null); designSources.Remove(slot); }), !ro, "Use the theme's own " + title));
            var orig = designOpened?.ImageFor(slot);
            if (designOpened != null && orig != sha) row.Children.Add(Small("Undo My Change", () => DesignEdit(() => { design.SetSlot(slot, orig == null ? null : designOpened.Images[orig]); designSources.Remove(slot); }), !ro,"Undo my change to the " + title));
            info.Children.Add(row);
            if (mine) { var fit = FitChoice(slot, rule, bytes => design.SetSlot(slot, bytes), ro); if (fit != null) info.Children.Add(fit); }
            return g;
        }

        /// <summary>how a picture fills a place of an exact size: the whole place (its edges trimmed when the shapes differ) or all of the
        /// picture (with bars). Offered for a picture chosen in this window (its file is still at hand).</summary>
        UIElement FitChoice(string key, ImageSlotRule rule, Action<byte[]> use, bool ro)
        {
            if (!rule.Exact || !designSources.TryGetValue(key, out var src)) return null;
            var cur = designFits.TryGetValue(key, out var f) ? f : "cover";
            var sp = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            sp.Children.Add(T("Fit", Ty.Small, true, true, new Thickness(0, 0, 0, 2)));
            foreach (var (id, text) in new[] { ("cover", "Fill the space (the edges may be trimmed)"), ("contain", "Show all of the picture (with bars where needed)") })
            {
                var rb = new RadioButton { GroupName = "fit-" + key, Content = text, IsChecked = cur == id, IsEnabled = !ro, Margin = new Thickness(0, 1, 0, 1) };
                AutomationProperties.SetName(rb, text + ", " + PictureTitleFor(key));
                var fid = id; rb.Checked += (s, e) => { if (designFits.TryGetValue(key, out var was) ? was == fid : fid == "cover") return; if (DesignEdit(() => use(ImageNormalizer.Normalize(src, rule, fid)))) designFits[key] = fid; };
                sp.Children.Add(rb);
            }
            return sp;
        }
        static string PictureTitleFor(string key) => key == "startup" ? "startup picture" : PictureTitle(key);

        /// <summary>a picture of the person's own for a place: the Windows file picker, then the picture is fitted (filling the place first;
        /// the Fit choice beside it can show all of it instead). False when nothing was chosen or the picture was refused.</summary>
        bool PickPicture(string key, ImageSlotRule rule, Action<byte[]> use)
        {
            var d = new Microsoft.Win32.OpenFileDialog { Title = "Choose a picture for the " + PictureTitleFor(key) + " (PNG or JPEG)", Filter = PictureFilter };
            if (d.ShowDialog(this) != true) return false;
            byte[] src;
            try { var fi = new FileInfo(d.FileName); if (fi.Length > ImageRules.ImportMaxBytes) { SetDesignProblem("This picture is too large. Choose a smaller picture.", "The file is " + CUnits.Size(fi.Length) + "; at most " + CUnits.Size(ImageRules.ImportMaxBytes) + " can be used."); return false; } src = File.ReadAllBytes(d.FileName); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { SetDesignProblem("That file could not be read. Choose another one.", ex.Message); return false; }
            if (!DesignEdit(() => use(ImageNormalizer.Normalize(src, rule, "cover")), false)) return false;
            if (rule.Exact) { designSources[key] = src; designFits[key] = "cover"; }
            RefreshDesigner();
            return true;
        }

        // ------------------------------------------------------------------ step 3: text and colors
        static readonly (string hex, string name)[] TextColors = { ("#e9e7e1", "Warm white"), ("#ffffff", "White"), ("#fde68a", "Pale gold"), ("#bfdbfe", "Pale blue"), ("#bbf7d0", "Pale green"), ("#fbcfe8", "Pale pink") };
        static readonly (string hex, string name)[] AccentColors = { ("#e4000f", "Red"), ("#b3001b", "Dark red"), ("#c2410c", "Orange"), ("#a16207", "Gold"), ("#15803d", "Green"), ("#0f766e", "Teal"), ("#1d4ed8", "Blue"), ("#6d28d9", "Purple"), ("#be185d", "Pink"), ("#374151", "Gray") };
        static readonly (string hex, string name)[] PanelColors = { ("#111115", "Near black"), ("#000000", "Black"), ("#1e293b", "Slate"), ("#3b0a0a", "Dark red"), ("#052e16", "Dark green"), ("#2e1065", "Dark purple"), ("#334155", "Gray") };
        DispatcherTimer opacityTimer;

        void TextStep(StackPanel sp, HomeDesign h, bool ro)
        {
            StepHead(sp, "Colors", "The colors of the HOME's menus and panels.");
            var ap = h.Document["appearance"] as JObj ?? new JObj();
            sp.Children.Add(ColorRow("Text color", "text_color", TextColors, ap, ro));
            sp.Children.Add(ColorRow("Highlight color", "accent_color", AccentColors, ap, ro, "Behind the chosen menu row and buttons, under white lettering."));
            sp.Children.Add(ColorRow("Panel color", "panel_color", PanelColors, ap, ro));
            var op = new Slider { Minimum = 0, Maximum = 100, Value = ap["panel_opacity"] is long o ? o : 100, IsSnapToTickEnabled = true, TickFrequency = 5, SmallChange = 5, LargeChange = 10, IsEnabled = !ro, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 320 };
            AutomationProperties.SetName(op, "How solid the panels are");
            var opText = T(((int)op.Value) + "%", 13, false, false, new Thickness(10, 0, 0, 0)); opText.VerticalAlignment = VerticalAlignment.Center;
            op.ValueChanged += (s, e) =>
            {
                opText.Text = ((int)op.Value) + "%";
                if (opacityTimer == null) { opacityTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) }; opacityTimer.Tick += (s2, e2) => { opacityTimer.Stop(); (opacityTimer.Tag as Action)?.Invoke(); }; }
                opacityTimer.Stop(); var v = (int)op.Value; opacityTimer.Tag = (Action)(() => { var a2 = design?.Document["appearance"] as JObj; if (design != null && (a2?["panel_opacity"] is long cur ? cur : -1) != v) DesignEdit(() => design.SetAppearance("panel_opacity", v), false); }); opacityTimer.Start();
            };
            var opRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) }; DockPanel.SetDock(opText, Dock.Right); opRow.Children.Add(opText); opRow.Children.Add(op);
            sp.Children.Add(T("How solid the panels are", Ty.Small, false, true, new Thickness(0, 6, 0, 2))); sp.Children.Add(new Border { MaxWidth = 380, HorizontalAlignment = HorizontalAlignment.Left, Child = opRow });

            StepHead(sp, "Lettering", "The HOME's own fonts, for menu titles and menu text.", false);
            foreach (var (k, label) in new[] { ("title_font", "Title font"), ("text_font", "Text font") })
            {
                var cb = new ComboBox { IsEnabled = !ro, MaxWidth = 320, HorizontalAlignment = HorizontalAlignment.Left };
                foreach (var f in DesignProfile.Fonts.Where(f => k == "title_font" ? DesignProfile.FontPx(f) <= DesignProfile.MaxMenuTitlePx : DesignProfile.FontPx(f) >= DesignProfile.MinMenuTextPx && DesignProfile.FontPx(f) <= DesignProfile.MaxMenuTextPx))
                    cb.Items.Add(new ComboBoxItem { Content = FontName(f), Tag = f, IsSelected = f == ap[k] as string });
                var kk = k; cb.SelectionChanged += (s, e) => { var v = (cb.SelectedItem as ComboBoxItem)?.Tag as string; if (v != null && v != (design?.Document["appearance"] as JObj)?[kk] as string) DesignEdit(() => design.SetAppearance(kk, v), false); };
                sp.Children.Add(Field(label, cb, null, 340));
            }

            StepHead(sp, "HOME settings", null, false);
            var se = h.Document["settings"] as JObj ?? new JObj();
            foreach (var (k, label) in new[] { ("music", "Play the HOME music"), ("hints", "Show the button hints"), ("reduced_motion", "Less motion") })
            {
                var cb = new CheckBox { Content = label, IsChecked = se[k] is bool bv && bv, IsEnabled = !ro, Margin = new Thickness(0, 2, 0, 4) };
                var kk = k; cb.Click += (s0, e0) => DesignEdit(() => design.SetSetting(kk, cb.IsChecked == true), false); sp.Children.Add(cb);
            }
        }

        UIElement ColorRow(string label, string key, (string hex, string name)[] presets, JObj ap, bool ro, string what = null)
        {
            var cur = (ap[key] as string ?? "").ToLowerInvariant();
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            sp.Children.Add(new TextBlock { Text = label, FontSize = Ty.Text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, what == null ? 4 : 1) });
            if (what != null) sp.Children.Add(T(what, Ty.Small, false, true, new Thickness(0, 0, 0, 4)));
            var row = new WrapPanel();
            var all = presets.ToList(); if (cur.Length == 7 && !all.Any(p => p.hex == cur)) all.Add((cur, "Your color " + cur));
            foreach (var (hex, name) in all)
            {
                bool on = hex == cur;
                var sw = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(4), Background = Hex(hex), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1) };
                var ring = new Border { Padding = new Thickness(2), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(2), BorderBrush = on ? Res<Brush>("B.Ink") : Brushes.Transparent, Child = sw };
                var b = new Button { Style = Res<Style>("Wb.Plain"), Content = ring, Margin = new Thickness(0, 0, 4, 4), IsEnabled = !ro, ToolTip = name };
                AutomationProperties.SetName(b, label + ": " + name + (on ? ", chosen" : ""));
                var hx = hex; b.Click += (s, e) => { if (hx != ((design?.Document["appearance"] as JObj)?[key] as string ?? "").ToLowerInvariant()) DesignEdit(() => design.SetAppearance(key, hx)); };
                row.Children.Add(b);
            }
            sp.Children.Add(row);
            var custom = new TextBox { Text = cur, IsReadOnly = ro, Width = 110, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 0) };
            AutomationProperties.SetName(custom, label + ", as #rrggbb");
            void Commit()
            {
                var v = custom.Text.Trim().ToLowerInvariant(); if (v.Length > 0 && v[0] != '#') v = "#" + v;
                if (v == ((design?.Document["appearance"] as JObj)?[key] as string ?? "").ToLowerInvariant()) return;
                if (!System.Text.RegularExpressions.Regex.IsMatch(v, "^#[0-9a-f]{6}$")) { SetDesignProblem("Write a color as # and six letters or digits, for example #e4000f.", null); return; }
                DesignEdit(() => design.SetAppearance(key, v));
            }
            custom.KeyDown += (s, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
            custom.LostKeyboardFocus += (s, e) => { if (!ro && IsDesignPage()) Dispatcher.BeginInvoke(new Action(() => { if (IsDesignPage()) Commit(); }), DispatcherPriority.Input); };
            var cr = new StackPanel { Orientation = Orientation.Horizontal }; cr.Children.Add(new TextBlock { Text = "Or a color code", FontSize = Ty.Small, Foreground = Res<Brush>("B.Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 8, 0) }); cr.Children.Add(custom);
            sp.Children.Add(cr);
            return sp;
        }
        static Brush Hex(string s) { try { return (Brush)new BrushConverter().ConvertFromInvariantString(s); } catch (FormatException) { return Brushes.Transparent; } }

        // ------------------------------------------------------------------ step 4: layout and folders
        void LayoutStep(StackPanel sp, HomeDesign h, bool ro)
        {
            StepHead(sp, "The HOME's controls", "Move a group of the HOME's controls. Each group moves whole and keeps its size, inside the screen's safe area.");
            if (h.ImageFor("scene") == null)
            {
                sp.Children.Add(Note("To move the HOME's controls, first choose your own HOME background: each theme's own background has its controls' panels painted in place.", "info"));
                sp.Children.Add(Buttons(Small("Choose a HOME Background", () => GoStep(StepPictures, "Choose a picture for the HOME background…"), !ro)));
            }
            var placed = h.ControlRegions; bool can = h.ImageFor("scene") != null && !ro;
            foreach (var ui in DesignProfile.UiOrder)
            {
                var box = DesignProfile.RegionBox(h.BaseTheme, ui);
                if (box == null)
                {
                    if (placed.TryGetValue(ui, out var gone)) { var gid = gone["id"] as string; var gr = new StackPanel { Margin = new Thickness(0, 6, 0, 6) }; gr.Children.Add(T(Cap(DesignProfile.UiName(ui)) + ": " + ThemeName(h.BaseTheme) + " cannot move this. The place it had in another theme is kept in the design.", Ty.Small, false, true)); gr.Children.Add(Buttons(Small("Remove", () => DesignEdit(() => design.RemoveRegion(gid)), !ro, "Remove the kept place of the " + DesignProfile.UiName(ui)))); sp.Children.Add(gr); }
                    continue;
                }
                sp.Children.Add(ControlRow(h, ui, box, placed.TryGetValue(ui, out var r) ? r : null, can, ro));
            }

            StepHead(sp, "Your panels, labels and pictures", "Drawn under the HOME's controls, inside the safe area.", false);
            var regs = h.Regions; int pn = 0, ln = 0, imn = 0;
            foreach (var r in regs.Where(x => x["kind"] as string != "ui"))
            {
                var k = r["kind"] as string; var id = r["id"] as string;
                var name = k == "panel" ? "Panel " + (++pn) : k == "label" ? "Label " + (++ln) + ": " + Display.Line(r["text"] as string, 30) : k == "image" ? "Picture " + (++imn) : "Something from a newer version (kept as it is)";
                var box = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
                var line = new DockPanel();
                if (k == "panel" || k == "label" || k == "image") { var rm = Small("Remove", () => DesignEdit(() => design.RemoveRegion(id)), !ro, "Remove " + name); rm.Margin = new Thickness(8, 0, 0, 0); DockPanel.SetDock(rm, Dock.Right); line.Children.Add(rm); }
                line.Children.Add(NoTerms(new TextBlock { Text = name, FontSize = Ty.Text, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }));
                box.Children.Add(line);
                if (k == "panel" || k == "label" || k == "image") box.Children.Add(Disclosure("Place and size", RegionFields(r, name, ro), false));
                sp.Children.Add(box);
            }
            string NewId(string kind) { int n = 1; while (regs.Any(x => x["id"] as string == kind + "-" + n)) n++; return kind + "-" + n; }
            var lt = Pending("New label's text", ro);
            var add = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            add.Children.Add(Small("Add a Panel", () => DesignEdit(() => design.UpsertRegion(NewId("panel"), x => { x["kind"] = "panel"; x["x"] = 40L; x["y"] = 40L; x["w"] = 300L; x["h"] = 120L; x["layer"] = 1L; x["opacity"] = 80L; x["color"] = "#101018"; })), !ro));
            add.Children.Add(Small("Add a Picture…", () => PickPicture("decoration", DesignProfile.Slots["decoration"], bytes => { var sha = design.AddImage(bytes); design.UpsertRegion(NewId("picture"), x => { x["kind"] = "image"; x["x"] = 40L; x["y"] = 40L; x["w"] = 300L; x["h"] = 200L; x["layer"] = 1L; x["image"] = sha; }); }), !ro));
            sp.Children.Add(add);
            var labelRow = new Grid { MaxWidth = 460, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) }; labelRow.ColumnDefinitions.Add(new ColumnDefinition()); labelRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            labelRow.Children.Add(lt);
            var addLabel = Small("Add a Label", () => { var text = lt.Text.Trim(); if (text.Length == 0) { SetDesignProblem("Type the label's text first.", null); return; } DesignEdit(() => { design.UpsertRegion(NewId("label"), x => { x["kind"] = "label"; x["x"] = 40L; x["y"] = 40L; x["w"] = 400L; x["h"] = 40L; x["layer"] = 2L; x["text"] = text; x["font"] = "sans700_20"; }); lt.Text = ""; }); }, !ro);
            addLabel.Margin = new Thickness(8, 0, 0, 0); Grid.SetColumn(addLabel, 1); labelRow.Children.Add(addLabel);
            sp.Children.Add(T("New label's text", Ty.Small, false, true, new Thickness(0, 6, 0, 3))); sp.Children.Add(labelRow);

            StepHead(sp, "Folders", "Group games into folders, each with a picture of your own if you like.", false);
            FolderEditor(sp, h, ro);
            StepHead(sp, "Game order", null, false);
            OrderEditor(sp, h, ro);
        }

        const int Nudge = 16;
        UIElement ControlRow(HomeDesign h, string ui, int[] box, JObj mine, bool can, bool ro)
        {
            var name = Cap(DesignProfile.UiName(ui)); int w = box[2] - box[0], hh = box[3] - box[1];
            long x = mine?["x"] is long mx ? mx : box[0], y = mine?["y"] is long my ? my : box[1]; bool moved = mine != null && (x != box[0] || y != box[1]);
            var sp = new StackPanel { Margin = new Thickness(0, 4, 0, 10) };
            var line = new WrapPanel(); line.Children.Add(new TextBlock { Text = name, FontSize = Ty.Text, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
            line.Children.Add(Pill(moved ? "Moved" : "In the theme's place", moved ? "B.Warn" : "B.Muted")); sp.Children.Add(line);
            var row = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            foreach (var (label, dx, dy) in new[] { ("Left", -Nudge, 0), ("Right", Nudge, 0), ("Up", 0, -Nudge), ("Down", 0, Nudge) })
            {
                long nx = Math.Max(DesignProfile.Margin, Math.Min(DesignProfile.StageW - DesignProfile.Margin - w, x + dx)), ny = Math.Max(DesignProfile.Margin, Math.Min(DesignProfile.StageH - DesignProfile.Margin - hh, y + dy));
                row.Children.Add(Small(label, () => DesignEdit(() => design.PlaceRegion(ui, nx, ny)), can && (nx != x || ny != y), "Move the " + DesignProfile.UiName(ui) + " " + label.ToLowerInvariant()));
            }
            if (mine != null) { var id = mine["id"] as string; row.Children.Add(Small("Back to the Theme's Place", () => DesignEdit(() => design.RemoveRegion(id)), !ro, "Put the " + DesignProfile.UiName(ui) + " back in the theme's place")); }
            sp.Children.Add(row);
            var exact = new StackPanel();
            exact.Children.Add(T("Its top-left corner, in screen pixels: x from " + DesignProfile.Margin + " to " + (DesignProfile.StageW - DesignProfile.Margin - w) + ", y from " + DesignProfile.Margin + " to " + (DesignProfile.StageH - DesignProfile.Margin - hh) + ". The theme puts it at " + box[0] + ", " + box[1] + ".", Ty.Small, false, true));
            var xb = new TextBox { Text = x.ToString(En), IsReadOnly = !can }; var yb = new TextBox { Text = y.ToString(En), IsReadOnly = !can };
            AutomationProperties.SetName(xb, name + ", x"); AutomationProperties.SetName(yb, name + ", y");
            var fr = FieldRow(("x (left)", xb, 100), ("y (top)", yb, 100));
            fr.Children.Add(BottomAligned(Small("Move There", () => DesignEdit(() =>
            {
                if (!long.TryParse(xb.Text.Trim(), System.Globalization.NumberStyles.Integer, En, out var nx) || !long.TryParse(yb.Text.Trim(), System.Globalization.NumberStyles.Integer, En, out var ny)) throw new ComposeException("design.type", "Use whole numbers for the place.");
                design.PlaceRegion(ui, nx, ny);
            }), can, "Move the " + DesignProfile.UiName(ui) + " there")));
            exact.Children.Add(fr);
            sp.Children.Add(Disclosure("Exact place", exact, false));
            return sp;
        }

        UIElement RegionFields(JObj r, string name, bool ro)
        {
            var id = r["id"] as string; var f = new Dictionary<string, TextBox>(); var sp = new StackPanel();
            var fields = new List<(string, FrameworkElement, double)>();
            foreach (var (k, label) in new[] { ("x", "x (left)"), ("y", "y (top)"), ("w", "Width"), ("h", "Height") })
            { var tb = new TextBox { Text = (r[k] is long v ? v : 0).ToString(En), IsReadOnly = ro }; AutomationProperties.SetName(tb, name + ", " + label); f[k] = tb; fields.Add((label, tb, 84)); }
            var row = FieldRow(fields.ToArray());
            TextBox text = null;
            if (r["kind"] as string == "label") { text = new TextBox { Text = r["text"] as string ?? "", IsReadOnly = ro }; AutomationProperties.SetName(text, name + ", text"); sp.Children.Add(Field("Text", text, null, 360)); }
            row.Children.Add(BottomAligned(Small("Apply", () => DesignEdit(() =>
            {
                var vals = new Dictionary<string, long>();
                foreach (var kv in f) { if (!long.TryParse(kv.Value.Text.Trim(), System.Globalization.NumberStyles.Integer, En, out var n)) throw new ComposeException("design.type", "Use whole numbers for the place and size."); vals[kv.Key] = n; }
                var t = text?.Text.Trim();
                design.UpsertRegion(id, x => { foreach (var kv in vals) x[kv.Key] = kv.Value; if (t != null) x["text"] = t; });
            }), !ro, "Apply the place and size of " + name)));
            sp.Children.Add(row);
            return sp;
        }

        void FolderEditor(StackPanel sp, HomeDesign h, bool ro)
        {
            var folders = h.Folders;
            var byParent = folders.GroupBy(f => f["parent"] as string ?? "").ToDictionary(x => x.Key, x => x.OrderBy(f => f["pos"] is long p ? p : 0).ThenBy(f => f["name"] as string, StringComparer.CurrentCultureIgnoreCase).ToList());
            void Level(string parent, int depth)
            {
                if (!byParent.TryGetValue(parent, out var list)) return;
                foreach (var f in list)
                {
                    var id = f["id"] as string; var box = new StackPanel { Margin = new Thickness(depth * 22, 4, 0, 8) };
                    var games = (f["games"] as List<object> ?? new List<object>()).Count; var fname = Display.Line(f["name"] as string, 40);
                    var line = new DockPanel();
                    if (f["art"] is string art && h.Images.TryGetValue(art, out var ab)) { var th = Thumb(ab, 110, 30); DockPanel.SetDock(th, Dock.Right); line.Children.Add(th); }
                    line.Children.Add(NoTerms(new TextBlock { Text = fname + "  (" + Count(games, "game") + ")", FontSize = Ty.Text, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }));
                    box.Children.Add(line);
                    var row = new WrapPanel();
                    var nm = new TextBox { Text = f["name"] as string, Width = 180, IsReadOnly = ro, Margin = new Thickness(0, 4, 6, 0) }; AutomationProperties.SetName(nm, "Name of the folder " + fname);
                    row.Children.Add(nm); row.Children.Add(Small("Rename", () => DesignEdit(() => design.UpsertFolder(id, x => x["name"] = nm.Text.Trim())), !ro, "Rename the folder " + fname));
                    row.Children.Add(Small("Picture…", () => PickPicture("folder-art", DesignProfile.Slots["folder-art"], bytes => { var sha = design.AddImage(bytes); design.UpsertFolder(id, x => x["art"] = sha); }), !ro, "Choose a picture for the folder " + fname + "…"));
                    row.Children.Add(Small("Remove", () => DesignEdit(() => design.RemoveFolder(id)), !ro && !byParent.ContainsKey(id), "Remove the folder " + fname));
                    box.Children.Add(row); sp.Children.Add(box);
                    Level(id, depth + 1);
                }
            }
            Level("", 0);
            if (folders.Count == 0) sp.Children.Add(T("No folders yet: games are listed in the game order.", Ty.Small, false, true));
            var nameBox = Pending("New folder's name", ro);
            var parentBox = new ComboBox { IsEnabled = !ro }; parentBox.Items.Add(new ComboBoxItem { Content = "At the top", Tag = null });
            foreach (var f in folders) parentBox.Items.Add(new ComboBoxItem { Content = "Inside " + Display.Line(f["name"] as string, 40), Tag = f["id"] as string });
            // the place chosen for a folder not added yet is kept with its name (a folder removed since: at the top)
            parentBox.SelectedItem = parentBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag as string == designDraftParent) ?? parentBox.Items[0];
            designDraftParent = (parentBox.SelectedItem as ComboBoxItem)?.Tag as string;
            parentBox.SelectionChanged += (s, e) => designDraftParent = (parentBox.SelectedItem as ComboBoxItem)?.Tag as string;
            AutomationProperties.SetName(parentBox, "Where the new folder goes");
            var addRow = FieldRow(("New folder", nameBox, 200), ("Where", parentBox, 200));
            addRow.Children.Add(BottomAligned(Small("Add the Folder", () => DesignEdit(() =>
            {
                var n = nameBox.Text.Trim(); if (n.Length == 0) throw new ComposeException("design.type", "Give the folder a name.");
                var slug = new string(n.ToLowerInvariant().Select(ch => ch >= 'a' && ch <= 'z' || ch >= '0' && ch <= '9' ? ch : '-').ToArray()).Trim('-'); if (slug.Length == 0) slug = "folder"; if (slug.Length > 32) slug = slug.Substring(0, 32).Trim('-');
                var fid = slug; int k = 2; while (folders.Any(f => f["id"] as string == fid)) fid = slug + "-" + k++;
                var parent = (parentBox.SelectedItem as ComboBoxItem)?.Tag as string;
                design.UpsertFolder(fid, x => { x["name"] = n; if (parent != null) x["parent"] = parent; x["pos"] = (long)folders.Count(f => (f["parent"] as string) == parent); x["games"] = new List<object>(); }); nameBox.Text = ""; designDraftParent = null;
            }), !ro)));
            sp.Children.Add(addRow);
        }

        void OrderEditor(StackPanel sp, HomeDesign h, bool ro)
        {
            // games are named by their public catalog entries or as numbered placeholders; no game files
            var order = h.Order;
            if (order.Count == 0) { sp.Children.Add(T("The HOME's own order. A design opened from a file can carry its own order, which you can change here.", Ty.Small, false, true)); return; }
            var list = new StackPanel();
            for (int i = 0; i < Math.Min(order.Count, 300); i++)
            {
                var line = new Grid { Margin = new Thickness(0, 0, 0, 2) }; line.ColumnDefinitions.Add(new ColumnDefinition()); line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var gname = Display.Line(order[i].DisplayName, 60);
                line.Children.Add(NoTerms(new TextBlock { Text = (i + 1) + ". " + gname, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 0) }));
                var row = new StackPanel { Orientation = Orientation.Horizontal }; Grid.SetColumn(row, 1); line.Children.Add(row); int at = i;
                row.Children.Add(Small("Up", () => DesignEdit(() => { var l = design.Order; var x = l[at]; l.RemoveAt(at); l.Insert(at - 1, x); design.SetOrder(l); }), !ro && i > 0, "Move " + gname + " up"));
                row.Children.Add(Small("Down", () => DesignEdit(() => { var l = design.Order; var x = l[at]; l.RemoveAt(at); l.Insert(at + 1, x); design.SetOrder(l); }), !ro && i < order.Count - 1, "Move " + gname + " down"));
                row.Children.Add(Small("Take Out", () => DesignEdit(() => { var l = design.Order; l.RemoveAt(at); design.SetOrder(l); }), !ro, "Take " + gname + " out of the order"));
                list.Children.Add(line);
            }
            if (order.Count > 300) list.Children.Add(T("…and " + (order.Count - 300) + " more.", 12.5, false, true));
            sp.Children.Add(Disclosure("The game order (" + Count(order.Count, "game") + ")", list, false));
        }

        // ------------------------------------------------------------------ step 5: review and save
        void ReviewStep(StackPanel sp, HomeDesign h, bool ro)
        {
            StepHead(sp, "Name your design", null);
            var name = new TextBox { Text = h.Name ?? "", IsReadOnly = ro }; var author = new TextBox { Text = (h.Document["design"] as JObj)?["author"] as string ?? "", IsReadOnly = ro };
            AutomationProperties.SetName(name, "Design name"); AutomationProperties.SetName(author, "Made by (optional)");
            void CommitName() { var v = name.Text.Trim(); if (design != null && v != (design.Name ?? "")) DesignEdit(() => design.SetName(v), false); }
            void CommitAuthor() { var v = author.Text.Trim(); if (design != null && v != ((design.Document["design"] as JObj)?["author"] as string ?? "")) DesignEdit(() => design.SetAuthor(v), false); }
            name.KeyDown += (s, e) => { if (e.Key == Key.Enter) { CommitName(); e.Handled = true; } }; name.LostKeyboardFocus += (s, e) => { if (!ro && IsDesignPage()) CommitName(); };
            author.KeyDown += (s, e) => { if (e.Key == Key.Enter) { CommitAuthor(); e.Handled = true; } }; author.LostKeyboardFocus += (s, e) => { if (!ro && IsDesignPage()) CommitAuthor(); };
            // typing pauses also keep the words (so nothing typed is lost when the window closes from here)
            var typing = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            typing.Tick += (s, e) => { typing.Stop(); if (ro || !IsDesignPage() || !Within(page, name)) return; if (name.Text.Trim().Length > 0) CommitName(); CommitAuthor(); };
            name.TextChanged += (s, e) => { typing.Stop(); typing.Start(); }; author.TextChanged += (s, e) => { typing.Stop(); typing.Start(); };
            sp.Children.Add(FieldRow(("Design name", name, 260), ("Made by (optional)", author, 220)));

            StepHead(sp, "What your design changes", null, false);
            sp.Children.Add(ChangeSummary(h));
            var issues = PlainIssues(h.Unsupported);
            if (!ro && issues.Count > 0) sp.Children.Add(Note("Before it can go on a console:\n" + string.Join("\n", issues.Select(x => "• " + x)), "warn"));
            var bud = h.Budget();
            if (!ro && bud.Ok && bud.PeakEstimateOver) sp.Children.Add(Note("Your pictures may need more memory than the HOME has. See Every Screen checks it.", "info"));

            StepHead(sp, "Save", "Your design is saved as a " + FileKinds.HomeDesign + " file (.ccshome) on this PC.", false);
            sp.Children.Add(Buttons(B("Save", "Btn.Primary", () => SaveDesign(false), !ro), B("Save As…", "Btn", () => SaveDesign(true), !ro), B("Share…", "Btn", ShareDesign, !ro),
                                    h.InspectOnly && h.OriginalFile != null ? B("Export the Original File…", "Btn.Ghost", ExportOriginal) : null));

            StepHead(sp, "See every screen", "The preview shows the HOME's first screen. See the others too, such as Game Details, the menus and Browse.", false);
            sp.Children.Add(Buttons(B("See Every Screen", "Btn", ShowEveryScreen, h.CanApply && !ro)));

            StepHead(sp, "Put it on your console", "Check your design with the games on your " + Con + ", then apply it. Your console is backed up first.", false);
            sp.Children.Add(Buttons(B("Check and Apply on My Console…", "Btn", () => ShowDesignOnConsole(), h.CanApply && !ro)));

            var cr = new StackPanel();
            var credits = (h.Document["credits"] as List<object> ?? new List<object>()).OfType<JObj>().ToList();
            if (credits.Count == 0) cr.Children.Add(T("No credits yet. A credit says who made the pictures or the design.", Ty.Small, false, true));
            foreach (var c0 in credits) cr.Children.Add(NoTerms(T("• " + Display.Line(c0["by"] as string, 60) + (c0["title"] is string ct ? ": " + Display.Line(ct, 80) : "") + (c0["license"] is string cl ? " (" + Display.Line(cl, 60) + ")" : ""), 13)));
            var by = Pending("Credit: made by", ro); var ti = Pending("Credit: title (optional)", ro); var li = Pending("Credit: license (optional)", ro);
            cr.Children.Add(FieldRow(("Made by", by, 200), ("Title (optional)", ti, 200), ("License (optional)", li, 160)));
            cr.Children.Add(Buttons(Small("Add the Credit", () => { if (by.Text.Trim().Length == 0) { SetDesignProblem("Type who made it first.", null); return; } DesignEdit(() => { design.AddCredit(by.Text.Trim(), Null(ti.Text), Null(li.Text)); by.Text = ti.Text = li.Text = ""; }); }, !ro)));
            // open when it holds a credit typed but not added yet, so the kept text is in view
            sp.Children.Add(Disclosure("Credits (" + credits.Count + ")", cr, new[] { by, ti, li }.Any(t => t.Text.Trim().Length > 0)));

            var tech = new List<object>();
            tech.Add("Pictures to decode when the HOME starts, as this program works it out: " + CUnits.Size(bud.DecodeWorkBytes) + " (at most " + CUnits.Size(bud.WorkLimit) + ").");
            tech.Add("Most picture memory the HOME may need at once, an estimate: " + CUnits.Size(bud.PeakDecodedBytes) + " (the limit is " + CUnits.Size(bud.Limit) + "). The check before applying measures it with the console HOME's own program; neither is a test on console hardware.");
            if (bud.StartupDecodedBytes > 0) tech.Add("The startup picture, by itself: " + CUnits.Size(bud.StartupDecodedBytes) + ".");
            foreach (var u in h.Unsupported) tech.Add("Check: " + u);
            tech.Add("Design " + h.Id + ", revision " + h.Revision.ToString(En) + ", " + h.Schema + ".");
            sp.Children.Add(Details("Technical details", tech.ToArray()));
        }

        void ExportOriginal()
        {
            var h = design; if (h?.OriginalFile == null) return;
            var d = new Microsoft.Win32.SaveFileDialog { Title = "Export the Original File (Shared HOME Design)", Filter = DesignFilter, FileName = Path.GetFileName(designPath ?? "design.ccshome"), OverwritePrompt = false };
            if (d.ShowDialog(this) != true) return;
            try { h.ExportOriginal(d.FileName); Tell("Original file exported", "The design was saved exactly as it was received:\n\n" + d.FileName); }
            catch (Exception ex) when (ex is ComposeException || ex is IOException || ex is UnauthorizedAccessException) { SetDesignProblem(ex is IOException && File.Exists(d.FileName) ? "A file with that name is already there. Choose another name." : "The original file could not be exported there.", ex.Message); }
        }

        /// <summary>what the design changes, compared with where it started (the file it was opened from, or the theme's own look)</summary>
        UIElement ChangeSummary(HomeDesign h)
        {
            var b = designOpened ?? HomeDesign.New(h.Name ?? "My HOME", h.BaseTheme);
            var rows = new List<(string key, string value, bool changed)>();
            rows.Add(("Theme", ThemeName(h.BaseTheme), designOpened == null ? h.BaseTheme != DefaultTheme : h.BaseTheme != b.BaseTheme));
            string Pics(HomeDesign d) { var mine = DesignProfile.SlotIds.Where(s => d.ImageFor(s) != null).Select(PictureTitle).ToList(); return mine.Count == 0 ? "The theme's own" : "Yours: " + string.Join(", ", mine); }
            rows.Add(("Pictures", Pics(h), Pics(h) != Pics(b)));
            string Start(HomeDesign d) { var st = d.Document["startup"] as JObj; var m = st?["mode"] as string ?? "random"; return m == "image" ? "Your own picture" : m == "builtin" ? "Always " + StartupName(st["builtin"] as string) : "A different built-in picture each time"; }
            rows.Add(("Startup picture", Start(h), Start(h) != Start(b) || (h.Document["startup"] as JObj)?["image"] as string != (b.Document["startup"] as JObj)?["image"] as string));
            var ah = h.Document["appearance"] as JObj ?? new JObj(); var ab = b.Document["appearance"] as JObj ?? new JObj();
            var changedLook = new[] { ("text_color", "text color"), ("accent_color", "highlight color"), ("panel_color", "panel color"), ("panel_opacity", "panels"), ("title_font", "title font"), ("text_font", "text font") }.Where(x => !Equals(ah[x.Item1], ab[x.Item1])).Select(x => x.Item2).ToList();
            rows.Add(("Text and colors", changedLook.Count == 0 ? (designOpened == null ? "The HOME's own" : "As in the file") : "Changed: " + string.Join(", ", changedLook), changedLook.Count > 0));
            var moved = h.ControlRegions.Keys.Select(DesignProfile.UiName).ToList(); var own = h.Regions.Count(r => r["kind"] as string != "ui");
            string lay = (moved.Count == 0 ? "The theme's places" : "Moved: " + string.Join(", ", moved)) + (own > 0 ? "; " + Count(own, "panel, label or picture", "panels, labels and pictures") + " of your own" : "");
            string Lay(HomeDesign d) => string.Join(";", d.Regions.Select(r => Json.Write(r)));
            rows.Add(("Layout", lay, Lay(h) != Lay(b)));
            string Fold(HomeDesign d) => Json.Write(new JObj { { "f", d.Document["folders"] }, { "o", d.Document["order"] } });
            rows.Add(("Folders and game order", (h.Folders.Count == 0 ? "No folders" : Count(h.Folders.Count, "folder")) + "; " + (h.Order.Count == 0 ? "the HOME's own order" : "an order of " + Count(h.Order.Count, "game")), Fold(h) != Fold(b)));
            var se = h.Document["settings"] as JObj ?? new JObj(); var sb = b.Document["settings"] as JObj ?? new JObj();
            string Set(JObj s) => (s["music"] is bool m && m ? "Music on" : "Music off") + ", " + (s["hints"] is bool hi && hi ? "button hints shown" : "no button hints") + (s["reduced_motion"] is bool rm && rm ? ", less motion" : "");
            rows.Add(("HOME settings", Set(se), Set(se) != Set(sb)));
            var g = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            foreach (var (key, value, changed) in rows)
            {
                var head = new WrapPanel { Margin = new Thickness(0, 6, 0, 1) };
                head.Children.Add(new TextBlock { Text = key, FontSize = Ty.Text, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
                if (changed) head.Children.Add(Pill("Changed", "B.Ok"));
                g.Children.Add(head);
                g.Children.Add(NoTerms(new TextBlock { Text = value, FontSize = 13, Foreground = Res<Brush>("B.Ink2"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 2) }));
            }
            return g;
        }

        // ------------------------------------------------------------------ share
        void ShareDesign() => ShowShare(null);

        void ShowShare(string savedTo)
        {
            var h = design; if (h == null) { ShowDesigner(); return; }
            var titles = new Dictionary<string, string>();
            foreach (var r in h.Order.Concat(h.Folders.SelectMany(f => (f["games"] as List<object> ?? new List<object>()).OfType<JObj>().Select(GameRef.Of))))
                if (!r.IsPlaceholder && r.Title != null) titles[r.Catalog] = r.Title;
            var priv = new List<string> { Environment.UserName, Environment.MachineName, settings.PrimaryFolder, settings.ArchiveFolder };
            try { priv.AddRange(History.List(settings).Select(e => e.ConsoleTag)); } catch (Exception) { }
            var p = h.PrepareShare(id => titles.TryGetValue(id, out var t) ? t : null, priv.Where(x => !string.IsNullOrEmpty(x)));
            var sp = SectionHead("Share Your Design", "Save a copy of your design that you can give to someone else. Your own design file stays as it is.");
            if (savedTo != null) { sp.Children.Add(Note("The shared copy was saved and checked. It is ready to give to someone.", "ok")); sp.Children.Add(PathText(savedTo)); }
            var c = new StackPanel();
            c.Children.Add(T("The shared copy holds", Ty.Body, true, false, new Thickness(0, 0, 0, 4))); foreach (var x in p.Included) c.Children.Add(NoTerms(T("• " + x, Ty.Text)));
            if (p.Removed.Count > 0) { c.Children.Add(T("Left out of the shared copy", Ty.Body, true, false, new Thickness(0, 8, 0, 4))); foreach (var x in p.Removed) c.Children.Add(NoTerms(T("• " + x, Ty.Text))); }
            var plainRefusals = p.Refusals.Select(x => x.StartsWith("This HOME design was refused", StringComparison.Ordinal) ? "Something in the design cannot be shared yet." : x).Distinct().ToList();
            foreach (var x in plainRefusals) c.Children.Add(Note(x, "bad"));
            if (p.Refusals.Any(x => x.StartsWith("This HOME design was refused", StringComparison.Ordinal))) c.Children.Add(Details("Technical details", p.Refusals.Cast<object>().ToArray()));
            c.Children.Add(Note("Share only pictures you have the right to share.", "warn"));
            sp.Children.Add(Card(c));
            sp.Children.Add(Buttons(p.CanShare ? B(savedTo == null ? "Save the Shared Copy…" : "Save Another Shared Copy…", savedTo == null ? "Btn.Primary" : "Btn", () =>
            {
                var d = new Microsoft.Win32.SaveFileDialog { Title = "Save a Shared Copy (Shared HOME Design)", Filter = DesignFilter, FileName = SafeFileName(h.Name ?? "My HOME") + " (shared)" + DesignProfile.Extension, OverwritePrompt = true };
                if (d.ShowDialog(this) != true) return;
                if (SaveSharedCopy(p, d.FileName)) ShowShare(d.FileName);
            }) : null, B("Back to the Design", savedTo == null ? "Btn.Ghost" : "Btn.Primary", () => ShowDesignEditor())));
            if (!p.CanShare) sp.Children.Add(T("A shared copy can be saved once the problems above are fixed in the design.", Ty.Small, false, true));
            Show(sp, "design-share");
        }

        /// <summary>the shared copy written to the file chosen, then checked as a received file would be (ShareGuard); a copy that does not
        /// pass is not kept. False: a notice said why.</summary>
        bool SaveSharedCopy(DesignSharePreview p, string file)
        {
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                p.Shared.Save(file, overwrite: true);
                try { ShareGuard.RequireShareable(file); } catch (ComposeException) { File.Delete(file); throw; }
                return true;
            }
            catch (Exception ex) when (ex is ComposeException || ex is IOException || ex is UnauthorizedAccessException)
            {
                Mouse.OverrideCursor = null;
                Tell("The shared copy was not saved", (ex is ComposeException ? "It did not pass the check for sharing, so it was not kept." : "It could not be saved there. Choose another folder, or try again.") + "\n\n" + ex.Message);
                return false;
            }
            finally { Mouse.OverrideCursor = null; }
        }

        // ------------------------------------------------------------------ small parts
        /// <summary>a built-in startup picture's display name ("forgotten-keep" → "Forgotten Keep")</summary>
        static string StartupName(string id) => string.Join(" ", (id ?? "").Split('-').Where(w => w.Length > 0).Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1)));
        /// <summary>a baked HOME font's name and size ("sans400_20" → "Barlow, 20 px"; the families as native_home/derive.py FAMILY_LABEL names them)</summary>
        static string FontName(string id)
        {
            var m = System.Text.RegularExpressions.Regex.Match(id ?? "", @"^([a-z]+?)(\d{3})?_?(\d+)(b?)$");
            if (!m.Success) return id;
            string fam = m.Groups[1].Value, w = m.Groups[2].Value, px = m.Groups[3].Value, alt = m.Groups[4].Value;
            string name = fam == "silk" ? "Silkscreen" : fam == "silkh" || fam == "silkt" ? "Silkscreen, capitals" : fam == "sans" ? "Barlow" : fam == "cin" ? "Cinzel" : fam == "vt" ? "VT323" : fam == "px" ? "Press Start 2P" : fam == "mono" ? "Space Mono" : fam;
            string weight = w == "600" ? " Semibold" : w == "700" ? " Bold" : w == "800" ? " Extra Bold" : "";
            return name + weight + ", " + px + " px" + (alt == "b" ? " (second style)" : "");
        }
        static string ThemeName(string t) => DesignProfile.ThemeName(t);
        static string Null(string s) { s = s?.Trim(); return string.IsNullOrEmpty(s) ? null : s; }
        static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
        /// <summary>a button beside fields, level with their inputs (not stretched to the labels' height)</summary>
        static Button BottomAligned(Button b) { b.VerticalAlignment = VerticalAlignment.Bottom; b.Margin = new Thickness(0, 0, 0, 10); b.MinHeight = 34; return b; }
        Button Small(string text, Action click, bool enabled = true, string name = null)
        {
            var b = B(text, "Btn.Ghost", click, enabled); b.MinWidth = 0; b.MinHeight = 30; b.Padding = new Thickness(10, 4, 10, 4); b.Margin = new Thickness(0, 4, 6, 0);
            if (name != null) AutomationProperties.SetName(b, name);
            return b;
        }
        /// <summary>a short heading that opens and closes the controls under it (closed at first unless open)</summary>
        FrameworkElement Disclosure(string title, UIElement body, bool open)
        {
            var host = new Border { Child = body, Margin = new Thickness(24, 2, 0, 6), Visibility = open ? Visibility.Visible : Visibility.Collapsed };
            var glyph = new TextBlock { Text = open ? "" : "", FontFamily = Res<FontFamily>("F.Icons"), FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 10, 0), Foreground = Res<Brush>("B.Muted") };
            var head = new ToggleButton { Style = Res<Style>("Wb.Disclosure"), IsChecked = open, Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { glyph, new TextBlock { Text = title, FontSize = Ty.Text, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center } } } };
            AutomationProperties.SetName(head, title);
            head.Checked += (s, e) => { host.Visibility = Visibility.Visible; glyph.Text = ""; };
            head.Unchecked += (s, e) => { host.Visibility = Visibility.Collapsed; glyph.Text = ""; };
            var sp = new StackPanel { Margin = new Thickness(-6, 2, 0, 2) }; sp.Children.Add(head); sp.Children.Add(host);
            return sp;
        }
        UIElement PictureFrame(UIElement pic) => new Border { BorderBrush = Res<Brush>("B.Line2"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Left, Child = pic };
        static UIElement Thumb(byte[] b, int w, int h)
        {
            try { var bi = new BitmapImage(); bi.BeginInit(); bi.StreamSource = new MemoryStream(b); bi.DecodePixelWidth = w * 2; bi.CacheOption = BitmapCacheOption.OnLoad; bi.EndInit(); bi.Freeze(); return new Image { Source = bi, MaxWidth = w, MaxHeight = h, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left }; }
            catch (Exception) { return new TextBlock { Text = "(this picture cannot be shown)" }; }
        }
    }
}
