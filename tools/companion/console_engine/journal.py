"""Append-only JSON-lines journal for engine runs. Records stage boundaries, progress, results and errors with plan hashes,
codes, byte counts and durable error codes. Never tokens, never payload bytes, never backup contents, never paths outside
the run folder. Bounded: a run journal stops accepting entries after MAX_LINES and says so once.
"""
from __future__ import annotations
import json, os, time

MAX_LINES = 5000
SECRET_KEYS = ("token", "bearer", "authorization", "password", "secret", "cookie")


def _scrub(obj):
    if isinstance(obj, dict): return {k: ("[redacted]" if any(s in str(k).lower() for s in SECRET_KEYS) else _scrub(v)) for k, v in obj.items()}
    if isinstance(obj, (list, tuple)): return [_scrub(x) for x in obj]
    if isinstance(obj, (bytes, bytearray)): return f"<{len(obj)} bytes>"
    return obj


class Journal:
    def __init__(self, path: str, run_id: str, plan_hash: str = ""):
        self.path = path; self.run_id = run_id; self.plan_hash = plan_hash; self.lines = 0; self.full = False
        os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
        self.append("run", "start", {"run_id": run_id, "plan_hash": plan_hash})

    def append(self, stage: str, event: str, data: dict | None = None, level: str = "info", code: str = "") -> dict:
        rec = {"ts": round(time.time(), 3), "run": self.run_id, "stage": stage, "event": event, "level": level}
        if code: rec["code"] = code
        if data: rec["data"] = _scrub(data)
        if self.full: return rec
        self.lines += 1
        if self.lines > MAX_LINES:
            self.full = True; rec = {"ts": rec["ts"], "run": self.run_id, "stage": "journal", "event": "truncated", "level": "warn", "data": {"max_lines": MAX_LINES}}
        with open(self.path, "a", encoding="utf-8") as f: f.write(json.dumps(rec, ensure_ascii=False) + "\n")
        return rec

    @staticmethod
    def read(path: str, limit: int = 2000) -> list[dict]:
        out = []
        try:
            with open(path, encoding="utf-8") as f:
                for line in f:
                    try: out.append(json.loads(line))
                    except ValueError: continue
        except OSError: return []
        return out[-limit:]
