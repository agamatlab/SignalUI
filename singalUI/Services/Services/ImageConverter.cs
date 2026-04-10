using System;
using Avalonia;
using Avalonia.Media.Imaging;
using OpenCvSharp;

namespace singalUI.Services
{
    /// <summary>
    /// Helper class to convert between OpenCV Mat and Avalonia Bitmap
    /// </summary>
    public static class ImageConverter
    {
        /// <summary>
        /// Convert OpenCV Mat to Avalonia WriteableBitmap
        /// </summary>
        public static WriteableBitmap? MatToBitmap(Mat mat)
        {
            if (mat == null || mat.Empty()) return null;

            try
            {
                // Ensure Mat is in BGR format (what Avalonia expects)
                Mat convertedMat = mat;
                if (mat.Type() == MatType.CV_8UC1)
                {
                    // Grayscale to BGR
                    convertedMat = new Mat();
                    Cv2.CvtColor(mat, convertedMat, ColorConversionCodes.GRAY2BGR);
                }

                int width = convertedMat.Cols;
                int height = convertedMat.Rows;
                int channels = convertedMat.Channels();

                // Get pixel data
                var pixelData = new byte[width * height * channels];
                unsafe
                {
                    var ptr = (byte*)convertedMat.DataPointer;
                    for (int i = 0; i < pixelData.Length; i++)
                    {
                        pixelData[i] = ptr[i];
                    }
                }

                // Create bitmap - use Bgra8888 format with 4 bytes per pixel
                var bitmap = new WriteableBitmap(
                    new PixelSize(width, height),
                    new Vector(96, 96),
                    Avalonia.Platform.PixelFormat.Bgra8888);

                using (var buffer = bitmap.Lock())
                {
                    unsafe
                    {
                        var dstPtr = (byte*)buffer.Address.ToPointer();
                        int srcIdx = 0;
                        for (int i = 0; i < pixelData.Length; i += 3)
                        {
                            // Convert BGR to BGRA
                            dstPtr[srcIdx++] = pixelData[i];     // B
                            dstPtr[srcIdx++] = pixelData[i + 1]; // G
                            dstPtr[srcIdx++] = pixelData[i + 2]; // R
                            dstPtr[srcIdx++] = 255;              // A
                        }
                    }
                }

                // Cleanup temporary mat if we created it
                if (convertedMat != mat && !convertedMat.IsDisposed)
                {
                    convertedMat.Dispose();
                }

                return bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error converting Mat to Bitmap: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Convert Avalonia Bitmap to OpenCV Mat
        /// </summary>
        public static Mat? BitmapToMat(WriteableBitmap bitmap)
        {
            if (bitmap == null) return null;

            try
            {
                using var buffer = bitmap.Lock();
                int width = bitmap.PixelSize.Width;
                int height = bitmap.PixelSize.Height;

                var mat = new Mat(height, width, MatType.CV_8UC3);

                unsafe
                {
                    var srcPtr = (byte*)buffer.Address.ToPointer();
                    var dstPtr = (byte*)mat.DataPointer;

                    // Copy pixel data (assume BGRA format, convert to BGR)
                    int srcIdx = 0;
                    for (int i = 0; i < width * height; i++)
                    {
                        dstPtr[i * 3] = srcPtr[srcIdx++];     // B
                        dstPtr[i * 3 + 1] = srcPtr[srcIdx++]; // G
                        dstPtr[i * 3 + 2] = srcPtr[srcIdx++]; // R
                        srcIdx++; // Skip Alpha
                    }
                }

                return mat;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error converting Bitmap to Mat: {ex.Message}");
                return null;
            }
        }
    }
}
