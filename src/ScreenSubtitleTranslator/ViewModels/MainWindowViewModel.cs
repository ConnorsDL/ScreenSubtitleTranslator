using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using ScreenSubtitleTranslator.Commands;
using ScreenSubtitleTranslator.Overlay;
using ScreenSubtitleTranslator.Pipeline;
using ScreenSubtitleTranslator.Settings;
using ScreenSubtitleTranslator.Translation;

namespace ScreenSubtitleTranslator.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase, IAsyncDisposable
{
    private const int MaxLogEntries = 100;
    private const string DiagnosticLogPathEnvironmentVariable = "SCREEN_SUBTITLE_DIAGNOSTIC_LOG";

    private readonly SubtitlePipelineService _pipeline;
    private readonly ISettingsStore _settingsStore;
    private readonly IOpenAIApiKeyManager _apiKeyManager;
    private readonly IApiKeyConfigurationDialogService _apiKeyDialogService;
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<string, PipelineModuleStatus> _moduleStatuses = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _diagnosticLogLock = new();
    private readonly string? _diagnosticLogPath;

    private LanguageOption _sourceLanguage;
    private LanguageOption _targetLanguage;
    private SubtitleModeOption _subtitleMode;
    private int _audioChunkSeconds = 3;
    private bool _isRunning;
    private string _statusMessage = "Ready.";
    private string _appStatus = "Idle";
    private string _apiKeyStatus = "Checking...";
    private bool _isApiKeyConfigured;
    private string _currentCaptureDeviceName = "Not capturing.";
    private string _deviceAlertMessage = string.Empty;
    private string _errorMessage = string.Empty;

    public MainWindowViewModel(
        SubtitlePipelineService pipeline,
        ISettingsStore settingsStore,
        IOpenAIApiKeyManager apiKeyManager,
        IApiKeyConfigurationDialogService apiKeyDialogService,
        Dispatcher dispatcher)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _apiKeyManager = apiKeyManager ?? throw new ArgumentNullException(nameof(apiKeyManager));
        _apiKeyDialogService = apiKeyDialogService ?? throw new ArgumentNullException(nameof(apiKeyDialogService));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _diagnosticLogPath = GetEnvironmentVariable(DiagnosticLogPathEnvironmentVariable);
        _pipeline.StatusChanged += OnPipelineStatusChanged;

        SourceLanguages = new ObservableCollection<LanguageOption>(LanguageCatalog.SourceLanguages);
        TargetLanguages = new ObservableCollection<LanguageOption>(LanguageCatalog.TargetLanguages);

        SubtitleModes = new ObservableCollection<SubtitleModeOption>
        {
            new(SubtitleDisplayMode.ChineseOnly, "只显示中文"),
            new(SubtitleDisplayMode.EnglishAndChinese, "显示英文 + 中文")
        };

        LatencyModes = new ObservableCollection<LatencyModeOption>
        {
            new(2, "Low Latency - 2s"),
            new(3, "Balanced - 3s (Recommended)"),
            new(4, "Stable - 4s"),
            new(5, "Extra Stable - 5s")
        };

        _sourceLanguage = SourceLanguages[0];
        _targetLanguage = TargetLanguages[0];
        _subtitleMode = SubtitleModes[1];

        RecentLogs = new ObservableCollection<string>();
        PipelineModules = new ObservableCollection<PipelineModuleStatus>
        {
            CreateModule("AudioCapture", "Idle", "Waiting."),
            CreateModule("SpeechRecognition", "Idle", "Waiting."),
            CreateModule("Translation", "Idle", "Waiting."),
            CreateModule("Overlay", "Idle", "Waiting."),
            CreateModule("Pipeline", "Idle", "Ready.")
        };

        StartCommand = new RelayCommand(async _ => await StartAsync(), _ => !IsRunning);
        StopCommand = new RelayCommand(async _ => await StopAsync(), _ => IsRunning);
        SaveSettingsCommand = new RelayCommand(async _ => await SaveSettingsFromCommandAsync());
        RefreshApiKeyStatusCommand = new RelayCommand(async _ => await RefreshApiKeyStatusAsync());
        ConfigureApiKeyCommand = new RelayCommand(
            async _ => await ConfigureApiKeyAsync(),
            _ => !IsRunning);

