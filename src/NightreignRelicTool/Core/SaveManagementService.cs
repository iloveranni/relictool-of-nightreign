using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

[assembly: InternalsVisibleTo("NightreignRelicTool.Integration.Tests")]

namespace NightreignRelicTool.Core
{
    public enum SaveSourceKind
    {
        LocalSteam,
        Manual
    }

    public enum SaveFileType
    {
        Sl2,
        Co2
    }

    public enum SaveValidationStatus
    {
        Unknown,
        Valid,
        Invalid,
        Missing
    }

    public enum SaveRefreshStatus
    {
        Updated,
        Unchanged,
        Failed
    }

    public sealed class SaveSourceKey : IEquatable<SaveSourceKey>
    {
        internal SaveSourceKey(string value)
        {
            if (!SaveManagementService.IsDigest(value)) throw new ArgumentException("来源键无效。", "value");
            Value = value.ToUpperInvariant();
        }

        public string Value { get; private set; }

        public bool Equals(SaveSourceKey other)
        {
            return other != null && string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) { return Equals(obj as SaveSourceKey); }
        public override int GetHashCode() { return StringComparer.Ordinal.GetHashCode(Value); }
        public override string ToString() { return Value; }
    }

    public sealed class SaveCharacterSlotInfo
    {
        internal SaveCharacterSlotInfo(int slotIndex, string playerName, string repositoryFingerprint)
        {
            SlotIndex = slotIndex;
            PlayerName = playerName;
            RepositoryFingerprint = repositoryFingerprint;
        }

        public int SlotIndex { get; private set; }
        public string PlayerName { get; private set; }
        public string RepositoryFingerprint { get; private set; }
    }

    public sealed class SaveSourceInfo
    {
        internal SaveSourceInfo(
            SaveSourceKey sourceKey,
            SaveSourceKind sourceKind,
            SaveFileType fileType,
            string sourcePath,
            string displayName,
            DateTime sourceLastWriteTimeUtc,
            SaveValidationStatus validationStatus,
            bool isSourceAvailable,
            bool hasCurrentCopy,
            int selectedCharacterSlotIndex,
            SaveCharacterSlotInfo[] characters,
            string errorMessage)
        {
            SourceKey = sourceKey;
            SourceKind = sourceKind;
            FileType = fileType;
            InternalSourcePath = sourcePath;
            SourceFileName = string.IsNullOrWhiteSpace(sourcePath) ? string.Empty : Path.GetFileName(sourcePath);
            DisplayName = displayName ?? string.Empty;
            SourceLastWriteTimeUtc = sourceLastWriteTimeUtc;
            ValidationStatus = validationStatus;
            IsSourceAvailable = isSourceAvailable;
            HasCurrentCopy = hasCurrentCopy;
            SelectedCharacterSlotIndex = selectedCharacterSlotIndex;
            Characters = characters ?? new SaveCharacterSlotInfo[0];
            ErrorMessage = errorMessage ?? string.Empty;
        }

        public SaveSourceKey SourceKey { get; private set; }
        public SaveSourceKind SourceKind { get; private set; }
        public SaveFileType FileType { get; private set; }
        public string SourceFileName { get; private set; }
        public string DisplayName { get; private set; }
        public DateTime SourceLastWriteTimeUtc { get; private set; }
        public SaveValidationStatus ValidationStatus { get; private set; }
        public bool IsSourceAvailable { get; private set; }
        public bool HasCurrentCopy { get; private set; }
        public int SelectedCharacterSlotIndex { get; private set; }
        public SaveCharacterSlotInfo[] Characters { get; private set; }
        public string ErrorMessage { get; private set; }
        internal string InternalSourcePath { get; private set; }
    }

    public sealed class SaveServiceSnapshot
    {
        internal SaveServiceSnapshot(SaveSourceInfo[] sources, SaveSourceKey selectedSourceKey, string warning)
        {
            Sources = sources ?? new SaveSourceInfo[0];
            SelectedSourceKey = selectedSourceKey;
            Warning = warning ?? string.Empty;
        }

        public SaveSourceInfo[] Sources { get; private set; }
        public SaveSourceKey SelectedSourceKey { get; private set; }
        public SaveSourceInfo SelectedSource
        {
            get
            {
                return SelectedSourceKey == null ? null : Sources.FirstOrDefault(
                    item => item.SourceKey.Equals(SelectedSourceKey));
            }
        }
        public string Warning { get; private set; }
    }

    public sealed class LegacySaveCacheMigrationResult
    {
        internal LegacySaveCacheMigrationResult(int removedEntries, int retainedUnassignedEntries)
        {
            RemovedEntries = removedEntries;
            RetainedUnassignedEntries = retainedUnassignedEntries;
        }

        public int RemovedEntries { get; private set; }
        public int RetainedUnassignedEntries { get; private set; }
    }

    public sealed class SaveRefreshResult
    {
        internal SaveRefreshResult(
            SaveRefreshStatus status,
            SaveSourceInfo source,
            SaveImportResult imported,
            string previousRepositoryFingerprint,
            string currentRepositoryFingerprint,
            string errorMessage,
            LegacySaveCacheMigrationResult migration)
        {
            Status = status;
            Source = source;
            Imported = imported;
            PreviousRepositoryFingerprint = previousRepositoryFingerprint ?? string.Empty;
            CurrentRepositoryFingerprint = currentRepositoryFingerprint ?? string.Empty;
            ErrorMessage = errorMessage ?? string.Empty;
            Migration = migration ?? new LegacySaveCacheMigrationResult(0, 0);
        }

        public SaveRefreshStatus Status { get; private set; }
        public bool Succeeded { get { return Status != SaveRefreshStatus.Failed; } }
        public SaveSourceInfo Source { get; private set; }
        public SaveImportResult Imported { get; private set; }
        public string PreviousRepositoryFingerprint { get; private set; }
        public string CurrentRepositoryFingerprint { get; private set; }
        public bool RepositoryChanged
        {
            get
            {
                return !string.Equals(PreviousRepositoryFingerprint, CurrentRepositoryFingerprint,
                    StringComparison.Ordinal);
            }
        }
        public string ErrorMessage { get; private set; }
        public LegacySaveCacheMigrationResult Migration { get; private set; }
    }

    public sealed class SaveDiscoveryResult
    {
        internal SaveDiscoveryResult(SaveServiceSnapshot snapshot, SaveRefreshResult[] refreshResults)
        {
            Snapshot = snapshot;
            RefreshResults = refreshResults ?? new SaveRefreshResult[0];
        }

        public SaveServiceSnapshot Snapshot { get; private set; }
        public SaveRefreshResult[] RefreshResults { get; private set; }
    }

    public sealed class SaveRefreshCycleResult
    {
        internal SaveRefreshCycleResult(
            SaveServiceSnapshot snapshot,
            SaveRefreshResult selectedSourceRefresh,
            SaveRefreshResult[] discoveryRefreshResults)
        {
            Snapshot = snapshot;
            SelectedSourceRefresh = selectedSourceRefresh;
            DiscoveryRefreshResults = discoveryRefreshResults ?? new SaveRefreshResult[0];
        }

        public SaveServiceSnapshot Snapshot { get; private set; }
        public SaveRefreshResult SelectedSourceRefresh { get; private set; }
        public SaveRefreshResult[] DiscoveryRefreshResults { get; private set; }
    }

    public interface ISaveManagementService
    {
        SaveServiceSnapshot Restore();
        SaveDiscoveryResult DiscoverLocalSaves();
        SaveRefreshCycleResult RefreshSelectedAndDiscover();
        SaveRefreshResult ImportManual(string sourcePath);
        SaveRefreshResult Refresh(SaveSourceKey sourceKey);
        SaveServiceSnapshot SelectSource(SaveSourceKey sourceKey);
        SaveServiceSnapshot SelectCharacterSlot(SaveSourceKey sourceKey, int slotIndex);
    }

    /// <summary>
    /// Owns save discovery, stable read-only copies, validation, parsing and source-level state.
    /// Search and presentation behavior deliberately remain outside this service.
    /// </summary>
    public sealed class SaveManagementService : ISaveManagementService
    {
        private const string SchemaVersion = "3.0.0";
        private const int CopyBufferSize = 128 * 1024;
        private const int MaximumSnapshotAttempts = 3;
        private const int ContainerKeyLength = 24;
        private const string MutexDomain = "RelicTool.SaveCopies.v3.source\0";
        private const string SelectionMutexName = @"Local\RelicTool.SaveCopies.v3.selection";

        private static readonly object SourceLocksSync = new object();
        private static readonly Dictionary<string, SourceLockEntry> SourceLocks =
            new Dictionary<string, SourceLockEntry>(StringComparer.Ordinal);
        private static readonly object SelectionSync = new object();

        private readonly RuntimeModel _model;
        private readonly string _applicationRoot;
        private readonly string _saveCopiesRoot;
        private readonly string _root;
        private readonly string _accountsRoot;
        private readonly string _selectionPath;
        private readonly string _localSaveRoot;
        private readonly string _loginUsersPath;
        private string _lastWarning;

        internal static Action<string> AfterInitialSourceSnapshotForTests { get; set; }
        internal static Action<string> BeforeMetadataCommitForTests { get; set; }
        internal static Action<string> AfterMetadataReadOpenedForTests { get; set; }
        internal static Action<string> BeforeLayoutMigrationCommitForTests { get; set; }
        internal static Action<string> AfterLayoutMigrationCommitForTests { get; set; }

        public SaveManagementService(RuntimeModel model, string applicationRoot)
            : this(model, applicationRoot, null)
        {
        }

        public SaveManagementService(RuntimeModel model, string applicationRoot, string localSaveRoot)
            : this(model, applicationRoot, localSaveRoot, null)
        {
        }

