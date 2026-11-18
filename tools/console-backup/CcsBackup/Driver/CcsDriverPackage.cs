// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The driver package CCS Console Workbench sets up when this PC has nothing usable for the console (DRIVER-AUDIT.md section 9). It is data
// only: an INF that tells Windows to use its own in-box WinUSB driver (winusb.sys, through Include/Needs winusb.inf: Microsoft's universal
// WinUSB INF) for the console's USB identity 1F3A:EFE8 and registers an interface GUID, and the INF's catalog. No driver program, no
// co-installer, no service of its own and no change to Windows' WinUSB service. The INF's bytes are fixed here (their SHA-256 is a
// constant), so no request can change what is installed, and a package with any other bytes is never treated as Workbench's own.
// Windows stages a driver package only with a signed catalog whose signer it trusts (Microsoft Learn, "WinUSB (Winusb.sys) Installation for
// Developers": "Create a signed catalog file for the package. This file is required to install WinUSB on Windows"; "PnP Device
// Installation Signing Requirements"; "Installing Test Certificates"). Without a Microsoft or commercial signature, the signer must be made
// trusted on this PC, so each setup makes its own:
//   CcsSigner    a new self-signed certificate per operation, its subject naming the operation; RSA 3072 in memory only (an unnamed CNG key:
//                never written to disk, gone when Dispose runs, before anything on the PC changes); CA=false (it can never issue a
//                certificate) and Code Signing only. The elevated step adds only its public part to Root and Trusted Publishers.
//   CcsCatalog   Write: the catalog made by Windows' own catalog functions (wintrust CryptCAT*, the calls libwdi makes), then signed (CMS,
//                SHA-256). Check: read back by Windows' CMS reader and Windows' catalog reader, its one member the INF's hash. VerifyTrust:
//                Windows' Authenticode check of the INF through the catalog (reads only; with the signer not yet trusted it reports exactly that).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace CcsBackup.Driver
{
    public static class CcsDriver
    {
        public const string Method = "ccs-winusb-inf";
        public const string InfName = "ccs_console_usb.inf", CatalogName = "ccs_console_usb.cat";
        public const string Provider = "Rance9 LLC";
        public const string InterfaceGuid = "{FBA1B326-D7E7-4546-9933-75C8B24ED040}";
        public const string Service = "WinUSB";
        public const string SubjectPrefix = "CN=CCS Console Workbench USB driver (1F3A-EFE8) ";
        public const string FriendlyName = "CCS Console Workbench USB driver";
        /// <summary>the catalog's operating-system attributes (informational for a catalog that is not signed by Microsoft)</summary>
        public const string OsAttr = "2:6.1,2:6.2,2:6.3,2:10.0", OsList = "10_X86,10_X64,10_ARM64";

        public static readonly string InfText = string.Join("\r\n", new[]
        {
            "; ccs_console_usb.inf: CCS Console Workbench. Windows' own WinUSB driver for the NES Classic's USB connection (1F3A:EFE8).",
            "; Data only: this package carries no driver program. Copyright (C) 2026 Rance9 LLC. GPL-3.0-or-later.",
            "",
            "[Version]",
            "Signature   = \"$Windows NT$\"",
            "Class       = USBDevice",
            "ClassGUID   = {88BAE032-5A81-49f0-BC3D-A4FF138216D6}",
            "Provider    = %ProviderName%",
            "CatalogFile = " + CatalogName,
            "DriverVer   = 11/15/2026,1.0.0.0",
            "PnpLockdown = 1",
            "",
            "[Manufacturer]",
            "%ProviderName% = CcsConsole,NTx86,NTamd64,NTarm64",
            "",
            "[CcsConsole.NTx86]",
            "%DeviceName% = CcsConsole_Install, USB\\VID_1F3A&PID_EFE8",
            "",
            "[CcsConsole.NTamd64]",
            "%DeviceName% = CcsConsole_Install, USB\\VID_1F3A&PID_EFE8",
            "",
            "[CcsConsole.NTarm64]",
            "%DeviceName% = CcsConsole_Install, USB\\VID_1F3A&PID_EFE8",
            "",
            "[CcsConsole_Install]",
            "Include = winusb.inf",
            "Needs   = WINUSB.NT",
            "",
            "[CcsConsole_Install.Services]",
            "Include = winusb.inf",
            "Needs   = WINUSB.NT.Services",
            "",
            "[CcsConsole_Install.HW]",
            "AddReg = CcsConsole_AddReg",
            "",
            "[CcsConsole_AddReg]",
            "HKR,,DeviceInterfaceGUIDs,0x10000,\"" + InterfaceGuid + "\"",
            "",
            "[Strings]",
            "ProviderName = \"" + Provider + "\"",
            "DeviceName   = \"NES Classic (CCS Console Workbench)\"",
            "",
        });
        public static byte[] InfBytes() => Encoding.ASCII.GetBytes(InfText);
        public static readonly string InfSha256 = Digest(SHA256.Create(), InfBytes());
        /// <summary>the catalog member tag: the INF's SHA-1 in capital hexadecimal (what CryptCATAdminCalcHashFromFileHandle gives for a flat file)</summary>
        public static readonly string InfTag = Digest(SHA1.Create(), InfBytes()).ToUpperInvariant();
        static string Digest(HashAlgorithm h, byte[] b) { using (h) return Hex.Of(h.ComputeHash(b)); }

        public static string SubjectFor(string operationId) => SubjectPrefix + (operationId ?? "");
        /// <summary>a signer certificate's subject made by a Workbench setup (for one operation when operationId is given)</summary>
        public static bool IsCcsSubject(string subject, string operationId = null)
        {
            if (subject == null || !subject.StartsWith(SubjectPrefix, StringComparison.Ordinal)) return false;
            var id = subject.Substring(SubjectPrefix.Length);
            return Hex.IsHex(id, 32) && (operationId == null || id == operationId);
        }
        /// <summary>Workbench's own package: exactly these INF bytes under this name, Windows' WinUSB, this interface GUID, signed by a Workbench certificate</summary>
        public static bool IsCcsPackage(DriverPackage p) => p != null && !p.IsInbox && PinnedDriver.Eq(p.OriginalName, InfName) && PinnedDriver.Eq(p.InfSha256, InfSha256)
            && PinnedDriver.Eq(p.Provider, Provider) && p.Services.Any(s => PinnedDriver.Eq(s, Service)) && p.InterfaceGuids.Any(g => PinnedDriver.Eq(g, InterfaceGuid)) && IsCcsSubject(p.SignerSubject);
    }

    /// <summary>One setup's signing certificate. The key exists in this process's memory only (an unnamed CNG key) and is destroyed by Dispose.</summary>
    public sealed class CcsSigner : IDisposable
    {
        RSA key;
        public X509Certificate2 Certificate { get; private set; }
        public byte[] Der { get; private set; }
        public string Thumbprint { get; private set; }
        public string Subject { get; private set; }
        public static readonly DateTimeOffset NotAfter = new DateTimeOffset(2099, 12, 31, 0, 0, 0, TimeSpan.Zero);

        /// <summary>valid from a day before now (clock differences) until the end of 2099: the package's signature is checked again whenever
        /// Windows sets the driver up for the console on another USB port, and no signing time is stamped (nothing is sent anywhere). The key is
        /// gone before the certificate is trusted, so the long validity lets nothing else be signed with it.</summary>
        public static CcsSigner New(string operationId, DateTime utcNow)
        {
            if (!Hex.IsHex(operationId, 32)) throw new ArgumentException("the operation id is not 32 hexadecimal digits");
            var rsa = new RSACng(3072);
            try
            {
                var req = new CertificateRequest(new X500DistinguishedName(CcsDriver.SubjectFor(operationId)), rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
                req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
                req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.3") }, true));
                req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));
                var cert = req.CreateSelfSigned(new DateTimeOffset(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc).AddDays(-1)), NotAfter);
                return new CcsSigner { key = rsa, Certificate = cert, Der = cert.RawData, Thumbprint = UsbId.NormThumbprint(cert.Thumbprint), Subject = cert.Subject };
            }
            catch { rsa.Dispose(); throw; }
        }

        public void Dispose() { Certificate?.Dispose(); Certificate = null; key?.Dispose(); key = null; }
    }

    public sealed class CatalogCheck
    {
        public List<string> Problems = new List<string>();
        public bool Ok => Problems.Count == 0;
        public string SignerThumbprint, SignerSubject, MemberTag, DigestAlgorithm;
        public int Members, Signers;
        public Dictionary<string, string> CatalogAttributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), MemberAttributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public static class CcsCatalog
    {
        /// <summary>writes ccs_console_usb.inf and its signed ccs_console_usb.cat into folder (both must be new) and returns their paths</summary>
        public static (string inf, string cat) Write(string folder, CcsSigner signer)
        {
            if (signer?.Certificate == null) throw new ArgumentException("no signing certificate");
            var inf = Path.Combine(folder, CcsDriver.InfName); var cat = Path.Combine(folder, CcsDriver.CatalogName); var draft = Path.Combine(folder, "ccs_console_usb.draft.cat");
            using (var f = new FileStream(inf, FileMode.CreateNew, FileAccess.Write)) { var b = CcsDriver.InfBytes(); f.Write(b, 0, b.Length); }
            byte[] sha1; using (var h = SHA1.Create()) sha1 = h.ComputeHash(File.ReadAllBytes(inf));
            Draft(draft, sha1);
            var unsigned = new SignedCms(); unsigned.Decode(File.ReadAllBytes(draft));
            if (unsigned.ContentInfo.ContentType.Value != CtlOid) throw new InvalidDataException("Windows' catalog functions did not make a catalog");
            var cms = new SignedCms(new ContentInfo(unsigned.ContentInfo.ContentType, unsigned.ContentInfo.Content), false);
            var s = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, signer.Certificate) { DigestAlgorithm = new Oid(Sha256Oid), IncludeOption = X509IncludeOption.EndCertOnly };
            s.SignedAttributes.Add(new AsnEncodedData(new Oid(SpcSpOpusInfo), new byte[] { 0x30, 0x00 }));
            s.SignedAttributes.Add(new AsnEncodedData(new Oid(SpcStatementType), IndividualCodeSigning));
            cms.ComputeSignature(s, true);
            using (var f = new FileStream(cat, FileMode.CreateNew, FileAccess.Write)) { var b = cms.Encode(); f.Write(b, 0, b.Length); }
            File.Delete(draft);
            return (inf, cat);
        }

        /// <summary>the unsigned catalog, by Windows: HWID1 and OS attributes, one member (the INF by its SHA-1, flat-file subject type) with
        /// File and OSAttr attributes, sorted and saved (libwdi pki.c CreateCat/AddFileHash, the same calls)</summary>
        static void Draft(string path, byte[] sha1)
        {
            var mem = new List<IntPtr>();
            IntPtr H(byte[] b) { var p = Marshal.AllocHGlobal(b.Length); Marshal.Copy(b, 0, p, b.Length); mem.Add(p); return p; }
            IntPtr A(string s) { var p = Marshal.StringToHGlobalAnsi(s); mem.Add(p); return p; }
            var cat = CryptCATOpen(path, CRYPTCAT_OPEN_CREATENEW, IntPtr.Zero, 0, 0);
            if (cat == IntPtr.Zero || cat == new IntPtr(-1)) throw new IOException("Windows could not create the catalog (error " + Marshal.GetLastWin32Error() + ")");
            try
            {
                const uint attr = CRYPTCAT_ATTR_AUTHENTICATED | CRYPTCAT_ATTR_NAMEASCII | CRYPTCAT_ATTR_DATAASCII;
                var hw = W(PinnedDriver.HardwareId.ToLowerInvariant()); var os = W(CcsDriver.OsList);
                if (CryptCATPutCatAttrInfo(cat, "HWID1", attr, (uint)hw.Length, hw) == IntPtr.Zero || CryptCATPutCatAttrInfo(cat, "OS", attr, (uint)os.Length, os) == IntPtr.Zero)
                    throw new IOException("Windows could not write the catalog's attributes (error " + Marshal.GetLastWin32Error() + ")");
                var ind = new SIP_INDIRECT_DATA { DataOid = A(SpcCabDataOid), DataValue = new BLOB { cb = ObsoleteLink.Length, pb = H(ObsoleteLink) }, DigestOid = A(Sha1Oid), Digest = new BLOB { cb = sha1.Length, pb = H(sha1) } };
                var pind = Marshal.AllocHGlobal(Marshal.SizeOf(ind)); mem.Add(pind); Marshal.StructureToPtr(ind, pind, false);
                var flat = FlatFileSip;
                var m = CryptCATPutMemberInfo(cat, null, string.Concat(sha1.Select(x => x.ToString("X2"))), ref flat, 0x200, (uint)Marshal.SizeOf(ind), pind);
                if (m == IntPtr.Zero) throw new IOException("Windows could not add the INF to the catalog (error " + Marshal.GetLastWin32Error() + ")");
                var fn = W(CcsDriver.InfName.ToLowerInvariant()); var oa = W(CcsDriver.OsAttr);
                if (CryptCATPutAttrInfo(cat, m, "File", attr, (uint)fn.Length, fn) == IntPtr.Zero || CryptCATPutAttrInfo(cat, m, "OSAttr", attr, (uint)oa.Length, oa) == IntPtr.Zero)
                    throw new IOException("Windows could not write the INF's catalog attributes (error " + Marshal.GetLastWin32Error() + ")");
                if (!CryptCATPersistStore(cat)) throw new IOException("Windows could not save the catalog (error " + Marshal.GetLastWin32Error() + ")");
            }
            finally { CryptCATClose(cat); foreach (var p in mem) Marshal.FreeHGlobal(p); }
        }

        /// <summary>reads a catalog back (never changes it): Windows' CMS reader checks the signature, Windows' catalog reader lists the
        /// members and attributes; the one member must be the INF's SHA-1, flat-file type, with the INF's name</summary>
        public static CatalogCheck Check(string infPath, string catPath)
        {
            var r = new CatalogCheck();
            byte[] infBytes, catBytes;
            try { infBytes = File.ReadAllBytes(infPath); catBytes = File.ReadAllBytes(catPath); }
            catch (Exception ex) { r.Problems.Add("the package files cannot be read: " + ex.Message); return r; }
            string tag; using (var h = SHA1.Create()) tag = string.Concat(h.ComputeHash(infBytes).Select(x => x.ToString("X2")));
            try
            {
                var cms = new SignedCms(); cms.Decode(catBytes);
                if (cms.ContentInfo.ContentType.Value != CtlOid) r.Problems.Add("the catalog's content is not a certificate trust list (" + cms.ContentInfo.ContentType.Value + ")");
                r.Signers = cms.SignerInfos.Count;
                if (r.Signers != 1) r.Problems.Add("the catalog has " + r.Signers + " signers, not one");
                else
                {
                    var si = cms.SignerInfos[0];
                    r.DigestAlgorithm = si.DigestAlgorithm.Value;
                    if (si.DigestAlgorithm.Value != Sha256Oid) r.Problems.Add("the catalog is not signed with SHA-256");
                    try { cms.CheckSignature(true); } catch (CryptographicException ex) { r.Problems.Add("the catalog's signature does not verify: " + ex.Message); }
                    r.SignerThumbprint = si.Certificate == null ? null : UsbId.NormThumbprint(si.Certificate.Thumbprint);
                    r.SignerSubject = si.Certificate?.Subject;
                    if (si.Certificate == null) r.Problems.Add("the catalog does not carry its signer's certificate");
                }
            }
            catch (CryptographicException ex) { r.Problems.Add("the catalog is not a signed message: " + ex.Message); return r; }

            var cat = CryptCATOpen(catPath, CRYPTCAT_OPEN_EXISTING, IntPtr.Zero, 0, 0);
            if (cat == IntPtr.Zero || cat == new IntPtr(-1)) { r.Problems.Add("Windows' catalog reader cannot open it (error " + Marshal.GetLastWin32Error() + ")"); return r; }
            try
            {
                for (var pa = CryptCATEnumerateCatAttr(cat, IntPtr.Zero); pa != IntPtr.Zero; pa = CryptCATEnumerateCatAttr(cat, pa)) { var a = Marshal.PtrToStructure<CRYPTCATATTRIBUTE>(pa); r.CatalogAttributes[Marshal.PtrToStringUni(a.Tag) ?? ""] = Value(a); }
                for (var pm = CryptCATEnumerateMember(cat, IntPtr.Zero); pm != IntPtr.Zero; pm = CryptCATEnumerateMember(cat, pm))
                {
                    r.Members++;
                    var m = Marshal.PtrToStructure<CRYPTCATMEMBER>(pm);
                    r.MemberTag = Marshal.PtrToStringUni(m.Tag);
                    if (m.SubjectType != FlatFileSip) r.Problems.Add("the INF's catalog entry is not of the flat-file type");
                    if (m.IndirectData != IntPtr.Zero)
                    {
                        var ind = Marshal.PtrToStructure<SIP_INDIRECT_DATA>(m.IndirectData);
                        var d = new byte[ind.Digest.cb]; if (ind.Digest.cb > 0 && ind.Digest.cb <= 64) Marshal.Copy(ind.Digest.pb, d, 0, d.Length);
                        if (string.Concat(d.Select(x => x.ToString("X2"))) != tag) r.Problems.Add("the catalog entry's digest is not the INF's SHA-1");
                        if (Marshal.PtrToStringAnsi(ind.DigestOid) != Sha1Oid) r.Problems.Add("the catalog entry's digest is not SHA-1");
                    }
                    else r.Problems.Add("the catalog entry has no digest");
                    for (var pa = CryptCATEnumerateAttr(cat, pm, IntPtr.Zero); pa != IntPtr.Zero; pa = CryptCATEnumerateAttr(cat, pm, pa)) { var a = Marshal.PtrToStructure<CRYPTCATATTRIBUTE>(pa); r.MemberAttributes[Marshal.PtrToStringUni(a.Tag) ?? ""] = Value(a); }
                }
            }
            finally { CryptCATClose(cat); }
            if (r.Members != 1) r.Problems.Add("the catalog has " + r.Members + " entries, not one");
            if (r.MemberTag != tag) r.Problems.Add("the catalog does not list the INF's hash (" + tag + ")");
            if (!r.MemberAttributes.TryGetValue("File", out var file) || !PinnedDriver.Eq(file, Path.GetFileName(infPath))) r.Problems.Add("the catalog entry does not name the INF");
            return r;
        }

        /// <summary>Windows' Authenticode check of the INF through the catalog (WinVerifyTrust, generic verify, no user interface, no
        /// revocation and no network). Reads only. 0 when Windows trusts it; 0x800B0109 (CERT_E_UNTRUSTEDROOT) when only the signer's trust is
        /// missing; 0x80096010 (TRUST_E_BAD_DIGEST) when the INF is not the one catalogued.</summary>
        public static int VerifyTrust(string infPath, string catPath)
        {
            string tag; using (var h = SHA1.Create()) tag = string.Concat(h.ComputeHash(File.ReadAllBytes(infPath)).Select(x => x.ToString("X2")));
            var ci = new WINTRUST_CATALOG_INFO { cbStruct = Marshal.SizeOf<WINTRUST_CATALOG_INFO>(), CatalogFile = catPath, MemberTag = tag, MemberFile = infPath };
            var pci = Marshal.AllocHGlobal(Marshal.SizeOf(ci)); var pwd = IntPtr.Zero;
            try
            {
                Marshal.StructureToPtr(ci, pci, false);
                var wd = new WINTRUST_DATA { cbStruct = Marshal.SizeOf<WINTRUST_DATA>(), UIChoice = WTD_UI_NONE, RevocationChecks = WTD_REVOKE_NONE, UnionChoice = WTD_CHOICE_CATALOG, Info = pci,
                    StateAction = WTD_STATEACTION_VERIFY, ProvFlags = WTD_REVOCATION_CHECK_NONE | WTD_CACHE_ONLY_URL_RETRIEVAL };
                pwd = Marshal.AllocHGlobal(Marshal.SizeOf(wd)); Marshal.StructureToPtr(wd, pwd, false);
                var act = GenericVerifyV2;
                int r = WinVerifyTrust(new IntPtr(-1), ref act, pwd);
                wd = Marshal.PtrToStructure<WINTRUST_DATA>(pwd); wd.StateAction = WTD_STATEACTION_CLOSE; Marshal.StructureToPtr(wd, pwd, true);
                WinVerifyTrust(new IntPtr(-1), ref act, pwd);
                return r;
            }
            finally { Marshal.DestroyStructure<WINTRUST_CATALOG_INFO>(pci); Marshal.FreeHGlobal(pci); if (pwd != IntPtr.Zero) Marshal.FreeHGlobal(pwd); }
        }

        static string Value(CRYPTCATATTRIBUTE a) => a.pbValue == IntPtr.Zero || a.cbValue <= 0 ? "" : Marshal.PtrToStringUni(a.pbValue, a.cbValue / 2).TrimEnd('\0');
        static byte[] W(string s) => Encoding.Unicode.GetBytes(s + "\0");

        // ---- declarations
        internal const string CtlOid = "1.3.6.1.4.1.311.10.1", Sha256Oid = "2.16.840.1.101.3.4.2.1", Sha1Oid = "1.3.14.3.2.26", SpcCabDataOid = "1.3.6.1.4.1.311.2.1.25",
            SpcSpOpusInfo = "1.3.6.1.4.1.311.2.1.12", SpcStatementType = "1.3.6.1.4.1.311.2.1.11";
        /// <summary>SpcStatementType: individual code signing (1.3.6.1.4.1.311.2.1.21), as SignerSignEx writes it</summary>
        static readonly byte[] IndividualCodeSigning = { 0x30, 0x0C, 0x06, 0x0A, 0x2B, 0x06, 0x01, 0x04, 0x01, 0x82, 0x37, 0x02, 0x01, 0x15 };
        /// <summary>SpcLink file "&lt;&lt;&lt;Obsolete&gt;&gt;&gt;" (BMPString), the link every catalog entry carries; the same bytes CryptEncodeObject makes for
        /// SPC_CAB_DATA_OBJID (the package test compares them)</summary>
        internal static readonly byte[] ObsoleteLink = { 0xA2, 0x1E, 0x80, 0x1C, 0, 0x3C, 0, 0x3C, 0, 0x3C, 0, 0x4F, 0, 0x62, 0, 0x73, 0, 0x6F, 0, 0x6C, 0, 0x65, 0, 0x74, 0, 0x65, 0, 0x3E, 0, 0x3E, 0, 0x3E };
        /// <summary>the flat-file subject interface package (an INF is hashed as a whole file)</summary>
        internal static readonly Guid FlatFileSip = new Guid("DE351A42-8E59-11D0-8C47-00C04FC295EE");
        static readonly Guid GenericVerifyV2 = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        const uint CRYPTCAT_OPEN_CREATENEW = 1, CRYPTCAT_OPEN_EXISTING = 4, CRYPTCAT_ATTR_AUTHENTICATED = 0x10000000, CRYPTCAT_ATTR_NAMEASCII = 1, CRYPTCAT_ATTR_DATAASCII = 0x10000;
        const uint WTD_UI_NONE = 2, WTD_REVOKE_NONE = 0, WTD_CHOICE_CATALOG = 2, WTD_STATEACTION_VERIFY = 1, WTD_STATEACTION_CLOSE = 2, WTD_REVOCATION_CHECK_NONE = 0x10, WTD_CACHE_ONLY_URL_RETRIEVAL = 0x1000;

        [StructLayout(LayoutKind.Sequential)] struct BLOB { public int cb; public IntPtr pb; }
        [StructLayout(LayoutKind.Sequential)] struct SIP_INDIRECT_DATA { public IntPtr DataOid; public BLOB DataValue; public IntPtr DigestOid; public BLOB DigestParameters; public BLOB Digest; }
        [StructLayout(LayoutKind.Sequential)] struct CRYPTCATMEMBER { public int cbStruct; public IntPtr Tag; public IntPtr FileName; public Guid SubjectType; public uint MemberFlags; public IntPtr IndirectData; public uint CertVersion; }
        [StructLayout(LayoutKind.Sequential)] struct CRYPTCATATTRIBUTE { public int cbStruct; public IntPtr Tag; public uint AttrTypeAndAction; public int cbValue; public IntPtr pbValue; public uint Reserved; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct WINTRUST_CATALOG_INFO { public int cbStruct; public uint CatalogVersion; public string CatalogFile; public string MemberTag; public string MemberFile; public IntPtr MemberHandle; public IntPtr CalculatedHash; public int cbCalculatedHash; public IntPtr CatalogContext; public IntPtr CatAdmin; }
        [StructLayout(LayoutKind.Sequential)] struct WINTRUST_DATA { public int cbStruct; public IntPtr PolicyCallbackData; public IntPtr SIPClientData; public uint UIChoice; public uint RevocationChecks; public uint UnionChoice; public IntPtr Info; public uint StateAction; public IntPtr StateData; public IntPtr URLReference; public uint ProvFlags; public uint UIContext; public IntPtr SignatureSettings; }

        [DllImport("wintrust.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CryptCATOpen(string file, uint flags, IntPtr prov, uint version, uint encoding);
        [DllImport("wintrust.dll", SetLastError = true)] static extern bool CryptCATClose(IntPtr cat);
        [DllImport("wintrust.dll", SetLastError = true)] static extern bool CryptCATPersistStore(IntPtr cat);
        [DllImport("wintrust.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CryptCATPutCatAttrInfo(IntPtr cat, string tag, uint flags, uint cb, byte[] data);
        [DllImport("wintrust.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CryptCATPutMemberInfo(IntPtr cat, string file, string tag, ref Guid subject, uint certVersion, uint cb, IntPtr data);
        [DllImport("wintrust.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CryptCATPutAttrInfo(IntPtr cat, IntPtr member, string tag, uint flags, uint cb, byte[] data);
        [DllImport("wintrust.dll", SetLastError = true)] static extern IntPtr CryptCATEnumerateMember(IntPtr cat, IntPtr previous);
        [DllImport("wintrust.dll", SetLastError = true)] static extern IntPtr CryptCATEnumerateCatAttr(IntPtr cat, IntPtr previous);
        [DllImport("wintrust.dll", SetLastError = true)] static extern IntPtr CryptCATEnumerateAttr(IntPtr cat, IntPtr member, IntPtr previous);
        [DllImport("wintrust.dll")] static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, IntPtr data);
    }
}
