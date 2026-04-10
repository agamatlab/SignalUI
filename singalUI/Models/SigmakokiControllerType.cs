namespace singalUI.Models;

/// <summary>
/// Sigma Koki controller types supported by the application
/// </summary>
public enum SigmakokiControllerType
{
    GIP_101B,     // 1 axis
    GSC01,        // 1 axis
    SHOT_302GS,   // 2 axes
    HSC_103,      // 3 axes (default)
    SHOT_304GS,   // 4 axes
    Hit_MV,       // 4 axes
    PGC_04        // 4 axes
}

/// <summary>
/// Extension methods for SigmakokiControllerType
/// </summary>
public static class SigmakokiControllerTypeExtensions
{
    /// <summary>
    /// Get the number of axes for a controller type
    /// </summary>
    public static int GetAxesCount(this SigmakokiControllerType type)
    {
        return type switch
        {
            SigmakokiControllerType.GIP_101B => 1,
            SigmakokiControllerType.GSC01 => 1,
            SigmakokiControllerType.SHOT_302GS => 2,
            SigmakokiControllerType.HSC_103 => 3,
            SigmakokiControllerType.SHOT_304GS => 4,
            SigmakokiControllerType.Hit_MV => 4,
            SigmakokiControllerType.PGC_04 => 4,
            _ => 1
        };
    }

    /// <summary>
    /// Get display name for controller type
    /// </summary>
    public static string GetDisplayName(this SigmakokiControllerType type)
    {
        return type switch
        {
            SigmakokiControllerType.GIP_101B => "GIP-101B (1 axis)",
            SigmakokiControllerType.GSC01 => "GSC-01 (1 axis)",
            SigmakokiControllerType.SHOT_302GS => "SHOT-302GS (2 axes)",
            SigmakokiControllerType.HSC_103 => "HSC-103 (3 axes)",
            SigmakokiControllerType.SHOT_304GS => "SHOT-304GS (4 axes)",
            SigmakokiControllerType.Hit_MV => "Hit-MV (4 axes)",
            SigmakokiControllerType.PGC_04 => "PGC-04 (4 axes)",
            _ => type.ToString()
        };
    }
}
