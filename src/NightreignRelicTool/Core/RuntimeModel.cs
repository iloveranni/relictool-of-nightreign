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
    internal struct FocusMask
    {
        public ulong A;
        public ulong B;
        public ulong C;

        public static FocusMask operator |(FocusMask left, FocusMask right)
        {
            return new FocusMask { A = left.A | right.A, B = left.B | right.B, C = left.C | right.C };
        }

        public bool Intersects(FocusMask other)
        {
            return (A & other.A) != 0 || (B & other.B) != 0 || (C & other.C) != 0;
        }
    }

    public sealed class RuntimeModel
    {
        private const string ModelResourceName = "NightreignRelicTool.Resources.ScoringModel.gz";
        private const string CatalogResourceName = "NightreignRelicTool.Resources.RelicCatalog.gz";

        private readonly Dictionary<string, CharacterDefinition> _charactersById;
        private readonly Dictionary<string, ArchetypeDefinition> _archetypesById;
        private readonly Dictionary<int, EffectDefinition> _effectsById;
        private readonly Dictionary<int, RelicTypeDefinition> _relicTypes;
        private readonly Dictionary<int, VesselDefinition> _vesselsById;
        private readonly Dictionary<string, int> _focusIndices;
        private byte[] _sourceScoringModel;
        private byte[] _sourceRelicCatalog;

        private RuntimeModel(ScoringModelDto source, CompactCatalogDto catalog)
        {
            if (source == null || source.GameVersion == null || source.GameVersion.Regulation != "1.03.5")
                throw new InvalidDataException("内置评分模型不是受支持的 Regulation 1.03.5。 ");
            if (source.Characters == null || source.Archetypes == null || source.EffectFamilies == null
                || source.EffectIdIndex == null || source.NegativeRules == null || source.VesselCatalog == null)
                throw new InvalidDataException("内置评分模型缺少运行所需的数据段。");

            ModelVersion = source.ModelVersion;
            RegulationVersion = source.GameVersion.Regulation;
            IsDirectScoreModel = false;
            _focusIndices = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (FocusDto focus in source.FocusCatalog ?? new List<FocusDto>())
            {
                if (!_focusIndices.ContainsKey(focus.Id))
                    _focusIndices.Add(focus.Id, _focusIndices.Count);
            }
            if (_focusIndices.Count > 192)
                throw new InvalidDataException("评分模型的关注维度超过当前位掩码容量。");

            Dictionary<string, EffectFamilyDefinition> families = source.EffectFamilies.ToDictionary(
                item => item.Id,
                item => new EffectFamilyDefinition(
                    item.Id,
                    item.CanonicalName,
                    MakeFocusMask(item.PositiveFocusIds),
                    item.CharacterIds ?? new List<string>()),
                StringComparer.Ordinal);

            Effects = new EffectDefinition[source.EffectIdIndex.Count];
            _effectsById = new Dictionary<int, EffectDefinition>(source.EffectIdIndex.Count);
            for (int index = 0; index < source.EffectIdIndex.Count; index++)
            {
                EffectIndexDto item = source.EffectIdIndex[index];
                EffectFamilyDefinition family;
                if (!families.TryGetValue(item.FamilyId, out family))
                    throw new InvalidDataException("评分模型词条引用了不存在的效果家族：" + item.FamilyId);
                EffectDefinition effect = new EffectDefinition(
                    index,
                    item.EffectId,
                    family,
                    item.PotencyFactor,
                    item.TriggerReliability,
                    item.RuntimeScoreFactor,
                    item.VerificationConfidence,
                    item.StackPolicy,
                    Math.Max(1, item.MaxUsefulCopies));
                if (_effectsById.ContainsKey(effect.EffectId))
                    throw new InvalidDataException("评分模型包含重复词条 ID：" + effect.EffectId);
                Effects[index] = effect;
                _effectsById.Add(effect.EffectId, effect);
            }

            _charactersById = source.Characters.ToDictionary(
                item => item.Id,
                item => new CharacterDefinition(
                    item.Id,
                    item.Name,
                    item.Risk ?? new Dictionary<string, int>(),
                    (item.EligibleVesselIds ?? new List<int>()).ToArray()),
                StringComparer.Ordinal);

            _archetypesById = new Dictionary<string, ArchetypeDefinition>(StringComparer.Ordinal);
            foreach (ArchetypeDto item in source.Archetypes)
            {
                if (!_charactersById.ContainsKey(item.CharacterId))
                    throw new InvalidDataException("流派引用了不存在的游戏角色：" + item.Id);
                ArchetypeOptionDefinition[] options = (item.Options ?? new List<ArchetypeOptionDto>())
                    .Select(option => new ArchetypeOptionDefinition(
                        option.Id,
                        option.Name,
                        option.IsDefault,
                        option.FocusWeights ?? new Dictionary<string, int>(),
                        (option.CoreGroups ?? new List<CoreGroupDto>())
                            .Select(group => new CoreGroupDefinition(
                                group.Id,
                                MakeFocusMask(group.FocusIds),
                                group.CompletionBonus))
                            .ToArray(),
                        option.AllCoreGroupsCompletionBonus,
                        option.RiskModifiers ?? new Dictionary<string, int>()))
                    .ToArray();
                _archetypesById.Add(
                    item.Id,
                    new ArchetypeDefinition(
                        item.Id,
                        item.CharacterId,
                        item.Name,
                        item.Tier,
                        item.VersionStatus,
                        item.DefaultOptionId,
                        options,
                        true));
            }

            Dictionary<string, int> tagIndices = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string tag in source.NegativeRules.SelectMany(item => item.Tags ?? new List<string>()).Distinct())
                tagIndices.Add(tag, tagIndices.Count);
            if (tagIndices.Count > 64)
                throw new InvalidDataException("评分模型的负面标签超过当前位掩码容量。");

            NegativeRules = new NegativeRuleDefinition[Effects.Length];
            foreach (NegativeRuleDto item in source.NegativeRules)
            {
                EffectDefinition effect;
                if (!_effectsById.TryGetValue(item.EffectId, out effect))
                    throw new InvalidDataException("负面规则引用了不存在的词条 ID：" + item.EffectId);
                ulong tags = 0;
                foreach (string tag in item.Tags ?? new List<string>())
                    tags |= 1UL << tagIndices[tag];
                NegativeRules[effect.RuntimeIndex] = new NegativeRuleDefinition(
                    item.EffectId,
                    item.BasePenalty,
                    item.RiskWeights ?? new Dictionary<string, int>(),
                    (item.CompensatingPositiveFocusGroups ?? new List<List<string>>())
                        .Select(MakeFocusMask)
                        .ToArray(),
                    tags);
            }

            CombinationPenaltyRules = (source.CombinationPenaltyRules ?? new List<CombinationPenaltyDto>())
                .Select(item =>
                {
                    ulong tags = 0;
                    foreach (string tag in item.RequiredNegativeTags ?? new List<string>())
                        tags |= 1UL << tagIndices[tag];
                    return new CombinationPenaltyDefinition(tags, item.PenaltyPoints);
                })
                .ToArray();

            _vesselsById = (source.VesselCatalog.Vessels ?? new List<VesselDto>()).ToDictionary(
                item => item.Id,
                item => new VesselDefinition(
                    item.Id,
                    item.Name,
                    item.CharacterId,
                    item.IsUniversal,
                    (item.OrdinarySlotColorIds ?? new List<int>()).ToArray(),
                    (item.DeepSlotColorIds ?? new List<int>()).ToArray()));

            _relicTypes = LoadRelicCatalog(catalog);

            Characters = _charactersById.Values.OrderBy(item => item.DisplayOrder).ToArray();
            Archetypes = _archetypesById.Values.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
            ValidateRuntimeContract();
        }

        private RuntimeModel(LookupScoringModelDto source, CompactCatalogDto catalog)
        {
            if (source == null || source.GameVersion == null || source.GameVersion.Regulation != "1.03.5")
                throw new InvalidDataException("查表评分模型不是受支持的 Regulation 1.03.5。");
            if (!string.Equals(source.SchemaVersion, "nightreign.relic-ranking.lookup.v2", StringComparison.Ordinal)
                || source.Characters == null || source.Archetypes == null || source.EffectKeyOrder == null
                || source.EffectMeta == null || source.CompiledOptionScoreRows == null
                || source.RuntimeEffectResolution == null || source.EffectIdIndex == null
                || source.VesselCatalog == null || catalog == null)
                throw new InvalidDataException("查表评分模型缺少运行所需的数据段。");

            ModelVersion = source.ModelVersion;
            RegulationVersion = source.GameVersion.Regulation;
            IsDirectScoreModel = true;
            _focusIndices = new Dictionary<string, int>(StringComparer.Ordinal);

            int scoreColumnCount = source.EffectKeyOrder.Count;
            if (scoreColumnCount == 0 || source.EffectMeta.Count != scoreColumnCount
                || source.EffectKeyOrder.Distinct(StringComparer.Ordinal).Count() != scoreColumnCount)
                throw new InvalidDataException("查表评分模型的词条列目录不完整或包含重复项。");

            Dictionary<string, EffectMetaDto> metaByKey = source.EffectMeta.ToDictionary(
                item => item.Key,
                StringComparer.Ordinal);
            foreach (string key in source.EffectKeyOrder)
                if (!metaByKey.ContainsKey(key))
                    throw new InvalidDataException("查表评分模型缺少词条元数据：" + key);

            Dictionary<string, short[]> scoresByOption = new Dictionary<string, short[]>(StringComparer.Ordinal);
            foreach (LookupScoreRowDto row in source.CompiledOptionScoreRows)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.OptionId) || row.Scores == null
                    || row.Scores.Count != scoreColumnCount || scoresByOption.ContainsKey(row.OptionId))
                    throw new InvalidDataException("查表评分模型包含无效或重复的流派分数行。");
                short[] scores = new short[scoreColumnCount];
                for (int index = 0; index < scores.Length; index++)
                {
                    int value = row.Scores[index];
                    if (value < short.MinValue || value > short.MaxValue)
                        throw new InvalidDataException("查表评分超出 Int16 范围：" + row.OptionId);
                    scores[index] = (short)value;
                }
                scoresByOption.Add(row.OptionId, scores);
            }

            _charactersById = source.Characters.ToDictionary(
                item => item.Id,
                item => new CharacterDefinition(
                    item.Id,
                    item.Name,
                    item.Risk ?? new Dictionary<string, int>(),
                    (item.EligibleVesselIds ?? new List<int>()).ToArray()),
                StringComparer.Ordinal);

            _archetypesById = new Dictionary<string, ArchetypeDefinition>(StringComparer.Ordinal);
            HashSet<string> usedOptionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (LookupArchetypeDto item in source.Archetypes)
            {
                if (!_charactersById.ContainsKey(item.CharacterId))
                    throw new InvalidDataException("流派引用了不存在的游戏角色：" + item.Id);
                ArchetypeOptionDefinition[] options = (item.Options ?? new List<LookupArchetypeOptionDto>())
                    .Select(option =>
                    {
                        short[] scores;
                        if (!usedOptionIds.Add(option.Id) || !scoresByOption.TryGetValue(option.Id, out scores))
                            throw new InvalidDataException("查表评分模型缺少或重复流派选项：" + option.Id);
                        return new ArchetypeOptionDefinition(
                            option.Id,
                            option.Name,
                            option.IsDefault,
                            new Dictionary<string, int>(),
                            new CoreGroupDefinition[0],
                            0,
                            new Dictionary<string, int>(),
                            scores);
                    })
                    .ToArray();
                _archetypesById.Add(
                    item.Id,
                    new ArchetypeDefinition(
                        item.Id,
                        item.CharacterId,
                        item.Name,
                        item.LegacyTier,
                        item.Lifecycle,
                        item.DefaultOptionId,
                        options,
                        item.VisibleByDefault));
            }
            if (usedOptionIds.Count != scoresByOption.Count)
                throw new InvalidDataException("查表评分模型包含没有对应流派选项的分数行。");

            Dictionary<int, RuntimeEffectResolutionDto> resolutionById = source.RuntimeEffectResolution.ToDictionary(
                item => item.EffectId);
            Effects = new EffectDefinition[source.EffectIdIndex.Count];
            _effectsById = new Dictionary<int, EffectDefinition>(source.EffectIdIndex.Count);
            for (int index = 0; index < source.EffectIdIndex.Count; index++)
            {
                EffectIndexDto item = source.EffectIdIndex[index];
                RuntimeEffectResolutionDto resolution;
                if (!resolutionById.TryGetValue(item.EffectId, out resolution)
                    || resolution.OrdinaryScoreColumn < 0 || resolution.OrdinaryScoreColumn >= scoreColumnCount
                    || resolution.DeepScoreColumn < 0 || resolution.DeepScoreColumn >= scoreColumnCount)
                    throw new InvalidDataException("查表评分模型缺少有效的运行时词条映射：" + item.EffectId);
                EffectMetaDto ordinaryMeta;
                EffectMetaDto deepMeta;
                if (!metaByKey.TryGetValue(resolution.OrdinaryResolvedKey, out ordinaryMeta)
                    || !metaByKey.TryGetValue(resolution.DeepResolvedKey, out deepMeta))
                    throw new InvalidDataException("查表评分模型词条映射引用了不存在的列：" + item.EffectId);
                EffectFamilyDefinition family = new EffectFamilyDefinition(
                    item.FamilyId ?? "effect:" + item.EffectId,
                    ordinaryMeta.Name,
                    new FocusMask(),
                    new List<string>());
                EffectDefinition effect = new EffectDefinition(
                    index,
                    item.EffectId,
                    family,
                    item.PotencyFactor,
                    item.TriggerReliability,
                    item.RuntimeScoreFactor,
                    item.VerificationConfidence,
                    item.StackPolicy,
                    Math.Max(1, item.MaxUsefulCopies),
                    resolution.OrdinaryScoreColumn,
                    resolution.DeepScoreColumn);
                if (_effectsById.ContainsKey(effect.EffectId))
                    throw new InvalidDataException("查表评分模型包含重复词条 ID：" + effect.EffectId);
                Effects[index] = effect;
                _effectsById.Add(effect.EffectId, effect);

                foreach (short[] scores in scoresByOption.Values)
                {
                    if (IsNegativeType(ordinaryMeta.Type) && scores[resolution.OrdinaryScoreColumn] > 0
                        || IsNegativeType(deepMeta.Type) && scores[resolution.DeepScoreColumn] > 0)
                        throw new InvalidDataException("负面词条在查表模型中出现正分：" + item.EffectId);
                }
            }
            if (resolutionById.Count != Effects.Length)
                throw new InvalidDataException("查表评分模型包含没有运行时词条定义的映射。");

            NegativeRules = new NegativeRuleDefinition[Effects.Length];
            CombinationPenaltyRules = new CombinationPenaltyDefinition[0];
            _vesselsById = (source.VesselCatalog.Vessels ?? new List<VesselDto>()).ToDictionary(
                item => item.Id,
                item => new VesselDefinition(
                    item.Id,
                    item.Name,
                    item.CharacterId,
                    item.IsUniversal,
                    (item.OrdinarySlotColorIds ?? new List<int>()).ToArray(),
                    (item.DeepSlotColorIds ?? new List<int>()).ToArray()));

            _relicTypes = LoadRelicCatalog(catalog);

            Characters = _charactersById.Values.OrderBy(item => item.DisplayOrder).ToArray();
            Archetypes = _archetypesById.Values.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
            ValidateRuntimeContract();
        }

        public string ModelVersion { get; private set; }
        public string RegulationVersion { get; private set; }
        public bool IsDirectScoreModel { get; private set; }
        public CharacterDefinition[] Characters { get; private set; }
        public ArchetypeDefinition[] Archetypes { get; private set; }
        internal EffectDefinition[] Effects { get; private set; }
        internal NegativeRuleDefinition[] NegativeRules { get; private set; }
        internal CombinationPenaltyDefinition[] CombinationPenaltyRules { get; private set; }

        public static RuntimeModel LoadBuiltIn()
        {
            Assembly assembly = typeof(RuntimeModel).Assembly;
            return LoadCompressed(
                ReadResourceBytes(assembly, ModelResourceName),
                ReadResourceBytes(assembly, CatalogResourceName));
        }

        internal static RuntimeModel LoadCompressed(byte[] scoringModel, byte[] relicCatalog)
        {
            if (scoringModel == null) throw new ArgumentNullException("scoringModel");
            if (relicCatalog == null) throw new ArgumentNullException("relicCatalog");
            byte[] modelCopy = (byte[])scoringModel.Clone();
            byte[] catalogCopy = (byte[])relicCatalog.Clone();
            CompactCatalogDto catalog = ReadGzipJson<CompactCatalogDto>(catalogCopy);
            ModelHeaderDto header = ReadGzipJson<ModelHeaderDto>(modelCopy);
            RuntimeModel result = string.Equals(header.SchemaVersion, "nightreign.relic-ranking.lookup.v2", StringComparison.Ordinal)
                ? new RuntimeModel(ReadGzipJson<LookupScoringModelDto>(modelCopy), catalog)
                : new RuntimeModel(ReadGzipJson<ScoringModelDto>(modelCopy), catalog);
            result._sourceScoringModel = modelCopy;
            result._sourceRelicCatalog = catalogCopy;
            return result;
        }

        internal RuntimeModel CreateDefensiveCopy()
        {
            if (_sourceScoringModel == null || _sourceRelicCatalog == null)
                throw new InvalidOperationException("运行时模型缺少可复制的已验证来源。");
            return LoadCompressed(_sourceScoringModel, _sourceRelicCatalog);
        }

        public CharacterDefinition GetCharacter(string id)
        {
            CharacterDefinition result;
            return id != null && _charactersById.TryGetValue(id, out result) ? result : null;
        }

        public ArchetypeDefinition GetArchetype(string id)
        {
            ArchetypeDefinition result;
            return id != null && _archetypesById.TryGetValue(id, out result) ? result : null;
        }

        public VesselDefinition GetVessel(int id)
        {
            VesselDefinition result;
            return _vesselsById.TryGetValue(id, out result) ? result : null;
        }

        internal EffectDefinition GetEffect(int effectId)
        {
            EffectDefinition result;
            return _effectsById.TryGetValue(effectId, out result) ? result : null;
        }

        public string GetEffectName(int effectId)
        {
            EffectDefinition effect = GetEffect(effectId);
            return effect == null ? "未知词条 #" + effectId : effect.Family.Name;
        }

        public RelicTypeDefinition GetRelicType(int itemId)
        {
            RelicTypeDefinition result;
            return _relicTypes.TryGetValue(itemId, out result) ? result : null;
        }

        internal FocusMask MakeFocusMask(IEnumerable<string> ids)
        {
            FocusMask result = new FocusMask();
            if (ids == null) return result;
            foreach (string id in ids)
            {
                int index;
                if (!_focusIndices.TryGetValue(id, out index)) continue;
                if (index < 64) result.A |= 1UL << index;
                else if (index < 128) result.B |= 1UL << (index - 64);
                else result.C |= 1UL << (index - 128);
            }
            return result;
        }

        private void ValidateRuntimeContract()
        {
            int optionCount = Archetypes.Sum(item => item.Options.Length);
            if (Characters.Length != 10 || Archetypes.Length == 0 || optionCount == 0)
                throw new InvalidDataException("评分模型的角色或流派目录无效。");
            foreach (VesselDefinition vessel in _vesselsById.Values)
            {
                if (vessel.OrdinarySlotColorIds == null || vessel.OrdinarySlotColorIds.Length != 3
                    || vessel.DeepSlotColorIds == null || vessel.DeepSlotColorIds.Length != 3)
                    throw new InvalidDataException("器皿 " + vessel.Id + " 的普通或深夜槽位数量不是 3。");
                if (vessel.OrdinarySlotColorIds.Concat(vessel.DeepSlotColorIds).Any(color => color < 0 || color > 4))
                    throw new InvalidDataException("器皿 " + vessel.Id + " 包含无效槽位颜色。");
                if (!vessel.IsUniversal && !_charactersById.ContainsKey(vessel.CharacterId))
                    throw new InvalidDataException("器皿 " + vessel.Id + " 引用了不存在的角色：" + vessel.CharacterId);
            }
            foreach (CharacterDefinition character in Characters)
            {
                if (character.EligibleVesselIds.Length != 11
                    || character.EligibleVesselIds.Distinct().Count() != character.EligibleVesselIds.Length)
                    throw new InvalidDataException("角色 " + character.Name + " 的可用器皿数量不是 11。");
                foreach (int vesselId in character.EligibleVesselIds)
                {
                    VesselDefinition vessel;
                    if (!_vesselsById.TryGetValue(vesselId, out vessel))
                        throw new InvalidDataException("角色 " + character.Name + " 引用了不存在的器皿 " + vesselId + "。");
                    if (!vessel.IsUniversal && !string.Equals(vessel.CharacterId, character.Id, StringComparison.Ordinal))
                        throw new InvalidDataException("角色 " + character.Name + " 引用了其他角色的专属器皿 " + vesselId + "。");
                }
            }
            foreach (ArchetypeDefinition archetype in Archetypes)
            {
                if (!_charactersById.ContainsKey(archetype.CharacterId) || archetype.Options.Length == 0)
                    throw new InvalidDataException("流派缺少有效角色或选项：" + archetype.Id);
                if (archetype.Options.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != archetype.Options.Length)
                    throw new InvalidDataException("流派包含重复选项 ID：" + archetype.Id);
                if (!archetype.Options.Any(item => item.Id == archetype.DefaultOptionId))
                    throw new InvalidDataException("流派的默认选项不存在：" + archetype.Id);
            }
        }

        private static Dictionary<int, RelicTypeDefinition> LoadRelicCatalog(CompactCatalogDto catalog)
        {
            if (catalog == null || catalog.Relics == null || catalog.Relics.Count == 0)
                throw new InvalidDataException("遗物目录为空。");
            Dictionary<int, RelicTypeDefinition> result = new Dictionary<int, RelicTypeDefinition>();
            foreach (int[] row in catalog.Relics)
            {
                if (row == null || row.Length != 3 || row[0] <= 0
                    || row[1] < 0 || row[1] > 3 || (row[2] != 0 && row[2] != 1))
                    throw new InvalidDataException("遗物目录包含无效行。");
                if (result.ContainsKey(row[0]))
                    throw new InvalidDataException("遗物目录包含重复 ItemId：" + row[0]);
                result.Add(row[0], new RelicTypeDefinition(row[1], row[2] != 0));
            }
            return result;
        }

        private static bool IsNegativeType(string type)
        {
            return string.Equals(type, "负面效果", StringComparison.Ordinal);
        }

        private static byte[] ReadResourceBytes(Assembly assembly, string resourceName)
        {
            using (Stream resource = assembly.GetManifestResourceStream(resourceName))
            {
                if (resource == null) throw new InvalidDataException("找不到内置资源：" + resourceName);
                using (MemoryStream output = new MemoryStream())
                {
                    resource.CopyTo(output);
                    return output.ToArray();
                }
            }
        }

        private static T ReadGzipJson<T>(byte[] compressed)
        {
            const int maximumJsonBytes = 8 * 1024 * 1024;
            using (MemoryStream input = new MemoryStream(compressed, false))
            using (GZipStream gzip = new GZipStream(input, CompressionMode.Decompress))
            using (MemoryStream json = new MemoryStream())
            {
                byte[] buffer = new byte[64 * 1024];
                int read;
                while ((read = gzip.Read(buffer, 0, buffer.Length)) != 0)
                {
                    if (json.Length + read > maximumJsonBytes)
                        throw new InvalidDataException("数据包解压后的 JSON 超过 8 MB 限制。");
                    json.Write(buffer, 0, read);
                }
                json.Position = 0;
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(
                    typeof(T),
                    new DataContractJsonSerializerSettings
                    {
                        MaxItemsInObjectGraph = int.MaxValue,
                        UseSimpleDictionaryFormat = true
                    });
                return (T)serializer.ReadObject(json);
            }
        }
    }

    public sealed class RelicTypeDefinition
    {
        internal RelicTypeDefinition(int colorId, bool isDeep)
        {
            ColorId = colorId;
            IsDeep = isDeep;
        }

        public int ColorId { get; private set; }
        public bool IsDeep { get; private set; }
    }

    public sealed class CharacterDefinition
    {
        private static readonly string[] Order =
        {
            "wylder", "guardian", "ironeye", "duchess", "raider",
            "revenant", "recluse", "executor", "scholar", "undertaker"
        };

        internal CharacterDefinition(string id, string name, Dictionary<string, int> risk, int[] eligibleVesselIds)
        {
            Id = id;
            Name = name;
            Risk = risk;
            EligibleVesselIds = eligibleVesselIds;
            int order = Array.IndexOf(Order, id);
            DisplayOrder = order < 0 ? int.MaxValue : order;
        }

        public string Id { get; private set; }
        public string Name { get; private set; }
        public int[] EligibleVesselIds { get; private set; }
        internal Dictionary<string, int> Risk { get; private set; }
        internal int DisplayOrder { get; private set; }
    }

    public sealed class ArchetypeDefinition
    {
        internal ArchetypeDefinition(
            string id,
            string characterId,
            string name,
            string tier,
            string versionStatus,
            string defaultOptionId,
            ArchetypeOptionDefinition[] options,
            bool visibleByDefault)
        {
            Id = id;
            CharacterId = characterId;
            Name = name;
            Tier = tier;
            VersionStatus = versionStatus;
            DefaultOptionId = defaultOptionId;
            Options = options;
            VisibleByDefault = visibleByDefault;
        }

        public string Id { get; private set; }
        public string CharacterId { get; private set; }
        public string Name { get; private set; }
        public string Tier { get; private set; }
        public string VersionStatus { get; private set; }
        public string DefaultOptionId { get; private set; }
        public ArchetypeOptionDefinition[] Options { get; private set; }
        public bool VisibleByDefault { get; private set; }
    }

    public sealed class ArchetypeOptionDefinition
    {
        internal ArchetypeOptionDefinition(
            string id,
            string name,
            bool isDefault,
            Dictionary<string, int> focusWeights,
            CoreGroupDefinition[] coreGroups,
            int allCoreGroupsCompletionBonus,
            Dictionary<string, int> riskModifiers,
            short[] directScores = null)
        {
            Id = id;
            Name = name;
            IsDefault = isDefault;
            FocusWeights = focusWeights;
            CoreGroups = coreGroups;
            AllCoreGroupsCompletionBonus = allCoreGroupsCompletionBonus;
            RiskModifiers = riskModifiers;
            DirectScores = directScores;
        }

        public string Id { get; private set; }
        public string Name { get; private set; }
        public bool IsDefault { get; private set; }
        internal Dictionary<string, int> FocusWeights { get; private set; }
        internal CoreGroupDefinition[] CoreGroups { get; private set; }
        internal int AllCoreGroupsCompletionBonus { get; private set; }
        internal Dictionary<string, int> RiskModifiers { get; private set; }
        internal short[] DirectScores { get; private set; }
    }

    internal sealed class CoreGroupDefinition
    {
        public CoreGroupDefinition(string id, FocusMask focusMask, int completionBonus)
        {
            Id = id;
            FocusMask = focusMask;
            CompletionBonus = completionBonus;
        }

        public string Id { get; private set; }
        public FocusMask FocusMask { get; private set; }
        public int CompletionBonus { get; private set; }
    }

    public sealed class VesselDefinition
    {
        internal VesselDefinition(
            int id,
            string name,
            string characterId,
            bool isUniversal,
            int[] ordinarySlotColorIds,
            int[] deepSlotColorIds)
        {
            Id = id;
            Name = name;
            CharacterId = characterId;
            IsUniversal = isUniversal;
            OrdinarySlotColorIds = ordinarySlotColorIds;
            DeepSlotColorIds = deepSlotColorIds;
        }

        public int Id { get; private set; }
        public string Name { get; private set; }
        public string CharacterId { get; private set; }
        public bool IsUniversal { get; private set; }
        public int[] OrdinarySlotColorIds { get; private set; }
        public int[] DeepSlotColorIds { get; private set; }
    }

    internal sealed class EffectFamilyDefinition
    {
        public EffectFamilyDefinition(string id, string name, FocusMask positiveFocusMask, List<string> characterIds)
        {
            Id = id;
            Name = name;
            PositiveFocusMask = positiveFocusMask;
            CharacterIds = new HashSet<string>(characterIds, StringComparer.Ordinal);
        }

        public string Id { get; private set; }
        public string Name { get; private set; }
        public FocusMask PositiveFocusMask { get; private set; }
        public HashSet<string> CharacterIds { get; private set; }
    }

    internal sealed class EffectDefinition
    {
        public EffectDefinition(
            int runtimeIndex,
            int effectId,
            EffectFamilyDefinition family,
            int potencyFactor,
            int triggerReliability,
            int runtimeScoreFactor,
            int verificationConfidence,
            string stackPolicy,
            int maxUsefulCopies,
            int ordinaryScoreColumn = -1,
            int deepScoreColumn = -1)
        {
            RuntimeIndex = runtimeIndex;
            EffectId = effectId;
            Family = family;
            PotencyFactor = potencyFactor;
            TriggerReliability = triggerReliability;
            RuntimeScoreFactor = runtimeScoreFactor;
            VerificationConfidence = verificationConfidence;
            MaxUsefulCopies = string.Equals(stackPolicy, "stacks", StringComparison.Ordinal)
                ? maxUsefulCopies
                : 1;
            OrdinaryScoreColumn = ordinaryScoreColumn;
            DeepScoreColumn = deepScoreColumn;
        }

        public int RuntimeIndex { get; private set; }
        public int EffectId { get; private set; }
        public EffectFamilyDefinition Family { get; private set; }
        public int PotencyFactor { get; private set; }
        public int TriggerReliability { get; private set; }
        public int RuntimeScoreFactor { get; private set; }
        public int VerificationConfidence { get; private set; }
        public int MaxUsefulCopies { get; private set; }
        public int OrdinaryScoreColumn { get; private set; }
        public int DeepScoreColumn { get; private set; }
    }

    internal sealed class NegativeRuleDefinition
    {
        public NegativeRuleDefinition(
            int effectId,
            int basePenalty,
            Dictionary<string, int> riskWeights,
            FocusMask[] compensationGroups,
            ulong tags)
        {
            EffectId = effectId;
            BasePenalty = basePenalty;
            RiskWeights = riskWeights;
            CompensationGroups = compensationGroups;
            Tags = tags;
        }

        public int EffectId { get; private set; }
        public int BasePenalty { get; private set; }
        public Dictionary<string, int> RiskWeights { get; private set; }
        public FocusMask[] CompensationGroups { get; private set; }
        public ulong Tags { get; private set; }
    }

    internal sealed class CombinationPenaltyDefinition
    {
        public CombinationPenaltyDefinition(ulong requiredTags, int penaltyPoints)
        {
            RequiredTags = requiredTags;
            PenaltyPoints = penaltyPoints;
        }

        public ulong RequiredTags { get; private set; }
        public int PenaltyPoints { get; private set; }
    }

