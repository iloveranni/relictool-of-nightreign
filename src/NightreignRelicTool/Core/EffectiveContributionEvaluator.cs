using System;
using System.Collections.Generic;
using System.Linq;

namespace NightreignRelicTool.Core
{
    public enum EffectiveContributionExclusionReason
    {
        None,
        CharacterInapplicable,
        CatalogUnavailable,
        SingleInstanceLimit,
        CapCountLimit,
        UnknownAggregationAdditional,
        ExclusivityNotConfirmed
    }

    public sealed class EffectiveContributionOccurrence
    {
        internal EffectiveContributionOccurrence(
            string ruleId,
            string selectorId,
            int ruleIndex,
            int instanceId,
            int runtimeEffectId,
            int physicalOrdinal,
            string stackGroupKey,
            string aggregationRule,
            int variantPreferenceRank,
            bool isEffective,
            EffectiveContributionExclusionReason exclusionReason,
            bool uncertainAdditional,
            bool physicalLocationConfirmed,
            string verificationStatus,
            CustomEffectCalculationTerm[] calculationTerms,
            bool calculationTermsReliable)
        {
            RuleId = ruleId ?? string.Empty;
            SelectorId = selectorId;
            RuleIndex = ruleIndex;
            InstanceId = instanceId;
            RuntimeEffectId = runtimeEffectId;
            PhysicalOrdinal = physicalOrdinal;
            StackGroupKey = stackGroupKey ?? string.Empty;
            AggregationRule = aggregationRule ?? "unknown";
            VariantPreferenceRank = variantPreferenceRank;
            IsEffective = isEffective;
            ExclusionReason = exclusionReason;
            IsUncertainAdditional = uncertainAdditional;
            IsPhysicalLocationConfirmed = physicalLocationConfirmed;
            VerificationStatus = verificationStatus ?? string.Empty;
            CalculationTerms = calculationTerms ?? new CustomEffectCalculationTerm[0];
            CalculationTermsReliable = calculationTermsReliable;
        }

        public string RuleId { get; private set; }
        public string SelectorId { get; private set; }
        public int RuleIndex { get; private set; }
        public int InstanceId { get; private set; }
        public int RuntimeEffectId { get; private set; }
        public int PhysicalOrdinal { get; private set; }
        public string StackGroupKey { get; private set; }
        public string AggregationRule { get; private set; }
        public int VariantPreferenceRank { get; private set; }
        public bool IsEffective { get; private set; }
        public EffectiveContributionExclusionReason ExclusionReason { get; private set; }
        public bool IsUncertainAdditional { get; private set; }
        public bool IsPhysicalLocationConfirmed { get; private set; }
        public string VerificationStatus { get; private set; }
        public CustomEffectCalculationTerm[] CalculationTerms { get; private set; }
        public bool CalculationTermsReliable { get; private set; }
    }

    public sealed class EffectiveRuleContribution
    {
        internal EffectiveRuleContribution(
            string ruleId,
            string selectorId,
            string displayName,
            bool isRequired,
            int quantityTarget,
            EffectiveContributionOccurrence[] occurrences,
            RuleComparisonProfile comparisonProfile)
        {
            RuleId = ruleId ?? string.Empty;
            SelectorId = selectorId;
            DisplayName = displayName ?? string.Empty;
            IsRequired = isRequired;
            QuantityTarget = quantityTarget;
            Occurrences = occurrences ?? new EffectiveContributionOccurrence[0];
            ComparisonProfile = comparisonProfile;
        }

        public string RuleId { get; private set; }
        public string SelectorId { get; private set; }
        public string DisplayName { get; private set; }
        public bool IsRequired { get; private set; }
        public int QuantityTarget { get; private set; }
        public EffectiveContributionOccurrence[] Occurrences { get; private set; }
        public RuleComparisonProfile ComparisonProfile { get; private set; }
        public int RawCount { get { return ComparisonProfile == null ? 0 : ComparisonProfile.RawCount; } }
        public int EffectiveCount { get { return ComparisonProfile == null ? 0 : ComparisonProfile.EffectiveCount; } }
        public int UncertainAdditionalCount
        {
            get { return ComparisonProfile == null ? 0 : ComparisonProfile.UncertainAdditionalCount; }
        }
    }

    public sealed class EffectiveContributionEvaluation
    {
        internal EffectiveContributionEvaluation(
            EffectiveRuleContribution[] rules,
            CoLocationProfile coLocationProfile,
            int exclusivityUncertaintyCount)
        {
            Rules = rules ?? new EffectiveRuleContribution[0];
            CoLocationProfile = coLocationProfile ?? new CoLocationProfile(new int[0]);
            ExclusivityUncertaintyCount = Math.Max(0, exclusivityUncertaintyCount);
            NumericSummaries = new EffectiveContributionNumericSummary[0];
        }

