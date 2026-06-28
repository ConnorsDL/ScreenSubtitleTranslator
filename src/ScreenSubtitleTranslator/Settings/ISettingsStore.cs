namespace ScreenSubtitleTranslator.Settings;

public interface ISettingsStore
{
    Task<UserSettings> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(UserSettings settings, CancellationToken cancellationToken);
}
