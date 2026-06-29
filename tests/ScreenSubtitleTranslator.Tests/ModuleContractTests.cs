using System.IO;
using System.Windows.Threading;
using ScreenSubtitleTranslator.AudioCapture;
using ScreenSubtitleTranslator.Logging;
using ScreenSubtitleTranslator.Overlay;
using ScreenSubtitleTranslator.Pipeline;
using ScreenSubtitleTranslator.Settings;
using ScreenSubtitleTranslator.SpeechRecognition;
using ScreenSubtitleTranslator.Translation;
using ScreenSubtitleTranslator.ViewModels;
using Xunit;

namespace ScreenSubtitleTranslator.Tests;

public sealed class ModuleContractTests
{
    [Fact]
    public void ArchitectureDefinesRequiredModuleInterfaces()
    {
        Assert.True(typeof(IAudioCaptureService).IsInterface);
        Assert.True(typeof(IAudioBuffer).IsInterface);
        Assert.True(typeof(ISpeechRecognitionService).IsInterface);
        Assert.True(typeof(ITranslationService).IsInterface);
        Assert.True(typeof(ISubtitleOverlayController).IsInterface);
        Assert.True(typeof(ISubtitleOverlayController).IsAssignableFrom(typeof(WpfSubtitleOverlayController)));
        Assert.True(typeof(ISettingsStore).IsInterface);
        Assert.True(typeof(IApiKeyStore).IsInterface);
        Assert.True(typeof(IOpenAIApiKeyManager).IsInterface);
        Assert.True(typeof(IOpenAIApiKeyValidationService).IsInterface);
        Assert.True(typeof(IApiKeyConfigurationDialogService).IsInterface);
        Assert.True(typeof(IAppLogger).IsInterface);
    }

    [Fact]
    public void DefaultSettingsUseSystemAudioAndOverlayDefaults()
    {
        var settings = UserSettings.CreateDefault();

        Assert.True(settings.AudioCapture.UseWasapiLoopback);
        Assert.Equal(48000, settings.AudioCapture.SampleRate);
        Assert.Equal(2, settings.AudioCapture.ChannelCount);
        Assert.True(settings.Overlay.ShowOnStartup);
        Assert.Equal(30, settings.Overlay.FontSize);
        Assert.Equal(920, settings.Overlay.SubtitleWidth);
        Assert.Equal("OpenAI", settings.SpeechRecognition.ProviderId);
        Assert.Equal("gpt-4o-mini-transcribe", settings.SpeechRecognition.ModelId);
        Assert.Equal("en-US", settings.SpeechRecognition.SourceLanguage);
        Assert.Null(settings.SpeechRecognition.ApiKey);
        Assert.Equal("OpenAI", settings.Translation.ProviderId);
        Assert.Equal("en", settings.Translation.SourceLanguage);
        Assert.Equal("zh-CN", settings.Translation.TargetLanguage);
        Assert.Equal("gpt-4.1-mini", settings.Translation.ModelId);
        Assert.Equal(OpenAITranslationService.DefaultRequestTimeout, settings.Translation.RequestTimeout);
        Assert.Equal(TimeSpan.FromSeconds(3), settings.SpeechRecognition.AudioChunkDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(400), settings.SpeechRecognition.AudioOverlapDuration);
        Assert.Equal(SubtitleDisplayMode.EnglishAndChinese, settings.SubtitleDisplayMode);
    }

    [Fact]
    public void AudioFrameCarriesPcmMetadata()
    {
        var frame = new AudioFrame(
            new byte[] { 1, 2, 3, 4 },
            DateTimeOffset.UtcNow,
            TimeSpan.FromMilliseconds(10),
            48000,
            2,
            16,
            AudioSampleFormat.Pcm16);

        Assert.Equal(4, frame.PcmData.Length);
        Assert.Equal(48000, frame.SampleRate);
        Assert.Equal(2, frame.ChannelCount);
        Assert.Equal(AudioSampleFormat.Pcm16, frame.SampleFormat);
    }

