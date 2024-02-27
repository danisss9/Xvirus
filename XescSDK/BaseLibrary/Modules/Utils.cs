using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;

namespace XescSDK
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
            return "4.2.0.0";
        }
    }
}
