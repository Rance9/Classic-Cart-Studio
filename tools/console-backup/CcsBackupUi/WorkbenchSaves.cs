// Classic Cart Studio console backup and restore window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Backup Browser's Game save file conversion (CcsBackup.Saves): a battery save from a backup (or the console, read live) checked and
// converted to the plain battery file the CCS player uses, only for an exact game whose entry allows it. The original bytes are never
// changed; the result is a new Game save file the person saves on this PC, with its conversion report beside it. A game entry not yet
// confirmed on a real console gives a labeled comparison copy only, never something to import or put on a console (said on the page, not
// only in the file name). Suspend points are not battery saves and are refused.
using CcsBackup.Browse;
using CcsBackup.Saves;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace CcsBackup.Ui
{
    public sealed partial class BackupWorkspace
    {
        /// <summary>the SHA-256 of an NES game file's content after its 16-byte header (and trainer), as Classic Cart Studio identifies games</summary>
        static string NesContentSha(byte[] b)
        {
            if (b == null || b.Length < 16 || b[0] != 'N' || b[1] != 'E' || b[2] != 'S' || b[3] != 0x1A) return null;
            int skip = 16 + ((b[6] & 4) != 0 ? 512 : 0); if (b.Length <= skip) return null;
            using (var h = SHA256.Create()) return CcsBackup.Hex.Of(h.ComputeHash(b, skip, b.Length - skip));
        }

        /// <summary>converts one battery save into a Game save file, in its own area of the preview (cleared each time)</summary>
        void ConvertSave(BrowseEntry e, StackPanel into)
        {
            var c = browsing; if (c == null) return;
            into.Children.Clear();
            into.Children.Add(T("Convert to a " + FileKinds.GameSaves, Ty.Body, true, false, new Thickness(0, 8, 0, 2)));
            into.Children.Add(T("Makes a battery save Classic Cart Studio's player can use, as a new file you save on this PC with a report beside it. The save in the " + (c.Live ? "console" : "backup") + " is never changed.", Ty.Small, false, true));
            into.Children.Add(Terms("battery-save", "suspend-point"));
            var wait = T("Checking the save...", 12.5, false, true); into.Children.Add(wait);
            Task.Run<(StockSaveInspection, SaveConversion, string)>(() =>
            {
                var bytes = c.ReadVerified(e, 1_000_000, out var why);
                if (bytes == null) return (null, null, why ?? "It could not be read against its checksum.");
                var src = SaveSource.FromConsolePath(e.ConsolePath ?? e.DisplayPath, e.MtimeUtc, c.Live ? "console " + c.ConsoleTag + ", read live" : "console backup " + c.BackupId);
                var ins = StockSave.Inspect(bytes, src);
                if (!ins.Recognized) return (ins, null, ins.Text);
                // the exact game: a game file in the same game group that this program can read (games the console's own system holds are
                // inside its encrypted system storage and cannot be read on a PC: their entry then gives a comparison copy at most)
                var g = c.Game(e.GameKey); string content = null;
                foreach (var gf in g?.GameFiles ?? Enumerable.Empty<BrowseEntry>()) { var gb = c.ReadVerified(gf, 8_000_000, out _); content = NesContentSha(gb); if (content != null) break; }
                var game = new GameIdentity { ConsoleCode = src.ConsoleCode, ContentSha256 = content, Title = g?.Title, IdentitySource = content != null ? "the game file in the same backup" : null };
                var entry = BatteryTable.For(game);
                if (entry == null) return (ins, null, "This game has no battery-save entry in this version, so its save is not converted. Nothing is guessed.");
                return (ins, StockSave.ConvertToRaw(bytes, game, entry, src), null);
            }).ContinueWith(t =>
            {
                if (browsing != c) return; into.Children.Remove(wait);
                if (t.IsFaulted) { into.Children.Add(Note("The save could not be converted.", "warn")); into.Children.Add(Details("Technical details", t.Exception.GetBaseException().Message)); return; }
                var (ins, conv, why) = t.Result;
                if (ins != null && (conv != null || why != ins.Text)) into.Children.Add(T("Found: " + ins.Text, 12.5));   // not said twice when it is also the reason
                if (conv == null) { into.Children.Add(Note(why ?? "Not converted.", "warn")); return; }
                if (!conv.Ok) { into.Children.Add(Note("Not converted: " + conv.Text, "warn")); return; }
                // a comparison copy's limits stay on the page, not only in its file name
                into.Children.Add(Note(conv.Trial ? "Converted as a comparison copy only: this game's entry is not yet confirmed on a real console, so the copy is for checking only. It is never imported or put on a console." : conv.Text, conv.Trial ? "warn" : "ok"));
                var saved = new StackPanel();
                into.Children.Add(Buttons(B(conv.Trial ? "Save the Comparison Copy…" : "Save the Game Save File…", "Btn", () =>
                {
                    var name = (conv.Trial ? "COMPARISON ONLY - " : "") + conv.OutputName;
                    var d = new Microsoft.Win32.SaveFileDialog { Title = conv.Trial ? "Save the comparison copy (for checking only)" : "Save the " + FileKinds.GameSaves.ToLowerInvariant(), FileName = name, OverwritePrompt = false,
                                                                Filter = FileKinds.GameSaves + " (*" + Path.GetExtension(conv.OutputName) + ")|*" + Path.GetExtension(conv.OutputName) + "|All files (*.*)|*.*" };
                    if (string.IsNullOrEmpty(Path.GetExtension(conv.OutputName))) d.Filter = "All files (*.*)|*.*";
                    if (d.ShowDialog(this) != true) return;
                    if (File.Exists(d.FileName) || File.Exists(d.FileName + ".conversion.json")) { MessageBox.Show(this, "A file with that name already exists; choose another name. Nothing is overwritten.", ProductTitle); return; }
                    try
                    {
                        using (var f = new FileStream(d.FileName, FileMode.CreateNew)) f.Write(conv.Output, 0, conv.Output.Length);
                        File.WriteAllText(d.FileName + ".conversion.json", CcsBackup.Json.Write(conv.Report));
                        saved.Children.Clear();
                        saved.Children.Add(Note((conv.Trial ? "The comparison copy is saved" : "The " + FileKinds.GameSaves.ToLowerInvariant() + " is saved") + ", with its conversion report beside it. The save in the " + (c.Live ? "console" : "backup") + " is unchanged.", "ok"));
                        saved.Children.Add(PathText(d.FileName)); saved.Children.Add(PathText(d.FileName + ".conversion.json", 12));
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { MessageBox.Show(this, "It could not be saved: " + ex.Message, ProductTitle); }
                })));
                into.Children.Add(saved);
                into.Children.Add(Details("Technical details", "Direction: the console's battery save to the player's plain battery file (" + conv.OutputName + ").",
                    conv.Entry != null ? "Battery-save entry: " + conv.Entry.Title + " (" + conv.Entry.Id + ", " + conv.Entry.Status + ")" : null,
                    conv.OutputSha256 != null ? "SHA-256 of the converted file: " + conv.OutputSha256 : null,
                    "The conversion report beside it (.conversion.json) records the source file and its SHA-256, the game, the battery-save entry and the converted file."));
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
    }
}