    [Fact]
    public async Task AudioBufferProvidesContinuousPcmFrames()
    {
        var buffer = new AudioBuffer(new AudioBufferOptions(Capacity: 4));
        var frame = new AudioFrame(
            new byte[] { 1, 2, 3, 4 },
            DateTimeOffset.UtcNow,
            TimeSpan.FromMilliseconds(10),
            48000,
            2,
            16,
            AudioSampleFormat.Pcm16);

        await buffer.WriteAsync(frame, CancellationToken.None);

        Assert.Equal(1, buffer.FramesWritten);
        Assert.Equal(4, buffer.BytesWritten);
        Assert.True(buffer.TryRead(out var readFrame));
        Assert.NotNull(readFrame);
        Assert.Equal(AudioSampleFormat.Pcm16, readFrame.SampleFormat);
    }

    [Fact]
    public async Task AudioCaptureServiceRejectsMicrophoneCaptureMode()
    {
        var service = new AudioCaptureService();
        var sink = new AudioBuffer();
        var options = AudioCaptureOptions.CreateDefault() with
        {
            UseWasapiLoopback = false
        };

        var exception = await Assert.ThrowsAsync<AudioCaptureException>(
            () => service.StartAsync(options, sink, CancellationToken.None));

        Assert.Equal(AudioCaptureErrorCode.InvalidState, exception.ErrorCode);
        Assert.Equal(AudioCaptureState.Faulted, service.State);
    }

    [Fact]
    public void Pcm16MonoConverterConvertsStereoFloat48kToMonoPcm16_16k()
    {
        var converter = new Pcm16MonoAudioFrameConverter();
        var frame = CreateStereoFloatFrame(sampleRate: 48000, sampleCountPerChannel: 480, left: 0.5f, right: -0.5f);

        var converted = converter.Convert(frame);

        Assert.Equal(320, converted.Length);
    }

    [Fact]
    public void Pcm16MonoConverterRejectsUnknownAudioFormat()
    {
        var converter = new Pcm16MonoAudioFrameConverter();
        var frame = new AudioFrame(
            new byte[] { 1, 2, 3, 4 },
            DateTimeOffset.UtcNow,
            TimeSpan.FromMilliseconds(10),
            48000,
            2,
            16,
            AudioSampleFormat.Unknown);

        var exception = Assert.Throws<SpeechRecognitionException>(() => converter.Convert(frame));

        Assert.Equal(SpeechRecognitionErrorCode.AudioFormatMismatch, exception.ErrorCode);
    }

    [Fact]
    public async Task AzureSpeechRecognitionServiceReportsMissingApiKeyBeforeNetworkCall()
    {
        var service = new AzureSpeechRecognitionService(new MissingSpeechCredentialProvider());

        var exception = await Assert.ThrowsAsync<SpeechRecognitionException>(async () =>
        {
            await foreach (var _ in service.RecognizeAsync(
                EmptyAudioFrames(),
                SpeechRecognitionOptions.CreateDefault(),
                CancellationToken.None))
            {
            }
        });

        Assert.Equal(SpeechRecognitionErrorCode.ApiKeyMissing, exception.ErrorCode);
    }

    [Fact]
    public async Task OpenAISpeechRecognitionServiceReportsMissingApiKeyBeforeNetworkCall()
    {
        using var service = new OpenAISpeechRecognitionService(
            new HttpClient(new JsonSpeechTranscriptionHandler("should not be called")),
            new MissingSpeechCredentialProvider());

        var exception = await Assert.ThrowsAsync<SpeechRecognitionException>(async () =>
        {
            await foreach (var _ in service.RecognizeAsync(
                EmptyAudioFrames(),
                SpeechRecognitionOptions.CreateDefault(),
                CancellationToken.None))
            {
            }
        });

        Assert.Equal(SpeechRecognitionErrorCode.ApiKeyMissing, exception.ErrorCode);
    }

