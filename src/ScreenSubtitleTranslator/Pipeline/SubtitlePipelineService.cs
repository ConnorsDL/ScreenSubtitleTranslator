using System.Diagnostics;
using ScreenSubtitleTranslator.AudioCapture;
using ScreenSubtitleTranslator.Overlay;
using ScreenSubtitleTranslator.SpeechRecognition;
using ScreenSubtitleTranslator.Translation;

namespace ScreenSubtitleTranslator.Pipeline;

public sealed class SubtitlePipelineService : IAsyncDisposable
{
    private static readonly TimeSpan TranslationDrainTimeout = TimeSpan.FromSeconds(10);

    private readonly ISubtitleOverlayController _overlayController;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly SemaphoreSlim _translationGate = new(1, 1);
    private readonly object _translationTaskLock = new();
    private readonly List<Task> _translationTasks = new();
    private readonly RecentTextCache _recentEnglishFinals = new(24);
    private readonly RecentTextCache _recentChineseFinals = new(24);

    private CancellationTokenSource? _cancellation;
    private CancellationTokenSource? _translationCancellation;
    private AudioBuffer? _audioBuffer;
    private AudioCaptureService? _audioCapture;
    private OpenAISpeechRecognitionService? _speechRecognition;
    private OpenAITranslationService? _translation;
    private Task? _recognitionTask;
    private Task? _audioMonitorTask;
    private SubtitlePipelineOptions? _options;
    private Stopwatch? _runStopwatch;
    private string? _lastPartialKey;
    private string? _lastAcceptedEnglishFinal;
    private string? _lastTranslatedSourceText;
    private string? _lastTranslatedText;
    private bool _firstPartialEmitted;
    private bool _firstFinalEmitted;
    private bool _firstTranslationEmitted;
    private long _nextSequenceId;
    private int _enFinalReceivedCount;
    private int _translationQueuedCount;
    private int _translationCompletedCount;
    private int _translationSkippedDuplicateCount;
    private int _translationFailedCount;
    private int _translationCanceledCount;
    private int _overlayUpdatedCount;

    public SubtitlePipelineService(ISubtitleOverlayController overlayController)
    {
        _overlayController = overlayController ?? throw new ArgumentNullException(nameof(overlayController));
    }

    public event EventHandler<SubtitlePipelineStatusChangedEventArgs>? StatusChanged;

    public bool IsRunning { get; private set; }

