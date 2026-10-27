using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Serialization;

namespace NightreignRelicTool.Core
{
    public sealed class CustomSearchOptions
    {
        public bool EnableDiagnostics { get; set; }
        // Experimental, deliberately absent from the application/UI defaults.
        internal bool EnableTerminalPruning { get; set; }
        internal Action<object> TerminalBoundAudit { get; set; }
        internal bool EnableIntermediatePruning { get; set; }
        internal bool EnableDepthSeparatedPhases { get; set; }
        internal bool EnableTerminalBlockPruning { get; set; }
        internal bool EnableTopThreeThreshold { get; set; }
        internal Action<object> GlobalBoundAudit { get; set; }
        internal Action<object> IntermediateBoundAudit { get; set; }
        // Stable exact model feature: reorders only the bounded early-reference
        // proposals. It does not alter the formal candidate set or search order.
        internal bool EnableRequiredGapReferenceOrdering { get; set; }
        // Test-only, never enabled by the application. No objects when absent.
        internal OracleReferenceTestHooks OracleReferenceTest { get; set; }
        internal ReferenceGuidanceTestOptions ReferenceGuidanceTest { get; set; }
        internal Action<CustomSearchBuild[]> CompletedVesselsForTest { get; set; }
        // Test-only cost sampling. The application never enables this flag.
        // It requires EnableDiagnostics and samples only a small fraction of
        // hot-loop operations; no timer is read for every pair.
        internal bool EnableDetailedCostDiagnostics { get; set; }
        // Independent compact-state experiments. All default false and are
        // deliberately absent from the application/UI.
        internal bool EnableExactCapacityStateMerges { get; set; }
        internal bool EnableCompiledSecondaryComparison { get; set; }
        internal bool EnableDeferredFrontierStates { get; set; }
        // Invoked once after the search stops, including thrown cancellation or
        // request errors. Observer failures must not change search behavior.
        public Action<CustomSearchDiagnostics> DiagnosticsCompleted { get; set; }
    }

    public enum CustomSearchDiagnosticOutcome
    {
        Running, Completed, NoLegalBuild, Canceled, DataFailure, InvalidRequest,
        TimeLimit, StateLimit, TransitionLimit, UnexpectedFailure
    }

