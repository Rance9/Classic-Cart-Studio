// Classic Cart Studio console transport worker. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The only place a real console connection is made. Gate G1 is checked first, before any USB enumeration, device handle, socket, DNS
// or mDNS query, or thread: a connection is the inspect tier (each change asks the write tier again, per command, in WorkerServer, and
// needs the backup admission). Both tiers are open in this version. Only hakchi's two links exist: its clovershell USB gadget and SSH on
// its USB network; any other kind is refused before anything is looked for.
using CcsWorker.Ops;

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
            throw new OpException("bad-transport", "this worker connects to a console only through clovershell or SSH");
        }
    }
}
