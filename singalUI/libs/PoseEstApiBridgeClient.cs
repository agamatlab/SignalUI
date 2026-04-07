using System;
using System.IO;
using System.Runtime.InteropServices;

namespace singalUI.libs
{
    public static class PoseEstApiBridgeClient
    {
        private const string DllName = "PoseEstApiBridge.dll";

        /// <summary>
        /// Active HMF bundle is staged under <c>Archive\hmf_temp_{G,X,Y}.txt</c> (overwritten each call).
        /// Source files are <c>Archive\{N}+{N}_{G,X,Y}.txt</c> with <c>N = round(windowSize)</c>.
        /// Components X/Y are separate API parameters and do not select these filenames.
        /// This PoseEstApiBridge build still reads <c>Archive\12+12_{G,X,Y}.txt</c>, so those paths are
        /// refreshed from <c>hmf_temp_*</c> after staging.
        /// </summary>
        private const string HmfTempStem = "hmf_temp";

        private const string BridgeDllHardcodedStem = "12+12";

        private static readonly object ArchiveStagingLock = new();

        public static void SyncHmfArchiveFilesForPoseBridge(double windowSize, string? applicationBaseDirectory = null)
        {
            string baseDir = applicationBaseDirectory ?? AppContext.BaseDirectory;
            int n = (int)Math.Round(windowSize);
            if (n <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(windowSize), windowSize, "windowSize must be positive.");
            }

            string stem = $"{n}+{n}";
            string archive = Path.Combine(baseDir, "Archive");
            lock (ArchiveStagingLock)
            {
                foreach (string suffix in new[] { "G", "X", "Y" })
                {
                    string src = Path.Combine(archive, $"{stem}_{suffix}.txt");
                    if (!File.Exists(src))
                    {
                        throw new FileNotFoundException(
                            $"HMF archive for windowSize={windowSize} (bundle '{stem}') not found.",
                            src);
                    }

                    string tempPath = Path.Combine(archive, $"{HmfTempStem}_{suffix}.txt");
                    string dllPath = Path.Combine(archive, $"{BridgeDllHardcodedStem}_{suffix}.txt");
                    File.Copy(src, tempPath, overwrite: true);
                    File.Copy(tempPath, dllPath, overwrite: true);
                }
            }
        }

        [DllImport(DllName, EntryPoint = "PoseEstApi", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern int PoseEstApi(
            string imagePath,
            [In] double[] intrinsic,
            double pixelSize,
            double focalLen,
            double pitchX,
            double pitchY,
            double compX,
            double compY,
            double windowSize,
            double codePitchBlocks,
            [Out] double[] poseResult,
            out int poseResultSize,
            out int status);

        public static PoseEstimateResult EstimatePoseFromImagePath(
            string imagePath,
            double[] intrinsicMatrix,
            double pixelSize,
            double focalLength,
            double pitchX,
            double pitchY,
            double componentsX,
            double componentsY,
            double windowSize,
            double codePitchBlocks)
        {
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                throw new ArgumentException("Image path is required.", nameof(imagePath));
            }

            if (intrinsicMatrix == null || intrinsicMatrix.Length != 9)
            {
                throw new ArgumentException("Intrinsic matrix must contain exactly 9 values.", nameof(intrinsicMatrix));
            }

            SyncHmfArchiveFilesForPoseBridge(windowSize);

            var pose = new double[6];
            var ok = PoseEstApi(
                imagePath,
                intrinsicMatrix,
                pixelSize,
                focalLength,
                pitchX,
                pitchY,
                componentsX,
                componentsY,
                windowSize,
                codePitchBlocks,
                pose,
                out var poseSize,
                out var status);

            if (ok == 0)
            {
                throw new InvalidOperationException("PoseEstApiBridge call failed.");
            }

            return new PoseEstimateResult(status, pose);
        }
    }
}
