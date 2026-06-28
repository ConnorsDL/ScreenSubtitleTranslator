namespace ScreenSubtitleTranslator.SpeechRecognition;

public interface ISpeechRecognitionCredentialProvider
{
    SpeechRecognitionCredentials GetCredentials(SpeechRecognitionOptions options);
}
