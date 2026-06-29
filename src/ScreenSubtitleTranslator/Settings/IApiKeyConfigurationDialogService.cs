namespace ScreenSubtitleTranslator.Settings;

public interface IApiKeyConfigurationDialogService
{
    Task<bool> ShowAsync(CancellationToken cancellationToken);
}
