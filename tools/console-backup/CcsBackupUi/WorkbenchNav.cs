// Classic Cart Studio console backup and restore window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// CCS Console Workbench: getting around the window, the running console task, the console's friendly name and Back Up Console's backup name.
//   Back and Forward  two arrows at the left of the console strip (the top of the page column), and Alt+Left / Alt+Right and a mouse's back
//                     and forward buttons: the pages opened before and after, each as it was left (its view, chosen items, search, folder
//                     and place on the page where the page keeps them). A page is a destination only when it can be opened again by
//                     itself: a section's page, a task's starting page, Settings, Help, an open backup. A task's progress, a review, a
//                     result, a message and a dialog never are, so going back or forward never starts, confirms or repeats anything, never
//                     brings back an earlier session's console and never opens a console session that has ended. An arrow is dimmed only
//                     when there is no page that way. The guided pages' own Next and Back are separate.
//   The running task  one console task at a time belongs to the window, not to a page: the side column, the arrows and every link stay
//                     usable while it runs, and opening another page never cancels, restarts or repeats it. While it runs (or waits for
//                     an answer, or has ended without being seen) the status strip says so with Return to Task, which brings back its
//                     page exactly as it is now: its progress, its question, the live files or its result. Another console task is
//                     refused where it would start, with Return to Task. A console session ends only by the person's own choice on the
//                     task's page (Cancel, Done, End the Session, Restart the Console) or by closing the window (which asks first).
//   Leaving a page    asked only when leaving would really discard something: what was typed or chosen and not used yet, named exactly.
//                     A guided setup's choices: Stay Here / Leave Setup; other unsaved entries: Stay Here / Leave Without Saving (with
//                     Save and Leave where saving is possible). A HOME design, settings in use and a running task stay as they are.
//   Console names     the console a session identified is shown by its friendly name and its short tag ("NES Classic (1) · Console
//                     73f23039"); the pencil beside it (Rename Console) gives it a name of its own. A name is a label on this PC only
//                     (CcsBackup.Library.ConsoleNameBook): never an identity, and kept with the settings under their saving rules (started
//                     by Classic Cart Studio: saved at once; the standalone: with Save Settings). A simulated console's name is kept in
//                     memory only.
//   Backup name       Back Up Console asks for the backup's name once the console is identified and before anything is read, in the same
//                     session: the console's friendly name and the date and time, accepted with Enter or Start Backup without typing.
using CcsBackup.Archive;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CcsBackup.Ui
{
    public sealed partial class BackupWorkspace
    {
        // ------------------------------------------------------------------ Back and Forward
        sealed class Visit
        {
            public string Tag, Section, Title, Key; public Action Restore; public double Scroll; public FrameworkElement Page;
            public Func<object> Capture; public Action<object> Apply; public object State;
        }
        readonly List<Visit> visits = new List<Visit>(); int visitAt = -1; Visit shownVisit, restoring, reopening;
        /// <summary>the place whose state was kept for the departure under way (kept once, before the next page's builder changes what the
        /// window holds, such as the open backup's search when the backup list opens)</summary>
        Visit rememberedFor;
        /// <summary>above zero while the window itself moves to another page (a side column entry, an arrow, Return to Task): a page shown
        /// then is never the console task's</summary>
        int navigating;
        const int MaxVisits = 60;
        Button backArrow, forwardArrow;

        /// <summary>the page shown now as a place in the history, or null when it is not one (a task's page, a message, a result)</summary>
        Visit VisitFor(FrameworkElement e, string tag)
        {
            Visit V(Action restore, string key = null) => new Visit { Tag = tag, Section = section, Title = TitleOf(e) ?? section, Restore = restore, Key = key ?? tag };
            switch (tag)
            {
                case "dashboard": return V(ShowDashboard);
                case "backup-home": return V(ShowBackupHome);
                case "backup": return V(ShowBackup);
                case "restore-hub": return V(ShowRestoreHub);
                case "restore-pick": return V(ShowRestorePick);
                case "prepare": return V(ShowPrepare);
                case "return": return V(ShowReturn);
                case "methods": return V(ShowMethods);
                case "firmware": return V(ShowFirmware);
                case "rescue": return V(ShowRescue);
                case "composer": return V(ShowComposer);
                case "composer-plan": { var k = composeKind; return V(() => ShowComposerPlan(k == "migrate" ? "like-my-other-console" : "rebuild-from-backups"), tag + ":" + k); }
                case "community": { var v = V(() => ShowCommunity()); v.Capture = () => commView; v.Apply = o => commView = o as string ?? commView; return v; }
                case "designer": return V(ShowDesigner);
                case "design-editor": { var v = V(ShowDesignEditor); v.Capture = () => designStep; v.Apply = o => { if (o is int k) designStep = k; }; return v; }
                case "console-browser": return V(ShowConsoleBrowser);
                case "files-intro": { var sc = filesScope; return V(() => ShowFilesIntro(sc), tag + ":" + sc); }
                case "backup-browser": return V(() => ShowBackupBrowser());
                case "backup-repair": return V(() => ShowBackupRepair());
                case "storage-home": return V(ShowStorageHome);
                case "developer": return V(ShowDeveloper);
                case "test-boot": return V(ShowTestBootIntro);
                case "settings": return V(ShowSettings);
                case "help": { var v = V(ShowHelp); v.Capture = () => helpTopic; v.Apply = o => helpTopic = o as string ?? helpTopic; return v; }
                case "credits": return V(ShowCredits);
                case "ccs-intro": return V(ShowFullApp);
                case "browse":
                case "browse-storage":
                    {
                        var c = browsing; if (c == null || c.Live) return null;   // the console's files read live belong to its session: Return to Task
                        string path = c.Path; bool salvage = c.Salvage; var opened = c;
                        var v = new Visit { Tag = "browse", Section = section, Title = TitleOf(e) ?? "Backup Browser", Key = "browse:" + path + ":" + salvage };
                        v.Capture = () => new BrowseState { View = browseView, Category = browseCat, Search = browseSearch, Selected = ReferenceEquals(browsing, opened) ? browseSel.Entries(opened).Select(x => x.Id).ToList() : null, Open = ReferenceEquals(browsing, opened) ? browseOpen.ToList() : null };
                        v.Restore = () =>
                        {
                            if (ReferenceEquals(browsing, opened)) { ApplyBrowse(v.State as BrowseState); ShowBrowse(); return; }
                            reopening = v; OpenBrowse(path, salvage);   // closed since: opened again (checked again first, as every opening is)
                        };
                        return v;
                    }
            }
            return null;
        }

        sealed class BrowseState { public string View, Search; public CcsBackup.Browse.Category Category; public List<string> Selected, Open; }
        void ApplyBrowse(BrowseState st)
        {
            if (st == null || browsing == null) return;
            browseView = st.View ?? browseView; browseCat = st.Category; browseSearch = st.Search ?? "";
            if (st.Selected != null) { browseSel.Clear(); foreach (var id in st.Selected) browseSel.Add(browsing.Find(id)); }
            if (st.Open != null) { browseOpen.Clear(); foreach (var k in st.Open) browseOpen.Add(k); }
        }
        /// <summary>a backup opened again by Back or Forward (OpenBrowse): its view, search and chosen files as they were</summary>
        internal void ShowBrowseOpened(string path)
        {
            var v = reopening; reopening = null;
            if (v == null || v.Key != "browse:" + path + ":" + (browsing?.Salvage ?? false)) { ShowBrowse(); return; }
            ApplyBrowse(v.State as BrowseState);
            restoring = v; try { ShowBrowse(); } finally { restoring = null; }
        }

        /// <summary>a page's own title (its large heading), for the arrows' tooltips</summary>
        string TitleOf(FrameworkElement e)
        {
            string found = null;
            void Walk(object o) { if (found != null) return; if (o is TextBlock tb && tb.FontSize >= 21.5 && !string.IsNullOrEmpty(tb.Text)) { found = tb.Text; return; } if (o is DependencyObject d) foreach (var k in LogicalTreeHelper.GetChildren(d)) Walk(k); }
            try { Walk(e); } catch (Exception) { }
            return found;
        }

        /// <summary>the page being left: its history place keeps where it was scrolled to and what it holds</summary>
        void RememberShown()
        {
            var v = shownVisit; if (v == null || v.Page == null || page.Content != v.Page) return;   // never a page shown while it was being opened again
            if (ReferenceEquals(rememberedFor, v)) return;   // already kept for this departure
            rememberedFor = v;
            if (scroller != null) v.Scroll = scroller.VerticalOffset;
            try { if (v.Capture != null) v.State = v.Capture(); } catch (Exception) { }
        }

        /// <summary>after a page is shown: its place in the history (a new place drops the pages ahead, as in a web browser; the same page
        /// shown again keeps its place), or none</summary>
        void NoteShown(FrameworkElement e, string tag)
        {
            rememberedFor = null;   // a page is in front: its own state is kept when it is left
            if (restoring != null)
            {
                var r = restoring; int at = visits.IndexOf(r); if (at >= 0) visitAt = at;
                shownVisit = r; r.Section = section;
                if (tag == "progress") { r.Page = null; UpdateArrows(); return; }   // it is being opened again (an open backup is checked first)
                r.Page = e; if (r.Title == null || r.Title == section) r.Title = TitleOf(e) ?? r.Title;
                double y = r.Scroll;
                Dispatcher.BeginInvoke(new Action(() => { if (page.Content == e) scroller?.ScrollToVerticalOffset(y); }), System.Windows.Threading.DispatcherPriority.Loaded);
                UpdateArrows(); return;
            }
            bool reopen = reopening != null; if (tag != "progress") reopening = null;
            var v = ReferenceEquals(e, taskPage) ? null : VisitFor(e, tag);
            if (v == null) { shownVisit = null; if (navigating == 0 && !reopen && !ReferenceEquals(e, taskPage)) DropAhead(); UpdateArrows(); return; }
            v.Page = e;
            if (visitAt >= 0 && visitAt < visits.Count && visits[visitAt].Key == v.Key) { visits[visitAt] = v; }   // the same page again (a refresh, another view of it)
            else { DropAhead(); visits.Add(v); if (visits.Count > MaxVisits) visits.RemoveAt(0); visitAt = visits.Count - 1; }
            shownVisit = v; UpdateArrows();
        }
        void DropAhead() { if (visitAt + 1 < visits.Count) visits.RemoveRange(visitAt + 1, visits.Count - visitAt - 1); }

        int BackTarget => shownVisit != null ? visitAt - 1 : visitAt;
        int ForwardTarget => visitAt + 1;
        bool CanBack => BackTarget >= 0 && BackTarget < visits.Count;
        bool CanForward => ForwardTarget >= 0 && ForwardTarget < visits.Count;

        /// <summary>Back (-1) or Forward (+1): the page there, rebuilt as it was left. The open page's unsaved work is asked about first.
        /// Nothing is started, confirmed or repeated: only pages that open by themselves are in the history.</summary>
        internal bool GoHistory(int way)
        {
            int to = way < 0 ? BackTarget : ForwardTarget;
            if (to < 0 || to >= visits.Count) return false;
            if (!LeaveOk()) return false;
            bool keyboardInPage = Keyboard.FocusedElement is DependencyObject f && page.Content is DependencyObject pc && IsInside(f, pc);
            RememberShown();
            var v = visits[to];
            navigating++; restoring = v;
            try { if (v.Apply != null) { try { v.Apply(v.State); } catch (Exception) { } } Enter(v.Section ?? section); v.Restore(); }
            finally { restoring = null; navigating--; }
            if (page.Content != null && visits.IndexOf(v) >= 0 && reopening == null) visitAt = visits.IndexOf(v);
            UpdateArrows(); UpdateChrome();
            if (keyboardInPage) (way < 0 ? (CanBack ? backArrow : forwardArrow) : (CanForward ? forwardArrow : backArrow))?.Focus();
            return true;
        }
        static bool IsInside(DependencyObject d, DependencyObject root) { for (var p = d; p != null; p = p is Visual || p is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(p) : LogicalTreeHelper.GetParent(p)) if (p == root) return true; return false; }

        /// <summary>the two arrows at the left of the console strip</summary>
        UIElement HistoryArrows()
        {
            Button Arrow(string glyph, string name, string keys, int way)
            {
                var g = new TextBlock { Text = glyph, FontFamily = Res<FontFamily>("F.Icons") ?? new FontFamily("Segoe MDL2 Assets"), FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var b = new Button { Content = g, Style = Res<Style>("Btn.Ghost"), Width = 34, Height = 34, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
                AutomationProperties.SetName(b, name); AutomationProperties.SetAcceleratorKey(b, keys); ToolTipService.SetShowOnDisabled(b, true);
                b.Click += (s, e) => GoHistory(way);
                return b;
            }
            backArrow = Arrow("", "Back", "Alt+Left", -1); forwardArrow = Arrow("", "Forward", "Alt+Right", 1);
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(backArrow); sp.Children.Add(forwardArrow);
            AutomationProperties.SetName(sp, "Page history");
            UpdateArrows();
            return sp;
        }

        void UpdateArrows()
        {
            if (backArrow == null) return;
            void Set(Button b, bool on, int to, string way, string keys)
            {
                b.IsEnabled = on;
                var title = on ? visits[to].Title : null;
                b.ToolTip = on ? way + " to " + title + " (" + keys + ")" : (way == "Back" ? "No earlier page" : "No later page");
                AutomationProperties.SetHelpText(b, on ? way + " to " + title : (way == "Back" ? "No earlier page" : "No later page"));
            }
            Set(backArrow, CanBack, BackTarget, "Back", "Alt+Left"); Set(forwardArrow, CanForward, ForwardTarget, "Forward", "Alt+Right");
        }

        /// <summary>a key pressed in the window: Alt+Left is Back and Alt+Right is Forward (true: it was one of them)</summary>
        internal bool HistoryKey(Key k, ModifierKeys mods)
        {
            if (mods != ModifierKeys.Alt || (k != Key.Left && k != Key.Right)) return false;
            GoHistory(k == Key.Left ? -1 : 1); return true;
        }

        /// <summary>Alt+Left / Alt+Right and a mouse's back and forward buttons, anywhere in the window (after the page's own use of them:
        /// Help's topic history on the Help page)</summary>
        void HookHistoryKeys()
        {
            AddHandler(KeyDownEvent, new KeyEventHandler((s, e) => { if (HistoryKey(e.Key == Key.System ? e.SystemKey : e.Key, Keyboard.Modifiers)) e.Handled = true; }));
            AddHandler(MouseDownEvent, new MouseButtonEventHandler((s, e) =>
            {
                if (e.ChangedButton != MouseButton.XButton1 && e.ChangedButton != MouseButton.XButton2) return;
                GoHistory(e.ChangedButton == MouseButton.XButton1 ? -1 : 1); e.Handled = true;
            }));
        }

        // ------------------------------------------------------------------ the running task
        /// <summary>the console task's page now (its progress, its question, the live files or its result), shown or not; its section and
        /// title; whether it changed while another page was in front (taskUnseen)</summary>
        FrameworkElement taskPage; string taskSection, taskTitle; bool taskUnseen; double taskScroll;
        /// <summary>above zero while a worker event or the worker's end is being handled: a page shown then is the task's</summary>
        int workerDispatch;
        readonly ConditionalWeakTable<FrameworkElement, ProgressView> progressViews = new ConditionalWeakTable<FrameworkElement, ProgressView>();
        Button returnButton;

        /// <summary>the task's session is open and not ending</summary>
        bool TaskAlive => worker != null && worker.Running && !quitting;
        /// <summary>the task's page is not the one in front</summary>
        bool TaskAway => taskPage != null && page.Content != taskPage;

        /// <summary>whether a page being shown is the console task's: one a worker event (or the worker's end) shows, or a step the person
        /// takes on the task's own page while its session is open. Never a page the window moves to by itself (navigating).</summary>
        bool IsTaskStep()
        {
            if (navigating > 0) return false;
            if (workerDispatch > 0) return true;
            return TaskAlive && taskPage != null && page.Content == taskPage;
        }

        /// <summary>a page of the task becomes its page now: the previous one's clock stops (that step is over), a progress page shown again
        /// starts its clock for its new step</summary>
        void SetTaskPage(FrameworkElement e)
        {
            if (ReferenceEquals(taskPage, e)) return;
            if (taskPage != null && progressViews.TryGetValue(taskPage, out var old)) old.Stop();
            taskPage = e;
            if (e != null && progressViews.TryGetValue(e, out var pv) && !pv.Running) pv.Restart();
        }

        /// <summary>Show's first step: a page of the console task while another page is in front is kept for Return to Task, not shown
        /// (true); the page in front, its unsaved work and the side column stay as they are</summary>
        bool KeepTaskPage(FrameworkElement e)
        {
            if (!IsTaskStep()) return false;
            if (TaskAway) { SetTaskPage(e); taskUnseen = true; taskScroll = 0; NewTermScope(e); UpdateStatus(null); return true; }
            SetTaskPage(e); return false;
        }

        /// <summary>a new console session's page: the one shown now (its progress)</summary>
        void BeginTask(string op)
        {
            if (taskPage != null && progressViews.TryGetValue(taskPage, out var old) && !ReferenceEquals(taskPage, page.Content)) old.Stop();
            taskPage = null; SetTaskPage(page.Content as FrameworkElement);
            taskSection = section; taskUnseen = false; taskTitle = OpTitle(op);
        }

        /// <summary>a console task's name in the window's words</summary>
        string OpTitle(string op)
        {
            switch (op)
            {
                case "backup": return "Back Up Console";
                case "restore": return "Restore a Backup";
                case "prepare": return "Prepare Console";
                case "return": return "Return to Original Software";
                case "browse": return "The console check-up";
                case "rescue": return "Console Rescue";
                case "compose": return "Console Composer";
                case "home-design": return "HOME Designer";
                case "method": return "Add-ons and Startup";
                case "firmware": return "Your System Images";
                case "test-boot": return "Developer Mode's test start";
                case "community": return "Community Mods";
                case "sega-pictures": return CcsBackup.Browse.SegaPictures.Title;
                case "files": return filesScope == "dev" ? "Developer Mode" : "Expert Mode";
                default: return "The console task";
            }
        }

        /// <summary>the page in front is left for another one while the task's page was in front: where it was scrolled to is kept, and the
        /// console's files read live (Console Browser) are kept apart with their view, so a backup opened meanwhile never closes them</summary>
        void NoteLeavingTask()
        {
            if (taskPage == null || page.Content != taskPage) return;
            if (scroller != null) taskScroll = scroller.VerticalOffset;
            if (browsing?.Live == true)
            {
                liveStash = new LiveStash { Contents = browsing, State = new BrowseState { View = browseView, Category = browseCat, Search = browseSearch, Selected = browseSel.Entries(browsing).Select(x => x.Id).ToList(), Open = browseOpen.ToList() } };
                browsing = null; browseSel.Clear(); browseOpen.Clear(); browseCurrent = null; browseDetails = null;
            }
        }

        /// <summary>the console's files read live while their page is not in front: kept for Return to Task (Pending: read after the person
        /// left, so their page is drawn on return)</summary>
        sealed class LiveStash { public CcsBackup.Browse.BackupContents Contents; public BrowseState State; public bool Pending; }
        LiveStash liveStash;

        /// <summary>back to the task's page: the console's files read live are the open ones again (a backup opened meanwhile is closed; its
        /// place in Back and Forward opens it again when needed)</summary>
        void TakeLiveStash()
        {
            var st = liveStash; if (st == null) return; liveStash = null;
            if (browsing != null && !ReferenceEquals(browsing, st.Contents)) browsing.Dispose();
            browsing = st.Contents; ApplyBrowse(st.State);
            if (st.Pending) ShowBrowse();   // read after the person left: drawn now, as the task's page
        }

        /// <summary>the session ends: the console's files kept apart are closed</summary>
        void DropLiveStash() { var st = liveStash; liveStash = null; st?.Contents?.Dispose(); }

        /// <summary>the console's files finished reading after the person left the task's page</summary>
        void KeepLiveAway(CcsBackup.Browse.BackupContents c)
        {
            DropLiveStash();
            liveStash = new LiveStash { Contents = c, State = new BrowseState { View = "game", Category = CcsBackup.Browse.Category.Games, Search = "" }, Pending = true };
            if (taskPage != null && progressViews.TryGetValue(taskPage, out var pv)) pv.Stop();
            taskUnseen = true; UpdateStatus(null);
        }

        /// <summary>an in-session step on this PC (the console's files being read or copied) finished while another page is in front: its
        /// result becomes the task's page, kept for Return to Task (true), instead of replacing the page in front</summary>
        bool TaskStepAway(FrameworkElement stepPage, Action show)
        {
            if (!ReferenceEquals(taskPage, stepPage) || page.Content == stepPage || !TaskAlive) return false;
            workerDispatch++; try { show(); } finally { workerDispatch--; }
            return true;
        }

        /// <summary>Return to Task: the task's page as it is now (its progress, its question, the live files or its result); the open
        /// page's unsaved work is asked about first</summary>
        internal void ReturnToTask()
        {
            var t = taskPage; if (t == null || page.Content == t) return;
            if (!LeaveOk()) return;
            RememberShown();
            navigating++;
            try
            {
                CloseTerm(false); page.Content = t; leaveCheck = null; pageUnsaved = null; settingsPending = null; settingsRefresh = null;
                if (taskSection != null) Enter(taskSection);
                shownVisit = null; taskUnseen = false;
                var y = taskScroll; Dispatcher.BeginInvoke(new Action(() => { if (page.Content == t) scroller?.ScrollToVerticalOffset(y); }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            finally { navigating--; }
            TakeLiveStash();   // the console's files read live, open again (their page is the task's)
            UpdateArrows(); UpdateChrome();
            Dispatcher.BeginInvoke(new Action(() => { if (page.Content == t) (t as UIElement)?.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)); }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>the status strip's Return to Task button (shown only while it can bring something back)</summary>
        Button ReturnButton()
        {
            returnButton = new Button { Content = "Return to Task", Style = Res<Style>("Btn.Primary"), MinWidth = 0, MinHeight = 30, Padding = new Thickness(14, 3, 14, 4), Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
            AutomationProperties.SetName(returnButton, "Return to Task");
            returnButton.Click += (s, e) => ReturnToTask();
            return returnButton;
        }

        /// <summary>the status strip's words about the task while another page is in front (null: nothing to say), and whether Return to
        /// Task shows something</summary>
        string TaskStatusLine(out bool canReturn)
        {
            canReturn = false;
            if (!TaskAway) return null;
            var title = taskTitle ?? "The console task";
            if (TaskAlive && !gotResult)
            {
                canReturn = true;
                bool waiting = !progressViews.TryGetValue(taskPage, out _);
                if (waiting && (workerOp == "browse" || workerOp == "files")) return title + " is open: the console waits in its memory program. Return to Task to go on, or to end the session.";
                return waiting ? title + " is waiting for you on its page. Nothing more happens on the console until you answer."
                     : writing ? title + " is writing to the console. Keep the USB cable connected and the PC on until it finishes; you can keep using Workbench."
                     : title + " is still running. You can keep using Workbench while it does.";
            }
            if (TaskAlive) { canReturn = true; return title + " has finished. Its console session is still open: the console waits in its memory program until you end it on the task's page."; }
            if (taskUnseen) { canReturn = true; return title + " has ended. Return to Task shows what happened."; }
            return null;
        }

        /// <summary>every console task asks this before it starts: this window's own task, if one is open, is never replaced or repeated by
        /// another one. The page says which task is open, with Return to Task (and, for a session whose task has finished, a way to end
        /// it). True when nothing of this window's is open.</summary>
        bool OwnTaskAllows(string starting)
        {
            if (!TaskAlive) return true;
            var title = taskTitle ?? "A console task";
            var sp = PageHead(gotResult ? "A console session is still open" : "A console task is running",
                gotResult ? title + " has finished, but its console session is still open: the console waits in its memory program. Only one console session runs at a time, so " + starting + " cannot start now."
                          : title + " is still running in this window. Only one console task runs at a time, so " + starting + " cannot start now. Nothing was started and nothing changed.");
            sp.Children.Add(Note(gotResult ? "End that session first (nothing is written to the console when it ends), then start " + starting + " again with the console in recovery mode."
                                           : "Return to the task to see its progress, or wait until it has finished, then start " + starting + " again.", "info"));
            var back = B("Back", "Btn.Ghost", () => { if (!GoHistory(-1)) BackToSection(); });
            sp.Children.Add(Buttons(B("Return to Task", "Btn.Primary", ReturnToTask),
                gotResult && !writing ? B("End That Session", "Btn", () => { EndOpenSession(); if (!GoHistory(-1)) BackToSection(); }) : null, back));
            Show(sp, "msg");
            return false;
        }

        /// <summary>ends this window's console session whose task has finished (the person's choice): the worker is told to quit; the
        /// console stays in its memory program until it is switched off</summary>
        void EndOpenSession()
        {
            var w = worker; if (w == null || !w.Running) return;
            quitting = true; w.Send(new JObj { { "cmd", "quit" } });
            UpdateStatus("The console session has ended. Switch the console off before you start another console task.");
        }

        // ------------------------------------------------------------------ console names
        /// <summary>a simulated console's names: in memory only, never in the settings</summary>
        readonly ConsoleNameBook simulatedNames = new ConsoleNameBook();
        ConsoleNameBook NamesFor(bool simulated) => simulated ? simulatedNames : settings.ConsoleNames;

        /// <summary>the friendly-name entry of a console a session identified (its number given the first time; the settings note it)</summary>
        ConsoleNameBook.Entry NameOf(ConsoleSeen s, bool note)
        {
            if (s?.Key == null || !ConsoleNameBook.IsKey(s.Key)) return null;
            var book = NamesFor(s.Simulated);
            if (!note) return book.Find(s.Key);
            var e = book.Note(s.Key, s.Model ?? "unknown", out bool added);
            if (added && !s.Simulated) PrefsChanged();   // started by Classic Cart Studio: saved now; the standalone: with its settings
            return e;
        }

        /// <summary>"NES Classic (1)", or a name of its own with the console's kind: "Living Room (NES Classic)"</summary>
        string FriendlyName(ConsoleSeen s)
        {
            var e = NameOf(s, false); if (e == null) return null;
            return e.Custom ? e.Display + " (" + e.ModelShort + ")" : e.Display;
        }

        /// <summary>Rename Console: a name of the person's own for the console a session identified (empty: the usual name). Only a label:
        /// nothing about the console, its backups or how Workbench recognizes it changes.</summary>
        internal bool RenameConsole(ConsoleSeen s, string name, out string problem)
        {
            problem = null;
            var e = NameOf(s, true); if (e == null) { problem = "Workbench has not identified this console in this window."; return false; }
            var clean = ConsoleNameBook.CleanName(name);
            if (clean.Length > 0 && ConsoleNameBook.NameProblem(clean) is string why) { problem = why; return false; }
            if ((e.Name ?? "") == clean) return true;
            NamesFor(s.Simulated).Rename(s.Key, clean);
            if (!s.Simulated) PrefsChanged();
            UpdateConsoleStrip();
            return true;
        }

        /// <summary>the pencil beside the console's name</summary>
        Button RenameButton()
        {
            var g = new TextBlock { Text = "", FontFamily = Res<FontFamily>("F.Icons") ?? new FontFamily("Segoe MDL2 Assets"), FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var b = new Button { Content = g, Style = Res<Style>("Btn.Ghost"), Width = 28, Height = 28, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Rename this console (a name for this PC only)", Visibility = Visibility.Collapsed };
            AutomationProperties.SetName(b, "Rename Console"); AutomationProperties.SetHelpText(b, "Gives the console a name shown on this PC. It does not change the console or its backups.");
            b.Click += (s, e) => { var who = seenNow; if (who == null) return; Modal(RenameWindow(who)); b.Focus(); };
            return b;
        }

        /// <summary>the Rename Console dialog, not shown yet</summary>
        internal Window RenameWindow(ConsoleSeen s)
        {
            var dlg = Owned("Rename Console", 520, false);
            var e = NameOf(s, true);
            var sp = new StackPanel { Margin = new Thickness(24, 20, 24, 16) };
            sp.Children.Add(T("Rename Console", 17, true, false, new Thickness(0, 0, 0, 8)));
            sp.Children.Add(T((e?.ModelShort ?? "Console") + " · " + (s.Simulated ? "Simulated console " : "Console ") + s.Tag + (e != null && !e.Custom ? " · now called " + e.Display : ""), Ty.Small, false, true, new Thickness(0, 0, 0, 10)));
            var label = T("Name", Ty.Body, true, false, new Thickness(0, 0, 0, 4)); sp.Children.Add(label);
            var box = new TextBox { Text = e?.Display ?? "", MaxLength = ConsoleNameBook.MaxName + 20 };
            AutomationProperties.SetName(box, "Console name"); AutomationProperties.SetLabeledBy(box, label);
            sp.Children.Add(box);
            var problem = new ContentControl { Focusable = false }; sp.Children.Add(problem);
            sp.Children.Add(T("The name is shown on this PC only. It never changes the console, its backups or how Workbench recognizes the console. " + (s.Simulated ? "A simulated console's name is kept until Workbench closes." : Standalone ? "Save Settings keeps it for next time." : "It is saved with your settings."), Ty.Small, false, true, new Thickness(0, 8, 0, 10)));
            var row = new WrapPanel();
            var save = B("Save Name", "Btn.Primary", () => { if (RenameConsole(s, box.Text, out var why)) dlg.Close(); else problem.Content = Note(why, "warn"); }); save.IsDefault = true;
            var usual = B("Use the Usual Name", "Btn", () => { if (RenameConsole(s, "", out var why)) dlg.Close(); else problem.Content = Note(why, "warn"); });
            var cancel = B("Cancel", "Btn.Ghost", () => dlg.Close()); cancel.IsCancel = true;
            row.Children.Add(save); if (e?.Custom == true) row.Children.Add(usual); row.Children.Add(cancel);
            box.TextChanged += (o, a) => { var c = ConsoleNameBook.CleanName(box.Text); var why = c.Length > 0 ? ConsoleNameBook.NameProblem(c) : null; problem.Content = why != null ? Note(why, "warn") : null; save.IsEnabled = why == null; };
            sp.Children.Add(row);
            dlg.Content = sp;
            dlg.Loaded += (o, a) => { box.Focus(); box.SelectAll(); };
            return dlg;
        }

        // ------------------------------------------------------------------ Back Up Console's backup name
        /// <summary>the suggested name: the console's friendly name and the date and time ("NES Classic (1) Backup 2026-11-24 1405")</summary>
        internal static string SuggestedBackupName(string friendly, DateTime now) => (string.IsNullOrEmpty(friendly) ? "Console" : friendly) + " Backup " + now.ToString("yyyy-MM-dd HHmm", CultureInfo.InvariantCulture);

        /// <summary>the worker asks for the backup's name: the console is identified and nothing has been read yet</summary>
        void ShowBackupName(Dictionary<string, object> ev)
        {
            bool sim = !string.IsNullOrEmpty(opt.SimulatedConsole);
            var who = seenNow != null && seenNow.Tag == ev.S("console_tag") ? seenNow : new ConsoleSeen { Tag = ev.S("console_tag"), Key = ev.S("console_key"), Model = ev.S("physical_model"), Simulated = sim, At = DateTime.Now };
            var entry = NameOf(who, true);
            var suggested = SuggestedBackupName(entry?.Display ?? ev.S("model"), DateTime.Now);
            var sp = PageHead("Name the Backup", "Workbench has identified your console. Keep the suggested name or type your own, then start the backup. Nothing has been read from the console yet.");
            var c = new StackPanel();
            c.Children.Add(T((FriendlyName(who) is string fn ? fn + "  ·  " : "") + (sim ? "Simulated console " : "Console ") + who.Tag, Ty.Body, true, false, new Thickness(0, 0, 0, 12)));
            var label = T("Backup name", Ty.Body, true, false, new Thickness(0, 0, 0, 4)); c.Children.Add(label);
            var box = new TextBox { Text = suggested, MaxLength = BackupNames.MaxLength + 40 };
            AutomationProperties.SetName(box, "Backup name"); AutomationProperties.SetLabeledBy(box, label);
            c.Children.Add(box);
            var file = T("", Ty.Small, false, true, new Thickness(0, 6, 0, 0)); SetNoTerms(file, true); c.Children.Add(file);
            var problem = new ContentControl { Focusable = false }; c.Children.Add(problem);
            c.Children.Add(T("The name is the backup file's name only. The backup's own record of the console is the same whatever it is called, and renaming the console later does not rename this file." + (string.IsNullOrEmpty(settings.ArchiveFolder) ? "" : " The additional copy gets the same name.") + " If a file with this name is already there, Workbench adds a number; it never replaces a file.", Ty.Small, false, true, new Thickness(0, 10, 0, 0)));
            sp.Children.Add(Card(c));
            Button start = null;
            void Check()
            {
                var why = BackupNames.Problem(box.Text);
                problem.Content = why != null ? Note(why, "warn") : null;
                file.Text = why != null ? "" : "Saved as: " + (sim ? "SIMULATED " : "") + BackupNames.Clean(box.Text) + BackupFormat.Extension;
                if (start != null) start.IsEnabled = why == null;
            }
            start = B("Start Backup", "Btn.Primary", () =>
            {
                if (BackupNames.Problem(box.Text) != null) return;
                worker?.Send(new JObj { { "cmd", "backup-name" }, { "name", box.Text } });
                if (progress != null) { Show(progress.Panel, "progress"); progress.Update("Reading the console...", 0, 0); }
            });
            start.IsDefault = true;   // Enter accepts the suggested name: no typing needed
            var again = B("Use the Suggested Name", "Btn", () => { box.Text = suggested; box.Focus(); box.SelectAll(); });
            var cancel = B("Cancel", "Btn.Ghost", () => { worker?.Cancel(); if (progress != null) { Show(progress.Panel, "progress"); progress.Say("Canceling..."); } });
            box.TextChanged += (s, e) => Check();
            Check();
            sp.Children.Add(Buttons(start, again, cancel));
            Show(sp, "backup-name");
            sp.Loaded += (s, e) => { if (page.Content == sp) start.Focus(); };
        }

        // ------------------------------------------------------------------ leaving a page
        /// <summary>the buttons of the leave question: a guided setup's choices are let go with Leave Setup, other unsaved entries with Leave
        /// Without Saving (or a page's own word, such as Leave Without Adding); Save and Leave first when the page can save (save: true when
        /// it saved); Stay Here is always there and is what Escape and the dialog's X do</summary>
        bool AskToLeave(string question, string leaveWord, Func<bool> save)
        {
            if (save != null)
            {
                int k = Ask("Leave this page?", question, new[] { "Save and Leave", "Leave Without Saving", "Stay Here" }, 2, 2);
                return k == 1 || k == 0 && save();
            }
            return Ask("Leave this page?", question, new[] { "Stay Here", leaveWord ?? "Leave Without Saving" }, 0, 0) == 1;
        }
    }
}