    /// <summary>Bounded, aggregate-only diagnostics: no inventory or instance identifiers.</summary>
    [DataContract]
    public sealed class CustomSearchDiagnostics
    {
        public CustomSearchDiagnostics()
        {
            Stages = new List<CustomSearchStageDiagnostics>();
            Vessels = new List<CustomSearchVesselDiagnostics>();
            Creations = new CustomSearchCreationCounts();
        }
        [DataMember] public CustomSearchDiagnosticOutcome Outcome { get; internal set; }
        [DataMember] public string OutcomeName { get; internal set; }
        [DataMember] public int RuleCount { get; internal set; }
        [DataMember] public int RequiredRuleCount { get; internal set; }
        [DataMember] public int WantedRuleCount { get; internal set; }
        [DataMember] public bool TerminalPruningEnabled { get; internal set; }
        [DataMember] public bool IntermediatePruningEnabled { get; internal set; }
        [DataMember] public bool RequiredGapReferenceOrderingEnabled { get; internal set; }
        [DataMember] public bool DetailedCostDiagnosticsEnabled { get; internal set; }
        [DataMember] public bool ExactCapacityStateMergesEnabled { get; internal set; }
        [DataMember] public bool CompiledSecondaryComparisonEnabled { get; internal set; }
        [DataMember] public bool DeferredFrontierStatesEnabled { get; internal set; }
        [DataMember] public int CostCheckSampleStride { get; internal set; }
        [DataMember] public int CostMergeSampleStride { get; internal set; }
        [DataMember] public double ProcessCpuMilliseconds { get; internal set; }
        [DataMember(EmitDefaultValue = false)] public double? OracleInputValidationMilliseconds { get; internal set; }
        [DataMember(EmitDefaultValue = false)] public string OracleInputStatus { get; internal set; }
        [DataMember(EmitDefaultValue = false)] public string PreviousHintInputStatus { get; internal set; }
        [DataMember(EmitDefaultValue = false)] public double? PreviousHintInputValidationMilliseconds { get; internal set; }
        [DataMember] public int OriginalCandidateCount { get; internal set; }
        [DataMember] public int CompressedCandidateCount { get; internal set; }
        [DataMember] public int CandidateProjectionDiscardCount { get; internal set; }
        [DataMember] public int InvalidColorCandidateCount { get; internal set; }
        [DataMember] public double ElapsedMilliseconds { get; internal set; }
        [DataMember] public long VisitedStateCount { get; internal set; }
        [DataMember] public long TransitionCount { get; internal set; }
        [DataMember] public long PeakStateCount { get; internal set; }
        // Sampled dictionary-entry total, including merge inputs and partial
        // worker tables. Shared references may be counted twice; not GC liveness.
        [DataMember] public long ObservedRetainedEntryPeak { get; internal set; }
        [DataMember] public int CompletedVesselCount { get; internal set; }
        [DataMember] public int GloballyExcludedVesselCount { get; internal set; }
        [DataMember] public double? ThirdThresholdFirstMilliseconds { get; internal set; }
        [DataMember] public int ThirdThresholdUpdates { get; internal set; }
        [DataMember] public int? ActiveVesselId { get; internal set; }
        [DataMember] public int? ActivePhaseIndex { get; internal set; }
        [DataMember] public string ActiveOperation { get; internal set; }
        [DataMember] public int Gen0Collections { get; internal set; }
        [DataMember] public int Gen1Collections { get; internal set; }
        [DataMember] public int Gen2Collections { get; internal set; }
        [DataMember] public long ManagedMemoryBefore { get; internal set; }
        [DataMember] public long ManagedMemoryAfter { get; internal set; }
        [DataMember] public long PrivateMemoryBefore { get; internal set; }
        [DataMember] public long PrivateMemoryAfter { get; internal set; }
        [DataMember] public List<CustomSearchStageDiagnostics> Stages { get; private set; }
        [DataMember] public List<CustomSearchVesselDiagnostics> Vessels { get; private set; }
        [DataMember] public CustomSearchCreationCounts Creations { get; private set; }

        private long _stageStart;
        private CustomSearchStageDiagnostics _stage;
        private int _gen0, _gen1, _gen2;
        private TimeSpan _cpuBefore;
        internal void Start()
        {
            _gen0 = GC.CollectionCount(0); _gen1 = GC.CollectionCount(1); _gen2 = GC.CollectionCount(2);
            ManagedMemoryBefore = GC.GetTotalMemory(false);
            PrivateMemoryBefore = ReadPrivateMemory();
            _cpuBefore = ReadCpuTime();
            BeginStage("RequestAndRules");
        }
        internal void BeginStage(string name)
        {
            EndStage();
            _stage = new CustomSearchStageDiagnostics { Name = name };
            Stages.Add(_stage);
            _stageStart = Stopwatch.GetTimestamp();
            ActiveOperation = name;
        }
        internal void EndStage()
        {
            if (_stage == null) return;
            _stage.ElapsedMilliseconds += MillisecondsSince(_stageStart);
            _stage = null;
        }
        internal void Finish()
        {
            EndStage();
            OutcomeName = Outcome.ToString();
            Gen0Collections = GC.CollectionCount(0) - _gen0;
            Gen1Collections = GC.CollectionCount(1) - _gen1;
            Gen2Collections = GC.CollectionCount(2) - _gen2;
            ManagedMemoryAfter = GC.GetTotalMemory(false);
            PrivateMemoryAfter = ReadPrivateMemory();
            TimeSpan cpuAfter = ReadCpuTime();
            ProcessCpuMilliseconds = cpuAfter == TimeSpan.MinValue || _cpuBefore == TimeSpan.MinValue
                ? -1 : (cpuAfter - _cpuBefore).TotalMilliseconds;
            foreach (CustomSearchVesselDiagnostics vessel in Vessels)
            {
                Creations.Add(vessel.Creations);
                ObservedRetainedEntryPeak = Math.Max(ObservedRetainedEntryPeak, vessel.ObservedRetainedEntryPeak);
            }
        }
        internal static double MillisecondsSince(long start)
        {
            return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        }
        internal static long ReadPrivateMemory()
        {
            try { using (Process process = Process.GetCurrentProcess()) return process.PrivateMemorySize64; }
            catch (InvalidOperationException) { return -1; }
        }
        private static TimeSpan ReadCpuTime()
        {
            try { using (Process process = Process.GetCurrentProcess()) return process.TotalProcessorTime; }
            catch (InvalidOperationException) { return TimeSpan.MinValue; }
        }
    }

