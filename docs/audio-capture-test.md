# AudioCapture 测试说明

## 自动测试

```powershell
dotnet restore
dotnet build
dotnet test
```

当前自动测试覆盖：

- `AudioBuffer` 能持续写入并读取 PCM 音频帧。
- `AudioCaptureService` 拒绝麦克风捕获模式，只允许 WASAPI Loopback。
- 音频帧元数据和模块接口契约。

## 手动测试 WASAPI Loopback

真实系统声音捕获依赖 Windows 输出设备，不能只靠单元测试证明。

1. 确认 Windows 有可用扬声器、耳机或 HDMI/DisplayAudio 输出设备。
2. 播放系统声音，例如浏览器视频或本地播放器。
3. 运行探针程序：

```powershell
dotnet run --project tools\AudioCaptureProbe\AudioCaptureProbe.csproj -- 30
```

4. 预期：每秒输出采样率、声道数、每秒字节数、峰值和每秒帧数。
5. 播放视频时 `bytes/sec` 应大于 0，`peak` 应随声音变化。
6. 视频静音或暂停时，`peak` 应下降到接近 0。

示例输出：

```text
AudioCaptureProbe
Play a YouTube video or any other system audio now.
This probe uses WASAPI Loopback render output, not microphone input.

[15:30:12] state=Starting code=None message=Starting WASAPI Loopback capture.
[15:30:12] state=Capturing code=None message=Capturing system output from: Speakers (Realtek Audio)
t=  1s | sampleRate= 48000 Hz | channels= 2 | bytes/sec=  384000 | peak=0.184 | frames/sec=  96
t=  2s | sampleRate= 48000 Hz | channels= 2 | bytes/sec=  384000 | peak=0.237 | frames/sec=  96
```

## 需要人工验证的异常场景

- 没有默认输出设备：应抛出 `AudioCaptureException`，错误码 `NoOutputDevice`。
- 切换默认输出设备：应进入 `Faulted`，错误码 `DeviceSwitchDetected`。
- 当前输出设备断开、禁用或移除：应进入 `Faulted`，错误码 `DeviceDisconnected`。
- 权限或系统音频服务异常：应映射为 `PermissionDenied` 或 `CaptureInitializationFailed`。
