using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ScreenSubtitleTranslator.Translation;

public sealed class OpenAITranslationService : ITranslationService, IDisposable
{
    public const string ProviderId = "OpenAI";
    public const string DefaultModel = "gpt-4.1-mini";
    public const string DefaultEndpoint = "https://api.openai.com/v1/responses";
    public const int DefaultTransientRetryCount = 1;
    public static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan DefaultTransientRetryDelay = TimeSpan.FromMilliseconds(250);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly ITranslationCredentialProvider _credentialProvider;
    private readonly Uri _endpoint;
    private readonly string _modelId;
    private readonly TimeSpan _requestTimeout;
    private readonly bool _ownsHttpClient;

    public OpenAITranslationService()
        : this(
            new HttpClient(),
            new EnvironmentTranslationCredentialProvider(),
            new Uri(DefaultEndpoint),
            DefaultModel,
            DefaultRequestTimeout,
            ownsHttpClient: true)
    {
    }

    public OpenAITranslationService(HttpClient httpClient, ITranslationCredentialProvider credentialProvider)
        : this(
            httpClient,
            credentialProvider,
            new Uri(DefaultEndpoint),
            DefaultModel,
            DefaultRequestTimeout,
            ownsHttpClient: false)
    {
    }

    public OpenAITranslationService(
        HttpClient httpClient,
        ITranslationCredentialProvider credentialProvider,
        Uri endpoint,
        string modelId,
        TimeSpan requestTimeout,
        bool ownsHttpClient = false)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _credentialProvider = credentialProvider ?? throw new ArgumentNullException(nameof(credentialProvider));
        _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        _modelId = string.IsNullOrWhiteSpace(modelId) ? DefaultModel : modelId.Trim();
        _requestTimeout = requestTimeout <= TimeSpan.Zero ? DefaultRequestTimeout : requestTimeout;
        _ownsHttpClient = ownsHttpClient;
    }

    public async Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Text))
        {
            throw new TranslationException(
                TranslationErrorCode.EmptySourceText,
                "Source text is empty; translation request was not sent.");
        }

        var credentials = _credentialProvider.GetCredentials();
        for (var attempt = 0; attempt <= DefaultTransientRetryCount; attempt++)
        {
            try
            {
                return await TranslateOnceAsync(request, credentials.ApiKey, cancellationToken).ConfigureAwait(false);
            }
            catch (TranslationException exception) when (
                attempt < DefaultTransientRetryCount && IsTransient(exception.ErrorCode))
            {
                await Task.Delay(DefaultTransientRetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException("OpenAI translation retry loop completed without a result.");
    }

    private async Task<TranslationResult> TranslateOnceAsync(
        TranslationRequest request,
        string apiKey,
        CancellationToken cancellationToken)
    {
        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(_requestTimeout);

        try
        {
            using var httpRequest = CreateRequest(request, apiKey);
            using var response = await _httpClient
                .SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, timeoutCancellation.Token)
                .ConfigureAwait(false);

            await EnsureSuccessAsync(response, timeoutCancellation.Token).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync(timeoutCancellation.Token).ConfigureAwait(false);
            var translatedText = ExtractTranslatedText(responseBody);

            if (string.IsNullOrWhiteSpace(translatedText))
            {
                throw new TranslationException(
                    TranslationErrorCode.EmptyResponse,
                    "OpenAI translation returned empty content.");
            }

            return new TranslationResult(
                request.Text,
                translatedText,
                NormalizeSourceLanguage(request.SourceLanguage),
                NormalizeTargetLanguage(request.TargetLanguage),
                DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TranslationException(
                TranslationErrorCode.Timeout,
                $"OpenAI translation timed out after {_requestTimeout.TotalSeconds:0.#} seconds.",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new TranslationException(
                TranslationErrorCode.NetworkFailure,
                "OpenAI translation network request failed.",
                exception);
        }
        catch (JsonException exception)
        {
            throw new TranslationException(
                TranslationErrorCode.ServiceError,
                "OpenAI translation returned invalid JSON.",
                exception);
        }
    }

    private static bool IsTransient(TranslationErrorCode errorCode)
    {
        return errorCode is TranslationErrorCode.Timeout or TranslationErrorCode.NetworkFailure;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private HttpRequestMessage CreateRequest(TranslationRequest request, string apiKey)
    {
        var payload = new
        {
            model = _modelId,
            input = BuildTranslationPrompt(request),
            max_output_tokens = 800
        };

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, _endpoint);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(payload, JsonOptions),
            Encoding.UTF8,
            "application/json");
        return httpRequest;
    }

    private static string BuildTranslationPrompt(TranslationRequest request)
    {
        var lines = new List<string>
        {
            "Translate the current subtitle text.",
            "Use previous subtitle context only to resolve pronouns, tone, and terminology.",
            "Do not translate, repeat, summarize, or continue the previous context.",
            "Return only the current subtitle translation, with no explanations, no labels, and no markdown.",
            $"Source language: {NormalizeSourceLanguage(request.SourceLanguage)}",
            $"Target language: {NormalizeTargetLanguage(request.TargetLanguage)}"
        };

        if (!string.IsNullOrWhiteSpace(request.PreviousSourceText))
        {
            lines.Add("Previous source subtitle for context:");
            lines.Add(request.PreviousSourceText.Trim());
        }

        if (!string.IsNullOrWhiteSpace(request.PreviousTranslatedText))
        {
            lines.Add("Previous translated subtitle for context:");
            lines.Add(request.PreviousTranslatedText.Trim());
        }

        lines.Add("Current subtitle text:");
        lines.Add(request.Text.Trim());

        return string.Join(Environment.NewLine, lines);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var code = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            ? TranslationErrorCode.ApiKeyMissing
            : TranslationErrorCode.ServiceError;

        if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.RequestTimeout)
        {
            code = TranslationErrorCode.NetworkFailure;
        }

        throw new TranslationException(
            code,
            $"OpenAI translation request failed: {(int)response.StatusCode} {response.ReasonPhrase}. {body}");
    }

    private static string ExtractTranslatedText(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;

        if (root.TryGetProperty("error", out var errorElement)
            && errorElement.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
        {
            throw new TranslationException(
                TranslationErrorCode.ServiceError,
                $"OpenAI translation returned an error: {errorElement}");
        }

        if (TryGetString(root, "output_text", out var outputText))
        {
            return outputText.Trim();
        }

        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var outputItem in output.EnumerateArray())
        {
            if (!outputItem.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var contentItem in content.EnumerateArray())
            {
                if (TryGetString(contentItem, "text", out var text))
                {
                    builder.Append(text);
                }
            }
        }

        return builder.ToString().Trim();
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        if (element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString() ?? string.Empty;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static string NormalizeSourceLanguage(string sourceLanguage)
    {
        return string.IsNullOrWhiteSpace(sourceLanguage) ? "en" : sourceLanguage.Trim();
    }

    private static string NormalizeTargetLanguage(string targetLanguage)
    {
        return string.IsNullOrWhiteSpace(targetLanguage) ? "zh-CN" : targetLanguage.Trim();
    }
}
