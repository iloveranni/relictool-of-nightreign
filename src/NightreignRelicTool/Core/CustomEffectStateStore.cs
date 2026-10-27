using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace NightreignRelicTool.Core
{
    public sealed class SavedCustomRule
    {
        public string RuleId { get; set; }
        public string SelectorId { get; set; }
        public int? PresetItemId { get; set; }
        public bool IsRequired { get; set; }
        public int QuantityTarget { get; set; }
    }

    public sealed class SavedCustomBuildState
    {
        public SavedCustomBuildState()
        {
            OptimalInstanceIds = new int[0];
            CurrentInstanceIds = new int[0];
            CandidateIndexes = new int[0];
            CandidateCounts = new int[0];
        }

        public int VesselId { get; set; }
        public int[] OptimalInstanceIds { get; set; }
        public int[] CurrentInstanceIds { get; set; }
        public int[] CandidateIndexes { get; set; }
        public int[] CandidateCounts { get; set; }
        public bool IsManual { get; set; }
    }

    public sealed class SavedCustomSearchState
    {
        public SavedCustomSearchState()
        {
            Rules = new List<SavedCustomRule>();
            ResultInstanceIds = new int[0];
            OptimalInstanceIds = new int[0];
            UsageCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            BuildStates = new List<SavedCustomBuildState>();
        }

        public string SaveIdentity { get; set; }
        public int SaveSlotIndex { get; set; }
        public string CharacterId { get; set; }
        public string RepositoryFingerprint { get; set; }
        public string DataIdentity { get; set; }
        public List<SavedCustomRule> Rules { get; set; }
        public int? ResultVesselId { get; set; }
        public int[] ResultInstanceIds { get; set; }
        public int? OptimalVesselId { get; set; }
        public int[] OptimalInstanceIds { get; set; }
        public bool IsManualResult { get; set; }
        public Dictionary<string, int> UsageCounts { get; set; }
        public string LastSelectedRuleId { get; set; }
        public int UiScaleIndex { get; set; }
        public string SearchText { get; set; }
        public string PrimaryCategory { get; set; }
        public int? SecondaryCategoryId { get; set; }
        public string PresetGroup { get; set; }
        public int SelectedResultIndex { get; set; }
        public List<SavedCustomBuildState> BuildStates { get; set; }
        public DateTime UpdatedUtc { get; set; }
        public bool RepositoryChangedOnLoad { get; internal set; }
        public bool DataChangedOnLoad { get; internal set; }
    }

    public sealed class CustomEffectStateStore
    {
        private readonly string _path;
        private readonly object _sync = new object();

        public CustomEffectStateStore(string applicationRoot)
        {
            string root = Path.GetFullPath(applicationRoot ?? AppDomain.CurrentDomain.BaseDirectory);
            _path = Path.Combine(root, "UserData", "CustomEffectSearch", "state.json");
        }

        public string LastWarning { get; private set; }

        public SavedCustomSearchState Load(string saveIdentity, string characterId)
        {
            return Load(saveIdentity, -1, characterId, null);
        }

        public SavedCustomSearchState Load(string saveIdentity, int saveSlotIndex, string characterId, string repositoryFingerprint)
        {
            return Load(saveIdentity, saveSlotIndex, characterId, repositoryFingerprint, null);
        }

        public SavedCustomSearchState Load(string saveIdentity, int saveSlotIndex, string characterId,
            string repositoryFingerprint, string dataIdentity)
        {
            if (string.IsNullOrWhiteSpace(saveIdentity) || string.IsNullOrWhiteSpace(characterId)) return null;
            lock (_sync)
            {
                StateFileDto file = ReadFile();
                StateEntryDto entry = file.Entries.FirstOrDefault(item => item != null
                    && string.Equals(item.SaveIdentity, saveIdentity, StringComparison.Ordinal)
                    && (saveSlotIndex < 0 || file.SchemaVersion == "1.0.0" || item.SaveSlotIndex == saveSlotIndex)
                    && string.Equals(item.CharacterId, characterId, StringComparison.OrdinalIgnoreCase));
                if (entry == null) return null;
                SavedCustomSearchState state = FromDto(entry);
                bool repositoryChanged = !string.IsNullOrWhiteSpace(repositoryFingerprint)
                    && !string.Equals(state.RepositoryFingerprint, repositoryFingerprint, StringComparison.Ordinal);
                bool dataChanged = !string.IsNullOrWhiteSpace(dataIdentity)
                    && !string.Equals(state.DataIdentity, dataIdentity, StringComparison.Ordinal);
                if (repositoryChanged || dataChanged)
                {
                    state.RepositoryChangedOnLoad = repositoryChanged;
                    state.DataChangedOnLoad = dataChanged;
                    state.RepositoryFingerprint = repositoryFingerprint;
                    state.DataIdentity = dataIdentity;
                    state.ResultVesselId = null;
                    state.ResultInstanceIds = new int[0];
                    state.OptimalVesselId = null;
                    state.OptimalInstanceIds = new int[0];
                    state.IsManualResult = false;
                    state.BuildStates.Clear();
                }
                return state;
            }
        }

        public void Save(SavedCustomSearchState state)
        {
            if (state == null) throw new ArgumentNullException("state");
            if (string.IsNullOrWhiteSpace(state.SaveIdentity) || string.IsNullOrWhiteSpace(state.CharacterId))
                throw new ArgumentException("状态缺少存档身份或角色。");
            lock (_sync)
            {
                StateFileDto file = ReadFile();
                file.SchemaVersion = "2.0.0";
                file.Entries.RemoveAll(item =>
                    string.Equals(item.SaveIdentity, state.SaveIdentity, StringComparison.Ordinal)
                    && item.SaveSlotIndex == state.SaveSlotIndex
                    && string.Equals(item.CharacterId, state.CharacterId, StringComparison.OrdinalIgnoreCase));
                state.UpdatedUtc = DateTime.UtcNow;
                file.Entries.Add(ToDto(state));
                WriteFile(file);
            }
        }

        public static string ComputeSaveIdentity(string sourcePath, int inventorySlotIndex)
        {
            string normalized = Path.GetFullPath(sourcePath ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar)
                .ToUpperInvariant() + "|slot:" + inventorySlotIndex;
            return Hash(Encoding.UTF8.GetBytes(normalized));
        }

        public static string ComputeSaveIdentityFromSourceKey(string sourceKey, int inventorySlotIndex)
        {
            if (!SaveManagementService.IsDigest(sourceKey))
                throw new ArgumentException("存档来源键无效。", "sourceKey");
            string normalized = "source:" + sourceKey.ToUpperInvariant() + "|slot:" + inventorySlotIndex;
            return Hash(Encoding.UTF8.GetBytes(normalized));
        }

        public static string ComputeRepositoryFingerprint(CharacterInventory inventory)
        {
            if (inventory == null) throw new ArgumentNullException("inventory");
            StringBuilder text = new StringBuilder();
            foreach (RelicInstance relic in inventory.Relics.OrderBy(item => item.InstanceId))
            {
                text.Append(relic.InstanceId).Append(':').Append(relic.ItemId).Append(':')
                    .Append(relic.ColorId).Append(':').Append(relic.IsDeep ? 1 : 0).Append(':');
                foreach (int id in relic.PositiveEffectIds) text.Append('p').Append(id).Append(',');
                foreach (int id in relic.NegativeEffectIds) text.Append('n').Append(id).Append(',');
                text.Append(';');
            }
            return Hash(Encoding.UTF8.GetBytes(text.ToString()));
        }

        private StateFileDto ReadFile()
        {
            LastWarning = null;
            if (!File.Exists(_path)) return EmptyFile();
            try
            {
                using (FileStream stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(StateFileDto));
                    StateFileDto file = (StateFileDto)serializer.ReadObject(stream);
                    if (file == null
                        || (file.SchemaVersion != "1.0.0" && file.SchemaVersion != "2.0.0")
                        || file.Entries == null)
                        throw new InvalidDataException("状态文件 schemaVersion 或 entries 无效。");
                    ValidateFile(file);
                    return file;
                }
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException
                || error is SerializationException || error is InvalidDataException)
            {
                LastWarning = "本地状态文件已损坏，已使用空状态。";
                return EmptyFile();
            }
        }

        private static StateFileDto EmptyFile()
        {
            return new StateFileDto { SchemaVersion = "2.0.0", Entries = new List<StateEntryDto>() };
        }

        private static void ValidateFile(StateFileDto file)
        {
            if (file.Entries.Any(entry => entry == null
                || string.IsNullOrWhiteSpace(entry.SaveIdentity)
                || string.IsNullOrWhiteSpace(entry.CharacterId)
                || (entry.Rules != null && entry.Rules.Any(rule => rule == null))
                || (entry.BuildStates != null && entry.BuildStates.Any(build => build == null))
                || (entry.UsageCounts != null && entry.UsageCounts.Keys.Any(string.IsNullOrWhiteSpace))))
                throw new InvalidDataException("状态文件包含无效的条目、规则或组合状态。");
        }

        private void WriteFile(StateFileDto file)
        {
            string directory = Path.GetDirectoryName(_path);
            Directory.CreateDirectory(directory);
            string temporary = _path + ".tmp";
            try
            {
                using (FileStream stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(StateFileDto));
                    serializer.WriteObject(stream, file);
                    stream.Flush(true);
                }
                if (File.Exists(_path))
                {
                    string backup = _path + ".bak";
                    File.Replace(temporary, _path, backup, true);
                    try { File.Delete(backup); } catch (IOException) { }
                }
                else File.Move(temporary, _path);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { }
                throw new CustomEffectDataException("无法保存自定义词条本地状态：" + error.Message, error);
            }
        }

        private static string Hash(byte[] bytes)
        {
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] hash = algorithm.ComputeHash(bytes);
                StringBuilder text = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash) text.Append(value.ToString("X2"));
                return text.ToString();
            }
        }

        private static SavedCustomSearchState FromDto(StateEntryDto entry)
        {
            return new SavedCustomSearchState
            {
                SaveIdentity = entry.SaveIdentity,
                SaveSlotIndex = entry.SaveSlotIndex,
                CharacterId = entry.CharacterId,
                RepositoryFingerprint = entry.RepositoryFingerprint,
                DataIdentity = entry.DataIdentity,
                Rules = (entry.Rules ?? new List<SavedRuleDto>()).Take(10).Select(item => new SavedCustomRule
                {
                    RuleId = item.RuleId,
                    SelectorId = item.SelectorId,
                    PresetItemId = item.PresetItemId,
                    IsRequired = item.IsRequired,
                    QuantityTarget = item.QuantityTarget
                }).ToList(),
                ResultVesselId = entry.ResultVesselId,
                ResultInstanceIds = entry.ResultInstanceIds == null ? new int[0] : entry.ResultInstanceIds.ToArray(),
                OptimalVesselId = entry.OptimalVesselId,
                OptimalInstanceIds = entry.OptimalInstanceIds == null ? new int[0] : entry.OptimalInstanceIds.ToArray(),
                IsManualResult = entry.IsManualResult,
                UsageCounts = entry.UsageCounts == null
                    ? new Dictionary<string, int>(StringComparer.Ordinal)
                    : new Dictionary<string, int>(entry.UsageCounts, StringComparer.Ordinal),
                LastSelectedRuleId = entry.LastSelectedRuleId,
                UiScaleIndex = entry.UiScaleIndex,
                SearchText = entry.SearchText,
                PrimaryCategory = entry.PrimaryCategory,
                SecondaryCategoryId = entry.SecondaryCategoryId,
                PresetGroup = entry.PresetGroup,
                SelectedResultIndex = entry.SelectedResultIndex,
                BuildStates = (entry.BuildStates ?? new List<SavedBuildStateDto>()).Select(item => new SavedCustomBuildState
                {
                    VesselId = item.VesselId,
                    OptimalInstanceIds = item.OptimalInstanceIds == null ? new int[0] : item.OptimalInstanceIds.ToArray(),
                    CurrentInstanceIds = item.CurrentInstanceIds == null ? new int[0] : item.CurrentInstanceIds.ToArray(),
                    CandidateIndexes = item.CandidateIndexes == null ? new int[0] : item.CandidateIndexes.ToArray(),
                    CandidateCounts = item.CandidateCounts == null ? new int[0] : item.CandidateCounts.ToArray(),
                    IsManual = item.IsManual
                }).ToList(),
                UpdatedUtc = entry.UpdatedUtc
            };
        }

        private static StateEntryDto ToDto(SavedCustomSearchState state)
        {
            return new StateEntryDto
            {
                SaveIdentity = state.SaveIdentity,
                SaveSlotIndex = state.SaveSlotIndex,
                CharacterId = state.CharacterId,
                RepositoryFingerprint = state.RepositoryFingerprint,
                DataIdentity = state.DataIdentity,
                Rules = (state.Rules ?? new List<SavedCustomRule>()).Take(10).Select(item => new SavedRuleDto
                {
                    RuleId = item.RuleId,
                    SelectorId = item.SelectorId,
                    PresetItemId = item.PresetItemId,
                    IsRequired = item.IsRequired,
                    QuantityTarget = item.QuantityTarget
                }).ToList(),
                ResultVesselId = state.ResultVesselId,
                ResultInstanceIds = state.ResultInstanceIds == null ? new List<int>() : state.ResultInstanceIds.ToList(),
                OptimalVesselId = state.OptimalVesselId,
                OptimalInstanceIds = state.OptimalInstanceIds == null ? new List<int>() : state.OptimalInstanceIds.ToList(),
                IsManualResult = state.IsManualResult,
                UsageCounts = state.UsageCounts == null
                    ? new Dictionary<string, int>()
                    : new Dictionary<string, int>(state.UsageCounts),
                LastSelectedRuleId = state.LastSelectedRuleId,
                UiScaleIndex = state.UiScaleIndex,
                SearchText = state.SearchText,
                PrimaryCategory = state.PrimaryCategory,
                SecondaryCategoryId = state.SecondaryCategoryId,
                PresetGroup = state.PresetGroup,
                SelectedResultIndex = state.SelectedResultIndex,
                BuildStates = (state.BuildStates ?? new List<SavedCustomBuildState>()).Select(item => new SavedBuildStateDto
                {
                    VesselId = item.VesselId,
                    OptimalInstanceIds = item.OptimalInstanceIds == null ? new List<int>() : item.OptimalInstanceIds.ToList(),
                    CurrentInstanceIds = item.CurrentInstanceIds == null ? new List<int>() : item.CurrentInstanceIds.ToList(),
                    CandidateIndexes = item.CandidateIndexes == null ? new List<int>() : item.CandidateIndexes.ToList(),
                    CandidateCounts = item.CandidateCounts == null ? new List<int>() : item.CandidateCounts.ToList(),
                    IsManual = item.IsManual
                }).ToList(),
                UpdatedUtc = state.UpdatedUtc
            };
        }
    }

    public sealed class CustomSearchRevisionGate<T> where T : class
    {
        private readonly object _sync = new object();
        private long _revision;

        public long BeginRevision()
        {
            lock (_sync) return ++_revision;
        }

        public bool TryAccept(long revision, T value)
        {
            lock (_sync)
            {
                if (revision != _revision) return false;
                Latest = value;
                return true;
            }
        }

        public T Latest { get; private set; }
        public long CurrentRevision { get { lock (_sync) return _revision; } }
    }

