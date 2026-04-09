"""
Local TCP/JSON line protocol for HSC-103. Started by singalUI (C#); one client, one serial session.
Each request is one JSON object per line (UTF-8); each response is one JSON object per line.
"""
from __future__ import annotations

import argparse
import json
import os
import socket
import sys
import traceback

_SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
if _SCRIPT_DIR not in sys.path:
    sys.path.insert(0, _SCRIPT_DIR)

import serial  # noqa: E402
from hsc103 import HSC103, DEFAULT_ROTATION_DEGREES_PER_PULSE  # noqa: E402


def read_line(conn: socket.socket) -> bytes:
    buf = bytearray()
    while True:
        ch = conn.recv(1)
        if not ch:
            raise EOFError("connection closed")
        if ch == b"\n":
            break
        buf.extend(ch)
    return bytes(buf)


def send_json(conn: socket.socket, obj: dict) -> None:
    conn.sendall(json.dumps(obj, separators=(",", ":")).encode("utf-8") + b"\n")


def main() -> None:
    ap = argparse.ArgumentParser(description="HSC-103 JSON/TCP helper for singalUI")
    ap.add_argument("--listen-host", default="127.0.0.1")
    ap.add_argument("--listen-port", type=int, required=True)
    args = ap.parse_args()

    state: dict = {"stage": None, "axis": 1, "dpp": float(DEFAULT_ROTATION_DEGREES_PER_PULSE)}

    def handle(req: dict) -> dict:
        rid = req.get("id", 0)
        cmd = req.get("cmd", "")
        try:
            if cmd == "ping":
                return {"id": rid, "ok": True}

            if cmd == "open":
                if state["stage"] is not None:
                    try:
                        state["stage"].close()
                    except Exception:
                        pass
                    state["stage"] = None
                port = req.get("serial_port") or req.get("port")
                if not port:
                    return {"id": rid, "ok": False, "error": "missing serial_port"}
                state["axis"] = int(req.get("axis", 1))
                state["dpp"] = float(req.get("degrees_per_pulse", DEFAULT_ROTATION_DEGREES_PER_PULSE))
                if state["dpp"] <= 0:
                    return {"id": rid, "ok": False, "error": "degrees_per_pulse must be > 0"}
                stage = HSC103(port=port)
                try:
                    stage.open()
                except serial.SerialException as e:
                    return {"id": rid, "ok": False, "error": f"SerialException: {e}"}
                if not stage.is_open:
                    return {"id": rid, "ok": False, "error": "port did not open"}
                if not stage.is_valid():
                    try:
                        stage.close()
                    except Exception:
                        pass
                    return {"id": rid, "ok": False, "error": "device not recognized (expected HSC-103)"}
                state["stage"] = stage
                return {"id": rid, "ok": True}

            st = state["stage"]
            if st is None:
                return {"id": rid, "ok": False, "error": "not connected (open first)"}

            if cmd == "check":
                return {"id": rid, "ok": bool(st.is_valid())}

            if cmd == "get_pos":
                deg = st.get_angle_degrees(axis=state["axis"], degrees_per_pulse=state["dpp"])
                return {"id": rid, "ok": True, "degrees": deg}

            if cmd == "home":
                ok = bool(st.return_origin(axis=state["axis"]))
                if ok:
                    return {"id": rid, "ok": True}
                return {"id": rid, "ok": False, "error": "home failed"}

            if cmd == "move_abs":
                target = float(req["degrees"])
                ok = bool(
                    st.set_angle_degrees(
                        axis=state["axis"],
                        target_degrees=target,
                        degrees_per_pulse=state["dpp"],
                    )
                )
                if ok:
                    return {"id": rid, "ok": True}
                return {"id": rid, "ok": False, "error": "move failed"}

            if cmd == "stop":
                ok = bool(st.stop(axis=state["axis"]))
                return {"id": rid, "ok": ok}

            if cmd == "set_speed":
                start = int(req["start"])
                bound = int(req["bound"])
                accel = int(req["accel_time"])
                st.set_speed(state["axis"], start, bound, accel)
                return {"id": rid, "ok": True}

            if cmd == "close_serial":
                try:
                    st.close()
                finally:
                    state["stage"] = None
                return {"id": rid, "ok": True}

            if cmd == "shutdown":
                try:
                    if state["stage"] is not None:
                        state["stage"].close()
                except Exception:
                    pass
                state["stage"] = None
                return {"id": rid, "ok": True, "_exit": True}

            return {"id": rid, "ok": False, "error": f"unknown cmd: {cmd}"}
        except Exception as e:
            return {"id": rid, "ok": False, "error": f"{type(e).__name__}: {e}\n{traceback.format_exc()}"}

    sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    sock.bind((args.listen_host, args.listen_port))
    sock.listen(1)
    print("READY", flush=True)
    # Avoid filling the C# stdout pipe when hsc103.py prints during moves
    try:
        sys.stdout = open(os.devnull, "w", encoding="utf-8")
    except Exception:
        pass

    conn, _ = sock.accept()
    conn.settimeout(600.0)
    try:
        while True:
            try:
                raw = read_line(conn)
            except EOFError:
                break
            line = raw.decode("utf-8").strip()
            if not line:
                continue
            req = json.loads(line)
            rsp = handle(req)
            send_json(conn, rsp)
            if rsp.get("_exit"):
                break
    finally:
        try:
            conn.close()
        except Exception:
            pass
        try:
            sock.close()
        except Exception:
            pass
        try:
            if state["stage"] is not None:
                state["stage"].close()
        except Exception:
            pass


if __name__ == "__main__":
    main()
