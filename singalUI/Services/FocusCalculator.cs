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
        /// <returns>Tuple of (normalized focus score 0-100, raw average energy)</returns>
        public static (double normalized, double rawEnergy) CalculateFFTFocus(
            byte[] grayBuffer, 
            int width, 
            int height, 
            int downsampleFactor = 4)
        {
            if (grayBuffer == null || width <= 0 || height <= 0)
            {
                Console.WriteLine($"[FocusCalculator] Invalid input: buffer={grayBuffer?.Length ?? 0}, width={width}, height={height}");
                return (0.0, 0.0);
            }

            if (grayBuffer.Length < width * height)
            {
                Console.WriteLine($"[FocusCalculator] Buffer too small: {grayBuffer.Length} < {width * height}");
                return (0.0, 0.0);
            }

            try
            {
                // Create Mat from grayscale buffer
                using var mat = new Mat(height, width, MatType.CV_8UC1, grayBuffer);
                
                // Downsample for performance (2x = 1/4 resolution for better accuracy)
                using var small = new Mat();
                int newW = width / 2;
                int newH = height / 2;
                Cv2.Resize(mat, small, new Size(newW, newH), interpolation: InterpolationFlags.Area);

                // Extract center region (60% of image) for more accurate focus measurement
                int roiWidth = (int)(small.Width * 0.6);
                int roiHeight = (int)(small.Height * 0.6);
                int roiX = (small.Width - roiWidth) / 2;
                int roiY = (small.Height - roiHeight) / 2;
                using var centerROI = new Mat(small, new Rect(roiX, roiY, roiWidth, roiHeight));

                // Method 1: Laplacian variance (edge sharpness)
                using var laplacian = new Mat();
                Cv2.Laplacian(centerROI, laplacian, MatType.CV_64F, ksize: 3);
                Cv2.MeanStdDev(laplacian, out var meanLap, out var stddevLap);
                double varianceLap = stddevLap.Val0 * stddevLap.Val0;
                
                // Method 2: Sobel gradient magnitude (directional edges)
                using var sobelX = new Mat();
                using var sobelY = new Mat();
                Cv2.Sobel(centerROI, sobelX, MatType.CV_64F, 1, 0, ksize: 3);
                Cv2.Sobel(centerROI, sobelY, MatType.CV_64F, 0, 1, ksize: 3);
                
                using var gradMag = new Mat();
                Cv2.Magnitude(sobelX, sobelY, gradMag);
                Cv2.MeanStdDev(gradMag, out var meanGrad, out var stddevGrad);
                double avgGradient = meanGrad.Val0;
                
                // Method 3: High-frequency content via FFT
                using var floatROI = new Mat();
                centerROI.ConvertTo(floatROI, MatType.CV_32F);
                using var fftResult = new Mat();
                Cv2.Dft(floatROI, fftResult, DftFlags.ComplexOutput);
                
                Mat[] planes = Cv2.Split(fftResult);
                using var magnitude = new Mat();
                Cv2.Magnitude(planes[0], planes[1], magnitude);
                
                // Calculate high-frequency energy (outer 40% of spectrum)
                int centerX = magnitude.Width / 2;
                int centerY = magnitude.Height / 2;
                int innerRadius = (int)(Math.Min(centerX, centerY) * 0.6);
                
                double highFreqEnergy = 0.0;
                int count = 0;
                
                for (int y = 0; y < magnitude.Height; y++)
                {
                    for (int x = 0; x < magnitude.Width; x++)
                    {
                        int dx = x - centerX;
                        int dy = y - centerY;
                        double dist = Math.Sqrt(dx * dx + dy * dy);
                        
                        if (dist > innerRadius)
                        {
                            highFreqEnergy += magnitude.At<float>(y, x);
                            count++;
                        }
                    }
                }
                
                foreach (var plane in planes)
                {
                    plane.Dispose();
                }
                
                double avgHighFreq = count > 0 ? highFreqEnergy / count : 0.0;
                
                // Combine all three metrics with weights
                // Laplacian: 40%, Gradient: 30%, FFT: 30%
                double combinedScore = 
                    NormalizeLaplacianScore(varianceLap) * 0.4 +
                    NormalizeGradientScore(avgGradient) * 0.3 +
                    NormalizeFFTScore(avgHighFreq) * 0.3;
                
                // Use Laplacian variance as the "raw" value for display
                double rawValue = varianceLap;
                
                Console.WriteLine($"[FocusCalculator] Lap={varianceLap:F0}, Grad={avgGradient:F1}, FFT={avgHighFreq:F1} → Combined={combinedScore:F1}%");
                
                return (combinedScore, rawValue);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FocusCalculator] Focus calculation error: {ex.Message}");
                return (0.0, 0.0);
            }
        }

        /// <summary>
        /// Normalize Laplacian variance to 0-100 scale
        /// </summary>
        private static double NormalizeLaplacianScore(double variance)
        {
            // Calibrated for typical camera variance: 10000-150000
            if (variance < 1000.0)
            {
                return 0.0;
            }
            
            double logVariance = Math.Log10(variance);
            // Map log(10000)=4 to log(150000)=5.18 → 0 to 100
            double normalized = (logVariance - 4.0) / 1.2 * 100.0;
            
            return Math.Clamp(normalized, 0.0, 100.0);
        }
        
        /// <summary>
        /// Normalize gradient magnitude to 0-100 scale
        /// </summary>
        private static double NormalizeGradientScore(double avgGradient)
        {
            // Typical gradient range: 5-50
            double normalized = (avgGradient - 5.0) / 45.0 * 100.0;
            return Math.Clamp(normalized, 0.0, 100.0);
        }
        
        /// <summary>
        /// Normalize FFT high-frequency energy to 0-100 scale
        /// </summary>
        private static double NormalizeFFTScore(double avgEnergy)
        {
            // Typical FFT energy range: 10-1000
            if (avgEnergy < 10.0)
            {
                return 0.0;
            }
            
            double logEnergy = Math.Log10(avgEnergy);
            // Map log(10)=1 to log(1000)=3 → 0 to 100
            double normalized = (logEnergy - 1.0) / 2.0 * 100.0;
            
            return Math.Clamp(normalized, 0.0, 100.0);
        }
    }
}
