using CommunityToolkit.Mvvm.ComponentModel;
using singalUI.Models;
using singalUI.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.ComponentModel;

namespace singalUI.Services;

/// <summary>
/// Static service for managing stage controllers globally
/// Accessible from anywhere in the application via StageManager.Wrappers
/// </summary>
public static class StageManager
{
    /// <summary>
    /// Event raised when any wrapper's property changes (useful for UI updates)
    /// </summary>
    public static event EventHandler<PropertyChangedEventArgs>? WrapperPropertyChanged;

    /// <summary>
    /// Collection of all stage wrappers (configuration + controller together)
    /// </summary>
    public static ObservableCollection<StageWrapper> Wrappers { get; } = new();

    /// <summary>
    /// Dictionary mapping AxisType to the StageWrapper that controls it
    /// </summary>
    public static System.Collections.Generic.Dictionary<AxisType, StageWrapper> AxisToWrapperMap { get; } = new();

    /// <summary>
    /// Overall status summary
    /// </summary>
    public static string OverallStatus => $"Stages: {Wrappers.Count}, Connected: {Wrappers.Count(w => w.IsConnected)}";

    /// <summary>
    /// Get all connected stage wrappers
    /// </summary>
    public static System.Collections.Generic.IEnumerable<StageWrapper> GetConnectedWrappers()
    {
        return Wrappers.Where(w => w.IsConnected);
    }

    /// <summary>
    /// Get the wrapper for a specific axis
    /// </summary>
    public static StageWrapper? GetWrapperForAxis(AxisType axis)
    {
        return AxisToWrapperMap.TryGetValue(axis, out var wrapper) ? wrapper : null;
    }

    /// <summary>
    /// Get all connected stage controllers
    /// </summary>
    public static System.Collections.Generic.IEnumerable<global::StageController> GetConnectedControllers()
    {
        return Wrappers.Where(w => w.IsConnected)
            .Select(w => w.Controller)
            .Where(c => c != null)!;
    }

    /// <summary>
    /// Check if any stage is connected
    /// </summary>
    public static bool HasConnectedStages => Wrappers.Any(w => w.IsConnected);

    /// <summary>
    /// Get total number of connected axes
    /// </summary>
    public static int TotalConnectedAxes => AxisToWrapperMap.Count;

    /// <summary>
    /// Initialize default stages
    /// </summary>
    public static void InitializeDefaultStages()
    {
        Wrappers.Clear();
        AxisToWrapperMap.Clear();

        // Add default PI stage (axes determined after connection)
        var wrapper1 = StageWrapper.Create(1, StageHardwareType.PI);

        // Subscribe to property changes to forward them to UI
        wrapper1.PropertyChanged += (s, e) => WrapperPropertyChanged?.Invoke(s, e);

        Wrappers.Add(wrapper1);
    }

    /// <summary>
    /// Add a new stage wrapper
    /// </summary>
    public static StageWrapper AddStage()
    {
        int maxId = Wrappers.Count > 0 ? Wrappers.Max(w => w.Id) : 0;
        var newWrapper = StageWrapper.Create(maxId + 1);

        // Subscribe to property changes to forward them to UI
        newWrapper.PropertyChanged += (s, e) => WrapperPropertyChanged?.Invoke(s, e);

        Wrappers.Add(newWrapper);
        return newWrapper;
    }

    /// <summary>
    /// Remove a stage wrapper
    /// </summary>
    public static bool RemoveStage(StageWrapper wrapper)
    {
        // Disconnect if connected
        if (wrapper.IsConnected)
        {
            wrapper.Disconnect();
        }

        // Remove from axis map
        var axesToRemove = AxisToWrapperMap
            .Where(kvp => kvp.Value == wrapper)
            .Select(kvp => kvp.Key)
            .ToList();
        foreach (var axis in axesToRemove)
        {
            AxisToWrapperMap.Remove(axis);
        }

        return Wrappers.Remove(wrapper);
    }

    /// <summary>
    /// Remove a stage wrapper by StageInstance
    /// </summary>
    public static bool RemoveStage(StageInstance instance)
    {
        var wrapper = Wrappers.FirstOrDefault(w => w.Configuration == instance);
        return wrapper != null ? RemoveStage(wrapper) : false;
    }

    /// <summary>
    /// Disconnect all stages
    /// </summary>
    public static void DisconnectAll()
    {
        foreach (var wrapper in Wrappers.ToList())
        {
            wrapper.Disconnect();
        }
    }

    /// <summary>
    /// Rebuild the axis-to-wrapper mapping based on connected stages
    /// </summary>
    public static void RebuildAxisToWrapperMap()
    {
        AxisToWrapperMap.Clear();

        Console.WriteLine("[StageManager] Rebuilding axis-to-wrapper map...");

        foreach (var wrapper in Wrappers.Where(w => w.IsConnected))
        {
            Console.WriteLine($"[StageManager] Stage {wrapper.Id}: EnabledAxes={wrapper.EnabledAxes.Length}, AvailableAxes={wrapper.AvailableAxes.Length}");
            Console.WriteLine($"[StageManager] Stage {wrapper.Id}: EnabledAxes=[{string.Join(", ", wrapper.EnabledAxes)}]");
            Console.WriteLine($"[StageManager] Stage {wrapper.Id}: AvailableAxes=[{string.Join(", ", wrapper.AvailableAxes)}]");

            int controllerIndex = 0;
            foreach (var axis in wrapper.EnabledAxes)
            {
                if (controllerIndex < wrapper.AvailableAxes.Length)
                {
                    AxisToWrapperMap[axis] = wrapper;
                    Console.WriteLine($"[StageManager] Mapped {axis} -> Stage {wrapper.Id} (controller index {controllerIndex})");
                    controllerIndex++;
                }
            }
        }

        Console.WriteLine($"[StageManager] Total mappings: {AxisToWrapperMap.Count}");
    }

    /// <summary>
    /// Check all stage connections and update status
    /// </summary>
    public static void CheckAllConnections()
    {
        foreach (var wrapper in Wrappers.ToList())
        {
            wrapper.CheckConnection();
        }

        // Rebuild axis map when connections change
        RebuildAxisToWrapperMap();
    }

    /// <summary>
    /// Force a UI refresh by raising CollectionChanged event for the Wrappers collection
    /// This is useful when individual items change but the collection itself doesn't
    /// </summary>
    public static void RefreshUI()
    {
        // Force the collection to raise a reset event
        var wrappers = Wrappers.ToList();
        Wrappers.Clear();
        foreach (var wrapper in wrappers)
        {
            Wrappers.Add(wrapper);
        }
    }
}
