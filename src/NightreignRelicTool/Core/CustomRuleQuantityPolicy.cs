using System;
using System.Collections.Generic;
using System.Linq;

namespace NightreignRelicTool.Core
{
    /// <summary>Product input capability; the frozen solver still accepts historical pressure inputs.</summary>
    public static class CustomRuleQuantityPolicy
    {
        public static bool IsFixedSelector(CustomEffectCatalog catalog, string selectorId)
        {
            if (catalog == null) throw new ArgumentNullException("catalog");
            CustomEffectSelector selector = catalog.GetSelector(selectorId);
            if (selector == null || !selector.IsAvailableForRealMatching
                || selector.RuntimeEffectIds.Length == 0) return false;
            var effects = selector.RuntimeEffectIds.Select(catalog.GetEffect).ToArray();
            if (effects.Any(effect => effect == null)) return false;
            // Each independent ordinary stack group or exclusivity group contributes
            // separately. A single non-stackable binding cannot cap the whole selector.
            var groups = effects.GroupBy(effect => effect.ExclusivityId.HasValue
                ? "exclusive:" + effect.ExclusivityId.Value
                : "stack:" + effect.StackGroupKey, StringComparer.Ordinal).ToArray();
            if (groups.Length != 1) return false;
            return groups[0].All(effect =>
            {
                if (effect.ExclusivityId.HasValue)
                {
                    var policy = catalog.GetExclusivityPolicy(effect.ExclusivityId.Value);
                    return policy != null && policy.MaxEffectiveEffects == 1
                        && policy.RuntimeEffectIds.Contains(effect.RuntimeEffectId);
                }
                return effect.MaxEffectiveCopies == 1
                    && (effect.AggregationRule == "single_instance" || effect.AggregationRule == "cap_count")
                    && (effect.AggregationVerificationStatus == "project_verified"
                        || effect.AggregationVerificationStatus == "confirmed");
            });
        }

        public static CustomEffectRule[] Normalize(CustomEffectCatalog catalog, IEnumerable<CustomEffectRule> rules)
        {
            return (rules ?? Enumerable.Empty<CustomEffectRule>()).Select(rule =>
                rule != null && rule.TargetKind == CustomRuleTargetKind.EffectSelector
                    && rule.QuantityTarget != 1 && IsFixedSelector(catalog, rule.SelectorId)
                    ? new CustomEffectRule(rule.RuleId, rule.SelectorId, rule.IsRequired, 1) : rule).ToArray();
        }

        internal static void NormalizeSaved(CustomEffectCatalog catalog, SavedCustomSearchState state)
        {
            if (state == null || state.Rules == null) return;
            foreach (var rule in state.Rules.Where(item => item != null))
            {
                var preset = rule.PresetItemId.HasValue ? catalog.GetPreset(rule.PresetItemId.Value) : null;
                if (preset != null && preset.IsAvailableForRealMatching
                    || !rule.PresetItemId.HasValue && IsFixedSelector(catalog, rule.SelectorId))
                    rule.QuantityTarget = 1;
            }
        }
    }
}
