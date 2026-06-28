using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.IO;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using ScreenSubtitleTranslator.AudioCapture;

namespace ScreenSubtitleTranslator.SpeechRecognition;

public sealed class OpenAISpeechRecognitionService : ISpeechRecognitionService, IDisposable
{
    public const string ProviderId = "OpenAI";
    public const string DefaultEndpoint = "https://api.openai.com/v1/audio/transcriptions";
    private const int MaxQueuedChunks = 4;

    private readonly HttpClient _httpClient;
    private readonly ISpeechRecognitionCredentialProvider _credentialProvider;
    private readonly Uri _endpoint;
    private readonly bool _ownsHttpClient;

    public OpenAISpeechRecognitionService()
        : this(new HttpClient(), new EnvironmentSpeechRecognitionCredentialProvider(), new Uri(DefaultEndpoint), ownsHttpClient: true)
    {
    }

    public OpenAISpeechRecognitionService(HttpClient httpClient, ISpeechRecognitionCredentialProvider credentialProvider)
        : this(httpClient, credentialProvider, new Uri(DefaultEndpoint), ownsHttpClient: false)
    {
    }

    public OpenAISpeechRecognitionService(
        HttpClient httpClient,
        ISpeechRecognitionCredentialProvider credentialProvider,
        Uri endpoint,
        bool ownsHttpClient = false)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _credentialProvider = credentialProvider ?? throw new ArgumentNullException(nameof(credentialProvider));
        _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        _ownsHttpClient = ownsHttpClient;
    }

    public async IAsyncEnumerable<SpeechRecognitionResult> RecognizeAsync(
        IAsyncEnumerable<AudioFrame> audioFrames,
        SpeechRecognitionOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audioFrames);
        ArgumentNullException.ThrowIfNull(options);

        var openAIOptions = options with { ProviderId = ProviderId };
        var credentials = _credentialProvider.GetCredentials(openAIOptions);
        var chunkDuration = NormalizeChunkDuration(openAIOptions.AudioChunkDuration);
        var overlapDuration = NormalizeOverlapDuration(openAIOptions.AudioOverlapDuration, chunkDuration);

        var resultChannel = Channel.CreateUnbounded<SpeechRecognitionResult>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        var chunkChannel = Channel.CreateBounded<AudioChunk>(new BoundedChannelOptions(MaxQueuedChunks)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var chunkProducer = ProduceChunksAsync(audioFrames, chunkDuration, overlapDuration, chunkChannel.Writer, linkedCancellation.Token);
        var recognitionConsumer = ConsumeChunksAsync(
            chunkChannel.Reader,
            resultChannel.Writer,
            credentials.ApiKey,
            openAIOptions,
            linkedCancellation.Token);

        try
        {
            await foreach (var result in resultChannel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return result;
            }
        }
        finally
        {
            linkedCancellation.Cancel();
            await AwaitBackgroundTask(chunkProducer, cancellationToken).ConfigureAwait(false);
            await AwaitBackgroundTask(recognitionConsumer, cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private async Task ProduceChunksAsync(
        IAsyncEnumerable<AudioFrame> audioFrames,
        TimeSpan chunkDuration,
        TimeSpan overlapDuration,
        ChannelWriter<AudioChunk> writer,
        CancellationToken cancellationToken)
    {
        var converter = new Pcm16MonoAudioFrameConverter();
        using var pcmChunk = new MemoryStream();
        var chunkIndex = 0;

        try
        {
            await foreach (var frame in audioFrames.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                var pcm16Mono = converter.Convert(frame);
                if (pcm16Mono.Length == 0)
                {
                    continue;
                }

                await pcmChunk.WriteAsync(pcm16Mono, cancellationToken).ConfigureAwait(false);

                if (GetPcmDuration(pcmChunk.Length) < chunkDuration)
                {
                    continue;
                }

                var chunkData = pcmChunk.ToArray();
                await writer.WriteAsync(
                    new AudioChunk(++chunkIndex, chunkData, GetPcmDuration(chunkData.Length)),
                    cancellationToken).ConfigureAwait(false);
                ResetChunkWithOverlap(pcmChunk, chunkData, overlapDuration);
            }

            if (pcmChunk.Length > 0)
            {
                await writer.WriteAsync(
                    new AudioChunk(++chunkIndex, pcmChunk.ToArray(), GetPcmDuration(pcmChunk.Length)),
                    cancellationToken).ConfigureAwait(false);
            }

            writer.TryComplete();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            writer.TryComplete();
        }
        catch (Exception exception)
        {
            writer.TryComplete(MapException(exception));
        }
    }

    private async Task ConsumeChunksAsync(
        ChannelReader<AudioChunk> chunks,
        ChannelWriter<SpeechRecognitionResult> results,
        string apiKey,
        SpeechRecognitionOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var chunk in chunks.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                var waveData = PcmWaveFileBuilder.BuildPcm16MonoWave(chunk.Pcm16MonoData);
                if (options.EnablePartialResults)
                {
                    await TranscribeStreamingAsync(
                        waveData,
                        chunk.Index,
                        chunk.AudioDuration,
                        apiKey,
                        options,
                        results,
                        cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    var transcription = await TranscribeJsonAsync(
                        waveData,
                        chunk.Index,
                        chunk.AudioDuration,
                        apiKey,
                        options,
                        cancellationToken).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(transcription.Text))
                    {
                        await results.WriteAsync(
                            CreateResult(
                                transcription.Text,
                                options,
                                isFinal: true,
                                chunk.Index,
                                chunk.AudioDuration,
                                transcription.SttDuration),
                            cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            results.TryComplete();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            results.TryComplete();
        }
        catch (Exception exception)
        {
            results.TryComplete(MapException(exception));
        }
    }

    private async Task<TranscriptionResult> TranscribeJsonAsync(
        byte[] waveData,
        int chunkIndex,
        TimeSpan audioDuration,
        string apiKey,
        SpeechRecognitionOptions options,
        CancellationToken cancellationToken)
    {
        using var request = CreateTranscriptionRequest(waveData, chunkIndex, apiKey, options, stream: false);
        var stopwatch = Stopwatch.StartNew();
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var text = document.RootElement.TryGetProperty("text", out var textElement)
            ? textElement.GetString()
            : null;
        return new TranscriptionResult(text, stopwatch.Elapsed);
    }

    private async Task TranscribeStreamingAsync(
        byte[] waveData,
        int chunkIndex,
        TimeSpan audioDuration,
        string apiKey,
        SpeechRecognitionOptions options,
        ChannelWriter<SpeechRecognitionResult> results,
        CancellationToken cancellationToken)
    {
        using var request = CreateTranscriptionRequest(waveData, chunkIndex, apiKey, options, stream: true);
        var stopwatch = Stopwatch.StartNew();
        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(responseStream, Encoding.UTF8);
        var eventName = string.Empty;
        var data = new StringBuilder();
        var partial = new StringBuilder();

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            if (line.Length == 0)
            {
                await ProcessServerSentEventAsync(
                        eventName,
                        data.ToString(),
                        partial,
                        options,
                        chunkIndex,
                        audioDuration,
                        stopwatch.Elapsed,
                        results,
                        cancellationToken)
                    .ConfigureAwait(false);
                eventName = string.Empty;
                data.Clear();
                continue;
            }

            if (line.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
            {
                eventName = line["event:".Length..].Trim();
            }
            else if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                if (data.Length > 0)
                {
                    data.AppendLine();
                }

                data.Append(line["data:".Length..].Trim());
            }
        }

        if (data.Length > 0)
        {
            await ProcessServerSentEventAsync(
                    eventName,
                    data.ToString(),
                    partial,
                    options,
                    chunkIndex,
                    audioDuration,
                    stopwatch.Elapsed,
                    results,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private HttpRequestMessage CreateTranscriptionRequest(
        byte[] waveData,
        int chunkIndex,
        string apiKey,
        SpeechRecognitionOptions options,
        bool stream)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, _endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var content = new MultipartFormDataContent
        {
            { new StringContent(NormalizeModel(options.ModelId)), "model" }
        };

        var language = NormalizeLanguage(options.SourceLanguage);
        if (language is not null)
        {
            content.Add(new StringContent(language), "language");
        }

        if (stream)
        {
            content.Add(new StringContent("true"), "stream");
        }
        else
        {
            content.Add(new StringContent("json"), "response_format");
        }

        var fileContent = new ByteArrayContent(waveData);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(fileContent, "file", $"speech-chunk-{chunkIndex:0000}.wav");
        request.Content = content;
        return request;
    }

    private static async Task ProcessServerSentEventAsync(
        string eventName,
        string data,
        StringBuilder partial,
        SpeechRecognitionOptions options,
        int chunkIndex,
        TimeSpan audioDuration,
        TimeSpan sttDuration,
        ChannelWriter<SpeechRecognitionResult> results,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(data) || string.Equals(data, "[DONE]", StringComparison.Ordinal))
        {
            return;
        }

        using var document = JsonDocument.Parse(data);
        var root = document.RootElement;

        var hasErrorProperty = root.TryGetProperty("error", out var errorElement);
        if (eventName.Contains("error", StringComparison.OrdinalIgnoreCase) || hasErrorProperty)
        {
            throw new SpeechRecognitionException(
                SpeechRecognitionErrorCode.ServiceCanceled,
                $"OpenAI transcription stream returned an error: {(hasErrorProperty ? GetJsonText(errorElement) : data)}");
        }

        if (TryGetJsonString(root, "delta", out var delta))
        {
            partial.Append(delta);
            if (partial.Length > 0)
            {
                await results.WriteAsync(
                    CreateResult(partial.ToString(), options, isFinal: false, chunkIndex, audioDuration, sttDuration),
                    cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        if (TryGetJsonString(root, "text", out var text) && !string.IsNullOrWhiteSpace(text))
        {
            await results.WriteAsync(
                CreateResult(text, options, isFinal: true, chunkIndex, audioDuration, sttDuration),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var code = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            ? SpeechRecognitionErrorCode.ApiKeyMissing
            : SpeechRecognitionErrorCode.ServiceCanceled;

        if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.RequestTimeout)
        {
            code = SpeechRecognitionErrorCode.NetworkFailure;
        }

        throw new SpeechRecognitionException(
            code,
            $"OpenAI transcription request failed: {(int)response.StatusCode} {response.ReasonPhrase}. {body}");
    }

    private static SpeechRecognitionResult CreateResult(
        string text,
        SpeechRecognitionOptions options,
        bool isFinal,
        int chunkIndex,
        TimeSpan audioDuration,
        TimeSpan sttDuration)
    {
        return new SpeechRecognitionResult(
            text,
            NormalizeLanguage(options.SourceLanguage) ?? "auto",
            isFinal,
            DateTimeOffset.UtcNow,
            chunkIndex,
            audioDuration,
            sttDuration);
    }

    private static string NormalizeModel(string modelId)
    {
        return string.IsNullOrWhiteSpace(modelId)
            ? SpeechRecognitionOptions.CreateDefault().ModelId
            : modelId.Trim();
    }

    private static string? NormalizeLanguage(string language)
    {
        if (string.IsNullOrWhiteSpace(language) || string.Equals(language, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var trimmed = language.Trim();
        var separatorIndex = trimmed.IndexOf('-', StringComparison.Ordinal);
        return separatorIndex > 0
            ? trimmed[..separatorIndex].ToLowerInvariant()
            : trimmed.ToLowerInvariant();
    }

    private static TimeSpan NormalizeChunkDuration(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return SpeechRecognitionOptions.CreateDefault().AudioChunkDuration;
        }

        if (duration < TimeSpan.FromSeconds(1))
        {
            return TimeSpan.FromSeconds(1);
        }

        return duration;
    }

    private static TimeSpan NormalizeOverlapDuration(TimeSpan overlapDuration, TimeSpan chunkDuration)
    {
        if (chunkDuration < TimeSpan.FromSeconds(3))
        {
            return TimeSpan.Zero;
        }

        if (overlapDuration <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        var maxOverlap = TimeSpan.FromMilliseconds(Math.Min(500, chunkDuration.TotalMilliseconds / 4));
        if (overlapDuration > maxOverlap)
        {
            return maxOverlap;
        }

        return overlapDuration;
    }

    private static void ResetChunkWithOverlap(MemoryStream pcmChunk, byte[] chunkData, TimeSpan overlapDuration)
    {
        pcmChunk.SetLength(0);
        var overlapBytes = GetPcmByteCount(overlapDuration);
        if (overlapBytes <= 0 || chunkData.Length <= overlapBytes)
        {
            return;
        }

        pcmChunk.Write(chunkData, chunkData.Length - overlapBytes, overlapBytes);
    }

    private static TimeSpan GetPcmDuration(long pcmByteCount)
    {
        const double bytesPerSecond = Pcm16MonoAudioFrameConverter.TargetSampleRate
            * Pcm16MonoAudioFrameConverter.TargetChannelCount
            * Pcm16MonoAudioFrameConverter.TargetBitsPerSample
            / 8.0;
        return TimeSpan.FromSeconds(pcmByteCount / bytesPerSecond);
    }

    private static int GetPcmByteCount(TimeSpan duration)
    {
        const double bytesPerSecond = Pcm16MonoAudioFrameConverter.TargetSampleRate
            * Pcm16MonoAudioFrameConverter.TargetChannelCount
            * Pcm16MonoAudioFrameConverter.TargetBitsPerSample
            / 8.0;
        var byteCount = (int)Math.Round(duration.TotalSeconds * bytesPerSecond);
        return byteCount - (byteCount % 2);
    }

    private static bool TryGetJsonString(JsonElement root, string propertyName, out string text)
    {
        if (root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
        {
            text = property.GetString() ?? string.Empty;
            return true;
        }

        text = string.Empty;
        return false;
    }

    private static string? GetJsonText(JsonElement element)
    {
        return element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : element.ToString();
    }

    private static Exception MapException(Exception exception)
    {
        return exception switch
        {
            SpeechRecognitionException => exception,
            HttpRequestException => new SpeechRecognitionException(
                SpeechRecognitionErrorCode.NetworkFailure,
                "OpenAI transcription network request failed.",
                exception),
            TaskCanceledException => new SpeechRecognitionException(
                SpeechRecognitionErrorCode.NetworkFailure,
                "OpenAI transcription request timed out or was canceled.",
                exception),
            _ => new SpeechRecognitionException(
                SpeechRecognitionErrorCode.ServiceInitializationFailed,
                "OpenAI transcription failed.",
                exception)
        };
    }

    private static async Task AwaitBackgroundTask(Task task, CancellationToken cancellationToken)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private sealed record AudioChunk(int Index, byte[] Pcm16MonoData, TimeSpan AudioDuration);

    private sealed record TranscriptionResult(string? Text, TimeSpan SttDuration);
}
