namespace ScreenSubtitleTranslator.SpeechRecognition;

public sealed class EnvironmentSpeechRecognitionCredentialProvider : ISpeechRecognitionCredentialProvider
{
    public const string OpenAIApiKeyEnvironmentVariable = "OPENAI_API_KEY";
    public const string AzureApiKeyEnvironmentVariable = "AZURE_SPEECH_KEY";
    public const string AzureRegionEnvironmentVariable = "AZURE_SPEECH_REGION";

    public SpeechRecognitionCredentials GetCredentials(SpeechRecognitionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.Equals(options.ProviderId, "AzureSpeech", StringComparison.OrdinalIgnoreCase))
        {
            return GetAzureCredentials(options);
        }

        return GetOpenAICredentials(options);
    }

    private static SpeechRecognitionCredentials GetOpenAICredentials(SpeechRecognitionOptions options)
    {
        var apiKey = string.IsNullOrWhiteSpace(options.ApiKey)
            ? GetEnvironmentVariable(OpenAIApiKeyEnvironmentVariable)
            : options.ApiKey;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new SpeechRecognitionException(
                SpeechRecognitionErrorCode.ApiKeyMissing,
                $"OpenAI API key is missing. Set {OpenAIApiKeyEnvironmentVariable} or pass SpeechRecognitionOptions.ApiKey.");
        }

        return new SpeechRecognitionCredentials(apiKey, Region: null);
    }

    private static SpeechRecognitionCredentials GetAzureCredentials(SpeechRecognitionOptions options)
    {
        var apiKey = string.IsNullOrWhiteSpace(options.ApiKey)
            ? GetEnvironmentVariable(AzureApiKeyEnvironmentVariable)
            : options.ApiKey;
        var region = string.IsNullOrWhiteSpace(options.Region)
            ? GetEnvironmentVariable(AzureRegionEnvironmentVariable)
            : options.Region;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new SpeechRecognitionException(
                SpeechRecognitionErrorCode.ApiKeyMissing,
                $"Azure Speech API key is missing. Set {AzureApiKeyEnvironmentVariable} or pass SpeechRecognitionOptions.ApiKey.");
        }

        if (string.IsNullOrWhiteSpace(region))
        {
            throw new SpeechRecognitionException(
                SpeechRecognitionErrorCode.RegionMissing,
                $"Azure Speech region is missing. Set {AzureRegionEnvironmentVariable} or pass SpeechRecognitionOptions.Region.");
        }

        return new SpeechRecognitionCredentials(apiKey, region);
    }

    private static string? GetEnvironmentVariable(string name)
    {
        return Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process)
            ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User)
            ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine);
    }
}
