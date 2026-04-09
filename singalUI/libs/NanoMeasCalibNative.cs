using System;
using System.Runtime.InteropServices;

namespace singalUI.libs;

/// <summary>
/// P/Invoke for RotCalibBridge.dll (NanoMeasCalib_wrapper.c linked with NanoMeasCalib.lib).
/// Requires RotCalibBridge.dll and NanoMeasCalib.dll next to the executable.
/// </summary>
public static class NanoMeasCalibNative
{
    private const string DllName = "RotCalibBridge.dll";

    private static readonly object InitLock = new();
    private static bool _initialized;

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern void nanomeas_calib_init();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern void nanomeas_calib_cleanup();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int nanomeas_calib_rotation_stage_simple(
        [In] double[] theta,
        [In] double[] meas_euler,
        int N,
        [In] double[] sigma_R,
        [In] double[] sigma_t,
        out int status,
        [Out] double[] T_CW_euler,
        [Out] double[] T_ST_euler,
        [Out] double[] errors_out);

    public static void EnsureInitialized()
    {
        lock (InitLock)
        {
            if (_initialized) return;
            nanomeas_calib_init();
            _initialized = true;
        }
    }

    public static void TryShutdown()
    {
        lock (InitLock)
        {
            if (!_initialized) return;
            try
            {
                nanomeas_calib_cleanup();
            }
            catch (DllNotFoundException)
            {
                // Bridge not deployed
            }
            _initialized = false;
        }
    }

    /// <summary>
    /// Runs rotation-stage calibration. Arrays use column-major meas_euler (6×N) per api_guide.md.
    /// </summary>
    public static RotationCalibrationNativeResult RunRotationCalibration(
        double[] theta,
        double[] measEulerColumnMajor,
        int n,
        double[]? sigmaR = null,
        double[]? sigmaT = null)
    {
        if (theta == null || theta.Length < n)
            throw new ArgumentException("theta length must be >= N.", nameof(theta));
        if (measEulerColumnMajor == null || measEulerColumnMajor.Length < 6 * n)
            throw new ArgumentException("meas_euler must have at least 6*N elements.", nameof(measEulerColumnMajor));

        EnsureInitialized();

        sigmaR ??= new[] { 20e-6, 20e-6, 1e-6 };
        sigmaT ??= new[] { 3e-6, 3e-6, 40e-6 };

        var tCw = new double[6];
        var tSt = new double[6];
        var errors = new double[6 * n];

        int status;
        int rc = nanomeas_calib_rotation_stage_simple(
            theta,
            measEulerColumnMajor,
            n,
            sigmaR,
            sigmaT,
            out status,
            tCw,
            tSt,
            errors);

        return new RotationCalibrationNativeResult(rc, status, tCw, tSt, errors);
    }

    public static string StatusMessage(int status) => status switch
    {
        0 => "Success",
        -1 => "Insufficient data (N < 12)",
        -2 => "Optimization failed",
        -99 => "Internal error",
        _ => $"Unknown status: {status}",
    };
}

public sealed class RotationCalibrationNativeResult
{
    public int ReturnCode { get; }
    public int Status { get; }
    public double[] T_CW_Euler { get; }
    public double[] T_ST_Euler { get; }
    public double[] ErrorsOut { get; }

    public RotationCalibrationNativeResult(int returnCode, int status, double[] tCw, double[] tSt, double[] errors)
    {
        ReturnCode = returnCode;
        Status = status;
        T_CW_Euler = tCw;
        T_ST_Euler = tSt;
        ErrorsOut = errors;
    }

    public bool IsSuccess => Status == 0 && ReturnCode == 0;
}
