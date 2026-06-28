using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScreenSubtitleTranslator.Settings;

public sealed class LocalUserSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public LocalUserSettingsStore()
        : this(GetDefaultSettingsPath())
    {
    }

    public LocalUserSettingsStore(string settingsFilePath)
    {
        SettingsFilePath = string.IsNullOrWhiteSpace(settingsFilePath)
            ? throw new ArgumentException("Settings file path cannot be empty.", nameof(settingsFilePath))
            : settingsFilePath;
    }

    public string SettingsFilePath { get; }

    public async Task<UserSettings> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(SettingsFilePath))
        {
            return UserSettings.CreateDefault();
        }

        await using var stream = File.OpenRead(SettingsFilePath);
        var dto = await JsonSerializer.DeserializeAsync<LocalSettingsDto>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        return dto?.ToUserSettings() ?? UserSettings.CreateDefault();
    }

    public async Task SaveAsync(UserSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = Path.GetDirectoryName(SettingsFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var dto = LocalSettingsDto.FromUserSettings(settings);
        await using var stream = File.Create(SettingsFilePath);
        await JsonSerializer.SerializeAsync(stream, dto, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private static string GetDefaultSettingsPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "ScreenSubtitleTranslator", "settings.json");
    }

    private sealed record LocalSettingsDto(
        string SourceLanguage,
        string TargetLanguage,
        SubtitleDisplayMode SubtitleDisplayMode,
        int AudioChunkSeconds)
    {
        public static LocalSettingsDto FromUserSettings(UserSettings settings)
        {
            return new LocalSettingsDto(
                settings.SpeechRecognition.SourceLanguage,
                settings.Translation.TargetLanguage,
                settings.SubtitleDisplayMode,
                (int)Math.Clamp(settings.SpeechRecognition.AudioChunkDuration.TotalSeconds, 2, 5));
        }

        public UserSettings ToUserSettings()
        {
            var defaults = UserSettings.CreateDefault();
            var chunkSeconds = Math.Clamp(AudioChunkSeconds, 2, 5);
            var sourceLanguage = string.IsNullOrWhiteSpace(SourceLanguage) ? "en-US" : SourceLanguage;
            var targetLanguage = string.IsNullOrWhiteSpace(TargetLanguage) ? "zh-CN" : TargetLanguage;

            return defaults with
            {
                SpeechRecognition = defaults.SpeechRecognition with
                {
                    SourceLanguage = sourceLanguage,
                    AudioChunkDuration = TimeSpan.FromSeconds(chunkSeconds)
                },
                Translation = defaults.Translation with
                {
                    SourceLanguage = NormalizeSourceLanguage(sourceLanguage),
                    TargetLanguage = targetLanguage
                },
                SubtitleDisplayMode = SubtitleDisplayMode
            };
        }

        private static string NormalizeSourceLanguage(string language)
        {
            var trimmed = language.Trim();
            if (string.Equals(trimmed, "auto", StringComparison.OrdinalIgnoreCase))
            {
                return "en";
            }

            var separatorIndex = trimmed.IndexOf('-', StringComparison.Ordinal);
            return separatorIndex > 0
                ? trimmed[..separatorIndex].ToLowerInvariant()
                : trimmed.ToLowerInvariant();
        }
    }
}
