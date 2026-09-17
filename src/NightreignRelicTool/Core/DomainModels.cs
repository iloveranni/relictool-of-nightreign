using System;

namespace NightreignRelicTool.Core
{
    public class SaveReadException : Exception
    {
        public SaveReadException(string message) : base(message) { }
        public SaveReadException(string message, Exception innerException) : base(message, innerException) { }
    }

    public sealed class SaveChecksumException : SaveReadException
    {
        public SaveChecksumException(int entryIndex)
            : base("存档数据块 USERDATA_" + entryIndex + " 的 MD5 校验失败，副本可能损坏、被截断或版本不受支持。")
        {
            EntryIndex = entryIndex;
        }

        public int EntryIndex { get; private set; }
    }

    public sealed class SourceFileSnapshot
    {
        public SourceFileSnapshot(long length, DateTime lastWriteTimeUtc, string sha256)
        {
            Length = length;
            LastWriteTimeUtc = lastWriteTimeUtc;
            Sha256 = sha256;
        }

        public long Length { get; private set; }
        public DateTime LastWriteTimeUtc { get; private set; }
        public string Sha256 { get; private set; }

        public bool SameFile(SourceFileSnapshot other)
        {
            return other != null
                && Length == other.Length
                && LastWriteTimeUtc == other.LastWriteTimeUtc
                && string.Equals(Sha256, other.Sha256, StringComparison.OrdinalIgnoreCase);
        }
    }

    public sealed class SaveImportResult
    {
        public SaveImportResult(
            string sourcePath,
            string copyPath,
            SourceFileSnapshot sourceBefore,
            SourceFileSnapshot sourceAfter,
            SourceFileSnapshot copySnapshot,
            ParsedSave save)
        {
            SourcePath = sourcePath;
            CopyPath = copyPath;
            SourceBefore = sourceBefore;
            SourceAfter = sourceAfter;
            CopySnapshot = copySnapshot;
            Save = save;
        }

        internal SaveImportResult(
            string sourcePath,
            string copyPath,
            SourceFileSnapshot sourceBefore,
            SourceFileSnapshot sourceAfter,
            SourceFileSnapshot copySnapshot,
            ParsedSave save,
            SaveSourceKey sourceKey,
            SaveRefreshStatus refreshStatus,
            string displayName,
            string previousRepositoryFingerprint,
            string currentRepositoryFingerprint)
            : this(sourcePath, copyPath, sourceBefore, sourceAfter, copySnapshot, save)
        {
            SourceKey = sourceKey;
            RefreshStatus = refreshStatus;
            DisplayName = displayName ?? string.Empty;
            PreviousRepositoryFingerprint = previousRepositoryFingerprint ?? string.Empty;
            CurrentRepositoryFingerprint = currentRepositoryFingerprint ?? string.Empty;
        }

        public string SourcePath { get; private set; }
        public string CopyPath { get; private set; }
        public SourceFileSnapshot SourceBefore { get; private set; }
        public SourceFileSnapshot SourceAfter { get; private set; }
        public SourceFileSnapshot CopySnapshot { get; private set; }
        public ParsedSave Save { get; private set; }
        public bool SourceUnchanged { get { return SourceBefore.SameFile(SourceAfter); } }
        public SaveSourceKey SourceKey { get; private set; }
        public SaveRefreshStatus RefreshStatus { get; private set; }
        public string DisplayName { get; private set; }
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
    }

    public sealed class ParsedSave
    {
        public ParsedSave(
            string path,
            int bnd4EntryCount,
            bool[] activeCharacterSlots,
            CharacterInventory[] characters,
            string[] warnings)
        {
            Path = path;
            Bnd4EntryCount = bnd4EntryCount;
            ActiveCharacterSlots = activeCharacterSlots;
            Characters = characters;
            Warnings = warnings;
        }

        public string Path { get; private set; }
        public int Bnd4EntryCount { get; private set; }
        public bool[] ActiveCharacterSlots { get; private set; }
        public CharacterInventory[] Characters { get; private set; }
        public string[] Warnings { get; private set; }
    }

    public sealed class CharacterInventory
    {
        public CharacterInventory(
            int slotIndex,
            string playerName,
            uint murks,
            uint sovereignSigs,
            RelicInstance[] relics,
            string[] warnings)
        {
            SlotIndex = slotIndex;
            PlayerName = playerName;
            Murks = murks;
            SovereignSigs = sovereignSigs;
            Relics = relics;
            Warnings = warnings;
        }

        public int SlotIndex { get; private set; }
        public int SlotNumber { get { return SlotIndex + 1; } }
        public string PlayerName { get; private set; }
        public uint Murks { get; private set; }
        public uint SovereignSigs { get; private set; }
        public RelicInstance[] Relics { get; private set; }
        public string[] Warnings { get; private set; }
    }

