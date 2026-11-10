// Classic Cart Studio console transport worker. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The only place a real console connection could be made. Gate G1 is checked first, before any USB enumeration, device handle,
// socket, DNS or mDNS query, or thread: a connection is the inspect tier (each change asks the write tier again, per command, in
// WorkerServer). With the constant false the compiler leaves everything after the check out of the program (CS0162), so this version
// contains no path from a request to the hardware.
namespace CcsWorker.Shell
{
    public static class RealTransports
    {
        public const string ConsoleAddress = "169.254.13.37";   // hakchi's USB network (RNDIS) address, port 22

        public static IConsoleShell Open(string kind)
        {
            if (!Gate.PhysicalInspectEnabled) throw new GateClosedException("connect " + (kind ?? "?"));
            switch (kind)
            {
                case "clovershell": return new ClovershellShell(new LibUsbBulkPipe());
                case "ssh": return new SshShell(new SshNetSession(ConsoleAddress, 22));
            }
            throw new GateClosedException("connect " + kind);
        }
    }
}
