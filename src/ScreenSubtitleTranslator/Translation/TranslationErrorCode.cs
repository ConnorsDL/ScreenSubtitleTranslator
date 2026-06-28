namespace ScreenSubtitleTranslator.Translation;

public enum TranslationErrorCode
{
    None,
    ApiKeyMissing,
    NetworkFailure,
    EmptyResponse,
    Timeout,
    EmptySourceText,
    ServiceError
}