    [DataContract]
    public sealed class CustomSearchStageDiagnostics
    {
        [DataMember] public string Name { get; internal set; }
        [DataMember] public double ElapsedMilliseconds { get; internal set; }
    }

    [DataContract]
    public sealed class CustomSearchVesselDiagnostics
    {
        public CustomSearchVesselDiagnostics()
        {
            Phases = new List<CustomSearchPhaseDiagnostics>();
            Creations = new CustomSearchCreationCounts();
        }
        [DataMember] public int VesselId { get; internal set; }
        [DataMember(EmitDefaultValue = false)] public CustomSearchPruningDiagnostics IntermediatePreparation { get; internal set; }
        [DataMember(EmitDefaultValue = false)] public OracleReferenceDiagnostics OracleReference { get; internal set; }
        [DataMember(EmitDefaultValue = false)] public ReferenceGuidanceDiagnostics ReferenceGuidance { get; internal set; }
        [DataMember] public string VesselName { get; internal set; }
        [DataMember] public int[] SlotColorIds { get; internal set; }
        [DataMember] public bool[] SlotIsDeep { get; internal set; }
        [DataMember] public int[] LegalCandidateCountBySlot { get; internal set; }
        [DataMember] public int[] ConfiguredPhaseSlotMasks { get; internal set; }
        [DataMember] public int IncompatibleCandidateCount { get; internal set; }
        [DataMember] public bool Completed { get; internal set; }
        [DataMember] public string ProofStatus { get; internal set; }
        [DataMember] public bool GlobalEarlyExcluded { get; internal set; }
        [DataMember] public double GlobalPreparationMilliseconds { get; internal set; }
        [DataMember] public long GlobalChecks { get; internal set; }
        [DataMember] public long GlobalPrunedBranches { get; internal set; }
        [DataMember] public long GlobalPrunedBlocks { get; internal set; }
        [DataMember] public long GlobalPrunedPairs { get; internal set; }
        // Nullable when reading diagnostics from the captured phase 2A assembly.
        // Process-wide endpoint samples, not per-vessel allocated bytes or peaks.
        [DataMember(EmitDefaultValue = false)] public long? ManagedMemoryBefore { get; internal set; }
        [DataMember(EmitDefaultValue = false)] public long? ManagedMemoryAfter { get; internal set; }
        [DataMember(EmitDefaultValue = false)] public long? PrivateMemoryBefore { get; internal set; }
        [DataMember(EmitDefaultValue = false)] public long? PrivateMemoryAfter { get; internal set; }
        [DataMember] public double ElapsedMilliseconds { get; internal set; }
        [DataMember] public double PhaseConfigurationMilliseconds { get; internal set; }
        [DataMember] public double TerminalMergeMilliseconds { get; internal set; }
        [DataMember] public double TerminalPartitionUnionMilliseconds { get; internal set; }
        [DataMember] public double EffectiveEvaluationMilliseconds { get; internal set; }
        [DataMember] public double ResultAssemblyMilliseconds { get; internal set; }
        [DataMember(EmitDefaultValue = false)] public CustomSearchCostDiagnostics Cost { get; internal set; }
        [DataMember] public long TerminalFoldInputCount { get; internal set; }
        [DataMember] public long TerminalFoldOutputCount { get; internal set; }
        [DataMember] public long CompletedEvaluationCount { get; internal set; }
        [DataMember] public long VisitedStateCount { get; internal set; }
        [DataMember] public long TransitionCount { get; internal set; }
        [DataMember] public long ObservedRetainedEntryPeak { get; internal set; }
        [DataMember] public List<CustomSearchPhaseDiagnostics> Phases { get; private set; }
        [DataMember] public CustomSearchCreationCounts Creations { get; private set; }
    }

