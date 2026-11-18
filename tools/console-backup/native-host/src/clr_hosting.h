/* CCS Console Workbench native host: the .NET Framework hosting interfaces it calls, declared here because the mingw-w64 headers that
 * ship with zig have no metahost.h and no mscorlib type-library interfaces. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
 *
 * Every interface is called through its vtable slot number. Sources of each number:
 *   ICLRMetaHost, ICLRRuntimeInfo   Microsoft's metahost.h order (cross-checked against Wine include/metahost.idl)
 *   ICorRuntimeHost                 mscoree.h (the copy in zig's mingw-w64 headers, ICorRuntimeHostVtbl)
 *   _AppDomain, _Assembly, _MethodInfo   this PC's mscorlib.tlb, read by tools/dump_vtables.ps1 (FUNCDESC.oVft / pointer size); the
 *                                    test compares these constants with that dump on every run
 * The GUIDs are the documented ones (CLSID_CLRMetaHost, IID_ICLRMetaHost, IID_ICLRRuntimeInfo in metahost.h; CLSID_CorRuntimeHost and
 * IID_ICorRuntimeHost in mscoree.h; the three mscorlib interface IIDs in mscorlib.tlb, printed by the same dump). */
#ifndef CCS_CLR_HOSTING_H
#define CCS_CLR_HOSTING_H

#include <windows.h>
#include <oleauto.h>

/* {9280188D-0E8E-4867-B30C-7FA83884E8DE} */
static const GUID CCS_CLSID_CLRMetaHost = { 0x9280188d, 0x0e8e, 0x4867, { 0xb3, 0x0c, 0x7f, 0xa8, 0x38, 0x84, 0xe8, 0xde } };
/* {D332DB9E-B9B3-4125-8207-A14884F53216} */
static const GUID CCS_IID_ICLRMetaHost = { 0xd332db9e, 0xb9b3, 0x4125, { 0x82, 0x07, 0xa1, 0x48, 0x84, 0xf5, 0x32, 0x16 } };
/* {BD39D1D2-BA2F-486A-89B0-B4B0CB466891} */
static const GUID CCS_IID_ICLRRuntimeInfo = { 0xbd39d1d2, 0xba2f, 0x486a, { 0x89, 0xb0, 0xb4, 0xb0, 0xcb, 0x46, 0x68, 0x91 } };
/* {CB2F6723-AB3A-11D2-9C40-00C04FA30A3E} */
static const GUID CCS_CLSID_CorRuntimeHost = { 0xcb2f6723, 0xab3a, 0x11d2, { 0x9c, 0x40, 0x00, 0xc0, 0x4f, 0xa3, 0x0a, 0x3e } };
/* {CB2F6722-AB3A-11D2-9C40-00C04FA30A3E} */
static const GUID CCS_IID_ICorRuntimeHost = { 0xcb2f6722, 0xab3a, 0x11d2, { 0x9c, 0x40, 0x00, 0xc0, 0x4f, 0xa3, 0x0a, 0x3e } };
/* {05F696DC-2B29-3663-AD8B-C4389CF2A713} */
static const GUID CCS_IID__AppDomain = { 0x05f696dc, 0x2b29, 0x3663, { 0xad, 0x8b, 0xc4, 0x38, 0x9c, 0xf2, 0xa7, 0x13 } };
/* {17156360-2F1A-384A-BC52-FDE93C215C5B} */
static const GUID CCS_IID__Assembly = { 0x17156360, 0x2f1a, 0x384a, { 0xbc, 0x52, 0xfd, 0xe9, 0x3c, 0x21, 0x5c, 0x5b } };
/* {FFCC1B5D-ECB8-38DD-9B01-3DC8ABC2AA5F} */
static const GUID CCS_IID__MethodInfo = { 0xffcc1b5d, 0xecb8, 0x38dd, { 0x9b, 0x01, 0x3d, 0xc8, 0xab, 0xc2, 0xaa, 0x5f } };

/* vtable slots (0 = QueryInterface, 1 = AddRef, 2 = Release) */
#define SLOT_ICLRMetaHost_GetRuntime                 3
#define SLOT_ICLRRuntimeInfo_GetVersionString        3
#define SLOT_ICLRRuntimeInfo_GetRuntimeDirectory     4
#define SLOT_ICLRRuntimeInfo_GetInterface            9
#define SLOT_ICLRRuntimeInfo_IsLoadable             10
#define SLOT_ICorRuntimeHost_Start                  10
#define SLOT_ICorRuntimeHost_GetDefaultDomain       13
#define SLOT__AppDomain_Load_3                      45   /* Assembly Load(byte[] rawAssembly) */
#define SLOT__AppDomain_SetData                     63   /* void SetData(string name, object data) */
#define SLOT__Assembly_get_EntryPoint               16   /* MethodInfo EntryPoint { get; } */
#define SLOT__MethodInfo_Invoke_3                   37   /* object Invoke(object obj, object[] parameters) */

typedef HRESULT (STDMETHODCALLTYPE *PFN_CLRCreateInstance)(REFCLSID clsid, REFIID riid, LPVOID *ppInterface);

typedef HRESULT (STDMETHODCALLTYPE *PFN_MetaHost_GetRuntime)(void *self, LPCWSTR version, REFIID riid, LPVOID *ppRuntime);
typedef HRESULT (STDMETHODCALLTYPE *PFN_RuntimeInfo_GetString)(void *self, LPWSTR buffer, DWORD *pcch);
typedef HRESULT (STDMETHODCALLTYPE *PFN_RuntimeInfo_GetInterface)(void *self, REFCLSID clsid, REFIID riid, LPVOID *ppUnk);
typedef HRESULT (STDMETHODCALLTYPE *PFN_RuntimeInfo_IsLoadable)(void *self, BOOL *pbLoadable);
typedef HRESULT (STDMETHODCALLTYPE *PFN_CorHost_Start)(void *self);
typedef HRESULT (STDMETHODCALLTYPE *PFN_CorHost_GetDefaultDomain)(void *self, IUnknown **ppAppDomain);
typedef HRESULT (STDMETHODCALLTYPE *PFN_AppDomain_Load_3)(void *self, SAFEARRAY *rawAssembly, void **ppAssembly);
typedef HRESULT (STDMETHODCALLTYPE *PFN_AppDomain_SetData)(void *self, BSTR name, VARIANT data);
typedef HRESULT (STDMETHODCALLTYPE *PFN_Assembly_get_EntryPoint)(void *self, void **ppMethodInfo);
typedef HRESULT (STDMETHODCALLTYPE *PFN_MethodInfo_Invoke_3)(void *self, VARIANT obj, SAFEARRAY *parameters, VARIANT *pRetVal);

/* the function in slot n of a COM object's vtable */
#define CCS_VFN(obj, n) ((*(void ***)(obj))[(n)])

static inline ULONG ccs_release(void *obj)
{
    if (!obj) return 0;
    typedef ULONG (STDMETHODCALLTYPE *PFN_Release)(void *);
    return ((PFN_Release)CCS_VFN(obj, 2))(obj);
}

static inline HRESULT ccs_query(void *obj, REFIID iid, void **out)
{
    typedef HRESULT (STDMETHODCALLTYPE *PFN_QI)(void *, REFIID, void **);
    return ((PFN_QI)CCS_VFN(obj, 0))(obj, iid, out);
}

#endif
