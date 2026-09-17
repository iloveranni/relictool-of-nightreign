using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Serialization;

namespace NightreignRelicTool.Core
{
    // Internal, opt-in experiment. It has no application/UI or persistence path.
    internal sealed class OracleReferenceTestHooks
    {
        public string Mode = "original";
        public object QueryIdentity;
        public object Witnesses;
        public Action<object> Captured;
        public Action<object> Trace;
        public int TraceVesselId = -1;
    }

    [DataContract]
    public sealed class OracleReferenceDiagnostics
    {
        [DataMember] public string Mode, Status;
        [DataMember] public double ValidationMilliseconds;
        [DataMember] public long ValidationTransitions;
        [DataMember] public int ValidationEvaluations;
        [DataMember] public int? OriginalMissingRequired, OracleMissingRequired;
        [DataMember] public int? FullComparisonToOracle, PrefixComparisonToOracle, FirstPrefixField;
        [DataMember] public string FirstFullDifference;
    }

    public sealed partial class CustomEffectSearcher
    {
        private static void PublishOracleTestResults(SearchBudget budget, CustomSearchOptions options,
            List<CustomSearchBuild> perVessel)
        {
            OracleWitnessSet captured=null;
            PreviousExactHintSet previous=null;
            try
            {
                budget.Checkpoint();
                if(budget.OracleQuery!=null && budget.OracleTest.Captured!=null)
                {
                    captured=new OracleWitnessSet(budget.OracleQuery,perVessel.ToArray());
                    budget.OracleTest.Captured(captured);
                }
                if(budget.PreviousHintQuery!=null && budget.GuidanceTest.Captured!=null
                    && budget.PreviousHintQuery.RevisionIsCurrent())
                {
                    previous=new PreviousExactHintSet(budget.PreviousHintQuery,perVessel);
                    budget.GuidanceTest.Captured(previous);
                }
                if(options.CompletedVesselsForTest!=null) options.CompletedVesselsForTest(perVessel.ToArray());
                budget.Checkpoint();
                if(captured!=null) captured.Committed=true;
                if(previous!=null)
                {
                    if(budget.PreviousHintQuery.RevisionIsCurrent()) previous.Committed=true;
                    else previous.Dispose();
                }
            }
            catch
            {
                if(captured!=null) captured.Dispose();
                if(previous!=null) previous.Dispose();
                throw;
            }
        }
        // These objects contain private physical witnesses. Never serialize them.
        private sealed class OracleInputEvidence
        {
            public readonly RuntimeModel Model;
            public readonly CustomEffectCatalog Catalog;
            public readonly CharacterInventory Inventory;
            public readonly object Identity;
            public readonly string CharacterId;
            public readonly int[] VesselIds;
            public readonly CustomEffectRule[] Rules;
            private readonly int[][] _relics;
            public OracleInputEvidence(CustomEffectSearcher owner, CharacterInventory inventory,
                CharacterDefinition character, PreparedRule[] rules, object identity, SearchBudget budget)
            {
                Model=owner._model; Catalog=owner._catalog; Inventory=inventory; Identity=identity;
                CharacterId=character.Id; VesselIds=(int[])character.EligibleVesselIds.Clone();
                Rules=rules.Select(r => r.Source.TargetKind==CustomRuleTargetKind.EffectSelector
                    ? new CustomEffectRule(r.Source.RuleId,r.Source.SelectorId,r.Source.IsRequired,r.Source.QuantityTarget)
                    : new CustomEffectRule(r.Source.RuleId,r.Source.PresetItemId,r.Source.IsRequired)).ToArray();
                _relics=new int[inventory.Relics.Length][];
                for(int i=0;i<_relics.Length;i++) { budget.Checkpoint(); _relics[i]=RelicEvidence(inventory.Relics[i]); }
            }
            private static int[] RelicEvidence(RelicInstance r)
            {
                return new[]{r.InstanceId,r.ItemId,r.ColorId,r.IsDeep?1:0,r.PositiveEffectIds.Length}
                    .Concat(r.PositiveEffectIds).Concat(r.NegativeEffectIds).ToArray();
            }
            public bool Matches(CustomEffectSearcher owner, CharacterInventory inventory, CharacterDefinition character,
                PreparedRule[] rules, object identity, SearchBudget budget)
            {
                if(identity==null || !ReferenceEquals(Identity,identity) || !ReferenceEquals(Model,owner._model)
                    || !ReferenceEquals(Catalog,owner._catalog) || !ReferenceEquals(Inventory,inventory)
                    || CharacterId!=character.Id || !VesselIds.SequenceEqual(character.EligibleVesselIds)
                    || Rules.Length!=rules.Length || _relics.Length!=inventory.Relics.Length) return false;
                for(int i=0;i<rules.Length;i++)
                {
                    var a=Rules[i]; var b=rules[i].Source;
                    if(a.RuleId!=b.RuleId || a.TargetKind!=b.TargetKind || a.SelectorId!=b.SelectorId
                        || a.PresetItemId!=b.PresetItemId || a.IsRequired!=b.IsRequired || a.QuantityTarget!=b.QuantityTarget) return false;
                }
                for(int i=0;i<_relics.Length;i++)
                {
                    budget.Checkpoint(); var r=inventory.Relics[i]; var e=_relics[i];
                    if(e.Length!=5+r.PositiveEffectIds.Length+r.NegativeEffectIds.Length || e[0]!=r.InstanceId
                        || e[1]!=r.ItemId || e[2]!=r.ColorId || e[3]!=(r.IsDeep?1:0) || e[4]!=r.PositiveEffectIds.Length) return false;
                    for(int j=0;j<r.PositiveEffectIds.Length;j++) if(e[5+j]!=r.PositiveEffectIds[j]) return false;
                    for(int j=0;j<r.NegativeEffectIds.Length;j++) if(e[5+r.PositiveEffectIds.Length+j]!=r.NegativeEffectIds[j]) return false;
                }
                return true;
            }
        }

