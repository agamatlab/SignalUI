#!/usr/bin/env python3
"""
Test bench for **NanoMeas.dll** (see ``PoseEstApi/NanoMeas_SDK_secret/README.md`` and ``include/nanomeas_api.h``).

Loads **12** grayscale PNGs from a rotation-capture session (``manifest.csv`` + ``frame_*.png``),
runs ``NanoMeas_ProcessFrame_Track_U8`` on each frame with a **single** handle so tracking state
carries across frames, and writes a CSV/JSON summary.

**Deploy DLLs** into ``--dll-dir`` (recommended: ``PoseEstApi/NanoMeas_SDK_secret`` next to your
checked-in header — copy ``NanoMeas.dll``, ``fftw3.dll``, and MKL DLLs if using MKL build):

  NanoMeas.dll, fftw3.dll, (+ mkl_*.dll for MKL build), MSVCP140 / VCRUNTIME140 from VC++ Redist.

**HMF sequences** for ``NanoMeas_SetHmfData`` must be **0/1 int** arrays. This script parses
comma-separated files such as ``PoseEstApi/code/10+10_X.txt`` / ``10+10_Y.txt``.
Per ``nanomeas_api.h``: ``seq_x`` = physical-Y axis, ``seq_y`` = physical-X axis — by default
we map **Y.txt → seq_x** and **X.txt → seq_y** (``--no-hmf-axis-swap`` to use X→seq_x, Y→seq_y).

Example::

  python tools/nanomeas_sdk_testbench/run_nanomeas_on_session.py ^
    --dll-dir F:\\adhoc_peisenlab_6dofdemo\\6_dof_demo\\agha\\PoseEstApi\\NanoMeas_SDK_secret ^
    --session F:\\adhoc_peisenlab_6dofdemo\\6_dof_demo\\agha\\SignalUI\\out\\storage\\session_20260402_184325_811_1bcfa923
"""
from __future__ import annotations

import argparse
import csv
import json
import os
import sys
from pathlib import Path


def _find_default_paths() -> tuple[Path, Path, Path]:
    """Repo-relative defaults: NanoMeas_SDK_secret, session under SignalUI/out, HMF txt in PoseEstApi/code."""
    here = Path(__file__).resolve()
    for p in [here.parent] + list(here.parents):
        nano = p / "PoseEstApi" / "NanoMeas_SDK_secret"
        sig_out = p / "SignalUI" / "out" / "storage" / "session_20260402_184325_811_1bcfa923"
        code = p / "PoseEstApi" / "code"
        if nano.is_dir() and (code / "10+10_X.txt").is_file():
            return nano, sig_out, code
    root = here.parents[3] if len(here.parents) > 3 else here.parent
    return (
        root / "PoseEstApi" / "NanoMeas_SDK_secret",
        root / "SignalUI" / "out" / "storage" / "session_20260402_184325_811_1bcfa923",
        root / "PoseEstApi" / "code",
    )


def load_manifest_session_pngs(session_dir: Path, count: int) -> list[tuple[int, str, Path]]:
    manifest = session_dir / "manifest.csv"
    if not manifest.is_file():
        raise FileNotFoundError(manifest)
    rows: list[dict[str, str]] = []
    with open(manifest, newline="", encoding="utf-8-sig") as f:
        for row in csv.DictReader(f):
            rows.append({k.strip(): (v or "").strip() for k, v in row.items()})
    rows.sort(key=lambda r: int(float(r["index"])))
    if len(rows) < count:
        raise ValueError(f"Need {count} manifest rows, got {len(rows)}")
    out: list[tuple[int, str, Path]] = []
    for r in rows[:count]:
        idx = int(float(r["index"]))
        fn = r["file"]
        p = session_dir / fn
        if not p.is_file():
            raise FileNotFoundError(p)
        out.append((idx, fn, p))
    return out


def parse_hmf_comma01(path: Path) -> list[int]:
    text = path.read_text(encoding="utf-8").replace("\n", "").replace("\r", "")
    parts = [p.strip() for p in text.split(",") if p.strip() != ""]
    seq = [int(float(x)) for x in parts]
    for v in seq:
        if v not in (0, 1):
            raise ValueError(f"{path}: expected 0/1 only, got {v}")
    return seq


