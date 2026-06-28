namespace ScreenSubtitleTranslator.Translation;

public sealed record TranslationResult(
    string SourceText,
    string TranslatedText,
    string SourceLanguage,
    string TargetLanguage,
    DateTimeOffset TranslatedAt);
