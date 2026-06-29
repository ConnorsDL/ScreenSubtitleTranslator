Screen Subtitle Translator v0.1.0
Windows x64 Self-contained Release

用途
====
Screen Subtitle Translator 捕获 Windows 正在播放的系统声音，不使用麦克风，
通过 OpenAI 进行语音识别和翻译，并在屏幕底部显示置顶、点击穿透的双语字幕。

系统要求
========
- Windows 10 或 Windows 11 x64
- 可用的音箱、耳机或显示器播放设备
- 可访问 OpenAI API 的互联网连接
- 有效的 OpenAI API Key 和可用 API 额度

安装器和 zip 都是 self-contained 版本，目标电脑不需要预先安装 .NET Runtime。

如何安装
========
1. 双击 ScreenSubtitleTranslatorSetup-v0.1.0.exe。
2. 按安装向导选择目录。默认安装到 Program Files，也可以选择其它目录。
3. 安装器会创建开始菜单快捷方式；桌面快捷方式为可选项。
4. 安装完成后，可以直接勾选启动软件。

安装器当前没有代码签名。Windows SmartScreen 可能显示“未知发布者”或保护提示。
请只使用可信发布页面提供的安装包，并核对发布说明中的校验信息。

第一次启动与配置 API Key
=========================
普通用户不需要 PowerShell 或手动设置环境变量：

1. 从开始菜单或桌面快捷方式启动 Screen Subtitle Translator。
2. 首次没有 Key 时，软件会自动打开 Configure OpenAI API Key 窗口。
3. 在密码输入框中输入 OpenAI API Key。
4. 点击 Test API Key 检查连接。
5. 测试成功后点击 Save。

Key 保存在当前用户的 Windows Credential Manager 中，不写入 settings.json。
API 调用会产生费用。不要把 API Key 发给别人，也不要放进截图、日志或公开仓库。

高级用户也可以使用 OPENAI_API_KEY 环境变量；环境变量始终优先于软件内保存的 Key。

PowerShell 临时设置方式（只对当前 PowerShell 窗口有效）：

    $env:OPENAI_API_KEY="<your-api-key>"
    .\ScreenSubtitleTranslator.exe

Windows 永久设置方式（当前用户）：

    setx OPENAI_API_KEY "<your-api-key>"

执行 setx 后，请关闭并重新打开 PowerShell 或重新启动软件。

如何启动
========
安装器用户：

1. 打开 Windows 开始菜单。
2. 点击 Screen Subtitle Translator。
3. 也可以使用安装时选择创建的桌面快捷方式。

zip 用户需要解压整个 zip，然后双击 start.bat 或 ScreenSubtitleTranslator.exe。
安装器用户不需要 start.bat。

如何使用 Start / Stop
=====================
1. 播放包含语音的 YouTube 视频或其它系统音频。
2. 在主窗口选择源语言、目标语言、字幕模式和延迟模式。
3. 点击 Start 开始捕获、识别、翻译和显示字幕。
4. 运行期间关键设置会被锁定。
5. 点击 Stop 停止后台任务并清空、隐藏字幕。

如何切换语言
============
在 Languages 区域选择 Source Language 和 Target Language。
默认设置为 English (US) en-US -> Chinese Simplified zh-CN。
默认字幕模式为 English + Chinese，默认延迟模式为 Balanced 3s。

切换耳机或音箱
==============
Start 前切换 Windows 默认播放设备，软件会在下一次 Start 时使用新设备。
如果运行中切换耳机、音箱或显示器音频，请点击 Stop，然后重新点击 Start。
v0.1.0 不会在运行中自动迁移到新的播放设备。

常见错误
========
OPENAI_API_KEY 缺失
    设置环境变量，重新打开软件后再点击 Start。

网络失败或 OpenAI API 错误
    检查网络、代理、防火墙、API Key、API 额度和模型访问权限。

没有捕获到系统声音
    确认视频正在播放、Windows 音量正常，并确认正确设备是默认播放设备。

播放设备已切换或断开
    重新连接并设为默认设备，然后执行 Stop / Start。

Translation failed 或 timeout
    检查网络和 OpenAI API 状态，等待片刻后重新 Start。

有声音但没有字幕
    确认内容包含人声、源语言正确，并查看 Runtime Status 和最近事件。

Windows SmartScreen 显示未知发布者
    v0.1.0 安装器尚未代码签名。请确认安装包来自项目的可信发布页面。

API 费用提醒
============
OpenAI API 调用会产生费用，并且与 ChatGPT 订阅分开计费。
语音分片会产生 Speech-to-Text 请求，final 字幕会产生翻译请求。
请在 OpenAI 平台检查当前价格、使用量和额度限制。

本地设置
========
语言、字幕模式和延迟设置保存在：

    %APPDATA%\ScreenSubtitleTranslator\settings.json

settings.json 不包含 API Key。

如何卸载
========
打开 Windows 设置 -> 应用 -> 已安装的应用，找到 Screen Subtitle Translator，
然后点击卸载。也可以运行安装目录中的 unins000.exe。

卸载会删除安装目录中的程序文件和快捷方式。
当前行为是保留用户的 settings.json 和 Windows Credential Manager 中的 API Key，
方便重新安装后继续使用。若要删除 Key，请在卸载前进入软件点击 Clear Key，
或在 Windows Credential Manager 中删除 ScreenSubtitleTranslator:OpenAI。

版本范围
========
v0.1.0 不包含自动更新、Realtime API 或本地 Whisper。安装器尚未代码签名。
