using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace NightreignRelicTool.Core
{
    public sealed class RelicInventoryLayoutProfile
    {
        private const string ResourceName = "NightreignRelicTool.Resources.RelicInventoryLayout.json";
        private const string SupportedSchemaVersion = "nightreign.relic-inventory-layout.v1";
        private const string SupportedFillDirection = "row_major_left_to_right_top_to_bottom";
        private const string SupportedSortKey = "inventory_entry_sort_key_uint32_le";
        private const string SupportedSortDirection = "descending";
        private const string SupportedPartition = "is_deep_then_color";
        private const string SupportedTiePolicy = "exact_position_unavailable";

        internal RelicInventoryLayoutProfile(
            string profileId,
            string[] supportedGameVersions,
            int columns,
            string fillDirection,
            string sortKey,
            string sortDirection,
            string partition,
            string tiePolicy,
            string evidenceStatus,
            string verificationStatus,
            RelicInventoryLayoutSource source)
        {
            ProfileId = Required(profileId, "profileId");
            SupportedGameVersions = (supportedGameVersions ?? new string[0]).ToArray();
            Columns = columns;
            FillDirection = Required(fillDirection, "fillDirection");
            SortKey = Required(sortKey, "sortKey");
            SortDirection = Required(sortDirection, "sortDirection");
            Partition = Required(partition, "partition");
            TiePolicy = Required(tiePolicy, "tiePolicy");
            EvidenceStatus = Required(evidenceStatus, "evidenceStatus");
            VerificationStatus = Required(verificationStatus, "verificationStatus");
            Source = source ?? throw new InvalidDataException("遗物仓库布局资料缺少来源。");
            Validate();
        }

        public string ProfileId { get; private set; }
        public string[] SupportedGameVersions { get; private set; }
        public int Columns { get; private set; }
        public string FillDirection { get; private set; }
        public string SortKey { get; private set; }
        public string SortDirection { get; private set; }
        public string Partition { get; private set; }
        public string TiePolicy { get; private set; }
        public string EvidenceStatus { get; private set; }
        public string VerificationStatus { get; private set; }
        public RelicInventoryLayoutSource Source { get; private set; }

        public bool IsExactPositionVerified
        {
            get { return string.Equals(VerificationStatus, "project_verified", StringComparison.Ordinal); }
        }

        public bool SupportsGameVersion(string gameVersion)
        {
            return !string.IsNullOrWhiteSpace(gameVersion)
                && SupportedGameVersions.Contains(gameVersion, StringComparer.Ordinal);
        }

        public static RelicInventoryLayoutProfile LoadBuiltIn()
        {
            Assembly assembly = typeof(RelicInventoryLayoutProfile).Assembly;
            using (Stream stream = assembly.GetManifestResourceStream(ResourceName))
            {
                if (stream == null) throw new InvalidDataException("找不到内置遗物仓库布局资料。");
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(RelicInventoryLayoutProfileDto));
                RelicInventoryLayoutProfileDto dto;
                try { dto = (RelicInventoryLayoutProfileDto)serializer.ReadObject(stream); }
                catch (Exception error) when (error is SerializationException || error is InvalidCastException)
                {
                    throw new InvalidDataException("内置遗物仓库布局资料无法解析。", error);
                }
                if (dto == null || !string.Equals(dto.SchemaVersion, SupportedSchemaVersion, StringComparison.Ordinal))
                    throw new InvalidDataException("内置遗物仓库布局 schemaVersion 不受支持。");
                RelicInventoryLayoutSource source = dto.Source == null ? null : new RelicInventoryLayoutSource(
                    dto.Source.Kind, dto.Source.Uri, dto.Source.Revision,
                    string.IsNullOrWhiteSpace(dto.Source.ConfirmedDate)
                        ? dto.Source.RetrievedDate : dto.Source.ConfirmedDate,
                    dto.Source.ClaimScope);
                return new RelicInventoryLayoutProfile(
                    dto.ProfileId,
                    dto.SupportedGameVersions == null ? null : dto.SupportedGameVersions.ToArray(),
                    dto.Columns,
                    dto.FillDirection,
                    dto.SortKey,
                    dto.SortDirection,
                    dto.Partition,
                    dto.TiePolicy,
                    dto.EvidenceStatus,
                    dto.VerificationStatus,
                    source);
            }
        }

        private void Validate()
        {
            if (SupportedGameVersions.Length == 0
                || SupportedGameVersions.Any(string.IsNullOrWhiteSpace)
                || SupportedGameVersions.Distinct(StringComparer.Ordinal).Count() != SupportedGameVersions.Length)
                throw new InvalidDataException("遗物仓库布局支持的游戏版本无效。");
            if (Columns <= 0 || Columns > 64)
                throw new InvalidDataException("遗物仓库布局列数无效。");
            if (!string.Equals(FillDirection, SupportedFillDirection, StringComparison.Ordinal)
                || !string.Equals(SortKey, SupportedSortKey, StringComparison.Ordinal)
                || !string.Equals(SortDirection, SupportedSortDirection, StringComparison.Ordinal)
                || !string.Equals(Partition, SupportedPartition, StringComparison.Ordinal)
                || !string.Equals(TiePolicy, SupportedTiePolicy, StringComparison.Ordinal))
                throw new InvalidDataException("遗物仓库布局包含当前程序不支持的规则。");
            if (!string.Equals(EvidenceStatus, "external_cross_reference", StringComparison.Ordinal)
                && !string.Equals(EvidenceStatus, "project_verified", StringComparison.Ordinal))
                throw new InvalidDataException("遗物仓库布局证据状态无效。");
            if (!string.Equals(VerificationStatus, "unverified", StringComparison.Ordinal)
                && !string.Equals(VerificationStatus, "project_verified", StringComparison.Ordinal))
                throw new InvalidDataException("遗物仓库布局验证状态无效。");
            if (IsExactPositionVerified
                && !string.Equals(EvidenceStatus, "project_verified", StringComparison.Ordinal))
                throw new InvalidDataException("遗物仓库布局未具备项目实机证据，不能标记为已验证。");
            if (IsExactPositionVerified
                && !string.Equals(Source.Kind, "user_game_observation", StringComparison.Ordinal))
                throw new InvalidDataException("项目确认的遗物仓库布局必须保留用户实机观察来源。");
        }

        private static string Required(string value, string label)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidDataException("遗物仓库布局缺少 " + label + "。");
            return value.Trim();
        }
    }

    public sealed class RelicInventoryLayoutSource
    {
        internal RelicInventoryLayoutSource(string kind, string uri, string revision, string confirmedDate, string claimScope)
        {
            Kind = Required(kind, "来源类型");
            Uri = Required(uri, "来源地址");
            Revision = Required(revision, "来源版本");
            ConfirmedDate = Required(confirmedDate, "确认日期");
            ClaimScope = Required(claimScope, "证据边界");
        }

        public string Kind { get; private set; }
        public string Uri { get; private set; }
        public string Revision { get; private set; }
        public string ConfirmedDate { get; private set; }
        public string RetrievedDate { get { return ConfirmedDate; } }
        public string ClaimScope { get; private set; }

        private static string Required(string value, string label)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidDataException("遗物仓库布局缺少" + label + "。");
            return value.Trim();
        }
    }

