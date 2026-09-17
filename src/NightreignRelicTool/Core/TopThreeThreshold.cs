using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace NightreignRelicTool.Core
{
    public sealed partial class CustomEffectSearcher
    {
        // Synchronous test observation only; player identities are never serialized.
        internal sealed class GlobalBoundAudit
        {
            public string Kind;
            public VesselDefinition Vessel;
            public RelicInstance[] Selected;
            public CustomSearchBuild[] Witnesses;
            public int Decision, MissingLowerBound;
            public RuleComparisonProfile[] OptimisticProfiles;
        }

        private sealed class TopThreePool
        {
            private readonly List<CustomSearchBuild> _best = new List<CustomSearchBuild>();
            private readonly SearchBudget _budget;
            public readonly TerminalBoundSchema Schema;
            public readonly Dictionary<Candidate, TerminalBoundSummary> Candidates;
            public TopThreePool(PreparedRule[] rules, Candidate[] candidates,
                SearchBudget budget, CustomEffectCatalog catalog)
            {
                _budget = budget;
                Schema = new TerminalBoundSchema(rules, candidates, budget, catalog);
                Candidates = new Dictionary<Candidate, TerminalBoundSummary>();
                foreach (var candidate in candidates)
                {
                    budget.Checkpoint();
                    Candidates.Add(candidate, Schema.ForCandidate(candidate, null));
                }
            }
            public CustomSearchBuild[] Snapshot()
            {
                return _best.Count < 3 ? null : _best.Take(3).ToArray();
            }
            public void Add(CustomSearchBuild build)
            {
                if (build == null) return;
                var old = _best.FirstOrDefault(b => b.Vessel.Id == build.Vessel.Id);
                if (old != null && CompareBuilds(old, build) <= 0) return;
                if (old != null) _best.Remove(old);
                _best.Add(build);
                _best.Sort(CompareBuilds);
                if (_budget.Diagnostics != null && _best.Count >= 3)
                {
                    if (!_budget.Diagnostics.ThirdThresholdFirstMilliseconds.HasValue)
                        _budget.Diagnostics.ThirdThresholdFirstMilliseconds = _budget.ElapsedMilliseconds;
                    _budget.Diagnostics.ThirdThresholdUpdates++;
                }
            }
        }

        private sealed partial class FrontierVesselSearch
        {
            private TopThreePool _globalPool;
            private CustomSearchBuild[] _globalWitnesses;
            private TerminalBoundSummary _globalReference;
            private TerminalBoundSummary[] _globalRemaining;
            private int _globalPruned;
            public bool GloballyExcluded { get; private set; }

            public void SetGlobalThreshold(TopThreePool pool)
            {
                _globalPool = pool;
                _globalWitnesses = pool == null ? null : pool.Snapshot();
            }

            private bool PrepareGlobalBound()
            {
                if (_globalWitnesses == null) return false;
                long start = Stopwatch.GetTimestamp();
                try
                {
                    _globalReference = _globalPool.Schema.ForReference(_globalWitnesses[2].Comparison, null);
                    if (!_globalReference.Resolved) { _globalReference = null; return false; }
                    var legal = Enumerable.Range(0, 6).Select(slot =>
                        _candidates.Where(candidate => AcceptsSlot(candidate, slot)).ToArray()).ToArray();
                    // Preserve the ordinary infeasibility proof/status for empty slots.
                    if (legal.Any(slot => slot.Length == 0)) { _globalReference = null; return false; }
                    _globalRemaining = _globalPool.Schema.RemainingPotentials(legal, _globalPool.Candidates, _budget, null);
                    bool skip = CheckGlobalBound("Vessel", _globalPool.Schema, _globalRemaining[63], null,
                        _globalReference, new Candidate[6]);
                    if (skip)
                    {
                        GloballyExcluded = true;
                        if (_diagnostics != null) _diagnostics.GlobalEarlyExcluded = true;
                    }
                    return skip;
                }
                finally
                {
                    if (_diagnostics != null) _diagnostics.GlobalPreparationMilliseconds += CustomSearchDiagnostics.MillisecondsSince(start);
                }
            }

            private bool CheckGlobalPartial(GeneralFrontierState state, int mask)
            {
                if (_globalReference == null) return false;
                var selected = _globalPool.Schema.ForState(state, _globalPool.Candidates, null);
                return CheckGlobalBound("Branch", _globalPool.Schema, selected, _globalRemaining[63 ^ mask],
                    _globalReference, state.Selected);
            }

            private bool CheckGlobalBound(string kind, TerminalBoundSchema schema, TerminalBoundSummary left,
                TerminalBoundSummary right, TerminalBoundSummary reference, Candidate[] selected)
            {
                int field;
                int decision = schema.Compare(left, right, reference, out field);
                ObserveGlobalBound(kind, schema, left, right, decision, selected);
                return decision > 0;
            }

            private void ObserveGlobalBound(string kind, TerminalBoundSchema schema, TerminalBoundSummary left,
                TerminalBoundSummary right, int decision, Candidate[] selected)
            {
                if (decision > 0) Interlocked.Exchange(ref _globalPruned, 1);
                if (_diagnostics != null)
                {
                    lock (_diagnostics)
                    {
                        _diagnostics.GlobalChecks++;
                        if (decision > 0)
                        {
                            if (kind == "Branch") _diagnostics.GlobalPrunedBranches++;
                            else if (kind == "Block") _diagnostics.GlobalPrunedBlocks++;
                            else if (kind == "Pair") _diagnostics.GlobalPrunedPairs++;
                        }
                    }
                }
                if (_budget.GlobalBoundAudit != null)
                    _budget.GlobalBoundAudit(new GlobalBoundAudit { Kind = kind, Vessel = _vessel,
                        Selected = selected.Select(c => c == null ? null : c.Relic).ToArray(),
                        Witnesses = (CustomSearchBuild[])_globalWitnesses.Clone(), Decision = decision,
                        MissingLowerBound = decision == int.MinValue ? 0 : schema.Missing(left, right),
                        OptimisticProfiles = decision == int.MinValue ? null : schema.AuditProfiles(left, right) });
            }

            private TerminalBoundSummary GlobalJoinReference(TerminalBoundSchema schema, TerminalBoundSummary local)
            {
                if (_globalReference == null) return null;
                var global = schema.ForReference(_globalWitnesses[2].Comparison, null);
                int field;
                // Keep local realizability and global ranking proofs in separate fields.
                return global.Resolved && schema.Compare(local, null, global, out field) >= 0 ? global : null;
            }

            public void FinishGlobalProof()
            {
                if (_globalPruned == 0 || _globalWitnesses == null) return;
                // A surviving result at least as good as T is its cup's optimum:
                // every globally removed completion was strictly below T.
                if (Best == null || CompareBuilds(Best, _globalWitnesses[2]) > 0)
                    GloballyExcluded = true;
            }
        }
    }
}
