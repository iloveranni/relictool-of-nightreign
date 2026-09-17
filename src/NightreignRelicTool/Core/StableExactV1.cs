using System;
using System.Collections.Generic;
using System.Threading;

namespace NightreignRelicTool.Core
{
    /// <summary>
    /// Immutable description of the frozen exact-search backend. This profile
    /// records algorithm selection only; game and catalog data remain runtime inputs.
    /// </summary>
    public sealed class StableExactSearchProfile
    {
        internal StableExactSearchProfile()
        {
            ModelId = StableExactV1.ModelId;
            ModelVersion = StableExactV1.ModelVersion;
            TerminalWorkspaceOptimizationEnabled = true;
            TerminalPruningEnabled = true;
            IntermediatePruningEnabled = true;
            RequiredGapReferenceOrderingEnabled = true;
            CompiledSecondaryComparisonEnabled = true;
            AdaptiveExtraEvaluationBudget = 0;
            ConservativePrefixPrescreenEnabled = false;
            PrefixProposalOrderingEnabled = false;
            OracleReferenceEnabled = false;
            PreviousExactResultHintEnabled = false;
            ExactCapacityStateMergesEnabled = false;
            DeferredFrontierStatesEnabled = false;
            TwoSlotReferenceRepairEnabled = false;
            ApproximateSearchEnabled = false;
            MaxElapsedMilliseconds = CustomSearchLimits.DefaultMaxElapsedMilliseconds;
            MaxVisitedStates = CustomSearchLimits.DefaultMaxVisitedStates;
            MaxTransitions = CustomSearchLimits.DefaultMaxTransitions;
            BaseReferenceEvaluationLimit = StableExactV1Configuration.BaseReferenceEvaluationLimit;
            ComparatorId = "CustomComparisonVector.Compare/v1";
            ContributionEvaluatorId = "EffectiveContributionEvaluator/v1";
        }

        public string ModelId { get; private set; }
        public string ModelVersion { get; private set; }
        public bool TerminalWorkspaceOptimizationEnabled { get; private set; }
        public bool TerminalPruningEnabled { get; private set; }
        public bool IntermediatePruningEnabled { get; private set; }
        public bool RequiredGapReferenceOrderingEnabled { get; private set; }
        public bool CompiledSecondaryComparisonEnabled { get; private set; }
        public int AdaptiveExtraEvaluationBudget { get; private set; }
        public bool ConservativePrefixPrescreenEnabled { get; private set; }
        public bool PrefixProposalOrderingEnabled { get; private set; }
        public bool OracleReferenceEnabled { get; private set; }
        public bool PreviousExactResultHintEnabled { get; private set; }
        public bool ExactCapacityStateMergesEnabled { get; private set; }
        public bool DeferredFrontierStatesEnabled { get; private set; }
        public bool TwoSlotReferenceRepairEnabled { get; private set; }
        public bool ApproximateSearchEnabled { get; private set; }
        public long MaxElapsedMilliseconds { get; private set; }
        public long MaxVisitedStates { get; private set; }
        public long MaxTransitions { get; private set; }
        public int BaseReferenceEvaluationLimit { get; private set; }
        public string ComparatorId { get; private set; }
        public string ContributionEvaluatorId { get; private set; }
    }

    internal static class StableExactV1Configuration
    {
        internal const int ReferencePassCount = 2;
        internal const int ReferenceSlotCount = 6;
        internal const int ReferenceAttemptsPerSlot = 16;
        internal const int BaseReferenceEvaluationLimit =
            1 + ReferencePassCount * ReferenceSlotCount * ReferenceAttemptsPerSlot;

        internal static CustomSearchOptions CreateOptions(
            bool diagnostics,
            bool detailedCostDiagnostics,
            Action<CustomSearchDiagnostics> diagnosticsCompleted,
            Action<CustomSearchBuild[]> completedVessels)
        {
            return new CustomSearchOptions
            {
                EnableDiagnostics = diagnostics,
                DiagnosticsCompleted = diagnosticsCompleted,
                EnableTerminalPruning = true,
                EnableIntermediatePruning = true,
                EnableDepthSeparatedPhases = true,
                EnableTerminalBlockPruning = true,
                EnableTopThreeThreshold = true,
                EnableRequiredGapReferenceOrdering = true,
                EnableCompiledSecondaryComparison = true,
                EnableExactCapacityStateMerges = false,
                EnableDeferredFrontierStates = false,
                EnableDetailedCostDiagnostics = diagnostics && detailedCostDiagnostics,
                CompletedVesselsForTest = completedVessels,
                // Research-only reference modes are deliberately absent. In
                // particular this leaves Oracle, history, adaptive-budget and
                // prefix experiments unreachable from the stable entry point.
                OracleReferenceTest = null,
                ReferenceGuidanceTest = null
            };
        }
    }

    /// <summary>
    /// Frozen exact-search backend. Callers provide only business inputs; the
    /// validated algorithm switches cannot be recombined through this API.
    /// </summary>
    public sealed class StableExactV1
    {
        public const string ModelId = "StableExactV1";
        public const string ModelVersion = "1";

        private static readonly StableExactSearchProfile StableProfile =
            new StableExactSearchProfile();

        private readonly CustomEffectSearcher _searcher;
        private readonly CustomSearchOptions _runtimeOptions;

        public StableExactV1(RuntimeModel model, CustomEffectCatalog catalog)
            : this(model, catalog, new CustomSearchLimits())
        {
        }

        internal StableExactV1(
            RuntimeModel model,
            CustomEffectCatalog catalog,
            CustomSearchLimits limits)
        {
            _searcher = new CustomEffectSearcher(model, catalog, limits);
            _runtimeOptions = StableExactV1Configuration.CreateOptions(
                false, false, null, null);
        }

        public static StableExactSearchProfile Profile
        {
            get { return StableProfile; }
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
            return _searcher.Search(
                inventory, character, rules, cancellationToken, _runtimeOptions);
        }

        // Internal validation seam. It can add aggregate diagnostics and expose
        // completed per-vessel winners, but it cannot select experimental modes.
        internal CustomSearchResponse SearchForValidation(
            CharacterInventory inventory,
            CharacterDefinition character,
            IEnumerable<CustomEffectRule> rules,
            CancellationToken cancellationToken,
            bool diagnostics,
            bool detailedCostDiagnostics,
            Action<CustomSearchBuild[]> completedVessels,
            Action<CustomSearchDiagnostics> diagnosticsCompleted = null)
        {
            CustomSearchOptions options = StableExactV1Configuration.CreateOptions(
                diagnostics, detailedCostDiagnostics, diagnosticsCompleted,
                completedVessels);
            return _searcher.Search(
                inventory, character, rules, cancellationToken, options);
        }

        internal static CustomSearchOptions CreateOptionsForValidation(
            bool diagnostics = false)
        {
            return StableExactV1Configuration.CreateOptions(
                diagnostics, false, null, null);
        }
    }
}