        public EffectiveRuleContribution[] Rules { get; private set; }
        public CoLocationProfile CoLocationProfile { get; private set; }
        public int ExclusivityUncertaintyCount { get; private set; }
        public EffectiveContributionNumericSummary[] NumericSummaries { get; private set; }

        internal void SetNumericSummaries(EffectiveContributionNumericSummary[] summaries)
        {
            NumericSummaries = summaries ?? new EffectiveContributionNumericSummary[0];
        }

        public EffectiveRuleContribution GetRule(string ruleId)
        {
            return Rules.FirstOrDefault(item => string.Equals(item.RuleId, ruleId, StringComparison.Ordinal));
        }
    }

    public enum EffectiveNumericSummaryStatus
    {
        NoCalculationData,
        Reliable,
        Incomplete,
        Unverified,
        NonNumeric,
        Incompatible,
        InvalidNeutralValue,
        NumericOverflow
    }

    public sealed class EffectiveContributionNumericSummary
    {
        internal EffectiveContributionNumericSummary(
            string ruleId,
            string displayName,
            EffectiveNumericSummaryStatus status,
            string mechanicKey,
            string operation,
            string unit,
            string targetAttribute,
            decimal? neutralValue,
            decimal[] effectiveValues,
            decimal? totalValue,
            int effectiveOccurrenceCount,
            int uncertainAdditionalCount)
        {
            RuleId = ruleId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Status = status;
            MechanicKey = mechanicKey ?? string.Empty;
            Operation = operation ?? string.Empty;
            Unit = unit ?? string.Empty;
            TargetAttribute = targetAttribute ?? string.Empty;
            NeutralValue = neutralValue;
            EffectiveValues = effectiveValues ?? new decimal[0];
            TotalValue = totalValue;
            EffectiveOccurrenceCount = effectiveOccurrenceCount;
            UncertainAdditionalCount = uncertainAdditionalCount;
        }

        public string RuleId { get; private set; }
        public string DisplayName { get; private set; }
        public EffectiveNumericSummaryStatus Status { get; private set; }
        public bool HasCalculationData
        {
            get { return Status != EffectiveNumericSummaryStatus.NoCalculationData; }
        }
        public bool CanReliablyCombine
        {
            get { return Status == EffectiveNumericSummaryStatus.Reliable; }
        }
        public string MechanicKey { get; private set; }
        public string Operation { get; private set; }
        public string Unit { get; private set; }
        public string TargetAttribute { get; private set; }
        public decimal? NeutralValue { get; private set; }
        public decimal[] EffectiveValues { get; private set; }
        public decimal? TotalValue { get; private set; }
        public int EffectiveOccurrenceCount { get; private set; }
        public int UncertainAdditionalCount { get; private set; }
    }

    internal sealed class EffectiveContributionRuleDefinition
    {
        public EffectiveContributionRuleDefinition(
            CustomEffectRule source,
            CustomEffectSelector selector,
            OfficialPresetRelic preset)
        {
            Source = source;
            Selector = selector;
            Preset = preset;
            IsAvailableForRealMatching = selector == null
                ? preset != null && preset.IsAvailableForRealMatching
                : selector.IsAvailableForRealMatching;
            Ranks = selector == null
                ? new Dictionary<int, int>()
                : selector.RuntimeEffectIds.ToDictionary(
                    id => id,
                    id => selector.GetVariantPreferenceRank(id));
            RuntimeEffectIds = Ranks.Keys.ToArray();
        }

        public CustomEffectRule Source;
        public CustomEffectSelector Selector;
        public OfficialPresetRelic Preset;
        public bool IsAvailableForRealMatching;
        public Dictionary<int, int> Ranks;
        public int[] RuntimeEffectIds;
    }

    /// <summary>
    /// Single source of truth for applicability, exclusivity and repeated-copy
    /// aggregation.  Both exact ranking and UI explanations consume this same
    /// immutable result.
    /// </summary>
    public sealed class EffectiveContributionEvaluator
    {
        private readonly CustomEffectCatalog _catalog;

        public EffectiveContributionEvaluator(CustomEffectCatalog catalog)
        {
            _catalog = catalog ?? throw new ArgumentNullException("catalog");
        }

        public EffectiveContributionEvaluation Evaluate(
            SlotAssignment[] assignments,
            string characterId,
            IEnumerable<CustomEffectRule> rules)
        {
            if (assignments == null) throw new ArgumentNullException("assignments");
            return Evaluate(
                assignments.Select(item => item == null ? null : item.Relic).ToArray(),
                characterId,
                PrepareRules(rules),
                null,
                true);
        }

