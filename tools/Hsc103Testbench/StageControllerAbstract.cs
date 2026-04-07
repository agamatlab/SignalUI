// Minimal abstract base for linking Hsc103RotationStageController from singalUI (no PI / Avalonia).
public abstract class StageController
{
    public abstract bool CheckConnected();
    public abstract void MoveRelative(int axes, double amount);
    public abstract string[] GetAxesList();
    public abstract void Connect();
    public abstract void Disconnect();
    public abstract double GetPosition(int axisIndex);
    public abstract double GetMinTravel(int axisIndex);
    public abstract double GetMaxTravel(int axisIndex);
    public abstract void ReferenceAxes();
    public abstract bool AreAxesReferenced();
}
