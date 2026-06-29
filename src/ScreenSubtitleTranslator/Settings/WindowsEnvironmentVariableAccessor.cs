namespace ScreenSubtitleTranslator.Settings;

public sealed class WindowsEnvironmentVariableAccessor : IEnvironmentVariableAccessor
{
    public string? Get(string name, EnvironmentVariableTarget target)
    {
        return Environment.GetEnvironmentVariable(name, target);
    }

    public void SetProcess(string name, string? value)
    {
        Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.Process);
    }
}
