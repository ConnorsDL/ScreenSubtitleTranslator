namespace ScreenSubtitleTranslator.Overlay;

public sealed record SubtitleOverlayState(
    string OriginalText,
    string TranslatedText,
    string SourceLanguage,
    string TargetLanguage,
    DateTimeOffset UpdatedAt)
{
    public static SubtitleOverlayState Empty(string sourceLanguage, string targetLanguage) => new(
        OriginalText: string.Empty,
        TranslatedText: string.Empty,
        SourceLanguage: sourceLanguage,
        TargetLanguage: targetLanguage,
        UpdatedAt: DateTimeOffset.Now);
}