    public async Task StartAsync(SubtitlePipelineOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (IsRunning)
            {
                return;
            }

            ResetRunState();
            _options = options;
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _translationCancellation = new CancellationTokenSource();
            _audioBuffer = new AudioBuffer(new AudioBufferOptions(Capacity: 512));
            _audioCapture = new AudioCaptureService();
            _speechRecognition = new OpenAISpeechRecognitionService();
            _translation = new OpenAITranslationService();
            try
            {
                _audioCapture.StateChanged += OnAudioCaptureStateChanged;

                Emit("Pipeline", "Starting", "Starting audio capture, recognition, translation, and overlay.");
                if (options.ShowOverlay)
                {
                    _overlayController.Update(SubtitleOverlayState.Empty(options.SourceLanguage, options.TargetLanguage));
                    _overlayController.Show();
                    Emit("Overlay", "Visible", "Overlay window is ready at the bottom of the screen.");
                }

                await _audioCapture
                    .StartAsync(AudioCaptureOptions.CreateDefault(), _audioBuffer, _cancellation.Token)
                    .ConfigureAwait(false);

                IsRunning = true;
                _recognitionTask = Task.Run(
                    () => RunRecognitionAsync(_audioBuffer, options, _cancellation.Token),
                    CancellationToken.None);
                _audioMonitorTask = Task.Run(
                    () => MonitorAudioInputAsync(_audioBuffer, _cancellation.Token),
                    CancellationToken.None);
                Emit("Pipeline", "Running", "Listening to system audio.");
            }
            catch
            {
                _overlayController.Update(SubtitleOverlayState.Empty(options.SourceLanguage, options.TargetLanguage));
                _overlayController.Hide();
                DisposeCurrentServices();
                throw;
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycleLock.WaitAsync().ConfigureAwait(false);

        try
        {
            if (!IsRunning && _recognitionTask is null)
            {
                _overlayController.Update(SubtitleOverlayState.Empty(_options?.SourceLanguage ?? "en", _options?.TargetLanguage ?? "zh-CN"));
                _overlayController.Hide();
                return;
            }

            Emit("Pipeline", "Stopping", "Stopping background tasks.");
            IsRunning = false;
            _cancellation?.Cancel();
            _audioBuffer?.Complete();

            if (_audioCapture is not null)
            {
                await _audioCapture.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }

            if (_recognitionTask is not null)
            {
                await IgnoreCancellationAsync(_recognitionTask).ConfigureAwait(false);
            }

            if (_audioMonitorTask is not null)
            {
                await IgnoreCancellationAsync(_audioMonitorTask).ConfigureAwait(false);
            }

            await DrainTranslationQueueAsync(TranslationDrainTimeout).ConfigureAwait(false);
            EmitTranslationSummary();

            DisposeCurrentServices();
            _overlayController.Update(SubtitleOverlayState.Empty(_options?.SourceLanguage ?? "en", _options?.TargetLanguage ?? "zh-CN"));
            _overlayController.Hide();
            Emit("Overlay", "Hidden", "Overlay subtitles were cleared.");
            Emit("Pipeline", "Stopped", "All background tasks stopped.");
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _translationGate.Dispose();
        _lifecycleLock.Dispose();
    }

    private async Task RunRecognitionAsync(
        AudioBuffer audioBuffer,
        SubtitlePipelineOptions options,
        CancellationToken cancellationToken)
    {
        var speechRecognition = _speechRecognition;
        if (speechRecognition is null)
        {
            return;
        }

        try
        {
            var speechOptions = SpeechRecognitionOptions.CreateDefault() with
            {
                SourceLanguage = options.SourceLanguage,
                EnablePartialResults = true,
                AudioChunkDuration = options.AudioChunkDuration
            };

            Emit("SpeechRecognition", "Running", "Waiting for recognized speech.");
            await foreach (var result in speechRecognition
                .RecognizeAsync(audioBuffer.ReadAllAsync(cancellationToken), speechOptions, cancellationToken)
                .WithCancellation(cancellationToken)
                .ConfigureAwait(false))
            {
                if (string.IsNullOrWhiteSpace(result.Text))
                {
                    continue;
                }

                if (!result.IsFinal)
                {
                    HandlePartialResult(result);
                    continue;
                }

                var sequenceId = Interlocked.Increment(ref _nextSequenceId);
                Interlocked.Increment(ref _enFinalReceivedCount);
                Emit(
                    "TranslationQueue",
                    "EnFinalReceived",
                    $"en final received: id={sequenceId} {FormatSpeechMetric(result)}");

                var finalResult = PrepareFinalResult(sequenceId, result);
                if (finalResult is null)
                {
                    continue;
                }

                Emit("SpeechRecognition", "Final", $"id={sequenceId} {FormatSpeechMetric(finalResult)}");
                Interlocked.Increment(ref _translationQueuedCount);
                Emit(
                    "TranslationQueue",
                    "Queued",
                    $"translation queued: id={sequenceId} pending={GetPendingTranslationTaskCount() + 1}");
                var translationToken = _translationCancellation?.Token ?? CancellationToken.None;
                AddTranslationTask(TranslateAndShowAsync(new TranslationWorkItem(sequenceId, finalResult), options, translationToken));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Emit("SpeechRecognition", "Stopped", "Recognition was canceled.");
        }
        catch (SpeechRecognitionException exception)
        {
            Emit("SpeechRecognition", "Error", $"{exception.ErrorCode}: {exception.Message}");
        }
        catch (Exception exception)
        {
            Emit("SpeechRecognition", "Error", exception.Message);
        }
    }

    private async Task TranslateAndShowAsync(
        TranslationWorkItem workItem,
        SubtitlePipelineOptions options,
        CancellationToken cancellationToken)
    {
        var translation = _translation;
        if (translation is null)
        {
            return;
        }

        var speechResult = workItem.SpeechResult;
        try
        {
            await _translationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Emit(
                    "TranslationQueue",
                    "Started",
                    $"translation started: id={workItem.SequenceId} pending={GetPendingTranslationTaskCount()}");
                var request = new TranslationRequest(
                    speechResult.Text,
                    NormalizeLanguage(options.SourceLanguage),
                    options.TargetLanguage,
                    _lastTranslatedSourceText,
                    _lastTranslatedText);
                Emit("Translation", "Translating", $"id={workItem.SequenceId} elapsedMs={GetElapsedMilliseconds()} text={FormatLogText(speechResult.Text)}");
                var translationStopwatch = Stopwatch.StartNew();
                var result = await translation.TranslateAsync(request, cancellationToken).ConfigureAwait(false);
                translationStopwatch.Stop();

                if (!_recentChineseFinals.AddIfNew(result.TranslatedText, workItem.SequenceId, out var originalSequenceId))
                {
                    Interlocked.Increment(ref _translationSkippedDuplicateCount);
                    Emit(
                        "TranslationQueue",
                        "SkippedDuplicate",
                        $"translation skipped duplicate: id={workItem.SequenceId} originalId={originalSequenceId} reason=zh-final-duplicate elapsedMs={GetElapsedMilliseconds()} translationMs={translationStopwatch.ElapsedMilliseconds} text={FormatLogText(result.TranslatedText)}");
                    return;
                }

                _lastTranslatedSourceText = result.SourceText;
                _lastTranslatedText = result.TranslatedText;

                if (!_firstTranslationEmitted)
                {
                    _firstTranslationEmitted = true;
                    EmitMetric("FirstZhFinal", $"elapsedMs={GetElapsedMilliseconds()} translationMs={translationStopwatch.ElapsedMilliseconds}");
                }

                Emit(
                    "Translation",
                    "Final",
                    $"id={workItem.SequenceId} elapsedMs={GetElapsedMilliseconds()} translationMs={translationStopwatch.ElapsedMilliseconds} text={FormatLogText(result.TranslatedText)}");
                Interlocked.Increment(ref _translationCompletedCount);
                Emit(
                    "TranslationQueue",
                    "Completed",
                    $"translation completed: id={workItem.SequenceId} elapsedMs={GetElapsedMilliseconds()} translationMs={translationStopwatch.ElapsedMilliseconds}");

                if (options.ShowOverlay)
                {
                    var overlayStopwatch = Stopwatch.StartNew();
                    _overlayController.Update(new SubtitleOverlayState(
                        options.ShowOriginalText ? result.SourceText : string.Empty,
                        result.TranslatedText,
                        result.SourceLanguage,
                        result.TargetLanguage,
                        result.TranslatedAt));
                    overlayStopwatch.Stop();
                    Interlocked.Increment(ref _overlayUpdatedCount);
                    Emit(
                        "TranslationQueue",
                        "OverlayUpdated",
                        $"overlay updated: id={workItem.SequenceId} elapsedMs={GetElapsedMilliseconds()} overlayUpdateMs={overlayStopwatch.ElapsedMilliseconds}");
                    Emit(
                        "Overlay",
                        "Displaying",
                        $"id={workItem.SequenceId} elapsedMs={GetElapsedMilliseconds()} overlayUpdateMs={overlayStopwatch.ElapsedMilliseconds} text={FormatLogText(result.TranslatedText)}");
                }
            }
            finally
            {
                _translationGate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Interlocked.Increment(ref _translationCanceledCount);
            Emit(
                "TranslationQueue",
                "Canceled",
                $"translation canceled: id={workItem.SequenceId} elapsedMs={GetElapsedMilliseconds()}");
        }
        catch (TranslationException exception)
        {
            Interlocked.Increment(ref _translationFailedCount);
            Emit(
                "TranslationQueue",
                "Failed",
                $"translation failed: id={workItem.SequenceId} code={exception.ErrorCode} message={exception.Message}");
            Emit("Translation", "Error", $"id={workItem.SequenceId} {exception.ErrorCode}: {exception.Message}");
        }
        catch (Exception exception)
        {
            Interlocked.Increment(ref _translationFailedCount);
            Emit(
                "TranslationQueue",
                "Failed",
                $"translation failed: id={workItem.SequenceId} code=Unexpected message={exception.Message}");
            Emit("Translation", "Error", $"id={workItem.SequenceId} {exception.Message}");
        }
    }

    private void AddTranslationTask(Task task)
    {
        lock (_translationTaskLock)
        {
            _translationTasks.Add(task);
        }

        _ = task.ContinueWith(
            completedTask =>
            {
                lock (_translationTaskLock)
                {
                    _translationTasks.Remove(completedTask);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void OnAudioCaptureStateChanged(object? sender, AudioCaptureStateChangedEventArgs eventArgs)
    {
        Emit(
            "AudioCapture",
            eventArgs.State.ToString(),
            eventArgs.Message ?? eventArgs.State.ToString(),
            eventArgs.ErrorCode == AudioCaptureErrorCode.None ? null : eventArgs.ErrorCode.ToString(),
            eventArgs.DeviceName);
        if (eventArgs.State == AudioCaptureState.Capturing)
        {
            EmitMetric("StartToAudioCaptureCapturing", $"elapsedMs={GetElapsedMilliseconds()}");
        }
    }

    private void HandlePartialResult(SpeechRecognitionResult result)
    {
        var text = result.Text.Trim();
        var key = SubtitleTextUtilities.NormalizeForComparison(text);
        if (string.IsNullOrWhiteSpace(key) || string.Equals(key, _lastPartialKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastPartialKey = key;
        if (!_firstPartialEmitted)
        {
            _firstPartialEmitted = true;
            EmitMetric(
                "FirstEnPartial",
                $"elapsedMs={GetElapsedMilliseconds()} chunk={result.ChunkIndex} sttMs={GetMilliseconds(result.SttDuration)}");
        }

        Emit("SpeechRecognition", "Partial", FormatSpeechMetric(result with { Text = text }));
    }

    private SpeechRecognitionResult? PrepareFinalResult(long sequenceId, SpeechRecognitionResult result)
    {
        var text = result.Text.Trim();
        var trimmed = SubtitleTextUtilities.TrimRepeatedPrefixFromPrevious(_lastAcceptedEnglishFinal, text);
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            Interlocked.Increment(ref _translationSkippedDuplicateCount);
            Emit(
                "TranslationQueue",
                "SkippedDuplicate",
                $"translation skipped duplicate: id={sequenceId} originalId=0 reason=overlap-trim-empty elapsedMs={GetElapsedMilliseconds()} text={FormatLogText(text)}");
            return null;
        }

        if (!string.Equals(trimmed, text, StringComparison.Ordinal))
        {
            Emit("SpeechRecognition", "OverlapTrimmed", $"elapsedMs={GetElapsedMilliseconds()} text={FormatLogText(trimmed)}");
            text = trimmed;
        }

        if (!_recentEnglishFinals.AddIfNew(text, sequenceId, out var originalSequenceId))
        {
            Interlocked.Increment(ref _translationSkippedDuplicateCount);
            Emit(
                "TranslationQueue",
                "SkippedDuplicate",
                $"translation skipped duplicate: id={sequenceId} originalId={originalSequenceId} reason=en-final-duplicate elapsedMs={GetElapsedMilliseconds()} text={FormatLogText(text)}");
            return null;
        }

        if (SubtitleTextUtilities.EndsWithEllipsis(text))
        {
            Emit("SpeechRecognition", "EllipsisFinal", $"elapsedMs={GetElapsedMilliseconds()} text={FormatLogText(text)}");
        }

        var finalResult = result with { Text = text };
        _lastAcceptedEnglishFinal = text;
        if (!_firstFinalEmitted)
        {
            _firstFinalEmitted = true;
            EmitMetric(
                "FirstEnFinal",
                $"elapsedMs={GetElapsedMilliseconds()} chunk={finalResult.ChunkIndex} sttMs={GetMilliseconds(finalResult.SttDuration)}");
        }

        return finalResult;
    }

    private async Task MonitorAudioInputAsync(AudioBuffer audioBuffer, CancellationToken cancellationToken)
    {
        try
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
                if (audioBuffer.BytesWritten > 0)
                {
                    return;
                }
            }

            if (audioBuffer.BytesWritten == 0)
            {
                Emit("AudioCapture", "Error", "No system audio was captured within 15 seconds.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void DisposeCurrentServices()
    {
        if (_audioCapture is not null)
        {
            _audioCapture.StateChanged -= OnAudioCaptureStateChanged;
            _audioCapture.Dispose();
        }

        _speechRecognition?.Dispose();
        _translation?.Dispose();
        _cancellation?.Dispose();

        _audioCapture = null;
        _audioBuffer = null;
        _speechRecognition = null;
        _translation = null;
        _cancellation = null;
        _recognitionTask = null;
        _audioMonitorTask = null;
        _translationCancellation?.Dispose();
        _translationCancellation = null;
        _runStopwatch = null;
    }

    private void Emit(
        string moduleName,
        string state,
        string details,
        string? errorCode = null,
        string? deviceName = null)
    {
        StatusChanged?.Invoke(
            this,
            new SubtitlePipelineStatusChangedEventArgs(moduleName, state, details, errorCode, deviceName));
    }

    private void EmitMetric(string state, string details)
    {
        Emit("Metrics", state, details);
    }

    private void ResetRunState()
    {
        _recentEnglishFinals.Clear();
        _recentChineseFinals.Clear();
        _lastPartialKey = null;
        _lastAcceptedEnglishFinal = null;
        _lastTranslatedSourceText = null;
        _lastTranslatedText = null;
        _firstPartialEmitted = false;
        _firstFinalEmitted = false;
        _firstTranslationEmitted = false;
        _nextSequenceId = 0;
        _enFinalReceivedCount = 0;
        _translationQueuedCount = 0;
        _translationCompletedCount = 0;
        _translationSkippedDuplicateCount = 0;
        _translationFailedCount = 0;
        _translationCanceledCount = 0;
        _overlayUpdatedCount = 0;
        _runStopwatch = Stopwatch.StartNew();
    }

    private async Task DrainTranslationQueueAsync(TimeSpan timeout)
    {
        var drainStopwatch = Stopwatch.StartNew();
        var pendingBefore = GetPendingTranslationTaskCount();
        Emit("TranslationQueue", "DrainStarted", $"pending={pendingBefore} timeoutMs={(long)timeout.TotalMilliseconds}");
        if (pendingBefore == 0)
        {
            Emit("TranslationQueue", "DrainCompleted", "drainMs=0 pending=0");
            return;
        }

        while (true)
        {
            var tasks = GetTranslationTasksSnapshot();
            if (tasks.Length == 0)
            {
                Emit("TranslationQueue", "DrainCompleted", $"drainMs={drainStopwatch.ElapsedMilliseconds} pending=0");
                return;
            }

            var remaining = timeout - drainStopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            var allTasks = Task.WhenAll(tasks);
            var completed = await Task.WhenAny(allTasks, Task.Delay(remaining)).ConfigureAwait(false);
            if (completed != allTasks)
            {
                continue;
            }

            await IgnoreCancellationAsync(allTasks).ConfigureAwait(false);
        }

        var pendingAfterTimeout = GetPendingTranslationTaskCount();
        Emit(
            "TranslationQueue",
            "DrainTimeout",
            $"drainMs={drainStopwatch.ElapsedMilliseconds} pending={pendingAfterTimeout} canceled={pendingAfterTimeout}");
        _translationCancellation?.Cancel();
        var remainingTasks = GetTranslationTasksSnapshot();
        if (remainingTasks.Length > 0)
        {
            await IgnoreCancellationAsync(Task.WhenAll(remainingTasks)).ConfigureAwait(false);
        }

        Emit("TranslationQueue", "DrainCanceled", $"drainMs={drainStopwatch.ElapsedMilliseconds} pending={GetPendingTranslationTaskCount()}");
    }

    private Task[] GetTranslationTasksSnapshot()
    {
        lock (_translationTaskLock)
        {
            return _translationTasks.ToArray();
        }
    }

    private int GetPendingTranslationTaskCount()
    {
        lock (_translationTaskLock)
        {
            return _translationTasks.Count;
        }
    }

    private void EmitTranslationSummary()
    {
        Emit(
            "TranslationQueue",
            "Summary",
            string.Join(
                ' ',
                $"enFinal={Volatile.Read(ref _enFinalReceivedCount)}",
                $"queued={Volatile.Read(ref _translationQueuedCount)}",
                $"completed={Volatile.Read(ref _translationCompletedCount)}",
                $"skippedDuplicate={Volatile.Read(ref _translationSkippedDuplicateCount)}",
                $"failed={Volatile.Read(ref _translationFailedCount)}",
                $"canceled={Volatile.Read(ref _translationCanceledCount)}",
                $"overlayUpdated={Volatile.Read(ref _overlayUpdatedCount)}",
                $"pending={GetPendingTranslationTaskCount()}"));
    }

    private long GetElapsedMilliseconds()
    {
        return _runStopwatch?.ElapsedMilliseconds ?? 0;
    }

    private static long GetMilliseconds(TimeSpan duration)
    {
        return (long)Math.Round(duration.TotalMilliseconds);
    }

    private string FormatSpeechMetric(SpeechRecognitionResult result)
    {
        return string.Join(
            ' ',
            $"elapsedMs={GetElapsedMilliseconds()}",
            $"chunk={result.ChunkIndex}",
            $"audioMs={GetMilliseconds(result.AudioDuration)}",
            $"sttMs={GetMilliseconds(result.SttDuration)}",
            $"text={FormatLogText(result.Text)}");
    }

    private static string FormatLogText(string text)
    {
        return text.ReplaceLineEndings(" ").Trim();
    }

    private static string NormalizeLanguage(string language)
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

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private sealed record TranslationWorkItem(long SequenceId, SpeechRecognitionResult SpeechResult);
}
