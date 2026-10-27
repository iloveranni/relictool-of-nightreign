# 源码构建 / Build from source

## 中文

从 Release 页面下载 `Source code (zip)` 并完整解压，或克隆仓库。在包含 `NightreignRelicTool.sln` 的根目录运行命令。玩家程序 ZIP 不能用于构建。

准备环境：

- Windows x64、PowerShell 5.1 或 7。
- [.NET SDK 10.0.302](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)，版本由 `global.json` 固定。
- [Python 3.12+](https://www.python.org/downloads/windows/)，资源脚本只使用标准库。
- .NET Framework 4.8 或兼容运行时，用于运行程序和测试。

构建：

```powershell
.\Build.ps1
```

可选：构建完成后运行合成测试：

```powershell
.\Test.ps1 -SkipBuild
```

若工具不在 PATH 中，为 `Build.ps1` 指定 `-DotnetExecutable`、`-PythonExecutable`；`Test.ps1` 也接受这两个参数。若执行策略阻止脚本，可确认脚本内容后用 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1`，仅对该进程生效。

程序输出在 `src/NightreignRelicTool/bin/x64/Release/net48/`，运行时将 `NightreignRelicTool.exe` 与同名 `.exe.config` 放在一起。简单操作见仓库 [README](../README.md)。源码不包含发行程序或自动打包／上传步骤。

构建脚本按锁文件还原 NuGet 参考程序集（1.0.3）、生成资源并构建 Release x64。首次获取依赖需联网；完整缓存可供离线重建。源码包含所需游戏数据、图标和动画，无需游戏安装目录或私有维护包。目标为 WPF / net48 / x64 / C# 7.3。

直接构建也会准备资源；自定义 Python 路径通过 MSBuild 的 `RelicPythonExecutable` 属性传入。缓存与产物在 `.build/`、`_build/`、`bin/`、`obj/`，均被 Git 忽略。资源映射见 [RESOURCES.md](RESOURCES.md)。

合成测试不读取真实存档或 Steam 账户配置，不能替代真实存档、人工界面、性能、多屏或混合 DPI 验证。不同构建环境生成的 EXE 哈希可能不同。

## English

Download and fully extract `Source code (zip)` from Releases, or clone the repository. Run commands from the root containing `NightreignRelicTool.sln`. The app ZIP is not a source package.

Requirements:

- Windows x64 and PowerShell 5.1 or 7.
- [.NET SDK 10.0.302](https://dotnet.microsoft.com/en-us/download/dotnet/10.0), pinned by `global.json`.
- [Python 3.12+](https://www.python.org/downloads/windows/); resource scripts use only the standard library.
- .NET Framework 4.8 or a compatible runtime to run the app and tests.

Build with `Build.ps1`. Optionally run `Test.ps1 -SkipBuild` afterward. Both scripts accept `-DotnetExecutable` and `-PythonExecutable` if the tools are not in PATH. If execution policy blocks the script, review it and use `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1`; the override applies only to that process.

Output is in `src/NightreignRelicTool/bin/x64/Release/net48/`. Keep `NightreignRelicTool.exe` and its `.exe.config` together. See the repository [README](../README.md) for basic usage. No release binaries or automated packaging/upload steps are included.

The build restores locked NuGet reference assemblies (1.0.3), generates resources and builds Release x64. Initial dependency downloads need internet access; a complete cache supports offline rebuilds. Required game data, icons and animation are included; no game installation or private maintenance archive is needed. The target is WPF / net48 / x64 / C# 7.3.

Direct builds also prepare resources. Set the MSBuild `RelicPythonExecutable` property for a custom Python path. `.build/`, `_build/`, `bin/` and `obj/` contain ignored caches and outputs. See [RESOURCES.md](RESOURCES.md) for resource mappings.

Synthetic tests do not read real saves or Steam account configuration. They do not replace real-save, manual UI, performance, multi-monitor or mixed-DPI checks. EXE hashes may differ between build environments.
