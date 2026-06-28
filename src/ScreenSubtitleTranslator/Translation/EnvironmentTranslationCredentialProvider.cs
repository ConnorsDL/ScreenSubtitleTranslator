namespace ScreenSubtitleTranslator.Translation;

public sealed class EnvironmentTranslationCredentialProvider : ITranslationCredentialProvider
{
    public const string ApiKeyEnvironmentVariable = "OPENAI_API_KEY";

    public TranslationCredentials GetCredentials()
    {
        var apiKey = GetEnvironmentVariable(ApiKeyEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new TranslationException(
                TranslationErrorCode.ApiKeyMissing,
                $"OpenAI API key is missing. Set {ApiKeyEnvironmentVariable}.");
        }

        return new TranslationCredentials(apiKey);
    }

    private static string? GetEnvironmentVariable(string name)
    {
        return Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process)
            ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User)
            ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine);
    }
}
