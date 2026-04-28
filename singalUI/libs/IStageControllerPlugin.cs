using System;
using System.Collections.Generic;

namespace singalUI.libs;

/// <summary>
/// Interface that all stage controller plugins must implement
/// This allows the main application to discover and load plugins at runtime
/// </summary>
public interface IStageControllerPlugin
{
    /// <summary>Plugin metadata for identification and versioning</summary>
    PluginMetadata Metadata { get; }
    
    /// <summary>Create a new instance of the controller</summary>
    /// <param name="config">Configuration parameters for the controller</param>
    /// <returns>A new StageController instance</returns>
    StageController CreateController(PluginConfiguration config);
    
    /// <summary>Validate if this plugin can run on the current platform</summary>
    /// <returns>True if compatible, false otherwise</returns>
    bool IsCompatible();
}

/// <summary>
/// Plugin metadata for identification and versioning
/// </summary>
public class PluginMetadata
{
    /// <summary>Unique plugin identifier (e.g., "Newport", "Thorlabs")</summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>Human-readable display name</summary>
    public string DisplayName { get; set; } = string.Empty;
    
    /// <summary>Manufacturer name</summary>
    public string Manufacturer { get; set; } = string.Empty;
    
    /// <summary>Plugin version (e.g., "1.0.0")</summary>
    public string Version { get; set; } = string.Empty;
    
    /// <summary>Brief description of the controller</summary>
    public string Description { get; set; } = string.Empty;
    
    /// <summary>Plugin author/company</summary>
    public string Author { get; set; } = string.Empty;
    
    /// <summary>Supported platforms (e.g., "Windows", "Linux", "OSX")</summary>
    public string[] SupportedPlatforms { get; set; } = Array.Empty<string>();
    
    /// <summary>Required DLL files for this plugin</summary>
    public string[] RequiredDlls { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Configuration passed to plugin when creating controller
/// Allows flexible parameter passing without breaking interface
/// </summary>
public class PluginConfiguration
{
    /// <summary>Configuration parameters as key-value pairs</summary>
    public Dictionary<string, object> Parameters { get; set; } = new();
    
    /// <summary>
    /// Get a typed parameter value with optional default
    /// </summary>
    /// <typeparam name="T">Expected parameter type</typeparam>
    /// <param name="key">Parameter name</param>
    /// <param name="defaultValue">Default value if parameter not found</param>
    /// <returns>Parameter value or default</returns>
    public T GetParameter<T>(string key, T defaultValue = default!)
    {
        if (Parameters.TryGetValue(key, out var value))
        {
            if (value is T typedValue)
                return typedValue;
            
            // Try to convert
            try
            {
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        }
        return defaultValue;
    }
    
    /// <summary>
    /// Set a parameter value
    /// </summary>
    public void SetParameter<T>(string key, T value)
    {
        if (value != null)
        {
            Parameters[key] = value;
        }
    }
    
    /// <summary>
    /// Check if a parameter exists
    /// </summary>
    public bool HasParameter(string key)
    {
        return Parameters.ContainsKey(key);
    }
}
