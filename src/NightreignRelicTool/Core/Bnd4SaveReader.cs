using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace NightreignRelicTool.Core
{
    public sealed class Bnd4SaveReader
    {
        private const int HeaderSize = 64;
        private const int EntryHeaderSize = 32;
        private const int AccountEntryIndex = 10;
        private const int CharacterSlotCount = 10;
        private static readonly byte[] EntryMagic = { 0x40, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF };
        private static readonly byte[] SaveKey =
        {
            0x18, 0xF6, 0x32, 0x66, 0x05, 0xBD, 0x17, 0x8A,
            0x55, 0x24, 0x52, 0x3A, 0xC0, 0xA0, 0xC6, 0x09
        };
        private static readonly byte[] SlotMarker = { 0x27, 0x00, 0x00, 0x46, 0x41, 0x43, 0x45 };

        private readonly RuntimeModel _model;

        public Bnd4SaveReader(RuntimeModel model)
        {
            _model = model ?? throw new ArgumentNullException("model");
        }

        public ParsedSave Read(string copyPath)
        {
            string fullPath = Path.GetFullPath(copyPath);
            try
            {
                using (FileStream stream = new FileStream(
                    fullPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    128 * 1024,
                    FileOptions.SequentialScan))
                {
                    EntryHeader[] headers = ReadHeaders(stream);
                    List<string> warnings = new List<string>();
                    bool[] activeSlots = null;
                    byte[] accountPayload = null;
                    if (headers.Length > AccountEntryIndex)
                    {
                        accountPayload = DecryptAndValidate(stream, headers[AccountEntryIndex]);
                        activeSlots = FindActiveCharacterSlots(accountPayload);
                    }
                    if (activeSlots == null)
                        warnings.Add("没有找到角色栏位标记，已安全探测 10 个角色栏位。");

                    List<CharacterInventory> characters = new List<CharacterInventory>();
                    List<string> probeErrors = new List<string>();
                    for (int index = 0; index < headers.Length; index++)
                    {
                        byte[] payload;
                        if (index == AccountEntryIndex && accountPayload != null)
                            payload = accountPayload;
                        else
                            payload = DecryptAndValidate(stream, headers[index]);

                        if (index >= CharacterSlotCount) continue;
                        if (activeSlots != null && !activeSlots[index]) continue;
                        try
                        {
                            CharacterInventory character = InventoryParser.Parse(payload, index, _model);
                            if (activeSlots == null && string.IsNullOrEmpty(character.PlayerName) && character.Relics.Length == 0)
                                continue;
                            characters.Add(character);
                        }
                        catch (SaveReadException error)
                        {
                            if (activeSlots != null)
                                throw new SaveReadException("无法解析已启用的角色栏位 " + (index + 1) + "：" + error.Message, error);
                            probeErrors.Add("栏位 " + (index + 1) + "：" + error.Message);
                        }
                    }

                    if (characters.Count == 0 && probeErrors.Count != 0)
                        throw new SaveReadException("没有角色栏位符合当前 1.03.x 存档布局。" + probeErrors[0]);
                    if (characters.Count == 0)
                        warnings.Add("存档中没有找到已启用的角色数据。");
                    else if (probeErrors.Count != 0)
                        warnings.Add(probeErrors.Count + " 个未启用栏位不符合角色布局，已忽略。");

                    return new ParsedSave(
                        fullPath,
                        headers.Length,
                        activeSlots,
                        characters.ToArray(),
                        warnings.ToArray());
                }
            }
            catch (SaveReadException) { throw; }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is CryptographicException)
            {
                throw new SaveReadException("读取存档副本失败：" + error.Message, error);
            }
        }

        private static EntryHeader[] ReadHeaders(FileStream stream)
        {
            if (stream.Length < HeaderSize) throw new SaveReadException("文件过小，不包含完整的 BND4 头。");
            byte[] header = ReadAt(stream, 0, HeaderSize, "BND4 头");
            if (header[0] != (byte)'B' || header[1] != (byte)'N' || header[2] != (byte)'D' || header[3] != (byte)'4')
                throw new SaveReadException("不是受支持的 Nightreign PC 存档：缺少 BND4 标记。");
            int count = BitConverter.ToInt32(header, 12);
            if (count <= 0 || count > 1024) throw new SaveReadException("BND4 数据块数量无效：" + count);
            long tableEnd = HeaderSize + (long)count * EntryHeaderSize;
            if (tableEnd > stream.Length) throw new SaveReadException("BND4 数据块表被截断。");

            EntryHeader[] result = new EntryHeader[count];
            List<EntryHeader> ranges = new List<EntryHeader>(count);
            for (int index = 0; index < count; index++)
            {
                long offset = HeaderSize + (long)index * EntryHeaderSize;
                byte[] data = ReadAt(stream, offset, EntryHeaderSize, "USERDATA_" + index + " 头");
                if (!StartsWith(data, EntryMagic))
                    throw new SaveReadException("USERDATA_" + index + " 的 BND4 条目标记无效。");
                int encryptedSize = BitConverter.ToInt32(data, 8);
                int encryptedOffset = BitConverter.ToInt32(data, 16);
                if (encryptedSize <= 16 || encryptedOffset < tableEnd || (long)encryptedOffset + encryptedSize > stream.Length)
                    throw new SaveReadException("USERDATA_" + index + " 的数据范围无效。");
                if ((encryptedSize - 16) % 16 != 0)
                    throw new SaveReadException("USERDATA_" + index + " 的 AES 数据没有按 16 字节对齐。");
                result[index] = new EntryHeader(index, encryptedOffset, encryptedSize);
                ranges.Add(result[index]);
            }
            ranges.Sort((left, right) => left.Offset.CompareTo(right.Offset));
            for (int index = 1; index < ranges.Count; index++)
                if ((long)ranges[index - 1].Offset + ranges[index - 1].Size > ranges[index].Offset)
                    throw new SaveReadException("BND4 数据块范围发生重叠。");
            return result;
        }

        private static byte[] DecryptAndValidate(FileStream stream, EntryHeader header)
        {
            byte[] encrypted = ReadAt(stream, header.Offset, header.Size, "USERDATA_" + header.Index);
            byte[] iv = new byte[16];
            Buffer.BlockCopy(encrypted, 0, iv, 0, iv.Length);
            byte[] payload;
            using (Aes aes = Aes.Create())
            {
                aes.Key = SaveKey;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.None;
                using (ICryptoTransform decryptor = aes.CreateDecryptor())
                    payload = decryptor.TransformFinalBlock(encrypted, 16, encrypted.Length - 16);
            }
            if (!VerifyChecksum(payload)) throw new SaveChecksumException(header.Index);
            return payload;
        }

        private static bool VerifyChecksum(byte[] payload)
        {
            if (payload == null || payload.Length < 32) return false;
            int checksumOffset = payload.Length - 28;
            byte[] actual;
            using (MD5 md5 = MD5.Create())
                actual = md5.ComputeHash(payload, 4, checksumOffset - 4);
            int difference = 0;
            for (int index = 0; index < actual.Length; index++)
                difference |= actual[index] ^ payload[checksumOffset + index];
            return difference == 0;
        }

        private static bool[] FindActiveCharacterSlots(byte[] accountPayload)
        {
            for (int marker = 0; marker <= accountPayload.Length - SlotMarker.Length; marker++)
            {
                bool matches = true;
                for (int index = 0; index < SlotMarker.Length; index++)
                    if (accountPayload[marker + index] != SlotMarker[index]) { matches = false; break; }
                if (!matches) continue;
                int flagsOffset = marker - 61;
                if (flagsOffset < 0 || flagsOffset + CharacterSlotCount > accountPayload.Length) continue;
                bool[] flags = new bool[CharacterSlotCount];
                bool valid = true;
                for (int index = 0; index < CharacterSlotCount; index++)
                {
                    byte value = accountPayload[flagsOffset + index];
                    if (value > 1) { valid = false; break; }
                    flags[index] = value == 1;
                }
                if (valid) return flags;
            }
            return null;
        }

        private static byte[] ReadAt(FileStream stream, long offset, int count, string label)
        {
            byte[] result = new byte[count];
            stream.Position = offset;
            int total = 0;
            while (total < count)
            {
                int read = stream.Read(result, total, count - total);
                if (read == 0) throw new SaveReadException("读取 " + label + " 时文件被截断。");
                total += read;
            }
            return result;
        }

        private static bool StartsWith(byte[] data, byte[] prefix)
        {
            if (data.Length < prefix.Length) return false;
            for (int index = 0; index < prefix.Length; index++)
                if (data[index] != prefix[index]) return false;
            return true;
        }

        private sealed class EntryHeader
        {
            public EntryHeader(int index, int offset, int size)
            {
                Index = index;
                Offset = offset;
                Size = size;
            }

            public int Index { get; private set; }
            public int Offset { get; private set; }
            public int Size { get; private set; }
        }
    }

    internal static class InventoryParser
    {
        private const uint ItemTypeWeapon = 0x80000000;
        private const uint ItemTypeArmor = 0x90000000;
        private const uint ItemTypeGoods = 0xB0000000;
        private const uint ItemTypeRelic = 0xC0000000;
        private const uint ItemTypeMask = 0xF0000000;
        private const uint InstanceIdMask = 0x00FFFFFF;
        private const int StateStartOffset = 0x14;
        private const int StateSlotCount = 5120;
        private const int EntrySlotCount = 3065;
        private const int PostStatePadding = 0x94;
        private const int PlayerMetadataSize = 0x5B8;
        private const int EntrySize = 14;

        public static CharacterInventory Parse(byte[] data, int slotIndex, RuntimeModel model)
        {
            List<string> warnings = new List<string>();
            Dictionary<uint, RelicState> states = new Dictionary<uint, RelicState>();
            HashSet<uint> duplicateStates = new HashSet<uint>();
            int unknownStateTypes = 0;
            int cursor = StateStartOffset;
            for (int stateIndex = 0; stateIndex < StateSlotCount; stateIndex++)
            {
                EnsureRange(data, cursor, 8, "物品状态 " + stateIndex);
                uint handle = ReadUInt32(data, cursor);
                uint rawItemId = ReadUInt32(data, cursor + 4);
                uint type = handle & ItemTypeMask;
                int size = handle == 0 ? 8 : StateSize(type);
                EnsureRange(data, cursor, size, "物品状态 " + stateIndex);
                if (handle != 0 && type != ItemTypeWeapon && type != ItemTypeArmor
                    && type != ItemTypeGoods && type != ItemTypeRelic)
                    unknownStateTypes++;
                if (handle != 0 && type == ItemTypeRelic)
                {
                    RelicState state = new RelicState(
                        stateIndex,
                        handle,
                        (int)(handle & InstanceIdMask),
                        (int)(rawItemId & InstanceIdMask),
                        ReadEffects(data, cursor + 16),
                        ReadEffects(data, cursor + 56));
                    if (states.ContainsKey(handle)) duplicateStates.Add(handle);
                    else states.Add(handle, state);
                }
                cursor += size;
            }
            foreach (uint duplicate in duplicateStates) states.Remove(duplicate);
            if (duplicateStates.Count != 0)
                warnings.Add("发现 " + duplicateStates.Count + " 个重复遗物状态，相关条目已忽略。");
            if (unknownStateTypes != 0)
                warnings.Add("发现 " + unknownStateTypes + " 个未知物品状态类型，已按安全长度跳过。");

            int playerNameOffset = cursor + PostStatePadding;
            string playerName = ReadPlayerName(data, playerNameOffset);
            uint sovereignSigs = ReadUInt32Checked(data, playerNameOffset - 64, "王之证数量");
            uint murks = ReadUInt32Checked(data, playerNameOffset + 52, "卢恩数量");
            int entryCountOffset = playerNameOffset + PlayerMetadataSize;
            uint declaredEntryCount = ReadUInt32Checked(data, entryCountOffset, "背包条目数量");
            int entryStart = entryCountOffset + 4;
            EnsureRange(data, entryStart, EntrySlotCount * EntrySize, "背包条目表");

            int actualEntryCount = 0;
            int missingStateCount = 0;
            int duplicateEntryCount = 0;
            int unknownRelicCount = 0;
            HashSet<int> unknownEffectIds = new HashSet<int>();
            HashSet<uint> seenRelicHandles = new HashSet<uint>();
            List<RelicInstance> relics = new List<RelicInstance>();
            for (int entryIndex = 0; entryIndex < EntrySlotCount; entryIndex++)
            {
                int offset = entryStart + entryIndex * EntrySize;
                uint handle = ReadUInt32(data, offset);
                if (handle == 0) continue;
                actualEntryCount++;
                if ((handle & ItemTypeMask) != ItemTypeRelic) continue;
                if (!seenRelicHandles.Add(handle)) { duplicateEntryCount++; continue; }
                RelicState state;
                if (!states.TryGetValue(handle, out state)) { missingStateCount++; continue; }
                RelicTypeDefinition type = model.GetRelicType(state.ItemId);
                int colorId = type == null ? -1 : type.ColorId;
                bool isDeep = type == null
                    ? state.ItemId >= 2000000 && state.ItemId <= 2019999
                    : type.IsDeep;
                if (type == null) unknownRelicCount++;
                TrackUnknownEffects(model, state.PositiveEffectIds, unknownEffectIds);
                TrackUnknownEffects(model, state.NegativeEffectIds, unknownEffectIds);
                relics.Add(new RelicInstance(
                    state.StateIndex,
                    entryIndex,
                    state.Handle,
                    state.InstanceId,
                    state.ItemId,
                    ReadUInt32(data, offset + 8),
                    data[offset + 12] != 0,
                    data[offset + 13] != 0,
                    colorId,
                    isDeep,
                    state.PositiveEffectIds,
                    state.NegativeEffectIds));
            }

            if (declaredEntryCount != actualEntryCount)
                warnings.Add("背包条目数量不一致：记录为 " + declaredEntryCount + "，实际读取为 " + actualEntryCount + "。");
            if (duplicateEntryCount != 0)
                warnings.Add("发现 " + duplicateEntryCount + " 个重复遗物背包条目，已忽略重复项。");
            if (missingStateCount != 0)
                warnings.Add("有 " + missingStateCount + " 个遗物条目找不到对应状态，已忽略。");
            int orphanCount = states.Keys.Count(handle => !seenRelicHandles.Contains(handle));
            if (orphanCount != 0)
                warnings.Add("发现并忽略 " + orphanCount + " 个没有背包条目的残留遗物状态；它们不属于可用遗物。");
            if (unknownRelicCount != 0)
                warnings.Add("有 " + unknownRelicCount + " 颗遗物的颜色或类型不在 1.03.5 数据中，已保留但不参与配装。");
            if (unknownEffectIds.Count != 0)
                warnings.Add("有 " + unknownEffectIds.Count + " 个词条不在 1.03.5 评分模型中，将按 0 分保留。");

            return new CharacterInventory(
                slotIndex,
                playerName,
                murks,
                sovereignSigs,
                relics.ToArray(),
                warnings.ToArray());
        }

        private static int StateSize(uint type)
        {
            if (type == ItemTypeWeapon) return 88;
            if (type == ItemTypeArmor) return 16;
            if (type == ItemTypeRelic) return 80;
            return 8;
        }

        private static int[] ReadEffects(byte[] data, int offset)
        {
            List<int> result = new List<int>(3);
            for (int index = 0; index < 3; index++)
            {
                uint value = ReadUInt32(data, offset + index * 4);
                if (value != 0 && value != uint.MaxValue) result.Add((int)value);
            }
            return result.ToArray();
        }

        private static string ReadPlayerName(byte[] data, int offset)
        {
            EnsureRange(data, offset, 32, "角色名称");
            int length = 32;
            for (int index = 0; index < 32; index += 2)
                if (data[offset + index] == 0 && data[offset + index + 1] == 0) { length = index; break; }
            return length == 0 ? null : Encoding.Unicode.GetString(data, offset, length).Trim();
        }

        private static void TrackUnknownEffects(RuntimeModel model, int[] effectIds, HashSet<int> unknown)
        {
            foreach (int id in effectIds) if (model.GetEffect(id) == null) unknown.Add(id);
        }

        private static uint ReadUInt32Checked(byte[] data, int offset, string label)
        {
            EnsureRange(data, offset, 4, label);
            return ReadUInt32(data, offset);
        }

        private static uint ReadUInt32(byte[] data, int offset)
        {
            return (uint)(data[offset]
                | data[offset + 1] << 8
                | data[offset + 2] << 16
                | data[offset + 3] << 24);
        }

        private static ushort ReadUInt16(byte[] data, int offset)
        {
            return (ushort)(data[offset] | data[offset + 1] << 8);
        }

        private static void EnsureRange(byte[] data, int offset, int count, string label)
        {
            if (offset < 0 || count < 0 || offset > data.Length - count)
                throw new SaveReadException("读取" + label + "时角色数据被截断（偏移 0x" + offset.ToString("X") + "）。");
        }

        private sealed class RelicState
        {
            public RelicState(
                int stateIndex,
                uint handle,
                int instanceId,
                int itemId,
                int[] positiveEffectIds,
                int[] negativeEffectIds)
            {
                StateIndex = stateIndex;
                Handle = handle;
                InstanceId = instanceId;
                ItemId = itemId;
                PositiveEffectIds = positiveEffectIds;
                NegativeEffectIds = negativeEffectIds;
            }

            public int StateIndex { get; private set; }
            public uint Handle { get; private set; }
            public int InstanceId { get; private set; }
            public int ItemId { get; private set; }
            public int[] PositiveEffectIds { get; private set; }
            public int[] NegativeEffectIds { get; private set; }
        }
    }
}
