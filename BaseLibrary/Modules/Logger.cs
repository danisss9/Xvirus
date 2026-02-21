using System;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using System.Text.Json;
using BaseLibrary.Serializers;
using Xvirus.Model;

namespace Xvirus
{
    public class Logger
    {
        private static readonly ReaderWriterLock rwl = new ReaderWriterLock();
        private static readonly ReaderWriterLock rwl2 = new ReaderWriterLock();
        public static bool EnableLogging = true;

        internal static void LogException(Exception ex)
        {
            if (!EnableLogging)
                return;

            var path = Utils.RelativeToFullPath("errorlog.txt");
            var content = $"Error Log - ${DateTime.UtcNow}\n${ex}\n----------------------------------------------";
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

        public static void LogHistory(string type, string details)
        {
            if (!EnableLogging)
                return;

            var entry = new HistoryEntry(type, DateTime.UtcNow, details);
            var path = Utils.RelativeToFullPath("historylog.txt");

            rwl2.AcquireWriterLock(2000);
            try
            {
                var json = JsonSerializer.Serialize(entry, SourceGenerationContext.Default.HistoryEntry);
                File.AppendAllText(path, json + Environment.NewLine);
            }
            catch (Exception) { }
            finally
            {
                rwl2.ReleaseWriterLock();
            }
        }

        public static List<HistoryEntry> GetHistoryLog()
        {
            var result = new List<HistoryEntry>();
            var path = Utils.RelativeToFullPath("historylog.txt");
            try
            {
                if (File.Exists(path))
                {
                    foreach (var line in File.ReadAllLines(path))
                    {
                        try
                        {
                            var e = JsonSerializer.Deserialize(line, SourceGenerationContext.Default.HistoryEntry);
                            if (e != null)
                                result.Add(e);
                        }
                        catch { /* ignore parse errors */ }
                    }
                }
            }
            catch { /* swallow any I/O errors */ }
            return result;
        }

        public static void ClearHistoryLog()
        {
            var path = Utils.RelativeToFullPath("historylog.txt");
            rwl2.AcquireWriterLock(2000);
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch { /* ignore */ }
            finally
            {
                rwl2.ReleaseWriterLock();
            }
        }
    }
}
