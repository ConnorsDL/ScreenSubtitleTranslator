# Release Build Guide

本文说明如何为 Screen Subtitle Translator v0.1.0 生成可供后续 zip 打包的干净 Release 输出。当前阶段不创建安装器或 zip。

## 1. 环境

- Windows 10/11
- .NET 8 SDK
- PowerShell
- 已还原 NuGet 依赖

构建与测试不需要 `OPENAI_API_KEY`。只有运行真实识别和翻译时才需要 Key。

## 2. 还原、构建与测试

在仓库根目录运行：

```powershell
dotnet restore
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-restore
```

本仓库验收要求：0 个编译错误、0 个警告，全部测试通过。

## 3. 生成 framework-dependent win-x64 输出

以下命令仅生成发布目录，不创建安装器或 zip：

```powershell
dotnet publish src\ScreenSubtitleTranslator\ScreenSubtitleTranslator.csproj `
    --configuration Release `
    --runtime win-x64 `
    --self-contained false `
    --output release\ScreenSubtitleTranslator-v0.1.0-win-x64
```

目标电脑需要安装 .NET 8 Desktop Runtime。

## 4. 添加发布文档

在进入 zip 阶段时，只把以下文档复制到 publish 输出目录：

- `README.md`
- `RELEASE_NOTES.md`

不要复制整个仓库。

## 5. 检查输出

确认主程序版本：

```powershell
(Get-Item release\ScreenSubtitleTranslator-v0.1.0-win-x64\ScreenSubtitleTranslator.exe).VersionInfo |
    Select-Object FileVersion, ProductVersion
```

检查禁止项：

```powershell
Get-ChildItem release\ScreenSubtitleTranslator-v0.1.0-win-x64 -Recurse |
    Where-Object {
        $_.FullName -match 'artifacts|screenshots|diagnostic|TestResults|tests|tools|settings.json'
    }
```

预期没有输出。特别注意：

- 不包含 `artifacts/`。
- 不包含测试截图或视觉验证 JSON。
- 不包含诊断日志。
- 不包含 `%APPDATA%` 中的用户 `settings.json`。
- 不包含 `OPENAI_API_KEY`。

## 6. 运行候选输出

```powershell
release\ScreenSubtitleTranslator-v0.1.0-win-x64\ScreenSubtitleTranslator.exe
```

按照 `docs/release-checklist.md` 做最后一次真实系统音频、OpenAI、Overlay 和 Stop 检查。全部通过后，才进入 zip 打包阶段。
