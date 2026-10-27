using System;
using System.Linq;

namespace NightreignRelicTool.Core
{
    public sealed partial class CustomEffectSearcher
    {
        private sealed partial class TerminalBoundSchema
        {
            public TerminalBoundSummary Maximum(TerminalBoundSummary[] values, int start, int end,
                SearchBudget budget, CustomSearchPruningDiagnostics diagnostics)
            {
                var upper = new TerminalBoundSummary(_size, diagnostics);
                for (int row = start; row < end; row++)
                {
                    budget.Checkpoint();
                    upper.Resolved &= values[row].Resolved;
                    for (int cell = _rules.Length; cell < _size; cell++)
                        upper.Counts[cell] = Math.Max(upper.Counts[cell], values[row].Counts[cell]);
                }
                // Independent rank maxima can come from different rows. Rebuild
                // additive totals from that entire optimistic distribution. Group
                // cells retain their existing single/capped Count interpretation.
                for (int rule = 0; rule < _rules.Length; rule++)
                {
                    long count = 0;
                    for (int rank = 0; rank < _ranks[rule].Length; rank++)
                        count += upper.Counts[_offsets[rule] + rank];
                    if (count > int.MaxValue / 2) upper.Resolved = false;
                    else upper.Counts[rule] = (int)count;
                }
                return upper;
            }
        }

        private sealed partial class FrontierVesselSearch
        {
            private const int TerminalBlockSize = 32;
            private bool EarlyReferenceCanStrengthen(TerminalPruningContext context, CustomSearchPruningDiagnostics diagnostics)
            {
                if (context.ReferenceVector == null) return true;
                long pairs = (long)context.Aggregate.Length * context.Local.Length;
                if (pairs <= StableExactV1Configuration.BaseReferenceEvaluationLimit)
                {
                    if (diagnostics != null) diagnostics.ReferenceBuilderSkippedForSmallJoin = true;
                    return false;
                }
                var reference = context.Schema.ForReference(context.ReferenceVector, diagnostics);
                if (!reference.Resolved) return true;
                var leftRows = context.AggregateBlocks ?? context.Aggregate;
                var left = context.Schema.Maximum(leftRows, 0, leftRows.Length, _budget, diagnostics);
                var right = context.Schema.Maximum(context.Local, 0, context.Local.Length, _budget, diagnostics);
                int field;
                if (context.Schema.Compare(left, right, reference, out field) != 0) return true;
                if (diagnostics != null) diagnostics.ReferenceBuilderSkippedForOptimalPrefix = true;
                return false;
            }

            private TerminalBoundSummary[] BuildTerminalBlocks(TerminalBoundSchema schema,
                TerminalBoundSummary[] states, CustomSearchPruningDiagnostics diagnostics)
            {
                var result = new TerminalBoundSummary[(states.Length + TerminalBlockSize - 1) / TerminalBlockSize];
                for (int block = 0; block < result.Length; block++)
                    result[block] = schema.Maximum(states, block * TerminalBlockSize,
                        Math.Min(states.Length, (block + 1) * TerminalBlockSize), _budget, diagnostics);
                return result;
            }

            private int SkipTerminalBlock(TerminalPruningContext context, int aggregateIndex, int localIndex,
                GeneralFrontierState[] aggregate, GeneralFrontierState local, TerminalPruningWork work)
            {
                if (context == null || context.AggregateBlocks == null || aggregateIndex % TerminalBlockSize != 0) return 0;
                if ((aggregateIndex & 255) == 0) _budget.Checkpoint(); // Includes fully pruned traversal.
                int field;
                var upper = context.AggregateBlocks[aggregateIndex / TerminalBlockSize];
                int decision = context.Schema.Compare(upper, context.Local[localIndex], context.GlobalReference ?? context.Reference, out field);
                var auditReference = context.GlobalReference == null ? context.ReferenceVector : _globalWitnesses[2].Comparison;
                if (context.GlobalReference != null)
                {
                    int end = Math.Min(aggregate.Length, aggregateIndex + TerminalBlockSize);
                    // Observe every block member, including unpruned block uppers.
                    for (int member = aggregateIndex; member < end; member++)
                    {
                        if (member != aggregateIndex && _budget.GlobalBoundAudit == null) break;
                        ObserveGlobalBound("Block", context.Schema, upper, context.Local[localIndex], decision,
                            _budget.GlobalBoundAudit == null ? null : AuditPairCandidates(aggregate[member], local));
                    }
                }
                if (work != null) work.BlockChecks++;
                if (decision <= 0) return 0; // Equality and unknowns preserve every secondary/stable tie.
                int length = Math.Min(TerminalBlockSize, aggregate.Length - aggregateIndex);
                if (work != null)
                {
                    work.Checks += length;
                    work.Pruned += length;
                    work.Reasons[field] += length;
                    work.BlockPrunedPairs += length;
                }
                // Tests audit the BLOCK upper bound against every full legal pair
                // it represents, not merely against a selected sample or its rows.
                if (_budget.TerminalBoundAudit != null)
                    AuditTerminalBlock(context, aggregateIndex, localIndex, aggregate, local, upper, decision, length, auditReference);
                return length;
            }

            private void AuditTerminalBlock(TerminalPruningContext context, int aggregateIndex, int localIndex,
                GeneralFrontierState[] aggregate, GeneralFrontierState local, TerminalBoundSummary upper,
                int decision, int length, CustomComparisonVector auditReference)
            {
                var profiles = context.Schema.AuditProfiles(upper, context.Local[localIndex]);
                int missing = context.Schema.Missing(upper, context.Local[localIndex]);
                for (int index = aggregateIndex; index < aggregateIndex + length; index++)
                {
                    _budget.Checkpoint();
                    var selected = aggregate[index];
                    _budget.TerminalBoundAudit(new TerminalBoundAudit {
                        Vessel = _vessel,
                        Relics = Enumerable.Range(0, 6).Select(slot => (selected.Selected[slot] ?? local.Selected[slot]).Relic).ToArray(),
                        Resolved = true, Decision = decision, MissingLowerBound = missing, OptimisticProfiles = profiles,
                        ReferenceMissing = auditReference.MissingRequired, ReferenceComparison = auditReference,
                        ReferenceProfiles = auditReference.RuleComparisonProfiles
                    });
                }
            }
        }
    }
}
