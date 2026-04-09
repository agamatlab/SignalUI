using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using singalUI.ViewModels;

namespace singalUI.Services;

/// <summary>
/// Persists DLL-facing defaults under <c>Archive/default_dll_parameters.txt</c> next to the executable.
/// </summary>
public static class DllDefaultParametersArchive
{
    public const string FileName = "default_dll_parameters.txt";

    public static string ArchiveDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Archive");

    public static string DefaultFilePath => Path.Combine(ArchiveDirectory, FileName);

    /// <summary>Load parameters from disk, or write factory defaults then load.</summary>
    public static void LoadOrInitialize(ConfigViewModel cfg)
    {
        try
        {
            Directory.CreateDirectory(ArchiveDirectory);
        }
        catch
        {
            // If we cannot create Archive, still apply hardcoded defaults.
            cfg.ApplyDefaultDllParameters();
            return;
        }

        if (!File.Exists(DefaultFilePath))
        {
            cfg.ApplyDefaultDllParameters();
            TrySave(cfg);
            return;
        }

        if (!TryLoadInto(cfg, out _))
        {
            cfg.ApplyDefaultDllParameters();
            TrySave(cfg);
        }
    }

    public static bool TrySave(ConfigViewModel cfg)
    {
        try
        {
            Directory.CreateDirectory(ArchiveDirectory);
            var lines = new List<string>
            {
                "# Default parameters for NanoMeas / pose DLLs (key=value). Edited by app from Camera pattern + intrinsics.",
                $"CameraPatternPresetIndex={cfg.CameraPatternPresetIndex.ToString(CultureInfo.InvariantCulture)}",
                $"SelectedPatternType={cfg.SelectedPatternType.ToString(CultureInfo.InvariantCulture)}",
                $"PitchX={cfg.PitchX.ToString(CultureInfo.InvariantCulture)}",
                $"PitchY={cfg.PitchY.ToString(CultureInfo.InvariantCulture)}",
                $"ComponentsX={cfg.ComponentsX.ToString(CultureInfo.InvariantCulture)}",
                $"ComponentsY={cfg.ComponentsY.ToString(CultureInfo.InvariantCulture)}",
                $"FocalLength={cfg.FocalLength.ToString(CultureInfo.InvariantCulture)}",
                $"PixelSize={cfg.PixelSize.ToString(CultureInfo.InvariantCulture)}",
                $"Fx={cfg.Fx.ToString(CultureInfo.InvariantCulture)}",
                $"Fy={cfg.Fy.ToString(CultureInfo.InvariantCulture)}",
                $"Cx={cfg.Cx.ToString(CultureInfo.InvariantCulture)}",
                $"Cy={cfg.Cy.ToString(CultureInfo.InvariantCulture)}",
                $"WindowSize={cfg.WindowSize.ToString(CultureInfo.InvariantCulture)}",
                $"CodePitchBlocks={cfg.CodePitchBlocks.ToString(CultureInfo.InvariantCulture)}",
                $"ImageWidth={cfg.ImageWidth.ToString(CultureInfo.InvariantCulture)}",
                $"ImageHeight={cfg.ImageHeight.ToString(CultureInfo.InvariantCulture)}",
            };
            File.WriteAllLines(DefaultFilePath, lines);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryLoadInto(ConfigViewModel cfg, out string? error)
    {
        error = null;
        try
        {
            foreach (var raw in File.ReadAllLines(DefaultFilePath))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                    continue;
                int eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;
                var key = line[..eq].Trim();
                var val = line[(eq + 1)..].Trim();
                ApplyKey(cfg, key, val);
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static void ApplyKey(ConfigViewModel cfg, string key, string val)
    {
        switch (key)
        {
            case "CameraPatternPresetIndex":
                if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pi))
                    cfg.CameraPatternPresetIndex = Math.Clamp(pi, 0, 1);
                break;
            case "SelectedPatternType":
                if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var spt))
                    cfg.SelectedPatternType = spt;
                break;
            case "PitchX":
                if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var px))
                    cfg.PitchX = px;
                break;
            case "PitchY":
                if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var py))
                    cfg.PitchY = py;
                break;
            case "ComponentsX":
                if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var cx))
                    cfg.ComponentsX = cx;
                break;
            case "ComponentsY":
                if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var cy))
                    cfg.ComponentsY = cy;
                break;
            case "FocalLength":
                if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var fl))
                    cfg.FocalLength = fl;
                break;
            case "PixelSize":
                if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var ps))
                    cfg.PixelSize = ps;
                break;
            case "Fx":
                if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var fx))
                    cfg.Fx = fx;
                break;
            case "Fy":
                if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var fy))
                    cfg.Fy = fy;
                break;
            case "Cx":
                if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var cxp))
                    cfg.Cx = cxp;
                break;
            case "Cy":
                if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var cyp))
                    cfg.Cy = cyp;
                break;
            case "WindowSize":
                if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var ws))
                    cfg.WindowSize = ws;
                break;
            case "CodePitchBlocks":
                if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var cpb))
                    cfg.CodePitchBlocks = cpb;
                break;
            case "ImageWidth":
                if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iw))
                    cfg.ImageWidth = iw;
                break;
            case "ImageHeight":
                if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ih))
                    cfg.ImageHeight = ih;
                break;
        }
    }
}
