namespace ScreenSubtitleTranslator.SpeechRecognition;

public sealed record SpeechRecognitionOptions(
    string? ProviderId,
    string SourceLanguage,
    bool EnablePartialResults,
    string? ApiKey,
    string? Region,
    string ModelId,
    TimeSpan AudioChunkDuration,
    TimeSpan AudioOverlapDuration)
{
    public static SpeechRecognitionOptions CreateDefault() => new(
        ProviderId: "OpenAI",
        SourceLanguage: "en-US",
        EnablePartialResults: true,
        ApiKey: null,
        Region: null,
        ModelId: "gpt-4o-mini-transcribe",
        AudioChunkDuration: TimeSpan.FromSeconds(3),
        AudioOverlapDuration: TimeSpan.FromMilliseconds(400));
}
