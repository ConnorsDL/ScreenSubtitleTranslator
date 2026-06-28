using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace ScreenSubtitleTranslator.AudioCapture;

public sealed class AudioCaptureService : IAudioCaptureService, IDisposable
{
    private readonly object _syncRoot = new();
    private WasapiLoopbackCapture? _capture;
    private CancellationTokenRegistration _cancellationRegistration;
    private CancellationTokenSource? _captureCancellation;
    private MMDevice? _device;
    private string? _deviceId;
    private MMDeviceEnumerator? _deviceEnumerator;
    private IAudioFrameSink? _frameSink;
    private EndpointNotificationClient? _notificationClient;
    private bool _usesDefaultDevice;

    public event EventHandler<AudioCaptureStateChangedEventArgs>? StateChanged;

    public AudioCaptureState State { get; private set; } = AudioCaptureState.Stopped;

    public bool IsCapturing => State == AudioCaptureState.Capturing;

    public Task Start(AudioCaptureOptions options, IAudioFrameSink frameSink, CancellationToken cancellationToken)
    {
        return StartAsync(options, frameSink, cancellationToken);
    }

    public Task Pause(CancellationToken cancellationToken)
    {
        return PauseAsync(cancellationToken);
    }

    public Task Stop(CancellationToken cancellationToken)
    {
        return StopAsync(cancellationToken);
    }

    public Task StartAsync(AudioCaptureOptions options, IAudioFrameSink frameSink, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(frameSink);

        return Task.Run(() => StartCore(options, frameSink, cancellationToken), cancellationToken);
    }

