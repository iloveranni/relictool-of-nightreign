using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NightreignRelicTool.Core;

namespace NightreignRelicTool.Integration.Tests
{
    // Kept in a separate file so the production Program.cs entry point remains
    // untouched.  Run() is intentionally public for the focused reflection test
    // used while developing the frontier compression regression.
    public static class CoLocationFrontierRegressionTests
    {
        private const int LowA = 920001;
        private const int HighA = 920002;
        private const int EffectB = 920003;
        private const int EffectC = 920004;
        private const int ExclusiveA = 920005;
        private const int ExclusiveBlocker = 920006;
        private const int UnknownLowA = 920007;
        private const int UnknownHighA = 920008;
        private const int CapLowA = 920009;
        private const int CapMiddleA = 920010;
        private const int CapHighA = 920011;
        private const int PhaseA = 920012;
        private const int PhaseB = 920013;
        private const int PhaseBridgeA = 920014;
        private const int PhaseC = 920015;
        private const int PhaseRequiredFiller = 920016;
        private const int PhaseRequiredAnchor = 920017;

        public static int Run()
        {
            RuntimeModel model = RuntimeModel.LoadBuiltIn();
            CustomEffectCatalog catalog = CreateCatalog();
            VesselDefinition vessel = model.GetVessel(1002);
            CharacterDefinition character = new CharacterDefinition(
                model.Characters[0].Id, "synthetic", new Dictionary<string, int>(),
                new[] { vessel.Id });
            int assertions = 0;
            assertions += RunAggregationReplacementCase(model, catalog, character, vessel);
            assertions += RunCrossRelicExclusivityCase(model, catalog, character, vessel);
            assertions += RunUnknownReplacementCase(model, catalog, character, vessel);
            assertions += RunCapReplacementCase(model, catalog, character, vessel);
            assertions += RunSameSignatureExclusiveCase(model, catalog, character, vessel);
            assertions += RunSymmetricSlotCase(model);
            assertions += RunComponentPhaseCase(model);
            assertions += RunInterleavedBoundaryComponentCase(model, catalog);
            assertions += RunFourthProjectionRepresentativeCase(model, catalog);
            return assertions;
        }

        private static int RunFourthProjectionRepresentativeCase(
            RuntimeModel model,
            CustomEffectCatalog catalog)
        {
            VesselDefinition vessel = model.GetVessel(1002);
            CharacterDefinition character = new CharacterDefinition(
                model.Characters[0].Id, "synthetic-fourth-projection-representative",
                new Dictionary<string, int>(), new[] { vessel.Id });
            CharacterInventory inventory = new CharacterInventory(
                0, "synthetic-fourth-projection-representative", 0, 0,
                new[]
                {
                    // These four candidates have the same projection.  The first
                    // three win the equal-rank boundary tie against instance 40;
                    // the fourth deliberately loses it, which produces the better
                    // final co-location profile.
                    Relic(10, 0, false, new[] { PhaseBridgeA, PhaseC }),
                    Relic(20, 0, false, new[] { PhaseBridgeA, PhaseC }),
                    Relic(30, 0, false, new[] { PhaseBridgeA, PhaseC }),
                    Relic(50, 0, false, new[] { PhaseBridgeA, PhaseC }),
                    Relic(100, 2, false, new int[0]),
                    // The required filler is the only candidate that can occupy
                    // the ordinary universal slot without leaving a required miss,
                    // so every legal optimum selects exactly one projection twin.
                    Relic(110, 3, false, new[] { PhaseRequiredFiller }),
                    Relic(40, 0, true, new[] { PhaseA, PhaseB }),
                    Relic(120, 1, true, new int[0]),
                    Relic(130, 3, true, new int[0])
                }, new string[0]);
            CustomEffectRule[] rules =
            {
                new CustomEffectRule("projection-required-filler", "test:phase-f", true),
                new CustomEffectRule("projection-a", "test:phase-a", false),
                new CustomEffectRule("projection-b", "test:phase-b", false),
                new CustomEffectRule("projection-c", "test:phase-c", false)
            };

            CustomEffectSearcher searcher = new CustomEffectSearcher(model, catalog);
            CustomSearchBuild brute = BruteForce(
                searcher, model, inventory, character, rules);
            if (brute == null)
                throw new InvalidOperationException("第四个同投影代表反例没有合法组合");
            int[] selected = brute.Assignments.Select(item => item.Relic.InstanceId).ToArray();
            if (!selected.Contains(50)
                || selected.Contains(10) || selected.Contains(20) || selected.Contains(30)
                || !brute.Comparison.CoLocationProfile.DescendingMultiMatchCounts
                    .SequenceEqual(new[] { 2, 2 }))
                throw new InvalidOperationException(
                    "第四个同投影代表反例没有形成预期的 winner 阈值与共现分歧");

            return 3 + AssertSearchEqualsBruteForce(
                model, catalog, character, inventory, rules,
                "只保留最低三个同投影实例时遗漏了边界 winner 阈值另一侧的精确最优解");
        }

