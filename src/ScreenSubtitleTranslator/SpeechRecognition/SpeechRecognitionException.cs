namespace ScreenSubtitleTranslator.SpeechRecognition;

public sealed class SpeechRecognitionException : Exception
{
    public SpeechRecognitionException(SpeechRecognitionErrorCode errorCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    public SpeechRecognitionErrorCode ErrorCode { get; }
}
