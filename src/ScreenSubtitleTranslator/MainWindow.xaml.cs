using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using ScreenSubtitleTranslator.Overlay;
using ScreenSubtitleTranslator.Pipeline;
using ScreenSubtitleTranslator.Settings;
using ScreenSubtitleTranslator.ViewModels;

namespace ScreenSubtitleTranslator;

public partial class MainWindow : Window
{
    private readonly WpfSubtitleOverlayController _overlayController;
    private readonly WpfApiKeyConfigurationDialogService _apiKeyDialogService;
    private readonly MainWindowViewModel _viewModel;
    private bool _isClosing;

    public MainWindow()
    {
        InitializeComponent();

        _overlayController = new WpfSubtitleOverlayController(Dispatcher);
        var pipeline = new SubtitlePipelineService(_overlayController);
        var settingsStore = new LocalUserSettingsStore();
        var apiKeyManager = new OpenAIApiKeyManager(
            new WindowsCredentialManagerApiKeyStore(),
            new WindowsEnvironmentVariableAccessor());
        _apiKeyDialogService = new WpfApiKeyConfigurationDialogService(apiKeyManager, () => this);
        _viewModel = new MainWindowViewModel(
            pipeline,
            settingsStore,
            apiKeyManager,
            _apiKeyDialogService,
            Dispatcher);
        DataContext = _viewModel;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_isClosing)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        _isClosing = true;
        await _viewModel.DisposeAsync();
        _apiKeyDialogService.Dispose();
        _overlayController.Dispose();
        await Dispatcher.InvokeAsync(Close, DispatcherPriority.Background);
    }
}
