namespace ScreenSubtitleTranslator.Settings;

public interface IEnvironmentVariableAccessor
{
    string? Get(string name, EnvironmentVariableTarget target);

    void SetProcess(string name, string? value);
}
