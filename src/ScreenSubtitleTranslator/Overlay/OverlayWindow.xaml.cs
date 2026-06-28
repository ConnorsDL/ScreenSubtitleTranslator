using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ScreenSubtitleTranslator.Overlay;

public partial class OverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    public OverlayWindow()
    {
        InitializeComponent();
        ApplyState(SubtitleOverlayState.Empty("auto", "zh-CN"));
        Loaded += OnLoaded;
    }

    public void ApplyState(SubtitleOverlayState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(() => ApplyState(state), DispatcherPriority.Normal);
            return;
        }

        OriginalTextBlock.Text = state.OriginalText;
        OriginalTextBlock.Visibility = string.IsNullOrWhiteSpace(state.OriginalText)
            ? Visibility.Collapsed
            : Visibility.Visible;
        TranslatedTextBlock.Text = string.IsNullOrWhiteSpace(state.TranslatedText)
            ? string.Empty
            : state.TranslatedText;
        CenterAtPrimaryScreenBottom();
    }

    public void ActivateTopmostWithoutFocus()
    {
        Topmost = false;
        Topmost = true;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        EnableClickThrough();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        CenterAtPrimaryScreenBottom();
    }

    private void CenterAtPrimaryScreenBottom()
    {
        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(1040, workArea.Width * 0.8);
        UpdateLayout();

        var overlayHeight = ActualHeight > 0 ? ActualHeight : 160;
        Left = workArea.Left + Math.Max(0, (workArea.Width - Width) / 2);
        Top = workArea.Bottom - overlayHeight - 40;
    }

    private void EnableClickThrough()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var extendedStyle = GetWindowLong(handle, GwlExStyle);
        _ = SetWindowLong(
            handle,
            GwlExStyle,
            extendedStyle | WsExTransparent | WsExToolWindow | WsExNoActivate);
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
}
