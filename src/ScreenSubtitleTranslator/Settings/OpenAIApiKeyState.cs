namespace ScreenSubtitleTranslator.Settings;

public enum OpenAIApiKeySource
{
    None,
    EnvironmentVariable,
    WindowsCredentialManager
}

public sealed record OpenAIApiKeyState(string? ApiKey, OpenAIApiKeySource Source)
{
    public bool HasKey => !string.IsNullOrWhiteSpace(ApiKey);
}