        public EffectiveContributionNumericSummary[] BuildNumericSummaries(
            EffectiveContributionEvaluation evaluation)
        {
            if (evaluation == null) throw new ArgumentNullException("evaluation");
            return evaluation.NumericSummaries.ToArray();
        }

        private static EffectiveContributionNumericSummary BuildNumericSummary(
            EffectiveRuleContribution rule)
        {
            EffectiveContributionOccurrence[] effective = rule.Occurrences
                .Where(item => item.IsEffective).ToArray();
            CustomEffectCalculationTerm[] terms = effective
                .SelectMany(item => item.CalculationTerms ?? new CustomEffectCalculationTerm[0])
                .ToArray();
            bool hasAnyData = terms.Length != 0;
            if (!hasAnyData)
                return NumericSummary(rule, EffectiveNumericSummaryStatus.NoCalculationData,
                    null, new decimal[0], null, effective.Length);
            if (effective.Any(item => !item.CalculationTermsReliable
                || item.CalculationTerms == null || item.CalculationTerms.Length == 0))
                return NumericSummary(rule, EffectiveNumericSummaryStatus.Incomplete,
                    terms.FirstOrDefault(), new decimal[0], null, effective.Length);
            if (terms.Any(item => !IsConfirmedCalculationStatus(item.VerificationStatus)))
                return NumericSummary(rule, EffectiveNumericSummaryStatus.Unverified,
                    terms[0], new decimal[0], null, effective.Length);
            if (terms.Any(item => !item.NumericValue.HasValue
                || !IsNumericOperation(item.Operation)))
                return NumericSummary(rule, EffectiveNumericSummaryStatus.NonNumeric,
                    terms[0], new decimal[0], null, effective.Length);

            CustomEffectCalculationTerm first = terms[0];
            if (terms.Any(item => !string.Equals(item.MechanicKey, first.MechanicKey, StringComparison.Ordinal)
                || !string.Equals(item.Operation, first.Operation, StringComparison.Ordinal)
                || !string.Equals(item.Unit ?? string.Empty, first.Unit ?? string.Empty, StringComparison.Ordinal)
                || !string.Equals(item.TargetAttribute ?? string.Empty,
                    first.TargetAttribute ?? string.Empty, StringComparison.Ordinal)
                || item.NeutralValue != first.NeutralValue))
                return NumericSummary(rule, EffectiveNumericSummaryStatus.Incompatible,
                    first, new decimal[0], null, effective.Length);

            decimal expectedNeutral = string.Equals(first.Operation, "multiplier", StringComparison.Ordinal)
                ? 1m : 0m;
            if (!first.NeutralValue.HasValue || first.NeutralValue.Value != expectedNeutral)
                return NumericSummary(rule, EffectiveNumericSummaryStatus.InvalidNeutralValue,
                    first, new decimal[0], null, effective.Length);

            decimal[] values = terms.Select(item => item.NumericValue.Value).ToArray();
            decimal total = expectedNeutral;
            try
            {
                checked
                {
                    if (string.Equals(first.Operation, "multiplier", StringComparison.Ordinal))
                    {
                        foreach (decimal value in values) total *= value;
                    }
                    else
                    {
                        foreach (decimal value in values) total += value;
                    }
                }
            }
            catch (OverflowException)
            {
                return NumericSummary(rule, EffectiveNumericSummaryStatus.NumericOverflow,
                    first, values, null, effective.Length);
            }
            return NumericSummary(rule, EffectiveNumericSummaryStatus.Reliable,
                first, values, total, effective.Length);
        }

        private static EffectiveContributionNumericSummary NumericSummary(
            EffectiveRuleContribution rule,
            EffectiveNumericSummaryStatus status,
            CustomEffectCalculationTerm sample,
            decimal[] values,
            decimal? total,
            int effectiveOccurrenceCount)
        {
            return new EffectiveContributionNumericSummary(
                rule.RuleId,
                rule.DisplayName,
                status,
                sample == null ? string.Empty : sample.MechanicKey,
                sample == null ? string.Empty : sample.Operation,
                sample == null ? string.Empty : sample.Unit,
                sample == null ? string.Empty : sample.TargetAttribute,
                sample == null ? null : sample.NeutralValue,
                values,
                total,
                effectiveOccurrenceCount,
                rule.UncertainAdditionalCount);
        }

        private static bool IsNumericOperation(string operation)
        {
            return string.Equals(operation, "multiplier", StringComparison.Ordinal)
                || string.Equals(operation, "additive", StringComparison.Ordinal)
                || string.Equals(operation, "flat", StringComparison.Ordinal);
        }

