using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace singalUI.Models;

/// <summary>
/// Complete camera configuration that can be saved and loaded
/// </summary>
public class CameraConfig
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "Untitled";

    [JsonPropertyName("created")]
    public DateTime Created { get; set; } = DateTime.Now;

    [JsonPropertyName("modified")]
    public DateTime Modified { get; set; } = DateTime.Now;

    // Camera selection
    [JsonPropertyName("cameraSerial")]
    public string? CameraSerial { get; set; }

    [JsonPropertyName("ctiFile")]
    public string? CtiFile { get; set; }

    // Camera parameters
    [JsonPropertyName("exposure")]
    public double Exposure { get; set; } = 2500;

    [JsonPropertyName("gain")]
    public double Gain { get; set; } = 12;

    [JsonPropertyName("illumination")]
    public double Illumination { get; set; } = 80;

    [JsonPropertyName("fps")]
    public double Fps { get; set; } = 30;

    [JsonPropertyName("binning")]
    public BinningMode Binning { get; set; } = BinningMode.Bin1x1;

    // Pattern analysis
    [JsonPropertyName("patternType")]
    public int PatternType { get; set; } = 0; // 0 = CheckerBoard

    [JsonPropertyName("checkerBoardColumns")]
    public int CheckerBoardColumns { get; set; } = 9;

    [JsonPropertyName("checkerBoardRows")]
    public int CheckerBoardRows { get; set; } = 6;

    [JsonPropertyName("checkerBoardPitchX")]
    public double CheckerBoardPitchX { get; set; } = 1.0;

    [JsonPropertyName("checkerBoardPitchY")]
    public double CheckerBoardPitchY { get; set; } = 1.0;

    [JsonPropertyName("checkerBoardUnit")]
    public int CheckerBoardUnit { get; set; } = 0; // 0 = mm, 1 = μm, 2 = in

    [JsonPropertyName("patternHasCode")]
    public bool PatternHasCode { get; set; } = false;

    [JsonPropertyName("patternConfigType")]
    public string PatternConfigType { get; set; } = "Checker Board";

    [JsonPropertyName("patternPitchSize")]
    public double PatternPitchSize { get; set; } = 1.0;

    [JsonPropertyName("patternCompatibleLenses")]
    public string PatternCompatibleLenses { get; set; } = "Not specified";

    // Auto modes
    [JsonPropertyName("autoFocus")]
    public bool AutoFocus { get; set; } = false;

    [JsonPropertyName("autoIllumination")]
    public bool AutoIllumination { get; set; } = false;

    // FFT Focus Calculation
    [JsonPropertyName("enableFftFocus")]
    public bool EnableFftFocus { get; set; } = false;

    [JsonPropertyName("fftCalculationInterval")]
    public int FftCalculationInterval { get; set; } = 10;

    /// <summary>
    /// Convert to JSON string
    /// </summary>
    public string ToJson()
    {
        return JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    /// <summary>
    /// Parse from JSON string
    /// </summary>
    public static CameraConfig? FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<CameraConfig>(json);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Create a CameraConfig from CameraSetupViewModel properties
    /// </summary>
    public static CameraConfig FromViewModel(ViewModels.CameraSetupViewModel vm)
    {
        return new CameraConfig
        {
            Name = $"Config_{DateTime.Now:yyyyMMdd_HHmmss}",
            CameraSerial = vm.SelectedConnectedCamera,
            CtiFile = vm.SelectedCtiFile,
            Exposure = vm.CameraParameters.Exposure,
            Gain = vm.CameraParameters.Gain,
            Illumination = vm.CameraParameters.Illumination,
            Fps = vm.CameraParameters.Fps,
            Binning = vm.CameraParameters.Binning,
            PatternType = vm.SelectedPatternType,
            CheckerBoardColumns = vm.CheckerBoardColumns,
            CheckerBoardRows = vm.CheckerBoardRows,
            CheckerBoardPitchX = vm.CheckerBoardPitchX,
            CheckerBoardPitchY = vm.CheckerBoardPitchY,
            CheckerBoardUnit = vm.CheckerBoardUnit,
            PatternHasCode = vm.PatternHasCode,
            PatternConfigType = vm.PatternConfigType,
            PatternPitchSize = vm.PatternConfigPitchSize,
            PatternCompatibleLenses = vm.PatternCompatibleLenses,
            AutoFocus = vm.AutoFocus,
            AutoIllumination = vm.AutoIllumination,
            EnableFftFocus = vm.EnableFftFocus,
            FftCalculationInterval = vm.FftCalculationInterval
        };
    }

    /// <summary>
    /// Apply this config to a CameraSetupViewModel
    /// </summary>
    public void ApplyToViewModel(ViewModels.CameraSetupViewModel vm)
    {
        vm.SelectedConnectedCamera = CameraSerial;
        vm.SelectedCtiFile = CtiFile;
        vm.CameraParameters = new CameraParameters
        {
            Exposure = Exposure,
            Gain = Gain,
            Illumination = Illumination,
            Fps = Fps,
            Binning = Binning
        };
        vm.SelectedPatternType = PatternType;
        vm.CheckerBoardColumns = CheckerBoardColumns;
        vm.CheckerBoardRows = CheckerBoardRows;
        vm.CheckerBoardPitchX = CheckerBoardPitchX;
        vm.CheckerBoardPitchY = CheckerBoardPitchY;
        vm.CheckerBoardUnit = CheckerBoardUnit;
        vm.PatternHasCode = PatternHasCode;
        vm.PatternConfigType = PatternConfigType;
        vm.PatternConfigPitchSize = PatternPitchSize;
        vm.PatternCompatibleLenses = PatternCompatibleLenses;
        vm.AutoFocus = AutoFocus;
        vm.AutoIllumination = AutoIllumination;
        vm.EnableFftFocus = EnableFftFocus;
        vm.FftCalculationInterval = FftCalculationInterval;
    }

    public void ApplyPatternToViewModel(ViewModels.CameraSetupViewModel vm)
    {
        vm.SelectedPatternType = PatternType;
        vm.CheckerBoardColumns = CheckerBoardColumns;
        vm.CheckerBoardRows = CheckerBoardRows;
        vm.CheckerBoardPitchX = CheckerBoardPitchX;
        vm.CheckerBoardPitchY = CheckerBoardPitchY;
        vm.CheckerBoardUnit = CheckerBoardUnit;
        vm.PatternHasCode = PatternHasCode;
        vm.PatternConfigType = PatternConfigType;
        vm.PatternConfigPitchSize = PatternPitchSize;
        vm.PatternCompatibleLenses = PatternCompatibleLenses;
    }
}