    [DataContract]
    public sealed class CustomSearchPhaseDiagnostics
    {
        public CustomSearchPhaseDiagnostics()
        {
            Depths = new CustomSearchDepthDiagnostics[7];
            for (int i = 0; i < Depths.Length; i++) Depths[i] = new CustomSearchDepthDiagnostics { FilledSlotCount = i };
        }
        [DataMember] public int PhaseIndex { get; internal set; }
        [DataMember] public int SlotMask { get; internal set; }
        [DataMember] public int[] Slots { get; internal set; }
        [DataMember] public int CandidateCount { get; internal set; }
        [DataMember] public int CandidatesStarted { get; internal set; }
        [DataMember] public int CandidatesCompleted { get; internal set; }
        [DataMember] public bool Completed { get; internal set; }
        [DataMember] public long LocalCompletedFrontierCount { get; internal set; }
        [DataMember] public long AggregateFrontierInputCount { get; internal set; }
        [DataMember] public long AggregateFrontierOutputCount { get; internal set; }
        [DataMember] public double ExpansionMilliseconds { get; internal set; }
        [DataMember] public double ElapsedMilliseconds { get; internal set; }
        [DataMember] public CustomSearchDepthDiagnostics[] Depths { get; private set; }
        [DataMember] public CustomSearchMergeDiagnostics Merge { get; internal set; }
        [DataMember(EmitDefaultValue = false)] public CustomSearchCostDiagnostics ExpansionCost { get; internal set; }
    }

    /// <summary>
    /// Input counts repeated source visits, not distinct states. Generated,
    /// equivalent and retained refer to destination depth. Retained is the
    /// currently stored distinct-state count, including the initial depth 0.
    /// </summary>
    [DataContract]
    public sealed class CustomSearchDepthDiagnostics
    {
        [DataMember] public int FilledSlotCount { get; internal set; }
        [DataMember] public long InputStateVisits { get; internal set; }
        [DataMember] public long GeneratedStates { get; internal set; }
        [DataMember] public long EquivalentStateHits { get; internal set; }
        [DataMember] public long ReplacedStates { get; internal set; }
        [DataMember] public long DiscardedStates { get; internal set; }
        [DataMember] public long RetainedStates { get; internal set; }
        [DataMember] public long IncompatibleSlotAttempts { get; internal set; }
        [DataMember] public long SymmetrySlotAttempts { get; internal set; }
        [DataMember] public double ElapsedMilliseconds { get; internal set; }
    }

