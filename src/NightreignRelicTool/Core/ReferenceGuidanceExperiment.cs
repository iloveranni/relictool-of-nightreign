using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Serialization;

namespace NightreignRelicTool.Core
{
    // Internal experiment only. It has no application, UI, persistence or static cache path.
    internal sealed class ReferenceGuidanceTestOptions
    {
        public string Mode = "original";
        public int ExtraEvaluationBudget;
        public long EstimatedPairThreshold = 50000;
        public object GenerationIdentity;
        public object PreviousExactHints;
        public Action<object> Captured;
        // Test-only in-memory observation of the bounded reference builder.
        public Action<object> Trace;
        // Zero observes every vessel; a positive value observes one public vessel id.
        public int TraceVesselId = -1;
        public long QueryRevision;
        public Func<long> CurrentRevision;

        internal bool UsesRequiredGapReferenceOrdering
        {
            get
            {
                return Mode == "gap_same" || Mode == "gap_adaptive"
                    || Mode == "history_adaptive" || Mode == "prefix_adaptive"
                    || Mode == "prefix_ordered_adaptive";
            }
        }
    }

    [DataContract]
    public sealed class ReferenceGuidanceDiagnostics
    {
        [DataMember] public string Mode, PreviousHintStatus, ExtraTriggerReason, ExtraStopReason;
        [DataMember] public int GreedyMissingRequired, FinalMissingRequired;
        [DataMember] public int GuidedScans, GuidedEvaluations, GuidedAccepted;
        [DataMember] public int ExtraBudget, ExtraScans, ExtraEvaluations, ExtraAccepted;
        [DataMember] public int ExtraPrescreenPassed, ExtraPrescreenRejected;
        [DataMember] public int FirstExtraEvaluationScan, FirstExtraAcceptedScan, FirstExtraAcceptedEvaluation;
        [DataMember] public bool ExtraTriggered, PreviousHintAccepted;
        [DataMember] public long EstimatedNextPairCount, PreviousHintTransitions;
        [DataMember] public int PreviousHintEvaluations;
        [DataMember] public double ColdBuilderMilliseconds, ExtraMilliseconds, PreviousHintMilliseconds;
        [DataMember(EmitDefaultValue=false)] public int? GreedyToFinalPrefixComparison, PreviousToColdFullComparison;
    }

    public sealed partial class CustomEffectSearcher
    {
        private sealed class PreviousExactHintSet : IDisposable
        {
            public RuntimeModel Model;
            public CustomEffectCatalog Catalog;
            public CharacterInventory Inventory;
            public object GenerationIdentity;
            public string CharacterId;
            public int[] VesselIds;
            public Dictionary<int,SlotAssignment[]> Assignments;
            public bool Committed;

            public PreviousExactHintSet(PreviousHintQueryContext query,IEnumerable<CustomSearchBuild> builds)
            {
                Model=query.Owner._model; Catalog=query.Owner._catalog; Inventory=query.Inventory;
                GenerationIdentity=query.Options.GenerationIdentity; CharacterId=query.Character.Id;
                VesselIds=(int[])query.Character.EligibleVesselIds.Clone();
                Assignments=builds.ToDictionary(b=>b.Vessel.Id,b=>(SlotAssignment[])b.Assignments.Clone());
            }
            public void Dispose()
            {
                Committed=false; Model=null;Catalog=null;Inventory=null;GenerationIdentity=null;
                CharacterId=null;VesselIds=null;Assignments=null;
            }
        }

        private sealed class PreviousHintQueryContext
        {
            public readonly CustomEffectSearcher Owner;
            public readonly CharacterInventory Inventory;
            public readonly CharacterDefinition Character;
            public readonly ReferenceGuidanceTestOptions Options;
            public readonly PreviousExactHintSet Source;
            public readonly bool InputValid;
            public readonly string InputStatus;

