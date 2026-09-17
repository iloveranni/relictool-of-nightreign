# Public resources

`src/NightreignRelicTool/Resources` contains all data required by the public build. No game installation, downloaded data pack or authoring database is needed. Regulation identifiers, runtime effect IDs, calculation terms, verification states, bindings and official Chinese/English text are retained.

| Input | Embedded logical name | Format |
| --- | --- | --- |
| `ui-text.json` | `NightreignRelicTool.Localization.Ui.json` | Generated gzip |
| `game-text-1.03.5.json` | `NightreignRelicTool.Localization.Game.json` | Generated gzip |
| `game-text-bindings-1.03.5.json` | `NightreignRelicTool.Localization.Bindings.json` | Generated gzip |
| `relic-scoring-model-1.03.5.json` | `NightreignRelicTool.Resources.ScoringModel.gz` | Generated gzip |
| `custom-effect-search-1.03.5.json` | `NightreignRelicTool.Resources.CustomEffectSearch.gz` | Generated gzip |
| `relic-catalog-1.03.5.compact.json.gz` | `NightreignRelicTool.Resources.RelicCatalog.gz` | Existing gzip |
| `relic-inventory-layout-1.03.5.json` | `NightreignRelicTool.Resources.RelicInventoryLayout.json` | JSON |
| `nrpack-public-key.xml` | `NightreignRelicTool.Resources.NrpackPublicKey.xml` | RSA public key |

WPF dictionaries are compiled from `App.xaml`, `Themes/*.xaml` and `FinalUI/*.xaml`. `Assets/AppIcon.ico` supplies the application/WPF icon and `NightreignRelicTool.FinalUI.Icon.ico`; `Assets/revenant_crawl_spritesheet.png` supplies the existing animation. The public key contains only a modulus and exponent, not private key material.

The generator writes decoded and compressed byte lengths, SHA-256 values, top-level entry counts and logical names to `_build/release-resources/manifest.json`. Input UTF-8 bytes are preserved exactly. Git attributes prevent newline conversion in data JSON and the approved player guide.

## Inventory layout

The layout retains Regulation 1.03.5, ordinary/deep and actual-color partitions, the full unsigned 32-bit descending inventory key, eight columns and uncertain positioning for equal keys. Its provenance links to this public description. Validation remains limited to observed ordinary-red and deep-red first-row samples; other colors, later rows, visibility options and other game versions or modes remain unverified. Metadata does not imply broader validation.

## Effect bindings

Runtime effect IDs, Param/FMG references, aggregation source/version and applicability information are part of the current data contract. They are retained with the original bindings. Non-runtime authoring-file indexes are omitted from public inputs, and non-runtime evidence links use this public description. This does not change the effect records, calculations, official text or matching rules.

The UI intentionally retains six relics in the location window, while hiding only the location text for a white vessel slot.
