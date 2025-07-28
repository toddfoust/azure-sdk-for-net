// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Azure.Monitor.OpenTelemetry.Exporter.Internals.Diagnostics
{
    /// <summary>
    /// Configuration class for self-diagnostics logging in Azure Monitor OpenTelemetry agents
    /// </summary>
    public class SelfDiagnosticsConfig
    {
        /// <summary>
        /// Gets or sets the source from which the configuration was loaded (e.g., "local config", "Profile API").
        /// </summary>
        /// <value>The configuration source. Defaults to "OTEL_DIAGNOSTICS.json".</value>
        public string ConfigSource { get; set; } = "OTEL_DIAGNOSTICS.json";

        /// <summary>
        /// Gets or sets the directory path where the configuration file was found.
        /// </summary>
        /// <value>The full path to the directory containing the configuration file. Defaults to empty string.</value>
        public string ConfigDirectory { get; set; } = "";

        /// <summary>
        /// Gets or sets the directory path where diagnostic log files will be written.
        /// </summary>
        /// <value>The log output directory path. Supports relative paths. Defaults to current directory (".").</value>
        public string LogDirectory { get; set; } = ".";

        /// <summary>
        /// Gets or sets the maximum size of each diagnostic log file in megabytes before rotation occurs.
        /// </summary>
        /// <value>The maximum file size in MB (1-128). Defaults to 10 MB.</value>
        public int FileSizeMB { get; set; } = 10;

        /// <summary>
        /// Gets or sets the default minimum log level for diagnostic events.
        /// </summary>
        /// <value>The minimum EventLevel for logging. Individual EventSources can override this via LogFilters. Defaults to Informational.</value>
        public EventLevel LogLevel { get; set; } = EventLevel.Informational;

        /// <summary>
        /// Gets or sets the per-EventSource log level overrides for granular filtering.
        /// </summary>
        /// <value>A dictionary mapping EventSource names to their specific log levels (e.g., "OpenTelemetry-AzureMonitor-Diagnostics-Exporter" -> "Debug"). Defaults to empty dictionary.</value>
        public Dictionary<string, string> LogFilters { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Gets or sets the duration in seconds after which diagnostic logging will automatically stop.
        /// </summary>
        /// <value>The timeout duration in seconds. Set to 0 for no timeout (logging continues until manually stopped or file rotation limits are reached). Defaults to 0.</value>
        public int LogLevelDurationSeconds { get; set; } = 0; // 0 = no timeout

        /// <summary>
        /// Gets or sets whether to include OpenTelemetry SDK internal events in the diagnostic logs.
        /// </summary>
        /// <value>True to include raw OpenTelemetry SDK events in addition to Azure Monitor diagnostic events; false to only log Azure Monitor diagnostics. Defaults to false.</value>
        public bool IncludeOtelSdkLogs { get; set; } = false;
    }
}
