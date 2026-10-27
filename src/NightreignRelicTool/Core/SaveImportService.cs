using System;
using System.IO;
using System.Security.Cryptography;

namespace NightreignRelicTool.Core
{
    /// <summary>
    /// Compatibility facade for existing callers. New UI code should bind to
    /// SaveManagementService so discovery, selection and refresh states remain explicit.
    /// </summary>
    public sealed class SaveImportService
    {
        private const int CopyBufferSize = 128 * 1024;
        private readonly SaveManagementService _service;

        public SaveImportService(RuntimeModel model, string applicationRoot)
        {
            _service = new SaveManagementService(model, applicationRoot);
        }

        internal SaveImportService(RuntimeModel model, string applicationRoot, string localSaveRoot)
        {
            _service = new SaveManagementService(model, applicationRoot, localSaveRoot);
        }

        internal static Action<string> AfterInitialSourceSnapshotForTests
        {
            get { return SaveManagementService.AfterInitialSourceSnapshotForTests; }
            set { SaveManagementService.AfterInitialSourceSnapshotForTests = value; }
        }

        public string[] FindLocalSaves() { return _service.FindLocalSavePaths(); }
        public SaveDiscoveryResult DiscoverLocalSources() { return _service.DiscoverLocalSaves(); }
        public SaveRefreshCycleResult RefreshSelectedAndDiscover() { return _service.RefreshSelectedAndDiscover(); }
        public SaveServiceSnapshot RestoreSources() { return _service.Restore(); }
        public SaveRefreshResult Refresh(SaveSourceKey sourceKey) { return _service.Refresh(sourceKey); }
        public SaveRefreshResult ImportManual(string path) { return _service.ImportManual(path); }

        public SaveImportResult Import(string sourcePath)
        {
            SaveRefreshResult result = _service.ImportPath(sourcePath);
            if (!result.Succeeded || result.Imported == null)
                throw new SaveReadException(string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? "存档导入失败；上次验证副本保持不变。" : result.ErrorMessage);
            return result.Imported;
        }

        public static SourceFileSnapshot CaptureReadOnly(string path)
        {
            FileInfo info = new FileInfo(path);
            if (!info.Exists) throw new SaveReadException("找不到存档文件。");
            string hash;
            try
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete, CopyBufferSize, FileOptions.SequentialScan))
                using (SHA256 algorithm = SHA256.Create())
                    hash = ToHex(algorithm.ComputeHash(stream));
                info.Refresh();
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                throw new SaveReadException("无法以只读方式访问存档。", error);
            }
            return new SourceFileSnapshot(info.Length, info.LastWriteTimeUtc, hash);
        }

        private static string ToHex(byte[] bytes)
        {
            char[] chars = new char[bytes.Length * 2];
            const string alphabet = "0123456789ABCDEF";
            for (int index = 0; index < bytes.Length; index++)
            {
                chars[index * 2] = alphabet[bytes[index] >> 4];
                chars[index * 2 + 1] = alphabet[bytes[index] & 15];
            }
            return new string(chars);
        }
    }
}
