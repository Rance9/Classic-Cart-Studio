// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The HOME Designer's render check, from managed code (HOME-DESIGNER.md "The render check"): the compiled design is decoded and drawn
// by the native console HOME itself (tools/console-simulator/core/design_check.c, ccs_home_render_check) in every theme and every
// required screen and state, next to the theme defaults, and judged there: readability of the HOME's text and the design's labels, every
// character in its lettering, the shared menus inside their panels and the screen's safe area, the fallback when a picture is missing,
// the startup picture, and the measured decoded-memory peak against DesignProfile.ActiveDecodedBudget. Two ways to reach that code:
//   ProcessHomeRenderer   ccs-sim-core.exe --render-check (Classic Cart Studio for Windows carries it as native/ccs-sim-core.exe; tests)
//   HomeRenderNative      the standalone Workbench's native host, which links the same C code and passes it in its ABI table (proposal:
//                         native-host ABI version 2, HOME-DESIGNER.md "Host ABI"); absent anywhere else, never an extracted helper
// Nothing here writes to a console; the result (its report's SHA-256) is what an apply plan binds.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace CcsBackup.Compose
{
    public sealed class RenderState
    {
        public string Theme, State, DefaultSha256, DesignSha256; public bool Ok; public int Texts; public double MinContrast; public long PeakBytes; public List<string> Problems = new List<string>();
        /// <summary>texts the design makes harder to read: (text, its box on the 1280x720 screen, contrast, the contrast it needs)</summary>
        public List<(string text, int x0, int y0, int x1, int y1, double contrast, double need)> HardToRead = new List<(string, int, int, int, int, double, double)>();
        /// <summary>where the HOME draws its own controls on this screen (layout names as the renderer records them: cell, card, tab, legend, menu ...)</summary>
        public List<(string name, int x0, int y0, int x1, int y1)> Controls = new List<(string, int, int, int, int)>();
    }

    /// <summary>one drawn screen: RGB, 3 bytes a pixel</summary>
    public sealed class RenderFrame
    {
        public string Theme, State; public bool WithDesign; public int Width, Height; public byte[] Rgb;
        /// <summary>the frame as an RGB PNG (the engine's own PNG writer: the same pixels give the same bytes)</summary>
        public byte[] Png()
        {
            var bgra = new byte[Width * Height * 4];
            for (int i = 0, j = 0; i < Rgb.Length; i += 3, j += 4) { bgra[j] = Rgb[i + 2]; bgra[j + 1] = Rgb[i + 1]; bgra[j + 2] = Rgb[i]; bgra[j + 3] = 255; }
            return ImageRules.EncodePng(Width, Height, bgra, false);
        }
    }

    public sealed class RenderCheckOptions
    {
        public string WorkDir;                 // an empty folder the check may write into (created; removed afterwards unless KeepWork)
        public bool Frames = true, DefaultFrames, KeepWork;
        public CancellationToken Cancel;
        /// <summary>which drawn screens to keep in the result (theme, state, drawn with the design); null keeps every one. The check itself,
        /// its report and its verdict are the same either way: only the pictures handed back are fewer.</summary>
        public Func<string, string, bool, bool> KeepFrame;
    }

    public sealed class RenderCheckResult
    {
        public bool Ok; public int Result; public string Renderer, Report, ReportSha256, DesignId, DesignTheme, PackJsonSha256, StartupMode, FallbackWhy;
        public bool FallbackSameAsDefaults;
        public List<RenderState> States = new List<RenderState>();
        public List<string> Problems = new List<string>();
        public List<RenderFrame> Frames = new List<RenderFrame>();
        public long DefaultsLoadPeakBytes, DesignLoadPeakBytes, DefaultsStatePeakBytes, DesignStatePeakBytes, DefaultsResidentBytes, DesignResidentBytes, DesignDecoderPeakBytes, AddedPeakBytes, StartupPeakBytes;
        public long Budget = DesignProfile.ActiveDecodedBudget;

        /// <summary>reads the native check's JSON report (schema ccs-home-render-check/1)</summary>
        public static RenderCheckResult FromReport(string json, string renderer)
        {
            var r = new RenderCheckResult { Renderer = renderer, Report = json, ReportSha256 = Sha.Of(json.Trim()) };
            var d = Json.Parse(json);
            if (d.S("schema") != "ccs-home-render-check/1") throw new ComposeException("render.report", "The HOME renderer did not answer with a render check report.");
            r.Ok = d.B("ok"); r.Result = (int)d.L("result", 2); r.DesignId = d.S("design"); r.DesignTheme = d.S("design_theme"); r.PackJsonSha256 = d.S("pack_json_sha256");
            r.Problems.AddRange((d.A("problems") ?? new List<object>()).OfType<string>());
            foreach (var s in (d.A("states") ?? new List<object>()).OfType<Dictionary<string, object>>())
            {
                var st = new RenderState { Theme = s.S("theme"), State = s.S("state"), Ok = s.B("ok"), DefaultSha256 = s.S("default_sha256"), DesignSha256 = s.S("design_sha256"), Texts = (int)s.L("texts", 0), PeakBytes = s.L("peak_bytes", 0) };
                st.MinContrast = s.TryGetValue("min_contrast", out var mc) ? Convert.ToDouble(mc, CultureInfo.InvariantCulture) : 0;
                st.Problems.AddRange((s.A("problems") ?? new List<object>()).OfType<string>()); r.States.Add(st);
                int I(object v) => Convert.ToInt32(v, CultureInfo.InvariantCulture);
                foreach (var h in (s.A("hard_to_read") ?? new List<object>()).OfType<Dictionary<string, object>>())
                { var b = h.A("box"); if (b == null || b.Count != 4) continue; st.HardToRead.Add((h.S("text"), I(b[0]), I(b[1]), I(b[2]), I(b[3]), Convert.ToDouble(h["contrast"], CultureInfo.InvariantCulture), Convert.ToDouble(h["need"], CultureInfo.InvariantCulture))); }
                foreach (var c in (s.A("controls") ?? new List<object>()).OfType<object[]>().Select(x => x.ToList()).Concat((s.A("controls") ?? new List<object>()).OfType<List<object>>()))
                    if (c.Count == 5 && c[0] is string cn) st.Controls.Add((cn, I(c[1]), I(c[2]), I(c[3]), I(c[4])));
            }
            var m = d.O("memory");
            if (m != null)
            {
                r.DefaultsLoadPeakBytes = m.L("defaults_load_peak_bytes", 0); r.DesignLoadPeakBytes = m.L("design_load_peak_bytes", 0); r.DefaultsStatePeakBytes = m.L("defaults_state_peak_bytes", 0);
                r.DesignStatePeakBytes = m.L("design_state_peak_bytes", 0); r.DefaultsResidentBytes = m.L("defaults_resident_bytes", 0); r.DesignResidentBytes = m.L("design_resident_bytes", 0);
                r.DesignDecoderPeakBytes = m.L("design_decoder_peak_bytes", 0); r.AddedPeakBytes = m.L("added_peak_bytes", 0); r.Budget = m.L("peak_budget_bytes", DesignProfile.ActiveDecodedBudget);
            }
            var fb = d.O("fallback"); if (fb != null) { r.FallbackSameAsDefaults = fb.B("same_as_defaults"); r.FallbackWhy = fb.S("why"); }
            var su = d.O("startup"); if (su != null) { r.StartupMode = su.S("mode"); r.StartupPeakBytes = su.L("peak_bytes", 0); }
            if (r.Budget != DesignProfile.ActiveDecodedBudget) { r.Ok = false; r.Problems.Add("The console HOME renderer measures against another memory budget (" + Units.Size(r.Budget) + "); this program expects " + Units.Size(DesignProfile.ActiveDecodedBudget) + "."); }
            return r;
        }
    }

    public interface IHomeRenderer
    {
        string Name { get; }
        /// <summary>packDir: the HOME package the console runs (its themes, fonts and games); designDir: a compiled design (null: the theme
        /// defaults only)</summary>
        RenderCheckResult Check(string packDir, string designDir, RenderCheckOptions options);
    }

    /// <summary>The HOME as the console starts it (ccs_home_design_boot, the same native code as the render check): its design store is
    /// read the way the console reads ccs/data/home-design (current, then known-good) and its first screen is drawn. HOME Designer's
    /// preview: one screen, quickly, on this PC. Ok with Source "current": the HOME used the design it was given; any other source means it
    /// set the design aside (Why says why) and the frame shows something else, so it is never the design's picture.</summary>
    public sealed class HomeBootResult
    {
        public bool Ok; public string Source, DesignId, Theme, FrameSha256, Why, Report; public RenderFrame Frame;
        public bool UsedDesign => Ok && Source == "current" && Frame != null;
        internal static HomeBootResult FromReport(string json)
        {
            var d = Json.Parse(json);
            if (d.S("schema") != "ccs-home-design-boot/1") throw new ComposeException("render.report", "The HOME renderer did not answer with a start report.");
            return new HomeBootResult { Report = json, Ok = d.B("ok"), Source = d.S("source"), DesignId = d.S("design"), Theme = d.S("theme"), FrameSha256 = d.S("frame_sha256"), Why = d.S("why") ?? d.S("error") };
        }
    }

    public static class HomeBoot
    {
        /// <summary>puts a compiled design into designRoot the way the console keeps it (d/&lt;id&gt;/ and the "current" pointer) and removes
        /// any other design kept there; designRoot is a folder of this program's own (never a console's)</summary>
        public static void Place(CompiledDesign c, string designRoot)
        {
            var d = Path.Combine(designRoot, "d"); Directory.CreateDirectory(d);
            foreach (var old in Directory.GetDirectories(d)) if (!string.Equals(Path.GetFileName(old), c.Id, StringComparison.Ordinal)) Directory.Delete(old, true);
            var mine = Path.Combine(d, c.Id); if (!Directory.Exists(mine)) c.WriteTo(mine);
            File.WriteAllText(Path.Combine(designRoot, "current"), c.Id + "\n", new UTF8Encoding(false));
        }

        /// <summary>the HOME's first screen as it starts with designRoot's design (Place). workDir: an empty folder for the HOME's own state
        /// and the frame (created; removed afterwards). The renderer is the same one the render check uses.</summary>
        public static HomeBootResult Draw(IHomeRenderer renderer, string packDir, string designRoot, string workDir)
        {
            if (renderer is HomeRenderNative n) return n.Boot(packDir, designRoot, workDir);
            if (renderer is ProcessHomeRenderer p) return p.Boot(packDir, designRoot, workDir);
            throw new ComposeException("render.boot", "This HOME renderer cannot draw the HOME as it starts.");
        }

        internal static HomeBootResult Finish(string report, string frame)
        {
            var r = HomeBootResult.FromReport(report);
            if (r.Ok && File.Exists(frame)) { var (w, h, rgb) = ProcessHomeRenderer.ReadRgb(frame); r.Frame = new RenderFrame { Theme = r.Theme, State = "start", WithDesign = r.Source == "current", Width = w, Height = h, Rgb = rgb }; }
            return r;
        }
    }

    /// <summary>the native HOME as a process: ccs-sim-core.exe --render-check (Classic Cart Studio for Windows' native/ccs-sim-core.exe)</summary>
    public sealed class ProcessHomeRenderer : IHomeRenderer
    {
        public readonly string Exe;
        public ProcessHomeRenderer(string exe) { Exe = exe; }
        public string Name => "ccs-sim-core " + Path.GetFileName(Exe);
        public RenderCheckResult Check(string packDir, string designDir, RenderCheckOptions o)
        {
            o = o ?? new RenderCheckOptions();
            if (!File.Exists(Exe)) throw new ComposeException("render.missing", "The console HOME renderer is not installed (" + Path.GetFileName(Exe) + ").");
            var work = o.WorkDir ?? Path.Combine(Path.GetTempPath(), "ccs-render-check-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(work); var frames = Path.Combine(work, "frames"); Directory.CreateDirectory(frames); var state = Path.Combine(work, "work"); Directory.CreateDirectory(state);
            try
            {
                var args = new StringBuilder("--render-check --pack " + Q(packDir) + " --work " + Q(state));
                if (designDir != null) args.Append(" --design ").Append(Q(designDir));
                if (o.Frames || o.DefaultFrames) args.Append(" --frames ").Append(Q(frames));
                if (o.DefaultFrames) args.Append(" --default-frames");
                var psi = new ProcessStartInfo(Exe, args.ToString()) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, StandardOutputEncoding = new UTF8Encoding(false) };
                string output, err; int code;
                using (var p = Process.Start(psi))
                {
                    var ot = p.StandardOutput.ReadToEndAsync(); var et = p.StandardError.ReadToEndAsync();
                    while (!p.WaitForExit(250)) if (o.Cancel.IsCancellationRequested) { try { p.Kill(); } catch (InvalidOperationException) { } throw new OperationCanceledException(); }
                    output = ot.Result; err = et.Result; code = p.ExitCode;
                }
                var line = output.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith("{"));
                if (line == null) throw new ComposeException("render.failed", "The console HOME renderer stopped without a report (status " + code + ")" + (err.Length > 0 ? ": " + Display.Line(err, 200) : "."));
                var r = RenderCheckResult.FromReport(line, Name);
                foreach (var f in Directory.EnumerateFiles(frames, "*.ppm").OrderBy(x => x, StringComparer.Ordinal))
                {
                    var (ft, fs, fw) = FrameName(f);
                    if (o.KeepFrame == null || o.KeepFrame(ft, fs, fw)) r.Frames.Add(ReadPpm(f));
                }
                return r;
            }
            finally { if (!o.KeepWork && o.WorkDir == null) try { Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
        static string Q(string s) => "\"" + s.TrimEnd('\\') + "\"";

        /// <summary>ccs-sim-core.exe --design-boot (HomeBoot.Draw)</summary>
        public HomeBootResult Boot(string packDir, string designRoot, string workDir)
        {
            if (!File.Exists(Exe)) throw new ComposeException("render.missing", "The console HOME renderer is not installed (" + Path.GetFileName(Exe) + ").");
            Directory.CreateDirectory(workDir); var frame = Path.Combine(workDir, "start.ppm"); var state = Path.Combine(workDir, "state"); Directory.CreateDirectory(state);
            var args = "--design-boot --pack " + Q(packDir) + " --work " + Q(state) + (designRoot != null ? " --design-root " + Q(designRoot) : "") + " --frame " + Q(frame);
            var psi = new ProcessStartInfo(Exe, args) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, StandardOutputEncoding = new UTF8Encoding(false) };
            string output, err; int code;
            using (var p = Process.Start(psi))
            {
                var ot = p.StandardOutput.ReadToEndAsync(); var et = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(120000)) { try { p.Kill(); } catch (InvalidOperationException) { } throw new ComposeException("render.failed", "The console HOME renderer did not finish drawing."); }
                output = ot.Result; err = et.Result; code = p.ExitCode;
            }
            var line = output.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith("{"));
            if (line == null) throw new ComposeException("render.failed", "The console HOME renderer stopped without a report (status " + code + ")" + (err.Length > 0 ? ": " + Display.Line(err, 200) : "."));
            return HomeBoot.Finish(line, frame);
        }

        internal static (int w, int h, byte[] rgb) ReadRgb(string path)
        {
            var b = File.ReadAllBytes(path); int p = 0;
            string Tok() { while (p < b.Length && char.IsWhiteSpace((char)b[p])) p++; int s = p; while (p < b.Length && !char.IsWhiteSpace((char)b[p])) p++; return Encoding.ASCII.GetString(b, s, p - s); }
            if (Tok() != "P6") throw new IOException("not a PPM frame: " + path);
            int w = int.Parse(Tok(), CultureInfo.InvariantCulture), h = int.Parse(Tok(), CultureInfo.InvariantCulture); Tok(); p++;
            if (w <= 0 || h <= 0 || (long)w * h * 3 > b.Length - p) throw new IOException("a damaged PPM frame: " + path);
            var rgb = new byte[w * h * 3]; Buffer.BlockCopy(b, p, rgb, 0, rgb.Length);
            return (w, h, rgb);
        }

        /// <summary>a frame file's theme, state and whether it was drawn with the design, from its name (with-|without-THEME-STATE.ppm)</summary>
        static (string theme, string state, bool with) FrameName(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path); bool with = name.StartsWith("with-"); var rest = name.Substring(with ? 5 : 8);
            string theme = new[] { "arcade-grid", "box-art-showcase", "featured-shelf", "adventure-map" }.FirstOrDefault(t => rest.StartsWith(t + "-")) ?? "startup";
            return (theme, theme == "startup" ? "startup" : rest.Substring(theme.Length + 1), with);
        }

        internal static RenderFrame ReadPpm(string path)
        {
            var (w, h, rgb) = ReadRgb(path); var (theme, state, with) = FrameName(path);
            return new RenderFrame { Theme = theme, State = state, WithDesign = with, Width = w, Height = h, Rgb = rgb };
        }
    }

    /// <summary>The same C entry point called inside the standalone Workbench's native host, through its ABI table (proposal: version 2 adds
    /// home_render_check after the PAR2 functions; HOME-DESIGNER.md "Host ABI"). Available only there; NotHosted everywhere else.</summary>
    public sealed class HomeRenderNative : IHomeRenderer
    {
        public const string AbiDataName = "CcsNativeHost.Abi";
        const ulong Magic = 0x3154534f48534343UL;   // "CCSHOST1"
        public const uint MinAbiVersion = 2;
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FrameFn(IntPtr ctx, IntPtr theme, IntPtr state, int withDesign, IntPtr rgb, int w, int h);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int CheckFn(IntPtr request, IntPtr report, uint cap, out uint len);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int BootFn(IntPtr pack, IntPtr designRoot, IntPtr stateDir, IntPtr framePpm, IntPtr report, uint cap, out uint len);
        [StructLayout(LayoutKind.Sequential)]
        struct Request { public uint size, flags; public IntPtr pack, design, work, frames, onFrame, ctx, cancel; }
        static readonly Lazy<(CheckFn fn, BootFn boot, string why)> abi = new Lazy<(CheckFn, BootFn, string)>(Find, LazyThreadSafetyMode.ExecutionAndPublication);
        /// <summary>the native HOME code keeps process-wide drawing state (its scale, clock and text probes): one call at a time</summary>
        static readonly object Gate = new object();
        public static bool Available => abi.Value.fn != null;
        public static string UnavailableReason => abi.Value.why;
        public string Name => "native host";

        static (CheckFn, BootFn, string) Find()
        {
            const string not = "The HOME render check runs inside the standalone CCS Console Workbench, or through Classic Cart Studio's own HOME renderer.";
            object d = null; try { d = AppDomain.CurrentDomain.GetData(AbiDataName); } catch { }
            if (!(d is long address) || address == 0) return (null, null, not);
            var t = new IntPtr(address); int p = IntPtr.Size;
            try
            {
                if ((ulong)Marshal.ReadInt64(t, 0) != Magic || (uint)Marshal.ReadInt32(t, 8) < MinAbiVersion) return (null, null, not + " (this host has no HOME renderer)");
                int v1 = 24 + 6 * p, slot = v1 + 8;   // version 2: home_check_abi u32, reserved u32, then home_render_check, home_design_boot
                if ((uint)Marshal.ReadInt32(t, 12) < slot + 2 * p || (uint)Marshal.ReadInt32(t, v1) != 1) return (null, null, not + " (this host's HOME renderer interface differs)");
                var f = Marshal.ReadIntPtr(t, slot); if (f == IntPtr.Zero) return (null, null, not + " (this host was built without the HOME renderer)");
                var b = Marshal.ReadIntPtr(t, slot + p);
                return ((CheckFn)Marshal.GetDelegateForFunctionPointer(f, typeof(CheckFn)), b == IntPtr.Zero ? null : (BootFn)Marshal.GetDelegateForFunctionPointer(b, typeof(BootFn)), null);
            }
            catch (Exception ex) { return (null, null, not + " (" + ex.GetType().Name + ")"); }
        }

        /// <summary>home_design_boot in the host's ABI table (HomeBoot.Draw)</summary>
        public HomeBootResult Boot(string packDir, string designRoot, string workDir)
        {
            var a = abi.Value; if (a.boot == null) throw new ComposeException("render.not-hosted", a.why ?? "This host's HOME renderer cannot draw the HOME as it starts.");
            Directory.CreateDirectory(workDir); var frame = Path.Combine(workDir, "start.ppm"); var state = Path.Combine(workDir, "state"); Directory.CreateDirectory(state);
            var handles = new List<IntPtr>(); IntPtr Utf8(string s) { if (s == null) return IntPtr.Zero; var b = Encoding.UTF8.GetBytes(s + "\0"); var h = Marshal.AllocHGlobal(b.Length); Marshal.Copy(b, 0, h, b.Length); handles.Add(h); return h; }
            try
            {
                lock (Gate)
                {
                    uint cap = 64u << 10; var rep = Marshal.AllocHGlobal((int)cap); handles.Add(rep);
                    IntPtr pk = Utf8(packDir), root = Utf8(designRoot), st = Utf8(state), fr = Utf8(frame);
                    int rc = a.boot(pk, root, st, fr, rep, cap, out var len);
                    if (rc == 3) { cap = len + 1; rep = Marshal.AllocHGlobal((int)cap); handles.Add(rep); rc = a.boot(pk, root, st, fr, rep, cap, out len); }
                    if (len == 0 || len >= cap) throw new ComposeException("render.failed", "The console HOME renderer stopped without a report (status " + rc + ").");
                    var bytes = new byte[len]; Marshal.Copy(rep, bytes, 0, (int)len);
                    return HomeBoot.Finish(Encoding.UTF8.GetString(bytes), frame);
                }
            }
            finally { foreach (var h in handles) Marshal.FreeHGlobal(h); }
        }

        public RenderCheckResult Check(string packDir, string designDir, RenderCheckOptions o)
        {
            lock (Gate) return CheckOnce(packDir, designDir, o);
        }

        RenderCheckResult CheckOnce(string packDir, string designDir, RenderCheckOptions o)
        {
            o = o ?? new RenderCheckOptions(); var a = abi.Value; if (a.fn == null) throw new ComposeException("render.not-hosted", a.why);
            var work = o.WorkDir ?? Path.Combine(Path.GetTempPath(), "ccs-render-check-" + Guid.NewGuid().ToString("N").Substring(0, 8)); Directory.CreateDirectory(work);
            var frames = new List<RenderFrame>(); var handles = new List<IntPtr>(); IntPtr Utf8(string s) { if (s == null) return IntPtr.Zero; var b = Encoding.UTF8.GetBytes(s + "\0"); var h = Marshal.AllocHGlobal(b.Length); Marshal.Copy(b, 0, h, b.Length); handles.Add(h); return h; }
            FrameFn cb = (ctx, th, st, with, rgb, w, h) =>
            {
                try
                {
                    string theme = Marshal.PtrToStringAnsi(th), state = Marshal.PtrToStringAnsi(st);
                    if (o.KeepFrame == null || o.KeepFrame(theme, state, with != 0)) { var px = new byte[w * h * 3]; Marshal.Copy(rgb, px, 0, px.Length); frames.Add(new RenderFrame { Theme = theme, State = state, WithDesign = with != 0, Width = w, Height = h, Rgb = px }); }
                    return o.Cancel.IsCancellationRequested ? 1 : 0;
                }
                catch { return 1; }
            };
            var cancel = Marshal.AllocHGlobal(4); Marshal.WriteInt32(cancel, 0); handles.Add(cancel);
            using (o.Cancel.Register(() => Marshal.WriteInt32(cancel, 1)))
            {
                try
                {
                    var q = new Request { size = (uint)Marshal.SizeOf(typeof(Request)), flags = (o.Frames ? 1u : 0) | (o.DefaultFrames ? 2u : 0), pack = Utf8(packDir), design = Utf8(designDir), work = Utf8(work), frames = IntPtr.Zero,
                                          onFrame = Marshal.GetFunctionPointerForDelegate(cb), ctx = IntPtr.Zero, cancel = cancel };
                    var qp = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Request))); handles.Add(qp); Marshal.StructureToPtr(q, qp, false);
                    uint cap = 1u << 20; var rep = Marshal.AllocHGlobal((int)cap); handles.Add(rep);
                    int rc = a.fn(qp, rep, cap, out var len);
                    if (rc == 3) { cap = len + 1; rep = Marshal.AllocHGlobal((int)cap); handles.Add(rep); rc = a.fn(qp, rep, cap, out len); }
                    var bytes = new byte[len]; Marshal.Copy(rep, bytes, 0, (int)len);
                    var r = RenderCheckResult.FromReport(Encoding.UTF8.GetString(bytes), Name); r.Frames.AddRange(frames); GC.KeepAlive(cb); return r;
                }
                finally { foreach (var h in handles) Marshal.FreeHGlobal(h); if (!o.KeepWork && o.WorkDir == null) try { Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
            }
        }
    }

    public static class HomeRenderers
    {
        /// <summary>the native host's renderer in the standalone Workbench, else the given ccs-sim-core.exe (Classic Cart Studio's
        /// native/ccs-sim-core.exe, or CCS_HOME_RENDERER for the tests); null when neither is there</summary>
        public static IHomeRenderer Find(string exe = null)
        {
            if (HomeRenderNative.Available) return new HomeRenderNative();
            exe = exe ?? Environment.GetEnvironmentVariable("CCS_HOME_RENDERER");
            return exe != null && File.Exists(exe) ? new ProcessHomeRenderer(exe) : null;
        }
    }
}
