using System;
using System.IO;
using System.Threading;

namespace Xvirus
{
    public class Logger
    {
        private static readonly ReaderWriterLock rwl = new ReaderWriterLock();
        public static bool EnableLogging = true;

        internal static void LogException(Exception ex)
        {
            if (!EnableLogging)
                return;

            var path = Utils.RelativeToFullPath("errorlog.txt");
            var content = $"Error Log - ${DateTime.Now}\n${ex}\n----------------------------------------------";
            rwl.AcquireWriterLock(2000);
            try
            {
                File.AppendAllText(path, content);
            }
            catch (Exception) { }
            finally
            {
                rwl.ReleaseWriterLock();
            }
        }
    }
}
