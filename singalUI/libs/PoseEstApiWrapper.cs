using System;
using System.Runtime.InteropServices;
using System.Linq;

namespace singalUI.libs
{
    /// <summary>
    /// C# Wrapper for PoseEstApi - 6DoF Pose Estimation Library
    /// </summary>
    public class PoseEstimator : IDisposable
    {
        private const string DLL_NAME = "PoseEstApiWrapper.dll";

        #region P/Invoke Declarations
        private readonly string _errorLogFile = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "pose_estimation_errors.log");
        
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern int PoseEst_Initialize();

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern void PoseEst_Terminate();

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern IntPtr PoseEst_CreateImage2D(int rows, int cols);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern IntPtr PoseEst_CreateArray2D(int rows, int cols);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern void PoseEst_FreeArray(IntPtr handle);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern void PoseEst_SetImageData(IntPtr handle, double[] data, int size);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern void PoseEst_SetArrayData(IntPtr handle, double[] data, int size);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern int PoseEst_Estimate(
            IntPtr image,
            int imageRows, int imageCols,
            double[] intrinsic,
            double pixelSize,
            double focalLen,
            IntPtr hmfG,
            int hmfGRows, int hmfGCols,
            double[] hmfXData, int hmfXSize,
            double[] hmfYData, int hmfYSize,
            double pitchX, double pitchY,
            double compX, double compY,
            double windowSize,
            double codePitchBlocks,
            double[] poseResult,
            out int poseResultSize
        );

        #endregion

        private bool _initialized = false;
        private bool _disposed = false;
        public string ErrorLog = string.Empty;

        /// <summary>
        /// Initialize the PoseEstApi library
        /// </summary>
        public void Initialize()
        {
            Console.WriteLine("Position Estimator Api was Initialized.");
            if (_initialized) return;

            int result = PoseEst_Initialize();
            if (result == 0)
            {
                throw new InvalidOperationException("Failed to initialize PoseEstApi library");
            }
            _initialized = true;
        }

        /// <summary>
        /// Create a 2D array for image or HMF data
        /// </summary>
        public NativeArray CreateArray2D(int rows, int cols)
        {
            IntPtr handle = PoseEst_CreateArray2D(rows, cols);
            if (handle == IntPtr.Zero)
            {
                throw new OutOfMemoryException("Failed to create array");
            }
            return new NativeArray(handle, rows * cols);
        }

        /// <summary>
        /// Create an image array
        /// </summary>
        public NativeArray CreateImage(int rows, int cols)
        {
            IntPtr handle = PoseEst_CreateImage2D(rows, cols);
            if (handle == IntPtr.Zero)
            {
                throw new OutOfMemoryException("Failed to create image array");
            }
            return new NativeArray(handle, rows * cols);
        }

