// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Diagnostics.Tracing;
using System.Runtime.CompilerServices;

namespace Azure.Monitor.OpenTelemetry.Exporter.Internals.Diagnostics
{
    /// <summary>
    /// EventSource for Azure Monitor core diagnostic events including agent lifecycle,
    /// configuration loading, and environment reporting.
    /// </summary>
    [EventSource(Name = EventSourceName)]
    internal sealed class AzureMonitorDiagnosticsCoreEventSource : EventSource
    {
        internal const string EventSourceName = "OpenTelemetry-AzureMonitor-Diagnostics-Core";

        internal static readonly AzureMonitorDiagnosticsCoreEventSource Log = new AzureMonitorDiagnosticsCoreEventSource();
#if DEBUG
        internal static readonly AzureMonitorDiagnosticsEventListener Listener = new AzureMonitorDiagnosticsEventListener();
#endif
        private AzureMonitorDiagnosticsCoreEventSource() : base(EventSourceSettings.EtwSelfDescribingEventFormat)
        {
        }

        #region Agent Lifecycle Events

        /// <summary>
        /// Logs when the Azure Monitor agent starts up and reports environment information.
        /// </summary>
        [Event(1, Level = EventLevel.Informational, Message = "Azure Monitor agent starting up. Version: {agentVersion}, Process: {processName} ({processId}), OS: {osVersion}")]
        public void AgentStartup(string agentVersion, string processName, int processId, string osVersion)
        {
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                WriteEvent(1, agentVersion, processName, processId, osVersion);
            }
        }

