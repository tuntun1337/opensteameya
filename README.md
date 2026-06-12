# SteamEYA

SteamEYA 是一个 WinUI 3 桌面工具，用于通过 EYA 令牌登录 Steam、清理创意工坊订阅、解析上游卡密，并查询账号的 Steam/JWT/CS2 状态。

应用使用 .NET 10 **Native AOT** 编译为原生代码，并采用 Windows App SDK **自包含**部署：用户电脑无需安装任何运行时（既不需要 .NET Desktop Runtime，也不需要 Windows App Runtime），解压即用。

## 界面结构

- **登录**：手动模式（账户名 + EYA 令牌）/ 自动模式（上游服务器 + 卡密解析），右侧账号信息面板实时展示 Steam64、JWT 过期时间、可用状态、优先分、CS2 等级与冷却状态。
- **历史账号**：自动记录登录/查询过的账号，支持按账户名、昵称、Steam64 搜索过滤，可一键查询或载入到登录页。
- **关于**：版本信息、GitHub Releases 自动更新检查、更新日志。

## 运行环境

当前 Release 为 Windows x64 Native AOT 自包含包（7z 压缩后约 15 MB，解压后约 64 MB）。

必需依赖：

- Windows 10 1809（Build 17763）或更高版本，推荐 Windows 11。
- Steam 客户端。
- **无需**安装 .NET Desktop Runtime 或 Windows App Runtime——所有运行时都已随包打包。

解压依赖：

- [7-Zip](https://www.7-zip.org/download.html) 或 NanaZip，用于解压 Release 里的 `.7z` 文件。
- 下载 7-Zip 时请认准官方域名 `7-zip.org`。

如果双击 `SteamEyaWinUI.exe` 没有反应，请确认已把压缩包**完整解压到一个普通目录**（不要在压缩包内直接运行，运行时 DLL 必须和 exe 在同一目录）。

## 下载和运行

1. 打开 GitHub Releases。
2. 下载最新的 `SteamEYA-<version>-win-x64.7z`。
3. 解压到一个普通目录，不要直接在压缩包内运行。
4. 双击 `SteamEyaWinUI.exe`。

发布包不做自解压，避免弹出 7-Zip SFX 解压框。

## 本地开发与构建

日常开发（JIT，便于调试）：

```
dotnet build SteamEyaWinUI\SteamEyaWinUI.csproj -c Release
```

Native AOT 发布（需要 Visual Studio C++ 工具链，请在 VS 开发者命令提示符中执行，或确保 `vswhere.exe` 在 PATH 中）：

```
dotnet publish SteamEyaWinUI\SteamEyaWinUI.csproj -c Release -r win-x64 -p:Platform=x64
```

说明：

- `PublishAot` 已固定在项目文件中（Windows App SDK 要求在还原期生效）。
- 默认是自包含部署（`WindowsAppSDKSelfContained=true`）：把 Windows App SDK 运行时一并打包，用户零安装。若改为框架依赖（更小但需用户装 Windows App Runtime），设为 `false` 并加 `Microsoft.WindowsAppSDK.Runtime` 包引用。
- 项目只引用 `Microsoft.WindowsAppSDK.WinUI` 组件包，避免元包拖入 onnxruntime/DirectML 等无用载荷。
- JSON 全部使用 source generator（`JsonSerializerIsReflectionEnabledByDefault=false`），新增序列化类型时请加入对应的 `JsonSerializerContext`。
- XAML 绑定请用 `{x:Bind}`；跨 WinRT ABI 的模型类需要标注 `partial`。

## 发版规则

每次向 `main` 分支推送都会自动创建一个 Release。

版本号规则：

- Tag 格式：`v0.1.<run_number>`
- Release 标题：`SteamEYA v0.1.<run_number>`
- 产物名称：`SteamEYA-0.1.<run_number>-win-x64.7z`

Release 会包含：

- Windows x64 Native AOT 自包含 `.7z` 成品包（CI 强制 25 MB 上限）。
- `latest.json`，包含版本号、tag、平台、commit、文件大小、SHA256 和更新日志。
- Release Notes，包含版本号和从上一个 `v*` 版本以来的提交更新日志。

## 检查更新

项目仓库为公开仓库，客户端可以直接读取 GitHub Latest Release，不需要 GitHub Token。

应用内“关于”页面会：

- 显示当前版本。
- 连接 GitHub Releases。
- 读取最新 Release 附件里的 `latest.json`。
- 比较本地版本和最新版本。
- 提供 GitHub 仓库、发布页和下载更新入口。

后续自动更新可以继续复用 `latest.json` 里的 `artifactName`、`artifactSize`、`artifactSha256` 和 `changelog` 字段。