        private sealed class OracleWitness
        {
            public readonly VesselDefinition Vessel;
            public readonly SlotAssignment[] Assignments;
            public readonly CustomComparisonVector Comparison;
            public OracleWitness(CustomSearchBuild build)
            { Vessel=build.Vessel; Assignments=(SlotAssignment[])build.Assignments.Clone(); Comparison=build.Comparison; }
        }

        private sealed class OracleWitnessSet : IDisposable
        {
            public OracleInputEvidence Input;
            public CustomSearchBuild[] Builds;
            public Dictionary<int,OracleWitness> Witnesses;
            public bool Committed;
            public OracleWitnessSet(OracleQueryContext query, CustomSearchBuild[] builds)
            {
                Input=query.CaptureInput; Builds=builds;
                Witnesses=builds.ToDictionary(b=>b.Vessel.Id,b=>new OracleWitness(b));
            }
            public void Dispose() { Committed=false; Input=null; Builds=null; Witnesses=null; }
        }

        private sealed class OracleQueryContext
        {
            public readonly OracleReferenceTestHooks Hooks;
            public readonly OracleInputEvidence CaptureInput;
            public readonly OracleWitnessSet Source;
            public readonly bool InputValid;
            public readonly string InputStatus;
            public OracleQueryContext(CustomEffectSearcher owner, CharacterInventory inventory,
                CharacterDefinition character, PreparedRule[] rules, SearchBudget budget)
            {
                Hooks=budget.OracleTest;
                long start=budget.Diagnostics==null?0:Stopwatch.GetTimestamp();
                try
                {
                    if(Hooks.Captured!=null)
                        CaptureInput=new OracleInputEvidence(owner,inventory,character,rules,Hooks.QueryIdentity,budget);
                    Source=Hooks.Witnesses as OracleWitnessSet;
                    InputValid=Source!=null && Source.Committed && Source.Input!=null
                        && Source.Input.Matches(owner,inventory,character,rules,Hooks.QueryIdentity,budget);
                    InputStatus=InputValid?"Matched":Source==null?"NoWitnessSet":"StaleOrMismatchedInput";
                }
                finally
                {
                    if(budget.Diagnostics!=null)
                    { budget.Diagnostics.OracleInputValidationMilliseconds=CustomSearchDiagnostics.MillisecondsSince(start);
                      budget.Diagnostics.OracleInputStatus=InputStatus; }
                }
            }
        }

        private sealed class OracleReferenceTrace
        {
            public int VesselId,Pass,Slot,Attempt;
            public string Stage;
            public bool Accepted;
            public RelicInstance[] Selected;
            public RelicInstance[][] Ordered;
            public CustomComparisonVector Comparison,Before;
        }

