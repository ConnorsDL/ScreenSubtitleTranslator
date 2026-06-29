using System.Windows;
using System.Windows.Media;
using ScreenSubtitleTranslator.Settings;

namespace ScreenSubtitleTranslator;

public partial class ApiKeyConfigurationWindow : Window
{
    private readonly IOpenAIApiKeyManager _apiKeyManager;
    private readonly IOpenAIApiKeyValidationService _validationService;
    private readonly CancellationTokenSource _lifetime = new();

    public ApiKeyConfigurationWindow(
        IOpenAIApiKeyManager apiKeyManager,
        IOpenAIApiKeyValidationService validationService)
    {
        _apiKeyManager = apiKeyManager ?? throw new ArgumentNullException(nameof(apiKeyManager));
        _validationService = validationService ?? throw new ArgumentNullException(nameof(validationService));
        InitializeComponent();
        Loaded += OnLoaded;
    }

    protected override void OnClosed(EventArgs e)
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        base.OnClosed(e);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await RefreshSourceAsync();
        ApiKeyPasswordBox.Focus();
    }

    private async void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        var apiKey = ApiKeyPasswordBox.Password;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            ShowStatus("Enter an API key before saving.", isSuccess: false);
            return;
        }

        SetBusy(true);
        try
        {
            var state = await _apiKeyManager.SaveLocalKeyAsync(apiKey, _lifetime.Token);
            ApiKeyPasswordBox.Clear();
            ShowStatus(GetSavedMessage(state), isSuccess: true);
            DialogResult = true;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ShowStatus($"Could not save the API key: {exception.Message}", isSuccess: false);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnTestClicked(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        try
        {
            var apiKey = ApiKeyPasswordBox.Password;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                apiKey = (await _apiKeyManager.GetStateAsync(_lifetime.Token)).ApiKey;
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                ShowStatus("Enter or save an API key before testing.", isSuccess: false);
                return;
            }

            ShowStatus("Testing the OpenAI connection...", isSuccess: null);
            var result = await _validationService.ValidateAsync(apiKey, _lifetime.Token);
            ShowStatus(result.Message, result.IsSuccess);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ShowStatus($"Connection test failed: {exception.Message}", isSuccess: false);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnClearClicked(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        try
        {
            var state = await _apiKeyManager.ClearLocalKeyAsync(_lifetime.Token);
            ApiKeyPasswordBox.Clear();
            KeySourceText.Text = GetSourceText(state);
            ShowStatus(
                state.Source == OpenAIApiKeySource.EnvironmentVariable
                    ? "The saved key was cleared. OPENAI_API_KEY is still active and has priority."
                    : "The saved API key was cleared.",
                isSuccess: true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ShowStatus($"Could not clear the saved API key: {exception.Message}", isSuccess: false);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RefreshSourceAsync()
    {
        try
        {
            var state = await _apiKeyManager.GetStateAsync(_lifetime.Token);
            KeySourceText.Text = GetSourceText(state);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ShowStatus($"Could not check the API key status: {exception.Message}", isSuccess: false);
        }
    }

    private void SetBusy(bool isBusy)
    {
        SaveButton.IsEnabled = !isBusy;
        TestButton.IsEnabled = !isBusy;
        ClearButton.IsEnabled = !isBusy;
        ApiKeyPasswordBox.IsEnabled = !isBusy;
    }

    private void ShowStatus(string message, bool? isSuccess)
    {
        StatusText.Text = message;
        StatusText.Foreground = (Brush)FindResource(isSuccess switch
        {
            true => "SuccessBrush",
            false => "ErrorBrush",
            _ => "MutedTextBrush"
        });
        StatusBorder.Background = (Brush)FindResource(isSuccess switch
        {
            true => "SuccessBackgroundBrush",
            false => "ErrorBackgroundBrush",
            _ => "PanelAltBrush"
        });
    }

    private static string GetSavedMessage(OpenAIApiKeyState state)
    {
        return state.Source == OpenAIApiKeySource.EnvironmentVariable
            ? "The key was saved securely. OPENAI_API_KEY remains active because it has priority."
            : "The API key was saved securely in Windows Credential Manager.";
    }

    private static string GetSourceText(OpenAIApiKeyState state)
    {
        return state.Source switch
        {
            OpenAIApiKeySource.EnvironmentVariable => "Active source: OPENAI_API_KEY environment variable.",
            OpenAIApiKeySource.WindowsCredentialManager => "Active source: Windows Credential Manager.",
            _ => "No API key is currently configured."
        };
    }
}
