using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Xvirus
{
    /// <summary>
    /// Verifies Authenticode signatures with the Windows trust provider (WinVerifyTrust), covering
    /// both signatures embedded in the file and signatures provided by Windows security catalogs
    /// (how most files shipped with Windows are signed). Only blittable P/Invoke types are used so
    /// the code is NativeAOT-safe.
    /// </summary>
    internal static class SignatureVerifier
    {
        /// <summary>
        /// Returns true when the file has a valid, chain-trusted signature.
        /// </summary>
        /// <param name="hasEmbeddedSignature">The file carries an embedded Authenticode certificate.</param>
        /// <param name="checkCatalog">Look the file up in the Windows security catalogs when it has no embedded signature.</param>
        public static bool IsTrusted(string filePath, bool hasEmbeddedSignature, bool checkCatalog)
        {
            // WinVerifyTrust is Windows-only. Other platforms keep the previous behaviour, where an
            // embedded certificate alone is treated as trusted.
            if (!OperatingSystem.IsWindows())
                return hasEmbeddedSignature;

            try
            {
                if (hasEmbeddedSignature)
                    return VerifyEmbedded(filePath);

                return checkCatalog && VerifyCatalog(filePath);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool VerifyEmbedded(string filePath)
        {
            var pPath = Marshal.StringToHGlobalUni(filePath);
            var pFileInfo = IntPtr.Zero;
            try
            {
                var fileInfo = new WINTRUST_FILE_INFO
                {
                    cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
                    pcwszFilePath = pPath,
                };
                pFileInfo = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
                Marshal.StructureToPtr(fileInfo, pFileInfo, false);
                return Verify(WTD_CHOICE_FILE, pFileInfo);
            }
            finally
            {
                if (pFileInfo != IntPtr.Zero) Marshal.FreeHGlobal(pFileInfo);
                Marshal.FreeHGlobal(pPath);
            }
        }

        // Windows 10 and later index their system catalogs by SHA-256. Older SHA-1-only catalogs
        // are not checked, which keeps the lookup to a single pass over the file.
        private static bool VerifyCatalog(string filePath) => VerifyCatalog(filePath, "SHA256");

        private static bool VerifyCatalog(string filePath, string? hashAlgorithm)
        {
            var pAlgorithm = hashAlgorithm == null ? IntPtr.Zero : Marshal.StringToHGlobalUni(hashAlgorithm);
            IntPtr hCatAdmin = IntPtr.Zero, hCatInfo = IntPtr.Zero, pHash = IntPtr.Zero;
            IntPtr pCatInfo = IntPtr.Zero, pTag = IntPtr.Zero, pPath = IntPtr.Zero, pCatalog = IntPtr.Zero;
            try
            {
                if (CryptCATAdminAcquireContext2(out hCatAdmin, IntPtr.Zero, pAlgorithm, IntPtr.Zero, 0) == 0)
                    return false;

                using var file = File.OpenHandle(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                uint cbHash = 0;
                CryptCATAdminCalcHashFromFileHandle2(hCatAdmin, file.DangerousGetHandle(), ref cbHash, IntPtr.Zero, 0);
                if (cbHash == 0)
                    return false;

                pHash = Marshal.AllocHGlobal((int)cbHash);
                if (CryptCATAdminCalcHashFromFileHandle2(hCatAdmin, file.DangerousGetHandle(), ref cbHash, pHash, 0) == 0)
                    return false;

                hCatInfo = CryptCATAdminEnumCatalogFromHash(hCatAdmin, pHash, cbHash, 0, IntPtr.Zero);
                if (hCatInfo == IntPtr.Zero)
                    return false;

                // CATALOG_INFO is { DWORD cbStruct; WCHAR wszCatalogFile[MAX_PATH]; }.
                const int catalogInfoSize = 4 + 260 * 2;
                pCatInfo = Marshal.AllocHGlobal(catalogInfoSize);
                Marshal.WriteInt32(pCatInfo, catalogInfoSize);
                if (CryptCATCatalogInfoFromContext(hCatInfo, pCatInfo, 0) == 0)
                    return false;
                var catalogFile = Marshal.PtrToStringUni(pCatInfo + 4);

                var hash = new byte[cbHash];
                Marshal.Copy(pHash, hash, 0, (int)cbHash);
                pTag = Marshal.StringToHGlobalUni(Convert.ToHexString(hash));
                pPath = Marshal.StringToHGlobalUni(filePath);
                var pCatalogFile = Marshal.StringToHGlobalUni(catalogFile);
                try
                {
                    var catalogInfo = new WINTRUST_CATALOG_INFO
                    {
                        cbStruct = (uint)Marshal.SizeOf<WINTRUST_CATALOG_INFO>(),
                        pcwszCatalogFilePath = pCatalogFile,
                        pcwszMemberTag = pTag,
                        pcwszMemberFilePath = pPath,
                        pbCalculatedFileHash = pHash,
                        cbCalculatedFileHash = cbHash,
                        hCatAdmin = hCatAdmin,
                    };
                    pCatalog = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_CATALOG_INFO>());
                    Marshal.StructureToPtr(catalogInfo, pCatalog, false);
                    return Verify(WTD_CHOICE_CATALOG, pCatalog);
                }
                finally
                {
                    Marshal.FreeHGlobal(pCatalogFile);
                }
            }
            finally
            {
                if (hCatInfo != IntPtr.Zero) CryptCATAdminReleaseCatalogContext(hCatAdmin, hCatInfo, 0);
                if (hCatAdmin != IntPtr.Zero) CryptCATAdminReleaseContext(hCatAdmin, 0);
                if (pCatalog != IntPtr.Zero) Marshal.FreeHGlobal(pCatalog);
                if (pCatInfo != IntPtr.Zero) Marshal.FreeHGlobal(pCatInfo);
                if (pHash != IntPtr.Zero) Marshal.FreeHGlobal(pHash);
                if (pTag != IntPtr.Zero) Marshal.FreeHGlobal(pTag);
                if (pPath != IntPtr.Zero) Marshal.FreeHGlobal(pPath);
                if (pAlgorithm != IntPtr.Zero) Marshal.FreeHGlobal(pAlgorithm);
            }
        }

        private static bool Verify(uint unionChoice, IntPtr pUnion)
        {
            var data = new WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                dwUIChoice = WTD_UI_NONE,
                fdwRevocationChecks = WTD_REVOKE_NONE,
                dwUnionChoice = unionChoice,
                pUnion = pUnion,
                dwStateAction = WTD_STATEACTION_VERIFY,
                // Scans must work offline and never wait on the network.
                dwProvFlags = WTD_REVOCATION_CHECK_NONE | WTD_CACHE_ONLY_URL_RETRIEVAL,
            };
            var pData = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_DATA>());
            var action = WINTRUST_ACTION_GENERIC_VERIFY_V2;
            try
            {
                Marshal.StructureToPtr(data, pData, false);
                var status = WinVerifyTrust(new IntPtr(-1), ref action, pData);

                // Always release the provider state allocated by the verify call.
                data = Marshal.PtrToStructure<WINTRUST_DATA>(pData);
                data.dwStateAction = WTD_STATEACTION_CLOSE;
                Marshal.StructureToPtr(data, pData, false);
                WinVerifyTrust(new IntPtr(-1), ref action, pData);

                return status == 0;
            }
            finally
            {
                Marshal.FreeHGlobal(pData);
            }
        }

        private static Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        private const uint WTD_UI_NONE = 2;
        private const uint WTD_REVOKE_NONE = 0;
        private const uint WTD_CHOICE_FILE = 1;
        private const uint WTD_CHOICE_CATALOG = 2;
        private const uint WTD_STATEACTION_VERIFY = 1;
        private const uint WTD_STATEACTION_CLOSE = 2;
        private const uint WTD_REVOCATION_CHECK_NONE = 0x10;
        private const uint WTD_CACHE_ONLY_URL_RETRIEVAL = 0x1000;

        [StructLayout(LayoutKind.Sequential)]
        private struct WINTRUST_FILE_INFO
        {
            public uint cbStruct;
            public IntPtr pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WINTRUST_CATALOG_INFO
        {
            public uint cbStruct;
            public uint dwCatalogVersion;
            public IntPtr pcwszCatalogFilePath;
            public IntPtr pcwszMemberTag;
            public IntPtr pcwszMemberFilePath;
            public IntPtr hMemberFile;
            public IntPtr pbCalculatedFileHash;
            public uint cbCalculatedFileHash;
            public IntPtr pcCatalogContext;
            public IntPtr hCatAdmin;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WINTRUST_DATA
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pUnion;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
            public IntPtr pSignatureSettings;
        }

        [DllImport("wintrust.dll", ExactSpelling = true)]
        private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, IntPtr pWVTData);

        [DllImport("wintrust.dll", ExactSpelling = true)]
        private static extern int CryptCATAdminAcquireContext2(out IntPtr phCatAdmin, IntPtr pgSubsystem, IntPtr pwszHashAlgorithm, IntPtr pStrongHashPolicy, uint dwFlags);

        [DllImport("wintrust.dll", ExactSpelling = true)]
        private static extern int CryptCATAdminCalcHashFromFileHandle2(IntPtr hCatAdmin, IntPtr hFile, ref uint pcbHash, IntPtr pbHash, uint dwFlags);

        [DllImport("wintrust.dll", ExactSpelling = true)]
        private static extern IntPtr CryptCATAdminEnumCatalogFromHash(IntPtr hCatAdmin, IntPtr pbHash, uint cbHash, uint dwFlags, IntPtr phPrevCatInfo);

        [DllImport("wintrust.dll", ExactSpelling = true)]
        private static extern int CryptCATCatalogInfoFromContext(IntPtr hCatInfo, IntPtr psCatInfo, uint dwFlags);

        [DllImport("wintrust.dll", ExactSpelling = true)]
        private static extern int CryptCATAdminReleaseCatalogContext(IntPtr hCatAdmin, IntPtr hCatInfo, uint dwFlags);

        [DllImport("wintrust.dll", ExactSpelling = true)]
        private static extern int CryptCATAdminReleaseContext(IntPtr hCatAdmin, uint dwFlags);
    }
}
