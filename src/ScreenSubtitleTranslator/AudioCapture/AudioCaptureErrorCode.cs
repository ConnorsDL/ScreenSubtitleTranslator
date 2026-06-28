namespace ScreenSubtitleTranslator.AudioCapture;

public enum AudioCaptureErrorCode
{
    None,
    NoOutputDevice,
    DeviceSwitchDetected,
    DeviceDisconnected,
    PermissionDenied,
    NoAudioInput,
    CaptureInitializationFailed,
    CaptureRuntimeFailed,
    InvalidState
}
