namespace singalUI.Models;

/// <summary>
/// Stage hardware controller types supported by the application
/// </summary>
public enum StageHardwareType
{
    None,
    PI,           // Physik Instrumente controllers
    Sigmakoki,    // Sigma Koki controllers (SKSampleClass)
    SigmakokiRotationStage // HSC-103 COM, single rotation axis (θ / Rz), hsc103.py protocol
}
