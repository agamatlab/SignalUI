using System.Globalization;

/// <summary>
/// Standalone testbench: same sequence as hsc103.py main() — open, validate HSC-103, home,
/// move to absolute angle, hold, L: stop, close. Proves Hsc103RotationStageController COM logic.
/// </summary>
internal static class Program
{
    private const double DefaultDegreesPerPulse = 0.00001;

    private static int Main(string[] args)
    {
        var opt = ParseArgs(args);
        if (opt.ShowHelp)
        {
            PrintHelp();
            return 0;
        }

        Console.WriteLine($"OPTOSIGMA >> C# testbench — port={opt.Port}, axis={opt.Axis}, dpp={opt.DegreesPerPulse:G9}, angle={opt.Angle}°, hold={opt.HoldTime}s");

        var stage = new Hsc103RotationStageController(opt.Port, opt.Axis, opt.DegreesPerPulse);
        try
        {
            try
            {
                stage.Connect();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"OPTOSIGMA >> Could not connect on {opt.Port}: {ex.Message}");
                Console.Error.WriteLine("OPTOSIGMA >> Check Device Manager for the correct COM port, or use --port COMx.");
                return 1;
            }

            if (!stage.CheckConnected())
            {
                Console.Error.WriteLine("OPTOSIGMA >> Failed to open or verify connection.");
                return 1;
            }

            Console.WriteLine($"OPTOSIGMA >> Starting rotation stage routine on axis {opt.Axis}...");

            Console.WriteLine("OPTOSIGMA >> Step 1: Homing rotation stage to 0 degrees...");
            stage.ReferenceAxes();
            Thread.Sleep(500);

            Console.WriteLine($"OPTOSIGMA >> Step 2: Moving rotation stage to {opt.Angle} degrees...");
            try
            {
                stage.MoveAbsoluteDegrees(opt.Angle);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"OPTOSIGMA >> Failed to move rotation axis to target angle: {ex.Message}");
                return 1;
            }

            Console.WriteLine($"OPTOSIGMA >> Step 3: Holding position for {opt.HoldTime} seconds...");
            Thread.Sleep(TimeSpan.FromSeconds(opt.HoldTime));

            Console.WriteLine("OPTOSIGMA >> Step 4: Explicitly issuing stop command to the stage...");
            stage.StopAxis();

            try
            {
                double pos = stage.GetPosition(0);
                Console.WriteLine($"OPTOSIGMA >> Position readback after stop: {pos:F6}°");
            }
            catch
            {
                /* optional */
            }

            Console.WriteLine("OPTOSIGMA >> Routine completed successfully.");
            return 0;
        }
        finally
        {
            try
            {
                stage.Disconnect();
            }
            catch
            {
                /* ignore */
            }
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
            """
            Hsc103Testbench — OptoSigma HSC-103 rotation (same flow as hsc103.py main())

            Options:
              --port COM3              Serial port (default COM3)
              --axis 1|2|3             HSC-103 axis index (default 1)
              --degrees-per-pulse D    Default 1e-5
              --angle DEG              Absolute angle after home (default 10)
              --hold-time SEC          Hold at angle before L: stop (default 10)
              -h, --help               This text

            Example:
              Hsc103Testbench --port COM5 --axis 1 --angle 15 --hold-time 5
            """);
    }

    private sealed class Options
    {
        public string Port { get; set; } = "COM3";
        public int Axis { get; set; } = 1;
        public double DegreesPerPulse { get; set; } = DefaultDegreesPerPulse;
        public double Angle { get; set; } = 10;
        public double HoldTime { get; set; } = 10;
        public bool ShowHelp { get; set; }
    }

    private static Options ParseArgs(string[] args)
    {
        var o = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            if (a is "-h" or "--help")
            {
                o.ShowHelp = true;
                return o;
            }

            string? Next() => i + 1 < args.Length ? args[++i] : null;

            if (a == "--port")
            {
                var v = Next();
                if (v != null) o.Port = v;
            }
            else if (a == "--axis")
            {
                var v = Next();
                if (v != null && int.TryParse(v, out int ax) && ax is >= 1 and <= 3)
                    o.Axis = ax;
            }
            else if (a == "--degrees-per-pulse")
            {
                var v = Next();
                if (v != null && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double dpp) && dpp > 0)
                    o.DegreesPerPulse = dpp;
            }
            else if (a == "--angle")
            {
                var v = Next();
                if (v != null && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double ang))
                    o.Angle = ang;
            }
            else if (a == "--hold-time")
            {
                var v = Next();
                if (v != null && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double ht) && ht >= 0)
                    o.HoldTime = ht;
            }
        }

        return o;
    }
}