    [DataContract]
    public sealed class CustomSearchMergeDiagnostics
    {
        [DataMember] public CustomSearchPruningDiagnostics Pruning { get; internal set; }
        [DataMember] public string Kind { get; internal set; }
        [DataMember] public long AggregateInputCount { get; internal set; }
        [DataMember] public long LocalInputCount { get; internal set; }
        [DataMember] public long TheoreticalPairCount { get; internal set; }
        [DataMember] public long CompletedPairCount { get; internal set; }
        [DataMember] public long TransitionCount { get; internal set; }
        [DataMember] public long EquivalentStateHits { get; internal set; }
        [DataMember] public long ReplacedStates { get; internal set; }
        [DataMember] public long DiscardedStates { get; internal set; }
        [DataMember] public long OutputStateCount { get; internal set; }
        [DataMember] public long PartialTerminalStateCount { get; internal set; }
        [DataMember] public int WorkerCount { get; internal set; }
        [DataMember] public bool Completed { get; internal set; }
        [DataMember] public double ElapsedMilliseconds { get; internal set; }
        [DataMember(EmitDefaultValue = false)] public CustomSearchCostDiagnostics Cost { get; internal set; }
    }

    /// <summary>
    /// Detailed test-only cost samples. Exact operation counts are separate
    /// from sampled intervals. Estimated totals must be derived by scaling
    /// homogeneous sampled classes; sampled milliseconds are never a wall-clock
    /// phase and may be cumulative across terminal workers.
    /// </summary>
    [DataContract]
    public sealed class CustomSearchCostDiagnostics
    {
        [DataMember] public long BoundPrunedOperations { get; internal set; }
        [DataMember] public long BoundPassedOperations { get; internal set; }
        [DataMember] public long BoundPrunedSamples { get; internal set; }
        [DataMember] public long BoundPassedSamples { get; internal set; }
        [DataMember] public double BoundPrunedSampleMilliseconds { get; internal set; }
        [DataMember] public double BoundPassedSampleMilliseconds { get; internal set; }
        [DataMember] public long StateAddOperations { get; internal set; }
        [DataMember] public long StateAddSamples { get; internal set; }
        [DataMember] public double SelectedCopySampleMilliseconds { get; internal set; }
        [DataMember] public double ContributionUpdateSampleMilliseconds { get; internal set; }
        [DataMember] public double SemanticKeySampleMilliseconds { get; internal set; }
        [DataMember] public double StateFinalizeSampleMilliseconds { get; internal set; }
        [DataMember] public long DictionaryOperations { get; internal set; }
        [DataMember] public long DictionarySamples { get; internal set; }
        [DataMember] public double DictionarySampleMilliseconds { get; internal set; }
        [DataMember] public double SecondaryCompareSampleMilliseconds { get; internal set; }
        [DataMember] public long TerminalFoldOperations { get; internal set; }
        [DataMember] public long TerminalFoldSamples { get; internal set; }
        [DataMember] public double CoProfileSampleMilliseconds { get; internal set; }
        [DataMember] public double TerminalKeySampleMilliseconds { get; internal set; }
        [DataMember] public double SnapshotSampleMilliseconds { get; internal set; }
        [DataMember] public double LoopWallMilliseconds { get; internal set; }
        // Sum/max of whole worker intervals. Sum is CPU-like worker occupancy,
        // not query wall time. Dispatch overhead is interpreted with max only.
        [DataMember] public double WorkerMillisecondsSum { get; internal set; }
        [DataMember] public double WorkerMillisecondsMax { get; internal set; }
        [DataMember] public int TimedWorkerCount { get; internal set; }
        // Sufficient statistics for y = checkCost*checks + mergeCost*merges.
        // One wall interval is recorded per 4,096-pair batch, never per pair.
        [DataMember] public long BatchCount { get; internal set; }
        [DataMember] public long BatchCheckOperations { get; internal set; }
        [DataMember] public long BatchMergeOperations { get; internal set; }
        [DataMember] public double BatchMilliseconds { get; internal set; }
        [DataMember] public double BatchCheckSquared { get; internal set; }
        [DataMember] public double BatchMergeSquared { get; internal set; }
        [DataMember] public double BatchCheckMerge { get; internal set; }
        [DataMember] public double BatchCheckMilliseconds { get; internal set; }
        [DataMember] public double BatchMergeMilliseconds { get; internal set; }

