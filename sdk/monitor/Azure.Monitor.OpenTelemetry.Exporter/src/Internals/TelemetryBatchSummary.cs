// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace Azure.Monitor.OpenTelemetry.Exporter.Internals
{
    internal class TelemetryBatchSummary
    {
        public int RequestCount { get; set; }
        public int DependencyCount { get; set; }
        public int TraceCount { get; set; }
        public int ExceptionCount { get; set; }
        public int MetricCount { get; set; }
        public int UnknownCount { get; set; }

        public int TotalCount => RequestCount + DependencyCount + TraceCount + ExceptionCount + MetricCount + UnknownCount;

        public void Reset()
        {
            RequestCount = 0;
            DependencyCount = 0;
            TraceCount = 0;
            ExceptionCount = 0;
            MetricCount = 0;
            UnknownCount = 0;
        }

        public string GetSummaryString()
        {
            var parts = new List<string>();

            if (RequestCount > 0)
                parts.Add($"{RequestCount} requests");
            if (DependencyCount > 0)
                parts.Add($"{DependencyCount} dependencies");
            if (TraceCount > 0)
                parts.Add($"{TraceCount} traces");
            if (ExceptionCount > 0)
                parts.Add($"{ExceptionCount} exceptions");
            if (MetricCount > 0)
                parts.Add($"{MetricCount} metrics");
            if (UnknownCount > 0)
                parts.Add($"{UnknownCount} unknown");

            return parts.Count > 0 ? string.Join(", ", parts) : "0 items";
        }

        public Dictionary<string, int> GetCountsDictionary()
        {
            return new Dictionary<string, int>
        {
            { "Request", RequestCount },
            { "Dependency", DependencyCount },
            { "Trace", TraceCount },
            { "Exception", ExceptionCount },
            { "Metric", MetricCount },
            { "Unknown", UnknownCount }
        };
        }
    }
}
