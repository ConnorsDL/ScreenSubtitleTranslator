namespace ScreenSubtitleTranslator.AudioCapture;

public sealed record AudioBufferOptions(int Capacity)
{
    public static AudioBufferOptions Default => new(Capacity: 256);
}
