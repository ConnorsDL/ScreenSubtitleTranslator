using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using ScreenSubtitleTranslator.AudioCapture;

namespace ScreenSubtitleTranslator.SpeechRecognition;

public sealed class AzureSpeechRecognitionService : ISpeechRecognitionService
{
    private readonly ISpeechRecognitionCredentialProvider _credentialProvider;

    public AzureSpeechRecognitionService()
        : this(new EnvironmentSpeechRecognitionCredentialProvider())
    {
    }

    public AzureSpeechRecognitionService(ISpeechRecognitionCredentialProvider credentialProvider)
    {
        _credentialProvider = credentialProvider;
    }

    public async IAsyncEnumerable<SpeechRecognitionResult> RecognizeAsync(
        IAsyncEnumerable<AudioFrame> audioFrames,
        SpeechRecognitionOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audioFrames);
        ArgumentNullException.ThrowIfNull(options);

        var azureOptions = options with { ProviderId = "AzureSpeech" };
        var credentials = _credentialProvider.GetCredentials(azureOptions);
        if (string.IsNullOrWhiteSpace(credentials.Region))
        {
            throw new SpeechRecognitionException(
                SpeechRecognitionErrorCode.RegionMissing,
                "Azure Speech region is required for AzureSpeechRecognitionService.");
        }

        var resultChannel = Channel.CreateUnbounded<SpeechRecognitionResult>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        var speechConfig = SpeechConfig.FromSubscription(credentials.ApiKey, credentials.Region);
        speechConfig.SpeechRecognitionLanguage = NormalizeLanguage(options.SourceLanguage);

        var audioFormat = AudioStreamFormat.GetWaveFormatPCM(
            samplesPerSecond: Pcm16MonoAudioFrameConverter.TargetSampleRate,
            bitsPerSample: Pcm16MonoAudioFrameConverter.TargetBitsPerSample,
            channels: Pcm16MonoAudioFrameConverter.TargetChannelCount);
        using var pushStream = AudioInputStream.CreatePushStream(audioFormat);
        using var audioConfig = AudioConfig.FromStreamInput(pushStream);
        using var recognizer = new SpeechRecognizer(speechConfig, audioConfig);

        recognizer.Recognizing += (_, eventArgs) =>
        {
            if (!options.EnablePartialResults)
            {
                return;
            }

            var text = eventArgs.Result?.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            resultChannel.Writer.TryWrite(new SpeechRecognitionResult(
                text,
                speechConfig.SpeechRecognitionLanguage,
                IsFinal: false,
                DateTimeOffset.UtcNow));
        };

        recognizer.Recognized += (_, eventArgs) =>
        {
            if (eventArgs.Result?.Reason == ResultReason.NoMatch || string.IsNullOrWhiteSpace(eventArgs.Result?.Text))
            {
                return;
            }

            if (eventArgs.Result.Reason == ResultReason.RecognizedSpeech)
            {
                resultChannel.Writer.TryWrite(new SpeechRecognitionResult(
                    eventArgs.Result.Text,
                    speechConfig.SpeechRecognitionLanguage,
                    IsFinal: true,
                    DateTimeOffset.UtcNow));
            }
        };

        recognizer.Canceled += (_, eventArgs) =>
        {
            var exception = new SpeechRecognitionException(
                MapCancellationError(eventArgs.ErrorCode),
                $"Azure Speech recognition canceled: {eventArgs.Reason}; {eventArgs.ErrorCode}; {eventArgs.ErrorDetails}");
            resultChannel.Writer.TryComplete(exception);
        };

        recognizer.SessionStopped += (_, _) =>
        {
            resultChannel.Writer.TryComplete();
        };

        var converter = new Pcm16MonoAudioFrameConverter();
        var audioPump = Task.Run(async () =>
        {
            try
            {
                await foreach (var frame in audioFrames.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    var pcm16Mono = converter.Convert(frame);
                    if (pcm16Mono.Length == 0)
                    {
                        continue;
                    }

                    pushStream.Write(pcm16Mono);
                }

                pushStream.Close();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                pushStream.Close();
            }
            catch (Exception exception)
            {
                resultChannel.Writer.TryComplete(MapAudioPumpException(exception));
                pushStream.Close();
            }
        }, CancellationToken.None);

        try
        {
            await recognizer.StartContinuousRecognitionAsync().ConfigureAwait(false);

            await foreach (var result in resultChannel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return result;
            }
        }
        finally
        {
            pushStream.Close();
            await recognizer.StopContinuousRecognitionAsync().ConfigureAwait(false);

            try
            {
                await audioPump.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }
    }

    private static string NormalizeLanguage(string language)
    {
        return language.Trim().ToLowerInvariant() switch
        {
            "" => "en-US",
            "auto" => "en-US",
            "en" => "en-US",
            "zh" => "zh-CN",
            "ja" => "ja-JP",
            "ko" => "ko-KR",
            _ => language
        };
    }

    private static SpeechRecognitionErrorCode MapCancellationError(CancellationErrorCode errorCode)
    {
        var errorName = errorCode.ToString();
        if (errorName.Contains("Authentication", StringComparison.OrdinalIgnoreCase)
            || errorName.Contains("Forbidden", StringComparison.OrdinalIgnoreCase))
        {
            return SpeechRecognitionErrorCode.ApiKeyMissing;
        }

        if (errorName.Contains("Connection", StringComparison.OrdinalIgnoreCase)
            || errorName.Contains("Timeout", StringComparison.OrdinalIgnoreCase))
        {
            return SpeechRecognitionErrorCode.NetworkFailure;
        }

        return SpeechRecognitionErrorCode.ServiceCanceled;
    }

    private static Exception MapAudioPumpException(Exception exception)
    {
        return exception is SpeechRecognitionException
            ? exception
            : new SpeechRecognitionException(
                SpeechRecognitionErrorCode.ServiceInitializationFailed,
                "Azure Speech audio streaming failed.",
                exception);
    }
}
