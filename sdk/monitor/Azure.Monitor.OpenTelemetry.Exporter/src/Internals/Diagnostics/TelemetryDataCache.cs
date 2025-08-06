// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Azure.Monitor.OpenTelemetry.Exporter.Internals.Diagnostics
{
    internal static class TelemetryDataCache
    {
        private static readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
        private static readonly Timer _cleanupTimer;
        private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5); // Short TTL
        private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(2); // Frequent cleanup
        private const int MaxCacheSize = 1000; // Conservative size limit

        static TelemetryDataCache()
        {
            _cleanupTimer = new Timer(PerformMaintenance, null, CleanupInterval, CleanupInterval);
        }

        public static void Store(string id, object data)
        {
            var entry = new CacheEntry(data, DateTime.UtcNow.Add(DefaultTtl));
            _cache[id] = entry;
        }

        public static object? Retrieve(string id)
        {
            if (_cache.TryRemove(id, out var entry))
            {
                if (DateTime.UtcNow <= entry.ExpiresAt)
                {
                    return entry.Data;
                }
            }
            return null;
        }

        private static void PerformMaintenance(object? state)
        {
            var now = DateTime.UtcNow;
            var keysToRemove = new List<string>();

            // Remove expired entries
            foreach (var kvp in _cache)
            {
                if (now > kvp.Value.ExpiresAt)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }

            foreach (var key in keysToRemove)
            {
                _cache.TryRemove(key, out _);
            }

            // If still too large after cleanup, remove oldest entries
            if (_cache.Count > MaxCacheSize)
            {
                var entriesToRemove = _cache.Count - MaxCacheSize + 100; // Remove extra to avoid frequent cleanups
                var oldestEntries = _cache
                    .OrderBy(kvp => kvp.Value.ExpiresAt)
                    .Take(entriesToRemove)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in oldestEntries)
                {
                    _cache.TryRemove(key, out _);
                }
            }
        }

        private class CacheEntry
        {
            public object Data { get; }
            public DateTime ExpiresAt { get; }

            public CacheEntry(object data, DateTime expiresAt)
            {
                Data = data;
                ExpiresAt = expiresAt;
            }
        }
    }
}