        private static int RunInterleavedBoundaryComponentCase(
            RuntimeModel model,
            CustomEffectCatalog catalog)
        {
            VesselDefinition vessel = model.GetVessel(1002);
            CharacterDefinition character = new CharacterDefinition(
                model.Characters[0].Id, "synthetic-interleaved-boundary",
                new Dictionary<string, int>(), new[] { vessel.Id });
            CharacterInventory inventory = new CharacterInventory(
                0, "synthetic-interleaved-boundary", 0, 0,
                new[]
                {
                    Relic(1, 0, false, new[] { PhaseRequiredFiller }),
                    Relic(100, 0, false, new[] { PhaseA, PhaseB }),
                    Relic(200, 2, false, new[] { PhaseA, PhaseB }),
                    Relic(1000, 2, false, new[] { PhaseRequiredFiller }),
                    Relic(500, 3, false, new[] { PhaseRequiredAnchor }),
                    Relic(150, 0, true, new[] { PhaseBridgeA, PhaseC }),
                    Relic(600, 1, true, new int[0]),
                    Relic(700, 3, true, new int[0])
                }, new string[0]);
            CustomEffectRule[] rules =
            {
                new CustomEffectRule("phase-required-x", "test:phase-x", true),
                new CustomEffectRule("phase-required-filler", "test:phase-f", true),
                new CustomEffectRule("phase-required-anchor", "test:phase-z", true),
                new CustomEffectRule("phase-a", "test:phase-a", false),
                new CustomEffectRule("phase-b", "test:phase-b", false),
                new CustomEffectRule("phase-c", "test:phase-c", false)
            };
            int assertions = AssertSearchEqualsBruteForce(
                model, catalog, character, inventory, rules,
                "交错实例 ID 的跨分量 boundary winner 搜索与暴力枚举不一致");
            CustomSearchBuild build = new CustomEffectSearcher(model, catalog)
                .Search(inventory, character, rules).Builds.Single();
            int[] selected = build.Assignments.Select(item => item.Relic.InstanceId).ToArray();
            if (!selected.Contains(100) || selected.Contains(200)
                || !build.Comparison.CoLocationProfile.DescendingMultiMatchCounts
                    .SequenceEqual(new[] { 2, 2 }))
                throw new InvalidOperationException(
                    "交错实例 ID 反例没有保留未来 boundary 扩展所需的较小 winner selected="
                    + string.Join(",", selected) + " co=" + string.Join(",",
                        build.Comparison.CoLocationProfile.DescendingMultiMatchCounts));
            return assertions + 3;
        }