        /// <summary>
        /// Logs comprehensive environment report during agent startup.
        /// </summary>
        [Event(2, Level = EventLevel.Informational, Message = "Agent environment report generated")]
        public void AgentEnvironmentReport(string machineName, string workingDirectory, string agentDirectory,
            string instrumentationKey, string connectionString, string cloudRole, string cloudRoleInstance)
        {
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                WriteEvent(2, machineName, workingDirectory, agentDirectory, instrumentationKey, connectionString, cloudRole, cloudRoleInstance);
            }
        }

        /// <summary>
        /// Logs when the agent shuts down gracefully.
        /// </summary>
        [Event(3, Level = EventLevel.Informational, Message = "Azure Monitor agent shutting down")]
        public void AgentShutdown()
        {
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                WriteEvent(3);
            }
        }

        #endregion

        #region Configuration Loading Events

        /// <summary>
        /// Logs successful configuration loading.
        /// </summary>
        [Event(10, Level = EventLevel.Informational, Message = "Configuration loaded successfully from {configSource}")]
        public void ConfigurationLoaded(string configSource, string configPath)
        {
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                WriteEvent(10, configSource, configPath);
            }
        }

        /// <summary>
        /// Logs configuration loading errors.
        /// </summary>
        [Event(11, Level = EventLevel.Error, Message = "Failed to load configuration from {configPath}: {errorMessage}")]
        public void ConfigurationLoadFailed(string configPath, string errorMessage)
        {
            if (IsEnabled(EventLevel.Error, EventKeywords.None))
            {
                WriteEvent(11, configPath, errorMessage);
            }
        }

        /// <summary>
        /// Logs configuration validation errors.
        /// </summary>
        [Event(12, Level = EventLevel.Error, Message = "Configuration validation failed: {validationError}")]
        public void ConfigurationValidationFailed(string validationError, string configSource)
        {
            if (IsEnabled(EventLevel.Error, EventKeywords.None))
            {
                WriteEvent(12, validationError, configSource);
            }
        }

        /// <summary>
        /// Logs when connection string is parsed and validated.
        /// </summary>
        [Event(13, Level = EventLevel.Informational, Message = "Connection string parsed. Endpoint: {endpoint}, Authentication: {authType}")]
        public void ConnectionStringParsed(string endpoint, string authType, string instrumentationKey)
        {
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                WriteEvent(13, endpoint, authType, instrumentationKey);
            }
        }

        #endregion

        #region Connection and Network Events

        /// <summary>
        /// Logs resolved connection endpoints during startup.
        /// </summary>
        [Event(20, Level = EventLevel.Informational, Message = "Connection endpoints resolved")]
        public void ConnectionEndpointsResolved(string ingestionEndpoint, string liveMetricsEndpoint,
            string profilerEndpoint, string resolvedIPs)
        {
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                WriteEvent(20, ingestionEndpoint, liveMetricsEndpoint, profilerEndpoint, resolvedIPs);
            }
        }

        /// <summary>
        /// Logs DNS resolution results for endpoints.
        /// </summary>
        [Event(21, Level = EventLevel.Informational, Message = "DNS resolved {hostname} to {ipAddresses}")]
        public void DnsResolved(string hostname, string ipAddresses)
        {
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                WriteEvent(21, hostname, ipAddresses);
            }
        }

        /// <summary>
        /// Logs DNS resolution failures.
        /// </summary>
        [Event(22, Level = EventLevel.Warning, Message = "DNS resolution failed for {hostname}: {errorMessage}")]
        public void DnsResolutionFailed(string hostname, string errorMessage)
        {
            if (IsEnabled(EventLevel.Warning, EventKeywords.None))
            {
                WriteEvent(22, hostname, errorMessage);
            }
        }

        #endregion

        #region Authentication Events

        /// <summary>
        /// Logs authentication configuration and status.
        /// </summary>
        [Event(30, Level = EventLevel.Informational, Message = "Authentication configured. Type: {authType}, Status: {status}")]
        public void AuthenticationConfigured(string authType, string status)
        {
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                WriteEvent(30, authType, status);
            }
        }

        /// <summary>
        /// Logs authentication failures.
        /// </summary>
        [Event(31, Level = EventLevel.Error, Message = "Authentication failed: {errorMessage}")]
        public void AuthenticationFailed(string errorMessage, string authType)
        {
            if (IsEnabled(EventLevel.Error, EventKeywords.None))
            {
                WriteEvent(31, errorMessage, authType);
            }
        }

        /// <summary>
        /// Logs successful authentication.
        /// </summary>
        [Event(32, Level = EventLevel.Informational, Message = "Authentication successful using {authType}")]
        public void AuthenticationSuccessful(string authType)
        {
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                WriteEvent(32, authType);
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
        [Event(41, Level = EventLevel.Warning, Message = "Profile API call failed. Endpoint: {endpoint}, Error: {errorMessage}")]
        public void ProfileApiFailed(string endpoint, string errorMessage, int statusCode)
        {
            if (IsEnabled(EventLevel.Warning, EventKeywords.None))
            {
                WriteEvent(41, endpoint, errorMessage, statusCode);
            }
        }

        /// <summary>
        /// Logs dynamic configuration updates from Profile API.
        /// </summary>
        [Event(42, Level = EventLevel.Informational, Message = "Dynamic configuration updated from Profile API")]
        public void DynamicConfigurationUpdated(string configSection, string changes)
        {
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                WriteEvent(42, configSection, changes);
            }
        }

        #endregion

        #region Storage and Persistence Events

        /// <summary>
        /// Logs storage directory initialization.
        /// </summary>
        [Event(50, Level = EventLevel.Informational, Message = "Storage directory initialized: {storagePath}")]
        public void StorageDirectoryInitialized(string storagePath, long availableSpaceBytes)
        {
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                WriteEvent(50, storagePath, availableSpaceBytes);
            }
        }

        /// <summary>
        /// Logs storage directory access issues.
        /// </summary>
        [Event(51, Level = EventLevel.Error, Message = "Storage directory access failed: {storagePath}, Error: {errorMessage}")]
        public void StorageDirectoryAccessFailed(string storagePath, string errorMessage)
        {
            if (IsEnabled(EventLevel.Error, EventKeywords.None))
            {
                WriteEvent(51, storagePath, errorMessage);
            }
        }

        /// <summary>
        /// Logs low disk space warnings.
        /// </summary>
        [Event(52, Level = EventLevel.Warning, Message = "Low disk space detected. Available: {availableSpaceBytes} bytes")]
        public void LowDiskSpaceWarning(long availableSpaceBytes, string storagePath)
        {
            if (IsEnabled(EventLevel.Warning, EventKeywords.None))
            {
                WriteEvent(52, availableSpaceBytes, storagePath);
            }
        }

        #endregion

        #region Resource Detection Events

        /// <summary>
        /// Logs cloud resource detection results.
        /// </summary>
        [Event(60, Level = EventLevel.Informational, Message = "Cloud resource detected. Provider: {cloudProvider}, Platform: {cloudPlatform}")]
        public void CloudResourceDetected(string cloudProvider, string cloudPlatform, string resourceId)
        {
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                WriteEvent(60, cloudProvider, cloudPlatform, resourceId);
            }
        }

        /// <summary>
        /// Logs service resource information.
        /// </summary>
        [Event(61, Level = EventLevel.Informational, Message = "Service resource detected. Name: {serviceName}, Version: {serviceVersion}")]
        public void ServiceResourceDetected(string serviceName, string serviceVersion, string serviceInstanceId)
        {
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                WriteEvent(61, serviceName, serviceVersion, serviceInstanceId);
            }
        }

        #endregion

        #region Performance and Health Events

        /// <summary>
        /// Logs performance counters and health metrics.
        /// </summary>
        [Event(70, Level = EventLevel.Informational, Message = "Performance metrics collected")]
        public void PerformanceMetricsCollected(int cpuUsagePercent, long memoryUsageBytes,
            int queueDepth, int processingRate)
        {
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                WriteEvent(70, cpuUsagePercent, memoryUsageBytes, queueDepth, processingRate);
            }
        }

        /// <summary>
        /// Logs resource pressure warnings.
        /// </summary>
        [Event(71, Level = EventLevel.Warning, Message = "Resource pressure detected. Type: {resourceType}, Level: {pressureLevel}")]
        public void ResourcePressureDetected(string resourceType, string pressureLevel, int currentValue)
        {
            if (IsEnabled(EventLevel.Warning, EventKeywords.None))
            {
                WriteEvent(71, resourceType, pressureLevel, currentValue);
            }
        }

        #endregion

        #region Error and Exception Events

        /// <summary>
        /// Logs unhandled exceptions in the agent.
        /// </summary>
        [Event(80, Level = EventLevel.Error, Message = "Unhandled exception in agent: {exceptionMessage}")]
        public void UnhandledException(string exceptionType, string exceptionMessage, string stackTrace)
        {
            if (IsEnabled(EventLevel.Error, EventKeywords.None))
            {
                WriteEvent(80, exceptionType, exceptionMessage, stackTrace);
            }
        }

        /// <summary>
        /// Logs agent recovery from errors.
        /// </summary>
        [Event(81, Level = EventLevel.Warning, Message = "Agent recovered from error. Component: {component}, Action: {recoveryAction}")]
        public void AgentRecovery(string component, string recoveryAction, string errorType)
        {
            if (IsEnabled(EventLevel.Warning, EventKeywords.None))
            {
                WriteEvent(81, component, recoveryAction, errorType);
            }
        }

        #endregion

        #region Non-Event Helper Methods

        /// <summary>
        /// Helper method to log agent startup with environment details.
        /// </summary>
        [NonEvent]
        public void LogAgentStartupWithEnvironment()
        {
            if (!IsEnabled(EventLevel.Informational, EventKeywords.None))
                return;

            try
            {
                var agentVersion = GetAgentVersion();
                var processName = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
                var processId = System.Diagnostics.Process.GetCurrentProcess().Id;
                var osVersion = Environment.OSVersion.ToString();

                AgentStartup(agentVersion, processName, processId, osVersion);

                // Log comprehensive environment report
                var machineName = Environment.MachineName;
                var workingDirectory = Directory.GetCurrentDirectory();
                var agentDirectory = AppContext.BaseDirectory;

                // These would come from actual configuration
                var instrumentationKey = "****-masked-for-security";
                var connectionString = "****-masked-for-security";
                var cloudRole = Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME") ?? "Unknown";
                var cloudRoleInstance = Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID") ?? machineName;

                AgentEnvironmentReport(machineName, workingDirectory, agentDirectory,
                    instrumentationKey, connectionString, cloudRole, cloudRoleInstance);
            }
            catch (Exception ex)
            {
                UnhandledException(ex.GetType().Name, ex.Message, ex.StackTrace ?? string.Empty);
            }
        }

        /// <summary>
        /// Helper method to log configuration loading with validation.
        /// </summary>
        [NonEvent]
        public void LogConfigurationLoading(string configPath, bool success, string? errorMessage = null)
        {
            if (!IsEnabled(EventLevel.Informational, EventKeywords.None))
                return;

            try
            {
                if (success)
                {
                    var configSource = DetermineConfigurationSource(configPath);
                    ConfigurationLoaded(configSource, configPath);
                }
                else
                {
                    ConfigurationLoadFailed(configPath, errorMessage ?? "Unknown error");
                }
            }
            catch (Exception ex)
            {
                UnhandledException(ex.GetType().Name, ex.Message, ex.StackTrace ?? string.Empty);
            }
        }

        /// <summary>
        /// Helper method to log connection endpoint resolution.
        /// </summary>
        [NonEvent]
        public void LogConnectionEndpointResolution(string ingestionEndpoint,
            string[] resolvedIPs)
        {
            if (!IsEnabled(EventLevel.Informational, EventKeywords.None))
                return;

            try
            {
                var liveMetricsEndpoint = "https://rt.services.visualstudio.com";
                var profilerEndpoint = "https://agent.azureserviceprofiler.net";
                var resolvedIPsString = string.Join(", ", resolvedIPs);

                ConnectionEndpointsResolved(ingestionEndpoint, liveMetricsEndpoint,
                    profilerEndpoint, resolvedIPsString);

                // Log individual DNS resolutions
                DnsResolved(ExtractHostname(ingestionEndpoint), resolvedIPsString);
            }
            catch (Exception ex)
            {
                UnhandledException(ex.GetType().Name, ex.Message, ex.StackTrace ?? string.Empty);
            }
        }

        /// <summary>
        /// Helper method to log performance metrics collection.
        /// </summary>
        [NonEvent]
        public void LogPerformanceMetrics()
        {
            if (!IsEnabled(EventLevel.Informational, EventKeywords.None))
                return;

            try
            {
                var process = System.Diagnostics.Process.GetCurrentProcess();
                var cpuUsage = GetCpuUsagePercent();
                var memoryUsage = process.WorkingSet64;
                var queueDepth = 0; // Would come from actual queue
                var processingRate = 0; // Would come from actual processing metrics

                PerformanceMetricsCollected(cpuUsage, memoryUsage, queueDepth, processingRate);
            }
            catch (Exception ex)
            {
                UnhandledException(ex.GetType().Name, ex.Message, ex.StackTrace ?? string.Empty);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private string GetAgentVersion()
        {
            try
            {
                var assembly = typeof(AzureMonitorDiagnosticsCoreEventSource).Assembly;
                return assembly.GetName().Version?.ToString() ?? "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        private string DetermineConfigurationSource(string configPath)
        {
            if (configPath.Contains("OTEL_DIAGNOSTICS.json"))
                return "OTEL_DIAGNOSTICS";
            if (configPath.Contains("appsettings"))
                return "AppSettings";
            if (configPath.IndexOf("environment", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Environment";

            return "File";
        }

        private string ExtractHostname(string url)
        {
            try
            {
                var uri = new Uri(url);
                return uri.Host;
            }
            catch
            {
                return url;
            }
        }

        private int GetCpuUsagePercent()
        {
            try
            {
                // This is a simplified implementation
                // In practice, you'd want to use PerformanceCounter or similar
                using var process = System.Diagnostics.Process.GetCurrentProcess();
                return (int)(process.TotalProcessorTime.TotalMilliseconds / Environment.TickCount * 100);
            }
            catch
            {
                return 0;
            }
        }

        #endregion
    }
}
