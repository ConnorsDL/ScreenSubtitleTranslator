# AGENTS.md

## 项目目标

开发一个 Windows 实时屏幕字幕翻译软件。软件捕获电脑系统播放声音，将语音识别为文本，翻译成用户选择的语言，并以悬浮字幕形式显示在屏幕上。

## 当前状态

当前版本为 `v0.1.0` 发布候选，核心链路已经实现并完成真实验证：

`AudioCapture -> SpeechRecognition -> Translation -> Overlay`

当前工作以稳定性、发布检查和文档维护为主。除非任务明确要求，不新增功能，不引入 Realtime API、本地 Whisper 或安装器，也不随意修改已经验证的核心链路。

默认设置必须保持：

- Source Language：`en-US`
- Target Language：`zh-CN`
- Subtitle Mode：English + Chinese
- Latency Mode：Balanced 3 seconds

## 技术栈

- C# / .NET 8
- WPF
- WASAPI Loopback Capture
- OpenAI Speech-to-Text
- OpenAI Translation

## 核心模块

- `AudioCapture`：使用 WASAPI Loopback 捕获系统输出，不使用麦克风。
- `SpeechRecognition`：负责语音识别，通过接口支持替换 Provider。
- `Translation`：负责翻译 final 字幕，通过接口支持替换 Provider。
- `Pipeline`：连接捕获、识别、翻译和 Overlay，并管理异步生命周期。
- `Overlay`：负责置顶、点击穿透的屏幕字幕显示。
- `Settings`：负责非敏感用户配置。
- `Logging`：负责 UI 事件和显式启用的诊断日志。

## 架构与开发原则

- 不把所有逻辑写在一个文件或 `MainWindow` 中。
- 不使用假数据冒充真实捕获、识别或翻译结果。
- 不吞掉异常，不跳过错误处理。
- 不通过删除、跳过或弱化测试来让项目通过。
- 不在生产代码中留下无说明的 TODO、placeholder 或 mock。
- 音频捕获、识别、翻译和队列任务必须异步执行，不得阻塞 UI 线程。
- 后台线程不得直接修改 WPF 控件；UI 更新必须经过 Dispatcher。
- 保持 Provider 接口可替换，不把云服务调用写进 UI 层。
- 修改范围应尽量小，不做与当前任务无关的重构。

## 实时性能与稳定性

- 音频采集必须连续稳定，并持续输出带格式信息的 PCM 数据。
- UI 不应出现明显卡顿或跨线程异常。
- 字幕更新延迟应尽可能低，并通过真实测试记录，不凭空声称。
- 默认 3 秒分片以稳定性优先；2 秒仅作为可选低延迟模式。
- 英文 final、翻译和 Overlay 必须保持 sequence id 可追踪。
- Stop 时翻译队列必须 drain 或明确记录取消，最终 pending 应为 0。
- 程序关闭后不得留下后台进程。

## 自动化测试要求

测试范围应覆盖：

1. 模块接口和默认设置。
2. AudioCapture PCM 格式与异常状态。
3. SpeechRecognition 参数、缺失 Key、格式错误和空结果。
4. Translation 参数、超时、网络失败和空结果。
5. Settings 保存与恢复，并确认不保存 API Key。
6. Overlay 状态与 Dispatcher 边界。
7. 翻译队列去重、失败、取消和 drain 完整性。

修改完成后至少运行：

```powershell
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-restore --no-build
```

不得为了通过测试而删除现有测试。

## 手动测试清单

涉及运行时行为的修改，应按影响范围验证：

- 播放英文 YouTube 视频时能否捕获、识别、翻译并显示字幕。
- 确认捕获的是系统播放声音而不是麦克风。
- Start 前切换默认播放设备后能否捕获新设备。
- 运行中切换耳机或音箱时是否明确提示 Stop / Start。
- API Key 缺失、网络失败和 OpenAI API 错误是否有明确提示。
- Overlay 是否置顶、底部居中、自动换行且点击穿透。
- Stop 后字幕是否清空，是否可以再次 Start。
- 长时间运行时是否有卡死、持续内存爬升或队列遗漏。
- 应用关闭后是否没有残留进程。

## 凭据与发布安全

- API Key 只允许从 `OPENAI_API_KEY` 环境变量读取。
- 不在源码、`.env`、`settings.json`、日志、截图、测试数据或发布包中保存真实 Key。
- 诊断日志默认关闭；启用后可能包含识别原文和译文，分享前必须检查。
- Git 和 Release 不得包含 `artifacts/`、`bin/`、`obj/`、测试截图、诊断日志、结果 JSON、用户设置或 zip 中间产物。
- 发布前必须扫描候选提交和发布包，确认没有真实 Key 或疑似 `sk-...` 凭据。

## 完成标准

- 实现或文档与任务要求一致，没有用假结果替代真实验证。
- 所有相关自动化测试通过。
- 需要真实验证的功能提供可运行方法和实际结果。
- README、Release Notes 或测试文档与当前行为一致。
- 报告修改文件、测试结果、已知问题；涉及延迟时提供实测或明确说明未测试。
