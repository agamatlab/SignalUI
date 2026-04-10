using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;

/// <summary>
/// Sigma Koki HSC-103 rotation axis: no C# serial code. Spawns <c>PythonStage/hsc103_json_service.py</c>
/// (uses <c>hsc103.py</c> / pyserial) and talks JSON lines over loopback TCP.
/// </summary>
public sealed class Hsc103RotationStageController : StageController
{
    public const double DefaultRotationDegreesPerPulse = 0.00001;

    private static readonly string LogFile = Path.Combine(Path.GetTempPath(), "singalui_hsc103_rotation.log");

    private readonly object _lock = new();
    private readonly string _portName;
    private readonly int _axisNumber;
    private readonly double _degreesPerPulse;
    private bool _referenced = true;

    private Process? _python;
    private TcpClient? _tcp;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private int _reqId;
    private readonly object _ioLock = new();

    public Hsc103RotationStageController(string portName, int hsc103AxisNumber, double degreesPerPulse)
    {
        _portName = portName ?? "COM3";
        _axisNumber = Math.Clamp(hsc103AxisNumber, 1, 3);
        _degreesPerPulse = degreesPerPulse > 0 ? degreesPerPulse : DefaultRotationDegreesPerPulse;
    }

    private static void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] [HSC103-Rot] {message}";
        Console.WriteLine(line);
        try { File.AppendAllText(LogFile, line + "\n"); } catch { /* ignore */ }
    }

    private static string PythonStageDirectory
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("SINGALUI_PYTHON_STAGE_DIR");
            if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env))
                return env.Trim();
            var p = Path.Combine(AppContext.BaseDirectory, "PythonStage");
            if (!Directory.Exists(p))
                throw new DirectoryNotFoundException(
                    $"PythonStage not found at '{p}'. Set SINGALUI_PYTHON_STAGE_DIR or copy PythonStage next to the app. Install: pip install -r PythonStage/requirements.txt");
            return p;
        }
    }

    private static string ResolvePythonExecutable()
    {
        var env = Environment.GetEnvironmentVariable("SINGALUI_PYTHON_EXE");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env))
            return env.Trim();
        var bundled = Path.Combine(AppContext.BaseDirectory, "PythonStage", "python.exe");
        if (File.Exists(bundled))
            return bundled;
        return OperatingSystem.IsWindows() ? "python" : "python3";
    }

    private static int GetFreeTcpPort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }

    public override void Connect()
    {
        lock (_lock)
        {
            DisconnectInternal();
            string pyDir = PythonStageDirectory;
            string script = Path.Combine(pyDir, "hsc103_json_service.py");
            if (!File.Exists(script))
                throw new FileNotFoundException("hsc103_json_service.py not found", script);

            int tcpPort = GetFreeTcpPort();
            string exe = ResolvePythonExecutable();
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"-u \"{script}\" --listen-port {tcpPort.ToString(CultureInfo.InvariantCulture)}",
                WorkingDirectory = pyDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            Log($"Starting Python HSC103 service: {exe} (port {tcpPort})");
            var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            proc.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    Log("[py] " + e.Data);
            };
            try
            {
                proc.Start();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to start Python ({exe}). Install Python 3 and pyserial (pip install -r PythonStage/requirements.txt), or set SINGALUI_PYTHON_EXE to python.exe.", ex);
            }

            proc.BeginErrorReadLine();
            _python = proc;

            const int readyTimeoutMs = 20000;
            var sw = Stopwatch.StartNew();
            bool ready = false;
            while (sw.ElapsedMilliseconds < readyTimeoutMs)
            {
                if (proc.HasExited)
                    throw new InvalidOperationException($"Python service exited early (code {proc.ExitCode}). See log / stderr in {LogFile}.");
                string? line = proc.StandardOutput.ReadLine();
                if (line != null && line.Trim() == "READY")
                {
                    ready = true;
                    break;
                }
            }

            if (!ready)
            {
                try { proc.Kill(true); } catch { /* ignore */ }
                throw new TimeoutException("Python HSC103 service did not signal READY on stdout.");
            }

            _tcp = new TcpClient();
            _tcp.ReceiveTimeout = 600_000;
            _tcp.SendTimeout = 60_000;
            _tcp.Connect(IPAddress.Loopback, tcpPort);
            var stream = _tcp.GetStream();
            _reader = new StreamReader(stream, Encoding.UTF8, false, 65536, leaveOpen: false);
            _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };

            RpcExpectOk("open", new Dictionary<string, object?>
            {
                ["serial_port"] = _portName,
                ["axis"] = _axisNumber,
                ["degrees_per_pulse"] = _degreesPerPulse
            });

            _referenced = true;
            Log($"Python bridge connected: {_portName}, axis {_axisNumber}, DPP={_degreesPerPulse}");
        }
    }

    private JsonDocument Rpc(string cmd, Dictionary<string, object?>? extra = null)
    {
        lock (_ioLock)
        {
            if (_writer == null || _reader == null)
                throw new InvalidOperationException("Not connected");

            int id = Interlocked.Increment(ref _reqId);
            var dict = new Dictionary<string, object?> { ["id"] = id, ["cmd"] = cmd };
            if (extra != null)
            {
                foreach (var kv in extra)
                    dict[kv.Key] = kv.Value;
            }

            string line = JsonSerializer.Serialize(dict);
            _writer.Write(line);
            _writer.Write('\n');
            _writer.Flush();

            string? resp = _reader.ReadLine();
            if (resp == null)
                throw new IOException("Python service closed the connection.");

            return JsonDocument.Parse(resp);
        }
    }

    private void RpcExpectOk(string cmd, Dictionary<string, object?>? extra = null)
    {
        using var doc = Rpc(cmd, extra);
        var root = doc.RootElement;
        if (!root.TryGetProperty("ok", out var okEl) || !okEl.GetBoolean())
        {
            string err = root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String
                ? e.GetString() ?? "unknown"
                : "unknown";
            throw new InvalidOperationException(err);
        }
    }

    private double RpcGetDegrees(string cmd, Dictionary<string, object?>? extra = null)
    {
        using var doc = Rpc(cmd, extra);
        var root = doc.RootElement;
        if (!root.TryGetProperty("ok", out var okEl) || !okEl.GetBoolean())
        {
            string err = root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String
                ? e.GetString() ?? "unknown"
                : "unknown";
            throw new InvalidOperationException(err);
        }

        if (!root.TryGetProperty("degrees", out var d))
            throw new InvalidOperationException("Missing degrees in response");
        return d.GetDouble();
    }

    public void SetSpeed(int start, int bound, int accelTime)
    {
        lock (_lock)
        {
            RpcExpectOk("set_speed", new Dictionary<string, object?>
            {
                ["start"] = start,
                ["bound"] = bound,
                ["accel_time"] = accelTime
            });
        }
    }

    private void DisconnectInternal()
    {
        lock (_ioLock)
        {
            try
            {
                if (_writer != null && _reader != null && _tcp is { Connected: true })
                {
                    try
                    {
                        int sid = Interlocked.Increment(ref _reqId);
                        var shutdown = new Dictionary<string, object?> { ["id"] = sid, ["cmd"] = "shutdown" };
                        _writer.Write(JsonSerializer.Serialize(shutdown));
                        _writer.Write('\n');
                        _writer.Flush();
                        _reader.ReadLine();
                    }
                    catch
                    {
                        /* ignore */
                    }
                }
            }
            catch { /* ignore */ }

            try { _reader?.Dispose(); } catch { /* ignore */ }
            try { _writer?.Dispose(); } catch { /* ignore */ }
            _reader = null;
            _writer = null;
            try { _tcp?.Close(); } catch { /* ignore */ }
            _tcp = null;
        }

        if (_python != null)
        {
            try
            {
                if (!_python.HasExited)
                {
                    if (!_python.WaitForExit(3000))
                        _python.Kill(entireProcessTree: true);
                }
            }
            catch { /* ignore */ }
            try { _python.Dispose(); } catch { /* ignore */ }
            _python = null;
            Thread.Sleep(200);
        }
    }

    public override void Disconnect()
    {
        lock (_lock)
        {
            DisconnectInternal();
            Log("Disconnected (Python bridge).");
        }
    }

    public override bool CheckConnected()
    {
        lock (_lock)
        {
            if (_tcp == null || !_tcp.Connected || _reader == null || _writer == null)
                return false;
            try
            {
                using var doc = Rpc("ping", null);
                return doc.RootElement.TryGetProperty("ok", out var ok) && ok.GetBoolean();
            }
            catch
            {
                return false;
            }
        }
    }

    public override string[] GetAxesList() => new[] { "Rz" };

    public override double GetPosition(int axisIndex)
    {
        if (axisIndex != 0)
            throw new ArgumentOutOfRangeException(nameof(axisIndex));
        lock (_lock)
        {
            return RpcGetDegrees("get_pos", null);
        }
    }

    public override void MoveRelative(int axisIndex, double amountDegrees)
    {
        if (axisIndex != 0)
            throw new ArgumentOutOfRangeException(nameof(axisIndex));
        lock (_lock)
        {
            double current = GetPosition(0);
            MoveAbsoluteDegrees(current + amountDegrees);
        }
    }

    public void MoveAbsoluteDegrees(double targetDegrees)
    {
        lock (_lock)
        {
            double current = RpcGetDegrees("get_pos", null);
            double deltaDeg = targetDegrees - current;
            int relative = (int)(deltaDeg / _degreesPerPulse);
            Log($"MoveAbsoluteDegrees target={targetDegrees.ToString(CultureInfo.InvariantCulture)}° current={current.ToString(CultureInfo.InvariantCulture)}° Δpulses={relative}");
            if (relative == 0)
            {
                if (Math.Abs(deltaDeg) <= _degreesPerPulse * 0.5)
                {
                    Log("Move skipped (already at target within one pulse).");
                    return;
                }

                throw new InvalidOperationException(
                    $"Computed 0 pulses for {deltaDeg.ToString(CultureInfo.InvariantCulture)}° delta (current={current.ToString(CultureInfo.InvariantCulture)}°, target={targetDegrees.ToString(CultureInfo.InvariantCulture)}°, DPP={_degreesPerPulse.ToString(CultureInfo.InvariantCulture)}). Check Q: readback and degrees-per-pulse.");
            }

            RpcExpectOk("move_abs", new Dictionary<string, object?> { ["degrees"] = targetDegrees });
        }
    }

    public override void ReferenceAxes()
    {
        lock (_lock)
        {
            RpcExpectOk("home", null);
            _referenced = true;
            Log("Reference (home) complete.");
        }
    }

    public override bool AreAxesReferenced() => _referenced;

    public override double GetMinTravel(int axisIndex) => -360.0;

    public override double GetMaxTravel(int axisIndex) => 360.0;

    public void StopAxis()
    {
        lock (_lock)
        {
            try
            {
                RpcExpectOk("stop", null);
            }
            catch (Exception ex)
            {
                Log($"StopAxis: {ex.Message}");
            }
        }
    }
}
