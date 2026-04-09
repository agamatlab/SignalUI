#!/usr/bin/env python3
"""
Batch-call PoseEstApiBridge.dll (same entry point as singalUI PoseEstApiBridgeClient)
on every frame_*.png under a rotation-capture session folder.

``--window-size`` (rounded to int N) selects ``Archive/{N}+{N}_{G,X,Y}.txt``. Those files are copied
to ``hmf_temp_*`` then ``12+12_*`` (DLL path). ``--comp-x`` / ``--comp-y`` are only numeric arguments
to PoseEstApi (default 11); they do not choose the matrix filenames.

This isolates "DLL + Archive + intrinsics" from the Avalonia app: if results match
the app's pose_probe.csv, the problem is likely UI/camera/timing; if they differ,
check cwd, DLL search path, or parameters.

Usage (from anywhere):
  python run_pose_bridge_batch.py ^
    --out-root "F:\\...\\SignalUI\\out" ^
    --session  "storage\\session_20260402_175203_697"

Defaults match ConfigViewModel-ish HMF preset (tune with CLI flags).
"""
from __future__ import annotations

import argparse
import csv
import ctypes
import os
import shutil
import sys
from pathlib import Path


def _status_name(code: int) -> str:
    return {1: "Success", -1: "CoarseFail", -3: "DecodeFail"}.get(code, f"Unknown({code})")


_HMF_TEMP_STEM = "hmf_temp"
# PoseEstApiBridge.dll still opens these paths (embedded in binary).
_BRIDGE_DLL_ARCHIVE_STEM = "12+12"


def hmf_archive_stem_from_window(window_size: float) -> str:
    """Archive prefix ``{N}+{N}`` from HMF window size N (rounded to int)."""
    n = int(round(window_size))
    if n <= 0:
        raise ValueError(f"window-size must be positive, got {window_size!r}")
    return f"{n}+{n}"


def _archive_triplet(archive_dir: Path, stem: str) -> tuple[Path, Path, Path]:
    return (
        archive_dir / f"{stem}_G.txt",
        archive_dir / f"{stem}_X.txt",
        archive_dir / f"{stem}_Y.txt",
    )


def verify_hmf_archive_files(out_root: Path, window_size: float) -> str:
    """Ensure ``Archive/{N+N}_{G,X,Y}.txt`` exist for the chosen ``--window-size``."""
    stem = hmf_archive_stem_from_window(window_size)
    ad = out_root / "Archive"
    missing: list[str] = []
    for p in _archive_triplet(ad, stem):
        if not p.is_file():
            missing.append(str(p))
    if missing:
        raise FileNotFoundError(
            f"Missing HMF archive for --window-size {window_size} (bundle {stem}):\n"
            + "\n".join(missing)
        )
    return stem


def sync_hmf_archive_for_bridge(out_root: Path, window_size: float) -> str:
    """
    ``{N+N}_*`` → ``hmf_temp_*`` (active staging), then ``hmf_temp_*`` → ``12+12_*`` (DLL path).
    """
    stem = hmf_archive_stem_from_window(window_size)
    ad = out_root / "Archive"
    for suf in ("G", "X", "Y"):
        src = ad / f"{stem}_{suf}.txt"
        mid = ad / f"{_HMF_TEMP_STEM}_{suf}.txt"
        dll_slot = ad / f"{_BRIDGE_DLL_ARCHIVE_STEM}_{suf}.txt"
        shutil.copy2(src, mid)
        shutil.copy2(mid, dll_slot)
    return stem


def _encode_path_winansi(p: Path) -> bytes:
    s = str(p.resolve())
    try:
        return s.encode("mbcs")
    except Exception:
        return s.encode(sys.getfilesystemencoding() or "utf-8", errors="replace")


def load_bridge(out_root: Path) -> ctypes.CDLL:
    out_root = out_root.resolve()
    if not out_root.is_dir():
        raise FileNotFoundError(f"out-root not a directory: {out_root}")
    bridge = out_root / "PoseEstApiBridge.dll"
    if not bridge.is_file():
        raise FileNotFoundError(f"Missing {bridge}")

    os.chdir(out_root)
    if hasattr(os, "add_dll_directory"):
        os.add_dll_directory(str(out_root))

    dll = ctypes.CDLL(str(bridge))
    # int PoseEstApi(const char*, double* K9, double pixel, double focal, pitchX, pitchY,
    #                  compX, compY, windowSize, codePitchBlocks,
    #                  double* pose6, int* poseSize, int* status);
    dll.PoseEstApi.argtypes = [
        ctypes.c_char_p,
        ctypes.POINTER(ctypes.c_double),
        ctypes.c_double,
        ctypes.c_double,
        ctypes.c_double,
        ctypes.c_double,
        ctypes.c_double,
        ctypes.c_double,
        ctypes.c_double,
        ctypes.c_double,
        ctypes.POINTER(ctypes.c_double),
        ctypes.POINTER(ctypes.c_int),
        ctypes.POINTER(ctypes.c_int),
    ]
    dll.PoseEstApi.restype = ctypes.c_int
    return dll


