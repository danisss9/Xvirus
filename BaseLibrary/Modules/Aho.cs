using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace AhoCorasick.Net
{
    public class AhoCorasickTree
    {
        private readonly AhoCorasickTreeNode _rootNode;

        public AhoCorasickTree(string[] keywords)
        {
            if (keywords == null) throw new ArgumentNullException("keywords");
            if (keywords.Length == 0) throw new ArgumentException("should contain keywords");

            _rootNode = new AhoCorasickTreeNode();

            var length = keywords.Length;
            for (var i = 0; i < length; i++)
            {
                AddPatternToTree(keywords[i]);
            }

            SetFailures();
        }

        public bool Contains(string text)
        {
            var currentNode = _rootNode;
            var length = text.Length;
            for (var i = 0; i < length; i++)
            {
                currentNode = Advance(currentNode, text[i], out var finished);
                if (finished)
                    return true;
            }

            return false;
        }

        public bool Contains(FileStream stream)
        {
            var currentNode = _rootNode;
            // Reusable 64 KB buffer — the automaton walks hex characters (2 per byte), so this
            // feeds 131072 hex chars per read instead of formatting one byte at a time (which
            // allocated a string and a byte[] for every single byte in the file). The stream is
            // owned by the caller, so it is read directly and left open.
            var buffer = new byte[65536];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (var i = 0; i < read; i++)
                {
                    ByteToHexChars(buffer[i], out var hi, out var lo);
                    currentNode = Advance(currentNode, hi, out var finishedHi);
                    if (finishedHi) return true;
                    currentNode = Advance(currentNode, lo, out var finishedLo);
                    if (finishedLo) return true;
                }
            }

            return false;
        }

        public IEnumerable<string> Search(FileStream stream, CancellationToken ct = default)
        {
            var currentNode = _rootNode;
            // The stream is owned by the caller, so it is read directly (64 KB at a time) and left open.
            var buffer = new byte[65536];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();

                for (var i = 0; i < read; i++)
                {
                    ByteToHexChars(buffer[i], out var hi, out var lo);
                    currentNode = Advance(currentNode, hi, out var finishedHi);
                    if (finishedHi)
                        foreach (var result in currentNode.Results)
                            yield return result;
                    currentNode = Advance(currentNode, lo, out var finishedLo);
                    if (finishedLo)
                        foreach (var result in currentNode.Results)
                            yield return result;
                }
            }
        }

        /// <summary>
        /// Walks the automaton one character from <paramref name="current"/>. When the
        /// destination node is a finished pattern, <paramref name="finished"/> is set to
        /// <c>true</c> and the node is returned so the caller can read its <c>Results</c>.
        /// Shared by <see cref="Contains(string)"/>, <see cref="Contains(FileStream)"/>,
        /// and <see cref="Search(FileStream, CancellationToken)"/>.
        /// </summary>
        private AhoCorasickTreeNode Advance(AhoCorasickTreeNode current, char c, out bool finished)
        {
            while (true)
            {
                var node = current.GetNode(c);
                if (node != null)
                {
                    finished = node.IsFinished;
                    return node;
                }

                // No transition for c. Unwind the failure chain and retry — crucially this must
                // include retrying at the root, otherwise a pattern that begins at this very
                // character is dropped (e.g. patterns {AB, CD} would miss "CD" in "ABCD"). Only
                // give up (consume c, stay at root) once we are at the root with no child for c.
                if (current == _rootNode)
                {
                    finished = false;
                    return _rootNode;
                }

                current = current.Failure;
            }
        }

        /// <summary>
        /// Converts a byte to two uppercase hex characters without allocating a string.
        /// Replaces <c>BitConverter.ToString(reader.ReadBytes(1))</c> which allocated a
        /// <c>byte[]</c> and a <c>string</c> per byte.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ByteToHexChars(byte b, out char hi, out char lo)
        {
            hi = HexChar(b >> 4);
            lo = HexChar(b & 0x0F);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static char HexChar(int nibble)
        {
            return (char)(nibble < 10 ? '0' + nibble : 'A' + nibble - 10);
        }

        private void AddPatternToTree(string pattern)
        {
            var latestNode = _rootNode;
            var length = pattern.Length;
            for (var i = 0; i < length; i++)
            {
                latestNode = latestNode.GetNode(pattern[i])
                             ?? latestNode.AddNode(pattern[i]);
            }

            latestNode.IsFinished = true;
            latestNode.Results.Add(pattern);
        }

        private void SetFailures()
        {
            _rootNode.Failure = _rootNode;
            var queue = new Queue<AhoCorasickTreeNode>();
            queue.Enqueue(_rootNode);

            while (queue.Count > 0)
            {
                var currentNode = queue.Dequeue();
                foreach (var node in currentNode.Nodes)
                {
                    queue.Enqueue(node);
                }

                if (currentNode == _rootNode)
                {
                    continue;
                }

                var failure = currentNode.Parent.Failure;
                var key = currentNode.Key;
                while (failure.GetNode(key) == null && failure != _rootNode)
                {
                    failure = failure.Failure;
                }

                failure = failure.GetNode(key);
                if (failure == null || failure == currentNode)
                {
                    failure = _rootNode;
                }

                currentNode.Failure = failure;
                if (!currentNode.IsFinished)
                {
                    currentNode.IsFinished = failure.IsFinished;
                }

                if (currentNode.IsFinished && failure.IsFinished)
                {
                    currentNode.Results.AddRange(failure.Results);
                }
            }
        }

        private class AhoCorasickTreeNode
        {
            public readonly AhoCorasickTreeNode Parent;
            public AhoCorasickTreeNode Failure;
            public bool IsFinished;
            public List<string> Results;
            public readonly char Key;

            private int[] _buckets;
            private int _count;
            private Entry[] _entries;

            internal AhoCorasickTreeNode()
                : this(null, ' ')
            {
            }

            private AhoCorasickTreeNode(AhoCorasickTreeNode parent, char key)
            {
                Key = key;
                Parent = parent;

                _buckets = new int[0];
                _entries = new Entry[0];
                Results = new List<string>();
            }

            public AhoCorasickTreeNode[] Nodes
            {
                get { return _entries.Select(x => x.Value).ToArray(); }
            }

            public AhoCorasickTreeNode AddNode(char key)
            {
                var node = new AhoCorasickTreeNode(this, key);

                var newSize = _count + 1;
                Resize(newSize);

                var targetBucket = key % newSize;
                _entries[_count].Key = key;
                _entries[_count].Value = node;
                _entries[_count].Next = _buckets[targetBucket];
                _buckets[targetBucket] = _count;
                _count++;

                return node;
            }

            public AhoCorasickTreeNode GetNode(char key)
            {
                if (_count == 0) return null;

                var bucketIndex = key % _count;
                for (var i = _buckets[bucketIndex]; i >= 0; i = _entries[i].Next)
                {
                    if (_entries[i].Key == key)
                    {
                        return _entries[i].Value;
                    }
                }

                return null;
            }

            private void Resize(int newSize)
            {
                var newBuckets = new int[newSize];
                for (var i = 0; i < newSize; i++)
                {
                    newBuckets[i] = -1;
                }

                var newEntries = new Entry[newSize];
                Array.Copy(_entries, 0, newEntries, 0, _entries.Length);

                // rebalancing buckets for existing entries
                for (var i = 0; i < _entries.Length; i++)
                {
                    var bucket = newEntries[i].Key % newSize;
                    newEntries[i].Next = newBuckets[bucket];
                    newBuckets[bucket] = i;
                }

                _buckets = newBuckets;
                _entries = newEntries;
            }

            private struct Entry
            {
                public char Key;
                public int Next;
                public AhoCorasickTreeNode Value;
            }
        }
    }
}