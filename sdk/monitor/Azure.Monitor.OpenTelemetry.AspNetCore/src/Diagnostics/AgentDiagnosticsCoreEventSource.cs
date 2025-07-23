using System;
using System.Diagnostics.Tracing;

namespace Azure.Monitor.OpenTelemetry.AspNetCore.Diagnostics
{
    [EventSource(Name = "OpenTelemetry-AzureMonitor-Diagnostics-Core")]
    public sealed class AgentDiagnosticsCoreEventSource : EventSource
    {
        public static readonly AgentDiagnosticsCoreEventSource Log = new();

        private AgentDiagnosticsCoreEventSource() { }

        [NonEvent]
        public void ConfigurationLoadFailed(Exception ex)
        {
            if (IsEnabled())
            {
                ConfigurationLoadFailed(
                    ex.GetType().FullName ?? "UnknownException",
                    ex.Message,
                    ex.StackTrace ?? "No stack trace");
            }
        }

        [Event(1, Level = EventLevel.Error, Message = "Failed to load diagnostics config: {0}")]
        public void ConfigurationLoadFailed(string exceptionType, string message, string stackTrace) { }

        [NonEvent]
        public void ProfileApiCallFailed(Exception ex)
        {
            if (IsEnabled())
            {
                ProfileApiCallFailed(
                    ex.GetType().FullName ?? "UnknownException",
                    ex.Message,
                    ex.StackTrace ?? "No stack trace");
            }
        }

        [Event(2, Level = EventLevel.Error, Message = "Failed to call Profile API: {0}")]
        public void ProfileApiCallFailed(string exceptionType, string message, string stackTrace) { }

        [Event(3, Level = EventLevel.Informational, Message = "Agent diagnostics initialized.")]
        public void AgentDiagnosticsInitialized() { }

        [Event(4, Level = EventLevel.Warning, Message = "Diagnostics config file not found. Logging is disabled.")]
        public void ConfigFileMissing() { }

        [NonEvent]
        public void AttachStatusReport(bool attached, string reason = null)
        {
            if (IsEnabled())
            {
                AttachStatusReport(
                    attached ? "Attached" : "BackedOff",
                    reason ?? "N/A",
                    Environment.MachineName,
                    Environment.ProcessId,
                    System.Diagnostics.Process.GetCurrentProcess().ProcessName);
            }
        }

        [Event(5, Level = EventLevel.Informational, Message = "Agent attach status: {0}")]
        public void AttachStatusReport(string status, string reason, string machineName, int processId, string processName) { }


        [NonEvent]
        public void AgentEnvironmentReport(
            string osVersion,
            string machineName,
            int processId,
            string processName,
            string workingDirectory,
            string configSource,
            string instrumentationKey,
            string samplingType,
            double samplingRate)
        {
            if (IsEnabled())
            {
                AgentEnvironmentReportInternal(
                    osVersion,
                    machineName,
                    processId,
                    processName,
                    workingDirectory,
                    configSource,
                    instrumentationKey,
                    samplingType,
                    samplingRate);
            }
        }

        [Event(6, Level = EventLevel.Informational, Message = "Agent environment report emitted.")]
        public void AgentEnvironmentReportInternal(
            string osVersion,
            string machineName,
            int processId,
            string processName,
            string workingDirectory,
            string configSource,
            string instrumentationKey,
            string samplingType,
            double samplingRate)
        { }

        [NonEvent]
        public void ConnectionEndpointsReport(List<EndpointInfo> endpoints)
        {
            if (IsEnabled())
            {
                foreach (var endpoint in endpoints)
                {
                    ConnectionEndpointResolved(
                        endpoint.Name,
                        endpoint.Url,
                        string.Join(",", endpoint.ResolvedIps)
                    );
                }
            }
        }

        [Event(7, Level = EventLevel.Informational, Message = "Resolved endpoint {0}: {1} -> {2}")]
        public void ConnectionEndpointResolved(string name, string url, string resolvedIps) { }

        public class EndpointInfo
        {
            public string Name { get; set; }
            public string Url { get; set; }
            public List<string> ResolvedIps { get; set; } = new();
        }


    }
}
