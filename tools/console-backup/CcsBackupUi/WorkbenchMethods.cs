// Classic Cart Studio console backup and restore window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The add-ons (Engine/MethodEngine, Engine/FirmwareEngine; tools/console-nucleus): CCS Mod (CCS HOME, opened from its tile in Original Menu)
// and CCS Nucleus (what opens at power-on: a menu straight away, or the startup chooser), chosen on two pages, the same when preparing a
// console (Prepare Console, which only adds) and in Console Restore's Add-ons and Startup (adding, removing, and changing what opens first).
// The worker first reads what the console has (its event addon-state) and the pages start from that: a suggestion never removes anything
// installed, and an explicit choice stays when the person goes back and forth. Then the console is backed up in the same session, the
// change is reviewed and confirmed once, and it takes effect at the console's next normal start through hakchi's own package folder. An
// owner's own system image as a startup choice is Console Composer's Your System Images. Package names, step codes and checksums are in
// each page's Technical details.
using CcsBackup.Engine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace CcsBackup.Ui
{
    public sealed partial class BackupWorkspace
    {
        /// <summary>the console the open method or system image review names (for the result)</summary>
        string methodTag;

        /// <summary>one console session's add-on pages: what the console has (from the worker) and what the person chooses</summary>
        sealed class AddonSession
        {
            public string Op, Situation, Tag, StartNow; public int OwnImages, OtherPackages;
            public HashSet<string> Installed = new HashSet<string>();
            public AddonChoice Current, Choice;
            /// <summary>Original Menu took CCS HOME's place as what opens first, because CCS HOME is not chosen (said once on the page)</summary>
            public bool StartReplaced;
            /// <summary>the person turned CCS Nucleus on here (its first suggestion was applied then)</summary>
            public bool NucleusTouched;
            public bool Prepare => Op == "prepare";
            public bool Has(string package) => Installed.Contains(package);
        }
        AddonSession addons;
        /// <summary>what the Add-ons and Startup page's action asked for: manage (both pages), startup (the second page), remove-mod or
        /// remove-nucleus (straight to their review; removing CCS Mod while CCS Nucleus opens CCS HOME first asks for another menu first)</summary>
        string addonIntent = "manage";

        const string ModName = "CCS Mod — CCS HOME", NucleusQuestion = "Choose what opens first with CCS Nucleus";
        static string MenuName(string id) => id == "ccs-home" ? "CCS HOME" : "Original Menu";

        // ------------------------------------------------------------------ the worker's report: what the console has now
        void OnAddonState(Dictionary<string, object> ev)
        {
            var cur = ev.O("current") ?? new Dictionary<string, object>();
            var a = new AddonSession
            {
                Op = ev.S("operation"), Situation = ev.S("situation"), Tag = ev.S("console_tag"), StartNow = ev.S("start_text"),
                OwnImages = (int)ev.L("own_images", 0), OtherPackages = (int)ev.L("other_packages", 0),
                Current = new AddonChoice { Mod = cur.B("mod"), Nucleus = cur.S("nucleus") ?? "none", Start = cur.S("start") ?? "native", Auto = cur.S("auto") ?? "menu" },
            };
            foreach (var p in (ev.A("installed") ?? new List<object>()).OfType<string>()) a.Installed.Add(p);
            a.Choice = Suggested(a, !Standalone);
            addons = a;
            // a console this version does not prepare: no pages; the review says why
            if (a.Prepare && a.Situation == "other") { SendAddons(new AddonChoice()); return; }
            if (a.Prepare || addonIntent == "manage") { ShowAddons(); return; }
            if (addonIntent == "startup") { ShowStartup(); return; }
            var package = addonIntent == "remove-mod" ? Methods.Mod : Methods.Nucleus;
            if (!a.Has(package))
            {
                addons = null; Leave("Console Restore");
                Show(Message(Methods.Label(package) + " is not on this console", "Nothing was changed. Console " + a.Tag + " does not have " + Methods.Label(package) + ", so there is nothing to remove.", "info"), "msg");
                return;
            }
            if (package == Methods.Mod)
            {   // CCS HOME cannot stay what opens first without CCS Mod: the second page shows Original Menu in its place first
                a.Choice.Mod = false;
                if (a.Choice.Nucleus != "none" && a.Choice.Start == "ccs-home") { ShowStartup(); return; }
            }
            else a.Choice.Nucleus = "none";
            SendAddons(a.Choice);
        }

        /// <summary>the pages' first suggestion. Console Restore: exactly what the console has. Preparing: the original software from Classic
        /// Cart Studio gets CCS Mod and CCS Nucleus opening CCS HOME straight away; a console already running Hakchi keeps its startup, with
        /// CCS Mod suggested from Classic Cart Studio; the standalone Workbench suggests nothing new. Never a removal.</summary>
        static AddonChoice Suggested(AddonSession a, bool studio)
        {
            var c = new AddonChoice { Mod = a.Current.Mod, Nucleus = a.Current.Nucleus, Start = a.Current.Start, Auto = a.Current.Auto };
            if (!a.Prepare) return c;
            if (a.Situation == "original") { if (studio) { c.Mod = true; c.Nucleus = "direct"; c.Start = "ccs-home"; c.Auto = "menu"; } return c; }
            if (studio) c.Mod = true;
            return c;
        }

        void SendAddons(AddonChoice c)
        {
            var op = addons?.Op ?? "method";
            progress = new ProgressView(this, "Backing Up the Console", "The console is backed up as it is now, then the change is shown for you to review. Nothing is written until you confirm. Keep the USB cable connected.", "Cancel", () => { worker?.Cancel(); progress.Say("Canceling..."); });
            Show(progress.Panel, "progress");
            worker?.Send(new JObj { { "cmd", op + "-target" }, { "mod", c.Mod }, { "nucleus", c.Nucleus }, { "start", c.Start }, { "auto", c.Auto } });
        }

        void CancelAddons()
        {
            var prep = addons?.Prepare == true; addons = null;
            if (prep) { Leave("Console Composer"); ShowPrepare(); } else { Leave("Console Restore"); ShowMethods(); }
        }

        // ------------------------------------------------------------------ page 1: Choose your add-ons
        void ShowAddons()
        {
            var a = addons; if (a == null) return;
            var sp = SectionHead("Choose your add-ons", "Choose what you want to add. Your console's startup is on the next page.");
            sp.Children.Add(ModCard(a));
            CommunityCards(sp, a);   // the community catalog's cards, when it lists any (WorkbenchCommunity.cs)
            var back = B("Back", "Btn", CancelAddons); var next = B("Next: Startup", "Btn.Primary", ShowStartup);
            sp.Children.Add(Buttons(back, next));
            Show(sp, "addons");
        }

        /// <summary>CCS Mod's card, the first and featured one: selected from Classic Cart Studio for a new setup, not from the standalone
        /// Workbench; installed and kept when preparing (removing it is in Console Restore)</summary>
        UIElement ModCard(AddonSession a)
        {
            bool installed = a.Has(Methods.Mod), locked = a.Prepare && installed;
            var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
            var cb = new CheckBox { IsChecked = a.Choice.Mod, IsEnabled = !locked, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 8),
                Content = new TextBlock { Text = ModName, FontSize = Ty.CardTitle, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap } };
            System.Windows.Automation.AutomationProperties.SetName(cb, ModName);
            left.Children.Add(cb);
            left.Children.Add(T("A menu for your game collections", Ty.Body, true, false, new Thickness(0, 0, 0, 4)));
            left.Children.Add(T("Browse your games in four themes, arrange folders, and choose your own pictures and colors. Adds a CCS HOME tile to Original Menu. What opens at power-on stays unchanged unless you choose CCS Nucleus on the next page.", Ty.Text));
            var opens = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = Ty.Text, Margin = new Thickness(0, 2, 0, 4) };
            opens.Inlines.Add(new Run("Opens from: ") { FontWeight = FontWeights.SemiBold }); opens.Inlines.Add(new Run("Original Menu. CCS Nucleus can open it too."));
            left.Children.Add(opens);
            left.Children.Add(LinkNames(T("By Rance9 · Included with Classic Cart Studio", Ty.Text, true, false, new Thickness(0, 0, 0, 8))));
            var status = new StackPanel();
            void Status()
            {
                status.Children.Clear();
                if (installed) status.Children.Add(Pill("Installed on this console", "B.Ok"));
                if (locked) status.Children.Add(T("It stays. Removing it is in Console Restore: Add-ons and Startup.", Ty.Small, false, true, new Thickness(0, 6, 0, 0)));
                else if (installed && !a.Choice.Mod) status.Children.Add(Note("CCS Mod will be removed: the CCS HOME tile leaves Original Menu. Your games, saves and CCS HOME's files stay on the console." + (a.Choice.Nucleus != "none" && a.Choice.Start == "ccs-home" ? " What opens first changes to Original Menu (next page)." : ""), "warn"));
            }
            Status();
            cb.Checked += (s, e) => { a.Choice.Mod = true; Status(); };
            cb.Unchecked += (s, e) => { a.Choice.Mod = false; Status(); };
            left.Children.Add(status);
            left.Children.Add(Buttons(B("See CCS HOME", "Btn.Ghost", SeeHome)));
            g.Children.Add(left);
            // the featured card's picture: CCS HOME as the console draws it (Featured Shelf), which opens the four themes larger
            var ex = ThemeExamples().FirstOrDefault(x => x.Theme == "featured-shelf"); var pic = ThemePicture(ex, true);
            if (pic != null)
            {
                var img = new Image { Source = pic, Width = 240, Stretch = Stretch.Uniform }; RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                var frame = new Border { BorderBrush = Res<Brush>("B.Line2"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Child = img, Background = Res<Brush>("B.Surface2") };
                var pb = new Button { Content = frame, Style = Res<Style>("Wb.Plain"), VerticalAlignment = VerticalAlignment.Top };
                System.Windows.Automation.AutomationProperties.SetName(pb, "CCS HOME in its Featured Shelf theme. Select to see the four themes larger.");
                pb.Click += (s, e) => { SeeHome(); pb.Focus(); };
                Grid.SetColumn(pb, 1); g.Children.Add(pb);
            }
            return new Border { Background = Res<Brush>("B.Surface"), BorderBrush = Res<Brush>("B.Red"), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(10), Padding = new Thickness(20), Margin = new Thickness(0, 0, 0, 16), Child = g };
        }

        /// <summary>See CCS HOME: its four themes, as the console HOME draws them, in one picture viewer</summary>
        void SeeHome()
        {
            var items = ThemeExamples().Select(x => new { x, src = ThemePicture(x, false) }).Where(p => p.src != null)
                .Select(p => new GalleryPicture { Source = p.src, Title = "CCS HOME: " + p.x.Name, Caption = p.x.Description }).ToList();
            if (items.Count == 0) { Show(Message("No pictures of CCS HOME in this copy", "This copy of Workbench carries no example pictures of CCS HOME.", "info"), "msg"); return; }
            ShowGallery(items, 0, "Examples of CCS HOME's four themes with the NES Classic's 30 built-in games. Your own collection and design show in their place.");
        }

        // ------------------------------------------------------------------ page 2: What should open when you turn on the console?
        void ShowStartup()
        {
            var a = addons; if (a == null) return;
            var c = a.Choice; bool studio = !Standalone;
            bool nucleusInstalled = a.Has(Methods.Nucleus), lockNucleus = a.Prepare && nucleusInstalled, lockChooser = a.Prepare && a.Has(Methods.Chooser);
            // CCS HOME is never what opens first without CCS Mod: Original Menu takes its place, visibly
            if (!c.Mod && c.Start == "ccs-home") { c.Start = "native"; a.StartReplaced = true; }
            else if (c.Mod && a.StartReplaced) { c.Start = "ccs-home"; a.StartReplaced = false; }   // CCS Mod chosen again: what was replaced comes back
            var sp = SectionHead("What should open when you turn on the console?", null);
            var card = new StackPanel();
            var on = new CheckBox { IsChecked = c.Nucleus != "none", IsEnabled = !lockNucleus, Margin = new Thickness(0, 0, 0, 6),
                Content = new TextBlock { Text = NucleusQuestion, FontSize = Ty.Heading, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap } };
            System.Windows.Automation.AutomationProperties.SetName(on, NucleusQuestion);
            card.Children.Add(on);
            card.Children.Add(T("CCS Nucleus can open a menu straight away or show a startup chooser. It can be used with or without CCS HOME.", Ty.Text, false, true));
            if (lockNucleus) card.Children.Add(T("CCS Nucleus is installed on this console and stays. Removing it is in Console Restore: Add-ons and Startup.", Ty.Small, false, true));
            if (a.StartReplaced) card.Children.Add(Note("CCS HOME is not chosen, so Original Menu opens first instead. To open CCS HOME, go back and choose CCS Mod.", "info"));

            // off: the console's startup as it is (or, with CCS Nucleus installed, as it will be without it)
            var off = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            if (!nucleusInstalled)
            {
                off.Children.Add(T("Keep the console's current startup.", Ty.Body, true));
                off.Children.Add(T(a.Situation == "original" ? "Now: Nintendo's own menu opens when the console is turned on."
                    : a.OtherPackages > 0 ? "Now: Original Menu opens when the console is turned on, unless another add-on on this console changes that." : "Now: Original Menu opens when the console is turned on.", Ty.Text, false, true));
            }
            else off.Children.Add(Note("CCS Nucleus will be removed" + (a.Has(Methods.Chooser) ? ", with its startup chooser" : "") + ". Then Original Menu opens when the console is turned on" + (a.OtherPackages > 0 ? ", unless another add-on on this console changes that" : "") + "." + (a.OwnImages > 0 ? " Your own system images stay on the console as files, but without the startup chooser they do not start." : ""), "warn"));

            // on: straight away, or the startup chooser
            var onPanel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            onPanel.Children.Add(T("When the console turns on", Ty.Body, true));
            var direct = new RadioButton { GroupName = "nucleus-start", Content = "Open a menu straight away", IsChecked = c.Nucleus != "chooser", IsEnabled = !lockChooser, Margin = new Thickness(0, 4, 0, 4) };
            var chooser = new RadioButton { GroupName = "nucleus-start", Content = "Show the startup chooser", IsChecked = c.Nucleus == "chooser", Margin = new Thickness(0, 10, 0, 4) };
            System.Windows.Automation.AutomationProperties.SetName(direct, "Open a menu straight away"); System.Windows.Automation.AutomationProperties.SetName(chooser, "Show the startup chooser");
            var menus = new List<string> { "native" }; if (c.Mod) menus.Add("ccs-home");
            ComboBox MenuList(bool withChooser)
            {
                var list = new ComboBox();
                foreach (var m in menus) list.Items.Add(new ComboBoxItem { Content = MenuName(m), Tag = m });
                if (withChooser) { list.Items.Add(new ComboBoxItem { Content = "Last menu used", Tag = "last" }); list.Items.Add(new ComboBoxItem { Content = "Wait for me to choose", Tag = "wait" }); }
                return list;
            }
            void Select(ComboBox list, string tag) { foreach (ComboBoxItem it in list.Items) if ((string)it.Tag == tag) { list.SelectedItem = it; return; } list.SelectedIndex = 0; }
            var firstList = MenuList(false); Select(firstList, c.Start);
            var autoList = MenuList(true); Select(autoList, c.Auto == "menu" ? c.Start : c.Auto);
            var directBox = new StackPanel { Margin = new Thickness(26, 0, 0, 0) };
            directBox.Children.Add(Field("Open first:", firstList, null, 320));
            directBox.Children.Add(T("This menu opens immediately. The startup chooser is off.", Ty.Small, false, true));
            if (lockChooser) directBox.Children.Add(T("The startup chooser is installed on this console and stays. Turning it off is in Console Restore: Add-ons and Startup.", Ty.Small, false, true));
            var chooserBox = new StackPanel { Margin = new Thickness(26, 0, 0, 0) };
            chooserBox.Children.Add(Field("Start automatically with:", autoList, null, 320));
            var autoText = T("", Ty.Small, false, true); chooserBox.Children.Add(autoText);
            if (a.OwnImages > 0) chooserBox.Children.Add(T("Your own system images are choices in the startup chooser too.", Ty.Small, false, true));
            onPanel.Children.Add(direct); onPanel.Children.Add(directBox); onPanel.Children.Add(chooser); onPanel.Children.Add(chooserBox);

            void Refresh()
            {
                bool isOn = c.Nucleus != "none";
                off.Visibility = isOn ? Visibility.Collapsed : Visibility.Visible; onPanel.Visibility = isOn ? Visibility.Visible : Visibility.Collapsed;
                directBox.IsEnabled = c.Nucleus != "chooser"; chooserBox.IsEnabled = c.Nucleus == "chooser";
                var fallback = MenuName(c.Start);
                autoText.Text = c.Auto == "wait" ? "Nothing starts until you choose."
                    : c.Auto == "last" ? "If you do nothing, the menu you used last starts in " + AddonChoice.Countdown + " seconds. Until you have used one, that is " + fallback + "."
                    : "If you do nothing, " + fallback + " starts in " + AddonChoice.Countdown + " seconds.";
            }
            on.Checked += (s, e) =>
            {
                c.Nucleus = chooser.IsChecked == true ? "chooser" : "direct";
                // its first suggestion when it is turned on here: CCS HOME from Classic Cart Studio when it is chosen, else the console's own menu
                if (!a.NucleusTouched && a.Current.Nucleus == "none") { c.Start = studio && c.Mod ? "ccs-home" : "native"; Select(firstList, c.Start); Select(autoList, c.Start); }
                a.NucleusTouched = true; Refresh();
            };
            on.Unchecked += (s, e) => { c.Nucleus = "none"; a.NucleusTouched = true; Refresh(); };
            direct.Checked += (s, e) => { if (c.Nucleus != "none") c.Nucleus = "direct"; Refresh(); };
            chooser.Checked += (s, e) => { if (c.Nucleus != "none") c.Nucleus = "chooser"; Refresh(); };
            firstList.SelectionChanged += (s, e) => { if ((firstList.SelectedItem as ComboBoxItem)?.Tag is string t) { c.Start = t; a.StartReplaced = false; if (c.Auto == "menu") Select(autoList, t); Refresh(); } };
            autoList.SelectionChanged += (s, e) =>
            {
                if (!((autoList.SelectedItem as ComboBoxItem)?.Tag is string t)) return;
                if (t == "last" || t == "wait") c.Auto = t; else { c.Auto = "menu"; c.Start = t; a.StartReplaced = false; Select(firstList, t); }
                Refresh();
            };
            card.Children.Add(off); card.Children.Add(onPanel);
            card.Children.Add(Details(TechNes, "CCS Nucleus is the hakchi package ccs-nucleus; its startup chooser is ccs-nucleus-chooser, installed after it. CCS Mod is ccs-mod. Each is placed in hakchi's own transfer folder and installed by hakchi at the console's next normal start.",
                "What opens first is CCS Nucleus's own setting on the console's data storage (default menu, chooser shown or not, the " + AddonChoice.Countdown + "-second countdown, last used or waiting). CCS Mod adds nothing that runs at power-on.",
                a.StartNow != null ? "Now: " + a.StartNow : null));
            sp.Children.Add(Card(card));
            Refresh();
            var review = B("Review Changes", "Btn.Primary", () =>
            {
                var why = c.Problem(); if (why != null) { Show(Message("This choice cannot be made", why, "bad"), "msg"); return; }
                SendAddons(c);
            });
            var nothing = T("This is what the console has now. Change something to review it.", Ty.Small, false, true);
            if (!a.Prepare)
            {   // Console Restore: a review needs a change
                void Same() { bool same = c.SameAs(a.Current); review.IsEnabled = !same; nothing.Visibility = same ? Visibility.Visible : Visibility.Collapsed; }
                on.Checked += (s, e) => Same(); on.Unchecked += (s, e) => Same(); direct.Checked += (s, e) => Same(); chooser.Checked += (s, e) => Same();
                firstList.SelectionChanged += (s, e) => Same(); autoList.SelectionChanged += (s, e) => Same();
                Same();
            }
            else nothing.Visibility = Visibility.Collapsed;
            sp.Children.Add(Buttons(B("Back: Add-ons", "Btn", ShowAddons), review));
            sp.Children.Add(nothing);
            Show(sp, "startup");
        }

        // ------------------------------------------------------------------ Console Restore: Add-ons and Startup
        void ShowMethods()
        {
            var sp = SectionHead("Add-ons and Startup", "Add or remove CCS Mod and CCS Nucleus on your " + Con + ", and choose what opens when it is turned on. Workbench first reads what the console has; the next pages start from that.");
            var what = new StackPanel();
            what.Children.Add(T("The two add-ons", Ty.Heading, true, false, new Thickness(0, 0, 0, 8)));
            what.Children.Add(KeyValues(("CCS Mod — CCS HOME", "A menu for your game collections, opened from its tile in Original Menu. It adds nothing that runs when the console is turned on."),
                                        ("CCS Nucleus", "What opens when the console is turned on: a menu straight away, or a startup chooser. It works with or without CCS HOME.")));
            what.Children.Add(T("Each is added or removed on its own, and you review every change first. Removing one takes away only its own files. Games, saves, settings, other hakchi packages and your system images stay, and the change takes effect the next time the console starts normally.", Ty.Text));
            what.Children.Add(T("The console must run Hakchi2 CE 3.9.3; any other setup is refused before anything is written.", Ty.Small, false, true, new Thickness(0)));
            what.Children.Add(Details(TechNes, "CCS Mod is the hakchi package ccs-mod (ccs-mod.hmod); CCS Nucleus is ccs-nucleus (ccs-nucleus.hmod) and its startup chooser ccs-nucleus-chooser (ccs-nucleus-chooser.hmod), all three pinned in this build's preparation program.",
                "Adding places a package in hakchi's transfer folder; removing places a removal list there (the startup chooser before CCS Nucleus). hakchi applies them at the console's next normal start and restarts the console once. Only a removed package's unchanged files are removed; its settings stay on the console as data. What opens first is set in CCS Nucleus's own setting on the data storage.",
                "Only files on the console's data storage are written (hakchi's transfer folder, CCS Nucleus's setting and CCS HOME's folder); no kernel, bootloader or system is changed."));
            sp.Children.Add(Card(what));
            if (string.IsNullOrEmpty(opt.SimulatedConsole) || !profile.ConsoleTasks) sp.Children.Add(ConnectSteps("Nothing changes until you confirm; the change takes effect at the console's next normal start.", "method-plan"));
            // each action first reads the console's add-ons (nothing is written then), then opens its page or its review
            void Check(string intent)
            {
                methodTag = null; addons = null; addonIntent = intent; var q = ConsoleReq("method-plan"); q.Add("choose", true);
                StartOp("method", "Checking the Console", "Workbench reads which add-ons the console has. Nothing is written in this step. Keep the USB cable connected.", q);
            }
            var acts = CardGrid(240, 4,
                ActionCard("Manage Add-ons", "Add or remove CCS Mod and CCS Nucleus, then choose what opens first.", "Btn.Primary", () => Check("manage"), "Manage Add-ons…"),
                ActionCard("Change Startup", "Choose what opens when the console is turned on: a menu straight away, or the startup chooser.", "Btn", () => Check("startup"), "Change Startup…"),
                ActionCard("Remove CCS Mod", "The CCS HOME tile leaves Original Menu. Games, saves and CCS HOME's files stay on the console.", "Btn", () => Check("remove-mod"), "Remove CCS Mod…"),
                ActionCard("Remove CCS Nucleus", "Original Menu opens when the console is turned on. The CCS HOME tile and its data stay.", "Btn", () => Check("remove-nucleus"), "Remove CCS Nucleus…"));
            sp.Children.Add(acts);
            sp.Children.Add(Buttons(B("Back", "Btn.Ghost", BackToSection)));
            Show(sp, "methods");
        }

        // ------------------------------------------------------------------ Console Composer: an owner's system image as a CCS Nucleus choice
        void ShowFirmware()
        {
            var sp = SectionHead("Your System Images", "Add a system image file of your own from this PC as a start choice in CCS Nucleus on your " + Con + ", or remove one you added. This changes files on the console: you review it first, and the console is backed up before anything is written.");
            var how = new StackPanel();
            how.Children.Add(T("How it works", Ty.Heading, true, false, new Thickness(0, 0, 0, 8)));
            how.Children.Add(Numbered("You choose a system image file on this PC. Workbench does not supply or download system images.",
                                      "Workbench checks the file and tells you which system it holds, in the review, before anything is written.",
                                      "The image becomes a choice in CCS Nucleus's chooser. It starts only when you pick it, for that one start. When you don't pick it, your console starts its own system as before, and that stays your way back."));
            how.Children.Add(T("CCS Nucleus must be installed on the console with its startup chooser (Console Restore: Add-ons and Startup).", Ty.Small, false, true, new Thickness(0, 4, 0, 0)));
            how.Children.Add(Details(TechNes, "A system image is a SquashFS file (.hsqs) holding a console system's root file system. It is checked with hakchi's own rules, and a kernel file is refused.",
                "It goes to CCS Nucleus's own folder on the console's data storage, never to hakchi's firmware folder (hakchi starts the first image there by itself, without a choice). The image, room kept for saves and a small safety margin must fit on the data storage.",
                "The console must run Hakchi2 CE 3.9.3's hakchi with CCS Nucleus installed; its list of start choices is read before anything is written."));
            sp.Children.Add(Card(how));
            var c = new StackPanel();
            c.Children.Add(T("Add an image", Ty.Heading, true, false, new Thickness(0, 0, 0, 10)));
            var label = new TextBox(); System.Windows.Automation.AutomationProperties.SetName(label, "Name shown in the chooser (optional)");
            c.Children.Add(Field("Name shown in the chooser (optional)", label, null, 420));
            c.Children.Add(T("Left empty, the name follows the system the image holds; the review shows which system that is.", Ty.Small, false, true, new Thickness(0, 0, 0, 8)));
            c.Children.Add(Buttons(B("Choose an Image File and Review…", "Btn.Primary", () =>
            {
                var d = new Microsoft.Win32.OpenFileDialog { Title = "Choose a system image file (.hsqs)", Filter = "System images (*.hsqs)|*.hsqs" }; if (d.ShowDialog(this) != true) return;
                methodTag = null; var q = ConsoleReq("firmware-plan"); q.Add("op", "add"); q.Add("file", d.FileName); q.Add("label", label.Text.Trim());
                StartOp("firmware", "Preparing the Review", "The image file is checked on this PC, then the connected console is identified and backed up. Nothing is written until you confirm. Keep the USB cable connected.", q);
            })));
            var id = new TextBox(); System.Windows.Automation.AutomationProperties.SetName(id, "Image to remove (its id)");
            c.Children.Add(new Border { Height = 1, Background = Res<System.Windows.Media.Brush>("B.Line"), Margin = new Thickness(0, 10, 0, 16) });
            c.Children.Add(T("Remove an image", Ty.Heading, true, false, new Thickness(0, 0, 0, 10)));
            c.Children.Add(Field("The image's id (shown when it was added)", id, null, 420));
            var remove = B("Review Removing It…", "Btn", () =>
            {
                if (string.IsNullOrWhiteSpace(id.Text)) return;
                methodTag = null; var q = ConsoleReq("firmware-plan"); q.Add("op", "remove"); q.Add("id", id.Text.Trim());
                StartOp("firmware", "Preparing the Review", "The connected console is identified and backed up first. Nothing is written until you confirm. Keep the USB cable connected.", q);
            }, false);
            var need = T("Type the id of the image to remove.", Ty.Small, false, true, new Thickness(0, 0, 0, 0));
            id.TextChanged += (s, e) => { remove.IsEnabled = !string.IsNullOrWhiteSpace(id.Text); need.Visibility = remove.IsEnabled ? Visibility.Collapsed : Visibility.Visible; };
            c.Children.Add(Buttons(remove)); c.Children.Add(need);
            sp.Children.Add(Card(c));
            if (string.IsNullOrEmpty(opt.SimulatedConsole) || !profile.ConsoleTasks) sp.Children.Add(ConnectSteps("Nothing changes until you confirm.", "firmware-plan"));
            sp.Children.Add(Buttons(B("Back", "Btn.Ghost", () => { if (LeaveOk()) BackToSection(); })));
            Show(sp, "firmware");
            LeaveGuard(() => label.Text.Trim().Length == 0 && id.Text.Trim().Length == 0 ? null : "What you typed on Your System Images",
                       "What you typed on this page is not kept if you leave it.");
        }

        /// <summary>an add-on step in plain words (the plan's own removal list named)</summary>
        static string AddonStep(string s, Dictionary<string, object> p)
        {
            if (s == "unlist")
            {
                var names = (p.O("removal_list")?.S("names") ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(Methods.Label);
                return "Removal list placed for hakchi (" + string.Join(", ", names) + ")";
            }
            return MethodEngine.StepLabel(s);
        }

        /// <summary>the review's plain summary of the add-ons after the change (target) beside what the console has (current): CCS HOME,
        /// Original Menu, the startup and, with CCS Nucleus, the chooser or what starts if nobody chooses</summary>
        UIElement AddonSummary(AddonChoice target, AddonChoice current)
        {
            target = target ?? new AddonChoice(); current = current ?? new AddonChoice();
            var rows = new List<(string, string)>();
            rows.Add(("CCS HOME", target.Mod ? (current.Mod ? "Keep, with CCS Mod" : "Add with CCS Mod") : current.Mod ? "Remove CCS Mod (its files stay on the console)" : "Not selected"));
            if (target.Mod || current.Mod) rows.Add(("Original Menu", target.Mod ? "Keep, with the CCS HOME tile" : "Keep, without the CCS HOME tile"));
            if (target.Nucleus == "none") rows.Add(("Startup", current.Nucleus != "none" ? "Remove CCS Nucleus: Original Menu opens" : "Keep current startup"));
            else
            {
                var menu = MenuName(target.Start);
                rows.Add(("Startup", "CCS Nucleus — " + (target.Nucleus == "direct" ? "open " + menu + " immediately" : "show the startup chooser")));
                if (target.Nucleus == "direct") rows.Add(("Startup chooser", current.Nucleus == "chooser" ? "Off (removed)" : "Off"));
                else rows.Add(("If you do nothing", target.Auto == "wait" ? "Nothing starts until you choose"
                    : target.Auto == "last" ? "The menu used last starts after " + AddonChoice.Countdown + " seconds (" + menu + " until you have used one)"
                    : menu + " starts after " + AddonChoice.Countdown + " seconds"));
            }
            return KeyValues(rows.ToArray());
        }

        static AddonChoice ChoiceOf(Dictionary<string, object> j) => j == null ? null : new AddonChoice { Mod = j.B("mod"), Nucleus = j.S("nucleus") ?? "none", Start = j.S("start") ?? "native", Auto = j.S("auto") ?? "menu" };

        // the plans and results of both (worker events method-plan / firmware-plan; result operation method / firmware)
        void ShowMethodOrFirmwarePlan(string kind, Dictionary<string, object> p)
        {
            bool fw = kind == "firmware"; string op = p.S("operation"); methodTag = p.S("console_tag");
            var target = fw ? null : ChoiceOf(p.O("target"));
            var adds = fw ? new List<string>() : (p.A("add") ?? new List<object>()).OfType<Dictionary<string, object>>().Select(x => x.S("name")).ToList();
            var removes = fw ? new List<string>() : (p.O("removal_list")?.S("names") ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            var title = fw ? (op == "remove" ? "Review: Remove a System Image" : "Review: Add a System Image") : "Review: Add-ons and Startup";
            var sp = SectionHead(title, "What this would change on your " + Con + ", console " + methodTag + ", the console connected to this PC now. Nothing has been written yet.");
            var originalNote = OriginalUnavailableNote(methodTag); if (originalNote != null) sp.Children.Add(originalNote);
            var c = new StackPanel();
            if (!fw) c.Children.Add(AddonSummary(target, ChoiceOf(p.O("current"))));
            if (p.S("text") != null) c.Children.Add(T(p.S("text"), fw ? Ty.Heading : Ty.Text, fw, false, new Thickness(0, fw ? 0 : 4, 0, 8)));
            var img = p.O("image"); var sys = p.O("system");
            c.Children.Add(KeyValues(("Console", "Console " + methodTag + ", connected to this PC now"),
                                     ("Image file", fw && op != "remove" && img != null ? Path.GetFileName(img.S("file") ?? "") + ", " + (img.S("size") ?? Size(img.L("bytes"))) : null),
                                     ("System in the file", fw && sys != null ? sys.S("text") : null),
                                     ("Adds", fw || adds.Count == 0 ? null : string.Join(", ", adds.Select(Methods.Label))),
                                     ("Removes", fw || removes.Count == 0 ? null : string.Join(", ", removes.Select(Methods.Label))),
                                     ("Takes effect", fw ? null : "The next time the console starts normally; it then restarts once"),
                                     ("Backed up first", p.S("protective_backup") != null ? Path.GetFileName(p.S("protective_backup")) + ", made in this session" : null)));
            if (!fw) c.Children.Add(Note("Your games and saves stay. Only files on the console's data storage are written.", "info"));
            var steps = (p.A("steps") ?? new List<object>()).OfType<string>().ToList();
            string Label(string s) => fw ? (FirmwareEngine.Steps.Contains(s) ? FirmwareEngine.StepLabel(s) : s) : AddonStep(s, p);
            c.Children.Add(T("What is written, in order", Ty.Body, true, false, new Thickness(0, 6, 0, 6)));
            // the plan names its steps by code (the engine's step list); the page shows the engine's own words for them
            c.Children.Add(Numbered(steps.Select(s => Cap(Label(s))).ToArray()));
            var notes = (p.A("notes") ?? new List<object>()).OfType<string>().ToList();
            if (notes.Count > 0) { c.Children.Add(T("Notes", Ty.Body, true, false, new Thickness(0, 6, 0, 6))); foreach (var n in notes) c.Children.Add(T("• " + n, Ty.Text)); }
            if (!fw && EarlierHomeReview(p.O("earlier_home"), () => { Enter("Console Restore"); ShowAddons(); }, "Back to Add-ons and Startup", p.O("home")?.S("size")) is UIElement eh) c.Children.Add(eh);
            if (p.S("refusal") != null) c.Children.Add(Note(p.S("refusal"), "bad"));
            var tech = new List<object> { "Steps: " + string.Join(", ", steps) };
            if (p.O("package") is Dictionary<string, object> pk) tech.Add("Package: " + pk.S("name") + ".hmod, SHA-256 " + pk.S("sha256"));
            foreach (var a in (p.A("add") ?? new List<object>()).OfType<Dictionary<string, object>>()) tech.Add("Package: " + a.S("name") + ".hmod, SHA-256 " + a.S("sha256"));
            if (p.O("removal_list") is Dictionary<string, object> rl) tech.Add("Removal list for: " + rl.S("names") + ", SHA-256 " + rl.S("sha256"));
            if (p.S("start") != null) tech.Add("CCS Nucleus's start setting (default, chooser, CCS HOME offered, selection, countdown): " + p.S("start"));
            if (p.O("home") is Dictionary<string, object> hm) tech.Add("CCS HOME's package: copy " + hm.S("copy") + (hm.S("size") != null ? ", " + hm.S("size") : "") + (hm.S("replaces") != null ? ", replaces " + hm.S("replaces") : ""));
            if (img != null && fw && op != "remove") tech.Add("Image: " + img.S("file") + "; SHA-256 " + img.S("sha256") + "; " + img.L("bytes").ToString("N0", En) + " bytes");
            if (fw && p.S("id") != null) tech.Add("Choice id: " + p.S("id") + (p.S("label") != null ? "; name in the chooser: " + p.S("label") : ""));
            if (sys != null) tech.Add("System: platform " + sys.S("platform") + ", region " + sys.S("region") + ", board " + sys.S("board") + ", compression " + sys.S("compression") + (sys.S("version") != null ? ", version " + sys.S("version") : ""));
            if (p.O("console_state") is Dictionary<string, object> cs) tech.Add("The console now: hakchi " + cs.S("hakchi") + "; packages " + string.Join(", ", (cs.A("packages") ?? new List<object>()).OfType<string>()) + "; start " + cs.S("start"));
            if (p.S("protective_backup") != null) tech.Add("Backup made just now: " + p.S("protective_backup"));
            tech.Add("Review digest " + p.S("digest") + "; the confirmation is good until " + (p.S("expires_utc") ?? "unknown") + " (UTC), once.");
            c.Children.Add(Details("Technical details", tech.ToArray()));
            sp.Children.Add(Card(c));
            var section = fw ? "Console Composer" : "Console Restore";
            if (p.S("refusal") != null || p.S("nonce") == null) { sp.Children.Add(Buttons(B("Done", "Btn", () => Leave(section)))); Show(sp, "method-plan"); return; }
            var go = fw ? (op == "remove" ? "Remove Now" : "Add Now") : "Apply Changes";
            sp.Children.Add(Buttons(B(go, "Btn.Primary", () =>
            {
                progress = new ProgressView(this, fw ? (op == "remove" ? "Removing the System Image" : "Adding the System Image") : "Changing the Add-ons",
                    "Do not unplug the console. Each step is written once and read back; the first problem stops every later step. Stop After This Step finishes the step in progress, then stops.", "Stop After This Step", () => { worker?.Cancel(); progress.Say("Stopping after this step..."); });
                Show(progress.Panel, "progress");
                worker?.Send(new JObj { { "cmd", fw ? "firmware-confirm" : "method-confirm" }, { "digest", p.S("digest") }, { "nonce", p.S("nonce") } });
            }), B("Cancel", "Btn.Ghost", () => Leave(section))));
            Show(sp, "method-plan");
        }

        /// <summary>the title and first sentence of a console change's result that did not complete (states from the engine: nothing-to-do,
        /// canceled, stopped, refused). A refusal after a step was written reads as stopped.</summary>
        static (string title, string text) NotDone(string st, int doneCount, string unit) =>
            st == "nothing-to-do" ? ("Nothing needed to change", "Nothing was written to the console.")
            : st == "canceled" ? ("Canceled: nothing was changed", null)
            : st == "stopped" || doneCount > 0 ? ("Stopped before every " + unit + " was written", "The first problem stopped every later " + unit + ", and nothing was retried. What happened is below.")
            : ("Nothing was changed", "The console was not changed. The reason is below.");

        void MethodOrFirmwareResult(string kind, Dictionary<string, object> r)
        {
            bool fw = kind == "firmware"; var st = r.S("state"); bool ok = st == "scheduled" || st == "added" || st == "removed";
            var (failTitle, failText) = NotDone(st, (r.A("done") ?? new List<object>()).Count, "step");
            var title = st == "scheduled" ? "Change scheduled for the next start" : st == "added" ? "System image added" : st == "removed" ? "System image removed" : failTitle;
            var sp = SectionHead(title, ok ? "Written to your " + Con + (methodTag != null ? ", console " + methodTag : "") + ", and read back." : failText);
            var text = st == "scheduled" ? "At the console's next normal start, hakchi makes the change and restarts the console once. Your games and saves stay."
                : st == "added" ? "It is a choice in CCS Nucleus's chooser, and starts only when you pick it." + (r.S("id") != null ? " Its id is " + r.S("id") + ": you need it to remove the image later." : "")
                : st == "removed" ? "The choice and its image file are gone from the console. Your console's own system is unchanged."
                : r.S("problem") ?? st ?? "";
            sp.Children.Add(Note(text, ok ? "ok" : st == "canceled" ? "warn" : "bad"));
            if (r.S("protective") != null) { sp.Children.Add(T("The backup of the console made just before is kept:", Ty.Small, false, true)); sp.Children.Add(PathText(r.S("protective"))); }
            var done = (r.A("done") ?? new List<object>()).OfType<string>().ToList();
            sp.Children.Add(Details("Technical details", done.Count > 0 ? "Steps done: " + string.Join(", ", done) : null, r.S("id") != null ? "Choice id: " + r.S("id") : null, r.S("record") != null ? "Record: " + r.S("record") : null, "Result: " + st));
            var section = fw ? "Console Composer" : "Console Restore";
            sp.Children.Add(Buttons(B("Restart the Console", ok ? "Btn.Primary" : "Btn.Ghost", () => worker?.Send(new JObj { { "cmd", "finish" } })),
                                    B("Done", ok ? "Btn.Ghost" : "Btn", () => { worker?.Send(new JObj { { "cmd", "quit" } }); Go(section); })));
            Show(sp, "result");
        }
    }
}