        AddLog("Application initialized.");
    }

    public ObservableCollection<LanguageOption> SourceLanguages { get; }

    public ObservableCollection<LanguageOption> TargetLanguages { get; }

    public ObservableCollection<SubtitleModeOption> SubtitleModes { get; }

    public ObservableCollection<LatencyModeOption> LatencyModes { get; }

    public ObservableCollection<PipelineModuleStatus> PipelineModules { get; }

    public ObservableCollection<string> RecentLogs { get; }

    public ICommand StartCommand { get; }

    public ICommand StopCommand { get; }

    public ICommand SaveSettingsCommand { get; }

    public ICommand RefreshApiKeyStatusCommand { get; }

    public ICommand ConfigureApiKeyCommand { get; }

    public LanguageOption SourceLanguage
    {
        get => _sourceLanguage;
        set
        {
            if (SetProperty(ref _sourceLanguage, value))
            {
                OnPropertyChanged(nameof(SelectedSettingsSummary));
            }
        }
    }

    public LanguageOption TargetLanguage
    {
        get => _targetLanguage;
        set
        {
            if (SetProperty(ref _targetLanguage, value))
            {
                OnPropertyChanged(nameof(SelectedSettingsSummary));
            }
        }
    }

    public SubtitleModeOption SubtitleMode
    {
        get => _subtitleMode;
        set
        {
            if (SetProperty(ref _subtitleMode, value))
            {
                OnPropertyChanged(nameof(SelectedSettingsSummary));
            }
        }
    }

    public int AudioChunkSeconds
    {
        get => _audioChunkSeconds;
        set
        {
            var clamped = Math.Clamp(value, 2, 5);
            if (SetProperty(ref _audioChunkSeconds, clamped))
            {
                OnPropertyChanged(nameof(SelectedSettingsSummary));
            }
        }
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(WindowStatus));
                RaiseCommandStates();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string AppStatus
    {
        get => _appStatus;
        private set
        {
            if (SetProperty(ref _appStatus, value))
            {
                OnPropertyChanged(nameof(WindowStatus));
            }
        }
    }

    public string ApiKeyStatus
    {
        get => _apiKeyStatus;
        private set => SetProperty(ref _apiKeyStatus, value);
    }

    public bool IsApiKeyConfigured
    {
        get => _isApiKeyConfigured;
        private set
        {
            if (SetProperty(ref _isApiKeyConfigured, value))
            {
                OnPropertyChanged(nameof(ApiKeyActionLabel));
            }
        }
    }

    public string ApiKeyActionLabel => IsApiKeyConfigured
        ? "Change API Key"
        : "Configure API Key";

    public string CurrentCaptureDeviceName
    {
        get => _currentCaptureDeviceName;
        private set => SetProperty(ref _currentCaptureDeviceName, value);
    }

    public string DeviceAlertMessage
    {
        get => _deviceAlertMessage;
        private set => SetProperty(ref _deviceAlertMessage, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public string WindowStatus => string.Equals(AppStatus, "Error", StringComparison.OrdinalIgnoreCase)
        ? "Error"
        : IsRunning
            ? "Running"
            : "Ready";

    public string SelectedSettingsSummary =>
        $"Source: {SourceLanguage.Code}; Target: {TargetLanguage.Code}; Mode: {SubtitleMode.DisplayName}; Chunk: {AudioChunkSeconds}s.";

    public async Task InitializeAsync()
    {
        try
        {
            var settings = await _settingsStore.LoadAsync(CancellationToken.None);
            ApplySettings(settings);
            AddLog("Settings loaded.");
        }
        catch (Exception exception)
        {
            AddLog($"Settings load failed: {exception.Message}");
        }

        try
        {
            var state = await RefreshApiKeyStatusAsync();
            if (!state.HasKey)
            {
                AddLog("No OpenAI API key was found. Opening configuration.");
                await _apiKeyDialogService.ShowAsync(CancellationToken.None);
                await RefreshApiKeyStatusAsync();
            }
        }
        catch (Exception exception)
        {
            SetError($"Could not check the OpenAI API key: {exception.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        _pipeline.StatusChanged -= OnPipelineStatusChanged;
        await _pipeline.DisposeAsync();
    }

    public async Task StartAsync()
    {
        OpenAIApiKeyState keyState;
        try
        {
            keyState = await RefreshApiKeyStatusAsync();
            if (!keyState.HasKey)
            {
                AddLog("Start blocked: no OpenAI API key is configured.");
                await _apiKeyDialogService.ShowAsync(CancellationToken.None);
                keyState = await RefreshApiKeyStatusAsync();
            }
        }
        catch (Exception exception)
        {
            SetError($"Could not check the OpenAI API key: {exception.Message}");
            return;
        }

        if (!keyState.HasKey)
        {
            SetError("An OpenAI API key is required. Use Configure API Key before starting.");
            return;
        }

        try
        {
            ErrorMessage = string.Empty;
            DeviceAlertMessage = string.Empty;
            CurrentCaptureDeviceName = "Detecting default playback device...";
            IsRunning = true;
            SetAppStatus("Capturing", "Starting pipeline...");
            AddLog("Start requested.");
            await _pipeline.StartAsync(CreateOptions(), CancellationToken.None);
            try
            {
                await SaveSettingsAsync(addSuccessLog: false);
            }
            catch (Exception exception)
            {
                AddLog($"Settings save failed: {exception.Message}");
            }
        }
        catch (Exception exception)
        {
            IsRunning = false;
            SetError(GetFriendlyError(exception.Message));
            UpdateModule("Pipeline", "Error", exception.Message);
        }
    }

    private async Task StopAsync()
    {
        try
        {
            StatusMessage = "Stopping...";
            AddLog("Stop requested.");
            await _pipeline.StopAsync();
            SetAppStatus("Idle", "Stopped.");
        }
        catch (Exception exception)
        {
            SetError(GetFriendlyError(exception.Message));
            UpdateModule("Pipeline", "Error", exception.Message);
        }
        finally
        {
            IsRunning = false;
        }
    }

    private async Task SaveSettingsFromCommandAsync()
    {
        try
        {
            await SaveSettingsAsync();
        }
        catch (Exception exception)
        {
            SetError($"Settings save failed: {exception.Message}");
        }
    }

    private async Task SaveSettingsAsync(bool addSuccessLog = true)
    {
        var settings = BuildSettings();
        await _settingsStore.SaveAsync(settings, CancellationToken.None);
        if (addSuccessLog)
        {
            AddLog("Settings saved.");
            StatusMessage = "Settings saved.";
        }
    }

    private UserSettings BuildSettings()
    {
        var defaults = UserSettings.CreateDefault();
        return defaults with
        {
            SpeechRecognition = defaults.SpeechRecognition with
            {
                SourceLanguage = SourceLanguage.Code,
                AudioChunkDuration = TimeSpan.FromSeconds(AudioChunkSeconds),
                ApiKey = null
            },
            Translation = defaults.Translation with
            {
                SourceLanguage = NormalizeSourceLanguage(SourceLanguage.Code),
                TargetLanguage = TargetLanguage.Code
            },
            SubtitleDisplayMode = SubtitleMode.Mode
        };
    }

    private void ApplySettings(UserSettings settings)
    {
        SelectSourceLanguage(settings.SpeechRecognition.SourceLanguage);
        SelectTargetLanguage(settings.Translation.TargetLanguage);
        SelectSubtitleMode(settings.SubtitleDisplayMode);
        AudioChunkSeconds = (int)Math.Clamp(settings.SpeechRecognition.AudioChunkDuration.TotalSeconds, 2, 5);
    }

    private SubtitlePipelineOptions CreateOptions()
    {
        return new SubtitlePipelineOptions(
            SourceLanguage.Code,
            TargetLanguage.Code,
            ShowOverlay: true,
            SubtitleMode.Mode == SubtitleDisplayMode.EnglishAndChinese,
            TimeSpan.FromSeconds(AudioChunkSeconds));
    }

    private void OnPipelineStatusChanged(object? sender, SubtitlePipelineStatusChangedEventArgs eventArgs)
    {
        DispatchToUi(() =>
        {
            UpdateModule(eventArgs.ModuleName, eventArgs.State, eventArgs.Details);
            var errorCode = string.IsNullOrWhiteSpace(eventArgs.ErrorCode)
                ? string.Empty
                : $" {eventArgs.ErrorCode}";
            AddLog($"{eventArgs.ModuleName} {eventArgs.State}{errorCode}: {eventArgs.Details}");

            if (string.Equals(eventArgs.ModuleName, "AudioCapture", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(eventArgs.DeviceName))
            {
                CurrentCaptureDeviceName = eventArgs.DeviceName;
            }

            if (string.Equals(eventArgs.State, "Error", StringComparison.OrdinalIgnoreCase)
                || string.Equals(eventArgs.State, "Faulted", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(eventArgs.ErrorCode, "DeviceSwitchDetected", StringComparison.OrdinalIgnoreCase))
                {
                    DeviceAlertMessage = "播放设备已切换，请 Stop 后重新 Start";
                }
                else if (string.Equals(eventArgs.ErrorCode, "DeviceDisconnected", StringComparison.OrdinalIgnoreCase))
                {
                    DeviceAlertMessage = "播放设备已断开，请重新连接后 Stop / Start";
                }

                SetError(GetFriendlyError(eventArgs.Details, eventArgs.ErrorCode));
                return;
            }

            switch (eventArgs.ModuleName)
            {
                case "AudioCapture" when string.Equals(eventArgs.State, "Capturing", StringComparison.OrdinalIgnoreCase):
                    SetAppStatus("Capturing", "Capturing system audio.");
                    break;
                case "SpeechRecognition":
                    SetAppStatus("Recognizing", "Recognizing speech.");
                    break;
                case "Translation" when string.Equals(eventArgs.State, "Translating", StringComparison.OrdinalIgnoreCase):
                    SetAppStatus("Translating", "Translating final subtitle.");
                    break;
                case "Overlay" when string.Equals(eventArgs.State, "Displaying", StringComparison.OrdinalIgnoreCase):
                    SetAppStatus("Displaying", "Displaying subtitle.");
                    break;
                case "Pipeline" when string.Equals(eventArgs.State, "Stopped", StringComparison.OrdinalIgnoreCase):
                    IsRunning = false;
                    CurrentCaptureDeviceName = "Not capturing.";
                    SetAppStatus("Idle", "Stopped.");
                    break;
            }
        });
    }

    private async Task ConfigureApiKeyAsync()
    {
        try
        {
            await _apiKeyDialogService.ShowAsync(CancellationToken.None);
            await RefreshApiKeyStatusAsync();
        }
        catch (Exception exception)
        {
            SetError($"Could not configure the OpenAI API key: {exception.Message}");
        }
    }

    private async Task<OpenAIApiKeyState> RefreshApiKeyStatusAsync()
    {
        var state = await _apiKeyManager.GetStateAsync(CancellationToken.None);
        IsApiKeyConfigured = state.HasKey;
        ApiKeyStatus = state.Source switch
        {
            OpenAIApiKeySource.EnvironmentVariable => "API Key: Environment",
            OpenAIApiKeySource.WindowsCredentialManager => "API Key: Secure storage",
            _ => "API Key: Not configured"
        };
        return state;
    }

    private static string? GetEnvironmentVariable(string name)
    {
        return Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process)
            ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User)
            ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine);
    }

    private void SetAppStatus(string status, string message)
    {
        AppStatus = status;
        StatusMessage = message;
    }

    private void SetError(string message)
    {
        AppStatus = "Error";
        ErrorMessage = message;
        StatusMessage = message;
        AddLog($"Error: {message}");
    }

    private static string GetFriendlyError(string details, string? errorCode = null)
    {
        if (string.Equals(errorCode, "DeviceSwitchDetected", StringComparison.OrdinalIgnoreCase)
            || details.Contains("default Windows output device changed", StringComparison.OrdinalIgnoreCase))
        {
            return "播放设备已切换，请点击 Stop 后再点击 Start。";
        }

        if (string.Equals(errorCode, "DeviceDisconnected", StringComparison.OrdinalIgnoreCase))
        {
            return "当前播放设备已断开；重新连接设备后请点击 Stop，再点击 Start。";
        }

        if (details.Contains("ApiKeyMissing", StringComparison.OrdinalIgnoreCase)
            || details.Contains("OPENAI_API_KEY", StringComparison.OrdinalIgnoreCase))
        {
            return "OPENAI_API_KEY 缺失或无效。";
        }

        if (details.Contains("NetworkFailure", StringComparison.OrdinalIgnoreCase)
            || details.Contains("network", StringComparison.OrdinalIgnoreCase))
        {
            return "网络失败，请检查网络或 OpenAI API 访问。";
        }

        if (details.Contains("Translation", StringComparison.OrdinalIgnoreCase))
        {
            return "Translation failed。";
        }

        if (details.Contains("ServiceError", StringComparison.OrdinalIgnoreCase)
            || details.Contains("ServiceCanceled", StringComparison.OrdinalIgnoreCase)
            || details.Contains("OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            return "OpenAI API 错误。";
        }

        if (details.Contains("No system audio", StringComparison.OrdinalIgnoreCase))
        {
            return "没有捕获到系统声音。";
        }

        return details;
    }

    private PipelineModuleStatus CreateModule(string name, string state, string details)
    {
        var module = new PipelineModuleStatus(name, state, details);
        _moduleStatuses[name] = module;
        return module;
    }

    private void UpdateModule(string moduleName, string state, string details)
    {
        if (!_moduleStatuses.TryGetValue(moduleName, out var module))
        {
            module = CreateModule(moduleName, state, details);
            PipelineModules.Add(module);
            return;
        }

        module.State = state;
        module.Details = details;
    }

    private void AddLog(string message)
    {
        var entry = $"{DateTime.Now:HH:mm:ss} {message}";
        AppendDiagnosticLog(entry);
        DispatchToUi(() =>
        {
            RecentLogs.Insert(0, entry);
            while (RecentLogs.Count > MaxLogEntries)
            {
                RecentLogs.RemoveAt(RecentLogs.Count - 1);
            }
        });
    }

    private void AppendDiagnosticLog(string entry)
    {
        if (string.IsNullOrWhiteSpace(_diagnosticLogPath))
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(_diagnosticLogPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            lock (_diagnosticLogLock)
            {
                File.AppendAllText(_diagnosticLogPath, entry + Environment.NewLine);
            }
        }
        catch
        {
            // Diagnostic logging must never affect the capture/recognition pipeline.
        }
    }

    private void SelectSourceLanguage(string code)
    {
        SourceLanguage = SourceLanguages.FirstOrDefault(language =>
            string.Equals(language.Code, code, StringComparison.OrdinalIgnoreCase)) ?? SourceLanguages[0];
    }

    private void SelectTargetLanguage(string code)
    {
        TargetLanguage = TargetLanguages.FirstOrDefault(language =>
            string.Equals(language.Code, code, StringComparison.OrdinalIgnoreCase)) ?? TargetLanguages[0];
    }

    private void SelectSubtitleMode(SubtitleDisplayMode mode)
    {
        SubtitleMode = SubtitleModes.FirstOrDefault(option => option.Mode == mode) ?? SubtitleModes[1];
    }

    private static string NormalizeSourceLanguage(string language)
    {
        if (string.IsNullOrWhiteSpace(language) || string.Equals(language, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return "en";
        }

        var separatorIndex = language.IndexOf('-', StringComparison.Ordinal);
        return separatorIndex > 0
            ? language[..separatorIndex].ToLowerInvariant()
            : language.ToLowerInvariant();
    }

    private void DispatchToUi(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _dispatcher.InvokeAsync(action, DispatcherPriority.Normal);
    }

    private void RaiseCommandStates()
    {
        if (StartCommand is RelayCommand startCommand)
        {
            startCommand.RaiseCanExecuteChanged();
        }

        if (StopCommand is RelayCommand stopCommand)
        {
            stopCommand.RaiseCanExecuteChanged();
        }

        if (ConfigureApiKeyCommand is RelayCommand configureApiKeyCommand)
        {
            configureApiKeyCommand.RaiseCanExecuteChanged();
        }
    }
}
