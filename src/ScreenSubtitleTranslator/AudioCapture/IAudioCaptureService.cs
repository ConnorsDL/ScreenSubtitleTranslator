namespace ScreenSubtitleTranslator.AudioCapture;

public interface IAudioCaptureService
{
    event EventHandler<AudioCaptureStateChangedEventArgs>? StateChanged;

    AudioCaptureState State { get; }

    bool IsCapturing { get; }

    Task StartAsync(AudioCaptureOptions options, IAudioFrameSink frameSink, CancellationToken cancellationToken);

    Task PauseAsync(CancellationToken cancellationToken);

    Task ResumeAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}