def _windows_last_error_info() -> tuple[int, str]:
    """Best-effort Win32 error after a native call (may be 0 or stale if DLL did not set it)."""
    if os.name != "nt":
        return 0, ""
    try:
        code = int(ctypes.get_last_error())
    except (AttributeError, ctypes.ArgumentError):
        return 0, ""
    if code == 0:
        return 0, ""
    try:
        msg = ctypes.FormatError(code).strip()
    except Exception:
        msg = ""
    return code, msg


def call_pose(
    dll: ctypes.CDLL,
    image_path: Path,
    *,
    fx: float,
    fy: float,
    cx: float,
    cy: float,
    pixel_size: float,
    focal_len: float,
    pitch_x: float,
    pitch_y: float,
    comp_x: float,
    comp_y: float,
    window_size: float,
    code_pitch_blocks: float,
) -> dict[str, object]:
    """
    Invoke PoseEstApi and return every out-value we can read from this binding.

    ``bridge_ret``: 0 => bridge reports failure (same idea as C# throwing); nonzero => call completed, interpret ``status_code``.
    """
    k = (ctypes.c_double * 9)(fx, 0.0, cx, 0.0, fy, cy, 0.0, 0.0, 1.0)
    pose = (ctypes.c_double * 6)()
    pose_size = ctypes.c_int(-1)
    status = ctypes.c_int(-999999)
    path_b = _encode_path_winansi(image_path)

    if os.name == "nt":
        try:
            ctypes.set_last_error(0)
        except (AttributeError, ctypes.ArgumentError):
            pass

    ret = dll.PoseEstApi(
        path_b,
        k,
        ctypes.c_double(pixel_size),
        ctypes.c_double(focal_len),
        ctypes.c_double(pitch_x),
        ctypes.c_double(pitch_y),
        ctypes.c_double(comp_x),
        ctypes.c_double(comp_y),
        ctypes.c_double(window_size),
        ctypes.c_double(code_pitch_blocks),
        pose,
        ctypes.byref(pose_size),
        ctypes.byref(status),
    )

    psz = int(pose_size.value)
    st = int(status.value)
    pose_list = [float(pose[i]) for i in range(6)]
    win_code, win_msg = _windows_last_error_info()

    return {
        "bridge_ret": int(ret),
        "pose_size": psz,
        "status_code": st,
        "pose": pose_list,
        "image_path_resolved": str(image_path.resolve()),
        "windows_last_error_code": win_code,
        "windows_last_error_message": win_msg,
    }


def format_dll_result_line(r: dict[str, object], *, python_error: str = "") -> str:
    """Single-line summary of everything returned / captured (for console and CSV column)."""
    pose = list(r.get("pose") or [])
    parts = [
        f"bridge_ret={r.get('bridge_ret')}",
        f"pose_size={r.get('pose_size')}",
        f"status_code={r.get('status_code')}",
        f"pose6={pose}",
        f"path={r.get('image_path_resolved')}",
    ]
    wc = r.get("windows_last_error_code")
    if wc:
        wm = r.get("windows_last_error_message") or ""
        parts.append(f"Win32Error={wc}" + (f"({wm})" if wm else ""))
    if python_error:
        parts.append(f"python_exc={python_error}")
    return "; ".join(parts)


