# Release Notes

## v0.1.0 - Release Candidate

发布日期候选：2026-06-27

### 功能列表

- 使用 WASAPI Loopback 捕获 Windows 默认播放设备，不使用麦克风。
- 使用 OpenAI Speech-to-Text 识别系统音频原文。
- 使用 OpenAI 翻译 final 字幕，默认从英文翻译为简体中文。
- WPF 半透明双语 Overlay，始终置顶、点击穿透、底部居中并自动换行。
- 现代化主窗口，显示 API Key 状态、捕获设备、语言设置、字幕设置和运行状态。
- 支持只显示中文或英文 + 中文。
- 支持 Low Latency 2 秒、Balanced 3 秒、Stable 4 秒和 Extra Stable 5 秒模式。
- 支持 9 种源语言选项和 9 种目标语言选项。
- 设置保存到用户 AppData，不保存 API Key。
- 英文 final、翻译和 Overlay 使用 sequence id 跟踪；Stop 最多等待 10 秒完成翻译队列。
- 检测运行中的默认播放设备切换，并提示 Stop 后重新 Start。

### 已验证内容

- Release 构建成功，0 个编译错误、0 个警告。
- 自动化测试 27/27 通过。
- WASAPI Loopback 已验证捕获 48 kHz、双声道系统输出。
- 已验证 Start 前切换默认设备可捕获新设备。
- 已验证运行中切换 BenQ 与 USB 播放设备会触发明确提示，Stop / Start 后恢复。
- OpenAI 识别与翻译已使用真实英文 YouTube 音频验证。
- 3 分钟 UI 视觉测试：72 条英文 final、71 条翻译完成、1 条重复跳过、pending/failed/canceled 均为 0。
- 3 分钟内 Overlay 更新 71 次，没有 Translation timeout、OpenAI API 或跨线程错误。
- 30 分钟稳定性测试完成：翻译队列 pending/failed/canceled 均为 0，没有持续内存爬升或残留进程。
- Stop 后字幕清空，应用关闭后没有后台进程残留。
- 当前实际 `settings.json` 已检查，不包含 API Key 字段。
- 诊断日志默认关闭。

### 已知限制

- 仅支持 Windows 10/11。
- 必须连接互联网并使用用户自己的 OpenAI API Key；API 请求会产生费用。
- 当前为分片架构，不是逐音频帧的实时流式 API；默认端到端延迟通常为数秒。
- 运行中切换默认播放设备不会自动恢复，必须 Stop 后重新 Start。
- `en-US` 与 `en-GB` 会按 OpenAI Speech-to-Text 参数要求映射为 `en`。
- Overlay 当前使用主屏幕底部，不保存用户自定义位置。
- 当前只有浅色主界面主题。
- 不包含安装器、代码签名、自动更新、本地 Whisper 或 Realtime API。

### 后续计划

- 根据发布检查结果生成干净的 win-x64 zip 包。
- 增加代码签名、版本化发布资产和校验和。
- 评估播放设备切换后的自动恢复。
- 增加 Overlay 位置、字号与透明度的用户设置。
- 扩展多语言真实音频回归测试。
- 在 zip 发布稳定后再评估安装器与自动更新流程。
