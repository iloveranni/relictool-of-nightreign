# 构建 / Build

## 环境

- Windows 10 / 11 x64，Windows PowerShell 5.1 或 PowerShell 7。
- 官方 [.NET SDK 10.0.302，Windows x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)；`global.json` 固定这一版本，不自动升级。
- 官方 [Python](https://www.python.org/downloads/windows/) 3.12 或更高版本。已验证 Python 3.13.7 x64；资源脚本只使用标准库，无需 pip 包。
- .NET Framework 4.8 或兼容的系统运行时，用于运行程序及测试。

目标保持 Windows WPF、.NET Framework 4.8、x64、C# 7.3。SDK 自带构建工具；参考程序集由 NuGet 获取，无需另从游戏安装目录提取资料。

`NuGet.Config` 仅使用公共 `nuget.org`。两个工程的 `packages.lock.json` 固定 `Microsoft.NETFramework.ReferenceAssemblies` 和 `Microsoft.NETFramework.ReferenceAssemblies.net48` 为 1.0.3，含内容校验值。它们只用于构建，不随程序分发。首次还原需联网；已有完整依赖缓存时可以断网重建。不要在仓库配置中添加凭据。

## 命令

在仓库根目录运行：

```powershell
.\Build.ps1
.\Test.ps1 -SkipBuild
```

构建脚本检查 SDK 和 Python，按锁定文件还原依赖，生成资源，再构建整个解决方案的 Release x64。默认使用 PATH 中的 `dotnet` 和 `python`。工具未在 PATH 时，使用 `-DotnetExecutable`、`-PythonExecutable` 指定对应可执行文件的路径；路径可包含空格。`Test.ps1` 也支持这两个参数。

若 Windows PowerShell 的执行策略阻止本地脚本，可在确认脚本内容后使用仅对本次进程生效的命令：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Test.ps1 -SkipBuild
```

输出为：

```text
src/NightreignRelicTool/bin/x64/Release/net48/NightreignRelicTool.exe
src/NightreignRelicTool/bin/x64/Release/net48/NightreignRelicTool.exe.config
```

从构建输出运行程序时，将 EXE 和对应 config 放在同一可写文件夹。玩家说明在 `docs/README.txt`。本源码不包含发布程序或自动打包／上传步骤。

依赖缓存位于 `.build/`，生成资源位于 `_build/release-resources/`，编译产物位于各工程 `bin/`、`obj/`，均被 Git 忽略。

## 直接构建与资源

项目的 `PreparePublicResources` 目标已接入 `PrepareForBuild`，直接构建解决方案或产品 csproj 时也会执行资源准备。无需先生成任何未提交文件。使用 Python 的自定义路径时须将同一路径传给 MSBuild 的 `RelicPythonExecutable` 属性。

```powershell
dotnet restore .\NightreignRelicTool.sln --configfile .\NuGet.Config --locked-mode
dotnet build .\NightreignRelicTool.sln -c Release -p:Platform=x64 --no-restore
```

五份可读 JSON 是完整公开输入。脚本保留输入字节，生成不带文件名／时间戳的 gzip 和 `ReleaseResourceIntegrity.g.cs`。三个本地化资源仍验证解码长度与 SHA-256。`Resources` 中的遗物目录 gzip、布局 JSON、公钥以及 `Assets` 中的图标和动画也参与正式构建。

同一压缩库版本的输出可复现；其他 Python／zlib 版本可能产生不同的压缩字节，但解码数据保持一致。不承诺不同构建环境的 EXE 哈希相同。

## 测试范围

`Test.ps1` 运行纯合成回归：模型和数据加载、缺失／损坏／截断资源拒绝、版本及布局、公钥保留、WPF 资源和关于窗口入口、独立穷举对照的前三结果、警告归属、同颗贡献、候选替换及请求恢复。测试不启动应用的存档自动发现，不读取真实存档，不访问用户 Steam 配置。

这些测试不代表完整原生界面验收、真实存档兼容性验证、性能矩阵、多屏或混合 DPI 验证，也不保证所有输入都在固定时间内完成。

## English

Build on Windows x64 with PowerShell, the official .NET SDK **10.0.302**, Python **3.12+** (validated with **3.13.7**, standard library only) and a .NET Framework 4.8-compatible runtime. The target remains WPF / net48 / x64 / C# 7.3.

Run `Build.ps1`, then `Test.ps1 -SkipBuild` from the repository root. Use `-DotnetExecutable` and `-PythonExecutable` to select executable paths when needed. The two reference-assembly packages are locked to **1.0.3** and restored from public nuget.org; initial downloads need internet access. Build dependencies are not bundled with the app.

The product EXE and matching config are written to `src/NightreignRelicTool/bin/x64/Release/net48/`. Keep them together in a writable folder. The player guide is `docs/README.txt`. No packaging, commit or upload step is included.

Direct `dotnet build` also generates the five compressed JSON resources and their integrity descriptors before compilation. Set the MSBuild `RelicPythonExecutable` property if Python is not available as `python`. `.build/`, `_build/`, `bin/` and `obj/` are generated and ignored.

Synthetic checks cover embedded resources, corrupt-input rejection, model and layout loading, the About controls, exhaustive search comparisons, warning attribution, replacement and recovery. They never discover real saves or read Steam account data. They do not constitute a full UI, real-save, performance, multi-monitor or mixed-DPI acceptance test. Different build paths and compression-library versions may produce different EXE hashes.
