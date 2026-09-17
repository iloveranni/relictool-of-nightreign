using System;
using System.Collections.Generic;
using System.Linq;

namespace NightreignRelicTool.Core
{
    public sealed class RelicLocator
    {
        private const string ExactPositionUnavailable = "当前版本精确位置不可用";
        private const string PositionOrderUncertain = "位置顺序不确定";
        private readonly string _gameVersion;
        private readonly RelicInventoryLayoutProfile _profile;

        public RelicLocator(RuntimeModel model)
            : this(model, null, RelicInventoryLayoutProfile.LoadBuiltIn())
        {
        }

        public RelicLocator(RuntimeModel model, CustomEffectCatalog catalog)
            : this(model, catalog, RelicInventoryLayoutProfile.LoadBuiltIn())
        {
        }

        internal RelicLocator(RuntimeModel model, CustomEffectCatalog catalog, RelicInventoryLayoutProfile profile)
        {
            if (model == null) throw new ArgumentNullException("model");
            if (catalog != null
                && !string.Equals(model.RegulationVersion, catalog.GameVersion, StringComparison.Ordinal))
                throw new ArgumentException("定位器的运行时模型与词条目录版本不一致。", "catalog");
            _profile = profile ?? throw new ArgumentNullException("profile");
            _gameVersion = model.RegulationVersion;
        }

        public RelicInventoryLayoutProfile Profile { get { return _profile; } }

        public RelicLocation[] Locate(CharacterInventory inventory, OptimizationResult result)
        {
            if (inventory == null) throw new ArgumentNullException("inventory");
            if (result == null) throw new ArgumentNullException("result");
            return Locate(inventory, result.Assignments);
        }

        public RelicLocation[] Locate(CharacterInventory inventory, CustomSearchBuild result)
        {
            if (inventory == null) throw new ArgumentNullException("inventory");
            if (result == null) throw new ArgumentNullException("result");
            return Locate(inventory, result.Assignments);
        }

        private RelicLocation[] Locate(CharacterInventory inventory, SlotAssignment[] assignments)
        {
            if (assignments == null) throw new ArgumentNullException("assignments");
            List<RelicLocation> locations = new List<RelicLocation>(assignments.Length);
            foreach (SlotAssignment assignment in assignments)
            {
                if (assignment == null || assignment.Relic == null)
                    throw new ArgumentException("定位组合包含空遗物槽。", "assignments");

                RelicInstance target = assignment.Relic;
                RelicInstance[] group = inventory.Relics
                    .Where(item => item.IsDeep == target.IsDeep && item.ColorId == target.ColorId)
                    .OrderByDescending(item => item.InventorySortKey)
                    .ToArray();
                int index = Array.FindIndex(group, item => item.InstanceId == target.InstanceId);

                if (!_profile.SupportsGameVersion(_gameVersion)
                    || !_profile.IsExactPositionVerified
                    || target.ColorId < 0 || target.ColorId > 3
                    || index < 0)
                {
                    locations.Add(new RelicLocation(assignment, false, 0, 0, ExactPositionUnavailable));
                    continue;
                }
                if (group.Count(item => item.InventorySortKey == target.InventorySortKey) != 1)
                {
                    locations.Add(new RelicLocation(assignment, false, 0, 0, PositionOrderUncertain));
                    continue;
                }

                int row = index / _profile.Columns + 1;
                int column = index % _profile.Columns + 1;
                locations.Add(new RelicLocation(assignment, true, row, column, null));
            }
            return locations.ToArray();
        }
    }
}
