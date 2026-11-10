// Classic Cart Studio console transport worker tests. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The test project's openers: what "connect" reaches in every worker server a test builds (Harness, --serve-busybox). They never reach
// RealTransports, whatever Gate G1 says, so a test run cannot enumerate USB or open a socket even in a build whose gate is open (the
// activation rehearsal). Only the closed-gate tests in GateTests call the real opener themselves, and only while its gate is closed.
using CcsWorker.Shell;

namespace CcsWorker.Tests
{
    static class TestOpeners
    {
        public const string NeverText = "a test never connects to a console";

        /// <summary>Refuses every transport kind with the gate's own refusal ("physical-disabled" on the protocol), before any device.</summary>
        public static IConsoleShell Never(string kind) => throw new GateClosedException("connect " + (kind ?? "?") + ": " + NeverText);
    }
}
