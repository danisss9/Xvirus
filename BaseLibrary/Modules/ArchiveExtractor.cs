using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using Xvirus.Model;

namespace Xvirus
{
    /// <summary>
    /// Extracts archives to a temporary directory for recursive scanning, with depth, size, and
    /// path-traversal (zip-slip) guards. Only zip-based archives are supported via the built-in
    /// <see cref="System.IO.Compression"/>; rar/7z support is pending a license check of
    /// SharpCompress (see todo #6).
    /// </summary>
    internal static class ArchiveExtractor
    {
        public const int DefaultMaxDepth = 3;
        public const long DefaultMaxTotalSize = 104_857_600; // 100 MB
        public const int DefaultMaxFileCount = 1000;

        // Extensions that are conventionally zip containers holding scannable binaries/scripts.
        // Office Open XML (.docx/.xlsx/...) is intentionally excluded — its inner XML parts are
        // not executables and would only generate noise.
        private static readonly string[] ZipExtensions =
        {
            ".zip", ".jar", ".war", ".ear", ".apk", ".xpi", ".nupkg", ".whl", ".egg"
        };

        /// <summary>
        /// Cheap extension-based check used by <see cref="Scanner"/> to decide whether to attempt
        /// archive extraction. The actual zip signature is re-verified when the archive is opened.
        /// </summary>
        internal static bool IsArchive(string filePath)
        {
            var ext = Path.GetExtension(filePath);
            if (string.IsNullOrEmpty(ext))
                return false;
            return Array.IndexOf(ZipExtensions, ext.ToLowerInvariant()) >= 0;
        }

        internal readonly struct ArchiveLimits
        {
            public readonly int MaxDepth;
            public readonly long MaxTotalSize;
            public readonly int MaxFileCount;

            internal ArchiveLimits(int maxDepth, long maxTotalSize, int maxFileCount)
            {
                MaxDepth = maxDepth;
                MaxTotalSize = maxTotalSize;
                MaxFileCount = maxFileCount;
            }
        }

        /// <summary>
        /// Thrown when an archive exceeds the configured depth/size/count guards and is therefore
        /// treated as a potential zip-bomb.
        /// </summary>
        internal sealed class ArchiveBombException : Exception
        {
            internal ArchiveBombException(string message) : base(message) { }
        }

        /// <summary>
        /// Extracts every entry of <paramref name="archivePath"/> under <paramref name="destDir"/>,
        /// enforcing the supplied <paramref name="limits"/>. Returns the list of extracted file
        /// paths (directories are not included). Entries are rejected if they attempt path
        /// traversal (zip-slip), exceed the remaining size/file-count budget, or use absolute or
        /// rooted paths.
        /// </summary>
        /// <remarks>
        /// rar/7z archives are not handled here — <see cref="IsArchive"/> only advertises zip-based
        /// containers, so reaching this method with a non-zip file is a bug. We still guard with a
        /// signature check and silently return an empty list for anything that is not a valid zip.
        /// </remarks>
        internal static List<string> Extract(string archivePath, string destDir, in ArchiveLimits limits, CancellationToken ct)
        {
            var extracted = new List<string>();
            long totalSize = 0;

            using var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!HasZipSignature(fs))
                return extracted;

            using var zip = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);
            foreach (var entry in zip.Entries)
            {
                ct.ThrowIfCancellationRequested();

                if (extracted.Count >= limits.MaxFileCount)
                    throw new ArchiveBombException($"Archive exceeded max file count of {limits.MaxFileCount}.");

                var safeName = SanitizeEntryName(entry.Name, entry.FullName);
                if (safeName == null)
                    continue; // directory entry or otherwise skipped

                var destPath = Path.Combine(destDir, safeName);
                var fullDest = Path.GetFullPath(destPath);
                var fullDestDir = Path.GetFullPath(destDir);
                if (!fullDest.StartsWith(fullDestDir, StringComparison.Ordinal))
                    throw new ArchiveBombException($"Archive entry '{entry.FullName}' attempts path traversal.");

                // Directory entries have Length 0 and an empty/relative name; create them and skip.
                if (entry.Length == 0 && (entry.Name == string.Empty || entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')))
                {
                    Directory.CreateDirectory(fullDest);
                    continue;
                }

                if (entry.Length > 0 && totalSize + entry.Length > limits.MaxTotalSize)
                    throw new ArchiveBombException($"Archive exceeded max total extracted size of {limits.MaxTotalSize} bytes.");

                var dir = Path.GetDirectoryName(fullDest);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                using var es = entry.Open();
                using var ofs = new FileStream(fullDest, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                // Copy in chunks so we can enforce the size budget even if entry.Length lies.
                var buffer = new byte[64 * 1024];
                int read;
                while ((read = es.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    totalSize += read;
                    if (totalSize > limits.MaxTotalSize)
                        throw new ArchiveBombException($"Archive exceeded max total extracted size of {limits.MaxTotalSize} bytes.");
                    ofs.Write(buffer, 0, read);
                }

                extracted.Add(fullDest);
            }

            return extracted;
        }

        private static bool HasZipSignature(Stream s)
        {
            // Local file header signature: 0x04034b50 ("PK\x03\x04").
            // Empty/spanned zips use 0x06054b50 / 0x07064b50 which we don't bother scanning.
            if (s.Length < 4)
                return false;
            var pos = s.Position;
            try
            {
                s.Position = 0;
                int b0 = s.ReadByte();
                int b1 = s.ReadByte();
                int b2 = s.ReadByte();
                int b3 = s.ReadByte();
                return b0 == 0x50 && b1 == 0x4B && b2 == 0x03 && b3 == 0x04;
            }
            finally
            {
                s.Position = pos;
            }
        }

        /// <summary>
        /// Returns a sanitized, relative entry name suitable for <see cref="Path.Combine"/>, or
        /// <c>null</c> if the entry should be skipped (directory or empty).
        /// </summary>
        private static string? SanitizeEntryName(string name, string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
                return null;

            // Use the forward-slash-separated FullName so nested directory structure is preserved,
            // then normalize separators.
            var normalized = fullName.Replace('/', Path.DirectorySeparatorChar);
            if (normalized.Contains("..", StringComparison.Ordinal))
                return null; // traversal handled by the caller's full-path check, but refuse outright

            // Reject absolute / rooted paths (Windows drive letters, UNC, leading separator).
            if (Path.IsPathRooted(normalized))
                return null;

            var trimmed = normalized.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrEmpty(trimmed))
                return null;

            return trimmed;
        }
    }
}
