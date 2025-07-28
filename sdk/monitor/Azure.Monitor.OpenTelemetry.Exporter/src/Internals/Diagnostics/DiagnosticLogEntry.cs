// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Azure.Monitor.OpenTelemetry.Exporter.Internals.Diagnostics
{
    /// <summary>
    /// Represents a structured diagnostic log entry according to the ADF specification.
    /// Fields are ordered for optimal readability when viewing JSON files in text editors.
    /// </summary>
    internal class DiagnosticLogEntry
    {
        [JsonPropertyOrder(1)]
        public string Timestamp { get; set; } = string.Empty;

        [JsonPropertyOrder(2)]
        public string ObservedTimestamp { get; set; } = string.Empty;

        [JsonPropertyOrder(3)]
        public string InstrumentationScope { get; set; } = string.Empty;

        [JsonPropertyOrder(4)]
        public string EventName { get; set; } = string.Empty;

        [JsonPropertyOrder(5]
        public string? TraceId { get; set; }

        [JsonPropertyOrder(6)]
        public string? SpanId { get; set; }

        [JsonPropertyOrder(7)]
        public string SeverityText { get; set; } = string.Empty;

        [JsonPropertyOrder(8)]
        public int SeverityNumber { get; set; }

        [JsonPropertyOrder(9)]
        public string Body { get; set; } = string.Empty;

        [JsonPropertyOrder(10)]
        public Dictionary<string, object> Resource { get; set; } = new();

        [JsonPropertyOrder(11)]
        public Dictionary<string, object> Attributes { get; set; } = new();
    }
}
