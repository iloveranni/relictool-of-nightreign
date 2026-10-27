using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace NightreignRelicTool.Core
{
    public sealed class DataPackLoadResult
    {
        internal DataPackLoadResult(RuntimeModel model, CustomEffectCatalog customEffects, string packId, string packVersion, bool isBuiltIn, string warning)
        {
            Model = model;
            CustomEffects = customEffects;
            PackId = packId;
            PackVersion = packVersion;
            IsBuiltIn = isBuiltIn;
            Warning = warning;
        }

        public RuntimeModel Model { get; private set; }
        public CustomEffectCatalog CustomEffects { get; private set; }
        public string PackId { get; private set; }
        public string PackVersion { get; private set; }
        public bool IsBuiltIn { get; private set; }
        public string Warning { get; private set; }
    }

    public sealed class DataPackManager
    {
        private const string PublicKeyResource = "NightreignRelicTool.Resources.NrpackPublicKey.xml";
        private static readonly object GlobalSync = new object();
        private readonly string _packRoot;
        private readonly string _activePath;
        private readonly string _previousPath;
        private readonly string _builtInMarkerPath;
        private readonly string _publicKeyXml;

        public DataPackManager(string applicationRoot)
            : this(applicationRoot, ReadPublicKey())
        {
        }

        internal DataPackManager(string applicationRoot, string publicKeyXml)
        {
            string root = Path.GetFullPath(applicationRoot ?? AppDomain.CurrentDomain.BaseDirectory);
            _packRoot = Path.Combine(root, "UserData", "DataPacks");
            _activePath = Path.Combine(_packRoot, "active.nrpack");
            _previousPath = Path.Combine(_packRoot, "previous.nrpack");
            _builtInMarkerPath = Path.Combine(_packRoot, "previous-is-builtin");
            if (string.IsNullOrWhiteSpace(publicKeyXml))
                throw new ArgumentException("数据包公钥不能为空。", "publicKeyXml");
            _publicKeyXml = publicKeyXml;
        }

        public DataPackLoadResult LoadCurrent()
        {
            lock (GlobalSync)
            {
                if (File.Exists(_activePath))
                {
                    try { return Validate(_activePath); }
                    catch (Exception)
                    {
                        return BuiltIn("外部数据包无效，已继续使用内置 1.03.5。");
                    }
                }
                return BuiltIn(null);
            }
        }

        // The formal application ships one reviewed data/text version. Historical
        // package files remain intact and are only used by explicit legacy tooling.
        public DataPackLoadResult LoadBuiltIn()
        {
            lock (GlobalSync) return BuiltIn(null);
        }

        public DataPackLoadResult Import(string path)
        {
            return Import(path, null);
        }

        internal DataPackLoadResult Import(string path, Action<DataPackLoadResult> prepareBeforeCommit)
        {
            lock (GlobalSync)
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    throw new InvalidDataException("找不到所选 .nrpack 文件。");
                if (!string.Equals(Path.GetExtension(path), ".nrpack", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("数据更新包必须使用 .nrpack 扩展名。");

                Directory.CreateDirectory(_packRoot);
                string staging = Path.Combine(_packRoot, "incoming-" + Guid.NewGuid().ToString("N") + ".nrpack");
                try
                {
                    CopyToNewFile(path, staging);
                    DataPackLoadResult installed = Validate(staging);
                    if (prepareBeforeCommit != null) prepareBeforeCommit(installed);
                    if (File.Exists(_activePath))
                    {
                        bool activeIsValid;
                        try
                        {
                            Validate(_activePath);
                            activeIsValid = true;
                        }
                        catch
                        {
                            activeIsValid = false;
                        }

                        if (activeIsValid)
                        {
                            // ReplaceFile installs the staged package and snapshots the old active
                            // package as previous in one same-volume atomic operation.
                            File.Replace(staging, _activePath, _previousPath, true);
                            DeleteIfPresent(_builtInMarkerPath);
                        }
                        else
                        {
                            // LoadCurrent already treats an invalid active package as built-in.
                            // Preserve that logical state as the rollback target.
                            DeleteRequired(_previousPath);
                            EnsureBuiltInMarker();
                            File.Replace(staging, _activePath, null, true);
                        }
                    }
                    else
                    {
                        DeleteRequired(_previousPath);
                        EnsureBuiltInMarker();
                        File.Move(staging, _activePath);
                    }
                    return installed;
                }
                finally
                {
                    DeleteIfPresent(staging);
                }
            }
        }

        public DataPackLoadResult Rollback()
        {
            return Rollback(null);
        }

        internal DataPackLoadResult Rollback(Action<DataPackLoadResult> prepareBeforeCommit)
        {
            lock (GlobalSync)
            {
                Directory.CreateDirectory(_packRoot);
                if (File.Exists(_activePath) && File.Exists(_previousPath))
                {
                    DataPackLoadResult previous = Validate(_previousPath);
                    bool activeIsValid;
                    try
                    {
                        Validate(_activePath);
                        activeIsValid = true;
                    }
                    catch
                    {
                        activeIsValid = false;
                    }

                    if (prepareBeforeCommit != null) prepareBeforeCommit(previous);

                    if (!activeIsValid)
                    {
                        // The current logical state is built-in. Replace the invalid active file
                        // directly with the validated previous package and remember built-in.
                        EnsureBuiltInMarker();
                        File.Replace(_previousPath, _activePath, null, true);
                        return previous;
                    }

                    string staging = Path.Combine(_packRoot, "rollback-" + Guid.NewGuid().ToString("N") + ".nrpack");
                    try
                    {
                        CopyToNewFile(_previousPath, staging);
                        DataPackLoadResult staged = Validate(staging);
                        // The existing previous file is atomically overwritten with the old active
                        // package while the validated staging file becomes active.
                        File.Replace(staging, _activePath, _previousPath, true);
                        DeleteIfPresent(_builtInMarkerPath);
                        return staged;
                    }
                    finally
                    {
                        DeleteIfPresent(staging);
                    }
                }
                if (File.Exists(_activePath) && File.Exists(_builtInMarkerPath))
                {
                    Validate(_activePath);
                    DataPackLoadResult builtIn = BuiltIn(null);
                    if (prepareBeforeCommit != null) prepareBeforeCommit(builtIn);
                    File.Move(_activePath, _previousPath);
                    return builtIn;
                }
                if (!File.Exists(_activePath) && File.Exists(_previousPath))
                {
                    DataPackLoadResult previous = Validate(_previousPath);
                    if (prepareBeforeCommit != null) prepareBeforeCommit(previous);
                    EnsureBuiltInMarker();
                    File.Move(_previousPath, _activePath);
                    return previous;
                }
                throw new InvalidOperationException("没有可以回退的数据包。");
            }
        }

        public bool CanRollback
        {
            get
            {
                lock (GlobalSync)
                {
                    return File.Exists(_previousPath)
                        || (File.Exists(_activePath) && File.Exists(_builtInMarkerPath));
                }
            }
        }

        public string PreviousDescription
        {
            get
            {
                lock (GlobalSync)
                {
                    if (File.Exists(_previousPath))
                    {
                        try
                        {
                            DataPackLoadResult previous = Validate(_previousPath);
                            return previous.PackId + " " + previous.PackVersion;
                        }
                        catch { return "上一数据包不可用"; }
                    }
                    return File.Exists(_builtInMarkerPath) ? "内置数据包 1.03.5" : "没有上一版本";
                }
            }
        }

        private DataPackLoadResult Validate(string path)
        {
            FileInfo info = new FileInfo(path);
            if (!info.Exists || info.Length <= 0 || info.Length > 4 * 1024 * 1024)
                throw new InvalidDataException("数据包为空或超过 4 MB 限制。");
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read, false))
            {
                string[] actual = archive.Entries.Select(item => item.FullName).OrderBy(item => item, StringComparer.Ordinal).ToArray();
                byte[] manifestBytes = ReadEntry(archive.GetEntry("manifest.json"), 64 * 1024);
                NrpackManifest manifest = DeserializeManifest(manifestBytes);
                ValidateManifestMetadata(manifest);
                string[] expected = manifest.SchemaVersion == 2
                    ? new[] { "manifest.json", "model.json.gz", "catalog.json.gz", "custom-effects.json.gz" }
                    : new[] { "manifest.json", "model.json.gz", "catalog.json.gz" };
                if (!actual.SequenceEqual(expected.OrderBy(item => item, StringComparer.Ordinal), StringComparer.Ordinal))
                    throw new InvalidDataException(manifest.SchemaVersion == 2
                        ? "v2 数据包只能包含 manifest.json、model.json.gz、catalog.json.gz 和 custom-effects.json.gz。"
                        : "v1 数据包只能包含 manifest.json、model.json.gz 和 catalog.json.gz。");
                byte[] modelBytes = ReadEntry(archive.GetEntry("model.json.gz"), 2 * 1024 * 1024);
                byte[] catalogBytes = ReadEntry(archive.GetEntry("catalog.json.gz"), 512 * 1024);
                byte[] customEffectBytes = manifest.SchemaVersion == 2
                    ? ReadEntry(archive.GetEntry("custom-effects.json.gz"), 2 * 1024 * 1024)
                    : null;
                ValidateManifestSecurity(manifest, modelBytes, catalogBytes, customEffectBytes);
                RuntimeModel model = RuntimeModel.LoadCompressed(modelBytes, catalogBytes);
                CustomEffectCatalog customEffects = customEffectBytes == null
                    ? CustomEffectCatalog.LoadBuiltIn()
                    : CustomEffectCatalog.LoadCompressed(customEffectBytes);
                if (!string.Equals(model.RegulationVersion, manifest.GameRegulation, StringComparison.Ordinal))
                    throw new InvalidDataException("清单游戏版本与评分模型不一致。");
                if (!string.Equals(customEffects.GameVersion, manifest.GameRegulation, StringComparison.Ordinal))
                    throw new InvalidDataException("清单游戏版本与自定义词条数据不一致。");
                if (customEffectBytes != null) ValidateCrossReferences(model, customEffects);
                string warning = customEffectBytes == null ? "该 v1 数据包没有自定义词条资源；自定义检索继续使用内置 1.03.5 数据。" : null;
                return new DataPackLoadResult(model, customEffects, manifest.PackId, manifest.PackVersion, false, warning);
            }
        }

        private static void ValidateManifestMetadata(NrpackManifest manifest)
        {
            if (manifest == null || (manifest.SchemaVersion != 1 && manifest.SchemaVersion != 2))
                throw new InvalidDataException("不支持的数据包清单版本。");
            if (string.IsNullOrWhiteSpace(manifest.PackId) || string.IsNullOrWhiteSpace(manifest.PackVersion))
                throw new InvalidDataException("数据包 ID 或版本为空。");
            if (!string.Equals(manifest.GameRegulation, "1.03.5", StringComparison.Ordinal))
                throw new InvalidDataException("当前主程序只支持 Regulation 1.03.5 数据包。");
            Version minimum;
            if (!Version.TryParse(manifest.MinimumProgramVersion, out minimum)
                || minimum > typeof(DataPackManager).Assembly.GetName().Version)
                throw new InvalidDataException("数据包要求更高版本的主程序。");
        }

        private void ValidateManifestSecurity(NrpackManifest manifest, byte[] modelBytes, byte[] catalogBytes, byte[] customEffectBytes)
        {
            string modelHash = ComputeSha256(modelBytes);
            string catalogHash = ComputeSha256(catalogBytes);
            if (!FixedEquals(modelHash, manifest.ModelSha256) || !FixedEquals(catalogHash, manifest.CatalogSha256))
                throw new InvalidDataException("数据包文件哈希不匹配。");
            if (manifest.SchemaVersion == 2)
            {
                if (customEffectBytes == null || !FixedEquals(ComputeSha256(customEffectBytes), manifest.CustomEffectsSha256))
                    throw new InvalidDataException("自定义词条资源哈希不匹配。");
            }
            byte[] signedData = Encoding.UTF8.GetBytes(CanonicalManifest(manifest));
            byte[] signature;
            if (string.IsNullOrWhiteSpace(manifest.Signature))
                throw new InvalidDataException("数据包签名格式无效。");
            try { signature = Convert.FromBase64String(manifest.Signature); }
            catch (FormatException error) { throw new InvalidDataException("数据包签名格式无效。", error); }
            using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider())
            {
                rsa.PersistKeyInCsp = false;
                rsa.FromXmlString(_publicKeyXml);
                if (!rsa.VerifyData(signedData, CryptoConfig.MapNameToOID("SHA256"), signature))
                    throw new InvalidDataException("数据包数字签名无效。");
            }
        }

        private static void ValidateCrossReferences(RuntimeModel model, CustomEffectCatalog customEffects)
        {
            HashSet<string> characterIds = new HashSet<string>(
                model.Characters.Select(item => item.Id),
                StringComparer.OrdinalIgnoreCase);
            foreach (CustomRuntimeEffect effect in customEffects.Effects)
            {
                if (effect.ApplicableCharacterIds.Distinct(StringComparer.OrdinalIgnoreCase).Count()
                    != effect.ApplicableCharacterIds.Length)
                    throw new InvalidDataException("Runtime Effect " + effect.RuntimeEffectId + " 包含重复角色适用引用。");
                foreach (string characterId in effect.ApplicableCharacterIds)
                    if (!characterIds.Contains(characterId))
                        throw new InvalidDataException("Runtime Effect " + effect.RuntimeEffectId + " 引用了不存在的角色：" + characterId);
            }
            foreach (OfficialPresetRelic preset in customEffects.Presets)
                if (model.GetRelicType(preset.ItemId) == null)
                    throw new InvalidDataException("预设遗物引用了遗物目录中不存在的 ItemId：" + preset.ItemId);
        }

        internal static string CanonicalManifest(NrpackManifest manifest)
        {
            List<string> lines = new List<string>(new[]
            {
                manifest.SchemaVersion.ToString(),
                manifest.PackId ?? string.Empty,
                manifest.PackVersion ?? string.Empty,
                manifest.GameRegulation ?? string.Empty,
                manifest.MinimumProgramVersion ?? string.Empty,
                manifest.ModelSha256 ?? string.Empty,
                manifest.CatalogSha256 ?? string.Empty
            });
            if (manifest.SchemaVersion >= 2) lines.Add(manifest.CustomEffectsSha256 ?? string.Empty);
            return string.Join("\n", lines);
        }

        private static byte[] ReadEntry(ZipArchiveEntry entry, int maximumBytes)
        {
            if (entry == null || entry.Length < 0 || entry.Length > maximumBytes)
                throw new InvalidDataException("数据包条目缺失或超过大小限制。");
            using (Stream input = entry.Open())
            using (MemoryStream output = new MemoryStream((int)entry.Length))
            {
                byte[] buffer = new byte[64 * 1024];
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) != 0)
                {
                    if (output.Length + read > maximumBytes)
                        throw new InvalidDataException("数据包条目解压后超过大小限制。");
                    output.Write(buffer, 0, read);
                }
                return output.ToArray();
            }
        }

        private static NrpackManifest DeserializeManifest(byte[] bytes)
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(NrpackManifest));
            using (MemoryStream stream = new MemoryStream(bytes, false))
                return (NrpackManifest)serializer.ReadObject(stream);
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] hash = algorithm.ComputeHash(bytes);
                StringBuilder text = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash) text.Append(value.ToString("X2"));
                return text.ToString();
            }
        }

        private static bool FixedEquals(string left, string right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            int difference = 0;
            for (int index = 0; index < left.Length; index++) difference |= left[index] ^ right[index];
            return difference == 0;
        }

        private static string ReadPublicKey()
        {
            using (Stream stream = typeof(DataPackManager).Assembly.GetManifestResourceStream(PublicKeyResource))
            {
                if (stream == null) throw new InvalidDataException("找不到内置数据包公钥。");
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true)) return reader.ReadToEnd();
            }
        }

        private void EnsureBuiltInMarker()
        {
            if (File.Exists(_builtInMarkerPath)) return;
            string staging = Path.Combine(_packRoot, "marker-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (FileStream output = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    byte[] bytes = Encoding.ASCII.GetBytes("builtin-1.03.5");
                    output.Write(bytes, 0, bytes.Length);
                    output.Flush(true);
                }
                File.Move(staging, _builtInMarkerPath);
            }
            finally
            {
                DeleteIfPresent(staging);
            }
        }

        private static void CopyToNewFile(string sourcePath, string destinationPath)
        {
            using (FileStream input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (FileStream output = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                input.CopyTo(output);
                output.Flush(true);
            }
        }

        private static void DeleteRequired(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }

        private static void DeleteIfPresent(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static DataPackLoadResult BuiltIn(string warning)
        {
            return new DataPackLoadResult(RuntimeModel.LoadBuiltIn(), CustomEffectCatalog.LoadBuiltIn(), "builtin", "1.03.5", true, warning);
        }
    }

#pragma warning disable 0649
    [DataContract]
    internal sealed class NrpackManifest
    {
        [DataMember(Name = "schemaVersion")] public int SchemaVersion;
        [DataMember(Name = "packId")] public string PackId;
        [DataMember(Name = "packVersion")] public string PackVersion;
        [DataMember(Name = "gameRegulation")] public string GameRegulation;
        [DataMember(Name = "minimumProgramVersion")] public string MinimumProgramVersion;
        [DataMember(Name = "modelSha256")] public string ModelSha256;
        [DataMember(Name = "catalogSha256")] public string CatalogSha256;
        [DataMember(Name = "customEffectsSha256")] public string CustomEffectsSha256;
        [DataMember(Name = "signature")] public string Signature;
    }
#pragma warning restore 0649
}
