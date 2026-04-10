using System.Diagnostics;
using System.Reflection;
using mv.impact.acquire;
using mv.impact.acquire.GenICam;

static string WaitResultText(int requestNr)
{
    return requestNr switch
    {
        (int)TDMR_ERROR.DMR_TIMEOUT => "TIMEOUT",
        (int)TDMR_ERROR.DEV_WAIT_FOR_REQUEST_FAILED => "WAIT_FAILED",
        _ when requestNr < 0 => $"ERR({requestNr})",
        _ => requestNr.ToString()
    };
}

try
{
    Console.WriteLine("=== CameraLatencyProbe START ===");
    Console.WriteLine($"Time: {DateTime.Now:O}");

    DeviceManager.updateDeviceList();
    int count = DeviceManager.deviceCount;
    Console.WriteLine($"Device count: {count}");
    if (count <= 0)
    {
        Console.WriteLine("No camera detected.");
        return;
    }

    var d = DeviceManager.getDevice(0);
    d.open();
    Console.WriteLine($"Using device: Serial={d.serial.read()} Family={d.family.read()} Product={d.product.read()}");
    try
    {
        Console.WriteLine($"interfaceLayout(valid={d.interfaceLayout.isValid}): {d.interfaceLayout.readS()}");
        d.interfaceLayout.write(TDeviceInterfaceLayout.dilGenICam);
        Console.WriteLine($"interfaceLayout(after): {d.interfaceLayout.readS()}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"interfaceLayout switch failed: {ex.Message}");
    }

    var fi = new FunctionInterface(d);
    var sys = new SystemSettings(d);
    if (sys.requestCount.isValid)
    {
        sys.requestCount.write(1);
        Console.WriteLine($"requestCount: {sys.requestCount.read()}");
    }

    Console.WriteLine("Applying camera parameters...");
    try
    {
        var acq = new AcquisitionControl(d);
        if (acq.exposureAuto.isValid)
        {
            acq.exposureAuto.write(0);
            Console.WriteLine("  exposureAuto=Off");
        }
        if (acq.exposureTime.isValid)
        {
            Console.WriteLine($"  exposureTime(before)={acq.exposureTime.read()}");
            acq.exposureTime.write(2500.0);
            Console.WriteLine($"  exposureTime(after)={acq.exposureTime.read()}");
        }
        if (acq.acquisitionFrameRateEnable.isValid)
        {
            acq.acquisitionFrameRateEnable.write(TBoolean.bTrue);
            Console.WriteLine("  frameRateEnable=True");
        }
        if (acq.acquisitionFrameRate.isValid)
        {
            Console.WriteLine($"  fps(before)={acq.acquisitionFrameRate.read()}");
            acq.acquisitionFrameRate.write(30.0);
            Console.WriteLine($"  fps(after)={acq.acquisitionFrameRate.read()}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  GenICam acquisition control failed: {ex.Message}");
    }

    try
    {
        var analog = new AnalogControl(d);
        if (analog.gainAuto.isValid)
        {
            analog.gainAuto.write(0);
            Console.WriteLine("  gainAuto=Off");
        }
        if (analog.gain.isValid)
        {
            Console.WriteLine($"  gain(before)={analog.gain.read()}");
            analog.gain.write(0.0);
            Console.WriteLine($"  gain(after)={analog.gain.read()}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  GenICam analog control failed: {ex.Message}");
    }

    try
    {
        var blue = new CameraSettingsBlueCOUGAR(d);
        Console.WriteLine("DeviceSpecific camera settings available.");
        var interestingProps = blue.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p =>
                p.Name.Contains("frame", StringComparison.OrdinalIgnoreCase) ||
                p.Name.Contains("trigger", StringComparison.OrdinalIgnoreCase) ||
                p.Name.Contains("exposure", StringComparison.OrdinalIgnoreCase) ||
                p.Name.Contains("gain", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Name)
            .OrderBy(n => n)
            .Take(40);
        var interestingFields = blue.GetType()
            .GetFields(BindingFlags.Instance | BindingFlags.Public)
            .Where(f =>
                f.Name.Contains("frame", StringComparison.OrdinalIgnoreCase) ||
                f.Name.Contains("trigger", StringComparison.OrdinalIgnoreCase) ||
                f.Name.Contains("exposure", StringComparison.OrdinalIgnoreCase) ||
                f.Name.Contains("gain", StringComparison.OrdinalIgnoreCase))
            .Select(f => f.Name)
            .OrderBy(n => n)
            .Take(80);
        Console.WriteLine("  Props: " + string.Join(", ", interestingProps));
        Console.WriteLine("  Fields: " + string.Join(", ", interestingFields));

        if (blue.frameRate_Hz.isValid)
        {
            Console.WriteLine($"  frameRate_Hz(before)={blue.frameRate_Hz.read()}");
            blue.frameRate_Hz.write(300.0);
            Console.WriteLine($"  frameRate_Hz(after)={blue.frameRate_Hz.read()}");
        }
        if (blue.triggerMode.isValid)
        {
            Console.WriteLine($"  triggerMode(before)={blue.triggerMode.readS()}");
            try
            {
                blue.triggerMode.writeS("Off");
            }
            catch { }
            Console.WriteLine($"  triggerMode(after)={blue.triggerMode.readS()}");
        }
        if (blue.triggerSource.isValid)
        {
            Console.WriteLine($"  triggerSource(before)={blue.triggerSource.readS()} ({blue.triggerSource.read()})");
            try
            {
                blue.triggerSource.write(0);
            }
            catch { }
            Console.WriteLine($"  triggerSource(after)={blue.triggerSource.readS()} ({blue.triggerSource.read()})");
        }
        if (blue.triggerInterface.isValid)
        {
            Console.WriteLine($"  triggerInterface={blue.triggerInterface.readS()}");
        }
        if (blue.gain_dB.isValid)
        {
            Console.WriteLine($"  gain_dB(before)={blue.gain_dB.read()}");
            blue.gain_dB.write(0.0);
            Console.WriteLine($"  gain_dB(after)={blue.gain_dB.read()}");
        }
        if (blue.frameDelay_us.isValid)
        {
            Console.WriteLine($"  frameDelay_us={blue.frameDelay_us.read()}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  DeviceSpecific settings failed: {ex.Message}");
    }

    fi.acquisitionStart();
    fi.imageRequestSingle();

    var sw = Stopwatch.StartNew();
    long totalFrames = 0;
    long totalDrained = 0;
    long maxDrainedInCycle = 0;
    long lastReportMs = 0;
    const int timeoutMs = 200;
    const int runSeconds = 10;

    while (sw.ElapsedMilliseconds < runSeconds * 1000)
    {
        long waitStart = sw.ElapsedMilliseconds;
        int requestNr = fi.imageRequestWaitFor(timeoutMs);
        long waitMs = sw.ElapsedMilliseconds - waitStart;

        if (requestNr < 0)
        {
            Console.WriteLine($"wait={waitMs,4}ms result={WaitResultText(requestNr)}");
            continue;
        }

        if (!fi.isRequestNrValid(requestNr))
        {
            Console.WriteLine($"Invalid request number: {requestNr}");
            continue;
        }

        int drainedThisCycle = 0;
        while (true)
        {
            int nextNr = fi.imageRequestWaitFor(0);
            if (nextNr < 0) break;

            fi.imageRequestUnlock(requestNr);
            fi.imageRequestSingle();
            requestNr = nextNr;
            drainedThisCycle++;
        }

        var req = fi.getRequest(requestNr);
        if (req.isOK)
        {
            totalFrames++;
        }

        if (drainedThisCycle > maxDrainedInCycle) maxDrainedInCycle = drainedThisCycle;
        totalDrained += drainedThisCycle;

        fi.imageRequestUnlock(requestNr);
        fi.imageRequestSingle();

        if (sw.ElapsedMilliseconds - lastReportMs >= 1000)
        {
            double seconds = sw.ElapsedMilliseconds / 1000.0;
            double fps = totalFrames / seconds;
            Console.WriteLine(
                $"t={seconds,5:F1}s fps={fps,6:F2} frames={totalFrames,4} drainedTotal={totalDrained,4} maxDrain={maxDrainedInCycle,2} lastWait={waitMs,4}ms");
            lastReportMs = sw.ElapsedMilliseconds;
        }
    }

    fi.acquisitionStop();
    d.close();
    Console.WriteLine("=== CameraLatencyProbe END ===");
}
catch (Exception ex)
{
    Console.WriteLine("Probe failed:");
    Console.WriteLine(ex);
}
