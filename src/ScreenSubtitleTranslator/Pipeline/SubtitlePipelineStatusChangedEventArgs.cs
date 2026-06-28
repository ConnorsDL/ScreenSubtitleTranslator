namespace ScreenSubtitleTranslator.Pipeline;

public sealed class SubtitlePipelineStatusChangedEventArgs : EventArgs
{
    public SubtitlePipelineStatusChangedEventArgs(
        string moduleName,
        string state,
        string details,
        string? errorCode = null,
        string? deviceName = null)
    {
        ModuleName = moduleName;
        State = state;
        Details = details;
        ErrorCode = errorCode;
        DeviceName = deviceName;
    }

    public string ModuleName { get; }

    public string State { get; }

    public string Details { get; }

    public string? ErrorCode { get; }

    public string? DeviceName { get; }
}
