# v0.1.0 Release Checklist

本清单用于生成 zip 之前的发布候选检查。当前阶段不制作安装器。

## 版本与默认设置

- [x] 项目版本为 `0.1.0`，产品标签为 `v0.1.0`。
- [x] 默认源语言为 `en-US`。
- [x] 默认目标语言为 `zh-CN`。
- [x] 默认字幕模式为 English + Chinese。
- [x] 默认延迟模式为 Balanced 3 seconds。

## 凭据与隐私

- [x] 应用只从环境变量读取 `OPENAI_API_KEY`。
- [x] UI 不显示完整 API Key。
- [x] `LocalUserSettingsStore` 的 DTO 不包含 API Key。
- [x] 测试确认带有 API Key 的内存设置保存后，JSON 不包含 Key 或 `apiKey` 字段。
- [x] 当前 `%APPDATA%\ScreenSubtitleTranslator\settings.json` 不包含 API Key 字段。
- [x] 仓库未发现疑似 `sk-...` OpenAI Key 字面量。
- [x] 诊断日志默认关闭；只有设置 `SCREEN_SUBTITLE_DIAGNOSTIC_LOG` 才会落盘。
- [ ] 发布前再次检查 git diff、提交历史和 zip 内容中的凭据。

## 构建与自动化测试

- [x] `dotnet build --configuration Release` 成功。
- [x] Release 构建为 0 errors / 0 warnings。
- [x] `dotnet test --no-restore` 全部通过。
- [ ] 在干净 Windows 10 或 Windows 11 用户环境运行 Release 输出。
- [ ] 使用仅安装 .NET 8 Desktop Runtime 的机器验证 framework-dependent 输出。

## 手动功能检查

- [x] WASAPI Loopback 捕获系统输出，不使用麦克风。
- [x] Start 前切换默认播放设备后可捕获新设备。
- [x] 运行中切换默认播放设备会显示 Stop / Start 提示。
- [x] OpenAI Speech-to-Text 可产生英文 final。
- [x] final 字幕可翻译为中文。
- [x] Overlay 显示英文 + 中文、置顶、点击穿透并自动更新。
- [x] Stop 后 Overlay 清空或隐藏。
- [x] Stop drain 后 translation pending 为 0。
- [x] 应用关闭后没有后台进程残留。
- [ ] 在 100%、125%、150% DPI 各做一次布局检查；当前已验证 125%。
- [ ] 在多显示器环境检查主屏 Overlay 位置。

## Release 输出内容

- [x] `.gitignore` 排除 `artifacts/`、`release/`、诊断日志和结果 JSON。
- [x] `artifacts/` 位于 WPF 项目目录之外，不属于默认 MSBuild Content。
- [ ] 只从 `dotnet publish` 的独立输出目录创建 zip。
- [ ] zip 中不得包含 `artifacts/`、测试截图、诊断日志、测试结果或验证 JSON。
- [ ] zip 中不得包含 `tests/`、`tools/`、源码、`.git/` 或 `.vs/`。
- [ ] zip 中必须包含 `ScreenSubtitleTranslator.exe`、运行时配置、依赖 DLL、README 和 Release Notes。
- [ ] 生成并记录 zip 的 SHA-256。

发布输出检查命令：

```powershell
Get-ChildItem release\ScreenSubtitleTranslator-v0.1.0-win-x64 -Recurse |
    Where-Object {
        $_.FullName -match 'artifacts|screenshots|diagnostic|TestResults|tests|tools'
    }
```

预期没有输出。

## 发布决定

- [ ] 所有阻塞项完成。
- [ ] 已知限制已写入 `RELEASE_NOTES.md`。
- [ ] 可以进入 zip 打包阶段。
