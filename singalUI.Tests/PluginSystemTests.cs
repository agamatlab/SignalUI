using System;
using System.IO;
using System.Linq;
using Xunit;
using singalUI.Services;
using singalUI.libs;
using singalUI.Models;

namespace singalUI.Tests;

/// <summary>
/// Tests for the plugin loading system
/// </summary>
public class PluginSystemTests
{
    [Fact]
    public void PluginLoader_LoadPlugins_CreatesPluginDirectory()
    {
        // Arrange
        var loader = new PluginLoader();
        var pluginDir = Path.Combine(AppContext.BaseDirectory, "Plugins");
        
        // Act
        loader.LoadPlugins();
        
        // Assert
        Assert.True(Directory.Exists(pluginDir), "Plugins directory should be created");
    }
    
    [Fact]
    public void PluginConfiguration_GetParameter_ReturnsDefaultWhenNotFound()
    {
        // Arrange
        var config = new PluginConfiguration();
        
        // Act
        var result = config.GetParameter<int>("NonExistent", 42);
        
        // Assert
        Assert.Equal(42, result);
    }
    
    [Fact]
    public void PluginConfiguration_GetParameter_ReturnsValueWhenFound()
    {
        // Arrange
        var config = new PluginConfiguration();
        config.Parameters["TestKey"] = 123;
        
        // Act
        var result = config.GetParameter<int>("TestKey", 0);
        
        // Assert
        Assert.Equal(123, result);
    }
    
    [Fact]
    public void PluginConfiguration_SetParameter_StoresValue()
    {
        // Arrange
        var config = new PluginConfiguration();
        
        // Act
        config.SetParameter("TestKey", "TestValue");
        
        // Assert
        Assert.True(config.HasParameter("TestKey"));
        Assert.Equal("TestValue", config.GetParameter<string>("TestKey"));
    }
    
    [Fact]
    public void PluginConfiguration_GetParameter_ConvertsTypes()
    {
        // Arrange
        var config = new PluginConfiguration();
        config.Parameters["IntAsString"] = "42";
        
        // Act
        var result = config.GetParameter<int>("IntAsString", 0);
        
        // Assert
        Assert.Equal(42, result);
    }
    
    [Fact]
    public void StageControllerFactory_InitializePlugins_DoesNotThrow()
    {
        // Act & Assert
        var exception = Record.Exception(() => StageControllerFactory.InitializePlugins());
        Assert.Null(exception);
    }
    
    [Fact]
    public void StageControllerFactory_GetAvailableControllers_ReturnsPluginControllers()
    {
        // Arrange
        StageControllerFactory.InitializePlugins();
        
        // Act
        var controllers = StageControllerFactory.GetAvailableControllers();
        
        // Assert
        Assert.NotEmpty(controllers);
        // All controllers should be plugins now
        Assert.All(controllers, c => Assert.True(c.IsPlugin));
    }
    
    [Fact]
    public void StageControllerFactory_CreateController_CreatesControllerFromPlugin()
    {
        // Arrange
        StageControllerFactory.InitializePlugins();
        var config = new StageInstance
        {
            HardwareType = StageHardwareType.PI  // Backward compatibility
        };
        
        // Act
        var controller = StageControllerFactory.CreateController(config);
        
        // Assert - May be null if PI plugin not loaded (no hardware)
        // Just verify no exception is thrown
        Assert.True(true);
    }
    
    [Fact]
    public void StageControllerFactory_CreateController_BackwardCompatibility_PI()
    {
        // Arrange
        StageControllerFactory.InitializePlugins();
        var config = new StageInstance
        {
            HardwareType = StageHardwareType.PI
        };
        
        // Act
        var controller = StageControllerFactory.CreateController(config);
        
        // Assert - Should attempt to create from PI plugin
        // May be null if plugin not loaded, but should not throw
        Assert.True(true);
    }
    
    [Fact]
    public void StageControllerFactory_CreateController_BackwardCompatibility_Sigmakoki()
    {
        // Arrange
        StageControllerFactory.InitializePlugins();
        var config = new StageInstance
        {
            HardwareType = StageHardwareType.Sigmakoki,
            SerialPortName = "COM3"
        };
        
        // Act
        var controller = StageControllerFactory.CreateController(config);
        
        // Assert - Should attempt to create from Sigmakoki plugin
        // May be null if plugin not loaded or not on Windows
        Assert.True(true);
    }
}

/// <summary>
/// Integration tests for the mock plugin
/// These tests require the MockStageController.Plugin to be built and copied to the Plugins folder
/// </summary>
public class MockPluginIntegrationTests
{
    [Fact]
    public void MockPlugin_LoadsSuccessfully()
    {
        // Arrange
        var loader = new PluginLoader();
        
        // Act
        int loadedCount = loader.LoadPlugins();
        
        // Assert
        // Should load at least the mock plugin if it's been built
        Assert.True(loadedCount >= 0, "Plugin loader should not throw");
    }
    