        private void LogToError(string message)
        {
            try
            {
                string logMessage = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
                ErrorLog = logMessage + "\n" + ErrorLog;

                // Also write to file
                System.IO.File.AppendAllText(_errorLogFile, logMessage + "\n");
            }
            catch
            {
                // Ignore logging errors
            }
        }
        /// <summary>
        /// Estimate 6DoF pose from image
        /// </summary>
        public PoseEstimateResult EstimatePose(
            double[] imageData, int imageRows, int imageCols,
            double[] intrinsicMatrix,
            double pixelSize, double focalLength,
            double[] hmfGData, int hmfGRows, int hmfGCols,
            double[] hmfXData, double[] hmfYData,
            double pitchX, double pitchY,
            double componentsX, double componentsY,
            double windowSize, double codePitchBlocks
        )
        {
            LogToError("Here0");
            if (!_initialized)
            {
                throw new InvalidOperationException("Library not initialized. Call Initialize() first.");
            }
            LogToError("Here1");

            int expectedImageSize = imageRows * imageCols;
            int expectedHmfSize = hmfGRows * hmfGCols;

            LogToError(
                $"[EstimatePose] InputSummary " +
                $"image={imageRows}x{imageCols} len={imageData?.Length ?? -1} expected={expectedImageSize}; " +
                $"intrinsicLen={intrinsicMatrix?.Length ?? -1}; " +
                $"hmfG={hmfGRows}x{hmfGCols} len={hmfGData?.Length ?? -1} expected={expectedHmfSize}; " +
                $"hmfX len={hmfXData?.Length ?? -1}; hmfY len={hmfYData?.Length ?? -1}; " +
                $"pixelSize={pixelSize}; focal={focalLength}; pitch=({pitchX},{pitchY}); " +
                $"components=({componentsX},{componentsY}); window={windowSize}; codePitchBlocks={codePitchBlocks}"
            );

            if (imageRows <= 0 || imageCols <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(imageRows),
                    $"Image dimensions must be > 0. Got {imageRows}x{imageCols}."
                );
            }
            if (hmfGRows <= 0 || hmfGCols <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(hmfGRows),
                    $"HMF dimensions must be > 0. Got {hmfGRows}x{hmfGCols}."
                );
            }
            if (imageData == null || imageData.Length != expectedImageSize)
            {
                throw new ArgumentException(
                    $"imageData length mismatch. Expected {expectedImageSize}, got {imageData?.Length ?? -1}.",
                    nameof(imageData)
                );
            }
            if (intrinsicMatrix == null || intrinsicMatrix.Length != 9)
            {
                throw new ArgumentException(
                    $"intrinsicMatrix must have 9 values. Got {intrinsicMatrix?.Length ?? -1}.",
                    nameof(intrinsicMatrix)
                );
            }
            if (hmfGData == null || hmfGData.Length != expectedHmfSize)
            {
                throw new ArgumentException(
                    $"hmfGData length mismatch. Expected {expectedHmfSize}, got {hmfGData?.Length ?? -1}.",
                    nameof(hmfGData)
                );
            }
            if (hmfXData == null || hmfXData.Length == 0)
            {
                throw new ArgumentException(
                    $"hmfXData must contain at least one value. Got {hmfXData?.Length ?? -1}.",
                    nameof(hmfXData)
                );
            }
            if (hmfYData == null || hmfYData.Length == 0)
            {
                throw new ArgumentException(
                    $"hmfYData must contain at least one value. Got {hmfYData?.Length ?? -1}.",
                    nameof(hmfYData)
                );
            }
            if (pixelSize <= 0 || focalLength <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(pixelSize),
                    $"pixelSize and focalLength must be > 0. Got pixelSize={pixelSize}, focalLength={focalLength}."
                );
            }
            if (imageData.Any(v => double.IsNaN(v) || double.IsInfinity(v)) ||
                intrinsicMatrix.Any(v => double.IsNaN(v) || double.IsInfinity(v)) ||
                hmfGData.Any(v => double.IsNaN(v) || double.IsInfinity(v)) ||
                hmfXData.Any(v => double.IsNaN(v) || double.IsInfinity(v)) ||
                hmfYData.Any(v => double.IsNaN(v) || double.IsInfinity(v)))
            {
                throw new ArgumentException("Input arrays contain NaN or Infinity values.");
            }

            // Create native arrays
            using (var image = CreateImage(imageRows, imageCols))
            using (var hmfG = CreateArray2D(hmfGRows, hmfGCols))
            {
                // Copy data to native arrays
                PoseEst_SetImageData(image.Handle, imageData, imageData.Length);
                PoseEst_SetArrayData(hmfG.Handle, hmfGData, hmfGData.Length);
            LogToError("Here2");

                // Prepare output
                double[] poseResult = new double[6];
                int poseResultSize;
            LogToError("Here3");

                // Call pose estimation
                int status;
                try
                {
                    status = PoseEst_Estimate(
                        image.Handle, imageRows, imageCols,
                        intrinsicMatrix,
                        pixelSize, focalLength,
                        hmfG.Handle, hmfGRows, hmfGCols,
                        hmfXData, hmfXData.Length,
                        hmfYData, hmfYData.Length,
                        pitchX, pitchY,
                        componentsX, componentsY,
                        windowSize, codePitchBlocks,
                        poseResult, out poseResultSize
                    );
                }
                catch (Exception ex)
                {
                    LogToError($"[EstimatePose] Exception in native call: {ex.GetType().Name}: {ex.Message}");
                    throw;
                }
            LogToError("Here4");
                LogToError($"[EstimatePose] Native return status={status}, poseResultSize={poseResultSize}");

                return new PoseEstimateResult(status, poseResult);
            }
        }

        /// <summary>
        /// Terminate the library and cleanup
        /// </summary>
        public void Shutdown()
        {
            if (_initialized)
            {
                PoseEst_Terminate();
                _initialized = false;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    Shutdown();
                }
                _disposed = true;
            }
        }

        ~PoseEstimator()
        {
            Dispose(false);
        }
    }

    /// <summary>
    /// Wrapper for native array handles
    /// </summary>
    public class NativeArray : IDisposable
    {
        [DllImport("PoseEstApiWrapper.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void PoseEst_FreeArray(IntPtr handle);

        public IntPtr Handle { get; private set; }
        public int Size { get; private set; }
        private bool _disposed;

        public NativeArray(IntPtr handle, int size)
        {
            Handle = handle;
            Size = size;
        }

        public void Dispose()
        {
            if (!_disposed && Handle != IntPtr.Zero)
            {
                PoseEst_FreeArray(Handle);
                Handle = IntPtr.Zero;
                _disposed = true;
            }
        }

        ~NativeArray()
        {
            Dispose();
        }
    }

    /// <summary>
    /// Result of pose estimation
    /// </summary>
    public class PoseEstimateResult
    {
        public PoseStatus Status { get; private set; }
        public string StatusMessage { get; private set; }
        public double[] PoseData { get; private set; }

        public PoseEstimateResult(int statusCode, double[] poseData)
        {
            PoseData = poseData ?? new double[6];
            Status = (PoseStatus)statusCode;
            StatusMessage = GetStatusMessage(statusCode);
        }

        private string GetStatusMessage(int status)
        {
            return status switch
            {
                1 => "Success",
                -1 => "Coarse Fail (IpDFT stage failed)",
                -3 => "Decode Fail (HMF decoding failed, relative pose fallback)",
                _ => $"Unknown status: {status}"
            };
        }

        /// <summary>
        /// Rotation around X axis (radians)
        /// </summary>
        public double Rx => PoseData[0];

        /// <summary>
        /// Rotation around Y axis (radians)
        /// </summary>
        public double Ry => PoseData[1];

        /// <summary>
        /// Rotation around Z axis (radians)
        /// </summary>
        public double Rz => PoseData[2];

        /// <summary>
        /// Translation X (mm)
        /// </summary>
        public double Tx => PoseData[3];

        /// <summary>
        /// Translation Y (mm)
        /// </summary>
        public double Ty => PoseData[4];

        /// <summary>
        /// Translation Z (mm)
        /// </summary>
        public double Tz => PoseData[5];

        public override string ToString()
        {
            if (Status != PoseStatus.Success)
            {
                return $"Status: {StatusMessage}";
            }

            return $"Status: {StatusMessage}\n" +
                   $"Rotation (rad): Rx={Rx:F6}, Ry={Ry:F6}, Rz={Rz:F6}\n" +
                   $"Rotation (deg): Rx={Rx * 180 / Math.PI:F2}, Ry={Ry * 180 / Math.PI:F2}, Rz={Rz * 180 / Math.PI:F2}\n" +
                   $"Translation (mm): Tx={Tx:F2}, Ty={Ty:F2}, Tz={Tz:F2}";
        }
    }

    /// <summary>
    /// Pose estimation status codes
    /// </summary>
    public enum PoseStatus
    {
        Success = 1,
        CoarseFail = -1,
        DecodeFail = -3
    }

    /// <summary>
    /// Camera intrinsic matrix
    /// </summary>
    public class CameraIntrinsics
    {
        public double Fx { get; set; }
        public double Fy { get; set; }
        public double Cx { get; set; }
        public double Cy { get; set; }

        /// <summary>
        /// Convert to 3x3 row-major array
        /// </summary>
        public double[] ToArray()
        {
            return new double[] { Fx, 0, Cx, 0, Fy, Cy, 0, 0, 1 };
        }

        public static CameraIntrinsics Default => new CameraIntrinsics
        {
            Fx = 500.0,
            Fy = 500.0,
            Cx = 320.0,
            Cy = 240.0
        };
    }

    /// <summary>
    /// Algorithm parameters
    /// </summary>
    public class AlgorithmParams
    {
        public double PitchX { get; set; } = 1.2;
        public double PitchY { get; set; } = 1.2;
        public double ComponentsX { get; set; } = 11.0;
        public double ComponentsY { get; set; } = 11.0;
        public double WindowSize { get; set; } = 5.0;
        public double CodePitchBlocks { get; set; } = 4.0;
    }
}
