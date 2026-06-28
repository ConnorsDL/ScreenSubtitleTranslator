namespace ScreenSubtitleTranslator.AudioCapture;

public sealed class AudioCaptureStateChangedEventArgs : EventArgs
{
    public AudioCaptureStateChangedEventArgs(
        AudioCaptureState state,
        string? message = null,
        Exception? exception = null,
        AudioCaptureErrorCode errorCode = AudioCaptureErrorCode.None,
        string? deviceName = null,
        string? deviceId = null)
    {
        State = state;
        Message = message;
        Exception = exception;
        ErrorCode = errorCode;
        DeviceName = deviceName;
        DeviceId = deviceId;
    }

    public AudioCaptureState State { get; }

    public string? Message { get; }

    public Exception? Exception { get; }

    public AudioCaptureErrorCode ErrorCode { get; }

    public string? DeviceName { get; }

    public string? DeviceId { get; }
}
