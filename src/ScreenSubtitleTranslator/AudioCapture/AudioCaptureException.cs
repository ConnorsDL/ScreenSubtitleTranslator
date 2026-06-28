namespace ScreenSubtitleTranslator.AudioCapture;

public sealed class AudioCaptureException : Exception
{
    public AudioCaptureException(AudioCaptureErrorCode errorCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    public AudioCaptureErrorCode ErrorCode { get; }
}