    [Fact]
    public async Task OpenAISpeechRecognitionServiceReadsJsonFinalResult()
    {
        using var service = new OpenAISpeechRecognitionService(
            new HttpClient(new JsonSpeechTranscriptionHandler("""{"text":"hello from openai"}""")),
            new FixedSpeechCredentialProvider());
        var frame = new AudioFrame(
            new byte[3200],
            DateTimeOffset.UtcNow,
            TimeSpan.FromMilliseconds(100),
            16000,
            1,
            16,
            AudioSampleFormat.Pcm16);

        var results = new List<SpeechRecognitionResult>();
        await foreach (var result in service.RecognizeAsync(
            SingleAudioFrame(frame),
            SpeechRecognitionOptions.CreateDefault() with
            {
                EnablePartialResults = false,
                AudioChunkDuration = TimeSpan.FromSeconds(1)
            },
            CancellationToken.None))
        {
            results.Add(result);
        }

        var finalResult = Assert.Single(results);
        Assert.True(finalResult.IsFinal);
        Assert.Equal("hello from openai", finalResult.Text);
    }

    [Fact]
    public async Task OpenAISpeechRecognitionServiceSendsSelectedSourceLanguage()
    {
        using var service = new OpenAISpeechRecognitionService(
            new HttpClient(new JsonSpeechTranscriptionHandler(
                """{"text":"guten tag"}""",
                expectedLanguage: "de")),
            new FixedSpeechCredentialProvider());
        var frame = new AudioFrame(
            new byte[3200],
            DateTimeOffset.UtcNow,
            TimeSpan.FromMilliseconds(100),
            16000,
            1,
            16,
            AudioSampleFormat.Pcm16);

        var results = new List<SpeechRecognitionResult>();
        await foreach (var result in service.RecognizeAsync(
            SingleAudioFrame(frame),
            SpeechRecognitionOptions.CreateDefault() with
            {
                SourceLanguage = "de-DE",
                EnablePartialResults = false,
                AudioChunkDuration = TimeSpan.FromSeconds(1)
            },
            CancellationToken.None))
        {
            results.Add(result);
        }

        Assert.Equal("de", Assert.Single(results).Language);
    }

    [Fact]
    public void PcmWaveFileBuilderCreatesWaveHeader()
    {
        var waveData = PcmWaveFileBuilder.BuildPcm16MonoWave(new byte[320]);

        Assert.Equal((byte)'R', waveData[0]);
        Assert.Equal((byte)'I', waveData[1]);
        Assert.Equal((byte)'W', waveData[8]);
        Assert.Equal((byte)'E', waveData[11]);
        Assert.Equal(364, waveData.Length);
    }

    [Fact]
    public void SubtitlePipelineOptionsCarryOverlaySettings()
    {
        var options = new SubtitlePipelineOptions(
            SourceLanguage: "en-US",
            TargetLanguage: "zh-CN",
            ShowOverlay: true,
            ShowOriginalText: true,
            AudioChunkDuration: TimeSpan.FromSeconds(3));

        Assert.Equal("en-US", options.SourceLanguage);
        Assert.Equal("zh-CN", options.TargetLanguage);
        Assert.True(options.ShowOverlay);
        Assert.True(options.ShowOriginalText);
        Assert.Equal(TimeSpan.FromSeconds(3), options.AudioChunkDuration);
    }

    [Fact]
    public void LanguageCatalogContainsReleaseLanguageOptions()
    {
        Assert.Equal(
            new[] { "en-US", "en-GB", "de-DE", "zh-CN", "ja-JP", "ko-KR", "fr-FR", "es-ES", "it-IT" },
            LanguageCatalog.SourceLanguages.Select(language => language.Code));
        Assert.Equal(
            new[] { "zh-CN", "zh-TW", "en", "de", "ja", "ko", "fr", "es", "it" },
            LanguageCatalog.TargetLanguages.Select(language => language.Code));
    }

