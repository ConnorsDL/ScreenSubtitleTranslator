namespace ScreenSubtitleTranslator.Settings;

public interface IOpenAIApiKeyManager
{
    Task<OpenAIApiKeyState> GetStateAsync(CancellationToken cancellationToken);

    Task<OpenAIApiKeyState> SaveLocalKeyAsync(string apiKey, CancellationToken cancellationToken);

    Task<OpenAIApiKeyState> ClearLocalKeyAsync(CancellationToken cancellationToken);
}
