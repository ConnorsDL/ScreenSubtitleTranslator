using ScreenSubtitleTranslator.AudioCapture;

namespace ScreenSubtitleTranslator.SpeechRecognition;

public sealed class Pcm16MonoAudioFrameConverter
{
    public const int TargetSampleRate = 16000;
    public const byte TargetBitsPerSample = 16;
    public const byte TargetChannelCount = 1;

    private double _sourcePosition;

    public byte[] Convert(AudioFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (frame.SampleRate <= 0)
        {
            throw new SpeechRecognitionException(
                SpeechRecognitionErrorCode.AudioFormatMismatch,
                $"Invalid audio sample rate: {frame.SampleRate}.");
        }

        if (frame.ChannelCount <= 0)
        {
            throw new SpeechRecognitionException(
                SpeechRecognitionErrorCode.AudioFormatMismatch,
                $"Invalid audio channel count: {frame.ChannelCount}.");
        }

        var monoSamples = ConvertToMonoFloat(frame);
        if (monoSamples.Length == 0)
        {
            return Array.Empty<byte>();
        }

        var resampled = ResampleToTargetRate(monoSamples, frame.SampleRate);
        return ConvertFloatToPcm16(resampled);
    }

    private static float[] ConvertToMonoFloat(AudioFrame frame)
    {
        var bytesPerSample = frame.SampleFormat switch
        {
            AudioSampleFormat.IeeeFloat32 => 4,
            AudioSampleFormat.Pcm16 => 2,
            AudioSampleFormat.Pcm24 => 3,
            AudioSampleFormat.Pcm32 => 4,
            _ => throw new SpeechRecognitionException(
                SpeechRecognitionErrorCode.AudioFormatMismatch,
                $"Unsupported audio sample format: {frame.SampleFormat}.")
        };

        var bytes = frame.PcmData.Span;
        var frameSize = bytesPerSample * frame.ChannelCount;
        if (frameSize <= 0 || bytes.Length < frameSize)
        {
            return Array.Empty<float>();
        }

        var sampleCount = bytes.Length / frameSize;
        var mono = new float[sampleCount];

        for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            var mixedSample = 0.0;
            var sampleBase = sampleIndex * frameSize;

            for (var channel = 0; channel < frame.ChannelCount; channel++)
            {
                var offset = sampleBase + channel * bytesPerSample;
                mixedSample += ReadSample(bytes.Slice(offset, bytesPerSample), frame.SampleFormat);
            }

            mono[sampleIndex] = (float)Math.Clamp(mixedSample / frame.ChannelCount, -1.0, 1.0);
        }

        return mono;
    }

    private float[] ResampleToTargetRate(float[] sourceSamples, int sourceSampleRate)
    {
        if (sourceSampleRate == TargetSampleRate)
        {
            return sourceSamples;
        }

        var sourceStep = (double)sourceSampleRate / TargetSampleRate;
        var output = new List<float>(Math.Max(1, (int)(sourceSamples.Length / sourceStep) + 1));

        while (_sourcePosition < sourceSamples.Length)
        {
            output.Add(sourceSamples[(int)_sourcePosition]);
            _sourcePosition += sourceStep;
        }

        _sourcePosition -= sourceSamples.Length;
        if (_sourcePosition < 0)
        {
            _sourcePosition = 0;
        }

        return output.ToArray();
    }

    private static byte[] ConvertFloatToPcm16(float[] samples)
    {
        var output = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            var clamped = Math.Clamp(samples[i], -1.0f, 1.0f);
            var pcm = (short)Math.Round(clamped * short.MaxValue);
            output[i * 2] = (byte)(pcm & 0xFF);
            output[i * 2 + 1] = (byte)((pcm >> 8) & 0xFF);
        }

        return output;
    }

    private static double ReadSample(ReadOnlySpan<byte> sampleBytes, AudioSampleFormat sampleFormat)
    {
        return sampleFormat switch
        {
            AudioSampleFormat.IeeeFloat32 => BitConverter.ToSingle(sampleBytes),
            AudioSampleFormat.Pcm16 => BitConverter.ToInt16(sampleBytes) / 32768.0,
            AudioSampleFormat.Pcm24 => ReadPcm24(sampleBytes) / 8388608.0,
            AudioSampleFormat.Pcm32 => BitConverter.ToInt32(sampleBytes) / 2147483648.0,
            _ => throw new SpeechRecognitionException(
                SpeechRecognitionErrorCode.AudioFormatMismatch,
                $"Unsupported audio sample format: {sampleFormat}.")
        };
    }

    private static int ReadPcm24(ReadOnlySpan<byte> sampleBytes)
    {
        var sample = sampleBytes[0] | (sampleBytes[1] << 8) | (sampleBytes[2] << 16);
        if ((sample & 0x800000) != 0)
        {
            sample |= unchecked((int)0xFF000000);
        }

        return sample;
    }
}
