# Screen Subtitle Translator

Windows 实时屏幕字幕翻译工具。当前发布候选版本为 **v0.1.0**。

应用通过 WASAPI Loopback 捕获 Windows 正在播放的系统声音，不使用麦克风；随后调用 OpenAI 进行语音识别和翻译，并在屏幕底部显示置顶、点击穿透的双语字幕。

## 软件功能

- 捕获 Windows 默认播放设备的系统输出声音。
- 使用 OpenAI Speech-to-Text 识别原文字幕。
- 使用 OpenAI 翻译确认后的 final 字幕。
- 显示中文或英文 + 中文悬浮字幕。
- 支持多种源语言和目标语言。
- 支持 2 / 3 / 4 / 5 秒延迟模式，默认使用 Balanced 3 秒。
- 显示当前捕获设备、运行阶段、错误和最近事件。
- 保存语言、字幕模式和延迟设置；API Key 可安全保存到 Windows Credential Manager，不写入 `settings.json`。

默认设置：

| 设置 | 默认值 |
| --- | --- |
| Source Language | English (US), `en-US` |
| Target Language | Chinese Simplified, `zh-CN` |
| Subtitle Mode | English + Chinese |
| Latency Mode | Balanced, 3 seconds |

## 系统要求

- Windows 10 或 Windows 11。
- 可用的 Windows 播放设备，例如音箱、耳机或显示器音频。
- 稳定的互联网连接，并允许访问 OpenAI API。
- 使用 self-contained 安装器或 zip：无需预先安装 .NET Runtime。
- 从源码构建：需要 .NET 8 SDK。
- 有效的 OpenAI API Key 及可用 API 额度。

## 配置 OpenAI API Key

普通用户不需要 PowerShell：

1. 首次启动且没有可用 Key 时，应用会自动打开 `Configure OpenAI API Key` 窗口。
2. 在密码输入框中输入 Key。界面不会显示完整内容。
3. 可点击 `Test API Key` 做一次轻量连接检查；该检查不会启动音频捕获、语音识别或翻译。
4. 点击 `Save`。Key 会保存到当前 Windows 用户的 Windows Credential Manager。
5. 之后可通过主窗口顶部的 `Change API Key` 再次打开配置窗口。
6. 点击 `Clear Key` 可删除本地保存的 Key。

Key 读取优先级：

1. 环境变量 `OPENAI_API_KEY`。
2. Windows Credential Manager 中保存的 Key。
3. 两者都不存在时提示配置。

`settings.json`、诊断日志和 UI 日志都不会保存或显示完整 Key。如果已经使用环境变量，应用会继续优先使用它；`Clear Key` 只清除 Windows Credential Manager 中的副本，不会修改环境变量。

高级用户仍可仅为当前 PowerShell 会话设置：

```powershell
$env:OPENAI_API_KEY="<your-api-key>"
```

或为当前 Windows 用户持久设置：

```powershell
setx OPENAI_API_KEY "<your-api-key>"
```

使用 `setx` 后需要关闭并重新打开终端或应用。不要把真实 Key 写入源码、脚本、截图、Issue 或提交记录。可在不显示 Key 内容的情况下检查状态：

```powershell
if ([string]::IsNullOrWhiteSpace($env:OPENAI_API_KEY)) {
    "OPENAI_API_KEY is missing"
} else {
    "OPENAI_API_KEY is configured"
}
```

