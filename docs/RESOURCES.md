# 构建资源 / Build resources

`src/NightreignRelicTool/Resources` 包含公开构建所需数据；无需游戏安装目录、外部数据包或私有编写数据库。

`src/NightreignRelicTool/Resources` contains the data needed for public builds. No game installation, external data pack or private authoring database is required.

| 输入 / Input | 嵌入名称 / Embedded name | 格式 / Format |
| --- | --- | --- |
| `ui-text.json` | `NightreignRelicTool.Localization.Ui.json` | Generated gzip |
| `game-text-1.03.5.json` | `NightreignRelicTool.Localization.Game.json` | Generated gzip |
| `game-text-bindings-1.03.5.json` | `NightreignRelicTool.Localization.Bindings.json` | Generated gzip |
| `relic-scoring-model-1.03.5.json` | `NightreignRelicTool.Resources.ScoringModel.gz` | Generated gzip |
| `custom-effect-search-1.03.5.json` | `NightreignRelicTool.Resources.CustomEffectSearch.gz` | Generated gzip |
| `relic-catalog-1.03.5.compact.json.gz` | `NightreignRelicTool.Resources.RelicCatalog.gz` | Existing gzip |
| `relic-inventory-layout-1.03.5.json` | `NightreignRelicTool.Resources.RelicInventoryLayout.json` | JSON |
| `nrpack-public-key.xml` | `NightreignRelicTool.Resources.NrpackPublicKey.xml` | RSA public key |

资源脚本保留五份输入 JSON 的 UTF-8 字节，生成无文件名／时间戳的 gzip、完整性代码及 `_build/release-resources/manifest.json`（长度、SHA-256、条目数与嵌入名称）。不同压缩库版本可能生成不同压缩字节，解压数据不变。

The generator preserves the UTF-8 bytes of five input JSON files and creates gzip files without filenames or timestamps, integrity code and `_build/release-resources/manifest.json` (lengths, SHA-256, entry counts and embedded names). Different compression-library versions may produce different compressed bytes while preserving decoded data.

WPF 使用 `App.xaml`、`Themes/*.xaml`、`FinalUI/*.xaml`、`Assets/AppIcon.ico` 和 `Assets/revenant_crawl_spritesheet.png`。RSA XML 仅含公钥，不含私钥。

WPF uses `App.xaml`, `Themes/*.xaml`, `FinalUI/*.xaml`, `Assets/AppIcon.ico` and `Assets/revenant_crawl_spritesheet.png`. The RSA XML contains only a public key, not a private key.

公开输入保留 Regulation 1.03.5 的效果 ID、Param/FMG 映射、计算语义、来源版本、验证状态及官方中英文文本。非运行所需的内部文件索引已去除，相关证据链接指向本文；不能据此扩大验证结论。

Public inputs retain Regulation 1.03.5 effect IDs, Param/FMG mappings, calculation semantics, source versions, verification states and official Chinese/English text. Internal file indexes not needed at runtime are omitted, and related evidence links point here; this does not expand the scope of validation.

库存定位保留普通／深夜、实际颜色分区、完整 UInt32 降序键及每行八列；同键位置不确定。目前仅核对过普通红色与深夜红色首行样本，其他颜色、后续行、显示选项及游戏版本／模式未全面验证。位置窗口保留六颗遗物，仅隐藏白色器皿槽的位置文字。

Inventory positioning keeps ordinary/deep and actual-color partitions, the full descending UInt32 key and eight columns per row. Equal keys have uncertain positions. Validation is limited to ordinary-red and deep-red first-row samples; other colors, later rows, display options and game versions/modes are not fully verified. The position window retains all six relics and hides only position text for white vessel slots.
