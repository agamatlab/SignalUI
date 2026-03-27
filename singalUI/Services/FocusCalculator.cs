using OpenCvSharp;
using System;

namespace singalUI.Services
{
    /// <summary>
    /// Provides focus quality measurement using FFT (Fast Fourier Transform)
    /// Analyzes high-frequency content to determine image sharpness
    /// </summary>
    public static class FocusCalculator
    {
        /// <summary>
        /// Calculate focus quality using FFT analysis
        /// Sharp images have more high-frequency content
        /// </summary>
        /// <param name="grayBuffer">Grayscale image buffer (8-bit)</param>
        /// <param name="width">Image width in pixels</param>
        /// <param name="height">Image height in pixels</param>
        /// <param name="downsampleFactor">Downsample factor for performance (1=full, 2=1/4, 4=1/16)</param>
        /// <returns>Focus score 0-100 (0=blurry, 100=sharp)</returns>
        public static double CalculateFFTFocus(
            byte[] grayBuffer, 
            int width, 
            int height, 
            int downsampleFactor = 4)
        {
            if (grayBuffer == null || width <= 0 || height <= 0)
            {
                return 0.0;
            }

            try
            {
                // Create Mat from grayscale buffer
                using var mat = new Mat(height, width, MatType.CV_8UC1, grayBuffer);
                
                // Downsample for performance (4x = 1/16 resolution)
                using var small = new Mat();
                if (downsampleFactor > 1)
                {
                    int newW = width / downsampleFactor;
                    int newH = height / downsampleFactor;
                    Cv2.Resize(mat, small, new Size(newW, newH), interpolation: InterpolationFlags.Area);
                }
                else
                {
                    mat.CopyTo(small);
                }

                // Convert to float32 for FFT
                using var floatMat = new Mat();
                small.ConvertTo(floatMat, MatType.CV_32F);

                // Perform 2D FFT
                using var fftResult = new Mat();
                Cv2.Dft(floatMat, fftResult, DftFlags.ComplexOutput);

                // Split into real and imaginary parts
                Mat[] planes = Cv2.Split(fftResult);
                
                // Calculate magnitude spectrum
                using var magnitude = new Mat();
                Cv2.Magnitude(planes[0], planes[1], magnitude);

                // Calculate high-frequency energy (outer 50% of spectrum)
                int centerX = magnitude.Width / 2;
                int centerY = magnitude.Height / 2;
                int innerRadius = Math.Min(centerX, centerY) / 2;

                double highFreqEnergy = 0.0;
                int count = 0;

                // Sum energy in high-frequency regions (outside inner circle)
                for (int y = 0; y < magnitude.Height; y++)
                {
                    for (int x = 0; x < magnitude.Width; x++)
                    {
                        int dx = x - centerX;
                        int dy = y - centerY;
                        double dist = Math.Sqrt(dx * dx + dy * dy);

                        // Only count high-frequency components (outer region)
                        if (dist > innerRadius)
                        {
                            highFreqEnergy += magnitude.At<float>(y, x);
                            count++;
                        }
                    }
                }

                // Cleanup Mat arrays
                foreach (var plane in planes)
                {
                    plane.Dispose();
                }

                // Calculate average high-frequency energy
                double avgEnergy = count > 0 ? highFreqEnergy / count : 0.0;

                // Normalize to 0-100 scale
                return NormalizeFFTScore(avgEnergy);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FocusCalculator] FFT error: {ex.Message}");
                return 0.0;
            }
        }

        /// <summary>
        /// Normalize raw FFT energy to 0-100 scale
        /// Uses logarithmic scaling for better discrimination at high focus levels
        /// </summary>
        /// <param name="rawEnergy">Raw FFT high-frequency energy</param>
        /// <returns>Normalized focus score 0-100</returns>
        private static double NormalizeFFTScore(double rawEnergy)
        {
            // Typical range for raw FFT energy: 0-2000
            // Use logarithmic scaling for better discrimination
            // log10(1) = 0, log10(10) = 1, log10(100) = 2, log10(1000) = 3
            
            // Add 1 to avoid log(0)
            double logEnergy = Math.Log10(rawEnergy + 1);
            
            // Scale to 0-100 range
            // log10(2000) ≈ 3.3, so multiply by ~30 to get 0-100 range
            double normalized = logEnergy * 30.0;
            
            // Clamp to 0-100
            return Math.Clamp(normalized, 0.0, 100.0);
        }
    }
}
