"""Gate (a): static proof that raw audio cannot leave the audio path.

    python tools/gate_static.py [package_dir]     # exit 0 = pass

Rules, checked on the AST of every module in the runtime package ``adduck/``:

1. Audio-path modules (features, ringbuffer, capture) may import only an
   allowlist (numpy, math, typing, dataclasses, collections, queue, the
   ``sounddevice`` reader, and each other). No os/io/pathlib, no logging, no
   json/pickle/wave/soundfile, no socket/http/urllib/requests/subprocess.
2. Audio-path modules call no writers: open, print, eval/exec, save/savez/
   savetxt/tofile/tobytes/dump(s)/write(lines)/send(all)/connect/urlopen and
   no logger methods.
3. Every public function in the audio path is annotated to return only
   Features, Iterator[Features], None, str, bool or int - never an array.
4. ``extract_features`` and ``RingBuffer`` (the only things that ever hold
   samples) are referenced nowhere outside the audio path, and np.fft is used
   only in features.py.
5. No module anywhere in the package imports a network or serialisation
   library (socket, http, urllib, requests, httpx, aiohttp, pickle, marshal,
   shelve, wave, soundfile, sqlite3, subprocess).
"""

from __future__ import annotations

import ast
import sys
from pathlib import Path
from typing import List

AUDIO_PATH = {"features.py", "ringbuffer.py", "capture.py"}
AUDIO_IMPORT_ALLOW = {"__future__", "math", "typing", "dataclasses", "collections", "queue", "numpy", "sounddevice"}
AUDIO_RELATIVE_ALLOW = {"features", "ringbuffer"}
BANNED_EVERYWHERE = {
    "socket", "http", "urllib", "urllib3", "requests", "httpx", "aiohttp", "websocket", "websockets",
    "pickle", "marshal", "shelve", "wave", "soundfile", "audioread", "sqlite3", "subprocess",
    "ftplib", "smtplib", "telnetlib", "scipy.io",
}
BANNED_CALLS = {"open", "print", "eval", "exec", "compile", "__import__", "input", "breakpoint"}
BANNED_ATTR_CALLS = {
    "save", "savez", "savez_compressed", "savetxt", "tofile", "tobytes", "tolist", "dump", "dumps",
    "write", "writelines", "send", "sendall", "sendto", "connect", "urlopen", "request",
    "debug", "info", "warning", "warn", "error", "exception", "critical", "log",
}
ALLOWED_RETURNS = {"Features", "Iterator[Features]", "None", "str", "bool", "int"}
SAMPLE_HOLDERS = {"extract_features", "RingBuffer"}


def _module_root(name: str) -> str:
    return name.split(".")[0]


def check_source(name: str, source: str) -> List[str]:
    """Violations for one module (``name`` is its file name)."""
    tree = ast.parse(source, filename=name)
    audio = name in AUDIO_PATH
    out: List[str] = []
    for node in ast.walk(tree):
        if isinstance(node, (ast.Import, ast.ImportFrom)):
            if isinstance(node, ast.ImportFrom) and node.level:
                mods = [node.module or ""]
                if audio and mods[0] not in AUDIO_RELATIVE_ALLOW:
                    out.append(f"{name}:{node.lineno}: audio path imports .{mods[0]}")
                if not audio and mods[0] == "ringbuffer":
                    out.append(f"{name}:{node.lineno}: non-audio module imports ringbuffer")
                continue
            mods = [a.name for a in node.names] if isinstance(node, ast.Import) else [node.module or ""]
            for m in mods:
                if m in BANNED_EVERYWHERE or _module_root(m) in BANNED_EVERYWHERE:
                    out.append(f"{name}:{node.lineno}: imports banned module {m}")
                elif audio and _module_root(m) not in AUDIO_IMPORT_ALLOW:
                    out.append(f"{name}:{node.lineno}: audio path imports non-allowlisted {m}")
        elif isinstance(node, ast.Call) and audio:
            fn = node.func
            if isinstance(fn, ast.Name) and fn.id in BANNED_CALLS:
                out.append(f"{name}:{node.lineno}: audio path calls {fn.id}()")
            if isinstance(fn, ast.Attribute) and fn.attr in BANNED_ATTR_CALLS:
                out.append(f"{name}:{node.lineno}: audio path calls .{fn.attr}()")
        elif isinstance(node, ast.Name) and not audio and node.id in SAMPLE_HOLDERS:
            out.append(f"{name}:{node.lineno}: non-audio module references {node.id}")
        elif isinstance(node, ast.alias) and not audio and node.name in SAMPLE_HOLDERS:
            out.append(f"{name}: non-audio module imports {node.name}")
        elif isinstance(node, ast.Attribute):
            if node.attr in SAMPLE_HOLDERS and not audio:
                out.append(f"{name}:{node.lineno}: non-audio module references .{node.attr}")
            if node.attr == "fft" and name != "features.py":
                out.append(f"{name}:{node.lineno}: np.fft used outside features.py")
        if audio and isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
            if node.name.startswith("_") and node.name not in ("__repr__", "__init__"):
                continue
            if node.name == "__init__":
                continue
            ret = ast.unparse(node.returns) if node.returns is not None else "<unannotated>"
            ret = ret.strip("'\"")
            if ret not in ALLOWED_RETURNS:
                out.append(f"{name}:{node.lineno}: public {node.name}() returns {ret}, not a Features type")
    return out


def check_package(pkg: Path) -> List[str]:
    out: List[str] = []
    for path in sorted(pkg.glob("*.py")):
        out += check_source(path.name, path.read_text())
    present = {p.name for p in pkg.glob("*.py")}
    missing = AUDIO_PATH - present
    out += [f"missing audio-path module {m}" for m in sorted(missing)]
    return out


def main(argv: List[str]) -> int:
    pkg = Path(argv[1]) if len(argv) > 1 else Path(__file__).resolve().parent.parent / "adduck"
    problems = check_package(pkg)
    files = sorted(p.name for p in pkg.glob("*.py"))
    print(f"gate_static: scanned {len(files)} modules in {pkg.name}/ ({', '.join(files)})")
    print(f"gate_static: audio path = {sorted(AUDIO_PATH)}")
    for p in problems:
        print("VIOLATION", p)
    print("gate_static: PASS" if not problems else f"gate_static: FAIL ({len(problems)} violations)")
    return 0 if not problems else 1


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
