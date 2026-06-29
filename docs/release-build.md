# Release Build Guide

本文说明如何为 Screen Subtitle Translator v0.1.0 生成干净的 self-contained win-x64 输出和 Inno Setup 安装器。

## 1. 环境

- Windows 10/11
- .NET 8 SDK
- PowerShell
- Inno Setup 6
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

## 3. 生成 self-contained win-x64 输出

以下命令生成安装器和 zip 共用的发布目录：

```powershell
dotnet publish src\ScreenSubtitleTranslator\ScreenSubtitleTranslator.csproj `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output artifacts\release\ScreenSubtitleTranslator-v0.1.0-win-x64
```

目标电脑不需要预先安装 .NET Runtime。

## 4. 添加发布文档

把普通用户发布说明复制到 publish 输出目录：

- `packaging/README_RELEASE.txt`
- `packaging/start.bat`，仅供 zip 使用

不要复制整个仓库。

## 5. 检查输出

确认主程序版本：

```powershell
(Get-Item artifacts\release\ScreenSubtitleTranslator-v0.1.0-win-x64\ScreenSubtitleTranslator.exe).VersionInfo |
    Select-Object FileVersion, ProductVersion
```

检查禁止项：

```powershell
Get-ChildItem artifacts\release\ScreenSubtitleTranslator-v0.1.0-win-x64 -Recurse |
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

## 6. 编译安装器

```powershell
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" `
    installer\ScreenSubtitleTranslator.iss
```

输出：

```text
artifacts\installer\ScreenSubtitleTranslatorSetup-v0.1.0.exe
```

安装器不包含 `start.bat` 或 PDB。当前安装器未代码签名，Windows SmartScreen 可能显示未知发布者。

## 7. 运行候选输出

```powershell
artifacts\release\ScreenSubtitleTranslator-v0.1.0-win-x64\ScreenSubtitleTranslator.exe
```

按照 `docs/release-checklist.md` 做安装、快捷方式、OpenAI、Start / Stop、卸载和敏感信息检查。
