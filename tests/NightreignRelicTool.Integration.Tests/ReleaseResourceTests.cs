using System;
using System.IO;
using System.IO.Compression;
using NightreignRelicTool.Localization;

namespace NightreignRelicTool.Integration.Tests
{
    internal static partial class Program
    {
        private static void RunReleaseResourceTests()
        {
            foreach (string name in new[] { "NightreignRelicTool.Localization.Ui.json", "NightreignRelicTool.Localization.Game.json", "NightreignRelicTool.Localization.Bindings.json" })
            {
                byte[] valid;
                using (var source = typeof(LocalizationService).Assembly.GetManifestResourceStream(name))
                using (var decoded = LocalizationService.DecodeResource(source, name)) valid = decoded.ToArray();
                Assert(valid.Length > 0, "Release resource decoded with its exact integrity descriptor");
                bool missing = false;
                try { using (LocalizationService.DecodeResource(null, name)) { } }
                catch (InvalidDataException) { missing = true; }
                Assert(missing, "Missing localization must fail explicitly");
                foreach (bool truncated in new[] { false, true })
                {
                    byte[] damaged = (byte[])valid.Clone(); damaged[damaged.Length / 2] ^= 1;
                    using (var packed = new MemoryStream())
                    {
                        using (var gzip = new GZipStream(packed, CompressionMode.Compress, true))
                            gzip.Write(damaged, 0, truncated ? damaged.Length - 1 : damaged.Length);
                        packed.Position = 0; bool rejected = false;
                        try { using (LocalizationService.DecodeResource(packed, name)) { } }
                        catch (InvalidDataException) { rejected = true; }
                        Assert(rejected, "Valid gzip containing corrupted or incomplete localization must be rejected");
                    }
                }
            }
            Console.WriteLine("RELEASE_RESOURCE_INTEGRITY_PASS;resources=3;missing_corrupt_incomplete_rejected=true");
        }
    }
}
