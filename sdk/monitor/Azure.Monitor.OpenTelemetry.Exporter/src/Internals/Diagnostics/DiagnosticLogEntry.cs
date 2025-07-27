// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace Azure.Monitor.OpenTelemetry.Exporter.Internals.Diagnostics
{
    /// <summary>
    /// Represents a structured diagnostic log entry according to the ADF specification.
    /// </summary>
    internal class DiagnosticLogEntry
    {
        public string Timestamp { get; set; } = string.Empty;
        public string ObservedTimestamp { get; set; } = string.Empty;
        public string? TraceId { get; set; }
        public string? SpanId { get; set; }
        public string SeverityText { get; set; } = string.Empty;
        public int SeverityNumber { get; set; }
        public string Body { get; set; } = string.Empty;
        public string EventName { get; set; } = string.Empty;
        public string InstrumentationScope { get; set; } = string.Empty;
        public Dictionary<string, object> Resource { get; set; } = new();
        public Dictionary<string, object> Attributes { get; set; } = new();
    }
}
