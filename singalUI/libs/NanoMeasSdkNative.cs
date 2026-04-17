using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace singalUI.libs;

/// <summary>
/// P/Invoke for native NanoMeas SDK (see <c>PoseEstApi/NanoMeas_SDK_secret/include/nanomeas_api.h</c>).
/// On Windows, the native library is preloaded via <see cref="LoadLibraryExW"/> with
/// <c>LOAD_WITH_ALTERED_SEARCH_PATH</c> so <c>fftw3.dll</c> / MKL in the same folder resolve.
/// Search order: <c>NANOMEAS_SDK_DIR</c>, developer paths for <c>NanoMeas_SDK_noblas_*</c> / <c>NanoMeas_SDK_secret</c>,
/// next to the executable, then repo-relative <c>PoseEstApi\...</c> from the app base (published <c>out\</c>).
/// </summary>
public static unsafe class NanoMeasSdkNative
{
    public const int NanoMeasOk = 0;

    private const string DllName = "NanoMeasNative.dll";
    private const string UpstreamDllName = "NanoMeas.dll";

    private const uint LoadWithAlteredSearchPath = 0x00000008;

    private static readonly object LoadLock = new();
    private static bool _preloadDone;
    private static string? _loadedFromPath;
    private static string? _loadedFromCategory;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern IntPtr LoadLibraryExW(string lpLibFileName, IntPtr hFile, uint dwFlags);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);
    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    private static extern bool FreeLibrary(IntPtr hModule);

    /// <summary>Call before any NanoMeas P/Invoke (e.g. before starting a background pose task).</summary>
    public static void EnsureNanoMeasNativeReady()
    {
        lock (LoadLock)
        {
            if (_preloadDone)
                return;

            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("NanoMeas native DLL is only supported on Windows.");

            var log = new StringBuilder();
            foreach (var (category, p) in EnumerateNanoMeasDllCandidates())
            {
                if (!File.Exists(p))
                {
                    log.AppendLine($"  —   [{category}] {p} (missing)");
                    continue;
                }

                string fullPath = Path.IsPathRooted(p) ? p : Path.GetFullPath(p);
                IntPtr module = LoadLibraryExW(fullPath, IntPtr.Zero, LoadWithAlteredSearchPath);
                if (module == IntPtr.Zero)
                {
                    int loadErr = Marshal.GetLastWin32Error();
                    log.AppendLine($"  !!  [{category}] {fullPath} (LoadLibraryEx failed: {loadErr} {new Win32Exception(loadErr).Message})");
                    continue;
                }

                IntPtr createPtr = GetProcAddress(module, "NanoMeas_Create");
                if (createPtr == IntPtr.Zero)
                {
                    int procErr = Marshal.GetLastWin32Error();
                    log.AppendLine($"  xx  [{category}] {fullPath} (missing export NanoMeas_Create; GetProcAddress={procErr})");
                    _ = FreeLibrary(module);
                    continue;
                }

                _loadedFromPath = fullPath;
                _loadedFromCategory = category;
                _preloadDone = true;
                return;
            }

            throw new DllNotFoundException(
                "No usable NanoMeas native DLL found (must export NanoMeas_Create). Checked paths:\n" + log +
                "Set NANOMEAS_SDK_DIR to a valid SDK folder, or ensure NativeSDK\\NanoMeasNative.dll + fftw3.dll exist next to the executable.");
        }
    }

    /// <summary>Full path used after a successful <see cref="EnsureNanoMeasNativeReady"/>; otherwise null.</summary>
    public static string? GetLoadedNanoMeasDllPath() => _loadedFromPath;
    public static string? GetLoadedNanoMeasDllCategory() => _loadedFromCategory;

    private static IEnumerable<(string Category, string Path)> EnumerateNanoMeasDllCandidates()
    {
        string fileName = DllName;
        string upstreamFileName = UpstreamDllName;
        string baseDir = AppContext.BaseDirectory;

        // Published layout first: avoids loading wrong developer-side DLLs.
        yield return ("NativeSDK", Path.Combine(baseDir, "NativeSDK", fileName));
        yield return ("NativeSDK-UpstreamName", Path.Combine(baseDir, "NativeSDK", upstreamFileName));
        yield return ("AppBase", Path.Combine(baseDir, fileName));
        yield return ("AppBase-UpstreamName", Path.Combine(baseDir, upstreamFileName));
        yield return ("AppBaseNoblasSubdir", Path.Combine(baseDir, "NanoMeas_SDK_noblas_20260403", upstreamFileName));
        yield return ("AppBaseSecretSubdir", Path.Combine(baseDir, "NanoMeas_SDK_secret", upstreamFileName));

        // Explicit environment override.
        string? env = Environment.GetEnvironmentVariable("NANOMEAS_SDK_DIR");
        if (!string.IsNullOrWhiteSpace(env))
        {
            string envDir = env.Trim().Trim('"');
            yield return ("Env:NANOMEAS_SDK_DIR-Alias", Path.Combine(envDir, fileName));
            yield return ("Env:NANOMEAS_SDK_DIR-Upstream", Path.Combine(envDir, upstreamFileName));
        }

        // Developer machine fallbacks.
        const string preferredNoblas =
            @"F:\adhoc_peisenlab_6dofdemo\6_dof_demo\agha\PoseEstApi\NanoMeas_SDK_noblas_20260403\NanoMeas.dll";
        if (File.Exists(preferredNoblas))
            yield return ("DevAbsoluteNoblas", preferredNoblas);

        const string preferredSecret =
            @"F:\adhoc_peisenlab_6dofdemo\6_dof_demo\agha\PoseEstApi\NanoMeas_SDK_secret\NanoMeas.dll";
        if (File.Exists(preferredSecret))
            yield return ("DevAbsoluteSecret", preferredSecret);

        // Published: repo-root sibling PoseEstApi (e.g. out\ next to agha\)
        yield return ("RepoSiblingNoblas", Path.GetFullPath(Path.Combine(baseDir, "..", "..", "PoseEstApi", "NanoMeas_SDK_noblas_20260403", fileName)));
        yield return ("RepoSiblingSecret", Path.GetFullPath(Path.Combine(baseDir, "..", "..", "PoseEstApi", "NanoMeas_SDK_secret", fileName)));

        // Dev: singalUI\bin\...\ → 5×.. → repo sibling PoseEstApi
        yield return ("DevRepoSiblingNoblas", Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "PoseEstApi", "NanoMeas_SDK_noblas_20260403", fileName)));
        yield return ("DevRepoSiblingSecret", Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "PoseEstApi", "NanoMeas_SDK_secret", fileName)));

        // Dev with win-x64 subfolder: 6×.. → PoseEstApi
        yield return ("DevWinRidSiblingNoblas", Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "..", "PoseEstApi", "NanoMeas_SDK_noblas_20260403", fileName)));
        yield return ("DevWinRidSiblingSecret", Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "..", "PoseEstApi", "NanoMeas_SDK_secret", fileName)));
    }

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern IntPtr NanoMeas_Create();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void NanoMeas_Destroy(IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int NanoMeas_Initialize(
        IntPtr handle,
        int height,
        int width,
        ref NanoMeasIpDFTConfigNative config);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int NanoMeas_SetHmfData(
        IntPtr handle,
        [In] int[] seq_x,
        int len_x,
        [In] int[] seq_y,
        int len_y);

    /// <summary>
    /// <paramref name="result"/> is blittable (fixed <c>double[6]</c> in <see cref="NanoMeasPipelineResultNative"/>).
    /// Use <see cref="InAttribute"/> + <see cref="OutAttribute"/> on <paramref name="result"/> for clarity.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int NanoMeas_ProcessFrame_Track_U8(
        IntPtr handle,
        [In] byte[] img_u8,
        int height,
        int width,
        ref NanoMeasCameraParamsNative cam,
        ref NanoMeasGridParamsNative grid,
        IntPtr refine_cfg_null,
        int refresh_period,
        double residual_threshold,
        [In, Out] ref NanoMeasPipelineResultNative result);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern IntPtr NanoMeas_GetLastError(IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern IntPtr NanoMeas_GetVersion();

    [StructLayout(LayoutKind.Sequential)]
    public struct NanoMeasCameraParamsNative
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 9)]
        public double[] K;

        public double pixel_size_x;
        public double pixel_size_y;
        public double focal_length;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NanoMeasGridParamsNative
    {
        public double pitch_x;
        public double pitch_y;
        public int code_pitch_blocks;
        public int window_size;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NanoMeasIpDFTConfigNative
    {
        public int win_order;
    }

    /// <summary>
    /// Must match <c>NanoMeasPipelineResult</c> in native API (embedded <c>double[6]</c> arrays).
    /// Fixed buffers are blittable: native writes into this memory directly (no failed <c>ByValArray</c> copy-back).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each of <see cref="pose"/> and <see cref="pose_abs"/> is a six-DOF vector, not a 4×4 matrix.
    /// Indices (same for both buffers): 0 = Rx, 1 = Ry, 2 = Rz (radians); 3 = X, 4 = Y, 5 = Z (millimeters).
    /// Euler axis order and relation to a homogeneous transform are defined by the native SDK (see upstream <c>nanomeas_api.h</c>);
    /// managed code uses these scalars as-is.
    /// </para>
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct NanoMeasPipelineResultNative
    {
        public fixed double pose[6];
        public fixed double pose_abs[6];
        public double amp_signal;
        public double amp_dc;
        public int decode_success;
        public int did_full_solve;
        public int did_ipdft;
        public int did_decode;
        public double refine_ms;
    }

    public static string? MarshalUtf8String(IntPtr p)
    {
        if (p == IntPtr.Zero)
            return null;
        return Marshal.PtrToStringUTF8(p);
    }

    public static NanoMeasCameraParamsNative BuildCameraParams(
        double fx, double fy, double cx, double cy,
        double pixelSizeX, double pixelSizeY, double focalLengthMm)
    {
        return new NanoMeasCameraParamsNative
        {
            K = new[] { fx, 0.0, cx, 0.0, fy, cy, 0.0, 0.0, 1.0 },
            pixel_size_x = pixelSizeX,
            pixel_size_y = pixelSizeY,
            focal_length = focalLengthMm,
        };
    }

    public static NanoMeasGridParamsNative BuildGridParams(
        double pitchX, double pitchY, int codePitchBlocks, int windowSize) =>
        new()
        {
            pitch_x = pitchX,
            pitch_y = pitchY,
            code_pitch_blocks = codePitchBlocks,
            window_size = windowSize,
        };

    /// <summary>Stack-allocated result struct (native fills <see cref="NanoMeasPipelineResultNative.pose"/> in place).</summary>
    public static NanoMeasPipelineResultNative CreatePipelineResult() => default;

    /// <summary>
    /// Copies <see cref="NanoMeasPipelineResultNative.pose"/> and <see cref="NanoMeasPipelineResultNative.pose_abs"/> into managed buffers
    /// after <see cref="NanoMeas_ProcessFrame_Track_U8"/> (element order: Rx, Ry, Rz rad; X, Y, Z mm).
    /// </summary>
    public static unsafe void CopyPipelinePoseArrays(ref NanoMeasPipelineResultNative r, double[] poseOut, double[] poseAbsOut)
    {
        if (poseOut.Length < 6 || poseAbsOut.Length < 6)
            throw new ArgumentException("Each buffer must have length 6.");

        fixed (double* pp = r.pose)
        fixed (double* pa = r.pose_abs)
        {
            for (int i = 0; i < 6; i++)
            {
                poseOut[i] = pp[i];
                poseAbsOut[i] = pa[i];
            }
        }
    }

    /// <summary>
    /// Loads HMF 0/1 sequences from <c>Archive\{n}+{n}_Y.txt</c> (→ seq_x) and <c>_X.txt</c> (→ seq_y) under <see cref="AppContext.BaseDirectory"/>.
    /// <c>n</c> matches <see cref="NanoMeasGridParamsNative.window_size"/> (e.g. pattern CKB040040 → 10 → <c>10+10_*.txt</c>, CKB100100 → 12 → <c>12+12_*.txt</c>).
    /// </summary>
    public static bool TryLoadArchiveHmfSequences(int windowRounded, out int[] seqX, out int[] seqY, out string? error)
    {
        seqX = Array.Empty<int>();
        seqY = Array.Empty<int>();
        error = null;
        if (windowRounded < 1)
        {
            error = "Window size must round to a positive integer for HMF file selection.";
            return false;
        }

        string stem = $"{windowRounded}+{windowRounded}";
        string baseDir = AppContext.BaseDirectory;
        var pathY = Path.Combine(baseDir, "Archive", $"{stem}_Y.txt");
        var pathX = Path.Combine(baseDir, "Archive", $"{stem}_X.txt");
        if (!File.Exists(pathY) || !File.Exists(pathX))
        {
            error =
                $"HMF text not found (stem {stem}). Expected: Archive\\{stem}_Y.txt and Archive\\{stem}_X.txt next to the executable.";
            return false;
        }

        try
        {
            seqX = ParseCommaSeparatedBinaryInts(pathY);
            seqY = ParseCommaSeparatedBinaryInts(pathX);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static int[] ParseCommaSeparatedBinaryInts(string path)
    {
        var text = File.ReadAllText(path).Replace("\r", "").Replace("\n", "");
        var parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var arr = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) || v is not (0 or 1))
                throw new InvalidDataException($"{path}: expected 0/1 comma list at token {i}: '{parts[i]}'");
            arr[i] = v;
        }

        return arr;
    }

    /// <summary>
    /// Runs <see cref="NanoMeas_ProcessFrame_Track_U8"/> on each frame with one handle (tracking carries across frames).
    /// </summary>
    public static IReadOnlyList<NanoMeasFrameTrackResult> RunProcessFrameTrackU8Sequence(
        IReadOnlyList<NanoMeasFrameInput> frames,
        int[] seqX,
        int[] seqY,
        NanoMeasCameraParamsNative cam,
        NanoMeasGridParamsNative grid,
        int winOrder = 3,
        int refreshPeriod = 10,
        double residualThreshold = 3.0)
    {
        if (frames == null || frames.Count == 0)
            throw new ArgumentException("No frames.", nameof(frames));
        if (seqX == null || seqY == null)
            throw new ArgumentNullException(nameof(seqX));

        EnsureNanoMeasNativeReady();

        int h = frames[0].Height;
        int w = frames[0].Width;
        foreach (var f in frames)
        {
            if (f.Height != h || f.Width != w)
                throw new InvalidOperationException("All frames must share the same width and height.");
            if (f.PixelsGrayU8 == null || f.PixelsGrayU8.Length != h * w)
                throw new InvalidOperationException($"Frame {f.FileName}: buffer size must be H×W ({h}×{w}).");
        }

        IntPtr handle = NanoMeas_Create();
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException("NanoMeas_Create returned NULL.");

        var list = new List<NanoMeasFrameTrackResult>(frames.Count);

        try
        {
            var ip = new NanoMeasIpDFTConfigNative { win_order = winOrder };
            int ini = NanoMeas_Initialize(handle, h, w, ref ip);
            if (ini != NanoMeasOk)
            {
                throw new InvalidOperationException(
                    $"NanoMeas_Initialize failed ({ini}): {MarshalUtf8String(NanoMeas_GetLastError(handle))}");
            }

            int sh = NanoMeas_SetHmfData(handle, seqX, seqX.Length, seqY, seqY.Length);
            if (sh != NanoMeasOk)
            {
                throw new InvalidOperationException(
                    $"NanoMeas_SetHmfData failed ({sh}): {MarshalUtf8String(NanoMeas_GetLastError(handle))}");
            }

            var pose = new double[6];
            var poseAbs = new double[6];
            foreach (var fr in frames)
            {
                var res = CreatePipelineResult();
                int st = NanoMeas_ProcessFrame_Track_U8(
                    handle,
                    fr.PixelsGrayU8!,
                    h,
                    w,
                    ref cam,
                    ref grid,
                    IntPtr.Zero,
                    refreshPeriod,
                    residualThreshold,
                    ref res);

                CopyPipelinePoseArrays(ref res, pose, poseAbs);
                string? err = MarshalUtf8String(NanoMeas_GetLastError(handle));
                list.Add(new NanoMeasFrameTrackResult(
                    fr.ManifestIndex,
                    fr.FileName,
                    st,
                    err ?? "",
                    (double[])pose.Clone(),
                    (double[])poseAbs.Clone(),
                    res.decode_success,
                    res.did_full_solve,
                    res.did_ipdft,
                    res.did_decode,
                    res.refine_ms,
                    res.amp_signal,
                    res.amp_dc));
            }
        }
        finally
        {
            NanoMeas_Destroy(handle);
        }

        return list;
    }
}

public sealed class NanoMeasFrameInput
{
    public NanoMeasFrameInput(int manifestIndex, string fileName, byte[] pixelsGrayU8, int height, int width)
    {
        ManifestIndex = manifestIndex;
        FileName = fileName;
        PixelsGrayU8 = pixelsGrayU8;
        Height = height;
        Width = width;
    }

    public int ManifestIndex { get; }
    public string FileName { get; }
    public byte[] PixelsGrayU8 { get; }
    public int Height { get; }
    public int Width { get; }
}

public sealed record NanoMeasFrameTrackResult(
    int ManifestIndex,
    string FileName,
    int Status,
    string LastError,
    double[] Pose,
    double[] PoseAbs,
    int DecodeSuccess,
    int DidFullSolve,
    int DidIpDft,
    int DidDecode,
    double RefineMs,
    double AmpSignal,
    double AmpDc);
