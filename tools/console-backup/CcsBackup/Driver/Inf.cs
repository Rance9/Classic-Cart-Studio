// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Reading a driver package's INF file (untrusted text, bounded): its [Version] fields, the USB identities it names, the DeviceInterfaceGUIDs
// it writes and the services it adds (AddService, or Windows' WinUSB through Needs = WINUSB.NT.Services), with %string% tokens expanded from [Strings] (libwdi writes USB\%DeviceID% and DeviceID = "VID_..").
// UTF-16 (with or without a byte order mark), UTF-8 and ANSI files are read. Nothing is executed or interpreted beyond these fields.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CcsBackup.Driver
{
    public sealed class InfInfo
    {
        public string Provider = "", Class = "", ClassGuid = "", CatalogFile = "", DriverVer = "";
        public List<string> UsbIds = new List<string>(), InterfaceGuids = new List<string>(), Services = new List<string>();
    }

    public static class Inf
    {
        public const int MaxBytes = 4 * 1000 * 1000, MaxLines = 200000;
        static readonly Regex usbId = new Regex(@"USB\\VID_[0-9A-F]{4}&PID_[0-9A-F]{4}(?:&[A-Z0-9_]+)*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex ifGuid = new Regex(@"DeviceInterfaceGUIDs?\s*,\s*[^,]*,\s*""?\{?([0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex addService = new Regex(@"^\s*AddService\s*=\s*""?([^,""\s]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        /// <summary>Microsoft's universal WinUSB INF adds Windows' own service through Include = winusb.inf with Needs = WINUSB.NT.Services</summary>
        static readonly Regex needsWinUsb = new Regex(@"^\s*Needs\s*=.*\bWINUSB\.NT\.Services\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex token = new Regex(@"%([^%]*)%", RegexOptions.CultureInvariant);

        public static string Decode(byte[] b)
        {
            if (b == null) return "";
            if (b.Length > MaxBytes) throw new FormatException("the INF file is larger than " + MaxBytes + " bytes");
            if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) return Encoding.Unicode.GetString(b, 2, b.Length - 2);
            if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2);
            if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) return Encoding.UTF8.GetString(b, 3, b.Length - 3);
            if (b.Length >= 4 && b[1] == 0 && b[3] == 0 && b[0] != 0) return Encoding.Unicode.GetString(b);
            return Encoding.Default.GetString(b);
        }

        /// <summary>arch: the platform decoration preferred for CatalogFile (NTamd64, NTx86, NTarm64)</summary>
        public static InfInfo Parse(string text, string arch = null)
        {
            arch = arch ?? (Environment.Is64BitOperatingSystem ? "NTamd64" : "NTx86");
            var sections = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase); List<string> cur = null;
            var raw = (text ?? "").Replace("\r\n", "\n").Split('\n');
            if (raw.Length > MaxLines) throw new FormatException("the INF file has too many lines");
            var pending = "";
            foreach (var r0 in raw)
            {
                var line = pending + StripComment(r0); pending = "";
                var t = line.TrimEnd();
                if (t.EndsWith("\\")) { pending = t.Substring(0, t.Length - 1); continue; }   // line continuation
                t = t.Trim();
                if (t.Length == 0) continue;
                if (t.StartsWith("[") && t.IndexOf(']') > 0) { var name = t.Substring(1, t.IndexOf(']') - 1).Trim(); if (!sections.TryGetValue(name, out cur)) sections[name] = cur = new List<string>(); continue; }
                cur?.Add(t);
            }
            var strings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in sections.Where(s => s.Key.Equals("Strings", StringComparison.OrdinalIgnoreCase)).Concat(sections.Where(s => s.Key.StartsWith("Strings.", StringComparison.OrdinalIgnoreCase))))
                foreach (var l in kv.Value) { var (k, v) = KeyValue(l); if (k != null && !strings.ContainsKey(k)) strings[k] = Unquote(v); }
            string Expand(string s) => token.Replace(s, m => m.Groups[1].Value.Length == 0 ? "%" : strings.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);

            var info = new InfInfo();
            if (sections.TryGetValue("Version", out var ver))
            {
                var vals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var l in ver) { var (k, v) = KeyValue(l); if (k != null) vals[k] = Unquote(Expand(v)); }
                vals.TryGetValue("Provider", out info.Provider); vals.TryGetValue("Class", out info.Class); vals.TryGetValue("ClassGuid", out info.ClassGuid); vals.TryGetValue("DriverVer", out info.DriverVer);
                if (!vals.TryGetValue("CatalogFile." + arch, out info.CatalogFile) && !vals.TryGetValue("CatalogFile.NT", out info.CatalogFile)) vals.TryGetValue("CatalogFile", out info.CatalogFile);
                info.Provider = info.Provider ?? ""; info.Class = info.Class ?? ""; info.ClassGuid = (info.ClassGuid ?? "").ToUpperInvariant(); info.DriverVer = info.DriverVer ?? ""; info.CatalogFile = info.CatalogFile ?? "";
            }
            foreach (var kv in sections)
            {
                if (kv.Key.StartsWith("Strings", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var l0 in kv.Value)
                {
                    var l = Expand(l0);
                    foreach (Match m in usbId.Matches(l)) { var id = m.Value.ToUpperInvariant(); if (!info.UsbIds.Contains(id)) info.UsbIds.Add(id); }
                    foreach (Match m in ifGuid.Matches(l)) { var g = "{" + m.Groups[1].Value.ToUpperInvariant() + "}"; if (!info.InterfaceGuids.Contains(g)) info.InterfaceGuids.Add(g); }
                    var a = addService.Match(l); if (a.Success && !info.Services.Any(x => x.Equals(a.Groups[1].Value, StringComparison.OrdinalIgnoreCase))) info.Services.Add(a.Groups[1].Value);
                    if (needsWinUsb.IsMatch(l) && !info.Services.Any(x => x.Equals("WinUSB", StringComparison.OrdinalIgnoreCase))) info.Services.Add("WinUSB");
                }
            }
            return info;
        }

        static string StripComment(string l)
        {
            bool q = false;
            for (int i = 0; i < l.Length; i++) { if (l[i] == '"') q = !q; else if (l[i] == ';' && !q) return l.Substring(0, i); }
            return l;
        }
        static (string, string) KeyValue(string l) { var i = l.IndexOf('='); return i <= 0 ? (null, null) : (l.Substring(0, i).Trim(), l.Substring(i + 1).Trim()); }
        static string Unquote(string v) { v = (v ?? "").Trim(); if (v.Length >= 2 && v[0] == '"' && v[v.Length - 1] == '"') v = v.Substring(1, v.Length - 2); return v.Replace("\"\"", "\""); }
    }
}