参考：[OpenAI API Key 安全建议](https://help.openai.com/en/articles/5112595-best-practices-for-api-key-safety)。

## 安装并启动

普通用户推荐使用 `ScreenSubtitleTranslatorSetup-v0.1.0.exe`：

1. 双击安装包并选择安装目录。
2. 安装器默认创建开始菜单快捷方式；桌面快捷方式可选。
3. 从开始菜单或桌面快捷方式启动软件。
4. 首次启动时在软件内配置并测试 OpenAI API Key。

安装器尚未代码签名，Windows SmartScreen 可能显示“未知发布者”。请只从可信发布页面下载安装包。

zip 版本解压后可运行 `start.bat` 或 `ScreenSubtitleTranslator.exe`；安装器用户不需要 `start.bat`。

## 使用 Start / Stop

1. 播放包含语音的 YouTube 视频或其它系统音频。
2. 确认顶部 API Key 状态为已配置。
3. 选择源语言、目标语言、字幕模式和延迟模式。
4. 点击 `Start`。运行期间关键设置会被锁定。
5. 等待底部 Overlay 显示 final 原文和译文。
6. 点击 `Stop`。应用会停止捕获，等待翻译队列结束，并清空、隐藏字幕。

Start 只捕获 Windows 默认播放设备的输出，不捕获麦克风。应用退出时会停止后台任务。

## 选择语言

源语言选项：

- English (US) `en-US`
- English (UK) `en-GB`
- German `de-DE`
- Chinese `zh-CN`
- Japanese `ja-JP`
- Korean `ko-KR`
- French `fr-FR`
- Spanish `es-ES`
- Italian `it-IT`

目标语言选项：

- Chinese Simplified `zh-CN`
- Chinese Traditional `zh-TW`
- English `en`
- German `de`
- Japanese `ja`
- Korean `ko`
- French `fr`
- Spanish `es`
- Italian `it`

OpenAI Speech-to-Text 的语言参数使用 ISO-639-1，因此 `en-US` 和 `en-GB` 当前都会映射为 `en`。语言选项会保存到本地设置，下次启动恢复。

## 播放设备切换

- 在点击 Start 之前切换 Windows 默认播放设备：应用会在下次 Start 时捕获新设备。
- 运行中切换音箱、耳机或其它默认播放设备：当前捕获会进入错误状态，界面显示“播放设备已切换，请 Stop 后重新 Start”。
- 点击 `Stop`，确认设备连接正常后再次点击 `Start`。

v0.1.0 不会在运行中自动迁移到新的播放设备。

## 本地设置与诊断日志

基础设置保存在：

```text
%APPDATA%\ScreenSubtitleTranslator\settings.json
```

该文件只包含：

- `sourceLanguage`
- `targetLanguage`
- `subtitleDisplayMode`
- `audioChunkSeconds`

诊断日志默认关闭。只有显式设置 `SCREEN_SUBTITLE_DIAGNOSTIC_LOG` 时才会落盘，例如：

```powershell
$env:SCREEN_SUBTITLE_DIAGNOSTIC_LOG="$PWD\subtitle-diagnostic.log"
```

诊断日志可能包含识别原文和译文，分享前应检查并删除敏感内容。

## 常见错误

| 提示或现象 | 处理方法 |
| --- | --- |
| API Key 未配置 | 点击 `Configure API Key`，输入 Key 后保存；Start 会被阻止直到配置完成。 |
| API Key 测试失败 | 检查 Key、网络、项目权限和 OpenAI 服务状态；测试不会运行字幕链路。 |
| 网络失败 | 检查网络、代理、防火墙以及 OpenAI API 可访问性。 |
| OpenAI API 错误 | 检查 Key、API 额度、模型访问权限和 OpenAI 服务状态。 |
| 没有捕获到系统声音 | 确认视频正在播放、Windows 音量正常且正确设备为默认输出。 |
| 播放设备已切换 | 点击 Stop，将设备设为默认输出，再点击 Start。 |
| 播放设备已断开 | 重新连接设备后执行 Stop / Start。 |
| `Translation failed` 或 timeout | 检查网络和 API 状态；等待片刻后重新 Start。 |
| 有声音但长时间没有字幕 | 检查内容是否有人声、源语言是否正确，并查看 Runtime Status。 |

## API 费用提醒

OpenAI API 与 ChatGPT 订阅分开计费。语音分片会产生 Speech-to-Text 请求，英文 final 会产生翻译请求；运行时间、语音量、分片长度和模型都会影响费用。2 秒 Low Latency 模式通常会比 3 秒 Balanced 模式产生更高的请求频率。

发布前请检查账户额度与使用量限制。价格可能调整，请以 [OpenAI API Pricing](https://openai.com/api/pricing/) 为准，本项目不内置固定价格表。

## 构建与测试

开发者从源码运行：

```powershell
dotnet restore
dotnet run --project src\ScreenSubtitleTranslator\ScreenSubtitleTranslator.csproj
```

```powershell
dotnet build --configuration Release
dotnet test --no-restore
```

完整 Release 输出说明见 [docs/release-build.md](docs/release-build.md)，发布检查见 [docs/release-checklist.md](docs/release-checklist.md)。

## 项目结构

```text
src/ScreenSubtitleTranslator/
  AudioCapture/
  SpeechRecognition/
  Translation/
  Pipeline/
  Overlay/
  Settings/
  Logging/
  ViewModels/
tests/ScreenSubtitleTranslator.Tests/
tools/AudioCaptureProbe/
tools/SpeechRecognitionProbe/
docs/
```

## 当前范围

v0.1.0 是 Windows 发布候选版本，提供 self-contained zip 和未签名安装器，不包含 OpenAI Realtime API 或本地 Whisper。

## 安全

不要提交真实 API Key、`.env`、用户 `settings.json`、诊断日志或包含私人字幕的截图。安全问题请按 [SECURITY.md](SECURITY.md) 私下报告。

## 许可证

本项目使用 [MIT License](LICENSE)。
