using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;

namespace Xvirus
{
    public class Utils
    {
        public static string CurrentDir = AppContext.BaseDirectory;
  
        internal static string RelativeToFullPath(string fileName)
        {
            return Path.Combine(CurrentDir, fileName);
        }

        internal static string RelativeToFullPath(string relativePath, string fileName)
        {
            return Path.Combine(CurrentDir, relativePath, fileName);
        }

        internal static string? GetCertificateSubjectName(string filePath)
        {
            try
            {
                var cert = new X509Certificate2(filePath);
                return cert.GetNameInfo(X509NameType.SimpleName, false);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static string GetVersion()
        {
            return "5.0.0.0";   
        }
    
        public static FileStream ReadFile(string filePath, long fileSize)
        {
            return fileSize >= 16777216 // 16MBs
                ? new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 16777216) // 16MBs
                : new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
    }
}