    [Fact]
    public void AudioAndPipelineStatusCarryCaptureDeviceMetadata()
    {
        var audioStatus = new AudioCaptureStateChangedEventArgs(
            AudioCaptureState.Capturing,
            "capturing",
            errorCode: AudioCaptureErrorCode.None,
            deviceName: "Test speakers",
            deviceId: "device-1");
        var pipelineStatus = new SubtitlePipelineStatusChangedEventArgs(
            "AudioCapture",
            "Faulted",
            "device changed",
            AudioCaptureErrorCode.DeviceSwitchDetected.ToString(),
            audioStatus.DeviceName);

        Assert.Equal("Test speakers", audioStatus.DeviceName);
        Assert.Equal("device-1", audioStatus.DeviceId);
        Assert.Equal("DeviceSwitchDetected", pipelineStatus.ErrorCode);
        Assert.Equal("Test speakers", pipelineStatus.DeviceName);
    }

    [Fact]
    public void SubtitleTextUtilitiesTrimRepeatedOverlapConservatively()
    {
        var trimmed = SubtitleTextUtilities.TrimRepeatedPrefixFromPrevious(
            "we are going to talk about latency",
            "about latency in the next example");

        Assert.Equal("in the next example", trimmed);
        Assert.Equal("the next topic", SubtitleTextUtilities.TrimRepeatedPrefixFromPrevious(
            "this is the",
            "the next topic"));
    }

    [Fact]
    public void RecentTextCacheRejectsNormalizedDuplicates()
    {
        var cache = new RecentTextCache(capacity: 4);

        Assert.True(cache.AddIfNew("Hello, world.", sequenceId: 7, out var originalSequenceId));
        Assert.Equal(0, originalSequenceId);
        Assert.False(cache.AddIfNew("hello world", sequenceId: 8, out originalSequenceId));
        Assert.Equal(7, originalSequenceId);
        Assert.True(cache.AddIfNew("Hello again.", sequenceId: 9, out originalSequenceId));
    }