        internal void Add(CustomSearchCostDiagnostics other)
        {
            if (other == null) return;
            BoundPrunedOperations += other.BoundPrunedOperations;
            BoundPassedOperations += other.BoundPassedOperations;
            BoundPrunedSamples += other.BoundPrunedSamples;
            BoundPassedSamples += other.BoundPassedSamples;
            BoundPrunedSampleMilliseconds += other.BoundPrunedSampleMilliseconds;
            BoundPassedSampleMilliseconds += other.BoundPassedSampleMilliseconds;
            StateAddOperations += other.StateAddOperations;
            StateAddSamples += other.StateAddSamples;
            SelectedCopySampleMilliseconds += other.SelectedCopySampleMilliseconds;
            ContributionUpdateSampleMilliseconds += other.ContributionUpdateSampleMilliseconds;
            SemanticKeySampleMilliseconds += other.SemanticKeySampleMilliseconds;
            StateFinalizeSampleMilliseconds += other.StateFinalizeSampleMilliseconds;
            DictionaryOperations += other.DictionaryOperations;
            DictionarySamples += other.DictionarySamples;
            DictionarySampleMilliseconds += other.DictionarySampleMilliseconds;
            SecondaryCompareSampleMilliseconds += other.SecondaryCompareSampleMilliseconds;
            TerminalFoldOperations += other.TerminalFoldOperations;
            TerminalFoldSamples += other.TerminalFoldSamples;
            CoProfileSampleMilliseconds += other.CoProfileSampleMilliseconds;
            TerminalKeySampleMilliseconds += other.TerminalKeySampleMilliseconds;
            SnapshotSampleMilliseconds += other.SnapshotSampleMilliseconds;
            LoopWallMilliseconds += other.LoopWallMilliseconds;
            WorkerMillisecondsSum += other.WorkerMillisecondsSum;
            WorkerMillisecondsMax = Math.Max(WorkerMillisecondsMax, other.WorkerMillisecondsMax);
            TimedWorkerCount += other.TimedWorkerCount;
            BatchCount += other.BatchCount;
            BatchCheckOperations += other.BatchCheckOperations;
            BatchMergeOperations += other.BatchMergeOperations;
            BatchMilliseconds += other.BatchMilliseconds;
            BatchCheckSquared += other.BatchCheckSquared;
            BatchMergeSquared += other.BatchMergeSquared;
            BatchCheckMerge += other.BatchCheckMerge;
            BatchCheckMilliseconds += other.BatchCheckMilliseconds;
            BatchMergeMilliseconds += other.BatchMergeMilliseconds;
        }
    }

    [DataContract]
    public sealed class CustomSearchPruningDiagnostics
    {
        // Checks/PrunedPairs cover logical pairs, including block proofs. These
        // separate counters expose actual block work and avoided pair checks.
        [DataMember] public long BlockChecks { get; internal set; }
        [DataMember] public long BlockPrunedPairs { get; internal set; }
        [DataMember] public bool ReferenceBuilderSkippedForSmallJoin { get; internal set; }
        [DataMember] public bool ReferenceBuilderSkippedForOptimalPrefix { get; internal set; }
        [DataMember] public bool ReferenceFound { get; internal set; }
        [DataMember] public int ReferenceAttempts { get; internal set; }
        [DataMember] public int ReferenceEvaluations { get; internal set; }
        [DataMember] public int ReferenceImprovements { get; internal set; }
        [DataMember] public int ReferenceMissingRequired { get; internal set; }
        // Compared only after this vessel is proven complete; null if unresolved.
        [DataMember(EmitDefaultValue = false)] public int? ReferencePrefixComparisonToBest { get; internal set; }
        [DataMember] public long ReferenceProposalArrays { get; internal set; }
        [DataMember] public long ReferenceTransitions { get; internal set; }
        [DataMember] public double ReferenceMilliseconds { get; internal set; }
        [DataMember] public double PreparationMilliseconds { get; internal set; }
        // Summed worker intervals, not wall-clock phases; do not add to total time.
        [DataMember] public double CheckWorkerMilliseconds { get; internal set; }
        [DataMember] public double MergeWorkerMilliseconds { get; internal set; }
        [DataMember] public long Checks { get; internal set; }
        [DataMember] public long PrunedPairs { get; internal set; }
        [DataMember] public long OptimisticBetterPasses { get; internal set; }
        [DataMember] public long PrefixEqualPasses { get; internal set; }
        [DataMember] public long UnresolvedPasses { get; internal set; }
        [DataMember] public long SummaryObjects { get; internal set; }
        [DataMember] public long SummaryIntArrays { get; internal set; }
        [DataMember] public long SummaryIntCells { get; internal set; }
        // 0 = total required deficit; then five fields per original rule index:
        // reached, maximum rank, highest-rank count, distribution, effective count.
        [DataMember] public long[] PrunedByPrefixField { get; internal set; }
        [DataMember] public long[] OptimisticBetterByPrefixField { get; internal set; }
    }