def load_png_gray_u8(path: Path) -> tuple[int, int, bytes]:
    try:
        from PIL import Image
    except ImportError as e:
        raise RuntimeError("Install Pillow: pip install pillow") from e
    im = Image.open(path).convert("L")
    w, h = im.size
    data = im.tobytes()
    if len(data) != w * h:
        raise RuntimeError(f"Bad buffer size for {path}")
    return h, w, data


def setup_nanomeas_dll(dll_dir: Path):
    import ctypes
    from ctypes import (
        POINTER,
        Structure,
        byref,
        c_double,
        c_int,
        c_int32,
        c_uint8,
        c_void_p,
    )

    dll_dir = dll_dir.resolve()
    nano = dll_dir / "NanoMeas.dll"
    if not nano.is_file():
        raise FileNotFoundError(
            f"NanoMeas.dll not found in {dll_dir}. Copy SDK binaries here per README.md "
            "(NanoMeas.dll, fftw3.dll, VC++ runtime, optional MKL DLLs)."
        )

    os.chdir(dll_dir)
    if hasattr(os, "add_dll_directory"):
        os.add_dll_directory(str(dll_dir))

    dll = ctypes.CDLL(str(nano))

    class NanoMeasCameraParams(Structure):
        _fields_ = [
            ("K", c_double * 9),
            ("pixel_size_x", c_double),
            ("pixel_size_y", c_double),
            ("focal_length", c_double),
        ]

    class NanoMeasGridParams(Structure):
        _fields_ = [
            ("pitch_x", c_double),
            ("pitch_y", c_double),
            ("code_pitch_blocks", c_int),
            ("window_size", c_int),
        ]

    class NanoMeasIpDFTConfig(Structure):
        _fields_ = [("win_order", c_int)]

    class NanoMeasPipelineResult(Structure):
        _fields_ = [
            ("pose", c_double * 6),
            ("pose_abs", c_double * 6),
            ("amp_signal", c_double),
            ("amp_dc", c_double),
            ("decode_success", c_int),
            ("did_full_solve", c_int),
            ("did_ipdft", c_int),
            ("did_decode", c_int),
            ("refine_ms", c_double),
        ]

    dll.NanoMeas_Create.argtypes = []
    dll.NanoMeas_Create.restype = c_void_p

    dll.NanoMeas_Destroy.argtypes = [c_void_p]
    dll.NanoMeas_Destroy.restype = None

    dll.NanoMeas_Initialize.argtypes = [c_void_p, c_int, c_int, POINTER(NanoMeasIpDFTConfig)]
    dll.NanoMeas_Initialize.restype = c_int32

    dll.NanoMeas_SetHmfData.argtypes = [c_void_p, POINTER(c_int32), c_int, POINTER(c_int32), c_int]
    dll.NanoMeas_SetHmfData.restype = c_int32

    dll.NanoMeas_ProcessFrame_Track_U8.argtypes = [
        c_void_p,
        POINTER(c_uint8),
        c_int,
        c_int,
        POINTER(NanoMeasCameraParams),
        POINTER(NanoMeasGridParams),
        c_void_p,
        c_int,
        c_double,
        POINTER(NanoMeasPipelineResult),
    ]
    dll.NanoMeas_ProcessFrame_Track_U8.restype = c_int32

    dll.NanoMeas_GetLastError.argtypes = [c_void_p]
    dll.NanoMeas_GetLastError.restype = ctypes.c_char_p

    dll.NanoMeas_GetVersion.argtypes = []
    dll.NanoMeas_GetVersion.restype = ctypes.c_char_p

    return dll, NanoMeasCameraParams, NanoMeasGridParams, NanoMeasIpDFTConfig, NanoMeasPipelineResult


def _decode_c_str(p) -> str:
    if not p:
        return ""
    try:
        return p.decode("utf-8", errors="replace")
    except Exception:
        return str(p)


