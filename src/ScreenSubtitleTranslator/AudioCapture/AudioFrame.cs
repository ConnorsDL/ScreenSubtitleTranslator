namespace ScreenSubtitleTranslator.AudioCapture;

public sealed record AudioFrame(
    ReadOnlyMemory<byte> PcmData,
    DateTimeOffset CapturedAt,
    TimeSpan Duration,
    int SampleRate,
    int ChannelCount,
    int BitsPerSample,
    AudioSampleFormat SampleFormat);
