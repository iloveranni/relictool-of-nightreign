using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace NightreignRelicTool.Core
{
    public sealed partial class CustomEffectSearcher
    {
        // Test-only, synchronous observation. Never serialized or retained by the search.
        internal sealed class IntermediateBoundAudit
        {
            public VesselDefinition Vessel;
            public RelicInstance[] Selected;
            public int AggregateMask, LocalMask, RemainingMask, Decision, MissingLowerBound;
            public bool Resolved;
            public RuleComparisonProfile[] OptimisticProfiles;
            public CustomComparisonVector ReferenceComparison;
        }

        private sealed partial class TerminalBoundSchema
        {
            public TerminalBoundSummary Plus(TerminalBoundSummary left, TerminalBoundSummary right,
                CustomSearchPruningDiagnostics diagnostics)
            {
                var result = new TerminalBoundSummary(_size, diagnostics);
                result.Resolved = left.Resolved && right.Resolved;
                for (int i = 0; i < _additiveSize; i++)
                {
                    long count = (long)left.Counts[i] + right.Counts[i];
                    if (count > int.MaxValue / 2) result.Resolved = false;
                    else result.Counts[i] = (int)count;
                }
                // A formally single-count group contributes at most one item. The highest
                // possible rank dominates every selected/future alternative, even
                // when slot identity or competition makes that alternative unreachable.
                for (int i = _additiveSize; i < _singleEnd; i++)
                    result.Counts[i] = Math.Max(left.Counts[i], right.Counts[i]);
                // Saturating each rank bin preserves every possible top-K result.
                // Slot alternatives may coexist in this summary, deliberately
                // overestimating reachability; Count applies each group's capacity.
                for (int i = _singleEnd; i < _size; i++)
                    result.Counts[i] = Math.Min(_cellCaps[i], left.Counts[i] + right.Counts[i]);
                return result;
            }

            public TerminalBoundSummary[] RemainingPotentials(Candidate[][] legal,
                Dictionary<Candidate, TerminalBoundSummary> candidates, SearchBudget budget,
                CustomSearchPruningDiagnostics diagnostics)
            {
                var slots = new TerminalBoundSummary[6];
                for (int slot = 0; slot < 6; slot++)
                {
                    var upper = slots[slot] = new TerminalBoundSummary(_size, diagnostics);
                    if (legal[slot].Length == 0) upper.Resolved = false;
                    foreach (Candidate candidate in legal[slot])
                    {
                        budget.Checkpoint();
                        var source = candidates[candidate];
                        upper.Resolved &= source.Resolved; // Unknown FUTURE options cannot disappear.
                        for (int i = _rules.Length; i < _size; i++)
                            upper.Counts[i] = Math.Max(upper.Counts[i], source.Counts[i]);
                    }
                    // Different bins may come from different stones. Sum the bin maxima,
                    // not the maximum total of any one stone: this is deliberately optimistic.
                    for (int rule = 0; rule < _rules.Length; rule++)
                    {
                        long count = 0;
                        for (int i = 0; i < _ranks[rule].Length; i++) count += upper.Counts[_offsets[rule] + i];
                        if (count > int.MaxValue / 2) upper.Resolved = false;
                        else upper.Counts[rule] = (int)count;
                    }
                }
                var result = new TerminalBoundSummary[64];
                result[0] = new TerminalBoundSummary(_size, diagnostics);
                for (int mask = 1; mask < 64; mask++)
                {
                    budget.Checkpoint();
                    int bit = mask & -mask, slot = 0;
                    while ((1 << slot) != bit) slot++;
                    result[mask] = Plus(result[mask ^ bit], slots[slot], diagnostics);
                }
                return result;
            }
        }

        private sealed class IntermediateVesselContext
        {
            public TerminalBoundSchema Schema;
            public Dictionary<Candidate, TerminalBoundSummary> Candidates;
            public TerminalBoundSummary[] Remaining;
            public TerminalBoundSummary Reference;
            public TerminalBoundSummary GlobalReference;
            public CustomComparisonVector ReferenceVector;
            public CustomComparisonVector GreedyVector;
            public Candidate[] ReferenceSelected;
            public Candidate[][] Ordered;
            public HashSet<Candidate>[] Attempted;
            public int[] SlotOrder;
            public ReferenceGuidanceDiagnostics Guidance;
            public bool AdaptiveChecked;
        }

        private sealed class IntermediateMergeContext
        {
            public IntermediateVesselContext Vessel;
            public Dictionary<GeneralFrontierState, TerminalBoundSummary> Aggregate, Local;
        }

        private sealed partial class FrontierVesselSearch
        {
            private bool _intermediatePrepared;
            private IntermediateVesselContext _intermediate;

            private IntermediateMergeContext PrepareIntermediateMerge(
                Dictionary<SemanticStateKey, GeneralFrontierState> aggregate,
                Dictionary<SemanticStateKey, GeneralFrontierState> local, int combinedMask)
            {
                var diagnostics = _diagnostics == null ? null : new CustomSearchPruningDiagnostics
                    { PrunedByPrefixField = new long[1 + _rules.Length * 5],
                        OptimisticBetterByPrefixField = new long[1 + _rules.Length * 5] };
                if (diagnostics != null) _phaseDiagnostics.Merge.Pruning = diagnostics;
                if (!_intermediatePrepared)
                {
                    _intermediatePrepared = true;
                    long transitions = _budget.TransitionCount;
                    try { _intermediate = PrepareIntermediateVessel(); }
                    finally
                    {
                        // The early reference is charged once, inside this first merge.
                        // Its evaluation/attempt statistics live only on the vessel.
                        if (diagnostics != null) diagnostics.ReferenceTransitions = _budget.TransitionCount - transitions;
                    }
                }
                if (_intermediate == null)
                {
                    if (diagnostics != null) _budget.Diagnostics.ActiveOperation = "SemanticJoin";
                    return null;
                }
                long estimatedPairs = aggregate.Count == 0 || local.Count == 0 ? 0
                    : aggregate.Count > long.MaxValue / local.Count ? long.MaxValue
                    : (long)aggregate.Count * local.Count;
                MaybeRunAdditionalGapRepair(_intermediate, estimatedPairs, diagnostics);
                if (diagnostics != null)
                {
                    diagnostics.ReferenceFound = true;
                    _budget.Diagnostics.ActiveOperation = "IntermediateBoundPreparation";
                }
                long start = diagnostics == null ? 0 : Stopwatch.GetTimestamp();
                try
                {
                    var result = new IntermediateMergeContext { Vessel = _intermediate,
                        Aggregate = new Dictionary<GeneralFrontierState, TerminalBoundSummary>(),
                        Local = new Dictionary<GeneralFrontierState, TerminalBoundSummary>() };
                    // Attach future potential ONCE per aggregate state, not once per pair.
                    foreach (var state in aggregate.Values)
                    {
                        _budget.Checkpoint();
                        var selected = _intermediate.Schema.ForState(state, _intermediate.Candidates, diagnostics);
                        result.Aggregate.Add(state, _intermediate.Schema.Plus(selected,
                            _intermediate.Remaining[CompleteMask ^ combinedMask], diagnostics));
                    }
                    foreach (var state in local.Values)
                    {
                        _budget.Checkpoint();
                        result.Local.Add(state, _intermediate.Schema.ForState(state, _intermediate.Candidates, diagnostics));
                    }
                    if (diagnostics != null) _budget.Diagnostics.ActiveOperation = "SemanticJoin";
                    return result;
                }
                finally
                {
                    if (diagnostics != null) diagnostics.PreparationMilliseconds = CustomSearchDiagnostics.MillisecondsSince(start);
                }
            }

            private IntermediateVesselContext PrepareIntermediateVessel()
            {
                var diagnostics = _diagnostics == null ? null : new CustomSearchPruningDiagnostics();
                if (diagnostics != null)
                {
                    _diagnostics.IntermediatePreparation = diagnostics;
                    _budget.Diagnostics.ActiveOperation = "IntermediatePotentialPreparation";
                }
                long start = diagnostics == null ? 0 : Stopwatch.GetTimestamp();
                var context = new IntermediateVesselContext();
                Candidate[][] legal = new Candidate[6][];
                try
                {
                    context.Schema = new TerminalBoundSchema(_rules, _candidates, _budget, _owner._catalog);
                    context.Candidates = new Dictionary<Candidate, TerminalBoundSummary>();
                    foreach (Candidate candidate in _candidates)
                    {
                        _budget.Checkpoint();
                        context.Candidates.Add(candidate, context.Schema.ForCandidate(candidate, diagnostics));
                    }
                    for (int slot = 0; slot < 6; slot++)
                    {
                        int index = slot;
                        legal[slot] = _candidates.Where(candidate => AcceptsSlot(candidate, index)).ToArray();
                    }
                    context.Remaining = context.Schema.RemainingPotentials(legal, context.Candidates, _budget, diagnostics);
                }
                finally
                {
                    if (diagnostics != null) diagnostics.PreparationMilliseconds = CustomSearchDiagnostics.MillisecondsSince(start);
                }
                if (diagnostics != null) _budget.Diagnostics.ActiveOperation = "IntermediateReference";
                if (_budget.OracleQuery != null)
                    return PrepareOracleReference(context, legal, diagnostics);
                ReferenceGuidanceDiagnostics guidance = _budget.GuidanceTest == null ? null
                    : new ReferenceGuidanceDiagnostics { Mode = _budget.GuidanceTest.Mode };
                context.Guidance = guidance;
                if (_diagnostics != null) _diagnostics.ReferenceGuidance = guidance;
                start = diagnostics == null ? 0 : Stopwatch.GetTimestamp();
                long transitions = _budget.TransitionCount;
                try
                {
                    long builderStart = guidance == null ? 0 : Stopwatch.GetTimestamp();
                    context.ReferenceVector = FindEarlyReference(legal, context, diagnostics);
                    if (guidance != null)
                        guidance.ColdBuilderMilliseconds = CustomSearchDiagnostics.MillisecondsSince(builderStart);
                    if (context.ReferenceVector == null) return null;
                    ApplyPreviousHint(context, guidance, diagnostics);
                    context.Reference = context.Schema.ForReference(context.ReferenceVector, diagnostics);
                    if (!context.Reference.Resolved) return null;
                    context.GlobalReference = GlobalJoinReference(context.Schema, context.Reference);
                    if (diagnostics != null)
                    {
                        diagnostics.ReferenceFound = true;
                        diagnostics.ReferenceMissingRequired = context.ReferenceVector.MissingRequired;
                        if (guidance != null)
                        {
                            guidance.FinalMissingRequired = context.ReferenceVector.MissingRequired;
                            int field;
                            var before = context.Schema.ForReference(context.GreedyVector, diagnostics);
                            int comparison = context.Schema.Compare(before, null, context.Reference, out field);
                            if (comparison != int.MinValue)
                                guidance.GreedyToFinalPrefixComparison = Math.Sign(comparison);
                        }
                    }
                    return context;
                }
                finally
                {
                    if (diagnostics != null)
                    {
                        diagnostics.ReferenceMilliseconds = CustomSearchDiagnostics.MillisecondsSince(start);
                        diagnostics.ReferenceTransitions = _budget.TransitionCount - transitions;
                    }
                }
            }

            private CustomComparisonVector FindEarlyReference(Candidate[][] legal, IntermediateVesselContext context,
                CustomSearchPruningDiagnostics diagnostics)
            {
                // Only the bounded reference heuristic uses this order. All legal
                // candidates and every formal traversal keep their original order.
                Candidate[][] ordered = new Candidate[6][];
                for (int slot = 0; slot < 6; slot++)
                {
                    _budget.Checkpoint();
                    ordered[slot] = (Candidate[])legal[slot].Clone();
                    Array.Sort(ordered[slot], (left, right) =>
                    {
                        var a = context.Candidates[left]; var b = context.Candidates[right];
                        if (a.Resolved != b.Resolved) return a.Resolved ? -1 : 1;
                        int field;
                        int value = a.Resolved && b.Resolved ? context.Schema.Compare(a, null, b, out field) : 0;
                        return value != 0 ? value : left.Relic.InstanceId.CompareTo(right.Relic.InstanceId);
                    });
                }
                int[] slots = Enumerable.Range(0, 6).OrderBy(slot => legal[slot].Length).ThenBy(slot => slot).ToArray();
                TraceOracleReference("Ordering", -1, -1, 0, null, null, null, ordered, false);
                Candidate[] selected = ReferenceArray(null, diagnostics);
                foreach (int slot in slots)
                {
                    Candidate candidate = ordered[slot].FirstOrDefault(value => !selected.Any(taken =>
                        taken != null && taken.Relic.InstanceId == value.Relic.InstanceId));
                    if (candidate == null) return null; // No unbounded matching/backtracking.
                    _budget.RecordTransitionAndState();
                    selected[slot] = candidate;
                }
                NormalizeReference(selected);
                if (!IsLegalTerminalReference(selected)) return null;
                CustomComparisonVector best = EvaluateEarlyReference(selected, diagnostics);
                context.GreedyVector = best;
                if (context.Guidance != null) context.Guidance.GreedyMissingRequired = best.MissingRequired;
                TraceOracleReference("Greedy", -1, -1, 0, selected, best, null, null, false);
                context.Ordered = ordered;
                context.SlotOrder = slots;
                context.Attempted = Enumerable.Range(0, 6).Select(slot => new HashSet<Candidate>()).ToArray();
                // At most 1 + 2*6*16 = 193 full evaluations per vessel. Proposals
                // never truncate the formal candidate set or seed the result table.
                for (int pass = 0;
                    pass < StableExactV1Configuration.ReferencePassCount;
                    pass++)
                {
                    foreach (int slot in slots)
                    {
                        int attempts = 0;
                        HashSet<Candidate> attemptedThisPass = new HashSet<Candidate>();
                        Candidate[] passOrder = _budget.RequiredGapReferenceOrderingEnabled
                            ? OrderForCurrentGaps(ordered[slot], best) : ordered[slot];
                        int cursor = 0;
                        while (attempts < StableExactV1Configuration.ReferenceAttemptsPerSlot)
                        {
                            Candidate candidate = null;
                            while (cursor < passOrder.Length)
                            {
                                Candidate next = passOrder[cursor++];
                                if (!attemptedThisPass.Contains(next)
                                    && !selected.Any(taken => taken.Relic.InstanceId == next.Relic.InstanceId))
                                { candidate = next; break; }
                            }
                            if (candidate == null) break;
                            _budget.Checkpoint();
                            attemptedThisPass.Add(candidate); attempts++;
                            context.Attempted[slot].Add(candidate);
                            bool guided = best.MissingRequired > 0 && HasCurrentGapPotential(candidate, best)
                                && _budget.RequiredGapReferenceOrderingEnabled;
                            if (guided && context.Guidance != null) context.Guidance.GuidedScans++;
                            _budget.RecordTransitionAndState(); // One actual proposed replacement/state.
                            Candidate[] proposal = ReferenceArray(selected, diagnostics);
                            proposal[slot] = candidate;
                            NormalizeReference(proposal);
                            if (!IsLegalTerminalReference(proposal)) continue;
                            var vector = EvaluateEarlyReference(proposal, diagnostics);
                            if (guided && context.Guidance != null) context.Guidance.GuidedEvaluations++;
                            TraceOracleReference("Proposal", pass, slot, attempts, proposal, vector, best, null,
                                false);
                            if (CustomComparisonVector.Compare(vector, best) < 0)
                            {
                                best = vector; selected = proposal;
                                if (diagnostics != null) diagnostics.ReferenceImprovements++;
                                if (guided && context.Guidance != null) context.Guidance.GuidedAccepted++;
                                if (_budget.RequiredGapReferenceOrderingEnabled)
                                { passOrder = OrderForCurrentGaps(ordered[slot], best); cursor = 0; }
                            }
                        }
                    }
                    TraceOracleReference("PassEnd", pass, -1, 0, selected, best, null, null, false);
                }
                context.ReferenceSelected = selected;
                return best; // Independent evaluator-owned immutable snapshot.
            }

            private Candidate[] ReferenceArray(Candidate[] source, CustomSearchPruningDiagnostics diagnostics)
            {
                if (diagnostics != null) { diagnostics.ReferenceProposalArrays++; _diagnostics.Creations.SixSlotArrays++; }
                return source == null ? new Candidate[6] : (Candidate[])source.Clone();
            }

            private void RecordIntermediateReferenceQuality()
            {
                int field;
                var diagnostics = _diagnostics.IntermediatePreparation;
                var best = _intermediate.Schema.ForReference(_bestVector, diagnostics);
                int value = _intermediate.Schema.Compare(_intermediate.Reference, null, best, out field);
                if (value != int.MinValue) diagnostics.ReferencePrefixComparisonToBest = Math.Sign(value);
            }

            private CustomComparisonVector EvaluateEarlyReference(Candidate[] selected, CustomSearchPruningDiagnostics diagnostics)
            {
                if (diagnostics != null) diagnostics.ReferenceAttempts++;
                var result = _owner.EvaluateCandidatesVector(_vessel, selected, _characterId, _rules, _budget);
                if (diagnostics != null) diagnostics.ReferenceEvaluations++;
                return result;
            }

            private void NormalizeReference(Candidate[] selected)
            {
                // Same-type, same-color slots are the existing symmetry class.
                for (int i = 0; i < 6; i++)
                    for (int j = i + 1; j < (i < 3 ? 3 : 6); j++)
                    {
                        int a = i < 3 ? _vessel.OrdinarySlotColorIds[i] : _vessel.DeepSlotColorIds[i - 3];
                        int b = j < 3 ? _vessel.OrdinarySlotColorIds[j] : _vessel.DeepSlotColorIds[j - 3];
                        if (a == b && selected[i].Relic.InstanceId > selected[j].Relic.InstanceId)
                        { Candidate swap = selected[i]; selected[i] = selected[j]; selected[j] = swap; }
                    }
            }

            private bool CheckIntermediateBound(IntermediateMergeContext context, GeneralFrontierState aggregate,
                GeneralFrontierState local, int aggregateMask, int localMask, TerminalPruningWork diagnostics)
            {
                bool sample = diagnostics != null && diagnostics.Cost != null
                    && (diagnostics.Checks & 4095L) == 0;
                long start = sample ? Stopwatch.GetTimestamp() : 0;
                int field;
                var left = context.Aggregate[aggregate]; var right = context.Local[local];
                int decision = context.Vessel.Schema.Compare(left, right, context.Vessel.GlobalReference ?? context.Vessel.Reference, out field);
                var auditReference = context.Vessel.GlobalReference == null ? context.Vessel.ReferenceVector : _globalWitnesses[2].Comparison;
                if (context.Vessel.GlobalReference != null)
                    ObserveGlobalBound("Branch", context.Vessel.Schema, left, right, decision,
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
                            double elapsed = 1000d * (Stopwatch.GetTimestamp() - start) / Stopwatch.Frequency;
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
                if (_budget.IntermediateBoundAudit != null)
                    AuditIntermediatePair(context, aggregate, local, aggregateMask, localMask, left, right, decision, auditReference);
                return decision > 0;
            }

            private void AuditIntermediatePair(IntermediateMergeContext context, GeneralFrontierState aggregate,
                GeneralFrontierState local, int aggregateMask, int localMask, TerminalBoundSummary left,
                TerminalBoundSummary right, int decision, CustomComparisonVector auditReference)
            {
                bool resolved = decision != int.MinValue;
                _budget.IntermediateBoundAudit(new IntermediateBoundAudit
                {
                    Vessel = _vessel, AggregateMask = aggregateMask, LocalMask = localMask,
                    RemainingMask = CompleteMask ^ (aggregateMask | localMask), Decision = decision, Resolved = resolved,
                    Selected = Enumerable.Range(0, 6).Select(slot =>
                    { var candidate = aggregate.Selected[slot] ?? local.Selected[slot]; return candidate == null ? null : candidate.Relic; }).ToArray(),
                    OptimisticProfiles = resolved ? context.Vessel.Schema.AuditProfiles(left, right) : null,
                    MissingLowerBound = resolved ? context.Vessel.Schema.Missing(left, right) : 0,
                    ReferenceComparison = auditReference
                });
            }
        }
    }
}
