using System.Threading.Channels;

namespace ScreenSubtitleTranslator.AudioCapture;

public sealed class AudioBuffer : IAudioBuffer, IAudioFrameSink
{
    private readonly Channel<AudioFrame> _channel;
    private long _bytesWritten;
    private long _framesWritten;

    public AudioBuffer()
        : this(AudioBufferOptions.Default)
    {
    }

    public AudioBuffer(AudioBufferOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options.Capacity), "Audio buffer capacity must be greater than zero.");
        }

        _channel = Channel.CreateBounded<AudioFrame>(new BoundedChannelOptions(options.Capacity)
        {
            SingleReader = false,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });
    }

    public event EventHandler<AudioBufferFrameWrittenEventArgs>? FrameWritten;

    public long FramesWritten => Interlocked.Read(ref _framesWritten);

    public long BytesWritten => Interlocked.Read(ref _bytesWritten);

    public ValueTask WriteAsync(AudioFrame frame, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(frame);

        if (!_channel.Writer.TryWrite(frame))
        {
            throw new InvalidOperationException("Audio buffer is closed.");
        }

        var framesWritten = Interlocked.Increment(ref _framesWritten);
        var bytesWritten = Interlocked.Add(ref _bytesWritten, frame.PcmData.Length);
        FrameWritten?.Invoke(this, new AudioBufferFrameWrittenEventArgs(frame, framesWritten, bytesWritten));
        return ValueTask.CompletedTask;
    }

    public ValueTask OnAudioFrameAsync(AudioFrame frame, CancellationToken cancellationToken)
    {
        return WriteAsync(frame, cancellationToken);
    }

    public IAsyncEnumerable<AudioFrame> ReadAllAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }

    public bool TryRead(out AudioFrame? frame)
    {
        return _channel.Reader.TryRead(out frame);
    }

    public void Complete(Exception? exception = null)
    {
        _channel.Writer.TryComplete(exception);
    }
}
