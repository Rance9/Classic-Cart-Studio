// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Whether this process already has administrator permission: the driver texts depend on it (an already elevated person is never told that
// Windows will ask again). Reading the process token only. RunasLauncher is the one place that asks Windows for elevation for a driver
// change, behind the DriverChanges boundary (open in this version); a whole-drive read or write asks through RunasRawDiskLauncher
// (External/RawDiskClient.cs), behind StorageRawReads / StorageDiskWrites.
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace CcsBackup.Driver
{
    public enum ElevationState
    {
        Unknown,
        Elevated,       // running with administrator permission now
        CanElevate,     // an administrator account with a limited token: Windows asks to confirm (UAC)
        StandardUser,   // not an administrator: Windows asks for an administrator's name and password
    }

    public static class Elevation
    {
        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool GetTokenInformation(IntPtr token, int infoClass, out int info, int length, out int returned);
        const int TokenElevationType = 18, TypeDefault = 1, TypeFull = 2, TypeLimited = 3;

        public static ElevationState Current()
        {
            try
            {
                using (var id = WindowsIdentity.GetCurrent())
                {
                    if (new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator)) return ElevationState.Elevated;
                    if (!GetTokenInformation(id.Token, TokenElevationType, out var t, 4, out _)) return ElevationState.Unknown;
                    return t == TypeLimited ? ElevationState.CanElevate : t == TypeFull ? ElevationState.Elevated : ElevationState.StandardUser;
                }
            }
            catch { return ElevationState.Unknown; }
        }
    }

    /// <summary>The real elevated run: this same program started by Windows with "runas" (Windows asks for administrator permission; an already
    /// elevated program is not asked again), hidden, in System32 as its working folder, waited for, its exit code returned. Refusing the prompt
    /// is ElevationCanceledException. It can be made only through Open, right after the DriverChanges boundary check (Gate.cs).</summary>
    public sealed class RunasLauncher : IElevatedRunner
    {
        readonly string exe;
        RunasLauncher(string exe) { this.exe = exe; }

        internal static IElevatedRunner Open(string exe)
        {
            if (!Gate.DriverChanges) throw new GateClosedException(Gate.DriverText, "the administrator step");
            return new RunasLauncher(exe);
        }

        public int Run(string arguments)
        {
            var psi = new ProcessStartInfo(exe, arguments) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = Environment.SystemDirectory };
            Process p;
            try { p = Process.Start(psi); }
            catch (Win32Exception ex) when (ex.NativeErrorCode == DriverExit.Canceled) { throw new ElevationCanceledException(); }
            if (p == null) throw new Win32Exception(DriverExit.Canceled);
            using (p) { p.WaitForExit(); return p.ExitCode; }
        }
    }
}