        private static bool IsConfirmedCalculationStatus(string status)
        {
            return string.Equals(status, "verified_direct_data", StringComparison.Ordinal);
        }

        internal EffectiveContributionRuleDefinition[] PrepareRules(
            IEnumerable<CustomEffectRule> rules)
        {
            CustomEffectRule[] source = (rules ?? Enumerable.Empty<CustomEffectRule>()).ToArray();
            if (source.Any(item => item == null))
                throw new ArgumentException("规则不能为空。", "rules");
            if (source.Select(item => item.RuleId).Distinct(StringComparer.Ordinal).Count() != source.Length)
                throw new ArgumentException("规则 ID 必须唯一。", "rules");
            List<EffectiveContributionRuleDefinition> result =
                new List<EffectiveContributionRuleDefinition>(source.Length);
            foreach (CustomEffectRule rule in source)
            {
                if (rule.TargetKind == CustomRuleTargetKind.EffectSelector)
                {
                    CustomEffectSelector selector = _catalog.GetSelector(rule.SelectorId);
                    if (selector == null)
                        throw new ArgumentException("规则引用不存在的选择项：" + rule.SelectorId, "rules");
                    result.Add(new EffectiveContributionRuleDefinition(rule, selector, null));
                }
                else
                {
                    OfficialPresetRelic preset = _catalog.GetPreset(rule.PresetItemId);
                    if (preset == null)
                        throw new ArgumentException("规则引用不存在的官方预设遗物：" + rule.PresetItemId, "rules");
                    result.Add(new EffectiveContributionRuleDefinition(rule, null, preset));
                }
            }
            return result.ToArray();
        }

        internal EffectiveContributionEvaluation Evaluate(
            IList<RelicInstance> relics,
            string characterId,
            EffectiveContributionRuleDefinition[] rules,
            Action checkpoint,
            bool includeDetails)
        {
            if (relics == null) throw new ArgumentNullException("relics");
            rules = rules ?? new EffectiveContributionRuleDefinition[0];
            List<WorkOccurrence> originals = Project(relics, characterId, rules, checkpoint);
            AssignLocalCoLocationPotential(originals);
            List<WorkExclusiveEffect> exclusiveEffects = ProjectExclusiveEffects(
                relics, characterId, checkpoint);
            ApplyOrdinaryAggregation(originals, checkpoint);
            int exclusivityUncertainty = ApplyExclusivity(
                originals, exclusiveEffects, checkpoint);

            List<WorkOccurrence> all = new List<WorkOccurrence>(originals);
            foreach (WorkOccurrence item in originals.SelectMany(value => value.SyntheticConfirmed))
                all.Add(item);

            EffectiveRuleContribution[] result = new EffectiveRuleContribution[rules.Length];
            for (int ruleIndex = 0; ruleIndex < rules.Length; ruleIndex++)
            {
                if (checkpoint != null) checkpoint();
                EffectiveContributionRuleDefinition rule = rules[ruleIndex];
                WorkOccurrence[] raw = originals.Where(item => item.RuleIndex == ruleIndex).ToArray();
                WorkOccurrence[] effective = all
                    .Where(item => item.RuleIndex == ruleIndex && item.IsEffective)
                    .OrderByDescending(item => item.Rank)
                    .ThenBy(item => item.RelicIndex)
                    .ThenBy(item => item.PhysicalOrdinal)
                    .ThenBy(item => item.EffectId)
                    .ToArray();
                int[] ranks = effective.Select(item => item.Rank).OrderByDescending(value => value).ToArray();
                int maximumRank = ranks.DefaultIfEmpty(0).Max();
                int highestRankCount = maximumRank == 0 ? 0 : ranks.Count(value => value == maximumRank);
                int uncertainAdditional = raw.Count(item => item.IsUncertainAdditional);
                RuleComparisonProfile profile = new RuleComparisonProfile(
                    rule.Source.RuleId,
                    rule.Source.SelectorId,
                    rule.Source.IsRequired,
                    rule.Source.QuantityTarget,
                    !rule.Source.IsRequired || effective.Length >= rule.Source.QuantityTarget,
                    maximumRank,
                    highestRankCount,
                    ranks,
                    effective.Length,
                    raw.Length,
                    uncertainAdditional);
                result[ruleIndex] = new EffectiveRuleContribution(
                    rule.Source.RuleId,
                    rule.Source.SelectorId,
                    rule.Selector == null ? rule.Preset.Name : rule.Selector.Name,
                    rule.Source.IsRequired,
                    rule.Source.QuantityTarget,
                    includeDetails
                        ? all.Where(item => item.RuleIndex == ruleIndex)
                            .OrderBy(item => item.RelicIndex)
                            .ThenBy(item => item.PhysicalOrdinal)
                            .ThenBy(item => item.EffectId)
                            .ThenByDescending(item => item.Rank)
                            .Select(item => item.ToPublic())
                            .ToArray()
                        : new EffectiveContributionOccurrence[0],
                    profile);
            }

            CoLocationProfile coLocation = BuildCoLocationProfile(all, relics.Count);
            EffectiveContributionEvaluation evaluation = new EffectiveContributionEvaluation(
                result, coLocation, exclusivityUncertainty);
            if (includeDetails)
                evaluation.SetNumericSummaries(result.Select(BuildNumericSummary).ToArray());
            return evaluation;
        }