#pragma warning disable 0649
    [DataContract]
    internal sealed class StateFileDto
    {
        [DataMember(Name = "schemaVersion")] public string SchemaVersion;
        [DataMember(Name = "entries")] public List<StateEntryDto> Entries;
    }
    [DataContract]
    internal sealed class StateEntryDto
    {
        [DataMember(Name = "saveIdentity")] public string SaveIdentity;
        [DataMember(Name = "saveSlotIndex")] public int SaveSlotIndex;
        [DataMember(Name = "characterId")] public string CharacterId;
        [DataMember(Name = "repositoryFingerprint")] public string RepositoryFingerprint;
        [DataMember(Name = "dataIdentity")] public string DataIdentity;
        [DataMember(Name = "rules")] public List<SavedRuleDto> Rules;
        [DataMember(Name = "resultVesselId")] public int? ResultVesselId;
        [DataMember(Name = "resultInstanceIds")] public List<int> ResultInstanceIds;
        [DataMember(Name = "optimalVesselId")] public int? OptimalVesselId;
        [DataMember(Name = "optimalInstanceIds")] public List<int> OptimalInstanceIds;
        [DataMember(Name = "isManualResult")] public bool IsManualResult;
        [DataMember(Name = "usageCounts")] public Dictionary<string, int> UsageCounts;
        [DataMember(Name = "lastSelectedRuleId")] public string LastSelectedRuleId;
        [DataMember(Name = "uiScaleIndex")] public int UiScaleIndex;
        [DataMember(Name = "searchText")] public string SearchText;
        [DataMember(Name = "primaryCategory")] public string PrimaryCategory;
        [DataMember(Name = "secondaryCategoryId")] public int? SecondaryCategoryId;
        [DataMember(Name = "presetGroup")] public string PresetGroup;
        [DataMember(Name = "selectedResultIndex")] public int SelectedResultIndex;
        [DataMember(Name = "buildStates")] public List<SavedBuildStateDto> BuildStates;
        [DataMember(Name = "updatedUtc")] public DateTime UpdatedUtc;
    }
    [DataContract]
    internal sealed class SavedRuleDto
    {
        [DataMember(Name = "ruleId")] public string RuleId;
        [DataMember(Name = "selectorId")] public string SelectorId;
        [DataMember(Name = "presetItemId")] public int? PresetItemId;
        [DataMember(Name = "isRequired")] public bool IsRequired;
        [DataMember(Name = "quantityTarget")] public int QuantityTarget;
    }
    [DataContract]
    internal sealed class SavedBuildStateDto
    {
        [DataMember(Name = "vesselId")] public int VesselId;
        [DataMember(Name = "optimalInstanceIds")] public List<int> OptimalInstanceIds;
        [DataMember(Name = "currentInstanceIds")] public List<int> CurrentInstanceIds;
        [DataMember(Name = "candidateIndexes")] public List<int> CandidateIndexes;
        [DataMember(Name = "candidateCounts")] public List<int> CandidateCounts;
        [DataMember(Name = "isManual")] public bool IsManual;
    }
#pragma warning restore 0649
}
