namespace ScreenSubtitleTranslator.Settings;

public interface IOpenAIApiKeyValidationService
{
    Task<ApiKeyValidationResult> ValidateAsync(string apiKey, CancellationToken cancellationToken);
}