        internal SaveManagementService(RuntimeModel model, string applicationRoot, string localSaveRoot, string loginUsersPath)
        {
            if (model == null) throw new ArgumentNullException("model");
            _model = model;
            _applicationRoot = Path.GetFullPath(applicationRoot ?? AppDomain.CurrentDomain.BaseDirectory);
            _saveCopiesRoot = Path.GetFullPath(Path.Combine(_applicationRoot, "UserData", "SaveCopies"));
            _root = _saveCopiesRoot;
            _accountsRoot = _root;
            _selectionPath = Path.GetFullPath(Path.Combine(_root, "state.json"));
            _loginUsersPath = loginUsersPath;
            if (string.IsNullOrWhiteSpace(localSaveRoot))
            {
                string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                _localSaveRoot = string.IsNullOrWhiteSpace(roaming)
                    ? string.Empty
                    : Path.GetFullPath(Path.Combine(roaming, "Nightreign"));
            }
            else _localSaveRoot = Path.GetFullPath(localSaveRoot);
        }

        public SaveServiceSnapshot Restore()
        {
            EnsureSafeRoots();
            CleanupOrphanTemporaryFiles();
            List<SaveSourceMetadataDto> metadata = ReadAllMetadata();
            for (int index = 0; index < metadata.Count; index++)
            {
                SaveSourceMetadataDto item = metadata[index];
                using (SourceLockLease lease = AcquireSourceLock(item.SourceKey))
                {
                    // The enumeration snapshot can be older than an in-flight commit.
                    // Re-read under the same lock before validating or deleting copies.
                    SaveSourceMetadataDto current = ReadMetadataForSource(item.SourceKey);
                    if (current == null) continue;
                    ValidateRestoredCopy(current);
                    metadata[index] = current;
                }
            }
            return BuildSnapshot(metadata, ReadSelection(), _lastWarning);
        }

        internal SaveServiceSnapshot GetSnapshot()
        {
            EnsureSafeRoots();
            return BuildSnapshot(ReadAllMetadata(), ReadSelection(), _lastWarning);
        }

        internal SaveImportResult LoadValidatedCopy(SaveSourceKey sourceKey)
        {
            if (sourceKey == null) throw new ArgumentNullException("sourceKey");
            EnsureSafeRoots();
            using (SourceLockLease lease = AcquireSourceLock(sourceKey.Value))
            {
                SaveSourceMetadataDto metadata = ReadMetadataForSource(sourceKey.Value);
                ParsedSave parsed;
                SourceFileSnapshot copy;
                if (metadata == null || !TryValidateCurrent(metadata, out parsed, out copy))
                    throw new SaveReadException("上次存档副本未通过恢复校验；请刷新来源。");
                SourceFileSnapshot source = new SourceFileSnapshot(metadata.CopyLength,
                    metadata.SourceLastWriteTimeUtc, metadata.ContentSha256);
                string fingerprint = FingerprintFor(metadata, metadata.SelectedSlotIndex);
                return CreateImportResult(metadata, metadata.SourcePath, source, source, copy,
                    parsed, SaveRefreshStatus.Unchanged, fingerprint, fingerprint);
            }
        }

        public SaveDiscoveryResult DiscoverLocalSaves()
        {
            EnsureSafeRoots();
            CleanupOrphanTemporaryFiles();
            List<SaveRefreshResult> refreshed = new List<SaveRefreshResult>();
            foreach (string path in FindLocalSavePaths())
            {
                try { refreshed.Add(RefreshPath(path, SaveSourceKind.LocalSteam, false)); }
                catch (Exception error) { refreshed.Add(CreateUnboundFailure(FriendlyError(error))); }
            }

            List<SaveSourceMetadataDto> metadata = ReadAllMetadata();
            foreach (SaveSourceMetadataDto item in metadata.Where(item => item.SourceKind == "local"))
            {
                if (!File.Exists(item.SourcePath)) item.SourceAvailable = false;
            }

            SaveSelectionDto selection = ReadSelection();
            SaveServiceSnapshot provisional = BuildSnapshot(metadata, selection, _lastWarning);
            if (provisional.SelectedSource == null || provisional.SelectedSource.ValidationStatus != SaveValidationStatus.Valid)
            {
                SaveSourceInfo defaultSource = provisional.Sources
                    .Where(item => item.ValidationStatus == SaveValidationStatus.Valid && item.IsSourceAvailable)
                    .OrderByDescending(item => item.SourceLastWriteTimeUtc)
                    .ThenBy(item => item.SourceKey.Value, StringComparer.Ordinal)
                    .FirstOrDefault();
                selection.SelectedSourceKey = defaultSource == null ? null : defaultSource.SourceKey.Value;
                WriteSelection(selection);
            }
            return new SaveDiscoveryResult(BuildSnapshot(metadata, selection, _lastWarning), refreshed.ToArray());
        }

        public SaveRefreshCycleResult RefreshSelectedAndDiscover()
        {
            SaveDiscoveryResult discovery = DiscoverLocalSaves();
            SaveSourceKey selectedKey = discovery.Snapshot.SelectedSourceKey;
            SaveRefreshResult selectedRefresh = selectedKey == null
                ? null
                : discovery.RefreshResults.FirstOrDefault(item => item.Source != null
                    && item.Source.SourceKey.Equals(selectedKey));
            if (selectedKey != null && selectedRefresh == null) selectedRefresh = Refresh(selectedKey);
            return new SaveRefreshCycleResult(Restore(), selectedRefresh, discovery.RefreshResults);
        }

        public SaveRefreshResult ImportManual(string sourcePath)
        {
            EnsureSafeRoots();
            return RefreshPath(sourcePath, SaveSourceKind.Manual, true);
        }

        public SaveRefreshResult ImportPath(string sourcePath)
        {
            EnsureSafeRoots();
            string normalized = ValidateSourcePath(sourcePath);
            SaveSourceKind kind = IsStandardLocalPath(normalized) ? SaveSourceKind.LocalSteam : SaveSourceKind.Manual;
            return RefreshPath(normalized, kind, true);
        }

        public SaveRefreshResult Refresh(SaveSourceKey sourceKey)
        {
            if (sourceKey == null) throw new ArgumentNullException("sourceKey");
            EnsureSafeRoots();
            using (SourceLockLease lease = AcquireSourceLock(sourceKey.Value))
            {
                SaveSourceMetadataDto metadata = ReadMetadataForSource(sourceKey.Value);
                if (metadata == null) return CreateUnboundFailure("找不到要刷新的存档来源。");
                if (!File.Exists(metadata.SourcePath))
                {
                    metadata.SourceAvailable = false;
                    metadata.LastError = "原始存档当前不可用；继续保留上次验证副本。";
                    TryWriteMetadata(metadata);
                    return CreateFailure(metadata, metadata.LastError);
                }
                return RefreshPath(metadata.SourcePath, ParseSourceKind(metadata.SourceKind), false);
            }
        }

        public SaveServiceSnapshot SelectSource(SaveSourceKey sourceKey)
        {
            if (sourceKey == null) throw new ArgumentNullException("sourceKey");
            EnsureSafeRoots();
            List<SaveSourceMetadataDto> metadata = ReadAllMetadata();
            if (!metadata.Any(item => string.Equals(item.SourceKey, sourceKey.Value, StringComparison.Ordinal)))
                throw new ArgumentException("存档来源不存在。", "sourceKey");
            SaveSelectionDto selection = ReadSelection();
            selection.SelectedSourceKey = sourceKey.Value;
            WriteSelection(selection);
            return BuildSnapshot(metadata, selection, _lastWarning);
        }

        public SaveServiceSnapshot SelectCharacterSlot(SaveSourceKey sourceKey, int slotIndex)
        {
            if (sourceKey == null) throw new ArgumentNullException("sourceKey");
            EnsureSafeRoots();
            using (SourceLockLease lease = AcquireSourceLock(sourceKey.Value))
            {
                SaveSourceMetadataDto metadata = ReadAllMetadata().FirstOrDefault(
                    item => string.Equals(item.SourceKey, sourceKey.Value, StringComparison.Ordinal));
                if (metadata == null) throw new ArgumentException("存档来源不存在。", "sourceKey");
                if (metadata.Characters == null || !metadata.Characters.Any(item => item.SlotIndex == slotIndex))
                    throw new ArgumentException("存档中不存在所选角色栏位。", "slotIndex");
                metadata.SelectedSlotIndex = slotIndex;
                WriteMetadata(metadata);
            }
            return SelectSource(sourceKey);
        }