            public PreviousHintQueryContext(CustomEffectSearcher owner,CharacterInventory inventory,
                CharacterDefinition character,SearchBudget budget)
            {
                Owner=owner;Inventory=inventory;Character=character;Options=budget.GuidanceTest;
                long start=budget.Diagnostics==null?0:Stopwatch.GetTimestamp();
                try
                {
                    Source=Options.PreviousExactHints as PreviousExactHintSet;
                    InputValid=Source!=null && Source.Committed && Options.GenerationIdentity!=null
                        && ReferenceEquals(Source.GenerationIdentity,Options.GenerationIdentity)
                        && ReferenceEquals(Source.Model,owner._model) && ReferenceEquals(Source.Catalog,owner._catalog)
                        && ReferenceEquals(Source.Inventory,inventory) && Source.CharacterId==character.Id
                        && Source.VesselIds!=null && Source.VesselIds.SequenceEqual(character.EligibleVesselIds)
                        && Source.Assignments!=null;
                    InputStatus=InputValid?"Matched":Source==null?"NoPreviousHint":"StaleOrMismatchedInput";
                }
                finally
                {
                    if(budget.Diagnostics!=null)
                    {
                        budget.Diagnostics.PreviousHintInputStatus=InputStatus;
                        budget.Diagnostics.PreviousHintInputValidationMilliseconds=CustomSearchDiagnostics.MillisecondsSince(start);
                    }
                }
            }

            public bool RevisionIsCurrent()
            {
                return Options.CurrentRevision==null || Options.CurrentRevision()==Options.QueryRevision;
            }
        }

        private sealed partial class FrontierVesselSearch
        {
            private struct GapOrder
            {
                public Candidate Candidate;
                public int FirstRule,HitRules,OriginalIndex;
            }

            private struct PrefixProposalOrder
            {
                public Candidate Candidate;
                public int FirstField,PotentialRank,OriginalIndex;
            }

            private struct PrefixSlotOrder
            {
                public int Slot,FirstField,PotentialRank,OriginalIndex;
            }

            private bool CanPotentiallyFill(Candidate candidate,int ruleIndex)
            {
                return candidate.Occurrences.Any(o=>o.RuleIndex==ruleIndex &&
                    (o.IsApplicable || (o.EffectId>0 && _owner._catalog.GetEffect(o.EffectId)==null)));
            }

            private void GapPriority(Candidate candidate,CustomComparisonVector vector,out int first,out int hits)
            {
                first=int.MaxValue;hits=0;
                for(int i=0;i<_rules.Length;i++)
                {
                    if(!_rules[i].Source.IsRequired
                        || vector.RuleComparisonProfiles[i].EffectiveCount>=_rules[i].Source.QuantityTarget
                        || !CanPotentiallyFill(candidate,i)) continue;
                    if(first==int.MaxValue)first=i;
                    hits++;
                }
            }

            private Candidate[] OrderForCurrentGaps(Candidate[] original,CustomComparisonVector vector)
            {
                if(vector==null || vector.MissingRequired==0)return original;
                GapOrder[] values=new GapOrder[original.Length];
                for(int i=0;i<original.Length;i++)
                {
                    int first,hits;GapPriority(original[i],vector,out first,out hits);
                    values[i]=new GapOrder {Candidate=original[i],FirstRule=first,HitRules=hits,OriginalIndex=i};
                }
                Array.Sort(values,(a,b)=>
                {
                    int value=a.FirstRule.CompareTo(b.FirstRule);
                    if(value!=0)return value;
                    value=b.HitRules.CompareTo(a.HitRules);
                    if(value!=0)return value;
                    value=a.OriginalIndex.CompareTo(b.OriginalIndex);
                    return value!=0?value:a.Candidate.Relic.InstanceId.CompareTo(b.Candidate.Relic.InstanceId);
                });
                return values.Select(v=>v.Candidate).ToArray();
            }

            private bool HasCurrentGapPotential(Candidate candidate,CustomComparisonVector vector)
            {
                int first,hits;GapPriority(candidate,vector,out first,out hits);return hits!=0;
            }

            private bool HasConservativePrefixPotential(Candidate incoming,Candidate outgoing)
            {
                // The formal B/C prefix can only change through a selected rule occurrence
                // or through an applicable exclusivity participant that changes which rule
                // occurrence is confirmed. Aggregation, caps, unknown winners, co-matches and
                // all other non-local cases deliberately pass this gate. The only rejection is
                // therefore the proven-empty case on both sides of the replacement.
                return HasPrefixSemanticFootprint(incoming,_owner._catalog)
                    || HasPrefixSemanticFootprint(outgoing,_owner._catalog);
            }

