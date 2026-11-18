// Classic Cart Studio console engine tests (PC only; no console, no USB device is opened).
// Copyright (C) 2026 Rance9. GPL-3.0-or-later.
//   CcsConsole.Tests.exe <image folder>     (reports\hardware\h1ram-v2)
using CcsConsole.RamBoot;
using CcsConsole.Usb;
using FelLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

namespace CcsConsole.Tests
{
    static class Program
    {
        static int pass, fail;
        static void Check(bool ok, string what) { if (ok) pass++; else fail++; Console.WriteLine((ok ? "ok   " : "FAIL ") + what); }

        static (RamBootResult r, SimulatedFelDevice dev, List<RamBootProgress> prog, int factoryCalls) Run(RamImage img, Action<SimulatedFelDevice> setup = null, FakeDeviceList list = null)
        {
            var dev = new SimulatedFelDevice(); setup?.Invoke(dev); int calls = 0;
            list = list ?? new FakeDeviceList { Items = { FakeDeviceList.Dev(0x1F3A, 0xEFE8, "Nintendo Classic") } };
            var loader = new RamBootLoader(list, () => { calls++; return dev; }) { DramInitSleepMs = 0 };
            var prog = new List<RamBootProgress>(); loader.Progress += p => prog.Add(p);
            return (loader.Boot(img), dev, prog, calls);
        }