        public string[] FindLocalSavePaths()
        {
            if (string.IsNullOrWhiteSpace(_localSaveRoot) || !Directory.Exists(_localSaveRoot))
                return new string[0];
            List<string> paths = new List<string>();
            try
            {
                foreach (string accountDirectory in Directory.EnumerateDirectories(_localSaveRoot))
                {
                    try
                    {
                        AddIfPresent(paths, Path.Combine(accountDirectory, "NR0000.sl2"));
                        AddIfPresent(paths, Path.Combine(accountDirectory, "NR0000.co2"));
                    }
                    catch (Exception error) when (error is IOException || error is UnauthorizedAccessException
                        || error is ArgumentException || error is NotSupportedException) { }
                }
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { }
            return paths.OrderByDescending(GetLastWriteTimeSafe)
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        public static SaveSourceKey ComputeSourceKey(string sourcePath, SaveSourceKind kind, string localSaveRoot)
        {
            string source = Path.GetFullPath(sourcePath ?? string.Empty).TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
            string extension = Path.GetExtension(source).ToLowerInvariant();
            string account = kind == SaveSourceKind.LocalSteam
                ? Path.GetDirectoryName(source).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant()
                : string.Empty;
            string material = (kind == SaveSourceKind.LocalSteam ? "local" : "manual")
                + "\0" + account + "\0" + source + "\0" + extension;
            return new SaveSourceKey(Hash(material));
        }

        public static string SanitizeDisplaySegment(string value, int maximumLength)
        {
            int limit = Math.Max(1, maximumLength);
            string source = string.IsNullOrWhiteSpace(value) ? "Player" : value.Trim();
            StringBuilder result = new StringBuilder(source.Length);
            const string invalid = "<>:\"/\\|?*";
            foreach (char character in source)
            {
                if (character < 32 || invalid.IndexOf(character) >= 0) result.Append('_');
                else result.Append(character);
            }
            string cleaned = result.ToString().Trim().TrimEnd('.', ' ');
            if (string.IsNullOrWhiteSpace(cleaned)) cleaned = "Player";
            if (IsReservedWindowsName(cleaned)) cleaned = "_" + cleaned;
            if (cleaned.Length > limit) cleaned = cleaned.Substring(0, limit).TrimEnd('.', ' ');
            if (cleaned.Length > 0 && char.IsHighSurrogate(cleaned[cleaned.Length - 1])) cleaned = cleaned.Substring(0, cleaned.Length - 1);
            return string.IsNullOrWhiteSpace(cleaned) ? "Player" : cleaned;
        }

        public static string MakeUniqueDisplayName(string desiredName, ISet<string> usedNames)
        {
            if (usedNames == null) throw new ArgumentNullException("usedNames");
            string desired = SanitizeDisplaySegment(desiredName, 72);
            string candidate = desired;
            int suffix = 2;
            while (!usedNames.Add(candidate))
            {
                string tail = "_" + suffix.ToString(CultureInfo.InvariantCulture);
                string prefix = desired.Length + tail.Length <= 72
                    ? desired : desired.Substring(0, Math.Max(1, 72 - tail.Length)).TrimEnd('.', ' ');
                candidate = prefix + tail;
                suffix++;
            }
            return candidate;
        }

        internal static bool IsDigest(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (char character in value)
                if ((character < '0' || character > '9') && (character < 'A' || character > 'F')
                    && (character < 'a' || character > 'f')) return false;
            return true;
        }

        private static string ContainerKey(string digest)
        {
            if (!IsDigest(digest)) throw UnsafeStructure();
            return digest.ToUpperInvariant().Substring(0, ContainerKeyLength);
        }

        private static bool IsContainerKey(string value)
        {
            if (value == null || value.Length != ContainerKeyLength) return false;
            foreach (char character in value)
                if ((character < '0' || character > '9') && (character < 'A' || character > 'F')) return false;
            return true;
        }

        private SaveRefreshResult RefreshPath(string sourcePath, SaveSourceKind kind, bool selectOnSuccess)
        {
            string source = ValidateSourcePath(sourcePath);
            EnsureSafeRoots(false);
            SourceIdentity identity = CreateIdentity(source, kind);
            using (SourceLockLease lease = AcquireSourceLock(identity.SourceKey))
            {
                EnsureSourceIdentity(identity.SourceDirectory, identity.AccountKey, identity.SourceKey);
                SaveSourceMetadataDto previous = ReadMetadataForSource(identity.SourceKey);
                if (previous != null && previous.LegacyDirectory != null)
                    return CreateFailure(previous, "存档缓存迁移未完成；已保留上次验证副本，请重试。");
                SourceFileSnapshot lastBefore = null;
                SourceFileSnapshot lastAfter = null;
                string temporaryPath = null;
                string unpublishedCopyPath = null;
                try
                {
                    for (int attempt = 0; attempt < MaximumSnapshotAttempts; attempt++)
                    {
                        DeleteManagedTemporaryFile(temporaryPath);
                        temporaryPath = CreateTemporaryPath(identity);
                        lastBefore = SaveImportService.CaptureReadOnly(source);
                        if (attempt == 0)
                        {
                            Action<string> hook = AfterInitialSourceSnapshotForTests;
                            if (hook != null) hook(source);
                        }
                        CopySourceToTemporary(source, temporaryPath);
                        SourceFileSnapshot temporarySnapshot = CaptureManagedSnapshot(temporaryPath);
                        lastAfter = SaveImportService.CaptureReadOnly(source);
                        if (!lastBefore.SameFile(lastAfter) || !ContentMatches(lastBefore, temporarySnapshot))
                        {
                            if (attempt + 1 < MaximumSnapshotAttempts) continue;
                            return FailWhileRetaining(previous, identity,
                                "原始存档在复制期间持续变化；已保留上次验证副本。", lastBefore);
                        }

                        ParsedSave parsedTemporary = ParseManagedFile(temporaryPath);
                        int selectedSlot = ChooseSelectedSlot(previous, parsedTemporary);
                        string previousFingerprint = FingerprintFor(previous, selectedSlot);

                        ParsedSave parsedCurrent;
                        SourceFileSnapshot currentSnapshot;
                        if (previous != null
                            && string.Equals(previous.ContentSha256, temporarySnapshot.Sha256, StringComparison.OrdinalIgnoreCase)
                            && IsCurrentShortName(identity, previous.CopyFileName, lastBefore.LastWriteTimeUtc)
                            && TryValidateCurrent(previous, out parsedCurrent, out currentSnapshot))
                        {
                            DeleteManagedTemporaryFile(temporaryPath);
                            temporaryPath = null;
                            SaveSourceMetadataDto unchanged = CreateMetadata(identity, previous.CopyFileName,
                                currentSnapshot, lastBefore, parsedCurrent, previous.Generation + 1, selectedSlot);
                            WriteMetadata(unchanged);
                            if (selectOnSuccess) SelectAfterSuccessfulImport(unchanged.SourceKey);
                            previous = unchanged;
                            string currentFingerprint = FingerprintFor(unchanged, unchanged.SelectedSlotIndex);
                            SaveImportResult imported = CreateImportResult(unchanged, source, lastBefore, lastAfter,
                                currentSnapshot, parsedCurrent, SaveRefreshStatus.Unchanged,
                                previousFingerprint, currentFingerprint);
                            LegacySaveCacheMigrationResult unchangedMigration =
                                MigrateLegacyCaches(unchanged.ContentSha256);
                            return new SaveRefreshResult(SaveRefreshStatus.Unchanged, ToInfo(unchanged, unchanged.DisplayName),
                                imported, previousFingerprint, currentFingerprint, string.Empty,
                                unchangedMigration);
                        }

                        string copyFileName = CreateUniqueCopyFileName(identity, parsedTemporary, lastBefore.LastWriteTimeUtc,
                            previous == null ? null : previous.CopyFileName);
                        string copyPath = Path.Combine(identity.SourceDirectory, copyFileName);
                        PublishTemporaryFile(temporaryPath, copyPath);
                        unpublishedCopyPath = copyPath;
                        temporaryPath = null;
                        SourceFileSnapshot copySnapshot = CaptureManagedSnapshot(copyPath);
                        if (!ContentMatches(lastBefore, copySnapshot))
                        {
                            DeleteManagedCopyFile(identity, copyPath);
                            return FailWhileRetaining(previous, identity,
                                "新存档副本发布后校验失败；已保留上次验证副本。", lastBefore);
                        }
                        ParsedSave parsedCopy = ParseManagedFile(copyPath);
                        SourceFileSnapshot afterParse = CaptureManagedSnapshot(copyPath);
                        if (!copySnapshot.SameFile(afterParse))
                        {
                            DeleteManagedCopyFile(identity, copyPath);
                            return FailWhileRetaining(previous, identity,
                                "新存档副本在解析期间发生变化；已保留上次验证副本。", lastBefore);
                        }
                        SetReadOnly(copyPath);
                        SaveSourceMetadataDto updated = CreateMetadata(identity, copyFileName, afterParse, lastBefore,
                            parsedCopy, previous == null ? 1 : previous.Generation + 1, selectedSlot);
                        Action<string> beforeCommit = BeforeMetadataCommitForTests;
                        if (beforeCommit != null) beforeCommit(updated.SourceKey);
                        WriteMetadata(updated);
                        // Selection is part of a user import. If its persistence fails,
                        // the catch below restores previous metadata while its copy
                        // still exists; finally removes only this uncommitted candidate.
                        if (selectOnSuccess) SelectAfterSuccessfulImport(updated.SourceKey);
                        unpublishedCopyPath = null;

                        if (previous != null && !string.Equals(previous.CopyFileName, copyFileName, StringComparison.Ordinal))
                            DeleteManagedCopyFile(identity, Path.Combine(identity.SourceDirectory, previous.CopyFileName));
                        // Once committed, no best-effort cleanup failure may restore a
                        // metadata pointer to a superseded and potentially deleted copy.
                        previous = updated;
                        CleanupSupersededManagedCopies(identity, copyFileName);
                        LegacySaveCacheMigrationResult migration = MigrateLegacyCaches(updated.ContentSha256);
                        string newFingerprint = FingerprintFor(updated, updated.SelectedSlotIndex);
                        SaveImportResult result = CreateImportResult(updated, source, lastBefore, lastAfter, afterParse,
                            parsedCopy, SaveRefreshStatus.Updated, previousFingerprint, newFingerprint);
                        return new SaveRefreshResult(SaveRefreshStatus.Updated, ToInfo(updated, updated.DisplayName), result,
                            previousFingerprint, newFingerprint, string.Empty, migration);
                    }
                }
                catch (Exception error) when (!(error is OutOfMemoryException) && !(error is StackOverflowException))
                {
                    return FailWhileRetaining(previous, identity, FriendlyError(error), lastBefore);
                }
                finally
                {
                    DeleteManagedTemporaryFile(temporaryPath);
                    if (unpublishedCopyPath != null)
                    {
                        SaveSourceMetadataDto recorded = TryReadMetadata(identity);
                        if ((recorded == null && !File.Exists(Path.Combine(identity.SourceDirectory, "metadata.json")))
                            || (recorded != null && !string.Equals(recorded.CopyFileName,
                                Path.GetFileName(unpublishedCopyPath), StringComparison.Ordinal)))
                            DeleteManagedCopyFile(identity, unpublishedCopyPath);
                    }
                }
            }
            return CreateUnboundFailure("存档刷新未完成。");
        }

        private SaveRefreshResult FailWhileRetaining(SaveSourceMetadataDto previous, SourceIdentity identity,
            string message, SourceFileSnapshot sourceSnapshot)
        {
            SaveSourceMetadataDto retained = previous == null ? CreateFailedMetadata(identity, sourceSnapshot, message) : previous;
            retained.SourceAvailable = File.Exists(identity.SourcePath);
            retained.LastError = message;
            retained.ValidationStatus = previous != null && !string.IsNullOrWhiteSpace(previous.CopyFileName)
                ? "valid" : "invalid";
            TryWriteMetadata(retained);
            return CreateFailure(retained, message);
        }

        private SaveRefreshResult CreateFailure(SaveSourceMetadataDto metadata, string message)
        {
            string previousFingerprint = FingerprintFor(metadata, metadata == null ? -1 : metadata.SelectedSlotIndex);
            return new SaveRefreshResult(SaveRefreshStatus.Failed,
                metadata == null ? null : ToInfo(metadata, metadata.DisplayName), null,
                previousFingerprint, previousFingerprint, message,
                new LegacySaveCacheMigrationResult(0, CountRetainedLegacyEntries()));
        }

        private static SaveRefreshResult CreateUnboundFailure(string message)
        {
            return new SaveRefreshResult(SaveRefreshStatus.Failed, null, null, string.Empty, string.Empty,
                message, new LegacySaveCacheMigrationResult(0, 0));
        }

        private SaveImportResult CreateImportResult(SaveSourceMetadataDto metadata, string source,
            SourceFileSnapshot sourceBefore, SourceFileSnapshot sourceAfter, SourceFileSnapshot copySnapshot,
            ParsedSave parsed, SaveRefreshStatus status, string previousFingerprint, string currentFingerprint)
        {
            string copyPath = Path.Combine(MetadataDirectory(metadata), metadata.CopyFileName);
            return new SaveImportResult(source, copyPath, sourceBefore, sourceAfter, copySnapshot, parsed,
                new SaveSourceKey(metadata.SourceKey), status, metadata.DisplayName,
                previousFingerprint, currentFingerprint);
        }

        private SaveSourceMetadataDto CreateMetadata(SourceIdentity identity, string copyFileName,
            SourceFileSnapshot copySnapshot, SourceFileSnapshot sourceSnapshot, ParsedSave parsed,
            long generation, int selectedSlot)
        {
            List<SaveCharacterMetadataDto> characters = parsed.Characters.OrderBy(item => item.SlotIndex)
                .Select(item => new SaveCharacterMetadataDto
                {
                    SlotIndex = item.SlotIndex,
                    PlayerName = item.PlayerName,
                    RepositoryFingerprint = ComputeRepositoryFingerprint(item)
                }).ToList();
            if (!characters.Any(item => item.SlotIndex == selectedSlot))
                selectedSlot = characters.Count == 0 ? -1 : characters[0].SlotIndex;
            return new SaveSourceMetadataDto
            {
                SchemaVersion = SchemaVersion,
                SourceKey = identity.SourceKey,
                AccountKey = identity.AccountKey,
                SourceKind = identity.Kind == SaveSourceKind.LocalSteam ? "local" : "manual",
                FileType = identity.FileType == SaveFileType.Sl2 ? "sl2" : "co2",
                SourcePath = identity.SourcePath,
                CopyFileName = copyFileName,
                ContentSha256 = copySnapshot.Sha256.ToUpperInvariant(),
                CopyLength = copySnapshot.Length,
                SourceLastWriteTimeUtc = sourceSnapshot.LastWriteTimeUtc,
                DisplayName = Path.GetFileNameWithoutExtension(copyFileName),
                ValidationStatus = "valid",
                SourceAvailable = true,
                SelectedSlotIndex = selectedSlot,
                Characters = characters,
                Generation = generation,
                LastError = string.Empty
            };
        }

        private SaveSourceMetadataDto CreateFailedMetadata(SourceIdentity identity, SourceFileSnapshot snapshot, string message)
        {
            return new SaveSourceMetadataDto
            {
                SchemaVersion = SchemaVersion,
                SourceKey = identity.SourceKey,
                AccountKey = identity.AccountKey,
                SourceKind = identity.Kind == SaveSourceKind.LocalSteam ? "local" : "manual",
                FileType = identity.FileType == SaveFileType.Sl2 ? "sl2" : "co2",
                SourcePath = identity.SourcePath,
                CopyFileName = string.Empty,
                ContentSha256 = string.Empty,
                CopyLength = 0,
                SourceLastWriteTimeUtc = snapshot == null ? DateTime.MinValue : snapshot.LastWriteTimeUtc,
                DisplayName = ShortBaseName(identity, snapshot == null ? DateTime.UtcNow : snapshot.LastWriteTimeUtc),
                ValidationStatus = "invalid",
                SourceAvailable = File.Exists(identity.SourcePath),
                SelectedSlotIndex = -1,
                Characters = new List<SaveCharacterMetadataDto>(),
                Generation = 0,
                LastError = message
            };
        }

        private void ValidateRestoredCopy(SaveSourceMetadataDto metadata)
        {
            metadata.SourceAvailable = File.Exists(metadata.SourcePath);
            if (string.IsNullOrWhiteSpace(metadata.CopyFileName))
            {
                metadata.ValidationStatus = metadata.SourceAvailable ? "invalid" : "missing";
                return;
            }
            ParsedSave parsed;
            SourceFileSnapshot snapshot;
            if (!TryValidateCurrent(metadata, out parsed, out snapshot))
            {
                metadata.ValidationStatus = "invalid";
                metadata.LastError = "上次存档副本未通过恢复校验；请刷新来源。";
                return;
            }
            metadata.ValidationStatus = "valid";
            metadata.Characters = parsed.Characters.OrderBy(item => item.SlotIndex).Select(item =>
                new SaveCharacterMetadataDto
                {
                    SlotIndex = item.SlotIndex,
                    PlayerName = item.PlayerName,
                    RepositoryFingerprint = ComputeRepositoryFingerprint(item)
                }).ToList();
            metadata.LastError = string.Empty;
            if (metadata.LegacyDirectory != null) return;
            SourceIdentity identity = new SourceIdentity(metadata.SourcePath, ParseSourceKind(metadata.SourceKind),
                metadata.FileType == "co2" ? SaveFileType.Co2 : SaveFileType.Sl2,
                metadata.AccountKey, metadata.SourceKey, SourceDirectory(metadata.AccountKey, metadata.SourceKey));
            CleanupSupersededManagedCopies(identity, metadata.CopyFileName);
        }

        private bool TryValidateCurrent(SaveSourceMetadataDto metadata, out ParsedSave parsed,
            out SourceFileSnapshot snapshot)
        {
            parsed = null;
            snapshot = null;
            try
            {
                if (metadata == null || !IsSafeManagedFileName(metadata.CopyFileName)) return false;
                string directory = MetadataDirectory(metadata);
                string path = Path.GetFullPath(Path.Combine(directory, metadata.CopyFileName));
                EnsureImmediateChild(directory, path, metadata.CopyFileName);
                if (!IsNormalFile(path)) return false;
                snapshot = CaptureManagedSnapshot(path);
                if (snapshot.Length != metadata.CopyLength
                    || !string.Equals(snapshot.Sha256, metadata.ContentSha256, StringComparison.OrdinalIgnoreCase)) return false;
                parsed = ParseManagedFile(path);
                SourceFileSnapshot after = CaptureManagedSnapshot(path);
                return snapshot.SameFile(after);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException
                || error is SaveReadException || error is ArgumentException || error is NotSupportedException) { return false; }
        }

        private SourceIdentity CreateIdentity(string source, SaveSourceKind kind)
        {
            if (kind == SaveSourceKind.LocalSteam && !IsStandardLocalPath(source)) kind = SaveSourceKind.Manual;
            SaveSourceKey sourceKey = ComputeSourceKey(source, kind, _localSaveRoot);
            string accountMaterial = kind == SaveSourceKind.LocalSteam
                ? Path.GetDirectoryName(source).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant()
                : "manual\0" + Path.GetDirectoryName(source).TrimEnd(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
            string accountKey = Hash(accountMaterial);
            SaveFileType fileType = string.Equals(Path.GetExtension(source), ".co2", StringComparison.OrdinalIgnoreCase)
                ? SaveFileType.Co2 : SaveFileType.Sl2;
            string directory = SourceDirectory(accountKey, sourceKey.Value);
            EnsureSourceDirectory(accountKey, sourceKey.Value, true);
            EnsureSourceIdentity(directory, accountKey, sourceKey.Value);
            return new SourceIdentity(source, kind, fileType, accountKey, sourceKey.Value, directory);
        }

        private static void EnsureSourceIdentity(string directory, string accountKey, string sourceKey)
        {
            SaveSourceMetadataDto occupant;
            if (TryDeserialize(Path.Combine(directory, "metadata.json"), out occupant)
                && occupant != null
                && ((!string.IsNullOrWhiteSpace(occupant.AccountKey)
                        && !string.Equals(occupant.AccountKey, accountKey, StringComparison.OrdinalIgnoreCase))
                    || (!string.IsNullOrWhiteSpace(occupant.SourceKey)
                        && !string.Equals(occupant.SourceKey, sourceKey, StringComparison.OrdinalIgnoreCase))))
                throw UnsafeStructure();
        }

        private bool IsStandardLocalPath(string source)
        {
            if (string.IsNullOrWhiteSpace(_localSaveRoot)) return false;
            string parent = Path.GetDirectoryName(source);
            string grandParent = string.IsNullOrWhiteSpace(parent) ? null : Path.GetDirectoryName(parent);
            string name = Path.GetFileName(source);
            return !string.IsNullOrWhiteSpace(parent) && PathsEqual(grandParent, _localSaveRoot)
                && (string.Equals(name, "NR0000.sl2", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "NR0000.co2", StringComparison.OrdinalIgnoreCase));
        }

        private string ValidateSourcePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new SaveReadException("尚未选择存档文件。");
            string fullPath;
            try { fullPath = Path.GetFullPath(path.Trim().Trim('"')); }
            catch (Exception error) when (error is ArgumentException || error is NotSupportedException
                || error is PathTooLongException) { throw new SaveReadException("存档路径无效。", error); }
            string extension = Path.GetExtension(fullPath);
            if (!string.Equals(extension, ".sl2", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(extension, ".co2", StringComparison.OrdinalIgnoreCase))
                throw new SaveReadException("只支持 .sl2 和 .co2 存档。");
            if (!File.Exists(fullPath)) throw new SaveReadException("找不到存档文件。");
            return fullPath;
        }

        private string CreateTemporaryPath(SourceIdentity identity)
        {
            string name = "incoming-" + Guid.NewGuid().ToString("N") + ".tmp";
            string path = Path.GetFullPath(Path.Combine(identity.SourceDirectory, name));
            EnsureImmediateChild(identity.SourceDirectory, path, name);
            return path;
        }

        private string CreateUniqueCopyFileName(SourceIdentity identity, ParsedSave parsed, DateTime sourceTimeUtc,
            string currentFileName)
        {
            string baseName = ShortBaseName(identity, sourceTimeUtc);
            string extension = identity.FileType == SaveFileType.Sl2 ? ".sl2" : ".co2";
            HashSet<string> used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in Directory.EnumerateFiles(identity.SourceDirectory, "*", SearchOption.TopDirectoryOnly))
            {
                string name = Path.GetFileName(path);
                if (!string.Equals(name, currentFileName, StringComparison.OrdinalIgnoreCase))
                    used.Add(Path.GetFileNameWithoutExtension(name));
            }
            string unique = MakeUniqueDisplayName(baseName, used);
            string candidate = unique + extension;
            if (string.Equals(candidate, currentFileName, StringComparison.OrdinalIgnoreCase))
                candidate = MakeUniqueDisplayName(unique, used) + extension;
            return candidate;
        }

        private string ShortBaseName(SourceIdentity identity, DateTime sourceTimeUtc)
        {
            string player = IsStandardLocalPath(identity.SourcePath)
                ? SteamPersonaNames.Find(Path.GetDirectoryName(identity.SourcePath), _loginUsersPath) : null;
            return SanitizeDisplaySegment(string.IsNullOrWhiteSpace(player) ? "存档" : player, 32)
                + "_" + sourceTimeUtc.ToLocalTime().ToString("yyMMdd-HHmm", CultureInfo.InvariantCulture);
        }

        private bool IsCurrentShortName(SourceIdentity identity, string fileName, DateTime sourceTimeUtc)
        {
            string expected = ShortBaseName(identity, sourceTimeUtc);
            string actual = Path.GetFileNameWithoutExtension(fileName);
            if (actual == expected) return true;
            int sequence;
            return actual.StartsWith(expected + "_", StringComparison.Ordinal)
                && int.TryParse(actual.Substring(expected.Length + 1), NumberStyles.None,
                    CultureInfo.InvariantCulture, out sequence) && sequence >= 2;
        }

        private void PublishTemporaryFile(string temporaryPath, string destination)
        {
            EnsureNormalFile(temporaryPath);
            if (File.Exists(destination) || Directory.Exists(destination))
                throw new SaveReadException("存档副本名称发生冲突，已停止发布。");
            try { File.Move(temporaryPath, destination); }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            { throw new SaveReadException("无法原子发布存档副本。", error); }
        }

        private static void CopySourceToTemporary(string source, string destination)
        {
            try
            {
                using (FileStream input = OpenSourceReadOnly(source))
                using (FileStream output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, CopyBufferSize, FileOptions.SequentialScan))
                {
                    byte[] buffer = new byte[CopyBufferSize];
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) != 0) output.Write(buffer, 0, read);
                    output.Flush(true);
                }
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            { throw new SaveReadException("创建存档副本失败。", error); }
        }

