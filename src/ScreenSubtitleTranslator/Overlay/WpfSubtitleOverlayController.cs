using System.Windows.Threading;

namespace ScreenSubtitleTranslator.Overlay;

public sealed class WpfSubtitleOverlayController : ISubtitleOverlayController, IDisposable
{
    private readonly Dispatcher _dispatcher;
    private OverlayWindow? _window;
    private bool _disposed;

    public WpfSubtitleOverlayController(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public void Show()
    {
        Dispatch(() =>
        {
            var window = EnsureWindow();
            window.Show();
            window.ActivateTopmostWithoutFocus();
        });
    }

    public void Hide()
    {
        Dispatch(() => _window?.Hide());
    }

    public void Update(SubtitleOverlayState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        Dispatch(() =>
        {
            var window = EnsureWindow();
            window.ApplyState(state);
            if (!window.IsVisible)
            {
                window.Show();
            }

            window.ActivateTopmostWithoutFocus();
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Dispatch(() =>
        {
            _window?.Close();
            _window = null;
        });
    }

    private OverlayWindow EnsureWindow()
    {
        if (_window is not null)
        {
            return _window;
        }

        _window = new OverlayWindow();
        _window.Closed += (_, _) => _window = null;
        return _window;
    }

    private void Dispatch(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _dispatcher.InvokeAsync(action, DispatcherPriority.Normal);
    }
}