    public sealed class RelicInstance
    {
        public RelicInstance(
            int stateIndex,
            int entryIndex,
            uint gaHandle,
            int instanceId,
            int itemId,
            uint inventorySortKey,
            bool favorite,
            bool isNew,
            int colorId,
            bool isDeep,
            int[] positiveEffectIds,
            int[] negativeEffectIds)
        {
            StateIndex = stateIndex;
            EntryIndex = entryIndex;
            GaHandle = gaHandle;
            InstanceId = instanceId;
            ItemId = itemId;
            InventorySortKey = inventorySortKey;
            Favorite = favorite;
            IsNew = isNew;
            ColorId = colorId;
            IsDeep = isDeep;
            PositiveEffectIds = positiveEffectIds;
            NegativeEffectIds = negativeEffectIds;
        }

        public int StateIndex { get; private set; }
        public int EntryIndex { get; private set; }
        public uint GaHandle { get; private set; }
        public int InstanceId { get; private set; }
        public int ItemId { get; private set; }
        public uint InventorySortKey { get; private set; }
        public bool Favorite { get; private set; }
        public bool IsNew { get; private set; }
        public int ColorId { get; private set; }
        public bool IsDeep { get; private set; }
        public int[] PositiveEffectIds { get; private set; }
        public int[] NegativeEffectIds { get; private set; }
    }

    public sealed class OptimizationResult
    {
        public OptimizationResult(
            VesselDefinition vessel,
            SlotAssignment[] assignments,
            ScoreBreakdown score,
            long elapsedMilliseconds,
            long evaluatedBuilds)
        {
            Vessel = vessel;
            Assignments = assignments;
            Score = score;
            ElapsedMilliseconds = elapsedMilliseconds;
            EvaluatedBuilds = evaluatedBuilds;
        }

        public VesselDefinition Vessel { get; private set; }
        public SlotAssignment[] Assignments { get; private set; }
        public ScoreBreakdown Score { get; private set; }
        public long ElapsedMilliseconds { get; private set; }
        public long EvaluatedBuilds { get; private set; }
    }

    public sealed class SlotAssignment
    {
        public SlotAssignment(bool isDeep, int slotIndex, int slotColorId, RelicInstance relic)
        {
            IsDeep = isDeep;
            SlotIndex = slotIndex;
            SlotColorId = slotColorId;
            Relic = relic;
        }

        public bool IsDeep { get; private set; }
        public int SlotIndex { get; private set; }
        public int SlotColorId { get; private set; }
        public RelicInstance Relic { get; private set; }
    }

    public sealed class ScoreBreakdown
    {
        public ScoreBreakdown(
            int totalScore,
            int positiveScore,
            int coreScore,
            int negativePenalty,
            int verifiedPositiveCount,
            int completedCoreCount)
        {
            TotalScore = totalScore;
            PositiveScore = positiveScore;
            CoreScore = coreScore;
            NegativePenalty = negativePenalty;
            VerifiedPositiveCount = verifiedPositiveCount;
            CompletedCoreCount = completedCoreCount;
        }

        public int TotalScore { get; private set; }
        public int PositiveScore { get; private set; }
        public int CoreScore { get; private set; }
        public int NegativePenalty { get; private set; }
        public int VerifiedPositiveCount { get; private set; }
        public int CompletedCoreCount { get; private set; }
    }

    public sealed class RelicLocation
    {
        public RelicLocation(
            SlotAssignment assignment,
            bool isExactPositionAvailable,
            int row,
            int column,
            string unavailableReason)
        {
            if (assignment == null) throw new ArgumentNullException("assignment");
            if (isExactPositionAvailable && (row <= 0 || column <= 0))
                throw new ArgumentOutOfRangeException("row", "精确位置必须包含有效行列。");
            Assignment = assignment;
            IsExactPositionAvailable = isExactPositionAvailable;
            Row = isExactPositionAvailable ? row : 0;
            Column = isExactPositionAvailable ? column : 0;
            UnavailableReason = isExactPositionAvailable ? string.Empty : (unavailableReason ?? string.Empty);
        }

        public RelicLocation(
            SlotAssignment assignment,
            int expectedNewestFirstIndex,
            string previousRelicSummary,
            string nextRelicSummary)
            : this(assignment, expectedNewestFirstIndex, 0, previousRelicSummary, nextRelicSummary)
        {
        }

        public RelicLocation(
            SlotAssignment assignment,
            int expectedNewestFirstIndex,
            int colorGroupCount,
            string previousRelicSummary,
            string nextRelicSummary)
        {
            Assignment = assignment;
            ExpectedNewestFirstIndex = expectedNewestFirstIndex;
            ColorGroupCount = colorGroupCount;
            PreviousRelicSummary = previousRelicSummary;
            NextRelicSummary = nextRelicSummary;
        }

        public SlotAssignment Assignment { get; private set; }
        public bool IsExactPositionAvailable { get; private set; }
        public int Row { get; private set; }
        public int Column { get; private set; }
        public string UnavailableReason { get; private set; }
        public int ExpectedNewestFirstIndex { get; private set; }
        public int ColorGroupCount { get; private set; }
        public int ExpectedOldestFirstIndex
        {
            get
            {
                return ExpectedNewestFirstIndex <= 0 || ColorGroupCount <= 0
                    ? 0
                    : ColorGroupCount - ExpectedNewestFirstIndex + 1;
            }
        }
        public string PreviousRelicSummary { get; private set; }
        public string NextRelicSummary { get; private set; }
    }
}
