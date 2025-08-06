// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Azure.Monitor.OpenTelemetry.Exporter.Internals.Diagnostics
{
    /// <summary>
    /// DNS cache that provides immediate IP resolution results while refreshing in the background.
    /// Critical for ADF Pillar 2 logging to show actual resolved IPs for AMPLS troubleshooting.
    /// </summary>
    internal sealed class DiagnosticsDnsCache : IDisposable
    {
        private readonly ConcurrentDictionary<string, CachedDnsEntry> _cache = new();
        private readonly Timer _refreshTimer;
        private readonly SemaphoreSlim _refreshSemaphore = new(1, 1);
        private readonly TimeSpan _cacheExpiry = TimeSpan.FromSeconds(60);
        private readonly TimeSpan _refreshInterval = TimeSpan.FromSeconds(30);
        private bool _disposed;

        public DiagnosticsDnsCache()
        {
            // Background refresh every 30 seconds
            _refreshTimer = new Timer(BackgroundRefreshCallback, null, _refreshInterval, _refreshInterval);
        }

        /// <summary>
        /// Gets the cached IP address for a hostname, or "Unknown" if not yet resolved.
        /// This method never blocks - it always returns immediately.
        /// </summary>
        public string GetResolvedIP(string endpoint)
        {
            if (string.IsNullOrEmpty(endpoint))
                return "Unknown";

            try
            {
                var hostname = ExtractHostname(endpoint);
                if (string.IsNullOrEmpty(hostname))
                    return "Unknown";

                if (_cache.TryGetValue(hostname, out var entry))
                {
                    // Return cached result even if expired - diagnostic info is better than none
                    return entry.ResolvedIP;
                }

                // Not in cache - trigger async resolution but return immediately
                _ = Task.Run(() => ResolveAndCacheAsync(hostname));
                return "Resolving...";
            }
            catch
            {
                return "Unknown";
            }
        }

        /// <summary>
        /// Pre-warms the DNS cache for a hostname. Call this during initialization.
        /// </summary>
        public void PrewarmCache(string endpoint)
        {
            if (string.IsNullOrEmpty(endpoint))
                return;

            try
            {
                var hostname = ExtractHostname(endpoint);
                if (!string.IsNullOrEmpty(hostname))
                {
                    _ = Task.Run(() => ResolveAndCacheAsync(hostname));
                }
            }
            catch
            {
                // Ignore errors during prewarming
            }
        }

        private async Task ResolveAndCacheAsync(string hostname)
        {
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(hostname).ConfigureAwait(false);
                var firstIP = addresses?.Length > 0 ? addresses[0].ToString() : "Unknown";

                _cache.AddOrUpdate(hostname,
                    new CachedDnsEntry(firstIP, DateTime.UtcNow),
                    (key, oldEntry) => new CachedDnsEntry(firstIP, DateTime.UtcNow));
            }
            catch
            {
                // If resolution fails, cache "Unknown" to avoid repeated failures
                _cache.AddOrUpdate(hostname,
                    new CachedDnsEntry("Unknown", DateTime.UtcNow),
                    (key, oldEntry) => new CachedDnsEntry("Unknown", DateTime.UtcNow));
            }
        }

        private async void BackgroundRefreshCallback(object? state)
        {
            if (_disposed || !await _refreshSemaphore.WaitAsync(100).ConfigureAwait(false))
                return;

            try
            {
                var now = DateTime.UtcNow;
                var expiredEntries = new List<string>();

                // Find entries that need refreshing
                foreach (var kvp in _cache)
                {
                    if (now - kvp.Value.ResolvedAt > _cacheExpiry)
                    {
                        expiredEntries.Add(kvp.Key);
                    }
                }

                // Refresh expired entries
                var refreshTasks = new List<Task>();
                foreach (var hostname in expiredEntries)
                {
                    refreshTasks.Add(ResolveAndCacheAsync(hostname));
                }

                if (refreshTasks.Count > 0)
                {
                    await Task.WhenAll(refreshTasks).ConfigureAwait(false);
                }
            }
            catch
            {
                // Ignore errors in background refresh
            }
            finally
            {
                _refreshSemaphore.Release();
            }
        }

        private string ExtractHostname(string endpoint)
        {
            try
            {
                var uri = new Uri(endpoint);
                return uri.Host;
            }
            catch
            {
                return string.Empty;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _refreshTimer?.Dispose();
            _refreshSemaphore?.Dispose();
        }

        private sealed class CachedDnsEntry
        {
            public string ResolvedIP { get; }
            public DateTime ResolvedAt { get; }

            public CachedDnsEntry(string resolvedIP, DateTime resolvedAt)
            {
                ResolvedIP = resolvedIP;
                ResolvedAt = resolvedAt;
            }
        }
    }
}
