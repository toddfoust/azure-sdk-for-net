// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.IO;
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
        /// Event #1: Self-diagnostics initialization starting
        /// </summary>
        [Event(1, Level = EventLevel.Informational, Message = "Azure Monitor .NET OpenTelemetry Distro self-diagnostics starting")]
        public void SelfDiagnosticsStarting()
        {
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                WriteEvent(1);
            }
        }

        /// <summary>
        /// Event #2: Configuration loading process beginning
        /// </summary>
        [Event(2, Level = EventLevel.Informational, Message = "Loading self-diagnostics configuration")]
        public void SelfDiagnosticsConfigLoading()
        {
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                WriteEvent(2);
            }
        }

        /// <summary>
        /// Event #3: Configuration successfully loaded
        /// </summary>
        [Event(3, Level = EventLevel.Informational, Message = "Self-diagnostics configuration loaded from {configSource}")]
        public void SelfDiagnosticsConfigLoaded(string configSource, string configDirectory, string logDirectory,
            int fileSizeMB, string logLevel, string logFilters, int logLevelDurationSeconds)
        {
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                WriteEvent(3, configSource, configDirectory, logDirectory, fileSizeMB, logLevel, logFilters, logLevelDurationSeconds);
            }
        }

        /// <summary>
        /// Event #4: Agent attachment status report
        /// </summary>
        [Event(4, Level = EventLevel.Informational, Message = "OpenTelemetry Agent attach status: {attachStatus}")]
        public void AttachStatusReport(string attachStatus, string attachMode, string backoffReason, string interopStatus)
        {
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                WriteEvent(4, attachStatus, attachMode, backoffReason, interopStatus);
            }
        }

        /// <summary>
        /// Event #5: Connection endpoints DNS resolution report
        /// </summary>
        [Event(5, Level = EventLevel.Informational, Message = "Resolved IP addresses for Application Insights endpoints")]
        public void ConnectionEndpointsReport(string ingestionUrl, string ingestionIPs, string liveMetricsUrl, string liveMetricsIPs,
            string profilerUrl, string profilerIPs, string snapshotDebuggerUrl, string snapshotDebuggerIPs)
        {
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                WriteEvent(5, ingestionUrl, ingestionIPs, liveMetricsUrl, liveMetricsIPs, profilerUrl, profilerIPs, snapshotDebuggerUrl, snapshotDebuggerIPs);
            }
        }

        /// <summary>
        /// Event #6: Environment and configuration details report
        /// </summary>
        [Event(6, Level = EventLevel.Informational, Message = "Reporting environment and configuration details")]
        public void EnvironmentDetails(string osType, string osVersion, string machineName, int processId, string processName,
            string processPath, string workingDirectory, string agentDirectory, string instrumentationKey, string connectionString,
            string cloudProvider, string cloudPlatform, string cloudResourceId, string cloudRole, string cloudRoleInstance,
            double cpuUsagePercent, long memoryUsageMB, string samplingType, double samplingRate,
            string distributedTracingInbound, string distributedTracingOutbound, string customProcessors)
        {
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                WriteEvent(6, osType, osVersion, machineName, processId, processName, processPath, workingDirectory, agentDirectory,
                    instrumentationKey, connectionString, cloudProvider, cloudPlatform, cloudResourceId, cloudRole, cloudRoleInstance,
                    cpuUsagePercent, memoryUsageMB, samplingType, samplingRate, distributedTracingInbound, distributedTracingOutbound, customProcessors);
            }
        }

        /// <summary>
        /// Event #7: Self-diagnostics startup completion
        /// </summary>
        [Event(7, Level = EventLevel.Informational, Message = "Azure Monitor .NET OpenTelemetry Distro self-diagnostics started")]
        public void SelfDiagnosticsStarted()
        {
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                WriteEvent(7);
            }
        }

        #endregion

        #region Agent Lifecycle Events

        /// <summary>
        /// Logs when the agent shuts down gracefully.
        /// </summary>
        [Event(10, Level = EventLevel.Informational, Message = "Azure Monitor agent shutting down")]
        public void AgentShutdown()
        {
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                WriteEvent(10);
            }
        }

        #endregion

        #region Configuration Loading Events

        /// <summary>
        /// Logs configuration loading errors.
        /// </summary>
        [Event(20, Level = EventLevel.Error, Message = "Failed to load self-diagnostics configuration from {configPath}: {errorMessage}")]
        public void ConfigurationLoadFailed(string configPath, string errorMessage)
        {
            if (IsEnabled(EventLevel.Error, EventKeywords.None))
            {
                WriteEvent(20, configPath, errorMessage);
            }
        }

        /// <summary>
        /// Logs configuration validation errors.
        /// </summary>
        [Event(21, Level = EventLevel.Error, Message = "Self-diagnostics configuration validation failed: {validationError}")]
        public void ConfigurationValidationFailed(string validationError, string configSource)
        {
            if (IsEnabled(EventLevel.Error, EventKeywords.None))
            {
                WriteEvent(21, validationError, configSource);
            }
        }

        /// <summary>
        /// Logs when connection string is parsed and validated.
        /// </summary>
        [Event(22, Level = EventLevel.Informational, Message = "Connection string parsed successfully. Endpoint: {endpoint}, Authentication: {authType}")]
        public void ConnectionStringParsed(string endpoint, string authType, string instrumentationKey)
        {
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                WriteEvent(22, endpoint, authType, instrumentationKey);
            }
        }

        /// <summary>
        /// Logs when self-diagnostics config file is missing.
        /// </summary>
        [Event(23, Level = EventLevel.Warning, Message = "Self-diagnostics config file not found at {configPath}. Attempting Profile API fallback")]
        public void ConfigFileMissing(string configPath)
        {
            if (IsEnabled(EventLevel.Warning, EventKeywords.None))
            {
                WriteEvent(23, configPath);
            }
        }

        #endregion

        #region Connection and Network Events

        /// <summary>
        /// Logs DNS resolution failures.
        /// </summary>
        [Event(30, Level = EventLevel.Warning, Message = "DNS resolution failed for {hostname}: {errorMessage}")]
        public void DnsResolutionFailed(string hostname, string errorMessage)
        {
            if (IsEnabled(EventLevel.Warning, EventKeywords.None))
            {
                WriteEvent(30, hostname, errorMessage);
            }
        }

        #endregion

        #region Profile API Events

        /// <summary>
        /// Logs successful Profile API calls.
        /// </summary>
        [Event(40, Level = EventLevel.Informational, Message = "Profile API call successful. Endpoint: {endpoint}, Duration: {durationMs}ms")]
        public void ProfileApiSuccess(string endpoint, int durationMs, string responseSize)
        {
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                WriteEvent(40, endpoint, durationMs, responseSize);
            }
        }

        /// <summary>
        /// Logs Profile API call failures.
        /// </summary>
        [Event(41, Level = EventLevel.Warning, Message = "Profile API call failed. Endpoint: {endpoint}, Error: {errorMessage}. Falling back to local configuration")]
        public void ProfileApiFailed(string endpoint, string errorMessage, int statusCode)
        {
            if (IsEnabled(EventLevel.Warning, EventKeywords.None))
            {
                WriteEvent(41, endpoint, errorMessage, statusCode);
            }
        }

        #endregion

        #region Error and Exception Events

        /// <summary>
        /// Logs unhandled exceptions in the agent.
        /// </summary>
        [Event(50, Level = EventLevel.Error, Message = "Unhandled exception in agent: {exceptionMessage}")]
        public void UnhandledException(string exceptionType, string exceptionMessage, string stackTrace)
        {
            if (IsEnabled(EventLevel.Error, EventKeywords.None))
            {
                WriteEvent(50, exceptionType, exceptionMessage, stackTrace);
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
                // Event #1: Starting
                SelfDiagnosticsStarting();

                // Event #2: Loading config
                SelfDiagnosticsConfigLoading();

                // Event #3: Config loaded
                EmitConfigLoaded(config);

                // Event #4: Attach status
                EmitAttachStatus();

                // Event #5: Connection endpoints
                EmitConnectionEndpoints(connectionString ?? string.Empty);

                // Event #6: Environment details
                EmitEnvironmentDetails(connectionString ?? string.Empty);

                // Event #7: Started
                SelfDiagnosticsStarted();
            }
            catch (Exception ex)
            {
                UnhandledException(ex.GetType().Name, ex.Message, ex.StackTrace ?? string.Empty);
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

            SelfDiagnosticsConfigLoaded(configSource, configDirectory, logDirectory, fileSizeMB, logLevel, logFilters, logLevelDurationSeconds);
        }

        [NonEvent]
        private void EmitAttachStatus()
        {
            // For manual instrumentation, we're always attached
            var attachStatus = "Attached";
            var attachMode = "Manual instrumentation";
            var backoffReason = "N/A - Manual instrumentation always attaches";
            var interopStatus = "N/A - Manual instrumentation";

            // TODO: In auto-instrumentation scenarios, check for:
            // - Conflicting ApplicationInsights.dll
            // - Interop settings
            // - Other backoff conditions

            AttachStatusReport(attachStatus, attachMode, backoffReason, interopStatus);
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

                ConnectionEndpointsReport(
                    endpoints.IngestionEndpoint, string.Join(", ", ingestionIPs),
                    endpoints.LiveMetricsEndpoint, string.Join(", ", liveMetricsIPs),
                    endpoints.ProfilerEndpoint, string.Join(", ", profilerIPs),
                    endpoints.SnapshotDebuggerEndpoint, string.Join(", ", snapshotDebuggerIPs));
            }
            catch (Exception ex)
            {
                UnhandledException(ex.GetType().Name, ex.Message, ex.StackTrace ?? string.Empty);
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

                EnvironmentDetails(osType, osVersion, machineName, processId, processName, processPath,
                    workingDirectory, agentDirectory, instrumentationKey, maskedConnectionString,
                    cloudProvider, cloudPlatform, cloudResourceId, cloudRole, cloudRoleInstance,
                    cpuUsage, memoryUsage, samplingType, samplingRate,
                    distributedTracingInbound, distributedTracingOutbound, customProcessors);
            }
            catch (Exception ex)
            {
                UnhandledException(ex.GetType().Name, ex.Message, ex.StackTrace ?? string.Empty);
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
                IngestionEndpoint = "https://dc.services.visualstudio.com/v2/track",
                LiveMetricsEndpoint = "https://rt.services.visualstudio.com/QuickPulseService.svc",
                ProfilerEndpoint = "https://agent.azureserviceprofiler.net/",
                SnapshotDebuggerEndpoint = "https://agent.azuresnapshotdebugger.net/"
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
                                    defaults.IngestionEndpoint = value + "v2/track";
                                else
                                    defaults.IngestionEndpoint = value + "/v2/track";
                                break;
                            case "liveendpoint":
                                defaults.LiveMetricsEndpoint = value.EndsWith("/") ? value + "QuickPulseService.svc" : value + "/QuickPulseService.svc";
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
                UnhandledException("ConnectionStringParsing", ex.Message, ex.StackTrace ?? string.Empty);
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
                UnhandledException("ConnectionStringParsing", ex.Message, ex.StackTrace ?? string.Empty);
            }

            return info;
        }

        private string[] ResolveHostname(string url)
        {
            try
            {
                var uri = new Uri(url);
                var addresses = Dns.GetHostAddresses(uri.Host);
                var ipStrings = new List<string>();

                foreach (var addr in addresses)
                {
                    ipStrings.Add(addr.ToString());
                }

                return ipStrings.ToArray();
            }
            catch (Exception ex)
            {
                DnsResolutionFailed(url, ex.Message);
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
