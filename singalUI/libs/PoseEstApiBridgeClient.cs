using System;
using System.Runtime.InteropServices;

namespace singalUI.libs
{
    public static class PoseEstApiBridgeClient
    {
        private const string DllName = "PoseEstApiBridge.dll";

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

        [DllImport(DllName, EntryPoint = "PoseEstApiWithHmfPaths", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern int PoseEstApiWithHmfPaths(
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
            string hmfGPath,
            string hmfXPath,
            string hmfYPath,
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

        public static PoseEstimateResult EstimatePoseFromImagePathWithHmfFiles(
            string imagePath,
            double[] intrinsicMatrix,
            double pixelSize,
            double focalLength,
            double pitchX,
            double pitchY,
            double componentsX,
            double componentsY,
            double windowSize,
            double codePitchBlocks,
            string hmfGPath,
            string hmfXPath,
            string hmfYPath)
        {
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                throw new ArgumentException("Image path is required.", nameof(imagePath));
            }

            if (intrinsicMatrix == null || intrinsicMatrix.Length != 9)
            {
                throw new ArgumentException("Intrinsic matrix must contain exactly 9 values.", nameof(intrinsicMatrix));
            }

            if (string.IsNullOrWhiteSpace(hmfGPath) || string.IsNullOrWhiteSpace(hmfXPath) || string.IsNullOrWhiteSpace(hmfYPath))
            {
                throw new ArgumentException("HMF file paths are required.");
            }

            var pose = new double[6];
            var ok = PoseEstApiWithHmfPaths(
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
                hmfGPath,
                hmfXPath,
                hmfYPath,
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
