"""Classic Cart Studio console engine (docs/console-install-architecture.md).

Simulation tier only in this build: an immutable installation plan, fake NES Classic profiles, a transport that answers
the pinned Hakchi2 CE 3.9.3 command surface in memory, the operation sequencing with progress events, a journal, and a
stdio worker protocol. Gate G1 is closed in code (`gate.py`): nothing here can open a USB device or a socket.
"""
ENGINE_VERSION = "0.1.0"
PROTOCOL_VERSION = 1
CAPABILITY = "console-sim"
