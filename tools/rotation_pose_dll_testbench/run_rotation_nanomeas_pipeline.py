#!/usr/bin/env python3
"""
Rotation capture session → per-image pose (PoseEstApiBridge) → NanoMeasCalib (RotCalibBridge.dll).

Reads ``manifest.csv`` from a rotation-capture folder (``frame_*.png`` + ``index,theta_deg,theta_rad,file``),
runs the same pose bridge as ``rotation_pose_dll_testbench/run_pose_bridge_batch.py``, then calls
``nanomeas_calib_rotation_stage_simple`` (same as Avalonia ``NanoMeasCalibNative``).

Requires under ``--out-root`` (published app folder):
  - PoseEstApiBridge.dll, PoseEstApi dependencies, Archive\\...
  - RotCalibBridge.dll + NanoMeasCalib.dll (built/copied per singalUI.csproj CopyRotCalibNative)

Example:
  python tools/run_rotation_nanomeas_pipeline.py ^
    --out-root out ^
    --session out/storage/session_20260402_184325_811_1bcfa923

Portability:
  - ``--out-root`` and ``--session`` may be absolute; only defaults depend on where this file lives.
  - Set env ``SIGNALUI_REPO`` to the SignalUI repo root (folder that contains ``tools/`` and ``out/``)
    if you move this script outside the repo.
  - Otherwise the script searches upward from its directory for ``tools/rotation_pose_dll_testbench/run_pose_bridge_batch.py``.
"""
from __future__ import annotations

import argparse
import csv
import json
import os
import sys
from pathlib import Path


def _find_pose_bridge_dir_and_repo_out() -> tuple[Path, Path]:
    """
    Directory containing ``run_pose_bridge_batch.py``, and default published ``out`` folder.

    Order: env SIGNALUI_REPO → sibling ``rotation_pose_dll_testbench`` under this file's parent
    → walk parents for ``tools/rotation_pose_dll_testbench``.
    """
    script_dir = Path(__file__).resolve().parent
    env = os.environ.get("SIGNALUI_REPO", "").strip()
    if env:
        root = Path(env).resolve()
        rpb = root / "tools" / "rotation_pose_dll_testbench"
        if (rpb / "run_pose_bridge_batch.py").is_file():
            return rpb, root / "out"

    rpb = script_dir / "rotation_pose_dll_testbench"
    if (rpb / "run_pose_bridge_batch.py").is_file():
        return rpb, script_dir.parent / "out"

    for parent in script_dir.parents:
        rpb = parent / "tools" / "rotation_pose_dll_testbench"
        if (rpb / "run_pose_bridge_batch.py").is_file():
            return rpb, parent / "out"

    return script_dir / "rotation_pose_dll_testbench", script_dir.parent / "out"


_RPB_DIR, _DEFAULT_OUT_ROOT = _find_pose_bridge_dir_and_repo_out()
if str(_RPB_DIR) not in sys.path:
    sys.path.insert(0, str(_RPB_DIR))
import run_pose_bridge_batch as pose_rpb  # noqa: E402


def _status_message(status: int) -> str:
    return {0: "Success", -1: "Insufficient data (N < 12)", -2: "Optimization failed", -99: "Internal error"}.get(
        status, f"Unknown({status})"
    )


def load_manifest_rows(session_dir: Path) -> list[dict[str, str]]:
    manifest = session_dir / "manifest.csv"
    if not manifest.is_file():
        raise FileNotFoundError(f"Missing manifest.csv: {manifest}")
    rows: list[dict[str, str]] = []
    with open(manifest, newline="", encoding="utf-8-sig") as f:
        r = csv.DictReader(f)
        for row in r:
            rows.append({k.strip(): (v or "").strip() for k, v in row.items()})
    if not rows:
        raise ValueError(f"Empty manifest: {manifest}")
    rows.sort(key=lambda x: int(float(x["index"])))
    return rows


def load_rotcalib_bridge(out_root: Path):
    import ctypes

    out_root = out_root.resolve()
    bridge_path = out_root / "RotCalibBridge.dll"
    nano_path = out_root / "NanoMeasCalib.dll"
    if not bridge_path.is_file():
        raise FileNotFoundError(
            f"Missing {bridge_path}. Build PoseEstApi/RotPoseEstApi/native/RotCalibBridge (x64) "
            "or run a singalUI build so CopyRotCalibNative copies DLLs to the output folder."
        )
    if not nano_path.is_file():
        raise FileNotFoundError(
            f"Missing {nano_path} next to the app. Copy from PoseEstApi/RotPoseEstApi/lib/ after building the bridge."
        )

    os.chdir(out_root)
    if hasattr(os, "add_dll_directory"):
        os.add_dll_directory(str(out_root))

    dll = ctypes.CDLL(str(bridge_path))
    dll.nanomeas_calib_init.argtypes = []
    dll.nanomeas_calib_init.restype = None
    dll.nanomeas_calib_cleanup.argtypes = []
    dll.nanomeas_calib_cleanup.restype = None

    # int nanomeas_calib_rotation_stage_simple(const double* theta, const double* meas_euler, int N,
    #   const double* sigma_R, const double* sigma_t, int* status,
    #   double* T_CW_euler, double* T_ST_euler, double* errors_out);
    dll.nanomeas_calib_rotation_stage_simple.argtypes = [
        ctypes.POINTER(ctypes.c_double),
        ctypes.POINTER(ctypes.c_double),
        ctypes.c_int,
        ctypes.POINTER(ctypes.c_double),
        ctypes.POINTER(ctypes.c_double),
        ctypes.POINTER(ctypes.c_int),
        ctypes.POINTER(ctypes.c_double),
        ctypes.POINTER(ctypes.c_double),
        ctypes.POINTER(ctypes.c_double),
    ]
    dll.nanomeas_calib_rotation_stage_simple.restype = ctypes.c_int
    return dll


