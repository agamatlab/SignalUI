using System.Runtime.InteropServices;

internal static class Native
{
    private const string DLL = "PoseEstApiWrapper.dll";

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern int PoseEst_Initialize();

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern void PoseEst_Terminate();

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern IntPtr PoseEst_CreateImage2D(int rows, int cols);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern IntPtr PoseEst_CreateArray2D(int rows, int cols);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern void PoseEst_FreeArray(IntPtr handle);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern void PoseEst_SetImageData(IntPtr handle, double[] data, int size);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern void PoseEst_SetArrayData(IntPtr handle, double[] data, int size);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern int PoseEst_Estimate(
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
}

internal class Program
{
    static int Main(string[] args)
    {
        var scenario = args.FirstOrDefault() ?? "current";
        Console.WriteLine($"SCENARIO={scenario}");

        int imageCols = 640;
        int imageRows = 480;
        int comp = 11;

        double focal = 16.0;
        double pixel = 0.0055;
        double fx = 500.0;
        double fy = 500.0;

        if (scenario == "consistent")
        {
            fx = focal / pixel;
            fy = focal / pixel;
        }
        else if (scenario == "pixels")
        {
            pixel = 1.0;
            focal = 500.0;
            fx = 500.0;
            fy = 500.0;
        }

        var intrinsic = new[] { fx, 0d, 320d, 0d, fy, 240d, 0d, 0d, 1d };
        var imageData = new double[imageCols * imageRows];
        if (scenario != "zero")
        {
            var scale = scenario == "norm" ? 1.0 : 255.0;
            for (int y = 0; y < imageRows; y++)
            {
                for (int x = 0; x < imageCols; x++)
                {
                    imageData[y * imageCols + x] = (x + y) / (double)(imageCols + imageRows) * scale;
                }
            }
        }

        var hmfG = new double[comp * comp];
        if (scenario != "zero")
        {
            for (int y = 0; y < comp; y++)
            {
                for (int x = 0; x < comp; x++)
                {
                    var freq = Math.Sqrt(x * x + y * y) / comp;
                    hmfG[y * comp + x] = Math.Exp(-freq * freq) * 100.0;
                }
            }
        }

        var hmfX = Enumerable.Range(0, comp).Select(i => i * 1.2).ToArray();
        var hmfY = Enumerable.Range(0, comp).Select(i => i * 1.2).ToArray();

        Console.WriteLine($"INIT: focal={focal}, pixel={pixel}, fx={fx}, fy={fy}");
        int init = Native.PoseEst_Initialize();
        Console.WriteLine($"PoseEst_Initialize => {init}");
        if (init == 0) return 2;

        IntPtr img = IntPtr.Zero;
        IntPtr g = IntPtr.Zero;

        try
        {
            img = Native.PoseEst_CreateImage2D(imageRows, imageCols);
            g = Native.PoseEst_CreateArray2D(comp, comp);
            Console.WriteLine($"handles: img={img}, hmfG={g}");

            Native.PoseEst_SetImageData(img, imageData, imageData.Length);
            Native.PoseEst_SetArrayData(g, hmfG, hmfG.Length);
            Console.WriteLine("data set done");

            var pose = new double[6];
            int poseSize;
            Console.WriteLine("calling PoseEst_Estimate...");
            var status = Native.PoseEst_Estimate(
                img, imageRows, imageCols,
                intrinsic,
                pixel, focal,
                g, comp, comp,
                hmfX, hmfX.Length,
                hmfY, hmfY.Length,
                1.2, 1.2,
                comp, comp,
                5.0, 4.0,
                pose, out poseSize
            );
            Console.WriteLine($"PoseEst_Estimate => status={status}, poseSize={poseSize}, pose=[{string.Join(",", pose.Select(v => v.ToString("F4")))}]");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"EXCEPTION: {ex.GetType().Name}: {ex.Message}");
            return 3;
        }
        finally
        {
            if (g != IntPtr.Zero) Native.PoseEst_FreeArray(g);
            if (img != IntPtr.Zero) Native.PoseEst_FreeArray(img);
            Native.PoseEst_Terminate();
            Console.WriteLine("terminated");
        }
    }
}
