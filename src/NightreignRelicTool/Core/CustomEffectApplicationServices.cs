using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace NightreignRelicTool.Core
{
    /// <summary>
    /// One validated, consistent set of runtime resources.  The model and custom-effect
    /// catalog always move together so a search cannot combine vessels from one pack
    /// with effects from another.
    /// </summary>
    public sealed class RuntimeDataSnapshot
    {
        private readonly RuntimeModel _model;
        private readonly CustomEffectCatalog _catalog;

        internal RuntimeDataSnapshot(DataPackLoadResult source)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (source.Model == null || source.CustomEffects == null)
                throw new InvalidDataException("运行时数据快照缺少模型或自定义词条目录。");
            if (!string.Equals(source.Model.RegulationVersion, source.CustomEffects.GameVersion, StringComparison.Ordinal))
                throw new InvalidDataException("运行时模型与自定义词条目录的 Regulation 版本不一致。");

            SnapshotId = Guid.NewGuid().ToString("N");
            _model = source.Model.CreateDefensiveCopy();
            _catalog = source.CustomEffects.CreateDefensiveCopy();
            PackId = source.PackId;
            PackVersion = source.PackVersion;
            IsBuiltIn = source.IsBuiltIn;
            Warning = source.Warning;
        }

        public string SnapshotId { get; private set; }
        public RuntimeModel Model { get { return _model.CreateDefensiveCopy(); } }
        public CustomEffectCatalog Catalog { get { return _catalog.CreateDefensiveCopy(); } }
        public string PackId { get; private set; }
        public string PackVersion { get; private set; }
        public bool IsBuiltIn { get; private set; }
        public string Warning { get; private set; }
        public string GameVersion { get { return _catalog.GameVersion; } }
        public string DataVersion { get { return _catalog.DataVersion; } }
        internal RuntimeModel CoreModel { get { return _model; } }
        internal CustomEffectCatalog CoreCatalog { get { return _catalog; } }
    }

    public sealed class SaveSession
    {
        private readonly string _sourcePath;
        private readonly string _copyPath;
        private readonly SourceFileSnapshot _sourceBefore;
        private readonly SourceFileSnapshot _sourceAfter;
        private readonly SourceFileSnapshot _copySnapshot;
        private readonly SaveSourceKey _sourceKey;
        private readonly Dictionary<int, CharacterInventory> _inventories;

        internal SaveSession(RuntimeDataSnapshot snapshot, SaveImportResult imported)
            : this(snapshot, imported, imported == null ? null : imported.Save)
        {
        }

        private SaveSession(RuntimeDataSnapshot snapshot, SaveImportResult imported, ParsedSave parsed)
        {
            if (snapshot == null) throw new ArgumentNullException("snapshot");
            if (imported == null) throw new ArgumentNullException("imported");
            if (parsed == null) throw new ArgumentNullException("parsed");

            SnapshotId = snapshot.SnapshotId;
            _sourcePath = imported.SourcePath;
            _copyPath = imported.CopyPath;
            _sourceBefore = imported.SourceBefore;
            _sourceAfter = imported.SourceAfter;
            _copySnapshot = imported.CopySnapshot;
            _sourceKey = imported.SourceKey;
            SourceFileName = Path.GetFileName(imported.SourcePath);
            SourceUnchanged = imported.SourceUnchanged;
            RefreshStatus = imported.RefreshStatus;
            DisplayName = imported.DisplayName;
            RepositoryChanged = imported.RepositoryChanged;
            ParsedSave = parsed;
            _inventories = parsed.Characters.ToDictionary(item => item.SlotIndex);
            Inventories = parsed.Characters.OrderBy(item => item.SlotIndex).ToArray();
        }

        public string SnapshotId { get; private set; }
        public string SourceFileName { get; private set; }
        public bool SourceUnchanged { get; private set; }
        public SaveRefreshStatus RefreshStatus { get; private set; }
        public string DisplayName { get; private set; }
        public bool RepositoryChanged { get; private set; }
        public ParsedSave ParsedSave { get; private set; }
        public CharacterInventory[] Inventories { get; private set; }
        public SaveSourceKey SourceKey { get { return _sourceKey; } }

        public CharacterInventory GetInventory(int slotIndex)
        {
            CharacterInventory result;
            return _inventories.TryGetValue(slotIndex, out result) ? result : null;
        }

        internal string ComputeIdentity(int slotIndex)
        {
            return _sourceKey == null
                ? CustomEffectStateStore.ComputeSaveIdentity(_sourcePath, slotIndex)
                : CustomEffectStateStore.ComputeSaveIdentityFromSourceKey(_sourceKey.Value, slotIndex);
        }

        internal bool HasSameValidatedContent(SaveImportResult imported)
        {
            return imported != null && _sourceKey != null && _sourceKey.Equals(imported.SourceKey)
                && _copySnapshot != null && imported.CopySnapshot != null
                && _copySnapshot.Length == imported.CopySnapshot.Length
                && string.Equals(_copySnapshot.Sha256, imported.CopySnapshot.Sha256, StringComparison.OrdinalIgnoreCase)
                && string.Equals(_copyPath, imported.CopyPath, StringComparison.OrdinalIgnoreCase);
        }

        internal SaveSession Reparse(RuntimeDataSnapshot snapshot)
        {
            SourceFileSnapshot before = SaveImportService.CaptureReadOnly(_copyPath);
            if (_copySnapshot == null || !_copySnapshot.SameFile(before))
                throw new SaveReadException("已验证的存档副本发生了变化，已拒绝重新解析。");
            ParsedSave parsed = new Bnd4SaveReader(snapshot.CoreModel).Read(_copyPath);
            SourceFileSnapshot after = SaveImportService.CaptureReadOnly(_copyPath);
            if (!before.SameFile(after))
                throw new SaveReadException("重新解析期间存档副本发生了变化，已拒绝切换数据。");
            SaveImportResult reparsed = new SaveImportResult(
                _sourcePath,
                _copyPath,
                _sourceBefore,
                _sourceAfter,
                after,
                parsed,
                _sourceKey,
                SaveRefreshStatus.Unchanged,
                Path.GetFileNameWithoutExtension(_copyPath),
                string.Empty,
                string.Empty);
            return new SaveSession(snapshot, reparsed, parsed);
        }
    }

    public sealed class DataPackUpdateOutcome
    {
        internal DataPackUpdateOutcome(RuntimeDataSnapshot snapshot, SaveSession saveSession)
        {
            Snapshot = snapshot;
            SaveSession = saveSession;
        }

        public RuntimeDataSnapshot Snapshot { get; private set; }
        public SaveSession SaveSession { get; private set; }
    }

    public interface ICustomEffectApplicationService
    {
        RuntimeDataSnapshot Snapshot { get; }
        SaveSession CurrentSave { get; }
        string[] FindLocalSaves();
        SaveSession ImportSave(string path);
        void ClearSave();
        CustomSearchResponse Search(int inventorySlotIndex, string characterId,
            IEnumerable<CustomEffectRule> rules, CancellationToken cancellationToken);
        CustomSearchBuild[] RankReplacements(int inventorySlotIndex, string characterId,
            IEnumerable<CustomEffectRule> rules, CustomSearchBuild current, int assignmentIndex,
            CancellationToken cancellationToken);
        RelicLocation[] Locate(int inventorySlotIndex, CustomSearchBuild build);
        CustomSearchBuild RehydrateBuild(int inventorySlotIndex, string characterId,
            IEnumerable<CustomEffectRule> rules, int vesselId, int[] instanceIds);
        SavedCustomSearchState LoadState(int inventorySlotIndex, string characterId);
        void SaveState(int inventorySlotIndex, SavedCustomSearchState state);
        DataPackUpdateOutcome ImportDataPack(string path);
        DataPackUpdateOutcome RollbackDataPack();
        bool CanRollbackDataPack { get; }
        string PreviousDataPackDescription { get; }
        string StateWarning { get; }
    }

    public interface ISaveSourceApplicationService
    {
        SaveServiceSnapshot SaveSources { get; }
        SaveServiceSnapshot RestoreSaveSources();
        SaveDiscoveryResult DiscoverSaveSources();
        SaveRefreshCycleResult RefreshSelectedSaveSources();
        SaveRefreshResult RefreshSave(SaveSourceKey sourceKey);
        SaveSession SelectSaveSource(SaveSourceKey sourceKey);
        SaveServiceSnapshot SelectSaveCharacterSlot(SaveSourceKey sourceKey, int slotIndex);
    }

    /// <summary>
    /// Production boundary used by WPF.  Save parsing, data-pack switching, search,
    /// replacement ranking, locating, and formal state persistence stay out of the ViewModel.
    /// </summary>
    public sealed class CustomEffectApplicationService : ICustomEffectApplicationService, ISaveSourceApplicationService
    {
        private readonly object _sync = new object();
        private readonly string _applicationRoot;
        private readonly DataPackManager _dataPackManager;
        private readonly string _localSaveRoot;
        private readonly CustomEffectStateStore _stateStore;
        private volatile RuntimeDataSnapshot _snapshot;
        private volatile SaveSession _currentSave;
        private CustomEffectSearcher _searcher;
        private StableExactV1 _stableSearch;
        private volatile SaveServiceSnapshot _saveSources = new SaveServiceSnapshot(null, null, null);
        private RelicLocator _locator;
        private SaveManagementService _saveManagement;

        public CustomEffectApplicationService(string applicationRoot)
            : this(applicationRoot, null, null)
        {
        }

        public CustomEffectApplicationService(string applicationRoot, string localSaveRoot)
            : this(applicationRoot, null, localSaveRoot)
        {
        }

        internal CustomEffectApplicationService(string applicationRoot, DataPackManager dataPackManager)
            : this(applicationRoot, dataPackManager, null)
        {
        }

        private CustomEffectApplicationService(string applicationRoot, DataPackManager dataPackManager, string localSaveRoot)
        {
            _applicationRoot = Path.GetFullPath(applicationRoot ?? AppDomain.CurrentDomain.BaseDirectory);
            _dataPackManager = dataPackManager ?? new DataPackManager(_applicationRoot);
            _localSaveRoot = localSaveRoot;
            _stateStore = new CustomEffectStateStore(_applicationRoot);
            InstallSnapshot(new RuntimeDataSnapshot(_dataPackManager.LoadBuiltIn()), null);
        }

        public RuntimeDataSnapshot Snapshot
        {
            get { return _snapshot; }
        }

        public SaveSession CurrentSave
        {
            // Publish only a fully prepared session. UI binding reads must keep
            // observing the last valid session while another thread copies a save.
            get { return _currentSave; }
        }

        public string[] FindLocalSaves()
        {
            lock (_sync) return _saveManagement.FindLocalSavePaths();
        }

        public SaveSession ImportSave(string path)
        {
            lock (_sync)
            {
                SaveRefreshResult refreshed = _saveManagement.ImportPath(path);
                if (!refreshed.Succeeded || refreshed.Imported == null)
                    throw new SaveReadException(string.IsNullOrWhiteSpace(refreshed.ErrorMessage)
                        ? "存档导入失败；上次验证副本保持不变。" : refreshed.ErrorMessage);
                ApplyImportedSave(refreshed.Imported);
                _saveSources = _saveManagement.GetSnapshot();
                return _currentSave;
            }
        }

        public SaveServiceSnapshot RestoreSaveSources()
        {
            lock (_sync)
            {
                _saveSources = _saveManagement.Restore();
                SynchronizeSelectedSource(null);
                return _saveSources;
            }
        }

        public SaveServiceSnapshot SaveSources { get { return _saveSources; } }

        public SaveDiscoveryResult DiscoverSaveSources()
        {
            lock (_sync)
            {
                SaveDiscoveryResult discovery = _saveManagement.DiscoverLocalSaves();
                _saveSources = discovery.Snapshot;
                SynchronizeSelectedSource(discovery.RefreshResults);
                return discovery;
            }
        }

        public SaveRefreshCycleResult RefreshSelectedSaveSources()
        {
            lock (_sync)
            {
                SaveRefreshCycleResult cycle = _saveManagement.RefreshSelectedAndDiscover();
                _saveSources = cycle.Snapshot;
                SynchronizeSelectedSource(cycle.SelectedSourceRefresh == null ? null : new[] { cycle.SelectedSourceRefresh });
                return cycle;
            }
        }

        public SaveRefreshResult RefreshSave(SaveSourceKey sourceKey)
        {
            lock (_sync)
            {
                SaveRefreshResult refreshed = _saveManagement.Refresh(sourceKey);
                _saveSources = _saveManagement.GetSnapshot();
                if (refreshed.Succeeded && refreshed.Imported != null
                    && _saveSources.SelectedSourceKey != null && _saveSources.SelectedSourceKey.Equals(sourceKey)
                    && (_currentSave == null || (_currentSave.SourceKey != null
                        && _currentSave.SourceKey.Equals(sourceKey))))
                    ApplyImportedSave(refreshed.Imported);
                return refreshed;
            }
        }

        public SaveSession SelectSaveSource(SaveSourceKey sourceKey)
        {
            lock (_sync)
            {
                SaveImportResult imported = _saveManagement.LoadValidatedCopy(sourceKey);
                SaveSession prepared = PrepareSession(imported);
                _saveSources = _saveManagement.SelectSource(sourceKey);
                _currentSave = prepared;
                return prepared;
            }
        }

        public SaveServiceSnapshot SelectSaveCharacterSlot(SaveSourceKey sourceKey, int slotIndex)
        {
            lock (_sync)
            {
                if (_currentSave == null || _currentSave.SourceKey == null || !_currentSave.SourceKey.Equals(sourceKey))
                    throw new ArgumentException("所选栏位不属于当前存档来源。", "sourceKey");
                RequireInventory(slotIndex);
                _saveSources = _saveManagement.SelectCharacterSlot(sourceKey, slotIndex);
                return _saveSources;
            }
        }

        private SaveSession PrepareSession(SaveImportResult imported)
        {
            return _currentSave != null && _currentSave.HasSameValidatedContent(imported)
                ? _currentSave : new SaveSession(_snapshot, imported);
        }

        private void ApplyImportedSave(SaveImportResult imported) { _currentSave = PrepareSession(imported); }

        private void SynchronizeSelectedSource(IEnumerable<SaveRefreshResult> results)
        {
            SaveSourceInfo selected = _saveSources.SelectedSource;
            if (selected == null || selected.ValidationStatus != SaveValidationStatus.Valid) return;
            SaveRefreshResult refreshed = (results ?? Enumerable.Empty<SaveRefreshResult>())
                .FirstOrDefault(item => item != null && item.Source != null && item.Source.SourceKey.Equals(selected.SourceKey));
            if (refreshed != null && refreshed.Succeeded && refreshed.Imported != null)
                ApplyImportedSave(refreshed.Imported);
            else if (_currentSave == null || _currentSave.SourceKey == null || !_currentSave.SourceKey.Equals(selected.SourceKey))
                ApplyImportedSave(_saveManagement.LoadValidatedCopy(selected.SourceKey));
        }

        public void ClearSave()
        {
            lock (_sync) _currentSave = null;
        }

        public CustomSearchResponse Search(int inventorySlotIndex, string characterId,
            IEnumerable<CustomEffectRule> rules, CancellationToken cancellationToken)
        {
            CustomEffectRule[] ruleArray = LimitRules(rules);
            StableExactV1 searcher;
            CharacterInventory inventory;
            CharacterDefinition character;
            RuntimeDataSnapshot snapshot;
            SaveSession save;
            lock (_sync)
            {
                inventory = RequireInventory(inventorySlotIndex);
                character = RequireCharacter(characterId);
                searcher = _stableSearch;
                snapshot = _snapshot;
                save = _currentSave;
                ruleArray = CustomRuleQuantityPolicy.Normalize(snapshot.CoreCatalog, ruleArray);
            }
            CustomSearchResponse response = searcher.Search(inventory, character, ruleArray, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                if (!ReferenceEquals(snapshot, _snapshot) || !ReferenceEquals(save, _currentSave))
                    throw new OperationCanceledException("运行时数据或存档会话已切换。", cancellationToken);
            }
            return response;
        }

        public CustomSearchBuild[] RankReplacements(int inventorySlotIndex, string characterId,
            IEnumerable<CustomEffectRule> rules, CustomSearchBuild current, int assignmentIndex,
            CancellationToken cancellationToken)
        {
            CustomEffectRule[] ruleArray = LimitRules(rules);
            CustomEffectSearcher searcher;
            CharacterInventory inventory;
            CharacterDefinition character;
            RuntimeDataSnapshot snapshot;
            SaveSession save;
            CustomSearchBuild validatedCurrent;
            lock (_sync)
            {
                inventory = RequireInventory(inventorySlotIndex);
                character = RequireCharacter(characterId);
                searcher = _searcher;
                snapshot = _snapshot;
                save = _currentSave;
                ruleArray = CustomRuleQuantityPolicy.Normalize(snapshot.CoreCatalog, ruleArray);
                RequireBuildOwnership(inventory, current);
                validatedCurrent = RehydrateBuildLocked(inventory, character, ruleArray,
                    current == null ? -1 : current.Vessel.Id,
                    current == null ? null : current.Assignments.Select(item => item.Relic.InstanceId).ToArray());
                if (validatedCurrent == null || !string.Equals(BuildKey(validatedCurrent), BuildKey(current), StringComparison.Ordinal))
                    throw new ArgumentException("当前组合不属于此数据快照或存档仓库。", "current");
            }
            CustomSearchBuild[] result = searcher.RankReplacements(inventory, character, ruleArray,
                validatedCurrent, assignmentIndex, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                if (!ReferenceEquals(snapshot, _snapshot) || !ReferenceEquals(save, _currentSave))
                    throw new OperationCanceledException("运行时数据或存档会话已切换。", cancellationToken);
            }
            return result;
        }

        public RelicLocation[] Locate(int inventorySlotIndex, CustomSearchBuild build)
        {
            lock (_sync)
            {
                CharacterInventory inventory = RequireInventory(inventorySlotIndex);
                RequireBuildOwnership(inventory, build);
                return _locator.Locate(inventory, build);
            }
        }

        public CustomSearchBuild RehydrateBuild(int inventorySlotIndex, string characterId,
            IEnumerable<CustomEffectRule> rules, int vesselId, int[] instanceIds)
        {
            if (instanceIds == null || instanceIds.Length != 6) return null;
            CustomEffectRule[] ruleArray = LimitRules(rules);
            lock (_sync)
            {
                CharacterInventory inventory = RequireInventory(inventorySlotIndex);
                CharacterDefinition character = RequireCharacter(characterId);
                return RehydrateBuildLocked(inventory, character,
                    CustomRuleQuantityPolicy.Normalize(_snapshot.CoreCatalog, ruleArray), vesselId, instanceIds);
            }
        }

        public SavedCustomSearchState LoadState(int inventorySlotIndex, string characterId)
        {
            lock (_sync)
            {
                CharacterInventory inventory = RequireInventory(inventorySlotIndex);
                string identity = _currentSave.ComputeIdentity(inventorySlotIndex);
                string fingerprint = CustomEffectStateStore.ComputeRepositoryFingerprint(inventory);
                var state = _stateStore.Load(identity, inventorySlotIndex, characterId, fingerprint, CurrentDataIdentity());
                CustomRuleQuantityPolicy.NormalizeSaved(_snapshot.CoreCatalog, state);
                return state;
            }
        }

        public void SaveState(int inventorySlotIndex, SavedCustomSearchState state)
        {
            if (state == null) throw new ArgumentNullException("state");
            lock (_sync)
            {
                CharacterInventory inventory = RequireInventory(inventorySlotIndex);
                RequireCharacter(state.CharacterId);
                CustomRuleQuantityPolicy.NormalizeSaved(_snapshot.CoreCatalog, state);
                state.SaveIdentity = _currentSave.ComputeIdentity(inventorySlotIndex);
                state.SaveSlotIndex = inventorySlotIndex;
                state.RepositoryFingerprint = CustomEffectStateStore.ComputeRepositoryFingerprint(inventory);
                state.DataIdentity = CurrentDataIdentity();
                _stateStore.Save(state);
            }
        }

        public DataPackUpdateOutcome ImportDataPack(string path)
        {
            lock (_sync) return SwitchDataPack(prepare => _dataPackManager.Import(path, prepare));
        }

        public DataPackUpdateOutcome RollbackDataPack()
        {
            lock (_sync) return SwitchDataPack(prepare => _dataPackManager.Rollback(prepare));
        }

        public bool CanRollbackDataPack
        {
            get { lock (_sync) return _dataPackManager.CanRollback; }
        }

        public string PreviousDataPackDescription
        {
            get { lock (_sync) return _dataPackManager.PreviousDescription; }
        }

        public string StateWarning
        {
            get { lock (_sync) return _stateStore.LastWarning; }
        }

        private DataPackUpdateOutcome SwitchDataPack(
            Func<Action<DataPackLoadResult>, DataPackLoadResult> switchPack)
        {
            SaveSession oldSave = _currentSave;
            RuntimeDataSnapshot next = null;
            SaveSession reparsed = null;
            CustomEffectSearcher nextSearcher = null;
            RelicLocator nextLocator = null;
            DataPackUpdateOutcome outcome = null;
            switchPack(loaded =>
            {
                RuntimeDataSnapshot preparedSnapshot = new RuntimeDataSnapshot(loaded);
                SaveSession preparedSave = oldSave == null ? null : oldSave.Reparse(preparedSnapshot);
                CustomEffectSearcher preparedSearcher = new CustomEffectSearcher(
                    preparedSnapshot.CoreModel, preparedSnapshot.CoreCatalog);
                RelicLocator preparedLocator = new RelicLocator(
                    preparedSnapshot.CoreModel, preparedSnapshot.CoreCatalog);
                next = preparedSnapshot;
                reparsed = preparedSave;
                nextSearcher = preparedSearcher;
                nextLocator = preparedLocator;
                outcome = new DataPackUpdateOutcome(preparedSnapshot, preparedSave);
            });
            InstallPreparedSnapshot(next, reparsed, nextSearcher, nextLocator);
            return outcome;
        }

        private void InstallSnapshot(RuntimeDataSnapshot snapshot, SaveSession saveSession)
        {
            if (snapshot == null) throw new ArgumentNullException("snapshot");
            InstallPreparedSnapshot(
                snapshot,
                saveSession,
                new CustomEffectSearcher(snapshot.CoreModel, snapshot.CoreCatalog),
                new RelicLocator(snapshot.CoreModel, snapshot.CoreCatalog));
        }

        private void InstallPreparedSnapshot(
            RuntimeDataSnapshot snapshot,
            SaveSession saveSession,
            CustomEffectSearcher searcher,
            RelicLocator locator)
        {
            _snapshot = snapshot;
            _searcher = searcher;
            _stableSearch = new StableExactV1(snapshot.CoreModel, snapshot.CoreCatalog);
            _locator = locator;
            _currentSave = saveSession;
            _saveManagement = new SaveManagementService(snapshot.CoreModel, _applicationRoot, _localSaveRoot);
        }

        private CharacterInventory RequireInventory(int slotIndex)
        {
            if (_currentSave == null) throw new InvalidOperationException("尚未导入存档。");
            if (!string.Equals(_currentSave.SnapshotId, _snapshot.SnapshotId, StringComparison.Ordinal))
                throw new InvalidOperationException("存档会话与运行时数据快照不一致。");
            CharacterInventory inventory = _currentSave.GetInventory(slotIndex);
            if (inventory == null) throw new ArgumentException("存档中不存在角色栏位 " + (slotIndex + 1) + "。");
            return inventory;
        }

        private void RequireBuildOwnership(CharacterInventory inventory, CustomSearchBuild build)
        {
            if (build == null || build.Vessel == null || build.Assignments == null || build.Assignments.Length != 6
                || !ReferenceEquals(build.Vessel, _snapshot.CoreModel.GetVessel(build.Vessel.Id)))
                throw new ArgumentException("当前组合不属于此数据快照或存档仓库。", "build");
            Dictionary<int, RelicInstance> owned = inventory.Relics.ToDictionary(item => item.InstanceId);
            HashSet<int> seen = new HashSet<int>();
            foreach (SlotAssignment assignment in build.Assignments)
            {
                RelicInstance current;
                if (assignment == null || assignment.Relic == null || !seen.Add(assignment.Relic.InstanceId)
                    || !owned.TryGetValue(assignment.Relic.InstanceId, out current) || !ReferenceEquals(current, assignment.Relic))
                    throw new ArgumentException("当前组合不属于此数据快照或存档仓库。", "build");
            }
        }

        private static CustomEffectRule[] LimitRules(IEnumerable<CustomEffectRule> rules)
        {
            return (rules ?? Enumerable.Empty<CustomEffectRule>()).Take(10).ToArray();
        }

        private CharacterDefinition RequireCharacter(string characterId)
        {
            CharacterDefinition character = _snapshot.CoreModel.GetCharacter(characterId);
            if (character == null) throw new ArgumentException("不存在的游戏角色：" + characterId);
            return character;
        }

        private CustomSearchBuild RehydrateBuildLocked(CharacterInventory inventory, CharacterDefinition character,
            IEnumerable<CustomEffectRule> rules, int vesselId, int[] instanceIds)
        {
            if (instanceIds == null || instanceIds.Length != 6 || instanceIds.Distinct().Count() != 6) return null;
            if (!character.EligibleVesselIds.Contains(vesselId)) return null;
            VesselDefinition vessel = _snapshot.CoreModel.GetVessel(vesselId);
            if (vessel == null) return null;
            Dictionary<int, RelicInstance> owned = inventory.Relics.ToDictionary(item => item.InstanceId);
            SlotAssignment[] assignments = new SlotAssignment[6];
            for (int index = 0; index < 6; index++)
            {
                RelicInstance relic;
                if (!owned.TryGetValue(instanceIds[index], out relic)) return null;
                bool deep = index >= 3;
                if (relic.IsDeep != deep) return null;
                int slotIndex = index % 3;
                int colorId = deep ? vessel.DeepSlotColorIds[slotIndex] : vessel.OrdinarySlotColorIds[slotIndex];
                if (!AcceptsColor(colorId, relic.ColorId)) return null;
                assignments[index] = new SlotAssignment(deep, slotIndex, colorId, relic);
            }
            return _searcher.EvaluateBuild(vessel, assignments, character.Id, rules);
        }

        private string CurrentDataIdentity()
        {
            return (_snapshot.PackId ?? string.Empty) + "|" + (_snapshot.PackVersion ?? string.Empty)
                + "|" + (_snapshot.GameVersion ?? string.Empty) + "|" + (_snapshot.DataVersion ?? string.Empty);
        }

        private static string BuildKey(CustomSearchBuild build)
        {
            return build == null || build.Assignments == null
                ? string.Empty
                : build.Vessel.Id + ":" + string.Join(",", build.Assignments.Select(item => item.Relic.InstanceId));
        }

        private static bool AcceptsColor(int slotColorId, int relicColorId)
        {
            return relicColorId >= 0 && relicColorId <= 3 && (slotColorId == 4 || slotColorId == relicColorId);
        }
    }
}
