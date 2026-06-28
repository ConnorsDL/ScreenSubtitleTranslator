using ScreenSubtitleTranslator.AudioCapture;
using ScreenSubtitleTranslator.SpeechRecognition;
using ScreenSubtitleTranslator.Translation;

var duration = args.Length > 0 && int.TryParse(args[0], out var parsedSeconds)
    ? Math.Max(1, parsedSeconds)
    : 60;
var language = args.Length > 1 && !string.IsNullOrWhiteSpace(args[1])
    ? args[1]
    : "en-US";
var chunkSeconds = args.Length > 2 && int.TryParse(args[2], out var parsedChunkSeconds)
    ? Math.Max(1, parsedChunkSeconds)
    : (int)SpeechRecognitionOptions.CreateDefault().AudioChunkDuration.TotalSeconds;
var targetLanguage = args.Length > 3 && !string.IsNullOrWhiteSpace(args[3])
    ? args[3]
    : TranslationOptions.CreateDefault().TargetLanguage;
var sourceLanguage = NormalizeLanguageForTranslation(language);

using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(duration));
var audioBuffer = new AudioBuffer(new AudioBufferOptions(Capacity: 512));
using var audioCapture = new AudioCaptureService();
using var speechRecognition = new OpenAISpeechRecognitionService();
using var translation = new OpenAITranslationService();
var translationTasks = new List<Task>();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

audioCapture.StateChanged += (_, eventArgs) =>
{
    Console.WriteLine($"[audio] state={eventArgs.State} code={eventArgs.ErrorCode} message={eventArgs.Message}");
};

Console.WriteLine("SpeechRecognitionProbe");
Console.WriteLine("Play an English YouTube video now.");
Console.WriteLine("Required environment variable: OPENAI_API_KEY.");
Console.WriteLine("This probe captures system output through AudioCapture; it does not use microphone input.");
Console.WriteLine($"Provider=OpenAI, model={SpeechRecognitionOptions.CreateDefault().ModelId}, chunkSeconds={chunkSeconds}, language={language}");
Console.WriteLine($"Translation=OpenAI, model={TranslationOptions.CreateDefault().ModelId}, sourceLanguage={sourceLanguage}, targetLanguage={targetLanguage}");
Console.WriteLine();

try
{
    await audioCapture.StartAsync(AudioCaptureOptions.CreateDefault(), audioBuffer, cancellation.Token);

    var options = SpeechRecognitionOptions.CreateDefault() with
    {
        SourceLanguage = language,
        EnablePartialResults = true,
        AudioChunkDuration = TimeSpan.FromSeconds(chunkSeconds)
    };

    await foreach (var result in speechRecognition
        .RecognizeAsync(audioBuffer.ReadAllAsync(cancellation.Token), options, cancellation.Token)
        .WithCancellation(cancellation.Token))
    {
        if (!result.IsFinal)
        {
            Console.WriteLine($"[{GetConsoleLanguageLabel(sourceLanguage)} partial] {result.Text}");
            continue;
        }

        Console.WriteLine($"[{GetConsoleLanguageLabel(sourceLanguage)} final] {result.Text}");
        translationTasks.Add(PrintTranslationAsync(
            translation,
            result.Text,
            sourceLanguage,
            targetLanguage,
            CancellationToken.None));
    }

    await Task.WhenAll(translationTasks);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Speech recognition probe stopped.");
}
catch (SpeechRecognitionException exception)
{
    Console.WriteLine($"Speech recognition failed. code={exception.ErrorCode}, message={exception.Message}");
}
catch (AudioCaptureException exception)
{
    Console.WriteLine($"Audio capture failed. code={exception.ErrorCode}, message={exception.Message}");
}
finally
{
    await audioCapture.StopAsync(CancellationToken.None);
    if (translationTasks.Count > 0)
    {
        await Task.WhenAll(translationTasks);
    }
}

static async Task PrintTranslationAsync(
    ITranslationService translation,
    string text,
    string sourceLanguage,
    string targetLanguage,
    CancellationToken cancellationToken)
{
    try
    {
        var result = await translation.TranslateAsync(
            new TranslationRequest(text, sourceLanguage, targetLanguage),
            cancellationToken);
        Console.WriteLine($"[{GetConsoleLanguageLabel(result.TargetLanguage)} final] {result.TranslatedText}");
    }
    catch (TranslationException exception)
    {
        Console.WriteLine($"Translation failed. code={exception.ErrorCode}, message={exception.Message}");
    }
}

static string NormalizeLanguageForTranslation(string language)
{
    if (string.IsNullOrWhiteSpace(language) || string.Equals(language, "auto", StringComparison.OrdinalIgnoreCase))
    {
        return TranslationOptions.CreateDefault().SourceLanguage;
    }

    var trimmed = language.Trim();
    var separatorIndex = trimmed.IndexOf('-', StringComparison.Ordinal);
    return separatorIndex > 0
        ? trimmed[..separatorIndex].ToLowerInvariant()
        : trimmed.ToLowerInvariant();
}

static string GetConsoleLanguageLabel(string language)
{
    if (string.IsNullOrWhiteSpace(language))
    {
        return "text";
    }

    var trimmed = language.Trim();
    var separatorIndex = trimmed.IndexOf('-', StringComparison.Ordinal);
    return separatorIndex > 0
        ? trimmed[..separatorIndex].ToLowerInvariant()
        : trimmed.ToLowerInvariant();
}
