using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using NightreignRelicTool.Core;

namespace NightreignRelicTool.Integration.Tests
{
    public static class SearchImplementationFixTests
    {
        private static int assertions, leaves;
        private const int Low = 930001, High = 930002, Other = 930003, Extra = 930004;
        private const int Exclusive = 930005, Blocker = 930006, Inapplicable = 930007;
        private const int Cap = 930008;
        private static readonly string UnknownWarning = (string)typeof(CustomEffectSearcher)
            .GetField("UnknownRepeatedContributionWarning", BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue();

        public static int Run()
        {
            assertions = 0; leaves = 0;
            var model = RuntimeModel.LoadBuiltIn();
            var catalog = CreateCatalog();
            var source = model.GetCharacter("ironeye");
            var ids = source.EligibleVesselIds.Take(4).ToArray();
            var character = new CharacterDefinition(source.Id, "synthetic", new Dictionary<string, int>(), ids);
            foreach (int id in ids)
            {
                var vessel = model.GetVessel(id);
                for (int i = 0; i < 3; i++)
                    vessel.OrdinarySlotColorIds[i] = vessel.DeepSlotColorIds[i] = 4;
            }
            int[][][] cases = {
                new[]{new[]{High},new[]{Low},new int[0],new int[0],new int[0],new int[0]},
                new[]{new[]{Low},new[]{Low,Extra},new int[0],new int[0],new int[0],new int[0]},
                new[]{new[]{Low},new[]{Low},new int[0],new int[0],new int[0],new int[0]},
                new[]{new[]{High,Other},new[]{Low,Other,Extra},new int[0],new[]{Low,Cap},new[]{Cap},new int[0]},
                new[]{new[]{Low,Low},new[]{Extra},new int[0],new int[0],new int[0],new int[0]},
                new[]{new[]{Low},new[]{Extra},new int[0],new int[0],new int[0],new int[0]},
                new[]{new[]{Exclusive},new[]{Blocker},new[]{High},new[]{Low},new int[0],new int[0]},
                new[]{new[]{Inapplicable},new[]{Inapplicable},new[]{Low},new int[0],new int[0],new int[0]},
                new[]{new[]{High},new[]{Extra},new int[0],new[]{Low,Extra},new[]{Low},new int[0]}
            };
            var rules = new[]{new CustomEffectRule("u","fix:u",true,6),
                new CustomEffectRule("alias","fix:alias",false,3),
                new CustomEffectRule("same-selector","fix:u",false,1),
                new CustomEffectRule("other","fix:other",false),
                new CustomEffectRule("extra","fix:extra",false),
                new CustomEffectRule("exclusive","fix:exclusive",false),
                new CustomEffectRule("inapplicable","fix:inapplicable",false),
                new CustomEffectRule("cap","fix:cap",false,6)};
            var searcher = new CustomEffectSearcher(model,catalog);
            for (int c = 0; c < cases.Length; c++)
            {
                var relics = cases[c].Select((effects,i)=>Relic(i==0?(c==1?10:20):i==1?(c==1?20:10):30+i,i%4,i>=3,effects)).ToArray();
                var inventory = new CharacterInventory(0,"synthetic",0,0,relics,new string[0]);
                var assignments = relics.Select((r,i)=>new SlotAssignment(i>=3,i%3,4,r)).ToArray();
                var direct = searcher.EvaluateBuild(model.GetVessel(ids[0]),assignments,character.Id,rules);
                CheckWarnings(direct,catalog);
                if(c==0 || c==2) Check(direct.ContributionEvaluation.GetRule("u").Occurrences.Single(o=>o.IsEffective).InstanceId==10,"rank or stable identity winner");
                if(c==1) Check(direct.ContributionEvaluation.GetRule("u").Occurrences.Single(o=>o.IsEffective).InstanceId==20,"co-location beats lower instance id");
                if(c==5 || c==7) Check(!direct.Warnings.Any(w=>w.Message==UnknownWarning),"no extra unknown warning");
                var oracle = new List<CustomSearchBuild>();
                foreach(int id in ids)
                {
                    CustomSearchBuild best=null;
                    Enumerate(searcher,model.GetVessel(id),character.Id,rules,relics,new SlotAssignment[6],0,new HashSet<int>(),ref best,catalog);
                    Check(best!=null,"oracle legal completion");oracle.Add(best);
                }
                oracle.Sort(CustomEffectSearcher.CompareBuilds);
                var stable=new StableExactV1(model,catalog);
                var result=stable.Search(inventory,character,rules);
                var repeated=stable.Search(new CharacterInventory(0,"synthetic",0,0,relics.Reverse().ToArray(),new string[0]),character,rules);
                Check(result.IsExact && result.Builds.Length==3,"exact three vessels");
                for(int i=0;i<3;i++)
                {
                    Check(Canonical(result.Builds[i])==Canonical(oracle[i]),"independent complete top three");
                    Check(Canonical(result.Builds[i])==Canonical(repeated.Builds[i]),"stable reversed inventory");
                    CheckWarnings(result.Builds[i],catalog);
                }
                foreach(var replacement in searcher.RankReplacements(inventory,character,rules,result.Builds[0],0))
                    CheckWarnings(replacement,catalog);
                Check(Canonical(stable.Search(inventory,character,rules).Builds)==Canonical(result.Builds),"restore original request");
            }
            Console.WriteLine("SEARCH_IMPLEMENTATION_WARNINGS_PASS cases="+cases.Length+" independentLeaves="+leaves+" assertions="+assertions+" fullTop3=true replacement_restore=true");
            return assertions;
        }

        private static void CheckWarnings(CustomSearchBuild build, CustomEffectCatalog catalog)
        {
            // Expected attribution comes from the formal evaluator, never the
            // warning builder's ordering. Other warning kinds are also checked.
            var expected=new HashSet<string>(build.ContributionEvaluation.Rules.SelectMany(r=>r.Occurrences)
                .Where(o=>o.IsUncertainAdditional).Select(o=>o.InstanceId+"/"+o.RuntimeEffectId));
            var actual=build.Warnings.Where(w=>w.Message==UnknownWarning).ToArray();
            Check(expected.SetEquals(actual.Select(w=>w.InstanceId+"/"+w.RuntimeEffectId)),"unknown warning attribution");
            Check(actual.Length==expected.Count,"deduplicated physical warning");
            var inapplicable=build.Assignments.SelectMany(a=>a.Relic.PositiveEffectIds.Select(e=>new{a.Relic.InstanceId,Effect=catalog.GetEffect(e)}))
                .Where(x=>!x.Effect.IsApplicableTo("ironeye")).Select(x=>x.InstanceId+"/"+x.Effect.RuntimeEffectId);
            Check(new HashSet<string>(inapplicable).SetEquals(build.Warnings.Where(w=>w.Message==catalog.Warning("characterInapplicable")).Select(w=>w.InstanceId+"/"+w.RuntimeEffectId)),"inapplicable warning unchanged");
            var exclusive=build.Assignments.SelectMany(a=>a.Relic.PositiveEffectIds.Select(e=>new{a.Relic.InstanceId,Effect=catalog.GetEffect(e)}))
                .Where(x=>x.Effect.ExclusivityId.HasValue).GroupBy(x=>x.Effect.ExclusivityId.Value).Where(g=>g.Count()>1)
                .SelectMany(g=>g).Select(x=>x.InstanceId+"/"+x.Effect.RuntimeEffectId);
            Check(new HashSet<string>(exclusive).SetEquals(build.Warnings.Where(w=>w.Message==catalog.Warning("runtimeExclusive")).Select(w=>w.InstanceId+"/"+w.RuntimeEffectId)),"exclusive warning unchanged");
        }

        private static void Enumerate(CustomEffectSearcher searcher,VesselDefinition vessel,string character,
            CustomEffectRule[] rules,RelicInstance[] relics,SlotAssignment[] slots,int slot,HashSet<int> used,
            ref CustomSearchBuild best,CustomEffectCatalog catalog)
        {
            if(slot==6)
            {
                var build=searcher.EvaluateBuild(vessel,(SlotAssignment[])slots.Clone(),character,rules);
                leaves++;CheckWarnings(build,catalog);
                if(best==null || CustomEffectSearcher.CompareBuilds(build,best)<0)best=build;
                return;
            }
            bool deep=slot>=3;int color=(deep?vessel.DeepSlotColorIds:vessel.OrdinarySlotColorIds)[slot%3];
            foreach(var relic in relics)
            {
                if(relic.IsDeep!=deep || (color!=4 && relic.ColorId!=color) || !used.Add(relic.InstanceId))continue;
                slots[slot]=new SlotAssignment(deep,slot%3,color,relic);
                Enumerate(searcher,vessel,character,rules,relics,slots,slot+1,used,ref best,catalog);
                used.Remove(relic.InstanceId);
            }
        }

        internal static string Canonical(object value)
        {
            if(value==null)return "null";
            var type=value.GetType();
            if(value is string)return "s"+((string)value).Length+":"+value;
            if(type.IsPrimitive || type.IsEnum || value is decimal)return Convert.ToString(value,CultureInfo.InvariantCulture);
            var list=value as IEnumerable;if(list!=null)return "["+string.Join(";",list.Cast<object>().Select(Canonical))+"]";
            return "{"+string.Join(";",type.GetProperties(BindingFlags.Instance|BindingFlags.Public).Where(p=>p.GetIndexParameters().Length==0)
                .OrderBy(p=>p.Name,StringComparer.Ordinal).Select(p=>p.Name+"="+Canonical(p.GetValue(value,null))))+"}";
        }
        private static void Check(bool ok,string message){assertions++;if(!ok)throw new InvalidOperationException("Search implementation: "+message);}
        private static RelicInstance Relic(int id,int color,bool deep,int[] effects)
        {return new RelicInstance(id,id,(uint)id,id,0,(uint)id,false,false,color,deep,effects,new int[0]);}

        internal static CustomEffectCatalog CreateCatalog()
        {
            var effects=new List<CustomRuntimeEffectDto>();
            foreach(int id in new[]{Low,High,Other,Extra,Exclusive,Blocker,Inapplicable,Cap})
                effects.Add(new CustomRuntimeEffectDto{RuntimeEffectId=id,Name="synthetic",DisplayValue="synthetic",ValueConfirmed=true,
                    ApplicableCharacterIds=id==Inapplicable?new List<string>{"duchess"}:new List<string>(),ApplicabilityConfirmed=id==Inapplicable,
                    StackGroupKey=id==Low||id==High?"u":"group-"+id,AggregationRule=id==Cap?"cap_count":id==Extra?"single_instance":"unknown",
                    MaxEffectiveCopies=id==Cap?2:id==Extra?1:0,ExclusivityId=id==Exclusive||id==Blocker?(int?)88:null,
                    AggregationVerificationStatus="project_verified",AggregationSource="synthetic test",AggregationVersion="1.03.5",
                    VerificationStatus="project_verified",CalculationTerms=new List<CustomCalculationTermDto>()});
            string[] names={"u","alias","other","extra","exclusive","inapplicable","cap"};
            int[][] values={new[]{Low,High},new[]{Low,High},new[]{Other},new[]{Extra},new[]{Exclusive},new[]{Inapplicable},new[]{Cap}};
            var selectors=names.Select((name,i)=>new CustomSelectorDto{Id="fix:"+name,SubcategoryParamId=1,Name="synthetic",RuntimeEffectIds=values[i].ToList(),
                IsAvailableForRealMatching=true,IsOfficialFilter=true,VariantPreferenceRanks=values[i].ToDictionary(id=>id.ToString(),id=>id==High?2:1),
                VariantPreferences=new List<CustomVariantPreferenceDto>()}).ToList();
            var dto=new CustomEffectRuntimeDto{SchemaVersion="nightreign.custom-effect-search.runtime.v2",GameVersion="1.03.5",DataVersion="implementation-fix-synthetic",
                Counts=new CustomEffectCountsDto{PrimaryCategories=1,Categories=1,OfficialFilters=selectors.Count,MechanicalRuntimeEffects=effects.Count},
                PrimaryCategories=new List<CustomPrimaryDto>{new CustomPrimaryDto{Name="synthetic"}},
                Categories=new List<CustomCategoryDto>{new CustomCategoryDto{SubcategoryParamId=1,PrimaryCategory="synthetic",Name="synthetic"}},
                Selectors=selectors,Effects=effects,Bindings=new List<CustomBindingDto>(),Presets=new List<CustomPresetDto>(),
                ExclusivityPolicies=new List<CustomExclusivityDto>{new CustomExclusivityDto{ExclusivityId=88,MaxEffectiveEffects=1,Warning="synthetic",RuntimeEffectIds=new List<int>{Exclusive,Blocker}}},
                WarningMessages=new Dictionary<string,string>{{"characterInapplicable","synthetic-inapplicable"},{"runtimeExclusive","synthetic-exclusive"}}};
            return (CustomEffectCatalog)typeof(CustomEffectCatalog).GetConstructor(BindingFlags.Instance|BindingFlags.NonPublic,null,new[]{typeof(CustomEffectRuntimeDto)},null).Invoke(new object[]{dto});
        }
    }
}
