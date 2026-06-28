namespace ScreenSubtitleTranslator.SpeechRecognition;

public sealed record SpeechRecognitionResult(
    string Text,
    string Language,
    bool IsFinal,
    DateTimeOffset RecognizedAt,
    int ChunkIndex = 0,
    TimeSpan AudioDuration = default,
    TimeSpan SttDuration = default);