def main() -> int:
    def_dll, def_sess, def_code = _find_default_paths()

    ap = argparse.ArgumentParser(description="NanoMeas.dll: 12 session PNGs via ProcessFrame_Track_U8")
    ap.add_argument("--dll-dir", type=Path, default=def_dll, help="Folder containing NanoMeas.dll + deps")
    ap.add_argument("--session", type=Path, default=def_sess, help="Rotation capture folder")
    ap.add_argument("--count", type=int, default=12, help="Number of frames (manifest order)")
    ap.add_argument(
        "--hmf-x-file",
        type=Path,
        default=None,
        help="Comma 0/1 sequence file (default: PoseEstApi/code/10+10_X.txt)",
    )
    ap.add_argument("--hmf-y-file", type=Path, default=None)
    ap.add_argument(
        "--no-hmf-axis-swap",
        action="store_true",
        help="If set: seq_x from X file, seq_y from Y file (default follows API: Y→seq_x, X→seq_y)",
    )
    ap.add_argument("--win-order", type=int, default=3, help="NanoMeasIpDFTConfig.win_order")
    ap.add_argument("--refresh-period", type=int, default=10)
    ap.add_argument("--residual-threshold", type=float, default=3.0)
    ap.add_argument("--fx", type=float, default=6363.64)
    ap.add_argument("--fy", type=float, default=6363.64)
    ap.add_argument("--cx", type=float, default=0.0)
    ap.add_argument("--cy", type=float, default=0.0)
    ap.add_argument("--pixel-size-x", type=float, default=4.4e-3)
    ap.add_argument("--pixel-size-y", type=float, default=4.4e-3)
    ap.add_argument("--focal-length", type=float, default=28.0)
    ap.add_argument("--pitch-x", type=float, default=0.1)
    ap.add_argument("--pitch-y", type=float, default=0.1)
    ap.add_argument("--code-pitch-blocks", type=int, default=6)
    ap.add_argument("--window-size", type=int, default=10)
    args = ap.parse_args()

    dll_dir = args.dll_dir.resolve()
    session = args.session.resolve()
    code_root = def_code.resolve()
    hmf_x_path = (args.hmf_x_file or (code_root / "10+10_X.txt")).resolve()
    hmf_y_path = (args.hmf_y_file or (code_root / "10+10_Y.txt")).resolve()

    if not hmf_x_path.is_file() or not hmf_y_path.is_file():
        print(f"ERROR: HMF text files not found:\n  {hmf_x_path}\n  {hmf_y_path}", file=sys.stderr)
        return 2

    seq_x_file, seq_y_file = (hmf_x_path, hmf_y_path) if args.no_hmf_axis_swap else (hmf_y_path, hmf_x_path)
    seq_x = parse_hmf_comma01(seq_x_file)
    seq_y = parse_hmf_comma01(seq_y_file)

    try:
        frames = load_manifest_session_pngs(session, args.count)
    except Exception as ex:
        print(f"ERROR: session/manifest: {ex}", file=sys.stderr)
        return 3

    h0, w0, _ = load_png_gray_u8(frames[0][2])
    for _, _, p in frames[1:]:
        h, w, _ = load_png_gray_u8(p)
        if (h, w) != (h0, w0):
            print(f"ERROR: image size mismatch {p} vs first frame", file=sys.stderr)
            return 4

    try:
        pack = setup_nanomeas_dll(dll_dir)
    except Exception as ex:
        print(f"ERROR: {ex}", file=sys.stderr)
        return 5

    import ctypes
    from ctypes import byref, c_int32, c_uint8

    dll, Cam, Grid, IpCfg, PipeRes = pack

    handle = dll.NanoMeas_Create()
    if not handle:
        print("ERROR: NanoMeas_Create returned NULL", file=sys.stderr)
        return 6

    ip_cfg = IpCfg(win_order=int(args.win_order))
    st = dll.NanoMeas_Initialize(handle, int(h0), int(w0), byref(ip_cfg))
    if st != 0:
        err = _decode_c_str(dll.NanoMeas_GetLastError(handle))
        print(f"ERROR: NanoMeas_Initialize status={st} {err}", file=sys.stderr)
        dll.NanoMeas_Destroy(handle)
        return 7

    seq_x_arr = (c_int32 * len(seq_x))(*seq_x)
    seq_y_arr = (c_int32 * len(seq_y))(*seq_y)
    st = dll.NanoMeas_SetHmfData(handle, seq_x_arr, len(seq_x), seq_y_arr, len(seq_y))
    if st != 0:
        err = _decode_c_str(dll.NanoMeas_GetLastError(handle))
        print(f"ERROR: NanoMeas_SetHmfData status={st} len_x={len(seq_x)} len_y={len(seq_y)} {err}", file=sys.stderr)
        dll.NanoMeas_Destroy(handle)
        return 8

    cam = Cam()
    cam.K[0], cam.K[1], cam.K[2] = args.fx, 0.0, args.cx
    cam.K[3], cam.K[4], cam.K[5] = 0.0, args.fy, args.cy
    cam.K[6], cam.K[7], cam.K[8] = 0.0, 0.0, 1.0
    cam.pixel_size_x = args.pixel_size_x
    cam.pixel_size_y = args.pixel_size_y
    cam.focal_length = args.focal_length

    grid = Grid()
    grid.pitch_x = args.pitch_x
    grid.pitch_y = args.pitch_y
    grid.code_pitch_blocks = int(args.code_pitch_blocks)
    grid.window_size = int(args.window_size)

    ver = _decode_c_str(dll.NanoMeas_GetVersion())
    print(f"NanoMeas_GetVersion: {ver}")
    print(f"dll-dir={dll_dir}")
    print(f"session={session} frames={args.count} image={h0}x{w0}")
    print(
        f"HMF: seq_x from {seq_x_file.name} (len={len(seq_x)}), "
        f"seq_y from {seq_y_file.name} (len={len(seq_y)}) "
        f"(swap={'off' if args.no_hmf_axis_swap else 'on'} per API)"
    )
    print("---")

    rows_out: list[dict] = []
    for idx, fn, png_path in frames:
        h, w, raw = load_png_gray_u8(png_path)
        buf = (c_uint8 * len(raw)).from_buffer_copy(raw)
        res = PipeRes()
        st = dll.NanoMeas_ProcessFrame_Track_U8(
            handle,
            buf,
            h,
            w,
            byref(cam),
            byref(grid),
            None,
            int(args.refresh_period),
            float(args.residual_threshold),
            byref(res),
        )
        err = _decode_c_str(dll.NanoMeas_GetLastError(handle))
        pose = [float(res.pose[i]) for i in range(6)]
        pose_abs = [float(res.pose_abs[i]) for i in range(6)]
        row = {
            "manifest_index": idx,
            "file": fn,
            "nano_status": int(st),
            "last_error": err,
            "pose_rx_ry_rz_tx_ty_tz": pose,
            "pose_abs": pose_abs,
            "decode_success": int(res.decode_success),
            "did_full_solve": int(res.did_full_solve),
            "did_ipdft": int(res.did_ipdft),
            "did_decode": int(res.did_decode),
            "refine_ms": float(res.refine_ms),
            "amp_signal": float(res.amp_signal),
            "amp_dc": float(res.amp_dc),
        }
        rows_out.append(row)
        ok = "OK" if st == 0 else "FAIL"
        print(f"{ok}  {fn}  status={st}  decode={res.decode_success}  pose[:]={pose[:]}  err={err[:80]!r}")

    dll.NanoMeas_Destroy(handle)

    out_csv = session / "nanomeas_sdk_track_u8.csv"
    out_json = session / "nanomeas_sdk_track_u8.json"
    fieldnames = list(rows_out[0].keys()) if rows_out else []
    with open(out_csv, "w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=fieldnames)
        w.writeheader()
        for r in rows_out:
            w.writerow({k: json.dumps(r[k]) if isinstance(r[k], list) else r[k] for k in fieldnames})

    with open(out_json, "w", encoding="utf-8") as f:
        json.dump(
            {
                "dll_dir": str(dll_dir),
                "session": str(session),
                "version": ver,
                "hmf": {
                    "seq_x_file": str(seq_x_file),
                    "seq_y_file": str(seq_y_file),
                    "len_x": len(seq_x),
                    "len_y": len(seq_y),
                },
                "frames": rows_out,
            },
            f,
            indent=2,
        )
    print("---")
    print(f"Wrote {out_csv}")
    print(f"Wrote {out_json}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
