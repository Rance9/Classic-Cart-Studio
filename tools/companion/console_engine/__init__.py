"""Classic Cart Studio console engine (docs/console-install-architecture.md).

The older plan's simulation (an immutable installation plan, fake NES Classic profiles, a transport that answers the pinned Hakchi2 CE 3.9.3
command surface in memory, the operation sequencing with progress events, a journal), Classic Cart Studio's own HOME on a console
(ccs_install.py: rehearsed on disposable stand-ins, and run on a real console through the console transport worker, worker_console.py) and
the stdio worker protocol (worker.py). Gate G1 is in code (`gate.py`): both physical tiers are open in this version; nothing here opens a
USB device or a socket itself.
"""
ENGINE_VERSION = "0.1.0"
PROTOCOL_VERSION = 1
CAPABILITY = "console-sim"