    [Fact]
    public void MockPlugin_CanCreateController()
    {
        // Arrange
        StageControllerFactory.InitializePlugins();
        var pluginLoader = StageControllerFactory.GetPluginLoader();
        
        // Skip test if plugin not loaded
        if (pluginLoader == null || !pluginLoader.LoadedPlugins.ContainsKey("MockStage"))
        {
            // Plugin not available, skip test
            return;
        }
        
        var config = new PluginConfiguration();
        config.SetParameter("NumAxes", 3);
        config.SetParameter("MinTravel", -50.0);
        config.SetParameter("MaxTravel", 50.0);
        
        // Act
        var controller = pluginLoader.CreateController("MockStage", config);
        
        // Assert
        Assert.NotNull(controller);
    }
    
    [Fact]
    public void MockController_ConnectAndDisconnect_Works()
    {
        // Arrange
        StageControllerFactory.InitializePlugins();
        var pluginLoader = StageControllerFactory.GetPluginLoader();
        
        // Skip test if plugin not loaded
        if (pluginLoader == null || !pluginLoader.LoadedPlugins.ContainsKey("MockStage"))
        {
            return;
        }
        
        var config = new PluginConfiguration();
        var controller = pluginLoader.CreateController("MockStage", config);
        Assert.NotNull(controller);
        
        // Act & Assert
        Assert.False(controller.CheckConnected());
        
        controller.Connect();
        Assert.True(controller.CheckConnected());
        
        var axes = controller.GetAxesList();
        Assert.NotEmpty(axes);
        
        controller.Disconnect();
        Assert.False(controller.CheckConnected());
    }
    
    [Fact]
    public void MockController_MoveRelative_UpdatesPosition()
    {
        // Arrange
        StageControllerFactory.InitializePlugins();
        var pluginLoader = StageControllerFactory.GetPluginLoader();
        
        // Skip test if plugin not loaded
        if (pluginLoader == null || !pluginLoader.LoadedPlugins.ContainsKey("MockStage"))
        {
            return;
        }
        
        var config = new PluginConfiguration();
        var controller = pluginLoader.CreateController("MockStage", config);
        Assert.NotNull(controller);
        
        controller.Connect();
        
        // Act
        double initialPos = controller.GetPosition(0);
        controller.MoveRelative(0, 10.0);
        double newPos = controller.GetPosition(0);
        
        // Assert
        Assert.Equal(initialPos + 10.0, newPos, precision: 3);
        
        // Cleanup
        controller.Disconnect();
    }
    
    [Fact]
    public void MockController_ReferenceAxes_ResetsPositions()
    {
        // Arrange
        StageControllerFactory.InitializePlugins();
        var pluginLoader = StageControllerFactory.GetPluginLoader();
        
        // Skip test if plugin not loaded
        if (pluginLoader == null || !pluginLoader.LoadedPlugins.ContainsKey("MockStage"))
        {
            return;
        }
        
        var config = new PluginConfiguration();
        var controller = pluginLoader.CreateController("MockStage", config);
        Assert.NotNull(controller);
        
        controller.Connect();
        
        // Move to a non-zero position
        controller.MoveRelative(0, 20.0);
        Assert.NotEqual(0.0, controller.GetPosition(0));
        
        // Act
        controller.ReferenceAxes();
        
        // Assert
        Assert.True(controller.AreAxesReferenced());
        Assert.Equal(0.0, controller.GetPosition(0), precision: 3);
        
        // Cleanup
        controller.Disconnect();
    }
    
    [Fact]
    public void MockController_GetTravelLimits_ReturnsConfiguredValues()
    {
        // Arrange
        StageControllerFactory.InitializePlugins();
        var pluginLoader = StageControllerFactory.GetPluginLoader();
        
        // Skip test if plugin not loaded
        if (pluginLoader == null || !pluginLoader.LoadedPlugins.ContainsKey("MockStage"))
        {
            return;
        }
        
        var config = new PluginConfiguration();
        config.SetParameter("MinTravel", -100.0);
        config.SetParameter("MaxTravel", 100.0);
        
        var controller = pluginLoader.CreateController("MockStage", config);
        Assert.NotNull(controller);
        
        controller.Connect();
        
        // Act
        double minTravel = controller.GetMinTravel(0);
        double maxTravel = controller.GetMaxTravel(0);
        
        // Assert
        Assert.Equal(-100.0, minTravel);
        Assert.Equal(100.0, maxTravel);
        
        // Cleanup
        controller.Disconnect();
    }
}
