using System;
using System.IO;
using System.Threading;

namespace NightreignRelicTool.Core
{
    // Only local display preferences use this bounded publication recovery.
    // Save-copy transactions retain their own source/selection locking contract.
    internal static class LocalPreferencePublication
    {
        internal static void Publish(string temporary, string destination, Action<string, string> publish = null)
        {
            Action<string, string> action = publish ?? PublishFile;
            for (int attempt = 0; ; attempt++)
            {
                try { action(temporary, destination); return; }
                catch (IOException error) when (attempt < 3 && CanRetry(error))
                {
                    // 32/33/1175 leave both names intact. Never retry 1176/1177:
                    // Windows may already have renamed or removed a file then.
                    Thread.Sleep(10 << attempt);
                }
            }
        }

        internal static bool IsUncertain(Exception error)
        {
            int code = error.HResult & 0xffff;
            return error is IOException && (code == 1176 || code == 1177);
        }

        private static bool CanRetry(IOException error)
        {
            int code = error.HResult & 0xffff;
            return code == 32 || code == 33 || code == 1175;
        }

        private static void PublishFile(string temporary, string destination)
        {
            if (File.Exists(destination)) File.Replace(temporary, destination, null);
            else File.Move(temporary, destination);
        }
    }
}
