namespace ScreenSubtitleTranslator.Settings;

public sealed class OpenAIApiKeyManager : IOpenAIApiKeyManager
{
    public const string EnvironmentVariableName = "OPENAI_API_KEY";

    private readonly IApiKeyStore _store;
    private readonly IEnvironmentVariableAccessor _environment;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _injectedProcessKey;

    public OpenAIApiKeyManager(IApiKeyStore store, IEnvironmentVariableAccessor environment)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    public async Task<OpenAIApiKeyState> GetStateAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ResolveStateAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OpenAIApiKeyState> SaveLocalKeyAsync(
        string apiKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("API key cannot be empty.", nameof(apiKey));
        }

        var normalizedKey = apiKey.Trim();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _store.SaveAsync(normalizedKey, cancellationToken).ConfigureAwait(false);

            var environmentKey = GetExternalEnvironmentKey();
            if (!string.IsNullOrWhiteSpace(environmentKey))
            {
                return ActivateEnvironmentKey(environmentKey);
            }

            _environment.SetProcess(EnvironmentVariableName, normalizedKey);
            _injectedProcessKey = normalizedKey;
            return new OpenAIApiKeyState(normalizedKey, OpenAIApiKeySource.WindowsCredentialManager);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OpenAIApiKeyState> ClearLocalKeyAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _store.ClearAsync(cancellationToken).ConfigureAwait(false);

            var processKey = Normalize(_environment.Get(
                EnvironmentVariableName,
                EnvironmentVariableTarget.Process));
            if (!string.IsNullOrWhiteSpace(_injectedProcessKey)
                && string.Equals(processKey, _injectedProcessKey, StringComparison.Ordinal))
            {
                _environment.SetProcess(EnvironmentVariableName, null);
            }

            _injectedProcessKey = null;
            return await ResolveStateAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<OpenAIApiKeyState> ResolveStateAsync(CancellationToken cancellationToken)
    {
        var environmentKey = GetExternalEnvironmentKey();
        if (!string.IsNullOrWhiteSpace(environmentKey))
        {
            return ActivateEnvironmentKey(environmentKey);
        }

        var savedKey = Normalize(await _store.ReadAsync(cancellationToken).ConfigureAwait(false));
        if (string.IsNullOrWhiteSpace(savedKey))
        {
            var processKey = Normalize(_environment.Get(
                EnvironmentVariableName,
                EnvironmentVariableTarget.Process));
            if (!string.IsNullOrWhiteSpace(_injectedProcessKey)
                && string.Equals(processKey, _injectedProcessKey, StringComparison.Ordinal))
            {
                _environment.SetProcess(EnvironmentVariableName, null);
            }

            _injectedProcessKey = null;
            return new OpenAIApiKeyState(null, OpenAIApiKeySource.None);
        }

        _environment.SetProcess(EnvironmentVariableName, savedKey);
        _injectedProcessKey = savedKey;
        return new OpenAIApiKeyState(savedKey, OpenAIApiKeySource.WindowsCredentialManager);
    }

    private string? GetExternalEnvironmentKey()
    {
        var processKey = Normalize(_environment.Get(
            EnvironmentVariableName,
            EnvironmentVariableTarget.Process));
        if (!string.IsNullOrWhiteSpace(processKey)
            && (string.IsNullOrWhiteSpace(_injectedProcessKey)
                || !string.Equals(processKey, _injectedProcessKey, StringComparison.Ordinal)))
        {
            return processKey;
        }

        return Normalize(_environment.Get(EnvironmentVariableName, EnvironmentVariableTarget.User))
            ?? Normalize(_environment.Get(EnvironmentVariableName, EnvironmentVariableTarget.Machine));
    }

    private OpenAIApiKeyState ActivateEnvironmentKey(string environmentKey)
    {
        var processKey = Normalize(_environment.Get(
            EnvironmentVariableName,
            EnvironmentVariableTarget.Process));
        if (!string.IsNullOrWhiteSpace(_injectedProcessKey)
            && string.Equals(processKey, _injectedProcessKey, StringComparison.Ordinal))
        {
            _environment.SetProcess(EnvironmentVariableName, environmentKey);
        }

        _injectedProcessKey = null;
        return new OpenAIApiKeyState(environmentKey, OpenAIApiKeySource.EnvironmentVariable);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
