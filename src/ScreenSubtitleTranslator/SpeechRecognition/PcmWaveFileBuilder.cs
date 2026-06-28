using System.Buffers.Binary;
using System.Text;

namespace ScreenSubtitleTranslator.SpeechRecognition;

public static class PcmWaveFileBuilder
{
    public static byte[] BuildPcm16MonoWave(byte[] pcm16MonoData)
    {
        ArgumentNullException.ThrowIfNull(pcm16MonoData);

        const int riffHeaderSize = 44;
        const short audioFormatPcm = 1;
        const short channelCount = Pcm16MonoAudioFrameConverter.TargetChannelCount;
        const short bitsPerSample = Pcm16MonoAudioFrameConverter.TargetBitsPerSample;
        const int sampleRate = Pcm16MonoAudioFrameConverter.TargetSampleRate;
        const short blockAlign = channelCount * bitsPerSample / 8;
        const int byteRate = sampleRate * blockAlign;

        var waveData = new byte[riffHeaderSize + pcm16MonoData.Length];
        WriteAscii(waveData, 0, "RIFF");
        BinaryPrimitives.WriteInt32LittleEndian(waveData.AsSpan(4), waveData.Length - 8);
        WriteAscii(waveData, 8, "WAVE");
        WriteAscii(waveData, 12, "fmt ");
        BinaryPrimitives.WriteInt32LittleEndian(waveData.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(waveData.AsSpan(20), audioFormatPcm);
        BinaryPrimitives.WriteInt16LittleEndian(waveData.AsSpan(22), channelCount);
        BinaryPrimitives.WriteInt32LittleEndian(waveData.AsSpan(24), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(waveData.AsSpan(28), byteRate);
        BinaryPrimitives.WriteInt16LittleEndian(waveData.AsSpan(32), blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(waveData.AsSpan(34), bitsPerSample);
        WriteAscii(waveData, 36, "data");
        BinaryPrimitives.WriteInt32LittleEndian(waveData.AsSpan(40), pcm16MonoData.Length);
        pcm16MonoData.CopyTo(waveData.AsSpan(riffHeaderSize));

        return waveData;
    }

    private static void WriteAscii(byte[] buffer, int offset, string value)
    {
        Encoding.ASCII.GetBytes(value, buffer.AsSpan(offset, value.Length));
    }
}
