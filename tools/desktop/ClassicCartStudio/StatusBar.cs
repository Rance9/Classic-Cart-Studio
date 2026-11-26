// The window's status bar: one quiet bar along the bottom of the window, in a row of its own below the page (MainWindow.xaml), so it never
// covers the page, its dialogs or a wizard's buttons. Left: the open collection and its game count, as the page reports them; the middle
// stays empty (progress belongs to the page's own windows); right: this program's running version (App.VersionLabel) and Check for Updates.
//
//   Collection: My Collection · Games: 23                                v0.11.12 (Preview) · Check for Updates
//
// The name has no width limit of its own: the empty middle gives its room to the name first, and the name is shortened (an ellipsis; the full
// name is the item's tooltip and its spoken name) only when "Collection: <name> · Games: N", the normal gap and "v<version> · Check for
// Updates" no longer fit in the bar's width less its side padding, all measured from the real text. Narrower: the bar keeps one row while
// the shortened name can still show a comfortable part of itself (NameComfort); narrower still: two rows, each centered, collection on the
// first (its name again with the bar's whole width before any ellipsis), version and update link on the second. Everything is measured in
// logical pixels, so the window's width, the text and the display's scaling decide it; never a third row, never smaller text, never a
// hidden count or a clipped link.
//
// What the page reports (update.js, "status-bar {json}" over the window's message channel, from this window's own page only): the open
// collection's name and game count (the count the collection itself shows), or none; and whether an update is known. Until this page reports
// (loading, the recovery screen, another page) the bar shows Collection: None · Games: — and the version. The name is shown as text only.
// The link asks the page for the same check as Check for Updates… (window.__ccsAppUpdate): the page's own update messages and dialogs
// answer it (checking, up to date, update available, could not check). Once an update is known the link reads Update Available and opens
// the same Update Available dialog; nothing is downloaded or installed without Update Now.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ClassicCartStudio
{
    /// <summary>the status bar's layout: two children, the collection item (first) and the version with the update link (second)</summary>
    public sealed class StatusBarPanel : Panel
    {
        public const double Row = 34;            // the one-row bar's height (logical pixels)
        public const double Pad = 14;            // left and right
        public const double Gap = 24;            // the normal room kept between the two sides on one row (the approved design's gap)
        // one row while a shortened name still shows at least this much of itself (about 45 letters; all of a shorter name). Below that the
        // two-row layout gives the name the bar's whole width, so long names reach it at the window's least width (720) and show more there
        public const double NameComfort = 340;
        public const double TopPad = 5, BottomPad = 3, Row1 = 20, Row2 = 26;   // the two-row bar: its padding and its rows' least heights

        public static readonly DependencyProperty LineBrushProperty = DependencyProperty.Register("LineBrush", typeof(Brush), typeof(StatusBarPanel), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public Brush LineBrush { get => (Brush)GetValue(LineBrushProperty); set => SetValue(LineBrushProperty, value); }
        /// <summary>the collection name's text: the part that is shortened</summary>
        public FrameworkElement NameElement { get; set; }
        /// <summary>the layout chosen at the last measure: two centered rows</summary>
        public bool TwoRows { get; private set; }

        public StatusBarPanel() { ClipToBounds = true; SnapsToDevicePixels = true; }

        protected override Size MeasureOverride(Size available)
        {
            if (InternalChildren.Count < 2) return new Size(0, Row);
            UIElement left = InternalChildren[0], right = InternalChildren[1];
            var any = new Size(double.PositiveInfinity, double.PositiveInfinity);
            right.Measure(any); left.Measure(any);
            double full = left.DesiredSize.Width, r = right.DesiredSize.Width, name = NameElement?.DesiredSize.Width ?? 0;
            double least = full - name + Math.Min(name, NameComfort);   // the collection side with the name at its comfortable least
            double w = double.IsInfinity(available.Width) ? full + Gap + r + 2 * Pad : available.Width, inner = Math.Max(0, w - 2 * Pad);
            TwoRows = inner < least + Gap + r;
            if (!TwoRows)
            {
                // only the name gives way. A name that fits is offered a pixel more than its width: offered exactly its own width, a rounding
                // error of a fraction of a pixel makes the text think it does not fit and shortens it ("My Collecti…")
                left.Measure(new Size(Math.Max(0, Math.Min(full + 1, inner - Gap - r)), double.PositiveInfinity));
                return new Size(Math.Min(w, full + Gap + r + 2 * Pad), Row);
            }
            left.Measure(new Size(Math.Min(full + 1, inner), double.PositiveInfinity)); right.Measure(new Size(inner, double.PositiveInfinity));
            return new Size(Math.Min(w, Math.Max(left.DesiredSize.Width, right.DesiredSize.Width) + 2 * Pad), TopPad + Math.Max(Row1, left.DesiredSize.Height) + Math.Max(Row2, right.DesiredSize.Height) + BottomPad);
        }

        protected override Size ArrangeOverride(Size final)
        {
            if (InternalChildren.Count < 2) return final;
            UIElement left = InternalChildren[0], right = InternalChildren[1];
            Size l = left.DesiredSize, r = right.DesiredSize;
            // the collection side is arranged a pixel wider than it measured where there is room (the same reason as in MeasureOverride)
            if (!TwoRows)
            {
                double lw = Math.Max(l.Width, Math.Min(l.Width + 1, final.Width - 2 * Pad - Gap - r.Width));
                left.Arrange(new Rect(Pad, (final.Height - l.Height) / 2, lw, l.Height));
                right.Arrange(new Rect(Math.Max(Pad, final.Width - Pad - r.Width), (final.Height - r.Height) / 2, r.Width, r.Height));
                return final;
            }
            double h1 = Math.Max(Row1, l.Height), h2 = Math.Max(Row2, r.Height), w1 = Math.Max(0, Math.Min(l.Width + 1, final.Width - 2 * Pad));
            left.Arrange(new Rect(Math.Max(Pad, (final.Width - w1) / 2), TopPad + (h1 - l.Height) / 2, w1, l.Height));
            right.Arrange(new Rect(Math.Max(Pad, (final.Width - r.Width) / 2), TopPad + h1 + (h2 - r.Height) / 2, r.Width, r.Height));
            return final;
        }

        // the thin line along the top: one device pixel at 100 %, whole device pixels at other scales (never a blurred half pixel)
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            if (LineBrush == null || ActualWidth <= 0) return;
            var k = VisualTreeHelper.GetDpi(this).DpiScaleY; if (k <= 0) k = 1;
            dc.DrawRectangle(LineBrush, null, new Rect(0, 0, ActualWidth, Math.Max(1, Math.Round(k)) / k));
        }

        protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);
        sealed class Peer : FrameworkElementAutomationPeer
        {
            public Peer(StatusBarPanel o) : base(o) { }
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.StatusBar;
            protected override string GetClassNameCore() => "StatusBar";
        }
    }

    /// <summary>the collection item: reachable with Tab (its tooltip, the full name, then shows), read as one piece of text</summary>
    public sealed class StatusItem : Border
    {
        public StatusItem() { Focusable = true; KeyboardNavigation.SetIsTabStop(this, true); }
        protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);
        sealed class Peer : FrameworkElementAutomationPeer
        {
            public Peer(StatusItem o) : base(o) { }
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
            protected override string GetClassNameCore() => "StatusItem";
            protected override bool IsKeyboardFocusableCore() => Owner.Focusable && Owner.IsEnabled;
            protected override List<AutomationPeer> GetChildrenCore() => null;   // its words are its name (no second reading of each part)
        }
    }

    public partial class MainWindow
    {
        static readonly Regex StatusVersion = new Regex(@"^\d{1,5}\.\d{1,5}\.\d{1,5}$");
        string statusUpdate;   // the version of an update the page knows of, or null

        void StatusBarInit()
        {
            statusBar.NameElement = sbName;
            sbVersion.Text = "v" + App.VersionLabel;   // the running program's own version and channel, never a release's
            AutomationProperties.SetName(sbVersion, "Version " + App.VersionLabel);
            StatusReset();
        }

        /// <summary>no page reports: no collection is shown as open (the version and the link stay)</summary>
        void StatusReset() { StatusShow(null, 0); StatusShowUpdate(null); }

        void StatusShow(string name, long games)
        {
            if (name == null)
            {
                sbName.Text = "None"; sbName.Foreground = (Brush)FindResource("SB.Muted"); sbGames.Text = "· Games: —";
                sbCollection.ToolTip = "No collection is open";
                AutomationProperties.SetName(sbCollection, "Collection: None. Games: none.");
            }
            else
            {
                var count = games.ToString("N0", CultureInfo.GetCultureInfo("en-US"));
                sbName.Text = name; sbName.Foreground = (Brush)FindResource("SB.Name"); sbGames.Text = "· Games: " + count;
                sbCollection.ToolTip = name;   // the full name, also when the bar shortens it
                AutomationProperties.SetName(sbCollection, "Collection: " + name + ". Games: " + count + ".");
            }
            // the bar lays itself out again with its whole width: on its own, WPF measures a changed text only within the room the earlier
            // text had (a name that replaced "None" stayed "Inclu…" at any window width)
            statusBar.InvalidateMeasure();
        }

        void StatusShowUpdate(string version)
        {
            statusUpdate = version;
            var label = version == null ? null : version + (App.IsPreview ? " (Preview)" : "");
            sbUpdate.Content = version == null ? "Check for Updates" : "Update Available";
            sbUpdate.ToolTip = version == null ? "Check for a newer Classic Cart Studio now" : "Classic Cart Studio " + label + " is available";
            AutomationProperties.SetName(sbUpdate, version == null ? "Check for Updates" : "Update Available: Classic Cart Studio " + label);
            AutomationProperties.SetHelpText(sbUpdate, version == null ? "Checks for a newer Classic Cart Studio now" : "Opens the update's details");
            statusBar.InvalidateMeasure();   // the link's words changed: the whole bar is laid out again (see StatusShow)
        }

        /// <summary>"status-bar {json}" from this window's own page (OnWebMessage checks the origin): applied only when every part is as expected</summary>
        void StatusFromPage(string json)
        {
            try
            {
                if (json == null || json.Length > 4096) return;
                var o = new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
                if (o == null || !o.ContainsKey("collection") || !o.ContainsKey("update")) return;
                string name = null; long games = 0;
                if (o["collection"] != null)
                {
                    var c = o["collection"] as Dictionary<string, object>;
                    if (c == null || !(c.TryGetValue("name", out var n) && n is string) || !c.TryGetValue("games", out var g) || !(g is int || g is long)) return;
                    games = Convert.ToInt64(g); if (games < 0 || games > 10000000) return;
                    name = Plain((string)n); if (name.Length == 0) name = "My Collection";
                }
                string version = null;
                if (o["update"] != null)
                {
                    var u = o["update"] as Dictionary<string, object>;
                    if (u == null || !(u.TryGetValue("version", out var v) && v is string s && StatusVersion.IsMatch(s))) return;
                    version = (string)u["version"];
                }
                StatusShow(name, games); StatusShowUpdate(version);
            }
            catch (Exception e) { App.Log("status bar: report not used (" + e.GetType().Name + ")"); }
        }

        // a name as one line of text: control characters become spaces, at most 200 characters (the page keeps names to 80)
        static string Plain(string s)
        {
            var b = new StringBuilder(s.Length);
            foreach (var ch in s) b.Append(char.IsControl(ch) ? ' ' : ch);
            var t = Regex.Replace(b.ToString(), @"\s+", " ").Trim();
            return t.Length > 200 ? t.Substring(0, 200) : t;
        }

        // Check for Updates / Update Available: the page's own check and dialogs answer it. While the page is still loading, the check runs once
        // it has loaded (as when CCS Console Workbench asks); on the recovery screen, the download page opens in the browser (as App tools ›
        // Check for Updates does when the window's own helper is not running).
        void StatusUpdate_Click(object sender, RoutedEventArgs e)
        {
            App.Log("status bar: " + (statusUpdate == null ? "check for updates" : "update available"));
            if (coreReady && backend.Ready && WebsiteShown)
            {
                _ = web.CoreWebView2.ExecuteScriptAsync("window.__ccsAppUpdate ? window.__ccsAppUpdate() : (window.__ccsCheckUpdates && window.__ccsCheckUpdates())");
                try { web.Focus(); } catch { }   // the answer (a short message or the Update Available dialog) is on the page: the keyboard goes there too
                return;
            }
            if (!recoveryShown) { pendingUpdateCheck = true; DeliverPendingLink(); return; }
            App.OpenExternal(App.UpdateUrl);
        }
    }
}
