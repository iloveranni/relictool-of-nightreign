using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace NightreignRelicTool.Core
{
    public sealed class CustomEffectDataException : Exception
    {
        public CustomEffectDataException(string message) : base(message) { }
        public CustomEffectDataException(string message, Exception inner) : base(message, inner) { }
    }

    public sealed class CustomEffectPrimaryCategory
    {
        public CustomEffectPrimaryCategory(string name, int sortOrder)
        {
            Name = name;
            SortOrder = sortOrder;
        }

        public string Name { get; private set; }
        public int SortOrder { get; private set; }
    }

    public sealed class CustomEffectCategory
    {
        public CustomEffectCategory(int id, string primaryCategory, string name, int sortOrder)
        {
            Id = id;
            PrimaryCategory = primaryCategory;
            Name = name;
            SortOrder = sortOrder;
        }

        public int Id { get; private set; }
        public string PrimaryCategory { get; private set; }
        public string Name { get; private set; }
        public int SortOrder { get; private set; }
    }

    public sealed class CustomEffectVariantPreference
    {
        public CustomEffectVariantPreference(
            int runtimeEffectId,
            int? tier,
            string tierLabel,
            int variantPreferenceRank,
            string verificationStatus,
            string source,
            string gameVersion)
        {
            RuntimeEffectId = runtimeEffectId;
            Tier = tier;
            TierLabel = tierLabel;
            VariantPreferenceRank = variantPreferenceRank;
            VerificationStatus = verificationStatus;
            Source = source;
            GameVersion = gameVersion;
        }

        public int RuntimeEffectId { get; private set; }
        public int? Tier { get; private set; }
        public string TierLabel { get; private set; }
        public int VariantPreferenceRank { get; private set; }
        public string VerificationStatus { get; private set; }
        public string Source { get; private set; }
        public string GameVersion { get; private set; }
    }

    public sealed class CustomEffectSelector
    {
        private readonly Dictionary<int, int> _variantRanks;

        public CustomEffectSelector(
            string id,
            int? filterParamId,
            int categoryId,
            string name,
            int sortOrder,
            int[] runtimeEffectIds,
            bool available,
            bool officialFilter,
            IDictionary<int, int> variantRanks = null,
            CustomEffectVariantPreference[] variantPreferences = null)
        {
            Id = id;
            FilterParamId = filterParamId;
            CategoryId = categoryId;
            Name = name;
            SortOrder = sortOrder;
            RuntimeEffectIds = runtimeEffectIds ?? new int[0];
            IsAvailableForRealMatching = available;
            IsOfficialFilter = officialFilter;
            _variantRanks = variantRanks == null
                ? new Dictionary<int, int>()
                : new Dictionary<int, int>(variantRanks);
            VariantPreferences = variantPreferences ?? new CustomEffectVariantPreference[0];
        }

        public string Id { get; private set; }
        public int? FilterParamId { get; private set; }
        public int CategoryId { get; private set; }
        public string Name { get; private set; }
        public int SortOrder { get; private set; }
        public int[] RuntimeEffectIds { get; private set; }
        public bool IsAvailableForRealMatching { get; private set; }
        public bool IsOfficialFilter { get; private set; }
        public CustomEffectVariantPreference[] VariantPreferences { get; private set; }
        internal IEnumerable<int> VariantRankEffectIds { get { return _variantRanks.Keys; } }

        public int GetVariantPreferenceRank(int runtimeEffectId)
        {
            int value;
            return _variantRanks.TryGetValue(runtimeEffectId, out value) ? value : 1;
        }

        public CustomEffectVariantPreference GetVariantPreference(int runtimeEffectId)
        {
            return VariantPreferences.FirstOrDefault(item => item.RuntimeEffectId == runtimeEffectId);
        }
    }

    public sealed class CustomEffectCalculationTerm
    {
        public CustomEffectCalculationTerm(
            string mechanicKey,
            string operation,
            decimal? numericValue,
            decimal? neutralValue,
            string unit,
            string targetAttribute,
            string verificationStatus,
            string source,
            string gameVersion)
        {
            MechanicKey = mechanicKey;
            Operation = operation;
            NumericValue = numericValue;
            NeutralValue = neutralValue;
            Unit = unit;
            TargetAttribute = targetAttribute;
            VerificationStatus = verificationStatus;
            Source = source;
            GameVersion = gameVersion;
        }

        public string MechanicKey { get; private set; }
        public string Operation { get; private set; }
        public decimal? NumericValue { get; private set; }
        public decimal? NeutralValue { get; private set; }
        public string Unit { get; private set; }
        public string TargetAttribute { get; private set; }
        public string VerificationStatus { get; private set; }
        public string Source { get; private set; }
        public string GameVersion { get; private set; }
    }

    public sealed class CustomRuntimeEffect
    {
        public CustomRuntimeEffect(
            int runtimeEffectId,
            string name,
            string displayValue,
            bool valueConfirmed,
            bool isNegative,
            string[] applicableCharacterIds,
            bool applicabilityConfirmed,
            int? exclusivityId,
            string stackGroupKey,
            string aggregationRule,
            int? maxEffectiveCopies,
            string aggregationVerificationStatus,
            string aggregationSource,
            string aggregationVersion,
            string verificationStatus,
            int uncertaintyCount,
            CustomEffectCalculationTerm[] calculationTerms = null)
        {
            RuntimeEffectId = runtimeEffectId;
            Name = name;
            DisplayValue = displayValue;
            ValueConfirmed = valueConfirmed;
            IsNegative = isNegative;
            ApplicableCharacterIds = applicableCharacterIds ?? new string[0];
            ApplicabilityConfirmed = applicabilityConfirmed;
            ExclusivityId = exclusivityId;
            StackGroupKey = stackGroupKey;
            AggregationRule = aggregationRule;
            MaxEffectiveCopies = maxEffectiveCopies;
            AggregationVerificationStatus = aggregationVerificationStatus;
            AggregationSource = aggregationSource;
            AggregationVersion = aggregationVersion;
            VerificationStatus = verificationStatus;
            UncertaintyCount = Math.Max(0, uncertaintyCount);
            CalculationTerms = calculationTerms ?? new CustomEffectCalculationTerm[0];
        }

        public int RuntimeEffectId { get; private set; }
        public string Name { get; private set; }
        public string DisplayValue { get; private set; }
        public bool ValueConfirmed { get; private set; }
        public bool IsNegative { get; private set; }
        public string[] ApplicableCharacterIds { get; private set; }
        public bool ApplicabilityConfirmed { get; private set; }
        public int? ExclusivityId { get; private set; }
        public string StackGroupKey { get; private set; }
        public string AggregationRule { get; private set; }
        public int? MaxEffectiveCopies { get; private set; }
        public string AggregationVerificationStatus { get; private set; }
        public string AggregationSource { get; private set; }
        public string AggregationVersion { get; private set; }
        public string VerificationStatus { get; private set; }
        public int UncertaintyCount { get; private set; }
        public CustomEffectCalculationTerm[] CalculationTerms { get; private set; }

        public bool IsApplicableTo(string characterId)
        {
            return !ApplicabilityConfirmed
                || ApplicableCharacterIds.Any(item => string.Equals(item, characterId, StringComparison.OrdinalIgnoreCase));
        }
    }

    public sealed class OfficialPresetRelic
    {
        public OfficialPresetRelic(
            int itemId,
            string name,
            string group,
            string classification,
            int sortOrder,
            int[] fixedRuntimeEffectIds,
            int[] negativeRuntimeEffectIds,
            string verificationStatus,
            string acquisitionEvidenceStatus,
            bool available)
        {
            ItemId = itemId;
            Name = name;
            Group = group;
            Classification = classification;
            SortOrder = sortOrder;
            FixedRuntimeEffectIds = fixedRuntimeEffectIds ?? new int[0];
            NegativeRuntimeEffectIds = negativeRuntimeEffectIds ?? new int[0];
            VerificationStatus = verificationStatus;
            AcquisitionEvidenceStatus = acquisitionEvidenceStatus;
            IsAvailableForRealMatching = available;
        }

        public int ItemId { get; private set; }
        public string Name { get; private set; }
        public string Group { get; private set; }
        public string Classification { get; private set; }
        public int SortOrder { get; private set; }
        public int[] FixedRuntimeEffectIds { get; private set; }
        public int[] NegativeRuntimeEffectIds { get; private set; }
        public string VerificationStatus { get; private set; }
        public string AcquisitionEvidenceStatus { get; private set; }
        public bool IsAvailableForRealMatching { get; private set; }
    }

    public sealed class CustomEffectBinding
    {
        public CustomEffectBinding(string knowledgeKey, string selectorId, int[] runtimeEffectIds, string bindingStatus, string verificationStatus)
        {
            KnowledgeKey = knowledgeKey;
            SelectorId = selectorId;
            RuntimeEffectIds = runtimeEffectIds ?? new int[0];
            BindingStatus = bindingStatus;
            VerificationStatus = verificationStatus;
        }

        public string KnowledgeKey { get; private set; }
        public string SelectorId { get; private set; }
        public int[] RuntimeEffectIds { get; private set; }
        public string BindingStatus { get; private set; }
        public string VerificationStatus { get; private set; }
    }

    public sealed class CustomExclusivityPolicy
    {
        public CustomExclusivityPolicy(int id, int maxEffectiveEffects, bool hard, string warning, int[] runtimeEffectIds)
        {
            Id = id;
            MaxEffectiveEffects = maxEffectiveEffects;
            IsHardLegalityConstraint = hard;
            Warning = warning;
            RuntimeEffectIds = runtimeEffectIds ?? new int[0];
        }

        public int Id { get; private set; }
        public int MaxEffectiveEffects { get; private set; }
        public bool IsHardLegalityConstraint { get; private set; }
        public string Warning { get; private set; }
        public int[] RuntimeEffectIds { get; private set; }
    }

    public sealed class CustomEffectCatalog
    {
        private const string ResourceName = "NightreignRelicTool.Resources.CustomEffectSearch.gz";
        private readonly Dictionary<string, CustomEffectSelector> _selectors;
        private readonly Dictionary<int, CustomRuntimeEffect> _effects;
        private readonly Dictionary<int, OfficialPresetRelic> _presets;
        private readonly Dictionary<int, CustomExclusivityPolicy> _exclusivity;
        private byte[] _sourceCompressed;

        private CustomEffectCatalog(CustomEffectRuntimeDto source)
        {
            if (source == null) throw new CustomEffectDataException("自定义词条数据为空。");
            if (!string.Equals(source.SchemaVersion, "nightreign.custom-effect-search.runtime.v2", StringComparison.Ordinal))
                throw new CustomEffectDataException("不支持的自定义词条 schemaVersion：" + source.SchemaVersion);
            if (!string.Equals(source.GameVersion, "1.03.5", StringComparison.Ordinal))
                throw new CustomEffectDataException("自定义词条数据仅支持 Regulation 1.03.5，实际为：" + source.GameVersion);
            if (source.Counts == null || source.PrimaryCategories == null || source.Categories == null
                || source.Selectors == null || source.Bindings == null || source.Effects == null
                || source.Presets == null || source.ExclusivityPolicies == null || source.WarningMessages == null)
                throw new CustomEffectDataException("自定义词条数据缺少必需字段。");

            SchemaVersion = source.SchemaVersion;
            GameVersion = source.GameVersion;
            DataVersion = source.DataVersion;
            PrimaryCategories = source.PrimaryCategories.Select(item =>
                new CustomEffectPrimaryCategory(Required(item.Name, "一级分类名称"), item.SortOrder)).ToArray();
            Categories = source.Categories.Select(item =>
                new CustomEffectCategory(item.SubcategoryParamId, Required(item.PrimaryCategory, "一级分类引用"),
                    Required(item.Name, "二级分类名称"), item.SortOrder)).ToArray();
            Effects = source.Effects.Select(item => new CustomRuntimeEffect(
                item.RuntimeEffectId, Required(item.Name, "Runtime Effect 名称"),
                Required(item.DisplayValue, "Runtime Effect 显示数值"), item.ValueConfirmed, item.IsNegative,
                Array(item.ApplicableCharacterIds), item.ApplicabilityConfirmed, item.ExclusivityId,
                Required(item.StackGroupKey, "Runtime Effect 聚合组"), Required(item.AggregationRule, "Runtime Effect 聚合规则"),
                item.MaxEffectiveCopies, Required(item.AggregationVerificationStatus, "Runtime Effect 聚合验证状态"),
                Required(item.AggregationSource, "Runtime Effect 聚合来源"), Required(item.AggregationVersion, "Runtime Effect 聚合版本"),
                item.VerificationStatus, item.UncertaintyCount,
                (item.CalculationTerms ?? new List<CustomCalculationTermDto>()).Select(term =>
                    new CustomEffectCalculationTerm(
                        Required(term.MechanicKey, "计算项机械键"), Required(term.Operation, "计算项运算"),
                        term.NumericValue, term.NeutralValue, Required(term.Unit, "计算项单位"),
                        Required(term.TargetAttribute, "计算项目标属性"),
                        Required(term.VerificationStatus, "计算项验证状态"), Required(term.Source, "计算项来源"),
                        Required(term.GameVersion, "计算项游戏版本"))).ToArray())).ToArray();
            _effects = Unique(Effects, item => item.RuntimeEffectId, "Runtime Effect ID");
            Selectors = source.Selectors.Select(item => new CustomEffectSelector(
                Required(item.Id, "选择项 ID"), item.FilterParamId, item.SubcategoryParamId,
                Required(item.Name, "选择项名称"), item.SortOrder, Array(item.RuntimeEffectIds),
                item.IsAvailableForRealMatching, item.IsOfficialFilter, ParseRanks(item.VariantPreferenceRanks),
                (item.VariantPreferences ?? new List<CustomVariantPreferenceDto>()).Select(preference =>
                    new CustomEffectVariantPreference(
                        preference.RuntimeEffectId, preference.Tier, Required(preference.TierLabel, "档位标签"),
                        preference.VariantPreferenceRank, Required(preference.VerificationStatus, "档位验证状态"),
                        Required(preference.Source, "档位来源"), Required(preference.GameVersion, "档位游戏版本")))
                    .ToArray())).ToArray();
            _selectors = Unique(Selectors, item => item.Id, "选择项 ID", StringComparer.Ordinal);
            Bindings = source.Bindings.Select(item => new CustomEffectBinding(
                Required(item.KnowledgeKey, "知识键"), Required(item.SelectorId, "选择项引用"),
                Array(item.RuntimeEffectIds), Required(item.BindingStatus, "绑定状态"), item.VerificationStatus)).ToArray();
            Presets = source.Presets.Select(item => new OfficialPresetRelic(
                item.ItemId, Required(item.Name, "预设遗物名称"), Required(item.Group, "预设遗物分组"),
                Required(item.Classification, "预设遗物正式分类"), item.SortOrder,
                Array(item.FixedRuntimeEffectIds), Array(item.NegativeRuntimeEffectIds),
                item.VerificationStatus, item.AcquisitionEvidenceStatus, item.IsAvailableForRealMatching)).ToArray();
            _presets = Unique(Presets, item => item.ItemId, "预设遗物 ItemId");
            ExclusivityPolicies = source.ExclusivityPolicies.Select(item => new CustomExclusivityPolicy(
                item.ExclusivityId, item.MaxEffectiveEffects, item.IsHardLegalityConstraint,
                Required(item.Warning, "互斥警告"), Array(item.RuntimeEffectIds))).ToArray();
            _exclusivity = Unique(ExclusivityPolicies, item => item.Id, "互斥组 ID");
            WarningMessages = new Dictionary<string, string>(source.WarningMessages, StringComparer.Ordinal);
            Validate(source.Counts);
        }

        public string SchemaVersion { get; private set; }
        public string GameVersion { get; private set; }
        public string DataVersion { get; private set; }
        public CustomEffectPrimaryCategory[] PrimaryCategories { get; private set; }
        public CustomEffectCategory[] Categories { get; private set; }
        public CustomEffectSelector[] Selectors { get; private set; }
        public CustomEffectBinding[] Bindings { get; private set; }
        public CustomRuntimeEffect[] Effects { get; private set; }
        public OfficialPresetRelic[] Presets { get; private set; }
        public CustomExclusivityPolicy[] ExclusivityPolicies { get; private set; }
        public Dictionary<string, string> WarningMessages { get; private set; }

        public static CustomEffectCatalog LoadBuiltIn()
        {
            Assembly assembly = typeof(CustomEffectCatalog).Assembly;
            using (Stream stream = assembly.GetManifestResourceStream(ResourceName))
            {
                if (stream == null) throw new CustomEffectDataException("找不到内置自定义词条资源：" + ResourceName);
                using (MemoryStream copy = new MemoryStream())
                {
                    stream.CopyTo(copy);
                    return LoadCompressed(copy.ToArray());
                }
            }
        }

        internal static CustomEffectCatalog LoadCompressed(byte[] compressed)
        {
            const int maximumJsonBytes = 16 * 1024 * 1024;
            try
            {
                if (compressed == null) throw new ArgumentNullException("compressed");
                byte[] compressedCopy = (byte[])compressed.Clone();
                using (MemoryStream input = new MemoryStream(compressedCopy, false))
                using (GZipStream gzip = new GZipStream(input, CompressionMode.Decompress))
                using (MemoryStream json = new MemoryStream())
                {
                    byte[] buffer = new byte[64 * 1024];
                    int read;
                    while ((read = gzip.Read(buffer, 0, buffer.Length)) != 0)
                    {
                        if (json.Length + read > maximumJsonBytes)
                            throw new CustomEffectDataException("自定义词条资源解压后的 JSON 超过 16 MB 限制。");
                        json.Write(buffer, 0, read);
                    }
                    json.Position = 0;
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(
                        typeof(CustomEffectRuntimeDto),
                        new DataContractJsonSerializerSettings
                        {
                            MaxItemsInObjectGraph = int.MaxValue,
                            UseSimpleDictionaryFormat = true
                        });
                    CustomEffectCatalog result = new CustomEffectCatalog((CustomEffectRuntimeDto)serializer.ReadObject(json));
                    result._sourceCompressed = compressedCopy;
                    return result;
                }
            }
            catch (CustomEffectDataException) { throw; }
            catch (Exception error)
            {
                throw new CustomEffectDataException("自定义词条资源无法解析：" + error.Message, error);
            }
        }

        internal CustomEffectCatalog CreateDefensiveCopy()
        {
            if (_sourceCompressed == null)
                throw new InvalidOperationException("自定义词条目录缺少可复制的已验证来源。");
            return LoadCompressed(_sourceCompressed);
        }

        public CustomEffectSelector GetSelector(string id)
        {
            CustomEffectSelector value;
            return id != null && _selectors.TryGetValue(id, out value) ? value : null;
        }

        public CustomRuntimeEffect GetEffect(int runtimeEffectId)
        {
            CustomRuntimeEffect value;
            return _effects.TryGetValue(runtimeEffectId, out value) ? value : null;
        }

        public OfficialPresetRelic GetPreset(int itemId)
        {
            OfficialPresetRelic value;
            return _presets.TryGetValue(itemId, out value) ? value : null;
        }

        public CustomExclusivityPolicy GetExclusivityPolicy(int id)
        {
            CustomExclusivityPolicy value;
            return _exclusivity.TryGetValue(id, out value) ? value : null;
        }

        public string Warning(string key)
        {
            string value;
            return WarningMessages.TryGetValue(key, out value) ? value : key;
        }

        private void Validate(CustomEffectCountsDto counts)
        {
            CheckCount(counts.PrimaryCategories, PrimaryCategories.Length, "一级分类");
            CheckCount(counts.Categories, Categories.Length, "二级分类");
            CheckCount(counts.OfficialFilters, Selectors.Count(item => item.IsOfficialFilter), "官方筛选项");
            CheckCount(counts.IncludedEffectBindings, Bindings.Length, "已通过词条");
            CheckCount(counts.OfficialPresetRelics, Presets.Length, "官方预设遗物");
            CheckCount(counts.MechanicalRuntimeEffects, Effects.Length, "机械 Runtime Effect");
            if (PrimaryCategories.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count() != PrimaryCategories.Length)
                throw new CustomEffectDataException("一级分类名称包含重复项。");
            if (Categories.Select(item => item.Id).Distinct().Count() != Categories.Length)
                throw new CustomEffectDataException("二级分类 ID 包含重复项。");
            HashSet<string> primary = new HashSet<string>(PrimaryCategories.Select(item => item.Name), StringComparer.Ordinal);
            foreach (CustomEffectCategory category in Categories)
                if (!primary.Contains(category.PrimaryCategory))
                    throw new CustomEffectDataException("二级分类 " + category.Id + " 引用了不存在的一级分类：" + category.PrimaryCategory);
            HashSet<int> categories = new HashSet<int>(Categories.Select(item => item.Id));
            foreach (CustomEffectSelector selector in Selectors)
            {
                if (!categories.Contains(selector.CategoryId))
                    throw new CustomEffectDataException("选择项 " + selector.Id + " 引用了不存在的二级分类：" + selector.CategoryId);
                if (selector.RuntimeEffectIds.Distinct().Count() != selector.RuntimeEffectIds.Length)
                    throw new CustomEffectDataException("选择项 " + selector.Id + " 包含重复 Runtime Effect 引用。");
                foreach (int effectId in selector.RuntimeEffectIds)
                    if (!_effects.ContainsKey(effectId))
                        throw new CustomEffectDataException("选择项 " + selector.Id + " 引用了不存在的 Runtime Effect：" + effectId);
                foreach (int effectId in selector.VariantRankEffectIds)
                    if (!selector.RuntimeEffectIds.Contains(effectId))
                        throw new CustomEffectDataException("选择项 " + selector.Id + " 的 variantPreferenceRanks 引用了未绑定的 Runtime Effect：" + effectId);
                if (selector.VariantPreferences.Select(item => item.RuntimeEffectId).Distinct().Count()
                    != selector.VariantPreferences.Length)
                    throw new CustomEffectDataException("选择项 " + selector.Id + " 包含重复档位元数据。");
                foreach (CustomEffectVariantPreference preference in selector.VariantPreferences)
                {
                    if (!selector.RuntimeEffectIds.Contains(preference.RuntimeEffectId))
                        throw new CustomEffectDataException("选择项 " + selector.Id + " 的档位元数据引用了未绑定的 Runtime Effect：" + preference.RuntimeEffectId);
                    if (!preference.Tier.HasValue || preference.Tier.Value < 0
                        || preference.VariantPreferenceRank != preference.Tier.Value + 1)
                        throw new CustomEffectDataException("选择项 " + selector.Id + " 的档位与偏好 rank 不一致：" + preference.RuntimeEffectId);
                    if (selector.GetVariantPreferenceRank(preference.RuntimeEffectId) != preference.VariantPreferenceRank)
                        throw new CustomEffectDataException("选择项 " + selector.Id + " 的档位元数据与 rank 映射不一致：" + preference.RuntimeEffectId);
                    if (!string.Equals(preference.GameVersion, GameVersion, StringComparison.Ordinal))
                        throw new CustomEffectDataException("选择项 " + selector.Id + " 的档位游戏版本不一致：" + preference.RuntimeEffectId);
                }
                if (selector.IsAvailableForRealMatching != (selector.RuntimeEffectIds.Length != 0))
                    throw new CustomEffectDataException("选择项 " + selector.Id + " 的可匹配状态与 Runtime Effect 绑定不一致。");
            }
            foreach (CustomEffectBinding binding in Bindings)
            {
                if (!_selectors.ContainsKey(binding.SelectorId))
                    throw new CustomEffectDataException("词条 " + binding.KnowledgeKey + " 引用了不存在的选择项：" + binding.SelectorId);
                if (binding.RuntimeEffectIds.Length == 0 || !string.Equals(binding.BindingStatus, "bound", StringComparison.Ordinal))
                    throw new CustomEffectDataException("进入选择器的词条仍未绑定 Runtime Effect：" + binding.KnowledgeKey);
                if (binding.RuntimeEffectIds.Distinct().Count() != binding.RuntimeEffectIds.Length)
                    throw new CustomEffectDataException("词条 " + binding.KnowledgeKey + " 包含重复 Runtime Effect 引用。");
                CustomEffectSelector selector = GetSelector(binding.SelectorId);
                foreach (int effectId in binding.RuntimeEffectIds)
                    if (!_effects.ContainsKey(effectId) || !selector.RuntimeEffectIds.Contains(effectId))
                        throw new CustomEffectDataException("词条 " + binding.KnowledgeKey + " 的 Runtime Effect 与选择项不一致：" + effectId);
            }
            foreach (OfficialPresetRelic preset in Presets)
            {
                if (preset.FixedRuntimeEffectIds.Distinct().Count() != preset.FixedRuntimeEffectIds.Length
                    || preset.NegativeRuntimeEffectIds.Distinct().Count() != preset.NegativeRuntimeEffectIds.Length)
                    throw new CustomEffectDataException("预设遗物 " + preset.ItemId + " 包含重复 Runtime Effect 引用。");
                foreach (int effectId in preset.FixedRuntimeEffectIds.Concat(preset.NegativeRuntimeEffectIds))
                    if (!_effects.ContainsKey(effectId))
                        throw new CustomEffectDataException("预设遗物 " + preset.ItemId + " 引用了不存在的 Runtime Effect：" + effectId);
            }
            foreach (CustomExclusivityPolicy policy in ExclusivityPolicies)
            {
                if (policy.MaxEffectiveEffects != 1 || policy.IsHardLegalityConstraint)
                    throw new CustomEffectDataException("互斥组 " + policy.Id + " 使用了当前搜索器不支持的规则。");
                if (policy.RuntimeEffectIds.Distinct().Count() != policy.RuntimeEffectIds.Length)
                    throw new CustomEffectDataException("互斥组 " + policy.Id + " 包含重复 Runtime Effect 引用。");
                foreach (int effectId in policy.RuntimeEffectIds)
                {
                    CustomRuntimeEffect effect = GetEffect(effectId);
                    if (effect == null || effect.ExclusivityId != policy.Id)
                        throw new CustomEffectDataException("互斥组 " + policy.Id + " 的 Runtime Effect 引用不一致：" + effectId);
                }
            }
            foreach (CustomRuntimeEffect effect in Effects.Where(item => item.ExclusivityId.HasValue))
            {
                CustomExclusivityPolicy policy = GetExclusivityPolicy(effect.ExclusivityId.Value);
                if (policy == null || !policy.RuntimeEffectIds.Contains(effect.RuntimeEffectId))
                    throw new CustomEffectDataException("Runtime Effect " + effect.RuntimeEffectId + " 的互斥组反向引用不一致。");
            }
            foreach (CustomRuntimeEffect effect in Effects)
            {
                if (!string.Equals(effect.AggregationVersion, GameVersion, StringComparison.Ordinal))
                    throw new CustomEffectDataException("Runtime Effect " + effect.RuntimeEffectId + " 的聚合版本不一致。");
                if (string.Equals(effect.AggregationRule, "single_instance", StringComparison.Ordinal))
                {
                    if (effect.MaxEffectiveCopies != 1)
                        throw new CustomEffectDataException("Runtime Effect " + effect.RuntimeEffectId + " 的 single_instance 上限必须为 1。");
                }
                else if (string.Equals(effect.AggregationRule, "cap_count", StringComparison.Ordinal))
                {
                    if (!effect.MaxEffectiveCopies.HasValue || effect.MaxEffectiveCopies.Value < 1
                        || effect.MaxEffectiveCopies.Value > 6)
                        throw new CustomEffectDataException("Runtime Effect " + effect.RuntimeEffectId + " 的 cap_count 上限无效。");
                }
                else if (!string.Equals(effect.AggregationRule, "unknown", StringComparison.Ordinal))
                    throw new CustomEffectDataException("Runtime Effect " + effect.RuntimeEffectId + " 的聚合规则不受支持：" + effect.AggregationRule);

                foreach (CustomEffectCalculationTerm term in effect.CalculationTerms)
                {
                    if (!string.Equals(term.GameVersion, GameVersion, StringComparison.Ordinal))
                        throw new CustomEffectDataException("Runtime Effect " + effect.RuntimeEffectId + " 的计算项版本不一致。");
                    bool multiplier = string.Equals(term.Operation, "multiplier", StringComparison.Ordinal);
                    bool additive = string.Equals(term.Operation, "additive", StringComparison.Ordinal);
                    bool flat = string.Equals(term.Operation, "flat", StringComparison.Ordinal);
                    bool nonNumeric = string.Equals(term.Operation, "non_numeric", StringComparison.Ordinal);
                    bool unknown = string.Equals(term.Operation, "unknown", StringComparison.Ordinal);
                    if (!multiplier && !additive && !flat && !nonNumeric && !unknown)
                        throw new CustomEffectDataException("Runtime Effect " + effect.RuntimeEffectId + " 的计算项运算不受支持：" + term.Operation);
                    if (multiplier)
                    {
                        if (!term.NumericValue.HasValue || !term.NeutralValue.HasValue || term.NeutralValue.Value != 1m)
                            throw new CustomEffectDataException("Runtime Effect " + effect.RuntimeEffectId + " 的乘算项必须有数值且中性值为 1。");
                    }
                    else if (additive || flat)
                    {
                        if (!term.NumericValue.HasValue || !term.NeutralValue.HasValue || term.NeutralValue.Value != 0m)
                            throw new CustomEffectDataException("Runtime Effect " + effect.RuntimeEffectId + " 的加算或固定值项必须有数值且中性值为 0。");
                    }
                    else if (nonNumeric && term.NumericValue.HasValue)
                        throw new CustomEffectDataException("Runtime Effect " + effect.RuntimeEffectId + " 的非数值计算项不得携带数值。");
                }
            }
            foreach (IGrouping<string, CustomRuntimeEffect> group in Effects
                .GroupBy(item => item.StackGroupKey, StringComparer.Ordinal))
            {
                if (group.Select(item => item.AggregationRule).Distinct(StringComparer.Ordinal).Count() != 1
                    || group.Select(item => item.MaxEffectiveCopies).Distinct().Count() != 1
                    || group.Select(item => item.AggregationVerificationStatus)
                        .Distinct(StringComparer.Ordinal).Count() != 1
                    || group.Select(item => item.AggregationVersion).Distinct(StringComparer.Ordinal).Count() != 1)
                    throw new CustomEffectDataException("StackGroup " + group.Key + " 的聚合声明不一致。");
            }
        }

        private static string Required(string value, string label)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new CustomEffectDataException(label + "为空。");
            return value.Trim();
        }

        private static int[] Array(List<int> values) { return values == null ? new int[0] : values.ToArray(); }
        private static string[] Array(List<string> values) { return values == null ? new string[0] : values.ToArray(); }

        private static Dictionary<int, int> ParseRanks(Dictionary<string, int> values)
        {
            Dictionary<int, int> result = new Dictionary<int, int>();
            foreach (KeyValuePair<string, int> item in values ?? new Dictionary<string, int>())
            {
                int id;
                if (!int.TryParse(item.Key, out id) || item.Value < 1)
                    throw new CustomEffectDataException("variantPreferenceRanks 含有无效项：" + item.Key);
                result.Add(id, item.Value);
            }
            return result;
        }

        private static Dictionary<TKey, TValue> Unique<TKey, TValue>(
            IEnumerable<TValue> values,
            Func<TValue, TKey> key,
            string label,
            IEqualityComparer<TKey> comparer = null)
        {
            Dictionary<TKey, TValue> result = new Dictionary<TKey, TValue>(comparer ?? EqualityComparer<TKey>.Default);
            foreach (TValue value in values)
            {
                TKey itemKey = key(value);
                if (result.ContainsKey(itemKey)) throw new CustomEffectDataException(label + " 重复：" + itemKey);
                result.Add(itemKey, value);
            }
            return result;
        }

        private static void CheckCount(int expected, int actual, string label)
        {
            if (expected != actual)
                throw new CustomEffectDataException(label + "数量不一致：声明 " + expected + "，实际 " + actual + "。");
        }
    }