        private ParsedSave ParseManagedFile(string path)
        {
            try { return new Bnd4SaveReader(_model).Read(path); }
            catch (SaveReadException error)
            {
                string message = (error.Message ?? "存档副本解析失败。").Replace(path, "存档副本");
                throw new SaveReadException(message, error);
            }
        }

        private static SourceFileSnapshot CaptureManagedSnapshot(string path)
        {
            EnsureNormalFile(path);
            try
            {
                long length;
                string hash;
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.Read, CopyBufferSize, FileOptions.SequentialScan))
                using (SHA256 algorithm = SHA256.Create())
                {
                    length = stream.Length;
                    hash = ToHex(algorithm.ComputeHash(stream));
                }
                EnsureNormalFile(path);
                return new SourceFileSnapshot(length, File.GetLastWriteTimeUtc(path), hash);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            { throw new SaveReadException("无法校验存档副本。", error); }
        }

        private void EnsureSafeRoots(bool migrate = true)
        {
            try
            {
                EnsureNormalDirectory(_applicationRoot, true);
                string userData = Path.Combine(_applicationRoot, "UserData");
                EnsureImmediateChild(_applicationRoot, userData, "UserData");
                EnsureNormalDirectory(userData, true);
                EnsureImmediateChild(userData, _saveCopiesRoot, "SaveCopies");
                EnsureNormalDirectory(_saveCopiesRoot, true);
                EnsureImmediateChild(_root, _selectionPath, "state.json");
                if (Directory.Exists(_selectionPath)) throw UnsafeStructure();
                if (migrate) MigrateV3Layout();
            }
            catch (SaveReadException) { throw; }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException
                || error is ArgumentException || error is NotSupportedException)
            { throw new SaveReadException("无法创建安全的存档服务目录。", error); }
        }

        private string SourceDirectory(string accountKey, string sourceKey)
        {
            if (!IsDigest(accountKey) || !IsDigest(sourceKey)) throw UnsafeStructure();
            string accountToken = ContainerKey(accountKey);
            string sourceToken = ContainerKey(sourceKey);
            string accountDirectory = Path.GetFullPath(Path.Combine(_accountsRoot, accountToken));
            string sourceDirectory = Path.GetFullPath(Path.Combine(accountDirectory, sourceToken));
            EnsureImmediateChild(_accountsRoot, accountDirectory, accountToken);
            EnsureImmediateChild(accountDirectory, sourceDirectory, sourceToken);
            return sourceDirectory;
        }

        private void EnsureSourceDirectory(string accountKey, string sourceKey, bool create)
        {
            string accountDirectory = Path.Combine(_accountsRoot, ContainerKey(accountKey));
            string sourceDirectory = SourceDirectory(accountKey, sourceKey);
            EnsureNormalDirectory(accountDirectory, create);
            EnsureNormalDirectory(sourceDirectory, create);
        }

        private SaveSourceMetadataDto TryReadMetadata(SourceIdentity identity)
        {
            string path = Path.Combine(identity.SourceDirectory, "metadata.json");
            SaveSourceMetadataDto metadata;
            if (!TryDeserialize(path, out metadata)) return null;
            return ValidateMetadata(metadata, identity.AccountKey, identity.SourceKey) ? metadata : null;
        }

        private List<SaveSourceMetadataDto> ReadAllMetadata()
        {
            List<SaveSourceMetadataDto> result = new List<SaveSourceMetadataDto>();
            if (!Directory.Exists(_accountsRoot)) return result;
            foreach (string accountDirectory in SafeDirectories(_accountsRoot))
            {
                string accountKey = Path.GetFileName(accountDirectory);
                if (!IsContainerKey(accountKey) || !IsNormalDirectory(accountDirectory)) continue;
                string sources = accountDirectory;
                if (!IsNormalDirectory(sources)) continue;
                foreach (string sourceDirectory in SafeDirectories(sources))
                {
                    string sourceKey = Path.GetFileName(sourceDirectory);
                    if (!IsContainerKey(sourceKey) || !IsNormalDirectory(sourceDirectory)) continue;
                    SaveSourceMetadataDto metadata;
                    if (TryDeserialize(Path.Combine(sourceDirectory, "metadata.json"), out metadata)
                        && ValidateMetadata(metadata, accountKey, sourceKey)) result.Add(metadata);
                    else _lastWarning = "部分存档服务元数据已损坏；本机扫描时会安全重建。";
                }
            }
            foreach (SaveSourceMetadataDto old in ReadLegacyV3Metadata())
            {
                SaveSourceMetadataDto current = result.FirstOrDefault(item => item.SourceKey == old.SourceKey);
                ParsedSave parsed; SourceFileSnapshot snapshot;
                if (current == null || !TryValidateCurrent(current, out parsed, out snapshot))
                {
                    if (current != null) result.Remove(current);
                    result.Add(old);
                }
            }
            return result.GroupBy(item => item.SourceKey, StringComparer.Ordinal)
                .Select(group => group.OrderByDescending(item => item.Generation).First()).ToList();
        }

        private void WriteMetadata(SaveSourceMetadataDto metadata)
        {
            if (metadata.LegacyDirectory != null) throw new SaveReadException("存档缓存迁移未完成；已保留上次验证副本，请重试。");
            if (!ValidateMetadata(metadata, metadata.AccountKey, metadata.SourceKey))
                throw new SaveReadException("存档服务元数据无效。");
            EnsureSourceDirectory(metadata.AccountKey, metadata.SourceKey, true);
            string directory = SourceDirectory(metadata.AccountKey, metadata.SourceKey);
            EnsureSourceIdentity(directory, metadata.AccountKey, metadata.SourceKey);
            AtomicWrite(Path.Combine(directory, "metadata.json"), metadata);
        }

        private void TryWriteMetadata(SaveSourceMetadataDto metadata)
        {
            try { WriteMetadata(metadata); }
            catch (Exception) { }
        }

        private SaveSelectionDto ReadSelection()
        {
            lock (SelectionSync)
            {
                using (NamedMutexLease mutex = AcquireNamedMutex(SelectionMutexName))
                {
                    SaveSelectionDto selection;
                    string path = File.Exists(_selectionPath) || !IsNormalDirectory(Path.Combine(_root, "v3"))
                        ? _selectionPath : Path.Combine(_root, "v3", "state.json");
                    if (!TryDeserialize(path, out selection) || selection.SchemaVersion != SchemaVersion
                        || (!string.IsNullOrWhiteSpace(selection.SelectedSourceKey)
                            && !IsDigest(selection.SelectedSourceKey)))
                    {
                        if (File.Exists(_selectionPath)) _lastWarning = "存档服务选择状态已损坏，已安全重建。";
                        return new SaveSelectionDto { SchemaVersion = SchemaVersion };
                    }
                    return selection;
                }
            }
        }

        private void WriteSelection(SaveSelectionDto selection)
        {
            if (selection == null) selection = new SaveSelectionDto();
            selection.SchemaVersion = SchemaVersion;
            if (!string.IsNullOrWhiteSpace(selection.SelectedSourceKey) && !IsDigest(selection.SelectedSourceKey))
                throw new SaveReadException("所选存档来源键无效。");
            lock (SelectionSync)
            {
                using (NamedMutexLease mutex = AcquireNamedMutex(SelectionMutexName)) AtomicWrite(_selectionPath, selection);
            }
        }

        private void SelectAfterSuccessfulImport(string sourceKey)
        {
            SaveSelectionDto selection = ReadSelection();
            selection.SelectedSourceKey = sourceKey;
            WriteSelection(selection);
        }

        private SaveServiceSnapshot BuildSnapshot(List<SaveSourceMetadataDto> metadata,
            SaveSelectionDto selection, string warning)
        {
            HashSet<string> used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<SaveSourceInfo> sources = new List<SaveSourceInfo>();
            foreach (SaveSourceMetadataDto item in metadata.OrderByDescending(value => value.SourceLastWriteTimeUtc)
                .ThenBy(value => value.SourceKey, StringComparer.Ordinal))
            {
                string display = MakeUniqueDisplayName(item.DisplayName, used);
                sources.Add(ToInfo(item, display));
            }
            SaveSourceKey selected = selection != null && IsDigest(selection.SelectedSourceKey)
                && sources.Any(item => item.SourceKey.Value == selection.SelectedSourceKey)
                ? new SaveSourceKey(selection.SelectedSourceKey) : null;
            return new SaveServiceSnapshot(sources.ToArray(), selected, warning);
        }

        private SaveSourceInfo ToInfo(SaveSourceMetadataDto metadata, string displayName)
        {
            SaveValidationStatus status;
            if (metadata.ValidationStatus == "valid") status = SaveValidationStatus.Valid;
            else if (metadata.ValidationStatus == "missing") status = SaveValidationStatus.Missing;
            else if (metadata.ValidationStatus == "invalid") status = SaveValidationStatus.Invalid;
            else status = SaveValidationStatus.Unknown;
            SaveCharacterSlotInfo[] characters = (metadata.Characters ?? new List<SaveCharacterMetadataDto>())
                .OrderBy(item => item.SlotIndex).Select(item => new SaveCharacterSlotInfo(
                    item.SlotIndex, item.PlayerName, item.RepositoryFingerprint)).ToArray();
            return new SaveSourceInfo(new SaveSourceKey(metadata.SourceKey), ParseSourceKind(metadata.SourceKind),
                metadata.FileType == "co2" ? SaveFileType.Co2 : SaveFileType.Sl2,
                metadata.SourcePath, displayName, metadata.SourceLastWriteTimeUtc, status,
                metadata.SourceAvailable, !string.IsNullOrWhiteSpace(metadata.CopyFileName),
                metadata.SelectedSlotIndex, characters, metadata.LastError);
        }

        private void AtomicWrite<T>(string path, T value)
        {
            string directory = Path.GetDirectoryName(path);
            EnsureNormalDirectory(directory, true);
            string temporary = Path.Combine(directory, "incoming-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    CreateSerializer(typeof(T)).WriteObject(stream, value);
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, null, true);
                else File.Move(temporary, path);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException
                || error is SerializationException)
            { throw new SaveReadException("无法原子保存存档服务元数据。", error); }
            finally { DeleteManagedTemporaryFile(temporary); }
        }

        private static bool TryDeserialize<T>(string path, out T value) where T : class
        {
            value = null;
            if (!File.Exists(path) || !IsNormalFile(path)) return false;
            try
            {
                // Readers observe one immutable JSON version while File.Replace may
                // publish the next version. A read handle must not deny that rename.
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                {
                    Action<string> hook = AfterMetadataReadOpenedForTests;
                    if (hook != null && Path.GetFileName(path) == "metadata.json") hook(path);
                    value = (T)CreateSerializer(typeof(T)).ReadObject(stream);
                }
                return value != null;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException
                || error is SerializationException || error is InvalidDataException) { return false; }
        }

        private static DataContractJsonSerializer CreateSerializer(Type type)
        {
            return new DataContractJsonSerializer(type,
                new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
        }

        private static bool ValidateMetadata(SaveSourceMetadataDto metadata, string accountKey, string sourceKey)
        {
            if (metadata == null || metadata.SchemaVersion != SchemaVersion
                || !IsDigest(metadata.AccountKey) || !IsDigest(metadata.SourceKey)
                || !MatchesContainerIdentity(metadata.AccountKey, accountKey)
                || !MatchesContainerIdentity(metadata.SourceKey, sourceKey)
                || string.IsNullOrWhiteSpace(metadata.SourcePath)
                || (metadata.SourceKind != "local" && metadata.SourceKind != "manual")
                || (metadata.FileType != "sl2" && metadata.FileType != "co2")
                || (metadata.Characters != null && metadata.Characters.Any(item => item == null))) return false;
            return string.IsNullOrWhiteSpace(metadata.CopyFileName)
                || (IsSafeManagedFileName(metadata.CopyFileName) && IsDigest(metadata.ContentSha256)
                    && metadata.CopyLength > 0);
        }

        private static bool MatchesContainerIdentity(string fullDigest, string expected)
        {
            if (!IsDigest(fullDigest) || string.IsNullOrWhiteSpace(expected)) return false;
            if (IsDigest(expected)) return string.Equals(fullDigest, expected, StringComparison.OrdinalIgnoreCase);
            return IsContainerKey(expected) && string.Equals(ContainerKey(fullDigest), expected,
                StringComparison.OrdinalIgnoreCase);
        }

        private string MetadataDirectory(SaveSourceMetadataDto metadata)
        {
            return metadata.LegacyDirectory ?? SourceDirectory(metadata.AccountKey, metadata.SourceKey);
        }

        private IEnumerable<SaveSourceMetadataDto> ReadLegacyV3Metadata()
        {
            string legacy = Path.Combine(_root, "v3");
            string accounts = Path.Combine(legacy, "accounts");
            if (!IsNormalDirectory(legacy) || !IsNormalDirectory(accounts)) yield break;
            foreach (string account in SafeDirectories(accounts))
            {
                string accountKey = Path.GetFileName(account);
                if (!IsContainerKey(accountKey) || !IsNormalDirectory(account)) continue;
                string sources = Path.Combine(account, "sources");
                if (!IsNormalDirectory(sources)) continue;
                foreach (string source in SafeDirectories(sources))
                {
                    string sourceKey = Path.GetFileName(source);
                    if (!IsContainerKey(sourceKey) || !IsNormalDirectory(source)) continue;
                    SaveSourceMetadataDto value;
                    if (TryDeserialize(Path.Combine(source, "metadata.json"), out value)
                        && ValidateMetadata(value, accountKey, sourceKey))
                    {
                        value.LegacyDirectory = source;
                        yield return value;
                    }
                }
            }
        }

        private void MigrateV3Layout()
        {
            string legacy = Path.Combine(_root, "v3");
            if (!IsNormalDirectory(legacy)) return;
            // Persist selection before any old metadata is removed. The existing
            // source and selection mutex domains are retained across both layouts.
            try
            {
                lock (SelectionSync)
                using (NamedMutexLease mutex = AcquireNamedMutex(SelectionMutexName))
                {
                    SaveSelectionDto selection;
                    if (!File.Exists(_selectionPath)
                        && TryDeserialize(Path.Combine(legacy, "state.json"), out selection)
                        && selection.SchemaVersion == SchemaVersion
                        && (string.IsNullOrEmpty(selection.SelectedSourceKey) || IsDigest(selection.SelectedSourceKey)))
                        AtomicWrite(_selectionPath, selection);
                }
            }
            catch (SaveReadException)
            { _lastWarning = "存档缓存迁移未完成；已保留上次验证副本，请重试。"; return; }
            foreach (SaveSourceMetadataDto enumerated in ReadLegacyV3Metadata().ToArray())
            using (SourceLockLease lease = AcquireSourceLock(enumerated.SourceKey))
            {
                string candidate = null, temporary = null;
                SourceIdentity identity = null;
                try
                {
                    SaveSourceMetadataDto old;
                    if (!TryDeserialize(Path.Combine(enumerated.LegacyDirectory, "metadata.json"), out old)
                        || !ValidateMetadata(old, enumerated.AccountKey, enumerated.SourceKey)) continue;
                    old.LegacyDirectory = enumerated.LegacyDirectory;
                    identity = new SourceIdentity(old.SourcePath, ParseSourceKind(old.SourceKind),
                        old.FileType == "co2" ? SaveFileType.Co2 : SaveFileType.Sl2, old.AccountKey,
                        old.SourceKey, SourceDirectory(old.AccountKey, old.SourceKey));
                    EnsureSourceDirectory(old.AccountKey, old.SourceKey, true);
                    EnsureSourceIdentity(identity.SourceDirectory, old.AccountKey, old.SourceKey);
                    SaveSourceMetadataDto current = TryReadMetadata(identity);
                    ParsedSave parsed; SourceFileSnapshot currentCopy;
                    bool committed = current != null && current.Generation >= old.Generation
                        && TryValidateCurrent(current, out parsed, out currentCopy);
                    if (!committed)
                    {
                        SourceFileSnapshot before;
                        if (!TryValidateCurrent(old, out parsed, out before)) continue;
                        temporary = CreateTemporaryPath(identity);
                        string oldCopy = Path.Combine(old.LegacyDirectory, old.CopyFileName);
                        CopySourceToTemporary(oldCopy, temporary);
                        SourceFileSnapshot copied = CaptureManagedSnapshot(temporary);
                        if (!ContentMatches(before, copied) || !before.SameFile(CaptureManagedSnapshot(oldCopy)))
                            throw new SaveReadException("存档缓存迁移校验失败，已保留旧副本。");
                        string fileName = CreateUniqueCopyFileName(identity, parsed, old.SourceLastWriteTimeUtc,
                            current == null ? null : current.CopyFileName);
                        candidate = Path.Combine(identity.SourceDirectory, fileName);
                        PublishTemporaryFile(temporary, candidate); temporary = null;
                        SetReadOnly(candidate);
                        current = CreateMetadata(identity, fileName, CaptureManagedSnapshot(candidate),
                            new SourceFileSnapshot(old.CopyLength, old.SourceLastWriteTimeUtc, old.ContentSha256),
                            parsed, old.Generation, old.SelectedSlotIndex);
                        current.SourceAvailable = File.Exists(old.SourcePath);
                        Action<string> beforeCommit = BeforeLayoutMigrationCommitForTests;
                        if (beforeCommit != null) beforeCommit(old.SourceKey);
                        WriteMetadata(current);
                        candidate = null;
                        Action<string> afterCommit = AfterLayoutMigrationCommitForTests;
                        if (afterCommit != null) afterCommit(old.SourceKey);
                    }
                    // Revalidate the committed destination before deleting precisely
                    // the old pointer and its owned copy; unknown entries remain.
                    if (!TryValidateCurrent(current, out parsed, out currentCopy)) continue;
                    EnsureImmediateChild(Path.Combine(legacy, "accounts", ContainerKey(old.AccountKey), "sources"),
                        old.LegacyDirectory, ContainerKey(old.SourceKey));
                    SourceIdentity oldIdentity = new SourceIdentity(old.SourcePath, identity.Kind, identity.FileType,
                        old.AccountKey, old.SourceKey, old.LegacyDirectory);
                    DeleteManagedCopyFile(oldIdentity, Path.Combine(old.LegacyDirectory, old.CopyFileName));
                    if (File.Exists(Path.Combine(old.LegacyDirectory, old.CopyFileName))) continue;
                    string oldMetadata = Path.Combine(old.LegacyDirectory, "metadata.json");
                    EnsureNormalFile(oldMetadata);
                    ClearReadOnly(oldMetadata); File.Delete(oldMetadata);
                    TryDeleteEmptyNormalDirectory(old.LegacyDirectory);
                    string oldSources = Path.GetDirectoryName(old.LegacyDirectory);
                    TryDeleteEmptyNormalDirectory(oldSources);
                    TryDeleteEmptyNormalDirectory(Path.GetDirectoryName(oldSources));
                    CleanupSupersededManagedCopies(identity, current.CopyFileName);
                }
                catch (Exception error) when (!(error is OutOfMemoryException) && !(error is StackOverflowException))
                { _lastWarning = "存档缓存迁移未完成；已保留上次验证副本，请重试。"; }
                finally
                {
                    DeleteManagedTemporaryFile(temporary);
                    if (candidate != null && identity != null) DeleteManagedCopyFile(identity, candidate);
                }
            }
            try
            {
                string legacyAccounts = Path.Combine(legacy, "accounts");
                TryDeleteEmptyNormalDirectory(legacyAccounts);
                // Leave the small old selection file until every source was migrated.
                if (!Directory.Exists(legacyAccounts) && File.Exists(_selectionPath))
                {
                    string oldSelection = Path.Combine(legacy, "state.json");
                    if (IsNormalFile(oldSelection)) { ClearReadOnly(oldSelection); File.Delete(oldSelection); }
                    TryDeleteEmptyNormalDirectory(legacy);
                }
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { }
        }

        private LegacySaveCacheMigrationResult MigrateLegacyCaches(string contentHash)
        {
            int removed = 0;
            try
            {
                string v2 = Path.Combine(_saveCopiesRoot, "v2");
                if (IsNormalDirectory(v2))
                {
                    string entry = Path.Combine(v2, contentHash.ToUpperInvariant());
                    if (TryDeleteExactLegacyEntry(entry, contentHash, "save.copy")) removed++;
                    TryDeleteEmptyNormalDirectory(v2);
                }
                foreach (string directory in SafeDirectories(_saveCopiesRoot))
                {
                    string name = Path.GetFileName(directory);
                    if (name == "v2" || name == "v3" || !IsLegacyTimestampName(name)) continue;
                    string[] entries = SafeEntries(directory, 2);
                    if (entries.Length != 1 || !IsNormalFile(entries[0])) continue;
                    string extension = Path.GetExtension(entries[0]);
                    if (!string.Equals(extension, ".sl2", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(extension, ".co2", StringComparison.OrdinalIgnoreCase)) continue;
                    SourceFileSnapshot snapshot = CaptureManagedSnapshot(entries[0]);
                    if (!string.Equals(snapshot.Sha256, contentHash, StringComparison.OrdinalIgnoreCase)) continue;
                    ClearReadOnly(entries[0]);
                    File.Delete(entries[0]);
                    Directory.Delete(directory, false);
                    removed++;
                }
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException
                || error is ArgumentException || error is NotSupportedException || error is SaveReadException) { }
            return new LegacySaveCacheMigrationResult(removed, CountRetainedLegacyEntries());
        }

        private bool TryDeleteExactLegacyEntry(string directory, string contentHash, string expectedFileName)
        {
            try
            {
                if (!IsNormalDirectory(directory)) return false;
                string[] entries = SafeEntries(directory, 2);
                if (entries.Length != 1 || !IsNormalFile(entries[0])
                    || !string.Equals(Path.GetFileName(entries[0]), expectedFileName, StringComparison.Ordinal)) return false;
                SourceFileSnapshot snapshot = CaptureManagedSnapshot(entries[0]);
                if (!string.Equals(snapshot.Sha256, contentHash, StringComparison.OrdinalIgnoreCase)) return false;
                ClearReadOnly(entries[0]);
                File.Delete(entries[0]);
                Directory.Delete(directory, false);
                return true;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException
                || error is SaveReadException) { return false; }
        }

        private int CountRetainedLegacyEntries()
        {
            try
            {
                if (!Directory.Exists(_saveCopiesRoot)) return 0;
                int count = 0;
                foreach (string entry in Directory.EnumerateFileSystemEntries(_saveCopiesRoot, "*", SearchOption.TopDirectoryOnly))
                {
                    string name = Path.GetFileName(entry);
                    if (name == "v3" || name == "state.json" || IsContainerKey(name)) continue;
                    if (name == "v2" && IsNormalDirectory(entry)) count += SafeEntries(entry, int.MaxValue).Length;
                    else count++;
                }
                return count;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { return 0; }
        }

        private void CleanupOrphanTemporaryFiles()
        {
            foreach (string directory in EnumerateManagedDirectories())
            {
                if (PathsEqual(directory, _root))
                {
                    lock (SelectionSync)
                    using (NamedMutexLease mutex = AcquireNamedMutex(SelectionMutexName))
                        foreach (string path in SafeFiles(directory, "incoming-*.tmp")) DeleteManagedTemporaryFile(path);
                }
                else
                {
                    using (SourceLockLease lease = AcquireSourceLock(Path.GetFileName(directory)))
                        foreach (string path in SafeFiles(directory, "incoming-*.tmp")) DeleteManagedTemporaryFile(path);
                }
            }
        }

        private IEnumerable<string> EnumerateManagedDirectories()
        {
            yield return _root;
            foreach (string account in SafeDirectories(_accountsRoot))
            {
                if (!IsContainerKey(Path.GetFileName(account)) || !IsNormalDirectory(account)) continue;
                string sources = account;
                if (!IsNormalDirectory(sources)) continue;
                foreach (string source in SafeDirectories(sources))
                    if (IsContainerKey(Path.GetFileName(source)) && IsNormalDirectory(source)) yield return source;
            }
        }

        private void CleanupSupersededManagedCopies(SourceIdentity identity, string currentFileName)
        {
            foreach (string path in SafeFiles(identity.SourceDirectory, "*"))
            {
                string name = Path.GetFileName(path);
                if (name == "metadata.json" || name == currentFileName || name.StartsWith("incoming-", StringComparison.Ordinal))
                    continue;
                if (IsSafeManagedFileName(name)) DeleteManagedCopyFile(identity, path);
            }
        }

        private void DeleteManagedTemporaryFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                string name = Path.GetFileName(path);
                if (!name.StartsWith("incoming-", StringComparison.Ordinal) || !name.EndsWith(".tmp", StringComparison.Ordinal)
                    || name.Length != "incoming-".Length + 32 + ".tmp".Length) return;
                if (!File.Exists(path) || !IsNormalFile(path)) return;
                string parent = Path.GetDirectoryName(Path.GetFullPath(path));
                bool allowed = PathsEqual(parent, _root) || EnumerateManagedDirectories().Any(item => PathsEqual(item, parent));
                if (!allowed) return;
                ClearReadOnly(path);
                File.Delete(path);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException
                || error is ArgumentException || error is NotSupportedException || error is SaveReadException) { }
        }

        private void DeleteManagedCopyFile(SourceIdentity identity, string path)
        {
            try
            {
                string full = Path.GetFullPath(path);
                string name = Path.GetFileName(full);
                EnsureImmediateChild(identity.SourceDirectory, full, name);
                if (!IsSafeManagedFileName(name) || !IsNormalFile(full)) return;
                ClearReadOnly(full);
                File.Delete(full);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException
                || error is ArgumentException || error is NotSupportedException || error is SaveReadException) { }
        }

        private static string FingerprintFor(SaveSourceMetadataDto metadata, int slotIndex)
        {
            SaveCharacterMetadataDto character = metadata == null || metadata.Characters == null
                ? null : metadata.Characters.FirstOrDefault(item => item.SlotIndex == slotIndex);
            return character == null ? string.Empty : character.RepositoryFingerprint ?? string.Empty;
        }

        internal static string ComputeRepositoryFingerprint(CharacterInventory inventory)
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
            return Hash(text.ToString());
        }

        private static int ChooseSelectedSlot(SaveSourceMetadataDto previous, ParsedSave parsed)
        {
            if (previous != null && parsed.Characters.Any(item => item.SlotIndex == previous.SelectedSlotIndex))
                return previous.SelectedSlotIndex;
            CharacterInventory first = parsed.Characters.OrderBy(item => item.SlotIndex).FirstOrDefault();
            return first == null ? -1 : first.SlotIndex;
        }

        private static bool ContentMatches(SourceFileSnapshot expected, SourceFileSnapshot actual)
        {
            return expected != null && actual != null && expected.Length == actual.Length
                && string.Equals(expected.Sha256, actual.Sha256, StringComparison.OrdinalIgnoreCase);
        }

        private static FileStream OpenSourceReadOnly(string path)
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                CopyBufferSize, FileOptions.SequentialScan);
        }

        private static void SetReadOnly(string path)
        {
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
        }

        private static void ClearReadOnly(string path)
        {
            if (File.Exists(path)) File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
        }

        private static bool IsSafeManagedFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value != Path.GetFileName(value)) return false;
            string extension = Path.GetExtension(value);
            return string.Equals(extension, ".sl2", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".co2", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsReservedWindowsName(string value)
        {
            string stem = value.Split('.')[0].ToUpperInvariant();
            if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL") return true;
            if (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal)
                || stem.StartsWith("LPT", StringComparison.Ordinal)))
                return stem[3] >= '1' && stem[3] <= '9';
            return false;
        }

        private static SaveSourceKind ParseSourceKind(string value)
        {
            return value == "local" ? SaveSourceKind.LocalSteam : SaveSourceKind.Manual;
        }

        private static string FriendlyError(Exception error)
        {
            SaveReadException saveError = error as SaveReadException;
            if (saveError != null && !string.IsNullOrWhiteSpace(saveError.Message)) return saveError.Message;
            if (error is UnauthorizedAccessException) return "没有读取或保存存档副本所需的权限。";
            return "存档刷新失败；已保留上次验证副本。";
        }

        private static string Hash(string value)
        {
            using (SHA256 algorithm = SHA256.Create()) return ToHex(
                algorithm.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty)));
        }

        private static string ToHex(byte[] bytes)
        {
            StringBuilder result = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes) result.Append(value.ToString("X2", CultureInfo.InvariantCulture));
            return result.ToString();
        }

        private static void AddIfPresent(List<string> paths, string path)
        {
            try { if (File.Exists(path)) paths.Add(Path.GetFullPath(path)); }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException
                || error is ArgumentException || error is NotSupportedException) { }
        }

        private static DateTime GetLastWriteTimeSafe(string path)
        {
            try { return File.GetLastWriteTimeUtc(path); }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { return DateTime.MinValue; }
        }

        private static bool IsLegacyTimestampName(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length < 23) return false;
            DateTime ignored;
            if (!DateTime.TryParseExact(value.Substring(0, 23), "yyyy-MM-dd_HH-mm-ss-fff",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out ignored)) return false;
            if (value.Length == 23) return true;
            if (value[23] != '-') return false;
            int suffix;
            return int.TryParse(value.Substring(24), NumberStyles.None, CultureInfo.InvariantCulture, out suffix);
        }

        private static void TryDeleteEmptyNormalDirectory(string path)
        {
            try { if (IsNormalDirectory(path) && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path, false); }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { }
        }

        private static string[] SafeDirectories(string path)
        {
            try { return Directory.Exists(path) ? Directory.GetDirectories(path, "*", SearchOption.TopDirectoryOnly) : new string[0]; }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { return new string[0]; }
        }

        private static string[] SafeFiles(string path, string pattern)
        {
            try { return Directory.Exists(path) ? Directory.GetFiles(path, pattern, SearchOption.TopDirectoryOnly) : new string[0]; }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { return new string[0]; }
        }

        private static string[] SafeEntries(string path, int maximum)
        {
            try { return Directory.Exists(path) ? Directory.EnumerateFileSystemEntries(path).Take(maximum).ToArray() : new string[0]; }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { return new string[0]; }
        }

        private static void EnsureImmediateChild(string parent, string child, string expectedName)
        {
            string fullParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string fullChild = Path.GetFullPath(child).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string actualParent = Path.GetDirectoryName(fullChild);
            if (string.IsNullOrEmpty(actualParent)
                || !string.Equals(actualParent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    fullParent, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetFileName(fullChild), expectedName, StringComparison.Ordinal)) throw UnsafeStructure();
        }

        private static void EnsureNormalDirectory(string path, bool create)
        {
            if (create) Directory.CreateDirectory(path);
            if (!IsNormalDirectory(path)) throw UnsafeStructure();
        }

        private static bool IsNormalDirectory(string path)
        {
            if (!Directory.Exists(path)) return false;
            FileAttributes attributes = File.GetAttributes(path);
            return (attributes & FileAttributes.Directory) != 0 && (attributes & FileAttributes.ReparsePoint) == 0;
        }

        private static void EnsureNormalFile(string path)
        {
            if (!IsNormalFile(path)) throw UnsafeStructure();
        }

        private static bool IsNormalFile(string path)
        {
            if (!File.Exists(path)) return false;
            FileAttributes attributes = File.GetAttributes(path);
            return (attributes & FileAttributes.Directory) == 0 && (attributes & FileAttributes.ReparsePoint) == 0;
        }

        private static bool PathsEqual(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
            return string.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }

        private static SaveReadException UnsafeStructure()
        {
            return new SaveReadException("存档服务目录结构不安全，已停止操作。");
        }

        private static SourceLockLease AcquireSourceLock(string sourceKey)
        {
            // The directory token is available even before the first metadata
            // commit, so restore cleanup can synchronize with a first import.
            sourceKey = IsDigest(sourceKey) ? ContainerKey(sourceKey) : sourceKey;
            if (!IsContainerKey(sourceKey)) throw UnsafeStructure();
            SourceLockEntry entry;
            lock (SourceLocksSync)
            {
                if (!SourceLocks.TryGetValue(sourceKey, out entry))
                {
                    entry = new SourceLockEntry();
                    SourceLocks.Add(sourceKey, entry);
                }
                entry.ReferenceCount++;
            }
            Monitor.Enter(entry.Gate);
            Mutex mutex = null;
            try
            {
                mutex = new Mutex(false, CreateMutexName(sourceKey));
                try { mutex.WaitOne(); } catch (AbandonedMutexException) { }
                return new SourceLockLease(sourceKey, entry, mutex);
            }
            catch
            {
                if (mutex != null) mutex.Dispose();
                Monitor.Exit(entry.Gate);
                ReturnSourceLock(sourceKey, entry);
                throw;
            }
        }

        private SaveSourceMetadataDto ReadMetadataForSource(string sourceKey)
        {
            return ReadAllMetadata().FirstOrDefault(item => string.Equals(item.SourceKey, sourceKey, StringComparison.Ordinal));
        }

        private static NamedMutexLease AcquireNamedMutex(string name)
        {
            Mutex mutex = new Mutex(false, name);
            try
            {
                try { mutex.WaitOne(); } catch (AbandonedMutexException) { }
                return new NamedMutexLease(mutex);
            }
            catch { mutex.Dispose(); throw; }
        }

        private static string CreateMutexName(string sourceKey)
        {
            return @"Local\RelicTool.SaveCopies.v3." + Hash(MutexDomain + sourceKey);
        }

        private static void ReturnSourceLock(string sourceKey, SourceLockEntry entry)
        {
            lock (SourceLocksSync)
            {
                entry.ReferenceCount--;
                SourceLockEntry current;
                if (entry.ReferenceCount == 0 && SourceLocks.TryGetValue(sourceKey, out current)
                    && ReferenceEquals(current, entry)) SourceLocks.Remove(sourceKey);
            }
        }

        private sealed class SourceIdentity
        {
            public SourceIdentity(string sourcePath, SaveSourceKind kind, SaveFileType fileType,
                string accountKey, string sourceKey, string sourceDirectory)
            {
                SourcePath = sourcePath;
                Kind = kind;
                FileType = fileType;
                AccountKey = accountKey;
                SourceKey = sourceKey;
                SourceDirectory = sourceDirectory;
            }
            public string SourcePath;
            public SaveSourceKind Kind;
            public SaveFileType FileType;
            public string AccountKey;
            public string SourceKey;
            public string SourceDirectory;
        }

        private sealed class SourceLockEntry
        {
            public readonly object Gate = new object();
            public int ReferenceCount;
        }

        private sealed class SourceLockLease : IDisposable
        {
            private readonly string _sourceKey;
            private readonly SourceLockEntry _entry;
            private Mutex _mutex;
            public SourceLockLease(string sourceKey, SourceLockEntry entry, Mutex mutex)
            { _sourceKey = sourceKey; _entry = entry; _mutex = mutex; }
            public void Dispose()
            {
                Mutex mutex = Interlocked.Exchange(ref _mutex, null);
                if (mutex == null) return;
                try { try { mutex.ReleaseMutex(); } finally { mutex.Dispose(); } }
                finally { Monitor.Exit(_entry.Gate); ReturnSourceLock(_sourceKey, _entry); }
            }
        }

        private sealed class NamedMutexLease : IDisposable
        {
            private Mutex _inner;
            public NamedMutexLease(Mutex inner) { _inner = inner; }
            public void Dispose()
            {
                Mutex inner = Interlocked.Exchange(ref _inner, null);
                if (inner != null)
                {
                    try { inner.ReleaseMutex(); } catch (ApplicationException) { }
                    inner.Dispose();
                }
            }
        }
    }

    [DataContract]
    internal sealed class SaveSelectionDto
    {
        [DataMember(Name = "schemaVersion")] public string SchemaVersion;
        [DataMember(Name = "selectedSourceKey", EmitDefaultValue = false)] public string SelectedSourceKey;
    }

    [DataContract]
    internal sealed class SaveSourceMetadataDto
    {
        [IgnoreDataMember] internal string LegacyDirectory;
        [DataMember(Name = "schemaVersion")] public string SchemaVersion;
        [DataMember(Name = "sourceKey")] public string SourceKey;
        [DataMember(Name = "accountKey")] public string AccountKey;
        [DataMember(Name = "sourceKind")] public string SourceKind;
        [DataMember(Name = "fileType")] public string FileType;
        [DataMember(Name = "sourcePath")] public string SourcePath;
        [DataMember(Name = "copyFileName")] public string CopyFileName;
        [DataMember(Name = "contentSha256")] public string ContentSha256;
        [DataMember(Name = "copyLength")] public long CopyLength;
        [DataMember(Name = "sourceLastWriteTimeUtc")] public DateTime SourceLastWriteTimeUtc;
        [DataMember(Name = "displayName")] public string DisplayName;
        [DataMember(Name = "validationStatus")] public string ValidationStatus;
        [DataMember(Name = "sourceAvailable")] public bool SourceAvailable;
        [DataMember(Name = "selectedSlotIndex")] public int SelectedSlotIndex;
        [DataMember(Name = "characters")] public List<SaveCharacterMetadataDto> Characters;
        [DataMember(Name = "generation")] public long Generation;
        [DataMember(Name = "lastError", EmitDefaultValue = false)] public string LastError;
    }

    [DataContract]
    internal sealed class SaveCharacterMetadataDto
    {
        [DataMember(Name = "slotIndex")] public int SlotIndex;
        [DataMember(Name = "playerName", EmitDefaultValue = false)] public string PlayerName;
        [DataMember(Name = "repositoryFingerprint")] public string RepositoryFingerprint;
    }
}
