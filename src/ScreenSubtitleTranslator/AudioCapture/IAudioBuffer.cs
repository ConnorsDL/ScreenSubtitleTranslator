namespace ScreenSubtitleTranslator.AudioCapture;

public interface IAudioBuffer
{
    ValueTask WriteAsync(AudioFrame frame, CancellationToken cancellationToken);

    IAsyncEnumerable<AudioFrame> ReadAllAsync(CancellationToken cancellationToken);
}
