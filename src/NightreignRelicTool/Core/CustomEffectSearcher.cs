using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NightreignRelicTool.Core
{
    public enum CustomRuleTargetKind
    {
        EffectSelector,
        OfficialPresetRelic
    }

    public sealed class CustomEffectRule
    {
        public CustomEffectRule(string ruleId, string selectorId, bool isRequired, int quantityTarget = 1)
        {
            if (string.IsNullOrWhiteSpace(ruleId)) throw new ArgumentException("规则 ID 不能为空。", "ruleId");
            if (string.IsNullOrWhiteSpace(selectorId)) throw new ArgumentException("选择项 ID 不能为空。", "selectorId");
            if (quantityTarget < 1 || quantityTarget > 6) throw new ArgumentOutOfRangeException("quantityTarget");
            RuleId = ruleId;
            TargetKind = CustomRuleTargetKind.EffectSelector;
            SelectorId = selectorId;
            IsRequired = isRequired;
            QuantityTarget = quantityTarget;
        }

        public CustomEffectRule(string ruleId, int presetItemId, bool isRequired)
        {
            if (string.IsNullOrWhiteSpace(ruleId)) throw new ArgumentException("规则 ID 不能为空。", "ruleId");
            RuleId = ruleId;
            TargetKind = CustomRuleTargetKind.OfficialPresetRelic;
            PresetItemId = presetItemId;
            IsRequired = isRequired;
            QuantityTarget = 1;
        }

        public string RuleId { get; private set; }
        public CustomRuleTargetKind TargetKind { get; private set; }
        public string SelectorId { get; private set; }
        public int PresetItemId { get; private set; }
        public bool IsRequired { get; private set; }
        public int QuantityTarget { get; private set; }
    }

    /// <summary>
    /// The complete, per-user-rule preference profile used by the exact
    /// lexicographic comparison.  The source rule order is retained; required
    /// and wanted profiles are separated only while comparing two builds.
    /// </summary>
    public sealed class RuleComparisonProfile
    {
        public RuleComparisonProfile(
            string ruleId,
            string selectorId,
            bool isRequired,
            int quantityTarget,
            bool requiredTargetReached,
            int maximumVariantPreferenceRank,
            int highestVariantRankCount,
            int[] completeVariantRankDistribution,
            int effectiveCount,
            int rawCount,
            int uncertainAdditionalCount)
        {
            RuleId = ruleId ?? string.Empty;
            SelectorId = selectorId;
            IsRequired = isRequired;
            QuantityTarget = quantityTarget;
            RequiredTargetReached = requiredTargetReached;
            MaximumVariantPreferenceRank = maximumVariantPreferenceRank;
            HighestVariantRankCount = highestVariantRankCount;
            CompleteVariantRankDistribution = completeVariantRankDistribution ?? new int[0];
            EffectiveCount = effectiveCount;
            RawCount = rawCount;
            UncertainAdditionalCount = uncertainAdditionalCount;
        }

        public string RuleId { get; private set; }
        public string SelectorId { get; private set; }
        public bool IsRequired { get; private set; }
        public int QuantityTarget { get; private set; }
        public bool RequiredTargetReached { get; private set; }
        public int MaximumVariantPreferenceRank { get; private set; }
        public int HighestVariantRankCount { get; private set; }
        public int[] CompleteVariantRankDistribution { get; private set; }
        public int EffectiveCount { get; private set; }
        public int RawCount { get; private set; }
        public int UncertainAdditionalCount { get; private set; }
    }

    /// <summary>
    /// Describes how confirmed effective, physically distinct selected effects
    /// are concentrated on the six relics.  Counts are stored in assignment
    /// order and also as a descending comparison profile.
    /// </summary>
    public sealed class CoLocationProfile
    {
        public CoLocationProfile(int[] selectedEffectCountsByRelic)
        {
            SelectedEffectCountsByRelic = selectedEffectCountsByRelic ?? new int[0];
            DescendingSelectedEffectCounts = SelectedEffectCountsByRelic
                .OrderByDescending(value => value)
                .ToArray();
            DescendingMultiMatchCounts = SelectedEffectCountsByRelic
                .Where(value => value >= 2)
                .OrderByDescending(value => value)
                .ToArray();
            MultiMatchRelicCount = SelectedEffectCountsByRelic.Count(value => value >= 2);
        }

        public int[] SelectedEffectCountsByRelic { get; private set; }
        public int[] DescendingSelectedEffectCounts { get; private set; }
        public int[] DescendingMultiMatchCounts { get; private set; }
        public int MultiMatchRelicCount { get; private set; }
    }

    public sealed class CustomComparisonVector
    {
        public CustomComparisonVector(
            int missingRequired,
            int coveredRuleCount,
            int[] variantRanks,
            int[] quantities,
            int negativeEffectCount,
            int relicsWithNegativeCount,
            int uncertaintyCount,
            int vesselId,
            int[] stableInstanceIds)
            : this(
                missingRequired, coveredRuleCount, variantRanks,
                (variantRanks ?? new int[0]).Select(item => new[] { item }).ToArray(),
                quantities, quantities, new int[(quantities ?? new int[0]).Length],
                negativeEffectCount, relicsWithNegativeCount, uncertaintyCount, vesselId, stableInstanceIds,
                CreateCompatibilityProfiles(variantRanks, quantities), new CoLocationProfile(new int[0]))
        {
        }

        internal CustomComparisonVector(
            int missingRequired,
            int coveredRuleCount,
            int[] variantRanks,
            int[][] variantRankProfiles,
            int[] rawOccurrences,
            int[] effectiveContributions,
            int[] uncertainAdditionalCounts,
            int negativeEffectCount,
            int relicsWithNegativeCount,
            int uncertaintyCount,
            int vesselId,
            int[] stableInstanceIds,
            RuleComparisonProfile[] ruleComparisonProfiles,
            CoLocationProfile coLocationProfile)
        {
            MissingRequired = missingRequired;
            CoveredRuleCount = coveredRuleCount;
            VariantRanks = variantRanks ?? new int[0];
            VariantRankProfiles = variantRankProfiles ?? new int[0][];
            RawOccurrences = rawOccurrences ?? new int[0];
            EffectiveContributions = effectiveContributions ?? new int[0];
            UncertainAdditionalCounts = uncertainAdditionalCounts ?? new int[0];
            ContributionLayers = BuildContributionLayers(EffectiveContributions);
            NegativeEffectCount = negativeEffectCount;
            RelicsWithNegativeCount = relicsWithNegativeCount;
            UncertaintyCount = uncertaintyCount;
            VesselId = vesselId;
            StableInstanceIds = stableInstanceIds ?? new int[0];
            RuleComparisonProfiles = ruleComparisonProfiles ?? new RuleComparisonProfile[0];
            CoLocationProfile = coLocationProfile ?? new CoLocationProfile(new int[0]);
        }

        public int MissingRequired { get; private set; }
        public int CoveredRuleCount { get; private set; }
        public int[] VariantRanks { get; private set; }
        public int[][] VariantRankProfiles { get; private set; }
        public int[] RawOccurrences { get; private set; }
        public int[] EffectiveContributions { get; private set; }
        public int[] UncertainAdditionalCounts { get; private set; }
        public int[] ContributionLayers { get; private set; }
        // Kept as a compatibility alias for diagnostics. It is never target-capped.
        public int[] Quantities { get { return RawOccurrences; } }
        internal int[] TargetSaturatedQuantities { get { return EffectiveContributions; } }
        public int NegativeEffectCount { get; private set; }
        public int RelicsWithNegativeCount { get; private set; }
        public int UncertaintyCount { get; private set; }
        public int VesselId { get; private set; }
        public int[] StableInstanceIds { get; private set; }
        public RuleComparisonProfile[] RuleComparisonProfiles { get; private set; }
        public CoLocationProfile CoLocationProfile { get; private set; }

        public static int Compare(CustomComparisonVector left, CustomComparisonVector right)
        {
            int value = left.MissingRequired.CompareTo(right.MissingRequired);
            if (value != 0) return value;
            value = CompareRuleProfiles(left.RuleComparisonProfiles, right.RuleComparisonProfiles, true);
            if (value != 0) return value;
            value = CompareRuleProfiles(left.RuleComparisonProfiles, right.RuleComparisonProfiles, false);
            if (value != 0) return value;
            value = CompareDescending(
                left.CoLocationProfile.DescendingMultiMatchCounts,
                right.CoLocationProfile.DescendingMultiMatchCounts);
            if (value != 0) return value;
            // These aggregate positives are intentionally after every ordered
            // user-rule profile and co-location.  In a complete profile they
            // are normally equal, but retain deterministic compatibility with
            // vectors produced by older callers.
            value = right.CoveredRuleCount.CompareTo(left.CoveredRuleCount);
            if (value != 0) return value;
            value = CompareDescending(left.EffectiveContributions, right.EffectiveContributions);
            if (value != 0) return value;
            value = left.NegativeEffectCount.CompareTo(right.NegativeEffectCount);
            if (value != 0) return value;
            value = left.RelicsWithNegativeCount.CompareTo(right.RelicsWithNegativeCount);
            if (value != 0) return value;
            value = CompareAscending(left.UncertainAdditionalCounts, right.UncertainAdditionalCounts);
            if (value != 0) return value;
            value = left.UncertaintyCount.CompareTo(right.UncertaintyCount);
            if (value != 0) return value;
            value = left.VesselId.CompareTo(right.VesselId);
            if (value != 0) return value;
            return CompareAscending(left.StableInstanceIds, right.StableInstanceIds);
        }

        private static int CompareRuleProfiles(
            RuleComparisonProfile[] left,
            RuleComparisonProfile[] right,
            bool required)
        {
            left = left ?? new RuleComparisonProfile[0];
            right = right ?? new RuleComparisonProfile[0];
            int leftIndex = NextRuleIndex(left, 0, required);
            int rightIndex = NextRuleIndex(right, 0, required);
            while (leftIndex >= 0 && rightIndex >= 0)
            {
                RuleComparisonProfile leftValue = left[leftIndex];
                RuleComparisonProfile rightValue = right[rightIndex];
                if (required)
                {
                    int reached = rightValue.RequiredTargetReached.CompareTo(leftValue.RequiredTargetReached);
                    if (reached != 0) return reached;
                }
                int value = rightValue.MaximumVariantPreferenceRank
                    .CompareTo(leftValue.MaximumVariantPreferenceRank);
                if (value != 0) return value;
                value = rightValue.HighestVariantRankCount.CompareTo(leftValue.HighestVariantRankCount);
                if (value != 0) return value;
                value = CompareDescending(
                    leftValue.CompleteVariantRankDistribution,
                    rightValue.CompleteVariantRankDistribution);
                if (value != 0) return value;
                value = rightValue.EffectiveCount.CompareTo(leftValue.EffectiveCount);
                if (value != 0) return value;
                leftIndex = NextRuleIndex(left, leftIndex + 1, required);
                rightIndex = NextRuleIndex(right, rightIndex + 1, required);
            }
            if (leftIndex >= 0) return -1;
            if (rightIndex >= 0) return 1;
            return 0;
        }

        private static int NextRuleIndex(
            RuleComparisonProfile[] values,
            int start,
            bool required)
        {
            for (int index = start; index < values.Length; index++)
                if (values[index].IsRequired == required) return index;
            return -1;
        }

        private static RuleComparisonProfile[] CreateCompatibilityProfiles(
            int[] variantRanks,
            int[] quantities)
        {
            int[] ranks = variantRanks ?? new int[0];
            int[] counts = quantities ?? new int[0];
            int length = Math.Max(ranks.Length, counts.Length);
            RuleComparisonProfile[] result = new RuleComparisonProfile[length];
            for (int index = 0; index < length; index++)
            {
                int rank = index < ranks.Length ? ranks[index] : 0;
                int count = index < counts.Length ? counts[index] : 0;
                int[] distribution = rank <= 0 || count <= 0
                    ? new int[0]
                    : Enumerable.Repeat(rank, count).ToArray();
                result[index] = new RuleComparisonProfile(
                    "compatibility-" + index, null, false, 1, true,
                    rank, count, distribution, count, count, 0);
            }
            return result;
        }

        private static int[] BuildContributionLayers(int[] effective)
        {
            if (effective == null || effective.Length == 0) return new int[0];
            int maximum = Math.Min(6, effective.Max());
            if (maximum <= 1) return new int[0];
            int[] result = new int[maximum - 1];
            for (int copy = 2; copy <= maximum; copy++)
                result[copy - 2] = effective.Count(value => value >= copy);
            return result;
        }

        private static int CompareRankProfiles(int[][] left, int[][] right)
        {
            int length = Math.Min(left.Length, right.Length);
            for (int index = 0; index < length; index++)
            {
                int value = CompareDescending(left[index] ?? new int[0], right[index] ?? new int[0]);
                if (value != 0) return value;
            }
            return right.Length.CompareTo(left.Length);
        }

        private static int CompareDescending(int[] left, int[] right)
        {
            int length = Math.Min(left.Length, right.Length);
            for (int index = 0; index < length; index++)
            {
                int value = right[index].CompareTo(left[index]);
                if (value != 0) return value;
            }
            return right.Length.CompareTo(left.Length);
        }

        private static int CompareAscending(int[] left, int[] right)
        {
            int length = Math.Min(left.Length, right.Length);
            for (int index = 0; index < length; index++)
            {
                int value = left[index].CompareTo(right[index]);
                if (value != 0) return value;
            }
            return left.Length.CompareTo(right.Length);
        }
    }

    public sealed class CustomBuildWarning
    {
        public CustomBuildWarning(int instanceId, int? runtimeEffectId, string message)
        {
            InstanceId = instanceId;
            RuntimeEffectId = runtimeEffectId;
            Message = message;
        }

        public int InstanceId { get; private set; }
        public int? RuntimeEffectId { get; private set; }
        public string Message { get; private set; }
    }

    public sealed class CustomSearchBuild
    {
        public CustomSearchBuild(
            VesselDefinition vessel,
            SlotAssignment[] assignments,
            CustomComparisonVector comparison,
            CustomBuildWarning[] warnings)
            : this(vessel, assignments, comparison, warnings, null)
        {
        }

        public CustomSearchBuild(
            VesselDefinition vessel,
            SlotAssignment[] assignments,
            CustomComparisonVector comparison,
            CustomBuildWarning[] warnings,
            EffectiveContributionEvaluation contributionEvaluation)
        {
            Vessel = vessel;
            Assignments = assignments;
            Comparison = comparison;
            Warnings = warnings ?? new CustomBuildWarning[0];
            ContributionEvaluation = contributionEvaluation;
        }

        public VesselDefinition Vessel { get; private set; }
        public SlotAssignment[] Assignments { get; private set; }
        public CustomComparisonVector Comparison { get; private set; }
        public CustomBuildWarning[] Warnings { get; private set; }
        public EffectiveContributionEvaluation ContributionEvaluation { get; private set; }
    }

    public enum CustomSearchStatus
    {
        CompletedExact,
        NoLegalBuild,
        Canceled,
        BudgetExceeded,
        Failed
    }

    public sealed class CustomSearchLimits
    {
        public const long DefaultMaxElapsedMilliseconds = 10000;
        public const long DefaultMaxVisitedStates = 20000000;
        public const long DefaultMaxTransitions = 20000000;

        public CustomSearchLimits()
            : this(DefaultMaxElapsedMilliseconds, DefaultMaxVisitedStates, DefaultMaxTransitions)
        {
        }

        public CustomSearchLimits(long maxElapsedMilliseconds, long maxVisitedStates, long maxTransitions)
        {
            if (maxElapsedMilliseconds <= 0) throw new ArgumentOutOfRangeException("maxElapsedMilliseconds");
            if (maxVisitedStates <= 0) throw new ArgumentOutOfRangeException("maxVisitedStates");
            if (maxTransitions <= 0) throw new ArgumentOutOfRangeException("maxTransitions");
            MaxElapsedMilliseconds = maxElapsedMilliseconds;
            MaxVisitedStates = maxVisitedStates;
            MaxTransitions = maxTransitions;
        }

        public long MaxElapsedMilliseconds { get; private set; }
        public long MaxVisitedStates { get; private set; }
        public long MaxTransitions { get; private set; }
    }

    public sealed class CustomSearchResponse
    {
        public CustomSearchResponse(CustomSearchBuild[] builds, string[] warnings, long elapsedMilliseconds, long evaluatedBuilds)
            : this(
                builds,
                warnings,
                elapsedMilliseconds,
                evaluatedBuilds,
                builds != null && builds.Length != 0
                    ? CustomSearchStatus.CompletedExact
                    : CustomSearchStatus.NoLegalBuild,
                0,
                0,
                0,
                0,
                0)
        {
        }

        public CustomSearchResponse(
            CustomSearchBuild[] builds,
            string[] warnings,
            long elapsedMilliseconds,
            long evaluatedBuilds,
            CustomSearchStatus status,
            int originalCandidateCount,
            int compressedCandidateCount,
            long peakStateCount,
            long visitedStateCount,
            long transitionCount)
        {
            Builds = builds ?? new CustomSearchBuild[0];
            Warnings = warnings ?? new string[0];
            ElapsedMilliseconds = elapsedMilliseconds;
            EvaluatedBuilds = evaluatedBuilds;
            Status = status;
            IsExact = status == CustomSearchStatus.CompletedExact || status == CustomSearchStatus.NoLegalBuild;
            OriginalCandidateCount = originalCandidateCount;
            CompressedCandidateCount = compressedCandidateCount;
            PeakStateCount = peakStateCount;
            VisitedStateCount = visitedStateCount;
            TransitionCount = transitionCount;
        }

        public CustomSearchBuild[] Builds { get; private set; }
        public string[] Warnings { get; private set; }
        public long ElapsedMilliseconds { get; private set; }
        public long EvaluatedBuilds { get; private set; }
        public CustomSearchStatus Status { get; private set; }
        public bool IsExact { get; private set; }
        public int OriginalCandidateCount { get; private set; }
        public int CompressedCandidateCount { get; private set; }
        public long PeakStateCount { get; private set; }
        public long VisitedStateCount { get; private set; }
        public long TransitionCount { get; private set; }
        public CustomSearchDiagnostics Diagnostics { get; internal set; }
    }

    public sealed partial class CustomEffectSearcher
    {
        private const string UnavailableForRealMatchingWarning = "待验证／暂不可用于真实匹配";
        private const string UnknownRepeatedContributionWarning =
            "聚合规则待验证：仅一份计入有效贡献，额外重复已标记为不确定。";
        private const string BudgetExceededWarning = "检索超过安全预算，未返回未经完整证明的组合。";
        private const string FailedWarning = "检索因运行时数据约束失败，未返回未经完整证明的组合。";
        private readonly RuntimeModel _model;
        private readonly CustomEffectCatalog _catalog;
        private readonly CustomSearchLimits _limits;
        private readonly EffectiveContributionEvaluator _contributionEvaluator;

        public CustomEffectSearcher(RuntimeModel model, CustomEffectCatalog catalog)
            : this(model, catalog, new CustomSearchLimits())
        {
        }

        public CustomEffectSearcher(RuntimeModel model, CustomEffectCatalog catalog, CustomSearchLimits limits)
        {
            _model = model ?? throw new ArgumentNullException("model");
            _catalog = catalog ?? throw new ArgumentNullException("catalog");
            _limits = limits ?? throw new ArgumentNullException("limits");
            _contributionEvaluator = new EffectiveContributionEvaluator(_catalog);
            if (!string.Equals(model.RegulationVersion, catalog.GameVersion, StringComparison.Ordinal))
                throw new ArgumentException("正式器皿数据与自定义词条数据的 Regulation 版本不一致。");
        }

        public CustomSearchResponse Search(
            CharacterInventory inventory,
            CharacterDefinition character,
            IEnumerable<CustomEffectRule> rules)
        {
            return Search(inventory, character, rules, CancellationToken.None);
        }

        public CustomSearchResponse Search(
            CharacterInventory inventory,
            CharacterDefinition character,
            IEnumerable<CustomEffectRule> rules,
            CancellationToken cancellationToken)
        {
            return Search(inventory, character, rules, cancellationToken, null);
        }

        public CustomSearchResponse Search(
            CharacterInventory inventory,
            CharacterDefinition character,
            IEnumerable<CustomEffectRule> rules,
            CancellationToken cancellationToken,
            CustomSearchOptions options)
        {
            if (options == null || !options.EnableDiagnostics)
                return SearchCore(inventory, character, rules, cancellationToken, null, options);
            CustomSearchDiagnostics diagnostics = new CustomSearchDiagnostics();
            diagnostics.Start();
            long started = Stopwatch.GetTimestamp();
            try
            {
                CustomSearchResponse response = SearchCore(inventory, character, rules, cancellationToken, diagnostics, options);
                response.Diagnostics = diagnostics;
                if (response.Status == CustomSearchStatus.CompletedExact)
                    diagnostics.Outcome = CustomSearchDiagnosticOutcome.Completed;
                else if (response.Status == CustomSearchStatus.NoLegalBuild)
                    diagnostics.Outcome = CustomSearchDiagnosticOutcome.NoLegalBuild;
                else if (response.Status == CustomSearchStatus.Failed)
                    diagnostics.Outcome = CustomSearchDiagnosticOutcome.DataFailure;
                return response;
            }
            catch (OperationCanceledException) { diagnostics.Outcome = CustomSearchDiagnosticOutcome.Canceled; throw; }
            catch (CustomEffectDataException) { diagnostics.Outcome = CustomSearchDiagnosticOutcome.DataFailure; throw; }
            catch (ArgumentException) { diagnostics.Outcome = CustomSearchDiagnosticOutcome.InvalidRequest; throw; }
            catch { diagnostics.Outcome = CustomSearchDiagnosticOutcome.UnexpectedFailure; throw; }
            finally
            {
                diagnostics.ElapsedMilliseconds = CustomSearchDiagnostics.MillisecondsSince(started);
                diagnostics.Finish();
                if (options.DiagnosticsCompleted != null)
                {
                    try { options.DiagnosticsCompleted(diagnostics); }
                    catch { /* A diagnostic observer cannot alter exact results or cancellation. */ }
                }
            }
        }

        private CustomSearchResponse SearchCore(
            CharacterInventory inventory,
            CharacterDefinition character,
            IEnumerable<CustomEffectRule> rules,
            CancellationToken cancellationToken,
            CustomSearchDiagnostics diagnostics,
            CustomSearchOptions options)
        {
            if (inventory == null) throw new ArgumentNullException("inventory");
            if (character == null) throw new ArgumentNullException("character");
            cancellationToken.ThrowIfCancellationRequested();
            Stopwatch stopwatch = Stopwatch.StartNew();
            EnsureUniqueInstanceIds(inventory, cancellationToken);
            PreparedRule[] prepared = PrepareRules(rules);
            SearchBudget budget = new SearchBudget(stopwatch, cancellationToken, _limits, diagnostics, options);
            budget.ConfigureStateExperiment(prepared);
            if (diagnostics != null)
            {
                diagnostics.RuleCount = prepared.Length;
                diagnostics.RequiredRuleCount = prepared.Count(item => item.Source.IsRequired);
                diagnostics.WantedRuleCount = prepared.Length - diagnostics.RequiredRuleCount;
                diagnostics.TerminalPruningEnabled = budget.TerminalPruningEnabled;
                diagnostics.IntermediatePruningEnabled = budget.IntermediatePruningEnabled;
                diagnostics.RequiredGapReferenceOrderingEnabled =
                    budget.RequiredGapReferenceOrderingEnabled;
                diagnostics.DetailedCostDiagnosticsEnabled = budget.DetailedCostDiagnostics;
                diagnostics.ExactCapacityStateMergesEnabled = budget.StateExperiment != null
                    && budget.StateExperiment.ExactCapacityMerges;
                diagnostics.CompiledSecondaryComparisonEnabled = budget.StateExperiment != null
                    && budget.StateExperiment.CompiledSecondaryComparison;
                diagnostics.DeferredFrontierStatesEnabled = budget.StateExperiment != null
                    && budget.StateExperiment.DeferredFrontierStates;
                diagnostics.CostCheckSampleStride = budget.DetailedCostDiagnostics ? 4096 : 0;
                diagnostics.CostMergeSampleStride = budget.DetailedCostDiagnostics ? 256 : 0;
            }
            List<string> warnings = new List<string>();
            long evaluated = 0;
            int originalCandidateCount = 0;
            int compressedCandidateCount = 0;
            try
            {
                warnings.AddRange(ValidateRequest(inventory, prepared));
                if (budget.OracleTest != null)
                    budget.OracleQuery = new OracleQueryContext(this, inventory, character, prepared, budget);
                if (budget.GuidanceTest != null)
                    budget.PreviousHintQuery = new PreviousHintQueryContext(this, inventory, character, budget);
                if (diagnostics != null) diagnostics.BeginStage("CandidateGeneration");
                List<Candidate> projected = new List<Candidate>();
                foreach (RelicInstance relic in inventory.Relics)
                {
                    budget.Checkpoint();
                    if (relic.ColorId < 0 || relic.ColorId > 3)
                    {
                        if (diagnostics != null) diagnostics.InvalidColorCandidateCount++;
                        continue;
                    }
                    originalCandidateCount++;
                    projected.Add(new Candidate(relic, prepared, character.Id, _catalog, budget));
                    if (diagnostics != null) diagnostics.Creations.Candidates++;
                }

                if (diagnostics != null) diagnostics.BeginStage("SemanticConfiguration");
                ConfigureCandidateSemanticStates(projected, prepared);

                if (diagnostics != null) diagnostics.BeginStage("CandidateCompression");
                Candidate[] candidates = CompressCandidates(projected);
                compressedCandidateCount = candidates.Length;
                budget.Checkpoint();

                if (diagnostics != null) diagnostics.BeginStage("AllVesselSearches");
                List<CustomSearchBuild> perVessel = new List<CustomSearchBuild>();
                TopThreePool globalPool = budget.TopThreeThresholdEnabled && prepared.Length > 0
                    && character.EligibleVesselIds.Distinct().Count() > 3
                    ? new TopThreePool(prepared, candidates, budget, _catalog) : null;
                foreach (int vesselId in character.EligibleVesselIds)
                {
                    budget.Checkpoint();
                    VesselDefinition vessel = _model.GetVessel(vesselId);
                    if (vessel == null) continue;
                    CustomSearchVesselDiagnostics vesselDiagnostics = diagnostics == null ? null
                        : CreateVesselDiagnostics(vessel, candidates);
                    if (vesselDiagnostics != null && budget.DetailedCostDiagnostics)
                        vesselDiagnostics.Cost = new CustomSearchCostDiagnostics();
                    bool globallyExcluded = false;
                    long vesselStart = diagnostics == null ? 0 : Stopwatch.GetTimestamp();
                    long stateStart = diagnostics == null ? 0 : budget.VisitedStateCount;
                    long transitionStart = diagnostics == null ? 0 : budget.TransitionCount;
                    if (diagnostics != null)
                    {
                        diagnostics.ActiveVesselId = vesselId;
                        diagnostics.ActivePhaseIndex = null;
                        diagnostics.ActiveOperation = "VesselSetup";
                        diagnostics.Vessels.Add(vesselDiagnostics);
                        vesselDiagnostics.ProofStatus = "Unresolved";
                        vesselDiagnostics.ManagedMemoryBefore = GC.GetTotalMemory(false);
                        vesselDiagnostics.PrivateMemoryBefore = CustomSearchDiagnostics.ReadPrivateMemory();
                    }
                    try
                    {
                    if (prepared.Length == 0)
                    {
                        ZeroRuleVesselSearch zeroRuleSearch = new ZeroRuleVesselSearch(
                            this, candidates, vessel, character.Id, prepared, budget, vesselDiagnostics);
                        zeroRuleSearch.Run();
                        evaluated += zeroRuleSearch.EvaluatedBuilds;
                        if (zeroRuleSearch.Best != null) perVessel.Add(zeroRuleSearch.Best);
                    }
                    else
                    {
                        FrontierVesselSearch search = new FrontierVesselSearch(
                            this, prepared, candidates, vessel, character.Id, budget, vesselDiagnostics);
                        search.SetGlobalThreshold(globalPool);
                        search.Run();
                        search.FinishGlobalProof();
                        globallyExcluded = search.GloballyExcluded;
                        evaluated += search.EvaluatedBuilds;
                        if (search.Best != null && !globallyExcluded)
                        {
                            perVessel.Add(search.Best);
                            if (globalPool != null) globalPool.Add(search.Best);
                        }
                    }
                    if (vesselDiagnostics != null)
                    {
                        vesselDiagnostics.Completed = !globallyExcluded;
                        vesselDiagnostics.ProofStatus = globallyExcluded ? "GloballyExcluded"
                            : perVessel.Any(b => b.Vessel.Id == vesselId) ? "OwnOptimum" : "Infeasible";
                        if (globallyExcluded) diagnostics.GloballyExcludedVesselCount++;
                        else diagnostics.CompletedVesselCount++;
                    }
                    }
                    finally
                    {
                        if (vesselDiagnostics != null)
                        {
                            vesselDiagnostics.ManagedMemoryAfter = GC.GetTotalMemory(false);
                            vesselDiagnostics.PrivateMemoryAfter = CustomSearchDiagnostics.ReadPrivateMemory();
                            vesselDiagnostics.ElapsedMilliseconds = CustomSearchDiagnostics.MillisecondsSince(vesselStart);
                            vesselDiagnostics.VisitedStateCount = budget.VisitedStateCount - stateStart;
                            vesselDiagnostics.TransitionCount = budget.TransitionCount - transitionStart;
                        }
                    }
                    if (diagnostics != null)
                    {
                        diagnostics.ActiveVesselId = null;
                        diagnostics.ActivePhaseIndex = null;
                        diagnostics.ActiveOperation = "BetweenVessels";
                    }
                }
                if (diagnostics != null)
                {
                    diagnostics.ActiveVesselId = null;
                    diagnostics.ActivePhaseIndex = null;
                    diagnostics.BeginStage("FinalSortAndAssembly");
                }
                perVessel.Sort(CompareBuilds);
                CustomSearchBuild[] builds = perVessel.Take(3).ToArray();
                CustomSearchStatus status;
                if (builds.Length == 0)
                {
                    status = CustomSearchStatus.NoLegalBuild;
                    warnings.Add("当前仓库没有满足三颗普通、三颗深夜、实例唯一及器皿颜色限制的合法组合。");
                }
                else
                {
                    status = CustomSearchStatus.CompletedExact;
                    if (builds[0].Comparison.MissingRequired > 0)
                        warnings.Add(_catalog.Warning("requiredUnavailable"));
                }
                if ((budget.OracleQuery != null && budget.OracleTest.Captured != null)
                    || (status == CustomSearchStatus.CompletedExact && budget.PreviousHintQuery != null
                        && budget.GuidanceTest.Captured != null)
                    || (options != null && options.CompletedVesselsForTest != null))
                    PublishOracleTestResults(budget, options, perVessel);
                budget.Checkpoint();
                stopwatch.Stop();
                return new CustomSearchResponse(
                    builds,
                    warnings.Distinct().ToArray(),
                    stopwatch.ElapsedMilliseconds,
                    evaluated,
                    status,
                    originalCandidateCount,
                    compressedCandidateCount,
                    budget.PeakStateCount,
                    budget.VisitedStateCount,
                    budget.TransitionCount);
            }
            catch (SearchBudgetExceededException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                stopwatch.Stop();
                warnings.Add(BudgetExceededWarning);
                return new CustomSearchResponse(
                    new CustomSearchBuild[0],
                    warnings.Distinct().ToArray(),
                    stopwatch.ElapsedMilliseconds,
                    evaluated,
                    CustomSearchStatus.BudgetExceeded,
                    originalCandidateCount,
                    compressedCandidateCount,
                    budget.PeakStateCount,
                    budget.VisitedStateCount,
                    budget.TransitionCount);
            }
            catch (CustomEffectDataException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                stopwatch.Stop();
                warnings.Add(FailedWarning);
                return new CustomSearchResponse(
                    new CustomSearchBuild[0],
                    warnings.Distinct().ToArray(),
                    stopwatch.ElapsedMilliseconds,
                    evaluated,
                    CustomSearchStatus.Failed,
                    originalCandidateCount,
                    compressedCandidateCount,
                    budget.PeakStateCount,
                    budget.VisitedStateCount,
                    budget.TransitionCount);
            }
            finally
            {
                if (diagnostics != null)
                {
                    diagnostics.OriginalCandidateCount = originalCandidateCount;
                    diagnostics.CompressedCandidateCount = compressedCandidateCount;
                    diagnostics.CandidateProjectionDiscardCount = compressedCandidateCount == 0 ? 0
                        : originalCandidateCount - compressedCandidateCount;
                    diagnostics.VisitedStateCount = budget.VisitedStateCount;
                    diagnostics.TransitionCount = budget.TransitionCount;
                    diagnostics.PeakStateCount = budget.PeakStateCount;
                }
            }
        }

        private static CustomSearchVesselDiagnostics CreateVesselDiagnostics(VesselDefinition vessel, Candidate[] candidates)
        {
            CustomSearchVesselDiagnostics result = new CustomSearchVesselDiagnostics
            {
                VesselId = vessel.Id,
                VesselName = vessel.Name,
                SlotColorIds = vessel.OrdinarySlotColorIds.Concat(vessel.DeepSlotColorIds).ToArray(),
                SlotIsDeep = new[] { false, false, false, true, true, true },
                LegalCandidateCountBySlot = new int[6]
            };
            foreach (Candidate candidate in candidates)
            {
                bool any = false;
                for (int slot = 0; slot < 6; slot++)
                {
                    if (candidate.Relic.IsDeep != result.SlotIsDeep[slot]
                        || !AcceptsColor(result.SlotColorIds[slot], candidate.Relic.ColorId)) continue;
                    result.LegalCandidateCountBySlot[slot]++;
                    any = true;
                }
                if (!any) result.IncompatibleCandidateCount++;
            }
            return result;
        }

        private static Candidate[] CompressCandidates(IEnumerable<Candidate> candidates)
        {
            return candidates
                .GroupBy(item => item.ProjectionKey, StringComparer.Ordinal)
                .SelectMany(group =>
                {
                    IOrderedEnumerable<Candidate> ordered = group
                        .OrderBy(item => item.Relic.InstanceId);
                    if (group.Any(item => item.UnknownWinnerOptions.Length != 0
                        || !string.IsNullOrEmpty(item.UnstableCoLocationToken)))
                        return ordered;
                    // A stable projection group can occupy at most the three slots
                    // of its own depth kind.  Co-sensitive groups are deliberately
                    // excluded: a higher-ID fourth item can lose a boundary tie to
                    // another graph and therefore produce a different co-location
                    // result than any retained lower-ID item.
                    return ordered.Take(3);
                })
                .OrderBy(item => item.Relic.InstanceId)
                .ThenBy(item => item.ProjectionKey, StringComparer.Ordinal)
                .ToArray();
        }

        private static void ConfigureCandidateSemanticStates(
            IEnumerable<Candidate> candidates,
            PreparedRule[] rules)
        {
            Candidate[] source = candidates.ToArray();
            HashSet<string> collapsibleGroups = new HashSet<string>(StringComparer.Ordinal);
            for (int ruleIndex = 0; ruleIndex < rules.Length; ruleIndex++)
            {
                int currentRule = ruleIndex;
                foreach (IGrouping<string, Occurrence> group in source
                    .SelectMany(candidate => candidate.Occurrences)
                    .Where(item => item.RuleIndex == currentRule
                        && item.IsApplicable
                        && !item.ExclusivityId.HasValue)
                    .GroupBy(item => item.StackGroupKey, StringComparer.Ordinal))
                {
                    int maximumOrdinary = source.Where(candidate => !candidate.Relic.IsDeep)
                        .Select(candidate => candidate.Occurrences.Count(item => item.RuleIndex == currentRule
                            && item.IsApplicable && !item.ExclusivityId.HasValue
                            && string.Equals(item.StackGroupKey, group.Key, StringComparison.Ordinal)))
                        .OrderByDescending(value => value).Take(3).Sum();
                    int maximumDeep = source.Where(candidate => candidate.Relic.IsDeep)
                        .Select(candidate => candidate.Occurrences.Count(item => item.RuleIndex == currentRule
                            && item.IsApplicable && !item.ExclusivityId.HasValue
                            && string.Equals(item.StackGroupKey, group.Key, StringComparison.Ordinal)))
                        .OrderByDescending(value => value).Take(3).Sum();
                    Occurrence sample = group.First();
                    int maximum = maximumOrdinary + maximumDeep;
                    bool cannotReachAggregationBoundary =
                        string.Equals(sample.AggregationRule, "cap_count", StringComparison.Ordinal)
                            && sample.MaxEffectiveCopies.HasValue
                            && maximum <= sample.MaxEffectiveCopies.Value
                        || string.Equals(sample.AggregationRule, "single_instance", StringComparison.Ordinal)
                            && maximum <= 1
                        || string.Equals(sample.AggregationRule, "unknown", StringComparison.Ordinal)
                            && maximum <= 1;
                    if (cannotReachAggregationBoundary)
                        collapsibleGroups.Add(currentRule + "\u001f" + group.Key);
                }
            }
            foreach (Candidate candidate in source)
                candidate.ConfigureSemanticContributions(collapsibleGroups);

            // Boundary-limited groups only need location identity when changing
            // their selected physical occurrence can affect a >=2 co-location
            // value.  Once one relic makes a group co-sensitive, keep every
            // competitor for that group so later rank/tie changes remain exact.
            HashSet<string> coSensitiveGroups = new HashSet<string>(StringComparer.Ordinal);
            HashSet<int> coSensitiveExclusivityIds = new HashSet<int>();
            foreach (Candidate candidate in source.Where(item =>
                item.RawLocalCoLocationPotential >= 2))
            {
                foreach (Occurrence occurrence in candidate.Occurrences.Where(item =>
                    item.IsApplicable && item.PhysicalOrdinal >= 0))
                {
                    if (occurrence.ExclusivityId.HasValue)
                        coSensitiveExclusivityIds.Add(occurrence.ExclusivityId.Value);
                    else
                    {
                        string key = occurrence.RuleIndex + "\u001f" + occurrence.StackGroupKey;
                        if (!collapsibleGroups.Contains(key)) coSensitiveGroups.Add(key);
                    }
                }
            }
            foreach (Candidate candidate in source)
                candidate.ConfigureCoLocationSemanticState(
                    coSensitiveGroups, coSensitiveExclusivityIds);
        }

        private static void EnsureUniqueInstanceIds(
            CharacterInventory inventory,
            CancellationToken cancellationToken)
        {
            HashSet<int> instanceIds = new HashSet<int>();
            foreach (RelicInstance relic in inventory.Relics)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!instanceIds.Add(relic.InstanceId))
                    throw new ArgumentException("仓库包含重复的实例编号。", "inventory");
            }
        }

        public CustomSearchBuild[] RankReplacements(
            CharacterInventory inventory,
            CharacterDefinition character,
            IEnumerable<CustomEffectRule> rules,
            CustomSearchBuild current,
            int assignmentIndex)
        {
            return RankReplacements(inventory, character, rules, current, assignmentIndex, CancellationToken.None);
        }

        public CustomSearchBuild[] RankReplacements(
            CharacterInventory inventory,
            CharacterDefinition character,
            IEnumerable<CustomEffectRule> rules,
            CustomSearchBuild current,
            int assignmentIndex,
            CancellationToken cancellationToken)
        {
            if (inventory == null) throw new ArgumentNullException("inventory");
            if (character == null) throw new ArgumentNullException("character");
            if (current == null) throw new ArgumentNullException("current");
            cancellationToken.ThrowIfCancellationRequested();
            EnsureUniqueInstanceIds(inventory, cancellationToken);
            if (assignmentIndex < 0 || assignmentIndex >= current.Assignments.Length)
                throw new ArgumentOutOfRangeException("assignmentIndex");
            PreparedRule[] prepared = PrepareRules(rules);
            SlotAssignment target = current.Assignments[assignmentIndex];
            HashSet<int> used = new HashSet<int>(current.Assignments
                .Where((item, index) => index != assignmentIndex)
                .Select(item => item.Relic.InstanceId));
            List<CustomSearchBuild> result = new List<CustomSearchBuild>();
            foreach (RelicInstance relic in inventory.Relics)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (relic.IsDeep != target.IsDeep || used.Contains(relic.InstanceId)) continue;
                if (!AcceptsColor(target.SlotColorId, relic.ColorId)) continue;
                SlotAssignment[] assignments = (SlotAssignment[])current.Assignments.Clone();
                assignments[assignmentIndex] = new SlotAssignment(target.IsDeep, target.SlotIndex, target.SlotColorId, relic);
                result.Add(EvaluateBuild(current.Vessel, assignments, character.Id, prepared));
            }
            result.Sort(CompareBuilds);
            return result.ToArray();
        }

        public CustomSearchBuild EvaluateBuild(
            VesselDefinition vessel,
            SlotAssignment[] assignments,
            string characterId,
            IEnumerable<CustomEffectRule> rules)
        {
            return EvaluateBuild(vessel, assignments, characterId, PrepareRules(rules));
        }

        public static int CompareBuilds(CustomSearchBuild left, CustomSearchBuild right)
        {
            return CustomComparisonVector.Compare(left.Comparison, right.Comparison);
        }

        private PreparedRule[] PrepareRules(IEnumerable<CustomEffectRule> rules)
        {
            return _contributionEvaluator.PrepareRules(rules)
                .Select(item => new PreparedRule(item))
                .ToArray();
        }

        private static bool HasPrefixSemanticFootprint(Candidate candidate, CustomEffectCatalog catalog)
        {
            for (int index = 0; index < candidate.Occurrences.Length; index++)
            {
                Occurrence occurrence = candidate.Occurrences[index];
                if (occurrence.IsApplicable
                    || occurrence.EffectId > 0 && catalog.GetEffect(occurrence.EffectId) == null)
                    return true;
            }
            for (int index = 0; index < candidate.ExclusiveEffects.Length; index++)
                if (candidate.ExclusiveEffects[index].IsApplicable) return true;
            return false;
        }

        // Reflection-only experiment probe. The application never calls this method.
        private bool ConservativePrefixRelevantForTest(RelicInstance incoming, RelicInstance outgoing,
            string characterId, IEnumerable<CustomEffectRule> rules)
        {
            PreparedRule[] prepared = PrepareRules(rules);
            return HasPrefixSemanticFootprint(new Candidate(incoming, prepared, characterId, _catalog), _catalog)
                || HasPrefixSemanticFootprint(new Candidate(outgoing, prepared, characterId, _catalog), _catalog);
        }

        private List<string> ValidateRequest(CharacterInventory inventory, PreparedRule[] rules)
        {
            List<string> warnings = new List<string>();
            HashSet<int> ownedItems = new HashSet<int>(inventory.Relics.Select(item => item.ItemId));
            foreach (PreparedRule rule in rules)
            {
                if (!rule.IsAvailableForRealMatching)
                    warnings.Add((rule.Preset == null ? rule.Selector.Name : rule.Preset.Name)
                        + "：" + UnavailableForRealMatchingWarning);
                else if (rule.Preset != null && !ownedItems.Contains(rule.Preset.ItemId))
                    warnings.Add(rule.Preset.Name + "：" + _catalog.Warning("presetNotOwned"));
            }
            var requiredGroups = rules
                .SelectMany((rule, index) => rule.RuntimeEffectIds.Select(id => new
                {
                    Rule = rule,
                    Index = index,
                    Effect = _catalog.GetEffect(id)
                }))
                .Where(item => item.Rule.Source.IsRequired && item.Effect != null && item.Effect.ExclusivityId.HasValue)
                .GroupBy(item => item.Effect.ExclusivityId.Value)
                .Where(group => group.Select(item => item.Index).Distinct().Count() > 1);
            foreach (var group in requiredGroups)
                warnings.Add(_catalog.Warning("requiredExclusive") + "（exclusivityId=" + group.Key + "）");
            return warnings;
        }

        private CustomSearchBuild EvaluateBuild(
            VesselDefinition vessel,
            SlotAssignment[] assignments,
            string characterId,
            PreparedRule[] rules)
        {
            Candidate[] candidates = assignments
                .Select(item => new Candidate(item.Relic, rules, characterId, _catalog))
                .ToArray();
            EffectiveContributionEvaluation evaluation = EvaluateContributions(
                candidates, characterId, rules, null, true);
            CustomComparisonVector comparison = CreateComparisonVector(vessel, candidates, evaluation);
            return new CustomSearchBuild(
                vessel, assignments, comparison, BuildWarnings(assignments, characterId, evaluation), evaluation);
        }

        private CustomComparisonVector EvaluateCandidatesVector(
            VesselDefinition vessel,
            Candidate[] candidates,
            string characterId,
            PreparedRule[] rules,
            SearchBudget budget)
        {
            EffectiveContributionEvaluation evaluation = EvaluateContributions(
                candidates, characterId, rules, budget, false);
            return CreateComparisonVector(vessel, candidates, evaluation);
        }

        private EffectiveContributionEvaluation EvaluateContributions(
            Candidate[] candidates,
            string characterId,
            PreparedRule[] rules,
            SearchBudget budget,
            bool includeDetails)
        {
            return _contributionEvaluator.Evaluate(
                candidates.Select(item => item.Relic).ToArray(),
                characterId,
                rules.Select(item => item.EvaluationDefinition).ToArray(),
                budget == null ? (Action)null : budget.Checkpoint,
                includeDetails);
        }

        private static CustomComparisonVector CreateComparisonVector(
            VesselDefinition vessel,
            Candidate[] candidates,
            EffectiveContributionEvaluation evaluation)
        {
            RuleComparisonProfile[] profiles = evaluation.Rules
                .Select(item => item.ComparisonProfile).ToArray();
            int[] rawCounts = profiles.Select(item => item.RawCount).ToArray();
            int[] effective = profiles.Select(item => item.EffectiveCount).ToArray();
            int[] uncertainAdditional = profiles.Select(item => item.UncertainAdditionalCount).ToArray();
            int[][] rankProfiles = profiles.Select(item => item.CompleteVariantRankDistribution).ToArray();
            int[] maxRanks = profiles.Select(item => item.MaximumVariantPreferenceRank).ToArray();
            int missing = profiles.Where(item => item.IsRequired)
                .Sum(item => Math.Max(0, item.QuantityTarget - item.EffectiveCount));
            int covered = profiles.Count(item => item.EffectiveCount > 0);
            int uncertainty = candidates.Sum(item => item.UncertaintyCount)
                + uncertainAdditional.Sum() + evaluation.ExclusivityUncertaintyCount;
            return new CustomComparisonVector(
                missing, covered, maxRanks, rankProfiles, rawCounts, effective, uncertainAdditional,
                candidates.Sum(item => item.NegativeEffectCount),
                candidates.Sum(item => item.NegativeRelicCount),
                uncertainty, vessel.Id,
                candidates.Select(item => item.Relic.InstanceId).ToArray(),
                profiles, evaluation.CoLocationProfile);
        }

        private CustomSearchBuild CreateSearchBuild(
            VesselDefinition vessel,
            Candidate[] candidates,
            string characterId,
            PreparedRule[] rules,
            SearchBudget budget,
            CustomComparisonVector comparison)
        {
            SlotAssignment[] assignments = new SlotAssignment[6];
            for (int index = 0; index < 3; index++)
                assignments[index] = new SlotAssignment(
                    false, index, vessel.OrdinarySlotColorIds[index], candidates[index].Relic);
            for (int index = 0; index < 3; index++)
                assignments[index + 3] = new SlotAssignment(
                    true, index, vessel.DeepSlotColorIds[index], candidates[index + 3].Relic);
            EffectiveContributionEvaluation evaluation = EvaluateContributions(
                candidates, characterId, rules, null, true);
            CustomComparisonVector exact = comparison
                ?? CreateComparisonVector(vessel, candidates, evaluation);
            return new CustomSearchBuild(
                vessel, assignments, exact, BuildWarnings(assignments, characterId, evaluation), evaluation);
        }

        private CustomBuildWarning[] BuildWarnings(
            SlotAssignment[] assignments,
            string characterId,
            EffectiveContributionEvaluation evaluation)
        {
            List<CustomBuildWarning> result = new List<CustomBuildWarning>();
            Dictionary<int, List<Tuple<RelicInstance, int>>> exclusive = new Dictionary<int, List<Tuple<RelicInstance, int>>>();
            foreach (SlotAssignment assignment in assignments)
            {
                foreach (int effectId in AllEffectIds(assignment.Relic))
                {
                    CustomRuntimeEffect effect = _catalog.GetEffect(effectId);
                    if (effect == null) continue;
                    if (!effect.IsApplicableTo(characterId))
                        result.Add(new CustomBuildWarning(assignment.Relic.InstanceId, effectId, _catalog.Warning("characterInapplicable")));
                    if (effect.ExclusivityId.HasValue)
                    {
                        List<Tuple<RelicInstance, int>> list;
                        if (!exclusive.TryGetValue(effect.ExclusivityId.Value, out list))
                        {
                            list = new List<Tuple<RelicInstance, int>>();
                            exclusive.Add(effect.ExclusivityId.Value, list);
                        }
                        list.Add(Tuple.Create(assignment.Relic, effectId));
                    }
                }
            }
            foreach (KeyValuePair<int, List<Tuple<RelicInstance, int>>> group in exclusive)
            {
                if (group.Value.Count <= 1) continue;
                foreach (Tuple<RelicInstance, int> occurrence in group.Value)
                    result.Add(new CustomBuildWarning(occurrence.Item1.InstanceId, occurrence.Item2, _catalog.Warning("runtimeExclusive")));
            }
            // Use the evaluator's actual winner; slot order need not select the
            // same physical occurrence after rank and co-location tie breaking.
            foreach (EffectiveRuleContribution rule in evaluation.Rules)
            {
                foreach (EffectiveContributionOccurrence occurrence in rule.Occurrences)
                    if (occurrence.IsUncertainAdditional)
                        result.Add(new CustomBuildWarning(
                            occurrence.InstanceId, occurrence.RuntimeEffectId,
                            UnknownRepeatedContributionWarning));
            }
            return result
                .GroupBy(item => new { item.InstanceId, item.RuntimeEffectId, item.Message })
                .Select(group => group.First())
                .ToArray();
        }

        private static IEnumerable<int> AllEffectIds(RelicInstance relic)
        {
            return relic.PositiveEffectIds.Concat(relic.NegativeEffectIds);
        }

        private static bool AcceptsColor(int slotColorId, int relicColorId)
        {
            return relicColorId >= 0 && relicColorId <= 3 && (slotColorId == 4 || slotColorId == relicColorId);
        }

        private static bool IsCanonicalEquivalentSlot(
            VesselDefinition vessel,
            int filledMask,
            int outputSlot)
        {
            bool deep = outputSlot >= 3;
            int localSlot = deep ? outputSlot - 3 : outputSlot;
            int[] colors = deep ? vessel.DeepSlotColorIds : vessel.OrdinarySlotColorIds;
            int firstOutputSlot = deep ? 3 : 0;
            for (int priorLocal = 0; priorLocal < localSlot; priorLocal++)
            {
                int priorOutputSlot = firstOutputSlot + priorLocal;
                if (colors[priorLocal] == colors[localSlot]
                    && (filledMask & (1 << priorOutputSlot)) == 0)
                    return false;
            }
            return true;
        }

        private sealed class PreparedRule
        {
            public PreparedRule(EffectiveContributionRuleDefinition definition)
            {
                EvaluationDefinition = definition;
                Source = definition.Source;
                Selector = definition.Selector;
                Preset = definition.Preset;
                IsAvailableForRealMatching = definition.IsAvailableForRealMatching;
                Ranks = definition.Ranks;
                RuntimeEffectIds = definition.RuntimeEffectIds;
            }

            public EffectiveContributionRuleDefinition EvaluationDefinition;
            public CustomEffectRule Source;
            public CustomEffectSelector Selector;
            public OfficialPresetRelic Preset;
            public bool IsAvailableForRealMatching;
            public Dictionary<int, int> Ranks;
            public int[] RuntimeEffectIds;
        }

        private sealed class Candidate
        {
            public Candidate(
                RelicInstance relic,
                PreparedRule[] rules,
                string characterId,
                CustomEffectCatalog catalog,
                SearchBudget budget = null)
            {
                Relic = relic;
                NegativeEffectCount = relic.NegativeEffectIds.Length;
                NegativeRelicCount = NegativeEffectCount == 0 ? 0 : 1;
                int[] effectIds = AllEffectIds(relic).ToArray();
                PresetMatches = new int[rules.Length];
                List<Occurrence> occurrences = new List<Occurrence>();
                List<ExclusiveEffect> exclusiveEffects = new List<ExclusiveEffect>();
                HashSet<int> relevantEffectIds = new HashSet<int>();
                for (int physicalOrdinal = 0; physicalOrdinal < effectIds.Length; physicalOrdinal++)
                {
                    if (budget != null) budget.Checkpoint();
                    int effectId = effectIds[physicalOrdinal];
                    CustomRuntimeEffect effect = catalog.GetEffect(effectId);
                    if (effect != null && effect.ExclusivityId.HasValue)
                        exclusiveEffects.Add(new ExclusiveEffect(
                            effectId, physicalOrdinal, effect.ExclusivityId.Value,
                            effect.IsApplicableTo(characterId)));
                }
                for (int index = 0; index < rules.Length; index++)
                {
                    if (budget != null) budget.Checkpoint();
                    PreparedRule rule = rules[index];
                    if (rule.Preset != null)
                    {
                        bool matches = relic.ItemId == rule.Preset.ItemId;
                        PresetMatches[index] = matches ? 1 : 0;
                        if (rule.IsAvailableForRealMatching && matches)
                            occurrences.Add(Occurrence.Preset(index, relic.ItemId));
                        continue;
                    }
                    for (int physicalOrdinal = 0; physicalOrdinal < effectIds.Length; physicalOrdinal++)
                    {
                        if (budget != null) budget.Checkpoint();
                        int effectId = effectIds[physicalOrdinal];
                        int rank;
                        if (!rule.Ranks.TryGetValue(effectId, out rank)) continue;
                        CustomRuntimeEffect effect = catalog.GetEffect(effectId);
                        relevantEffectIds.Add(effectId);
                        if (effect == null)
                            occurrences.Add(Occurrence.UnknownCatalog(index, effectId, rank, physicalOrdinal));
                        else
                            occurrences.Add(new Occurrence(
                                index, effectId, rank, physicalOrdinal,
                                effect.StackGroupKey, effect.AggregationRule, effect.MaxEffectiveCopies,
                                effect.ExclusivityId, effect.IsApplicableTo(characterId)));
                    }
                }
                IEnumerable<int> uncertaintyIds = rules.Length == 0
                    ? (IEnumerable<int>)effectIds
                    : relevantEffectIds;
                foreach (int effectId in uncertaintyIds.Distinct())
                {
                    CustomRuntimeEffect effect = catalog.GetEffect(effectId);
                    UncertaintyCount += effect == null ? 2 : effect.UncertaintyCount;
                }
                Occurrences = occurrences
                    .OrderBy(item => item.PhysicalOrdinal)
                    .ThenBy(item => item.RuleIndex)
                    .ThenBy(item => item.EffectId)
                    .ThenBy(item => item.Rank)
                    .ToArray();
                RawLocalCoLocationPotential = ComputeRawLocalCoLocationPotential();
                ExclusiveEffects = exclusiveEffects.OrderBy(item => item.ExclusivityId)
                    .ThenBy(item => item.PhysicalOrdinal).ThenBy(item => item.EffectId).ToArray();
                SemanticContributions = ContributionState.Create(Occurrences, null);
                ExclusiveOptions = CreateExclusiveOptions();
                LocalCoLocationCount = 0;
                BaseCoLocationGraphToken = string.Empty;
                BaseCoLocationOccurrences = new Occurrence[0];
                UnknownWinnerOptions = new UnknownWinnerOption[0];
                UnknownWinnerStates = new UnknownWinnerState[0];
                UnstableCoLocationToken = string.Empty;
                ProjectionKey = CreateProjectionKey();
            }

            public void ConfigureSemanticContributions(HashSet<string> collapsibleGroups)
            {
                SemanticContributions = ContributionState.Create(Occurrences, collapsibleGroups);
            }

            public void ConfigureCoLocationSemanticState(
                HashSet<string> coSensitiveGroups,
                HashSet<int> coSensitiveExclusivityIds)
            {
                HashSet<string> coSensitiveBoundaryGroups = new HashSet<string>(
                    Occurrences.Where(item => item.IsApplicable
                            && item.PhysicalOrdinal >= 0
                            && !item.ExclusivityId.HasValue
                            && coSensitiveGroups != null && coSensitiveGroups.Contains(
                                item.RuleIndex + "\u001f" + item.StackGroupKey))
                        .Select(item => item.RuleIndex + "\u001f" + item.StackGroupKey),
                    StringComparer.Ordinal);
                UnknownWinnerOptions = Occurrences.Where(item => item.IsApplicable
                        && item.PhysicalOrdinal >= 0
                        && !item.ExclusivityId.HasValue
                        && coSensitiveBoundaryGroups.Contains(
                            item.RuleIndex + "\u001f" + item.StackGroupKey))
                    .Select(item => new UnknownWinnerOption(this, item))
                    .Where(item => item.Capacity > 0)
                    .OrderBy(item => item.Key, StringComparer.Ordinal)
                    .ThenBy(item => item, UnknownWinnerOption.PreferenceComparer)
                    .ToArray();
                UnknownWinnerStates = UnknownWinnerOptions
                    .GroupBy(item => item.Key, StringComparer.Ordinal)
                    .Select(group =>
                    {
                        UnknownWinnerOption first = group.First();
                        if (group.Any(item => item.Capacity != first.Capacity
                            || !string.Equals(item.AggregationRule,
                                first.AggregationRule, StringComparison.Ordinal)))
                            throw new CustomEffectDataException(
                                "同一 StackGroup 的聚合声明不一致。");
                        return new UnknownWinnerState(group.Key,
                            group.Take(first.Capacity).ToArray());
                    })
                    .ToArray();

                List<Occurrence> baseEligible = BuildLocalEligibleOccurrences(
                    coSensitiveBoundaryGroups);
                BaseCoLocationOccurrences = baseEligible
                    .Where(item => !item.ExclusivityId.HasValue).ToArray();
                BaseCoLocationGraphToken = CreateLocalGraphToken(baseEligible);
                int baseCoLocationCount = MaximumLocalMatching(CreateLocalEdges(baseEligible));
                bool hasApplicableExclusiveEffect = ExclusiveEffects.Any(item => item.IsApplicable
                    && coSensitiveExclusivityIds != null
                    && coSensitiveExclusivityIds.Contains(item.ExclusivityId));
                if (hasApplicableExclusiveEffect)
                {
                    // A later relic can replace the effective physical occurrence
                    // of a boundary-limited group, or add another option to an
                    // exclusivity group.  Preserve the small, exceptional graph
                    // needed to distinguish those states; stable groups below use
                    // only their final integer comparison value.
                    LocalCoLocationCount = 0;
                    UnstableCoLocationToken = CreateUnstableCoLocationToken();
                    return;
                }
                LocalCoLocationCount = baseCoLocationCount;
                UnstableCoLocationToken = string.Empty;
            }

            public int ComputeRawLocalCoLocationPotential()
            {
                Dictionary<string, int[]> edges = Occurrences
                    .Where(item => item.IsApplicable && item.PhysicalOrdinal >= 0)
                    .GroupBy(item => item.PhysicalOrdinal + "/" + item.EffectId,
                        StringComparer.Ordinal)
                    .ToDictionary(
                        item => item.Key,
                        item => item.Select(value => value.RuleIndex).Distinct().ToArray(),
                        StringComparer.Ordinal);
                return MaximumLocalMatching(edges);
            }

            private string CreateUnstableCoLocationToken()
            {
                string occurrences = string.Join(",", Occurrences
                    .Where(item => item.IsApplicable && item.PhysicalOrdinal >= 0)
                    .Select(item => item.RuleIndex + "/" + item.EffectId + "/" + item.Rank
                        + "/" + item.PhysicalOrdinal + "/" + item.StackGroupKey + "/"
                        + item.AggregationRule + "/"
                        + (item.MaxEffectiveCopies.HasValue
                            ? item.MaxEffectiveCopies.Value.ToString() : "?") + "/"
                        + (item.ExclusivityId.HasValue
                            ? item.ExclusivityId.Value.ToString() : "-")));
                string blockers = string.Join(",", ExclusiveEffects
                    .Where(item => item.IsApplicable)
                    .Select(item => item.ExclusivityId + "/" + item.EffectId
                        + "/" + item.PhysicalOrdinal));
                // Instance ID participates in the evaluator's deterministic tie
                // choice.  Associating it with this rare unstable graph prevents
                // two swapped graphs from merging before a future higher rank is
                // added.
                return Relic.InstanceId + "|O" + occurrences + "|X" + blockers;
            }

            private ExclusiveOption[] CreateExclusiveOptions()
            {
                return ExclusiveEffects.Where(item => item.IsApplicable)
                    .Select(item => new ExclusiveOption(
                        item.ExclusivityId,
                        Occurrences.Where(match => match.IsApplicable
                                && match.PhysicalOrdinal == item.PhysicalOrdinal
                                && match.EffectId == item.EffectId)
                            .GroupBy(match => match.RuleIndex)
                            .ToDictionary(group => group.Key,
                                group => group.Min(match => match.Rank))))
                    .GroupBy(item => item.ExclusivityId)
                    .Select(group => group.Aggregate((left, right) => left.Merge(right)))
                    .OrderBy(item => item.ExclusivityId).ToArray();
            }

            private string CreateProjectionKey()
            {
                // Inapplicable occurrences cannot contribute, block, or create
                // uncertain additional contributions. Keep the original data for
                // evaluation/details; only the request's equivalence key omits it.
                string occurrences = string.Join(",", Occurrences.Where(item => item.IsApplicable).Select(item =>
                    item.RuleIndex + "/" + item.EffectId + "/" + item.Rank + "/" + item.PhysicalOrdinal
                    + "/" + item.StackGroupKey + "/" + item.AggregationRule + "/"
                    + (item.MaxEffectiveCopies.HasValue ? item.MaxEffectiveCopies.Value.ToString() : "?")
                    + "/" + (item.ExclusivityId.HasValue ? item.ExclusivityId.Value.ToString() : "-")
                    + "/" + (item.IsApplicable ? "1" : "0")));
                string blockers = string.Join(",", ExclusiveEffects.Where(item => item.IsApplicable).Select(item =>
                    item.ExclusivityId + "/" + item.EffectId + "/" + item.PhysicalOrdinal
                    + "/" + (item.IsApplicable ? "1" : "0")));
                return (Relic.IsDeep ? "D" : "O") + ":" + Relic.ColorId
                    + "|P" + string.Join(",", PresetMatches)
                    + "|O" + occurrences
                    + "|X" + blockers
                    + "|N" + NegativeEffectCount + "/" + NegativeRelicCount + "/" + UncertaintyCount;
            }

            private List<Occurrence> BuildLocalEligibleOccurrences(
                HashSet<string> excludedUnknownGroups)
            {
                List<Occurrence> eligible = new List<Occurrence>();
                foreach (IGrouping<string, Occurrence> group in Occurrences
                    .Where(item => item.IsApplicable
                        && item.PhysicalOrdinal >= 0
                        && !item.ExclusivityId.HasValue)
                    .GroupBy(item => item.RuleIndex + "\u001f" + item.StackGroupKey,
                        StringComparer.Ordinal))
                {
                    if (excludedUnknownGroups != null
                        && excludedUnknownGroups.Contains(group.Key))
                        continue;
                    Occurrence[] values = group.OrderByDescending(item => item.Rank)
                        .ThenBy(item => item.PhysicalOrdinal).ThenBy(item => item.EffectId).ToArray();
                    Occurrence sample = values[0];
                    if (string.Equals(sample.AggregationRule, "cap_count", StringComparison.Ordinal))
                    {
                        int cap = sample.MaxEffectiveCopies.HasValue ? sample.MaxEffectiveCopies.Value : 0;
                        eligible.AddRange(values.Take(cap));
                    }
                    else if (string.Equals(sample.AggregationRule, "single_instance", StringComparison.Ordinal))
                        eligible.Add(values[0]);
                    else
                        eligible.Add(values.OrderBy(item => item.Rank)
                            .ThenBy(item => item.PhysicalOrdinal).ThenBy(item => item.EffectId).First());
                }

                foreach (IGrouping<int, ExclusiveEffect> group in ExclusiveEffects
                    .Where(item => item.IsApplicable).GroupBy(item => item.ExclusivityId))
                {
                    ExclusiveEffect[] physical = group
                        .GroupBy(item => item.PhysicalOrdinal + "/" + item.EffectId,
                            StringComparer.Ordinal)
                        .Select(item => item.First()).ToArray();
                    if (physical.Length != 1) continue;
                    ExclusiveEffect only = physical[0];
                    eligible.AddRange(Occurrences.Where(item => item.IsApplicable
                        && item.PhysicalOrdinal == only.PhysicalOrdinal
                        && item.EffectId == only.EffectId));
                }
                return eligible;
            }

            private static Dictionary<string, int[]> CreateLocalEdges(
                IEnumerable<Occurrence> eligible)
            {
                return eligible
                    .GroupBy(item => item.PhysicalOrdinal + "/" + item.EffectId,
                        StringComparer.Ordinal)
                    .ToDictionary(
                        item => item.Key,
                        item => item.Select(value => value.RuleIndex).Distinct().ToArray(),
                        StringComparer.Ordinal);
            }

            private static string CreateLocalGraphToken(IEnumerable<Occurrence> eligible)
            {
                return string.Join(",", eligible
                    .GroupBy(item => item.PhysicalOrdinal + "/" + item.EffectId,
                        StringComparer.Ordinal)
                    .OrderBy(item => item.Key, StringComparer.Ordinal)
                    .Select(item => item.Key + "=" + string.Join(".", item
                        .Select(value => value.RuleIndex).Distinct().OrderBy(value => value))));
            }

            public string CreateUnknownWinnerCandidateToken(
                UnknownWinnerState[] winners)
            {
                int count = 0;
                foreach (UnknownWinnerState group in winners)
                    foreach (UnknownWinnerOption option in group.Options)
                        if (option.Candidate.Relic.InstanceId == Relic.InstanceId) count++;
                if (count == 0) return null;

                // Own this array. Keep stable ordering even when equal options
                // arrive from different groups; never reorder the input states.
                UnknownWinnerOption[] activeWinners = new UnknownWinnerOption[count];
                int length = 0;
                bool requiresIdentity = false;
                foreach (UnknownWinnerState group in winners)
                    foreach (UnknownWinnerOption option in group.Options)
                    {
                        if (option.Candidate.Relic.InstanceId != Relic.InstanceId) continue;
                        int insert = length;
                        while (insert > 0)
                        {
                            UnknownWinnerOption previous = activeWinners[insert - 1];
                            int comparison = string.CompareOrdinal(previous.Key, option.Key);
                            if (comparison == 0)
                                comparison = UnknownWinnerOption.ComparePreference(previous, option);
                            if (comparison <= 0) break;
                            activeWinners[insert] = previous;
                            insert--;
                        }
                        activeWinners[insert] = option;
                        length++;
                        requiresIdentity |= option.RequiresStableIdentity;
                    }
                string[] tokens = new string[count];
                for (int index = 0; index < count; index++)
                    tokens[index] = activeWinners[index].LocalSemanticToken;
                string active = string.Join(",", tokens);
                string identity = requiresIdentity ? Relic.InstanceId + "|" : string.Empty;
                return identity + RawLocalCoLocationPotential
                    + "|B" + BaseCoLocationGraphToken
                    + "|W" + active;
            }

            public int ComputeFinalCoLocationCount(IEnumerable<Occurrence> additional)
            {
                return MaximumLocalMatching(CreateLocalEdges(
                    BaseCoLocationOccurrences.Concat(additional ?? Enumerable.Empty<Occurrence>())));
            }

            private static int MaximumLocalMatching(Dictionary<string, int[]> edges)
            {
                Dictionary<int, string> matched = new Dictionary<int, string>();
                int count = 0;
                foreach (string physicalKey in edges.Keys.OrderBy(value => value, StringComparer.Ordinal))
                    if (TryMatchLocal(physicalKey, edges, matched, new HashSet<int>())) count++;
                return count;
            }

            private static bool TryMatchLocal(
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
                        || TryMatchLocal(existing, edges, matchedPhysicalByRule, visitedRules))
                    {
                        matchedPhysicalByRule[ruleIndex] = physicalKey;
                        return true;
                    }
                }
                return false;
            }

            public RelicInstance Relic;
            public int[] PresetMatches;
            public Occurrence[] Occurrences;
            public ExclusiveEffect[] ExclusiveEffects;
            public ContributionState[] SemanticContributions;
            public ExclusiveOption[] ExclusiveOptions;
            public int NegativeEffectCount;
            public int NegativeRelicCount;
            public int UncertaintyCount;
            public int RawLocalCoLocationPotential;
            public int LocalCoLocationCount;
            public string BaseCoLocationGraphToken;
            public Occurrence[] BaseCoLocationOccurrences;
            public UnknownWinnerOption[] UnknownWinnerOptions;
            public UnknownWinnerState[] UnknownWinnerStates;
            public string UnstableCoLocationToken;
            public string ProjectionKey;
        }

        private sealed class UnknownWinnerOption
        {
            private sealed class OptionPreferenceComparer : IComparer<UnknownWinnerOption>
            {
                public int Compare(UnknownWinnerOption left, UnknownWinnerOption right)
                {
                    return ComparePreference(left, right);
                }
            }

            public UnknownWinnerOption(Candidate candidate, Occurrence occurrence)
            {
                Candidate = candidate;
                Occurrence = occurrence;
                Key = occurrence.RuleIndex + "\u001f" + occurrence.StackGroupKey;
                AggregationRule = occurrence.AggregationRule;
                Capacity = string.Equals(AggregationRule, "cap_count", StringComparison.Ordinal)
                    ? Math.Max(0, occurrence.MaxEffectiveCopies.GetValueOrDefault())
                    : 1;
                LocalSemanticToken = Key + "/" + occurrence.Rank + "/"
                    + occurrence.PhysicalOrdinal + "/" + occurrence.EffectId;
            }

            public static readonly IComparer<UnknownWinnerOption> PreferenceComparer =
                new OptionPreferenceComparer();
            public Candidate Candidate;
            public Occurrence Occurrence;
            public string Key;
            public string AggregationRule;
            public int Capacity;
            public string LocalSemanticToken;
            public bool RequiresStableIdentity;

            public string CreateTieClassKey()
            {
                return Key + "\u001e" + Occurrence.Rank + "\u001e"
                    + Candidate.RawLocalCoLocationPotential;
            }

            public static int ComparePreference(
                UnknownWinnerOption left,
                UnknownWinnerOption right)
            {
                int value = string.Equals(left.AggregationRule, "unknown", StringComparison.Ordinal)
                    ? left.Occurrence.Rank.CompareTo(right.Occurrence.Rank)
                    : right.Occurrence.Rank.CompareTo(left.Occurrence.Rank);
                if (value != 0) return value;
                value = right.Candidate.RawLocalCoLocationPotential.CompareTo(
                    left.Candidate.RawLocalCoLocationPotential);
                if (value != 0) return value;
                value = left.Candidate.Relic.InstanceId.CompareTo(
                    right.Candidate.Relic.InstanceId);
                if (value != 0) return value;
                value = left.Occurrence.PhysicalOrdinal.CompareTo(
                    right.Occurrence.PhysicalOrdinal);
                if (value != 0) return value;
                return left.Occurrence.EffectId.CompareTo(right.Occurrence.EffectId);
            }
        }

        private sealed class UnknownWinnerState
        {
            public UnknownWinnerState(string key, UnknownWinnerOption[] options)
            {
                Key = key;
                Options = options;
            }

            public string Key;
            public UnknownWinnerOption[] Options;
        }

        private sealed class Occurrence
        {
            public Occurrence(
                int ruleIndex,
                int effectId,
                int rank,
                int physicalOrdinal,
                string stackGroupKey,
                string aggregationRule,
                int? maxEffectiveCopies,
                int? exclusivityId,
                bool isApplicable)
            {
                RuleIndex = ruleIndex;
                EffectId = effectId;
                Rank = rank;
                PhysicalOrdinal = physicalOrdinal;
                StackGroupKey = stackGroupKey;
                AggregationRule = aggregationRule;
                MaxEffectiveCopies = maxEffectiveCopies;
                ExclusivityId = exclusivityId;
                IsApplicable = isApplicable;
            }

            public static Occurrence Preset(int ruleIndex, int itemId)
            {
                return new Occurrence(ruleIndex, -itemId, 1, -1,
                    "preset:" + itemId, "single_instance", 1, null, true);
            }

            public static Occurrence UnknownCatalog(int ruleIndex, int effectId, int rank, int physicalOrdinal)
            {
                return new Occurrence(ruleIndex, effectId, rank, physicalOrdinal,
                    "runtime:" + effectId, "unknown", null, null, false);
            }

            public static Occurrence ConfirmedExclusive(int ruleIndex, int rank, int exclusivityId)
            {
                return new Occurrence(ruleIndex, 0, rank, -1,
                    "exclusive:" + exclusivityId, "single_instance", 1, null, true);
            }

            public int RuleIndex;
            public int EffectId;
            public int Rank;
            public int PhysicalOrdinal;
            public string StackGroupKey;
            public string AggregationRule;
            public int? MaxEffectiveCopies;
            public int? ExclusivityId;
            public bool IsApplicable;
        }

        private sealed class ExclusiveEffect
        {
            public ExclusiveEffect(int effectId, int physicalOrdinal, int exclusivityId, bool isApplicable)
            {
                EffectId = effectId;
                PhysicalOrdinal = physicalOrdinal;
                ExclusivityId = exclusivityId;
                IsApplicable = isApplicable;
            }
            public int EffectId;
            public int PhysicalOrdinal;
            public int ExclusivityId;
            public bool IsApplicable;
        }

        private sealed class PositionedOccurrence
        {
            public PositionedOccurrence(int candidateIndex, Occurrence value)
            {
                CandidateIndex = candidateIndex;
                Value = value;
            }
            public int CandidateIndex;
            public Occurrence Value;
        }

        private sealed class PositionedExclusiveEffect
        {
            public PositionedExclusiveEffect(int candidateIndex, ExclusiveEffect value)
            {
                CandidateIndex = candidateIndex;
                Value = value;
            }
            public int CandidateIndex;
            public ExclusiveEffect Value;
        }

        private sealed class ContributionState
        {
            private ContributionState(
                int ruleIndex,
                string stackGroupKey,
                string aggregationRule,
                int cap,
                int[] values)
            {
                RuleIndex = ruleIndex;
                StackGroupKey = stackGroupKey;
                AggregationRule = aggregationRule;
                Cap = cap;
                Values = values;
                Key = ruleIndex + "\u001f" + stackGroupKey;
                SemanticToken = string.Equals(stackGroupKey, "confirmed-safe", StringComparison.Ordinal)
                    ? ruleIndex + "/" + stackGroupKey + "/" + aggregationRule + "/count=" + values.Length
                    : ruleIndex + "/" + stackGroupKey + "/" + aggregationRule
                        + "/" + string.Join(".", values);
            }

            public int RuleIndex;
            public string StackGroupKey;
            public string AggregationRule;
            public int Cap;
            public int[] Values;
            public string Key;
            public string SemanticToken;

            public static ContributionState[] Create(
                IEnumerable<Occurrence> source,
                HashSet<string> collapsibleGroups)
            {
                return source.Where(item => item.IsApplicable && !item.ExclusivityId.HasValue)
                    .GroupBy(item => item.RuleIndex + "\u001f" + (
                        collapsibleGroups != null
                        && collapsibleGroups.Contains(item.RuleIndex + "\u001f" + item.StackGroupKey)
                            ? "confirmed-safe"
                            : item.StackGroupKey), StringComparer.Ordinal)
                    .Select(group =>
                    {
                        Occurrence sample = group.First();
                        bool collapsed = collapsibleGroups != null
                            && collapsibleGroups.Contains(sample.RuleIndex + "\u001f" + sample.StackGroupKey);
                        string stackGroupKey = collapsed ? "confirmed-safe" : sample.StackGroupKey;
                        string aggregationRule = collapsed ? "cap_count" : sample.AggregationRule;
                        int cap = collapsed ? 36
                            : sample.MaxEffectiveCopies.HasValue ? sample.MaxEffectiveCopies.Value : 0;
                        int[] values;
                        if (string.Equals(aggregationRule, "cap_count", StringComparison.Ordinal))
                            values = group.Select(item => item.Rank).OrderByDescending(item => item).Take(cap).ToArray();
                        else if (string.Equals(aggregationRule, "single_instance", StringComparison.Ordinal))
                            values = new[] { group.Max(item => item.Rank) };
                        else
                            values = new[] { group.Min(item => item.Rank), group.Count() };
                        return new ContributionState(sample.RuleIndex, stackGroupKey,
                            aggregationRule, cap, values);
                    })
                    .OrderBy(item => item.Key, StringComparer.Ordinal)
                    .ToArray();
            }

            public bool WouldMergeChange(ContributionState other)
            {
                if (!string.Equals(Key, other.Key, StringComparison.Ordinal)
                    || !string.Equals(AggregationRule, other.AggregationRule, StringComparison.Ordinal)
                    || Cap != other.Cap)
                    throw new CustomEffectDataException("同一 StackGroup 的聚合声明不一致。");
                if (string.Equals(AggregationRule, "cap_count", StringComparison.Ordinal))
                {
                    int length = Math.Min(Cap, Values.Length + other.Values.Length);
                    if (length != Values.Length) return true;
                    int leftIndex = 0;
                    int rightIndex = 0;
                    for (int index = 0; index < length; index++)
                    {
                        int value;
                        if (rightIndex >= other.Values.Length
                            || leftIndex < Values.Length
                            && Values[leftIndex] >= other.Values[rightIndex])
                            value = Values[leftIndex++];
                        else value = other.Values[rightIndex++];
                        if (value != Values[index]) return true;
                    }
                    return false;
                }
                if (string.Equals(AggregationRule, "single_instance", StringComparison.Ordinal))
                    return Math.Max(Values[0], other.Values[0]) != Values[0];
                return Math.Min(Values[0], other.Values[0]) != Values[0]
                    || Values[1] + other.Values[1] != Values[1];
            }

            public ContributionState Merge(
                ContributionState other,
                CustomSearchCreationCounts diagnostics = null,
                bool checkBeforeAllocate = false)
            {
                if (checkBeforeAllocate && !WouldMergeChange(other)) return this;
                if (!string.Equals(Key, other.Key, StringComparison.Ordinal)
                    || !string.Equals(AggregationRule, other.AggregationRule, StringComparison.Ordinal)
                    || Cap != other.Cap)
                    throw new CustomEffectDataException("同一 StackGroup 的聚合声明不一致。");
                int[] values;
                if (string.Equals(AggregationRule, "cap_count", StringComparison.Ordinal))
                    values = MergeRanks(Values, other.Values, Cap);
                else if (string.Equals(AggregationRule, "single_instance", StringComparison.Ordinal))
                    values = new[] { Math.Max(Values[0], other.Values[0]) };
                else
                    values = new[] { Math.Min(Values[0], other.Values[0]), Values[1] + other.Values[1] };
                if (diagnostics != null) diagnostics.ContributionValueArrays++;
                if (ArraysEqual(values, Values)) return this;
                return new ContributionState(RuleIndex, StackGroupKey, AggregationRule, Cap, values);
            }

            private static int[] MergeRanks(int[] left, int[] right, int cap)
            {
                int length = Math.Min(cap, left.Length + right.Length);
                int[] result = new int[length];
                int leftIndex = 0;
                int rightIndex = 0;
                for (int index = 0; index < length; index++)
                {
                    if (rightIndex >= right.Length
                        || leftIndex < left.Length && left[leftIndex] >= right[rightIndex])
                        result[index] = left[leftIndex++];
                    else
                        result[index] = right[rightIndex++];
                }
                return result;
            }

            private static bool ArraysEqual(int[] left, int[] right)
            {
                if (ReferenceEquals(left, right)) return true;
                if (left.Length != right.Length) return false;
                for (int index = 0; index < left.Length; index++)
                    if (left[index] != right[index]) return false;
                return true;
            }
        }

        private sealed class ExclusiveOption
        {
            public ExclusiveOption(
                int exclusivityId,
                Dictionary<int, int> confirmedRuleRanks,
                int physicalCountState = 1,
                bool hasDifferingMatchedRuleSets = false,
                Dictionary<int, int> rawRuleCounts = null)
            {
                ExclusivityId = exclusivityId;
                ConfirmedRuleRanks = confirmedRuleRanks ?? new Dictionary<int, int>();
                PhysicalCountState = Math.Min(2, Math.Max(1, physicalCountState));
                HasDifferingMatchedRuleSets = hasDifferingMatchedRuleSets;
                RawRuleCounts = rawRuleCounts ?? ConfirmedRuleRanks.Keys
                    .ToDictionary(ruleIndex => ruleIndex, ruleIndex => 1);
                Key = exclusivityId + "/" + PhysicalCountState + "/"
                    + (HasDifferingMatchedRuleSets ? "different" : "same") + "/"
                    + string.Join(".", ConfirmedRuleRanks.OrderBy(item => item.Key)
                        .Select(item => item.Key + "=" + item.Value)) + "/raw="
                    + string.Join(".", RawRuleCounts.OrderBy(item => item.Key)
                        .Select(item => item.Key + "=" + item.Value));
            }

            public int ExclusivityId;
            public Dictionary<int, int> ConfirmedRuleRanks;
            public int PhysicalCountState;
            public bool HasDifferingMatchedRuleSets;
            public Dictionary<int, int> RawRuleCounts;
            public string Key;

            public ExclusiveOption Merge(ExclusiveOption other)
            {
                if (other == null || ExclusivityId != other.ExclusivityId)
                    throw new CustomEffectDataException("互斥组状态不一致。");
                bool sameMatchedRuleSet = ConfirmedRuleRanks.Keys.OrderBy(value => value)
                    .SequenceEqual(other.ConfirmedRuleRanks.Keys.OrderBy(value => value));
                Dictionary<int, int> intersection = ConfirmedRuleRanks.Keys
                    .Intersect(other.ConfirmedRuleRanks.Keys)
                    .ToDictionary(ruleIndex => ruleIndex,
                        ruleIndex => Math.Min(
                            ConfirmedRuleRanks[ruleIndex],
                            other.ConfirmedRuleRanks[ruleIndex]));
                Dictionary<int, int> rawCounts = RawRuleCounts.Keys
                    .Union(other.RawRuleCounts.Keys).ToDictionary(
                        ruleIndex => ruleIndex,
                        ruleIndex => (RawRuleCounts.ContainsKey(ruleIndex)
                                ? RawRuleCounts[ruleIndex] : 0)
                            + (other.RawRuleCounts.ContainsKey(ruleIndex)
                                ? other.RawRuleCounts[ruleIndex] : 0));
                return new ExclusiveOption(
                    ExclusivityId,
                    intersection,
                    Math.Min(2, PhysicalCountState + other.PhysicalCountState),
                    HasDifferingMatchedRuleSets
                        || other.HasDifferingMatchedRuleSets
                        || !sameMatchedRuleSet,
                    rawCounts);
            }
        }

        private sealed class StateExperimentContext
        {
            public StateExperimentContext(
                bool exactCapacityMerges,
                bool compiledSecondaryComparison,
                bool deferredFrontierStates,
                PreparedRule[] rules)
            {
                ExactCapacityMerges = exactCapacityMerges;
                CompiledSecondaryComparison = compiledSecondaryComparison;
                DeferredFrontierStates = deferredFrontierStates;
                if (compiledSecondaryComparison)
                    ComparisonOrder = Enumerable.Range(0, rules.Length)
                        .Where(index => rules[index].Source.IsRequired)
                        .Concat(Enumerable.Range(0, rules.Length)
                            .Where(index => !rules[index].Source.IsRequired))
                        .ToArray();
            }

            public readonly bool ExactCapacityMerges;
            public readonly bool CompiledSecondaryComparison;
            public readonly bool DeferredFrontierStates;
            public readonly int[] ComparisonOrder;
        }

        private sealed class SearchBudget
        {
            private readonly Stopwatch _stopwatch;
            private readonly CancellationToken _cancellationToken;
            private readonly CustomSearchLimits _limits;
            private long _peakStateCount;
            private long _visitedStateCount;
            private long _transitionCount;
            private readonly CustomSearchDiagnostics _diagnostics;
            private int _diagnosticLimit;

            public SearchBudget(Stopwatch stopwatch, CancellationToken cancellationToken, CustomSearchLimits limits,
                CustomSearchDiagnostics diagnostics = null, CustomSearchOptions options = null)
            {
                _stopwatch = stopwatch;
                _cancellationToken = cancellationToken;
                _limits = limits;
                _diagnostics = diagnostics;
                TerminalPruningEnabled = options != null && options.EnableTerminalPruning;
                TerminalBoundAudit = options == null ? null : options.TerminalBoundAudit;
                IntermediatePruningEnabled = options != null && options.EnableIntermediatePruning;
                DepthSeparatedPhasesEnabled = options != null && options.EnableDepthSeparatedPhases;
                TerminalBlockPruningEnabled = options != null && options.EnableTerminalBlockPruning;
                TopThreeThresholdEnabled = options != null && options.EnableTopThreeThreshold;
                GlobalBoundAudit = options == null ? null : options.GlobalBoundAudit;
                IntermediateBoundAudit = options == null ? null : options.IntermediateBoundAudit;
                OracleTest = options == null ? null : options.OracleReferenceTest;
                GuidanceTest = options == null ? null : options.ReferenceGuidanceTest;
                RequiredGapReferenceOrderingEnabled = options != null
                    && (options.EnableRequiredGapReferenceOrdering
                        || GuidanceTest != null
                        && GuidanceTest.UsesRequiredGapReferenceOrdering);
                DetailedCostDiagnostics = diagnostics != null && options != null
                    && options.EnableDetailedCostDiagnostics;
                ExactCapacityStateMerges = options != null
                    && options.EnableExactCapacityStateMerges;
                CompiledSecondaryComparison = options != null
                    && options.EnableCompiledSecondaryComparison;
                DeferredFrontierStates = options != null
                    && options.EnableDeferredFrontierStates;
            }

            public readonly bool TerminalPruningEnabled;
            public readonly Action<object> TerminalBoundAudit;
            public readonly bool IntermediatePruningEnabled;
            public readonly bool DepthSeparatedPhasesEnabled;
            public readonly bool TerminalBlockPruningEnabled;
            public readonly bool TopThreeThresholdEnabled;
            public readonly Action<object> GlobalBoundAudit;
            public double ElapsedMilliseconds { get { return _stopwatch.Elapsed.TotalMilliseconds; } }
            public readonly Action<object> IntermediateBoundAudit;
            public readonly bool RequiredGapReferenceOrderingEnabled;
            public readonly OracleReferenceTestHooks OracleTest;
            public OracleQueryContext OracleQuery;
            public readonly ReferenceGuidanceTestOptions GuidanceTest;
            public readonly bool DetailedCostDiagnostics;
            private readonly bool ExactCapacityStateMerges;
            private readonly bool CompiledSecondaryComparison;
            private readonly bool DeferredFrontierStates;
            public StateExperimentContext StateExperiment;
            public PreviousHintQueryContext PreviousHintQuery;

            public void ConfigureStateExperiment(PreparedRule[] rules)
            {
                if (!ExactCapacityStateMerges && !CompiledSecondaryComparison
                    && !DeferredFrontierStates) return;
                StateExperiment = new StateExperimentContext(
                    ExactCapacityStateMerges,
                    CompiledSecondaryComparison,
                    DeferredFrontierStates,
                    rules);
            }

            public CustomSearchDiagnostics Diagnostics { get { return _diagnostics; } }

            public long PeakStateCount { get { return Interlocked.Read(ref _peakStateCount); } }
            public long VisitedStateCount { get { return Interlocked.Read(ref _visitedStateCount); } }
            public long TransitionCount { get { return Interlocked.Read(ref _transitionCount); } }

            public void Checkpoint()
            {
                _cancellationToken.ThrowIfCancellationRequested();
                ThrowIfLimitExceeded();
            }

            public void RecordState(long liveStateCount)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                long visitedStateCount = Interlocked.Increment(ref _visitedStateCount);
                ObservePeakStateCount(liveStateCount);
                if (visitedStateCount > _limits.MaxVisitedStates)
                    ThrowLimit(CustomSearchDiagnosticOutcome.StateLimit);
                if ((visitedStateCount & 255L) == 0L)
                    ThrowIfElapsedExceeded();
            }

            public void RecordTransition()
            {
                _cancellationToken.ThrowIfCancellationRequested();
                long transitionCount = Interlocked.Increment(ref _transitionCount);
                if (transitionCount > _limits.MaxTransitions)
                    ThrowLimit(CustomSearchDiagnosticOutcome.TransitionLimit);
                if ((transitionCount & 255L) == 0L)
                    ThrowIfElapsedExceeded();
            }

            public void RecordTransitionAndState()
            {
                long transitionCount = Interlocked.Increment(ref _transitionCount);
                long visitedStateCount = Interlocked.Increment(ref _visitedStateCount);
                if (transitionCount > _limits.MaxTransitions
                    || visitedStateCount > _limits.MaxVisitedStates)
                    ThrowLimit(transitionCount > _limits.MaxTransitions
                        ? CustomSearchDiagnosticOutcome.TransitionLimit : CustomSearchDiagnosticOutcome.StateLimit);
                if ((transitionCount & 255L) == 0L)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    ThrowIfElapsedExceeded();
                }
            }

            public void RecordTransitionsAndStates(long count)
            {
                if (count <= 0) return;
                long transitionCount = Interlocked.Add(ref _transitionCount, count);
                long visitedStateCount = Interlocked.Add(ref _visitedStateCount, count);
                _cancellationToken.ThrowIfCancellationRequested();
                if (transitionCount > _limits.MaxTransitions
                    || visitedStateCount > _limits.MaxVisitedStates)
                    ThrowLimit(transitionCount > _limits.MaxTransitions
                        ? CustomSearchDiagnosticOutcome.TransitionLimit : CustomSearchDiagnosticOutcome.StateLimit);
                ThrowIfElapsedExceeded();
            }

            public void ObservePeakStateCount(long liveStateCount)
            {
                long current = Interlocked.Read(ref _peakStateCount);
                while (liveStateCount > current)
                {
                    long observed = Interlocked.CompareExchange(
                        ref _peakStateCount, liveStateCount, current);
                    if (observed == current) return;
                    current = observed;
                }
            }

            private void ThrowIfLimitExceeded()
            {
                if (Interlocked.Read(ref _visitedStateCount) > _limits.MaxVisitedStates
                    || Interlocked.Read(ref _transitionCount) > _limits.MaxTransitions)
                    ThrowLimit(Interlocked.Read(ref _visitedStateCount) > _limits.MaxVisitedStates
                        ? CustomSearchDiagnosticOutcome.StateLimit : CustomSearchDiagnosticOutcome.TransitionLimit);
                ThrowIfElapsedExceeded();
            }

            private void ThrowIfElapsedExceeded()
            {
                if (_stopwatch.ElapsedMilliseconds > _limits.MaxElapsedMilliseconds)
                    ThrowLimit(CustomSearchDiagnosticOutcome.TimeLimit);
            }

            private void ThrowLimit(CustomSearchDiagnosticOutcome reason)
            {
                if (_diagnostics != null
                    && Interlocked.CompareExchange(ref _diagnosticLimit, (int)reason, 0) == 0)
                    _diagnostics.Outcome = reason;
                throw new SearchBudgetExceededException();
            }
        }

        private sealed class SearchBudgetExceededException : Exception
        {
        }

        private sealed class ZeroRuleVesselSearch
        {
            private const int CompleteMask = (1 << 6) - 1;
            private readonly CustomEffectSearcher _owner;
            private readonly Candidate[] _candidates;
            private readonly VesselDefinition _vessel;
            private readonly string _characterId;
            private readonly PreparedRule[] _rules;
            private readonly SearchBudget _budget;
            private readonly CustomSearchVesselDiagnostics _diagnostics;

            public ZeroRuleVesselSearch(
                CustomEffectSearcher owner,
                Candidate[] candidates,
                VesselDefinition vessel,
                string characterId,
                PreparedRule[] rules,
                SearchBudget budget,
                CustomSearchVesselDiagnostics diagnostics = null)
            {
                _owner = owner;
                _candidates = candidates;
                _vessel = vessel;
                _characterId = characterId;
                _rules = rules;
                _budget = budget;
                _diagnostics = diagnostics;
            }

            public CustomSearchBuild Best { get; private set; }
            public long EvaluatedBuilds { get; private set; }

            public void Run()
            {
                ZeroRuleState[] states = new ZeroRuleState[CompleteMask + 1];
                states[0] = new ZeroRuleState(new Candidate[6], 0, 0, 0);
                ZeroRuleState[] observedStates = states;
                CustomSearchPhaseDiagnostics phase = _diagnostics == null ? null : new CustomSearchPhaseDiagnostics
                {
                    PhaseIndex = 0, SlotMask = CompleteMask, Slots = new[] { 0, 1, 2, 3, 4, 5 },
                    CandidateCount = _candidates.Length, AggregateFrontierInputCount = 1
                };
                long phaseStart = _diagnostics == null ? 0 : Stopwatch.GetTimestamp();
                if (phase != null)
                {
                    _diagnostics.Phases.Add(phase);
                    _diagnostics.ConfiguredPhaseSlotMasks = new[] { CompleteMask };
                    _diagnostics.Creations.SixSlotArrays++;
                    _budget.Diagnostics.ActivePhaseIndex = 0;
                    _budget.Diagnostics.ActiveOperation = "ZeroRuleExpansion";
                }
                try
                {
                _budget.RecordState(1);
                foreach (IGrouping<int, Candidate> instanceGroup in _candidates
                    .GroupBy(item => item.Relic.InstanceId)
                    .OrderBy(group => group.Key))
                {
                    _budget.Checkpoint();
                    ZeroRuleState[] next = (ZeroRuleState[])states.Clone();
                    observedStates = next;
                    foreach (Candidate candidate in instanceGroup.OrderBy(item => item.ProjectionKey, StringComparer.Ordinal))
                    {
                        if (phase != null) phase.CandidatesStarted++;
                        for (int mask = 0; mask <= CompleteMask; mask++)
                        {
                            ZeroRuleState source = states[mask];
                            if (source == null) continue;
                            int depth = phase == null ? 0 : DiagnosticFilledSlotCount(mask);
                            if (phase != null) phase.Depths[depth].InputStateVisits++;
                            for (int outputSlot = 0; outputSlot < 6; outputSlot++)
                            {
                                int bit = 1 << outputSlot;
                                if (phase != null && (mask & bit) == 0)
                                {
                                    if (!IsCanonicalEquivalentSlot(_vessel, mask, outputSlot))
                                        phase.Depths[depth].SymmetrySlotAttempts++;
                                    else if (!AcceptsSlot(candidate, outputSlot))
                                        phase.Depths[depth].IncompatibleSlotAttempts++;
                                }
                                if ((mask & bit) != 0
                                    || !IsCanonicalEquivalentSlot(_vessel, mask, outputSlot)
                                    || !AcceptsSlot(candidate, outputSlot)) continue;
                                _budget.RecordTransitionAndState();
                                int nextMask = mask | bit;
                                ZeroRuleState contender = source.Add(candidate, outputSlot);
                                bool exists = next[nextMask] != null;
                                bool replace = !exists || CompareStates(contender, next[nextMask], nextMask) < 0;
                                if (replace)
                                    next[nextMask] = contender;
                                if (phase != null)
                                {
                                    _diagnostics.Creations.SixSlotArrays++;
                                    CustomSearchDepthDiagnostics output = phase.Depths[depth + 1];
                                    output.GeneratedStates++;
                                    if (exists)
                                    {
                                        output.EquivalentStateHits++;
                                        if (replace) output.ReplacedStates++;
                                        else output.DiscardedStates++;
                                    }
                                }
                                if (nextMask == CompleteMask) EvaluatedBuilds++;
                            }
                        }
                        if (phase != null) phase.CandidatesCompleted++;
                    }
                    states = next;
                    _budget.ObservePeakStateCount(states.Count(item => item != null));
                }
                if (phase != null) phase.Completed = true;
                }
                finally
                {
                    if (phase != null)
                    {
                        phase.ElapsedMilliseconds = CustomSearchDiagnostics.MillisecondsSince(phaseStart);
                        phase.ExpansionMilliseconds = phase.ElapsedMilliseconds;
                        for (int mask = 0; mask <= CompleteMask; mask++)
                            if (observedStates[mask] != null) phase.Depths[DiagnosticFilledSlotCount(mask)].RetainedStates++;
                        phase.LocalCompletedFrontierCount = observedStates[CompleteMask] == null ? 0 : 1;
                        phase.AggregateFrontierOutputCount = phase.LocalCompletedFrontierCount;
                        _diagnostics.ObservedRetainedEntryPeak = observedStates.Count(item => item != null);
                    }
                }
                ZeroRuleState complete = states[CompleteMask];
                if (complete == null) return;
                if (_diagnostics != null) _budget.Diagnostics.ActiveOperation = "VesselResultAssembly";
                long evaluationStart = _diagnostics == null ? 0 : Stopwatch.GetTimestamp();
                try
                {
                Best = _owner.CreateSearchBuild(
                    _vessel, complete.Selected, _characterId, _rules, _budget, null);
                if (_diagnostics != null) _diagnostics.CompletedEvaluationCount++;
                }
                finally
                {
                    if (_diagnostics != null)
                        _diagnostics.ResultAssemblyMilliseconds = CustomSearchDiagnostics.MillisecondsSince(evaluationStart);
                }
            }

            private static int DiagnosticFilledSlotCount(int mask)
            {
                int count = 0;
                while (mask != 0) { count += mask & 1; mask >>= 1; }
                return count;
            }

            private bool AcceptsSlot(Candidate candidate, int outputSlot)
            {
                bool deep = outputSlot >= 3;
                if (candidate.Relic.IsDeep != deep) return false;
                int localSlot = deep ? outputSlot - 3 : outputSlot;
                int colorId = deep
                    ? _vessel.DeepSlotColorIds[localSlot]
                    : _vessel.OrdinarySlotColorIds[localSlot];
                return AcceptsColor(colorId, candidate.Relic.ColorId);
            }

            private static int CompareStates(ZeroRuleState left, ZeroRuleState right, int mask)
            {
                int value = left.NegativeEffectCount.CompareTo(right.NegativeEffectCount);
                if (value != 0) return value;
                value = left.NegativeRelicCount.CompareTo(right.NegativeRelicCount);
                if (value != 0) return value;
                value = left.UncertaintyCount.CompareTo(right.UncertaintyCount);
                if (value != 0) return value;
                for (int outputSlot = 0; outputSlot < 6; outputSlot++)
                {
                    if ((mask & (1 << outputSlot)) == 0) continue;
                    value = left.Selected[outputSlot].Relic.InstanceId
                        .CompareTo(right.Selected[outputSlot].Relic.InstanceId);
                    if (value != 0) return value;
                }
                return 0;
            }
        }

        private sealed class ZeroRuleState
        {
            public ZeroRuleState(
                Candidate[] selected,
                int negativeEffectCount,
                int negativeRelicCount,
                int uncertaintyCount)
            {
                Selected = selected;
                NegativeEffectCount = negativeEffectCount;
                NegativeRelicCount = negativeRelicCount;
                UncertaintyCount = uncertaintyCount;
            }

            public Candidate[] Selected;
            public int NegativeEffectCount;
            public int NegativeRelicCount;
            public int UncertaintyCount;

            public ZeroRuleState Add(Candidate candidate, int outputSlot)
            {
                Candidate[] selected = (Candidate[])Selected.Clone();
                selected[outputSlot] = candidate;
                return new ZeroRuleState(
                    selected,
                    NegativeEffectCount + candidate.NegativeEffectCount,
                    NegativeRelicCount + candidate.NegativeRelicCount,
                    UncertaintyCount + candidate.UncertaintyCount);
            }
        }

        private sealed partial class FrontierVesselSearch
        {
            private const int CompleteMask = (1 << 6) - 1;
            private readonly CustomEffectSearcher _owner;
            private readonly PreparedRule[] _rules;
            private readonly Candidate[] _candidates;
            private readonly VesselDefinition _vessel;
            private readonly string _characterId;
            private readonly SearchBudget _budget;
            private readonly int[] _canonicalAvailableSlotMasks;
            private Candidate[] _bestCandidates;
            private CustomComparisonVector _bestVector;
            private readonly CustomSearchVesselDiagnostics _diagnostics;
            private CustomSearchPhaseDiagnostics _phaseDiagnostics;
            private long _mergeRetainedInputCount;

            public FrontierVesselSearch(
                CustomEffectSearcher owner,
                PreparedRule[] rules,
                Candidate[] candidates,
                VesselDefinition vessel,
                string characterId,
                SearchBudget budget,
                CustomSearchVesselDiagnostics diagnostics = null)
            {
                _owner = owner;
                _rules = rules;
                _candidates = candidates;
                _vessel = vessel;
                _characterId = characterId;
                _budget = budget;
                _canonicalAvailableSlotMasks = BuildCanonicalAvailableSlotMasks(vessel);
                _diagnostics = diagnostics;
            }

            public CustomSearchBuild Best { get; private set; }
            public long EvaluatedBuilds { get; private set; }

            public void Run()
            {
                if (PrepareGlobalBound()) return;
                GeneralFrontierState initial = GeneralFrontierState.CreateInitial(_rules.Length,
                    _diagnostics == null ? null : _diagnostics.Creations);
                Dictionary<SemanticStateKey, GeneralFrontierState> aggregate =
                    new Dictionary<SemanticStateKey, GeneralFrontierState>();
                aggregate.Add(initial.SemanticKey, initial);
                _budget.RecordState(1);

                long configurationStart = _diagnostics == null ? 0 : Stopwatch.GetTimestamp();
                SearchPhase[] phases = BuildSearchPhases();
                ConfigureCrossPhaseWinnerIdentity(phases);
                if (_diagnostics != null)
                {
                    _diagnostics.PhaseConfigurationMilliseconds = CustomSearchDiagnostics.MillisecondsSince(configurationStart);
                    _diagnostics.ConfiguredPhaseSlotMasks = phases.Select(item => item.SlotMask).ToArray();
                }
                int aggregateMask = 0;
                Dictionary<TerminalSemanticStateKey, GeneralFrontierState> terminal = null;
                for (int phaseIndex = 0; phaseIndex < phases.Length; phaseIndex++)
                {
                    SearchPhase phase = phases[phaseIndex];
                    bool finalPhase = phaseIndex == phases.Length - 1;
                    long phaseStart = _diagnostics == null ? 0 : Stopwatch.GetTimestamp();
                    if (_diagnostics != null)
                    {
                        _phaseDiagnostics = new CustomSearchPhaseDiagnostics
                        {
                            PhaseIndex = phaseIndex, SlotMask = phase.SlotMask,
                            Slots = DiagnosticSlots(phase.SlotMask),
                            CandidateCount = phase.Candidates.Length,
                            AggregateFrontierInputCount = aggregate.Count
                        };
                        if (_budget.DetailedCostDiagnostics)
                            _phaseDiagnostics.ExpansionCost = new CustomSearchCostDiagnostics();
                        _diagnostics.Phases.Add(_phaseDiagnostics);
                        _budget.Diagnostics.ActivePhaseIndex = phaseIndex;
                        _budget.Diagnostics.ActiveOperation = "LocalExpansion";
                    }
                    Dictionary<SemanticStateKey, GeneralFrontierState>[] localFrontiers =
                        new Dictionary<SemanticStateKey, GeneralFrontierState>[CompleteMask];
                    for (int mask = 0; mask < CompleteMask; mask++)
                        localFrontiers[mask] =
                            new Dictionary<SemanticStateKey, GeneralFrontierState>();
                    Dictionary<SemanticStateKey, GeneralFrontierState> localCompleted =
                        new Dictionary<SemanticStateKey, GeneralFrontierState>();
                    localFrontiers[0].Add(initial.SemanticKey, initial);
                    long expansionStart = _diagnostics == null ? 0 : Stopwatch.GetTimestamp();
                    try
                    {
                    try
                    {
                    _budget.RecordState(1);
                    int[][] phaseMasks = BuildPhaseMasksByFilledSlotCount(
                        0, phase.SlotMask);
                    for (int candidateIndex = 0;
                        candidateIndex < phase.Candidates.Length; candidateIndex++)
                    {
                        Candidate candidate = phase.Candidates[candidateIndex];
                        if (_phaseDiagnostics != null) _phaseDiagnostics.CandidatesStarted++;
                        ExpandCandidate(candidate, phaseMasks,
                            localFrontiers, localCompleted, phase.SlotMask);
                        if (_phaseDiagnostics != null) _phaseDiagnostics.CandidatesCompleted++;
                        long liveStateCount = aggregate.Count
                            + CountFrontierStates(localFrontiers) + localCompleted.Count;
                        _budget.ObservePeakStateCount(liveStateCount);
                        ObserveRetainedEntries(liveStateCount);
                    }
                    }
                    finally
                    {
                        if (_phaseDiagnostics != null)
                        {
                            _phaseDiagnostics.ExpansionMilliseconds = CustomSearchDiagnostics.MillisecondsSince(expansionStart);
                            _phaseDiagnostics.LocalCompletedFrontierCount = localCompleted.Count;
                            for (int mask = 0; mask < localFrontiers.Length; mask++)
                                _phaseDiagnostics.Depths[PopCount(mask)].RetainedStates += localFrontiers[mask].Count;
                            _phaseDiagnostics.Depths[PopCount(phase.SlotMask)].RetainedStates += localCompleted.Count;
                            ObserveRetainedEntries(aggregate.Count + CountFrontierStates(localFrontiers) + localCompleted.Count);
                        }
                    }
                    if (localCompleted.Count == 0)
                    {
                        if (_phaseDiagnostics != null) _phaseDiagnostics.Completed = true;
                        return;
                    }
                    long mergeStart = _diagnostics == null ? 0 : Stopwatch.GetTimestamp();
                    long mergeTransitionStart = _diagnostics == null ? 0 : _budget.TransitionCount;
                    if (_phaseDiagnostics != null)
                    {
                        _mergeRetainedInputCount = aggregate.Count + localCompleted.Count;
                        _phaseDiagnostics.Merge = new CustomSearchMergeDiagnostics
                        {
                            Kind = aggregateMask == 0 ? (finalPhase ? "TerminalCollapse" : "AdoptLocal")
                                : (finalPhase ? "TerminalJoin" : "SemanticJoin"),
                            AggregateInputCount = aggregate.Count, LocalInputCount = localCompleted.Count,
                            TheoreticalPairCount = aggregateMask == 0 ? 0 : (long)aggregate.Count * localCompleted.Count,
                            WorkerCount = 1
                        };
                        if (_budget.DetailedCostDiagnostics)
                            _phaseDiagnostics.Merge.Cost = new CustomSearchCostDiagnostics();
                        _budget.Diagnostics.ActiveOperation = _phaseDiagnostics.Merge.Kind;
                    }
                    try
                    {
                    if (finalPhase)
                    {
                        terminal = aggregateMask == 0
                            ? CollapseTerminalStates(localCompleted)
                            : CombineTerminalPhaseFrontiers(
                                aggregate, localCompleted, aggregateMask, phase.SlotMask);
                    }
                    else
                    {
                        aggregate = CombinePhaseFrontiers(
                            aggregate, localCompleted, aggregateMask, phase.SlotMask);
                        aggregateMask |= phase.SlotMask;
                        _budget.ObservePeakStateCount(aggregate.Count);
                    }
                    if (_phaseDiagnostics != null)
                    {
                        _phaseDiagnostics.Merge.Completed = true;
                        _phaseDiagnostics.Merge.OutputStateCount = finalPhase ? terminal.Count : aggregate.Count;
                        _phaseDiagnostics.AggregateFrontierOutputCount = _phaseDiagnostics.Merge.OutputStateCount;
                    }
                    }
                    finally
                    {
                        if (_phaseDiagnostics != null)
                        {
                            _phaseDiagnostics.Merge.ElapsedMilliseconds = CustomSearchDiagnostics.MillisecondsSince(mergeStart);
                            _phaseDiagnostics.Merge.TransitionCount = _budget.TransitionCount - mergeTransitionStart;
                            if (finalPhase) _diagnostics.TerminalMergeMilliseconds += _phaseDiagnostics.Merge.ElapsedMilliseconds;
                        }
                    }
                    if (_phaseDiagnostics != null) _phaseDiagnostics.Completed = true;
                    }
                    finally
                    {
                        if (_phaseDiagnostics != null)
                        {
                            _phaseDiagnostics.ElapsedMilliseconds = CustomSearchDiagnostics.MillisecondsSince(phaseStart);
                            if (_diagnostics.Cost != null)
                            {
                                _diagnostics.Cost.Add(_phaseDiagnostics.ExpansionCost);
                                _diagnostics.Cost.Add(_phaseDiagnostics.Merge == null
                                    ? null : _phaseDiagnostics.Merge.Cost);
                            }
                        }
                    }
                }

                if (terminal == null || terminal.Count == 0) return;
                if (_diagnostics != null) _budget.Diagnostics.ActiveOperation = "EffectiveEvaluation";
                long evaluationStart = _diagnostics == null ? 0 : Stopwatch.GetTimestamp();
                try { EvaluateTerminalStates(terminal); }
                finally
                {
                    if (_diagnostics != null)
                        _diagnostics.EffectiveEvaluationMilliseconds = CustomSearchDiagnostics.MillisecondsSince(evaluationStart);
                }

                if (_bestCandidates != null)
                {
                    if (_diagnostics != null) _budget.Diagnostics.ActiveOperation = "VesselResultAssembly";
                    long assemblyStart = _diagnostics == null ? 0 : Stopwatch.GetTimestamp();
                    try
                    {
                    if (_diagnostics != null && _intermediate != null) RecordIntermediateReferenceQuality();
                    Best = _owner.CreateSearchBuild(
                        _vessel, _bestCandidates, _characterId, _rules, _budget, _bestVector);
                    }
                    finally
                    {
                        if (_diagnostics != null)
                            _diagnostics.ResultAssemblyMilliseconds = CustomSearchDiagnostics.MillisecondsSince(assemblyStart);
                    }
                }
            }

            private void ExpandCandidate(
                Candidate candidate,
                int[][] masksByFilledSlotCount,
                Dictionary<SemanticStateKey, GeneralFrontierState>[] frontiers,
                Dictionary<SemanticStateKey, GeneralFrontierState> completed,
                int completionMask)
            {
                _budget.Checkpoint();
                int acceptedSlotMask = AcceptedSlotMask(candidate);
                for (int filledSlots = masksByFilledSlotCount.Length - 1;
                    filledSlots >= 0; filledSlots--)
                {
                    long depthStart = _budget.DetailedCostDiagnostics ? Stopwatch.GetTimestamp() : 0;
                    try
                    {
                    foreach (int mask in masksByFilledSlotCount[filledSlots])
                    {
                        Dictionary<SemanticStateKey, GeneralFrontierState> sourceFrontier =
                            frontiers[mask];
                        if (sourceFrontier.Count == 0) continue;
                        int incompatibleSlots = 0, symmetrySlots = 0;
                        if (_phaseDiagnostics != null)
                        {
                            int remainingSlots = completionMask & ~mask;
                            incompatibleSlots = PopCount(remainingSlots & ~acceptedSlotMask);
                            symmetrySlots = PopCount(remainingSlots & acceptedSlotMask & ~_canonicalAvailableSlotMasks[mask]);
                        }
                        foreach (GeneralFrontierState source in sourceFrontier.Values)
                        {
                            if (_phaseDiagnostics != null)
                            {
                                CustomSearchDepthDiagnostics sourceDepth = _phaseDiagnostics.Depths[filledSlots];
                                sourceDepth.InputStateVisits++;
                                sourceDepth.IncompatibleSlotAttempts += incompatibleSlots;
                                sourceDepth.SymmetrySlotAttempts += symmetrySlots;
                            }
                            int availableSlots = acceptedSlotMask
                                & _canonicalAvailableSlotMasks[mask];
                            GeneralFrontierState semanticTemplate = null;
                            GeneralFrontierProbe? semanticProbeTemplate = null;
                            while (availableSlots != 0)
                            {
                                int bit = availableSlots & -availableSlots;
                                availableSlots &= availableSlots - 1;
                                int outputSlot = SlotIndex(bit);
                                _budget.RecordTransitionAndState();
                                int nextMask = mask | bit;
                                CustomSearchCostDiagnostics expansionCost =
                                    _phaseDiagnostics == null ? null
                                    : _phaseDiagnostics.ExpansionCost;
                                CustomSearchDepthDiagnostics destinationDepth =
                                    _phaseDiagnostics == null ? null
                                    : _phaseDiagnostics.Depths[filledSlots + 1];
                                if (_budget.StateExperiment != null
                                    && _budget.StateExperiment.DeferredFrontierStates)
                                {
                                    GeneralFrontierProbe probe =
                                        GeneralFrontierState.AddProbe(
                                            GeneralFrontierState.CreateProbe(source),
                                            candidate, outputSlot, _rules,
                                            semanticProbeTemplate,
                                            _diagnostics == null ? null
                                                : _diagnostics.Creations,
                                            expansionCost,
                                            _budget.StateExperiment);
                                    if (destinationDepth != null)
                                        destinationDepth.GeneratedStates++;
                                    if (!semanticProbeTemplate.HasValue)
                                        semanticProbeTemplate = probe;
                                    GeneralFrontierState existingProbeState;
                                    bool probeExists;
                                    Dictionary<SemanticStateKey, GeneralFrontierState> probeTarget =
                                        nextMask == completionMask
                                            ? completed : frontiers[nextMask];
                                    bool replaceProbe = TimedFrontierDecision(
                                        probeTarget, probe, nextMask, expansionCost,
                                        out existingProbeState, out probeExists);
                                    RecordEquivalent(destinationDepth, probeExists, replaceProbe);
                                    continue;
                                }
                                GeneralFrontierState contender = source.Add(
                                    candidate, outputSlot, _rules, semanticTemplate, true,
                                    _diagnostics == null ? null : _diagnostics.Creations,
                                    false, expansionCost,
                                    _budget.StateExperiment);
                                if (destinationDepth != null) destinationDepth.GeneratedStates++;
                                if (semanticTemplate == null) semanticTemplate = contender;
                                if (nextMask == completionMask)
                                {
                                    if (CheckGlobalPartial(contender, nextMask)) continue;
                                    GeneralFrontierState existing;
                                    bool exists;
                                    bool replace = TimedFrontierDecision(completed, contender,
                                        completionMask, expansionCost,
                                        out existing, out exists);
                                    RecordEquivalent(destinationDepth, exists, replace);
                                    continue;
                                }

                                Dictionary<SemanticStateKey, GeneralFrontierState> target =
                                    frontiers[nextMask];
                                GeneralFrontierState existingState;
                                bool stateExists;
                                bool replaceState = TimedFrontierDecision(target, contender,
                                    nextMask, expansionCost,
                                    out existingState, out stateExists);
                                RecordEquivalent(destinationDepth, stateExists, replaceState);
                            }
                        }
                    }
                    }
                    finally
                    {
                        if (_budget.DetailedCostDiagnostics)
                            _phaseDiagnostics.Depths[filledSlots].ElapsedMilliseconds +=
                                CustomSearchDiagnostics.MillisecondsSince(depthStart);
                    }
                }
            }

            private bool TimedFrontierDecision(
                Dictionary<SemanticStateKey, GeneralFrontierState> target,
                GeneralFrontierState contender, int mask, CustomSearchCostDiagnostics cost,
                out GeneralFrontierState existing, out bool exists)
            {
                bool sample = cost != null && (cost.DictionaryOperations++ & 255L) == 0;
                long started = sample ? Stopwatch.GetTimestamp() : 0;
                exists = target.TryGetValue(contender.SemanticKey, out existing);
                if (sample)
                {
                    cost.DictionarySamples++;
                    cost.DictionarySampleMilliseconds += CustomSearchDiagnostics.MillisecondsSince(started);
                }
                bool replace = true;
                if (exists)
                {
                    started = sample ? Stopwatch.GetTimestamp() : 0;
                    replace = CompareSecondary(contender, existing, mask, _rules,
                        _budget.StateExperiment) < 0;
                    if (sample)
                        cost.SecondaryCompareSampleMilliseconds += CustomSearchDiagnostics.MillisecondsSince(started);
                }
                if (replace)
                {
                    started = sample ? Stopwatch.GetTimestamp() : 0;
                    target[contender.SemanticKey] = contender;
                    if (sample)
                        cost.DictionarySampleMilliseconds += CustomSearchDiagnostics.MillisecondsSince(started);
                }
                return replace;
            }

            private bool TimedFrontierDecision(
                Dictionary<SemanticStateKey, GeneralFrontierState> target,
                GeneralFrontierProbe contender, int mask, CustomSearchCostDiagnostics cost,
                out GeneralFrontierState existing, out bool exists)
            {
                bool sample = cost != null && (cost.DictionaryOperations++ & 255L) == 0;
                long started = sample ? Stopwatch.GetTimestamp() : 0;
                exists = target.TryGetValue(contender.SemanticKey, out existing);
                if (sample)
                {
                    cost.DictionarySamples++;
                    cost.DictionarySampleMilliseconds +=
                        CustomSearchDiagnostics.MillisecondsSince(started);
                }
                bool replace = true;
                if (exists)
                {
                    started = sample ? Stopwatch.GetTimestamp() : 0;
                    replace = CompareSecondary(contender, existing, mask, _rules,
                        _budget.StateExperiment) < 0;
                    if (sample)
                        cost.SecondaryCompareSampleMilliseconds +=
                            CustomSearchDiagnostics.MillisecondsSince(started);
                }
                if (replace)
                {
                    started = sample ? Stopwatch.GetTimestamp() : 0;
                    GeneralFrontierState retained = GeneralFrontierState.MaterializeProbe(
                        contender, _diagnostics == null ? null : _diagnostics.Creations);
                    target[contender.SemanticKey] = retained;
                    if (sample)
                        cost.DictionarySampleMilliseconds +=
                            CustomSearchDiagnostics.MillisecondsSince(started);
                }
                else if (_diagnostics != null)
                    _diagnostics.Creations.DeferredStateDiscards++;
                return replace;
            }

            private static void RecordEquivalent(CustomSearchDepthDiagnostics depth, bool exists, bool replace)
            {
                if (depth == null || !exists) return;
                depth.EquivalentStateHits++;
                if (replace) depth.ReplacedStates++;
                else depth.DiscardedStates++;
            }

            private static int[] DiagnosticSlots(int slotMask)
            {
                int[] slots = new int[PopCount(slotMask)];
                int index = 0;
                for (int slot = 0; slot < 6; slot++)
                    if ((slotMask & (1 << slot)) != 0) slots[index++] = slot;
                return slots;
            }

            private void ObserveRetainedEntries(long count)
            {
                if (_diagnostics != null)
                    _diagnostics.ObservedRetainedEntryPeak = Math.Max(_diagnostics.ObservedRetainedEntryPeak, count);
            }

            private Dictionary<SemanticStateKey, GeneralFrontierState> CombinePhaseFrontiers(
                Dictionary<SemanticStateKey, GeneralFrontierState> aggregate,
                Dictionary<SemanticStateKey, GeneralFrontierState> local,
                int aggregateMask,
                int phaseMask)
            {
                if (aggregateMask == 0) return local;
                Dictionary<SemanticStateKey, GeneralFrontierState> result =
                    new Dictionary<SemanticStateKey, GeneralFrontierState>();
                int combinedMask = aggregateMask | phaseMask;
                IntermediateMergeContext pruning = null;
                TerminalPruningWork pruningWork = null;
                CustomSearchCostDiagnostics mergeCost = null;
                CostBatchTimer batchTimer = null;
                long checkedPairs = 0;
                long loopStart = 0;
                try
                {
                if (_budget.IntermediatePruningEnabled)
                {
                    pruning = PrepareIntermediateMerge(aggregate, local, combinedMask);
                    if (pruning != null && _diagnostics != null)
                        pruningWork = new TerminalPruningWork(_rules.Length, _budget.DetailedCostDiagnostics);
                }
                mergeCost = pruningWork != null && pruningWork.Cost != null
                    ? pruningWork.Cost
                    : (_budget.DetailedCostDiagnostics ? _phaseDiagnostics.Merge.Cost : null);
                batchTimer = mergeCost == null ? null : new CostBatchTimer(mergeCost);
                if (_budget.DetailedCostDiagnostics) loopStart = Stopwatch.GetTimestamp();
                foreach (GeneralFrontierState localState in local.Values)
                {
                    int[] localSlots = Enumerable.Range(0, 6)
                        .Where(slot => localState.Selected[slot] != null)
                        .OrderBy(slot => localState.Selected[slot].Relic.InstanceId)
                        .ToArray();
                    foreach (GeneralFrontierState aggregateState in aggregate.Values)
                    {
                        if (batchTimer != null) batchTimer.BeginPair();
                        if (pruning != null)
                        {
                            if ((checkedPairs++ & 255) == 0) _budget.Checkpoint();
                            if (CheckIntermediateBound(pruning, aggregateState, localState, aggregateMask, phaseMask, pruningWork))
                            {
                                if (batchTimer != null) batchTimer.EndPair(false);
                                continue;
                            }
                        }
                        GeneralFrontierState existing;
                        bool exists;
                        bool replace;
                        if (_budget.StateExperiment != null
                            && _budget.StateExperiment.DeferredFrontierStates)
                        {
                            GeneralFrontierProbe probe =
                                GeneralFrontierState.CreateProbe(aggregateState);
                            foreach (int slot in localSlots)
                            {
                                _budget.RecordTransitionAndState();
                                probe = GeneralFrontierState.AddProbe(
                                    probe, localState.Selected[slot], slot, _rules,
                                    null,
                                    _diagnostics == null ? null : _diagnostics.Creations,
                                    mergeCost, _budget.StateExperiment);
                            }
                            replace = TimedFrontierDecision(
                                result, probe, combinedMask, mergeCost,
                                out existing, out exists);
                        }
                        else
                        {
                            GeneralFrontierState contender = aggregateState;
                            foreach (int slot in localSlots)
                            {
                                _budget.RecordTransitionAndState();
                                contender = contender.Add(
                                    localState.Selected[slot], slot, _rules, null, true,
                                    _diagnostics == null ? null : _diagnostics.Creations,
                                    false, mergeCost, _budget.StateExperiment);
                            }
                            replace = TimedFrontierDecision(
                                result, contender, combinedMask,
                                mergeCost, out existing, out exists);
                        }
                        if (_phaseDiagnostics != null)
                        {
                            _phaseDiagnostics.Merge.CompletedPairCount++;
                            if (exists)
                            {
                                _phaseDiagnostics.Merge.EquivalentStateHits++;
                                if (replace) _phaseDiagnostics.Merge.ReplacedStates++;
                                else _phaseDiagnostics.Merge.DiscardedStates++;
                            }
                        }
                        if (batchTimer != null) batchTimer.EndPair(true);
                    }
                    _budget.Checkpoint();
                }
                return result;
                }
                finally
                {
                    if (batchTimer != null) batchTimer.Finish();
                    if (mergeCost != null)
                    {
                        double elapsed = CustomSearchDiagnostics.MillisecondsSince(loopStart);
                        mergeCost.LoopWallMilliseconds = elapsed;
                        mergeCost.WorkerMillisecondsSum = elapsed;
                        mergeCost.WorkerMillisecondsMax = elapsed;
                        mergeCost.TimedWorkerCount = 1;
                    }
                    if (pruningWork != null) AccumulatePruningWork(pruningWork);
                    if (_phaseDiagnostics != null)
                    {
                        _phaseDiagnostics.Merge.OutputStateCount = result.Count;
                        ObserveRetainedEntries(_mergeRetainedInputCount + result.Count);
                    }
                }
            }

            private Dictionary<TerminalSemanticStateKey, GeneralFrontierState>
                CombineTerminalPhaseFrontiers(
                    Dictionary<SemanticStateKey, GeneralFrontierState> aggregate,
                    Dictionary<SemanticStateKey, GeneralFrontierState> local,
                    int aggregateMask,
                    int phaseMask)
            {
                GeneralFrontierState[] localStates = local.Values.ToArray();
                GeneralFrontierState[] aggregateStates = aggregate.Values.ToArray();
                TerminalPruningContext pruning = _budget.TerminalPruningEnabled
                    ? PrepareTerminalPruning(aggregateStates, localStates) : null;
                long pairCount = (long)localStates.Length * aggregateStates.Length;
                int workerCount = Math.Min(6,
                    Math.Min(Environment.ProcessorCount, localStates.Length));
                if (workerCount <= 1 || pairCount < 50000)
                    return CombineTerminalPhaseFrontiersSequential(
                        aggregateStates, localStates, pruning);

                TerminalPartitionResult[] partitions =
                    new TerminalPartitionResult[workerCount];
                TerminalWorkDiagnostics[] workDiagnostics = _diagnostics == null ? null
                    : new TerminalWorkDiagnostics[workerCount];
                if (_phaseDiagnostics != null) _phaseDiagnostics.Merge.WorkerCount = workerCount;
                try
                {
                    long parallelStart = _budget.DetailedCostDiagnostics ? Stopwatch.GetTimestamp() : 0;
                    try
                    {
                    Parallel.For(0, workerCount,
                        new ParallelOptions { MaxDegreeOfParallelism = workerCount },
                        workerIndex =>
                        {
                            Dictionary<TerminalSemanticStateKey, GeneralFrontierState> partial =
                                new Dictionary<TerminalSemanticStateKey, GeneralFrontierState>();
                            Dictionary<FinalCoProfileStateKey, int[]> coLocationCache =
                                new Dictionary<FinalCoProfileStateKey, int[]>();
                            TerminalWorkDiagnostics work = workDiagnostics == null ? null
                                : new TerminalWorkDiagnostics(_budget.DetailedCostDiagnostics);
                            if (work != null) workDiagnostics[workerIndex] = work;
                            if (work != null && pruning != null)
                                work.Pruning = new TerminalPruningWork(_rules.Length, _budget.DetailedCostDiagnostics);
                            GeneralFrontierState workspace = GeneralFrontierState.CreateTerminalWorkspace(
                                work == null ? null : work.Creations);
                            long pendingTransitions = 0;
                            long checkedPairs = 0;
                            long workerStart = _budget.DetailedCostDiagnostics ? Stopwatch.GetTimestamp() : 0;
                            CostBatchTimer batchTimer = work == null || work.Cost == null
                                ? null : new CostBatchTimer(work.Cost);
                            try
                            {
                                for (int localIndex = workerIndex;
                                    localIndex < localStates.Length;
                                    localIndex += workerCount)
                                {
                                    GeneralFrontierState localState = localStates[localIndex];
                                    int[] localSlots = SelectedSlotsByInstanceId(localState);
                                    for (int aggregateIndex = 0; aggregateIndex < aggregateStates.Length; aggregateIndex++)
                                    {
                                        int skipped = (aggregateIndex & (TerminalBlockSize - 1)) == 0
                                            ? SkipTerminalBlock(pruning, aggregateIndex, localIndex,
                                                aggregateStates, localState, work == null ? null : work.Pruning) : 0;
                                        if (skipped != 0) { aggregateIndex += skipped - 1; continue; }
                                        if (batchTimer != null) batchTimer.BeginPair();
                                        GeneralFrontierState aggregateState = aggregateStates[aggregateIndex];
                                        if (pruning != null)
                                        {
                                            if ((checkedPairs++ & 255L) == 0) _budget.Checkpoint();
                                            if (CheckTerminalBound(pruning, aggregateIndex, localIndex,
                                                aggregateState, localState, work == null ? null : work.Pruning))
                                            {
                                                if (batchTimer != null) batchTimer.EndPair(false);
                                                continue;
                                            }
                                        }
                                        workspace.ResetTerminalWorkspace(aggregateState);
                                        GeneralFrontierState contender = workspace;
                                        foreach (int slot in localSlots)
                                        {
                                            pendingTransitions++;
                                            if (pendingTransitions == 256)
                                            {
                                                _budget.RecordTransitionsAndStates(
                                                    pendingTransitions);
                                                pendingTransitions = 0;
                                            }
                                            contender = contender.Add(
                                                localState.Selected[slot], slot, _rules,
                                                null, false, work == null ? null : work.Creations, true,
                                                work == null ? null : work.Cost, _budget.StateExperiment);
                                        }
                                        AddTerminalContender(
                                            partial, coLocationCache, contender, work, true);
                                        if (work != null) work.CompletedPairs++;
                                        if (batchTimer != null) batchTimer.EndPair(true);
                                    }
                                    _budget.Checkpoint();
                                }
                            }
                            finally
                            {
                                if (batchTimer != null) batchTimer.Finish();
                                if (work != null) work.OutputStates = partial.Count;
                                if (work != null && work.Cost != null)
                                {
                                    double workerElapsed = CustomSearchDiagnostics.MillisecondsSince(workerStart);
                                    work.Cost.WorkerMillisecondsSum = workerElapsed;
                                    work.Cost.WorkerMillisecondsMax = workerElapsed;
                                    work.Cost.TimedWorkerCount = 1;
                                }
                                if (pendingTransitions != 0)
                                    _budget.RecordTransitionsAndStates(pendingTransitions);
                            }
                            partitions[workerIndex] = new TerminalPartitionResult(partial);
                        });
                    }
                    finally
                    {
                        if (_budget.DetailedCostDiagnostics && _phaseDiagnostics.Merge.Cost != null)
                            _phaseDiagnostics.Merge.Cost.LoopWallMilliseconds +=
                                CustomSearchDiagnostics.MillisecondsSince(parallelStart);
                    }
                }
                catch (AggregateException error)
                {
                    Exception cause = error.Flatten().InnerExceptions.FirstOrDefault(
                        item => item is OperationCanceledException)
                        ?? error.Flatten().InnerExceptions.FirstOrDefault(
                            item => item is SearchBudgetExceededException)
                        ?? error.Flatten().InnerExceptions.FirstOrDefault(
                            item => item is CustomEffectDataException);
                    if (cause != null) throw cause;
                    throw;
                }
                finally
                {
                    if (workDiagnostics != null)
                    {
                        foreach (TerminalWorkDiagnostics work in workDiagnostics)
                            if (work != null) AccumulateTerminalWork(work);
                        ObserveRetainedEntries(_mergeRetainedInputCount + _phaseDiagnostics.Merge.PartialTerminalStateCount);
                    }
                }

                Dictionary<TerminalSemanticStateKey, GeneralFrontierState> result =
                    new Dictionary<TerminalSemanticStateKey, GeneralFrontierState>();
                long unionStart = _diagnostics == null ? 0 : Stopwatch.GetTimestamp();
                long partitionStateCount = 0;
                foreach (TerminalPartitionResult partition in partitions)
                {
                    partitionStateCount += partition.States.Count;
                    foreach (KeyValuePair<TerminalSemanticStateKey, GeneralFrontierState> item
                        in partition.States)
                    {
                        GeneralFrontierState existing;
                        bool exists = result.TryGetValue(item.Key, out existing);
                        bool replace = !exists || CompareSecondary(item.Value, existing,
                            CompleteMask, _rules, _budget.StateExperiment) < 0;
                        if (replace)
                            result[item.Key] = item.Value;
                        if (_phaseDiagnostics != null && exists)
                        {
                            _phaseDiagnostics.Merge.EquivalentStateHits++;
                            if (replace) _phaseDiagnostics.Merge.ReplacedStates++;
                            else _phaseDiagnostics.Merge.DiscardedStates++;
                        }
                    }
                }
                _budget.ObservePeakStateCount(partitionStateCount + result.Count);
                if (_diagnostics != null)
                {
                    _diagnostics.TerminalPartitionUnionMilliseconds += CustomSearchDiagnostics.MillisecondsSince(unionStart);
                    _diagnostics.TerminalFoldOutputCount = result.Count;
                    ObserveRetainedEntries(_mergeRetainedInputCount + partitionStateCount + result.Count);
                }
                return result;
            }

            private Dictionary<TerminalSemanticStateKey, GeneralFrontierState>
                CombineTerminalPhaseFrontiersSequential(
                GeneralFrontierState[] aggregateStates,
                GeneralFrontierState[] localStates,
                TerminalPruningContext pruning = null)
            {
                Dictionary<TerminalSemanticStateKey, GeneralFrontierState> result =
                    new Dictionary<TerminalSemanticStateKey, GeneralFrontierState>();
                Dictionary<FinalCoProfileStateKey, int[]> coLocationCache =
                    new Dictionary<FinalCoProfileStateKey, int[]>();
                TerminalWorkDiagnostics work = _diagnostics == null ? null
                    : new TerminalWorkDiagnostics(_budget.DetailedCostDiagnostics);
                if (work != null && pruning != null)
                    work.Pruning = new TerminalPruningWork(_rules.Length, _budget.DetailedCostDiagnostics);
                GeneralFrontierState workspace = GeneralFrontierState.CreateTerminalWorkspace(
                    work == null ? null : work.Creations);
                long loopStart = _budget.DetailedCostDiagnostics ? Stopwatch.GetTimestamp() : 0;
                CostBatchTimer batchTimer = work == null || work.Cost == null
                    ? null : new CostBatchTimer(work.Cost);
                try
                {
                long checkedPairs = 0;
                for (int localIndex = 0; localIndex < localStates.Length; localIndex++)
                {
                    GeneralFrontierState localState = localStates[localIndex];
                    int[] localSlots = SelectedSlotsByInstanceId(localState);
                    for (int aggregateIndex = 0; aggregateIndex < aggregateStates.Length; aggregateIndex++)
                    {
                        int skipped = (aggregateIndex & (TerminalBlockSize - 1)) == 0
                            ? SkipTerminalBlock(pruning, aggregateIndex, localIndex,
                                aggregateStates, localState, work == null ? null : work.Pruning) : 0;
                        if (skipped != 0) { aggregateIndex += skipped - 1; continue; }
                        if (batchTimer != null) batchTimer.BeginPair();
                        GeneralFrontierState aggregateState = aggregateStates[aggregateIndex];
                        if (pruning != null)
                        {
                            if ((checkedPairs++ & 255L) == 0) _budget.Checkpoint();
                            if (CheckTerminalBound(pruning, aggregateIndex, localIndex,
                                aggregateState, localState, work == null ? null : work.Pruning))
                            {
                                if (batchTimer != null) batchTimer.EndPair(false);
                                continue;
                            }
                        }
                        workspace.ResetTerminalWorkspace(aggregateState);
                        GeneralFrontierState contender = workspace;
                        foreach (int slot in localSlots)
                        {
                            _budget.RecordTransitionAndState();
                            contender = contender.Add(
                                localState.Selected[slot], slot, _rules,
                                null, false, work == null ? null : work.Creations, true,
                                work == null ? null : work.Cost, _budget.StateExperiment);
                        }
                        AddTerminalContender(result, coLocationCache, contender, work, true);
                        if (work != null) work.CompletedPairs++;
                        if (batchTimer != null) batchTimer.EndPair(true);
                    }
                    _budget.Checkpoint();
                }
                return result;
                }
                finally
                {
                    if (batchTimer != null) batchTimer.Finish();
                    if (work != null)
                    {
                        if (work.Cost != null)
                        {
                            double elapsed = CustomSearchDiagnostics.MillisecondsSince(loopStart);
                            work.Cost.LoopWallMilliseconds = elapsed;
                            work.Cost.WorkerMillisecondsSum = elapsed;
                            work.Cost.WorkerMillisecondsMax = elapsed;
                            work.Cost.TimedWorkerCount = 1;
                        }
                        work.OutputStates = result.Count;
                        AccumulateTerminalWork(work);
                        _diagnostics.TerminalFoldOutputCount = result.Count;
                        _phaseDiagnostics.Merge.OutputStateCount = result.Count;
                        ObserveRetainedEntries(_mergeRetainedInputCount + result.Count);
                    }
                }
            }

            private static int[] SelectedSlotsByInstanceId(GeneralFrontierState state)
            {
                return Enumerable.Range(0, 6)
                    .Where(slot => state.Selected[slot] != null)
                    .OrderBy(slot => state.Selected[slot].Relic.InstanceId)
                    .ToArray();
            }

            private Dictionary<TerminalSemanticStateKey, GeneralFrontierState>
                CollapseTerminalStates(
                    Dictionary<SemanticStateKey, GeneralFrontierState> source)
            {
                Dictionary<TerminalSemanticStateKey, GeneralFrontierState> result =
                    new Dictionary<TerminalSemanticStateKey, GeneralFrontierState>();
                Dictionary<FinalCoProfileStateKey, int[]> coLocationCache =
                    new Dictionary<FinalCoProfileStateKey, int[]>();
                TerminalWorkDiagnostics work = _diagnostics == null ? null
                    : new TerminalWorkDiagnostics(_budget.DetailedCostDiagnostics);
                try
                {
                foreach (GeneralFrontierState contender in source.Values)
                    AddTerminalContender(result, coLocationCache, contender, work);
                return result;
                }
                finally
                {
                    if (work != null)
                    {
                        work.OutputStates = result.Count;
                        AccumulateTerminalWork(work);
                        _diagnostics.TerminalFoldOutputCount = result.Count;
                        _phaseDiagnostics.Merge.OutputStateCount = result.Count;
                        ObserveRetainedEntries(_mergeRetainedInputCount + result.Count);
                    }
                }
            }

            private void AddTerminalContender(
                Dictionary<TerminalSemanticStateKey, GeneralFrontierState> result,
                Dictionary<FinalCoProfileStateKey, int[]> coLocationCache,
                GeneralFrontierState contender,
                TerminalWorkDiagnostics diagnostics = null,
                bool snapshotWorkspace = false)
            {
                CustomSearchCostDiagnostics cost = diagnostics == null ? null : diagnostics.Cost;
                bool sampleCost = cost != null && (cost.TerminalFoldOperations++ & 255L) == 0;
                if (sampleCost) cost.TerminalFoldSamples++;
                if (diagnostics != null)
                {
                    diagnostics.FoldInputs++;
                    diagnostics.Creations.FinalCoProfileKeys++;
                }
                long partStart = sampleCost ? Stopwatch.GetTimestamp() : 0;
                FinalCoProfileStateKey coLocationKey = new FinalCoProfileStateKey(
                    contender.ExclusiveOptions,
                    contender.CoLocationCounts,
                    contender.UnstableCoLocationTokens,
                    contender.UnknownWinnerCandidateTokens);
                int[] finalCoLocationCounts;
                if (!coLocationCache.TryGetValue(
                    coLocationKey, out finalCoLocationCounts))
                {
                    finalCoLocationCounts = BuildFinalCoLocationCounts(contender);
                    coLocationCache.Add(coLocationKey, finalCoLocationCounts);
                    if (diagnostics != null) diagnostics.Creations.FinalCoProfileCacheMisses++;
                }
                if (sampleCost)
                    cost.CoProfileSampleMilliseconds +=
                        CustomSearchDiagnostics.MillisecondsSince(partStart);
                if (diagnostics != null) diagnostics.Creations.TerminalKeys++;
                partStart = sampleCost ? Stopwatch.GetTimestamp() : 0;
                TerminalSemanticStateKey key = new TerminalSemanticStateKey(
                    contender.Contributions,
                    contender.ExclusiveOptions,
                    finalCoLocationCounts);
                if (sampleCost)
                    cost.TerminalKeySampleMilliseconds +=
                        CustomSearchDiagnostics.MillisecondsSince(partStart);
                GeneralFrontierState existing;
                if (cost != null) cost.DictionaryOperations++;
                partStart = sampleCost ? Stopwatch.GetTimestamp() : 0;
                bool exists = result.TryGetValue(key, out existing);
                if (sampleCost)
                {
                    cost.DictionarySamples++;
                    cost.DictionarySampleMilliseconds +=
                        CustomSearchDiagnostics.MillisecondsSince(partStart);
                }
                bool replace = true;
                if (exists)
                {
                    partStart = sampleCost ? Stopwatch.GetTimestamp() : 0;
                    replace = CompareSecondary(contender, existing, CompleteMask, _rules,
                        _budget == null ? null : _budget.StateExperiment) < 0;
                    if (sampleCost)
                        cost.SecondaryCompareSampleMilliseconds +=
                            CustomSearchDiagnostics.MillisecondsSince(partStart);
                }
                if (replace)
                {
                    GeneralFrontierState retained = contender;
                    if (snapshotWorkspace)
                    {
                        partStart = sampleCost ? Stopwatch.GetTimestamp() : 0;
                        retained = contender.SnapshotTerminalWorkspace(
                            diagnostics == null ? null : diagnostics.Creations);
                        if (sampleCost)
                            cost.SnapshotSampleMilliseconds +=
                                CustomSearchDiagnostics.MillisecondsSince(partStart);
                    }
                    partStart = sampleCost ? Stopwatch.GetTimestamp() : 0;
                    result[key] = retained;
                    if (sampleCost)
                        cost.DictionarySampleMilliseconds +=
                            CustomSearchDiagnostics.MillisecondsSince(partStart);
                }
                if (diagnostics != null && exists)
                {
                    diagnostics.EquivalentHits++;
                    if (replace) diagnostics.Replacements++;
                    else diagnostics.Discards++;
                }
            }

            private sealed class TerminalWorkDiagnostics
            {
                public readonly CustomSearchCreationCounts Creations = new CustomSearchCreationCounts();
                public readonly CustomSearchCostDiagnostics Cost;
                public TerminalPruningWork Pruning;
                public long CompletedPairs, FoldInputs, EquivalentHits, Replacements, Discards, OutputStates;
                public TerminalWorkDiagnostics(bool detailedCost)
                {
                    if (detailedCost) Cost = new CustomSearchCostDiagnostics();
                }
            }

            private sealed class CostBatchTimer
            {
                private readonly CustomSearchCostDiagnostics _cost;
                private long _start;
                private int _checks, _merges;
                public CostBatchTimer(CustomSearchCostDiagnostics cost) { _cost = cost; }
                public void BeginPair()
                {
                    if (_checks == 0) _start = Stopwatch.GetTimestamp();
                }
                public void EndPair(bool merged)
                {
                    _checks++;
                    if (merged) _merges++;
                    if (_checks == 4096) Flush();
                }
                public void Finish()
                {
                    if (_checks != 0) Flush();
                }
                private void Flush()
                {
                    double elapsed = CustomSearchDiagnostics.MillisecondsSince(_start);
                    _cost.BatchCount++;
                    _cost.BatchCheckOperations += _checks;
                    _cost.BatchMergeOperations += _merges;
                    _cost.BatchMilliseconds += elapsed;
                    _cost.BatchCheckSquared += (double)_checks * _checks;
                    _cost.BatchMergeSquared += (double)_merges * _merges;
                    _cost.BatchCheckMerge += (double)_checks * _merges;
                    _cost.BatchCheckMilliseconds += _checks * elapsed;
                    _cost.BatchMergeMilliseconds += _merges * elapsed;
                    _checks = 0;
                    _merges = 0;
                }
            }

            private void AccumulateTerminalWork(TerminalWorkDiagnostics work)
            {
                _diagnostics.Creations.Add(work.Creations);
                if (work.Cost != null && _phaseDiagnostics.Merge.Cost != null)
                    _phaseDiagnostics.Merge.Cost.Add(work.Cost);
                _diagnostics.TerminalFoldInputCount += work.FoldInputs;
                _phaseDiagnostics.Merge.CompletedPairCount += work.CompletedPairs;
                _phaseDiagnostics.Merge.EquivalentStateHits += work.EquivalentHits;
                _phaseDiagnostics.Merge.ReplacedStates += work.Replacements;
                _phaseDiagnostics.Merge.DiscardedStates += work.Discards;
                _phaseDiagnostics.Merge.PartialTerminalStateCount += work.OutputStates;
                if (work.Pruning != null) AccumulatePruningWork(work.Pruning);
            }

            private static int[] BuildFinalCoLocationCounts(GeneralFrontierState state)
            {
                Dictionary<int, List<Occurrence>> additionalByInstance =
                    new Dictionary<int, List<Occurrence>>();
                foreach (UnknownWinnerOption option in state.UnknownWinners
                    .SelectMany(item => item.Options))
                    AddFinalOccurrence(additionalByInstance,
                        option.Candidate.Relic.InstanceId, option.Occurrence);

                Dictionary<int, FinalExclusivePhysical> soleExclusive =
                    new Dictionary<int, FinalExclusivePhysical>();
                HashSet<int> multipleExclusive = new HashSet<int>();
                foreach (Candidate candidate in state.Selected.Where(item => item != null))
                {
                    foreach (ExclusiveEffect effect in candidate.ExclusiveEffects
                        .Where(item => item.IsApplicable))
                    {
                        if (multipleExclusive.Contains(effect.ExclusivityId)) continue;
                        FinalExclusivePhysical existing;
                        FinalExclusivePhysical current =
                            new FinalExclusivePhysical(candidate, effect);
                        if (!soleExclusive.TryGetValue(effect.ExclusivityId, out existing))
                            soleExclusive.Add(effect.ExclusivityId, current);
                        else if (!existing.IsSamePhysical(current))
                        {
                            soleExclusive.Remove(effect.ExclusivityId);
                            multipleExclusive.Add(effect.ExclusivityId);
                        }
                    }
                }
                foreach (FinalExclusivePhysical physical in soleExclusive.Values)
                {
                    foreach (Occurrence occurrence in physical.Candidate.Occurrences
                        .Where(item => item.IsApplicable
                            && item.ExclusivityId == physical.Effect.ExclusivityId
                            && item.PhysicalOrdinal == physical.Effect.PhysicalOrdinal
                            && item.EffectId == physical.Effect.EffectId))
                        AddFinalOccurrence(additionalByInstance,
                            physical.Candidate.Relic.InstanceId, occurrence);
                }

                List<int> counts = new List<int>();
                foreach (Candidate candidate in state.Selected.Where(item => item != null))
                {
                    List<Occurrence> additional;
                    if (!additionalByInstance.TryGetValue(
                        candidate.Relic.InstanceId, out additional))
                        additional = new List<Occurrence>();
                    int count = candidate.ComputeFinalCoLocationCount(additional);
                    if (count >= 2) counts.Add(count);
                }
                return counts.OrderByDescending(value => value).ToArray();
            }

            private static void AddFinalOccurrence(
                Dictionary<int, List<Occurrence>> target,
                int instanceId,
                Occurrence occurrence)
            {
                List<Occurrence> values;
                if (!target.TryGetValue(instanceId, out values))
                {
                    values = new List<Occurrence>();
                    target.Add(instanceId, values);
                }
                values.Add(occurrence);
            }

            private void EvaluateComplete(GeneralFrontierState contender)
            {
                EvaluatedBuilds++;
                CustomComparisonVector vector = _owner.EvaluateCandidatesVector(
                    _vessel, contender.Selected, _characterId, _rules, _budget);
                if (_diagnostics != null) _diagnostics.CompletedEvaluationCount++;
                if (_bestVector == null || CustomComparisonVector.Compare(vector, _bestVector) < 0)
                {
                    _bestVector = vector;
                    _bestCandidates = contender.Selected;
                }
            }

            private void EvaluateTerminalStates(
                Dictionary<TerminalSemanticStateKey, GeneralFrontierState> terminal)
            {
                GeneralFrontierState[] states = terminal.Values.ToArray();
                int workerCount = Math.Min(6,
                    Math.Min(Environment.ProcessorCount, states.Length));
                if (workerCount <= 1 || states.Length < 512)
                {
                    foreach (GeneralFrontierState state in states)
                        EvaluateComplete(state);
                    return;
                }

                TerminalEvaluationResult[] partitions =
                    new TerminalEvaluationResult[workerCount];
                try
                {
                    Parallel.For(0, workerCount,
                        new ParallelOptions { MaxDegreeOfParallelism = workerCount },
                        workerIndex =>
                        {
                            CustomComparisonVector bestVector = null;
                            Candidate[] bestCandidates = null;
                            long evaluated = 0;
                            try
                            {
                            for (int stateIndex = workerIndex;
                                stateIndex < states.Length;
                                stateIndex += workerCount)
                            {
                                GeneralFrontierState state = states[stateIndex];
                                CustomComparisonVector vector =
                                    _owner.EvaluateCandidatesVector(
                                        _vessel, state.Selected, _characterId,
                                        _rules, _budget);
                                evaluated++;
                                if (bestVector == null
                                    || CustomComparisonVector.Compare(
                                        vector, bestVector) < 0)
                                {
                                    bestVector = vector;
                                    bestCandidates = state.Selected;
                                }
                            }
                            partitions[workerIndex] = new TerminalEvaluationResult(
                                bestVector, bestCandidates, evaluated);
                            }
                            finally
                            {
                                if (_diagnostics != null)
                                    lock (_diagnostics) _diagnostics.CompletedEvaluationCount += evaluated;
                            }
                        });
                }
                catch (AggregateException error)
                {
                    Exception cause = error.Flatten().InnerExceptions.FirstOrDefault(
                        item => item is OperationCanceledException)
                        ?? error.Flatten().InnerExceptions.FirstOrDefault(
                            item => item is SearchBudgetExceededException)
                        ?? error.Flatten().InnerExceptions.FirstOrDefault(
                            item => item is CustomEffectDataException);
                    if (cause != null) throw cause;
                    throw;
                }

                foreach (TerminalEvaluationResult partition in partitions)
                {
                    EvaluatedBuilds += partition.EvaluatedBuilds;
                    if (partition.BestVector != null
                        && (_bestVector == null || CustomComparisonVector.Compare(
                            partition.BestVector, _bestVector) < 0))
                    {
                        _bestVector = partition.BestVector;
                        _bestCandidates = partition.BestCandidates;
                    }
                }
            }

            private bool AcceptsSlot(Candidate candidate, int outputSlot)
            {
                bool deep = outputSlot >= 3;
                if (candidate.Relic.IsDeep != deep) return false;
                int localSlot = deep ? outputSlot - 3 : outputSlot;
                int colorId = deep
                    ? _vessel.DeepSlotColorIds[localSlot]
                    : _vessel.OrdinarySlotColorIds[localSlot];
                return AcceptsColor(colorId, candidate.Relic.ColorId);
            }

            private int AcceptedSlotMask(Candidate candidate)
            {
                int result = 0;
                for (int outputSlot = 0; outputSlot < 6; outputSlot++)
                    if (AcceptsSlot(candidate, outputSlot)) result |= 1 << outputSlot;
                return result;
            }

            private static int SlotIndex(int singleBit)
            {
                switch (singleBit)
                {
                    case 1: return 0;
                    case 2: return 1;
                    case 4: return 2;
                    case 8: return 3;
                    case 16: return 4;
                    default: return 5;
                }
            }

            private static int[] BuildCanonicalAvailableSlotMasks(VesselDefinition vessel)
            {
                int[] result = new int[CompleteMask];
                for (int mask = 0; mask < CompleteMask; mask++)
                {
                    int available = 0;
                    for (int outputSlot = 0; outputSlot < 6; outputSlot++)
                        if ((mask & (1 << outputSlot)) == 0
                            && IsCanonicalEquivalentSlot(vessel, mask, outputSlot))
                            available |= 1 << outputSlot;
                    result[mask] = available;
                }
                return result;
            }

            private SearchPhase[] BuildSearchPhases()
            {
                if (_budget.DepthSeparatedPhasesEnabled)
                {
                    // Ordinary and deep relics cannot share a physical instance.
                    // Keep the complete semantic frontier for each three-slot side;
                    // no half-score ranking or top-N truncation is permitted.
                    _budget.Checkpoint();
                    return new[] {
                        new SearchPhase(7, _candidates.Where(c => !c.Relic.IsDeep && AcceptedSlotMask(c) != 0).ToArray()),
                        new SearchPhase(56, _candidates.Where(c => c.Relic.IsDeep && AcceptedSlotMask(c) != 0).ToArray())
                    };
                }
                int[] parents = Enumerable.Range(0, 6).ToArray();
                foreach (Candidate candidate in _candidates)
                {
                    int accepted = AcceptedSlotMask(candidate);
                    if (accepted == 0) continue;
                    int firstBit = accepted & -accepted;
                    int firstSlot = SlotIndex(firstBit);
                    for (int remaining = accepted & ~firstBit;
                        remaining != 0; remaining &= remaining - 1)
                        UnionSlots(parents, firstSlot,
                            SlotIndex(remaining & -remaining));
                }

                Dictionary<int, int> componentMasks = new Dictionary<int, int>();
                for (int slot = 0; slot < 6; slot++)
                {
                    int root = FindSlotRoot(parents, slot);
                    int mask;
                    componentMasks.TryGetValue(root, out mask);
                    componentMasks[root] = mask | (1 << slot);
                }
                Dictionary<int, List<Candidate>> componentCandidates = componentMasks.Keys
                    .ToDictionary(root => root, root => new List<Candidate>());
                foreach (Candidate candidate in _candidates)
                {
                    int accepted = AcceptedSlotMask(candidate);
                    if (accepted == 0) continue;
                    int root = FindSlotRoot(parents, SlotIndex(accepted & -accepted));
                    componentCandidates[root].Add(candidate);
                }
                return componentMasks.Select(item => new SearchPhase(
                        item.Value, componentCandidates[item.Key].ToArray()))
                    .OrderBy(item => SlotIndex(item.SlotMask & -item.SlotMask))
                    .ToArray();
            }

            private void ConfigureCrossPhaseWinnerIdentity(SearchPhase[] phases)
            {
                foreach (Candidate candidate in _candidates)
                    foreach (UnknownWinnerOption option in candidate.UnknownWinnerOptions)
                        option.RequiresStableIdentity = false;

                Dictionary<string, int> firstPhaseByTieClass =
                    new Dictionary<string, int>(StringComparer.Ordinal);
                HashSet<string> crossPhaseTieClasses =
                    new HashSet<string>(StringComparer.Ordinal);
                for (int phaseIndex = 0; phaseIndex < phases.Length; phaseIndex++)
                {
                    foreach (Candidate candidate in phases[phaseIndex].Candidates)
                    {
                        foreach (UnknownWinnerOption option in candidate.UnknownWinnerOptions)
                        {
                            string tieClass = option.CreateTieClassKey();
                            int firstPhase;
                            if (!firstPhaseByTieClass.TryGetValue(tieClass, out firstPhase))
                                firstPhaseByTieClass.Add(tieClass, phaseIndex);
                            else if (firstPhase != phaseIndex)
                                crossPhaseTieClasses.Add(tieClass);
                        }
                    }
                }
                foreach (Candidate candidate in _candidates)
                    foreach (UnknownWinnerOption option in candidate.UnknownWinnerOptions)
                        option.RequiresStableIdentity = crossPhaseTieClasses.Contains(
                            option.CreateTieClassKey());
            }

            private static int[][] BuildPhaseMasksByFilledSlotCount(
                int completedSlotMask,
                int phaseSlotMask)
            {
                int slotCount = PopCount(phaseSlotMask);
                int[][] result = new int[slotCount][];
                for (int count = 0; count < result.Length; count++)
                    result[count] = Enumerable.Range(0, CompleteMask + 1)
                        .Where(mask => (mask & ~phaseSlotMask) == 0
                            && PopCount(mask) == count)
                        .Select(mask => completedSlotMask | mask)
                        .ToArray();
                return result;
            }

            private static int FindSlotRoot(int[] parents, int slot)
            {
                while (parents[slot] != slot)
                {
                    parents[slot] = parents[parents[slot]];
                    slot = parents[slot];
                }
                return slot;
            }

            private static void UnionSlots(int[] parents, int left, int right)
            {
                int leftRoot = FindSlotRoot(parents, left);
                int rightRoot = FindSlotRoot(parents, right);
                if (leftRoot == rightRoot) return;
                if (leftRoot < rightRoot) parents[rightRoot] = leftRoot;
                else parents[leftRoot] = rightRoot;
            }

            private static int CompareSecondary(
                GeneralFrontierState left,
                GeneralFrontierState right,
                int mask,
                PreparedRule[] rules,
                StateExperimentContext experiment = null)
            {
                int value = GeneralFrontierState.CompareCompressedPreferences(
                    left, right, rules, experiment);
                if (value != 0) return value;
                value = left.NegativeEffectCount.CompareTo(right.NegativeEffectCount);
                if (value != 0) return value;
                value = left.NegativeRelicCount.CompareTo(right.NegativeRelicCount);
                if (value != 0) return value;
                value = left.UncertaintyCount.CompareTo(right.UncertaintyCount);
                if (value != 0) return value;
                for (int outputSlot = 0; outputSlot < 6; outputSlot++)
                {
                    if ((mask & (1 << outputSlot)) == 0) continue;
                    value = left.Selected[outputSlot].Relic.InstanceId
                        .CompareTo(right.Selected[outputSlot].Relic.InstanceId);
                    if (value != 0) return value;
                }
                return 0;
            }

            private static int CompareSecondary(
                GeneralFrontierProbe left,
                GeneralFrontierState right,
                int mask,
                PreparedRule[] rules,
                StateExperimentContext experiment = null)
            {
                int value = GeneralFrontierState.CompareCompressedPreferences(
                    left.Contributions, right.Contributions, rules, experiment);
                if (value != 0) return value;
                value = left.NegativeEffectCount.CompareTo(right.NegativeEffectCount);
                if (value != 0) return value;
                value = left.NegativeRelicCount.CompareTo(right.NegativeRelicCount);
                if (value != 0) return value;
                value = left.UncertaintyCount.CompareTo(right.UncertaintyCount);
                if (value != 0) return value;
                for (int outputSlot = 0; outputSlot < 6; outputSlot++)
                {
                    if ((mask & (1 << outputSlot)) == 0) continue;
                    value = left.SelectedAt(outputSlot).Relic.InstanceId
                        .CompareTo(right.Selected[outputSlot].Relic.InstanceId);
                    if (value != 0) return value;
                }
                return 0;
            }

            private static int PopCount(int value)
            {
                int result = 0;
                while (value != 0)
                {
                    result += value & 1;
                    value >>= 1;
                }
                return result;
            }

            private static long CountFrontierStates(
                Dictionary<SemanticStateKey, GeneralFrontierState>[] frontiers)
            {
                long result = 0;
                foreach (Dictionary<SemanticStateKey, GeneralFrontierState> frontier in frontiers)
                    result += frontier.Count;
                return result;
            }

            private sealed class SearchPhase
            {
                public SearchPhase(int slotMask, Candidate[] candidates)
                {
                    SlotMask = slotMask;
                    Candidates = candidates;
                }

                public int SlotMask;
                public Candidate[] Candidates;
            }

            private sealed class TerminalPartitionResult
            {
                public TerminalPartitionResult(
                    Dictionary<TerminalSemanticStateKey, GeneralFrontierState> states)
                {
                    States = states;
                }

                public Dictionary<TerminalSemanticStateKey, GeneralFrontierState> States;
            }

            private sealed class TerminalEvaluationResult
            {
                public TerminalEvaluationResult(
                    CustomComparisonVector bestVector,
                    Candidate[] bestCandidates,
                    long evaluatedBuilds)
                {
                    BestVector = bestVector;
                    BestCandidates = bestCandidates;
                    EvaluatedBuilds = evaluatedBuilds;
                }

                public CustomComparisonVector BestVector;
                public Candidate[] BestCandidates;
                public long EvaluatedBuilds;
            }

            private sealed class FinalExclusivePhysical
            {
                public FinalExclusivePhysical(Candidate candidate, ExclusiveEffect effect)
                {
                    Candidate = candidate;
                    Effect = effect;
                }

                public Candidate Candidate;
                public ExclusiveEffect Effect;

                public bool IsSamePhysical(FinalExclusivePhysical other)
                {
                    return other != null
                        && Candidate.Relic.InstanceId == other.Candidate.Relic.InstanceId
                        && Effect.PhysicalOrdinal == other.Effect.PhysicalOrdinal
                        && Effect.EffectId == other.Effect.EffectId;
                }
            }
        }

        private sealed class FinalCoProfileStateKey :
            IEquatable<FinalCoProfileStateKey>
        {
            public FinalCoProfileStateKey(
                ExclusiveOption[] exclusiveOptions,
                int[] coLocationCounts,
                string[] unstableCoLocationTokens,
                string[] unknownWinnerCandidateTokens)
            {
                ExclusiveOptions = exclusiveOptions;
                CoLocationCounts = coLocationCounts;
                UnstableCoLocationTokens = unstableCoLocationTokens;
                UnknownWinnerCandidateTokens = unknownWinnerCandidateTokens;
                unchecked
                {
                    int hash = 17;
                    foreach (ExclusiveOption item in ExclusiveOptions)
                        hash = hash * 31 + StringComparer.Ordinal.GetHashCode(item.Key);
                    hash = hash * 31 + 1;
                    foreach (int item in CoLocationCounts)
                        hash = hash * 31 + item;
                    hash = hash * 31 + 1;
                    foreach (string item in UnstableCoLocationTokens)
                        hash = hash * 31 + StringComparer.Ordinal.GetHashCode(item);
                    hash = hash * 31 + 1;
                    foreach (string item in UnknownWinnerCandidateTokens)
                        hash = hash * 31 + StringComparer.Ordinal.GetHashCode(item);
                    HashCode = hash;
                }
            }

            public ExclusiveOption[] ExclusiveOptions;
            public int[] CoLocationCounts;
            public string[] UnstableCoLocationTokens;
            public string[] UnknownWinnerCandidateTokens;
            public int HashCode;

            public bool Equals(FinalCoProfileStateKey other)
            {
                if (ReferenceEquals(this, other)) return true;
                if (other == null || HashCode != other.HashCode
                    || ExclusiveOptions.Length != other.ExclusiveOptions.Length
                    || CoLocationCounts.Length != other.CoLocationCounts.Length
                    || UnstableCoLocationTokens.Length
                        != other.UnstableCoLocationTokens.Length
                    || UnknownWinnerCandidateTokens.Length
                        != other.UnknownWinnerCandidateTokens.Length)
                    return false;
                for (int index = 0; index < ExclusiveOptions.Length; index++)
                    if (!string.Equals(ExclusiveOptions[index].Key,
                        other.ExclusiveOptions[index].Key, StringComparison.Ordinal))
                        return false;
                for (int index = 0; index < CoLocationCounts.Length; index++)
                    if (CoLocationCounts[index] != other.CoLocationCounts[index])
                        return false;
                for (int index = 0; index < UnstableCoLocationTokens.Length; index++)
                    if (!string.Equals(UnstableCoLocationTokens[index],
                        other.UnstableCoLocationTokens[index], StringComparison.Ordinal))
                        return false;
                for (int index = 0; index < UnknownWinnerCandidateTokens.Length; index++)
                    if (!string.Equals(UnknownWinnerCandidateTokens[index],
                        other.UnknownWinnerCandidateTokens[index], StringComparison.Ordinal))
                        return false;
                return true;
            }

            public override bool Equals(object other)
            {
                return Equals(other as FinalCoProfileStateKey);
            }

            public override int GetHashCode() { return HashCode; }
        }

        private sealed class TerminalSemanticStateKey :
            IEquatable<TerminalSemanticStateKey>
        {
            public TerminalSemanticStateKey(
                ContributionState[] contributions,
                ExclusiveOption[] exclusiveOptions,
                int[] finalCoLocationCounts)
            {
                Contributions = contributions;
                ExclusiveOptions = exclusiveOptions;
                FinalCoLocationCounts = finalCoLocationCounts;
                unchecked
                {
                    int hash = 17;
                    foreach (ContributionState item in Contributions)
                        hash = hash * 31
                            + StringComparer.Ordinal.GetHashCode(item.SemanticToken);
                    hash = hash * 31 + 1;
                    foreach (ExclusiveOption item in ExclusiveOptions)
                        hash = hash * 31 + StringComparer.Ordinal.GetHashCode(item.Key);
                    hash = hash * 31 + 1;
                    foreach (int item in FinalCoLocationCounts)
                        hash = hash * 31 + item;
                    HashCode = hash;
                }
            }

            public ContributionState[] Contributions;
            public ExclusiveOption[] ExclusiveOptions;
            public int[] FinalCoLocationCounts;
            public int HashCode;

            public bool Equals(TerminalSemanticStateKey other)
            {
                if (ReferenceEquals(this, other)) return true;
                if (other == null || HashCode != other.HashCode
                    || Contributions.Length != other.Contributions.Length
                    || ExclusiveOptions.Length != other.ExclusiveOptions.Length
                    || FinalCoLocationCounts.Length != other.FinalCoLocationCounts.Length)
                    return false;
                for (int index = 0; index < Contributions.Length; index++)
                    if (!string.Equals(Contributions[index].SemanticToken,
                        other.Contributions[index].SemanticToken,
                        StringComparison.Ordinal))
                        return false;
                for (int index = 0; index < ExclusiveOptions.Length; index++)
                    if (!string.Equals(ExclusiveOptions[index].Key,
                        other.ExclusiveOptions[index].Key, StringComparison.Ordinal))
                        return false;
                for (int index = 0; index < FinalCoLocationCounts.Length; index++)
                    if (FinalCoLocationCounts[index] != other.FinalCoLocationCounts[index])
                        return false;
                return true;
            }

            public override bool Equals(object other)
            {
                return Equals(other as TerminalSemanticStateKey);
            }

            public override int GetHashCode() { return HashCode; }
        }

        private sealed class SemanticStateKey : IEquatable<SemanticStateKey>
        {
            public SemanticStateKey(
                ContributionState[] contributions,
                ExclusiveOption[] exclusiveOptions,
                int[] coLocationCounts,
                string[] unstableCoLocationTokens,
                string[] unknownWinnerCandidateTokens)
            {
                Contributions = contributions;
                ExclusiveOptions = exclusiveOptions;
                CoLocationCounts = coLocationCounts ?? new int[0];
                UnstableCoLocationTokens = unstableCoLocationTokens ?? new string[0];
                UnknownWinnerCandidateTokens = unknownWinnerCandidateTokens ?? new string[0];
                unchecked
                {
                    int hash = 17;
                    foreach (ContributionState item in contributions)
                        hash = hash * 31 + StringComparer.Ordinal.GetHashCode(item.SemanticToken);
                    hash = hash * 31 + 1;
                    foreach (ExclusiveOption item in exclusiveOptions)
                        hash = hash * 31 + StringComparer.Ordinal.GetHashCode(item.Key);
                    hash = hash * 31 + 1;
                    foreach (int item in CoLocationCounts)
                        hash = hash * 31 + item;
                    hash = hash * 31 + 1;
                    foreach (string item in UnstableCoLocationTokens)
                        hash = hash * 31 + StringComparer.Ordinal.GetHashCode(item);
                    hash = hash * 31 + 1;
                    foreach (string item in UnknownWinnerCandidateTokens)
                        hash = hash * 31 + StringComparer.Ordinal.GetHashCode(item);
                    HashCode = hash;
                }
            }

            public ContributionState[] Contributions;
            public ExclusiveOption[] ExclusiveOptions;
            public int[] CoLocationCounts;
            public string[] UnstableCoLocationTokens;
            public string[] UnknownWinnerCandidateTokens;
            public int HashCode;

            public bool Equals(SemanticStateKey other)
            {
                if (ReferenceEquals(this, other)) return true;
                if (other == null || HashCode != other.HashCode
                    || Contributions.Length != other.Contributions.Length
                    || ExclusiveOptions.Length != other.ExclusiveOptions.Length
                    || CoLocationCounts.Length != other.CoLocationCounts.Length
                    || UnstableCoLocationTokens.Length != other.UnstableCoLocationTokens.Length
                    || UnknownWinnerCandidateTokens.Length
                        != other.UnknownWinnerCandidateTokens.Length)
                    return false;
                for (int index = 0; index < Contributions.Length; index++)
                    if (!string.Equals(Contributions[index].SemanticToken,
                        other.Contributions[index].SemanticToken, StringComparison.Ordinal))
                        return false;
                for (int index = 0; index < ExclusiveOptions.Length; index++)
                    if (!string.Equals(ExclusiveOptions[index].Key,
                        other.ExclusiveOptions[index].Key, StringComparison.Ordinal))
                        return false;
                for (int index = 0; index < CoLocationCounts.Length; index++)
                    if (CoLocationCounts[index] != other.CoLocationCounts[index])
                        return false;
                for (int index = 0; index < UnstableCoLocationTokens.Length; index++)
                    if (!string.Equals(UnstableCoLocationTokens[index],
                        other.UnstableCoLocationTokens[index], StringComparison.Ordinal))
                        return false;
                for (int index = 0; index < UnknownWinnerCandidateTokens.Length; index++)
                    if (!string.Equals(UnknownWinnerCandidateTokens[index],
                        other.UnknownWinnerCandidateTokens[index], StringComparison.Ordinal))
                        return false;
                return true;
            }

            public override bool Equals(object other) { return Equals(other as SemanticStateKey); }
            public override int GetHashCode() { return HashCode; }
        }

        // A value-only contender used by the local and semantic joins. It owns
        // no mutable arrays and is never inserted into a frontier dictionary.
        // Materialization takes an immutable six-slot snapshot only after the
        // dictionary decision proves that the contender will be retained.
        private struct GeneralFrontierProbe
        {
            public GeneralFrontierProbe(GeneralFrontierState source)
            {
                Source = source;
                OverrideMask = 0;
                Selected0 = null; Selected1 = null; Selected2 = null;
                Selected3 = null; Selected4 = null; Selected5 = null;
                Contributions = source.Contributions;
                ExclusiveOptions = source.ExclusiveOptions;
                UnknownWinners = source.UnknownWinners;
                CoLocationCounts = source.CoLocationCounts;
                UnstableCoLocationTokens = source.UnstableCoLocationTokens;
                UnknownWinnerCandidateTokens = source.UnknownWinnerCandidateTokens;
                NegativeEffectCount = source.NegativeEffectCount;
                NegativeRelicCount = source.NegativeRelicCount;
                UncertaintyCount = source.UncertaintyCount;
                SemanticKey = source.SemanticKey;
            }

            public GeneralFrontierProbe WithSelected(Candidate candidate, int slot)
            {
                GeneralFrontierProbe result = this;
                result.OverrideMask |= 1 << slot;
                switch (slot)
                {
                    case 0: result.Selected0 = candidate; break;
                    case 1: result.Selected1 = candidate; break;
                    case 2: result.Selected2 = candidate; break;
                    case 3: result.Selected3 = candidate; break;
                    case 4: result.Selected4 = candidate; break;
                    case 5: result.Selected5 = candidate; break;
                    default: throw new ArgumentOutOfRangeException("slot");
                }
                return result;
            }

            public Candidate SelectedAt(int slot)
            {
                if ((OverrideMask & (1 << slot)) == 0) return Source.Selected[slot];
                switch (slot)
                {
                    case 0: return Selected0;
                    case 1: return Selected1;
                    case 2: return Selected2;
                    case 3: return Selected3;
                    case 4: return Selected4;
                    case 5: return Selected5;
                    default: throw new ArgumentOutOfRangeException("slot");
                }
            }

            public GeneralFrontierState Source;
            public int OverrideMask;
            public Candidate Selected0, Selected1, Selected2;
            public Candidate Selected3, Selected4, Selected5;
            public ContributionState[] Contributions;
            public ExclusiveOption[] ExclusiveOptions;
            public UnknownWinnerState[] UnknownWinners;
            public int[] CoLocationCounts;
            public string[] UnstableCoLocationTokens;
            public string[] UnknownWinnerCandidateTokens;
            public int NegativeEffectCount;
            public int NegativeRelicCount;
            public int UncertaintyCount;
            public SemanticStateKey SemanticKey;
        }

        private sealed class GeneralFrontierState
        {
            private GeneralFrontierState(
                Candidate[] selected,
                ContributionState[] contributions,
                ExclusiveOption[] exclusiveOptions,
                UnknownWinnerState[] unknownWinners,
                int[] coLocationCounts,
                string[] unstableCoLocationTokens,
                string[] unknownWinnerCandidateTokens,
                int negativeEffectCount,
                int negativeRelicCount,
                int uncertaintyCount,
                SemanticStateKey semanticKey = null,
                bool createSemanticKey = true,
                CustomSearchCostDiagnostics cost = null,
                bool sampleCost = false)
            {
                Selected = selected;
                Contributions = contributions;
                ExclusiveOptions = exclusiveOptions;
                UnknownWinners = unknownWinners;
                CoLocationCounts = coLocationCounts;
                UnstableCoLocationTokens = unstableCoLocationTokens;
                UnknownWinnerCandidateTokens = unknownWinnerCandidateTokens;
                NegativeEffectCount = negativeEffectCount;
                NegativeRelicCount = negativeRelicCount;
                UncertaintyCount = uncertaintyCount;
                SemanticKey = semanticKey;
                if (SemanticKey == null && createSemanticKey)
                {
                    long keyStart = sampleCost ? Stopwatch.GetTimestamp() : 0;
                    SemanticKey = new SemanticStateKey(
                        contributions, exclusiveOptions, coLocationCounts,
                        unstableCoLocationTokens, unknownWinnerCandidateTokens);
                    if (sampleCost)
                        cost.SemanticKeySampleMilliseconds +=
                            CustomSearchDiagnostics.MillisecondsSince(keyStart);
                }
            }

            public Candidate[] Selected;
            public ContributionState[] Contributions;
            public ExclusiveOption[] ExclusiveOptions;
            public UnknownWinnerState[] UnknownWinners;
            public int[] CoLocationCounts;
            public string[] UnstableCoLocationTokens;
            public string[] UnknownWinnerCandidateTokens;
            public int NegativeEffectCount;
            public int NegativeRelicCount;
            public int UncertaintyCount;
            public SemanticStateKey SemanticKey;

            public static int CompareCompressedPreferences(
                GeneralFrontierState left,
                GeneralFrontierState right,
                PreparedRule[] rules,
                StateExperimentContext experiment = null)
            {
                return CompareCompressedPreferences(
                    left.Contributions, right.Contributions, rules, experiment);
            }

            public static int CompareCompressedPreferences(
                ContributionState[] leftValues,
                ContributionState[] rightValues,
                PreparedRule[] rules,
                StateExperimentContext experiment = null)
            {
                if (experiment != null && experiment.CompiledSecondaryComparison)
                    return CompareCompressedPreferencesCompiled(
                        leftValues, rightValues, experiment.ComparisonOrder);
                IEnumerable<int> comparisonOrder = Enumerable.Range(0, rules.Length)
                    .Where(index => rules[index].Source.IsRequired)
                    .Concat(Enumerable.Range(0, rules.Length)
                        .Where(index => !rules[index].Source.IsRequired));
                foreach (int ruleIndex in comparisonOrder)
                {
                    ContributionState leftValue = leftValues.FirstOrDefault(item =>
                        item.RuleIndex == ruleIndex
                        && string.Equals(item.StackGroupKey, "confirmed-safe", StringComparison.Ordinal));
                    ContributionState rightValue = rightValues.FirstOrDefault(item =>
                        item.RuleIndex == ruleIndex
                        && string.Equals(item.StackGroupKey, "confirmed-safe", StringComparison.Ordinal));
                    if (leftValue == null || rightValue == null) continue;
                    int[] leftRanks = leftValue.Values;
                    int[] rightRanks = rightValue.Values;
                    int rankLength = Math.Min(leftRanks.Length, rightRanks.Length);
                    for (int rankIndex = 0; rankIndex < rankLength; rankIndex++)
                    {
                        int value = rightRanks[rankIndex].CompareTo(leftRanks[rankIndex]);
                        if (value != 0) return value;
                    }
                    int countValue = rightRanks.Length.CompareTo(leftRanks.Length);
                    if (countValue != 0) return countValue;
                }
                return 0;
            }

            private static int CompareCompressedPreferencesCompiled(
                ContributionState[] leftValues,
                ContributionState[] rightValues,
                int[] comparisonOrder)
            {
                foreach (int ruleIndex in comparisonOrder)
                {
                    int leftIndex = FindConfirmedSafeContribution(leftValues, ruleIndex);
                    if (leftIndex < 0) continue;
                    ContributionState rightValue = leftIndex < rightValues.Length
                        && rightValues[leftIndex].RuleIndex == ruleIndex
                        && string.Equals(rightValues[leftIndex].StackGroupKey,
                            "confirmed-safe", StringComparison.Ordinal)
                            ? rightValues[leftIndex]
                            : FindConfirmedSafeContributionValue(rightValues, ruleIndex);
                    if (rightValue == null) continue;
                    int[] leftRanks = leftValues[leftIndex].Values;
                    int[] rightRanks = rightValue.Values;
                    int rankLength = Math.Min(leftRanks.Length, rightRanks.Length);
                    for (int rankIndex = 0; rankIndex < rankLength; rankIndex++)
                    {
                        int value = rightRanks[rankIndex].CompareTo(leftRanks[rankIndex]);
                        if (value != 0) return value;
                    }
                    int countValue = rightRanks.Length.CompareTo(leftRanks.Length);
                    if (countValue != 0) return countValue;
                }
                return 0;
            }

            private static int FindConfirmedSafeContribution(
                ContributionState[] values, int ruleIndex)
            {
                for (int index = 0; index < values.Length; index++)
                    if (values[index].RuleIndex == ruleIndex
                        && string.Equals(values[index].StackGroupKey,
                            "confirmed-safe", StringComparison.Ordinal))
                        return index;
                return -1;
            }

            private static ContributionState FindConfirmedSafeContributionValue(
                ContributionState[] values, int ruleIndex)
            {
                int index = FindConfirmedSafeContribution(values, ruleIndex);
                return index < 0 ? null : values[index];
            }

            public static GeneralFrontierState CreateInitial(int ruleCount, CustomSearchCreationCounts diagnostics = null)
            {
                if (diagnostics != null)
                {
                    diagnostics.GeneralStates++;
                    diagnostics.SixSlotArrays++;
                    diagnostics.SemanticKeys++;
                }
                return new GeneralFrontierState(
                    new Candidate[6],
                    new ContributionState[0],
                    new ExclusiveOption[0],
                    new UnknownWinnerState[0],
                    new int[0],
                    new string[0],
                    new string[0],
                    0,
                    0,
                    0);
            }

            // Owned by one terminal-merge worker for one invocation, never stored
            // in a frontier. Only Selected and this state's fields are mutable.
            // All semantic arrays remain immutable under the existing merge helpers.
            public static GeneralFrontierState CreateTerminalWorkspace(CustomSearchCreationCounts diagnostics)
            {
                if (diagnostics != null)
                {
                    diagnostics.GeneralStates++;
                    diagnostics.SixSlotArrays++;
                    diagnostics.TerminalWorkspaces++;
                }
                return new GeneralFrontierState(new Candidate[6], null, null, null,
                    null, null, null, 0, 0, 0, null, false);
            }

            public void ResetTerminalWorkspace(GeneralFrontierState source)
            {
                Array.Copy(source.Selected, Selected, 6);
                Contributions = source.Contributions;
                ExclusiveOptions = source.ExclusiveOptions;
                UnknownWinners = source.UnknownWinners;
                CoLocationCounts = source.CoLocationCounts;
                UnstableCoLocationTokens = source.UnstableCoLocationTokens;
                UnknownWinnerCandidateTokens = source.UnknownWinnerCandidateTokens;
                NegativeEffectCount = source.NegativeEffectCount;
                NegativeRelicCount = source.NegativeRelicCount;
                UncertaintyCount = source.UncertaintyCount;
                SemanticKey = source.SemanticKey;
            }

            public GeneralFrontierState SnapshotTerminalWorkspace(CustomSearchCreationCounts diagnostics)
            {
                if (diagnostics != null)
                {
                    diagnostics.GeneralStates++;
                    diagnostics.SixSlotArrays++;
                    diagnostics.TerminalRetainedSnapshots++;
                }
                // Keys/cache entries may retain these semantic arrays: subsequent
                // Add calls replace array references, never modify their contents.
                return new GeneralFrontierState((Candidate[])Selected.Clone(), Contributions,
                    ExclusiveOptions, UnknownWinners, CoLocationCounts, UnstableCoLocationTokens,
                    UnknownWinnerCandidateTokens, NegativeEffectCount, NegativeRelicCount,
                    UncertaintyCount, SemanticKey, false);
            }

            public GeneralFrontierState Add(
                Candidate candidate,
                int outputSlot,
                PreparedRule[] rules,
                GeneralFrontierState semanticTemplate = null,
                bool createSemanticKey = true,
                CustomSearchCreationCounts diagnostics = null,
                bool reuseTerminalWorkspace = false,
                CustomSearchCostDiagnostics cost = null,
                StateExperimentContext experiment = null)
            {
                bool sampleCost = cost != null && (cost.StateAddOperations++ & 255L) == 0;
                if (sampleCost) cost.StateAddSamples++;
                long partStart = sampleCost ? Stopwatch.GetTimestamp() : 0;
                Candidate[] selected = reuseTerminalWorkspace ? Selected : (Candidate[])Selected.Clone();
                if (sampleCost)
                    cost.SelectedCopySampleMilliseconds +=
                        CustomSearchDiagnostics.MillisecondsSince(partStart);
                if (diagnostics != null && !reuseTerminalWorkspace)
                {
                    diagnostics.GeneralStates++;
                    diagnostics.SixSlotArrays++;
                }
                selected[outputSlot] = candidate;
                if (semanticTemplate != null)
                {
                    partStart = sampleCost ? Stopwatch.GetTimestamp() : 0;
                    GeneralFrontierState templated = new GeneralFrontierState(
                        selected,
                        semanticTemplate.Contributions,
                        semanticTemplate.ExclusiveOptions,
                        semanticTemplate.UnknownWinners,
                        semanticTemplate.CoLocationCounts,
                        semanticTemplate.UnstableCoLocationTokens,
                        semanticTemplate.UnknownWinnerCandidateTokens,
                        semanticTemplate.NegativeEffectCount,
                        semanticTemplate.NegativeRelicCount,
                        semanticTemplate.UncertaintyCount,
                        semanticTemplate.SemanticKey,
                        createSemanticKey,
                        cost,
                        sampleCost);
                    if (sampleCost)
                        cost.StateFinalizeSampleMilliseconds +=
                            CustomSearchDiagnostics.MillisecondsSince(partStart);
                    return templated;
                }
                partStart = sampleCost ? Stopwatch.GetTimestamp() : 0;
                ContributionState[] contributions = MergeContributions(
                    Contributions, candidate.SemanticContributions, diagnostics,
                    experiment != null && experiment.ExactCapacityMerges);
                ExclusiveOption[] exclusiveOptions = MergeExclusiveOptions(
                    ExclusiveOptions, candidate.ExclusiveOptions, diagnostics,
                    experiment != null && experiment.ExactCapacityMerges);
                UnknownWinnerState[] unknownWinners = MergeUnknownWinners(
                    UnknownWinners, candidate.UnknownWinnerStates, diagnostics,
                    experiment != null && experiment.ExactCapacityMerges);
                int[] coLocationCounts;
                string[] unstableCoLocationTokens;
                string[] unknownWinnerCandidateTokens;
                if (ReferenceEquals(unknownWinners, UnknownWinners))
                {
                    coLocationCounts = InsertCoLocationCount(
                        CoLocationCounts, candidate.LocalCoLocationCount);
                    unstableCoLocationTokens = InsertUnstableCoLocationToken(
                        UnstableCoLocationTokens, candidate.UnstableCoLocationToken);
                    unknownWinnerCandidateTokens = UnknownWinnerCandidateTokens;
                }
                else
                {
                    BuildCoLocationSemanticState(
                        selected, unknownWinners, out coLocationCounts,
                        out unstableCoLocationTokens, out unknownWinnerCandidateTokens);
                }
                SemanticStateKey semanticKey = ReferenceEquals(contributions, Contributions)
                    && ReferenceEquals(exclusiveOptions, ExclusiveOptions)
                    && ReferenceEquals(unknownWinners, UnknownWinners)
                    && ReferenceEquals(coLocationCounts, CoLocationCounts)
                    && ReferenceEquals(unstableCoLocationTokens, UnstableCoLocationTokens)
                    && ReferenceEquals(unknownWinnerCandidateTokens, UnknownWinnerCandidateTokens)
                        ? SemanticKey
                        : null;
                if (sampleCost)
                    cost.ContributionUpdateSampleMilliseconds +=
                        CustomSearchDiagnostics.MillisecondsSince(partStart);
                if (diagnostics != null)
                {
                    if (semanticKey == null && createSemanticKey) diagnostics.SemanticKeys++;
                }
                if (reuseTerminalWorkspace)
                {
                    partStart = sampleCost ? Stopwatch.GetTimestamp() : 0;
                    Contributions = contributions;
                    ExclusiveOptions = exclusiveOptions;
                    UnknownWinners = unknownWinners;
                    CoLocationCounts = coLocationCounts;
                    UnstableCoLocationTokens = unstableCoLocationTokens;
                    UnknownWinnerCandidateTokens = unknownWinnerCandidateTokens;
                    NegativeEffectCount += candidate.NegativeEffectCount;
                    NegativeRelicCount += candidate.NegativeRelicCount;
                    UncertaintyCount += candidate.UncertaintyCount;
                    SemanticKey = semanticKey;
                    if (sampleCost)
                        cost.StateFinalizeSampleMilliseconds +=
                            CustomSearchDiagnostics.MillisecondsSince(partStart);
                    return this;
                }
                partStart = sampleCost ? Stopwatch.GetTimestamp() : 0;
                double semanticKeyBefore = sampleCost ? cost.SemanticKeySampleMilliseconds : 0;
                GeneralFrontierState result = new GeneralFrontierState(
                    selected,
                    contributions,
                    exclusiveOptions,
                    unknownWinners,
                    coLocationCounts,
                    unstableCoLocationTokens,
                    unknownWinnerCandidateTokens,
                    NegativeEffectCount + candidate.NegativeEffectCount,
                    NegativeRelicCount + candidate.NegativeRelicCount,
                    UncertaintyCount + candidate.UncertaintyCount,
                    semanticKey,
                    createSemanticKey,
                    cost,
                    sampleCost);
                if (sampleCost)
                {
                    double total = CustomSearchDiagnostics.MillisecondsSince(partStart);
                    cost.StateFinalizeSampleMilliseconds += Math.Max(0,
                        total - (cost.SemanticKeySampleMilliseconds - semanticKeyBefore));
                }
                return result;
            }

            public static GeneralFrontierProbe CreateProbe(GeneralFrontierState source)
            {
                return new GeneralFrontierProbe(source);
            }

            public static GeneralFrontierProbe AddProbe(
                GeneralFrontierProbe source,
                Candidate candidate,
                int outputSlot,
                PreparedRule[] rules,
                GeneralFrontierProbe? semanticTemplate,
                CustomSearchCreationCounts diagnostics,
                CustomSearchCostDiagnostics cost,
                StateExperimentContext experiment)
            {
                bool sampleCost = cost != null && (cost.StateAddOperations++ & 255L) == 0;
                if (sampleCost) cost.StateAddSamples++;
                if (diagnostics != null) diagnostics.DeferredProbeOperations++;
                GeneralFrontierProbe result = source.WithSelected(candidate, outputSlot);
                if (semanticTemplate.HasValue)
                {
                    GeneralFrontierProbe template = semanticTemplate.Value;
                    result.Contributions = template.Contributions;
                    result.ExclusiveOptions = template.ExclusiveOptions;
                    result.UnknownWinners = template.UnknownWinners;
                    result.CoLocationCounts = template.CoLocationCounts;
                    result.UnstableCoLocationTokens = template.UnstableCoLocationTokens;
                    result.UnknownWinnerCandidateTokens = template.UnknownWinnerCandidateTokens;
                    result.NegativeEffectCount = template.NegativeEffectCount;
                    result.NegativeRelicCount = template.NegativeRelicCount;
                    result.UncertaintyCount = template.UncertaintyCount;
                    result.SemanticKey = template.SemanticKey;
                    return result;
                }

                long partStart = sampleCost ? Stopwatch.GetTimestamp() : 0;
                result.Contributions = MergeContributions(
                    source.Contributions, candidate.SemanticContributions, diagnostics,
                    experiment != null && experiment.ExactCapacityMerges);
                result.ExclusiveOptions = MergeExclusiveOptions(
                    source.ExclusiveOptions, candidate.ExclusiveOptions, diagnostics,
                    experiment != null && experiment.ExactCapacityMerges);
                result.UnknownWinners = MergeUnknownWinners(
                    source.UnknownWinners, candidate.UnknownWinnerStates, diagnostics,
                    experiment != null && experiment.ExactCapacityMerges);
                if (ReferenceEquals(result.UnknownWinners, source.UnknownWinners))
                {
                    result.CoLocationCounts = InsertCoLocationCount(
                        source.CoLocationCounts, candidate.LocalCoLocationCount);
                    result.UnstableCoLocationTokens = InsertUnstableCoLocationToken(
                        source.UnstableCoLocationTokens, candidate.UnstableCoLocationToken);
                    result.UnknownWinnerCandidateTokens = source.UnknownWinnerCandidateTokens;
                }
                else
                {
                    BuildCoLocationSemanticState(
                        result, result.UnknownWinners, out result.CoLocationCounts,
                        out result.UnstableCoLocationTokens,
                        out result.UnknownWinnerCandidateTokens);
                }
                bool unchanged = ReferenceEquals(result.Contributions, source.Contributions)
                    && ReferenceEquals(result.ExclusiveOptions, source.ExclusiveOptions)
                    && ReferenceEquals(result.UnknownWinners, source.UnknownWinners)
                    && ReferenceEquals(result.CoLocationCounts, source.CoLocationCounts)
                    && ReferenceEquals(result.UnstableCoLocationTokens,
                        source.UnstableCoLocationTokens)
                    && ReferenceEquals(result.UnknownWinnerCandidateTokens,
                        source.UnknownWinnerCandidateTokens);
                result.NegativeEffectCount = source.NegativeEffectCount
                    + candidate.NegativeEffectCount;
                result.NegativeRelicCount = source.NegativeRelicCount
                    + candidate.NegativeRelicCount;
                result.UncertaintyCount = source.UncertaintyCount
                    + candidate.UncertaintyCount;
                if (sampleCost)
                    cost.ContributionUpdateSampleMilliseconds +=
                        CustomSearchDiagnostics.MillisecondsSince(partStart);
                if (unchanged)
                    result.SemanticKey = source.SemanticKey;
                else
                {
                    long keyStart = sampleCost ? Stopwatch.GetTimestamp() : 0;
                    result.SemanticKey = new SemanticStateKey(
                        result.Contributions, result.ExclusiveOptions,
                        result.CoLocationCounts, result.UnstableCoLocationTokens,
                        result.UnknownWinnerCandidateTokens);
                    if (diagnostics != null) diagnostics.SemanticKeys++;
                    if (sampleCost)
                        cost.SemanticKeySampleMilliseconds +=
                            CustomSearchDiagnostics.MillisecondsSince(keyStart);
                }
                return result;
            }

            public static GeneralFrontierState MaterializeProbe(
                GeneralFrontierProbe probe,
                CustomSearchCreationCounts diagnostics)
            {
                Candidate[] selected = (Candidate[])probe.Source.Selected.Clone();
                for (int slot = 0; slot < 6; slot++)
                    if ((probe.OverrideMask & (1 << slot)) != 0)
                        selected[slot] = probe.SelectedAt(slot);
                if (diagnostics != null)
                {
                    diagnostics.GeneralStates++;
                    diagnostics.SixSlotArrays++;
                    diagnostics.DeferredStateMaterializations++;
                }
                return new GeneralFrontierState(
                    selected, probe.Contributions, probe.ExclusiveOptions,
                    probe.UnknownWinners, probe.CoLocationCounts,
                    probe.UnstableCoLocationTokens,
                    probe.UnknownWinnerCandidateTokens,
                    probe.NegativeEffectCount, probe.NegativeRelicCount,
                    probe.UncertaintyCount, probe.SemanticKey, false);
            }

            private static UnknownWinnerState[] MergeUnknownWinners(
                UnknownWinnerState[] left,
                UnknownWinnerState[] right,
                CustomSearchCreationCounts diagnostics,
                bool exactCapacity)
            {
                if (right == null || right.Length == 0) return left;
                if (exactCapacity)
                {
                    int resultLength = 0;
                    bool exactChanged = false;
                    int exactLeftIndex = 0;
                    int exactRightIndex = 0;
                    while (exactLeftIndex < left.Length || exactRightIndex < right.Length)
                    {
                        resultLength++;
                        if (exactLeftIndex == left.Length)
                        {
                            exactRightIndex++;
                            exactChanged = true;
                            continue;
                        }
                        if (exactRightIndex == right.Length)
                        {
                            exactLeftIndex++;
                            continue;
                        }
                        int keyComparison = string.CompareOrdinal(
                            left[exactLeftIndex].Key, right[exactRightIndex].Key);
                        if (keyComparison < 0) exactLeftIndex++;
                        else if (keyComparison > 0)
                        {
                            exactRightIndex++;
                            exactChanged = true;
                        }
                        else
                        {
                            ValidateUnknownWinnerDeclaration(
                                left[exactLeftIndex], right[exactRightIndex]);
                            if (PreferredWinnerOptionsWouldChange(
                                left[exactLeftIndex].Options,
                                right[exactRightIndex].Options,
                                left[exactLeftIndex].Options[0].Capacity))
                                exactChanged = true;
                            exactLeftIndex++;
                            exactRightIndex++;
                        }
                    }
                    if (!exactChanged && resultLength == left.Length) return left;

                    UnknownWinnerState[] exact = new UnknownWinnerState[resultLength];
                    if (diagnostics != null) diagnostics.UnknownWinnerArrays++;
                    exactLeftIndex = 0;
                    exactRightIndex = 0;
                    int exactResultIndex = 0;
                    while (exactLeftIndex < left.Length || exactRightIndex < right.Length)
                    {
                        if (exactLeftIndex == left.Length)
                            exact[exactResultIndex++] = right[exactRightIndex++];
                        else if (exactRightIndex == right.Length)
                            exact[exactResultIndex++] = left[exactLeftIndex++];
                        else
                        {
                            int keyComparison = string.CompareOrdinal(
                                left[exactLeftIndex].Key, right[exactRightIndex].Key);
                            if (keyComparison < 0)
                                exact[exactResultIndex++] = left[exactLeftIndex++];
                            else if (keyComparison > 0)
                                exact[exactResultIndex++] = right[exactRightIndex++];
                            else
                            {
                                UnknownWinnerState current = left[exactLeftIndex++];
                                UnknownWinnerState incoming = right[exactRightIndex++];
                                if (!PreferredWinnerOptionsWouldChange(
                                    current.Options, incoming.Options,
                                    current.Options[0].Capacity))
                                    exact[exactResultIndex++] = current;
                                else
                                {
                                    UnknownWinnerOption[] merged = MergePreferredWinnerOptions(
                                        current.Options, incoming.Options,
                                        current.Options[0].Capacity);
                                    exact[exactResultIndex++] = new UnknownWinnerState(
                                        current.Key, merged);
                                }
                            }
                        }
                    }
                    return exact;
                }
                if (diagnostics != null) diagnostics.UnknownWinnerBuilderBuffers++;
                List<UnknownWinnerState> result = new List<UnknownWinnerState>(
                    left.Length + right.Length);
                bool changed = false;
                int leftIndex = 0;
                int rightIndex = 0;
                while (leftIndex < left.Length || rightIndex < right.Length)
                {
                    if (leftIndex == left.Length)
                    {
                        result.Add(right[rightIndex++]);
                        changed = true;
                        continue;
                    }
                    if (rightIndex == right.Length)
                    {
                        result.Add(left[leftIndex++]);
                        continue;
                    }
                    int keyComparison = string.CompareOrdinal(
                        left[leftIndex].Key, right[rightIndex].Key);
                    if (keyComparison < 0) result.Add(left[leftIndex++]);
                    else if (keyComparison > 0)
                    {
                        result.Add(right[rightIndex++]);
                        changed = true;
                    }
                    else
                    {
                        UnknownWinnerState current = left[leftIndex];
                        UnknownWinnerState incoming = right[rightIndex];
                        int capacity = current.Options[0].Capacity;
                        ValidateUnknownWinnerDeclaration(current, incoming);
                        UnknownWinnerOption[] merged = MergePreferredWinnerOptions(
                            current.Options, incoming.Options, capacity);
                        bool same = merged.Length == current.Options.Length;
                        for (int index = 0; same && index < merged.Length; index++)
                            same = ReferenceEquals(merged[index], current.Options[index]);
                        result.Add(same ? current
                            : new UnknownWinnerState(current.Key, merged));
                        if (!same) changed = true;
                        leftIndex++;
                        rightIndex++;
                    }
                }
                if (changed && diagnostics != null) diagnostics.UnknownWinnerArrays++;
                return changed ? result.ToArray() : left;
            }

            private static void ValidateUnknownWinnerDeclaration(
                UnknownWinnerState current,
                UnknownWinnerState incoming)
            {
                int capacity = current.Options[0].Capacity;
                string aggregationRule = current.Options[0].AggregationRule;
                for (int index = 0; index < incoming.Options.Length; index++)
                    if (incoming.Options[index].Capacity != capacity
                        || !string.Equals(incoming.Options[index].AggregationRule,
                            aggregationRule, StringComparison.Ordinal))
                        throw new CustomEffectDataException(
                            "同一 StackGroup 的聚合声明不一致。");
            }

            private static bool PreferredWinnerOptionsWouldChange(
                UnknownWinnerOption[] left,
                UnknownWinnerOption[] right,
                int capacity)
            {
                int length = Math.Min(capacity, left.Length + right.Length);
                if (length != left.Length) return true;
                int leftIndex = 0;
                int rightIndex = 0;
                for (int index = 0; index < length; index++)
                {
                    UnknownWinnerOption value;
                    if (rightIndex == right.Length
                        || leftIndex < left.Length
                        && UnknownWinnerOption.ComparePreference(
                            left[leftIndex], right[rightIndex]) <= 0)
                        value = left[leftIndex++];
                    else
                        value = right[rightIndex++];
                    if (!ReferenceEquals(value, left[index])) return true;
                }
                return false;
            }

            private static UnknownWinnerOption[] MergePreferredWinnerOptions(
                UnknownWinnerOption[] left,
                UnknownWinnerOption[] right,
                int capacity)
            {
                int length = Math.Min(capacity, left.Length + right.Length);
                UnknownWinnerOption[] result = new UnknownWinnerOption[length];
                int leftIndex = 0;
                int rightIndex = 0;
                for (int index = 0; index < length; index++)
                {
                    if (rightIndex == right.Length
                        || leftIndex < left.Length
                        && UnknownWinnerOption.ComparePreference(
                            left[leftIndex], right[rightIndex]) <= 0)
                        result[index] = left[leftIndex++];
                    else
                        result[index] = right[rightIndex++];
                }
                return result;
            }

            private static void BuildCoLocationSemanticState(
                Candidate[] selected,
                UnknownWinnerState[] unknownWinners,
                out int[] coLocationCounts,
                out string[] unstableCoLocationTokens,
                out string[] unknownWinnerCandidateTokens)
            {
                CoLocationSemanticBuilder builder = new CoLocationSemanticBuilder();
                foreach (Candidate candidate in selected)
                    builder.Add(candidate, unknownWinners);
                builder.Complete(out coLocationCounts, out unstableCoLocationTokens,
                    out unknownWinnerCandidateTokens);
            }

            private static void BuildCoLocationSemanticState(
                GeneralFrontierProbe selected,
                UnknownWinnerState[] unknownWinners,
                out int[] coLocationCounts,
                out string[] unstableCoLocationTokens,
                out string[] unknownWinnerCandidateTokens)
            {
                CoLocationSemanticBuilder builder = new CoLocationSemanticBuilder();
                for (int slot = 0; slot < 6; slot++)
                    builder.Add(selected.SelectedAt(slot), unknownWinners);
                builder.Complete(out coLocationCounts, out unstableCoLocationTokens,
                    out unknownWinnerCandidateTokens);
            }

            // Shared by the retained-state and probe paths. Only final independent
            // arrays escape; these lazily created lists belong to this one call.
            private struct CoLocationSemanticBuilder
            {
                private List<int> _counts;
                private List<string> _unstable;
                private List<string> _winners;

                public void Add(Candidate candidate, UnknownWinnerState[] unknownWinners)
                {
                    if (candidate == null) return;
                    bool unstable = !string.IsNullOrEmpty(candidate.UnstableCoLocationToken);
                    if (unstable)
                    {
                        if (_unstable == null) _unstable = new List<string>(6);
                        _unstable.Add(candidate.UnstableCoLocationToken);
                    }
                    string token = candidate.CreateUnknownWinnerCandidateToken(unknownWinners);
                    if (token != null)
                    {
                        if (_winners == null) _winners = new List<string>(6);
                        _winners.Add(token);
                    }
                    else if (!unstable && candidate.LocalCoLocationCount >= 2)
                    {
                        if (_counts == null) _counts = new List<int>(6);
                        _counts.Add(candidate.LocalCoLocationCount);
                    }
                }

                public void Complete(out int[] counts, out string[] unstable, out string[] winners)
                {
                    if (_counts != null) { _counts.Sort(); _counts.Reverse(); }
                    if (_unstable != null) _unstable.Sort(StringComparer.Ordinal);
                    if (_winners != null) _winners.Sort(StringComparer.Ordinal);
                    counts = _counts == null ? Array.Empty<int>() : _counts.ToArray();
                    unstable = _unstable == null ? Array.Empty<string>() : _unstable.ToArray();
                    winners = _winners == null ? Array.Empty<string>() : _winners.ToArray();
                }
            }

            private static int[] InsertCoLocationCount(int[] source, int value)
            {
                // The comparison ignores zero/one-match relics.  Retaining only
                // this sorted profile avoids distinguishing slot or effect-token
                // arrangements that have the same final co-location preference.
                if (value < 2) return source;
                int insertAt = 0;
                while (insertAt < source.Length && source[insertAt] >= value)
                    insertAt++;
                int[] result = new int[source.Length + 1];
                Array.Copy(source, 0, result, 0, insertAt);
                result[insertAt] = value;
                Array.Copy(source, insertAt, result, insertAt + 1,
                    source.Length - insertAt);
                return result;
            }

            private static string[] InsertUnstableCoLocationToken(string[] source, string value)
            {
                if (string.IsNullOrEmpty(value)) return source;
                int insertAt = 0;
                while (insertAt < source.Length
                    && string.CompareOrdinal(source[insertAt], value) <= 0)
                    insertAt++;
                string[] result = new string[source.Length + 1];
                Array.Copy(source, 0, result, 0, insertAt);
                result[insertAt] = value;
                Array.Copy(source, insertAt, result, insertAt + 1,
                    source.Length - insertAt);
                return result;
            }

            private static ContributionState[] MergeContributions(
                ContributionState[] left,
                ContributionState[] right,
                CustomSearchCreationCounts diagnostics,
                bool exactCapacity)
            {
                if (left.Length == 0) return right;
                if (right.Length == 0) return left;
                if (exactCapacity)
                {
                    int resultLength = 0;
                    bool exactChanged = false;
                    int exactLeftIndex = 0;
                    int exactRightIndex = 0;
                    while (exactLeftIndex < left.Length || exactRightIndex < right.Length)
                    {
                        resultLength++;
                        if (exactLeftIndex == left.Length)
                        {
                            exactRightIndex++;
                            exactChanged = true;
                            continue;
                        }
                        if (exactRightIndex == right.Length)
                        {
                            exactLeftIndex++;
                            continue;
                        }
                        int comparison = string.CompareOrdinal(
                            left[exactLeftIndex].Key, right[exactRightIndex].Key);
                        if (comparison < 0) exactLeftIndex++;
                        else if (comparison > 0)
                        {
                            exactRightIndex++;
                            exactChanged = true;
                        }
                        else
                        {
                            if (left[exactLeftIndex].WouldMergeChange(right[exactRightIndex]))
                                exactChanged = true;
                            exactLeftIndex++;
                            exactRightIndex++;
                        }
                    }
                    if (!exactChanged && resultLength == left.Length) return left;

                    ContributionState[] exact = new ContributionState[resultLength];
                    if (diagnostics != null) diagnostics.ContributionArrays++;
                    exactLeftIndex = 0;
                    exactRightIndex = 0;
                    int exactResultIndex = 0;
                    while (exactLeftIndex < left.Length || exactRightIndex < right.Length)
                    {
                        if (exactLeftIndex == left.Length)
                            exact[exactResultIndex++] = right[exactRightIndex++];
                        else if (exactRightIndex == right.Length)
                            exact[exactResultIndex++] = left[exactLeftIndex++];
                        else
                        {
                            int comparison = string.CompareOrdinal(
                                left[exactLeftIndex].Key, right[exactRightIndex].Key);
                            if (comparison < 0)
                                exact[exactResultIndex++] = left[exactLeftIndex++];
                            else if (comparison > 0)
                                exact[exactResultIndex++] = right[exactRightIndex++];
                            else
                                exact[exactResultIndex++] = left[exactLeftIndex++].Merge(
                                    right[exactRightIndex++], diagnostics, true);
                        }
                    }
                    return exact;
                }
                ContributionState[] result = new ContributionState[left.Length + right.Length];
                if (diagnostics != null) diagnostics.ContributionArrays++;
                int resultIndex = 0;
                int leftIndex = 0;
                int rightIndex = 0;
                bool changed = false;
                while (leftIndex < left.Length || rightIndex < right.Length)
                {
                    if (leftIndex == left.Length)
                    {
                        result[resultIndex++] = right[rightIndex++];
                        changed = true;
                        continue;
                    }
                    if (rightIndex == right.Length)
                    {
                        result[resultIndex++] = left[leftIndex++];
                        continue;
                    }
                    int comparison = string.CompareOrdinal(left[leftIndex].Key, right[rightIndex].Key);
                    if (comparison < 0) result[resultIndex++] = left[leftIndex++];
                    else if (comparison > 0)
                    {
                        result[resultIndex++] = right[rightIndex++];
                        changed = true;
                    }
                    else
                    {
                        ContributionState merged = left[leftIndex].Merge(
                            right[rightIndex], diagnostics);
                        result[resultIndex++] = merged;
                        if (!ReferenceEquals(merged, left[leftIndex])) changed = true;
                        leftIndex++;
                        rightIndex++;
                    }
                }
                if (!changed && resultIndex == left.Length) return left;
                if (resultIndex != result.Length)
                {
                    Array.Resize(ref result, resultIndex);
                    if (diagnostics != null) diagnostics.ContributionArrays++;
                }
                return result;
            }

            private static ExclusiveOption[] MergeExclusiveOptions(
                ExclusiveOption[] left,
                ExclusiveOption[] right,
                CustomSearchCreationCounts diagnostics,
                bool exactCapacity)
            {
                if (left.Length == 0) return right;
                if (right.Length == 0) return left;
                if (exactCapacity)
                {
                    int resultLength = 0;
                    int leftCountIndex = 0;
                    int rightCountIndex = 0;
                    while (leftCountIndex < left.Length || rightCountIndex < right.Length)
                    {
                        resultLength++;
                        if (leftCountIndex == left.Length) rightCountIndex++;
                        else if (rightCountIndex == right.Length) leftCountIndex++;
                        else
                        {
                            int comparison = left[leftCountIndex].ExclusivityId.CompareTo(
                                right[rightCountIndex].ExclusivityId);
                            if (comparison < 0) leftCountIndex++;
                            else if (comparison > 0) rightCountIndex++;
                            else
                            {
                                leftCountIndex++;
                                rightCountIndex++;
                            }
                        }
                    }
                    ExclusiveOption[] exact = new ExclusiveOption[resultLength];
                    if (diagnostics != null) diagnostics.ExclusiveOptionArrays++;
                    int exactLeftIndex = 0;
                    int exactRightIndex = 0;
                    int exactResultIndex = 0;
                    while (exactLeftIndex < left.Length || exactRightIndex < right.Length)
                    {
                        if (exactLeftIndex == left.Length)
                            exact[exactResultIndex++] = right[exactRightIndex++];
                        else if (exactRightIndex == right.Length)
                            exact[exactResultIndex++] = left[exactLeftIndex++];
                        else
                        {
                            int comparison = left[exactLeftIndex].ExclusivityId.CompareTo(
                                right[exactRightIndex].ExclusivityId);
                            if (comparison < 0)
                                exact[exactResultIndex++] = left[exactLeftIndex++];
                            else if (comparison > 0)
                                exact[exactResultIndex++] = right[exactRightIndex++];
                            else
                                exact[exactResultIndex++] = left[exactLeftIndex++].Merge(
                                    right[exactRightIndex++]);
                        }
                    }
                    return exact;
                }
                if (diagnostics != null) diagnostics.ExclusiveOptionBuilderBuffers++;
                List<ExclusiveOption> result = new List<ExclusiveOption>(left.Length + right.Length);
                int leftIndex = 0;
                int rightIndex = 0;
                while (leftIndex < left.Length || rightIndex < right.Length)
                {
                    ExclusiveOption value;
                    if (leftIndex == left.Length) value = right[rightIndex++];
                    else if (rightIndex == right.Length) value = left[leftIndex++];
                    else
                    {
                        int comparison = left[leftIndex].ExclusivityId.CompareTo(
                            right[rightIndex].ExclusivityId);
                        if (comparison < 0) value = left[leftIndex++];
                        else if (comparison > 0) value = right[rightIndex++];
                        else
                        {
                            value = left[leftIndex++].Merge(right[rightIndex++]);
                        }
                    }
                    result.Add(value);
                }
                if (diagnostics != null) diagnostics.ExclusiveOptionArrays++;
                return result.ToArray();
            }
        }
    }
}
