using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using NightreignRelicTool.Core;

namespace NightreignRelicTool.CustomEffectSearch
{
    /// <summary>
    /// Small UI-only preference store. Repository-bound rules and results are persisted
    /// by Core.CustomEffectStateStore; this document only supports the pre-import UI and
    /// remembers the last local path and zoom level.
    /// </summary>
    public interface ICustomEffectUiStateService
    {
        CustomEffectUiStateDocument Load();
        void Save(CustomEffectUiStateDocument state);
    }

    [DataContract]
    public sealed class CustomEffectUiStateDocument
    {
        public CustomEffectUiStateDocument()
        {
            Characters = new List<CustomEffectCharacterUiState>();
        }

        [DataMember(Name = "lastCharacterId")] public string LastCharacterId { get; set; }
        [DataMember(Name = "uiScaleIndex", EmitDefaultValue = false)] public int? UiScaleIndex { get; set; }
        [DataMember(Name = "selectedSavePath", EmitDefaultValue = false)] public string SelectedSavePath { get; set; }
        [DataMember(Name = "selectedSaveSlotIndex", EmitDefaultValue = false)] public int? SelectedSaveSlotIndex { get; set; }
        [DataMember(Name = "characters")] public List<CustomEffectCharacterUiState> Characters { get; set; }
    }

    [DataContract]
    public sealed class CustomEffectCharacterUiState
    {
        public CustomEffectCharacterUiState()
        {
            Rules = new List<CustomEffectRuleUiState>();
            UsageCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        }

        [DataMember(Name = "characterId")] public string CharacterId { get; set; }
        [DataMember(Name = "searchText")] public string SearchText { get; set; }
        [DataMember(Name = "primaryCategory")] public string PrimaryCategory { get; set; }
        [DataMember(Name = "secondaryCategoryId")] public int? SecondaryCategoryId { get; set; }
        [DataMember(Name = "secondaryCategoryScopeExplicit")] public bool SecondaryCategoryScopeExplicit { get; set; }
        [DataMember(Name = "presetGroup")] public string PresetGroup { get; set; }
        [DataMember(Name = "selectedResultIndex")] public int SelectedResultIndex { get; set; }
        [DataMember(Name = "rules")] public List<CustomEffectRuleUiState> Rules { get; set; }
        [DataMember(Name = "usageCounts")] public Dictionary<string, int> UsageCounts { get; set; }
    }

    [DataContract]
    public sealed class CustomEffectRuleUiState
    {
        [DataMember(Name = "ruleId")] public string RuleId { get; set; }
        [DataMember(Name = "selectorId")] public string SelectorId { get; set; }
        [DataMember(Name = "presetItemId")] public int? PresetItemId { get; set; }
        [DataMember(Name = "isRequired")] public bool IsRequired { get; set; }
        [DataMember(Name = "quantityTarget")] public int QuantityTarget { get; set; }
    }

    public sealed class JsonCustomEffectUiStateService : ICustomEffectUiStateService
    {
        private static readonly object WriteSync = new object();
        private readonly string _path;
        private readonly string _legacyPath;
        private readonly Action<string, string> _publish;

        public JsonCustomEffectUiStateService(string applicationRoot)
            : this(applicationRoot, PublishFile)
        {
        }

        internal JsonCustomEffectUiStateService(string applicationRoot, Action<string, string> publish)
        {
            string root = Path.GetFullPath(applicationRoot ?? AppDomain.CurrentDomain.BaseDirectory);
            _path = Path.Combine(root, "UserData", "CustomEffectSearch", "ui-state.json");
            _legacyPath = Path.Combine(root, "UserData", "CustomEffectSearchPrototype", "state.json");
            _publish = publish ?? throw new ArgumentNullException("publish");
        }

        public CustomEffectUiStateDocument Load()
        {
            string source = File.Exists(_path) ? _path : File.Exists(_legacyPath) ? _legacyPath : null;
            if (source == null) return new CustomEffectUiStateDocument();
            try
            {
                using (FileStream stream = new FileStream(source, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                {
                    CustomEffectUiStateDocument value = (CustomEffectUiStateDocument)CreateSerializer().ReadObject(stream);
                    if (value == null) return new CustomEffectUiStateDocument();
                    if (value.Characters == null) value.Characters = new List<CustomEffectCharacterUiState>();
                    if (value.Characters.Any(item => item == null
                        || string.IsNullOrWhiteSpace(item.CharacterId)
                        || (item.Rules != null && item.Rules.Any(rule => rule == null))))
                        return new CustomEffectUiStateDocument();
                    foreach (CustomEffectCharacterUiState item in value.Characters)
                    {
                        if (item.Rules == null) item.Rules = new List<CustomEffectRuleUiState>();
                        if (item.UsageCounts == null) item.UsageCounts = new Dictionary<string, int>(StringComparer.Ordinal);
                    }
                    return value;
                }
            }
            catch (Exception)
            {
                return new CustomEffectUiStateDocument();
            }
        }

        public void Save(CustomEffectUiStateDocument state)
        {
            lock (WriteSync) SaveCore(state);
        }

        private void SaveCore(CustomEffectUiStateDocument state)
        {
            if (state == null) throw new ArgumentNullException("state");
            string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            bool publicationUncertain = false;
            try
            {
                string directory = Path.GetDirectoryName(_path);
                Directory.CreateDirectory(directory);
                using (FileStream stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    CreateSerializer().WriteObject(stream, state);
                    stream.Flush(true);
                }
                LocalPreferencePublication.Publish(temporary, _path, _publish);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException
                || error is SerializationException || error is ArgumentException || error is NotSupportedException)
            {
                publicationUncertain = LocalPreferencePublication.IsUncertain(error);
                throw new InvalidOperationException("无法保存本地界面状态。", error);
            }
            finally
            {
                // On 1176/1177 this may be the only remaining complete document.
                // Preserve it without guessing which path Windows has committed.
                try { if (!publicationUncertain && File.Exists(temporary)) File.Delete(temporary); }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { }
            }
        }

        private static void PublishFile(string temporary, string destination)
        {
            if (File.Exists(destination)) File.Replace(temporary, destination, null, true);
            else File.Move(temporary, destination);
        }

        private static DataContractJsonSerializer CreateSerializer()
        {
            return new DataContractJsonSerializer(
                typeof(CustomEffectUiStateDocument),
                new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
        }
    }
}
