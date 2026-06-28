namespace ScreenSubtitleTranslator.Pipeline;

public sealed record SubtitlePipelineOptions(
    string SourceLanguage,
    string TargetLanguage,
    bool ShowOverlay,
    bool ShowOriginalText,
    TimeSpan AudioChunkDuration);
