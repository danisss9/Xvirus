using System;
using System.Collections.Generic;

namespace Xvirus
{
    /// <summary>
    /// A compact, immutable Bloom filter used as a fast negative pre-filter in front of the exact
    /// MD5 hash sets. <see cref="MightContain"/> returning <c>false</c> guarantees the item is absent;
    /// returning <c>true</c> means it is <em>probably</em> present and must be confirmed against the
    /// backing <see cref="System.Collections.Generic.HashSet{T}"/>. Because every positive is confirmed
    /// against the exact set, the filter never changes a scan result — it only lets the common
    /// "clean file" path skip the hash-set lookup entirely.
    ///
    /// Items are expected to be normalized (upper-case, dash-free) MD5 hex strings, but any string is
    /// accepted. The instance is built once and is safe for concurrent reads thereafter.
    /// </summary>
    public sealed class BloomFilter
    {
        // ln(2) and ln(2)^2, used when sizing the bit array.
        private const double Ln2 = 0.6931471805599453;
        private const double Ln2Squared = 0.4804530139182014;

        private readonly ulong[] _bits;  // bit array packed into 64-bit words
        private readonly long _bitCount;  // total number of bits (m)
        private readonly int _hashCount;  // number of hash functions (k)

        /// <summary>Number of items that were added to the filter.</summary>
        public int Count { get; }

        /// <summary>Number of hash functions probed per lookup (k).</summary>
        public int HashCount => _hashCount;

        /// <summary>Size of the underlying bit array in bits (m).</summary>
        public long BitCount => _bitCount;

        private BloomFilter(long bitCount, int hashCount, int count)
        {
            _bitCount = bitCount;
            _hashCount = hashCount;
            Count = count;
            _bits = new ulong[(int)((bitCount + 63) / 64)];
        }

        /// <summary>
        /// Builds a Bloom filter sized for <paramref name="items"/> at the target
        /// <paramref name="falsePositiveRate"/>, then adds every entry.
        /// </summary>
        /// <param name="items">The items to insert (e.g. an MD5 hash set).</param>
        /// <param name="falsePositiveRate">
        /// Desired probability that <see cref="MightContain"/> reports a false positive at capacity.
        /// Smaller values use more memory and probe more bits. The default (1%) is a good pre-filter
        /// balance: ~99% of unknown files skip the exact lookup while the bit array stays small.
        /// </param>
        public static BloomFilter Build(ICollection<string> items, double falsePositiveRate = 0.01)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (falsePositiveRate <= 0 || falsePositiveRate >= 1)
                throw new ArgumentOutOfRangeException(nameof(falsePositiveRate), "Must be in the open interval (0, 1).");

            int n = Math.Max(1, items.Count);

            // Optimal sizing: m = -n*ln(p) / (ln 2)^2 ; k = (m/n)*ln 2
            long m = (long)Math.Ceiling(-(n * Math.Log(falsePositiveRate)) / Ln2Squared);
            if (m < 1) m = 1;
            int k = Math.Max(1, (int)Math.Round((m / (double)n) * Ln2));

            var filter = new BloomFilter(m, k, items.Count);
            foreach (var item in items)
                filter.Add(item);
            return filter;
        }

        private void Add(string item)
        {
            (ulong h1, ulong h2) = Hash(item);
            for (int i = 0; i < _hashCount; i++)
            {
                long bit = (long)((h1 + (ulong)i * h2) % (ulong)_bitCount);
                _bits[bit >> 6] |= 1UL << (int)(bit & 63);
            }
        }

        /// <summary>
        /// Returns <c>false</c> if <paramref name="item"/> is definitely not in the set, or <c>true</c>
        /// if it is probably present (callers must then confirm against the exact backing set).
        /// </summary>
        public bool MightContain(string item)
        {
            (ulong h1, ulong h2) = Hash(item);
            for (int i = 0; i < _hashCount; i++)
            {
                long bit = (long)((h1 + (ulong)i * h2) % (ulong)_bitCount);
                if ((_bits[bit >> 6] & (1UL << (int)(bit & 63))) == 0)
                    return false;
            }
            return true;
        }

        // Two independent 64-bit FNV-1a hashes (different offset bases) combined via the
        // Kirsch-Mitzenmacher technique to synthesize k indices. MD5 hex input is already
        // uniformly distributed, so this gives a near-ideal bit spread.
        private static (ulong, ulong) Hash(string item)
        {
            const ulong fnvPrime = 1099511628211UL;
            ulong h1 = 14695981039346656037UL;
            ulong h2 = 1099511628211UL;
            for (int i = 0; i < item.Length; i++)
            {
                char c = item[i];
                h1 = (h1 ^ (byte)c) * fnvPrime;
                h1 = (h1 ^ (byte)(c >> 8)) * fnvPrime;
                h2 = (h2 ^ (byte)c) * fnvPrime;
                h2 = (h2 ^ (byte)(c >> 8)) * fnvPrime;
            }
            // h2 must be non-zero (and odd helps spread) so the i*h2 stride visits distinct bits.
            h2 |= 1UL;
            return (h1, h2);
        }
    }
}