        private static int RunComponentPhaseCase(RuntimeModel model)
        {
            CustomEffectCatalog catalog = CustomEffectCatalog.LoadBuiltIn();
            int effectId = catalog.GetSelector("filter:7001400").RuntimeEffectIds[0];
            CustomEffectRule[] rules =
            {
                new CustomEffectRule("component-phase", "filter:7001400", false)
            };
            int assertions = 0;

            VesselDefinition separated = model.GetVessel(1000);
            CharacterDefinition separatedCharacter = new CharacterDefinition(
                model.Characters[0].Id, "synthetic-separated-components",
                new Dictionary<string, int>(), new[] { separated.Id });
            CharacterInventory separatedInventory = new CharacterInventory(
                0, "synthetic-separated-components", 0, 0,
                new[]
                {
                    Relic(810, 0, false, new int[0]),
                    Relic(811, 0, false, new[] { effectId }),
                    Relic(812, 0, false, new int[0]),
                    Relic(820, 1, false, new int[0]),
                    Relic(821, 1, false, new[] { effectId }),
                    Relic(910, 0, true, new int[0]),
                    Relic(911, 0, true, new[] { effectId }),
                    Relic(912, 0, true, new int[0]),
                    Relic(920, 1, true, new int[0]),
                    Relic(921, 1, true, new[] { effectId })
                }, new string[0]);
            assertions += AssertSearchEqualsBruteForce(
                model, catalog, separatedCharacter, separatedInventory,
                new CustomEffectRule[0],
                "多颜色独立连通分量的零规则搜索与暴力枚举不一致");
            assertions += AssertSearchEqualsBruteForce(
                model, catalog, separatedCharacter, separatedInventory, rules,
                "多颜色独立连通分量的规则搜索与暴力枚举不一致");

            VesselDefinition bridged = model.GetVessel(1002);
            CharacterDefinition bridgedCharacter = new CharacterDefinition(
                model.Characters[0].Id, "synthetic-wildcard-bridge",
                new Dictionary<string, int>(), new[] { bridged.Id });
            CharacterInventory bridgedInventory = new CharacterInventory(
                0, "synthetic-wildcard-bridge", 0, 0,
                new[]
                {
                    Relic(1010, 0, false, new[] { effectId }),
                    Relic(1011, 0, false, new int[0]),
                    Relic(1020, 2, false, new[] { effectId }),
                    Relic(1021, 2, false, new int[0]),
                    Relic(1030, 1, false, new[] { effectId }),
                    Relic(1031, 3, false, new int[0]),
                    Relic(1110, 0, true, new[] { effectId }),
                    Relic(1111, 0, true, new int[0]),
                    Relic(1120, 1, true, new[] { effectId }),
                    Relic(1121, 1, true, new int[0]),
                    Relic(1130, 3, true, new[] { effectId }),
                    Relic(1131, 3, true, new int[0])
                }, new string[0]);
            assertions += AssertSearchEqualsBruteForce(
                model, catalog, bridgedCharacter, bridgedInventory,
                new CustomEffectRule[0],
                "万能色槽桥接分量的零规则搜索与暴力枚举不一致");
            assertions += AssertSearchEqualsBruteForce(
                model, catalog, bridgedCharacter, bridgedInventory, rules,
                "万能色槽桥接分量的规则搜索与暴力枚举不一致");
            return assertions;
        }

        private static int RunSymmetricSlotCase(RuntimeModel model)
        {
            CustomEffectCatalog catalog = CustomEffectCatalog.LoadBuiltIn();
            VesselDefinition vessel = model.GetVessel(19010);
            CharacterDefinition character = new CharacterDefinition(
                model.Characters[0].Id, "synthetic-symmetric",
                new Dictionary<string, int>(), new[] { vessel.Id });
            int effectId = catalog.GetSelector("filter:7001400").RuntimeEffectIds[0];
            CharacterInventory inventory = new CharacterInventory(
                0, "synthetic-symmetric", 0, 0,
                new[]
                {
                    Relic(610, 0, false, new int[0]),
                    Relic(605, 0, false, new[] { effectId }),
                    Relic(620, 0, false, new int[0]),
                    Relic(615, 0, false, new[] { effectId }),
                    Relic(710, 0, true, new int[0]),
                    Relic(705, 0, true, new[] { effectId }),
                    Relic(720, 0, true, new int[0]),
                    Relic(715, 0, true, new[] { effectId })
                }, new string[0]);
            int assertions = AssertSearchEqualsBruteForce(
                model, catalog, character, inventory, new CustomEffectRule[0],
                "零规则同色槽位对称消除与暴力枚举不一致");
            CustomEffectRule[] rules =
            {
                new CustomEffectRule("symmetric", "filter:7001400", false)
            };
            assertions += AssertSearchEqualsBruteForce(
                model, catalog, character, inventory, rules,
                "非零规则同色槽位对称消除与暴力枚举不一致");

            CustomEffectSearcher searcher = new CustomEffectSearcher(model, catalog);
            CustomSearchBuild build = searcher.Search(inventory, character, rules).Builds.Single();
            int[] ordinary = build.Assignments.Take(3)
                .Select(item => item.Relic.InstanceId).ToArray();
            int[] deep = build.Assignments.Skip(3)
                .Select(item => item.Relic.InstanceId).ToArray();
            if (!ordinary.SequenceEqual(ordinary.OrderBy(value => value))
                || !deep.SequenceEqual(deep.OrderBy(value => value)))
                throw new InvalidOperationException("同色槽位没有保留实例号升序稳定分配");
            return assertions + 2;
        }