        private List<WorkOccurrence> Project(
            IList<RelicInstance> relics,
            string characterId,
            EffectiveContributionRuleDefinition[] rules,
            Action checkpoint)
        {
            List<WorkOccurrence> result = new List<WorkOccurrence>();
            for (int relicIndex = 0; relicIndex < relics.Count; relicIndex++)
            {
                RelicInstance relic = relics[relicIndex];
                if (relic == null) continue;
                int[] effectIds = (relic.PositiveEffectIds ?? new int[0])
                    .Concat(relic.NegativeEffectIds ?? new int[0]).ToArray();
                for (int ruleIndex = 0; ruleIndex < rules.Length; ruleIndex++)
                {
                    if (checkpoint != null) checkpoint();
                    EffectiveContributionRuleDefinition rule = rules[ruleIndex];
                    if (rule.Preset != null)
                    {
                        if (rule.IsAvailableForRealMatching && relic.ItemId == rule.Preset.ItemId)
                            result.Add(WorkOccurrence.Preset(ruleIndex, relicIndex, relic, rule));
                        continue;
                    }
                    for (int physicalOrdinal = 0; physicalOrdinal < effectIds.Length; physicalOrdinal++)
                    {
                        int rank;
                        int effectId = effectIds[physicalOrdinal];
                        if (!rule.Ranks.TryGetValue(effectId, out rank)) continue;
                        CustomRuntimeEffect effect = _catalog.GetEffect(effectId);
                        result.Add(new WorkOccurrence(
                            ruleIndex, relicIndex, relic, effectId, physicalOrdinal,
                            rank, rule, effect, characterId));
                    }
                }
            }
            return result;
        }

        private List<WorkExclusiveEffect> ProjectExclusiveEffects(
            IList<RelicInstance> relics,
            string characterId,
            Action checkpoint)
        {
            List<WorkExclusiveEffect> result = new List<WorkExclusiveEffect>();
            for (int relicIndex = 0; relicIndex < relics.Count; relicIndex++)
            {
                RelicInstance relic = relics[relicIndex];
                if (relic == null) continue;
                int[] effectIds = (relic.PositiveEffectIds ?? new int[0])
                    .Concat(relic.NegativeEffectIds ?? new int[0]).ToArray();
                for (int physicalOrdinal = 0; physicalOrdinal < effectIds.Length; physicalOrdinal++)
                {
                    if (checkpoint != null) checkpoint();
                    CustomRuntimeEffect effect = _catalog.GetEffect(effectIds[physicalOrdinal]);
                    if (effect == null || !effect.ExclusivityId.HasValue) continue;
                    result.Add(new WorkExclusiveEffect(
                        relicIndex,
                        physicalOrdinal,
                        effect.RuntimeEffectId,
                        effect.ExclusivityId.Value,
                        effect.IsApplicableTo(characterId)));
                }
            }
            return result;
        }

        private static void ApplyOrdinaryAggregation(
            List<WorkOccurrence> occurrences,
            Action checkpoint)
        {
            foreach (IGrouping<string, WorkOccurrence> group in occurrences
                .Where(item => item.IsApplicable && !item.ExclusivityId.HasValue)
                .GroupBy(item => item.RuleIndex + "\u001f" + item.StackGroupKey, StringComparer.Ordinal))
            {
                if (checkpoint != null) checkpoint();
                WorkOccurrence[] values = group
                    .OrderByDescending(item => item.Rank)
                    .ThenByDescending(item => item.LocalCoLocationPotential)
                    .ThenBy(item => item.InstanceId)
                    .ThenBy(item => item.PhysicalOrdinal)
                    .ThenBy(item => item.EffectId)
                    .ToArray();
                WorkOccurrence sample = values[0];
                if (string.Equals(sample.AggregationRule, "cap_count", StringComparison.Ordinal))
                {
                    int cap = sample.MaxEffectiveCopies.HasValue ? sample.MaxEffectiveCopies.Value : 0;
                    for (int index = 0; index < values.Length; index++)
                        SetAggregationResult(values[index], index < cap,
                            EffectiveContributionExclusionReason.CapCountLimit, false);
                }
                else if (string.Equals(sample.AggregationRule, "single_instance", StringComparison.Ordinal))
                {
                    for (int index = 0; index < values.Length; index++)
                        SetAggregationResult(values[index], index == 0,
                            EffectiveContributionExclusionReason.SingleInstanceLimit, false);
                }
                else
                {
                    WorkOccurrence conservative = values
                        .OrderBy(item => item.Rank)
                        .ThenByDescending(item => item.LocalCoLocationPotential)
                        .ThenBy(item => item.InstanceId)
                        .ThenBy(item => item.PhysicalOrdinal)
                        .ThenBy(item => item.EffectId)
                        .First();
                    foreach (WorkOccurrence value in values)
                        SetAggregationResult(value, ReferenceEquals(value, conservative),
                            EffectiveContributionExclusionReason.UnknownAggregationAdditional,
                            !ReferenceEquals(value, conservative));
                }
            }
        }