            private void CandidateRulePotential(Candidate candidate,int ruleIndex,out int count,out int rank)
            {
                count=0;rank=0;
                for(int index=0;index<candidate.Occurrences.Length;index++)
                {
                    Occurrence occurrence=candidate.Occurrences[index];
                    if(occurrence.RuleIndex!=ruleIndex || !occurrence.IsApplicable
                        && !(occurrence.EffectId>0 && _owner._catalog.GetEffect(occurrence.EffectId)==null))continue;
                    count++;
                    if(occurrence.Rank>rank)rank=occurrence.Rank;
                }
            }

            private void PrefixProposalPriority(Candidate incoming,Candidate outgoing,CustomComparisonVector vector,
                out int firstField,out int potentialRank)
            {
                firstField=int.MaxValue;potentialRank=0;
                int ordinal=0;
                for(int requiredGroup=0;requiredGroup<2;requiredGroup++)
                for(int ruleIndex=0;ruleIndex<_rules.Length;ruleIndex++)
                {
                    bool required=requiredGroup==0;
                    if(_rules[ruleIndex].Source.IsRequired!=required)continue;
                    int incomingCount,incomingRank,outgoingCount,outgoingRank;
                    CandidateRulePotential(incoming,ruleIndex,out incomingCount,out incomingRank);
                    CandidateRulePotential(outgoing,ruleIndex,out outgoingCount,out outgoingRank);
                    RuleComparisonProfile profile=vector.RuleComparisonProfiles[ruleIndex];
                    int baseField=1+ordinal*5;
                    if(required && profile.EffectiveCount<profile.QuantityTarget && incomingCount>outgoingCount)
                    {firstField=0;potentialRank=incomingRank;return;}
                    if(incomingRank>profile.MaximumVariantPreferenceRank)
                    {firstField=baseField+1;potentialRank=incomingRank;return;}
                    if(incomingRank==profile.MaximumVariantPreferenceRank && incomingCount>outgoingCount)
                    {firstField=baseField+2;potentialRank=incomingRank;return;}
                    if(incomingCount>outgoingCount)
                    {firstField=baseField+4;potentialRank=incomingRank;return;}
                    if((incomingCount!=0 || outgoingCount!=0) && firstField==int.MaxValue)
                    {firstField=baseField+3;potentialRank=incomingRank;}
                    ordinal++;
                }
            }

            private Candidate[] OrderForPrefixPotential(Candidate[] original,Candidate outgoing,
                CustomComparisonVector vector)
            {
                PrefixProposalOrder[] values=new PrefixProposalOrder[original.Length];
                for(int index=0;index<original.Length;index++)
                {
                    int field,rank;PrefixProposalPriority(original[index],outgoing,vector,out field,out rank);
                    values[index]=new PrefixProposalOrder {Candidate=original[index],FirstField=field,
                        PotentialRank=rank,OriginalIndex=index};
                }
                Array.Sort(values,(left,right)=>
                {
                    int value=left.FirstField.CompareTo(right.FirstField);
                    if(value!=0)return value;
                    value=right.PotentialRank.CompareTo(left.PotentialRank);
                    if(value!=0)return value;
                    value=left.OriginalIndex.CompareTo(right.OriginalIndex);
                    return value!=0?value:left.Candidate.Relic.InstanceId.CompareTo(right.Candidate.Relic.InstanceId);
                });
                Candidate[] ordered=new Candidate[values.Length];
                for(int index=0;index<values.Length;index++)ordered[index]=values[index].Candidate;
                return ordered;
            }

