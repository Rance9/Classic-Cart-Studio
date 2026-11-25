// Classic Cart Studio console backup and restore window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// CCS Console Workbench: its one file and folder dialog, used by every Browse, Change, Choose, Locate, Open, Import, Export and Save in
// the Workbench (standalone, and the same program started by Classic Cart Studio). Nothing in the Workbench opens Windows' own file or
// folder dialogs; a page asks this class:
//   ChooseFolder        a folder (a backup location, a folder to back up, where to put a repaired file or a copy)
//   ChooseExportFolder  a folder and the name of a new folder to make inside it (the name filled in, and editable)
//   OpenFiles/OpenFile  one existing file, or several, of the kinds the page takes
//   SaveFile            a file name to save to, with a question before an existing file is replaced (when the page asks for one)
// The dialog: Back, Forward and Up; an editable folder path and the folder's steps (breadcrumbs) to click; Desktop, Documents, Downloads,
// Pictures and this PC's drives at the side; the folder's folders and files with their date, type and size, sorted by any column; a search
// within the folder; the kinds of file the page takes; a name box; New Folder where something is saved or chosen; plain messages; Cancel
// changes nothing. Every part has a name for screen readers and works from the keyboard (Alt+Left/Right/Up, Backspace, F5, Ctrl+L, Ctrl+F,
// Ctrl+Shift+N, Enter, Escape, typing a name jumps to it). It resizes, follows the display's scale and reads long and non-English paths.
// Each page keeps its own rules about what it accepts (a folder that must not be inside the backup, a file it refuses); the dialog only makes
// sure the choice exists (or, for a save, can be made). The folder last used is remembered for the same kind of choice while the program
// runs (nothing is written anywhere for it).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace CcsBackup.Ui
{
    public enum FileDialogMode { Folder, ExportFolder, Open, Save }

    /// <summary>where an export goes: an existing folder, and the name of the new folder to make inside it</summary>
    public sealed class ExportFolderChoice
    {
        public string Folder { get; internal set; }
        public string Name { get; internal set; }
        public string FullPath => System.IO.Path.Combine(Folder, Name);
    }

    /// <summary>what a page asked for (given to a test's answer in place of the dialog)</summary>
    public sealed class FileDialogRequest
    {
        public FileDialogMode Mode; public string Title, InitialFolder, SuggestedName, Filters; public bool Multiple, ConfirmOverwrite, NewFolder = true;
    }

    /// <summary>a place in the dialog's side list or in its This PC view</summary>
    public sealed class FilePlace
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public bool IsDrive { get; set; }
        public bool IsThisPc { get; set; }
        public string Glyph => IsThisPc ? "\uE7F4" : IsDrive ? "\uEDA2" : Name == "Desktop" ? "\uE8FC" : Name == "Downloads" ? "\uE896" : Name == "Pictures" ? "\uE91B" : "\uE8B7";
        public Brush GlyphBrush => IsDrive || IsThisPc ? FileDialogWindow.Muted : FileDialogWindow.FolderBrush;
        public string Spoken => Name + (IsDrive ? ", drive" : IsThisPc ? ", the drives of this PC" : ", folder");
    }

    public static class CcsFileDialog
    {
        /// <summary>a test can answer in place of the dialog: the chosen path(s) (for an export: the folder, then the new folder's name);
        /// null is Cancel</summary>
#pragma warning disable CS0649   // set only by tests
        internal static Func<FileDialogRequest, string[]> Hook;
        /// <summary>a test can give the side list's places and the drives of This PC (nothing on the PC is listed then)</summary>
        internal static Func<IEnumerable<FilePlace>> PlacesHook;
        /// <summary>a test is told of each dialog window once it is shown (a probe can then drive it like a person)</summary>
        internal static Action<Window> ShownHook;
#pragma warning restore CS0649
        static readonly Dictionary<string, string> remembered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>a folder (null: canceled); newFolder: false when a new, empty folder would make no sense (choosing what to back up)</summary>
        public static string ChooseFolder(Window owner, string title, string initialFolder = null, string purpose = null, bool newFolder = true)
            => Run(owner, new FileDialogRequest { Mode = FileDialogMode.Folder, Title = title, InitialFolder = initialFolder, NewFolder = newFolder }, purpose)?.FirstOrDefault();

        /// <summary>a folder and the name of a new folder to make inside it, the name filled in with suggestedSubfolderName (null: canceled).
        /// The name is a valid folder name and nothing of that name is in the folder when it was chosen.</summary>
        public static ExportFolderChoice ChooseExportFolder(Window owner, string title, string initialFolder, string suggestedSubfolderName, string purpose = null)
        {
            var r = Run(owner, new FileDialogRequest { Mode = FileDialogMode.ExportFolder, Title = title, InitialFolder = initialFolder, SuggestedName = suggestedSubfolderName }, purpose);
            return r == null || r.Length < 2 ? null : new ExportFolderChoice { Folder = r[0], Name = r[1] };
        }

        /// <summary>existing files: one, or several when multiple (null: canceled). filters: "Label|*.a;*.b|Label 2|*.*" (null: any file).</summary>
        public static string[] OpenFiles(Window owner, string title, string initialFolder, string filters, bool multiple, string purpose = null, string suggestedName = null)
            => Run(owner, new FileDialogRequest { Mode = FileDialogMode.Open, Title = title, InitialFolder = initialFolder, Filters = filters, Multiple = multiple, SuggestedName = suggestedName }, purpose);

        /// <summary>one existing file (null: canceled); suggestedName: the file name the name box starts with (the file the page expects)</summary>
        public static string OpenFile(Window owner, string title, string initialFolder = null, string filters = null, string purpose = null, string suggestedName = null)
            => OpenFiles(owner, title, initialFolder, filters, false, purpose, suggestedName)?.FirstOrDefault();

        /// <summary>the file to save to (null: canceled). With confirmOverwrite an existing file is replaced only after the person says so;
        /// without it the page itself decides what happens to an existing file (it may refuse it, or review the replacement).</summary>
        public static string SaveFile(Window owner, string title, string initialFolder, string suggestedName, string filters, bool confirmOverwrite = true, string purpose = null)
            => Run(owner, new FileDialogRequest { Mode = FileDialogMode.Save, Title = title, InitialFolder = initialFolder, SuggestedName = suggestedName, Filters = filters, ConfirmOverwrite = confirmOverwrite }, purpose)?.FirstOrDefault();

        static string[] Run(Window owner, FileDialogRequest q, string purpose)
        {
            string key = purpose ?? (q.Mode == FileDialogMode.Save || q.Mode == FileDialogMode.Open ? "file|" + (q.Filters ?? "") : "folder");
            if ((string.IsNullOrWhiteSpace(q.InitialFolder) || !FileDialogWindow.FolderExists(q.InitialFolder)) && remembered.TryGetValue(key, out var last) && FileDialogWindow.FolderExists(last)) q.InitialFolder = last;
            string[] answer;
            if (Hook != null) answer = Hook(q);
            else
            {
                var focus = Keyboard.FocusedElement;
                var w = new FileDialogWindow(owner != null && owner.IsLoaded ? owner : null, q);
                answer = w.ShowModal();
                // the keyboard goes back where it was in the window that opened the dialog
                try { if (owner != null && owner.IsLoaded) { owner.Activate(); if (focus is UIElement u && u.IsVisible && PresentationSource.FromVisual(u) != null) Keyboard.Focus(u); else if (focus is ContentElement ce) Keyboard.Focus(ce); } } catch (Exception) { }
            }
            if (answer != null && answer.Length > 0)
            {
                var folder = q.Mode == FileDialogMode.Folder || q.Mode == FileDialogMode.ExportFolder ? answer[0] : System.IO.Path.GetDirectoryName(answer[0]);
                if (!string.IsNullOrEmpty(folder)) remembered[key] = folder;
            }
            return answer;
        }

        /// <summary>the places at the side: Desktop, Documents, Downloads and Pictures, then This PC and its drives (a test's own, when given)</summary>
        internal static List<FilePlace> Places()
        {
            if (PlacesHook != null) return PlacesHook().ToList();
            var l = new List<FilePlace>();
            void Known(string name, string path) { if (!string.IsNullOrEmpty(path) && FileDialogWindow.FolderExists(path) && !l.Any(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase))) l.Add(new FilePlace { Name = name, Path = path }); }
            Known("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            Known("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            Known("Downloads", KnownFolder(new Guid("374DE290-123F-4565-9164-39C4925E467B")));
            Known("Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
            l.Add(new FilePlace { Name = "This PC", Path = "", IsThisPc = true });
            try
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    if (d.DriveType == DriveType.NoRootDirectory) continue;
                    var letter = d.Name.TrimEnd('\\');
                    string kind = d.DriveType == DriveType.Removable ? "USB or SD drive" : d.DriveType == DriveType.CDRom ? "Disc drive" : d.DriveType == DriveType.Network ? "Network drive" : "Local disk";
                    l.Add(new FilePlace { Name = kind + " (" + letter + ")", Path = d.Name, IsDrive = true });
                }
            }
            catch (Exception) { }
            return l;
        }

        [DllImport("shell32.dll")] static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);
        static string KnownFolder(Guid id)
        {
            try { if (SHGetKnownFolderPath(id, 0, IntPtr.Zero, out var p) == 0) { try { return Marshal.PtrToStringUni(p); } finally { Marshal.FreeCoTaskMem(p); } } }
            catch (Exception) { }
            return null;
        }
    }

    /// <summary>a folder or a file in the dialog's list</summary>
    public sealed class FileRow
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public bool IsFolder { get; set; }
        public bool IsDrive { get; set; }
        public DateTime? Modified { get; set; }
        public long? Bytes { get; set; }
        public string TypeText { get; set; }
        public string Glyph => IsDrive ? "\uEDA2" : IsFolder ? "\uE8B7" : "\uE8A5";
        public Brush GlyphBrush => IsDrive ? FileDialogWindow.Muted : IsFolder ? FileDialogWindow.FolderBrush : FileDialogWindow.Muted;
        public string DateText => Modified?.ToString("MMM d, yyyy  h:mm tt", CultureInfo.GetCultureInfo("en-US")) ?? "";
        public string SizeText => IsFolder || Bytes == null ? "" : CcsBackup.Compose.Units.Size(Bytes.Value);
        public string Spoken => Name + ", " + (IsDrive ? "drive" : IsFolder ? "folder" : TypeText + (Bytes != null ? ", " + SizeText : "")) + (Modified != null ? ", changed " + DateText : "");
    }

    sealed class FileDialogWindow : Window
    {
        internal static readonly Brush FolderBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xE8, 0xB8, 0x4A)));
        internal static readonly Brush Muted = Freeze(new SolidColorBrush(Color.FromRgb(0x9C, 0x9A, 0x95)));
        static Brush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

        readonly FileDialogRequest q;
        readonly List<(string label, string[] patterns)> kinds = new List<(string, string[])>();
        readonly Stack<string> back = new Stack<string>(), forward = new Stack<string>();
        string current;            // the folder shown; "" is This PC
        List<FileRow> all = new List<FileRow>();
        int loadId;
        string sortBy = "Name"; bool sortDown;
        string[] answer;

        Button bBack, bForward, bUp, bRefresh, bNewFolder, bOk, bCancel;
        TextBox address, search, name, subName;
        TextBlock searchHint, status, exportLine;
        WrapPanel crumbs;
        ListBox places;
        ListView list; GridView grid;
        ComboBox kind;
        Border message;
        bool settingName, inPlaces;
        /// <summary>several files: Space or a check box was used, so the arrow keys move the focus only and the files chosen stay chosen</summary>
        bool toggled;
        List<FilePlace> placeList;
        List<FilePlace> PlaceList => placeList ?? (placeList = CcsFileDialog.Places());

        bool IsFolderMode => q.Mode == FileDialogMode.Folder || q.Mode == FileDialogMode.ExportFolder;

        internal FileDialogWindow(Window owner, FileDialogRequest request)
        {
            q = request;
            Title = string.IsNullOrWhiteSpace(q.Title) ? DefaultTitle() : q.Title;
            Owner = owner; ShowInTaskbar = owner == null; if (owner?.Icon != null) Icon = owner.Icon;
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
            var work = SystemParameters.WorkArea;
            Width = Math.Min(960, Math.Max(600, work.Width - 80)); Height = Math.Min(640, Math.Max(440, work.Height - 80)); MinWidth = 600; MinHeight = 440;
            ResizeMode = ResizeMode.CanResize; UseLayoutRounding = true; SnapsToDevicePixels = true;
            try { Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/CcsBackupUi;component/Workbench.xaml", UriKind.Absolute) }); } catch (Exception) { }
            if (owner != null) foreach (var k in owner.Resources.Keys) Resources[k] = owner.Resources[k];   // the Workbench's derived styles
            try { Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/CcsBackupUi;component/CcsFileDialog.xaml", UriKind.Absolute) }); } catch (Exception) { }
            Background = TryFindResource("B.Bg") as Brush ?? Brushes.Black;
            Foreground = TryFindResource("B.Ink") as Brush ?? Brushes.White;
            FontFamily = TryFindResource("F.Ui") as FontFamily ?? new FontFamily("Segoe UI");
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            BackupWorkspace.SetNoTerms(this, true);   // file names and paths are shown exactly as they are
            SourceInitialized += (s, e) => WindowFrame.Prepare(this);
            ParseKinds();
            Content = Framed(Build());
            PreviewKeyDown += OnKey;
            Loaded += (s, e) => Start();
            ContentRendered += (s, e) => CcsFileDialog.ShownHook?.Invoke(this);
        }

        internal string[] ShowModal() { ShowDialog(); return answer; }

        string DefaultTitle() => q.Mode == FileDialogMode.Save ? "Save As" : q.Mode == FileDialogMode.Open ? (q.Multiple ? "Choose Files" : "Choose a File") : "Choose a Folder";

        // ------------------------------------------------------------------ the dialog's parts
        T Res<T>(string k) where T : class => TryFindResource(k) as T;

        /// <summary>the dialog's content as it is: its red edge (B.Red, 2 px at 100% scale, all four sides, and Windows 11's outer edge) comes
        /// from WindowFrame.Prepare, which every Workbench dialog window calls, so it is drawn once</summary>
        static UIElement Framed(UIElement child) => child;

        Button Tool(string glyph, string spoken, string tip, Action click)
        {
            var b = new Button { Content = glyph, Style = Res<Style>("FD.Tool"), ToolTip = tip, Margin = new Thickness(0, 0, 6, 0) };
            AutomationProperties.SetName(b, spoken); AutomationProperties.SetHelpText(b, tip);
            b.Click += (s, e) => click(); return b;
        }
        Button Btn(string text, string style, Action click)
        {
            var b = new Button { Content = text, Style = Res<Style>(style), MinWidth = 104, MinHeight = 34, Margin = new Thickness(10, 0, 0, 0) };
            AutomationProperties.SetName(b, text); b.Click += (s, e) => click(); return b;
        }
        TextBox Box(string spoken)
        {
            var t = new TextBox { FontFamily = Res<FontFamily>("F.Ui"), FontSize = 13, Padding = new Thickness(8, 6, 8, 6), MinHeight = 34, VerticalContentAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(t, spoken); return t;
        }
        TextBlock Label(string text, UIElement target)
        {
            var l = new TextBlock { Text = text, FontSize = 12.5, Foreground = Res<Brush>("B.Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            if (target != null) AutomationProperties.SetLabeledBy(target, l);
            return l;
        }

        UIElement Build()
        {
            var root = new Grid { Margin = new Thickness(16, 12, 16, 14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // toolbar: Back, Forward, Up, Refresh, path, search
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // breadcrumbs
            root.RowDefinitions.Add(new RowDefinition());                              // places and the list
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // status and message
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // name, kind, buttons

            // the toolbar
            var bar = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); bar.ColumnDefinitions.Add(new ColumnDefinition()); bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
            var tools = new StackPanel { Orientation = Orientation.Horizontal };
            tools.Children.Add(bBack = Tool("\uE72B", "Back", "Back (Alt+Left)", GoBack));
            tools.Children.Add(bForward = Tool("\uE72A", "Forward", "Forward (Alt+Right)", GoForward));
            tools.Children.Add(bUp = Tool("\uE74A", "Up one folder", "Up one folder (Alt+Up)", GoUp));
            tools.Children.Add(bRefresh = Tool("\uE72C", "Refresh", "Refresh (F5)", () => Load(current, false)));
            bar.Children.Add(tools);
            address = Box("Folder path"); address.Margin = new Thickness(4, 0, 8, 0);
            AutomationProperties.SetHelpText(address, "Type or paste a folder's path, then press Enter (Ctrl+L)");
            address.KeyDown += (s, e) => { if (e.Key == Key.Enter) { e.Handled = true; GoTyped(address.Text); } };
            address.GotKeyboardFocus += (s, e) => address.SelectAll();
            address.LostKeyboardFocus += (s, e) => ShowAddress();
            Grid.SetColumn(address, 1); bar.Children.Add(address);
            var sg = new Grid(); search = Box("Search this folder"); AutomationProperties.SetHelpText(search, "Shows only the names that contain these letters (Ctrl+F)");
            searchHint = new TextBlock { Text = "Search this folder", FontSize = 13, Foreground = Res<Brush>("B.Muted"), Margin = new Thickness(11, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            search.TextChanged += (s, e) => { searchHint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; Refill(); };
            search.KeyDown += (s, e) => { if (e.Key == Key.Enter || e.Key == Key.Down) { e.Handled = true; FocusList(); } };
            sg.Children.Add(search); sg.Children.Add(searchHint); Grid.SetColumn(sg, 2); bar.Children.Add(sg);
            root.Children.Add(bar);

            // the breadcrumbs: each step of the folder's path, to click
            crumbs = new WrapPanel { Margin = new Thickness(0, 0, 0, 8), MinHeight = 28 };
            AutomationProperties.SetName(crumbs, "Folder steps");
            Grid.SetRow(crumbs, 1); root.Children.Add(crumbs);

            // the places, then the folder's list
            var mid = new Grid();
            mid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(196), MinWidth = 140 }); mid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); mid.ColumnDefinitions.Add(new ColumnDefinition { MinWidth = 280 });
            places = new ListBox { ItemContainerStyle = Res<Style>("FD.Place"), ItemTemplate = Res<DataTemplate>("FD.PlaceCell"), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0) };
            ScrollViewer.SetHorizontalScrollBarVisibility(places, ScrollBarVisibility.Disabled);
            AutomationProperties.SetName(places, "Places");
            places.PreviewMouseLeftButtonUp += (s, e) => { if (ItemsControl.ContainerFromElement(places, e.OriginalSource as DependencyObject) is ListBoxItem li && li.DataContext is FilePlace p) OpenPlace(p); };
            places.KeyDown += (s, e) => { if ((e.Key == Key.Enter || e.Key == Key.Space) && places.SelectedItem is FilePlace p) { e.Handled = true; OpenPlace(p); FocusList(); } };
            places.GotKeyboardFocus += (s, e) => inPlaces = true; places.LostKeyboardFocus += (s, e) => inPlaces = false;
            mid.Children.Add(places);
            var split = new GridSplitter { Width = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Stretch, Background = Brushes.Transparent, Focusable = false, ResizeBehavior = GridResizeBehavior.PreviousAndNext };
            Grid.SetColumn(split, 1); mid.Children.Add(split);

            grid = new GridView { ColumnHeaderContainerStyle = Res<Style>("FD.Header"), AllowsColumnReorder = false };
            if (q.Mode == FileDialogMode.Open && q.Multiple) grid.Columns.Add(new GridViewColumn { Header = "", Width = 38, CellTemplate = Res<DataTemplate>("FD.CheckCell") });
            var nameCol = Column("Name", 300, "FD.NameCell"); grid.Columns.Add(nameCol);
            grid.Columns.Add(Column("Date changed", 168, "FD.DateCell"));
            grid.Columns.Add(Column("Type", 132, "FD.TypeCell"));
            grid.Columns.Add(Column("Size", 88, "FD.SizeCell"));
            list = new ListView { View = grid, Style = Res<Style>("FD.List"), ItemContainerStyle = Res<Style>("FD.Row"), SelectionMode = q.Mode == FileDialogMode.Open && q.Multiple ? SelectionMode.Extended : SelectionMode.Single };
            list.Resources[SystemColors.ControlBrushKey] = Brushes.Transparent;
            TextSearch.SetTextPath(list, "Name"); list.IsTextSearchEnabled = true;
            AutomationProperties.SetName(list, IsFolderMode ? "Folders" : "Folders and files");
            list.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(OnHeader));
            list.SelectionChanged += (s, e) => Chosen();
            list.MouseDoubleClick += (s, e) => { if (ItemsControl.ContainerFromElement(list, e.OriginalSource as DependencyObject) is ListViewItem li && li.DataContext is FileRow r) { e.Handled = true; OpenRow(r); } };
            list.PreviewKeyDown += OnListKey;
            list.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new RoutedEventHandler((s, e) => { if (e.OriginalSource is CheckBox) toggled = true; }));
            list.PreviewMouseLeftButtonDown += (s, e) => { if (!(e.OriginalSource is DependencyObject o && FindUp<CheckBox>(o) != null) && Keyboard.Modifiers == ModifierKeys.None) toggled = false; };
            // the name column takes the room the other columns leave (until the person drags a column's edge)
            bool nameDragged = false;
            ((System.ComponentModel.INotifyPropertyChanged)nameCol).PropertyChanged += (s, e) => { if (e.PropertyName == "ActualWidth" && Mouse.LeftButton == MouseButtonState.Pressed) nameDragged = true; };
            list.SizeChanged += (s, e) => { if (nameDragged) return; double others = grid.Columns.Where(c => c != nameCol).Sum(c => c.ActualWidth); nameCol.Width = Math.Max(180, list.ActualWidth - others - 30); };
            Grid.SetColumn(list, 2); mid.Children.Add(list);
            Grid.SetRow(mid, 2); root.Children.Add(mid);

            // what is shown, and any problem, in plain words
            var info = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            status = new TextBlock { FontSize = 12.5, Foreground = Res<Brush>("B.Muted"), TextWrapping = TextWrapping.Wrap };
            AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite); AutomationProperties.SetName(status, "Status");
            message = new Border { BorderBrush = Res<Brush>("B.Red3"), BorderThickness = new Thickness(3, 0, 0, 0), Background = Res<Brush>("B.Surface2"), CornerRadius = new CornerRadius(0, 6, 6, 0), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
            var mt = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13 }; message.Child = mt;
            AutomationProperties.SetLiveSetting(mt, AutomationLiveSetting.Assertive);
            info.Children.Add(status); info.Children.Add(message);
            Grid.SetRow(info, 3); root.Children.Add(info);

            // the name, the kind of file, New Folder and the two buttons
            var foot = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); foot.ColumnDefinitions.Add(new ColumnDefinition()); foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            foot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); foot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); foot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            string nameLabel = q.Mode == FileDialogMode.Save ? "File name:" : q.Mode == FileDialogMode.Open ? (q.Multiple ? "File names:" : "File name:") : q.Mode == FileDialogMode.ExportFolder ? "Save in:" : "Folder:";
            name = Box(nameLabel.TrimEnd(':')); name.FontFamily = Res<FontFamily>("F.Ui");
            if (q.Mode == FileDialogMode.ExportFolder) { name.IsReadOnly = true; name.Foreground = Res<Brush>("B.Muted"); }
            var nl = Label(nameLabel, name); foot.Children.Add(nl);
            Grid.SetColumn(name, 1); foot.Children.Add(name);
            name.TextChanged += (s, e) => { if (!settingName) { HideMessage(); UpdateExportLine(); } };
            name.KeyDown += (s, e) => { if (e.Key == Key.Enter) { e.Handled = true; Accept(); } };
            kind = new ComboBox { MinWidth = 220, MaxWidth = 320, Margin = new Thickness(10, 0, 0, 0), MinHeight = 34 };
            AutomationProperties.SetName(kind, "Kind of file");
            foreach (var (label, _) in kinds) kind.Items.Add(new ComboBoxItem { Content = label });
            if (kinds.Count > 0) kind.SelectedIndex = 0;
            kind.SelectionChanged += (s, e) => { if (q.Mode == FileDialogMode.Save) MatchExtension(); Refill(); };
            if (IsFolderMode || kinds.Count == 0) kind.Visibility = Visibility.Collapsed;
            Grid.SetColumn(kind, 2); foot.Children.Add(kind);
            int row = 1;
            if (q.Mode == FileDialogMode.ExportFolder)
            {
                subName = Box("New folder name"); subName.Margin = new Thickness(0, 8, 0, 0);
                var sl = Label("New folder:", subName); sl.Margin = new Thickness(0, 8, 10, 0); Grid.SetRow(sl, row); foot.Children.Add(sl);
                Grid.SetRow(subName, row); Grid.SetColumn(subName, 1); foot.Children.Add(subName);
                subName.TextChanged += (s, e) => { HideMessage(); UpdateExportLine(); };
                subName.KeyDown += (s, e) => { if (e.Key == Key.Enter) { e.Handled = true; Accept(); } };
                row++;
                exportLine = new TextBlock { FontSize = 12.5, Foreground = Res<Brush>("B.Ink2"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
                Grid.SetRow(exportLine, row); Grid.SetColumn(exportLine, 1); Grid.SetColumnSpan(exportLine, 2); foot.Children.Add(exportLine);
            }
            // New Folder at the left; the choice and Cancel at the right, in that order on the screen and for the Tab key
            var buttons = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); buttons.ColumnDefinitions.Add(new ColumnDefinition()); buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bool canMake = q.Mode != FileDialogMode.Open && q.NewFolder;
            bNewFolder = Btn("New Folder…", "Btn.Ghost", NewFolder); bNewFolder.Margin = new Thickness(0); bNewFolder.ToolTip = "Make a new folder here (Ctrl+Shift+N)";
            if (!canMake) bNewFolder.Visibility = Visibility.Collapsed;
            buttons.Children.Add(bNewFolder);
            bOk = Btn(q.Mode == FileDialogMode.Save ? "Save" : q.Mode == FileDialogMode.Open ? "Open" : q.Mode == FileDialogMode.ExportFolder ? "Choose This Place" : "Choose This Folder", "Btn.Primary", Accept);
            bCancel = Btn("Cancel", "Btn.Ghost", Cancel); bCancel.IsCancel = false;
            var right = new StackPanel { Orientation = Orientation.Horizontal }; right.Children.Add(bOk); right.Children.Add(bCancel);
            Grid.SetColumn(right, 2); buttons.Children.Add(right);
            var outer = new StackPanel(); outer.Children.Add(foot); outer.Children.Add(buttons);
            Grid.SetRow(outer, 4); root.Children.Add(outer);
            return root;
        }

        GridViewColumn Column(string header, double width, string cell)
            => new GridViewColumn { Header = new GridViewColumnHeader { Content = header, Tag = header, Style = Res<Style>("FD.Header") }, Width = width, CellTemplate = Res<DataTemplate>(cell) };

        void ParseKinds()
        {
            var f = q.Filters;
            if (string.IsNullOrWhiteSpace(f)) return;
            var parts = f.Split('|');
            for (int i = 0; i + 1 < parts.Length; i += 2)
                kinds.Add((parts[i].Trim(), parts[i + 1].Split(';').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray()));
        }

        // ------------------------------------------------------------------ opening and showing a folder
        void Start()
        {
            places.ItemsSource = PlaceList;
            if (q.Mode == FileDialogMode.Save || q.Mode == FileDialogMode.Open) { settingName = true; name.Text = q.SuggestedName ?? ""; settingName = false; }
            if (q.Mode == FileDialogMode.ExportFolder) subName.Text = q.SuggestedName ?? "";
            string start = q.InitialFolder;
            if (string.IsNullOrWhiteSpace(start) || !FolderExists(start)) start = (PlaceList.FirstOrDefault(p => p.Name == "Documents") ?? PlaceList.FirstOrDefault(p => !p.IsThisPc))?.Path ?? "";
            Load(Normal(start), false);
            if (q.Mode == FileDialogMode.Save || q.Mode == FileDialogMode.ExportFolder)
            {
                var t = q.Mode == FileDialogMode.Save ? name : subName; t.Focus();
                var stem = System.IO.Path.GetFileNameWithoutExtension(t.Text);
                if (q.Mode == FileDialogMode.Save && stem.Length > 0 && stem.Length < t.Text.Length) t.Select(0, stem.Length); else t.SelectAll();
            }
            else FocusList();
        }

        /// <summary>shows a folder ("" is This PC); remember adds the folder left to Back</summary>
        void Load(string folder, bool remember)
        {
            folder = folder ?? "";
            if (remember && !string.Equals(folder, current, StringComparison.OrdinalIgnoreCase)) { if (current != null) back.Push(current); forward.Clear(); }
            current = folder; HideMessage(); toggled = false;
            if (q.Mode == FileDialogMode.Open && current != null && remember) SetName("");
            ShowAddress(); Crumbs(); MarkPlace();
            bUp.IsEnabled = folder.Length > 0; bBack.IsEnabled = back.Count > 0; bForward.IsEnabled = forward.Count > 0;
            bNewFolder.IsEnabled = folder.Length > 0;
            int id = ++loadId;
            if (folder.Length == 0)
            {
                all = PlaceList.Where(p => p.IsDrive).Select(p => new FileRow { Name = p.Name, Path = p.Path, IsFolder = true, IsDrive = true, TypeText = "Drive" }).ToList();
                Refill(); return;
            }
            status.Text = "Opening…"; list.ItemsSource = null;
            Task.Run(() => Read(folder)).ContinueWith(t =>
            {
                if (id != loadId) return;   // another folder was opened meanwhile
                var (rows, problem) = t.Result;
                if (problem != null) { all = new List<FileRow>(); Refill(); Problem(problem); return; }
                all = rows; Refill(); UpdateExportLine();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        static (List<FileRow>, string) Read(string folder)
        {
            var rows = new List<FileRow>();
            try
            {
                var d = new DirectoryInfo(Long(folder));
                if (!d.Exists) return (rows, "This folder is not there any more: " + folder);
                foreach (var e in d.EnumerateFileSystemInfos())
                {
                    FileAttributes a; try { a = e.Attributes; } catch (Exception) { continue; }
                    if ((a & FileAttributes.Hidden) != 0 || (a & FileAttributes.System) != 0) continue;   // as File Explorer shows them by default
                    bool dir = (a & FileAttributes.Directory) != 0;
                    DateTime? when = null; long? size = null;
                    try { when = e.LastWriteTime; } catch (Exception) { }
                    if (!dir) try { size = ((FileInfo)e).Length; } catch (Exception) { }
                    rows.Add(new FileRow { Name = e.Name, Path = System.IO.Path.Combine(folder, e.Name), IsFolder = dir, Modified = when, Bytes = size, TypeText = dir ? "Folder" : TypeOf(e.Name) });
                }
                return (rows, null);
            }
            catch (UnauthorizedAccessException) { return (rows, "Windows did not allow this folder to be opened (you may not have permission to see it): " + folder); }
            catch (DirectoryNotFoundException) { return (rows, "This folder is not there any more: " + folder); }
            catch (IOException ex) { return (rows, "This folder could not be opened: " + ex.Message.TrimEnd('.', ' ', '\r', '\n') + "."); }
            catch (Exception ex) { return (rows, "This folder could not be opened: " + ex.Message.TrimEnd('.', ' ', '\r', '\n') + "."); }
        }

        /// <summary>a plain name for a file's kind, from its extension</summary>
        static string TypeOf(string fileName)
        {
            var ext = System.IO.Path.GetExtension(fileName ?? "").TrimStart('.').ToLowerInvariant();
            switch (ext)
            {
                case "": return "File";
                case "zip": return "ZIP file";
                case "json": return "Settings or data file";
                case "png": return "PNG picture";
                case "jpg": case "jpeg": return "JPEG picture";
                case "gif": return "GIF picture";
                case "bmp": return "Bitmap picture";
                case "webp": return "WebP picture";
                case "nes": case "unh": case "unif": case "fds": return "NES game file";
                case "sfc": case "smc": return "SNES game file";
                case "ccs": return "Classic Cart Studio backup";
                case "ccshome": return "Shared HOME Design";
                case "ccsplan": return "Shared Composition Plan";
                case "hmod": return "Hakchi package";
                case "hsqs": case "squashfs": return "System image";
                case "img": case "bin": case "raw": case "iso": return "Image file";
                case "exe": return "Program";
                case "txt": return "Text file";
                case "sav": case "srm": return "Game save file";
                default: return ext.ToUpperInvariant() + " file";
            }
        }

        /// <summary>what the list shows now: the folder's rows that match the search and the chosen kind of file, folders first, sorted</summary>
        void Refill()
        {
            if (list == null) return;
            IEnumerable<FileRow> rows = all;
            var s = search?.Text?.Trim() ?? "";
            if (s.Length > 0) rows = rows.Where(r => r.Name.IndexOf(s, StringComparison.CurrentCultureIgnoreCase) >= 0);
            if (IsFolderMode) rows = rows.Where(r => r.IsFolder);
            else { var pats = SelectedPatterns(); if (pats != null) rows = rows.Where(r => r.IsFolder || Matches(r.Name, pats)); }
            var shown = rows.ToList();
            int dir = sortDown ? -1 : 1; var names = StringComparer.CurrentCultureIgnoreCase;
            Comparison<FileRow> by;
            if (sortBy == "Date changed") by = (a, b) => (a.Modified ?? DateTime.MinValue).CompareTo(b.Modified ?? DateTime.MinValue);
            else if (sortBy == "Type") by = (a, b) => names.Compare(a.TypeText ?? "", b.TypeText ?? "");
            else if (sortBy == "Size") by = (a, b) => (a.Bytes ?? -1).CompareTo(b.Bytes ?? -1);
            else by = (a, b) => names.Compare(a.Name, b.Name);
            shown.Sort((a, b) => { if (a.IsFolder != b.IsFolder) return a.IsFolder ? -1 : 1; int c = by(a, b) * dir; return c != 0 ? c : names.Compare(a.Name, b.Name); });
            list.ItemsSource = shown;
            int folders = shown.Count(r => r.IsFolder), files = shown.Count - folders;
            string what = current?.Length == 0 ? Count(shown.Count, "drive") : IsFolderMode ? Count(folders, "folder") : Count(folders, "folder") + ", " + Count(files, "file");
            status.Text = (s.Length > 0 ? "Matching \u201C" + s + "\u201D: " : "") + what + (all.Count > shown.Count && s.Length == 0 && !IsFolderMode && kinds.Count > 1 && current?.Length > 0 ? " (files of other kinds are not shown; choose the kind below to see them)" : "");
            foreach (var c in grid.Columns) if (c.Header is GridViewColumnHeader h && h.Tag is string t) h.Content = t + (t == sortBy ? (sortDown ? "  \u2193" : "  \u2191") : "");
        }
        static string Count(int n, string one) => n.ToString("N0", CultureInfo.GetCultureInfo("en-US")) + " " + one + (n == 1 ? "" : "s");

        string[] SelectedPatterns()
        {
            if (kinds.Count == 0) return null;
            int i = Math.Max(0, kind.SelectedIndex);
            var p = kinds[Math.Min(i, kinds.Count - 1)].patterns;
            return p.Any(x => x == "*.*" || x == "*") ? null : p;
        }
        static bool Matches(string fileName, string[] patterns)
        {
            foreach (var p in patterns)
            {
                if (p.StartsWith("*.", StringComparison.Ordinal)) { if (fileName.EndsWith(p.Substring(1), StringComparison.OrdinalIgnoreCase)) return true; }
                else if (string.Equals(fileName, p, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        void ShowAddress() { if (address != null && !address.IsKeyboardFocusWithin) address.Text = string.IsNullOrEmpty(current) ? "This PC" : current; }

        void Crumbs()
        {
            crumbs.Children.Clear();
            void Add(string text, string path, bool last)
            {
                var b = new Button { Content = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 260 }, Style = Res<Style>("FD.Crumb"), ToolTip = string.IsNullOrEmpty(path) ? "This PC" : path };
                if (last) b.FontWeight = FontWeights.SemiBold;
                AutomationProperties.SetName(b, text + (last ? ", the folder shown" : ""));
                b.Click += (s, e) => Load(path, true); crumbs.Children.Add(b);
                if (!last) crumbs.Children.Add(new TextBlock { Text = "\uE76C", FontFamily = Res<FontFamily>("F.Icons"), FontSize = 9, Foreground = Res<Brush>("B.Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 1, 2, 0) });
            }
            if (string.IsNullOrEmpty(current)) { Add("This PC", "", true); return; }
            var steps = new List<(string text, string path)>();
            for (var d = new DirectoryInfo(current); d != null; d = d.Parent) { var nm = d.Parent == null ? DriveName(d.FullName) : d.Name; steps.Insert(0, (nm, d.FullName)); }
            steps.Insert(0, ("This PC", ""));
            for (int i = 0; i < steps.Count; i++) Add(steps[i].text, steps[i].path, i == steps.Count - 1);
        }

        string DriveName(string root)
        {
            var p = PlaceList.FirstOrDefault(x => x.IsDrive && string.Equals(x.Path.TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
            return p?.Name ?? root.TrimEnd('\\');
        }

        void MarkPlace()
        {
            if (!(places.ItemsSource is List<FilePlace> ps)) return;
            var m = ps.FirstOrDefault(p => string.Equals((p.Path ?? "").TrimEnd('\\'), (current ?? "").TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
            if (!inPlaces) places.SelectedItem = m;
        }

        // ------------------------------------------------------------------ moving around
        void GoBack() { if (back.Count == 0) return; forward.Push(current); var to = back.Pop(); Load(to, false); }
        void GoForward() { if (forward.Count == 0) return; back.Push(current); var to = forward.Pop(); Load(to, false); }
        void GoUp()
        {
            if (string.IsNullOrEmpty(current)) return;
            var parent = new DirectoryInfo(current).Parent;
            var from = current; Load(parent?.FullName ?? "", true);
            SelectWhenShown(from);
        }
        void OpenPlace(FilePlace p) { if (p != null) Load(p.IsThisPc ? "" : p.Path, true); }

        /// <summary>the path typed into the address box: a folder opens; a file (when files are chosen) opens its folder with it selected</summary>
        void GoTyped(string typed)
        {
            var t = Environment.ExpandEnvironmentVariables((typed ?? "").Trim().Trim('"'));
            if (t.Length == 0 || string.Equals(t, "This PC", StringComparison.OrdinalIgnoreCase)) { Load("", true); FocusList(); return; }
            string full;
            try { full = Normal(System.IO.Path.IsPathRooted(t) || string.IsNullOrEmpty(current) ? t : System.IO.Path.Combine(current, t)); }
            catch (Exception) { Problem("That is not a folder's path: " + t); return; }
            if (FolderExists(full)) { Load(full, true); FocusList(); return; }
            if (!IsFolderMode && FileExists(full)) { Load(System.IO.Path.GetDirectoryName(full), true); SelectWhenShown(full); return; }
            Problem("There is no folder at that place: " + t + ". Check the spelling, or choose a place at the side.");
        }

        void SelectWhenShown(string path)
        {
            int id = loadId;
            Dispatcher.BeginInvoke(new Action(() => Select(path, id, 0)), System.Windows.Threading.DispatcherPriority.Background);
        }
        void Select(string path, int id, int tries)
        {
            if (id != loadId) return;
            var r = (list.ItemsSource as List<FileRow>)?.FirstOrDefault(x => string.Equals(x.Path.TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
            if (r == null) { if (tries < 40) Dispatcher.BeginInvoke(new Action(() => Select(path, id, tries + 1)), System.Windows.Threading.DispatcherPriority.Background); return; }
            list.SelectedItem = r; list.ScrollIntoView(r);
            if (list.ItemContainerGenerator.ContainerFromItem(r) is ListViewItem li) li.Focus();
        }

        static T FindUp<T>(DependencyObject d) where T : DependencyObject { for (; d != null; d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d)) if (d is T t) return t; return null; }

        void FocusList()
        {
            if (list.Items.Count == 0) { list.Focus(); return; }
            var target = list.SelectedItem ?? list.Items[0];
            list.ScrollIntoView(target);
            Dispatcher.BeginInvoke(new Action(() => { if (list.ItemContainerGenerator.ContainerFromItem(target) is ListViewItem li) li.Focus(); else list.Focus(); }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>Enter or a double-click on a row: a folder or drive opens; a file is the choice (Open and Save)</summary>
        void OpenRow(FileRow r)
        {
            if (r.IsFolder) { Load(r.Path, true); search.Text = ""; FocusList(); return; }
            if (q.Mode == FileDialogMode.Open) { if (!q.Multiple || list.SelectedItems.Count <= 1) { Finish(new[] { r.Path }); return; } Accept(); return; }
            if (q.Mode == FileDialogMode.Save) { SetName(r.Name); Accept(); }
        }

        void OnListKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && list.SelectedItem is FileRow r && !(list.SelectedItems.Count > 1)) { e.Handled = true; OpenRow(r); }
            else if (e.Key == Key.Enter && list.SelectedItems.Count > 1) { e.Handled = true; Accept(); }
            else if (e.Key == Key.Space && list.SelectionMode == SelectionMode.Extended && Keyboard.Modifiers == ModifierKeys.None)
            {   // several files: Space chooses the focused file (a second Space un-chooses it), as its check box does; folders are never chosen
                e.Handled = true;
                if (Keyboard.FocusedElement is ListViewItem li && li.DataContext is FileRow fr && !fr.IsFolder)
                {
                    if (!li.IsSelected) li.IsSelected = true; else if (toggled) li.IsSelected = false;
                    foreach (var f in list.SelectedItems.OfType<FileRow>().Where(x => x.IsFolder).ToList()) list.SelectedItems.Remove(f);
                    toggled = true;
                }
            }
            else if (list.SelectionMode == SelectionMode.Extended && toggled && Keyboard.Modifiers == ModifierKeys.None && (e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Home || e.Key == Key.End || e.Key == Key.PageUp || e.Key == Key.PageDown))
            {   // after Space: the arrow keys move the focus only
                e.Handled = true;
                var at = Keyboard.FocusedElement is ListViewItem cur ? list.ItemContainerGenerator.IndexFromContainer(cur) : 0; int n = list.Items.Count; if (n == 0) return;
                int page = Math.Max(1, (int)(list.ActualHeight / 30) - 2);
                int to = e.Key == Key.Up ? at - 1 : e.Key == Key.Down ? at + 1 : e.Key == Key.Home ? 0 : e.Key == Key.End ? n - 1 : e.Key == Key.PageUp ? at - page : at + page;
                to = Math.Max(0, Math.Min(n - 1, to)); var item = list.Items[to]; list.ScrollIntoView(item);
                Dispatcher.BeginInvoke(new Action(() => (list.ItemContainerGenerator.ContainerFromIndex(to) as ListViewItem)?.Focus()), System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        void OnHeader(object sender, RoutedEventArgs e)
        {
            if (!(e.OriginalSource is GridViewColumnHeader h) || !(h.Tag is string col)) return;
            if (sortBy == col) sortDown = !sortDown; else { sortBy = col; sortDown = col == "Date changed" || col == "Size"; }
            var keep = list.SelectedItem as FileRow; Refill(); if (keep != null) list.SelectedItem = (list.ItemsSource as List<FileRow>)?.FirstOrDefault(x => x.Path == keep.Path);
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key; var mods = Keyboard.Modifiers;
            bool inText = Keyboard.FocusedElement is TextBox;
            if (mods == ModifierKeys.Alt && key == Key.Left) { GoBack(); e.Handled = true; }
            else if (mods == ModifierKeys.Alt && key == Key.Right) { GoForward(); e.Handled = true; }
            else if (mods == ModifierKeys.Alt && key == Key.Up) { GoUp(); e.Handled = true; }
            else if (key == Key.Back && !inText && mods == ModifierKeys.None) { GoUp(); e.Handled = true; }
            else if (key == Key.F5) { Load(current, false); e.Handled = true; }
            else if ((mods == ModifierKeys.Control && key == Key.L) || (mods == ModifierKeys.Alt && key == Key.D) || key == Key.F4) { address.Focus(); address.SelectAll(); e.Handled = true; }
            else if ((mods == ModifierKeys.Control && key == Key.F) || key == Key.F3) { search.Focus(); search.SelectAll(); e.Handled = true; }
            else if (mods == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.N && bNewFolder.Visibility == Visibility.Visible && bNewFolder.IsEnabled) { NewFolder(); e.Handled = true; }
            else if (key == Key.Escape && !kind.IsDropDownOpen)
            {
                e.Handled = true;
                if (address.IsKeyboardFocusWithin) { ShowAddressNow(); FocusList(); }
                else if (search.IsKeyboardFocusWithin && search.Text.Length > 0) search.Text = "";
                else Cancel();
            }
        }
        void ShowAddressNow() { address.Text = string.IsNullOrEmpty(current) ? "This PC" : current; }

        // ------------------------------------------------------------------ the name box
        void SetName(string text) { settingName = true; name.Text = text; settingName = false; UpdateExportLine(); }

        /// <summary>the rows chosen in the list fill the name box (Open: the file names; Save: a chosen file's name; a folder choice: its name)</summary>
        void Chosen()
        {
            var sel = list.SelectedItems.OfType<FileRow>().ToList();
            if (q.Mode == FileDialogMode.Open)
            {
                var files = sel.Where(r => !r.IsFolder).ToList();
                if (files.Count == 1) SetName(files[0].Name);
                else if (files.Count > 1) SetName(string.Join(" ", files.Select(f => "\"" + f.Name + "\"")));
            }
            else if (q.Mode == FileDialogMode.Save) { var f = sel.FirstOrDefault(r => !r.IsFolder); if (f != null) SetName(f.Name); }
            else if (q.Mode == FileDialogMode.Folder) SetName(sel.FirstOrDefault()?.IsDrive == true ? sel[0].Path : sel.FirstOrDefault()?.Name ?? "");
            else UpdateExportLine();
            HideMessage();
        }

        /// <summary>Save: a name typed without an extension gets the chosen kind's; a different kind chosen changes the name's extension</summary>
        void MatchExtension()
        {
            var ext = ConcreteExtension(); if (ext == null) return;
            var n = name.Text.Trim(); if (n.Length == 0 || n.IndexOfAny(new[] { '\\', '/' }) >= 0) return;
            var cur = System.IO.Path.GetExtension(n);
            if (string.IsNullOrEmpty(cur)) return;
            if (kinds.SelectMany(k => k.patterns).Any(p => p.StartsWith("*.", StringComparison.Ordinal) && string.Equals(p.Substring(1), cur, StringComparison.OrdinalIgnoreCase)))
                SetName(System.IO.Path.GetFileNameWithoutExtension(n) + ext);
        }
        string ConcreteExtension()
        {
            var p = SelectedPatterns(); if (p == null) return null;
            var first = p.FirstOrDefault(x => x.StartsWith("*.", StringComparison.Ordinal) && x.IndexOfAny(new[] { '*', '?' }, 2) < 0);
            return first?.Substring(1);
        }

        string ChosenFolder()
        {
            var sel = list.SelectedItem as FileRow;
            if (sel != null && sel.IsFolder) return sel.Path;
            return string.IsNullOrEmpty(current) ? null : current;
        }

        void UpdateExportLine()
        {
            if (exportLine == null) return;
            var f = ChosenFolder(); var n = subName.Text.Trim();
            settingName = true; name.Text = f ?? ""; settingName = false;
            exportLine.Text = f == null ? "Choose a folder for the new folder." : n.Length == 0 ? "Type a name for the new folder." : "A new folder named \u201C" + n + "\u201D is made in " + f + ".";
        }

        // ------------------------------------------------------------------ the choice
        void Accept()
        {
            HideMessage();
            switch (q.Mode)
            {
                case FileDialogMode.Folder: AcceptFolder(); break;
                case FileDialogMode.ExportFolder: AcceptExport(); break;
                case FileDialogMode.Open: AcceptOpen(); break;
                case FileDialogMode.Save: AcceptSave(); break;
            }
        }

        void AcceptFolder()
        {
            var typed = name.Text.Trim().Trim('"');
            string f = null;
            var sel = list.SelectedItem as FileRow;
            if (sel != null && (sel.Name == typed || (sel.IsDrive && sel.Path == typed) || typed.Length == 0)) f = sel.Path;
            else if (typed.Length > 0)
            {
                try { f = Normal(System.IO.Path.IsPathRooted(typed) || string.IsNullOrEmpty(current) ? typed : System.IO.Path.Combine(current, typed)); } catch (Exception) { Problem("That is not a folder's name or path: " + typed); return; }
                if (!FolderExists(f)) { Problem("There is no folder named \u201C" + typed + "\u201D here. Choose a folder in the list, or make one with New Folder."); return; }
            }
            else f = string.IsNullOrEmpty(current) ? null : current;
            if (f == null) { Problem("Choose a drive or a folder first."); return; }
            if (!FolderExists(f)) { Problem("That folder is not there any more: " + f); return; }
            Finish(new[] { f });
        }

        void AcceptExport()
        {
            var f = ChosenFolder();
            if (f == null) { Problem("Choose a drive or a folder for the new folder first."); return; }
            if (!FolderExists(f)) { Problem("That folder is not there any more: " + f); return; }
            var n = subName.Text.Trim();
            var why = NameProblem(n, "folder"); if (why != null) { Problem(why); subName.Focus(); return; }
            var target = System.IO.Path.Combine(f, n);
            if (FolderExists(target) || FileExists(target)) { Problem("Something named \u201C" + n + "\u201D is already in " + f + ". Type another name for the new folder; nothing already there is changed."); subName.Focus(); subName.SelectAll(); return; }
            Finish(new[] { f, n });
        }

        void AcceptOpen()
        {
            var typed = name.Text.Trim();
            var sel = list.SelectedItems.OfType<FileRow>().Where(r => !r.IsFolder).ToList();
            var names = SplitNames(typed);
            if (names.Count == 0)
            {
                if (list.SelectedItem is FileRow fr && fr.IsFolder) { OpenRow(fr); return; }
                Problem(q.Multiple ? "Choose one or more files first." : "Choose a file first."); return;
            }
            if (!q.Multiple && names.Count > 1) { Problem("Choose only one file."); return; }
            var paths = new List<string>();
            foreach (var n in names)
            {
                string full;
                try { full = Normal(System.IO.Path.IsPathRooted(n) || string.IsNullOrEmpty(current) ? n : System.IO.Path.Combine(current, n)); } catch (Exception) { Problem("That is not a file's name: " + n); return; }
                if (FolderExists(full) && names.Count == 1) { Load(full, true); SetName(""); FocusList(); return; }
                if (!FileExists(full)) { Problem("There is no file named \u201C" + n + "\u201D here. Check the name, or choose the file in the list."); return; }
                paths.Add(full);
            }
            Finish(paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        }

        void AcceptSave()
        {
            var n = name.Text.Trim().Trim('"');
            if (n.Length == 0) { Problem("Type a name for the file."); name.Focus(); return; }
            string full;
            try { full = Normal(System.IO.Path.IsPathRooted(n) || string.IsNullOrEmpty(current) ? n : System.IO.Path.Combine(current, n)); } catch (Exception) { Problem(NameProblem(System.IO.Path.GetFileName(n), "file") ?? "That is not a file's name: " + n); return; }
            if (FolderExists(full)) { Load(full, true); SetName(""); name.Focus(); return; }
            var folder = System.IO.Path.GetDirectoryName(full); var file = System.IO.Path.GetFileName(full);
            if (string.IsNullOrEmpty(folder)) { Problem("Choose a drive or a folder to save in first."); return; }
            var why = NameProblem(file, "file"); if (why != null) { Problem(why); name.Focus(); return; }
            if (!FolderExists(folder)) { Problem("There is no folder at " + folder + ". Choose a folder in the list, or make one with New Folder."); return; }
            if (string.IsNullOrEmpty(System.IO.Path.GetExtension(file))) { var ext = ConcreteExtension(); if (ext != null) { file += ext; full = System.IO.Path.Combine(folder, file); } }
            if (FolderExists(full)) { Problem("A folder named \u201C" + file + "\u201D is already there. Type another name for the file."); return; }
            if (FileExists(full) && q.ConfirmOverwrite)
            {
                if (!Confirm("Replace the file?", "\u201C" + file + "\u201D is already in " + folder + ".\n\nReplace it with the new file? The file there now is lost.", "Replace It", "Keep It")) { name.Focus(); return; }
            }
            Finish(new[] { full });
        }

        /// <summary>several quoted names ("a.zip" "b.zip"), or one name as typed</summary>
        static List<string> SplitNames(string typed)
        {
            var l = new List<string>(); if (string.IsNullOrWhiteSpace(typed)) return l;
            if (typed.IndexOf('"') < 0) { l.Add(typed.Trim()); return l; }
            var m = System.Text.RegularExpressions.Regex.Matches(typed, "\"([^\"]+)\"");
            foreach (System.Text.RegularExpressions.Match x in m) { var s = x.Groups[1].Value.Trim(); if (s.Length > 0) l.Add(s); }
            return l;
        }

        static readonly string[] Reserved = { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
        /// <summary>why a name cannot be a file's or a folder's name on Windows (null: it can)</summary>
        internal static string NameProblem(string n, string what)
        {
            if (string.IsNullOrWhiteSpace(n)) return "Type a name for the " + what + ".";
            if (n.IndexOfAny(new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' }) >= 0 || n.Any(c => c < 32)) return "A " + what + " name cannot contain any of these: \\ / : * ? \" < > |";
            if (n.EndsWith(".", StringComparison.Ordinal) || n.EndsWith(" ", StringComparison.Ordinal)) return "A " + what + " name cannot end with a period or a space.";
            if (n == "." || n == "..") return "Type a name for the " + what + ".";
            var stem = n.Split('.')[0].TrimEnd();
            if (Reserved.Any(r => string.Equals(r, stem, StringComparison.OrdinalIgnoreCase))) return "\u201C" + stem + "\u201D is a name Windows keeps for itself. Type another name.";
            if (n.Length > 240) return "That name is too long (" + n.Length + " characters). Use a shorter one.";
            return null;
        }

        void Finish(string[] chosen) { answer = chosen; Close(); }
        void Cancel() { answer = null; Close(); }

        // ------------------------------------------------------------------ New Folder
        void NewFolder()
        {
            if (string.IsNullOrEmpty(current)) { Problem("Open a drive or a folder first; a new folder is made in the folder shown."); return; }
            string suggestion = "New folder"; for (int i = 2; FolderExists(System.IO.Path.Combine(current, suggestion)) || FileExists(System.IO.Path.Combine(current, suggestion)); i++) suggestion = "New folder (" + i + ")";
            var made = AskName("New Folder", "A new folder is made in " + current + ".", suggestion, "Make the Folder");
            if (made == null) return;
            var target = System.IO.Path.Combine(current, made);
            try
            {
                if (FolderExists(target) || FileExists(target)) { Problem("Something named \u201C" + made + "\u201D is already here. Nothing was changed."); return; }
                Directory.CreateDirectory(Long(target));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { Problem("The folder could not be made here: " + (ex is UnauthorizedAccessException ? "Windows did not allow it (you may not have permission to change this folder)." : ex.Message)); return; }
            Load(current, false); SelectWhenShown(target);
        }

        /// <summary>a short question for a name, in the dialog's own look (null: canceled)</summary>
        string AskName(string title, string text, string value, string ok)
        {
            var w = Small(title);
            var sp = new StackPanel { Margin = new Thickness(22, 18, 22, 14) };
            sp.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) });
            sp.Children.Add(new TextBlock { Text = text, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
            var box = Box("Folder name"); box.Text = value; sp.Children.Add(box);
            var err = new TextBlock { FontSize = 12.5, Foreground = Res<Brush>("B.Red3"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
            AutomationProperties.SetLiveSetting(err, AutomationLiveSetting.Assertive); sp.Children.Add(err);
            string result = null;
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            var yes = Btn(ok, "Btn.Primary", () => { var n = box.Text.Trim(); var why = NameProblem(n, "folder"); if (why != null) { err.Text = why; err.Visibility = Visibility.Visible; box.Focus(); return; } result = n; w.Close(); });
            var no = Btn("Cancel", "Btn.Ghost", () => w.Close());
            yes.IsDefault = true; row.Children.Add(yes); row.Children.Add(no); sp.Children.Add(row);
            w.Content = Framed(sp);
            w.Loaded += (s, e) => { box.Focus(); box.SelectAll(); };
            w.ShowDialog();
            return result;
        }

        bool Confirm(string title, string body, string yes, string no)
        {
            var w = Small(title);
            var sp = new StackPanel { Margin = new Thickness(22, 18, 22, 14) };
            sp.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) });
            foreach (var para in body.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries)) sp.Children.Add(new TextBlock { Text = para, FontSize = 13.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            bool ok = false;
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            var b1 = Btn(yes, "Btn.Primary", () => { ok = true; w.Close(); }); var b2 = Btn(no, "Btn.Ghost", () => w.Close());
            b2.IsDefault = true; row.Children.Add(b1); row.Children.Add(b2); sp.Children.Add(row);
            w.Content = Framed(sp); w.Loaded += (s, e) => b2.Focus();
            w.ShowDialog();
            return ok;
        }

        Window Small(string title)
        {
            var w = new Window { Title = title, Owner = this, Width = 480, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false, Background = Background, Foreground = Foreground, FontFamily = FontFamily, UseLayoutRounding = true };
            foreach (var md in Resources.MergedDictionaries) w.Resources.MergedDictionaries.Add(md);
            foreach (var k in Resources.Keys) w.Resources[k] = Resources[k];
            TextOptions.SetTextFormattingMode(w, TextFormattingMode.Ideal); BackupWorkspace.SetNoTerms(w, true);
            w.SourceInitialized += (s, e) => WindowFrame.Prepare(w);
            w.PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) { e.Handled = true; w.Close(); } };
            return w;
        }

        // ------------------------------------------------------------------ messages
        void Problem(string text)
        {
            ((TextBlock)message.Child).Text = text; message.Visibility = Visibility.Visible;
        }
        void HideMessage() { if (message != null) message.Visibility = Visibility.Collapsed; }

        // ------------------------------------------------------------------ paths
        /// <summary>a full path in Windows' usual form (no "\\?\", no trailing separator except a drive's root)</summary>
        internal static string Normal(string p)
        {
            if (string.IsNullOrEmpty(p)) return "";
            if (p.StartsWith(@"\\?\UNC\", StringComparison.Ordinal)) p = @"\\" + p.Substring(8);
            else if (p.StartsWith(@"\\?\", StringComparison.Ordinal)) p = p.Substring(4);
            if (p.Length == 2 && p[1] == ':') p += "\\";
            var full = System.IO.Path.GetFullPath(p.Length > 240 ? @"\\?\" + p : p);
            if (full.StartsWith(@"\\?\", StringComparison.Ordinal)) full = full.StartsWith(@"\\?\UNC\", StringComparison.Ordinal) ? @"\\" + full.Substring(8) : full.Substring(4);
            if (full.Length > 3 && (full.EndsWith("\\", StringComparison.Ordinal) || full.EndsWith("/", StringComparison.Ordinal))) full = full.TrimEnd('\\', '/');
            return full;
        }
        /// <summary>the form Windows reads at any length ("\\?\" in front of a long local or network path)</summary>
        internal static string Long(string p)
        {
            if (string.IsNullOrEmpty(p) || p.Length < 240 || p.StartsWith(@"\\?\", StringComparison.Ordinal)) return p;
            return p.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + p.Substring(2) : @"\\?\" + p;
        }
        internal static bool FolderExists(string p) { try { return !string.IsNullOrEmpty(p) && Directory.Exists(Long(p)); } catch (Exception) { return false; } }
        internal static bool FileExists(string p) { try { return !string.IsNullOrEmpty(p) && File.Exists(Long(p)); } catch (Exception) { return false; } }
    }
}
