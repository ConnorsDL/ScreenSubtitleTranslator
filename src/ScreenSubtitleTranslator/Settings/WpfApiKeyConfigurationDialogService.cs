using System.Windows;

namespace ScreenSubtitleTranslator.Settings;

public sealed class WpfApiKeyConfigurationDialogService : IApiKeyConfigurationDialogService, IDisposable
{
    private readonly IOpenAIApiKeyManager _apiKeyManager;
    private readonly Func<Window?> _ownerProvider;
    private readonly OpenAIApiKeyValidationService _validationService = new();

    public WpfApiKeyConfigurationDialogService(
        IOpenAIApiKeyManager apiKeyManager,
        Func<Window?> ownerProvider)
    {
        _apiKeyManager = apiKeyManager ?? throw new ArgumentNullException(nameof(apiKeyManager));
        _ownerProvider = ownerProvider ?? throw new ArgumentNullException(nameof(ownerProvider));
    }

    public Task<bool> ShowAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dispatcher = Application.Current?.Dispatcher
            ?? throw new InvalidOperationException("The API key dialog requires a WPF application dispatcher.");

        if (dispatcher.CheckAccess())
        {
            return Task.FromResult(ShowDialog(cancellationToken));
        }

        return dispatcher.InvokeAsync(() => ShowDialog(cancellationToken)).Task;
    }

    public void Dispose()
    {
        _validationService.Dispose();
    }

    private bool ShowDialog(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var window = new ApiKeyConfigurationWindow(_apiKeyManager, _validationService);
        var owner = _ownerProvider();
        if (owner is not null && owner.IsVisible)
        {
            window.Owner = owner;
        }

        return window.ShowDialog() == true;
    }
}
