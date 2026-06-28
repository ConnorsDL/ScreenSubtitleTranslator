namespace ScreenSubtitleTranslator.AudioCapture;

public sealed record AudioCaptureOptions(
    bool UseWasapiLoopback,
    string? DeviceId,
    int SampleRate,
    int ChannelCount)
{
    public static AudioCaptureOptions CreateDefault() => new(
        UseWasapiLoopback: true,
        DeviceId: null,
        SampleRate: 48000,
        ChannelCount: 2);
}