#pragma warning disable 0649
    [DataContract]
    internal sealed class ModelHeaderDto
    {
        [DataMember(Name = "schemaVersion")] public string SchemaVersion;
    }

    [DataContract]
    internal sealed class LookupScoringModelDto
    {
        [DataMember(Name = "schemaVersion")] public string SchemaVersion;
        [DataMember(Name = "modelVersion")] public string ModelVersion;
        [DataMember(Name = "gameVersion")] public GameVersionDto GameVersion;
        [DataMember(Name = "characters")] public List<CharacterDto> Characters;
        [DataMember(Name = "archetypes")] public List<LookupArchetypeDto> Archetypes;
        [DataMember(Name = "effectKeyOrder")] public List<string> EffectKeyOrder;
        [DataMember(Name = "effectMeta")] public List<EffectMetaDto> EffectMeta;
        [DataMember(Name = "compiledOptionScoreRows")] public List<LookupScoreRowDto> CompiledOptionScoreRows;
        [DataMember(Name = "runtimeEffectResolution")] public List<RuntimeEffectResolutionDto> RuntimeEffectResolution;
        [DataMember(Name = "effectIdIndex")] public List<EffectIndexDto> EffectIdIndex;
        [DataMember(Name = "vesselCatalog")] public VesselCatalogDto VesselCatalog;
    }

    [DataContract]
    internal sealed class LookupArchetypeDto
    {
        [DataMember(Name = "id")] public string Id;
        [DataMember(Name = "characterId")] public string CharacterId;
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "legacyTier")] public string LegacyTier;
        [DataMember(Name = "lifecycle")] public string Lifecycle;
        [DataMember(Name = "visibleByDefault")] public bool VisibleByDefault;
        [DataMember(Name = "defaultOptionId")] public string DefaultOptionId;
        [DataMember(Name = "options")] public List<LookupArchetypeOptionDto> Options;
    }

    [DataContract]
    internal sealed class LookupArchetypeOptionDto
    {
        [DataMember(Name = "id")] public string Id;
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "isDefault")] public bool IsDefault;
    }

    [DataContract]
    internal sealed class EffectMetaDto
    {
        [DataMember(Name = "key")] public string Key;
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "type")] public string Type;
    }

    [DataContract]
    internal sealed class LookupScoreRowDto
    {
        [DataMember(Name = "optionId")] public string OptionId;
        [DataMember(Name = "scores")] public List<int> Scores;
    }

    [DataContract]
    internal sealed class RuntimeEffectResolutionDto
    {
        [DataMember(Name = "effectId")] public int EffectId;
        [DataMember(Name = "ordinaryScoreColumn")] public int OrdinaryScoreColumn;
        [DataMember(Name = "deepScoreColumn")] public int DeepScoreColumn;
        [DataMember(Name = "ordinaryResolvedKey")] public string OrdinaryResolvedKey;
        [DataMember(Name = "deepResolvedKey")] public string DeepResolvedKey;
    }

    [DataContract]
    internal sealed class ScoringModelDto
    {
        [DataMember(Name = "modelVersion")] public string ModelVersion;
        [DataMember(Name = "gameVersion")] public GameVersionDto GameVersion;
        [DataMember(Name = "focusCatalog")] public List<FocusDto> FocusCatalog;
        [DataMember(Name = "characters")] public List<CharacterDto> Characters;
        [DataMember(Name = "archetypes")] public List<ArchetypeDto> Archetypes;
        [DataMember(Name = "effectFamilies")] public List<EffectFamilyDto> EffectFamilies;
        [DataMember(Name = "effectIdIndex")] public List<EffectIndexDto> EffectIdIndex;
        [DataMember(Name = "negativeRules")] public List<NegativeRuleDto> NegativeRules;
        [DataMember(Name = "combinationPenaltyRules")] public List<CombinationPenaltyDto> CombinationPenaltyRules;
        [DataMember(Name = "vesselCatalog")] public VesselCatalogDto VesselCatalog;
    }

    [DataContract] internal sealed class GameVersionDto { [DataMember(Name = "regulation")] public string Regulation; }
    [DataContract] internal sealed class FocusDto { [DataMember(Name = "id")] public string Id; }

    [DataContract]
    internal sealed class CharacterDto
    {
        [DataMember(Name = "id")] public string Id;
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "risk")] public Dictionary<string, int> Risk;
        [DataMember(Name = "eligibleVesselIds")] public List<int> EligibleVesselIds;
    }

    [DataContract]
    internal sealed class ArchetypeDto
    {
        [DataMember(Name = "id")] public string Id;
        [DataMember(Name = "characterId")] public string CharacterId;
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "tier")] public string Tier;
        [DataMember(Name = "versionStatus")] public string VersionStatus;
        [DataMember(Name = "defaultOptionId")] public string DefaultOptionId;
        [DataMember(Name = "options")] public List<ArchetypeOptionDto> Options;
    }

    [DataContract]
    internal sealed class ArchetypeOptionDto
    {
        [DataMember(Name = "id")] public string Id;
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "isDefault")] public bool IsDefault;
        [DataMember(Name = "focusWeights")] public Dictionary<string, int> FocusWeights;
        [DataMember(Name = "coreGroups")] public List<CoreGroupDto> CoreGroups;
        [DataMember(Name = "allCoreGroupsCompletionBonus")] public int AllCoreGroupsCompletionBonus;
        [DataMember(Name = "riskModifiers")] public Dictionary<string, int> RiskModifiers;
    }

    [DataContract]
    internal sealed class CoreGroupDto
    {
        [DataMember(Name = "id")] public string Id;
        [DataMember(Name = "focusIds")] public List<string> FocusIds;
        [DataMember(Name = "completionBonus")] public int CompletionBonus;
    }

    [DataContract]
    internal sealed class EffectFamilyDto
    {
        [DataMember(Name = "id")] public string Id;
        [DataMember(Name = "canonicalName")] public string CanonicalName;
        [DataMember(Name = "positiveFocusIds")] public List<string> PositiveFocusIds;
        [DataMember(Name = "characterIds")] public List<string> CharacterIds;
    }

    [DataContract]
    internal sealed class EffectIndexDto
    {
        [DataMember(Name = "effectId")] public int EffectId;
        [DataMember(Name = "familyId")] public string FamilyId;
        [DataMember(Name = "potencyFactor")] public int PotencyFactor;
        [DataMember(Name = "triggerReliability")] public int TriggerReliability;
        [DataMember(Name = "runtimeScoreFactor")] public int RuntimeScoreFactor;
        [DataMember(Name = "verificationConfidence")] public int VerificationConfidence;
        [DataMember(Name = "stackPolicy")] public string StackPolicy;
        [DataMember(Name = "maxUsefulCopies")] public int MaxUsefulCopies;
    }

    [DataContract]
    internal sealed class NegativeRuleDto
    {
        [DataMember(Name = "effectId")] public int EffectId;
        [DataMember(Name = "basePenalty")] public int BasePenalty;
        [DataMember(Name = "riskWeights")] public Dictionary<string, int> RiskWeights;
        [DataMember(Name = "tags")] public List<string> Tags;
        [DataMember(Name = "compensatingPositiveFocusGroups")] public List<List<string>> CompensatingPositiveFocusGroups;
    }

    [DataContract]
    internal sealed class CombinationPenaltyDto
    {
        [DataMember(Name = "requiredNegativeTags")] public List<string> RequiredNegativeTags;
        [DataMember(Name = "penaltyPoints")] public int PenaltyPoints;
    }

    [DataContract]
    internal sealed class VesselCatalogDto
    {
        [DataMember(Name = "vessels")] public List<VesselDto> Vessels;
    }

    [DataContract]
    internal sealed class VesselDto
    {
        [DataMember(Name = "id")] public int Id;
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "characterId")] public string CharacterId;
        [DataMember(Name = "isUniversal")] public bool IsUniversal;
        [DataMember(Name = "ordinarySlotColorIds")] public List<int> OrdinarySlotColorIds;
        [DataMember(Name = "deepSlotColorIds")] public List<int> DeepSlotColorIds;
    }

    [DataContract]
    internal sealed class CompactCatalogDto
    {
        [DataMember(Name = "relics")] public List<int[]> Relics;
    }
#pragma warning restore 0649
}
