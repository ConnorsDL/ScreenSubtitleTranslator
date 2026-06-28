namespace ScreenSubtitleTranslator.Translation;

public sealed class TranslationException : Exception
{
    public TranslationException(TranslationErrorCode errorCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    public TranslationErrorCode ErrorCode { get; }
}
