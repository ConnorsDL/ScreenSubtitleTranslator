using ScreenSubtitleTranslator.AudioCapture;

var duration = args.Length > 0 && int.TryParse(args[0], out var parsedSeconds)
    ? Math.Max(1, parsedSeconds)
    : 30;

var buffer = new AudioBuffer(new AudioBufferOptions(Capacity: 512));
using var capture = new AudioCaptureService();
using var cancellation = new CancellationTokenSource();

var stats = new AudioStats();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

capture.StateChanged += (_, eventArgs) =>
{
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] state={eventArgs.State} code={eventArgs.ErrorCode} message={eventArgs.Message}");
};

buffer.FrameWritten += (_, eventArgs) =>
{
    stats.Add(eventArgs.Frame);
};

Console.WriteLine("AudioCaptureProbe");
Console.WriteLine("Play a YouTube video or any other system audio now.");
Console.WriteLine("This probe uses WASAPI Loopback render output, not microphone input.");
Console.WriteLine();

try
{
    await capture.StartAsync(AudioCaptureOptions.CreateDefault(), buffer, cancellation.Token);

    for (var second = 1; second <= duration && !cancellation.IsCancellationRequested; second++)
    {
        await Task.Delay(TimeSpan.FromSeconds(1), cancellation.Token);
        var snapshot = stats.TakeSnapshot();

        Console.WriteLine(
            $"t={second,3}s | sampleRate={snapshot.SampleRate,6} Hz | channels={snapshot.ChannelCount,2} | " +
            $"bytes/sec={snapshot.BytesPerSecond,8} | peak={snapshot.Peak:F3} | frames/sec={snapshot.FramesPerSecond,4}");
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("Probe cancelled.");
}
catch (AudioCaptureException exception)
{
    Console.WriteLine($"Audio capture failed. code={exception.ErrorCode}, message={exception.Message}");
}
finally
{
    await capture.StopAsync(CancellationToken.None);
}

sealed class AudioStats
{
    private readonly object _syncRoot = new();
    private long _bytesThisSecond;
    private int _framesThisSecond;
    private int _sampleRate;
    private int _channelCount;
    private double _peakThisSecond;

    public void Add(AudioFrame frame)
    {
        lock (_syncRoot)
        {
            _bytesThisSecond += frame.PcmData.Length;
            _framesThisSecond++;
            _sampleRate = frame.SampleRate;
            _channelCount = frame.ChannelCount;
            _peakThisSecond = Math.Max(_peakThisSecond, CalculatePeak(frame));
        }
    }

    public AudioStatsSnapshot TakeSnapshot()
    {
        lock (_syncRoot)
        {
            var snapshot = new AudioStatsSnapshot(
                _sampleRate,
                _channelCount,
                _bytesThisSecond,
                _framesThisSecond,
                _peakThisSecond);

            _bytesThisSecond = 0;
            _framesThisSecond = 0;
            _peakThisSecond = 0;
            return snapshot;
        }
    }

    private static double CalculatePeak(AudioFrame frame)
    {
        var span = frame.PcmData.Span;
        if (span.IsEmpty)
        {
            return 0;
        }

        return frame.SampleFormat switch
        {
            AudioSampleFormat.IeeeFloat32 => CalculateFloat32Peak(span),
            AudioSampleFormat.Pcm16 => CalculatePcm16Peak(span),
            AudioSampleFormat.Pcm24 => CalculatePcm24Peak(span),
            AudioSampleFormat.Pcm32 => CalculatePcm32Peak(span),
            _ => 0
        };
    }

    private static double CalculateFloat32Peak(ReadOnlySpan<byte> bytes)
    {
        var peak = 0.0;
        for (var i = 0; i + 3 < bytes.Length; i += 4)
        {
            var sample = BitConverter.ToSingle(bytes.Slice(i, 4));
            peak = Math.Max(peak, Math.Abs(sample));
        }

        return Math.Clamp(peak, 0, 1);
    }

    private static double CalculatePcm16Peak(ReadOnlySpan<byte> bytes)
    {
        var peak = 0.0;
        for (var i = 0; i + 1 < bytes.Length; i += 2)
        {
            var sample = BitConverter.ToInt16(bytes.Slice(i, 2));
            peak = Math.Max(peak, Math.Abs(sample / 32768.0));
        }

        return Math.Clamp(peak, 0, 1);
    }

    private static double CalculatePcm24Peak(ReadOnlySpan<byte> bytes)
    {
        var peak = 0.0;
        for (var i = 0; i + 2 < bytes.Length; i += 3)
        {
            var sample = bytes[i] | (bytes[i + 1] << 8) | (bytes[i + 2] << 16);
            if ((sample & 0x800000) != 0)
            {
                sample |= unchecked((int)0xFF000000);
            }

            peak = Math.Max(peak, Math.Abs(sample / 8388608.0));
        }

        return Math.Clamp(peak, 0, 1);
    }

    private static double CalculatePcm32Peak(ReadOnlySpan<byte> bytes)
    {
        var peak = 0.0;
        for (var i = 0; i + 3 < bytes.Length; i += 4)
        {
            var sample = BitConverter.ToInt32(bytes.Slice(i, 4));
            peak = Math.Max(peak, Math.Abs(sample / 2147483648.0));
        }

        return Math.Clamp(peak, 0, 1);
    }
}

sealed record AudioStatsSnapshot(
    int SampleRate,
    int ChannelCount,
    long BytesPerSecond,
    int FramesPerSecond,
    double Peak);
