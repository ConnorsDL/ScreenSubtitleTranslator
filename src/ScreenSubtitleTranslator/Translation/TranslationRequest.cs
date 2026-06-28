namespace ScreenSubtitleTranslator.Translation;

public sealed record TranslationRequest(
    string Text,
    string SourceLanguage,
    string TargetLanguage,
    string? PreviousSourceText = null,
    string? PreviousTranslatedText = null);
