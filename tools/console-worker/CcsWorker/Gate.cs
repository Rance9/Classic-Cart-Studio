// Classic Cart Studio console transport worker. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Gate G1 in code (docs/console-install-architecture.md section 6): not a setting, not an environment variable, not an argument.
// Opening a physical tier is a reviewed code change released as its own milestone; this version opens both. The two tiers are separate: "connect"
// and every read need PhysicalInspectEnabled (RealTransports); each command that changes the console needs PhysicalWriteEnabled as well,
// asked again for that command (WorkerServer.WriteCommands), so a session opened to read can never write. The older game sync through
// hakchi (G2) is not a tier of this worker: it has no command for it.
using System;

namespace CcsWorker
{
    public static class Gate
    {
        public const bool PhysicalInspectEnabled = true;    // open in this version: connecting to a real console and reading it
        public const bool PhysicalWriteEnabled = true;      // open in this version: each command that changes the console (asked again per command)
        public const string Text = "Connecting to a real console is turned off in this build.";   // only while a tier is closed in code
    }

    public sealed class GateClosedException : Exception
    {
        public GateClosedException(string what) : base(Gate.Text + " (" + what + ": closed in code, Gate G1)") { }
    }
}