#pragma warning disable 0649
    [DataContract]
    internal sealed class CustomEffectRuntimeDto
    {
        [DataMember(Name = "schemaVersion")] public string SchemaVersion;
        [DataMember(Name = "gameVersion")] public string GameVersion;
        [DataMember(Name = "dataVersion")] public string DataVersion;
        [DataMember(Name = "counts")] public CustomEffectCountsDto Counts;
        [DataMember(Name = "primaryCategories")] public List<CustomPrimaryDto> PrimaryCategories;
        [DataMember(Name = "categories")] public List<CustomCategoryDto> Categories;
        [DataMember(Name = "selectors")] public List<CustomSelectorDto> Selectors;
        [DataMember(Name = "bindings")] public List<CustomBindingDto> Bindings;
        [DataMember(Name = "effects")] public List<CustomRuntimeEffectDto> Effects;
        [DataMember(Name = "presets")] public List<CustomPresetDto> Presets;
        [DataMember(Name = "exclusivityPolicies")] public List<CustomExclusivityDto> ExclusivityPolicies;
        [DataMember(Name = "warningMessages")] public Dictionary<string, string> WarningMessages;
    }

    [DataContract] internal sealed class CustomEffectCountsDto
    {
        [DataMember(Name = "primaryCategories")] public int PrimaryCategories;
        [DataMember(Name = "categories")] public int Categories;
        [DataMember(Name = "officialFilters")] public int OfficialFilters;
        [DataMember(Name = "includedEffectBindings")] public int IncludedEffectBindings;
        [DataMember(Name = "officialPresetRelics")] public int OfficialPresetRelics;
        [DataMember(Name = "mechanicalRuntimeEffects")] public int MechanicalRuntimeEffects;
    }
    [DataContract] internal sealed class CustomPrimaryDto
    {
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "sortOrder")] public int SortOrder;
    }
    [DataContract] internal sealed class CustomCategoryDto
    {
        [DataMember(Name = "subcategoryParamId")] public int SubcategoryParamId;
        [DataMember(Name = "primaryCategory")] public string PrimaryCategory;
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "sortOrder")] public int SortOrder;
    }
    [DataContract] internal sealed class CustomSelectorDto
    {
        [DataMember(Name = "id")] public string Id;
        [DataMember(Name = "filterParamId")] public int? FilterParamId;
        [DataMember(Name = "subcategoryParamId")] public int SubcategoryParamId;
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "sortOrder")] public int SortOrder;
        [DataMember(Name = "runtimeEffectIds")] public List<int> RuntimeEffectIds;
        [DataMember(Name = "isAvailableForRealMatching")] public bool IsAvailableForRealMatching;
        [DataMember(Name = "isOfficialFilter")] public bool IsOfficialFilter;
        [DataMember(Name = "variantPreferenceRanks")] public Dictionary<string, int> VariantPreferenceRanks;
        [DataMember(Name = "variantPreferences", EmitDefaultValue = false)] public List<CustomVariantPreferenceDto> VariantPreferences;
    }
    [DataContract] internal sealed class CustomVariantPreferenceDto
    {
        [DataMember(Name = "runtimeEffectId")] public int RuntimeEffectId;
        [DataMember(Name = "tier")] public int? Tier;
        [DataMember(Name = "tierLabel")] public string TierLabel;
        [DataMember(Name = "variantPreferenceRank")] public int VariantPreferenceRank;
        [DataMember(Name = "verificationStatus")] public string VerificationStatus;
        [DataMember(Name = "source")] public string Source;
        [DataMember(Name = "gameVersion")] public string GameVersion;
    }
    [DataContract] internal sealed class CustomBindingDto
    {
        [DataMember(Name = "knowledgeKey")] public string KnowledgeKey;
        [DataMember(Name = "selectorId")] public string SelectorId;
        [DataMember(Name = "runtimeEffectIds")] public List<int> RuntimeEffectIds;
        [DataMember(Name = "bindingStatus")] public string BindingStatus;
        [DataMember(Name = "verificationStatus")] public string VerificationStatus;
    }
    [DataContract] internal sealed class CustomRuntimeEffectDto
    {
        [DataMember(Name = "runtimeEffectId")] public int RuntimeEffectId;
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "displayValue")] public string DisplayValue;
        [DataMember(Name = "valueConfirmed")] public bool ValueConfirmed;
        [DataMember(Name = "isNegative")] public bool IsNegative;
        [DataMember(Name = "applicableCharacterIds")] public List<string> ApplicableCharacterIds;
        [DataMember(Name = "applicabilityConfirmed")] public bool ApplicabilityConfirmed;
        [DataMember(Name = "exclusivityId")] public int? ExclusivityId;
        [DataMember(Name = "stackGroupKey")] public string StackGroupKey;
        [DataMember(Name = "aggregationRule")] public string AggregationRule;
        [DataMember(Name = "maxEffectiveCopies")] public int? MaxEffectiveCopies;
        [DataMember(Name = "aggregationVerificationStatus")] public string AggregationVerificationStatus;
        [DataMember(Name = "aggregationSource")] public string AggregationSource;
        [DataMember(Name = "aggregationVersion")] public string AggregationVersion;
        [DataMember(Name = "verificationStatus")] public string VerificationStatus;
        [DataMember(Name = "uncertaintyCount")] public int UncertaintyCount;
        [DataMember(Name = "calculationTerms", EmitDefaultValue = false)] public List<CustomCalculationTermDto> CalculationTerms;
    }
    [DataContract] internal sealed class CustomCalculationTermDto
    {
        [DataMember(Name = "mechanicKey")] public string MechanicKey;
        [DataMember(Name = "operation")] public string Operation;
        [DataMember(Name = "numericValue")] public decimal? NumericValue;
        [DataMember(Name = "neutralValue")] public decimal? NeutralValue;
        [DataMember(Name = "unit")] public string Unit;
        [DataMember(Name = "targetAttribute")] public string TargetAttribute;
        [DataMember(Name = "verificationStatus")] public string VerificationStatus;
        [DataMember(Name = "source")] public string Source;
        [DataMember(Name = "gameVersion")] public string GameVersion;
    }
    [DataContract] internal sealed class CustomPresetDto
    {
        [DataMember(Name = "itemId")] public int ItemId;
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "group")] public string Group;
        [DataMember(Name = "classification")] public string Classification;
        [DataMember(Name = "sortOrder")] public int SortOrder;
        [DataMember(Name = "fixedRuntimeEffectIds")] public List<int> FixedRuntimeEffectIds;
        [DataMember(Name = "negativeRuntimeEffectIds")] public List<int> NegativeRuntimeEffectIds;
        [DataMember(Name = "verificationStatus")] public string VerificationStatus;
        [DataMember(Name = "acquisitionEvidenceStatus")] public string AcquisitionEvidenceStatus;
        [DataMember(Name = "isAvailableForRealMatching")] public bool IsAvailableForRealMatching;
    }
    [DataContract] internal sealed class CustomExclusivityDto
    {
        [DataMember(Name = "exclusivityId")] public int ExclusivityId;
        [DataMember(Name = "maxEffectiveEffects")] public int MaxEffectiveEffects;
        [DataMember(Name = "isHardLegalityConstraint")] public bool IsHardLegalityConstraint;
        [DataMember(Name = "warning")] public string Warning;
        [DataMember(Name = "runtimeEffectIds")] public List<int> RuntimeEffectIds;
    }
#pragma warning restore 0649
}