        private static int RunAggregationReplacementCase(
            RuntimeModel model,
            CustomEffectCatalog catalog,
            CharacterDefinition character,
            VesselDefinition vessel)
        {
            CharacterInventory inventory = CreateInventory(vessel,
                new[] { LowA, EffectB }, new[] { EffectC },
                new[] { LowA }, new[] { EffectB, EffectC }, new[] { HighA });
            CustomEffectRule[] rules =
            {
                new CustomEffectRule("a", "test:a", false),
                new CustomEffectRule("b", "test:b", false),
                new CustomEffectRule("c", "test:c", false)
            };
            int assertions = AssertBoundaryBehavior(
                model, catalog, character, inventory, rules,
                "single_instance 高档替换反例没有形成预期的共现分歧");
            return assertions + AssertSearchEqualsBruteForce(
                model, catalog, character, inventory, rules,
                "跨遗物 single_instance 高档替换改变旧颗共现时，压缩搜索与暴力枚举不一致");
        }

        private static int RunCrossRelicExclusivityCase(
            RuntimeModel model,
            CustomEffectCatalog catalog,
            CharacterDefinition character,
            VesselDefinition vessel)
        {
            CharacterInventory inventory = CreateInventory(vessel,
                new[] { ExclusiveA, EffectB }, new[] { EffectC },
                new[] { ExclusiveA }, new[] { EffectB, EffectC },
                new[] { ExclusiveBlocker });
            CustomEffectRule[] rules =
            {
                new CustomEffectRule("exclusive-a", "test:exclusive-a", false),
                new CustomEffectRule("b", "test:b", false),
                new CustomEffectRule("c", "test:c", false)
            };
            int assertions = AssertBoundaryBehavior(
                model, catalog, character, inventory, rules,
                "跨遗物互斥 blocker 反例没有形成预期的共现分歧");
            return assertions + AssertSearchEqualsBruteForce(
                model, catalog, character, inventory, rules,
                "跨遗物互斥 blocker 改变旧颗共现时，压缩搜索与暴力枚举不一致");
        }

        private static int RunUnknownReplacementCase(
            RuntimeModel model,
            CustomEffectCatalog catalog,
            CharacterDefinition character,
            VesselDefinition vessel)
        {
            CharacterInventory inventory = CreateInventory(vessel,
                new[] { UnknownHighA, EffectB }, new[] { EffectC },
                new[] { UnknownHighA }, new[] { EffectB, EffectC },
                new[] { UnknownLowA });
            CustomEffectRule[] rules =
            {
                new CustomEffectRule("unknown-a", "test:unknown-a", false),
                new CustomEffectRule("b", "test:b", false),
                new CustomEffectRule("c", "test:c", false)
            };
            int assertions = AssertBoundaryBehavior(
                model, catalog, character, inventory, rules,
                "unknown conservative winner 替换反例没有形成预期的共现分歧");
            return assertions + AssertSearchEqualsBruteForce(
                model, catalog, character, inventory, rules,
                "unknown 只保留 conservative winner 后，压缩搜索与暴力枚举不一致");
        }

        private static int RunCapReplacementCase(
            RuntimeModel model,
            CustomEffectCatalog catalog,
            CharacterDefinition character,
            VesselDefinition vessel)
        {
            CharacterInventory inventory = CreateInventory(vessel,
                new[] { CapLowA, EffectB }, new[] { EffectC },
                new[] { CapLowA }, new[] { EffectB, EffectC },
                new[] { CapMiddleA, CapHighA });
            CustomEffectRule[] rules =
            {
                new CustomEffectRule("cap-a", "test:cap-a", false),
                new CustomEffectRule("b", "test:b", false),
                new CustomEffectRule("c", "test:c", false)
            };
            int assertions = AssertBoundaryBehavior(
                model, catalog, character, inventory, rules,
                "cap_count top-K 挤出旧 occurrence 的反例没有形成预期共现分歧");
            return assertions + AssertSearchEqualsBruteForce(
                model, catalog, character, inventory, rules,
                "cap_count 只保留 top-K 后，压缩搜索与暴力枚举不一致");
        }

