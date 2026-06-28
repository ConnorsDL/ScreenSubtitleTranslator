using ScreenSubtitleTranslator.Translation;

namespace ScreenSubtitleTranslator.ViewModels;

public static class LanguageCatalog
{
    public static IReadOnlyList<LanguageOption> SourceLanguages { get; } = Array.AsReadOnly(
        new[]
        {
            new LanguageOption("en-US", "English (US)"),
            new LanguageOption("en-GB", "English (UK)"),
            new LanguageOption("de-DE", "German"),
            new LanguageOption("zh-CN", "Chinese"),
            new LanguageOption("ja-JP", "Japanese"),
            new LanguageOption("ko-KR", "Korean"),
            new LanguageOption("fr-FR", "French"),
            new LanguageOption("es-ES", "Spanish"),
            new LanguageOption("it-IT", "Italian")
        });

    public static IReadOnlyList<LanguageOption> TargetLanguages { get; } = Array.AsReadOnly(
        new[]
        {
            new LanguageOption("zh-CN", "Chinese Simplified"),
            new LanguageOption("zh-TW", "Chinese Traditional"),
            new LanguageOption("en", "English"),
            new LanguageOption("de", "German"),
            new LanguageOption("ja", "Japanese"),
            new LanguageOption("ko", "Korean"),
            new LanguageOption("fr", "French"),
            new LanguageOption("es", "Spanish"),
            new LanguageOption("it", "Italian")
        });
}
