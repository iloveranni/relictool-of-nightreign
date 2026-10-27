using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace NightreignRelicTool.Core
{
    public sealed partial class CustomEffectSearcher
    {
        // Internal, in-memory test observation only. Never part of diagnostic output.
        internal sealed class TerminalBoundAudit
        {
            public VesselDefinition Vessel;
            public RelicInstance[] Relics;
            public RuleComparisonProfile[] OptimisticProfiles, ReferenceProfiles;
            public CustomComparisonVector ReferenceComparison;
            public int MissingLowerBound, ReferenceMissing, Decision;
            public bool Resolved;
        }

        private sealed class TerminalBoundSummary
        {
            public readonly int[] Counts;
            public bool Resolved = true;
            public TerminalBoundSummary(int size, CustomSearchPruningDiagnostics diagnostics)
            {
                Counts = new int[size];
                if (diagnostics != null)
                {
                    diagnostics.SummaryObjects++;
                    diagnostics.SummaryIntArrays++;
                    diagnostics.SummaryIntCells += size;
                }
            }
        }

        private sealed partial class TerminalBoundSchema
        {
            private readonly PreparedRule[] _rules;
            private readonly int[] _order, _offsets;
            private readonly int[][] _ranks;
            private readonly Dictionary<int, int>[] _rankIndexes;
            private readonly int _size, _additiveSize, _singleEnd;
            private readonly Dictionary<string, int>[] _singleGroups;
            private readonly Dictionary<int, int>[] _exclusiveGroups;
            private readonly Dictionary<string, CappedBoundGroup>[] _cappedGroups;
            private readonly CappedBoundGroup[][] _capsByCell;
            private readonly int[] _cellCaps;
            private readonly int[][] _groupsByCell;
            private readonly int[] _cellRanks;

            private sealed class CappedBoundGroup
            {
                public int Cap;
                public int[] Cells, Ranks;
                public Dictionary<int, int> CellByRank;
            }

            public TerminalBoundSchema(PreparedRule[] rules, Candidate[] candidates, SearchBudget budget, CustomEffectCatalog catalog)
            {
                _rules = rules;
                _order = Enumerable.Range(0, rules.Length).Where(i => rules[i].Source.IsRequired)
                    .Concat(Enumerable.Range(0, rules.Length).Where(i => !rules[i].Source.IsRequired)).ToArray();
                _offsets = new int[rules.Length];
                _ranks = new int[rules.Length][];
                _rankIndexes = new Dictionary<int, int>[rules.Length];
                int size = rules.Length;
                for (int i = 0; i < rules.Length; i++)
                {
                    budget.Checkpoint();
                    int ruleIndex = i;
                    _ranks[i] = candidates.SelectMany(candidate => candidate.Occurrences)
                        .Where(value => value.RuleIndex == ruleIndex && value.IsApplicable && value.Rank > 0)
                        .Select(value => value.Rank).Distinct().OrderByDescending(value => value).ToArray();
                    _offsets[i] = size;
                    _rankIndexes[i] = new Dictionary<int, int>();
                    for (int rankIndex = 0; rankIndex < _ranks[i].Length; rankIndex++)
                        _rankIndexes[i].Add(_ranks[i][rankIndex], size++);
                }
                _additiveSize = size;
                _singleGroups = new Dictionary<string, int>[rules.Length];
                _exclusiveGroups = new Dictionary<int, int>[rules.Length];
                // Validate the whole catalog group, including effects which are not
                // selected by a rule (possible blockers). Unproved metadata keeps
                // its original physical bound. This does not select a winner.
                var confirmedExclusive = new HashSet<int>();
                foreach (var group in catalog.Effects.Where(e => e.ExclusivityId.HasValue)
                    .GroupBy(e => e.ExclusivityId.Value))
                {
                    budget.Checkpoint();
                    var policy = catalog.GetExclusivityPolicy(group.Key);
                    if (policy != null && policy.MaxEffectiveEffects == 1 && !policy.IsHardLegalityConstraint
                        && group.All(e => e.AggregationRule == "single_instance" && e.MaxEffectiveCopies == 1
                            && e.AggregationVerificationStatus == "project_verified"))
                        confirmedExclusive.Add(group.Key);
                }
                _groupsByCell = new int[size][];
                _cellRanks = new int[size];
                for (int rule = 0; rule < rules.Length; rule++)
                {
                    budget.Checkpoint();
                    var groups = _singleGroups[rule] = new Dictionary<string, int>(StringComparer.Ordinal);
                    int ruleIndex = rule;
                    foreach (var group in candidates.SelectMany(candidate => candidate.Occurrences)
                        .Where(value => value.RuleIndex == ruleIndex && value.IsApplicable)
                        .GroupBy(value => value.StackGroupKey, StringComparer.Ordinal))
                    {
                        budget.Checkpoint();
                        // Ordinary aggregation uses (rule, stack group).
                        // Any inconsistent declaration makes the entire group additive.
                        string aggregation = group.First().AggregationRule;
                        // Unknown ordinary aggregation also formally counts one item
                        // (the lowest rank). Its maximum possible rank is an upper
                        // bound, not a claim that the game's stacking is confirmed.
                        if (!string.IsNullOrEmpty(group.Key)
                            && (aggregation == "single_instance" || aggregation == "unknown")
                            && group.All(value => value.AggregationRule == aggregation && value.Rank > 0
                            && !value.ExclusivityId.HasValue
                            && (aggregation == "unknown" || !value.MaxEffectiveCopies.HasValue || value.MaxEffectiveCopies == 1))
                            && group.Select(value => value.MaxEffectiveCopies).Distinct().Count() == 1)
                            groups.Add(group.Key, size++);
                    }
                    var exclusive = _exclusiveGroups[rule] = new Dictionary<int, int>();
                    foreach (var group in candidates.SelectMany(candidate => candidate.Occurrences)
                        .Where(value => value.RuleIndex == ruleIndex && value.IsApplicable && value.ExclusivityId.HasValue)
                        .GroupBy(value => value.ExclusivityId.Value))
                    {
                        budget.Checkpoint();
                        if (confirmedExclusive.Contains(group.Key)
                            && group.All(value => value.Rank > 0 && value.AggregationRule == "single_instance"
                                && value.MaxEffectiveCopies == 1))
                            exclusive.Add(group.Key, size++);
                    }
                    // A separate cell per RULE INSTANCE and exclusive id is essential:
                    // one physical effect can contribute independently to rule aliases.
                    // All max-rank cells use the same associative max operation in
                    // selected states, remaining potentials, joins and block maxima.
                    int[] cells = groups.Values.Concat(exclusive.Values).ToArray();
                    _groupsByCell[rule] = cells;
                    for (int rank = 0; rank < _ranks[rule].Length; rank++)
                    {
                        _groupsByCell[_offsets[rule] + rank] = cells;
                        _cellRanks[_offsets[rule] + rank] = _ranks[rule][rank];
                    }
                }
                _singleEnd = size;
                _cappedGroups = new Dictionary<string, CappedBoundGroup>[rules.Length];
                _capsByCell = new CappedBoundGroup[_additiveSize][];
                var cellCaps = new List<int>(new int[size]);
                for (int rule = 0; rule < rules.Length; rule++)
                {
                    budget.Checkpoint();
                    int ruleIndex = rule;
                    var groups = _cappedGroups[rule] = new Dictionary<string, CappedBoundGroup>(StringComparer.Ordinal);
                    foreach (var group in candidates.SelectMany(candidate => candidate.Occurrences)
                        .Where(value => value.RuleIndex == ruleIndex && value.IsApplicable)
                        .GroupBy(value => value.StackGroupKey, StringComparer.Ordinal))
                    {
                        budget.Checkpoint();
                        int? cap = group.First().MaxEffectiveCopies;
                        if (string.IsNullOrEmpty(group.Key) || !cap.HasValue || cap <= 0 || cap > int.MaxValue / 2
                            || !group.All(value => value.AggregationRule == "cap_count" && value.Rank > 0
                                && !value.ExclusivityId.HasValue && value.MaxEffectiveCopies == cap)) continue;
                        var bounded = new CappedBoundGroup { Cap = cap.Value,
                            Ranks = group.Select(value => value.Rank).Distinct().OrderByDescending(value => value).ToArray(),
                            CellByRank = new Dictionary<int, int>() };
                        // If even the old six-slot potential cannot reach the cap,
                        // its additive cell already respects that cap and is cheaper.
                        // Restrict this proof to one rank: different rank-bin maxima
                        // in a future slot can otherwise coexist optimistically.
                        if (bounded.Ranks.Length == 1 && cap.Value >= 6)
                        {
                            bool canBind = false;
                            foreach (var candidate in candidates)
                            {
                                budget.Checkpoint();
                                int hits = candidate.Occurrences.Count(value => value.IsApplicable
                                    && value.RuleIndex == ruleIndex && value.StackGroupKey == group.Key);
                                if (6L * hits > cap.Value) { canBind = true; break; }
                            }
                            if (!canBind) continue;
                        }
                        bounded.Cells = new int[bounded.Ranks.Length];
                        for (int i = 0; i < bounded.Ranks.Length; i++)
                        {
                            bounded.Cells[i] = size;
                            bounded.CellByRank.Add(bounded.Ranks[i], size++);
                            cellCaps.Add(cap.Value);
                        }
                        groups.Add(group.Key, bounded);
                    }
                    var boundedGroups = groups.Values.ToArray();
                    _capsByCell[rule] = boundedGroups;
                    for (int i = 0; i < _ranks[rule].Length; i++) _capsByCell[_offsets[rule] + i] = boundedGroups;
                }
                _cellCaps = cellCaps.ToArray();
                _size = size;
            }

            public TerminalBoundSummary ForCandidate(Candidate candidate, CustomSearchPruningDiagnostics diagnostics)
            {
                TerminalBoundSummary result = new TerminalBoundSummary(_size, diagnostics);
                foreach (Occurrence occurrence in candidate.Occurrences)
                {
                    // Project and SyntheticConfirmed cannot activate inapplicable
                    // originals. All remaining physical/preset matches are retained.
                    if (!occurrence.IsApplicable) continue;
                    int offset;
                    if (occurrence.Rank <= 0 || !_rankIndexes[occurrence.RuleIndex].TryGetValue(occurrence.Rank, out offset)
                        || (occurrence.AggregationRule != "cap_count" && occurrence.AggregationRule != "single_instance"
                            && occurrence.AggregationRule != "unknown"))
                    {
                        result.Resolved = false;
                        continue;
                    }
                    int group;
                    if (occurrence.ExclusivityId.HasValue && _exclusiveGroups[occurrence.RuleIndex]
                        .TryGetValue(occurrence.ExclusivityId.Value, out group))
                    {
                        result.Counts[group] = Math.Max(result.Counts[group], occurrence.Rank);
                        continue; // Never also count this hit in the physical cells.
                    }
                    if (occurrence.StackGroupKey != null && _singleGroups[occurrence.RuleIndex]
                        .TryGetValue(occurrence.StackGroupKey, out group))
                    {
                        result.Counts[group] = Math.Max(result.Counts[group], occurrence.Rank);
                        continue;
                    }
                    CappedBoundGroup capped;
                    if (occurrence.StackGroupKey != null && _cappedGroups[occurrence.RuleIndex]
                        .TryGetValue(occurrence.StackGroupKey, out capped))
                    {
                        int cell = capped.CellByRank[occurrence.Rank];
                        result.Counts[cell] = Math.Min(capped.Cap, result.Counts[cell] + 1);
                        continue;
                    }
                    result.Counts[occurrence.RuleIndex]++;
                    result.Counts[offset]++;
                }
                return result;
            }

            public TerminalBoundSummary ForState(GeneralFrontierState state,
                Dictionary<Candidate, TerminalBoundSummary> candidates, CustomSearchPruningDiagnostics diagnostics)
            {
                TerminalBoundSummary result = new TerminalBoundSummary(_size, diagnostics);
                foreach (Candidate candidate in state.Selected)
                {
                    if (candidate == null) continue;
                    TerminalBoundSummary source = candidates[candidate];
                    result.Resolved &= source.Resolved;
                    for (int i = 0; i < _additiveSize; i++)
                    {
                        long count = (long)result.Counts[i] + source.Counts[i];
                        // Ensures even the sum of two summaries cannot overflow.
                        if (count > int.MaxValue / 2) result.Resolved = false;
                        else result.Counts[i] = (int)count;
                    }
                    for (int i = _additiveSize; i < _singleEnd; i++)
                        result.Counts[i] = Math.Max(result.Counts[i], source.Counts[i]);
                    for (int i = _singleEnd; i < _size; i++)
                        result.Counts[i] = Math.Min(_cellCaps[i], result.Counts[i] + source.Counts[i]);
                }
                return result;
            }

            public TerminalBoundSummary ForReference(CustomComparisonVector reference,
                CustomSearchPruningDiagnostics diagnostics)
            {
                TerminalBoundSummary result = new TerminalBoundSummary(_size, diagnostics);
                for (int i = 0; i < _rules.Length; i++)
                {
                    RuleComparisonProfile profile = reference.RuleComparisonProfiles[i];
                    result.Counts[i] = profile.EffectiveCount;
                    foreach (int rank in profile.CompleteVariantRankDistribution)
                    {
                        int offset;
                        if (rank <= 0 || !_rankIndexes[i].TryGetValue(rank, out offset)) result.Resolved = false;
                        else result.Counts[offset]++;
                    }
                }
                return result;
            }

            private int Count(TerminalBoundSummary left, TerminalBoundSummary right, int index)
            {
                int count = left.Counts[index] + (right == null ? 0 : right.Counts[index]);
                foreach (int group in _groupsByCell[index])
                {
                    int rank = right == null ? left.Counts[group] : Math.Max(left.Counts[group], right.Counts[group]);
                    if (rank > 0 && (_cellRanks[index] == 0 || rank == _cellRanks[index])) count++;
                }
                // Each capped group contributes its top K rank multiset. Bin counts
                // are saturated independently while combining summaries; the shared
                // capacity is applied here in descending rank order, so count, highest
                // rank and the complete distribution describe the same optimistic set.
                foreach (var group in _capsByCell[index])
                {
                    int remaining = group.Cap, wantedRank = _cellRanks[index];
                    for (int i = 0; i < group.Cells.Length && remaining > 0; i++)
                    {
                        if (wantedRank > group.Ranks[i]) break;
                        int cell = group.Cells[i];
                        int take = Math.Min(remaining, left.Counts[cell] + (right == null ? 0 : right.Counts[cell]));
                        remaining -= take;
                        if (wantedRank == 0 || wantedRank == group.Ranks[i]) count += take;
                    }
                }
                return count;
            }

            public int Missing(TerminalBoundSummary left, TerminalBoundSummary right)
            {
                int missing = 0;
                for (int i = 0; i < _rules.Length; i++)
                    if (_rules[i].Source.IsRequired)
                        missing += Math.Max(0, _rules[i].Source.QuantityTarget - Count(left, right, i));
                return missing;
            }

            // A negative comparison is BETTER, just like CustomComparisonVector.
            // int.MinValue is unresolved and must pass without inspecting suffixes.
            public int Compare(TerminalBoundSummary left, TerminalBoundSummary right,
                TerminalBoundSummary reference, out int field)
            {
                field = 0;
                if (!left.Resolved || (right != null && !right.Resolved) || !reference.Resolved) return int.MinValue;
                int value = Missing(left, right).CompareTo(Missing(reference, null));
                if (value != 0) return value;
                foreach (int ruleIndex in _order)
                {
                    CustomEffectRule rule = _rules[ruleIndex].Source;
                    int count = Count(left, right, ruleIndex), referenceCount = Count(reference, null, ruleIndex);
                    field = 1 + ruleIndex * 5;
                    if (rule.IsRequired)
                    {
                        value = (referenceCount >= rule.QuantityTarget).CompareTo(count >= rule.QuantityTarget);
                        if (value != 0) return value;
                    }
                    int offset = _offsets[ruleIndex], length = _ranks[ruleIndex].Length;
                    int highest = -1, referenceHighest = -1;
                    for (int i = 0; i < length; i++)
                    {
                        if (highest < 0 && Count(left, right, offset + i) != 0) highest = i;
                        if (referenceHighest < 0 && Count(reference, null, offset + i) != 0) referenceHighest = i;
                        if (highest >= 0 && referenceHighest >= 0) break;
                    }
                    int rank = highest < 0 ? 0 : _ranks[ruleIndex][highest];
                    int referenceRank = referenceHighest < 0 ? 0 : _ranks[ruleIndex][referenceHighest];
                    field++;
                    value = referenceRank.CompareTo(rank);
                    if (value != 0) return value;
                    field++;
                    value = (referenceHighest < 0 ? 0 : Count(reference, null, offset + referenceHighest))
                        .CompareTo(highest < 0 ? 0 : Count(left, right, offset + highest));
                    if (value != 0) return value;
                    field++;
                    // At positive, descending ranks, the first differing bin has
                    // exactly the ordering of the expanded descending rank lists.
                    for (int i = 0; i < length; i++)
                    {
                        value = Count(reference, null, offset + i).CompareTo(Count(left, right, offset + i));
                        if (value != 0) return value;
                    }
                    field++;
                    value = referenceCount.CompareTo(count);
                    if (value != 0) return value;
                }
                return 0; // Co-location, negatives, uncertainty and IDs never prune.
            }

            public RuleComparisonProfile[] AuditProfiles(TerminalBoundSummary left, TerminalBoundSummary right)
            {
                RuleComparisonProfile[] profiles = new RuleComparisonProfile[_rules.Length];
                for (int i = 0; i < _rules.Length; i++)
                {
                    List<int> ranks = new List<int>();
                    for (int j = 0; j < _ranks[i].Length; j++)
                        for (int n = Count(left, right, _offsets[i] + j); n > 0; n--) ranks.Add(_ranks[i][j]);
                    int maximum = ranks.Count == 0 ? 0 : ranks[0];
                    CustomEffectRule rule = _rules[i].Source;
                    int count = Count(left, right, i);
                    profiles[i] = new RuleComparisonProfile(rule.RuleId, rule.SelectorId, rule.IsRequired,
                        rule.QuantityTarget, !rule.IsRequired || count >= rule.QuantityTarget, maximum,
                        maximum == 0 ? 0 : ranks.Count(rank => rank == maximum), ranks.ToArray(), count, count, 0);
                }
                return profiles;
            }
        }

        private sealed class TerminalPruningContext
        {
            public TerminalBoundSchema Schema;
            public TerminalBoundSummary[] Aggregate, Local;
            public TerminalBoundSummary[] AggregateBlocks;
            public TerminalBoundSummary Reference;
            public TerminalBoundSummary GlobalReference;
            public CustomComparisonVector ReferenceVector;
        }

        private sealed class TerminalPruningWork
        {
            public long Checks, Pruned, Better, Equal, Unresolved;
            public long BlockChecks, BlockPrunedPairs;
            public readonly long[] Reasons, BetterReasons;
            public readonly CustomSearchCostDiagnostics Cost;
            public TerminalPruningWork(int ruleCount, bool detailedCost = false)
            {
                Reasons = new long[1 + ruleCount * 5];
                BetterReasons = new long[Reasons.Length];
                if (detailedCost) Cost = new CustomSearchCostDiagnostics();
            }
        }

        private sealed partial class FrontierVesselSearch
        {
            private TerminalPruningContext PrepareTerminalPruning(GeneralFrontierState[] aggregate, GeneralFrontierState[] local)
            {
                if (aggregate.Length == 0 || local.Length == 0) return null;
                CustomSearchPruningDiagnostics diagnostics = _diagnostics == null ? null : new CustomSearchPruningDiagnostics
                { PrunedByPrefixField = new long[1 + _rules.Length * 5],
                    OptimisticBetterByPrefixField = new long[1 + _rules.Length * 5] };
                if (diagnostics != null)
                {
                    _phaseDiagnostics.Merge.Pruning = diagnostics;
                    _budget.Diagnostics.ActiveOperation = "TerminalBoundPreparation";
                }
                long preparationStart = diagnostics == null ? 0 : Stopwatch.GetTimestamp();
                TerminalPruningContext context = new TerminalPruningContext();
                int[] aggregateSeeds, localSeeds;
                try
                {
                    _budget.Checkpoint();
                    context.Schema = new TerminalBoundSchema(_rules, _candidates, _budget, _owner._catalog);
                    Dictionary<Candidate, TerminalBoundSummary> summaries = new Dictionary<Candidate, TerminalBoundSummary>();
                    foreach (Candidate candidate in _candidates)
                    {
                        _budget.Checkpoint();
                        summaries.Add(candidate, context.Schema.ForCandidate(candidate, diagnostics));
                    }
                    context.Aggregate = BuildTerminalSummaries(aggregate, context.Schema, summaries, diagnostics);
                    context.Local = BuildTerminalSummaries(local, context.Schema, summaries, diagnostics);
                    if (_budget.TerminalBlockPruningEnabled && aggregate.Length >= TerminalBlockSize)
                        context.AggregateBlocks = BuildTerminalBlocks(context.Schema, context.Aggregate, diagnostics);
                    aggregateSeeds = ReferenceIndexes(context.Aggregate, context.Schema);
                    localSeeds = ReferenceIndexes(context.Local, context.Schema);
                }
                finally
                {
                    if (diagnostics != null) diagnostics.PreparationMilliseconds = CustomSearchDiagnostics.MillisecondsSince(preparationStart);
                }

                if (diagnostics != null) _budget.Diagnostics.ActiveOperation = "TerminalReference";
                long referenceStart = diagnostics == null ? 0 : Stopwatch.GetTimestamp();
                long transitionStart = _budget.TransitionCount;
                bool referenceCompleted = false;
                try
                {
                    GeneralFrontierState workspace = GeneralFrontierState.CreateTerminalWorkspace(
                        _diagnostics == null ? null : _diagnostics.Creations);
                    // At most 4 x 4 real pairs. These probes never restrict the
                    // formal pair traversal and never become an unproved result.
                    foreach (int localIndex in localSeeds)
                    {
                        int[] slots = SelectedSlotsByInstanceId(local[localIndex]);
                        foreach (int aggregateIndex in aggregateSeeds)
                        {
                            _budget.Checkpoint();
                            if (diagnostics != null) diagnostics.ReferenceAttempts++;
                            workspace.ResetTerminalWorkspace(aggregate[aggregateIndex]);
                            foreach (int slot in slots)
                            {
                                _budget.RecordTransitionAndState();
                                workspace.Add(local[localIndex].Selected[slot], slot, _rules, null, false,
                                    _diagnostics == null ? null : _diagnostics.Creations, true);
                            }
                            if (!IsLegalTerminalReference(workspace.Selected)) continue;
                            CustomComparisonVector vector = _owner.EvaluateCandidatesVector(
                                _vessel, workspace.Selected, _characterId, _rules, _budget);
                            if (diagnostics != null) diagnostics.ReferenceEvaluations++;
                            if (context.ReferenceVector == null || CustomComparisonVector.Compare(vector, context.ReferenceVector) < 0)
                                context.ReferenceVector = vector;
                        }
                    }
                    // First use the existing <=16 full, legal table probes. A
                    // <=193-pair join does not warrant a separate <=193-evaluation
                    // builder. Nor can that builder improve pruning if the probes
                    // already attain the complete table's optimistic prefix.
                    if (_budget.DepthSeparatedPhasesEnabled && _budget.IntermediatePruningEnabled
                        && !_intermediatePrepared && EarlyReferenceCanStrengthen(context, diagnostics))
                    {
                        _intermediatePrepared = true;
                        _intermediate = PrepareIntermediateVessel();
                    }
                    if (_budget.DepthSeparatedPhasesEnabled && _intermediate != null
                        && (context.ReferenceVector == null
                            || CustomComparisonVector.Compare(_intermediate.ReferenceVector, context.ReferenceVector) < 0))
                        context.ReferenceVector = _intermediate.ReferenceVector;
                    referenceCompleted = true;
                    if (context.ReferenceVector == null) return null;
                    context.Reference = context.Schema.ForReference(context.ReferenceVector, diagnostics);
                    if (!context.Reference.Resolved) return null;
                    context.GlobalReference = GlobalJoinReference(context.Schema, context.Reference);
                    if (diagnostics != null)
                    {
                        diagnostics.ReferenceFound = true;
                        diagnostics.ReferenceMissingRequired = context.ReferenceVector.MissingRequired;
                    }
                    return context;
                }
                finally
                {
                    if (diagnostics != null)
                    {
                        diagnostics.ReferenceMilliseconds = CustomSearchDiagnostics.MillisecondsSince(referenceStart);
                        diagnostics.ReferenceTransitions = _budget.TransitionCount - transitionStart;
                        if (referenceCompleted) _budget.Diagnostics.ActiveOperation = "TerminalJoin";
                    }
                }
            }

            private TerminalBoundSummary[] BuildTerminalSummaries(GeneralFrontierState[] states, TerminalBoundSchema schema,
                Dictionary<Candidate, TerminalBoundSummary> candidates, CustomSearchPruningDiagnostics diagnostics)
            {
                TerminalBoundSummary[] result = new TerminalBoundSummary[states.Length];
                for (int i = 0; i < states.Length; i++)
                {
                    if ((i & 255) == 0) _budget.Checkpoint();
                    result[i] = schema.ForState(states[i], candidates, diagnostics);
                }
                return result;
            }

            private int[] ReferenceIndexes(TerminalBoundSummary[] summaries, TerminalBoundSchema schema)
            {
                int best = 0;
                for (int i = 1; i < summaries.Length; i++)
                {
                    if ((i & 255) == 0) _budget.Checkpoint();
                    int field;
                    if (summaries[i].Resolved && (!summaries[best].Resolved
                        || schema.Compare(summaries[i], null, summaries[best], out field) < 0)) best = i;
                }
                return new[] { best, 0, summaries.Length / 2, summaries.Length - 1 }.Distinct().ToArray();
            }

            private bool IsLegalTerminalReference(Candidate[] selected)
            {
                for (int slot = 0; slot < 6; slot++)
                {
                    Candidate candidate = selected[slot];
                    if (candidate == null || candidate.Relic.IsDeep != (slot >= 3)) return false;
                    int color = slot < 3 ? _vessel.OrdinarySlotColorIds[slot] : _vessel.DeepSlotColorIds[slot - 3];
                    if (!AcceptsColor(color, candidate.Relic.ColorId)) return false;
                    for (int prior = 0; prior < slot; prior++)
                        if (selected[prior].Relic.InstanceId == candidate.Relic.InstanceId) return false;
                }
                return true; // Original phase assignments and slot normalization are unchanged.
            }

            private bool CheckTerminalBound(TerminalPruningContext context, int aggregateIndex, int localIndex,
                GeneralFrontierState aggregate, GeneralFrontierState local, TerminalPruningWork diagnostics)
            {
                bool sample = diagnostics != null && diagnostics.Cost != null
                    && (diagnostics.Checks & 4095L) == 0;
                long started = sample ? Stopwatch.GetTimestamp() : 0;
                int field;
                int decision = context.Schema.Compare(context.Aggregate[aggregateIndex], context.Local[localIndex],
                    context.GlobalReference ?? context.Reference, out field);
                var auditReference = context.GlobalReference == null ? context.ReferenceVector : _globalWitnesses[2].Comparison;
                if (context.GlobalReference != null)
                    ObserveGlobalBound("Pair", context.Schema, context.Aggregate[aggregateIndex], context.Local[localIndex], decision,
                        _budget.GlobalBoundAudit == null ? null : AuditPairCandidates(aggregate, local));
                if (diagnostics != null)
                {
                    diagnostics.Checks++;
                    if (decision > 0) { diagnostics.Pruned++; diagnostics.Reasons[field]++; }
                    else if (decision == int.MinValue) diagnostics.Unresolved++;
                    else if (decision == 0) diagnostics.Equal++;
                    else { diagnostics.Better++; diagnostics.BetterReasons[field]++; }
                    if (diagnostics.Cost != null)
                    {
                        bool pruned = decision > 0;
                        if (pruned) diagnostics.Cost.BoundPrunedOperations++;
                        else diagnostics.Cost.BoundPassedOperations++;
                        if (sample)
                        {
                            double elapsed = 1000d * (Stopwatch.GetTimestamp() - started) / Stopwatch.Frequency;
                            if (pruned)
                            {
                                diagnostics.Cost.BoundPrunedSamples++;
                                diagnostics.Cost.BoundPrunedSampleMilliseconds += elapsed;
                            }
                            else
                            {
                                diagnostics.Cost.BoundPassedSamples++;
                                diagnostics.Cost.BoundPassedSampleMilliseconds += elapsed;
                            }
                        }
                    }
                }
                if (_budget.TerminalBoundAudit != null)
                    AuditTerminalPair(context, aggregateIndex, localIndex, aggregate, local, decision, auditReference);
                return decision > 0;
            }

            // Keep captured audit parameters out of the hot callers: a lambda
            // inside an audit guard can still allocate its closure at method entry.
            private static Candidate[] AuditPairCandidates(GeneralFrontierState aggregate, GeneralFrontierState local)
            {
                return Enumerable.Range(0, 6).Select(slot => aggregate.Selected[slot] ?? local.Selected[slot]).ToArray();
            }

            private void AuditTerminalPair(TerminalPruningContext context, int aggregateIndex, int localIndex,
                GeneralFrontierState aggregate, GeneralFrontierState local, int decision, CustomComparisonVector auditReference)
            {
                bool resolved = decision != int.MinValue;
                _budget.TerminalBoundAudit(new TerminalBoundAudit
                {
                    Vessel = _vessel,
                    Relics = Enumerable.Range(0, 6).Select(slot => (aggregate.Selected[slot] ?? local.Selected[slot]).Relic).ToArray(),
                    Resolved = resolved, Decision = decision,
                    MissingLowerBound = resolved ? context.Schema.Missing(context.Aggregate[aggregateIndex], context.Local[localIndex]) : 0,
                    OptimisticProfiles = resolved ? context.Schema.AuditProfiles(context.Aggregate[aggregateIndex], context.Local[localIndex]) : null,
                    ReferenceMissing = auditReference.MissingRequired,
                    ReferenceComparison = auditReference,
                    ReferenceProfiles = auditReference.RuleComparisonProfiles
                });
            }

            private void AccumulatePruningWork(TerminalPruningWork work)
            {
                CustomSearchPruningDiagnostics result = _phaseDiagnostics.Merge.Pruning;
                result.Checks += work.Checks;
                result.BlockChecks += work.BlockChecks;
                result.BlockPrunedPairs += work.BlockPrunedPairs;
                result.PrunedPairs += work.Pruned;
                result.OptimisticBetterPasses += work.Better;
                result.PrefixEqualPasses += work.Equal;
                result.UnresolvedPasses += work.Unresolved;
                if (work.Cost != null && _phaseDiagnostics.Merge.Cost != null)
                    _phaseDiagnostics.Merge.Cost.Add(work.Cost);
                for (int i = 0; i < work.Reasons.Length; i++)
                {
                    result.PrunedByPrefixField[i] += work.Reasons[i];
                    result.OptimisticBetterByPrefixField[i] += work.BetterReasons[i];
                }
            }
        }
    }
}