            private int[] OrderSlotsForPrefixPotential(IntermediateVesselContext context)
            {
                PrefixSlotOrder[] values=new PrefixSlotOrder[context.SlotOrder.Length];
                for(int order=0;order<context.SlotOrder.Length;order++)
                {
                    int slot=context.SlotOrder[order];
                    int field=int.MaxValue,rank=0;
                    foreach(Candidate candidate in context.Ordered[slot])
                    {
                        int candidateField,candidateRank;
                        PrefixProposalPriority(candidate,context.ReferenceSelected[slot],context.ReferenceVector,
                            out candidateField,out candidateRank);
                        if(candidateField<field || candidateField==field && candidateRank>rank)
                        {field=candidateField;rank=candidateRank;}
                    }
                    values[order]=new PrefixSlotOrder {Slot=slot,FirstField=field,
                        PotentialRank=rank,OriginalIndex=order};
                }
                Array.Sort(values,(left,right)=>
                {
                    int value=left.FirstField.CompareTo(right.FirstField);
                    if(value!=0)return value;
                    value=right.PotentialRank.CompareTo(left.PotentialRank);
                    return value!=0?value:left.OriginalIndex.CompareTo(right.OriginalIndex);
                });
                int[] slots=new int[values.Length];
                for(int index=0;index<values.Length;index++)slots[index]=values[index].Slot;
                return slots;
            }

            private sealed class PrefixPrescreenTrace
            {
                public int VesselId,Slot,Scan,Evaluation;
                public string Stage,Mode;
                public bool Passed,Accepted;
                public RelicInstance Incoming,Outgoing;
                public CustomComparisonVector Before,After;
            }

            private void TracePrefixPrescreen(string stage,int slot,Candidate incoming,Candidate outgoing,
                CustomComparisonVector before,CustomComparisonVector after,bool passed,bool accepted,
                ReferenceGuidanceDiagnostics detail)
            {
                var options=_budget.GuidanceTest;
                if(options==null || options.Trace==null
                    || options.TraceVesselId!=0 && options.TraceVesselId!=_vessel.Id)return;
                _budget.Checkpoint();
                options.Trace(new PrefixPrescreenTrace {VesselId=_vessel.Id,Slot=slot,Stage=stage,
                    Mode=options.Mode,Scan=detail==null?0:detail.ExtraScans,
                    Evaluation=detail==null?0:detail.ExtraEvaluations,Incoming=incoming.Relic,
                    Outgoing=outgoing.Relic,Before=before,After=after,Passed=passed,Accepted=accepted});
                _budget.Checkpoint();
            }

            private void ApplyPreviousHint(IntermediateVesselContext context,ReferenceGuidanceDiagnostics detail,
                CustomSearchPruningDiagnostics diagnostics)
            {
                var query=_budget.PreviousHintQuery;
                if(query==null || _budget.GuidanceTest.Mode!="history_adaptive")return;
                long start=detail==null?0:Stopwatch.GetTimestamp(),transitions=_budget.TransitionCount;
                try
                {
                    _budget.Checkpoint();
                    if(!query.InputValid){if(detail!=null)detail.PreviousHintStatus=query.InputStatus;return;}
                    SlotAssignment[] assignments;
                    if(!query.Source.Assignments.TryGetValue(_vessel.Id,out assignments) || assignments==null || assignments.Length!=6)
                    {if(detail!=null)detail.PreviousHintStatus="MissingVessel";return;}
                    Candidate[] selected=new Candidate[6];
                    if(_diagnostics!=null)_diagnostics.Creations.SixSlotArrays++;
                    for(int slot=0;slot<6;slot++)
                    {
                        _budget.Checkpoint();var assignment=assignments[slot];
                        int color=slot<3?_vessel.OrdinarySlotColorIds[slot]:_vessel.DeepSlotColorIds[slot-3];
                        if(assignment==null || assignment.Relic==null || assignment.IsDeep!=(slot>=3)
                            || assignment.SlotIndex!=slot%3 || assignment.SlotColorId!=color)
                        {if(detail!=null)detail.PreviousHintStatus="InvalidSlot";return;}
                        RelicInstance relic=query.Inventory.Relics.FirstOrDefault(r=>ReferenceEquals(r,assignment.Relic));
                        if(relic==null){if(detail!=null)detail.PreviousHintStatus="MissingOrReplacedInstance";return;}
                        // A still-present physical witness may no longer be the representative
                        // selected by the new query's projection compression. It remains a
                        // legal reference witness, so recompile it for the current rules rather
                        // than treating projection replacement as repository deletion.
                        selected[slot]=_candidates.FirstOrDefault(c=>ReferenceEquals(c.Relic,relic))
                            ?? new Candidate(relic,_rules,_characterId,_owner._catalog,_budget);
                        _budget.RecordTransitionAndState();
                    }
                    if(!IsLegalTerminalReference(selected))
                    {if(detail!=null)detail.PreviousHintStatus="IllegalOrDuplicateInstance";return;}
                    for(int i=0;i<6;i++)for(int j=i+1;j<(i<3?3:6);j++)
                    {
                        int a=i<3?_vessel.OrdinarySlotColorIds[i]:_vessel.DeepSlotColorIds[i-3];
                        int b=j<3?_vessel.OrdinarySlotColorIds[j]:_vessel.DeepSlotColorIds[j-3];
                        if(a==b && selected[i].Relic.InstanceId>selected[j].Relic.InstanceId)
                        {if(detail!=null)detail.PreviousHintStatus="NonCanonicalSlots";return;}
                    }
                    var vector=_owner.EvaluateCandidatesVector(_vessel,selected,_characterId,_rules,_budget);
                    if(detail!=null)detail.PreviousHintEvaluations++;
                    int comparison=CustomComparisonVector.Compare(vector,context.ReferenceVector);
                    if(detail!=null)detail.PreviousToColdFullComparison=Math.Sign(comparison);
                    if(comparison<0)
                    {
                        context.ReferenceVector=vector;context.ReferenceSelected=selected;
                        if(detail!=null)detail.PreviousHintAccepted=true;
                    }
                    if(detail!=null)detail.PreviousHintStatus="AcceptedAndCompared";
                }
                finally
                {
                    if(detail!=null)
                    {
                        detail.PreviousHintMilliseconds=CustomSearchDiagnostics.MillisecondsSince(start);
                        detail.PreviousHintTransitions=_budget.TransitionCount-transitions;
                    }
                }
            }

