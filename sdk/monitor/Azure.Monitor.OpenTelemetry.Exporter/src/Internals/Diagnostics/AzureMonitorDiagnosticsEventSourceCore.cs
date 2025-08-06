// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Azure.Core;

namespace Azure.Monitor.OpenTelemetry.Exporter.Internals.Diagnostics
{
    /// <summary>
    /// EventSource for Azure Monitor core diagnostic events including agent lifecycle,
    /// configuration loading, and environment reporting.
    /// </summary>
    [EventSource(Name = EventSourceName)]
    internal sealed class AzureMonitorDiagnosticsEventSourceCore : EventSource
    {
        internal const string EventSourceName = "OpenTelemetry-AzureMonitor-Diagnostics-Core";

        internal static readonly AzureMonitorDiagnosticsEventSourceCore Log = new();
        private AzureMonitorDiagnosticsEventSourceCore() : base(EventSourceSettings.EtwSelfDescribingEventFormat)
        {
            AzureMonitorDiagnosticsEventListenerManager.Initialize();
        }

        #region Self-Diagnostics Startup Sequence Events

        /// <summary>
        /// Event #1: Configuration successfully loaded
        /// </summary>
        [Event(1, Level = EventLevel.Informational, Message = "Self-diagnostics configuration loaded from {0}")]
        public void SelfDiagnosticsConfigLoaded(string configSource, string configDirectory, string logDirectory,
            int fileSizeMB, string logLevel, string logFilters, int logLevelDurationSeconds, int threadId)
        {
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                WriteEvent(1, configSource, configDirectory, logDirectory, fileSizeMB, logLevel, logFilters, logLevelDurationSeconds, threadId);
            }
        }

