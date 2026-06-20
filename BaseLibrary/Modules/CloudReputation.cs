using System;
using System.Net.Http;
using System.Text.Json;
using BaseLibrary.Serializers;

namespace Xvirus
{
    /// <summary>
    /// Cloud-assisted hash reputation lookup against the Xvirus cloud. Fail-open: any
    /// error (offline, endpoint unavailable, timeout) returns <c>null</c> so local
    /// scanning is never blocked by the network.
    /// </summary>
    public static class CloudReputation
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(4) };

        /// <summary>
        /// Returns <c>true</c> if the cloud flags the MD5 hash as malware, <c>false</c>
        /// if explicitly clean, or <c>null</c> when unknown / the lookup failed.
        /// </summary>
        public static bool? CheckHash(string md5)
        {
            if (string.IsNullOrEmpty(md5)) return null;
            try
            {
                var url = $"https://cloud.xvirus.net/api/reputation?hash={md5}";
                var json = Http.GetStringAsync(url).GetAwaiter().GetResult();
                var info = JsonSerializer.Deserialize(json, SourceGenerationContextCamelCase.Default.CloudReputationInfo);
                return info?.Malware;
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
                return null;
            }
        }
    }
}
