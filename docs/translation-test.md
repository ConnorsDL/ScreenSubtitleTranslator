# Translation 测试说明

当前默认翻译实现是 `OpenAITranslationService`，使用 `OPENAI_API_KEY` 调用 OpenAI Responses API，把英文 final 字幕翻译为中文。WPF 应用会把中文 final 通过 Overlay 显示在屏幕底部。

官方参考：

- https://platform.openai.com/docs/api-reference/responses/create

## 单独模块行为

`ITranslationService` 保留为模块接口，当前实现文件：

- `src/ScreenSubtitleTranslator/Translation/OpenAITranslationService.cs`
- `src/ScreenSubtitleTranslator/Translation/EnvironmentTranslationCredentialProvider.cs`
- `src/ScreenSubtitleTranslator/Translation/TranslationException.cs`
- `src/ScreenSubtitleTranslator/Translation/TranslationErrorCode.cs`

默认参数：

- Provider：`OpenAI`
- 模型：`gpt-4.1-mini`
- 源语言：`en`
- 目标语言：`zh-CN`
- 请求超时：10 秒

## 真实测试

播放英文 YouTube 视频后运行：

```powershell
$env:OPENAI_API_KEY="你的 OpenAI API Key"
dotnet run --project tools\SpeechRecognitionProbe\SpeechRecognitionProbe.csproj -- 60 en-US 3 zh-CN
```

控制台应同时出现：

```text
[en final] ...
[zh final] ...
```

partial 只显示：

```text
[en partial] ...
```

## WPF Overlay 测试

```powershell
dotnet run --project src\ScreenSubtitleTranslator\ScreenSubtitleTranslator.csproj
```

1. 播放英文 YouTube 视频。
2. 点击 `Start`。
3. 屏幕底部应出现中文 final 字幕。
4. 点击 `Stop` 后字幕停止更新并隐藏。

## 失败定位

- `Translation failed. code=ApiKeyMissing`：未设置 `OPENAI_API_KEY`，或 API Key 无效。
- `Translation failed. code=NetworkFailure`：网络或 OpenAI 服务端连接失败。
- `Translation failed. code=EmptyResponse`：OpenAI 返回成功但没有可用文本。
- `Translation failed. code=Timeout`：超过默认 10 秒请求超时。
- `Translation failed. code=EmptySourceText`：final 原文为空，服务不会发送翻译请求。
