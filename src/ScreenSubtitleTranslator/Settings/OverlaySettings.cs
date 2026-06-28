namespace ScreenSubtitleTranslator.Settings;

public sealed record OverlaySettings(
    bool ShowOnStartup,
    double FontSize,
    double Opacity,
    double SubtitleWidth,
    double? Left,
    double? Top,
    bool IsClickThrough,
    string Theme)
{
    public static OverlaySettings Default => new(
        ShowOnStartup: true,
        FontSize: 30,
        Opacity: 0.9,
        SubtitleWidth: 920,
        Left: null,
        Top: null,
        IsClickThrough: true,
        Theme: "Dark");
}
