using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using XescSDK.Model;

namespace XescSDK
{
    public class Scanner
    {
        public readonly SettingsDTO settings;
        private readonly DB database;
        private readonly AI ai;

        public Scanner(SettingsDTO settings, DB database, AI ai)
        {
            this.settings = settings;
            this.database = database;
            this.ai = ai;
        }

        public ScanResult ScanFile(string filePath, out string md5Result)
        {
            md5Result = "";
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists)
                return new ScanResult(-1, "File not found!");

            if (settings.MaxScanLength != null && fileInfo.Length > settings.MaxScanLength)
                return new ScanResult(-1, "File too big!");

            string? hash = null;
            using (var md5 = MD5.Create())
            {
                using var stream = Utils.ReadFile(filePath);
                var checksum = md5.ComputeHash(stream);
                hash = BitConverter.ToString(checksum).Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
            }

            if (hash == null)
                return new ScanResult(-1, "Could not get file hash!");

            md5Result = hash;

            if (database.safeHashList.Contains(hash))
                return new ScanResult(0, "Safe");

            if (database.malHashList.Contains(hash))
                return new ScanResult(1, "Malware");

            var certName = Utils.GetCertificateSubjectName(filePath);
            if (certName != null)
            {
                if (database.malVendorList.TryGetValue(certName, out string? value))
                {
                    return new ScanResult(1, value);
                }
                else
                {
                    return new ScanResult(0, "Safe");
                }
            }

            if (settings.EnableHeuristics || settings.EnableAIScan)
            {
                bool isExecutable = false;
                using (var stream = File.OpenRead(filePath))
                {
                    using var reader = new BinaryReader(stream);
                    try
                    {
                        var bytes = reader.ReadChars(2);
                        isExecutable = bytes[0] == 'M' && bytes[1] == 'Z';
                    }
                    catch (ArgumentException) { }
                }

                if (settings.EnableHeuristics)
                {
                    if (isExecutable)
                    {
                        using var stream = Utils.ReadFile(filePath);
                        var match = database.heurListPatterns.Search(stream).FirstOrDefault();
                        if (match.Key != null)
                        {
                            var name = database.heurList[match.Key];
                            return new ScanResult(1, name);
                        }
                    }
                    else if(fileInfo.Length <= 52428800) // 50MBs
                    {
                        using var stream = Utils.ReadFile(filePath);
                        var match = database.heurScriptListPatterns.Search(stream).FirstOrDefault();
                        if (match.Key != null)
                        {
                            var name = database.heurScriptList[match.Key];
                            return new ScanResult(1, name);
                        }
                    }
                }

                if (isExecutable && settings.EnableAIScan)
                {
                    var aiScore = ai.ScanFile(filePath);
                    return new ScanResult(aiScore, $"AI.{aiScore * 100:F}");
                }
            }
            return new ScanResult(0, "Safe");
        }
    }
}