        /// <summary>
        /// Event #2: Agent attachment status report
        /// </summary>
        [Event(2, Level = EventLevel.Informational, Message = "OpenTelemetry Agent attach status: {0}")]
        public void AttachStatusReport(string attachStatus, string attachMode, string backoffReason, string interopStatus, int threadId)
        {
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                WriteEvent(2, attachStatus, attachMode, backoffReason, interopStatus, threadId);
            }
        }

        /// <summary>
        /// Event #3: Connection endpoints DNS resolution report
        /// </summary>
        [Event(3, Level = EventLevel.Informational, Message = "Resolved IP addresses for Application Insights endpoints")]
        public void EndpointResolutionReport(string ingestionUrl, string ingestionIPs, string liveMetricsUrl, string liveMetricsIPs,
            string profilerUrl, string profilerIPs, string snapshotDebuggerUrl, string snapshotDebuggerIPs, int threadId)
        {
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                WriteEvent(3, ingestionUrl, ingestionIPs, liveMetricsUrl, liveMetricsIPs, profilerUrl, profilerIPs, snapshotDebuggerUrl, snapshotDebuggerIPs, threadId);
            }
        }

        /// <summary>
        /// Event #4: Environment and configuration details report
        /// </summary>
        [Event(4, Level = EventLevel.Informational, Message = "Reporting environment and configuration details")]
        public void EnvironmentDetailsReport(string osType, string osVersion, string machineName, int processId, string processName,
            string processPath, string workingDirectory, string agentDirectory, string instrumentationKey, string connectionString,
            string cloudProvider, string cloudPlatform, string cloudResourceId, string cloudRole, string cloudRoleInstance,
            double cpuUsagePercent, long memoryUsageMB, string samplingType, double samplingRate,
            string distributedTracingInbound, string distributedTracingOutbound, string customProcessors, bool sdkMetricsEnabled, int threadId)
        {
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                WriteEvent(4, osType, osVersion, machineName, processId, processName, processPath, workingDirectory, agentDirectory,
                    instrumentationKey, connectionString, cloudProvider, cloudPlatform, cloudResourceId, cloudRole, cloudRoleInstance,
                    cpuUsagePercent, memoryUsageMB, samplingType, samplingRate, distributedTracingInbound, distributedTracingOutbound,
                    customProcessors, sdkMetricsEnabled, threadId);
            }
        }

        /// <summary>
        /// Event #5: Self-diagnostics startup completion
        /// </summary>
        [Event(5, Level = EventLevel.Informational, Message = "Azure Monitor .NET OpenTelemetry Distro self-diagnostics started")]
        public void SelfDiagnosticsStarted(int threadId)
        {
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                WriteEvent(5, threadId);
            }
        }

        [Event(6, Message = "Offline storage is enabled. Retriable telemetry for Instrumentation Key '{0}' will be stored at: {1}", Level = EventLevel.Informational)]
        public void OfflineStorageEnabled(string instrumentationKey, string storageDirectory, int threadId)
        {
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                WriteEvent(6, instrumentationKey, storageDirectory, threadId);
            }
        }

        #endregion

        #region Agent Lifecycle Events

        /// <summary>
        /// Logs when the agent shuts down gracefully.
        /// </summary>
        [Event(10, Level = EventLevel.Informational, Message = "Azure Monitor agent shutting down")]
        public void AgentShutdown(int threadId)
        {
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                WriteEvent(10, threadId);
            }
        }

        #endregion

        #region Configuration Loading Events

        /// <summary>
        /// Logs configuration loading errors.
        /// </summary>
        [Event(20, Level = EventLevel.Error, Message = "Failed to load self-diagnostics configuration from {0}: {1}")]
        public void ConfigurationLoadFailed(string configPath, string errorMessage, int threadId)
        {
            if (IsEnabled(EventLevel.Error, EventKeywords.None))
            {
                WriteEvent(20, configPath, errorMessage, threadId);
            }
        }

        /// <summary>
        /// Logs configuration validation errors.
        /// </summary>
        [Event(21, Level = EventLevel.Error, Message = "Self-diagnostics configuration validation failed: {0}")]
        public void ConfigurationValidationFailed(string validationError, string configSource, int threadId)
        {
            if (IsEnabled(EventLevel.Error, EventKeywords.None))
            {
                WriteEvent(21, validationError, configSource, threadId);
            }
        }

        /// <summary>
        /// Logs when connection string is parsed and validated.
        /// </summary>
        [Event(22, Level = EventLevel.Informational, Message = "Connection string parsed successfully. Endpoint: {0}, Authentication: {1}")]
        public void ConnectionStringParsed(string endpoint, string authType, string instrumentationKey, int threadId)
        {
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                WriteEvent(22, endpoint, authType, instrumentationKey, threadId);
            }
        }

        /// <summary>
        /// Logs when self-diagnostics config file is missing.
        /// </summary>
        [Event(23, Level = EventLevel.Warning, Message = "Self-diagnostics config file not found at {0}. Attempting Profile API fallback")]
        public void ConfigFileMissing(string configPath, int threadId)
        {
            if (IsEnabled(EventLevel.Warning, EventKeywords.None))
            {
                WriteEvent(23, configPath, threadId);
            }
        }

        #endregion

        #region Connection and Network Events

        /// <summary>
        /// Logs DNS resolution failures.
        /// </summary>
        [Event(30, Level = EventLevel.Warning, Message = "DNS resolution failed for {0}: {1}")]
        public void DnsResolutionFailed(string hostname, string errorMessage, int threadId)
        {
            if (IsEnabled(EventLevel.Warning, EventKeywords.None))
            {
                WriteEvent(30, hostname, errorMessage, threadId);
            }
        }

        #endregion

        #region Profile API Events

        /// <summary>
        /// Logs successful Profile API calls.
        /// </summary>
        [Event(40, Level = EventLevel.Informational, Message = "Profile API call successful. Endpoint: {0}, Duration: {1}ms")]
        public void ProfileApiSuccess(string endpoint, int durationMs, string responseSize, int threadId)
        {
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                WriteEvent(40, endpoint, durationMs, responseSize, threadId);
            }
        }

        /// <summary>
        /// Logs Profile API call failures.
        /// </summary>
        [Event(41, Level = EventLevel.Warning, Message = "Profile API call failed. Endpoint: {0}, Error: {1}. Falling back to local configuration")]
        public void ProfileApiFailed(string endpoint, string errorMessage, int statusCode, int threadId)
        {
            if (IsEnabled(EventLevel.Warning, EventKeywords.None))
            {
                WriteEvent(41, endpoint, errorMessage, statusCode, threadId);
            }
        }

        #endregion

        #region Error and Exception Events

        /// <summary>
        /// Logs unhandled exceptions in the agent.
        /// </summary>
        [Event(50, Level = EventLevel.Error, Message = "Unhandled exception in agent: {0} - {1}")]
        public void UnhandledException(string exceptionType, string exceptionMessage, string stackTrace, int threadId)
        {
            if (IsEnabled(EventLevel.Error, EventKeywords.None))
            {
                WriteEvent(50, exceptionType, exceptionMessage, stackTrace, threadId);
            }
        }

        #endregion

        #region Non-Event Helper Methods

        /// <summary>
        /// Runs the complete self-diagnostics startup sequence (Events #1-7)
        /// </summary>
        [NonEvent]
        public void RunStartupSequence(SelfDiagnosticsConfig config, string? connectionString = null)
        {
            if (!IsEnabled(EventLevel.Informational, EventKeywords.None))
                return;

            try
            {
                // Event #1: Config loaded
                EmitConfigLoaded(config);

                // Event #2: Attach status
                EmitAttachStatus();

                // Event #3: Connection endpoints
                EmitConnectionEndpoints(connectionString ?? string.Empty);

                // Event #4: Environment details
                EmitEnvironmentDetails(connectionString ?? string.Empty);

                // Event #5: Started
                SelfDiagnosticsStarted(Environment.CurrentManagedThreadId);
            }
            catch (Exception ex)
            {
                UnhandledException(ex.GetType().Name, ex.Message, ex.StackTrace ?? string.Empty, Environment.CurrentManagedThreadId);
            }
        }

        [NonEvent]
        private void EmitConfigLoaded(SelfDiagnosticsConfig config)
        {
            var configSource = config.ConfigSource ?? "OTEL_DIAGNOSTICS.json";
            var configDirectory = config.ConfigDirectory ?? Directory.GetCurrentDirectory();
            var logDirectory = config.LogDirectory ?? ".";
            var fileSizeMB = config.FileSizeMB;
            var logLevel = config.LogLevel.ToString();
            var logFilters = SerializeLogFilters(config.LogFilters);
            var logLevelDurationSeconds = config.LogLevelDurationSeconds;

            SelfDiagnosticsConfigLoaded(configSource, configDirectory, logDirectory, fileSizeMB, logLevel, logFilters, logLevelDurationSeconds, Environment.CurrentManagedThreadId);
        }

        [NonEvent]
        private void EmitAttachStatus()
        {
            // For manual instrumentation, we're always attached
            var attachStatus = "Attached";
            var attachMode = "Manual instrumentation";
            var backoffReason = "Not applicable; manual attach does not backoff";
            var interopStatus = "Not applicable for manual attach";

            // TODO: In auto-instrumentation scenarios, check for:
            // - Conflicting ApplicationInsights.dll
            // - Interop settings
            // - Other backoff conditions

            AttachStatusReport(attachStatus, attachMode, backoffReason, interopStatus, Environment.CurrentManagedThreadId);
        }

        [NonEvent]
        private void EmitConnectionEndpoints(string? connectionString = null)
        {
            try
            {
                var endpoints = ParseConnectionStringEndpoints(connectionString ?? string.Empty);

                var ingestionIPs = ResolveHostname(endpoints.IngestionEndpoint);
                var liveMetricsIPs = ResolveHostname(endpoints.LiveMetricsEndpoint);
                var profilerIPs = ResolveHostname(endpoints.ProfilerEndpoint);
                var snapshotDebuggerIPs = ResolveHostname(endpoints.SnapshotDebuggerEndpoint);

                EndpointResolutionReport(
                    endpoints.IngestionEndpoint, string.Join(", ", ingestionIPs),
                    endpoints.LiveMetricsEndpoint, string.Join(", ", liveMetricsIPs),
                    endpoints.ProfilerEndpoint, string.Join(", ", profilerIPs),
                    endpoints.SnapshotDebuggerEndpoint, string.Join(", ", snapshotDebuggerIPs), Environment.CurrentManagedThreadId);
            }
            catch (Exception ex)
            {
                UnhandledException(ex.GetType().Name, ex.Message, ex.StackTrace ?? string.Empty, Environment.CurrentManagedThreadId);
            }
        }

        [NonEvent]
        private void EmitEnvironmentDetails(string? connectionString = null)
        {
            try
            {
                var process = Process.GetCurrentProcess();
                var osType = Environment.OSVersion.Platform.ToString().ToLower().Contains("win") ? "windows" : "linux";
                var osVersion = Environment.OSVersion.ToString();
                var machineName = Environment.MachineName;
                var processId = process.Id;
                var processName = process.ProcessName;
                var processPath = process.MainModule?.FileName ?? "Unknown";
                var workingDirectory = Directory.GetCurrentDirectory();
                var agentDirectory = AppContext.BaseDirectory;

                var connInfo = ParseConnectionString(connectionString ?? string.Empty);
                var instrumentationKey = MaskSensitiveData(connInfo.InstrumentationKey);
                var maskedConnectionString = MaskSensitiveData(connectionString ?? string.Empty);

                var cloudProvider = DetectCloudProvider();
                var cloudPlatform = DetectCloudPlatform();
                var cloudResourceId = GetCloudResourceId();
                var cloudRole = Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME") ?? processName;
                var cloudRoleInstance = Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID") ?? machineName;

                var cpuUsage = GetCpuUsagePercent();
                var memoryUsage = process.WorkingSet64 / (1024 * 1024); // Convert to MB

                // TODO: Get actual sampling and tracing configuration from OpenTelemetry
                var samplingType = "Fixed";
                var samplingRate = 1.0;
                var distributedTracingInbound = "Enabled";
                var distributedTracingOutbound = "Enabled";
                var customProcessors = GetCustomProcessors();
                var sdkMetricsEnabled = true; // We somehow detect the new enhanced statsbeats metrics, aka SDK metrics is enabled or not, then add to env report

                EnvironmentDetailsReport(osType, osVersion, machineName, processId, processName, processPath,
                    workingDirectory, agentDirectory, instrumentationKey, maskedConnectionString,
                    cloudProvider, cloudPlatform, cloudResourceId, cloudRole, cloudRoleInstance,
                    cpuUsage, memoryUsage, samplingType, samplingRate,
                    distributedTracingInbound, distributedTracingOutbound, customProcessors, sdkMetricsEnabled, Environment.CurrentManagedThreadId);
            }
            catch (Exception ex)
            {
                UnhandledException(ex.GetType().Name, ex.Message, ex.StackTrace ?? string.Empty, Environment.CurrentManagedThreadId);
            }
        }

        private string SerializeLogFilters(Dictionary<string, string> logFilters)
        {
            if (logFilters == null || logFilters.Count == 0)
                return "{}";

            try
            {
                return JsonSerializer.Serialize(logFilters);
            }
            catch
            {
                return "{}";
            }
        }

        private ConnectionEndpoints ParseConnectionStringEndpoints(string connectionString)
        {
            var defaults = new ConnectionEndpoints
            {
                IngestionEndpoint = "https://dc.services.visualstudio.com/",
                LiveMetricsEndpoint = "https://rt.services.visualstudio.com/",
                ProfilerEndpoint = "https://agent.azureserviceprofiler.net/",
                SnapshotDebuggerEndpoint = "https://agent.azuresnapshotdebugger.net/" // "https://snapshot.monitor.azure.com/"
            };

            if (string.IsNullOrEmpty(connectionString))
                return defaults;

            try
            {
                // Parse connection string for regional endpoints
                // Format: InstrumentationKey=key;IngestionEndpoint=https://region.dc.services.visualstudio.com/;LiveEndpoint=https://region.livediagnostics.monitor.azure.com/
                var parts = connectionString.Split(';');
                foreach (var part in parts)
                {
                    var kvp = part.Split('=');
                    if (kvp.Length == 2)
                    {
                        var key = kvp[0].Trim();
                        var value = kvp[1].Trim();

                        switch (key.ToLower())
                        {
                            case "ingestionendpoint":
                                if (value.EndsWith("/"))
                                    defaults.IngestionEndpoint = value;
                                else
                                    defaults.IngestionEndpoint = value + "/";
                                break;
                            case "liveendpoint":
                                defaults.LiveMetricsEndpoint = value.EndsWith("/") ? value : value + "/";
                                break;
                            case "profilerendpoint":
                                defaults.ProfilerEndpoint = value;
                                break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                UnhandledException("ConnectionStringParsing", ex.Message, ex.StackTrace ?? string.Empty, Environment.CurrentManagedThreadId);
            }

            return defaults;
        }

        private ConnectionInfo ParseConnectionString(string connectionString)
        {
            var info = new ConnectionInfo();

            if (string.IsNullOrEmpty(connectionString))
                return info;

            try
            {
                var parts = connectionString.Split(';');
                foreach (var part in parts)
                {
                    var kvp = part.Split('=');
                    if (kvp.Length == 2)
                    {
                        var key = kvp[0].Trim();
                        var value = kvp[1].Trim();

                        if (key.Equals("InstrumentationKey", StringComparison.OrdinalIgnoreCase))
                        {
                            info.InstrumentationKey = value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                UnhandledException("ConnectionStringParsing", ex.Message, ex.StackTrace ?? string.Empty, Environment.CurrentManagedThreadId);
            }

            return info;
        }

        private string[] ResolveHostname(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            {
                DnsResolutionFailed(url, "Invalid URL or missing hostname", Environment.CurrentManagedThreadId);
                return new[] { "Invalid URL" };
            }

            try
            {
                var addresses = Dns.GetHostAddresses(uri.Host);
                return addresses.Select(addr => addr.ToString()).ToArray();
            }
            catch (Exception ex)
            {
                DnsResolutionFailed(uri.Host, ex.Message, Environment.CurrentManagedThreadId);
                return new[] { "Resolution failed" };
            }
        }

        private string MaskSensitiveData(string data)
        {
            if (string.IsNullOrEmpty(data))
                return "Not configured";

            return "****-masked-for-security";
        }

        private string DetectCloudProvider()
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME")))
                return "azure";

            // TODO: Add detection for AWS, GCP
            return "unknown";
        }

        private string DetectCloudPlatform()
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME")))
            {
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME")))
                    return "azure_functions";
                return "azure_app_service";
            }

            // TODO: Add detection for AKS, VM, etc.
            return "unknown";
        }

        private string GetCloudResourceId()
        {
            var siteName = Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME");
            var resourceGroup = Environment.GetEnvironmentVariable("WEBSITE_RESOURCE_GROUP");
            var subscriptionId = Environment.GetEnvironmentVariable("WEBSITE_OWNER_NAME");

            if (!string.IsNullOrEmpty(siteName) && !string.IsNullOrEmpty(resourceGroup))
            {
                return $"/subscriptions/{subscriptionId}/resourceGroups/{resourceGroup}/providers/Microsoft.Web/sites/{siteName}";
            }

            return "unknown";
        }

        private double GetCpuUsagePercent()
        {
            try
            {
                using var process = Process.GetCurrentProcess();
                return Math.Round((double)process.TotalProcessorTime.Ticks / Environment.TickCount * 100, 1);
            }
            catch
            {
                return 0.0;
            }
        }

        private string GetCustomProcessors()
        {
            // TODO: Detect custom OpenTelemetry processors, exporters, etc.
            return "None detected";
        }

        #endregion

        #region Helper Classes

        private class ConnectionEndpoints
        {
            public string IngestionEndpoint { get; set; } = string.Empty;
            public string LiveMetricsEndpoint { get; set; } = string.Empty;
            public string ProfilerEndpoint { get; set; } = string.Empty;
            public string SnapshotDebuggerEndpoint { get; set; } = string.Empty;
        }

        private class ConnectionInfo
        {
            public string InstrumentationKey { get; set; } = string.Empty;
        }

        #endregion
    }
}
