# Dependency Injection & Exception Standardization Guide

## Table of Contents
1. [Dependency Injection Explained](#dependency-injection-explained)
2. [Current Architecture Analysis](#current-architecture-analysis)
3. [Implementing DI in SignalUI](#implementing-di-in-signalui)
4. [Standardized Exception Handling](#standardized-exception-handling)
5. [Migration Guide](#migration-guide)

---

## Dependency Injection Explained

### What is Dependency Injection?

**Dependency Injection (DI)** is a design pattern where objects receive their dependencies from external sources rather than creating them internally.

### The Problem Without DI

```csharp
// ❌ BAD: Tight coupling, hard to test
public class CalibrationViewModel
{
    private PIController _controller;
    
    public void Initialize()
    {
        _controller = new PIController();  // Hard-coded dependency
        _controller.Connect();
    }
    
    public void Move(int axis, double amount)
    {
        _controller.MoveRelative(axis, amount);
    }
}
```

**Problems:**
- ❌ Cannot test without real hardware
- ❌ Cannot swap implementations (PI vs Sigmakoki)
- ❌ Violates Single Responsibility Principle
- ❌ Hard to maintain and extend

### The Solution With DI

```csharp
// ✅ GOOD: Loose coupling, testable
public class CalibrationViewModel
{
    private readonly IStageController _controller;
    
    // Dependency injected via constructor
    public CalibrationViewModel(IStageController controller)
    {
        _controller = controller;
    }
    
    public void Initialize()
    {
        _controller.Connect();
    }
    
    public void Move(int axis, double amount)
    {
        _controller.MoveRelative(axis, amount);
    }
}
```

**Benefits:**
- ✅ Easy to test (inject mock controllers)
- ✅ Loose coupling (depends on interface, not concrete type)
- ✅ Easy to swap implementations
- ✅ Centralized configuration

---

## Current Architecture Analysis

### Your Current Setup

**Good:**
- ✅ Factory pattern (`StageControllerFactory`)
- ✅ Service locator pattern (`StageManager`)
- ✅ Adapter pattern (multiple controller implementations)

**Can Be Improved:**
- ⚠️ Static service locator (`StageManager`) - global state
- ⚠️ Manual lifecycle management
- ⚠️ ViewModels create dependencies directly
- ⚠️ No interface for `StageController` (abstract class only)

### Current Flow

```
App.axaml.cs
  └─> StageManager.InitializeDefaultStages() (static)
       └─> StageWrapper.Create()
            └─> StageControllerFactory.CreateController()
                 └─> new PIController() or new SigmakokiController()

ViewModel
  └─> StageManager.Wrappers[0] (static access)
       └─> wrapper.Controller.MoveRelative()
```

---

## Implementing DI in SignalUI

### Step 1: Create Interface from Abstract Class

**Why?** Interfaces are more flexible than abstract classes for DI.

```csharp
// File: singalUI/libs/IStageController.cs
namespace singalUI.libs;

/// <summary>
/// Interface for all stage controller implementations
/// Allows for dependency injection and easier testing
/// </summary>
public interface IStageController
{
    /// <summary>Check if controller is connected</summary>
    bool CheckConnected();
    
    /// <summary>Move axis by relative amount</summary>
    /// <param name="axes">Axis index (0-based)</param>
    /// <param name="amount">Amount to move (units depend on controller)</param>
    void MoveRelative(int axes, double amount);
    
    /// <summary>Get list of available axes</summary>
    string[] GetAxesList();
    
    /// <summary>Connect to the controller</summary>
    void Connect();
    
    /// <summary>Disconnect from the controller</summary>
    void Disconnect();
    
    /// <summary>Get current position of an axis</summary>
    /// <param name="axisIndex">0-based axis index</param>
    /// <returns>Current position in controller-specific units</returns>
    double GetPosition(int axisIndex);
    
    /// <summary>Get minimum travel limit for an axis</summary>
    double GetMinTravel(int axisIndex);
    
    /// <summary>Get maximum travel limit for an axis</summary>
    double GetMaxTravel(int axisIndex);
    
    /// <summary>Reference (home) all axes</summary>
    void ReferenceAxes();
    
    /// <summary>Check if all axes are referenced</summary>
    bool AreAxesReferenced();
}
```

**Then update StageController:**

```csharp
// File: singalUI/libs/StageController.cs
public abstract class StageController : IStageController
{
    // Implementation stays the same
    public abstract bool CheckConnected();
    public abstract void MoveRelative(int axes, double amount);
    // ... etc
}
```

### Step 2: Set Up DI Container

**.NET has built-in DI support.** Add to your project:

```csharp
// File: singalUI/Services/ServiceCollectionExtensions.cs
using Microsoft.Extensions.DependencyInjection;
using singalUI.libs;
using singalUI.ViewModels;
using singalUI.Services;

namespace singalUI.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSignalUIServices(this IServiceCollection services)
    {
        // Register services as singletons (one instance for app lifetime)
        services.AddSingleton<MatrixVisionCameraService>();
        services.AddSingleton<IStageControllerFactory, StageControllerFactory>();
        services.AddSingleton<IStageManager, StageManager>();
        
        // Register ViewModels as transient (new instance each time)
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<CalibrationSetupViewModel>();
        services.AddTransient<CameraSetupViewModel>();
        services.AddTransient<AnalysisViewModel>();
        services.AddTransient<ConfigViewModel>();
        
        return services;
    }
}
```

### Step 3: Update App.axaml.cs

```csharp
// File: singalUI/App.axaml.cs
using Microsoft.Extensions.DependencyInjection;
using System;

namespace singalUI;

public partial class App : Application
{
    // DI Service Provider
    public static IServiceProvider Services { get; private set; } = null!;
    
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        
        // Configure DI container
        var services = new ServiceCollection();
        services.AddSignalUIServices();
        Services = services.BuildServiceProvider();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Get MainWindowViewModel from DI container
            var mainViewModel = Services.GetRequiredService<MainWindowViewModel>();
            
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };
            
            desktop.Exit += (_, __) =>
            {
                // Dispose DI container (cleans up all services)
                (Services as IDisposable)?.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
```

### Step 4: Update ViewModels to Use DI

**Before (without DI):**
```csharp
public class CalibrationSetupViewModel : ViewModelBase
{
    private StageController? _controller;
    
    public void ConnectStage()
    {
        // Hard-coded dependency creation
        _controller = new PIController();
        _controller.Connect();
    }
}
```

**After (with DI):**
```csharp
public class CalibrationSetupViewModel : ViewModelBase
{
    private readonly IStageControllerFactory _factory;
    private IStageController? _controller;
    
    // Dependencies injected via constructor
    public CalibrationSetupViewModel(IStageControllerFactory factory)
    {
        _factory = factory;
    }
    
    public void ConnectStage(StageHardwareType hardwareType)
    {
        // Use factory to create controller
        _controller = _factory.CreateController(hardwareType);
        _controller?.Connect();
    }
}
```

### Step 5: Testing Benefits

**With DI, you can easily create mock controllers for testing:**

```csharp
// File: singalUI.Tests/Mocks/MockStageController.cs
public class MockStageController : IStageController
{
    public bool IsConnected { get; set; }
    public List<(int axis, double amount)> MoveCalls { get; } = new();
    
    public bool CheckConnected() => IsConnected;
    
    public void MoveRelative(int axis, double amount)
    {
        MoveCalls.Add((axis, amount));
    }
    
    public void Connect() => IsConnected = true;
    public void Disconnect() => IsConnected = false;
    
    // ... implement other methods
}

// Test example
[Fact]
public void CalibrationViewModel_Move_CallsController()
{
    // Arrange
    var mockController = new MockStageController();
    var viewModel = new CalibrationSetupViewModel(mockController);
    
    // Act
    viewModel.Move(0, 10.0);
    
    // Assert
    Assert.Single(mockController.MoveCalls);
    Assert.Equal((0, 10.0), mockController.MoveCalls[0]);
}
```

---

## Standardized Exception Handling

### The Problem

Currently, your controllers throw generic exceptions:

```csharp
// Different error handling across controllers
throw new Exception("Connection failed");  // PIController
throw new Exception("Not connected");      // SigmakokiController
throw new PlatformNotSupportedException("..."); // Platform check
```

**Problems:**
- ❌ Cannot distinguish error types
- ❌ Hard to handle specific errors in UI
- ❌ No structured error information
- ❌ Inconsistent error messages

### The Solution: Custom Exception Hierarchy

```csharp
// File: singalUI/libs/Exceptions/StageControllerException.cs
namespace singalUI.libs.Exceptions;

/// <summary>
/// Base exception for all stage controller errors
/// </summary>
public class StageControllerException : Exception
{
    public string? ControllerType { get; }
    public int? AxisIndex { get; }
    public ErrorSeverity Severity { get; }
    
    public StageControllerException(
        string message, 
        ErrorSeverity severity = ErrorSeverity.Error,
        string? controllerType = null,
        int? axisIndex = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ControllerType = controllerType;
        AxisIndex = axisIndex;
        Severity = severity;
    }
}

public enum ErrorSeverity
{
    Warning,    // Recoverable, operation can continue
    Error,      // Operation failed, but controller still usable
    Critical    // Controller unusable, requires reconnection
}
```

### Specific Exception Types

```csharp
// File: singalUI/libs/Exceptions/StageControllerExceptions.cs
namespace singalUI.libs.Exceptions;

/// <summary>
/// Thrown when connection to controller fails
/// </summary>
public class StageConnectionException : StageControllerException
{
    public string? PortOrDevice { get; }
    
    public StageConnectionException(
        string message, 
        string? portOrDevice = null,
        string? controllerType = null,
        Exception? innerException = null)
        : base(message, ErrorSeverity.Critical, controllerType, null, innerException)
    {
        PortOrDevice = portOrDevice;
    }
}

/// <summary>
/// Thrown when controller is not connected
/// </summary>
public class StageNotConnectedException : StageControllerException
{
    public StageNotConnectedException(string? controllerType = null)
        : base("Stage controller is not connected", ErrorSeverity.Error, controllerType)
    {
    }
}

/// <summary>
/// Thrown when axis operation fails
/// </summary>
public class StageAxisException : StageControllerException
{
    public StageAxisException(
        string message, 
        int axisIndex,
        string? controllerType = null,
        Exception? innerException = null)
        : base(message, ErrorSeverity.Error, controllerType, axisIndex, innerException)
    {
    }
}

/// <summary>
/// Thrown when movement fails or is out of bounds
/// </summary>
public class StageMovementException : StageControllerException
{
    public double RequestedPosition { get; }
    public double? MinLimit { get; }
    public double? MaxLimit { get; }
    
    public StageMovementException(
        string message,
        int axisIndex,
        double requestedPosition,
        double? minLimit = null,
        double? maxLimit = null,
        string? controllerType = null,
        Exception? innerException = null)
        : base(message, ErrorSeverity.Error, controllerType, axisIndex, innerException)
    {
        RequestedPosition = requestedPosition;
        MinLimit = minLimit;
        MaxLimit = maxLimit;
    }
}

/// <summary>
/// Thrown when communication with controller fails
/// </summary>
public class StageCommunicationException : StageControllerException
{
    public string? Command { get; }
    public string? Response { get; }
    
    public StageCommunicationException(
        string message,
        string? command = null,
        string? response = null,
        string? controllerType = null,
        Exception? innerException = null)
        : base(message, ErrorSeverity.Error, controllerType, null, innerException)
    {
        Command = command;
        Response = response;
    }
}

/// <summary>
/// Thrown when referencing (homing) fails
/// </summary>
public class StageReferencingException : StageControllerException
{
    public StageReferencingException(
        string message,
        int? axisIndex = null,
        string? controllerType = null,
        Exception? innerException = null)
        : base(message, ErrorSeverity.Error, controllerType, axisIndex, innerException)
    {
    }
}

/// <summary>
/// Thrown when platform doesn't support the controller
/// </summary>
public class StagePlatformNotSupportedException : StageControllerException
{
    public string Platform { get; }
    
    public StagePlatformNotSupportedException(
        string message,
        string platform,
        string? controllerType = null)
        : base(message, ErrorSeverity.Critical, controllerType)
    {
        Platform = platform;
    }
}

/// <summary>
/// Thrown when required DLL or library is missing
/// </summary>
public class StageDllNotFoundException : StageControllerException
{
    public string DllName { get; }
    
    public StageDllNotFoundException(
        string message,
        string dllName,
        string? controllerType = null,
        Exception? innerException = null)
        : base(message, ErrorSeverity.Critical, controllerType, null, innerException)
    {
        DllName = dllName;
    }
}
```

### Update Controllers to Use Standardized Exceptions

**PIController Example:**

```csharp
// Before
public override void Connect()
{
    if (noDevices == 0)
    {
        throw new Exception("No PI USB controllers found");
    }
}

// After
public override void Connect()
{
    if (noDevices == 0)
    {
        throw new StageConnectionException(
            "No PI USB controllers found after " + maxRetries + " attempts. Please check:\n" +
            "1. PI controller is connected via USB\n" +
            "2. PI_GCS2_DLL.dll is in the application directory\n" +
            "3. Required PI drivers are installed\n" +
            "4. The PI controller is powered on",
            portOrDevice: "USB",
            controllerType: "PI"
        );
    }
}

// Before
public override void MoveRelative(int index, double amount)
{
    if (channels.Length == 0)
    {
        throw new Exception("Axes not initialized. Call Connect() first.");
    }
}

// After
public override void MoveRelative(int index, double amount)
{
    if (channels.Length == 0)
    {
        throw new StageNotConnectedException("PI");
    }
    
    if (index < 0 || index >= channels.Length)
    {
        throw new StageAxisException(
            $"Invalid axis index {index}. Available: 0-{channels.Length-1}",
            axisIndex: index,
            controllerType: "PI"
        );
    }
    
    // Check bounds
    if (targetPosition < minTravel[index] || targetPosition > maxTravel[index])
    {
        throw new StageMovementException(
            $"Target position {targetPosition:F4} is out of bounds",
            axisIndex: index,
            requestedPosition: targetPosition,
            minLimit: minTravel[index],
            maxLimit: maxTravel[index],
            controllerType: "PI"
        );
    }
}
```

**SigmakokiController Example:**

```csharp
// Before
public override void Connect()
{
    throw new Exception($"Connection failed: {ex.Message}");
}

// After
public override void Connect()
{
    try
    {
        StageSerialPortPlatform.RequireWindowsForComPort("Sigma Koki (serial)");
        // ... connection code
    }
    catch (PlatformNotSupportedException ex)
    {
        throw new StagePlatformNotSupportedException(
            ex.Message,
            platform: Environment.OSVersion.Platform.ToString(),
            controllerType: "Sigmakoki"
        );
    }
    catch (Exception ex)
    {
        throw new StageConnectionException(
            $"Connection failed: {ex.Message}",
            portOrDevice: _portName,
            controllerType: "Sigmakoki",
            innerException: ex
        );
    }
}

// Before
private string SendCommand(string command)
{
    if (_serialPort == null || !_serialPort.IsOpen)
    {
        throw new Exception("Not connected");
    }
}

// After
private string SendCommand(string command)
{
    if (_serialPort == null || !_serialPort.IsOpen)
    {
        throw new StageNotConnectedException("Sigmakoki");
    }
    
    try
    {
        // ... send command
    }
    catch (TimeoutException ex)
    {
        throw new StageCommunicationException(
            "Command timeout",
            command: command,
            controllerType: "Sigmakoki",
            innerException: ex
        );
    }
}
```

### Handling Exceptions in ViewModels

```csharp
public class CalibrationSetupViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _errorMessage = string.Empty;
    
    [ObservableProperty]
    private bool _hasError = false;
    
    public async Task ConnectStageAsync()
    {
        try
        {
            HasError = false;
            ErrorMessage = string.Empty;
            
            await Task.Run(() => _controller?.Connect());
            
            StatusMessage = "Connected successfully";
        }
        catch (StageConnectionException ex)
        {
            HasError = true;
            ErrorMessage = $"Connection failed: {ex.Message}";
            
            if (ex.PortOrDevice != null)
            {
                ErrorMessage += $"\nPort/Device: {ex.PortOrDevice}";
            }
            
            // Log for debugging
            Console.WriteLine($"[{ex.ControllerType}] Connection error: {ex.Message}");
        }
        catch (StagePlatformNotSupportedException ex)
        {
            HasError = true;
            ErrorMessage = $"Platform not supported: {ex.Message}\nPlatform: {ex.Platform}";
        }
        catch (StageDllNotFoundException ex)
        {
            HasError = true;
            ErrorMessage = $"Missing library: {ex.DllName}\n{ex.Message}";
        }
        catch (StageControllerException ex)
        {
            HasError = true;
            ErrorMessage = $"Stage error: {ex.Message}";
            
            // Handle based on severity
            if (ex.Severity == ErrorSeverity.Critical)
            {
                // Disconnect and disable controls
                _controller?.Disconnect();
            }
        }
        catch (Exception ex)
        {
            // Catch-all for unexpected errors
            HasError = true;
            ErrorMessage = $"Unexpected error: {ex.Message}";
            Console.WriteLine($"Unexpected error: {ex}");
        }
    }
    
    public async Task MoveAxisAsync(int axis, double amount)
    {
        try
        {
            HasError = false;
            
            await Task.Run(() => _controller?.MoveRelative(axis, amount));
        }
        catch (StageNotConnectedException ex)
        {
            HasError = true;
            ErrorMessage = "Controller not connected. Please connect first.";
        }
        catch (StageAxisException ex)
        {
            HasError = true;
            ErrorMessage = $"Axis error: {ex.Message}";
        }
        catch (StageMovementException ex)
        {
            HasError = true;
            ErrorMessage = $"Movement failed: {ex.Message}\n" +
                          $"Requested: {ex.RequestedPosition:F4}\n" +
                          $"Limits: [{ex.MinLimit:F4}, {ex.MaxLimit:F4}]";
        }
        catch (StageControllerException ex)
        {
            HasError = true;
            ErrorMessage = $"Stage error: {ex.Message}";
        }
    }
}
```

---

## Migration Guide

### Phase 1: Add Exception Classes (No Breaking Changes)

1. Create `singalUI/libs/Exceptions/` folder
2. Add all exception classes
3. **Don't change existing code yet**

### Phase 2: Update Controllers Gradually

1. Start with one controller (e.g., PIController)
2. Replace `throw new Exception()` with specific exceptions
3. Test thoroughly
4. Move to next controller

### Phase 3: Add Interface (Optional, for DI)

1. Create `IStageController` interface
2. Make `StageController` implement it
3. Update factory to return `IStageController`

### Phase 4: Implement DI (Optional, Advanced)

1. Add `Microsoft.Extensions.DependencyInjection` NuGet package
2. Create service registration
3. Update `App.axaml.cs`
4. Update ViewModels one at a time

---

## Benefits Summary

### Dependency Injection Benefits
- ✅ **Testability**: Easy to mock dependencies
- ✅ **Flexibility**: Swap implementations without code changes
- ✅ **Maintainability**: Clear dependency graph
- ✅ **Lifecycle Management**: Automatic disposal

### Standardized Exceptions Benefits
- ✅ **Type Safety**: Catch specific error types
- ✅ **Rich Context**: Structured error information
- ✅ **Better UX**: Show meaningful error messages
- ✅ **Debugging**: Easier to trace issues
- ✅ **Consistency**: Same error handling across controllers

---

## Conclusion

Both patterns significantly improve code quality:

1. **Start with exceptions** - easier to implement, immediate benefits
2. **Add DI later** - more complex but powerful for large applications

Your current architecture is already good with the Factory and Service Locator patterns. These improvements make it even better!
