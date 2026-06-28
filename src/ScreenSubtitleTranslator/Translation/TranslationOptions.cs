namespace ScreenSubtitleTranslator.Translation;

public sealed record TranslationOptions(
    string? ProviderId,
    string SourceLanguage,
    string TargetLanguage,
    string ModelId,
    TimeSpan RequestTimeout)
{
    public static TranslationOptions CreateDefault() => new(
        ProviderId: "OpenAI",
        SourceLanguage: "en",
        TargetLanguage: "zh-CN",
        ModelId: OpenAITranslationService.DefaultModel,
        RequestTimeout: OpenAITranslationService.DefaultRequestTimeout);
}
