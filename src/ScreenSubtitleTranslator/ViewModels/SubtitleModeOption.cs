using ScreenSubtitleTranslator.Settings;

namespace ScreenSubtitleTranslator.ViewModels;

public sealed record SubtitleModeOption(SubtitleDisplayMode Mode, string DisplayName)
{
    public override string ToString() => DisplayName;
}