        private static int RunSameSignatureExclusiveCase(
            RuntimeModel model,
            CustomEffectCatalog catalog,
            CharacterDefinition character,
            VesselDefinition vessel)
        {
            RelicInstance one = Relic(100, 0, false, new[] { ExclusiveA, EffectB });
            RelicInstance many = Relic(200, 0, false,
                new[] { ExclusiveA, ExclusiveA, EffectB });
            RelicInstance second = Relic(300, 2, false, new int[0]);
            RelicInstance third = Relic(301, 3, false, new int[0]);
            CharacterInventory inventory = new CharacterInventory(
                0, "synthetic-exclusive-same", 0, 0,
                new[]
                {
                    one, many, second, third,
                    Relic(400, 0, true, new int[0]),
                    Relic(401, 1, true, new int[0]),
                    Relic(402, 3, true, new int[0])
                }, new string[0]);
            CustomEffectRule[] rules =
            {
                new CustomEffectRule("exclusive-a", "test:exclusive-a", false),
                new CustomEffectRule("b", "test:b", false)
            };
            CustomEffectSearcher searcher = new CustomEffectSearcher(model, catalog);
            CustomSearchBuild oneBuild = searcher.EvaluateBuild(vessel,
                Assign(vessel, inventory, 100, 300, 301), character.Id, rules);
            CustomSearchBuild manyBuild = searcher.EvaluateBuild(vessel,
                Assign(vessel, inventory, 200, 300, 301), character.Id, rules);
            EffectiveRuleContribution manyExclusive =
                manyBuild.ContributionEvaluation.Rules[0];
            if (!oneBuild.Comparison.CoLocationProfile.DescendingMultiMatchCounts
                    .SequenceEqual(new[] { 2 })
                || manyBuild.Comparison.CoLocationProfile.DescendingMultiMatchCounts.Length != 0
                || manyExclusive.Occurrences.Count(item => item.IsEffective) != 1
                || manyExclusive.Occurrences.Where(item => item.IsEffective)
                    .Any(item => item.IsPhysicalLocationConfirmed))
                throw new InvalidOperationException(
                    "同签名两个物理互斥项没有进入 many 并失去物理共现位置");
            return 4 + AssertSearchEqualsBruteForce(
                model, catalog, character, inventory, rules,
                "同签名两个物理互斥项的优化搜索与暴力枚举不一致");
        }

        private static int AssertBoundaryBehavior(
            RuntimeModel model,
            CustomEffectCatalog catalog,
            CharacterDefinition character,
            CharacterInventory inventory,
            CustomEffectRule[] rules,
            string message)
        {
            CustomEffectSearcher searcher = new CustomEffectSearcher(model, catalog);
            VesselDefinition vessel = model.GetVessel(character.EligibleVesselIds[0]);
            CustomSearchBuild first = searcher.EvaluateBuild(vessel,
                Assign(vessel, inventory, 100, 101, 300), character.Id, rules);
            CustomSearchBuild second = searcher.EvaluateBuild(vessel,
                Assign(vessel, inventory, 200, 201, 300), character.Id, rules);
            if (!first.Comparison.RawOccurrences.SequenceEqual(second.Comparison.RawOccurrences)
                || !first.Comparison.EffectiveContributions.SequenceEqual(
                    second.Comparison.EffectiveContributions)
                || first.Comparison.CoLocationProfile.DescendingMultiMatchCounts.Length != 0
                || !second.Comparison.CoLocationProfile.DescendingMultiMatchCounts
                    .SequenceEqual(new[] { 2 })
                || CustomEffectSearcher.CompareBuilds(second, first) >= 0)
                throw new InvalidOperationException(message + " raw="
                    + string.Join(",", first.Comparison.RawOccurrences) + "/"
                    + string.Join(",", second.Comparison.RawOccurrences) + " effective="
                    + string.Join(",", first.Comparison.EffectiveContributions) + "/"
                    + string.Join(",", second.Comparison.EffectiveContributions) + " co="
                    + string.Join(",", first.Comparison.CoLocationProfile.DescendingMultiMatchCounts)
                    + "/" + string.Join(",", second.Comparison.CoLocationProfile.DescendingMultiMatchCounts)
                    + " compare=" + CustomEffectSearcher.CompareBuilds(second, first));
            return 5;
        }