        private static void SetAggregationResult(
            WorkOccurrence occurrence,
            bool effective,
            EffectiveContributionExclusionReason excludedReason,
            bool uncertainAdditional)
        {
            occurrence.IsEffective = effective;
            occurrence.ExclusionReason = effective
                ? EffectiveContributionExclusionReason.None
                : excludedReason;
            occurrence.IsUncertainAdditional = uncertainAdditional;
        }

        private static int ApplyExclusivity(
            List<WorkOccurrence> occurrences,
            List<WorkExclusiveEffect> exclusiveEffects,
            Action checkpoint)
        {
            int uncertainty = 0;
            foreach (IGrouping<int, WorkExclusiveEffect> group in exclusiveEffects
                .Where(item => item.IsApplicable)
                .GroupBy(item => item.ExclusivityId))
            {
                if (checkpoint != null) checkpoint();
                WorkExclusiveEffect[] physical = group
                    .GroupBy(item => item.PhysicalKey, StringComparer.Ordinal)
                    .Select(item => item.First())
                    .OrderBy(item => item.RelicIndex)
                    .ThenBy(item => item.PhysicalOrdinal)
                    .ThenBy(item => item.EffectId)
                    .ToArray();
                if (physical.Length == 0) continue;
                int[][] matchedRules = physical.Select(item => occurrences
                    .Where(value => value.IsApplicable
                        && string.Equals(value.PhysicalKey, item.PhysicalKey, StringComparison.Ordinal))
                    .Select(value => value.RuleIndex).Distinct().OrderBy(value => value).ToArray())
                    .ToArray();
                IEnumerable<int> confirmedRules = matchedRules[0];
                for (int index = 1; index < matchedRules.Length; index++)
                    confirmedRules = confirmedRules.Intersect(matchedRules[index]);

                foreach (WorkOccurrence value in occurrences.Where(item =>
                    item.IsApplicable && item.ExclusivityId == group.Key))
                {
                    value.IsEffective = false;
                    value.ExclusionReason = EffectiveContributionExclusionReason.ExclusivityNotConfirmed;
                }

                foreach (int ruleIndex in confirmedRules)
                {
                    WorkOccurrence[] options = occurrences.Where(item => item.IsApplicable
                            && item.ExclusivityId == group.Key && item.RuleIndex == ruleIndex)
                        .OrderBy(item => item.Rank)
                        .ThenBy(item => item.RelicIndex)
                        .ThenBy(item => item.PhysicalOrdinal)
                        .ThenBy(item => item.EffectId)
                        .ToArray();
                    if (physical.Length == 1)
                    {
                        WorkOccurrence selected = options[0];
                        selected.IsEffective = true;
                        selected.ExclusionReason = EffectiveContributionExclusionReason.None;
                    }
                    else
                    {
                        WorkOccurrence representative = options[0];
                        bool calculationReliable = options
                            .Select(CalculationSignature)
                            .Distinct(StringComparer.Ordinal).Count() == 1;
                        WorkOccurrence synthetic = representative.CreateSyntheticExclusive(
                            calculationReliable);
                        representative.SyntheticConfirmed.Add(synthetic);
                    }
                }

                if (physical.Length > 1 && matchedRules.Select(item => string.Join(",", item))
                    .Distinct(StringComparer.Ordinal).Count() > 1)
                    uncertainty++;
            }
            return uncertainty;
        }

        private static string CalculationSignature(WorkOccurrence value)
        {
            return string.Join("|", value.CalculationTerms.Select(item =>
                item.MechanicKey + "/" + item.Operation + "/"
                + (item.NumericValue.HasValue ? item.NumericValue.Value.ToString() : "?") + "/"
                + (item.NeutralValue.HasValue ? item.NeutralValue.Value.ToString() : "?") + "/"
                + item.Unit + "/" + item.TargetAttribute + "/" + item.VerificationStatus));
        }

