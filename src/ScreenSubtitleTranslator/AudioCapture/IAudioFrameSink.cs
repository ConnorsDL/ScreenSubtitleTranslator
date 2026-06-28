namespace ScreenSubtitleTranslator.AudioCapture;

public interface IAudioFrameSink
{
    ValueTask OnAudioFrameAsync(AudioFrame frame, CancellationToken cancellationToken);
}
