using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using NightreignRelicTool.CustomEffectSearch;

namespace NightreignRelicTool.Localization
{
    public sealed class UiExtension : MarkupExtension
    {
        public string Key { get; set; }
        public override object ProvideValue(IServiceProvider serviceProvider)
        { return new Binding("[" + Key + "]") { Source = LocalizationService.Current, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider); }
    }
    public sealed class DisplayExtension : MarkupExtension
    {
        public string Path { get; set; }
        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            var binding = new MultiBinding { Converter = new DisplayConverter(), ConverterParameter = Path, Mode = BindingMode.OneWay };
            binding.Bindings.Add(new Binding(Path));
            binding.Bindings.Add(new Binding());
            binding.Bindings.Add(new Binding("Revision") { Source = LocalizationService.Current });
            return binding.ProvideValue(serviceProvider);
        }
    }
    public sealed class DisplayConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type type, object parameter, CultureInfo culture)
        {
            if (values.Length < 2 || values[0] == DependencyProperty.UnsetValue) return "";
            return Render(values[1], (string)parameter, values[0]);
        }
        public object[] ConvertBack(object value, Type[] types, object parameter, CultureInfo culture) { throw new NotSupportedException(); }
        public static object Render(object context, string path, object raw)
        {
            if (raw == null) return "";
            if (path.Contains("."))
            {
                int dot = path.IndexOf('.'); var property = context == null ? null : context.GetType().GetProperty(path.Substring(0,dot));
                return Render(property == null ? null : property.GetValue(context, null), path.Substring(dot+1), raw);
            }
            string text = raw as string;
            var character = context as CustomEffectCharacterOption;
            if (character != null) return L.G("character",character.Id,text);
            var choice = context as CustomEffectEffectChoice;
            if (choice != null && new[]{"Name","SelectionName","VariantTooltipTitle"}.Contains(path)) return L.G("selector",choice.SelectorId,text);
            var category = context as CustomEffectCategoryOption;
            if (category != null && path == "Name") return L.G("category",category.Id,text);
            var primary = context as CustomEffectPrimaryCategoryOption;
            if (primary != null && (path == "Name" || path == "DisplayName"))
            { string name=L.G("primary",primary.Name,L.U(primary.Name)); return primary.ShowItemCount ? name + "  " + primary.ItemCount : name; }
            var preset = context as CustomEffectPresetChoice;
            if (preset != null && path == "Name") return L.G("preset",preset.Preset.ItemId,text);
            var rule = context as CustomEffectRuleViewModel;
            if (rule != null && (path == "DisplayName" || path == "VariantTooltipTitle"))
                return rule.IsPreset ? L.G("preset",rule.PresetItemId,text) : L.G("selector",rule.SelectorId,text);
            var effect = context as CustomEffectEffectLine;
            if (effect != null && path == "Name")
                return effect.SelectorId != null ? L.G("selector",effect.SelectorId,text) : L.G("effect",effect.RuntimeEffectId,text);
            var relic = context as CustomEffectRelicCandidate;
            if (relic != null)
            {
                if (path == "Name") return L.G("preset",relic.ItemId,text);
                string[] names = relic.Effects.Select(e => L.G("effect",e.RuntimeEffectId,e.Name)).ToArray();
                if (path == "EffectNames") return names;
                if (path == "EffectNameSummary") return string.Join("; ",names);
                if (path == "LocationFilter") return string.Join(" · ",(text??"").Split(new[]{" · "},StringSplitOptions.None).Select(L.U));
            }
            var result = context as CustomEffectVesselResultViewModel;
            if (result != null && path == "VesselName") return L.G("vessel",result.VesselId,text);
            var tooltip = context as CustomEffectVesselTooltip;
            if (tooltip != null && path == "Title") return L.G("vessel",tooltip.VesselId,text);
            var tooltipRule = context as CustomEffectVesselTooltipRule;
            if (tooltipRule != null && path == "DisplayName")
                return tooltipRule.PresetItemId.HasValue ? L.G("preset",tooltipRule.PresetItemId,text) : L.G("selector",tooltipRule.SelectorId,text);
            var vm = context as CustomEffectSearchViewModel;
            if (vm != null)
            {
                if (path == "DataVersionLabel") return string.Join(" · ",(text??"").Split(new[]{" · "},StringSplitOptions.None).Select(part => part=="内置" ? L.U(" · 内置").Substring(3) : part=="外部" ? L.U(" · 外部").Substring(3) : part));
                if (path == "VisibleEffectSummary")
                {
                    if (vm.IsPresetCategorySelected) return vm.SelectedPresetGroup==null ? L.F("ui.summary.presets",vm.VisiblePresets.Count) : L.F("ui.summary.group",L.U(vm.SelectedPresetGroup.Name),vm.VisiblePresets.Count);
                    string scope=vm.SelectedCategory!=null ? L.G("category",vm.SelectedCategory.Id,vm.SelectedCategory.Name)
                        : vm.SelectedPrimaryCategory!=null && vm.SelectedPrimaryCategory.Name!="全部"
                            ? L.G("primary",vm.SelectedPrimaryCategory.Name,vm.SelectedPrimaryCategory.Name) : L.U(string.IsNullOrWhiteSpace(vm.SearchText)?"全部词条":"全库搜索");
                    if(vm.SelectedCategory==null && vm.SelectedPrimaryCategory!=null && vm.SelectedPrimaryCategory.Name!="全部" && !string.IsNullOrWhiteSpace(vm.SearchText)) scope=L.F("ui.within",scope);
                    return L.F("ui.summary.effects",scope,vm.VisibleEffects.Count);
                }
                // Never translate player names, file names or user input, even if they equal a UI string.
                if (path == "SavePlayerNameDisplay" && vm.SelectedSaveSlot != null && !string.IsNullOrWhiteSpace(vm.SelectedSaveSlot.Inventory.PlayerName)) return raw;
                if (path == "SelectedSaveName" && !string.IsNullOrWhiteSpace(vm.SelectedSavePath)) return raw;
                if (path == "SearchText") return raw;
                if (path == "CategoryHeading") return vm.IsPresetCategorySelected ? L.U(text) : L.G("primary",vm.SelectedPrimaryCategory == null ? "" : vm.SelectedPrimaryCategory.Name,L.U(text));
            }
            if (path == "WarningText") return string.Join("; ",(text??"").Split('；').Select(L.U));
            if (path == "ToolTip" || path == "SaveCompactStatus")
                return string.Join(" · ",(text??"").Split(new[]{" · "},StringSplitOptions.None).Select(L.U));
            if (path == "TierCountLine")
                return string.Join(", ",(text??"").Split('，').Select(x => x.StartsWith("基础",StringComparison.Ordinal) ? L.U("基础")+x.Substring(2) : x));
            if (path == "TotalLine" && (text??"").StartsWith("合计：",StringComparison.Ordinal))
                return L.F("ui.sum",L.Numeric(text.Substring(3)));
            if (path == "Value" || path == "ValueLabel") return L.Numeric(text);
            return text == null ? raw : L.U(text);
        }
    }
}
