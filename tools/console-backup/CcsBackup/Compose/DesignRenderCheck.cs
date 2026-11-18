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
                foreach (var f in Directory.EnumerateFiles(frames, "*.ppm").OrderBy(x => x, StringComparer.Ordinal)) r.Frames.Add(ReadPpm(f));
                return r;
            }
            finally { if (!o.KeepWork && o.WorkDir == null) try { Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
        static string Q(string s) => "\"" + s.TrimEnd('\\') + "\"";
        internal static RenderFrame ReadPpm(string path)
        {
            var b = File.ReadAllBytes(path); int p = 0;
            string Tok() { while (p < b.Length && char.IsWhiteSpace((char)b[p])) p++; int s = p; while (p < b.Length && !char.IsWhiteSpace((char)b[p])) p++; return Encoding.ASCII.GetString(b, s, p - s); }
            if (Tok() != "P6") throw new IOException("not a PPM frame: " + path);
            int w = int.Parse(Tok(), CultureInfo.InvariantCulture), h = int.Parse(Tok(), CultureInfo.InvariantCulture); Tok(); p++;
            var rgb = new byte[w * h * 3]; Buffer.BlockCopy(b, p, rgb, 0, rgb.Length);
            var name = Path.GetFileNameWithoutExtension(path); bool with = name.StartsWith("with-"); var rest = name.Substring(with ? 5 : 8);
            string theme = new[] { "arcade-grid", "box-art-showcase", "featured-shelf", "adventure-map" }.FirstOrDefault(t => rest.StartsWith(t + "-")) ?? "startup";
            return new RenderFrame { Theme = theme, State = theme == "startup" ? "startup" : rest.Substring(theme.Length + 1), WithDesign = with, Width = w, Height = h, Rgb = rgb };
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
        [StructLayout(LayoutKind.Sequential)]
        struct Request { public uint size, flags; public IntPtr pack, design, work, frames, onFrame, ctx, cancel; }
        static readonly Lazy<(CheckFn fn, string why)> abi = new Lazy<(CheckFn, string)>(Find, LazyThreadSafetyMode.ExecutionAndPublication);
        public static bool Available => abi.Value.fn != null;
        public static string UnavailableReason => abi.Value.why;
        public string Name => "native host";

        static (CheckFn, string) Find()
        {
            const string not = "The HOME render check runs inside the standalone CCS Console Workbench, or through Classic Cart Studio's own HOME renderer.";
            object d = null; try { d = AppDomain.CurrentDomain.GetData(AbiDataName); } catch { }
            if (!(d is long address) || address == 0) return (null, not);
            var t = new IntPtr(address); int p = IntPtr.Size;
            try
            {
                if ((ulong)Marshal.ReadInt64(t, 0) != Magic || (uint)Marshal.ReadInt32(t, 8) < MinAbiVersion) return (null, not + " (this host has no HOME renderer)");
                int v1 = 24 + 6 * p, slot = v1 + 8;   // version 2: home_check_abi u32, reserved u32, then home_render_check, home_design_boot
                if ((uint)Marshal.ReadInt32(t, 12) < slot + 2 * p || (uint)Marshal.ReadInt32(t, v1) != 1) return (null, not + " (this host's HOME renderer interface differs)");
                var f = Marshal.ReadIntPtr(t, slot); if (f == IntPtr.Zero) return (null, not + " (this host was built without the HOME renderer)");
                return ((CheckFn)Marshal.GetDelegateForFunctionPointer(f, typeof(CheckFn)), null);
            }
            catch (Exception ex) { return (null, not + " (" + ex.GetType().Name + ")"); }
        }

        public RenderCheckResult Check(string packDir, string designDir, RenderCheckOptions o)
        {
            o = o ?? new RenderCheckOptions(); var a = abi.Value; if (a.fn == null) throw new ComposeException("render.not-hosted", a.why);
            var work = o.WorkDir ?? Path.Combine(Path.GetTempPath(), "ccs-render-check-" + Guid.NewGuid().ToString("N").Substring(0, 8)); Directory.CreateDirectory(work);
            var frames = new List<RenderFrame>(); var handles = new List<IntPtr>(); IntPtr Utf8(string s) { if (s == null) return IntPtr.Zero; var b = Encoding.UTF8.GetBytes(s + "\0"); var h = Marshal.AllocHGlobal(b.Length); Marshal.Copy(b, 0, h, b.Length); handles.Add(h); return h; }
            FrameFn cb = (ctx, th, st, with, rgb, w, h) =>
            {
                try { var px = new byte[w * h * 3]; Marshal.Copy(rgb, px, 0, px.Length); frames.Add(new RenderFrame { Theme = Marshal.PtrToStringAnsi(th), State = Marshal.PtrToStringAnsi(st), WithDesign = with != 0, Width = w, Height = h, Rgb = px }); return o.Cancel.IsCancellationRequested ? 1 : 0; }
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