        private sealed partial class FrontierVesselSearch
        {
            private void TraceOracleReference(string stage,int pass,int slot,int attempt,Candidate[] selected,
                CustomComparisonVector vector,CustomComparisonVector before,Candidate[][] ordered,bool unused)
            {
                var hooks=_budget.OracleTest;
                var guidance=_budget.GuidanceTest;
                bool oracleTrace=hooks!=null && hooks.Trace!=null
                    && (hooks.TraceVesselId==0 || hooks.TraceVesselId==_vessel.Id);
                bool guidanceTrace=guidance!=null && guidance.Trace!=null
                    && (guidance.TraceVesselId==0 || guidance.TraceVesselId==_vessel.Id);
                if(!oracleTrace && !guidanceTrace) return;
                _budget.Checkpoint();
                var observation=new OracleReferenceTrace { VesselId=_vessel.Id,Stage=stage,Pass=pass,Slot=slot,Attempt=attempt,
                    Selected=selected==null?null:selected.Select(c=>c.Relic).ToArray(),Comparison=vector,Before=before,
                    Ordered=ordered==null?null:ordered.Select(a=>a.Select(c=>c.Relic).ToArray()).ToArray(),
                    Accepted=before!=null && CustomComparisonVector.Compare(vector,before)<0 };
                if(oracleTrace) hooks.Trace(observation);
                if(guidanceTrace) guidance.Trace(observation);
                _budget.Checkpoint();
            }

            private IntermediateVesselContext PrepareOracleReference(IntermediateVesselContext context,
                Candidate[][] legal,CustomSearchPruningDiagnostics diagnostics)
            {
                var query=_budget.OracleQuery;
                var detail=_diagnostics==null?null:new OracleReferenceDiagnostics { Mode=query.Hooks.Mode,Status="Original" };
                if(_diagnostics!=null) _diagnostics.OracleReference=detail;
                long transitions=_budget.TransitionCount;
                CustomComparisonVector original=null,oracle=null;
                try
                {
                    bool direct=query.Hooks.Mode=="oracle_direct";
                    bool keep=query.Hooks.Mode=="oracle_keep_builder";
                    if(direct) oracle=ValidateOracleReference(context,detail);
                    if(!direct || oracle==null)
                    {
                        long start=diagnostics==null?0:Stopwatch.GetTimestamp();
                        try { original=FindEarlyReference(legal,context,diagnostics); }
                        finally { if(diagnostics!=null) diagnostics.ReferenceMilliseconds=CustomSearchDiagnostics.MillisecondsSince(start); }
                    }
                    if(keep) oracle=ValidateOracleReference(context,detail);
                    if(detail!=null && original!=null)
                    {
                        detail.OriginalMissingRequired=original.MissingRequired;
                        if(oracle!=null)
                        {
                            detail.FullComparisonToOracle=Math.Sign(CustomComparisonVector.Compare(original,oracle));
                            int field; int comparison=context.Schema.Compare(context.Schema.ForReference(original,diagnostics),null,
                                context.Schema.ForReference(oracle,diagnostics),out field);
                            if(comparison!=int.MinValue)
                            {
                                detail.PrefixComparisonToOracle=Math.Sign(comparison);
                                if(comparison!=0) detail.FirstPrefixField=field;
                                detail.FirstFullDifference=comparison!=0?"prefix_field_"+field:FirstOracleSuffixDifference(original,oracle);
                            }
                        }
                    }
                    context.ReferenceVector=oracle??original;
                    if(context.ReferenceVector==null) return null;
                    context.Reference=context.Schema.ForReference(context.ReferenceVector,diagnostics);
                    if(!context.Reference.Resolved) return null;
                    if(diagnostics!=null)
                    { diagnostics.ReferenceFound=true; diagnostics.ReferenceMissingRequired=context.ReferenceVector.MissingRequired; }
                    return context;
                }
                finally
                {
                    if(diagnostics!=null) diagnostics.ReferenceTransitions=_budget.TransitionCount-transitions;
                }
            }