    public Task PauseAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            if (State != AudioCaptureState.Capturing)
            {
                return Task.CompletedTask;
            }
        }

        SetState(AudioCaptureState.Paused, "System audio capture paused.");
        return Task.CompletedTask;
    }

    public Task ResumeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            if (State != AudioCaptureState.Paused)
            {
                return Task.CompletedTask;
            }
        }

        SetState(AudioCaptureState.Capturing, "System audio capture resumed.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        return Task.Run(() => StopCore(AudioCaptureState.Stopped, "System audio capture stopped.", null, AudioCaptureErrorCode.None), CancellationToken.None);
    }

    public void Dispose()
    {
        StopCore(AudioCaptureState.Stopped, "System audio capture disposed.", null, AudioCaptureErrorCode.None);
    }

    private void StartCore(AudioCaptureOptions options, IAudioFrameSink frameSink, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            if (State is AudioCaptureState.Starting or AudioCaptureState.Capturing or AudioCaptureState.Paused)
            {
                throw new AudioCaptureException(AudioCaptureErrorCode.InvalidState, "Audio capture is already running.");
            }
        }

        if (!options.UseWasapiLoopback)
        {
            var exception = new AudioCaptureException(
                AudioCaptureErrorCode.InvalidState,
                "AudioCaptureService only supports Windows system output through WASAPI Loopback. Microphone capture is not supported.");
            SetState(AudioCaptureState.Faulted, exception.Message, exception, exception.ErrorCode);
            throw exception;
        }

        SetState(AudioCaptureState.Starting, "Starting WASAPI Loopback capture.");

        MMDeviceEnumerator? enumerator = null;
        MMDevice? device = null;
        WasapiLoopbackCapture? capture = null;
        EndpointNotificationClient? notificationClient = null;
        CancellationTokenSource? linkedCancellation = null;
        CancellationTokenRegistration cancellationRegistration = default;

        try
        {
            enumerator = new MMDeviceEnumerator();
            device = ResolveOutputDevice(enumerator, options);
            capture = new WasapiLoopbackCapture(device);
            capture.DataAvailable += OnDataAvailable;
            capture.RecordingStopped += OnRecordingStopped;

            notificationClient = new EndpointNotificationClient(this);
            enumerator.RegisterEndpointNotificationCallback(notificationClient);

            linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cancellationRegistration = linkedCancellation.Token.Register(() =>
            {
                _ = StopAsync(CancellationToken.None);
            });

            lock (_syncRoot)
            {
                _capture = capture;
                _captureCancellation = linkedCancellation;
                _cancellationRegistration = cancellationRegistration;
                _device = device;
                _deviceEnumerator = enumerator;
                _deviceId = device.ID;
                _frameSink = frameSink;
                _notificationClient = notificationClient;
                _usesDefaultDevice = string.IsNullOrWhiteSpace(options.DeviceId);
            }

            capture.StartRecording();
            SetState(
                AudioCaptureState.Capturing,
                $"Capturing system output from: {device.FriendlyName}",
                deviceName: device.FriendlyName,
                deviceId: device.ID);
        }
        catch (Exception exception)
        {
            ClearCurrentResources(capture, enumerator, device, notificationClient, linkedCancellation, cancellationRegistration);
            Cleanup(capture, enumerator, device, notificationClient, linkedCancellation, cancellationRegistration, throwOnCleanupFailure: false);

            var mappedException = MapStartupException(exception);
            SetState(AudioCaptureState.Faulted, mappedException.Message, mappedException, mappedException.ErrorCode);
            throw mappedException;
        }
    }

    private MMDevice ResolveOutputDevice(MMDeviceEnumerator enumerator, AudioCaptureOptions options)
    {
        var activeOutputDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        if (activeOutputDevices.Count == 0)
        {
            throw new AudioCaptureException(AudioCaptureErrorCode.NoOutputDevice, "No active Windows output device is available.");
        }

        if (!string.IsNullOrWhiteSpace(options.DeviceId))
        {
            foreach (var activeOutputDevice in activeOutputDevices)
            {
                if (string.Equals(activeOutputDevice.ID, options.DeviceId, StringComparison.OrdinalIgnoreCase))
                {
                    return activeOutputDevice;
                }
            }

            throw new AudioCaptureException(
                AudioCaptureErrorCode.NoOutputDevice,
                $"The configured output device is not active or was not found: {options.DeviceId}");
        }

        if (!enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
        {
            throw new AudioCaptureException(AudioCaptureErrorCode.NoOutputDevice, "No default Windows output device is available.");
        }

        return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs args)
    {
        if (args.BytesRecorded <= 0)
        {
            return;
        }

        IAudioFrameSink? frameSink;
        CancellationToken cancellationToken;
        WaveFormat waveFormat;
        bool shouldDeliver;

        lock (_syncRoot)
        {
            shouldDeliver = State == AudioCaptureState.Capturing && _capture is not null && _frameSink is not null;
            frameSink = _frameSink;
            cancellationToken = _captureCancellation?.Token ?? CancellationToken.None;
            waveFormat = _capture?.WaveFormat ?? WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        }

        if (!shouldDeliver || frameSink is null || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        var pcm = new byte[args.BytesRecorded];
        Buffer.BlockCopy(args.Buffer, 0, pcm, 0, args.BytesRecorded);

        var averageBytesPerSecond = Math.Max(1, waveFormat.AverageBytesPerSecond);
        var frame = new AudioFrame(
            pcm,
            DateTimeOffset.UtcNow,
            TimeSpan.FromSeconds((double)args.BytesRecorded / averageBytesPerSecond),
            waveFormat.SampleRate,
            waveFormat.Channels,
            waveFormat.BitsPerSample,
            MapSampleFormat(waveFormat));

        _ = Task.Run(async () =>
        {
            try
            {
                await frameSink.OnAudioFrameAsync(frame, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                FaultAndStop(new AudioCaptureException(
                    AudioCaptureErrorCode.CaptureRuntimeFailed,
                    "Captured audio frame could not be written to the PCM stream.",
                    exception));
            }
        }, CancellationToken.None);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs args)
    {
        if (args.Exception is not null)
        {
            FaultAndStop(new AudioCaptureException(
                AudioCaptureErrorCode.CaptureRuntimeFailed,
                "WASAPI Loopback capture stopped unexpectedly.",
                args.Exception));
            return;
        }

        lock (_syncRoot)
        {
            if (State is AudioCaptureState.Stopping or AudioCaptureState.Stopped or AudioCaptureState.Faulted)
            {
                return;
            }
        }

        StopCore(AudioCaptureState.Stopped, "WASAPI Loopback capture stopped.", null, AudioCaptureErrorCode.None);
    }

    private void HandleDefaultDeviceChanged(DataFlow dataFlow, Role role, string? defaultDeviceId)
    {
        if (dataFlow != DataFlow.Render || role != Role.Multimedia)
        {
            return;
        }

        bool shouldFault;
        lock (_syncRoot)
        {
            shouldFault = _usesDefaultDevice
                && State is AudioCaptureState.Capturing or AudioCaptureState.Paused
                && !string.Equals(_deviceId, defaultDeviceId, StringComparison.OrdinalIgnoreCase);
        }

        if (shouldFault)
        {
            FaultAndStop(new AudioCaptureException(
                AudioCaptureErrorCode.DeviceSwitchDetected,
                "The default Windows output device changed. Stop and start capture again to use the new device."));
        }
    }

    private void HandleDeviceRemovedOrDisabled(string deviceId)
    {
        bool shouldFault;
        lock (_syncRoot)
        {
            shouldFault = State is AudioCaptureState.Capturing or AudioCaptureState.Paused
                && string.Equals(_deviceId, deviceId, StringComparison.OrdinalIgnoreCase);
        }

        if (shouldFault)
        {
            FaultAndStop(new AudioCaptureException(
                AudioCaptureErrorCode.DeviceDisconnected,
                "The active Windows output device was disconnected, disabled, or removed."));
        }
    }

    private void FaultAndStop(AudioCaptureException exception)
    {
        _ = Task.Run(() => StopCore(AudioCaptureState.Faulted, exception.Message, exception, exception.ErrorCode), CancellationToken.None);
    }

    private void StopCore(AudioCaptureState finalState, string message, Exception? exception, AudioCaptureErrorCode errorCode)
    {
        WasapiLoopbackCapture? capture;
        MMDeviceEnumerator? enumerator;
        MMDevice? device;
        EndpointNotificationClient? notificationClient;
        CancellationTokenSource? cancellation;
        CancellationTokenRegistration cancellationRegistration;

        lock (_syncRoot)
        {
            if (State == AudioCaptureState.Stopped && finalState == AudioCaptureState.Stopped)
            {
                return;
            }

            capture = _capture;
            enumerator = _deviceEnumerator;
            device = _device;
            notificationClient = _notificationClient;
            cancellation = _captureCancellation;
            cancellationRegistration = _cancellationRegistration;

            _capture = null;
            _deviceEnumerator = null;
            _device = null;
            _notificationClient = null;
            _captureCancellation = null;
            _cancellationRegistration = default;
            _frameSink = null;
            _deviceId = null;
            _usesDefaultDevice = false;
        }

        SetState(AudioCaptureState.Stopping, "Stopping system audio capture.");

        if (capture is not null)
        {
            capture.DataAvailable -= OnDataAvailable;
            capture.RecordingStopped -= OnRecordingStopped;
        }

        try
        {
            cancellationRegistration.Dispose();
            cancellation?.Cancel();
            capture?.StopRecording();
            Cleanup(capture, enumerator, device, notificationClient, cancellation, cancellationRegistration, throwOnCleanupFailure: true);
        }
        catch (Exception stopException) when (exception is null)
        {
            exception = stopException;
            finalState = AudioCaptureState.Faulted;
            errorCode = AudioCaptureErrorCode.CaptureRuntimeFailed;
            message = "Failed to stop WASAPI Loopback capture cleanly.";
        }

        SetState(finalState, message, exception, errorCode);
    }

    private static void Cleanup(
        WasapiLoopbackCapture? capture,
        MMDeviceEnumerator? enumerator,
        MMDevice? device,
        EndpointNotificationClient? notificationClient,
        CancellationTokenSource? cancellation,
        CancellationTokenRegistration cancellationRegistration,
        bool throwOnCleanupFailure)
    {
        Exception? cleanupException = null;

        try
        {
            cancellationRegistration.Dispose();
            cancellation?.Dispose();
            capture?.Dispose();
            device?.Dispose();

            if (enumerator is not null && notificationClient is not null)
            {
                enumerator.UnregisterEndpointNotificationCallback(notificationClient);
            }

            enumerator?.Dispose();
        }
        catch (Exception exception)
        {
            cleanupException = exception;
        }

        if (throwOnCleanupFailure && cleanupException is not null)
        {
            throw new AudioCaptureException(
                AudioCaptureErrorCode.CaptureRuntimeFailed,
                "Failed to release WASAPI Loopback capture resources.",
                cleanupException);
        }
    }

    private void ClearCurrentResources(
        WasapiLoopbackCapture? capture,
        MMDeviceEnumerator? enumerator,
        MMDevice? device,
        EndpointNotificationClient? notificationClient,
        CancellationTokenSource? cancellation,
        CancellationTokenRegistration cancellationRegistration)
    {
        lock (_syncRoot)
        {
            if (ReferenceEquals(_capture, capture))
            {
                _capture = null;
            }

            if (ReferenceEquals(_deviceEnumerator, enumerator))
            {
                _deviceEnumerator = null;
            }

            if (ReferenceEquals(_device, device))
            {
                _device = null;
            }

            if (ReferenceEquals(_notificationClient, notificationClient))
            {
                _notificationClient = null;
            }

            if (ReferenceEquals(_captureCancellation, cancellation))
            {
                _captureCancellation = null;
            }

            if (_cancellationRegistration.Equals(cancellationRegistration))
            {
                _cancellationRegistration = default;
            }

            _frameSink = null;
            _deviceId = null;
            _usesDefaultDevice = false;
        }
    }

    private void SetState(
        AudioCaptureState state,
        string? message = null,
        Exception? exception = null,
        AudioCaptureErrorCode errorCode = AudioCaptureErrorCode.None,
        string? deviceName = null,
        string? deviceId = null)
    {
        lock (_syncRoot)
        {
            State = state;
        }

        StateChanged?.Invoke(
            this,
            new AudioCaptureStateChangedEventArgs(
                state,
                message,
                exception,
                errorCode,
                deviceName,
                deviceId));
    }

    private static AudioCaptureException MapStartupException(Exception exception)
    {
        if (exception is AudioCaptureException audioCaptureException)
        {
            return audioCaptureException;
        }

        if (exception is UnauthorizedAccessException)
        {
            return new AudioCaptureException(
                AudioCaptureErrorCode.PermissionDenied,
                "Windows denied access to the output audio endpoint.",
                exception);
        }

        if (exception is COMException)
        {
            return new AudioCaptureException(
                AudioCaptureErrorCode.CaptureInitializationFailed,
                "WASAPI Loopback capture could not be initialized. Check the output device and Windows Audio service.",
                exception);
        }

        return new AudioCaptureException(
            AudioCaptureErrorCode.CaptureInitializationFailed,
            "WASAPI Loopback capture could not be initialized.",
            exception);
    }

    private static AudioSampleFormat MapSampleFormat(WaveFormat waveFormat)
    {
        if (waveFormat.Encoding == WaveFormatEncoding.IeeeFloat && waveFormat.BitsPerSample == 32)
        {
            return AudioSampleFormat.IeeeFloat32;
        }

        if (waveFormat.Encoding != WaveFormatEncoding.Pcm)
        {
            return AudioSampleFormat.Unknown;
        }

        return waveFormat.BitsPerSample switch
        {
            16 => AudioSampleFormat.Pcm16,
            24 => AudioSampleFormat.Pcm24,
            32 => AudioSampleFormat.Pcm32,
            _ => AudioSampleFormat.Unknown
        };
    }

    private sealed class EndpointNotificationClient : IMMNotificationClient
    {
        private readonly AudioCaptureService _owner;

        public EndpointNotificationClient(AudioCaptureService owner)
        {
            _owner = owner;
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
            if (newState != DeviceState.Active)
            {
                _owner.HandleDeviceRemovedOrDisabled(deviceId);
            }
        }

        public void OnDeviceAdded(string pwstrDeviceId)
        {
        }

        public void OnDeviceRemoved(string deviceId)
        {
            _owner.HandleDeviceRemovedOrDisabled(deviceId);
        }

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            _owner.HandleDefaultDeviceChanged(flow, role, defaultDeviceId);
        }

        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
        {
        }
    }
}