#pragma warning disable 0649
    [DataContract]
    internal sealed class RelicInventoryLayoutProfileDto
    {
        [DataMember(Name = "schemaVersion", IsRequired = true)] public string SchemaVersion;
        [DataMember(Name = "profileId", IsRequired = true)] public string ProfileId;
        [DataMember(Name = "supportedGameVersions", IsRequired = true)] public List<string> SupportedGameVersions;
        [DataMember(Name = "columns", IsRequired = true)] public int Columns;
        [DataMember(Name = "fillDirection", IsRequired = true)] public string FillDirection;
        [DataMember(Name = "sortKey", IsRequired = true)] public string SortKey;
        [DataMember(Name = "sortDirection", IsRequired = true)] public string SortDirection;
        [DataMember(Name = "partition", IsRequired = true)] public string Partition;
        [DataMember(Name = "tiePolicy", IsRequired = true)] public string TiePolicy;
        [DataMember(Name = "evidenceStatus", IsRequired = true)] public string EvidenceStatus;
        [DataMember(Name = "verificationStatus", IsRequired = true)] public string VerificationStatus;
        [DataMember(Name = "source", IsRequired = true)] public RelicInventoryLayoutSourceDto Source;
    }

    [DataContract]
    internal sealed class RelicInventoryLayoutSourceDto
    {
        [DataMember(Name = "kind", IsRequired = true)] public string Kind;
        [DataMember(Name = "uri", IsRequired = true)] public string Uri;
        [DataMember(Name = "revision", IsRequired = true)] public string Revision;
        [DataMember(Name = "confirmedDate", EmitDefaultValue = false)] public string ConfirmedDate;
        [DataMember(Name = "retrievedDate", EmitDefaultValue = false)] public string RetrievedDate;
        [DataMember(Name = "claimScope", IsRequired = true)] public string ClaimScope;
    }
#pragma warning restore 0649
}
