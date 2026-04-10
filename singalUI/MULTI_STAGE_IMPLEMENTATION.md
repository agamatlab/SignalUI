# Multi-Stage Support Implementation Summary

## What Has Been Implemented

### 1. New Model: `StageInstance.cs`
- Represents a single stage controller with assigned axes (X, Y, Z, Rx, Ry, Rz)
- Properties:
  - `Id`: Unique identifier
  - `HardwareType`: PI or Sigmakoki
  - `SigmakokiController`: Sigma Koki controller type (if applicable)
  - `IsConnected`: Connection status
  - `AvailableAxes`: Array of axes from the controller
  - `XEnabled`, `YEnabled`, `ZEnabled`, `RxEnabled`, `RyEnabled`, `RzEnabled`: Which axes this stage controls

### 2. New Model: `SigmakokiControllerType.cs`
- Enum for Sigma Koki controller types
- Extension methods for axes count and display names

### 3. ViewModel Updates: `CalibrationSetupViewModel.cs`
- New properties:
  - `StageInstances`: Collection of `StageInstance` objects
  - `OverallStageStatus`: Summary of all stages
  - `TotalConnectedAxes`: Count of connected axes
  - `_axisToStageMap`: Maps AxisType → (StageInstance, ControllerIndex)
  - `_stageControllers`: Dictionary of stage ID → StageController

- New commands:
  - `AddStageCommand`: Add a new stage instance
  - `RemoveStageCommand(StageInstance)`: Remove a stage
  - `ConnectStageCommand(StageInstance)`: Connect a specific stage
  - `DisconnectStageCommand(StageInstance)`: Disconnect a specific stage
  - `DisconnectAllStagesCommand`: Disconnect all stages

- New methods:
  - `InitializeStageInstances()`: Set up default stages
  - `RebuildAxisToStageMap()`: Build axis-to-stage mapping
  - `UpdateOverallStageStatus()`: Update status display

### 4. Factory Update: `StageControllerFactory.cs`
- Updated to accept `SigmakokiControllerType` parameter
- Creates appropriate controller with correct type

### 5. New Converters
- `StageHardwareToIndexConverter`: Hardware type ↔ ComboBox index
- `SigmakokiVisibleConverter`: Show/hide when Sigma Koki selected
- `SigmakokiControllerToIndexConverter`: Sigma Koki type ↔ ComboBox index
- `AxesArrayToStringConverter`: Array → comma-separated string
- `StageHardwareDisplayNameConverter`: Hardware type → display name

### 6. Legacy Support
- Old single-stage properties marked `[Obsolete]`
- Old methods renamed to `ConnectStageLegacy`, `DisconnectStageLegacy`
- Backward compatibility maintained

## What Remains to Be Done

### 1. Update the View: `CalibrationSetupView.axaml`

Replace the single-stage panel (lines ~381-470) with a multi-stage panel:

```xml
<!-- Multi-Stage Configuration Panel -->
<Border Grid.Row="0" Background="#252525"
        BorderBrush="#404040"
        BorderThickness="1"
        CornerRadius="8"
        Padding="16"
        Margin="0,0,0,12">
    <Grid RowDefinitions="Auto,Auto,Auto">
        <!-- Header -->
        <Grid Grid.Row="0" ColumnDefinitions="*,Auto" Margin="0,0,0,12">
            <TextBlock Grid.Column="0"
                       Text="Stage Controllers"
                       FontSize="16"
                       FontWeight="SemiBold"
                       Foreground="#e0e0e0"
                       VerticalAlignment="Center"/>
            <Button Grid.Column="1"
                    Content="+ Add Stage"
                    Command="{Binding AddStageCommand}"
                    Padding="10,6"
                    FontSize="12"
                    HorizontalAlignment="Right"/>
        </Grid>

        <!-- Overall Status -->
        <Border Grid.Row="1" Background="#1a1a1a"
                BorderBrush="#303030"
                BorderThickness="1"
                CornerRadius="4"
                Padding="10,8"
                Margin="0,0,0,12">
            <TextBlock Text="{Binding OverallStageStatus}"
                       FontSize="12"
                       Foreground="#00ff00"
                       TextWrapping="Wrap"/>
        </Border>

        <!-- Stage Instances List -->
        <ScrollViewer Grid.Row="2" MaxHeight="300">
            <ItemsControl ItemsSource="{Binding StageInstances}">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <!-- Stage instance card with hardware selection, axis checkboxes, connect/disconnect -->
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </ScrollViewer>
    </Grid>
</Border>
```

### 2. Update Movement Methods

The `MoveAxisLocal` and `MoveAllAxesLocal` methods need to be updated to use `_axisToStageMap`:

```csharp
// Instead of using CurrentStageController directly:
if (!_axisToStageMap.TryGetValue(axisConfig.Axis, out var stageMapping)) { ... }
var (stage, controllerIndex) = stageMapping;
controller.MoveRelative(controllerIndex, axisConfig.StepSize);
```

## How to Use Multi-Stage System

1. **Add Stages**: Click "+ Add Stage" button
2. **Configure Hardware**: Select PI or Sigma Koki for each stage
3. **Assign Axes**: Check which axes each stage controls (X, Y, Z, Rx, Ry, Rz)
4. **Connect**: Click "Connect" on each stage
5. **Move**: Use existing Move All Axes button - system automatically routes commands to correct stage

## Example Configuration

**Stage 1 (PI Controller)**: X, Y, Z enabled
**Stage 2 (Sigma Koki SHOT-304GS)**: Rx, Ry, Rz enabled

Result: 6-axis freedom system where:
- Moves to X, Y, Z go to Stage 1 (PI controller)
- Moves to Rx, Ry, Rz go to Stage 2 (Sigma Koki controller)
