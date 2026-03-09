using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var cert = new X509Certificate2(filePath);
                    return cert.GetNameInfo(X509NameType.SimpleName, false);
                }

                // On Linux, X509Certificate2(filePath) cannot read Authenticode from PE files.
                // Manually parse the PE Certificate Table to extract the PKCS#7 blob.
                byte[]? pkcs7 = ExtractAuthenticodePkcs7(filePath);
                if (pkcs7 == null) return null;

                var collection = new X509Certificate2Collection();
                collection.Import(pkcs7);
                if (collection.Count == 0) return null;

                // Prefer the leaf (code-signing) cert — the one that is not a CA
                var leaf = collection.Cast<X509Certificate2>()
                    .FirstOrDefault(c => !c.Extensions
                        .OfType<X509BasicConstraintsExtension>()
                        .Any(e => e.CertificateAuthority))
                    ?? collection[0];

                return leaf.GetNameInfo(X509NameType.SimpleName, false);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static byte[]? ExtractAuthenticodePkcs7(string filePath)
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var br = new BinaryReader(fs);

            // Check MZ header
            if (br.ReadUInt16() != 0x5A4D) return null;

            // Read PE header offset from DOS stub at 0x3C
            fs.Seek(0x3C, SeekOrigin.Begin);
            uint peOffset = br.ReadUInt32();

            // Check PE signature
            fs.Seek(peOffset, SeekOrigin.Begin);
            if (br.ReadUInt32() != 0x00004550) return null; // "PE\0\0"

            // Skip COFF header (20 bytes) to reach optional header magic
            fs.Seek(peOffset + 24, SeekOrigin.Begin);
            ushort magic = br.ReadUInt16();

            // Security data directory is entry #4 (each entry is 8 bytes: 4 VirtualAddress + 4 Size).
            // Its file offset within the optional header depends on whether it's PE32 or PE32+.
            // PE32  (0x10B): data directories start at optional header offset 96  → entry 4 at offset 128
            // PE32+ (0x20B): data directories start at optional header offset 112 → entry 4 at offset 144
            long secDirFileOffset = magic == 0x20B
                ? peOffset + 24 + 144
                : peOffset + 24 + 128;

            fs.Seek(secDirFileOffset, SeekOrigin.Begin);
            // For the security directory, VirtualAddress is actually a file offset (not an RVA)
            uint certTableOffset = br.ReadUInt32();
            uint certTableSize = br.ReadUInt32();

            if (certTableOffset == 0 || certTableSize < 8) return null;

            fs.Seek(certTableOffset, SeekOrigin.Begin);
            uint dwLength = br.ReadUInt32();
            /*wRevision*/
            br.ReadUInt16();
            ushort wCertType = br.ReadUInt16();

            // 0x0002 = WIN_CERT_TYPE_PKCS_SIGNED_DATA
            if (wCertType != 0x0002) return null;

            int dataLength = (int)(dwLength - 8);
            return dataLength > 0 ? br.ReadBytes(dataLength) : null;
        }

        public static FileStream ReadFile(string filePath, long fileSize)
        {
            return fileSize >= 16777216 // 16MBs
                ? new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 16777216) // 16MBs
                : new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
    }
}
