using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using singalUI.libs;

namespace singalUI.Services;

/// <summary>
/// Loads stage controller plugins from external DLL files at runtime
/// Scans the StageControllers directory with subfolders for each controller
/// Each controller can be in its own folder with its dependencies
/// </summary>
public class PluginLoader
{
    private static readonly string StageControllersDirectory = 
        Path.Combine(AppContext.BaseDirectory, "StageControllers");
    
    // Legacy support for old Plugins directory
    private static readonly string LegacyPluginsDirectory = 
        Path.Combine(AppContext.BaseDirectory, "Plugins");
    
    private readonly Dictionary<string, IStageControllerPlugin> _loadedPlugins = new();
    
    /// <summary>
    /// All successfully loaded plugins
    /// </summary>
    public IReadOnlyDictionary<string, IStageControllerPlugin> LoadedPlugins => _loadedPlugins;
    
    /// <summary>
    /// Load all plugins from the StageControllers directory
    /// Scans subdirectories for DLLs (e.g., StageControllers/PI/PIController.dll)
    /// </summary>
    /// <returns>Number of plugins successfully loaded</returns>
    public int LoadPlugins()
    {
        Console.WriteLine($"[PluginLoader] Loading stage controllers from: {StageControllersDirectory}");
        
        int loadedCount = 0;
        
        // Try new StageControllers directory structure
        if (Directory.Exists(StageControllersDirectory))
        {
            loadedCount += LoadFromDirectory(StageControllersDirectory);
        }
        else
        {
            Console.WriteLine("[PluginLoader] StageControllers directory not found, creating...");
            Directory.CreateDirectory(StageControllersDirectory);
        }
        
        // Also check legacy Plugins directory for backward compatibility
        if (Directory.Exists(LegacyPluginsDirectory))
        {
            Console.WriteLine($"[PluginLoader] Also checking legacy Plugins directory: {LegacyPluginsDirectory}");
            loadedCount += LoadFromDirectory(LegacyPluginsDirectory);
        }
        
        Console.WriteLine($"[PluginLoader] Successfully loaded {loadedCount} controller(s) total");
        return loadedCount;
    }
    
    /// <summary>
    /// Load plugins from a specific directory
    /// </summary>
    private int LoadFromDirectory(string directory)
    {
        // Look for all DLLs in subdirectories
        // Pattern: StageControllers/PI/*.dll, StageControllers/Sigmakoki/*.dll, etc.
        var dllFiles = Directory.GetFiles(directory, "*.dll", SearchOption.AllDirectories);
        Console.WriteLine($"[PluginLoader] Found {dllFiles.Length} DLL file(s) in {Path.GetFileName(directory)}");
        
        int loadedCount = 0;
        foreach (var dllPath in dllFiles)
        {
            // Skip if it's a native DLL (common patterns)
            string fileName = Path.GetFileName(dllPath).ToLower();
            if (fileName.Contains("native") || 
                fileName.StartsWith("pi_gcs2") || 
                fileName.Contains("opencv") ||
                fileName.Contains("python") ||
                fileName.EndsWith(".resources.dll"))
            {
                Console.WriteLine($"[PluginLoader] Skipping native/resource DLL: {fileName}");
                continue;
            }
            
            try
            {
                if (LoadPlugin(dllPath))
                {
                    loadedCount++;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PluginLoader] Failed to load {Path.GetFileName(dllPath)}: {ex.Message}");
            }
        }
        
        return loadedCount;
    }
    