        private static CoLocationProfile BuildCoLocationProfile(
            IEnumerable<WorkOccurrence> occurrences,
            int relicCount)
        {
            int[] counts = new int[relicCount];
            for (int relicIndex = 0; relicIndex < relicCount; relicIndex++)
            {
                Dictionary<string, int[]> edges = occurrences
                    .Where(item => item.RelicIndex == relicIndex
                        && item.IsEffective
                        && item.SelectorId != null
                        && item.PhysicalOrdinal >= 0
                        && item.PhysicalLocationConfirmed)
                    .GroupBy(item => item.PhysicalKey, StringComparer.Ordinal)
                    .ToDictionary(
                        item => item.Key,
                        item => item.Select(value => value.RuleIndex).Distinct().ToArray(),
                        StringComparer.Ordinal);
                counts[relicIndex] = MaximumDistinctRuleMatching(edges);
            }
            return new CoLocationProfile(counts);
        }

        private static void AssignLocalCoLocationPotential(
            IEnumerable<WorkOccurrence> occurrences)
        {
            foreach (IGrouping<int, WorkOccurrence> relic in occurrences
                .Where(item => item.IsApplicable
                    && item.SelectorId != null
                    && item.PhysicalOrdinal >= 0)
                .GroupBy(item => item.RelicIndex))
            {
                Dictionary<string, int[]> edges = relic
                    .GroupBy(item => item.PhysicalKey, StringComparer.Ordinal)
                    .ToDictionary(
                        item => item.Key,
                        item => item.Select(value => value.RuleIndex).Distinct().ToArray(),
                        StringComparer.Ordinal);
                int potential = MaximumDistinctRuleMatching(edges);
                foreach (WorkOccurrence item in relic)
                    item.LocalCoLocationPotential = potential;
            }
        }

        private static int MaximumDistinctRuleMatching(Dictionary<string, int[]> edges)
        {
            Dictionary<int, string> matchedPhysicalByRule = new Dictionary<int, string>();
            int count = 0;
            foreach (string physicalKey in edges.Keys.OrderBy(value => value, StringComparer.Ordinal))
            {
                HashSet<int> visitedRules = new HashSet<int>();
                if (TryMatch(physicalKey, edges, matchedPhysicalByRule, visitedRules)) count++;
            }
            return count;
        }

        private static bool TryMatch(
            string physicalKey,
            Dictionary<string, int[]> edges,
            Dictionary<int, string> matchedPhysicalByRule,
            HashSet<int> visitedRules)
        {
            foreach (int ruleIndex in edges[physicalKey].OrderBy(value => value))
            {
                if (!visitedRules.Add(ruleIndex)) continue;
                string existing;
                if (!matchedPhysicalByRule.TryGetValue(ruleIndex, out existing)
                    || TryMatch(existing, edges, matchedPhysicalByRule, visitedRules))
                {
                    matchedPhysicalByRule[ruleIndex] = physicalKey;
                    return true;
                }
            }
            return false;
        }

        private sealed class WorkOccurrence
        {
            public WorkOccurrence(
                int ruleIndex,
                int relicIndex,
                RelicInstance relic,
                int effectId,
                int physicalOrdinal,
                int rank,
                EffectiveContributionRuleDefinition rule,
                CustomRuntimeEffect effect,
                string characterId)
            {
                RuleIndex = ruleIndex;
                RelicIndex = relicIndex;
                InstanceId = relic.InstanceId;
                EffectId = effectId;
                PhysicalOrdinal = physicalOrdinal;
                Rank = rank;
                RuleId = rule.Source.RuleId;
                SelectorId = rule.Source.SelectorId;
                StackGroupKey = effect == null ? "runtime:" + effectId : effect.StackGroupKey;
                AggregationRule = effect == null ? "unknown" : effect.AggregationRule;
                MaxEffectiveCopies = effect == null ? null : effect.MaxEffectiveCopies;
                ExclusivityId = effect == null ? null : effect.ExclusivityId;
                VerificationStatus = effect == null ? string.Empty : effect.VerificationStatus;
                CalculationTerms = effect == null
                    ? new CustomEffectCalculationTerm[0]
                    : (effect.CalculationTerms ?? new CustomEffectCalculationTerm[0]).ToArray();
                CalculationTermsReliable = effect != null;
                IsApplicable = effect != null && effect.IsApplicableTo(characterId);
                ExclusionReason = effect == null
                    ? EffectiveContributionExclusionReason.CatalogUnavailable
                    : IsApplicable
                        ? EffectiveContributionExclusionReason.None
                        : EffectiveContributionExclusionReason.CharacterInapplicable;
                PhysicalLocationConfirmed = true;
                SyntheticConfirmed = new List<WorkOccurrence>();
            }

