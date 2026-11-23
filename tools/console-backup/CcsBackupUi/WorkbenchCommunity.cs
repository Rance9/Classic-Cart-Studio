// Classic Cart Studio console backup and restore window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Community Mods (in Console Composer): the curated catalog of other authors' mods, and their installation, update and removal through
// Hakchi's own route (the engine's CommunityEngine, one console session per change).
//   Browse     the catalog's sections in their fixed order (Menus, Utilities, Themes, Emulators), each entry in the catalog's own order, as standard
//              cards; Details; the honest empty state when nothing is listed (none is in this version)
//   Installed  what Workbench installed on each console (the PC's ledger, checked again at every connection); Remove (keeping the mod's
//              settings and favorites, or also removing them), Put Back Kept Settings, Restore the Pre-install Backup (a full restore)
//   Updates    an update only when a newer version is approved; never automatic
// A change: the console's mods read (nothing written) → the approved file for each mod to install, downloaded only into a folder the person
// chooses or chosen on this PC → the review after a fresh backup → the one-time confirmation → the result. The add-ons page shows the
// catalog's cards under CCS Mod's (CommunityCards). With a simulated console only, CCS_COMMUNITY_CATALOG names a private fixture catalog
// for tests and demonstrations; a real console always uses the catalog this build carries.
using CcsBackup.Engine;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CcsBackup.Ui
{
    public sealed partial class BackupWorkspace
    {
        public const string CommunityScope = "Community add-ons are designed for Original Menu — Nintendo's own menu. Their features and settings may not work in CCS HOME or other menus. Each card explains where it applies.";
        CommunityCatalog commCat; string commCatProblem; bool commCatRead;
        string commView = "browse";
        /// <summary>entries chosen on the add-ons page, offered on the Community Mods page</summary>
        readonly HashSet<string> commPick = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>each admitted package file on this PC, by its SHA-256 (the approved pin)</summary>
        readonly Dictionary<string, string> commFiles = new Dictionary<string, string>(StringComparer.Ordinal);
        CommunityChange commPending; Dictionary<string, object> commState; CommunityResolution commRes;

        string CommFixture => string.IsNullOrEmpty(opt.SimulatedConsole) ? null : Environment.GetEnvironmentVariable("CCS_COMMUNITY_CATALOG");
        CommunityCatalog CommCatalog()
        {
            if (commCatRead) return commCat;
            commCatRead = true;
            var f = CommFixture;
            if (string.IsNullOrEmpty(f)) { (commCat, commCatProblem) = CommunityCatalog.Shipping(); return commCat; }
            try { commCat = CommunityCatalog.Parse(File.ReadAllBytes(f)); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is CommunityCatalogException) { commCatProblem = "The test catalog cannot be used (" + ex.Message + ")."; }
            return commCat;
        }

        // ------------------------------------------------------------------ the page and its three views
        void ShowCommunity(string view = null)
        {
            if (view != null) commView = view;
            var cat = CommCatalog();
            var sp = SectionHead("Community Mods", "Mods other authors make for your " + Con + ", installed and removed through Hakchi's own route. A mod is listed only after its author agrees and its exact version is reviewed. You review every change, and the console is backed up before anything is written.");
            Button View(string text, string v) => B(text, commView == v ? "Btn.Primary" : "Btn.Ghost", () => ShowCommunity(v));
            sp.Children.Add(Buttons(View("Browse", "browse"), View("Installed", "installed"), View("Updates", "updates")));
            sp.Children.Add(new Border { Height = 8 });
            if (commView == "installed") CommInstalledView(sp, cat); else if (commView == "updates") CommUpdatesView(sp, cat); else CommBrowseView(sp, cat);
            sp.Children.Add(Buttons(B("Back", "Btn.Ghost", () => Go("Console Composer"))));
            Show(sp, "community");
        }

        void CommBrowseView(StackPanel sp, CommunityCatalog cat)
        {
            if (cat == null) { sp.Children.Add(Note(commCatProblem ?? "The community catalog cannot be read.", "bad")); return; }
            if (cat.Empty) { sp.Children.Add(CommEmpty()); return; }
            sp.Children.Add(Note(CommunityScope, "info"));
            var picked = commPick.Select(cat.Find).Where(r => r != null).ToList();
            if (picked.Count > 0)
            {
                var c = new StackPanel();
                c.Children.Add(T("Chosen on the add-ons page: " + string.Join(", ", picked.Select(r => r.Name)), Ty.Body, true));
                c.Children.Add(Buttons(B("Install These…", "Btn.Primary", () => StartComm(new CommunityChange { Install = picked.Select(r => r.Id).ToList() })), B("Clear", "Btn.Ghost", () => { commPick.Clear(); ShowCommunity("browse"); })));
                sp.Children.Add(Card(c));
            }
            foreach (var sec in CommunityCatalog.SectionOrder)
            {
                var list = cat.Section(sec); if (list.Count == 0) continue;
                sp.Children.Add(Group(CommunityCatalog.SectionTitle(sec)));
                sp.Children.Add(CardGrid(300, 2, list.Select(r => CommCard(r)).ToArray()));
            }
        }

        UIElement CommEmpty()
        {
            var c = new StackPanel();
            c.Children.Add(T("No community mods are listed yet", Ty.CardTitle, true, false, new Thickness(0, 0, 0, 8)));
            c.Children.Add(T("A mod appears here once its author agrees to have it listed and its exact version has been reviewed. None is listed in this version of Workbench, so there is nothing to install here yet.", Ty.Text));
            c.Children.Add(T("Mods you added with other tools stay on your console as they are. Workbench does not change or remove them.", Ty.Text, false, false, new Thickness(0, 8, 0, 0)));
            c.Children.Add(T("Classic Cart Studio's own add-ons, CCS Mod (CCS HOME) and CCS Nucleus, are in Console Restore: Add-ons and Startup.", Ty.Text, false, false, new Thickness(0, 8, 0, 0)));
            c.Children.Add(Buttons(B("Add-ons and Startup", "Btn", () => { Enter("Console Restore"); ShowMethods(); })));
            return Card(c);
        }

        /// <summary>an entry's standard card: name, Alpha, version, author and license, what it does and where it applies, the download and
        /// its size on the console, Details and Install</summary>
        UIElement CommCard(CommunityRecord r, CheckBox pick = null, string consoleTag = null)
        {
            // where Workbench installed it: on the add-ons page, only this console counts (its removal is in Community Mods)
            var on = CommunityLedger.All(settings).Where(kv => kv.Value.Any(m => m.Id == r.Id && m.State != CommunityLedger.Removing)).Select(kv => kv.Key).ToList();
            bool here = consoleTag != null && on.Contains(consoleTag);
            if (pick != null && here) { pick.IsChecked = false; pick.IsEnabled = false; }
            var sp = new StackPanel();
            if (pick != null) { pick.Content = new TextBlock { Text = r.Name, FontSize = Ty.CardTitle, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap }; pick.Margin = new Thickness(0, 0, 0, 6); System.Windows.Automation.AutomationProperties.SetName(pick, r.Name); sp.Children.Add(pick); }
            else sp.Children.Add(T(r.Name, Ty.CardTitle, true, false, new Thickness(0, 0, 0, 6)));
            if (r.Alpha) sp.Children.Add(Pill("Alpha", "B.Warn"));
            sp.Children.Add(T("Version " + r.Version + " · By " + r.AuthorName + " · " + r.LicenseName, Ty.Small, false, true, new Thickness(0, 4, 0, 6)));
            sp.Children.Add(T(r.Summary, Ty.Text));
            sp.Children.Add(T(r.Applies, Ty.Text, true, false, new Thickness(0, 6, 0, 0)));
            var v = r.DefaultVariant;
            sp.Children.Add(T("Download " + Units.Gb(v.Bytes) + " · about " + Units.Gb(v.UnpackedBytes) + " on the console", Ty.Small, false, true, new Thickness(0, 6, 0, 0)));
            if (pick != null ? here : on.Count > 0) sp.Children.Add(Pill(pick != null ? "Installed on this console" : "Installed by Workbench (console " + string.Join(", ", on) + ")", "B.Ok"));
            if (pick != null && here) sp.Children.Add(T("It stays. Removing it is in Community Mods.", Ty.Small, false, true, new Thickness(0, 6, 0, 0)));
            sp.Children.Add(Buttons(B("Details", "Btn.Ghost", () => ShowCommDetails(r)), pick == null ? B("Install…", "Btn", () => StartComm(new CommunityChange { Install = { r.Id } })) : null));
            var c = Card(sp); c.Margin = new Thickness(0); return c;
        }

        /// <summary>everything the record says, in plain words first and the exact pins under Technical details; the author's links open in
        /// the browser</summary>
        void ShowCommDetails(CommunityRecord r)
        {
            var sp = SectionHead(r.Name, r.Summary);
            var c = new StackPanel();
            var cat = CommCatalog();
            string Names(IEnumerable<string> ids) => string.Join(", ", ids.Select(i => cat?.Find(i)?.Name ?? i));
            c.Children.Add(KeyValues(("Version", r.Version + (r.Alpha ? " (Alpha)" : "")), ("Where it applies", r.Applies), ("Author", r.AuthorName + (r.Credits.Count > 0 ? "; also credited: " + string.Join(", ", r.Credits) : "")),
                ("License", r.LicenseName), ("Needs", r.Depends.Count == 0 ? "Nothing else" : Names(r.Depends)), ("Cannot be used with", r.Conflicts.Count == 0 ? "Nothing listed" : Names(r.Conflicts.Select(x => x.id))),
                ("Its install also removes", r.RemovesOnInstall.Count == 0 ? "Nothing" : string.Join(", ", r.RemovesOnInstall.Select(x => x.name)) + " (if installed)"),
                ("USB drive or SD card", r.WritesExternal ? r.ExternalText : "Not used"), ("Removing it", (r.RemovalText ?? "Hakchi runs its own removal.") + (r.Settings.Count > 0 ? " Workbench keeps its settings and favorites unless you choose to remove them." : "")),
                ("Download", Units.Gb(r.DefaultVariant.Bytes) + ", about " + Units.Gb(r.DefaultVariant.UnpackedBytes) + " on the console")));
            if (r.Description != null) c.Children.Add(T(r.Description, Ty.Text, false, false, new Thickness(0, 8, 0, 0)));
            foreach (var w in r.Warnings) c.Children.Add(Note("Its author says: " + w, "warn"));
            if (r.Conflicts.Count > 0) foreach (var x in r.Conflicts) c.Children.Add(Note(x.text, "info"));
            var links = new List<UIElement> { B("Project Page", "Btn.Ghost", () => Launch(r.ProjectUrl)), r.SupportUrl == null ? null : B("Support", "Btn.Ghost", () => Launch(r.SupportUrl)), B("License", "Btn.Ghost", () => Launch(r.LicenseUrl)), B("Source", "Btn.Ghost", () => Launch(r.SourceUrl)) };
            c.Children.Add(Buttons(links.ToArray()));
            c.Children.Add(Details("Technical details", new object[] { CommunityEngine.PrivilegeNote }
                .Concat(r.Variants.Select(v => (object)((v.IsDefault ? "File" : "File (" + v.Variant + ": " + v.Reason + ")") + ": " + v.Url + " · " + v.Bytes.ToString("N0", En) + " bytes · SHA-256 " + v.Sha256 + " · hakchi package " + v.Package)))
                .Concat(new object[] { "Installs into Hakchi's own files: " + string.Join(", ", r.PayloadPaths), r.ScriptWrites.Count > 0 ? "Its scripts also change: " + string.Join("; ", r.ScriptWrites) : null,
                    "For: " + string.Join(", ", r.Consoles) + (r.Regions.Count > 0 ? " (" + string.Join(", ", r.Regions) + ")" : "") + ", Hakchi2 CE 3.9.3 (" + r.Hakchi + ")",
                    "Starts as: " + r.LaunchKind + (r.LaunchReturn != null ? "; " + r.LaunchReturn : ""), r.CompanionId != null ? "Companion: " + r.CompanionId + " " + r.CompanionVersion : null,
                    "Updates: " + (r.UpdateMethod == "install-over" ? "the new version is installed over this one" : "this one is removed first, then the new one installed") + (r.UpdatesFrom.Count > 0 ? "; updates " + string.Join(", ", r.UpdatesFrom.Select(u => u.version)) : "") }).ToArray()));
            sp.Children.Add(Card(c));
            sp.Children.Add(Buttons(B("Install…", "Btn.Primary", () => StartComm(new CommunityChange { Install = { r.Id } })), r.Shots.Count > 0 ? B("Show Pictures", "Btn", () => CommPictures(r)) : null, B("Back", "Btn.Ghost", () => ShowCommunity())));
            Show(sp, "community-details");
        }

        /// <summary>the permitted pictures, fetched from their approved addresses and shown only when their bytes are the approved ones</summary>
        void CommPictures(CommunityRecord r)
        {
            var pv = new ProgressView(this, "Getting the Pictures", "Each picture is shown only when it is exactly the approved one.", "Back", () => ShowCommDetails(r));
            Show(pv.Panel, "progress");
            Task.Run(() =>
            {
                var got = new List<(byte[] b, CommunityShot s)>(); string bad = null;
                foreach (var s in r.Shots)
                {
                    try { using (var src = CommunityDownload.Open(s.Url)) { var m = new MemoryStream(); var buf = new byte[65536]; int n; while ((n = src.Read(buf, 0, buf.Length)) > 0 && m.Length <= s.Bytes) m.Write(buf, 0, n); var b = m.ToArray(); var why = CommunityAsset.Check(b, s); if (why == null) got.Add((b, s)); else bad = s.Caption + ": " + why; } }
                    catch (Exception ex) { bad = s.Caption + ": it could not be downloaded (" + ex.Message + ")"; }
                }
                Dispatcher.Invoke(() =>
                {
                    var items = new List<GalleryPicture>();
                    foreach (var (b, s) in got) { try { var bi = new System.Windows.Media.Imaging.BitmapImage(); bi.BeginInit(); bi.StreamSource = new MemoryStream(b); bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; bi.EndInit(); bi.Freeze(); items.Add(new GalleryPicture { Source = bi, Title = r.Name, Caption = s.Caption }); } catch (Exception) { bad = s.Caption + ": it is not a picture"; } }
                    ShowCommDetails(r);
                    if (items.Count > 0) ShowGallery(items, 0, "Pictures from " + r.AuthorName + ", shown with permission.");
                    else Show(Message("No picture could be shown", (bad ?? "No picture is available.") + " A picture is shown only when it is exactly the approved one.", "warn"), "msg");
                });
            });
        }

        void CommInstalledView(StackPanel sp, CommunityCatalog cat)
        {
            var all = CommunityLedger.All(settings);
            if (all.Count == 0)
            {
                var c = new StackPanel();
                c.Children.Add(T("No mods installed by Workbench", Ty.CardTitle, true, false, new Thickness(0, 0, 0, 8)));
                c.Children.Add(T("Mods you install from Community Mods are listed here with what Workbench found when it last checked the console. Mods you added with other tools are not listed; Workbench does not change them.", Ty.Text));
                sp.Children.Add(Card(c));
            }
            foreach (var kv in all)
            {
                sp.Children.Add(Group("Console " + kv.Key));
                var kept = commState != null && commState.S("console_tag") == kv.Key ? (commState.A("mods") ?? new List<object>()).OfType<Dictionary<string, object>>().Where(m => m.L("kept", 0) > 0).Select(m => m.S("package")).ToList() : new List<string>();
                var cards = new List<UIElement>();
                foreach (var m in kv.Value)
                {
                    var c = new StackPanel();
                    c.Children.Add(T(m.Name, Ty.CardTitle, true, false, new Thickness(0, 0, 0, 4)));
                    c.Children.Add(T("Version " + m.Version + (m.Variant != null && m.Variant != "standard" ? " (" + m.Variant + ")" : ""), Ty.Small, false, true));
                    c.Children.Add(Pill(m.State == CommunityLedger.Installed ? "Installed" : m.State == CommunityLedger.Waiting ? "Waits for the next start" : m.State == CommunityLedger.Removing ? "Removal waits for the next start" : "Not as planned", m.State == CommunityLedger.Installed ? "B.Ok" : m.State == CommunityLedger.Incomplete ? "B.Bad" : "B.Warn"));
                    if (m.Check != null) c.Children.Add(T(m.Check, Ty.Text, false, false, new Thickness(0, 6, 0, 0)));
                    if (cat?.Find(m.Id) == null) c.Children.Add(T("No longer listed. You can still remove it.", Ty.Small, false, true, new Thickness(0, 6, 0, 0)));
                    if (CommunityResolver.UpdateAvailable(cat, m)) c.Children.Add(T("An approved update is available: see Updates.", Ty.Small, false, true, new Thickness(0, 6, 0, 0)));
                    var mm = m; var tag = kv.Key;
                    c.Children.Add(Buttons(m.State == CommunityLedger.Installed || m.State == CommunityLedger.Incomplete ? B("Remove…", "Btn", () => ShowCommRemove(mm)) : null,
                        kept.Contains(m.Package) ? B("Put Back Kept Settings…", "Btn.Ghost", () => StartComm(new CommunityChange { RestoreSettings = { mm.Id } })) : null,
                        m.BackupPath != null ? B("Restore the Pre-install Backup…", "Btn.Ghost", () => ShowCommRollback(tag, mm)) : null));
                    var card = Card(c); card.Margin = new Thickness(0); cards.Add(card);
                }
                sp.Children.Add(CardGrid(300, 2, cards.ToArray()));
            }
            sp.Children.Add(T("Workbench checks each mod's files again whenever the console is connected; a mod is shown as installed only when its files are there.", Ty.Small, false, true, new Thickness(0, 12, 0, 0)));
            sp.Children.Add(Buttons(B("Check the Console", "Btn", () => StartComm(null))));
        }

        void CommUpdatesView(StackPanel sp, CommunityCatalog cat)
        {
            var ups = CommunityLedger.All(settings).SelectMany(kv => kv.Value.Where(m => CommunityResolver.UpdateAvailable(cat, m)).Select(m => (tag: kv.Key, m))).ToList();
            if (ups.Count == 0)
            {
                var c = new StackPanel();
                c.Children.Add(T("No updates", Ty.CardTitle, true, false, new Thickness(0, 0, 0, 8)));
                c.Children.Add(T("An update appears here only when a newer version of a mod you installed is approved. Workbench never updates a mod by itself, whatever its author calls the latest version.", Ty.Text));
                sp.Children.Add(Card(c)); return;
            }
            foreach (var (tag, m) in ups)
            {
                var r = cat.Find(m.Id); var c = new StackPanel();
                c.Children.Add(T(m.Name, Ty.CardTitle, true, false, new Thickness(0, 0, 0, 4)));
                c.Children.Add(T("Console " + tag + ": version " + m.Version + " now; version " + r.Version + " is approved.", Ty.Text));
                foreach (var w in r.Warnings) c.Children.Add(Note("Its author says: " + w, "warn"));
                c.Children.Add(Buttons(B("Review the Update…", "Btn.Primary", () => StartComm(new CommunityChange { Update = { m.Id } })), B("Details", "Btn.Ghost", () => ShowCommDetails(r))));
                sp.Children.Add(Card(c));
            }
        }

        void ShowCommRemove(CommunityInstalled m)
        {
            var sp = SectionHead("Remove " + m.Name, "Hakchi runs " + m.Name + "'s own removal at the console's next normal start. Games, saves and other mods stay.");
            var c = new StackPanel();
            var keep = new RadioButton { IsChecked = true, Margin = new Thickness(0, 0, 0, 8), Content = new TextBlock { TextWrapping = TextWrapping.Wrap, Text = "Remove mod. Its settings and favorites are kept on the console, so they can be put back if you install it again." } };
            var drop = new RadioButton { Margin = new Thickness(0, 0, 0, 8), Content = new TextBlock { TextWrapping = TextWrapping.Wrap, Text = "Also remove this mod's settings and favorites." } };
            System.Windows.Automation.AutomationProperties.SetName(keep, "Remove mod"); System.Windows.Automation.AutomationProperties.SetName(drop, "Also remove this mod's settings and favorites");
            c.Children.Add(keep); c.Children.Add(drop);
            c.Children.Add(T("Either way, the console is backed up first. Restore the Pre-install Backup is a different, full restore.", Ty.Small, false, true));
            sp.Children.Add(Card(c));
            sp.Children.Add(Buttons(B("Review the Removal", "Btn.Primary", () => StartComm(new CommunityChange { Remove = { m.Id }, RemoveSettings = drop.IsChecked == true })), B("Back", "Btn.Ghost", () => ShowCommunity("installed"))));
            Show(sp, "community-remove");
        }

        /// <summary>Restore the Pre-install Backup: the backup found and checked here, the warning in plain words, then Console Restore's own
        /// review of that backup (which backs up the console as it is then)</summary>
        void ShowCommRollback(string tag, CommunityInstalled m)
        {
            var pv = new ProgressView(this, "Checking the Pre-install Backup", "The backup made before " + m.Name + " was installed is read and checked. Nothing is written.", "Back", () => ShowCommunity("installed"));
            Show(pv.Panel, "progress");
            Task.Run(() =>
            {
                var (file, warn, why) = CommunityRollback.Check(settings, tag, m);
                Dispatcher.Invoke(() =>
                {
                    var sp = SectionHead("Restore the Pre-install Backup", null);
                    if (why != null) sp.Children.Add(Note(why, "bad"));
                    else { sp.Children.Add(Note(warn, "warn")); sp.Children.Add(Details("Technical details", "Backup: " + file, "SHA-256: " + m.BackupSha256)); }
                    sp.Children.Add(Buttons(why == null ? B("Continue to Restore…", "Btn.Primary", () => { Enter("Console Restore"); ShowRestoreReview(file); }) : null, B("Back", "Btn.Ghost", () => ShowCommunity("installed"))));
                    Show(sp, "community-rollback");
                });
            });
        }

        // ------------------------------------------------------------------ one change: read, files, review, confirm, result
        void StartComm(CommunityChange ch)
        {
            commPending = ch; commRes = null;
            var req = ConsoleReq("community");
            if (CommFixture != null) req.Add("catalog", CommFixture);
            StartOp("community", "Checking the Console", "Workbench reads which mods the console has. Nothing is written until you confirm a change.", req);
        }

        /// <summary>the worker's Community Mods events; false when the event is not one of them</summary>
        bool OnCommunityEvent(Dictionary<string, object> ev)
        {
            switch (ev.S("event"))
            {
                case "community-state":
                    commState = ev;
                    if (ev.S("refusal") != null) return true;   // the result that follows says why
                    if (commPending == null) { worker?.Send(new JObj { { "cmd", "end" } }); return true; }
                    var cat = CommCatalog(); var pk = (ev.A("packages") ?? new List<object>()).OfType<string>().ToList();
                    var ledger = (ev.A("ledger") ?? new List<object>()).OfType<Dictionary<string, object>>().Select(CommunityInstalled.From).ToList();
                    commRes = CommunityResolver.Resolve(cat, ledger, pk, commPending);
                    if (!commRes.Ok) { CommRefused(commRes.Refusal); return true; }
                    if (commRes.Items.Any(i => i.Places)) ShowCommFiles(); else CommSendChange();
                    return true;
                case "community-plan": ShowCommPlan(ev.O("plan")); return true;
            }
            return false;
        }

        /// <summary>leaves the session before its result, as Leave does (nothing the worker still sends changes the page), back to Community Mods</summary>
        void CommLeave(string view = null)
        {
            if (worker != null && worker.Running) { quitting = true; worker.Cancel(); worker.Send(new JObj { { "cmd", "quit" } }); }
            ShowCommunity(view);
        }

        void CommRefused(string why)
        {
            var sp = SectionHead("This change cannot be made", null);
            sp.Children.Add(Note(why + (why.EndsWith("Nothing was changed.") ? "" : " Nothing was changed."), "warn"));
            sp.Children.Add(Buttons(B("Back", "Btn", () => CommLeave())));
            Show(sp, "community-files");
        }

        /// <summary>the approved file for each mod to install: already admitted on this PC, downloaded into a folder the person chooses, or
        /// chosen on this PC; every one checked against its pin before the review</summary>
        void ShowCommFiles(string message = null, string kind = "info")
        {
            var res = commRes; if (res == null) return;
            var sp = SectionHead("The Mods' Files", "Each mod's file comes from its author's release, exactly as it was approved. Workbench never hosts or changes a mod's file.");
            foreach (var f in res.Feedback) sp.Children.Add(Note(f, "info"));
            bool all = true;
            foreach (var it in res.Placed)
            {
                var r = it.Record; var v = it.Variant; var c = new StackPanel();
                c.Children.Add(T(r.Name + " " + r.Version + (v.IsDefault ? "" : " (" + v.Variant + ")"), Ty.CardTitle, true, false, new Thickness(0, 0, 0, 4)));
                if (commFiles.TryGetValue(v.Sha256, out var have) && CommunityAdmission.Admit(r, v, have).pkg != null) c.Children.Add(Note("Ready: the approved file is on this PC (" + have + ").", "ok"));
                else
                {
                    all = false; commFiles.Remove(v.Sha256);
                    c.Children.Add(T(CommunityAdmission.NoFileText, Ty.Text));
                    c.Children.Add(T("Download " + Units.Gb(v.Bytes) + " from " + new Uri(v.Url).Host + ". The file is saved only where you choose.", Ty.Small, false, true, new Thickness(0, 4, 0, 0)));
                    var rr = r; var vv = v;
                    c.Children.Add(Buttons(B("Download…", "Btn", () => CommDownload(rr, vv)), B("Choose the File…", "Btn.Ghost", () => CommChoose(rr, vv))));
                }
                c.Children.Add(Details("Technical details", "Release file: " + v.Url, "Size: " + v.Bytes.ToString("N0", En) + " bytes", "SHA-256: " + v.Sha256));
                sp.Children.Add(Card(c));
            }
            if (message != null) sp.Children.Add(Note(message, kind));
            sp.Children.Add(Buttons(B("Review the Change", "Btn.Primary", CommSendChange, all), B("Cancel", "Btn.Ghost", () => CommLeave())));
            Show(sp, "community-files");
        }

        void CommChoose(CommunityRecord r, CommunityVariant v)
        {
            var d = new Microsoft.Win32.OpenFileDialog { Filter = "Hakchi packages (*.hmod)|*.hmod", Title = "Choose the approved file for " + r.Name + " " + r.Version, FileName = v.FileName };
            if (d.ShowDialog(this) != true) return;
            var (pkg, why) = CommunityAdmission.Admit(r, v, d.FileName);
            if (pkg != null) commFiles[v.Sha256] = pkg.File;
            ShowCommFiles(pkg != null ? null : why, "bad");
        }

        void CommDownload(CommunityRecord r, CommunityVariant v)
        {
            var d = new Microsoft.Win32.SaveFileDialog { Title = "Choose where to save " + r.Name + "'s file", FileName = v.FileName, Filter = "Hakchi packages (*.hmod)|*.hmod", OverwritePrompt = false };
            if (d.ShowDialog(this) != true) return;
            var folder = Path.GetDirectoryName(d.FileName);
            var pv = new ProgressView(this, "Downloading " + r.Name, "From the author's release: " + v.Url, null, null);
            Show(pv.Panel, "progress");
            Task.Run(() =>
            {
                var (file, why) = CommunityDownload.Fetch(r, v, folder, (n, total) => Dispatcher.BeginInvoke(new Action(() => pv.Update("Downloading...", n, total))));
                string bad = why;
                if (file != null) { var (pkg, w2) = CommunityAdmission.Admit(r, v, file); if (pkg != null) commFiles[v.Sha256] = pkg.File; else bad = w2; }
                Dispatcher.Invoke(() => ShowCommFiles(bad, "bad"));
            });
        }

        void CommSendChange()
        {
            var ch = commPending; if (ch == null) return;
            var j = new JObj { { "cmd", "community-change" }, { "install", ch.Install.Cast<object>().ToList() }, { "update", ch.Update.Cast<object>().ToList() }, { "remove", ch.Remove.Cast<object>().ToList() },
                { "restore_settings", ch.RestoreSettings.Cast<object>().ToList() }, { "remove_settings", ch.RemoveSettings } };
            var files = new JObj();
            foreach (var it in commRes?.Placed ?? Enumerable.Empty<CommunityItem>()) if (commFiles.TryGetValue(it.Variant.Sha256, out var f)) files.Add(it.Record.Id, f);
            j.Add("files", files);
            progress.Update("Backing up the console as it is now, before anything changes...", 0, 0); Show(progress.Panel, "progress");
            worker?.Send(j);
        }

        void ShowCommPlan(Dictionary<string, object> p)
        {
            var sp = SectionHead("Review the Change", "What would change on your " + Con + ". Nothing has been written yet.");
            var c = new StackPanel();
            foreach (var it in (p.A("items") ?? new List<object>()).OfType<Dictionary<string, object>>())
            {
                var n = it.S("name"); var op = it.S("op");
                var line = op == "install" ? "Install " + n + " " + it.S("version") + (it.S("variant") != "standard" ? " (" + it.S("variant") + " version)" : "")
                    : op == "update" ? "Update " + n + " to " + it.S("version") : op == "switch" ? "Change " + n + " to its " + it.S("variant") + " version"
                    : op == "remove" ? "Remove " + n + (it.B("keep_settings") ? " (its settings and favorites are kept)" : "") : "Put back " + n + "'s kept settings and favorites";
                c.Children.Add(T("• " + line + (it.B("alpha") && op != "remove" ? " · Alpha" : ""), Ty.Body, true, false, new Thickness(0, 0, 0, 4)));
                if (it.S("reason") != null) c.Children.Add(T("   " + it.S("reason"), Ty.Text, false, true));
            }
            foreach (var u in (p.A("upstream_removals") ?? new List<object>()).OfType<string>()) c.Children.Add(Note(u, "warn"));
            c.Children.Add(T(p.S("text") ?? "", Ty.Text, false, false, new Thickness(0, 8, 0, 6)));
            foreach (var n in (p.A("notes") ?? new List<object>()).OfType<string>().Where(x => !(p.A("upstream_removals") ?? new List<object>()).Contains(x) && !(p.A("feedback") ?? new List<object>()).Contains(x))) c.Children.Add(T(n, Ty.Text, false, true, new Thickness(0, 0, 0, 4)));
            c.Children.Add(T("Backup made just now: " + p.S("protective_backup"), Ty.Small, false, true, new Thickness(0, 6, 0, 0)));
            c.Children.Add(Details("Technical details", new object[] { "Steps: " + string.Join(", ", (p.A("steps") ?? new List<object>()).OfType<string>()) }
                .Concat((p.A("packages") ?? new List<object>()).OfType<Dictionary<string, object>>().Select(x => (object)(x.S("name") + ": " + x.S("file") + " · SHA-256 " + x.S("sha256") + " · hakchi package " + x.S("package") + " · its own scripts: " + string.Join(", ", (x.A("scripts") ?? new List<object>()).OfType<string>()))))
                .Concat(new object[] { p.O("removal_list") == null ? null : "Removal list: " + p.O("removal_list").S("names") + " (SHA-256 " + p.O("removal_list").S("sha256") + ")", "Catalog generation " + p.O("catalog")?.L("generation", 0) + ", SHA-256 " + p.O("catalog")?.S("sha256"), "Plan " + p.S("digest") }).ToArray()));
            sp.Children.Add(Card(c));
            sp.Children.Add(Buttons(B("Make This Change", "Btn.Primary", () => { progress.Update("Checking the backup and the files again...", 0, 0); Show(progress.Panel, "progress"); worker?.Send(new JObj { { "cmd", "community-confirm" }, { "digest", p.S("digest") }, { "nonce", p.S("nonce") } }); }),
                                    B("Cancel", "Btn.Ghost", () => CommLeave())));
            Show(sp, "community-plan");
        }

        void CommunityResult(Dictionary<string, object> r)
        {
            var st = r.S("state"); commPending = null;
            if (st == "checked") { worker?.Send(new JObj { { "cmd", "quit" } }); ShowCommunity("installed"); return; }
            var sp = new StackPanel();
            if (st == "scheduled")
            {
                sp.Children.Add(PageTitle("Ready for the console's next start"));
                sp.Children.Add(Note("Switch the console off, disconnect the USB cable and switch it on. Hakchi applies the change at that start and restarts the console once. Connect again afterwards: Workbench checks each mod's files then.", "ok"));
            }
            else
            {
                sp.Children.Add(PageTitle(st == "canceled" ? "Canceled" : st == "unavailable" ? "Not available in this version" : st == "refused" ? "Nothing was changed" : "The change did not finish"));
                sp.Children.Add(Note(r.S("problem") ?? "", st == "canceled" ? "info" : st == "stopped" ? "bad" : "warn"));
                if (st == "stopped") sp.Children.Add(T("If the console does not start as before, Console Restore can put back the backup made just now. A problem in a mod itself is for its author: its Details page links to their support page.", Ty.Text, false, true));
            }
            sp.Children.Add(Details("Technical details", r.S("record") != null ? "Record: " + r.S("record") : null, r.S("protective") != null ? "Backup made just now: " + r.S("protective") : null,
                (r.A("done") ?? new List<object>()).Count > 0 ? "Steps done: " + string.Join(", ", (r.A("done") ?? new List<object>()).OfType<string>()) : null));
            sp.Children.Add(Buttons(st == "scheduled" ? B("Restart the Console", "Btn.Primary", () => worker?.Send(new JObj { { "cmd", "finish" } })) : null,
                B("Done", st == "scheduled" ? "Btn.Ghost" : "Btn", () => { worker?.Send(new JObj { { "cmd", "quit" } }); ShowCommunity(st == "scheduled" ? "installed" : null); })));
            Show(sp, "community-result");
        }

        // ------------------------------------------------------------------ the add-ons page: the catalog's cards under CCS Mod's
        /// <summary>the community cards on the add-ons page, only when the catalog lists entries: the scope statement, then each section in its
        /// order with standard cards. A choice selects what it needs (and says why); a conflict is refused with its own words; unselecting what
        /// another choice needs asks first. The chosen entries are installed from Community Mods, each change with its own review and backup.</summary>
        void CommunityCards(StackPanel sp, AddonSession a)
        {
            var cat = CommCatalog(); if (cat == null || cat.Empty) return;
            sp.Children.Add(Note(CommunityScope, "info"));
            var ledger = a?.Tag == null ? new List<CommunityInstalled>() : CommunityLedger.Load(settings, a.Tag);   // what Workbench installed on this console counts as there
            var have = ledger.Where(m => m.State == CommunityLedger.Installed).Select(m => m.Package).ToList();
            var boxes = new Dictionary<string, CheckBox>(StringComparer.Ordinal);
            var feedback = new StackPanel();
            bool quiet = false;
            void Say(string text, string kind, params UIElement[] buttons) { feedback.Children.Clear(); if (text != null) { feedback.Children.Add(Note(text, kind)); if (buttons.Length > 0) feedback.Children.Add(Buttons(buttons)); } }
            void Set(string id, bool on) { quiet = true; if (boxes.TryGetValue(id, out var b)) b.IsChecked = on; quiet = false; if (on) commPick.Add(id); else commPick.Remove(id); }
            foreach (var sec in CommunityCatalog.SectionOrder)
            {
                var list = cat.Section(sec); if (list.Count == 0) continue;
                sp.Children.Add(Group(CommunityCatalog.SectionTitle(sec)));
                var cards = new List<UIElement>();
                foreach (var r in list)
                {
                    var cb = new CheckBox { IsChecked = commPick.Contains(r.Id), VerticalContentAlignment = VerticalAlignment.Center }; boxes[r.Id] = cb; var rec = r;
                    cb.Checked += (s, e) =>
                    {
                        if (quiet) return;
                        var res = CommunityResolver.Resolve(cat, ledger, have, new CommunityChange { Install = commPick.Concat(new[] { rec.Id }).Distinct().ToList() });
                        if (!res.Ok) { Set(rec.Id, false); Say(res.Refusal, "warn"); return; }
                        foreach (var it in res.Items) Set(it.Record.Id, true);
                        Say(res.Feedback.Count > 0 ? string.Join(" ", res.Feedback) : null, "info");
                    };
                    cb.Unchecked += (s, e) =>
                    {
                        if (quiet) return;
                        var needs = commPick.Where(x => x != rec.Id && cat.Find(x)?.Depends.Contains(rec.Id) == true).ToList();
                        if (needs.Count == 0) { commPick.Remove(rec.Id); Say(null, null); return; }
                        Set(rec.Id, true);
                        var names = string.Join(" and ", needs.Select(x => cat.Find(x).Name));
                        Say(names + (needs.Count == 1 ? " needs " : " need ") + rec.Name + ". Unselect " + names + " too?", "warn",
                            B("Unselect " + (needs.Count == 1 ? "It" : "Them") + " Too", "Btn", () => { foreach (var x in needs) Set(x, false); Set(rec.Id, false); Say(null, null); }),
                            B("Keep " + rec.Name, "Btn.Ghost", () => Say(null, null)));
                    };
                    cards.Add(CommCard(r, cb, a?.Tag));
                }
                sp.Children.Add(CardGrid(300, 2, cards.ToArray()));
            }
            sp.Children.Add(feedback);
            sp.Children.Add(T("Community add-ons you choose here are installed from Community Mods once this change is done: each with its own review and a fresh backup.", Ty.Small, false, true, new Thickness(0, 4, 0, 12)));
        }
    }
}
