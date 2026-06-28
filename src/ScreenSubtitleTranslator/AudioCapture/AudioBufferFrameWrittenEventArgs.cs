namespace ScreenSubtitleTranslator.AudioCapture;

public sealed class AudioBufferFrameWrittenEventArgs : EventArgs
{
    public AudioBufferFrameWrittenEventArgs(AudioFrame frame, long framesWritten, long bytesWritten)
    {
        Frame = frame;
        FramesWritten = framesWritten;
        BytesWritten = bytesWritten;
    }

    public AudioFrame Frame { get; }

    public long FramesWritten { get; }

    public long BytesWritten { get; }
}