        static int Main(string[] args)
        {
            var folder = args.Length > 0 ? args[0] : @"..\..\..\..\..\..\reports\hardware\h1ram-v2";
            var img = RamImage.Load(folder);
            Console.WriteLine($"image {img.ImageSha256} ({img.ImageBytes} bytes, padded {img.Image.Length})\n");

            // 1. the exact Hakchi2 CE memboot sequence
            {
                var (r, d, prog, _) = Run(img);
                Check(r.Error == null && r.Transferred && r.BootCommandSent, "happy path: transferred and boot command sent (" + (r.Error ?? "no error") + ")");
                Check(d.Violations.Count == 0, "no protocol violation (DRAM touched only after fes1; chunks <= 64 KiB) " + string.Join("; ", d.Violations));
                Check(d.Log.IndexOf("verify") >= 0 && d.Log.IndexOf("verify") < d.Log.IndexOf("run 0x00002000"), "board verified before anything runs");
                Check(d.Executed.SequenceEqual(new uint[] { 0x2000, 0x47000000 }), "executed exactly fes1 (0x2000) then u-boot (0x47000000): " + string.Join(",", d.Executed.Select(x => x.ToString("X"))));
                Check(d.Read(0x2000, img.Fes1.Length).SequenceEqual(img.Fes1), "fes1 written at 0x2000");
                Check(d.Read(0x47400000, img.Image.Length).SequenceEqual(img.Image), "the padded boot image is in RAM at 0x47400000, byte for byte");
                Check(img.Image.Length % 0x20000 == 0 && img.Image.Length <= 0x2000000, "image padded to the 128 KiB sector, within 32 MiB");
                var off = Encoding.ASCII.GetString(img.UbootSd).IndexOf("bootcmd=", StringComparison.Ordinal) + 8;
                var ub = d.Read(0x47000000, img.UbootSd.Length);
                Check(ub.Take(off).SequenceEqual(img.UbootSd.Take(off)), "u-boot written at 0x47000000 (bytes before bootcmd unchanged)");
                Check(d.BootCommand == "boota 47400000", "u-boot runs exactly \"boota 47400000\" (" + d.BootCommand + ")");
                var imgDl = d.Downloads.Where(x => x.addr >= 0x47400000 && x.addr < 0x49400000).ToList();
                Check(imgDl.Count == img.Image.Length / 0x10000 && imgDl.Select((x, i) => x.addr == 0x47400000 + (uint)i * 0x10000 && x.len == 0x10000).All(b => b), $"image sent as {imgDl.Count} contiguous 64 KiB FEL_DOWNLOADs");
                Check(d.Downloads.All(x => x.addr == 0x2000 || (x.addr >= 0x47000000 && x.addr < 0x47400000 + img.Image.Length)), "nothing written outside fes1, u-boot and the image area");
                Check(d.Log.Last() == "close", "USB released at the end");
                var tp = prog.Where(p => p.Stage == RamBootStage.Transfer && p.Message == null).ToList();
                Check(tp.Count > 0 && tp.Last().Done == tp.Last().Total, "transfer progress reaches 100%");
                Check(prog.Last().Stage == RamBootStage.Done && prog.Last().Message.Contains("does not prove"), "the result says transfer is not proof of boot");
            }
            // 2. DRAM already initialized (fes1 resident): upstream skips the fes1 upload
            {
                var (r, d, _, _) = Run(img, x => { x.Poke(0x2000, img.Fes1); x.DramReady = true; });
                Check(r.Error == null && d.Executed.SequenceEqual(new uint[] { 0x47000000 }) && !d.Downloads.Any(x => x.addr == 0x2000), "resident fes1: no fes1 upload or run; image and u-boot as usual");
            }
            // 3. refusals before any write
            {
                var (r, d, _, _) = Run(img, x => x.Board = 0x00165100);
                Check(r.Error != null && r.FailedAt == RamBootStage.Open && d.Downloads.Count == 0 && d.Executed.Count == 0, "wrong board id: refused at Open, nothing written or run (" + r.Error + ")");
                (r, d, _, _) = Run(img, x => x.OpenSucceeds = false);
                Check(r.FailedAt == RamBootStage.Open && d.Downloads.Count == 0, "wrong endpoints: refused at Open (" + r.Error + ")");
            }
            // 4. discovery: only 1F3A:EFE8 counts; Samsung's 04E8:6863 and others are ignored
            {
                var others = new[] { FakeDeviceList.Dev(0x04E8, 0x6863, "SAMSUNG Mobile USB Composite"), FakeDeviceList.Dev(0x046D, 0xC52B, "Logitech receiver"), FakeDeviceList.Dev(0x1F3A, 0x1234, "Allwinner other") };
                var l1 = new FakeDeviceList(); l1.Items.AddRange(others); l1.Items.Add(FakeDeviceList.Dev(0x1F3A, 0xEFE8, "Nintendo Classic"));
                Check(FelDiscovery.FindFel(l1).Count == 1, "discovery picks only 1F3A:EFE8 among Samsung, Logitech and other Allwinner devices");
                var (r, _, _, calls) = Run(img, null, new FakeDeviceList { Items = others.ToList() });
                Check(r.FailedAt == RamBootStage.Discover && calls == 0, "no FEL device: refused, the USB transport is never created");
                var l2 = new FakeDeviceList(); l2.Items.Add(FakeDeviceList.Dev(0x1F3A, 0xEFE8, "a")); l2.Items.Add(FakeDeviceList.Dev(0x1F3A, 0xEFE8, "b"));
                (r, _, _, calls) = Run(img, null, l2);
                Check(r.FailedAt == RamBootStage.Discover && calls == 0, "two FEL devices: refused (ambiguous)");
            }
            // 5. a transfer error stops everything: no boot command
            {
                var (r, d, _, _) = Run(img, x => x.FailDownloadAtChunk = 40);
                Check(r.Error != null && r.FailedAt == RamBootStage.Transfer && !r.BootCommandSent && !d.Executed.Contains(0x47000000), "error during the transfer: failed at Transfer, u-boot never run (" + r.Error + ")");
                Check(d.Log.Last() == "close", "USB released after the error");
            }
            // 6. no flash or arbitrary u-boot command can be issued through the engine
            {
                var flash = typeof(Fel).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).Where(m => m.Name.IndexOf("Flash", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                Check(flash.Count == 0, "Fel has no flash read/write methods");
                var dev = new SimulatedFelDevice(); var f = new Fel(dev) { Fes1Bin = img.Fes1, UBootBin = img.UbootSd, DramInitSleepMs = 0 }; f.Open(out _);
                foreach (var cmd in new[] { "sunxi_flash phy_write 47400000 0 1", "boota 47400000; sunxi_flash phy_write 0 0 1", "efex_test", "reset", "" })
                {
                    bool threw = false; try { f.RunUbootCmd(cmd); } catch (FelException) { threw = true; }
                    Check(threw && dev.Downloads.Count == 0 && dev.Executed.Count == 0, $"u-boot command refused before any transfer: \"{cmd}\"");
                }
                var asm = typeof(RamBootLoader).Assembly; var text = string.Join("\n", asm.GetTypes().SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Where(x => x.IsLiteral).Select(x => x.GetValue(null)?.ToString() ?? "")));
                Check(text.IndexOf("phy_write", StringComparison.Ordinal) < 0, "no flash command string in the engine's constants");
            }
            // 7. invalid images are refused before any USB activity
            {
                string tmp = Path.Combine(Path.GetTempPath(), "ccs-h1-img-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                string Clone() { var d = tmp + "-" + Guid.NewGuid().ToString("N").Substring(0, 6); Directory.CreateDirectory(d); foreach (var f in Directory.GetFiles(img.Folder)) File.Copy(f, Path.Combine(d, Path.GetFileName(f))); return d; }
                void Resum(string d, string name)
                {   // make SHA256SUMS and MANIFEST.json agree with a modified file (so only the deeper checks can refuse it)
                    var h = RamImage.Sha256(File.ReadAllBytes(Path.Combine(d, name)));
                    var sums = File.ReadAllLines(Path.Combine(d, "SHA256SUMS")).Select(l => l.EndsWith("  " + name) ? h + "  " + name : l); File.WriteAllLines(Path.Combine(d, "SHA256SUMS"), sums);
                    var js = new JavaScriptSerializer(); var man = (Dictionary<string, object>)js.DeserializeObject(File.ReadAllText(Path.Combine(d, "MANIFEST.json")));
                    ((Dictionary<string, object>)((Dictionary<string, object>)man["outputs"])[name])["sha256"] = h; File.WriteAllText(Path.Combine(d, "MANIFEST.json"), js.Serialize(man));
                }
                void Expect(string what, Action<string> mutate, string expect)
                {
                    var d = Clone(); mutate(d); string err = null; try { RamImage.Load(d); } catch (RamImageException ex) { err = ex.Message; }
                    Check(err != null && err.IndexOf(expect, StringComparison.OrdinalIgnoreCase) >= 0, $"refused: {what} ({err ?? "ACCEPTED"})");
                    try { Directory.Delete(d, true); } catch { }
                }
                void Patch(string d, string name, Action<byte[]> f) { var p = Path.Combine(d, name); var b = File.ReadAllBytes(p); f(b); File.WriteAllBytes(p, b); }
                Expect("image changed after the build", d => Patch(d, "ccs-h1-boot.img", b => b[5000] ^= 1), "SHA256SUMS");
                Expect("SHA256SUMS updated but not MANIFEST.json", d => { Patch(d, "ccs-h1-boot.img", b => b[5000] ^= 1); var h = RamImage.Sha256(File.ReadAllBytes(Path.Combine(d, "ccs-h1-boot.img"))); File.WriteAllLines(Path.Combine(d, "SHA256SUMS"), File.ReadAllLines(Path.Combine(d, "SHA256SUMS")).Select(l => l.EndsWith("  ccs-h1-boot.img") ? h + "  ccs-h1-boot.img" : l)); }, "MANIFEST");
                Expect("other fes1 (consistent sums)", d => { Patch(d, "fes1.bin", b => b[100] ^= 1); Resum(d, "fes1.bin"); }, "expected hakchi");
                Expect("other u-boot (consistent sums)", d => { Patch(d, "uboot-sd.bin", b => b[100] ^= 1); Resum(d, "uboot-sd.bin"); }, "expected hakchi");
                Expect("not an Android boot image", d => { Patch(d, "ccs-h1-boot.img", b => b[0] = (byte)'X'); Resum(d, "ccs-h1-boot.img"); }, "Android");
                Expect("clovershell command line", d => { Patch(d, "ccs-h1-boot.img", b => { var c = Encoding.ASCII.GetBytes("hakchi-clovershell"); var s = Encoding.ASCII.GetString(b, 64, 512); int k = s.IndexOf("hakchi-shell"); Array.Copy(c, 0, b, 64 + k, c.Length); }); Resum(d, "ccs-h1-boot.img"); }, "clovershell");
                Expect("larger than 32 MiB", d => { var p = Path.Combine(d, "ccs-h1-boot.img"); var b = File.ReadAllBytes(p); var big = new byte[0x2400000]; Array.Copy(b, big, b.Length); BitConverter.GetBytes(0x2100000u).CopyTo(big, 16); File.WriteAllBytes(p, big); Resum(d, "ccs-h1-boot.img"); }, "32 MiB");
                Expect("truncated image", d => { var p = Path.Combine(d, "ccs-h1-boot.img"); var b = File.ReadAllBytes(p); File.WriteAllBytes(p, b.Take(b.Length / 2).ToArray()); Resum(d, "ccs-h1-boot.img"); }, "truncated");
                Expect("other load address", d => { Patch(d, "ccs-h1-boot.img", b => BitConverter.GetBytes(0x42000000u).CopyTo(b, 20)); Resum(d, "ccs-h1-boot.img"); }, "layout");
                Expect("wrong schema", d => File.WriteAllText(Path.Combine(d, "MANIFEST.json"), File.ReadAllText(Path.Combine(d, "MANIFEST.json")).Replace("ccs-h1-ram-image/2", "ccs-h1-ram-image/1")), "schema");
                Expect("other boot command", d => File.WriteAllText(Path.Combine(d, "MANIFEST.json"), File.ReadAllText(Path.Combine(d, "MANIFEST.json")).Replace("boota 47400000", "boota 43800000")), "FEL parameters");
                Expect("image name with a path", d => File.WriteAllText(Path.Combine(d, "MANIFEST.json"), File.ReadAllText(Path.Combine(d, "MANIFEST.json")).Replace("\"image\": \"ccs-h1-boot.img\"", "\"image\": \"..\\\\x.img\"")), "plain image file name");
                Expect("u-boot missing", d => File.Delete(Path.Combine(d, "uboot-sd.bin")), "missing");
                Expect("manifest missing", d => File.Delete(Path.Combine(d, "MANIFEST.json")), "missing");
            }
            Console.WriteLine($"\n{pass} passed, {fail} failed");
            return fail == 0 ? 0 : 1;
        }
    }
}