def run_nanomeas_calib(
    *,
    theta: list[float],
    meas: list[float],
    sigma_r: list[float],
    sigma_t: list[float],
    bridge_dll,
) -> tuple[int, int, list[float], list[float], list[float]]:
    """Returns (return_code, status, T_CW[6], T_ST[6], errors[6*N]). C layout meas[6*i+k] per NanoMeasCalib_wrapper.h."""
    import ctypes

    n = len(theta)
    if len(meas) != 6 * n:
        raise ValueError(f"meas length {len(meas)} != 6*{n}")

    theta_a = (ctypes.c_double * n)(*theta)
    meas_a = (ctypes.c_double * (6 * n))(*meas)
    sr = (ctypes.c_double * 3)(*sigma_r)
    st = (ctypes.c_double * 3)(*sigma_t)
    status = ctypes.c_int(0)
    t_cw = (ctypes.c_double * 6)()
    t_st = (ctypes.c_double * 6)()
    errors = (ctypes.c_double * (6 * n))()

    bridge_dll.nanomeas_calib_init()
    try:
        rc = bridge_dll.nanomeas_calib_rotation_stage_simple(
            theta_a,
            meas_a,
            ctypes.c_int(n),
            sr,
            st,
            ctypes.byref(status),
            t_cw,
            t_st,
            errors,
        )
    finally:
        bridge_dll.nanomeas_calib_cleanup()

    return (
        int(rc),
        int(status.value),
        [float(t_cw[i]) for i in range(6)],
        [float(t_st[i]) for i in range(6)],
        [float(errors[i]) for i in range(6 * n)],
    )


