# SpeechRecognition 测试说明

当前默认语音识别实现是 `OpenAISpeechRecognitionService`。它读取 `AudioCapture` 输出的 PCM 音频流，将音频按短分片封装成 WAV，并调用 OpenAI Audio Transcriptions API 输出原文字幕。

`SpeechRecognitionProbe` 现在还会把 final 英文原文交给 `OpenAITranslationService` 翻译为中文；partial 只显示原文，不翻译。

Azure Speech 实现仍保留在 `AzureSpeechRecognitionService` 中，但不再作为默认实现。

官方参考：

- https://platform.openai.com/docs/guides/speech-to-text
- https://platform.openai.com/docs/api-reference/audio/createTranscription

## 准备 OpenAI API Key

设置当前 PowerShell 会话环境变量：

```powershell
$env:OPENAI_API_KEY="你的 OpenAI API Key"
```

检查环境变量：

```powershell
if ($env:OPENAI_API_KEY) { "OPENAI_API_KEY is set" } else { "OPENAI_API_KEY is missing" }
```

如果没有设置，程序会输出 `ApiKeyMissing`。

## 播放 YouTube 英文视频

1. 打开 YouTube 英文视频。
2. 确认浏览器标签页没有静音。
3. 确认 Windows 默认输出设备正在播放声音。
4. 不需要启用麦克风；本程序只使用 AudioCapture 的 WASAPI Loopback 系统输出。

## 运行识别 + 翻译探针

```powershell
dotnet run --project tools\SpeechRecognitionProbe\SpeechRecognitionProbe.csproj -- 60 en-US 3 zh-CN
```

参数说明：

- `60`：运行 60 秒。
- `en-US`：语音识别源语言。
- `3`：音频分片秒数，默认 `3`。
- `zh-CN`：翻译目标语言，默认 `zh-CN`。

## 预期输出

```text
SpeechRecognitionProbe
Play an English YouTube video now.
Required environment variable: OPENAI_API_KEY.
This probe captures system output through AudioCapture; it does not use microphone input.
Provider=OpenAI, model=gpt-4o-mini-transcribe, chunkSeconds=3, language=en-US
Translation=OpenAI, model=gpt-4.1-mini, sourceLanguage=en, targetLanguage=zh-CN

[audio] state=Capturing code=None message=Capturing system output from: ...
[en partial] this is an example
[en final] this is an example sentence from the video.
[zh final] 这是视频中的一个示例句子。
```

## 需要记录的真实测试指标

60 秒测试时记录：

- 是否出现 `[en partial]`
- 是否出现 `[en final]`
- 是否出现 `[zh final]`
- 首个英文字幕出现延迟
- 首个中文翻译出现延迟
- 是否有重复字幕或重复翻译
- 是否有明显乱码、识别错误或翻译错误
- 是否有取消、网络、API 报错
- 60 秒内大概识别和翻译了多少句
- 当前识别 + 翻译端到端延迟估计

## 异常场景

- Speech API Key 缺失：`SpeechRecognitionException(ApiKeyMissing)`。
- Translation API Key 缺失：`TranslationException(ApiKeyMissing)`。
- OpenAI 返回 401/403：映射为 `ApiKeyMissing`。
- 网络失败或服务端超时：映射为 `NetworkFailure`。
- 翻译超时：映射为 `TranslationException(Timeout)`。
- 空识别结果：不会输出空字幕。
- 空原文翻译请求：`TranslationException(EmptySourceText)`。
- OpenAI 翻译返回空内容：`TranslationException(EmptyResponse)`。
- 音频格式不匹配：`SpeechRecognitionException(AudioFormatMismatch)`。
- 没有系统声音输入：AudioCapture 仍可能运行，但 OpenAI 可能返回空结果；先用 `AudioCaptureProbe` 确认每秒数据量和峰值。
