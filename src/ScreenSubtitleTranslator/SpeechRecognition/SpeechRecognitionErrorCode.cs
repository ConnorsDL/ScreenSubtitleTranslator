namespace ScreenSubtitleTranslator.SpeechRecognition;

public enum SpeechRecognitionErrorCode
{
    None,
    ApiKeyMissing,
    RegionMissing,
    NetworkFailure,
    EmptyRecognitionResult,
    AudioFormatMismatch,
    ServiceCanceled,
    ServiceInitializationFailed
}
