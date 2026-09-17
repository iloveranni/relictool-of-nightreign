using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace NightreignRelicTool.Core
{
    public sealed class RelicOptimizer
    {
        private readonly RuntimeModel _model;

        public RelicOptimizer(RuntimeModel model)
        {
            _model = model ?? throw new ArgumentNullException("model");
        }

        public OptimizationResult Optimize(
            CharacterInventory inventory,
            CharacterDefinition character,
            ArchetypeDefinition archetype,
            ArchetypeOptionDefinition option)
        {
            ValidateSelection(inventory, character, archetype, option);
            Stopwatch stopwatch = Stopwatch.StartNew();
            PreparedOption prepared = new PreparedOption(_model, character, option);
            Candidate[] candidates = PrepareCandidates(inventory.Relics, prepared);
            BestBuild best = null;
            long evaluated = 0;

            foreach (int vesselId in character.EligibleVesselIds)
            {
                VesselDefinition vessel = _model.GetVessel(vesselId);
                if (vessel == null) continue;
                VesselSearch search = new VesselSearch(_model, prepared, candidates, vessel, best);
                search.Run();
                evaluated += search.EvaluatedBuilds;
                if (search.Best != null && (best == null || Compare(search.Best, best) < 0))
                    best = search.Best;
            }

            stopwatch.Stop();
            if (best == null)
                throw new InvalidOperationException("当前仓库没有满足三颗普通、三颗深夜及器皿颜色限制的合法组合。");
            return best.ToResult(stopwatch.ElapsedMilliseconds, evaluated);
        }

        public OptimizationResult[] RankReplacements(
            CharacterInventory inventory,
            CharacterDefinition character,
            ArchetypeDefinition archetype,
            ArchetypeOptionDefinition option,
            OptimizationResult current,
            int assignmentIndex)
        {
            ValidateSelection(inventory, character, archetype, option);
            if (current == null) throw new ArgumentNullException("current");
            if (assignmentIndex < 0 || assignmentIndex >= current.Assignments.Length)
                throw new ArgumentOutOfRangeException("assignmentIndex");

            PreparedOption prepared = new PreparedOption(_model, character, option);
            SlotAssignment target = current.Assignments[assignmentIndex];
            HashSet<int> used = new HashSet<int>(current.Assignments
                .Where((item, index) => index != assignmentIndex)
                .Select(item => item.Relic.InstanceId));
            List<BestBuild> builds = new List<BestBuild>();
            foreach (RelicInstance relic in inventory.Relics)
            {
                if (relic.IsDeep != target.IsDeep || used.Contains(relic.InstanceId)) continue;
                if (!AcceptsColor(target.SlotColorId, relic.ColorId)) continue;
                SlotAssignment[] assignments = (SlotAssignment[])current.Assignments.Clone();
                assignments[assignmentIndex] = new SlotAssignment(
                    target.IsDeep,
                    target.SlotIndex,
                    target.SlotColorId,
                    relic);
                ScoreBreakdown score = prepared.Evaluate(assignments.Select(item => item.Relic));
                builds.Add(new BestBuild(current.Vessel, assignments, score));
            }
            builds.Sort(Compare);
            return builds
                .Select(item => item.ToResult(0, builds.Count))
                .ToArray();
        }

        public ScoreBreakdown Evaluate(
            IEnumerable<RelicInstance> relics,
            CharacterDefinition character,
            ArchetypeOptionDefinition option)
        {
            return new PreparedOption(_model, character, option).Evaluate(relics);
        }

        private static void ValidateSelection(
            CharacterInventory inventory,
            CharacterDefinition character,
            ArchetypeDefinition archetype,
            ArchetypeOptionDefinition option)
        {
            if (inventory == null) throw new ArgumentNullException("inventory");
            if (character == null) throw new ArgumentNullException("character");
            if (archetype == null) throw new ArgumentNullException("archetype");
            if (option == null) throw new ArgumentNullException("option");
            if (!string.Equals(character.Id, archetype.CharacterId, StringComparison.Ordinal))
                throw new ArgumentException("所选流派不属于当前游戏角色。");
            if (!archetype.Options.Contains(option))
                throw new ArgumentException("所选二级流派不属于当前一级流派。");
        }

        private static Candidate[] PrepareCandidates(RelicInstance[] relics, PreparedOption prepared)
        {
            List<Candidate> result = new List<Candidate>(relics.Length);
            foreach (RelicInstance relic in relics)
            {
                if (relic.ColorId < 0 || relic.ColorId > 3) continue;
                result.Add(new Candidate(result.Count, relic, prepared));
            }
            return result.ToArray();
        }

        private static bool AcceptsColor(int slotColorId, int relicColorId)
        {
            return relicColorId >= 0 && relicColorId <= 3
                && (slotColorId == 4 || slotColorId == relicColorId);
        }

        private static int Compare(BestBuild left, BestBuild right)
        {
            int value = right.Score.TotalScore.CompareTo(left.Score.TotalScore);
            if (value != 0) return value;
            value = left.Score.NegativePenalty.CompareTo(right.Score.NegativePenalty);
            if (value != 0) return value;
            value = right.Score.VerifiedPositiveCount.CompareTo(left.Score.VerifiedPositiveCount);
            if (value != 0) return value;
            value = right.Score.CompletedCoreCount.CompareTo(left.Score.CompletedCoreCount);
            if (value != 0) return value;
            value = left.Vessel.Id.CompareTo(right.Vessel.Id);
            if (value != 0) return value;
            for (int index = 0; index < left.Assignments.Length; index++)
            {
                value = left.Assignments[index].Relic.InstanceId.CompareTo(right.Assignments[index].Relic.InstanceId);
                if (value != 0) return value;
            }
            return 0;
        }

        private sealed class PreparedOption
        {
            private readonly RuntimeModel _model;
            private readonly ArchetypeOptionDefinition _option;
            private readonly int[] _positiveContribution;
            private readonly int[] _deepContribution;
            private readonly byte[] _coreMask;
            private readonly FocusMask[] _positiveFocus;
            private readonly bool[] _verified;
            private readonly int[] _caps;
            private readonly int[] _negativeBasePenalty;
            private readonly int[] _positiveCounts;
            private readonly int[] _negativeCounts;
            private readonly int[] _touched;

            public PreparedOption(RuntimeModel model, CharacterDefinition character, ArchetypeOptionDefinition option)
            {
                _model = model;
                _option = option;
                int effectCount = model.Effects.Length;
                _positiveContribution = new int[effectCount];
                _deepContribution = new int[effectCount];
                _coreMask = new byte[effectCount];
                _positiveFocus = new FocusMask[effectCount];
                _verified = new bool[effectCount];
                _caps = new int[effectCount];
                _negativeBasePenalty = new int[effectCount];
                _positiveCounts = new int[effectCount];
                _negativeCounts = new int[effectCount];
                _touched = new int[effectCount];

                if (model.IsDirectScoreModel)
                {
                    if (option.DirectScores == null)
                        throw new InvalidOperationException("所选流派缺少查表评分行。");
                    for (int index = 0; index < effectCount; index++)
                    {
                        EffectDefinition effect = model.Effects[index];
                        _caps[index] = effect.MaxUsefulCopies;
                        _positiveContribution[index] = option.DirectScores[effect.OrdinaryScoreColumn];
                        _deepContribution[index] = option.DirectScores[effect.DeepScoreColumn];
                        _verified[index] = effect.VerificationConfidence > 0;
                    }
                    return;
                }

                List<WeightedFocus> weightedFocuses = option.FocusWeights
                    .Select(item => new WeightedFocus(model.MakeFocusMask(new[] { item.Key }), item.Value))
                    .ToList();
                for (int index = 0; index < effectCount; index++)
                {
                    EffectDefinition effect = model.Effects[index];
                    _caps[index] = effect.MaxUsefulCopies;
                    bool characterAllowed = effect.Family.CharacterIds.Count == 0
                        || effect.Family.CharacterIds.Contains(character.Id);
                    if (characterAllowed)
                    {
                        int weight = 0;
                        foreach (WeightedFocus focus in weightedFocuses)
                            if (effect.Family.PositiveFocusMask.Intersects(focus.Mask) && focus.Weight > weight)
                                weight = focus.Weight;
                        long numerator = (long)weight
                            * effect.PotencyFactor
                            * effect.TriggerReliability
                            * effect.RuntimeScoreFactor;
                        _positiveContribution[index] = RoundNonNegative(numerator, 1000000);
                        _positiveFocus[index] = effect.Family.PositiveFocusMask;
                        _verified[index] = effect.VerificationConfidence > 0
                            && effect.RuntimeScoreFactor > 0
                            && _positiveContribution[index] > 0;
                        for (int group = 0; group < option.CoreGroups.Length; group++)
                            if (effect.Family.PositiveFocusMask.Intersects(option.CoreGroups[group].FocusMask))
                                _coreMask[index] |= (byte)(1 << group);
                    }

                    NegativeRuleDefinition negative = model.NegativeRules[index];
                    if (negative != null)
                    {
                        long weightedRiskSum = 0;
                        foreach (KeyValuePair<string, int> riskWeight in negative.RiskWeights)
                        {
                            int baseRisk;
                            character.Risk.TryGetValue(riskWeight.Key, out baseRisk);
                            int modifier;
                            option.RiskModifiers.TryGetValue(riskWeight.Key, out modifier);
                            int adjusted = Math.Max(0, Math.Min(100, baseRisk + modifier));
                            weightedRiskSum += (long)adjusted * riskWeight.Value;
                        }
                        int weightedRisk = RoundNonNegative(weightedRiskSum, 100);
                        _negativeBasePenalty[index] = RoundNonNegative(
                            (long)negative.BasePenalty * (50 + weightedRisk),
                            100);
                    }
                }
            }

            public int Contribution(int effectIndex, bool isDeep)
            {
                return _model.IsDirectScoreModel && isDeep
                    ? _deepContribution[effectIndex]
                    : _positiveContribution[effectIndex];
            }
            public int Cap(int effectIndex) { return _caps[effectIndex]; }
            public bool UsesDirectScores { get { return _model.IsDirectScoreModel; } }
            public byte CoreMask(int effectIndex) { return _coreMask[effectIndex]; }
            public FocusMask PositiveFocus(int effectIndex) { return _positiveFocus[effectIndex]; }
            public bool IsVerified(int effectIndex, bool isDeep)
            {
                return _verified[effectIndex] && Contribution(effectIndex, isDeep) > 0;
            }

            public int ResolveEffectIndex(int effectId)
            {
                EffectDefinition effect = _model.GetEffect(effectId);
                return effect == null ? -1 : effect.RuntimeIndex;
            }

            public ScoreBreakdown Evaluate(IEnumerable<RelicInstance> relics)
            {
                int positiveScore = 0;
                int verifiedCount = 0;
                int coreMask = 0;
                FocusMask focusMask = new FocusMask();
                int touchedPositive = 0;
                int touchedNegative = 0;

                foreach (RelicInstance relic in relics)
                {
                    foreach (int effectId in relic.PositiveEffectIds)
                    {
                        int index = ResolveEffectIndex(effectId);
                        if (index < 0) continue;
                        if (_positiveCounts[index]++ == 0) _touched[touchedPositive++] = index;
                        if (_positiveCounts[index] <= _caps[index])
                        {
                            positiveScore += Contribution(index, relic.IsDeep);
                            if (IsVerified(index, relic.IsDeep)) verifiedCount++;
                        }
                        coreMask |= _coreMask[index];
                        focusMask = focusMask | _positiveFocus[index];
                    }
                }

                int negativeTouchedStart = touchedPositive;
                foreach (RelicInstance relic in relics)
                {
                    foreach (int effectId in relic.NegativeEffectIds)
                    {
                        int index = ResolveEffectIndex(effectId);
                        if (index < 0) continue;
                        if (_negativeCounts[index]++ == 0) _touched[negativeTouchedStart + touchedNegative++] = index;
                    }
                }

                ScoreBreakdown result = FinishScore(
                    positiveScore,
                    verifiedCount,
                    coreMask,
                    focusMask,
                    _negativeCounts,
                    _touched,
                    negativeTouchedStart,
                    touchedNegative);
                for (int index = 0; index < touchedPositive; index++) _positiveCounts[_touched[index]] = 0;
                for (int index = 0; index < touchedNegative; index++) _negativeCounts[_touched[negativeTouchedStart + index]] = 0;
                return result;
            }

            public ScoreBreakdown FinishScore(
                int positiveScore,
                int verifiedCount,
                int coreMask,
                FocusMask focusMask,
                int[] negativeCounts,
                int[] touched,
                int touchedStart,
                int touchedCount)
            {
                if (_model.IsDirectScoreModel)
                {
                    int directPenalty = 0;
                    for (int touchedIndex = 0; touchedIndex < touchedCount; touchedIndex++)
                    {
                        int index = touched[touchedStart + touchedIndex];
                        int value = Contribution(index, true) * Math.Min(negativeCounts[index], _caps[index]);
                        if (value < 0) directPenalty -= value;
                        else positiveScore += value;
                    }
                    return new ScoreBreakdown(
                        positiveScore - directPenalty,
                        positiveScore,
                        0,
                        directPenalty,
                        verifiedCount,
                        0);
                }

                int negativePenalty = 0;
                ulong negativeTags = 0;
                for (int touchedIndex = 0; touchedIndex < touchedCount; touchedIndex++)
                {
                    int index = touched[touchedStart + touchedIndex];
                    NegativeRuleDefinition rule = _model.NegativeRules[index];
                    if (rule == null) continue;
                    int copies = Math.Min(negativeCounts[index], _caps[index]);
                    int compensationCount = 0;
                    foreach (FocusMask group in rule.CompensationGroups)
                        if (focusMask.Intersects(group)) compensationCount++;
                    int reduction = Math.Min(45, compensationCount * 15);
                    int perCopy = RoundNonNegative((long)_negativeBasePenalty[index] * (100 - reduction), 100);
                    negativePenalty += perCopy * copies;
                    negativeTags |= rule.Tags;
                }
                foreach (CombinationPenaltyDefinition combination in _model.CombinationPenaltyRules)
                    if ((negativeTags & combination.RequiredTags) == combination.RequiredTags)
                        negativePenalty += combination.PenaltyPoints;

                int completedCoreCount = 0;
                int coreScore = 0;
                int allMask = (1 << _option.CoreGroups.Length) - 1;
                for (int group = 0; group < _option.CoreGroups.Length; group++)
                {
                    if ((coreMask & (1 << group)) == 0) continue;
                    completedCoreCount++;
                    coreScore += _option.CoreGroups[group].CompletionBonus;
                }
                if (coreMask == allMask) coreScore += _option.AllCoreGroupsCompletionBonus;
                return new ScoreBreakdown(
                    positiveScore + coreScore - negativePenalty,
                    positiveScore,
                    coreScore,
                    negativePenalty,
                    verifiedCount,
                    completedCoreCount);
            }

            public int CoreUpperScore(int coreMask)
            {
                int score = 0;
                int allMask = (1 << _option.CoreGroups.Length) - 1;
                for (int group = 0; group < _option.CoreGroups.Length; group++)
                    if ((coreMask & (1 << group)) != 0) score += _option.CoreGroups[group].CompletionBonus;
                if (coreMask == allMask) score += _option.AllCoreGroupsCompletionBonus;
                return score;
            }

            private static int RoundNonNegative(long numerator, int denominator)
            {
                return (int)((numerator + denominator / 2) / denominator);
            }
        }

        private sealed class Candidate
        {
            public Candidate(int ordinal, RelicInstance relic, PreparedOption prepared)
            {
                Ordinal = ordinal;
                Relic = relic;
                PositiveIndices = relic.PositiveEffectIds
                    .Select(prepared.ResolveEffectIndex)
                    .Where(index => index >= 0)
                    .ToArray();
                NegativeIndices = relic.NegativeEffectIds
                    .Select(prepared.ResolveEffectIndex)
                    .Where(index => index >= 0)
                    .ToArray();
                int optimistic = 0;
                int sortScore = 0;
                byte core = 0;
                FocusMask focus = new FocusMask();
                foreach (int index in PositiveIndices)
                {
                    int contribution = prepared.Contribution(index, relic.IsDeep);
                    optimistic += Math.Max(0, contribution);
                    sortScore += contribution;
                    core |= prepared.CoreMask(index);
                    focus = focus | prepared.PositiveFocus(index);
                }
                if (prepared.UsesDirectScores)
                    foreach (int index in NegativeIndices)
                        sortScore += prepared.Contribution(index, relic.IsDeep);
                OptimisticScore = optimistic;
                SortScore = prepared.UsesDirectScores ? sortScore : optimistic;
                CoreMask = core;
                PositiveFocusMask = focus;
            }

            public int Ordinal { get; private set; }
            public RelicInstance Relic { get; private set; }
            public int[] PositiveIndices { get; private set; }
            public int[] NegativeIndices { get; private set; }
            public int OptimisticScore { get; private set; }
            public int SortScore { get; private set; }
            public byte CoreMask { get; private set; }
            public FocusMask PositiveFocusMask { get; private set; }
        }

        private sealed class VesselSearch
        {
            private readonly PreparedOption _prepared;
            private readonly Candidate[] _allCandidates;
            private readonly VesselDefinition _vessel;
            private readonly SearchSlot[] _slots;
            private readonly Candidate[] _selectedByOutputSlot;
            private readonly bool[] _used;
            private readonly int[] _positiveCounts;
            private readonly int[] _negativeCounts;
            private readonly int[] _negativeTouched;
            private readonly int[] _chosenCandidateIndexByDepth;
            private readonly int[] _suffixOptimistic;
            private readonly byte[] _suffixCorePotential;

            public VesselSearch(
                RuntimeModel model,
                PreparedOption prepared,
                Candidate[] candidates,
                VesselDefinition vessel,
                BestBuild currentBest)
            {
                _prepared = prepared;
                _allCandidates = candidates;
                _vessel = vessel;
                _selectedByOutputSlot = new Candidate[6];
                _used = new bool[candidates.Length];
                _positiveCounts = new int[model.Effects.Length];
                _negativeCounts = new int[model.Effects.Length];
                _negativeTouched = new int[model.Effects.Length];
                _chosenCandidateIndexByDepth = new int[6];
                Best = currentBest;
                _slots = CreateSlots();
                _suffixOptimistic = new int[_slots.Length + 1];
                _suffixCorePotential = new byte[_slots.Length + 1];
                for (int depth = _slots.Length - 1; depth >= 0; depth--)
                {
                    _suffixOptimistic[depth] = _suffixOptimistic[depth + 1]
                        + _slots[depth].MaximumOptimisticScore;
                    _suffixCorePotential[depth] = (byte)(_suffixCorePotential[depth + 1] | _slots[depth].CorePotential);
                }
            }

            public BestBuild Best { get; private set; }
            public long EvaluatedBuilds { get; private set; }

            public void Run()
            {
                if (_slots.Any(slot => slot.Candidates.Length == 0)) return;
                Search(0, 0, 0, 0, new FocusMask(), 0);
            }

            private void Search(
                int depth,
                int positiveScore,
                int directAdjustment,
                int coreMask,
                FocusMask focusMask,
                int verifiedCount)
            {
                if (depth == _slots.Length)
                {
                    EvaluateLeaf(positiveScore, directAdjustment, coreMask, focusMask, verifiedCount);
                    return;
                }

                SearchSlot slot = _slots[depth];
                int minimumCandidateIndex = slot.PreviousSameSlotDepth < 0
                    ? 0
                    : _chosenCandidateIndexByDepth[slot.PreviousSameSlotDepth] + 1;
                for (int candidateIndex = minimumCandidateIndex; candidateIndex < slot.Candidates.Length; candidateIndex++)
                {
                    Candidate candidate = slot.Candidates[candidateIndex];
                    if (_used[candidate.Ordinal]) continue;
                    int verifiedDelta = CountVerifiedDelta(candidate);
                    int delta = ApplyPositive(candidate, true);
                    int directDelta = ApplyDirectNegative(candidate, true);
                    int nextPositive = positiveScore + delta;
                    int nextDirectAdjustment = directAdjustment + directDelta;
                    int nextCore = coreMask | candidate.CoreMask;
                    int upperCoreMask = nextCore | _suffixCorePotential[depth + 1];
                    int upper = nextPositive
                        + nextDirectAdjustment
                        + _suffixOptimistic[depth + 1]
                        + _prepared.CoreUpperScore(upperCoreMask);
                    if (Best == null || upper >= Best.Score.TotalScore)
                    {
                        _used[candidate.Ordinal] = true;
                        _selectedByOutputSlot[slot.OutputSlot] = candidate;
                        _chosenCandidateIndexByDepth[depth] = candidateIndex;
                        Search(
                            depth + 1,
                            nextPositive,
                            nextDirectAdjustment,
                            nextCore,
                            focusMask | candidate.PositiveFocusMask,
                            verifiedCount + verifiedDelta);
                        _used[candidate.Ordinal] = false;
                    }
                    ApplyDirectNegative(candidate, false);
                    ApplyPositive(candidate, false);
                }
            }

            private int ApplyPositive(Candidate candidate, bool add)
            {
                int delta = 0;
                if (add)
                {
                    foreach (int index in candidate.PositiveIndices)
                    {
                        if (_positiveCounts[index] < _prepared.Cap(index))
                            delta += _prepared.Contribution(index, candidate.Relic.IsDeep);
                        _positiveCounts[index]++;
                    }
                }
                else
                {
                    foreach (int index in candidate.PositiveIndices) _positiveCounts[index]--;
                }
                return delta;
            }

            private int ApplyDirectNegative(Candidate candidate, bool add)
            {
                if (!_prepared.UsesDirectScores) return 0;
                int delta = 0;
                if (add)
                {
                    foreach (int index in candidate.NegativeIndices)
                    {
                        if (_negativeCounts[index] < _prepared.Cap(index))
                            delta += _prepared.Contribution(index, candidate.Relic.IsDeep);
                        _negativeCounts[index]++;
                    }
                }
                else
                {
                    foreach (int index in candidate.NegativeIndices) _negativeCounts[index]--;
                }
                return delta;
            }

            private int CountVerifiedDelta(Candidate candidate)
            {
                int result = 0;
                for (int position = 0; position < candidate.PositiveIndices.Length; position++)
                {
                    int index = candidate.PositiveIndices[position];
                    if (!_prepared.IsVerified(index, candidate.Relic.IsDeep)) continue;
                    int priorWithinCandidate = 0;
                    for (int earlier = 0; earlier < position; earlier++)
                        if (candidate.PositiveIndices[earlier] == index) priorWithinCandidate++;
                    if (_positiveCounts[index] + priorWithinCandidate < _prepared.Cap(index)) result++;
                }
                return result;
            }

            private void EvaluateLeaf(
                int positiveScore,
                int directAdjustment,
                int coreMask,
                FocusMask focusMask,
                int verifiedCount)
            {
                EvaluatedBuilds++;
                if (_prepared.UsesDirectScores)
                {
                    int directPenalty = Math.Max(0, -directAdjustment);
                    ScoreBreakdown directScore = new ScoreBreakdown(
                        positiveScore + directAdjustment,
                        positiveScore,
                        0,
                        directPenalty,
                        verifiedCount,
                        0);
                    BestBuild directBuild = new BestBuild(_vessel, CreateAssignments(), directScore);
                    if (Best == null || Compare(directBuild, Best) < 0) Best = directBuild;
                    return;
                }
                int touchedCount = 0;
                foreach (Candidate candidate in _selectedByOutputSlot)
                {
                    foreach (int index in candidate.NegativeIndices)
                    {
                        if (_negativeCounts[index]++ == 0) _negativeTouched[touchedCount++] = index;
                    }
                }
                ScoreBreakdown score = _prepared.FinishScore(
                    positiveScore,
                    verifiedCount,
                    coreMask,
                    focusMask,
                    _negativeCounts,
                    _negativeTouched,
                    0,
                    touchedCount);
                SlotAssignment[] assignments = CreateAssignments();
                BestBuild build = new BestBuild(_vessel, assignments, score);
                if (Best == null || Compare(build, Best) < 0) Best = build;
                for (int index = 0; index < touchedCount; index++) _negativeCounts[_negativeTouched[index]] = 0;
            }

            private SearchSlot[] CreateSlots()
            {
                List<SearchSlot> slots = new List<SearchSlot>(6);
                for (int index = 0; index < 3; index++)
                    slots.Add(CreateSlot(false, index, _vessel.OrdinarySlotColorIds[index]));
                for (int index = 0; index < 3; index++)
                    slots.Add(CreateSlot(true, index + 3, _vessel.DeepSlotColorIds[index]));
                slots.Sort((left, right) =>
                {
                    int value = left.Candidates.Length.CompareTo(right.Candidates.Length);
                    if (value != 0) return value;
                    value = left.IsDeep.CompareTo(right.IsDeep);
                    if (value != 0) return value;
                    value = left.ColorId.CompareTo(right.ColorId);
                    return value != 0 ? value : left.OutputSlot.CompareTo(right.OutputSlot);
                });
                Dictionary<string, int> previousByGroup = new Dictionary<string, int>();
                for (int depth = 0; depth < slots.Count; depth++)
                {
                    string key = (slots[depth].IsDeep ? "D" : "O") + slots[depth].ColorId;
                    int previous;
                    slots[depth].PreviousSameSlotDepth = previousByGroup.TryGetValue(key, out previous) ? previous : -1;
                    previousByGroup[key] = depth;
                }
                return slots.ToArray();
            }

            private SearchSlot CreateSlot(bool isDeep, int outputSlot, int colorId)
            {
                Candidate[] candidates = _allCandidates
                    .Where(item => item.Relic.IsDeep == isDeep && AcceptsColor(colorId, item.Relic.ColorId))
                    .OrderByDescending(item => item.SortScore)
                    .ThenByDescending(item => item.OptimisticScore)
                    .ThenBy(item => item.Relic.InstanceId)
                    .ToArray();
                byte potential = 0;
                foreach (Candidate candidate in candidates) potential |= candidate.CoreMask;
                return new SearchSlot(isDeep, outputSlot, colorId, candidates, potential);
            }

            private SlotAssignment[] CreateAssignments()
            {
                SlotAssignment[] result = new SlotAssignment[6];
                for (int index = 0; index < 3; index++)
                    result[index] = new SlotAssignment(false, index, _vessel.OrdinarySlotColorIds[index], _selectedByOutputSlot[index].Relic);
                for (int index = 0; index < 3; index++)
                    result[index + 3] = new SlotAssignment(true, index, _vessel.DeepSlotColorIds[index], _selectedByOutputSlot[index + 3].Relic);
                return result;
            }
        }

        private sealed class SearchSlot
        {
            public SearchSlot(
                bool isDeep,
                int outputSlot,
                int colorId,
                Candidate[] candidates,
                byte corePotential)
            {
                IsDeep = isDeep;
                OutputSlot = outputSlot;
                ColorId = colorId;
                Candidates = candidates;
                CorePotential = corePotential;
                MaximumOptimisticScore = candidates.Length == 0
                    ? 0
                    : candidates.Max(item => item.OptimisticScore);
                PreviousSameSlotDepth = -1;
            }

            public bool IsDeep { get; private set; }
            public int OutputSlot { get; private set; }
            public int ColorId { get; private set; }
            public Candidate[] Candidates { get; private set; }
            public byte CorePotential { get; private set; }
            public int MaximumOptimisticScore { get; private set; }
            public int PreviousSameSlotDepth { get; set; }
        }

        private sealed class BestBuild
        {
            public BestBuild(VesselDefinition vessel, SlotAssignment[] assignments, ScoreBreakdown score)
            {
                Vessel = vessel;
                Assignments = assignments;
                Score = score;
            }

            public VesselDefinition Vessel { get; private set; }
            public SlotAssignment[] Assignments { get; private set; }
            public ScoreBreakdown Score { get; private set; }

            public OptimizationResult ToResult(long elapsedMilliseconds, long evaluatedBuilds)
            {
                return new OptimizationResult(Vessel, Assignments, Score, elapsedMilliseconds, evaluatedBuilds);
            }
        }

        private struct WeightedFocus
        {
            public WeightedFocus(FocusMask mask, int weight)
            {
                Mask = mask;
                Weight = weight;
            }

            public FocusMask Mask;
            public int Weight;
        }
    }
}