    /// <summary>
    /// Load a single plugin from a DLL file
    /// </summary>
    /// <param name="dllPath">Full path to the plugin DLL</param>
    /// <returns>True if plugin loaded successfully</returns>
    private bool LoadPlugin(string dllPath)
    {
        Console.WriteLine($"[PluginLoader] Loading: {Path.GetFileName(dllPath)}");
        
        try
        {
            // Load the assembly
            var assembly = Assembly.LoadFrom(dllPath);
            Console.WriteLine($"[PluginLoader] Assembly loaded: {assembly.FullName}");
            
            // Find types that implement IStageControllerPlugin
            var pluginTypes = assembly.GetTypes()
                .Where(t => typeof(IStageControllerPlugin).IsAssignableFrom(t) && 
                           !t.IsInterface && 
                           !t.IsAbstract)
                .ToList();
            
            if (pluginTypes.Count == 0)
            {
                Console.WriteLine($"[PluginLoader] No plugin types found in {Path.GetFileName(dllPath)}");
                return false;
            }
            
            Console.WriteLine($"[PluginLoader] Found {pluginTypes.Count} plugin type(s)");
            
            bool anyLoaded = false;
            foreach (var pluginType in pluginTypes)
            {
                try
                {
                    Console.WriteLine($"[PluginLoader] Instantiating plugin type: {pluginType.FullName}");
                    
                    // Create instance of the plugin
                    var plugin = (IStageControllerPlugin)Activator.CreateInstance(pluginType)!;
                    
                    Console.WriteLine($"[PluginLoader] Plugin instantiated: {plugin.Metadata.Name}");
                    
                    // Check compatibility
                    if (!plugin.IsCompatible())
                    {
                        Console.WriteLine($"[PluginLoader] Plugin {plugin.Metadata.Name} is not compatible with this platform");
                        continue;
                    }
                    
                    // Check for duplicate plugin names
                    if (_loadedPlugins.ContainsKey(plugin.Metadata.Name))
                    {
                        Console.WriteLine($"[PluginLoader] WARNING: Plugin {plugin.Metadata.Name} already loaded, skipping duplicate");
                        continue;
                    }
                    
                    // Register the plugin
                    _loadedPlugins[plugin.Metadata.Name] = plugin;
                    
                    Console.WriteLine($"[PluginLoader] ✓ Loaded plugin: {plugin.Metadata.DisplayName} v{plugin.Metadata.Version}");
                    Console.WriteLine($"[PluginLoader]   Manufacturer: {plugin.Metadata.Manufacturer}");
                    Console.WriteLine($"[PluginLoader]   Description: {plugin.Metadata.Description}");
                    Console.WriteLine($"[PluginLoader]   Author: {plugin.Metadata.Author}");
                    
                    anyLoaded = true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[PluginLoader] Failed to instantiate plugin type {pluginType.Name}: {ex.Message}");
                }
            }
            
            return anyLoaded;
        }
        catch (ReflectionTypeLoadException ex)
        {
            Console.WriteLine($"[PluginLoader] ReflectionTypeLoadException loading {Path.GetFileName(dllPath)}");
            foreach (var loaderEx in ex.LoaderExceptions)
            {
                Console.WriteLine($"[PluginLoader]   Loader exception: {loaderEx?.Message}");
            }
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PluginLoader] Exception loading {Path.GetFileName(dllPath)}: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Get a plugin by name
    /// </summary>
    /// <param name="name">Plugin name (case-sensitive)</param>
    /// <returns>Plugin instance or null if not found</returns>
    public IStageControllerPlugin? GetPlugin(string name)
    {
        return _loadedPlugins.TryGetValue(name, out var plugin) ? plugin : null;
    }
    
    /// <summary>
    /// Create a controller from a plugin
    /// </summary>
    /// <param name="pluginName">Name of the plugin</param>
    /// <param name="config">Configuration for the controller</param>
    /// <returns>StageController instance or null if plugin not found</returns>
    public StageController? CreateController(string pluginName, PluginConfiguration config)
    {
        var plugin = GetPlugin(pluginName);
        if (plugin == null)
        {
            Console.WriteLine($"[PluginLoader] Plugin not found: {pluginName}");
            return null;
        }
        
        try
        {
            Console.WriteLine($"[PluginLoader] Creating controller from plugin: {pluginName}");
            var controller = plugin.CreateController(config);
            Console.WriteLine($"[PluginLoader] Controller created successfully");
            return controller;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PluginLoader] Failed to create controller from plugin {pluginName}: {ex.Message}");
            Console.WriteLine($"[PluginLoader] Stack trace: {ex.StackTrace}");
            return null;
        }
    }
    
    /// <summary>
    /// Get all loaded plugin names
    /// </summary>
    public IEnumerable<string> GetPluginNames()
    {
        return _loadedPlugins.Keys;
    }
    
    /// <summary>
    /// Get count of loaded plugins
    /// </summary>
    public int PluginCount => _loadedPlugins.Count;
    
    /// <summary>
    /// Clear all loaded plugins
    /// </summary>
    public void ClearPlugins()
    {
        _loadedPlugins.Clear();
        Console.WriteLine("[PluginLoader] All plugins cleared");
    }
}
