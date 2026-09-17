# Relic Tool 1.0.0

从你已有的遗物中，按所选角色和词条寻找《艾尔登法环 黑夜君临》配装。

**实现思路：** 用 C# / WPF 解析存档副本，结合内置游戏数据完成词条匹配、配装展示和仓库位置推算。

**搜索模型：** `StableExactV1` 在槽位颜色、普通／深夜和遗物不重复的约束下搜索六颗组合。考虑词条适用、叠加与互斥，优先减少“必须”条件缺口，再按用户规则顺序比较有效词条；用安全剪枝减少枚举，选出最多三个不同器皿的最佳组合。最优性以当前内置规则和完整搜索为前提。

**玩家下载：[Windows x64 正式程序](https://github.com/iloveranni/relictool-of-nightreign/releases)**。请选择 Releases 中的 `Relic Tool 1.0.0-Windows-x64.zip`；GitHub 自动生成的 Source code 压缩包是源码。

需要 Windows 10 / 11 64 位及 .NET Framework 4.8。绿色免安装，程序离线运行，不上传数据；仅分析存档副本，不修改原始存档或游戏文件。设置和副本保存在程序目录的 `UserData` 中。

1. 完整解压到可写文件夹，运行 `Relic Tool.exe`。
2. 选择自动找到的存档来源和角色栏位，或点击“导入存档”。
3. 选择游戏角色，添加“必须”或“想要”的词条／预设遗物，按重要程度排序；条件变化后自动计算。
4. 查看器皿与六颗遗物，用“下一个”“恢复最优”和“显示位置”调整、查找。白色万能槽保留遗物，只隐藏位置文字。

完整操作见[双语使用说明](docs/README.txt)。标题栏 `EN / 中` 切换语言，`− / +` 提供四档界面大小。分享程序时请勿带上 `UserData`。

源码构建见[构建说明](docs/BUILD.md)，资源说明见[公开资源](docs/RESOURCES.md)。源码包含当前 WPF 程序、内置数据及合成回归测试；开发依赖首次获取需要联网，程序运行不需要联网。

反馈邮箱：[nrrelictool@gmail.com](mailto:nrrelictool@gmail.com)。作者与贡献者（按既定顺序）：yiyi、Jia、みたに、ymy_ds、is0091、Nightreign Community。

本项目原创代码与文档采用 [MIT 许可证](LICENSE) 开源。第三方依赖及游戏文本、数据和素材保留各自的权利与许可，详见[第三方说明](THIRD_PARTY_NOTICES.md)。

## English

Find ELDEN RING NIGHTREIGN relic loadouts for your chosen character and effects, using relics you already own.

**Implementation:** C# / WPF parses a copy of your save and uses embedded game data to match effects, display loadouts and calculate inventory locations.

**Search model:** `StableExactV1` searches six-relic combinations under slot-color, ordinary/deep and relic-uniqueness constraints. It accounts for applicability, stacking and exclusivity, minimizing unmet Required quantities before comparing effective effects in user-rule order. Safe pruning reduces enumeration, returning the best loadouts for up to three distinct vessels. Optimality is relative to the embedded rules and requires a completed search.

**Download the app from [Releases](https://github.com/iloveranni/relictool-of-nightreign/releases)**: choose `Relic Tool 1.0.0-Windows-x64.zip`. GitHub's automatic Source code archives contain source files.

Requires 64-bit Windows 10 / 11 and .NET Framework 4.8. Portable and offline, with no data uploads. The app analyzes a copy of your save without changing the original save or game files. Settings and save copies stay in `UserData` beside the program.

1. Extract all files to a writable folder and open `Relic Tool.exe`.
2. Select a discovered save source and character slot, or use **Import save**.
3. Choose a game character, add Required / Wanted effects or preset relics and order them by importance. Changes start the search automatically.
4. Inspect vessels and their six relics, then use **Next**, **Restore best** and **Show locations**. Relics in white universal slots remain visible, with location text hidden.

See the [bilingual user guide](docs/README.txt) for details. Use `EN / 中` for language and `− / +` for four interface sizes. Exclude `UserData` when sharing the app.

For development, see [Build instructions](docs/BUILD.md) and [Public resources](docs/RESOURCES.md). The source includes the current WPF app, embedded data and synthetic regression checks. Initial dependency downloads require internet access; running the app does not.

Feedback: [nrrelictool@gmail.com](mailto:nrrelictool@gmail.com). Author and contributors, in order: yiyi, Jia, みたに, ymy_ds, is0091, Nightreign Community.

The project's original code and documentation are released under the [MIT License](LICENSE). Third-party dependencies and game text, data and assets retain their respective rights and licenses; see [Third-party notices](THIRD_PARTY_NOTICES.md).

代码实现由 **GPT Codex** 协助完成。

Code implementation was assisted by **GPT Codex**.