def main() -> int:
    default_out = _DEFAULT_OUT_ROOT.resolve()
    default_session = default_out / "storage" / "session_20260402_184325_811_1bcfa923"

    ap = argparse.ArgumentParser(
        description="PoseEst per frame + NanoMeasCalib rotation calibration (RotCalibBridge)"
    )
    ap.add_argument(
        "--out-root",
        type=Path,
        default=default_out,
        help=f"Published singalUI folder (default: {_DEFAULT_OUT_ROOT})",
    )
    ap.add_argument(
        "--session",
        type=Path,
        default=default_session,
        help="Rotation capture folder (manifest.csv + PNGs)",
    )
    ap.add_argument("--count", type=int, default=12, help="Number of frames to use (sorted by manifest index)")
    ap.add_argument(
        "--require-pose-status",
        type=int,
        default=1,
        help="Require PoseEst status_code == this (1=Success). Use -1 to allow any status if bridge_ret!=0.",
    )
    # Pose bridge parameters (same defaults as run_pose_bridge_batch.py)
    ap.add_argument("--fx", type=float, default=6363.64)
    ap.add_argument("--fy", type=float, default=6363.64)
    ap.add_argument("--cx", type=float, default=0.0)
    ap.add_argument("--cy", type=float, default=0.0)
    ap.add_argument("--focal-len", type=float, default=28.0)
    ap.add_argument("--pixel-size", type=float, default=4.4e-3)
    ap.add_argument("--pitch-x", type=float, default=0.1)
    ap.add_argument("--pitch-y", type=float, default=0.1)
    ap.add_argument("--comp-x", type=float, default=11.0)
    ap.add_argument("--comp-y", type=float, default=11.0)
    ap.add_argument("--window-size", type=float, default=10.0)
    ap.add_argument("--code-pitch-blocks", type=float, default=6.0)
    ap.add_argument(
        "--sigma-rx",
        type=float,
        default=20e-6,
        help="rad (default matches NanoMeasCalib_wrapper / C#)",
    )
    ap.add_argument("--sigma-ry", type=float, default=20e-6)
    ap.add_argument("--sigma-rz", type=float, default=1e-6)
    ap.add_argument("--sigma-tx", type=float, default=3e-6)
    ap.add_argument("--sigma-ty", type=float, default=3e-6)
    ap.add_argument("--sigma-tz", type=float, default=40e-6)
    args = ap.parse_args()

    out_root = args.out_root.resolve()
    session = args.session
    if not session.is_absolute():
        session = (out_root / session).resolve()
    if not session.is_dir():
        print(f"ERROR: session not found: {session}", file=sys.stderr)
        return 2

    n_use = args.count
    if n_use < 12:
        print("ERROR: NanoMeasCalib requires N >= 12.", file=sys.stderr)
        return 2

    all_rows = load_manifest_rows(session)
    if len(all_rows) < n_use:
        print(f"ERROR: manifest has {len(all_rows)} rows, need {n_use}", file=sys.stderr)
        return 2
    rows = all_rows[:n_use]

    try:
        pose_rpb.verify_hmf_archive_files(out_root, args.window_size)
        pose_rpb.sync_hmf_archive_for_bridge(out_root, args.window_size)
    except Exception as ex:
        print(f"ERROR: HMF archive: {ex}", file=sys.stderr)
        return 3

    try:
        pose_dll = pose_rpb.load_bridge(out_root)
    except Exception as ex:
        print(f"ERROR: PoseEstApiBridge: {ex}", file=sys.stderr)
        return 4

    theta: list[float] = []
    meas: list[float] = []
    pose_log: list[dict[str, object]] = []

    print(f"session = {session}")
    print(f"Using first {n_use} manifest rows (by index).")
    print("--- PoseEstApiBridge per frame ---")

    for row in rows:
        idx = row["index"]
        fn = row["file"]
        theta_rad = float(row["theta_rad"])
        png = session / fn
        if not png.is_file():
            print(f"ERROR: missing image {png}", file=sys.stderr)
            return 5

        try:
            r = pose_rpb.call_pose(
                pose_dll,
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
            print(f"ERROR: frame {fn}: {ex!r}", file=sys.stderr)
            return 6

        br = int(r["bridge_ret"])
        st = int(r["status_code"])
        p6 = list(r["pose"])
        pose_log.append({"index": idx, "file": fn, "theta_rad": theta_rad, "pose_result": r})

        req = args.require_pose_status
        if br == 0:
            print(pose_rpb.format_dll_result_line(r))
            print(f"ERROR: pose bridge failed for {fn} (bridge_ret=0)", file=sys.stderr)
            return 7
        if req >= 0 and st != req:
            print(pose_rpb.format_dll_result_line(r))
            print(
                f"ERROR: pose status mismatch for {fn} (status={st}, require_pose_status={req})",
                file=sys.stderr,
            )
            return 7

        theta.append(theta_rad)
        meas.extend(p6)  # Rx,Ry,Rz rad, Tx,Ty,Tz mm — matches C# RotationCalibrationSample / wrapper
        print(f"OK  index={idx} {fn}  theta_rad={theta_rad:.8f}  pose6={p6}")

    print("--- RotCalibBridge / NanoMeasCalib ---")
    try:
        rot_dll = load_rotcalib_bridge(out_root)
    except Exception as ex:
        print(f"ERROR: {ex}", file=sys.stderr)
        return 8

    sigma_r = [args.sigma_rx, args.sigma_ry, args.sigma_rz]
    sigma_t = [args.sigma_tx, args.sigma_ty, args.sigma_tz]

    rc, st, t_cw, t_st, errors = run_nanomeas_calib(
        theta=theta,
        meas=meas,
        sigma_r=sigma_r,
        sigma_t=sigma_t,
        bridge_dll=rot_dll,
    )

    ok_native = rc == 0 and st == 0
    print(f"nanomeas_calib_rotation_stage_simple: return_code={rc} status={st} ({_status_message(st)})")
    print(f"T_CW euler [rad]: Rx,Ry,Rz = {t_cw[0]:.8f}, {t_cw[1]:.8f}, {t_cw[2]:.8f}")
    print(f"T_CW [mm]: tx,ty,tz = {t_cw[3]:.8f}, {t_cw[4]:.8f}, {t_cw[5]:.8f}")
    print(f"T_ST euler [rad]: {t_st[0]:.8f}, {t_st[1]:.8f}, {t_st[2]:.8f}")
    print(f"T_ST [mm]: {t_st[3]:.8f}, {t_st[4]:.8f}, {t_st[5]:.8f}")

    out_json = session / "nanomeas_pipeline_result.json"
    payload = {
        "session": str(session),
        "out_root": str(out_root),
        "n": n_use,
        "pose_params": {
            "window_size": args.window_size,
            "comp_x": args.comp_x,
            "comp_y": args.comp_y,
        },
        "nanomeas": {
            "return_code": rc,
            "status": st,
            "status_message": _status_message(st),
            "T_CW_euler_rad_mm": t_cw,
            "T_ST_euler_rad_mm": t_st,
            "errors_out": errors,
        },
        "theta_rad": theta,
        "pose_per_frame_summary": [
            {
                "index": pl["index"],
                "file": pl["file"],
                "theta_rad": pl["theta_rad"],
                "bridge_ret": pl["pose_result"]["bridge_ret"],
                "status_code": pl["pose_result"]["status_code"],
                "pose6": pl["pose_result"]["pose"],
            }
            for pl in pose_log
        ],
    }
    with open(out_json, "w", encoding="utf-8") as f:
        json.dump(payload, f, indent=2)
    print(f"Wrote {out_json}")
    return 0 if ok_native else 9


if __name__ == "__main__":
    raise SystemExit(main())