            // Diagnostic names only; this never participates in pruning or ranking.
            private static string FirstOracleSuffixDifference(CustomComparisonVector a,CustomComparisonVector b)
            {
                if(!a.CoLocationProfile.DescendingMultiMatchCounts.SequenceEqual(b.CoLocationProfile.DescendingMultiMatchCounts)) return "co_location";
                if(a.CoveredRuleCount!=b.CoveredRuleCount) return "covered_rule_count";
                if(!a.EffectiveContributions.SequenceEqual(b.EffectiveContributions)) return "effective_contributions";
                if(a.NegativeEffectCount!=b.NegativeEffectCount) return "negative_count";
                if(a.RelicsWithNegativeCount!=b.RelicsWithNegativeCount) return "negative_relic_count";
                if(!a.UncertainAdditionalCounts.SequenceEqual(b.UncertainAdditionalCounts)) return "uncertain_additional";
                if(a.UncertaintyCount!=b.UncertaintyCount) return "uncertainty_count";
                if(a.VesselId!=b.VesselId) return "vessel_id";
                if(!a.StableInstanceIds.SequenceEqual(b.StableInstanceIds)) return "stable_instance_order";
                return "equal_full";
            }

            private CustomComparisonVector ValidateOracleReference(IntermediateVesselContext context,OracleReferenceDiagnostics diagnostics)
            {
                long start=diagnostics==null?0:Stopwatch.GetTimestamp();
                long transitions=_budget.TransitionCount;
                try
                {
                    _budget.Checkpoint();
                    if(! _budget.OracleQuery.InputValid)
                    { if(diagnostics!=null) diagnostics.Status=_budget.OracleQuery.InputStatus; return null; }
                    OracleWitness witness;
                    if(!_budget.OracleQuery.Source.Witnesses.TryGetValue(_vessel.Id,out witness))
                    { if(diagnostics!=null) diagnostics.Status="MissingVessel"; return null; }
                    if(!ReferenceEquals(witness.Vessel,_vessel) || witness.Assignments==null || witness.Assignments.Length!=6)
                    { if(diagnostics!=null) diagnostics.Status="WrongVesselOrShape"; return null; }
                    Candidate[] selected=new Candidate[6];
                    if(_diagnostics!=null) _diagnostics.Creations.SixSlotArrays++;
                    for(int slot=0;slot<6;slot++)
                    {
                        _budget.Checkpoint(); var assignment=witness.Assignments[slot];
                        int color=slot<3?_vessel.OrdinarySlotColorIds[slot]:_vessel.DeepSlotColorIds[slot-3];
                        if(assignment==null || assignment.Relic==null || assignment.IsDeep!=(slot>=3)
                            || assignment.SlotIndex!=slot%3 || assignment.SlotColorId!=color)
                        { if(diagnostics!=null) diagnostics.Status="InvalidSlot"; return null; }
                        selected[slot]=_candidates.FirstOrDefault(c=>ReferenceEquals(c.Relic,assignment.Relic));
                        if(selected[slot]==null) { if(diagnostics!=null) diagnostics.Status="MissingPhysicalInstance"; return null; }
                        _budget.RecordTransitionAndState(); // An actual auxiliary slot placement.
                    }
                    if(!IsLegalTerminalReference(selected))
                    { if(diagnostics!=null) diagnostics.Status="IllegalOrDuplicateInstance"; return null; }
                    for(int i=0;i<6;i++) for(int j=i+1;j<(i<3?3:6);j++)
                    {
                        int a=i<3?_vessel.OrdinarySlotColorIds[i]:_vessel.DeepSlotColorIds[i-3];
                        int b=j<3?_vessel.OrdinarySlotColorIds[j]:_vessel.DeepSlotColorIds[j-3];
                        if(a==b && selected[i].Relic.InstanceId>selected[j].Relic.InstanceId)
                        { if(diagnostics!=null) diagnostics.Status="NonCanonicalSlots"; return null; }
                    }
                    var vector=_owner.EvaluateCandidatesVector(_vessel,selected,_characterId,_rules,_budget);
                    if(diagnostics!=null) diagnostics.ValidationEvaluations++;
                    if(CustomComparisonVector.Compare(vector,witness.Comparison)!=0)
                    { if(diagnostics!=null) diagnostics.Status="WitnessVectorMismatch"; return null; }
                    if(!context.Schema.ForReference(vector,_diagnostics==null?null:_diagnostics.IntermediatePreparation).Resolved)
                    { if(diagnostics!=null) diagnostics.Status="UnresolvedWitness"; return null; }
                    if(diagnostics!=null) { diagnostics.Status="Accepted"; diagnostics.OracleMissingRequired=vector.MissingRequired; }
                    return vector;
                }
                finally
                {
                    if(diagnostics!=null)
                    { diagnostics.ValidationMilliseconds=CustomSearchDiagnostics.MillisecondsSince(start);
                      diagnostics.ValidationTransitions=_budget.TransitionCount-transitions; }
                }
            }
        }
    }
}