def main() -> int:
    here = Path(__file__).resolve().parent
    default_out = (here.parent.parent / "out").resolve()

    ap = argparse.ArgumentParser(description="Batch PoseEstApiBridge on rotation capture PNGs")
    ap.add_argument(
        "--out-root",
        type=Path,
        default=default_out,
        help=f"Published app folder (PoseEstApiBridge.dll + Archive). Default: {default_out}",
    )
    ap.add_argument(
        "--session",
        type=Path,
        required=True,
        help="Session folder (under out/storage/...) or absolute path",
    )
    ap.add_argument("--fx", type=float, default=6363.64)
    ap.add_argument("--fy", type=float, default=6363.64)
    ap.add_argument("--cx", type=float, default=0.0)
    ap.add_argument("--cy", type=float, default=0.0)
    ap.add_argument("--focal-len", type=float, default=28.0, help="mm, passed to DLL")
    ap.add_argument("--pixel-size", type=float, default=4.4e-3, help="mm, passed to DLL")
    ap.add_argument("--pitch-x", type=float, default=0.1)
    ap.add_argument("--pitch-y", type=float, default=0.1)
    ap.add_argument(
        "--comp-x",
        type=float,
        default=11.0,
        help="HMF components X passed to PoseEstApi only (default 11; does not select Archive files)",
    )
    ap.add_argument(
        "--comp-y",
        type=float,
        default=11.0,
        help="HMF components Y passed to PoseEstApi only (default 11; does not select Archive files)",
    )
    ap.add_argument(
        "--window-size",
        type=float,
        default=10.0,
        help="Selects Archive/{N}+{N}_*.txt with N=round(value); also passed as windowSize to PoseEstApi",
    )
    ap.add_argument("--code-pitch-blocks", type=float, default=6.0)
    ap.add_argument("--output-csv", type=Path, default=None, help="Write results here (default: <session>/dll_pose_bridge_batch.csv)")
    args = ap.parse_args()

    out_root = args.out_root.resolve()
    session = args.session
    if not session.is_absolute():
        session = (out_root / session).resolve()
    if not session.is_dir():
        print(f"ERROR: session not found: {session}", file=sys.stderr)
        return 2

    frames = sorted(session.glob("frame_*.png"))
    if not frames:
        print(f"ERROR: no frame_*.png under {session}", file=sys.stderr)
        return 3

    try:
        verify_hmf_archive_files(out_root, args.window_size)
        bundle_stem = sync_hmf_archive_for_bridge(out_root, args.window_size)
    except Exception as ex:
        print(f"ERROR: HMF Archive: {ex}", file=sys.stderr)
        return 4

    try:
        dll = load_bridge(out_root)
    except Exception as ex:
        print(f"ERROR: load_bridge: {ex}", file=sys.stderr)
        return 4

    out_csv = args.output_csv or (session / "dll_pose_bridge_batch.csv")
    n_win = int(round(args.window_size))
    print(f"out_root (cwd for DLL) = {out_root}")
    print(
        f"HMF: Archive/{bundle_stem}_{{G,X,Y}}.txt (window-size → N={n_win}) → hmf_temp_* → "
        f"{_BRIDGE_DLL_ARCHIVE_STEM}_* (DLL); comp=({args.comp_x},{args.comp_y}) → PoseEstApi only"
    )
    print(f"session = {session}")
    print(f"frames  = {len(frames)}")
    print(f"csv out = {out_csv}")
    print("---")

    fieldnames = [
        "file",
        "bridge_ret",
        "pose_size",
        "status_code",
        "status_name",
        "rx",
        "ry",
        "rz",
        "tx",
        "ty",
        "tz",
        "windows_last_error_code",
        "windows_last_error_message",
        "dll_full_summary",
        "python_exception",
    ]

    rows = []
    for png in frames:
        py_exc = ""
        r: dict[str, object] = {
            "bridge_ret": -888,
            "pose_size": -1,
            "status_code": -999999,
            "pose": [0.0] * 6,
            "image_path_resolved": str(png.resolve()),
            "windows_last_error_code": 0,
            "windows_last_error_message": "",
        }
        try:
            r = call_pose(
                dll,
                png,
                fx=args.fx,
                fy=args.fy,
                cx=args.cx,
                cy=args.cy,
                pixel_size=args.pixel_size,
                focal_len=args.focal_len,
                pitch_x=args.pitch_x,
                pitch_y=args.pitch_y,
                comp_x=args.comp_x,
                comp_y=args.comp_y,
                window_size=args.window_size,
                code_pitch_blocks=args.code_pitch_blocks,
            )
        except Exception as ex:
            py_exc = repr(ex)
            wc, wm = _windows_last_error_info()
            r["windows_last_error_code"] = wc
            r["windows_last_error_message"] = wm

        br = int(r["bridge_ret"])
        psz = int(r["pose_size"])
        st = int(r["status_code"])
        pose = list(r["pose"])
        wc = int(r.get("windows_last_error_code") or 0)
        wm = str(r.get("windows_last_error_message") or "")
        dll_summary = format_dll_result_line(r, python_error=py_exc)

        if py_exc:
            st_name = "PythonException"
        elif br == 0:
            st_name = f"BridgeCallFailed(out_status={st},out_pose_size={psz})"
        else:
            st_name = _status_name(st)

        row = {
            "file": png.name,
            "bridge_ret": br,
            "pose_size": psz,
            "status_code": st,
            "status_name": st_name,
            "rx": pose[0],
            "ry": pose[1],
            "rz": pose[2],
            "tx": pose[3],
            "ty": pose[4],
            "tz": pose[5],
            "windows_last_error_code": wc,
            "windows_last_error_message": wm,
            "dll_full_summary": dll_summary,
            "python_exception": py_exc,
        }
        rows.append(row)
        ok = "OK" if br != 0 and not py_exc else "FAIL"
        print(f"{ok}  {png.name}")
        print(f"     {dll_summary}")

    with open(out_csv, "w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=fieldnames)
        w.writeheader()
        w.writerows(rows)

    print("---")
    print(f"Wrote {out_csv}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
