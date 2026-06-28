# 项目架构

本项目按模块拆分，避免把捕获、识别、翻译、悬浮显示和设置逻辑写进同一个窗口类。

## 模块

- `AudioCapture`：系统声音捕获接口、音频帧模型、缓冲接口和 WASAPI Loopback 实现。
- `SpeechRecognition`：语音识别接口、OpenAI 默认实现、Azure 可替换实现、PCM 转换和 WAV 分片工具。
- `Translation`：翻译服务接口、请求/结果模型、OpenAI 默认实现、凭据读取和错误类型。
- `Overlay`：WPF 悬浮字幕窗口、字幕显示控制接口和 Dispatcher 安全更新控制器。
- `Settings`：用户设置模型和设置存储接口。
- `Logging`：日志接口。

## SpeechRecognition 当前设计

- 公共接口仍是 `ISpeechRecognitionService`。
- 默认 Provider 是 `OpenAI`。
- 默认模型是 `gpt-4o-mini-transcribe`。
- API Key 从 `OPENAI_API_KEY` 读取。
- 输入音频来自 `AudioCapture` 的 PCM 流，不使用麦克风。
- `OpenAISpeechRecognitionService` 将 PCM 转成 16 kHz、mono、PCM16，再按默认 3 秒短分片封装为 WAV 上传。
- `AzureSpeechRecognitionService` 保留，但需要显式使用 Azure 相关环境变量。

## Translation 当前设计

- 公共接口仍是 `ITranslationService`。
- 默认 Provider 是 `OpenAI`。
- 默认模型是 `gpt-4.1-mini`。
- API Key 从 `OPENAI_API_KEY` 读取。
- 默认源语言是 `en`，默认目标语言是 `zh-CN`。
- 只翻译 SpeechRecognition 的 final result；partial result 暂不翻译，避免重复请求和额外成本。
- `OpenAITranslationService` 使用 OpenAI Responses API，异步执行请求并处理缺 Key、网络失败、空返回、超时和空原文。

## Overlay 当前设计

- `OverlayWindow` 是 WPF 无边框、透明背景、始终置顶窗口。
- 默认位置是主屏幕底部居中。
- `WpfSubtitleOverlayController` 是 UI 层唯一更新入口，后台 pipeline 不直接修改 UI 控件。
- Overlay 更新通过 WPF Dispatcher 调度。
- 窗口设置为不激活和点击穿透，减少对其它窗口操作的影响。
- 当前显示 final 字幕：英文原文可选，中文译文默认显示。

## Pipeline 当前设计

- `SubtitlePipelineService` 串联 `AudioCapture -> SpeechRecognition -> Translation -> Overlay`。
- Start 创建独立后台任务运行识别和翻译。
- Stop 取消后台任务、停止音频捕获、清空并隐藏 Overlay。
- Translation 只处理 final result；partial 不翻译。
- MainWindow 只负责组合服务和绑定 ViewModel，不承载核心业务逻辑。

## 后续实现顺序

1. 真实验证 WPF Overlay 的端到端延迟、UI 卡顿和字幕稳定性。
2. 根据真实效果调整音频分片时长或改用更低延迟的实时接口。
3. 将更完整的运行日志接入主窗口。
4. 实现 `ISettingsStore` 和 `IAppLogger`。
