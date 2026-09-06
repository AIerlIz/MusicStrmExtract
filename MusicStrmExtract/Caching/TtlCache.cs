using System;
using System.Collections.Generic;

namespace MusicStrmExtract.Caching
{
    /// <summary>
    /// Bounded TTL cache that expires entries lazily in insertion order.
    /// No access walks the full cache; capacity overflow evicts only the oldest entry.
    /// </summary>
    public sealed class TtlCache<TValue>
    {
        private readonly object _gate = new object();
        private readonly TimeSpan _ttl;
        private readonly int _maxEntries;
        private readonly Dictionary<string, Node> _map;
        private readonly Func<DateTime> _clock;
        private Node? _head;
        private Node? _tail;

        public TtlCache(TimeSpan ttl, int maxEntries, IEqualityComparer<string>? keyComparer = null)
            : this(ttl, maxEntries, () => DateTime.UtcNow, keyComparer)
        {
        }

        internal TtlCache(TimeSpan ttl, int maxEntries, Func<DateTime> clock, IEqualityComparer<string>? keyComparer = null)
        {
            if (ttl <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(ttl));
            }

            if (maxEntries <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxEntries));
            }

            _ttl = ttl;
            _maxEntries = maxEntries;
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _map = new Dictionary<string, Node>(keyComparer ?? StringComparer.Ordinal);
        }

        public int Count
        {
            get
            {
                lock (_gate)
                {
                    return _map.Count;
                }
            }
        }

        public bool TryGet(string key, out TValue value)
        {
            if (key is null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            lock (_gate)
            {
                var now = _clock();
                ExpireOldest(now);

                if (_map.TryGetValue(key, out var node))
                {
                    if (now - node.CreatedUtc >= _ttl)
                    {
                        RemoveNode(node);
                        value = default!;
                        return false;
                    }

                    value = node.Value;
                    return true;
                }

                value = default!;
                return false;
            }
        }

        public void Set(string key, TValue value)
        {
            if (key is null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            lock (_gate)
            {
                var now = _clock();
                ExpireOldest(now);

                if (_map.TryGetValue(key, out var existing))
                {
                    RemoveNode(existing);
                }

                var node = new Node(key, value, now);
                _map.Add(key, node);
                AddTail(node);
                EvictWhileOverCapacity();
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _map.Clear();
                _head = null;
                _tail = null;
            }
        }

        private void ExpireOldest(DateTime now)
        {
            while (_head != null && now - _head.CreatedUtc >= _ttl)
            {
                RemoveNode(_head);
            }
        }

        private void EvictWhileOverCapacity()
        {
            while (_head != null && _map.Count > _maxEntries)
            {
                RemoveNode(_head);
            }
        }

        private void AddTail(Node node)
        {
            if (_tail is null)
            {
                _head = node;
                _tail = node;
                return;
            }

            _tail.Next = node;
            node.Previous = _tail;
            _tail = node;
        }

        private void RemoveNode(Node node)
        {
            _map.Remove(node.Key);

            if (node.Previous is null)
            {
                _head = node.Next;
            }
            else
            {
                node.Previous.Next = node.Next;
            }

            if (node.Next is null)
            {
                _tail = node.Previous;
            }
            else
            {
                node.Next.Previous = node.Previous;
            }

            node.Previous = null;
            node.Next = null;
        }

        private sealed class Node
        {
            public Node(string key, TValue value, DateTime createdUtc)
            {
                Key = key;
                Value = value;
                CreatedUtc = createdUtc;
            }

            public string Key { get; }

            public TValue Value { get; }

            public DateTime CreatedUtc { get; }

            public Node? Previous { get; set; }

            public Node? Next { get; set; }
        }
    }
}
