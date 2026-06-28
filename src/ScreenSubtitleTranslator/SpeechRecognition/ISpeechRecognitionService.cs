using ScreenSubtitleTranslator.AudioCapture;

namespace ScreenSubtitleTranslator.SpeechRecognition;

public interface ISpeechRecognitionService
{
    IAsyncEnumerable<SpeechRecognitionResult> RecognizeAsync(
        IAsyncEnumerable<AudioFrame> audioFrames,
        SpeechRecognitionOptions options,
        CancellationToken cancellationToken);
}
