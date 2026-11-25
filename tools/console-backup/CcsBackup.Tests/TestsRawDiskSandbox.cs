// Classic Cart Studio console backup and restore engine tests: a whole drive read and written for real, INSIDE WINDOWS SANDBOX ONLY.
// GPL-3.0-or-later. Both modes refuse to run anywhere else (the Sandbox's own account, WDAGUtilityAccount): they open drives whole.
//   --raw-disk FILE SHA256        this test program as Workbench's administrator step (RawDiskHost.Execute on the real drives), with the
//                                 test machine's policy that also lets virtual hard disks through (the product's policy never does), or with
//                                 the product's own policy when CCS_SBX_POLICY=product
//   --raw-disk-sandbox --results DIR --work DIR
//                                 the whole run, inside the disposable Sandbox only: two small virtual hard disks made there (diskpart: made,
//                                 attached, formatted, filled), the first copied whole into a storage backup through the real elevated step
//                                 (Windows' runas, the pipe, the request file), then written onto the second and read back; the second checked
//                                 as a drive afterward; a cancel before the go-ahead (the second unchanged), a cancel part way, the step ended
//                                 part way, and refusals: another drive identity, the product's policy (a virtual disk is not a USB drive or SD
//                                 card), the drive Windows runs from, the drive holding the backup; the step started without administrator
//                                 permission (a standard account made in the Sandbox). Results and a log go to DIR (JSON, ASCII).
using CcsBackup.Engine;
using CcsBackup.External;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace CcsBackup.Tests
{
    static partial class Tests
    {
        internal static bool InSandbox => string.Equals(Environment.UserName, "WDAGUtilityAccount", StringComparison.OrdinalIgnoreCase)
            || Environment.GetEnvironmentVariable("CCS_SBX_STD") == "1" && string.Equals(Environment.UserName, "ccsstd", StringComparison.OrdinalIgnoreCase) && Directory.Exists(Path.Combine(Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) ?? "", "WDAGUtilityAccount"));

        internal static int SandboxStep(string[] args)
        {
            if (!InSandbox) { Console.Error.WriteLine("refused: this test step runs only inside Windows Sandbox"); return 3; }
            var policy = Environment.GetEnvironmentVariable("CCS_SBX_POLICY") == "product" ? RawDiskPolicy.UsbAndSd : RawDiskPolicy.WithVirtualForTests();
            return RawDiskHost.Execute(args, WindowsRawDiskSystem.Open(), policy);
        }

        static StreamWriter sbxLog;
        static void L(string s) { var line = DateTime.Now.ToString("HH:mm:ss.fff") + " " + s; Console.WriteLine(line); sbxLog?.WriteLine(line); sbxLog?.Flush(); }

        static string Diskpart(string work, params string[] lines)
        {
            var f = Path.Combine(work, "dp-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".txt"); File.WriteAllText(f, string.Join("\r\n", lines) + "\r\n");
            var p = Process.Start(new ProcessStartInfo("diskpart.exe", "/s \"" + f + "\"") { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true });
            var o = p.StandardOutput.ReadToEnd(); p.WaitForExit(); L("diskpart " + string.Join(" | ", lines) + " -> exit " + p.ExitCode); sbxLog?.WriteLine(o); return o;
        }

        static string RawHead(IRawDiskSystem sys, DiskEntry e, long n)
        {
            using (var d = sys.Open(e, false)) using (var h = SHA256.Create())
            {
                var b = new byte[1 << 20]; long at = 0;
                while (at < n)
                {
                    int want = (int)Math.Min(b.Length, n - at), sec = d.SectorBytes, whole = (want + sec - 1) / sec * sec;
                    int r = d.Read(at, b, whole); if (r <= 0) break;
                    int use = (int)Math.Min(r, n - at); h.TransformBlock(b, 0, use, null, 0); at += use;
                }
                h.TransformFinalBlock(new byte[0], 0, 0); return Hex.Of(h.Hash);
            }
        }

        static byte[] RegionsBytes(string backup)
        {
            var v = StorageVerifier.Verify(backup); var lay = v.Layouts["s01"]; int n = (int)lay.L("regions"); var all = new byte[lay.L("bytes")]; long at = 0;
            using (var z = File.OpenRead(backup))
                for (int i = 1; i <= n; i++) { var e = v.Entries.First(x => x.Name == ExternalFormat.Region("s01", i)); z.Position = e.DataOffset; long left = e.Size; while (left > 0) { int r = z.Read(all, (int)at, (int)Math.Min(1 << 20, left)); at += r; left -= r; } }
            return all;
        }

        static byte[] RawBytes(IRawDiskSystem sys, DiskEntry e, long n)
        {
            var all = new byte[n]; var b = new byte[1 << 20];
            using (var d = sys.Open(e, false)) for (long at = 0; at < n;) { int r = d.Read(at, b, (int)Math.Min(b.Length, (n - at + d.SectorBytes - 1) / d.SectorBytes * d.SectorBytes)); if (r <= 0) break; int use = (int)Math.Min(r, n - at); Buffer.BlockCopy(b, 0, all, (int)at, use); at += use; }
            return all;
        }

        static string RegionsSha(string backup)
        {
            var v = StorageVerifier.Verify(backup); var lay = v.Layouts["s01"]; int n = (int)lay.L("regions");
            using (var z = File.OpenRead(backup)) using (var h = SHA256.Create())
            {
                var b = new byte[1 << 20];
                for (int i = 1; i <= n; i++) { var e = v.Entries.First(x => x.Name == ExternalFormat.Region("s01", i)); z.Position = e.DataOffset; long left = e.Size; while (left > 0) { int r = z.Read(b, 0, (int)Math.Min(b.Length, left)); h.TransformBlock(b, 0, r, null, 0); left -= r; } }
                h.TransformFinalBlock(new byte[0], 0, 0); return Hex.Of(h.Hash);
            }
        }

        static void KillStep()
        {
            var me = Process.GetCurrentProcess();
            foreach (var p in Process.GetProcessesByName(me.ProcessName).Where(x => x.Id != me.Id)) { try { L("ending the administrator step, process " + p.Id); p.Kill(); } catch (Exception ex) { L("could not end process " + p.Id + ": " + ex.Message); } }
        }

        internal static int SandboxRun(string[] args)
        {
            if (!InSandbox) { Console.Error.WriteLine("refused: this run opens drives whole and runs only inside Windows Sandbox"); return 3; }
            string Arg(string k) { int i = Array.IndexOf(args, k); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
            var results = Arg("--results"); var work = Arg("--work"); Directory.CreateDirectory(results); Directory.CreateDirectory(work);
            sbxLog = new StreamWriter(Path.Combine(results, "sandbox-log.txt"), false, new UTF8Encoding(false));
            var R = new JObj(); var checks = new List<object>(); int pass = 0, fail = 0;
            void C(bool ok, string what) { checks.Add(new JObj { { "ok", ok }, { "what", what } }); if (ok) pass++; else fail++; L((ok ? "ok   " : "FAIL ") + what); }
            void Save() { R.Set("checks", checks); R.Set("passed", (long)pass); R.Set("failed", (long)fail); File.WriteAllText(Path.Combine(results, "sandbox-results.json"), Json.Write(R)); }
            try
            {
                using (var id = WindowsIdentity.GetCurrent())
                {
                    R.Set("user", id.Name); R.Set("administrator", new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator));
                }
                var sys = WindowsRawDiskSystem.Open(); R.Set("elevated", sys.Elevated); L("user " + R["user"] + ", elevated " + sys.Elevated);
                if (!sys.Elevated) { L("not elevated: Windows would show its permission prompt, which nobody can answer here; stopping"); Save(); return 2; }
                StorageBackup.WindowPolicy = RawDiskPolicy.WithVirtualForTests();   // this test machine only: its drives are virtual hard disks
                var q = WindowsDiskQueries.Instance;
                var a = Path.Combine(work, "a.vhd"); var bfile = Path.Combine(work, "b.vhd");
                Diskpart(work, "create vdisk file=\"" + a + "\" maximum=96 type=fixed", "select vdisk file=\"" + a + "\"", "attach vdisk", "convert mbr", "create partition primary", "format fs=fat32 quick label=CCSSBXA", "assign letter=R");
                Diskpart(work, "create vdisk file=\"" + bfile + "\" maximum=160 type=fixed", "select vdisk file=\"" + bfile + "\"", "attach vdisk", "convert mbr", "create partition primary", "format fs=ntfs quick label=OLDCARD", "assign letter=S");
                var files = new Dictionary<string, byte[]> { { @"README.TXT", Encoding.ASCII.GetBytes("a test drive made inside Windows Sandbox\r\n") }, { @"games\CLV-Z-SBXAA\game.bin", Rnd(300_000, 5) }, { @"big.bin", Rnd(5_000_000, 6) } };
                foreach (var kv in files) { var p = Path.Combine(@"R:\", kv.Key); Directory.CreateDirectory(Path.GetDirectoryName(p)); File.WriteAllBytes(p, kv.Value); }
                File.WriteAllText(@"S:\old.txt", "what was on the second drive before");
                Thread.Sleep(2000);
                DiskIdentity FindByLetter(string letter)
                {
                    foreach (var e in q.Disks()) { var d = q.Describe(e); if (d != null && q.VolumesOn(d.Number).Any(v => v.Mounts.Any(m => m.StartsWith(letter, StringComparison.OrdinalIgnoreCase)))) return d; }
                    return null;
                }
                var idA = FindByLetter("R:"); var idB = FindByLetter("S:");
                R.Set("drive_a", idA?.ToJson()); R.Set("drive_b", idB?.ToJson());
                var all = q.Disks().Select(e => q.Describe(e)).Where(x => x != null).ToList(); R.Set("drives", all.Select(x => (object)x.ToJson()).ToList()); R.Set("system_disk", (long)q.SystemDisk());
                C(idA != null && idB != null && idA.Bus == "FileBackedVirtual" && idA.Bytes == 96L << 20 && idB.Bytes == 160L << 20, "the two virtual hard disks are found by their volumes: " + idA + " / " + idB);
                if (idA == null || idB == null) { Save(); return 1; }
                // no drive letters while they are read and written: nothing else in the Sandbox holds them open
                Diskpart(work, "select volume=R", "remove letter=R"); Diskpart(work, "select volume=S", "remove letter=S"); Thread.Sleep(2000);
                var s = new Settings { Path = Path.Combine(work, "settings.json"), PrimaryFolder = Path.Combine(work, "loc", "backups") }; s.Save();

                // 1. the first drive copied whole through the real administrator step
                var o = StorageBackup.Run(new[] { new StorageRequest { Mode = "image", DiskNumber = idA.Number, Disk = idA } }, new StorageOptions { Settings = s, SecondCopy = false, Entry = "sandbox", ToolName = "CcsBackup.Tests" });
                R.Set("copy", new JObj { { "state", o.State }, { "problem", o.Problem }, { "scope_complete", o.ScopeComplete }, { "consistency", o.ConsistencyText }, { "bytes", o.Bytes } });
                C(o.Ok && o.Protected && StorageVerifier.Verify(o.PrimaryPath).Ok, "the virtual drive is copied whole into a checked storage backup through Windows' runas, the request file and the pipe (" + o.State + " " + o.Problem + ")");
                if (!o.Ok) { Save(); return 1; }
                var regions = RegionsSha(o.PrimaryPath); var eA = q.Disks().First(x => x.Number == idA.Number);
                C(RawHead(sys, eA, idA.Bytes) == regions, "the backup's regions are the drive's bytes exactly (read again from the drive)");
                var v = StorageVerifier.Verify(o.PrimaryPath);
                C(v.Index["s01"].Any(x => x.S("path") == "/README.TXT") && v.Index["s01"].Any(x => (x.S("path") ?? "").EndsWith("game.bin")), "the drive's FAT32 files are listed in the backup's index");
                Diskpart(work, "select vdisk file=\"" + a + "\"", "detach vdisk");   // the restored copy will carry the first drive's disk signature

                // 2. written onto the second drive and read back
                var eB = q.Disks().First(x => x.Number == idB.Number); var before = RawHead(sys, eB, idB.Bytes);
                var plan = StorageRestore.PlanImage(o.PrimaryPath, "s01", new DiskTarget { DiskNumber = idB.Number, Disk = idB }, null, null, StorageRestore.Protected(s));
                R.Set("review", plan.Review.Cast<object>().ToList()); R.Set("review_refusal", plan.Refusal);
                C(plan.Refusal == null && plan.TargetDisk != null && plan.Review.Any(x => x.Contains("lost")), "the review names the second drive and what is lost (" + plan.Refusal + ")");
                var rr = StorageRestore.RestoreImage(plan, plan.Digest, plan.Nonce, s);
                R.Set("restore", new JObj { { "state", rr.DiskState }, { "text", rr.Text }, { "bytes", rr.Bytes } });
                // read the drive again here, after the step let go of it, and say exactly where it differs from the backup's image, if anywhere:
                // once the step unlocks it, Windows mounts the restored volume and may write to it (that is Windows, not the restore)
                var img = RegionsBytes(o.PrimaryPath); var now = RawBytes(sys, eB, img.Length);
                var diffs = new List<long>(); for (long i = 0; i < img.Length; i++) if (img[i] != now[i]) { diffs.Add(i); if (diffs.Count >= 200) break; }
                long part = BitConverter.ToUInt32(img, 446 + 8) * 512L;
                R.Set("restore_reread", new JObj { { "differing_bytes_first_200", diffs.Select(x => (object)x).ToList() }, { "partition_offset", part }, { "differing_relative_to_partition", diffs.Select(x => (object)(x - part)).ToList() } });
                C(rr.Ok && rr.DiskState == "complete", "written onto the second drive; the step read every region back from the drive while it held it and each matched the backup (" + rr.Text + ")");
                C(diffs.Count == 0 || diffs.All(x => x >= part), "read again after the step let go: " + (diffs.Count == 0 ? "identical to the image" : diffs.Count + " differing byte(s), all inside the restored volume (offsets from its start: " + string.Join(", ", diffs.Take(12).Select(x => x - part)) + "), none in the partition table"));
                Thread.Sleep(3000);
                var vols = q.VolumesOn(idB.Number); R.Set("restored_volumes", vols.Select(x => (object)x.ToJson()).ToList());
                if (!vols.Any(x => x.Mounts.Count > 0)) { Diskpart(work, "select disk " + idB.Number, "select partition 1", "assign letter=T"); Thread.Sleep(2000); vols = q.VolumesOn(idB.Number); }
                var letter = vols.SelectMany(x => x.Mounts).FirstOrDefault(m => m.Length <= 3);
                C(letter != null && vols.Any(x => x.Label == "CCSSBXA" && x.FileSystem == "FAT32") && files.All(kv => File.Exists(Path.Combine(letter, kv.Key)) && File.ReadAllBytes(Path.Combine(letter, kv.Key)).SequenceEqual(kv.Value)) && !File.Exists(Path.Combine(letter, "old.txt")),
                  "Windows reads the second drive afterward as the first one's FAT32 volume, with every file exactly as it was (" + letter + ")");
                if (letter != null) Diskpart(work, "select volume=" + letter.Substring(0, 1), "remove letter=" + letter.Substring(0, 1));
                Thread.Sleep(2000);

                // 3. a cancel after the step is ready and before the go-ahead: nothing written
                var image = new RawImagePlan { Backup = plan.Backup, BackupBytes = plan.BackupBytes, BackupMtimeUtc = plan.BackupMtime, ImageBytes = plan.ImageBytes, SectorBytes = plan.ImageSectorBytes, RegionSize = plan.RegionSize, Regions = plan.Regions.ToList() };
                var idB2 = q.Describe(q.Disks().First(x => x.Number == idB.Number));
                // put different bytes on the second drive first, so a write would show
                using (var w = sys.Open(q.Disks().First(x => x.Number == idB.Number), true)) { var junk = Rnd(32768, 77); w.Write(0, junk, junk.Length); w.Flush(); }   // before the first partition
                var b0 = RawHead(sys, eB, idB.Bytes);
                var sess = RawDiskClient.Open(new RawDiskRequest { Kind = "write", Target = idB2, Image = image, Protect = StorageRestore.Protected(s) }, null);
                var ready = sess.Refusal == null && sess.Identity != null; sess.Dispose();
                C(ready && RawHead(sys, eB, idB.Bytes) == b0, "the step checked the drive, locked it and said it was ready; canceled before the go-ahead, nothing was written (" + sess.Refusal + ")");
                // 4. canceled part way
                bool stop = false;
                var pc1 = StorageRestore.PlanImage(o.PrimaryPath, "s01", new DiskTarget { DiskNumber = idB.Number, Disk = idB2 }, null, null, StorageRestore.Protected(s));
                var cr = StorageRestore.RestoreImage(pc1, pc1.Digest, pc1.Nonce, s, () => stop, p => { if (p.Phase == "write" && p.Done > (30L << 20)) stop = true; });
                R.Set("cancel_part_way", new JObj { { "state", cr.DiskState }, { "text", cr.Text }, { "bytes", cr.Bytes } });
                C(cr.DiskState == "canceled-incomplete" && !cr.NothingWritten && cr.Bytes > (30L << 20) && cr.Bytes < idA.Bytes, "canceled part way: reported as an incomplete drive, with how much was written (" + cr.Text + ")");
                // 5. the step ended part way (its process killed)
                bool killed = false;
                var pc2 = StorageRestore.PlanImage(o.PrimaryPath, "s01", new DiskTarget { DiskNumber = idB.Number, Disk = idB2 }, null, null, StorageRestore.Protected(s));
                var kr = StorageRestore.RestoreImage(pc2, pc2.Digest, pc2.Nonce, s, null, p => { if (!killed && p.Phase == "write" && p.Done > (30L << 20)) { killed = true; KillStep(); } });
                var recs = Directory.GetFiles(s.RecordsFolder, "disk-restore-*.json").Select(Record.Load).Select(d => d.S("state")).ToList();
                R.Set("killed", new JObj { { "state", kr.DiskState }, { "text", kr.Text }, { "records", recs.Cast<object>().ToList() } });
                C(killed && kr.DiskState == "interrupted" && !kr.Ok && !kr.NothingWritten && recs.Contains("interrupted") && StorageRestore.UnfinishedDiskRestores(s).Any(), "the step's process ended part way through the write: reported as an incomplete drive, its record says so (" + kr.Text + ")");
                Thread.Sleep(2000);
                // 6. refusals by the real step, nothing written
                var b1 = RawHead(sys, eB, idB.Bytes);
                var wrong = DiskIdentity.FromJson(Json.Parse(Json.Write(idB2.ToJson()))); wrong.Bytes += 512;
                var wr = RawDiskClient.Write(wrong, image, StorageRestore.Protected(s), null, null, null);
                C(wr.State == "refused" && wr.NothingWritten && wr.Text.Contains("size") && RawHead(sys, eB, idB.Bytes) == b1, "another drive identity (its size differs): the step refuses it, nothing written (" + wr.Text + ")");
                Environment.SetEnvironmentVariable("CCS_SBX_POLICY", "product");
                var pr = RawDiskClient.Write(idB2, image, StorageRestore.Protected(s), null, null, null);
                Environment.SetEnvironmentVariable("CCS_SBX_POLICY", null);
                C(pr.State == "refused" && pr.Text.Contains("not a USB drive or SD card") && RawHead(sys, eB, idB.Bytes) == b1, "with the product's own policy the step refuses a virtual hard disk (not a USB drive or SD card), nothing written (" + pr.Text + ")");
                var sysd = all.FirstOrDefault(x => x.Number == q.SystemDisk());
                if (sysd != null)
                {
                    var sr = RawDiskClient.Write(sysd, image, StorageRestore.Protected(s), null, null, null);
                    var sp = StorageRestore.PlanImage(o.PrimaryPath, "s01", new DiskTarget { DiskNumber = sysd.Number, Disk = sysd }, null, null, StorageRestore.Protected(s));
                    R.Set("system_disk_refusal", new JObj { { "step", sr.Text }, { "review", sp.Refusal } });
                    C(sr.State == "refused" && sr.NothingWritten && sp.Refusal != null, "the drive Windows runs from: refused at the review and by the step (" + sp.Refusal + " / " + sr.Text + ")");
                }
                else C(false, "the drive Windows runs from was not found among the drives");
                Diskpart(work, "select disk " + idB.Number, "select partition 1", "assign letter=T"); Thread.Sleep(2000);
                var place = @"T:\backups here"; try { Directory.CreateDirectory(place); } catch (Exception ex) { L("folder on the second drive: " + ex.Message); }
                var held = new List<ProtectedPath> { new ProtectedPath(place, "your backup location") };
                var hp = StorageRestore.PlanImage(o.PrimaryPath, "s01", new DiskTarget { DiskNumber = idB.Number, Disk = q.Describe(q.Disks().First(x => x.Number == idB.Number)) }, null, null, held);
                var hw = RawDiskClient.Write(q.Describe(q.Disks().First(x => x.Number == idB.Number)), image, held, null, null, null);
                C(hp.Refusal != null && hp.Refusal.Contains("your backup location") && hw.State == "refused" && hw.Text.Contains("your backup location"), "a drive holding the backup location (found by Windows' own volume extents) is refused at the review and by the step (" + hp.Refusal + " / " + hw.Text + ")");
                // 7. the step started without administrator permission (a standard account made in this Sandbox)
                try
                {
                    var pw = "Sx-" + Guid.NewGuid().ToString("N").Substring(0, 8) + "!a1";   // at most 14 characters: net user asks a question for longer ones
                    var nu = Process.Start(new ProcessStartInfo("net.exe", "user ccsstd " + pw + " /add") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true }); nu.StandardOutput.ReadToEnd(); nu.WaitForExit();
                    var kit = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
                    var req = new RawDiskRequest { Kind = "read", OperationId = Guid.NewGuid().ToString("N"), Nonce = Hex.Of(RawFrame.Random32()), Pipe = "ccs-raw-disk-" + Guid.NewGuid().ToString("N"), WindowPid = Process.GetCurrentProcess().Id, WindowProgram = Process.GetCurrentProcess().MainModule.FileName, CreatedUtc = DateTime.UtcNow, Target = idB2, RegionSize = 64L << 20 };
                    var (rf, rs) = RawDiskRequestFile.Write(req, work);
                    var ss = new SecureString(); foreach (var ch in pw) ss.AppendChar(ch);
                    var psi = new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, RawDiskHost.Flag + " \"" + rf + "\" " + rs) { UseShellExecute = false, UserName = "ccsstd", Password = ss, LoadUserProfile = true, WorkingDirectory = kit, CreateNoWindow = true };
                    psi.EnvironmentVariables["CCS_SBX_STD"] = "1";
                    var sp = Process.Start(psi); sp.WaitForExit(60000);
                    R.Set("standard_user_exit", "0x" + sp.ExitCode.ToString("X8"));
                    C(sp.ExitCode == RawDiskExit.NotElevated && File.Exists(rf), "the step started by a standard account stops at once without administrator permission, before reading even its request (exit 0x" + sp.ExitCode.ToString("X8") + ")");
                }
                catch (Exception ex) { R.Set("standard_user_error", ex.Message); C(false, "the step could not be started as a standard account: " + ex.Message); }
                Diskpart(work, "select vdisk file=\"" + bfile + "\"", "detach vdisk");
            }
            catch (Exception ex) { R.Set("error", ex.ToString()); L("stopped: " + ex); fail++; }
            Save(); sbxLog.Dispose();
            return fail == 0 ? 0 : 1;
        }
    }
}
