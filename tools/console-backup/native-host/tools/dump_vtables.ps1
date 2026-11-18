# CCS Console Workbench native host: read the vtable slot of every method the host calls on mscorlib's COM-visible interfaces
# (_AppDomain, _Assembly, _MethodInfo) straight from this PC's .NET Framework type library, so the hand-written vtables in
# clrhost.h can be checked against Microsoft's own description instead of memory. Reads only. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
#   pwsh -NoProfile -File dump_vtables.ps1 [-TypeLib C:\Windows\Microsoft.NET\Framework64\v4.0.30319\mscorlib.tlb]
param([string]$TypeLib = "$env:windir\Microsoft.NET\Framework64\v4.0.30319\mscorlib.tlb")
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
public static class CcsTlbDump
{
    [DllImport("oleaut32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void LoadTypeLibEx(string file, int regkind, out ITypeLib lib);
    public static List<string> Dump(string file, string[] names)
    {
        var o = new List<string>();
        ITypeLib lib; LoadTypeLibEx(file, 2 /* REGKIND_NONE */, out lib);
        int n = lib.GetTypeInfoCount();
        for (int i = 0; i < n; i++)
        {
            string name, doc, help; int ctx;
            lib.GetDocumentation(i, out name, out doc, out ctx, out help);
            if (Array.IndexOf(names, name) < 0) continue;
            ITypeInfo ti; lib.GetTypeInfo(i, out ti);
            IntPtr pa; ti.GetTypeAttr(out pa);
            var ta = (TYPEATTR)Marshal.PtrToStructure(pa, typeof(TYPEATTR));
            string dual = "";
            if (ta.typekind == TYPEKIND.TKIND_DISPATCH && (ta.wTypeFlags & TYPEFLAGS.TYPEFLAG_FDUAL) != 0)
            {
                // a dual interface: its vtable view is the type the dispinterface refers to as implemented type -1
                int href; ti.GetRefTypeOfImplType(-1, out href);
                ITypeInfo vt; ti.GetRefTypeInfo(href, out vt);
                ti.ReleaseTypeAttr(pa); ti = vt; ti.GetTypeAttr(out pa);
                ta = (TYPEATTR)Marshal.PtrToStructure(pa, typeof(TYPEATTR));
                dual = " (vtable view of a dual interface)";
            }
            o.Add(name + " {" + ta.guid.ToString().ToUpperInvariant() + "} kind=" + ta.typekind + dual + " funcs=" + ta.cFuncs + " vtbl_bytes=" + ta.cbSizeVft);
            for (int f = 0; f < ta.cFuncs; f++)
            {
                IntPtr pf; ti.GetFuncDesc(f, out pf);
                var fd = (FUNCDESC)Marshal.PtrToStructure(pf, typeof(FUNCDESC));
                string fn, fdoc, fhelp; int fctx;
                ti.GetDocumentation(fd.memid, out fn, out fdoc, out fctx, out fhelp);
                string suffix = fd.invkind == INVOKEKIND.INVOKE_PROPERTYGET ? " (get)" : fd.invkind == INVOKEKIND.INVOKE_PROPERTYPUT ? " (put)" : "";
                o.Add("  slot " + (fd.oVft / IntPtr.Size) + "  " + fn + suffix + "  params=" + fd.cParams);
                ti.ReleaseFuncDesc(pf);
            }
            ti.ReleaseTypeAttr(pa);
        }
        return o;
    }
}
'@
"type library: $TypeLib"
[CcsTlbDump]::Dump($TypeLib, @('_AppDomain', '_Assembly', '_MethodInfo')) | ForEach-Object { $_ }