    /// <summary>Selected creation sites only; these counts are not allocation bytes.</summary>
    [DataContract]
    public sealed class CustomSearchCreationCounts
    {
        [DataMember] public long Candidates { get; internal set; }
        [DataMember] public long GeneralStates { get; internal set; }
        [DataMember] public long SixSlotArrays { get; internal set; }
        [DataMember] public long TerminalWorkspaces { get; internal set; }
        [DataMember] public long TerminalRetainedSnapshots { get; internal set; }
        [DataMember] public long SemanticKeys { get; internal set; }
        [DataMember] public long TerminalKeys { get; internal set; }
        [DataMember] public long FinalCoProfileKeys { get; internal set; }
        [DataMember] public long FinalCoProfileCacheMisses { get; internal set; }
        [DataMember] public long ContributionArrays { get; internal set; }
        [DataMember] public long ContributionValueArrays { get; internal set; }
        [DataMember] public long ExclusiveOptionArrays { get; internal set; }
        [DataMember] public long UnknownWinnerArrays { get; internal set; }
        [DataMember] public long ExclusiveOptionBuilderBuffers { get; internal set; }
        [DataMember] public long UnknownWinnerBuilderBuffers { get; internal set; }
        // Probe operations are logical experiment counters, not allocations.
        [DataMember] public long DeferredProbeOperations { get; internal set; }
        [DataMember] public long DeferredStateMaterializations { get; internal set; }
        [DataMember] public long DeferredStateDiscards { get; internal set; }
        internal void Add(CustomSearchCreationCounts other)
        {
            Candidates += other.Candidates; GeneralStates += other.GeneralStates;
            SixSlotArrays += other.SixSlotArrays; SemanticKeys += other.SemanticKeys;
            TerminalWorkspaces += other.TerminalWorkspaces;
            TerminalRetainedSnapshots += other.TerminalRetainedSnapshots;
            TerminalKeys += other.TerminalKeys; FinalCoProfileKeys += other.FinalCoProfileKeys;
            FinalCoProfileCacheMisses += other.FinalCoProfileCacheMisses;
            ContributionArrays += other.ContributionArrays;
            ContributionValueArrays += other.ContributionValueArrays;
            ExclusiveOptionArrays += other.ExclusiveOptionArrays;
            UnknownWinnerArrays += other.UnknownWinnerArrays;
            ExclusiveOptionBuilderBuffers += other.ExclusiveOptionBuilderBuffers;
            UnknownWinnerBuilderBuffers += other.UnknownWinnerBuilderBuffers;
            DeferredProbeOperations += other.DeferredProbeOperations;
            DeferredStateMaterializations += other.DeferredStateMaterializations;
            DeferredStateDiscards += other.DeferredStateDiscards;
        }
    }
}
