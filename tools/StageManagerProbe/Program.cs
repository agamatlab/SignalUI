using singalUI.Models;
using singalUI.Services;
using System.IO.Ports;

static double ReadPositionSafe(StageWrapper wrapper, AxisType axis)
{
    try
    {
        return wrapper.GetAxisPosition(axis);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  position read failed: {ex.Message}");
        return double.NaN;
    }
}

Console.WriteLine("=== StageManagerProbe START ===");
Console.WriteLine($"Time: {DateTime.Now:O}");

StageWrapper? wrapper = null;

static (string? port, SKSampleClass.Controller_Type? type) DiscoverSigmakokiPort()
{
    var ports = SerialPort.GetPortNames();
    var types = new[]
    {
        SKSampleClass.Controller_Type.HSC_103,
        SKSampleClass.Controller_Type.GIP_101B,
        SKSampleClass.Controller_Type.GSC01,
        SKSampleClass.Controller_Type.SHOT_302GS,
        SKSampleClass.Controller_Type.SHOT_304GS,
        SKSampleClass.Controller_Type.PGC_04,
        SKSampleClass.Controller_Type.Hit_MV
    };

    Console.WriteLine($"Port discovery: [{string.Join(", ", ports)}]");
    foreach (var port in ports)
    {
        foreach (var type in types)
        {
            try
            {
                var sk = new SKSampleClass
                {
                    PortName = port,
                    Baudrate = 9600,
                    ControllerType = type
                };
                bool ok = sk.ConnectTest();
                Console.WriteLine($"  probe {port} / {type}: {(ok ? "OK" : "fail")}");
                try { sk.PortClose(); } catch { }
                if (ok) return (port, type);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  probe {port} / {type}: error {ex.Message}");
            }
        }
    }

    return (null, null);
}

try
{
    // Use StageManager path (same flow as UI), but force Sigmakoki for this probe.
    StageManager.Wrappers.Clear();
    StageManager.AxisToWrapperMap.Clear();

    wrapper = StageWrapper.Create(1, StageHardwareType.Sigmakoki);
    wrapper.SigmakokiController = SigmakokiControllerType.HSC_103;
    StageManager.Wrappers.Add(wrapper);

    Console.WriteLine($"Wrapper created: id={wrapper.Id}, hardware={wrapper.HardwareType}, sk={wrapper.SigmakokiController}");
    Console.WriteLine("Connecting...");
    bool connected = await wrapper.ConnectAsync();
    if (!connected)
    {
        Console.WriteLine("ConnectAsync failed. Trying COM auto-discovery fallback...");
        var (port, skType) = DiscoverSigmakokiPort();
        if (!string.IsNullOrWhiteSpace(port) && skType.HasValue)
        {
            Console.WriteLine($"Discovered Sigma Koki at {port}, type={skType.Value}");
            var controller = new SigmakokiController(skType.Value);
            controller.SetPort(port);
            controller.Connect();

            wrapper.Controller = controller;
            wrapper.IsConnected = true;
            wrapper.AvailableAxes = controller.GetAxesList();
            wrapper.ConnectionStatus = $"Connected ({wrapper.AvailableAxes.Length} axes) via {port}";
            connected = true;
        }
    }
    Console.WriteLine($"ConnectAsync={connected}, IsConnected={wrapper.IsConnected}, Status='{wrapper.ConnectionStatus}'");

    if (!connected)
    {
        Console.WriteLine("Connection failed. Exiting without motion.");
        return;
    }

    Console.WriteLine($"AvailableAxes: [{string.Join(", ", wrapper.AvailableAxes)}]");
    Console.WriteLine($"EnabledAxes:   [{string.Join(", ", wrapper.EnabledAxes)}]");

    StageManager.RebuildAxisToWrapperMap();
    Console.WriteLine($"Axis map size: {StageManager.AxisToWrapperMap.Count}");

    var axis = AxisType.X;
    var mapped = StageManager.GetWrapperForAxis(axis);
    Console.WriteLine($"Mapped wrapper for {axis}: {(mapped == null ? "NULL" : mapped.Id.ToString())}");
    if (mapped == null)
    {
        Console.WriteLine("No mapped wrapper for X axis. Exiting.");
        return;
    }

    Console.WriteLine("Reading initial position samples...");
    for (int i = 0; i < 3; i++)
    {
        double p = ReadPositionSafe(mapped, axis);
        Console.WriteLine($"  pre[{i}] = {p:F4}");
        await Task.Delay(200);
    }

    double before = ReadPositionSafe(mapped, axis);
    Console.WriteLine($"Position before move: {before:F4}");

    Console.WriteLine("Moving +X in 10 small commands...");
    for (int i = 0; i < 10; i++)
    {
        mapped.MoveAxis(axis, +0.001); // SigmakokiController currently executes 1 hardware step/sign.
        double p = ReadPositionSafe(mapped, axis);
        Console.WriteLine($"  + move {i + 1}/10 -> pos {p:F4}");
        await Task.Delay(150);
    }

    double afterForward = ReadPositionSafe(mapped, axis);
    Console.WriteLine($"Position after + moves: {afterForward:F4}, delta={afterForward - before:F4}");

    Console.WriteLine("Moving -X in 10 small commands (return test)...");
    for (int i = 0; i < 10; i++)
    {
        mapped.MoveAxis(axis, -0.001);
        double p = ReadPositionSafe(mapped, axis);
        Console.WriteLine($"  - move {i + 1}/10 -> pos {p:F4}");
        await Task.Delay(150);
    }

    double afterReturn = ReadPositionSafe(mapped, axis);
    Console.WriteLine($"Position final: {afterReturn:F4}, net={afterReturn - before:F4}");
}
catch (Exception ex)
{
    Console.WriteLine("Probe failed:");
    Console.WriteLine(ex);
}
finally
{
    try
    {
        wrapper?.Disconnect();
        Console.WriteLine("Disconnected.");
    }
    catch { }
}

Console.WriteLine("=== StageManagerProbe END ===");
