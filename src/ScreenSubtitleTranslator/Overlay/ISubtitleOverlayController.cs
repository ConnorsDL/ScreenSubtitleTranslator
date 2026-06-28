namespace ScreenSubtitleTranslator.Overlay;

public interface ISubtitleOverlayController
{
    void Show();

    void Hide();

    void Update(SubtitleOverlayState state);
}
