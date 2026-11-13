"""Read-only Win32 evidence for live FakeLock capture/input tests (Windows only)."""
import ctypes
import argparse
import json
import sys
from ctypes import wintypes
from datetime import datetime, timezone

user32 = ctypes.WinDLL("user32", use_last_error=True)
user32.GetForegroundWindow.restype = wintypes.HWND
user32.GetWindowLongW.argtypes = [wintypes.HWND, ctypes.c_int]
user32.GetWindowDisplayAffinity.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
user32.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
user32.GetWindowRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
user32.IsWindowVisible.argtypes = [wintypes.HWND]
user32.GetWindowTextW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
user32.GetLayeredWindowAttributes.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD), ctypes.POINTER(ctypes.c_ubyte), ctypes.POINTER(wintypes.DWORD)]
user32.WindowFromPoint.argtypes = [wintypes.POINT]
user32.WindowFromPoint.restype = wintypes.HWND
callback_type = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
user32.EnumWindows.argtypes = [callback_type, wintypes.LPARAM]


def inspect(hwnd):
    title = ctypes.create_unicode_buffer(512)
    user32.GetWindowTextW(hwnd, title, len(title))
    pid, affinity, rect = wintypes.DWORD(), wintypes.DWORD(), wintypes.RECT()
    user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
    user32.GetWindowRect(hwnd, ctypes.byref(rect))
    ok = bool(user32.GetWindowDisplayAffinity(hwnd, ctypes.byref(affinity)))
    color, alpha, flags = wintypes.DWORD(), ctypes.c_ubyte(), wintypes.DWORD()
    layered = bool(user32.GetLayeredWindowAttributes(hwnd, ctypes.byref(color), ctypes.byref(alpha), ctypes.byref(flags)))
    return dict(hwnd=hwnd, pid=pid.value, title=title.value,
                visible=bool(user32.IsWindowVisible(hwnd)),
                rect=[rect.left, rect.top, rect.right, rect.bottom],
                ex_style=hex(user32.GetWindowLongW(hwnd, -20) & 0xFFFFFFFF),
                affinity=affinity.value if ok else None,
                layered_alpha=alpha.value if layered else None,
                layered_flags=flags.value if layered else None)


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--lock-pid", type=int, help="Assert opaque, excluded, click-through lock window for this PID")
    args = parser.parse_args()
    windows = []

    @callback_type
    def visit(hwnd, _):
        item = inspect(hwnd)
        if item["visible"]:
            windows.append(item)
        return True

    user32.EnumWindows(visit, 0)
    foreground = inspect(user32.GetForegroundWindow())
    result = dict(time=datetime.now(timezone.utc).isoformat(), foreground=foreground, windows=windows)
    if args.lock_pid:
        locks = [w for w in windows if w["pid"] == args.lock_pid]
        if len(locks) != 1:
            raise SystemExit("Expected exactly one visible lock window")
        lock = locks[0]
        style = int(lock["ex_style"], 16)
        errors = []
        if style & 0x08080028 != 0x08080028:
            errors.append("Missing topmost/noactivate/layered/transparent styles")
        if lock["affinity"] != 17 or lock["layered_alpha"] != 255 or lock["layered_flags"] != 2:
            errors.append("Expected WDA_EXCLUDEFROMCAPTURE=17 and opaque alpha=255")
        if foreground["pid"] == args.lock_pid:
            errors.append("Lock took foreground focus")
        left, top, right, bottom = lock["rect"]
        point = wintypes.POINT((left + right) // 2, (top + bottom) // 2)
        hit = inspect(user32.WindowFromPoint(point))
        result["center_hit"] = hit
        if hit["pid"] == args.lock_pid:
            errors.append("WindowFromPoint still hits the lock")
        result["assertions"] = dict(passed=not errors, errors=errors)
        print(json.dumps(result, ensure_ascii=False, indent=2))
        raise SystemExit(1 if errors else 0)
    print(json.dumps(result, ensure_ascii=False, indent=2))
