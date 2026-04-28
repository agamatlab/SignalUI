using System;
using System.Linq;
using singalUI.libs;

namespace MockStageController.Plugin;

/// <summary>
/// Mock stage controller plugin for testing the plugin system
/// This is a simple test controller that simulates stage movement without real hardware
/// </summary>
public class MockStageControllerPlugin : IStageControllerPlugin
{
    public PluginMetadata Metadata => new()
    {
        Name = "MockStage",
        DisplayName = "Mock Stage Controller (Test)",
        Manufacturer = "Test Company",
        Version = "1.0.0",
        Description = "A mock stage controller for testing the plugin system without real hardware",
        Author = "SignalUI Team",
        SupportedPlatforms = new[] { "Windows", "Linux", "OSX" },
        RequiredDlls = Array.Empty<string>()
    };
    
    public bool IsCompatible()
    {
        // Mock controller works on all platforms
        Console.WriteLine("[MockStagePlugin] Checking compatibility... OK");
        return true;
    }
    
    public StageController CreateController(PluginConfiguration config)
    {
        Console.WriteLine("[MockStagePlugin] Creating mock controller instance");
        
        // Get configuration parameters
        int numAxes = config.GetParameter<int>("NumAxes", 3);
        double minTravel = config.GetParameter<double>("MinTravel", -50.0);
        double maxTravel = config.GetParameter<double>("MaxTravel", 50.0);
        
        Console.WriteLine($"[MockStagePlugin] Configuration: NumAxes={numAxes}, MinTravel={minTravel}, MaxTravel={maxTravel}");
        
        return new MockStageController(numAxes, minTravel, maxTravel);
    }
}

/// <summary>
/// Mock stage controller implementation
/// Simulates a multi-axis stage without requiring real hardware
/// </summary>
public class MockStageController : StageController
{
    private readonly int _numAxes;
    private readonly double _minTravel;
    private readonly double _maxTravel;
    private bool _isConnected = false;
    private string[] _axes = Array.Empty<string>();
    private double[] _positions = Array.Empty<double>();
    private bool _isReferenced = false;
    
    public MockStageController(int numAxes = 3, double minTravel = -50.0, double maxTravel = 50.0)
    {
        _numAxes = Math.Max(1, Math.Min(numAxes, 6)); // Clamp to 1-6 axes
        _minTravel = minTravel;
        _maxTravel = maxTravel;
        
        Console.WriteLine($"[MockStageController] Created with {_numAxes} axes");
    }
    
    public override void Connect()
    {
        Console.WriteLine("[MockStageController] Connecting...");
        
        // Simulate connection delay
        System.Threading.Thread.Sleep(100);
        
        // Initialize axes
        _axes = Enumerable.Range(1, _numAxes).Select(i => $"Axis{i}").ToArray();
        _positions = new double[_numAxes];
        
        _isConnected = true;
        _isReferenced = false;
        
        Console.WriteLine($"[MockStageController] Connected! Axes: [{string.Join(", ", _axes)}]");
    }
    
    public override void Disconnect()
    {
        Console.WriteLine("[MockStageController] Disconnecting...");
        _isConnected = false;
        _axes = Array.Empty<string>();
        _positions = Array.Empty<double>();
        Console.WriteLine("[MockStageController] Disconnected");
    }
    
    public override bool CheckConnected()
    {
        return _isConnected;
    }
    
    public override string[] GetAxesList()
    {
        if (!_isConnected)
        {
            throw new InvalidOperationException("Not connected. Call Connect() first.");
        }
        return _axes;
    }
    
    public override void MoveRelative(int axisIndex, double amount)
    {
        if (!_isConnected)
        {
            throw new InvalidOperationException("Not connected. Call Connect() first.");
        }
        
        if (axisIndex < 0 || axisIndex >= _numAxes)
        {
            throw new ArgumentOutOfRangeException(nameof(axisIndex), 
                $"Invalid axis index {axisIndex}. Valid range: 0-{_numAxes - 1}");
        }
        
        double currentPos = _positions[axisIndex];
        double targetPos = currentPos + amount;
        
        // Clamp to travel limits
        if (targetPos < _minTravel)
        {
            Console.WriteLine($"[MockStageController] Target position {targetPos:F4} below min limit {_minTravel:F4}, clamping");
            targetPos = _minTravel;
        }
        else if (targetPos > _maxTravel)
        {
            Console.WriteLine($"[MockStageController] Target position {targetPos:F4} above max limit {_maxTravel:F4}, clamping");
            targetPos = _maxTravel;
        }
        
        Console.WriteLine($"[MockStageController] Moving axis {axisIndex} ({_axes[axisIndex]}) by {amount:F4} mm");
        Console.WriteLine($"[MockStageController]   Position: {currentPos:F4} -> {targetPos:F4} mm");
        
        // Simulate movement delay (proportional to distance)
        int delayMs = (int)(Math.Abs(amount) * 10); // 10ms per mm
        delayMs = Math.Max(50, Math.Min(delayMs, 2000)); // Clamp to 50-2000ms
        System.Threading.Thread.Sleep(delayMs);
        
        _positions[axisIndex] = targetPos;
        
        Console.WriteLine($"[MockStageController] Move completed. New position: {targetPos:F4} mm");
    }
    
    public override double GetPosition(int axisIndex)
    {
        if (!_isConnected)
        {
            throw new InvalidOperationException("Not connected. Call Connect() first.");
        }
        
        if (axisIndex < 0 || axisIndex >= _numAxes)
        {
            throw new ArgumentOutOfRangeException(nameof(axisIndex), 
                $"Invalid axis index {axisIndex}. Valid range: 0-{_numAxes - 1}");
        }
        
        return _positions[axisIndex];
    }
    
    public override double GetMinTravel(int axisIndex)
    {
        if (axisIndex < 0 || axisIndex >= _numAxes)
        {
            throw new ArgumentOutOfRangeException(nameof(axisIndex));
        }
        return _minTravel;
    }
    
    public override double GetMaxTravel(int axisIndex)
    {
        if (axisIndex < 0 || axisIndex >= _numAxes)
        {
            throw new ArgumentOutOfRangeException(nameof(axisIndex));
        }
        return _maxTravel;
    }
    
    public override void ReferenceAxes()
    {
        if (!_isConnected)
        {
            throw new InvalidOperationException("Not connected. Call Connect() first.");
        }
        
        Console.WriteLine("[MockStageController] Referencing all axes (homing)...");
        
        // Simulate homing delay
        System.Threading.Thread.Sleep(500);
        
        // Reset all positions to zero
        for (int i = 0; i < _numAxes; i++)
        {
            _positions[i] = 0.0;
        }
        
        _isReferenced = true;
        
        Console.WriteLine("[MockStageController] All axes referenced (homed to zero)");
    }
    
    public override bool AreAxesReferenced()
    {
        return _isConnected && _isReferenced;
    }
}