            private void MaybeRunAdditionalGapRepair(IntermediateVesselContext context,long estimate,
                CustomSearchPruningDiagnostics phaseDiagnostics)
            {
                if(context.AdaptiveChecked)return;
                var detail=context.Guidance;
                if(detail==null)return;
                detail.EstimatedNextPairCount=Math.Max(detail.EstimatedNextPairCount,estimate);
                detail.ExtraBudget=_budget.GuidanceTest.ExtraEvaluationBudget;
                if(_budget.GuidanceTest.Mode!="gap_adaptive" && _budget.GuidanceTest.Mode!="history_adaptive"
                    && _budget.GuidanceTest.Mode!="prefix_adaptive"
                    && _budget.GuidanceTest.Mode!="prefix_ordered_adaptive")
                {context.AdaptiveChecked=true;detail.ExtraTriggerReason="ModeDisabled";return;}
                if(context.ReferenceVector.MissingRequired==0)
                {context.AdaptiveChecked=true;detail.ExtraTriggerReason="NoRequiredGap";return;}
                if(_budget.GuidanceTest.ExtraEvaluationBudget<=0)
                {context.AdaptiveChecked=true;detail.ExtraTriggerReason="ZeroBudget";return;}
                if(estimate<=_budget.GuidanceTest.EstimatedPairThreshold){detail.ExtraTriggerReason="BelowPairThreshold";return;}
                context.AdaptiveChecked=true;
                detail.ExtraTriggered=true;detail.ExtraTriggerReason="GapAndPairThreshold";
                long started=Stopwatch.GetTimestamp(),transitions=_budget.TransitionCount;
                    try
                {
                    while(detail.ExtraEvaluations<_budget.GuidanceTest.ExtraEvaluationBudget
                        && context.ReferenceVector.MissingRequired>0)
                    {
                        bool improved=false;
                        bool prefixOrdered=_budget.GuidanceTest.Mode=="prefix_ordered_adaptive";
                        int[] repairSlots=prefixOrdered?OrderSlotsForPrefixPotential(context):context.SlotOrder;
                        foreach(int slot in repairSlots)
                        {
                            Candidate[] candidates=prefixOrdered
                                ? OrderForPrefixPotential(context.Ordered[slot],context.ReferenceSelected[slot],context.ReferenceVector)
                                : OrderForCurrentGaps(context.Ordered[slot],context.ReferenceVector);
                            foreach(Candidate candidate in candidates)
                            {
                                _budget.Checkpoint();
                                if(context.Attempted[slot].Contains(candidate)
                                    || context.ReferenceSelected.Any(c=>c.Relic.InstanceId==candidate.Relic.InstanceId))continue;
                                detail.ExtraScans++;
                                Candidate outgoing=context.ReferenceSelected[slot];
                                bool prescreen=_budget.GuidanceTest.Mode=="prefix_adaptive"
                                    || _budget.GuidanceTest.Mode=="prefix_ordered_adaptive"
                                    ? HasConservativePrefixPotential(candidate,outgoing)
                                    : HasCurrentGapPotential(candidate,context.ReferenceVector);
                                if(!prescreen)
                                {
                                    detail.ExtraPrescreenRejected++;
                                    TracePrefixPrescreen("ExtraPrescreen",slot,candidate,outgoing,
                                        context.ReferenceVector,null,false,false,detail);
                                    continue;
                                }
                                detail.ExtraPrescreenPassed++;
                                TracePrefixPrescreen("ExtraPrescreen",slot,candidate,outgoing,
                                    context.ReferenceVector,null,true,false,detail);
                                context.Attempted[slot].Add(candidate);
                                _budget.RecordTransitionAndState();
                                Candidate[] proposal=ReferenceArray(context.ReferenceSelected,
                                    _diagnostics==null?null:_diagnostics.IntermediatePreparation);
                                proposal[slot]=candidate;NormalizeReference(proposal);
                                if(!IsLegalTerminalReference(proposal))continue;
                                var vector=EvaluateEarlyReference(proposal,
                                    _diagnostics==null?null:_diagnostics.IntermediatePreparation);
                                detail.ExtraEvaluations++;
                                if(detail.FirstExtraEvaluationScan==0)detail.FirstExtraEvaluationScan=detail.ExtraScans;
                                bool accepted=CustomComparisonVector.Compare(vector,context.ReferenceVector)<0;
                                TracePrefixPrescreen("ExtraEvaluation",slot,candidate,outgoing,
                                    context.ReferenceVector,vector,true,accepted,detail);
                                if(accepted)
                                {
                                    context.ReferenceVector=vector;context.ReferenceSelected=proposal;
                                    detail.ExtraAccepted++;improved=true;
                                    if(detail.FirstExtraAcceptedScan==0)
                                    {
                                        detail.FirstExtraAcceptedScan=detail.ExtraScans;
                                        detail.FirstExtraAcceptedEvaluation=detail.ExtraEvaluations;
                                    }
                                    if(_diagnostics!=null)_diagnostics.IntermediatePreparation.ReferenceImprovements++;
                                    break;
                                }
                                if(detail.ExtraEvaluations>=_budget.GuidanceTest.ExtraEvaluationBudget)break;
                            }
                            if(improved || detail.ExtraEvaluations>=_budget.GuidanceTest.ExtraEvaluationBudget)break;
                        }
                        if(!improved){detail.ExtraStopReason="NoImprovementRound";break;}
                    }
                    if(context.ReferenceVector.MissingRequired==0)detail.ExtraStopReason="RequiredGapsFilled";
                    else if(detail.ExtraEvaluations>=_budget.GuidanceTest.ExtraEvaluationBudget)detail.ExtraStopReason="BudgetUsed";
                    context.Reference=context.Schema.ForReference(context.ReferenceVector,
                        _diagnostics==null?null:_diagnostics.IntermediatePreparation);
                    if(_diagnostics!=null)
                    {
                        _diagnostics.IntermediatePreparation.ReferenceMissingRequired=context.ReferenceVector.MissingRequired;
                        detail.FinalMissingRequired=context.ReferenceVector.MissingRequired;
                        int field;var left=context.Schema.ForReference(context.GreedyVector,_diagnostics.IntermediatePreparation);
                        int comparison=context.Schema.Compare(left,null,context.Reference,out field);
                        if(comparison!=int.MinValue)detail.GreedyToFinalPrefixComparison=Math.Sign(comparison);
                    }
                }
                finally
                {
                    long extraTransitions=_budget.TransitionCount-transitions;
                    if(_diagnostics!=null)
                    {
                        detail.ExtraMilliseconds=CustomSearchDiagnostics.MillisecondsSince(started);
                        _diagnostics.IntermediatePreparation.ReferenceTransitions+=extraTransitions;
                        phaseDiagnostics.ReferenceTransitions+=extraTransitions;
                    }
                }
            }
        }
    }
}