    [Fact]
    public async Task LocalUserSettingsStorePersistsUiSettingsWithoutApiKey()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"screen-subtitle-settings-{Guid.NewGuid():N}.json");
        var store = new LocalUserSettingsStore(settingsPath);
        var defaults = UserSettings.CreateDefault();
        var settings = defaults with
        {
            SpeechRecognition = defaults.SpeechRecognition with
            {
                SourceLanguage = "de-DE",
                AudioChunkDuration = TimeSpan.FromSeconds(5),
                ApiKey = "do-not-save-this-key"
            },
            Translation = defaults.Translation with
            {
                TargetLanguage = "zh-TW"
            },
            SubtitleDisplayMode = SubtitleDisplayMode.ChineseOnly
        };

        try
        {
            await store.SaveAsync(settings, CancellationToken.None);

            var json = await File.ReadAllTextAsync(settingsPath);
            Assert.DoesNotContain("do-not-save-this-key", json);
            Assert.DoesNotContain("apiKey", json, StringComparison.OrdinalIgnoreCase);

            var loaded = await store.LoadAsync(CancellationToken.None);
            Assert.Equal("de-DE", loaded.SpeechRecognition.SourceLanguage);
            Assert.Equal("de", loaded.Translation.SourceLanguage);
            Assert.Equal("zh-TW", loaded.Translation.TargetLanguage);
            Assert.Equal(TimeSpan.FromSeconds(5), loaded.SpeechRecognition.AudioChunkDuration);
            Assert.Equal(SubtitleDisplayMode.ChineseOnly, loaded.SubtitleDisplayMode);
        }
        finally
        {
            if (File.Exists(settingsPath))
            {
                File.Delete(settingsPath);
            }
        }
    }

    [Fact]
    public async Task OpenAIApiKeyManagerSavesReadsAndClearsLocalKey()
    {
        var store = new InMemoryApiKeyStore();
        var environment = new InMemoryEnvironmentVariableAccessor();
        var manager = new OpenAIApiKeyManager(store, environment);

        var saved = await manager.SaveLocalKeyAsync("local-test-api-key", CancellationToken.None);

        Assert.Equal(OpenAIApiKeySource.WindowsCredentialManager, saved.Source);
        Assert.Equal("local-test-api-key", store.StoredKey);
        Assert.Equal(
            "local-test-api-key",
            environment.Get(OpenAIApiKeyManager.EnvironmentVariableName, EnvironmentVariableTarget.Process));

        var resolved = await manager.GetStateAsync(CancellationToken.None);
        Assert.True(resolved.HasKey);
        Assert.Equal(OpenAIApiKeySource.WindowsCredentialManager, resolved.Source);

        var cleared = await manager.ClearLocalKeyAsync(CancellationToken.None);

        Assert.False(cleared.HasKey);
        Assert.Null(store.StoredKey);
        Assert.Null(environment.Get(
            OpenAIApiKeyManager.EnvironmentVariableName,
            EnvironmentVariableTarget.Process));
    }

    [Fact]
    public async Task OpenAIApiKeyManagerPrefersEnvironmentVariableOverSavedKey()
    {
        var store = new InMemoryApiKeyStore { StoredKey = "saved-test-api-key" };
        var environment = new InMemoryEnvironmentVariableAccessor();
        var manager = new OpenAIApiKeyManager(store, environment);

        var initial = await manager.GetStateAsync(CancellationToken.None);
        Assert.Equal(OpenAIApiKeySource.WindowsCredentialManager, initial.Source);

        environment.Set(
            OpenAIApiKeyManager.EnvironmentVariableName,
            "environment-test-api-key",
            EnvironmentVariableTarget.User);

        var resolved = await manager.GetStateAsync(CancellationToken.None);

        Assert.Equal(OpenAIApiKeySource.EnvironmentVariable, resolved.Source);
        Assert.Equal("environment-test-api-key", resolved.ApiKey);
        Assert.Equal(
            "environment-test-api-key",
            environment.Get(OpenAIApiKeyManager.EnvironmentVariableName, EnvironmentVariableTarget.Process));
    }

    [Fact]
    public async Task MainWindowStartIsBlockedAndConfigurationIsPromptedWithoutApiKey()
    {
        var manager = new OpenAIApiKeyManager(
            new InMemoryApiKeyStore(),
            new InMemoryEnvironmentVariableAccessor());
        var dialog = new RecordingApiKeyConfigurationDialogService(result: false);
        var pipeline = new SubtitlePipelineService(new NoOpSubtitleOverlayController());
        var viewModel = new MainWindowViewModel(
            pipeline,
            new InMemorySettingsStore(),
            manager,
            dialog,
            Dispatcher.CurrentDispatcher);

        try
        {
            await viewModel.StartAsync();

            Assert.Equal(1, dialog.ShowCount);
            Assert.False(viewModel.IsRunning);
            Assert.Equal("Error", viewModel.AppStatus);
            Assert.Contains("Configure API Key", viewModel.ErrorMessage, StringComparison.Ordinal);
        }
        finally
        {
            await viewModel.DisposeAsync();
        }
    }

    [Fact]
    public async Task MainWindowInitializationPromptsForFirstApiKeyConfiguration()
    {
        var manager = new OpenAIApiKeyManager(
            new InMemoryApiKeyStore(),
            new InMemoryEnvironmentVariableAccessor());
        var dialog = new RecordingApiKeyConfigurationDialogService(result: false);
        var viewModel = new MainWindowViewModel(
            new SubtitlePipelineService(new NoOpSubtitleOverlayController()),
            new InMemorySettingsStore(),
            manager,
            dialog,
            Dispatcher.CurrentDispatcher);

        try
        {
            await viewModel.InitializeAsync();

            Assert.Equal(1, dialog.ShowCount);
            Assert.False(viewModel.IsApiKeyConfigured);
            Assert.Equal("API Key: Not configured", viewModel.ApiKeyStatus);
        }
        finally
        {
            await viewModel.DisposeAsync();
        }
    }

    [Fact]
    public async Task ApiKeyValidationUsesLightweightAuthenticatedGetRequest()
    {
        var handler = new ApiKeyValidationHandler();
        using var httpClient = new HttpClient(handler);
        using var service = new OpenAIApiKeyValidationService(httpClient);

        var result = await service.ValidateAsync("validation-test-api-key", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task OpenAITranslationServiceReportsMissingApiKeyBeforeNetworkCall()
    {
        using var service = new OpenAITranslationService(
            new HttpClient(new JsonTranslationHandler("""{"output_text":"不会调用"}""")),
            new MissingTranslationCredentialProvider());

        var exception = await Assert.ThrowsAsync<TranslationException>(() => service.TranslateAsync(
            new TranslationRequest("hello", "en", "zh-CN"),
            CancellationToken.None));

        Assert.Equal(TranslationErrorCode.ApiKeyMissing, exception.ErrorCode);
    }

    [Fact]
    public void OpenAITranslationServiceUsesStabilityOrientedDefaultTimeout()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), OpenAITranslationService.DefaultRequestTimeout);
    }

    [Fact]
    public async Task OpenAITranslationServiceSendsPreviousContextForCurrentSubtitleOnly()
    {
        using var service = new OpenAITranslationService(
            new HttpClient(new InspectingTranslationHandler(body =>
            {
                Assert.Contains("Previous source subtitle for context", body);
                Assert.Contains("Previous translated subtitle for context", body);
                Assert.Contains("Do not translate, repeat, summarize, or continue the previous context.", body);
                Assert.Contains("Current subtitle text", body);
                Assert.Contains("current sentence", body);
                Assert.Contains("Source language: de", body);
                Assert.Contains("Target language: ja", body);
            })),
            new FixedTranslationCredentialProvider());

        var result = await service.TranslateAsync(
            new TranslationRequest(
                "current sentence",
                "de",
                "ja",
                PreviousSourceText: "previous sentence",
                PreviousTranslatedText: "上一句"),
            CancellationToken.None);

        Assert.Equal("ok", result.TranslatedText);
    }

    [Fact]
    public async Task OpenAITranslationServiceRejectsEmptySourceText()
    {
        using var service = new OpenAITranslationService(
            new HttpClient(new JsonTranslationHandler("""{"output_text":"不会调用"}""")),
            new FixedTranslationCredentialProvider());

        var exception = await Assert.ThrowsAsync<TranslationException>(() => service.TranslateAsync(
            new TranslationRequest("   ", "en", "zh-CN"),
            CancellationToken.None));

        Assert.Equal(TranslationErrorCode.EmptySourceText, exception.ErrorCode);
    }

    [Fact]
    public async Task OpenAITranslationServiceReadsOutputText()
    {
        using var service = new OpenAITranslationService(
            new HttpClient(new JsonTranslationHandler("""{"error":null,"output_text":"你好，世界。"}""")),
            new FixedTranslationCredentialProvider());

        var result = await service.TranslateAsync(
            new TranslationRequest("Hello, world.", "en", "zh-CN"),
            CancellationToken.None);

        Assert.Equal("Hello, world.", result.SourceText);
        Assert.Equal("你好，世界。", result.TranslatedText);
        Assert.Equal("en", result.SourceLanguage);
        Assert.Equal("zh-CN", result.TargetLanguage);
    }

    [Fact]
    public async Task OpenAITranslationServiceReportsEmptyResponse()
    {
        using var service = new OpenAITranslationService(
            new HttpClient(new JsonTranslationHandler("""{"output_text":"   "}""")),
            new FixedTranslationCredentialProvider());

        var exception = await Assert.ThrowsAsync<TranslationException>(() => service.TranslateAsync(
            new TranslationRequest("hello", "en", "zh-CN"),
            CancellationToken.None));

        Assert.Equal(TranslationErrorCode.EmptyResponse, exception.ErrorCode);
    }

    [Fact]
    public async Task OpenAITranslationServiceMapsNetworkFailure()
    {
        using var service = new OpenAITranslationService(
            new HttpClient(new ThrowingTranslationHandler(new HttpRequestException("network down"))),
            new FixedTranslationCredentialProvider());

        var exception = await Assert.ThrowsAsync<TranslationException>(() => service.TranslateAsync(
            new TranslationRequest("hello", "en", "zh-CN"),
            CancellationToken.None));

        Assert.Equal(TranslationErrorCode.NetworkFailure, exception.ErrorCode);
    }

    [Fact]
    public async Task OpenAITranslationServiceMapsTimeout()
    {
        using var service = new OpenAITranslationService(
            new HttpClient(new DelayedTranslationHandler(TimeSpan.FromSeconds(5))),
            new FixedTranslationCredentialProvider(),
            new Uri(OpenAITranslationService.DefaultEndpoint),
            OpenAITranslationService.DefaultModel,
            TimeSpan.FromMilliseconds(20));

        var exception = await Assert.ThrowsAsync<TranslationException>(() => service.TranslateAsync(
            new TranslationRequest("hello", "en", "zh-CN"),
            CancellationToken.None));

        Assert.Equal(TranslationErrorCode.Timeout, exception.ErrorCode);
    }

    [Fact]
    public async Task OpenAITranslationServiceRetriesOneTransientTimeout()
    {
        var handler = new TimeoutThenSuccessTranslationHandler();
        using var service = new OpenAITranslationService(
            new HttpClient(handler),
            new FixedTranslationCredentialProvider(),
            new Uri(OpenAITranslationService.DefaultEndpoint),
            OpenAITranslationService.DefaultModel,
            TimeSpan.FromMilliseconds(20));

        var result = await service.TranslateAsync(
            new TranslationRequest("hello", "en", "zh-CN"),
            CancellationToken.None);

        Assert.Equal(2, handler.RequestCount);
        Assert.Equal("retry succeeded", result.TranslatedText);
    }

    private static AudioFrame CreateStereoFloatFrame(int sampleRate, int sampleCountPerChannel, float left, float right)
    {
        var bytes = new byte[sampleCountPerChannel * 2 * 4];
        for (var i = 0; i < sampleCountPerChannel; i++)
        {
            BitConverter.GetBytes(left).CopyTo(bytes, i * 8);
            BitConverter.GetBytes(right).CopyTo(bytes, i * 8 + 4);
        }

        return new AudioFrame(
            bytes,
            DateTimeOffset.UtcNow,
            TimeSpan.FromSeconds((double)sampleCountPerChannel / sampleRate),
            sampleRate,
            2,
            32,
            AudioSampleFormat.IeeeFloat32);
    }

    private static async IAsyncEnumerable<AudioFrame> EmptyAudioFrames()
    {
        await Task.CompletedTask;
        yield break;
    }

    private static async IAsyncEnumerable<AudioFrame> SingleAudioFrame(AudioFrame frame)
    {
        await Task.CompletedTask;
        yield return frame;
    }

    private sealed class MissingSpeechCredentialProvider : ISpeechRecognitionCredentialProvider
    {
        public SpeechRecognitionCredentials GetCredentials(SpeechRecognitionOptions options)
        {
            throw new SpeechRecognitionException(
                SpeechRecognitionErrorCode.ApiKeyMissing,
                "Test credential provider intentionally has no API key.");
        }
    }

    private sealed class FixedSpeechCredentialProvider : ISpeechRecognitionCredentialProvider
    {
        public SpeechRecognitionCredentials GetCredentials(SpeechRecognitionOptions options)
        {
            return new SpeechRecognitionCredentials("test-openai-key", Region: null);
        }
    }

    private sealed class JsonSpeechTranscriptionHandler : HttpMessageHandler
    {
        private readonly string _json;
        private readonly string? _expectedLanguage;

        public JsonSpeechTranscriptionHandler(string json, string? expectedLanguage = null)
        {
            _json = json;
            _expectedLanguage = expectedLanguage;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("test-openai-key", request.Headers.Authorization?.Parameter);

            if (_expectedLanguage is not null)
            {
                var multipart = Assert.IsType<MultipartFormDataContent>(request.Content);
                var languagePart = multipart.Single(part =>
                    string.Equals(
                        part.Headers.ContentDisposition?.Name?.Trim('"'),
                        "language",
                        StringComparison.Ordinal));
                Assert.Equal(_expectedLanguage, await languagePart.ReadAsStringAsync(cancellationToken));
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(_json)
            };
        }
    }

    private sealed class MissingTranslationCredentialProvider : ITranslationCredentialProvider
    {
        public TranslationCredentials GetCredentials()
        {
            throw new TranslationException(
                TranslationErrorCode.ApiKeyMissing,
                "Test translation credential provider intentionally has no API key.");
        }
    }

    private sealed class FixedTranslationCredentialProvider : ITranslationCredentialProvider
    {
        public TranslationCredentials GetCredentials()
        {
            return new TranslationCredentials("test-openai-key");
        }
    }

    private sealed class JsonTranslationHandler : HttpMessageHandler
    {
        private readonly string _json;

        public JsonTranslationHandler(string json)
        {
            _json = json;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("test-openai-key", request.Headers.Authorization?.Parameter);

            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Assert.Contains("gpt-4.1-mini", body);
            Assert.Contains("Target language", body);

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(_json)
            };
        }
    }

    private sealed class InspectingTranslationHandler : HttpMessageHandler
    {
        private readonly Action<string> _inspect;

        public InspectingTranslationHandler(Action<string> inspect)
        {
            _inspect = inspect;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            _inspect(body);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""{"output_text":"ok"}""")
            };
        }
    }

    private sealed class ThrowingTranslationHandler : HttpMessageHandler
    {
        private readonly Exception _exception;

        public ThrowingTranslationHandler(Exception exception)
        {
            _exception = exception;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromException<HttpResponseMessage>(_exception);
        }
    }

    private sealed class DelayedTranslationHandler : HttpMessageHandler
    {
        private readonly TimeSpan _delay;

        public DelayedTranslationHandler(TimeSpan delay)
        {
            _delay = delay;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(_delay, cancellationToken);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""{"output_text":"迟到的翻译"}""")
            };
        }
    }

    private sealed class TimeoutThenSuccessTranslationHandler : HttpMessageHandler
    {
        private int _requestCount;

        public int RequestCount => Volatile.Read(ref _requestCount);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _requestCount) == 1)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""{"output_text":"retry succeeded"}""")
            };
        }
    }

    private sealed class InMemoryApiKeyStore : IApiKeyStore
    {
        public string? StoredKey { get; set; }

        public Task<string?> ReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(StoredKey);
        }

        public Task SaveAsync(string apiKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StoredKey = apiKey;
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StoredKey = null;
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryEnvironmentVariableAccessor : IEnvironmentVariableAccessor
    {
        private readonly Dictionary<(string Name, EnvironmentVariableTarget Target), string> _values = new();

        public string? Get(string name, EnvironmentVariableTarget target)
        {
            return _values.GetValueOrDefault((name, target));
        }

        public void SetProcess(string name, string? value)
        {
            Set(name, value, EnvironmentVariableTarget.Process);
        }

        public void Set(string name, string? value, EnvironmentVariableTarget target)
        {
            if (value is null)
            {
                _values.Remove((name, target));
                return;
            }

            _values[(name, target)] = value;
        }
    }

    private sealed class RecordingApiKeyConfigurationDialogService : IApiKeyConfigurationDialogService
    {
        private readonly bool _result;

        public RecordingApiKeyConfigurationDialogService(bool result)
        {
            _result = result;
        }

        public int ShowCount { get; private set; }

        public Task<bool> ShowAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ShowCount++;
            return Task.FromResult(_result);
        }
    }

    private sealed class InMemorySettingsStore : ISettingsStore
    {
        public Task<UserSettings> LoadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(UserSettings.CreateDefault());
        }

        public Task SaveAsync(UserSettings settings, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpSubtitleOverlayController : ISubtitleOverlayController
    {
        public void Show()
        {
        }

        public void Hide()
        {
        }

        public void Update(SubtitleOverlayState state)
        {
        }
    }

    private sealed class ApiKeyValidationHandler : HttpMessageHandler
    {
        private int _requestCount;

        public int RequestCount => Volatile.Read(ref _requestCount);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _requestCount);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https://api.openai.com/v1/models", request.RequestUri?.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("validation-test-api-key", request.Headers.Authorization?.Parameter);

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }
}