            private WorkOccurrence()
            {
                SyntheticConfirmed = new List<WorkOccurrence>();
            }

            public static WorkOccurrence Preset(
                int ruleIndex,
                int relicIndex,
                RelicInstance relic,
                EffectiveContributionRuleDefinition rule)
            {
                return new WorkOccurrence
                {
                    RuleIndex = ruleIndex,
                    RelicIndex = relicIndex,
                    InstanceId = relic.InstanceId,
                    EffectId = -relic.ItemId,
                    PhysicalOrdinal = -1,
                    Rank = 1,
                    RuleId = rule.Source.RuleId,
                    SelectorId = null,
                    StackGroupKey = "preset:" + relic.ItemId,
                    AggregationRule = "single_instance",
                    MaxEffectiveCopies = 1,
                    VerificationStatus = rule.Preset.VerificationStatus,
                    CalculationTerms = new CustomEffectCalculationTerm[0],
                    CalculationTermsReliable = false,
                    IsApplicable = true,
                    PhysicalLocationConfirmed = true
                };
            }

            public WorkOccurrence CreateSyntheticExclusive(bool calculationReliable)
            {
                return new WorkOccurrence
                {
                    RuleIndex = RuleIndex,
                    RelicIndex = RelicIndex,
                    InstanceId = InstanceId,
                    EffectId = EffectId,
                    PhysicalOrdinal = -1,
                    Rank = Rank,
                    RuleId = RuleId,
                    SelectorId = SelectorId,
                    StackGroupKey = "exclusive:" + ExclusivityId.Value,
                    AggregationRule = "single_instance",
                    MaxEffectiveCopies = 1,
                    ExclusivityId = null,
                    VerificationStatus = VerificationStatus,
                    // Keep the representative structured terms even when the
                    // mutually-exclusive physical options disagree.  The
                    // reliability flag prevents a fabricated total while still
                    // distinguishing a real conflict from an old pack that has
                    // no calculation data at all.
                    CalculationTerms = CalculationTerms.ToArray(),
                    CalculationTermsReliable = calculationReliable,
                    IsApplicable = true,
                    IsEffective = true,
                    ExclusionReason = EffectiveContributionExclusionReason.None,
                    PhysicalLocationConfirmed = false,
                    IsSynthetic = true
                };
            }

            public EffectiveContributionOccurrence ToPublic()
            {
                return new EffectiveContributionOccurrence(
                    RuleId, SelectorId, RuleIndex, InstanceId, EffectId, PhysicalOrdinal,
                    StackGroupKey, AggregationRule, Rank, IsEffective, ExclusionReason,
                    IsUncertainAdditional, PhysicalLocationConfirmed, VerificationStatus,
                    CalculationTerms.ToArray(), CalculationTermsReliable);
            }

            public string PhysicalKey
            {
                get { return RelicIndex + "/" + PhysicalOrdinal + "/" + EffectId; }
            }

            public int RuleIndex;
            public int RelicIndex;
            public int InstanceId;
            public int EffectId;
            public int PhysicalOrdinal;
            public int Rank;
            public int LocalCoLocationPotential;
            public string RuleId;
            public string SelectorId;
            public string StackGroupKey;
            public string AggregationRule;
            public int? MaxEffectiveCopies;
            public int? ExclusivityId;
            public string VerificationStatus;
            public CustomEffectCalculationTerm[] CalculationTerms;
            public bool CalculationTermsReliable;
            public bool IsApplicable;
            public bool IsEffective;
            public EffectiveContributionExclusionReason ExclusionReason;
            public bool IsUncertainAdditional;
            public bool PhysicalLocationConfirmed;
            public bool IsSynthetic;
            public List<WorkOccurrence> SyntheticConfirmed;
        }

        private sealed class WorkExclusiveEffect
        {
            public WorkExclusiveEffect(
                int relicIndex,
                int physicalOrdinal,
                int effectId,
                int exclusivityId,
                bool isApplicable)
            {
                RelicIndex = relicIndex;
                PhysicalOrdinal = physicalOrdinal;
                EffectId = effectId;
                ExclusivityId = exclusivityId;
                IsApplicable = isApplicable;
            }

            public string PhysicalKey
            {
                get { return RelicIndex + "/" + PhysicalOrdinal + "/" + EffectId; }
            }

            public int RelicIndex;
            public int PhysicalOrdinal;
            public int EffectId;
            public int ExclusivityId;
            public bool IsApplicable;
        }

    }
}