        private static SlotAssignment[] Assign(
            VesselDefinition vessel,
            CharacterInventory inventory,
            int first,
            int second,
            int future)
        {
            int[] ids = { first, second, future, 400, 401, 402 };
            SlotAssignment[] result = new SlotAssignment[6];
            for (int slot = 0; slot < result.Length; slot++)
            {
                bool deep = slot >= 3;
                int local = deep ? slot - 3 : slot;
                int color = deep
                    ? vessel.DeepSlotColorIds[local]
                    : vessel.OrdinarySlotColorIds[local];
                result[slot] = new SlotAssignment(deep, local, color,
                    inventory.Relics.Single(item => item.InstanceId == ids[slot]));
            }
            return result;
        }

        private static int AssertSearchEqualsBruteForce(
            RuntimeModel model,
            CustomEffectCatalog catalog,
            CharacterDefinition character,
            CharacterInventory inventory,
            CustomEffectRule[] rules,
            string message)
        {
            CustomEffectSearcher searcher = new CustomEffectSearcher(model, catalog);
            CustomSearchResponse optimized = searcher.Search(inventory, character, rules);
            CustomSearchBuild brute = BruteForce(searcher, model, inventory, character, rules);
            var stable = new StableExactV1(model, catalog).Search(inventory, character, rules);
            if (!stable.IsExact || stable.Builds.Length != 1 || brute == null
                || CustomEffectSearcher.CompareBuilds(stable.Builds[0], brute) != 0
                || !stable.Builds[0].Assignments.Select(item => item.Relic.InstanceId)
                    .SequenceEqual(brute.Assignments.Select(item => item.Relic.InstanceId)))
                throw new InvalidOperationException("正式两半布局: " + message);
            if (optimized.Status != CustomSearchStatus.CompletedExact
                || optimized.Builds.Length != 1 || brute == null)
                throw new InvalidOperationException(message + "（未精确完成）");
            CustomSearchBuild actual = optimized.Builds[0];
            if (CustomEffectSearcher.CompareBuilds(actual, brute) != 0
                || !actual.Assignments.Select(item => item.Relic.InstanceId)
                    .SequenceEqual(brute.Assignments.Select(item => item.Relic.InstanceId)))
                throw new InvalidOperationException(message);
            return 2;
        }

        private static CustomSearchBuild BruteForce(
            CustomEffectSearcher searcher,
            RuntimeModel model,
            CharacterInventory inventory,
            CharacterDefinition character,
            CustomEffectRule[] rules)
        {
            VesselDefinition vessel = model.GetVessel(character.EligibleVesselIds[0]);
            SlotAssignment[] current = new SlotAssignment[6];
            HashSet<int> used = new HashSet<int>();
            CustomSearchBuild best = null;
            Enumerate(0, vessel, inventory.Relics, current, used, searcher,
                character.Id, rules, ref best);
            return best;
        }

        private static void Enumerate(
            int slot,
            VesselDefinition vessel,
            RelicInstance[] relics,
            SlotAssignment[] current,
            HashSet<int> used,
            CustomEffectSearcher searcher,
            string characterId,
            CustomEffectRule[] rules,
            ref CustomSearchBuild best)
        {
            if (slot == 6)
            {
                CustomSearchBuild contender = searcher.EvaluateBuild(
                    vessel, (SlotAssignment[])current.Clone(), characterId, rules);
                if (best == null || CustomEffectSearcher.CompareBuilds(contender, best) < 0)
                    best = contender;
                return;
            }
            bool deep = slot >= 3;
            int local = deep ? slot - 3 : slot;
            int slotColor = deep
                ? vessel.DeepSlotColorIds[local]
                : vessel.OrdinarySlotColorIds[local];
            foreach (RelicInstance relic in relics)
            {
                if (relic.IsDeep != deep || used.Contains(relic.InstanceId)
                    || relic.ColorId < 0 || relic.ColorId > 3
                    || slotColor != 4 && slotColor != relic.ColorId)
                    continue;
                used.Add(relic.InstanceId);
                current[slot] = new SlotAssignment(deep, local, slotColor, relic);
                Enumerate(slot + 1, vessel, relics, current, used, searcher,
                    characterId, rules, ref best);
                used.Remove(relic.InstanceId);
            }
        }

