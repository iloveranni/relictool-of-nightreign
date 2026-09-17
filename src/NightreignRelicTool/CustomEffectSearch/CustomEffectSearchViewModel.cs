using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using NightreignRelicTool.Core;
using NightreignRelicTool.Localization;

namespace NightreignRelicTool.CustomEffectSearch
{
    public abstract class CustomEffectNotifyObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public static class CustomEffectPlayerValueFormatter
    {
        private static readonly string[] HiddenTechnicalTokens =
        {
            "flat_change", "rate_change", "multiplier/", "percent/", "enum/", "hp/"
        };

        public static string Format(CustomRuntimeEffect effect)
        {
            return effect == null || !effect.ValueConfirmed ? string.Empty : Format(effect.DisplayValue);
        }

        public static string Format(string rawValue)
        {
            string value = (rawValue ?? string.Empty).Trim();
            if (value.Length == 0) return string.Empty;
            if (HiddenTechnicalTokens.Any(token => value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0))
                return string.Empty;
            string[] parts = value.Split(new[] { " / " }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1 && parts.All(part => part.TrimStart().StartsWith("×", StringComparison.Ordinal)))
                return string.Join(" / ", parts.Select(part => NormalizeMultiplier(part.Trim())));
            if (value.StartsWith("×", StringComparison.Ordinal)) return NormalizeMultiplier(value);
            Match points = Regex.Match(value, "^points(?<number>-?[0-9]+(?:\\.[0-9]+)?)$", RegexOptions.IgnoreCase);
            if (points.Success) return NormalizeNumber(points.Groups["number"].Value) + " 点";
            Match hp = Regex.Match(value, "^hp(?<number>-?[0-9]+(?:\\.[0-9]+)?)$", RegexOptions.IgnoreCase);
            if (hp.Success) return NormalizeNumber(hp.Groups["number"].Value) + " HP";
            Match rate = Regex.Match(value, "^rate(?<number>-?[0-9]+(?:\\.[0-9]+)?)$", RegexOptions.IgnoreCase);
            if (rate.Success) return NormalizeNumber(rate.Groups["number"].Value) + "%";
            return value.IndexOf('_') >= 0 ? string.Empty : value;
        }

        private static string NormalizeMultiplier(string value) { return "×" + NormalizeNumber(value.Substring(1)); }
        private static string NormalizeNumber(string value)
        {
            decimal number;
            return decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
                ? number.ToString("0.####", CultureInfo.InvariantCulture) : value;
        }
    }

    internal static class CustomEffectNumericDisplayFormatter
    {
        private const string PlainDecimalFormat = "0.############################";

        internal static string FormatSignificant(decimal value, int significantDigits = 6)
        {
            return RoundSignificant(value, significantDigits)
                .ToString(PlainDecimalFormat, CultureInfo.InvariantCulture);
        }

        internal static decimal RoundSignificant(decimal value, int significantDigits)
        {
            if (significantDigits < 1 || significantDigits > 28)
                throw new ArgumentOutOfRangeException("significantDigits");
            if (value == 0m) return 0m;

            decimal absolute = Math.Abs(value);
            if (absolute >= 1m)
            {
                int integerDigits = 0;
                decimal probe = decimal.Truncate(absolute);
                while (probe >= 1m)
                {
                    probe /= 10m;
                    integerDigits++;
                }
                int decimalPlaces = significantDigits - integerDigits;
                if (decimalPlaces >= 0)
                    return Math.Round(value, decimalPlaces, MidpointRounding.AwayFromZero);
                decimal factor = PowerOfTen(-decimalPlaces);
                return Math.Round(value / factor, 0, MidpointRounding.AwayFromZero) * factor;
            }

            int leadingDecimalPlaces = 0;
            decimal shifted = absolute;
            while (shifted < 1m && leadingDecimalPlaces < 28)
            {
                shifted *= 10m;
                leadingDecimalPlaces++;
            }
            int places = Math.Min(28, leadingDecimalPlaces + significantDigits - 1);
            return Math.Round(value, places, MidpointRounding.AwayFromZero);
        }

        private static decimal PowerOfTen(int exponent)
        {
            decimal result = 1m;
            for (int index = 0; index < exponent; index++) result *= 10m;
            return result;
        }
    }

    public sealed class CustomEffectCharacterOption
    {
        public CustomEffectCharacterOption(CharacterDefinition character)
        {
            Character = character ?? throw new ArgumentNullException("character");
        }
        public CharacterDefinition Character { get; private set; }
        public string Id { get { return Character.Id; } }
        public string Name { get { return Character.Name; } }
        public string DisplayName { get { return Name; } }
    }

    public sealed class CustomEffectSaveSlotOption
    {
        public CustomEffectSaveSlotOption(CharacterInventory inventory)
            : this(inventory, null)
        {
        }
        public CustomEffectSaveSlotOption(CharacterInventory inventory, IEnumerable<CharacterInventory> siblings)
        {
            Inventory = inventory ?? throw new ArgumentNullException("inventory");
            string name = SlotName(inventory);
            CharacterInventory[] matching = (siblings ?? new[] { inventory })
                .Where(item => string.Equals(SlotName(item), name, StringComparison.Ordinal))
                .OrderBy(item => item.SlotIndex).ToArray();
            DisplayName = matching.Length > 1 ? name + "（" + (Array.FindIndex(matching,
                item => item.SlotIndex == inventory.SlotIndex) + 1) + "）" : name;
        }
        private static string SlotName(CharacterInventory inventory)
        { return string.IsNullOrWhiteSpace(inventory.PlayerName) ? "未命名" : inventory.PlayerName.Trim(); }
        public CharacterInventory Inventory { get; private set; }
        public int SlotIndex { get { return Inventory.SlotIndex; } }
        public string DisplayName { get; private set; }
    }

    public sealed class CustomEffectPrimaryCategoryOption
    {
        public CustomEffectPrimaryCategoryOption(CustomEffectPrimaryCategory category)
        {
            Name = category.Name;
            SortOrder = category.SortOrder;
        }
        public CustomEffectPrimaryCategoryOption(string name, int sortOrder, bool isPresetCategory, int? itemCount)
        {
            Name = name;
            SortOrder = sortOrder;
            IsPresetCategory = isPresetCategory;
            ItemCount = itemCount;
        }
        public string Name { get; private set; }
        public int SortOrder { get; private set; }
        public bool IsPresetCategory { get; private set; }
        public int? ItemCount { get; private set; }
        public bool ShowItemCount { get { return ItemCount.HasValue && !IsPresetCategory; } }
        public string DisplayName { get { return ShowItemCount ? Name + "  " + ItemCount.Value : Name; } }
    }

    public sealed class CustomEffectCategoryOption
    {
        public CustomEffectCategoryOption(CustomEffectCategory category, int itemCount)
        {
            Id = category.Id;
            Name = category.Name;
            PrimaryCategory = category.PrimaryCategory;
            SortOrder = category.SortOrder;
            ItemCount = itemCount;
        }
        public int Id { get; private set; }
        public string Name { get; private set; }
        public string PrimaryCategory { get; private set; }
        public int SortOrder { get; private set; }
        public int ItemCount { get; private set; }
        public string CountLabel { get { return ItemCount.ToString(CultureInfo.InvariantCulture); } }
    }

    public sealed class CustomEffectEffectChoice
    {
        public CustomEffectEffectChoice(CustomEffectSelector selector, CustomEffectCategory category, CustomEffectCatalog catalog)
        {
            Selector = selector;
            Category = category;
            CustomRuntimeEffect[] effects = selector.RuntimeEffectIds.Select(catalog.GetEffect).Where(item => item != null).ToArray();
            int highestRank = effects.Length == 0 ? 1 : effects.Max(item => selector.GetVariantPreferenceRank(item.RuntimeEffectId));
            CustomRuntimeEffect[] displayed = highestRank > 1
                ? effects.Where(item => selector.GetVariantPreferenceRank(item.RuntimeEffectId) == highestRank).ToArray()
                : effects;
            SelectionName = selector.Name;
            PreferredVariantName = highestRank > 1 && displayed.Length != 0
                ? displayed.OrderBy(item => item.RuntimeEffectId).First().Name : selector.Name;
            ValueLabel = string.Join(" / ", displayed.Select(CustomEffectPlayerValueFormatter.Format)
                .Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.Ordinal).Take(4));
            IsWarning = !selector.IsAvailableForRealMatching;
            CanAdjustQuantity = !CustomRuleQuantityPolicy.IsFixedSelector(catalog, selector.Id);
            IsNegative = effects.Any(item => item.IsNegative);
            AvailabilityWarning = selector.IsAvailableForRealMatching ? string.Empty : "暂不可用于检索";
            VariantTooltipTitle = selector.Name;
            bool hasUnverifiedValues;
            VariantTooltipLines = BuildVariantTooltipLines(selector, catalog, out hasUnverifiedValues);
            VariantTooltipNote = hasUnverifiedValues && VariantTooltipLines.Length != 0
                ? "另有档位数值待验证" : string.Empty;
        }
        public CustomEffectSelector Selector { get; private set; }
        public CustomEffectCategory Category { get; private set; }
        public string SelectorId { get { return Selector.Id; } }
        public string SelectionName { get; private set; }
        public string PreferredVariantName { get; private set; }
        public string Name { get { return SelectionName; } }
        public string CategoryName { get { return Category.Name; } }
        public string ValueLabel { get; private set; }
        public bool IsWarning { get; private set; }
        public bool CanAdjustQuantity { get; private set; }
        public bool IsNegative { get; private set; }
        public string AvailabilityWarning { get; private set; }
        public bool IsManualSupplement { get { return !Selector.IsOfficialFilter; } }
        public string VariantTooltipTitle { get; private set; }
        public CustomEffectVariantTooltipLine[] VariantTooltipLines { get; private set; }
        public string VariantTooltipNote { get; private set; }
        public bool HasVariantTooltip { get { return VariantTooltipLines.Length != 0; } }

        private static CustomEffectVariantTooltipLine[] BuildVariantTooltipLines(
            CustomEffectSelector selector, CustomEffectCatalog catalog, out bool hasUnverifiedValues)
        {
            hasUnverifiedValues = false;
            List<CustomEffectVariantTooltipLine> lines = new List<CustomEffectVariantTooltipLine>();
            CustomEffectVariantPreference[] variants = selector.VariantPreferences
                ?? new CustomEffectVariantPreference[0];
            HashSet<int> describedEffectIds = new HashSet<int>();
            foreach (CustomEffectVariantPreference variant in variants
                .OrderByDescending(item => item.VariantPreferenceRank)
                .ThenByDescending(item => item.Tier)
                .ThenBy(item => item.RuntimeEffectId))
            {
                describedEffectIds.Add(variant.RuntimeEffectId);
                CustomRuntimeEffect effect = catalog.GetEffect(variant.RuntimeEffectId);
                if (HasUnverifiedCalculationTerms(effect)) hasUnverifiedValues = true;
                string value = FormatConfirmedCalculationTerms(effect == null
                    ? null : effect.CalculationTerms);
                if (string.IsNullOrWhiteSpace(value))
                {
                    hasUnverifiedValues = true;
                    continue;
                }
                string tierLabel = !string.IsNullOrWhiteSpace(variant.TierLabel)
                    ? variant.TierLabel.Trim()
                    : variant.Tier.HasValue
                        ? variant.Tier.Value > 0
                            ? "＋" + variant.Tier.Value.ToString(CultureInfo.InvariantCulture)
                            : "基础"
                        : "数值";
                if (!lines.Any(item => item.VariantPreferenceRank == variant.VariantPreferenceRank
                    && string.Equals(item.TierLabel, tierLabel, StringComparison.Ordinal)
                    && string.Equals(item.ValueLabel, value, StringComparison.Ordinal)))
                    lines.Add(new CustomEffectVariantTooltipLine(tierLabel, value,
                        variant.VariantPreferenceRank));
            }
            foreach (int runtimeEffectId in selector.RuntimeEffectIds.Where(item => !describedEffectIds.Contains(item))
                .OrderByDescending(selector.GetVariantPreferenceRank).ThenBy(item => item))
            {
                CustomRuntimeEffect effect = catalog.GetEffect(runtimeEffectId);
                if (HasUnverifiedCalculationTerms(effect)) hasUnverifiedValues = true;
                string value = FormatConfirmedCalculationTerms(effect == null ? null : effect.CalculationTerms);
                if (string.IsNullOrWhiteSpace(value))
                {
                    hasUnverifiedValues = true;
                    continue;
                }
                string tierLabel = selector.RuntimeEffectIds.Length == 1 ? "数值" : "已确认";
                int rank = selector.GetVariantPreferenceRank(runtimeEffectId);
                if (!lines.Any(item => item.VariantPreferenceRank == rank
                    && string.Equals(item.TierLabel, tierLabel, StringComparison.Ordinal)
                    && string.Equals(item.ValueLabel, value, StringComparison.Ordinal)))
                    lines.Add(new CustomEffectVariantTooltipLine(tierLabel, value, rank));
            }
            return lines.OrderByDescending(item => item.VariantPreferenceRank)
                .ThenBy(item => item.TierLabel, StringComparer.Ordinal).ToArray();
        }

