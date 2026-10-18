namespace Locksy.ViewModels;

public enum SettingsOperationStatus
{
    Succeeded,
    Cancelled,
    Failed
}

public sealed record SettingsOperationResult(SettingsOperationStatus Status, string? Message = null)
{
    public bool Succeeded => Status == SettingsOperationStatus.Succeeded;
    public bool Cancelled => Status == SettingsOperationStatus.Cancelled;
}