        private static CharacterInventory CreateInventory(
            VesselDefinition vessel,
            int[] firstLow,
            int[] secondLow,
            int[] firstHigh,
            int[] secondHigh,
            int[] future)
        {
            List<RelicInstance> relics = new List<RelicInstance>
            {
                Relic(100, 0, false, firstLow),
                Relic(101, 2, false, secondLow),
                Relic(200, 0, false, firstHigh),
                Relic(201, 2, false, secondHigh),
                Relic(300, 3, false, future),
                Relic(400, 0, true, new int[0]),
                Relic(401, 1, true, new int[0]),
                Relic(402, 3, true, new int[0])
            };
            return new CharacterInventory(
                0, "synthetic", 0, 0, relics.ToArray(), new string[0]);
        }

        private static RelicInstance Relic(int id, int color, bool deep, int[] effects)
        {
            return new RelicInstance(id, id, (uint)(id + 1), id, 0,
                (uint)(id + 100), false, false, color, deep,
                effects ?? new int[0], new int[0]);
        }

        private static CustomEffectCatalog CreateCatalog()
        {
            List<CustomRuntimeEffectDto> effects = new List<CustomRuntimeEffectDto>
            {
                Effect(LowA, "a-low", "a", "single_instance", 1, null),
                Effect(HighA, "a-high", "a", "single_instance", 1, null),
                Effect(EffectB, "b", "b", "single_instance", 1, null),
                Effect(EffectC, "c", "c", "cap_count", 6, null),
                Effect(ExclusiveA, "exclusive-a", "exclusive-a", "single_instance", 1, 77),
                Effect(ExclusiveBlocker, "exclusive-blocker", "exclusive-blocker",
                    "single_instance", 1, 77),
                Effect(UnknownLowA, "unknown-a-low", "unknown-a", "unknown", 0, null),
                Effect(UnknownHighA, "unknown-a-high", "unknown-a", "unknown", 0, null),
                Effect(CapLowA, "cap-a-low", "cap-a", "cap_count", 2, null),
                Effect(CapMiddleA, "cap-a-middle", "cap-a", "cap_count", 2, null),
                Effect(CapHighA, "cap-a-high", "cap-a", "cap_count", 2, null),
                Effect(PhaseA, "phase-a", "phase-a", "unknown", 0, null),
                Effect(PhaseB, "phase-b", "phase-b", "cap_count", 6, null),
                Effect(PhaseBridgeA, "phase-bridge-a", "phase-a", "unknown", 0, null),
                Effect(PhaseC, "phase-c", "phase-c", "cap_count", 6, null),
                Effect(PhaseRequiredFiller, "phase-f", "phase-f", "cap_count", 6, null),
                Effect(PhaseRequiredAnchor, "phase-z", "phase-z", "cap_count", 6, null)
            };
            List<CustomSelectorDto> selectors = new List<CustomSelectorDto>
            {
                Selector("test:a", new[] { LowA, HighA },
                    new Dictionary<string, int>
                    {
                        { LowA.ToString(), 1 }, { HighA.ToString(), 2 }
                    }),
                Selector("test:b", new[] { EffectB }, null),
                Selector("test:c", new[] { EffectC }, null),
                Selector("test:exclusive-a", new[] { ExclusiveA }, null),
                Selector("test:unknown-a", new[] { UnknownLowA, UnknownHighA },
                    new Dictionary<string, int>
                    {
                        { UnknownLowA.ToString(), 1 }, { UnknownHighA.ToString(), 2 }
                    }),
                Selector("test:cap-a", new[] { CapLowA, CapMiddleA, CapHighA },
                    new Dictionary<string, int>
                    {
                        { CapLowA.ToString(), 1 }, { CapMiddleA.ToString(), 2 },
                        { CapHighA.ToString(), 3 }
                    }),
                Selector("test:phase-a", new[] { PhaseA, PhaseBridgeA },
                    new Dictionary<string, int>
                    {
                        { PhaseA.ToString(), 1 }, { PhaseBridgeA.ToString(), 1 }
                    }),
                Selector("test:phase-x", new[] { PhaseB }, null),
                Selector("test:phase-b", new[] { PhaseB, PhaseBridgeA }, null),
                Selector("test:phase-c", new[] { PhaseC }, null),
                Selector("test:phase-f", new[] { PhaseRequiredFiller }, null),
                Selector("test:phase-z", new[] { PhaseRequiredAnchor }, null)
            };
            CustomEffectRuntimeDto dto = new CustomEffectRuntimeDto
            {
                SchemaVersion = "nightreign.custom-effect-search.runtime.v2",
                GameVersion = "1.03.5",
                DataVersion = "co-location-frontier-regression-v1",
                Counts = new CustomEffectCountsDto
                {
                    PrimaryCategories = 1,
                    Categories = 1,
                    OfficialFilters = selectors.Count,
                    IncludedEffectBindings = 0,
                    OfficialPresetRelics = 0,
                    MechanicalRuntimeEffects = effects.Count
                },
                PrimaryCategories = new List<CustomPrimaryDto>
                {
                    new CustomPrimaryDto { Name = "synthetic", SortOrder = 0 }
                },
                Categories = new List<CustomCategoryDto>
                {
                    new CustomCategoryDto
                    {
                        SubcategoryParamId = 1,
                        PrimaryCategory = "synthetic",
                        Name = "synthetic",
                        SortOrder = 0
                    }
                },
                Selectors = selectors,
                Bindings = new List<CustomBindingDto>(),
                Effects = effects,
                Presets = new List<CustomPresetDto>(),
                ExclusivityPolicies = new List<CustomExclusivityDto>
                {
                    new CustomExclusivityDto
                    {
                        ExclusivityId = 77,
                        MaxEffectiveEffects = 1,
                        IsHardLegalityConstraint = false,
                        Warning = "synthetic",
                        RuntimeEffectIds = new List<int> { ExclusiveA, ExclusiveBlocker }
                    }
                },
                WarningMessages = new Dictionary<string, string>()
            };
            ConstructorInfo constructor = typeof(CustomEffectCatalog).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(CustomEffectRuntimeDto) }, null);
            return (CustomEffectCatalog)constructor.Invoke(new object[] { dto });
        }

        private static CustomRuntimeEffectDto Effect(
            int id,
            string name,
            string stackGroup,
            string aggregation,
            int cap,
            int? exclusivityId)
        {
            return new CustomRuntimeEffectDto
            {
                RuntimeEffectId = id,
                Name = name,
                DisplayValue = "synthetic",
                ValueConfirmed = true,
                IsNegative = false,
                ApplicableCharacterIds = new List<string>(),
                ApplicabilityConfirmed = false,
                ExclusivityId = exclusivityId,
                StackGroupKey = stackGroup,
                AggregationRule = aggregation,
                MaxEffectiveCopies = cap,
                AggregationVerificationStatus = "project_verified",
                AggregationSource = "synthetic regression fixture",
                AggregationVersion = "1.03.5",
                VerificationStatus = "project_verified",
                UncertaintyCount = 0,
                CalculationTerms = new List<CustomCalculationTermDto>()
            };
        }

        private static CustomSelectorDto Selector(
            string id,
            int[] runtimeEffectIds,
            Dictionary<string, int> ranks)
        {
            return new CustomSelectorDto
            {
                Id = id,
                FilterParamId = null,
                SubcategoryParamId = 1,
                Name = id,
                SortOrder = 0,
                RuntimeEffectIds = runtimeEffectIds.ToList(),
                IsAvailableForRealMatching = true,
                IsOfficialFilter = true,
                VariantPreferenceRanks = ranks ?? new Dictionary<string, int>(),
                VariantPreferences = new List<CustomVariantPreferenceDto>()
            };
        }
    }
}
