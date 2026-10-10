"""Minimal WebSocket client (text frames, client masking, ws:// only) for driving the local DevTools endpoint from tests. No dependencies."""
from __future__ import annotations
import socket, os, struct, base64, urllib.parse

class WS:
    def __init__(self, url: str):
        u = urllib.parse.urlparse(url); self.s = socket.create_connection((u.hostname, u.port or 80), timeout=120)
        key = base64.b64encode(os.urandom(16)).decode()
        self.s.sendall((f"GET {u.path or '/'} HTTP/1.1\r\nHost: {u.hostname}:{u.port}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: {key}\r\nSec-WebSocket-Version: 13\r\n\r\n").encode())
        buf = b""
        while b"\r\n\r\n" not in buf: buf += self.s.recv(4096)
        if b" 101 " not in buf.split(b"\r\n", 1)[0]: raise OSError("websocket handshake failed: " + buf[:80].decode(errors="replace"))
        self.rest = buf.split(b"\r\n\r\n", 1)[1]
    def _read(self, n: int) -> bytes:
        while len(self.rest) < n:
            c = self.s.recv(65536)
            if not c: raise OSError("websocket closed")
            self.rest += c
        out, self.rest = self.rest[:n], self.rest[n:]; return out
    def send(self, text: str):
        data = text.encode(); mask = os.urandom(4); hdr = bytes([0x81])
        n = len(data)
        if n < 126: hdr += bytes([0x80 | n])
        elif n < 65536: hdr += bytes([0x80 | 126]) + struct.pack(">H", n)
        else: hdr += bytes([0x80 | 127]) + struct.pack(">Q", n)
        self.s.sendall(hdr + mask + bytes(b ^ mask[i % 4] for i, b in enumerate(data)))
    def recv(self) -> str:
        while True:
            b0, b1 = self._read(2); op = b0 & 0x0F; n = b1 & 0x7F
            if n == 126: n = struct.unpack(">H", self._read(2))[0]
            elif n == 127: n = struct.unpack(">Q", self._read(8))[0]
            if b1 & 0x80: self._read(4)   # servers do not mask, but be tolerant
            payload = self._read(n)
            if op == 0x1: return payload.decode()
            if op == 0x8: raise OSError("websocket closed by peer")
            if op == 0x9: self.s.sendall(bytes([0x8A, 0x80]) + os.urandom(4))   # pong (masked, empty)
