namespace ScreenSubtitleTranslator.Settings;

public interface IApiKeyStore
{
    Task<string?> ReadAsync(CancellationToken cancellationToken);

    Task SaveAsync(string apiKey, CancellationToken cancellationToken);

    Task ClearAsync(CancellationToken cancellationToken);
}