        private static bool HasUnverifiedCalculationTerms(CustomRuntimeEffect effect)
        {
            return effect == null || effect.CalculationTerms == null || effect.CalculationTerms.Length == 0
                || effect.CalculationTerms.Any(item => item == null || !item.NumericValue.HasValue
                    || !IsConfirmedCalculation(item.VerificationStatus)
                    || (!string.Equals(item.Operation, "multiplier", StringComparison.Ordinal)
                        && !string.Equals(item.Operation, "additive", StringComparison.Ordinal)
                        && !string.Equals(item.Operation, "flat", StringComparison.Ordinal)));
        }

        private static string FormatConfirmedCalculationTerms(CustomEffectCalculationTerm[] terms)
        {
            if (terms == null || terms.Length == 0) return string.Empty;
            string[] values = terms.Where(item => item != null && item.NumericValue.HasValue
                    && IsConfirmedCalculation(item.VerificationStatus))
                .Select(FormatCalculationTerm)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.Ordinal).ToArray();
            return string.Join(" / ", values);
        }

        private static bool IsConfirmedCalculation(string verificationStatus)
        {
            return string.Equals(verificationStatus, "verified_direct_data", StringComparison.Ordinal)
                || string.Equals(verificationStatus, "verified_structural", StringComparison.Ordinal)
                || string.Equals(verificationStatus, "project_verified", StringComparison.Ordinal)
                || string.Equals(verificationStatus, "confirmed", StringComparison.Ordinal);
        }

        private static string FormatCalculationTerm(CustomEffectCalculationTerm term)
        {
            string number = FormatTooltipNumber(term.NumericValue.Value);
            if (string.Equals(term.Operation, "multiplier", StringComparison.Ordinal)) return "×" + number;
            string sign = term.NumericValue.Value > 0 ? "+" : string.Empty;
            if (string.Equals(term.Operation, "additive", StringComparison.Ordinal)
                || string.Equals(term.Operation, "flat", StringComparison.Ordinal))
                return sign + number + FormatTooltipUnit(term.Unit);
            return string.Empty;
        }

        private static string FormatTooltipNumber(decimal number)
        {
            return CustomEffectNumericDisplayFormatter.FormatSignificant(number);
        }

        private static string FormatTooltipUnit(string unit)
        {
            string value = (unit ?? string.Empty).Trim();
            if (value.Length == 0 || string.Equals(value, "multiplier", StringComparison.OrdinalIgnoreCase)) return string.Empty;
            if (string.Equals(value, "percent", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "percentage_points", StringComparison.OrdinalIgnoreCase)) return "%";
            if (string.Equals(value, "points", StringComparison.OrdinalIgnoreCase)) return " 点";
            if (string.Equals(value, "hp", StringComparison.OrdinalIgnoreCase)) return " HP";
            return value.IndexOf('_') >= 0 || value.IndexOf('/') >= 0 ? string.Empty : " " + value;
        }
    }

    public sealed class CustomEffectVariantTooltipLine
    {
        public CustomEffectVariantTooltipLine(string tierLabel, string valueLabel, int variantPreferenceRank)
        {
            TierLabel = tierLabel ?? string.Empty;
            ValueLabel = valueLabel ?? string.Empty;
            VariantPreferenceRank = variantPreferenceRank;
        }

        public string TierLabel { get; private set; }
        public string ValueLabel { get; private set; }
        public int VariantPreferenceRank { get; private set; }
    }

    public sealed class CustomEffectPresetGroupOption
    {
        public CustomEffectPresetGroupOption(string key, string name, string classification, int count)
        {
            Key = key;
            Name = name;
            Classification = classification;
            Count = count;
        }
        public string Key { get; private set; }
        public string Name { get; private set; }
        public string Classification { get; private set; }
        public int Count { get; private set; }
        public string CountLabel { get { return Count.ToString(CultureInfo.InvariantCulture); } }
        public string ToolTip { get { return Name + " · " + Count + " 颗真实预设遗物"; } }
        public bool Matches(OfficialPresetRelic preset)
        {
            return preset != null && preset.IsAvailableForRealMatching
                && string.Equals(preset.Classification, Classification, StringComparison.Ordinal);
        }
    }

    public sealed class CustomEffectEffectLine
    {
        public CustomEffectEffectLine(int? runtimeEffectId, string name, string value, bool valueConfirmed, bool negative, string verificationStatus)
        {
            RuntimeEffectId = runtimeEffectId;
            Name = name;
            Value = value;
            IsValueConfirmed = valueConfirmed;
            IsNegative = negative;
            VerificationStatus = verificationStatus;
        }
        public CustomEffectEffectLine(string name, string value, bool valueConfirmed, bool negative, string verificationStatus)
            : this(null, name, value, valueConfirmed, negative, verificationStatus) { }
        public string SelectorId { get; internal set; }
        public int? RuntimeEffectId { get; private set; }
        public string Name { get; private set; }
        public string Value { get; private set; }
        public bool IsValueConfirmed { get; private set; }
        public bool IsApplicable { get; internal set; } = true;
        public bool IsCore { get; internal set; }
        public bool IsNegative { get; private set; }
        public string VerificationStatus { get; private set; }
    }

    public sealed class CustomEffectPresetChoice
    {
        public CustomEffectPresetChoice(OfficialPresetRelic preset, CustomEffectCatalog catalog)
        {
            Preset = preset;
            CustomRuntimeEffect[] runtimeEffects = preset.FixedRuntimeEffectIds.Select(catalog.GetEffect)
                .Where(item => item != null).ToArray();
            Effects = runtimeEffects.Select(item => new CustomEffectEffectLine(item.RuntimeEffectId, item.Name,
                CustomEffectPlayerValueFormatter.Format(item), item.ValueConfirmed, item.IsNegative, item.VerificationStatus)).ToArray();
            SelectionEffects = runtimeEffects.Select(item => new
                {
                    Effect = item,
                    Selector = catalog.Selectors.Where(selector => selector.RuntimeEffectIds.Contains(item.RuntimeEffectId))
                        .OrderByDescending(selector => selector.IsOfficialFilter).ThenBy(selector => selector.SortOrder)
                        .ThenBy(selector => selector.Id, StringComparer.Ordinal).FirstOrDefault()
                })
                .GroupBy(item => item.Selector == null ? item.Effect.Name : item.Selector.Name, StringComparer.Ordinal)
                .Select(group => new CustomEffectEffectLine(group.Key, string.Empty, false,
                    group.Any(item => item.Effect.IsNegative), string.Empty) { SelectorId = group.First().Selector == null ? null : group.First().Selector.Id }).ToArray();
        }
        public OfficialPresetRelic Preset { get; private set; }
        public string Name { get { return Preset.Name; } }
        public string Group { get { return Preset.Group; } }
        public string SearchText { get { return Name + " " + string.Join(" ", SelectionEffects.Select(item => item.Name)); } }
        public CustomEffectEffectLine[] Effects { get; private set; }
        public CustomEffectEffectLine[] SelectionEffects { get; private set; }
    }

    public sealed class CustomEffectRuleViewModel : CustomEffectNotifyObject
    {
        private readonly Action<CustomEffectRuleViewModel> _changed;
        private bool _isRequired;
        private int _quantityTarget;
        private int _priority;

        public CustomEffectRuleViewModel(string ruleId, CustomEffectEffectChoice effect, CustomEffectPresetChoice preset,
            bool isRequired, int quantityTarget, Action<CustomEffectRuleViewModel> changed)
        {
            RuleId = ruleId;
            Effect = effect;
            Preset = preset;
            _isRequired = isRequired;
            _quantityTarget = CanAdjustQuantity ? Math.Max(1, Math.Min(6, quantityTarget)) : 1;
            _changed = changed;
        }
        public string RuleId { get; private set; }
        public CustomEffectEffectChoice Effect { get; private set; }
        public CustomEffectPresetChoice Preset { get; private set; }
        public string SelectorId { get { return Effect == null ? null : Effect.SelectorId; } }
        public int? PresetItemId { get { return Preset == null ? (int?)null : Preset.Preset.ItemId; } }
        public bool IsPreset { get { return Preset != null; } }
        public bool CanAdjustQuantity { get { return !IsPreset && Effect != null && Effect.CanAdjustQuantity; } }
        public string DisplayName { get { return IsPreset ? Preset.Name : Effect.Name; } }
        public bool HasWarning { get { return !IsPreset && Effect.IsWarning; } }
        public bool IsNegative { get { return IsPreset ? Preset.Effects.Any(item => item.IsNegative) : Effect.IsNegative; } }
        public int Priority { get { return _priority; } internal set { if (SetField(ref _priority, value)) OnPropertyChanged("PriorityLabel"); } }
        public string PriorityLabel { get { return Priority.ToString("00", CultureInfo.InvariantCulture); } }
        public bool IsRequired
        {
            get { return _isRequired; }
            set { if (SetField(ref _isRequired, value)) { OnPropertyChanged("SelectionStatusLabel"); Changed(); } }
        }
        public int QuantityTarget
        {
            get { return _quantityTarget; }
            set { int next = CanAdjustQuantity ? Math.Max(1, Math.Min(6, value)) : 1; if (SetField(ref _quantityTarget, next)) Changed(); }
        }
        public string SelectionStatusLabel { get { return IsRequired ? "必须" : "想要"; } }
        public string VariantTooltipTitle { get { return Effect == null ? string.Empty : Effect.VariantTooltipTitle; } }
        public CustomEffectVariantTooltipLine[] VariantTooltipLines
        {
            get { return Effect == null ? new CustomEffectVariantTooltipLine[0] : Effect.VariantTooltipLines; }
        }
        public string VariantTooltipNote { get { return Effect == null ? string.Empty : Effect.VariantTooltipNote; } }
        public bool HasVariantTooltip { get { return Effect != null && Effect.HasVariantTooltip; } }
        private void Changed() { if (_changed != null) _changed(this); }
    }

    public sealed class CustomEffectRelicCandidate
    {
        public CustomEffectRelicCandidate(int instanceId, int itemId, int colorId, string colorName,
            string name, string group, string classification,
            CustomEffectEffectLine[] effects, bool warning, string warningText, string expectedLocation,
            string previousRelic, string nextRelic)
            : this(instanceId, itemId, colorId, colorName, name, group, classification,
                effects, warning, warningText, string.Empty, expectedLocation)
        {
            PreviousRelic = previousRelic ?? string.Empty;
            NextRelic = nextRelic ?? string.Empty;
        }

        public CustomEffectRelicCandidate(int instanceId, int itemId, int colorId, string colorName,
            string name, string group, string classification,
            CustomEffectEffectLine[] effects, bool warning, string warningText,
            string locationFilter, string positionText)
        {
            InstanceId = instanceId;
            ItemId = itemId;
            ColorId = colorId;
            ColorName = colorName ?? string.Empty;
            Name = name ?? string.Empty;
            Group = group ?? string.Empty;
            Classification = classification ?? string.Empty;
            Effects = effects ?? new CustomEffectEffectLine[0];
            EffectNames = Effects.Where(item => item != null && !string.IsNullOrWhiteSpace(item.Name))
                .Select(item => item.Name.Trim()).ToArray();
            EffectNameSummary = string.Join("；", EffectNames);
            EffectSummary = string.Join("；", Effects.Select(item => string.IsNullOrWhiteSpace(item.Value)
                ? item.Name : item.Name + " " + item.Value));
            HasWarning = warning;
            WarningText = warningText ?? string.Empty;
            LocationFilter = locationFilter ?? string.Empty;
            PositionText = positionText ?? string.Empty;
            ExpectedLocation = PositionText;
            PreviousRelic = string.Empty;
            NextRelic = string.Empty;
        }
        public int InstanceId { get; private set; }
        public int ItemId { get; private set; }
        public int ColorId { get; private set; }
        public string ColorName { get; private set; }
        public string Name { get; private set; }
        public string Group { get; private set; }
        public string Classification { get; private set; }
        public CustomEffectEffectLine[] Effects { get; private set; }
        public string[] EffectNames { get; private set; }
        public string EffectNameSummary { get; private set; }
        public string EffectSummary { get; private set; }
        public bool HasWarning { get; private set; }
        public string WarningText { get; private set; }
        public string LocationFilter { get; private set; }
        public string PositionText { get; private set; }
        public string ExpectedLocation { get; private set; }
        public string PreviousRelic { get; private set; }
        public string NextRelic { get; private set; }
        public bool ShouldDisplayName { get { return !string.IsNullOrWhiteSpace(Name); } }
        public Visibility NameVisibility { get { return ShouldDisplayName ? Visibility.Visible : Visibility.Collapsed; } }
        internal void UpdateWarnings(CustomEffectRelicCandidate current)
        {
            HasWarning = current.HasWarning;
            WarningText = current.WarningText;
        }
    }

    public sealed class CustomEffectRelicSlotViewModel : CustomEffectNotifyObject
    {
        private CustomEffectRelicCandidate _current;
        private bool _canPrevious;
        private bool _canNext;
        private int _candidateIndex;
        private int _candidateCount;

        internal CustomEffectRelicSlotViewModel(int assignmentIndex, bool isDeep, int slotIndex, int slotColorId,
            CustomEffectRelicCandidate current, bool canPrevious, bool canNext)
        {
            AssignmentIndex = assignmentIndex;
            IsDeep = isDeep;
            SlotIndex = slotIndex;
            SlotColorId = slotColorId;
            SlotLabel = isDeep ? "深夜" : "普通";
            _current = current;
            _canPrevious = canPrevious;
            _canNext = canNext;
            _candidateCount = canNext ? 2 : 1;
        }
        public int AssignmentIndex { get; private set; }
        public int SlotIndex { get; private set; }
        public string SlotLabel { get; private set; }
        public int SlotColorId { get; private set; }
        public bool IsDeep { get; private set; }
        public int DisplayRow { get { return AssignmentIndex % 3; } }
        public int DisplayColumn { get { return IsDeep ? 1 : 0; } }
        public int CandidateIndex { get { return _candidateIndex; } }
        public int CandidateCount { get { return _candidateCount; } }
        public CustomEffectRelicCandidate Current { get { return _current; } }
        public int RelicColorId { get { return Current.ColorId; } }
        public string RelicColorName { get { return Current.ColorName; } }
        public bool CanPrevious { get { return _canPrevious; } }
        public bool CanNext { get { return _canNext; } }

        internal void UpdateCurrent(CustomEffectRelicCandidate current, int legalCount)
        {
            // A vessel result belongs to one inventory/character session. An unchanged
            // instance keeps its presentation identity; warning attribution can still change.
            if (_current != null && _current.InstanceId == current.InstanceId) _current.UpdateWarnings(current);
            else { _current = current; OnPropertyChanged("Current"); OnPropertyChanged("RelicColorId"); OnPropertyChanged("RelicColorName"); }
            SetBounds(0, legalCount);
        }

        internal void SetBounds(int index, int count)
        {
            _candidateIndex = Math.Max(0, index);
            _candidateCount = Math.Max(0, count);
            _canPrevious = index > 0;
            _canNext = index >= 0 && index + 1 < count;
            OnPropertyChanged("CandidateIndex");
            OnPropertyChanged("CandidateCount");
            OnPropertyChanged("CanPrevious");
            OnPropertyChanged("CanNext");
        }
    }

    public sealed class CustomEffectSlotColorViewModel
    {
        public CustomEffectSlotColorViewModel(int colorId, string colorName, string slotKind)
        {
            ColorId = colorId;
            ColorName = colorId == 4 || colorName == "白色万能槽" || colorName == "白色万能草" ? "白色" : colorName;
            SlotKind = slotKind;
        }
        public int ColorId { get; private set; }
        public string ColorName { get; private set; }
        public string SlotKind { get; private set; }
        public string ToolTip { get { return SlotKind + " · " + ColorName; } }
    }

    public sealed class CustomEffectVesselTooltipRule
    {
        public CustomEffectVesselTooltipRule(string displayName, string tierCountLine,
            string formulaLine, string totalLine, string noteLine)
        {
            DisplayName = displayName ?? string.Empty;
            TierCountLine = tierCountLine ?? string.Empty;
            FormulaLine = formulaLine ?? string.Empty;
            TotalLine = totalLine ?? string.Empty;
            NoteLine = noteLine ?? string.Empty;
        }

        public string SelectorId { get; internal set; }
        public int? PresetItemId { get; internal set; }
        public string DisplayName { get; private set; }
        public string TierCountLine { get; private set; }
        public string FormulaLine { get; private set; }
        public string TotalLine { get; private set; }
        public string NoteLine { get; private set; }
    }

    public sealed class CustomEffectVesselTooltip
    {
        public static readonly CustomEffectVesselTooltip Empty = new CustomEffectVesselTooltip(
            string.Empty, new CustomEffectVesselTooltipRule[0]);

        public CustomEffectVesselTooltip(string title, CustomEffectVesselTooltipRule[] rules)
        {
            Title = title ?? string.Empty;
            Rules = rules ?? new CustomEffectVesselTooltipRule[0];
        }

        public int? VesselId { get; internal set; }
        public string Title { get; private set; }
        public CustomEffectVesselTooltipRule[] Rules { get; private set; }
        public bool HasContent { get { return Rules.Length != 0; } }
    }

    public sealed class CustomEffectVesselResultViewModel : CustomEffectNotifyObject
    {
        private CustomEffectRelicSlotViewModel[] _slots;
        private bool _isManual;
        internal CustomEffectVesselResultViewModel(int index, CustomSearchBuild optimalBuild)
        {
            Index = index;
            OptimalBuild = optimalBuild;
            CurrentBuild = optimalBuild;
            _slots = new CustomEffectRelicSlotViewModel[0];
            VesselTooltip = CustomEffectVesselTooltip.Empty;
        }
        public int Index { get; private set; }
        public int VesselId { get { return CurrentBuild.Vessel.Id; } }
        public string VesselName { get { return CurrentBuild.Vessel.Name; } }
        public CustomEffectSlotColorViewModel[] SlotColors { get; internal set; }
        public CustomEffectRelicSlotViewModel[] Slots { get { return _slots; } }
        public string RankLabel { get { return "推荐 " + (Index + 1); } }
        public bool IsManual { get { return _isManual; } }
        public CustomSearchBuild OptimalBuild { get; internal set; }
        public CustomSearchBuild CurrentBuild { get; internal set; }
        public CustomEffectVesselTooltip VesselTooltip { get; private set; }
        public bool HasVesselTooltip { get { return VesselTooltip != null && VesselTooltip.HasContent; } }
        internal void Apply(CustomSearchBuild build, bool manual, CustomEffectSlotColorViewModel[] colors,
            CustomEffectRelicSlotViewModel[] slots, CustomEffectVesselTooltip vesselTooltip)
        {
            CurrentBuild = build;
            _isManual = manual;
            SlotColors = colors;
            _slots = slots;
            VesselTooltip = vesselTooltip ?? CustomEffectVesselTooltip.Empty;
            OnPropertyChanged("VesselId");
            OnPropertyChanged("VesselName");
            OnPropertyChanged("SlotColors");
            OnPropertyChanged("Slots");
            OnPropertyChanged("IsManual");
            OnPropertyChanged("VesselTooltip");
            OnPropertyChanged("HasVesselTooltip");
        }
    }

    public sealed class CustomEffectSearchViewModel : CustomEffectNotifyObject, IDisposable
    {
        public const string DataMode = "PRODUCTION_REAL_READ_ONLY_1_03_5";
        private static readonly double[] ScaleLevels = { 0.90, 1.00, 1.20, 1.40 };
        public const int MaximumRules = 10;
        private const int DefaultScaleIndex = 1;
        private const int SearchDebounceMilliseconds = 120;
        private const int PersistenceDebounceMilliseconds = 350;

        private readonly ICustomEffectApplicationService _application;
        private readonly ICustomEffectUiStateService _uiStateService;
        private readonly SemaphoreSlim _importGate = new SemaphoreSlim(1, 1);
        private readonly SynchronizationContext _uiContext;
        private CustomEffectUiStateDocument _uiState;
        private CustomEffectCatalog _catalog;
        private CustomEffectCharacterOption _selectedCharacter;
        private CustomEffectSaveSlotOption _selectedSaveSlot;
        private CustomEffectPrimaryCategoryOption _selectedPrimaryCategory;
        private CustomEffectCategoryOption _selectedCategory;
        private CustomEffectPresetGroupOption _selectedPresetGroup;
        private CustomEffectVesselResultViewModel _selectedResult;
        private string _searchText;
        private string _statusText;
        private string _notificationText;
        private string _dataError;
        private int _uiScaleIndex;
        private string _selectedSavePath;
        private string _saveCheckStatus;
        private bool _suppressState;
        private bool _isSearching;
        private bool _isUpdating;
        private bool _isReading;
        private bool _isReplacing;
        private SaveSession _visibleSave;
        private SaveSourceInfo _selectedSaveSource;
        private bool _disposed;
        private long _importRevision;
        private long _searchRevision;
        private long _replacementRevision;
        private CancellationTokenSource _searchCancellation;
        private CancellationTokenSource _replacementCancellation;
        private CancellationTokenSource _persistenceCancellation;
        private Task _currentSearchTask = Task.FromResult(0);
        private Task _persistenceTask = Task.FromResult(0);
        private Task _initializationTask;
        private List<SavedCustomBuildState> _pendingBuildStates;
        private int _pendingSelectedResultIndex;
        private Dictionary<string, int> _usageCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        public CustomEffectSearchViewModel()
            : this(new CustomEffectApplicationService(AppDomain.CurrentDomain.BaseDirectory),
                new JsonCustomEffectUiStateService(AppDomain.CurrentDomain.BaseDirectory))
        {
        }

        public CustomEffectSearchViewModel(ICustomEffectApplicationService application,
            ICustomEffectUiStateService uiStateService)
        {
            _application = application ?? throw new ArgumentNullException("application");
            _uiStateService = uiStateService ?? throw new ArgumentNullException("uiStateService");
            _uiContext = SynchronizationContext.Current;
            LocalizationService.Current.Changed += LanguageChanged;
            Characters = new ObservableCollection<CustomEffectCharacterOption>();
            SaveSlots = new ObservableCollection<CustomEffectSaveSlotOption>();
            SaveSources = new ObservableCollection<SaveSourceInfo>();
            PrimaryCategories = new ObservableCollection<CustomEffectPrimaryCategoryOption>();
            Categories = new ObservableCollection<CustomEffectCategoryOption>();
            AllEffectChoices = new ObservableCollection<CustomEffectEffectChoice>();
            VisibleEffects = new ObservableCollection<CustomEffectEffectChoice>();
            PresetGroups = new ObservableCollection<CustomEffectPresetGroupOption>();
            VisiblePresets = new ObservableCollection<CustomEffectPresetChoice>();
            Rules = new ObservableCollection<CustomEffectRuleViewModel>();
            Results = new ObservableCollection<CustomEffectVesselResultViewModel>();
            _uiState = _uiStateService.Load() ?? new CustomEffectUiStateDocument();
            if (_uiState.Characters == null) _uiState.Characters = new List<CustomEffectCharacterUiState>();
            _uiScaleIndex = Math.Max(0, Math.Min(ScaleLevels.Length - 1, _uiState.UiScaleIndex ?? DefaultScaleIndex));
            _selectedSavePath = _uiState.SelectedSavePath ?? string.Empty;
            _saveCheckStatus = string.IsNullOrWhiteSpace(_selectedSavePath) ? "未导入" : "等待安全导入";
            try
            {
                LoadSnapshot(true);
                if (!(_application is ISaveSourceApplicationService)) DiscoverSave();
                StatusText = string.IsNullOrWhiteSpace(_application.Snapshot.Warning)
                    ? "正式数据已加载。" : _application.Snapshot.Warning;
            }
            catch (Exception)
            {
                DataError = "运行时数据加载失败。请检查程序文件完整性后重新启动。";
                StatusText = "无法启动正式检索。";
            }
        }

        public ObservableCollection<CustomEffectCharacterOption> Characters { get; private set; }
        public ObservableCollection<CustomEffectSaveSlotOption> SaveSlots { get; private set; }
        public ObservableCollection<SaveSourceInfo> SaveSources { get; private set; }
        public SaveSourceInfo SelectedSaveSource { get { return _selectedSaveSource; } }
        public bool IsReading { get { return _isReading; } private set { SetField(ref _isReading, value); } }
        public bool IsReplacing
        {
            get { return _isReplacing; }
            private set
            {
                if (SetField(ref _isReplacing, value))
                { OnPropertyChanged("CanShowPosition"); OnPropertyChanged("CanRestoreOptimal"); }
            }
        }
        public ObservableCollection<CustomEffectPrimaryCategoryOption> PrimaryCategories { get; private set; }
        public ObservableCollection<CustomEffectCategoryOption> Categories { get; private set; }
        public ObservableCollection<CustomEffectEffectChoice> AllEffectChoices { get; private set; }
        public ObservableCollection<CustomEffectEffectChoice> VisibleEffects { get; private set; }
        public ObservableCollection<CustomEffectPresetGroupOption> PresetGroups { get; private set; }
        public ObservableCollection<CustomEffectPresetChoice> VisiblePresets { get; private set; }
        public ObservableCollection<CustomEffectRuleViewModel> Rules { get; private set; }
        public ObservableCollection<CustomEffectVesselResultViewModel> Results { get; private set; }
        public Task CurrentSearchTask { get { return _currentSearchTask; } }
        public long LastSearchElapsedMilliseconds { get; private set; }
        public bool IsDataLoaded { get { return _catalog != null; } }
        // UI reads the session published after the gated operation; it must never
        // wait on the service's I/O lock while copying/parsing a save in the background.
        public bool HasSave { get { return _visibleSave != null && SelectedSaveSlot != null; } }
        public bool IsSearching
        {
            get { return _isSearching; }
            private set
            {
                if (SetField(ref _isSearching, value))
                {
                    OnPropertyChanged("SearchStateVisibility");
                    OnPropertyChanged("CanShowPosition");
                    OnPropertyChanged("CanRestoreOptimal");
                    OnPropertyChanged("NoResultVisibility");
                }
            }
        }
        public bool IsUpdating { get { return _isUpdating; } private set { SetField(ref _isUpdating, value); } }
        public Visibility SearchStateVisibility { get { return IsSearching ? Visibility.Visible : Visibility.Collapsed; } }
        public Visibility DataErrorVisibility { get { return IsDataLoaded ? Visibility.Collapsed : Visibility.Visible; } }
        public Visibility DataContentVisibility { get { return IsDataLoaded ? Visibility.Visible : Visibility.Collapsed; } }
        public bool IsPresetCategorySelected { get { return SelectedPrimaryCategory != null && SelectedPrimaryCategory.IsPresetCategory; } }
        public Visibility EffectCategoryVisibility { get { return IsPresetCategorySelected ? Visibility.Collapsed : Visibility.Visible; } }
        public Visibility PresetCategoryVisibility { get { return IsPresetCategorySelected ? Visibility.Visible : Visibility.Collapsed; } }
        public Visibility EffectListVisibility { get { return IsPresetCategorySelected ? Visibility.Collapsed : Visibility.Visible; } }
        public Visibility PresetListVisibility { get { return IsPresetCategorySelected ? Visibility.Visible : Visibility.Collapsed; } }
        public Visibility NoEffectsVisibility { get { return IsDataLoaded && (IsPresetCategorySelected ? VisiblePresets.Count : VisibleEffects.Count) == 0 ? Visibility.Visible : Visibility.Collapsed; } }
        public Visibility NoRulesVisibility { get { return Rules.Count == 0 ? Visibility.Visible : Visibility.Collapsed; } }
        public Visibility HasRulesVisibility { get { return Rules.Count == 0 ? Visibility.Collapsed : Visibility.Visible; } }
        public Visibility NoSaveResultVisibility { get { return HasSave ? Visibility.Collapsed : Visibility.Visible; } }
        public Visibility HasResultVisibility { get { return HasSave && Results.Count != 0 ? Visibility.Visible : Visibility.Collapsed; } }
        public Visibility NoResultVisibility { get { return HasSave && !IsSearching && Results.Count == 0 ? Visibility.Visible : Visibility.Collapsed; } }
        public string DataError { get { return _dataError; } private set { SetField(ref _dataError, value); OnPropertyChanged("DataErrorVisibility"); OnPropertyChanged("DataContentVisibility"); } }
        public string DataVersionLabel { get { return _catalog == null ? "数据不可用" : "Regulation " + _catalog.GameVersion + " · " + _application.Snapshot.PackVersion + (_application.Snapshot.IsBuiltIn ? " · 内置" : " · 外部"); } }
        public string ShortDataVersionLabel { get { return _catalog == null ? "不可用" : _catalog.GameVersion; } }
        public string ProgramVersionLabel
        {
            get
            {
                Version version = typeof(CustomEffectSearchViewModel).Assembly.GetName().Version;
                return "程序 " + version.Major + "." + version.Minor + "." + version.Build;
            }
        }
        public double UiScale { get { return ScaleLevels[_uiScaleIndex]; } }
        public int UiScaleIndex { get { return _uiScaleIndex; } }
        public bool CanZoomOut { get { return _uiScaleIndex > 0; } }
        public bool CanZoomIn { get { return _uiScaleIndex + 1 < ScaleLevels.Length; } }
        public string SavePlayerNameDisplay { get { return SelectedSaveSlot == null || string.IsNullOrWhiteSpace(SelectedSaveSlot.Inventory.PlayerName) ? "未导入存档" : SelectedSaveSlot.Inventory.PlayerName; } }
        public string SelectedSaveName { get { return string.IsNullOrWhiteSpace(_selectedSavePath) ? "未选择存档" : Path.GetFileName(_selectedSavePath); } }
        public string SelectedSavePath { get { return _selectedSavePath; } }
        public string SaveCheckStatus { get { return _saveCheckStatus; } }
        public string SaveCompactStatus { get { return SelectedSaveSlot == null ? "未导入存档" : "当前栏位遗物数量：" + SelectedSaveSlot.Inventory.Relics.Length; } }
        public string NotificationText { get { return _notificationText; } private set { SetField(ref _notificationText, value); } }
        public string StatusText { get { return _statusText; } private set { SetField(ref _statusText, value); NotificationText = string.Empty; } }
        private void ReportProblem(string message) { StatusText = message; NotificationText = message; }
        public bool CanShowPosition { get { return !_disposed && !IsReading && !IsSearching && !IsReplacing && HasSave && SelectedResult != null; } }
        public bool CanRestoreOptimal { get { return CanShowPosition && SelectedResult.IsManual; } }
        public bool CanRollbackDataPack { get { return _application.CanRollbackDataPack; } }
        public string PreviousDataPackDescription { get { return _application.PreviousDataPackDescription; } }
        public string VisibleEffectSummary
        {
            get
            {
                if (_catalog == null) return string.Empty;
                if (IsPresetCategorySelected)
                    return SelectedPresetGroup == null ? "全部预设 · " + VisiblePresets.Count + " 颗 · 单击即加入" : SelectedPresetGroup.Name + " · " + VisiblePresets.Count + " 颗";
                string scope;
                if (SelectedCategory != null) scope = SelectedCategory.Name;
                else if (SelectedPrimaryCategory != null && SelectedPrimaryCategory.Name != "全部")
                    scope = string.IsNullOrWhiteSpace(SearchText) ? SelectedPrimaryCategory.Name : SelectedPrimaryCategory.Name + "内搜索";
                else scope = string.IsNullOrWhiteSpace(SearchText) ? "全部词条" : "全库搜索";
                return scope + " · " + VisibleEffects.Count + " 项";
            }
        }

        public string SearchText
        {
            get { return _searchText; }
            set
            {
                string next = value ?? string.Empty;
                bool wasEmpty = string.IsNullOrWhiteSpace(_searchText);
                if (!SetField(ref _searchText, next)) return;
                if (wasEmpty && !string.IsNullOrWhiteSpace(next) && _selectedCategory != null)
                {
                    _selectedCategory = null;
                    OnPropertyChanged("SelectedCategory");
                    OnPropertyChanged("CategoryHeading");
                }
                RebuildVisibleEffects();
                RebuildVisiblePresets();
                ScheduleDeferredPersistence();
            }
        }

        public CustomEffectCharacterOption SelectedCharacter
        {
            get { return _selectedCharacter; }
            set
            {
                if (_disposed || IsReading || value == null || !Characters.Contains(value) || ReferenceEquals(_selectedCharacter, value)) return;
                CancelCalculations();
                if (!_suppressState) PersistCurrentCharacter();
                _selectedCharacter = value;
                OnPropertyChanged();
                RestoreCurrentCharacter();
            }
        }

        public CustomEffectSaveSlotOption SelectedSaveSlot
        {
            get { return _selectedSaveSlot; }
            set
            {
                if (_disposed || IsReading || value == null || !SaveSlots.Contains(value) || ReferenceEquals(_selectedSaveSlot, value)) return;
                CancelCalculations();
                if (!_suppressState) PersistCurrentCharacter();
                var sources = _application as ISaveSourceApplicationService;
                if (sources != null && _selectedSaveSource != null)
                {
                    try { sources.SelectSaveCharacterSlot(_selectedSaveSource.SourceKey, value.SlotIndex); }
                    catch (Exception error) { ReportProblem(FriendlyError(error, "本地状态保存失败")); ScheduleSearch(); return; }
                }
                _selectedSaveSlot = value;
                OnPropertyChanged();
                OnPropertyChanged("SavePlayerNameDisplay");
                OnPropertyChanged("HasSave");
                OnPropertyChanged("NoSaveResultVisibility");
                RestoreCurrentCharacter();
            }
        }

        public CustomEffectPrimaryCategoryOption SelectedPrimaryCategory
        {
            get { return _selectedPrimaryCategory; }
            set
            {
                if (value == null || ReferenceEquals(_selectedPrimaryCategory, value)) return;
                _selectedPrimaryCategory = value;
                if (!value.IsPresetCategory && _selectedPresetGroup != null)
                {
                    _selectedPresetGroup = null;
                    OnPropertyChanged("SelectedPresetGroup");
                }
                OnPropertyChanged();
                NotifyCategoryModeChanged();
                RebuildCategories(null);
                RebuildVisiblePresets();
                PersistCurrentCharacter();
            }
        }

        public CustomEffectCategoryOption SelectedCategory
        {
            get { return _selectedCategory; }
            set
            {
                if (ReferenceEquals(_selectedCategory, value)) return;
                _selectedCategory = value;
                if (value != null && _selectedPresetGroup != null)
                {
                    _selectedPresetGroup = null;
                    OnPropertyChanged("SelectedPresetGroup");
                }
                OnPropertyChanged();
                OnPropertyChanged("CategoryHeading");
                RebuildVisibleEffects();
                PersistCurrentCharacter();
            }
        }

        public CustomEffectPresetGroupOption SelectedPresetGroup
        {
            get { return _selectedPresetGroup; }
            set
            {
                if (ReferenceEquals(_selectedPresetGroup, value)) return;
                _selectedPresetGroup = value;
                if (value != null) _selectedCategory = null;
                OnPropertyChanged();
                RebuildVisiblePresets();
                PersistCurrentCharacter();
            }
        }

        public CustomEffectVesselResultViewModel SelectedResult
        {
            get { return _selectedResult; }
            set
            {
                if (_disposed || (value != null && !Results.Contains(value)) || ReferenceEquals(_selectedResult, value)) return;
                InvalidateReplacementCalculations();
                _selectedResult = value;
                OnPropertyChanged();
                OnPropertyChanged("CanRestoreOptimal");
                PersistCurrentCharacter();
            }
        }

        public string CategoryHeading
        {
            get
            {
                if (IsPresetCategorySelected) return "官方预设遗物";
                if (SelectedCategory != null) return SelectedCategory.Name;
                return SelectedPrimaryCategory == null || SelectedPrimaryCategory.Name == "全部"
                    ? "全部词条" : SelectedPrimaryCategory.Name;
            }
        }

        public Task InitializeAsync()
        {
            if (_initializationTask == null) _initializationTask = InitializeCoreAsync();
            return _initializationTask;
        }

        private async Task InitializeCoreAsync()
        {
            if (_disposed) return;
            var sources = _application as ISaveSourceApplicationService;
            if (sources != null)
            {
                await RunSaveOperationAsync(() =>
                {
                    sources.RestoreSaveSources();
                    return sources.DiscoverSaveSources().Snapshot;
                }, null, null);
            }
            else if (!string.IsNullOrWhiteSpace(_selectedSavePath) && _application.CurrentSave == null)
                await ImportSaveAsync(_selectedSavePath);
        }

        public Task ImportSaveAsync(string path)
        {
            if (_disposed || string.IsNullOrWhiteSpace(path)) return Task.FromResult(0);
            string fullPath;
            try { fullPath = Path.GetFullPath(path); }
            catch (Exception error)
            {
                ReportProblem(FriendlyError(error, "存档导入失败"));
                return Task.FromResult(0);
            }
            int? slot = string.Equals(_uiState.SelectedSavePath, fullPath, StringComparison.OrdinalIgnoreCase)
                ? _uiState.SelectedSaveSlotIndex : null;
            return RunSaveOperationAsync(() =>
            {
                _application.ImportSave(fullPath);
                var sources = _application as ISaveSourceApplicationService;
                return sources == null ? null : sources.SaveSources;
            }, fullPath, slot);
        }

        public Task SelectSaveSourceAsync(SaveSourceInfo source)
        {
            var sources = _application as ISaveSourceApplicationService;
            if (_disposed || sources == null || source == null || !SaveSources.Contains(source)) return Task.FromResult(0);
            return RunSaveOperationAsync(() => { sources.SelectSaveSource(source.SourceKey); return sources.SaveSources; },
                source.InternalSourcePath, source.SelectedCharacterSlotIndex);
        }

        public Task RefreshSavesAsync()
        {
            var sources = _application as ISaveSourceApplicationService;
            if (_disposed) return Task.FromResult(0);
            if (sources == null) return ImportSaveAsync(_selectedSavePath);
            return RunSaveOperationAsync(() =>
            {
                var cycle = sources.RefreshSelectedSaveSources();
                if (cycle.SelectedSourceRefresh != null && !cycle.SelectedSourceRefresh.Succeeded)
                    throw new SaveOperationFailure(cycle.Snapshot, cycle.SelectedSourceRefresh.ErrorMessage);
                return cycle.Snapshot;
            }, null, SelectedSaveSlot == null ? (int?)null : SelectedSaveSlot.SlotIndex);
        }

        // One gate covers every save mutation. Even when a newer request is queued, a
        // committed service session is reflected in the UI before the gate is released.
        // Thus a later failure cannot leave the service on A while the UI still shows B.
        private async Task RunSaveOperationAsync(Func<SaveServiceSnapshot> operation, string path, int? preferredSlot)
        {
            if (_disposed) return;
            PersistCurrentCharacter();
            long revision = Interlocked.Increment(ref _importRevision);
            CancelCalculations();
            IsReading = true;
            OnPropertyChanged("CanShowPosition");
            StatusText = "正在创建并解析只读副本…";
            bool entered = false;
            try
            {
                await _importGate.WaitAsync();
                entered = true;
                if (_disposed || revision != Interlocked.Read(ref _importRevision)) return;
                SaveServiceSnapshot snapshot = await Task.Run(operation);
                if (_disposed) return;
                ApplySaveSnapshot(snapshot);
                ApplySaveSession(_application.CurrentSave, path, preferredSlot);
            }
            catch (Exception error)
            {
                if (_disposed) return;
                var saveFailure = error as SaveOperationFailure;
                if (saveFailure != null) ApplySaveSnapshot(saveFailure.Snapshot);
                // The service keeps the last validated copy/session. Never call ClearSave
                // here or clear only part of the bound result and position state.
                if (!ReferenceEquals(_visibleSave, _application.CurrentSave))
                    ApplySaveSession(_application.CurrentSave, null, preferredSlot);
                ReportProblem(FriendlyError(error, "存档导入失败"));
            }
            finally
            {
                if (entered) _importGate.Release();
                if (!_disposed && revision == Interlocked.Read(ref _importRevision))
                {
                    IsReading = false;
                    NotifySaveChanged();
                    NotifyResultsChanged();
                    if (HasSave && Results.Count == 0) ScheduleSearch();
                }
            }
        }

        private sealed class SaveOperationFailure : Exception
        {
            public SaveServiceSnapshot Snapshot { get; private set; }
            public SaveOperationFailure(SaveServiceSnapshot snapshot, string reason) : base(reason) { Snapshot = snapshot; }
        }

        private void ApplySaveSnapshot(SaveServiceSnapshot snapshot)
        {
            if (snapshot == null) return;
            SaveSources.Clear();
            foreach (var source in snapshot.Sources) SaveSources.Add(source);
            var current = _application.CurrentSave;
            _selectedSaveSource = current == null || current.SourceKey == null ? snapshot.SelectedSource
                : SaveSources.FirstOrDefault(item => item.SourceKey.Equals(current.SourceKey));
            OnPropertyChanged("SelectedSaveSource");
        }

        private void ApplySaveSession(SaveSession session, string path, int? preferredSlot)
        {
            if (ReferenceEquals(_visibleSave, session))
            {
                _saveCheckStatus = session == null ? "未导入" : "已从只读副本导入";
                StatusText = session == null ? "正式数据已加载。" : "已从只读副本导入";
                return;
            }
            _visibleSave = session;
            Results.Clear();
            _selectedResult = null;
            _pendingBuildStates = null;
            SaveSlots.Clear();
            if (session != null)
                foreach (var inventory in session.Inventories) SaveSlots.Add(new CustomEffectSaveSlotOption(inventory, session.Inventories));
            int? selectedSlot = preferredSlot ?? (_selectedSaveSource == null ? (int?)null : _selectedSaveSource.SelectedCharacterSlotIndex);
            _selectedSaveSlot = SaveSlots.FirstOrDefault(item => selectedSlot == item.SlotIndex) ?? SaveSlots.FirstOrDefault();
            _selectedSavePath = _selectedSaveSource == null ? path ?? _selectedSavePath : _selectedSaveSource.InternalSourcePath;
            _saveCheckStatus = session == null ? "未导入" : "已从只读副本导入";
            _uiState.SelectedSavePath = _selectedSavePath;
            _uiState.SelectedSaveSlotIndex = _selectedSaveSlot == null ? (int?)null : _selectedSaveSlot.SlotIndex;
            NotifySaveChanged();
            NotifyResultsChanged();
            try { RestoreCurrentCharacter(); }
            catch (Exception error)
            {
                Rules.Clear();
                RenumberRules();
                ReportProblem(FriendlyError(error, "存档已导入，但本地状态恢复失败"));
            }
            TryPersistUiState();
        }

        public async Task ImportDataPackAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            PersistCurrentCharacter();
            CancelCalculations();
            IsUpdating = true;
            StatusText = "正在验证并切换更新包…";
            try
            {
                await Task.Run(() => _application.ImportDataPack(path));
                ReloadAfterDataPack();
                StatusText = "更新包已验证并完整切换。";
            }
            catch (Exception error) { StatusText = FriendlyError(error, "更新包导入失败，原数据继续可用"); }
            finally { IsUpdating = false; }
        }

        public async Task RollbackDataPackAsync()
        {
            PersistCurrentCharacter();
            CancelCalculations();
            IsUpdating = true;
            try
            {
                await Task.Run(() => _application.RollbackDataPack());
                ReloadAfterDataPack();
                StatusText = "已回退到上一份完整数据快照。";
            }
            catch (Exception error) { StatusText = FriendlyError(error, "数据回退失败"); }
            finally { IsUpdating = false; }
        }

        public void ToggleCategory(CustomEffectCategoryOption category) { if (category != null) SelectedCategory = ReferenceEquals(_selectedCategory, category) ? null : category; }
        public void TogglePresetGroup(CustomEffectPresetGroupOption group)
        {
            if (group == null) return;
            CustomEffectPrimaryCategoryOption preset = PrimaryCategories.FirstOrDefault(item => item.IsPresetCategory);
            if (preset != null && !ReferenceEquals(SelectedPrimaryCategory, preset)) SelectedPrimaryCategory = preset;
            SelectedPresetGroup = ReferenceEquals(_selectedPresetGroup, group) ? null : group;
        }
        public void ToggleRuleRequirement(CustomEffectRuleViewModel rule) { if (rule != null) rule.IsRequired = !rule.IsRequired; }

        public void AddEffect(CustomEffectEffectChoice choice)
        {
            if (_disposed || Rules.Count >= MaximumRules || choice == null || !AllEffectChoices.Contains(choice)
                || Rules.Any(item => item.SelectorId == choice.SelectorId)) return;
            Rules.Add(new CustomEffectRuleViewModel("selector:" + choice.SelectorId, choice, null, false, 1, RuleChanged));
            _usageCounts[choice.SelectorId] = Usage(choice.SelectorId) + 1;
            RulesChanged("已加入想要规则。");
        }

        public void AddPreset(CustomEffectPresetChoice choice)
        {
            if (_disposed || Rules.Count >= MaximumRules || choice == null || !choice.Preset.IsAvailableForRealMatching
                || Rules.Any(item => item.PresetItemId == choice.Preset.ItemId)) return;
            Rules.Add(new CustomEffectRuleViewModel("preset:" + choice.Preset.ItemId.ToString(CultureInfo.InvariantCulture),
                null, choice, false, 1, RuleChanged));
            RulesChanged("已加入预设遗物规则。");
        }

        public void DeleteRule(CustomEffectRuleViewModel rule)
        {
            if (rule != null && Rules.Remove(rule)) RulesChanged("已删除规则。");
        }

        public bool MoveRule(CustomEffectRuleViewModel rule, int delta)
        {
            int oldIndex = rule == null ? -1 : Rules.IndexOf(rule);
            int next = oldIndex + delta;
            if (oldIndex < 0 || next < 0 || next >= Rules.Count) return false;
            Rules.Move(oldIndex, next);
            RulesChanged("已调整规则优先顺序。");
            return true;
        }

        public void ClearRules() { Rules.Clear(); RulesChanged("已清空当前角色的规则。"); }

        public async Task<bool> StepSlotAsync(CustomEffectRelicSlotViewModel slot, int delta)
        {
            CustomEffectVesselResultViewModel result = SelectedResult;
            if (_disposed || IsReading || IsSearching || IsReplacing || slot == null || result == null || !result.Slots.Contains(slot) || !HasSave) return false;
            long revision = Interlocked.Increment(ref _replacementRevision);
            CancellationTokenSource cancellation = new CancellationTokenSource();
            CancellationTokenSource old = Interlocked.Exchange(ref _replacementCancellation, cancellation);
            if (old != null) { old.Cancel(); old.Dispose(); }
            CustomSearchBuild current = result.CurrentBuild;
            CustomEffectRule[] rules = BuildRules();
            int saveSlot = SelectedSaveSlot.SlotIndex;
            string characterId = SelectedCharacter.Id;
            string currentKey = BuildKey(current);
            IsReplacing = true;
            try
            {
                CustomSearchBuild[] ranking = await Task.Run(() => _application.RankReplacements(
                    saveSlot, characterId, rules, current, slot.AssignmentIndex, cancellation.Token), cancellation.Token);
                if (cancellation.IsCancellationRequested
                    || revision != Interlocked.Read(ref _replacementRevision)
                    || !ReferenceEquals(SelectedResult, result)
                    || !HasSave
                    || SelectedSaveSlot.SlotIndex != saveSlot
                    || SelectedCharacter.Id != characterId
                    || BuildKey(result.CurrentBuild) != currentKey)
                    return false;
                int index = Array.FindIndex(ranking, item => BuildKey(item) == BuildKey(current));
                int next = index + delta;
                if (index < 0 || next < 0 || next >= ranking.Length)
                {
                    slot.SetBounds(Math.Max(0, index), ranking.Length);
                    return false;
                }
                CustomSearchBuild replacement = ranking[next];
                ReplacementBounds[] bounds = await Task.Run(() => CalculateReplacementBounds(
                    saveSlot, characterId, rules, replacement, slot.AssignmentIndex, ranking, cancellation.Token),
                    cancellation.Token);
                if (cancellation.IsCancellationRequested
                    || revision != Interlocked.Read(ref _replacementRevision)
                    || !ReferenceEquals(SelectedResult, result)
                    || !HasSave
                    || SelectedSaveSlot.SlotIndex != saveSlot
                    || SelectedCharacter.Id != characterId
                    || BuildKey(result.CurrentBuild) != currentKey)
                    return false;
                ApplyBuild(result, replacement, BuildKey(replacement) != BuildKey(result.OptimalBuild));
                foreach (ReplacementBounds bound in bounds)
                    result.Slots.First(item => item.AssignmentIndex == bound.AssignmentIndex)
                        .SetBounds(bound.Index, bound.Count);
                StatusText = "已固定另外五颗并切换当前槽位。";
                OnPropertyChanged("CanRestoreOptimal");
                PersistCurrentCharacter();
                return true;
            }
            catch (OperationCanceledException)
            {
                if (!_disposed && revision == Interlocked.Read(ref _replacementRevision)) ReportProblem("单槽替换：操作已取消。");
                return false;
            }
            catch (Exception error)
            {
                if (!_disposed && revision == Interlocked.Read(ref _replacementRevision))
                    ReportProblem(FriendlyError(error, "单槽替换失败"));
                return false;
            }
            finally
            {
                if (!_disposed && revision == Interlocked.Read(ref _replacementRevision)) IsReplacing = false;
            }
        }

        private ReplacementBounds[] CalculateReplacementBounds(
            int saveSlot,
            string characterId,
            CustomEffectRule[] rules,
            CustomSearchBuild build,
            int reusableAssignmentIndex,
            CustomSearchBuild[] reusableRanking,
            CancellationToken cancellationToken)
        {
            ReplacementBounds[] result = new ReplacementBounds[build.Assignments.Length];
            string buildKey = BuildKey(build);
            for (int assignmentIndex = 0; assignmentIndex < build.Assignments.Length; assignmentIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CustomSearchBuild[] ranking = assignmentIndex == reusableAssignmentIndex
                    ? reusableRanking
                    : _application.RankReplacements(
                        saveSlot, characterId, rules, build, assignmentIndex, cancellationToken);
                int index = Array.FindIndex(ranking, item => BuildKey(item) == buildKey);
                if (index < 0)
                    throw new InvalidOperationException("完整替换列表缺少当前物理实例。");
                result[assignmentIndex] = new ReplacementBounds(assignmentIndex, index, ranking.Length);
            }
            return result;
        }

        public void RestoreOptimalSelected()
        {
            if (_disposed || IsReading || IsSearching || !HasSave || SelectedResult == null || !SelectedResult.IsManual) return;
            InvalidateReplacementCalculations();
            ApplyBuild(SelectedResult, SelectedResult.OptimalBuild, false);
            StatusText = "已恢复当前器皿的最优结果。";
            OnPropertyChanged("CanRestoreOptimal");
            PersistCurrentCharacter();
        }

        public bool AdjustUiScale(int delta)
        {
            int next = Math.Max(0, Math.Min(ScaleLevels.Length - 1, _uiScaleIndex + delta));
            if (next == _uiScaleIndex) return false;
            _uiScaleIndex = next;
            OnPropertyChanged("UiScale");
            OnPropertyChanged("UiScaleIndex");
            OnPropertyChanged("CanZoomOut");
            OnPropertyChanged("CanZoomIn");
            PersistCurrentCharacter();
            return true;
        }

        public void SaveCurrentState() { PersistCurrentCharacter(); }

        internal void ReportBoundaryFailure(string operation)
        {
            if (!_disposed) ReportProblem("操作未完成；当前会话仍可继续使用。");
        }

        private void LoadSnapshot(bool restoreUi, string preferredCharacterId = null)
        {
            _catalog = _application.Snapshot.Catalog;
            Characters.Clear();
            PrimaryCategories.Clear();
            Categories.Clear();
            AllEffectChoices.Clear();
            VisibleEffects.Clear();
            PresetGroups.Clear();
            VisiblePresets.Clear();
            foreach (CharacterDefinition character in _application.Snapshot.Model.Characters) Characters.Add(new CustomEffectCharacterOption(character));
            Dictionary<int, CustomEffectCategory> categories = _catalog.Categories.ToDictionary(item => item.Id);
            foreach (CustomEffectPrimaryCategory item in _catalog.PrimaryCategories.OrderBy(item => item.SortOrder))
                PrimaryCategories.Add(new CustomEffectPrimaryCategoryOption(item));
            PrimaryCategories.Add(new CustomEffectPrimaryCategoryOption("官方预设遗物",
                PrimaryCategories.Count == 0 ? 0 : PrimaryCategories.Max(item => item.SortOrder) + 1, true,
                _catalog.Presets.Count(item => item.IsAvailableForRealMatching)));
            foreach (CustomEffectSelector selector in _catalog.Selectors.OrderBy(item => categories[item.CategoryId].SortOrder)
                .ThenBy(item => item.SortOrder).ThenBy(item => item.Id, StringComparer.Ordinal))
                AllEffectChoices.Add(new CustomEffectEffectChoice(selector, categories[selector.CategoryId], _catalog));
            BuildPresetGroups();
            _selectedCharacter = Characters.FirstOrDefault(item => item.Id == preferredCharacterId)
                ?? Characters.FirstOrDefault(item => restoreUi && item.Id == _uiState.LastCharacterId)
                ?? Characters.FirstOrDefault();
            _selectedPrimaryCategory = PrimaryCategories.FirstOrDefault();
            OnPropertyChanged("SelectedCharacter");
            OnPropertyChanged("SelectedPrimaryCategory");
            OnPropertyChanged("DataVersionLabel");
            OnPropertyChanged("CanRollbackDataPack");
            RestoreCurrentCharacter();
        }

        private void DiscoverSave()
        {
            if (!string.IsNullOrWhiteSpace(_selectedSavePath)) return;
            try
            {
                _selectedSavePath = _application.FindLocalSaves().FirstOrDefault() ?? string.Empty;
                _saveCheckStatus = string.IsNullOrWhiteSpace(_selectedSavePath) ? "未导入" : "已发现，等待安全导入";
            }
            catch { _selectedSavePath = string.Empty; _saveCheckStatus = "未导入"; }
            NotifySaveChanged();
        }

        private void ReloadAfterDataPack()
        {
            _visibleSave = _application.CurrentSave;
            int previousSlot = _selectedSaveSlot == null ? -1 : _selectedSaveSlot.SlotIndex;
            string previousCharacter = _selectedCharacter == null ? null : _selectedCharacter.Id;
            SaveSlots.Clear();
            if (_application.CurrentSave != null)
                foreach (CharacterInventory inventory in _application.CurrentSave.Inventories) SaveSlots.Add(new CustomEffectSaveSlotOption(inventory, _application.CurrentSave.Inventories));
            _selectedSaveSlot = SaveSlots.FirstOrDefault(item => item.SlotIndex == previousSlot) ?? SaveSlots.FirstOrDefault();
            LoadSnapshot(false, previousCharacter);
            NotifySaveChanged();
        }

        private void RestoreCurrentCharacter()
        {
            if (_catalog == null || SelectedCharacter == null) return;
            Results.Clear();
            _selectedResult = null;
            _pendingBuildStates = null;
            _pendingSelectedResultIndex = 0;
            NotifyResultsChanged();
            _suppressState = true;
            int skipped = 0;
            try
            {
                CustomEffectCharacterUiState ui = HasSave ? null : _uiState.Characters.FirstOrDefault(item => item != null
                    && item.CharacterId == SelectedCharacter.Id);
                SavedCustomSearchState formal = HasSave ? _application.LoadState(SelectedSaveSlot.SlotIndex, SelectedCharacter.Id) : null;
                _usageCounts = formal != null && formal.UsageCounts != null
                    ? new Dictionary<string, int>(formal.UsageCounts, StringComparer.Ordinal)
                    : ui != null && ui.UsageCounts != null ? new Dictionary<string, int>(ui.UsageCounts, StringComparer.Ordinal)
                    : new Dictionary<string, int>(StringComparer.Ordinal);
                _searchText = formal != null ? formal.SearchText ?? string.Empty : ui == null ? string.Empty : ui.SearchText ?? string.Empty;
                string primary = formal != null ? formal.PrimaryCategory : ui == null ? null : ui.PrimaryCategory;
                _selectedPrimaryCategory = PrimaryCategories.FirstOrDefault(item => item.Name == primary) ?? PrimaryCategories.FirstOrDefault();
                int? categoryId = formal != null ? formal.SecondaryCategoryId : ui == null || !ui.SecondaryCategoryScopeExplicit ? null : ui.SecondaryCategoryId;
                string presetKey = formal != null ? formal.PresetGroup : ui == null ? null : ui.PresetGroup;
                CustomEffectPresetGroupOption restoredPresetGroup = PresetGroups.FirstOrDefault(item => item.Key == presetKey);
                bool presetGroupChanged = !ReferenceEquals(_selectedPresetGroup, restoredPresetGroup);
                _selectedPresetGroup = restoredPresetGroup;
                if (_selectedPresetGroup != null)
                    _selectedPrimaryCategory = PrimaryCategories.FirstOrDefault(item => item.IsPresetCategory) ?? _selectedPrimaryCategory;
                OnPropertyChanged("SearchText");
                OnPropertyChanged("SelectedPrimaryCategory");
                if (presetGroupChanged) OnPropertyChanged("SelectedPresetGroup");
                NotifyCategoryModeChanged();
                RebuildCategories(categoryId);
                RebuildVisiblePresets();
                Rules.Clear();
                IEnumerable<SavedCustomRule> savedRules = formal == null ? null : formal.Rules;
                if (savedRules != null)
                {
                    foreach (SavedCustomRule item in savedRules)
                    {
                        if (!AddRestoredRule(item.RuleId, item.SelectorId, item.PresetItemId, item.IsRequired, item.QuantityTarget)) skipped++;
                    }
                }
                else if (ui != null)
                {
                    foreach (CustomEffectRuleUiState item in ui.Rules ?? new List<CustomEffectRuleUiState>())
                        if (!AddRestoredRule(item.RuleId, item.SelectorId, item.PresetItemId, item.IsRequired, item.QuantityTarget)) skipped++;
                }
                RenumberRules();
                _pendingBuildStates = formal == null ? null : formal.BuildStates;
                _pendingSelectedResultIndex = formal == null ? ui == null ? 0 : ui.SelectedResultIndex : formal.SelectedResultIndex;
                if (!string.IsNullOrWhiteSpace(_application.StateWarning))
                    ReportProblem(_application.StateWarning);
                else if (formal != null && formal.DataChangedOnLoad)
                    StatusText = "数据版本已变化：已保留有效规则并清除旧实例结果。";
                else if (formal != null && formal.RepositoryChangedOnLoad)
                    StatusText = "仓库已变化：已保留有效规则并清除旧实例结果。";
                else if (skipped > 0) StatusText = "数据更新后有 " + skipped + " 条旧规则已安全跳过。";
            }
            finally { _suppressState = false; }
            RebuildVisibleEffects();
            ScheduleSearch();
            SaveCurrentCharacterToUi();
            TryPersistUiState();
        }

        private bool AddRestoredRule(string ruleId, string selectorId, int? presetItemId, bool required, int quantity)
        {
            if (Rules.Count >= MaximumRules) return true;
            CustomEffectEffectChoice effect = selectorId == null ? null : AllEffectChoices.FirstOrDefault(item => item.SelectorId == selectorId);
            CustomEffectPresetChoice preset = !presetItemId.HasValue ? null : FindPreset(presetItemId.Value);
            if (effect == null && preset == null) return false;
            string stable = !string.IsNullOrWhiteSpace(ruleId) ? ruleId : effect != null ? "selector:" + effect.SelectorId : "preset:" + preset.Preset.ItemId;
            if (Rules.Any(item => item.RuleId == stable || (effect != null && item.SelectorId == selectorId)
                || (preset != null && item.PresetItemId == presetItemId))) return false;
            Rules.Add(new CustomEffectRuleViewModel(stable, effect, preset, required, Math.Max(1, quantity), RuleChanged));
            return true;
        }

        private void BuildPresetGroups()
        {
            foreach (IGrouping<string, OfficialPresetRelic> group in _catalog.Presets
                .Where(item => item.IsAvailableForRealMatching)
                .GroupBy(item => item.Classification, StringComparer.Ordinal)
                .OrderBy(group => group.Min(item => item.SortOrder)).ThenBy(group => group.Key, StringComparer.Ordinal))
            {
                OfficialPresetRelic first = group.OrderBy(item => item.SortOrder).ThenBy(item => item.ItemId).First();
                PresetGroups.Add(new CustomEffectPresetGroupOption(group.Key, first.Group, group.Key, group.Count()));
            }
        }

        private void RebuildCategories(int? preferredId)
        {
            Categories.Clear();
            if (SelectedPrimaryCategory == null || SelectedPrimaryCategory.IsPresetCategory)
            {
                _selectedCategory = null;
                OnPropertyChanged("SelectedCategory");
                RebuildVisibleEffects();
                return;
            }
            foreach (CustomEffectCategory item in _catalog.Categories.Where(category => category.PrimaryCategory == SelectedPrimaryCategory.Name).OrderBy(category => category.SortOrder))
                Categories.Add(new CustomEffectCategoryOption(item, AllEffectChoices.Count(choice => choice.Category.Id == item.Id)));
            _selectedCategory = Categories.FirstOrDefault(item => preferredId.HasValue && item.Id == preferredId.Value);
            OnPropertyChanged("SelectedCategory");
            RebuildVisibleEffects();
        }

        private void LanguageChanged(object sender, EventArgs args)
        {
            // Display-only filtering. Do not persist, locate, rebuild results, cancel or schedule search.
            if (_disposed) return;
            RebuildVisibleEffects(); RebuildVisiblePresets();
        }

        private void RebuildVisibleEffects()
        {
            if (_catalog == null) return;
            IEnumerable<CustomEffectEffectChoice> query = AllEffectChoices;
            if (SelectedPrimaryCategory != null && !SelectedPrimaryCategory.IsPresetCategory
                && SelectedPrimaryCategory.Name != "全部")
                query = query.Where(item => item.Category.PrimaryCategory == SelectedPrimaryCategory.Name);
            if (SelectedCategory != null) query = query.Where(item => item.Category.Id == SelectedCategory.Id);
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                string term = SearchText.Trim();
                query = query.Where(item => L.G("selector", item.SelectorId, item.Name).IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0
                    || L.G("category", item.Category.Id, item.CategoryName).IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            VisibleEffects.Clear();
            foreach (CustomEffectEffectChoice item in query.OrderByDescending(item => Usage(item.SelectorId))
                .ThenBy(item => item.Category.SortOrder).ThenBy(item => item.Selector.SortOrder).ThenBy(item => item.SelectorId, StringComparer.Ordinal))
                VisibleEffects.Add(item);
            OnPropertyChanged("VisibleEffectSummary");
            OnPropertyChanged("NoEffectsVisibility");
        }

        private void RebuildVisiblePresets()
        {
            VisiblePresets.Clear();
            if (_catalog == null || !IsPresetCategorySelected) return;
            IEnumerable<CustomEffectPresetChoice> query = _catalog.Presets.Where(item => item.IsAvailableForRealMatching)
                .OrderBy(item => item.SortOrder).ThenBy(item => item.ItemId)
                .Select(item => new CustomEffectPresetChoice(item, _catalog));
            if (SelectedPresetGroup != null) query = query.Where(item => SelectedPresetGroup.Matches(item.Preset));
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                string term = SearchText.Trim();
                query = query.Where(item => (L.G("preset", item.Preset.ItemId, item.Name) + " " + string.Join(" ", item.Effects.Select(effect => L.G("effect", effect.RuntimeEffectId, effect.Name)))).IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            foreach (CustomEffectPresetChoice item in query) VisiblePresets.Add(item);
            OnPropertyChanged("VisibleEffectSummary");
            OnPropertyChanged("NoEffectsVisibility");
        }

        private void RulesChanged(string status)
        {
            RenumberRules();
            _pendingBuildStates = null;
            Results.Clear();
            _selectedResult = null;
            NotifyResultsChanged();
            ScheduleSearch();
            StatusText = status;
            PersistCurrentCharacter();
        }

        private void RuleChanged(CustomEffectRuleViewModel rule) { RulesChanged("已更新规则状态。"); }
        private void RenumberRules()
        {
            for (int index = 0; index < Rules.Count; index++) Rules[index].Priority = index + 1;
            OnPropertyChanged("NoRulesVisibility");
            OnPropertyChanged("HasRulesVisibility");
        }

        private void ScheduleSearch()
        {
            if (_disposed || IsReading) return;
            InvalidateReplacementCalculations();
            CancellationTokenSource cancellation = new CancellationTokenSource();
            CancellationTokenSource old = Interlocked.Exchange(ref _searchCancellation, cancellation);
            if (old != null) { old.Cancel(); old.Dispose(); }
            long revision = Interlocked.Increment(ref _searchRevision);
            if (!HasSave || SelectedCharacter == null)
            {
                Results.Clear();
                _selectedResult = null;
                IsSearching = false;
                NotifyResultsChanged();
                return;
            }
            IsSearching = true;
            _currentSearchTask = RecalculateAsync(revision, SelectedSaveSlot.SlotIndex, SelectedCharacter.Id,
                BuildRules(), cancellation.Token);
        }

        private async Task RecalculateAsync(long revision, int saveSlotIndex, string characterId,
            CustomEffectRule[] rules, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(SearchDebounceMilliseconds, cancellationToken);
                CustomSearchResponse response = await Task.Run(() => _application.Search(
                    saveSlotIndex, characterId, rules, cancellationToken), cancellationToken);
                if (_disposed || revision != Interlocked.Read(ref _searchRevision) || cancellationToken.IsCancellationRequested) return;
                LastSearchElapsedMilliseconds = response.ElapsedMilliseconds;
                ApplySearchResponse(response, rules, saveSlotIndex, characterId);
            }
            catch (OperationCanceledException)
            {
                if (!_disposed && revision == Interlocked.Read(ref _searchRevision)) ReportProblem("检索：操作已取消。");
            }
            catch (Exception error)
            {
                if (!_disposed && revision == Interlocked.Read(ref _searchRevision))
                {
                    Results.Clear();
                    _selectedResult = null;
                    ReportProblem(FriendlyError(error, "检索失败"));
                    NotifyResultsChanged();
                }
            }
            finally
            {
                if (!_disposed && revision == Interlocked.Read(ref _searchRevision)) IsSearching = false;
            }
        }

        private void ApplySearchResponse(CustomSearchResponse response, CustomEffectRule[] rules, int saveSlotIndex, string characterId)
        {
            Results.Clear();
            int index = 0;
            foreach (CustomSearchBuild build in response.IsExact
                ? response.Builds
                : new CustomSearchBuild[0])
            {
                CustomEffectVesselResultViewModel result = new CustomEffectVesselResultViewModel(index++, build);
                SavedCustomBuildState saved = _pendingBuildStates == null ? null : _pendingBuildStates.FirstOrDefault(item => item.VesselId == build.Vessel.Id);
                CustomSearchBuild current = saved == null || !saved.IsManual ? build : _application.RehydrateBuild(
                    saveSlotIndex, characterId, rules, saved.VesselId, saved.CurrentInstanceIds);
                bool restoredManual = current != null && saved != null && saved.IsManual;
                ApplyBuild(result, current ?? build, restoredManual, restoredManual ? saved : null);
                Results.Add(result);
            }
            _selectedResult = Results.FirstOrDefault(item => item.Index == _pendingSelectedResultIndex) ?? Results.FirstOrDefault();
            _pendingBuildStates = null;
            if (response.Status == CustomSearchStatus.BudgetExceeded)
                ReportProblem("检索超过安全预算，尚未证明最优；未显示不完整结果。");
            else if (response.Status == CustomSearchStatus.Canceled)
                ReportProblem("检索：操作已取消。");
            else if (response.Status == CustomSearchStatus.Failed)
                ReportProblem("检索失败，尚未证明最优；未显示不完整结果。");
            else if (Results.Count == 0) ReportProblem(response.Warnings.FirstOrDefault() ?? "当前仓库没有合法的六遗物组合。");
            else if (response.Warnings.Length != 0) ReportProblem(response.Warnings[0]);
            else if (Rules.Count == 0) StatusText = "未选择词条；已按正式模型的稳定默认顺序显示合法组合。";
            else StatusText = "已根据当前规则更新三个不同器皿。";
            NotifyResultsChanged();
            PersistCurrentCharacter();
        }

        private void ApplyBuild(CustomEffectVesselResultViewModel result, CustomSearchBuild build, bool manual,
            SavedCustomBuildState restoredState = null)
        {
            Dictionary<int, RelicLocation> locations = _application.Locate(SelectedSaveSlot.SlotIndex, build)
                .ToDictionary(item => item.Assignment.Relic.InstanceId);
            CustomEffectSlotColorViewModel[] colors = build.Assignments.Select(item => new CustomEffectSlotColorViewModel(
                item.SlotColorId, ColorName(item.SlotColorId), item.IsDeep ? "深夜槽" : "普通槽")).ToArray();
            HashSet<int> used = new HashSet<int>(build.Assignments.Select(item => item.Relic.InstanceId));
            CharacterInventory inventory = SelectedSaveSlot.Inventory;
            CustomEffectRelicSlotViewModel[] slots = build.Assignments.Select((assignment, assignmentIndex) =>
            {
                RelicLocation location;
                locations.TryGetValue(assignment.Relic.InstanceId, out location);
                int legal = inventory.Relics.Count(item => item.IsDeep == assignment.IsDeep
                    && (assignment.SlotColorId == 4 || item.ColorId == assignment.SlotColorId)
                    && (!used.Contains(item.InstanceId) || item.InstanceId == assignment.Relic.InstanceId));
                var candidate = MapCandidate(assignment.Relic, build.Warnings, location);
                CustomEffectRelicSlotViewModel slot = result.Slots.FirstOrDefault(item => item.AssignmentIndex == assignmentIndex
                    && item.IsDeep == assignment.IsDeep && item.SlotIndex == assignment.SlotIndex && item.SlotColorId == assignment.SlotColorId);
                if (slot == null) slot = new CustomEffectRelicSlotViewModel(assignmentIndex, assignment.IsDeep, assignment.SlotIndex,
                    assignment.SlotColorId, candidate, false, legal > 1);
                else slot.UpdateCurrent(candidate, legal);
                if (restoredState != null
                    && restoredState.CandidateIndexes != null && assignmentIndex < restoredState.CandidateIndexes.Length
                    && restoredState.CandidateCounts != null && assignmentIndex < restoredState.CandidateCounts.Length)
                {
                    int restoredIndex = restoredState.CandidateIndexes[assignmentIndex];
                    int restoredCount = restoredState.CandidateCounts[assignmentIndex];
                    if (restoredIndex >= 0 && restoredCount > restoredIndex) slot.SetBounds(restoredIndex, restoredCount);
                }
                return slot;
            }).ToArray();
            result.Apply(build, manual, colors, slots, BuildVesselTooltip(build));
        }

        private CustomEffectVesselTooltip BuildVesselTooltip(CustomSearchBuild build)
        {
            EffectiveContributionEvaluation evaluation = build == null ? null : build.ContributionEvaluation;
            if (evaluation == null) return CustomEffectVesselTooltip.Empty;
            var lines = new List<CustomEffectVesselTooltipRule>();
            foreach (var contribution in evaluation.Rules)
            {
                // Only confirmed stacking occurrences that survived the shared evaluator's
                // applicability, exclusivity and cap decisions are eligible for presentation.
                var effective = contribution.Occurrences.Where(item => item.IsEffective).ToArray();
                var stackable = effective.Where(item => item.AggregationRule == "cap_count"
                    && (_catalog.GetEffect(item.RuntimeEffectId) == null
                        || _catalog.GetEffect(item.RuntimeEffectId).MaxEffectiveCopies > 1)).ToArray();
                if (stackable.Length == 0) continue;
                var summary = (evaluation.NumericSummaries ?? new EffectiveContributionNumericSummary[0])
                    .FirstOrDefault(item => item.RuleId == contribution.RuleId);
                bool numeric = stackable.Length == effective.Length && summary != null
                    && summary.CanReliablyCombine && summary.TotalValue.HasValue;
                string formula = numeric ? FormatNumericFormula(summary.Operation, summary.EffectiveValues) : string.Empty;
                string total = numeric ? "合计：" + FormatNumericTotal(summary.Operation, summary.Unit, summary.TotalValue.Value, false)
                    : stackable.Length.ToString(CultureInfo.InvariantCulture) + " 条";
                lines.Add(new CustomEffectVesselTooltipRule(contribution.DisplayName,
                    BuildEffectiveTierCountLine(contribution), formula, total, string.Empty)
                    { SelectorId = contribution.SelectorId,
                      PresetItemId = Rules.FirstOrDefault(rule => rule.RuleId == contribution.RuleId)?.PresetItemId });
            }
            return new CustomEffectVesselTooltip(build.Vessel.Name, lines.ToArray()) { VesselId = build.Vessel.Id };
        }

        private string BuildEffectiveTierCountLine(EffectiveRuleContribution contribution)
        {
            if (contribution == null || string.IsNullOrWhiteSpace(contribution.SelectorId)) return string.Empty;
            CustomEffectSelector selector = _catalog.GetSelector(contribution.SelectorId);
            if (selector == null || selector.VariantPreferences == null
                || selector.VariantPreferences.Length == 0) return string.Empty;
            Dictionary<int, CustomEffectVariantPreference> variants = selector.VariantPreferences
                .GroupBy(item => item.RuntimeEffectId).ToDictionary(group => group.Key, group => group.First());
            return string.Join("，", contribution.Occurrences.Where(item => item.IsEffective)
                .Select(item =>
                {
                    CustomEffectVariantPreference value;
                    return variants.TryGetValue(item.RuntimeEffectId, out value) ? value : null;
                })
                .Where(item => item != null)
                .GroupBy(item => item.VariantPreferenceRank)
                .OrderByDescending(group => group.Key)
                .Select(group => group.First().TierLabel + " ×" + group.Count().ToString(CultureInfo.InvariantCulture)));
        }

        private static string FormatNumericFormula(string operation, decimal[] values)
        {
            decimal[] source = values ?? new decimal[0];
            if (source.Length == 0) return string.Empty;
            if (string.Equals(operation, "multiplier", StringComparison.Ordinal))
                return string.Join(" × ", source.Select(FormatTooltipDecimal));
            string formula = FormatTooltipDecimal(source[0]);
            for (int index = 1; index < source.Length; index++)
                formula += source[index] < 0
                    ? " − " + FormatTooltipDecimal(decimal.Negate(source[index]))
                    : " + " + FormatTooltipDecimal(source[index]);
            return formula;
        }

        private static string FormatNumericTotal(string operation, string unit, decimal total, bool includeApproximation)
        {
            string prefix = string.Equals(operation, "multiplier", StringComparison.Ordinal)
                ? "×" : total > 0 ? "+" : string.Empty;
            string result = prefix + FormatTooltipDecimal(total) + FormatVesselTooltipUnit(unit);
            if (includeApproximation && string.Equals(operation, "multiplier", StringComparison.Ordinal))
            {
                decimal rounded = CustomEffectNumericDisplayFormatter.RoundSignificant(total, 4);
                if (rounded != total)
                    result += "（约 ×" + FormatTooltipDecimal(rounded) + "）";
            }
            return result;
        }

        private static string FormatTooltipDecimal(decimal value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string FormatVesselTooltipUnit(string unit)
        {
            string value = (unit ?? string.Empty).Trim();
            if (value.Length == 0 || string.Equals(value, "multiplier", StringComparison.OrdinalIgnoreCase)) return string.Empty;
            if (string.Equals(value, "percent", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "percentage_points", StringComparison.OrdinalIgnoreCase)) return "%";
            if (string.Equals(value, "points", StringComparison.OrdinalIgnoreCase)) return " 点";
            if (string.Equals(value, "hp", StringComparison.OrdinalIgnoreCase)) return " HP";
            return value.IndexOf('_') >= 0 || value.IndexOf('/') >= 0 ? string.Empty : " " + value;
        }

        private CustomEffectRelicCandidate MapCandidate(RelicInstance relic, CustomBuildWarning[] warnings, RelicLocation location)
        {
            List<CustomEffectEffectLine> effects = new List<CustomEffectEffectLine>();
            foreach (int effectId in relic.PositiveEffectIds.Concat(relic.NegativeEffectIds))
            {
                CustomRuntimeEffect effect = _catalog.GetEffect(effectId);
                bool negative = relic.NegativeEffectIds.Contains(effectId) || (effect != null && effect.IsNegative);
                effects.Add(new CustomEffectEffectLine(effectId,
                    effect == null ? _application.Snapshot.Model.GetEffectName(effectId) : effect.Name,
                    CustomEffectPlayerValueFormatter.Format(effect), effect != null && effect.ValueConfirmed,
                    negative, effect == null ? string.Empty : effect.VerificationStatus)
                    {
                        IsApplicable = effect == null || SelectedCharacter == null || effect.IsApplicableTo(SelectedCharacter.Id),
                        IsCore = effect != null && !negative && effect.ApplicableCharacterIds.Length == 1
                    });
            }
            OfficialPresetRelic preset = _catalog.GetPreset(relic.ItemId);
            string warning = string.Join("；", (warnings ?? new CustomBuildWarning[0])
                .Where(item => item.InstanceId == relic.InstanceId).Select(item => item.Message).Distinct());
            string locationFilter = (relic.IsDeep ? "深夜" : "普通") + " · " + ColorName(relic.ColorId);
            string positionText = location != null && location.IsExactPositionAvailable
                ? "第" + location.Row + "行，第" + location.Column + "个"
                : location == null || string.IsNullOrWhiteSpace(location.UnavailableReason)
                    ? "当前版本精确位置不可用"
                    : location.UnavailableReason;
            return new CustomEffectRelicCandidate(relic.InstanceId, relic.ItemId, relic.ColorId, ColorName(relic.ColorId),
                preset == null ? string.Empty : preset.Name,
                preset == null ? string.Empty : preset.Group,
                preset == null ? string.Empty : preset.Classification,
                effects.ToArray(), !string.IsNullOrWhiteSpace(warning), warning, locationFilter, positionText);
        }

        private CustomEffectRule[] BuildRules()
        {
            return Rules.Take(MaximumRules).Select(item => item.IsPreset
                ? new CustomEffectRule(item.RuleId, item.PresetItemId.Value, item.IsRequired)
                : new CustomEffectRule(item.RuleId, item.SelectorId, item.IsRequired, item.QuantityTarget)).ToArray();
        }

        private void PersistCurrentCharacter()
        {
            if (_suppressState || IsReading || _catalog == null || SelectedCharacter == null) return;
            try
            {
                SaveCurrentCharacterToUi();
                PersistUiState();
                if (HasSave && !IsReading)
                {
                    SavedCustomSearchState state = new SavedCustomSearchState
                    {
                        CharacterId = SelectedCharacter.Id,
                        Rules = Rules.Select(item => new SavedCustomRule
                        {
                            RuleId = item.RuleId,
                            SelectorId = item.SelectorId,
                            PresetItemId = item.PresetItemId,
                            IsRequired = item.IsRequired,
                            QuantityTarget = item.QuantityTarget
                        }).ToList(),
                        UsageCounts = new Dictionary<string, int>(_usageCounts, StringComparer.Ordinal),
                        UiScaleIndex = _uiScaleIndex,
                        SearchText = SearchText ?? string.Empty,
                        PrimaryCategory = SelectedPrimaryCategory == null ? null : SelectedPrimaryCategory.Name,
                        SecondaryCategoryId = SelectedCategory == null ? (int?)null : SelectedCategory.Id,
                        PresetGroup = SelectedPresetGroup == null ? null : SelectedPresetGroup.Key,
                        SelectedResultIndex = SelectedResult == null ? 0 : SelectedResult.Index,
                        BuildStates = Results.Select(item => new SavedCustomBuildState
                        {
                            VesselId = item.VesselId,
                            OptimalInstanceIds = item.OptimalBuild.Assignments.Select(slot => slot.Relic.InstanceId).ToArray(),
                            CurrentInstanceIds = item.CurrentBuild.Assignments.Select(slot => slot.Relic.InstanceId).ToArray(),
                            CandidateIndexes = item.Slots.Select(slot => slot.CandidateIndex).ToArray(),
                            CandidateCounts = item.Slots.Select(slot => slot.CandidateCount).ToArray(),
                            IsManual = item.IsManual
                        }).ToList()
                    };
                    _application.SaveState(SelectedSaveSlot.SlotIndex, state);
                }
            }
            catch (Exception) { ReportProblem("本地状态保存失败；当前会话仍可继续使用。"); }
        }

        private void SaveCurrentCharacterToUi()
        {
            _uiState.LastCharacterId = SelectedCharacter.Id;
            _uiState.UiScaleIndex = _uiScaleIndex;
            _uiState.SelectedSavePath = _selectedSavePath;
            if (SelectedSaveSlot != null) _uiState.SelectedSaveSlotIndex = SelectedSaveSlot.SlotIndex;
            if (HasSave) return;

            CustomEffectCharacterUiState item = _uiState.Characters.FirstOrDefault(state => state != null
                && state.CharacterId == SelectedCharacter.Id);
            if (item == null) { item = new CustomEffectCharacterUiState { CharacterId = SelectedCharacter.Id }; _uiState.Characters.Add(item); }
            item.SearchText = SearchText ?? string.Empty;
            item.PrimaryCategory = SelectedPrimaryCategory == null ? null : SelectedPrimaryCategory.Name;
            item.SecondaryCategoryId = SelectedCategory == null ? (int?)null : SelectedCategory.Id;
            item.SecondaryCategoryScopeExplicit = SelectedCategory != null;
            item.PresetGroup = SelectedPresetGroup == null ? null : SelectedPresetGroup.Key;
            item.SelectedResultIndex = SelectedResult == null ? 0 : SelectedResult.Index;
            item.Rules = Rules.Select(rule => new CustomEffectRuleUiState
            {
                RuleId = rule.RuleId,
                SelectorId = rule.SelectorId,
                PresetItemId = rule.PresetItemId,
                IsRequired = rule.IsRequired,
                QuantityTarget = rule.QuantityTarget
            }).ToList();
            item.UsageCounts = new Dictionary<string, int>(_usageCounts, StringComparer.Ordinal);
        }

        private void PersistUiState() { _uiStateService.Save(_uiState); }
        private bool TryPersistUiState()
        {
            try { PersistUiState(); return true; }
            catch (Exception)
            {
                ReportProblem("本地界面状态保存失败；当前会话仍可继续使用。");
                return false;
            }
        }
        private void ScheduleDeferredPersistence()
        {
            if (_suppressState || _disposed) return;
            CancellationTokenSource cancellation = new CancellationTokenSource();
            CancellationTokenSource old = Interlocked.Exchange(ref _persistenceCancellation, cancellation);
            if (old != null) { old.Cancel(); old.Dispose(); }
            _persistenceTask = PersistAfterDelayAsync(cancellation);
        }
        private async Task PersistAfterDelayAsync(CancellationTokenSource cancellation)
        {
            try
            {
                await Task.Delay(PersistenceDebounceMilliseconds, cancellation.Token).ConfigureAwait(false);
                if (cancellation.IsCancellationRequested || _disposed
                    || !ReferenceEquals(cancellation, _persistenceCancellation)) return;
                Action persist = () =>
                {
                    if (!cancellation.IsCancellationRequested && !_disposed
                        && ReferenceEquals(cancellation, _persistenceCancellation)) PersistCurrentCharacter();
                };
                if (_uiContext == null) persist();
                else _uiContext.Post(state => persist(), null);
            }
            catch (OperationCanceledException) { }
        }
        private CustomEffectPresetChoice FindPreset(int itemId)
        {
            OfficialPresetRelic preset = _catalog.GetPreset(itemId);
            return preset == null || !preset.IsAvailableForRealMatching ? null : new CustomEffectPresetChoice(preset, _catalog);
        }
        private int Usage(string id) { int value; return id != null && _usageCounts.TryGetValue(id, out value) ? value : 0; }
        private string ColorName(int colorId)
        {
            switch (colorId) { case 0: return "红色"; case 1: return "蓝色"; case 2: return "黄色"; case 3: return "绿色"; case 4: return "白色"; default: return "未知颜色"; }
        }
        private static string BuildKey(CustomSearchBuild build)
        {
            return build == null ? string.Empty : build.Vessel.Id + ":" + string.Join(",", build.Assignments.Select(item => item.Relic.InstanceId));
        }
        private static string FriendlyError(Exception error, string prefix)
        {
            if (error is OperationCanceledException)
                return prefix + "：操作已取消。";
            // Classify controlled failure categories; never display raw exception text,
            // which may contain a source path or an account directory from the OS.
            var chain = new List<Exception>();
            for (Exception current = error; current != null; current = current.InnerException) chain.Add(current);
            string reason = string.Join(" ", chain.Select(item => item.Message ?? string.Empty));
            if (chain.Any(item => item is UnauthorizedAccessException) || reason.Contains("权限")
                || reason.Contains("创建存档副本") || reason.Contains("创建安全") || reason.Contains("原子保存") || reason.Contains("原子发布"))
                return "无法写入软件目录。请将程序解压到可写文件夹，并检查磁盘空间与访问权限。";
            if (reason.Contains("找不到") || reason.Contains("原始存档当前不可用") || chain.Any(item => item is FileNotFoundException))
                return "找不到所选存档。请确认文件仍在原位置或重新选择。";
            if (reason.Contains("只支持 .sl2") || reason.Contains("缺少 BND4"))
                return "存档格式不受支持。请选择 Nightreign PC 的 .sl2 或 .co2 文件。";
            if (reason.Contains("文件过小") || reason.Contains("截断") || reason.Contains("MD5") || reason.Contains("损坏")
                || reason.Contains("解密") || reason.Contains("校验") || reason.Contains("BND4"))
                return "存档未通过完整性检查。文件可能损坏或不完整；已保留上次有效副本。";
            if (reason.Contains("1.03.x") || reason.Contains("存档布局"))
                return "该存档版本或布局不受支持。当前数据适用于 Regulation 1.03.5。";
            if (reason.Contains("存档发生变化") || reason.Contains("读取期间"))
                return "复制期间存档发生变化。请稍后刷新重试；已保留上次有效副本。";
            return prefix + "。请检查文件、版本与访问权限后重试。";
        }
        private void NotifyCategoryModeChanged()
        {
            OnPropertyChanged("IsPresetCategorySelected");
            OnPropertyChanged("EffectCategoryVisibility");
            OnPropertyChanged("PresetCategoryVisibility");
            OnPropertyChanged("EffectListVisibility");
            OnPropertyChanged("PresetListVisibility");
            OnPropertyChanged("CategoryHeading");
        }
        private void NotifySaveChanged()
        {
            OnPropertyChanged("SelectedSaveName");
            OnPropertyChanged("SaveCheckStatus");
            OnPropertyChanged("SaveCompactStatus");
            OnPropertyChanged("SavePlayerNameDisplay");
            OnPropertyChanged("SelectedSaveSlot");
            OnPropertyChanged("HasSave");
            OnPropertyChanged("NoSaveResultVisibility");
            OnPropertyChanged("HasResultVisibility");
            OnPropertyChanged("NoResultVisibility");
        }
        private void NotifyResultsChanged()
        {
            OnPropertyChanged("SelectedResult");
            OnPropertyChanged("CanShowPosition");
            OnPropertyChanged("CanRestoreOptimal");
            OnPropertyChanged("HasResultVisibility");
            OnPropertyChanged("NoSaveResultVisibility");
            OnPropertyChanged("NoResultVisibility");
        }
        private void CancelCalculations()
        {
            Interlocked.Increment(ref _searchRevision);
            CancellationTokenSource search = Interlocked.Exchange(ref _searchCancellation, null);
            if (search != null) { search.Cancel(); search.Dispose(); }
            InvalidateReplacementCalculations();
            IsSearching = false;
        }
        private void InvalidateReplacementCalculations()
        {
            Interlocked.Increment(ref _replacementRevision);
            CancellationTokenSource replacement = Interlocked.Exchange(ref _replacementCancellation, null);
            if (replacement != null) { replacement.Cancel(); replacement.Dispose(); }
            IsReplacing = false;
        }

        private sealed class ReplacementBounds
        {
            public ReplacementBounds(int assignmentIndex, int index, int count)
            {
                AssignmentIndex = assignmentIndex;
                Index = index;
                Count = count;
            }

            public int AssignmentIndex { get; private set; }
            public int Index { get; private set; }
            public int Count { get; private set; }
        }

        public void Dispose()
        {
            LocalizationService.Current.Changed -= LanguageChanged;
            if (_disposed) return;
            _disposed = true;
            Interlocked.Increment(ref _importRevision);
            CancellationTokenSource persistence = Interlocked.Exchange(ref _persistenceCancellation, null);
            if (persistence != null) { persistence.Cancel(); persistence.Dispose(); }
            CancelCalculations();
            PersistCurrentCharacter();
        }
    }
}
