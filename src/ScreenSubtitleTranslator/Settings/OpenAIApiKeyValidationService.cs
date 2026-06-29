using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace ScreenSubtitleTranslator.Settings;

public sealed class OpenAIApiKeyValidationService : IOpenAIApiKeyValidationService, IDisposable
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    private static readonly Uri ModelsEndpoint = new("https://api.openai.com/v1/models");

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public OpenAIApiKeyValidationService()
        : this(new HttpClient(), ownsHttpClient: true)
    {
    }

    public OpenAIApiKeyValidationService(HttpClient httpClient)
        : this(httpClient, ownsHttpClient: false)
    {
    }

    private OpenAIApiKeyValidationService(HttpClient httpClient, bool ownsHttpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ownsHttpClient = ownsHttpClient;
    }

    public async Task<ApiKeyValidationResult> ValidateAsync(
        string apiKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new ApiKeyValidationResult(false, "Enter an API key first.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DefaultTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, ModelsEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());

        try
        {
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return new ApiKeyValidationResult(true, "Connection successful. The API key is valid.");
            }

            return response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => new ApiKeyValidationResult(
                    false,
                    "OpenAI rejected this API key. Check the key and try again."),
                HttpStatusCode.Forbidden => new ApiKeyValidationResult(
                    false,
                    "This API key does not have permission to access the OpenAI API."),
                (HttpStatusCode)429 => new ApiKeyValidationResult(
                    false,
                    "OpenAI rate-limited the request. Wait briefly and try again."),
                _ => new ApiKeyValidationResult(
                    false,
                    $"OpenAI returned HTTP {(int)response.StatusCode}. Try again later.")
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ApiKeyValidationResult(false, "The OpenAI connection test timed out.");
        }
        catch (HttpRequestException)
        {
            return new ApiKeyValidationResult(
                false,
                "Could not reach OpenAI. Check the network connection and try again.");
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
