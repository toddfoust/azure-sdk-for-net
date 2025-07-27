// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Azure.Monitor.OpenTelemetry.Exporter
{
    public partial class AzureMonitorOpenTelemetryExporterContext
    {
        /// <summary>
        /// Gets the default instance of <see cref="AzureMonitorOpenTelemetryExporterContext"/>.
        /// </summary>
        public static AzureMonitorOpenTelemetryExporterContext Default { get; } = new AzureMonitorOpenTelemetryExporterContext();
    }
}
