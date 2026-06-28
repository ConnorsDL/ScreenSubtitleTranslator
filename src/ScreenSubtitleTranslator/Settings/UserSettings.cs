using ScreenSubtitleTranslator.AudioCapture;
using ScreenSubtitleTranslator.SpeechRecognition;
using ScreenSubtitleTranslator.Translation;

namespace ScreenSubtitleTranslator.Settings;

public sealed record UserSettings(
    AudioCaptureOptions AudioCapture,
    SpeechRecognitionOptions SpeechRecognition,
    TranslationOptions Translation,
    OverlaySettings Overlay,
    SubtitleDisplayMode SubtitleDisplayMode)
{
    public static UserSettings CreateDefault() => new(
        AudioCaptureOptions.CreateDefault(),
        SpeechRecognitionOptions.CreateDefault(),
        TranslationOptions.CreateDefault(),
        OverlaySettings.Default,
        SubtitleDisplayMode.EnglishAndChinese);
}
