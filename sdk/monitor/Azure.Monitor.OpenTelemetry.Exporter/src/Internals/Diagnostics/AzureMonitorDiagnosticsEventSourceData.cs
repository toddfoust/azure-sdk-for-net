// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Azure.Monitor.OpenTelemetry.Exporter.Internals.Diagnostics
{
    /// <summary>
    /// EventSource for Azure Monitor telemetry production diagnostic events (Pillar 1).
    /// Answers the question: What telemetry did your application produce in memory?
    /// </summary>
    [EventSource(Name = EventSourceName)]
    internal sealed class AzureMonitorDiagnosticsEventSourceData : EventSource
    {
        internal const string EventSourceName = "OpenTelemetry-AzureMonitor-Diagnostics-Data";

        internal static readonly AzureMonitorDiagnosticsEventSourceData Log = new();

        private AzureMonitorDiagnosticsEventSourceData() : base(EventSourceSettings.EtwSelfDescribingEventFormat)
        {
            AzureMonitorDiagnosticsEventListenerManager.Initialize();
        }

        #region Keywords for Telemetry Types

        /// <summary>
        /// Keywords for categorizing telemetry types for performance and filtering
        /// </summary>
        public static class Keywords
        {
            /// <summary>Request telemetry (AppRequests table) - incoming HTTP requests, RPC calls</summary>
            public const EventKeywords Requests = (EventKeywords)0x0001;

            /// <summary>Dependency telemetry (AppDependencies table) - outgoing calls, database queries</summary>
            public const EventKeywords Dependencies = (EventKeywords)0x0002;

            /// <summary>Trace/Message telemetry (AppTraces table) - log messages, debug traces</summary>
            public const EventKeywords Traces = (EventKeywords)0x0004;

            /// <summary>Exception telemetry (AppExceptions table) - errors and exceptions</summary>
            public const EventKeywords Exceptions = (EventKeywords)0x0008;

            /// <summary>Metric telemetry (customMetrics table) - performance counters, business metrics</summary>
            public const EventKeywords Metrics = (EventKeywords)0x0010;

            /// <summary>Custom Event telemetry (customEvents table) - business events</summary>
            public const EventKeywords Events = (EventKeywords)0x0020;

            /// <summary>PageView telemetry (AppPageViews table) - page navigation</summary>
            public const EventKeywords PageViews = (EventKeywords)0x0040;

            /// <summary>High-frequency events that may need special handling</summary>
            public const EventKeywords HighFrequency = (EventKeywords)0x0080;
        }

        #endregion

        #region Telemetry Production Events

        /// <summary>
        /// Logs Request telemetry production with level-appropriate detail (will appear in AppRequests table)
        /// </summary>
        [Event(1, Level = EventLevel.Informational, Keywords = Keywords.Requests,
               Message = "Request telemetry produced: {0} ({1} {2})")]
        public void RequestTelemetryProduced(string operationName, string httpMethod, string url,
            double durationMs, int responseCode, bool success, string traceId, string spanId,
            string activityKind, string instrumentationLibrary, string telemetryDetails, int payloadSizeBytes)
        {
            if (IsEnabled(EventLevel.Informational, Keywords.Requests))
            {
                WriteEvent(1, operationName ?? "Unknown", httpMethod ?? "Unknown", url ?? "Unknown",
                    durationMs, responseCode, success, traceId ?? "", spanId ?? "",
                    activityKind ?? "Unknown", instrumentationLibrary ?? "Unknown",
                    telemetryDetails ?? "", payloadSizeBytes);
            }
        }

        /// <summary>
        /// Logs Dependency telemetry production with level-appropriate detail (will appear in AppDependencies table)
        /// </summary>
        [Event(2, Level = EventLevel.Informational, Keywords = Keywords.Dependencies,
               Message = "Dependency telemetry produced: {0} ({1} -> {2})")]
        public void DependencyTelemetryProduced(string dependencyName, string dependencyType, string target,
            string data, double durationMs, bool success, string resultCode, string traceId, string spanId,
            string activityKind, string instrumentationLibrary, string telemetryDetails, int payloadSizeBytes)
        {
            if (IsEnabled(EventLevel.Informational, Keywords.Dependencies))
            {
                WriteEvent(2, dependencyName ?? "Unknown", dependencyType ?? "Unknown", target ?? "Unknown",
                    data ?? "", durationMs, success, resultCode ?? "", traceId ?? "", spanId ?? "",
                    activityKind ?? "Unknown", instrumentationLibrary ?? "Unknown",
                    telemetryDetails ?? "", payloadSizeBytes);
            }
        }

        /// <summary>
        /// Logs Trace/Message telemetry production with level-appropriate detail (will appear in AppTraces table)
        /// </summary>
        [Event(3, Level = EventLevel.Informational, Keywords = Keywords.Traces,
               Message = "Trace telemetry produced: {0} (Level: {1})")]
        public void TraceTelemetryProduced(string message, string severityLevel, string categoryName,
            string traceId, string spanId, string loggerProvider, string instrumentationLibrary,
            string telemetryDetails, int payloadSizeBytes)
        {
            if (IsEnabled(EventLevel.Informational, Keywords.Traces))
            {
                WriteEvent(3, message ?? "Unknown", severityLevel ?? "Unknown", categoryName ?? "Unknown",
                    traceId ?? "", spanId ?? "", loggerProvider ?? "Unknown", instrumentationLibrary ?? "Unknown",
                    telemetryDetails ?? "", payloadSizeBytes);
            }
        }

        /// <summary>
        /// Logs Exception telemetry production with level-appropriate detail (will appear in AppExceptions table)
        /// </summary>
        [Event(4, Level = EventLevel.Error, Keywords = Keywords.Exceptions,
               Message = "Exception telemetry produced: {0} - {1}")]
        public void ExceptionTelemetryProduced(string exceptionType, string exceptionMessage, string problemId,
            string traceId, string spanId, string instrumentationLibrary, bool hasStackTrace, string telemetryDetails,
            int payloadSizeBytes)
        {
            if (IsEnabled(EventLevel.Error, Keywords.Exceptions))
            {
                WriteEvent(4, exceptionType ?? "Unknown", exceptionMessage ?? "Unknown", problemId ?? "Unknown",
                    traceId ?? "", spanId ?? "", instrumentationLibrary ?? "Unknown", hasStackTrace,
                    telemetryDetails ?? "", payloadSizeBytes);
            }
        }

        /// <summary>
        /// Logs Metric telemetry production with level-appropriate detail (will appear in customMetrics table)
        /// </summary>
        [Event(5, Level = EventLevel.Informational, Keywords = Keywords.Metrics,
               Message = "Metric telemetry produced: {0} = {1} {2} (Type: {3})")]
        public void MetricTelemetryProduced(string metricName, double value, string unit, string metricType,
            string aggregationType, string instrumentType, string instrumentationLibrary, int dataPointCount,
            string telemetryDetails, int payloadSizeBytes)
        {
            if (IsEnabled(EventLevel.Informational, Keywords.Metrics))
            {
                WriteEvent(5, metricName ?? "Unknown", value, unit ?? "", metricType ?? "Unknown",
                    aggregationType ?? "Unknown", instrumentType ?? "Unknown", instrumentationLibrary ?? "Unknown",
                    dataPointCount, telemetryDetails ?? "", payloadSizeBytes);
            }
        }

        /// <summary>
        /// Logs Custom Event telemetry production with level-appropriate detail (will appear in customEvents table)
        /// </summary>
        [Event(6, Level = EventLevel.Informational, Keywords = Keywords.Events,
               Message = "Custom Event telemetry produced: {0}")]
        public void EventTelemetryProduced(string eventName, string traceId, string spanId,
            string instrumentationLibrary, int propertiesCount, int measurementsCount,
            string telemetryDetails, int payloadSizeBytes)
        {
            if (IsEnabled(EventLevel.Informational, Keywords.Events))
            {
                WriteEvent(6, eventName ?? "Unknown", traceId ?? "", spanId ?? "",
                    instrumentationLibrary ?? "Unknown", propertiesCount, measurementsCount,
                    telemetryDetails ?? "", payloadSizeBytes);
            }
        }

        /// <summary>
        /// Logs PageView telemetry production with level-appropriate detail (will appear in AppPageViews table)
        /// </summary>
        [Event(7, Level = EventLevel.Informational, Keywords = Keywords.PageViews,
               Message = "PageView telemetry produced: {0} ({1})")]
        public void PageViewTelemetryProduced(string pageName, string url, double durationMs,
            string traceId, string spanId, string instrumentationLibrary,
            string telemetryDetails, int payloadSizeBytes)
        {
            if (IsEnabled(EventLevel.Informational, Keywords.PageViews))
            {
                WriteEvent(7, pageName ?? "Unknown", url ?? "Unknown", durationMs,
                    traceId ?? "", spanId ?? "", instrumentationLibrary ?? "Unknown",
                    telemetryDetails ?? "", payloadSizeBytes);
            }
        }

        #endregion

        #region Telemetry Processing Events

        /// <summary>
        /// Logs when telemetry is dropped during processing/sampling
        /// </summary>
        [Event(10, Level = EventLevel.Warning, Keywords = Keywords.HighFrequency,
               Message = "Telemetry dropped: {0} - {1}")]
        public void TelemetryDropped(string telemetryType, string reason, string traceId, string spanId,
            string samplerType, double samplingRate)
        {
            if (IsEnabled(EventLevel.Warning, Keywords.HighFrequency))
            {
                WriteEvent(10, telemetryType ?? "Unknown", reason ?? "Unknown", traceId ?? "", spanId ?? "",
                    samplerType ?? "Unknown", samplingRate);
            }
        }

        /// <summary>
        /// Logs when telemetry fails validation or transformation
        /// </summary>
        [Event(11, Level = EventLevel.Error, Keywords = Keywords.HighFrequency,
               Message = "Telemetry processing failed: {0} - {1}")]
        public void TelemetryProcessingFailed(string telemetryType, string errorMessage, string validationRule,
            string traceId, string spanId)
        {
            if (IsEnabled(EventLevel.Error, Keywords.HighFrequency))
            {
                WriteEvent(11, telemetryType ?? "Unknown", errorMessage ?? "Unknown", validationRule ?? "Unknown",
                    traceId ?? "", spanId ?? "");
            }
        }

        #endregion

        #region Non-Event Helper Methods

        /// <summary>
        /// Helper method to log Activity/Span as Request telemetry
        /// </summary>
        [NonEvent]
        public void LogRequestFromActivity(Activity activity, string? instrumentationLibrary = null)
        {
            if (!IsEnabled(EventLevel.Informational, Keywords.Requests))
                return;

            try
            {
                var operationName = activity.DisplayName ?? activity.OperationName;
                var httpMethod = GetActivityTagValue(activity, "http.request.method")?.ToString() ??
                                GetActivityTagValue(activity, "http.method")?.ToString() ?? "Unknown";
                var url = GetActivityTagValue(activity, "url.full")?.ToString() ??
                         GetActivityTagValue(activity, "http.url")?.ToString() ?? "Unknown";
                var responseCode = GetIntTagValue(activity, "http.response.status_code") ??
                                  GetIntTagValue(activity, "http.status_code") ?? 200;
                var success = responseCode < 400;
                var durationMs = activity.Duration.TotalMilliseconds;

                // Always include TraceId and SpanId for correlation
                var traceId = activity.TraceId.ToString();
                var spanId = activity.SpanId.ToString();

                // Determine what level of detail to include based on current EventLevel
                string telemetryDetails = "";
                int payloadSize = 0;

                if (IsEnabled(EventLevel.Verbose, Keywords.Requests))
                {
                    telemetryDetails = SerializeActivityToJson(activity);
                    payloadSize = Encoding.UTF8.GetByteCount(telemetryDetails);
                }

                RequestTelemetryProduced(operationName, httpMethod, url, durationMs, responseCode, success,
                    traceId, spanId, activity.Kind.ToString(),
                    instrumentationLibrary ?? "Unknown", telemetryDetails, payloadSize);
            }
            catch (Exception ex)
            {
                TelemetryProcessingFailed("Request", ex.Message, "Activity serialization",
                    activity.TraceId.ToString(), activity.SpanId.ToString());
            }
        }

        /// <summary>
        /// Helper method to log Activity/Span as Dependency telemetry
        /// </summary>
        [NonEvent]
        public void LogDependencyFromActivity(Activity activity, string? instrumentationLibrary = null)
        {
            if (!IsEnabled(EventLevel.Informational, Keywords.Dependencies))
                return;

            try
            {
                var dependencyName = activity.DisplayName ?? activity.OperationName;
                var dependencyType = DetermineDependencyType(activity);
                var target = GetTargetFromActivity(activity);
                var data = GetDataFromActivity(activity);
                var resultCode = GetIntTagValue(activity, "http.response.status_code")?.ToString() ??
                                GetIntTagValue(activity, "http.status_code")?.ToString() ??
                                GetTagValue(activity, "db.response.status_code") ?? "0";
                var success = activity.Status != ActivityStatusCode.Error;
                var durationMs = activity.Duration.TotalMilliseconds;

                // Always include TraceId and SpanId for correlation
                var traceId = activity.TraceId.ToString();
                var spanId = activity.SpanId.ToString();

                // Determine what level of detail to include based on current EventLevel
                string telemetryDetails = "";
                int payloadSize = 0;

                if (IsEnabled(EventLevel.Verbose, Keywords.Dependencies))
                {
                    telemetryDetails = SerializeActivityToJson(activity);
                    payloadSize = Encoding.UTF8.GetByteCount(telemetryDetails);
                }

                DependencyTelemetryProduced(dependencyName, dependencyType, target, data, durationMs, success,
                    resultCode, traceId, spanId, activity.Kind.ToString(),
                    instrumentationLibrary ?? "Unknown", telemetryDetails, payloadSize);
            }
            catch (Exception ex)
            {
                TelemetryProcessingFailed("Dependency", ex.Message, "Activity serialization",
                    activity.TraceId.ToString(), activity.SpanId.ToString());
            }
        }

        /// <summary>
        /// Helper method to log OpenTelemetry LogRecord as Trace telemetry
        /// </summary>
        [NonEvent]
        public void LogTraceFromLogRecord(object logRecord, string? instrumentationLibrary = null)
        {
            if (!IsEnabled(EventLevel.Informational, Keywords.Traces))
                return;

            try
            {
                // Use reflection to access LogRecord properties
                var logRecordType = logRecord.GetType();
                var body = GetPropertyValue(logRecordType, logRecord, "Body") ??
                          GetPropertyValue(logRecordType, logRecord, "FormattedMessage") ?? "Unknown";
                var severityLevel = GetPropertyValue(logRecordType, logRecord, "SeverityText") ?? "Information";
                var categoryName = GetPropertyValue(logRecordType, logRecord, "CategoryName") ?? "Unknown";

                // Always include TraceId and SpanId for correlation (even if empty)
                var traceId = GetPropertyValue(logRecordType, logRecord, "TraceId") ?? "";
                var spanId = GetPropertyValue(logRecordType, logRecord, "SpanId") ?? "";

                // Determine what level of detail to include based on current EventLevel
                string telemetryDetails = "";
                int payloadSize = 0;

                if (IsEnabled(EventLevel.Verbose, Keywords.Traces))
                {
                    telemetryDetails = SerializeObjectToJson(logRecord);
                    payloadSize = Encoding.UTF8.GetByteCount(telemetryDetails);
                }

                TraceTelemetryProduced(body, severityLevel, categoryName, traceId, spanId,
                    "Microsoft.Extensions.Logging", instrumentationLibrary ?? "Unknown",
                    telemetryDetails, payloadSize);
            }
            catch (Exception ex)
            {
                TelemetryProcessingFailed("Trace", ex.Message, "LogRecord serialization", "", "");
            }
        }

        /// <summary>
        /// Helper method to log Exception telemetry
        /// </summary>
        [NonEvent]
        public void LogExceptionTelemetry(Exception exception, Activity? activity = null, string? instrumentationLibrary = null)
        {
            if (!IsEnabled(EventLevel.Error, Keywords.Exceptions))
                return;

            try
            {
                var exceptionType = exception.GetType().Name;
                var exceptionMessage = exception.Message;
                var problemId = GenerateProblemId(exception);
                var hasStackTrace = !string.IsNullOrEmpty(exception.StackTrace);

                // Always include TraceId and SpanId for correlation (even if empty)
                var traceId = activity?.TraceId.ToString() ?? "";
                var spanId = activity?.SpanId.ToString() ?? "";

                // Determine what level of detail to include based on current EventLevel
                string telemetryDetails = "";
                int payloadSize = 0;

                if (IsEnabled(EventLevel.Verbose, Keywords.Exceptions))
                {
                    var exceptionData = new
                    {
                        Type = exceptionType,
                        Message = exceptionMessage,
                        StackTrace = exception.StackTrace,
                        ProblemId = problemId,
                        InnerException = exception.InnerException?.GetType().Name
                    };
                    telemetryDetails = JsonSerializer.Serialize(exceptionData);
                    payloadSize = Encoding.UTF8.GetByteCount(telemetryDetails);
                }

                ExceptionTelemetryProduced(exceptionType, exceptionMessage, problemId,
                    traceId, spanId, instrumentationLibrary ?? "Unknown", hasStackTrace,
                    telemetryDetails, payloadSize);
            }
            catch (Exception ex)
            {
                TelemetryProcessingFailed("Exception", ex.Message, "Exception serialization", "", "");
            }
        }

        /// <summary>
        /// Helper method to log Metric telemetry
        /// </summary>
        [NonEvent]
        public void LogMetricTelemetry(string metricName, double value, string unit = "",
            string metricType = "Unknown", string? instrumentationLibrary = null)
        {
            if (!IsEnabled(EventLevel.Informational, Keywords.Metrics))
                return;

            try
            {
                // Determine what level of detail to include based on current EventLevel
                string telemetryDetails = "";
                int payloadSize = 0;

                if (IsEnabled(EventLevel.Verbose, Keywords.Metrics))
                {
                    var metricData = new
                    {
                        Name = metricName,
                        Value = value,
                        Unit = unit,
                        Type = metricType,
                        Timestamp = DateTime.UtcNow
                    };
                    telemetryDetails = JsonSerializer.Serialize(metricData);
                    payloadSize = Encoding.UTF8.GetByteCount(telemetryDetails);
                }

                MetricTelemetryProduced(metricName, value, unit, metricType, "Sum", "Counter",
                    instrumentationLibrary ?? "Unknown", 1, telemetryDetails, payloadSize);
            }
            catch (Exception ex)
            {
                TelemetryProcessingFailed("Metric", ex.Message, "Metric serialization", "", "");
            }
        }

        #endregion

        #region Private Helper Methods

        private string DetermineDependencyType(Activity activity)
        {
            // Check for HTTP dependencies
            if (GetActivityTagValue(activity, "http.request.method") != null || GetActivityTagValue(activity, "http.method") != null)
                return "Http";

            // Check for database dependencies
            if (GetActivityTagValue(activity, "db.system") != null)
            {
                var dbSystem = GetActivityTagValue(activity, "db.system")?.ToString();
                return dbSystem switch
                {
                    "mssql" or "sqlserver" => "SQL",
                    "mysql" => "MySQL",
                    "postgresql" => "PostgreSQL",
                    "redis" => "Redis",
                    "cosmosdb" => "Azure DocumentDB",
                    _ => "Database"
                };
            }

            // Check for messaging dependencies
            if (GetActivityTagValue(activity, "messaging.system") != null)
                return "Queue";

            // Default for other types
            return "Other";
        }

        private string GetTargetFromActivity(Activity activity)
        {
            // For HTTP dependencies
            var host = GetActivityTagValue(activity, "server.address")?.ToString() ??
                      GetActivityTagValue(activity, "http.host")?.ToString() ??
                      GetActivityTagValue(activity, "net.peer.name")?.ToString();
            var port = GetActivityTagValue(activity, "server.port")?.ToString() ??
                      GetActivityTagValue(activity, "net.peer.port")?.ToString();

            if (!string.IsNullOrEmpty(host))
            {
                return !string.IsNullOrEmpty(port) ? $"{host}:{port}" : host!; // host is guaranteed non-null here
            }

            // For database dependencies
            var dbName = GetActivityTagValue(activity, "db.name")?.ToString();
            if (!string.IsNullOrEmpty(dbName))
                return dbName!; // dbName is guaranteed non-null here due to the check

            return "Unknown";
        }

        private string GetDataFromActivity(Activity activity)
        {
            // For HTTP dependencies, return the full URL
            var url = GetActivityTagValue(activity, "url.full")?.ToString() ?? GetActivityTagValue(activity, "http.url")?.ToString();
            if (!string.IsNullOrEmpty(url))
                return url!; // url is guaranteed non-null here due to the check

            // For database dependencies, return the SQL statement
            var statement = GetActivityTagValue(activity, "db.statement")?.ToString();
            if (!string.IsNullOrEmpty(statement))
                return statement!; // statement is guaranteed non-null here due to the check

            return "";
        }

        private int? GetIntTagValue(Activity activity, string tagName)
        {
            var value = GetActivityTagValue(activity, tagName);
            if (value != null && int.TryParse(value.ToString(), out var intValue))
                return intValue;
            return null;
        }

        private string GetTagValue(Activity activity, string tagName)
        {
            return GetActivityTagValue(activity, tagName)?.ToString() ?? "";
        }

        /// <summary>
        /// .NET Standard 2.0 compatible way to get Activity tag values
        /// </summary>
        private object? GetActivityTagValue(Activity activity, string tagName)
        {
            // Always use the manual approach for maximum compatibility
            // This works across all .NET versions (.NET Standard 2.0, .NET Framework, .NET 5+)
            if (activity.Tags != null)
            {
                foreach (var tag in activity.Tags)
                {
                    if (string.Equals(tag.Key, tagName, StringComparison.OrdinalIgnoreCase))
                    {
                        return tag.Value; // tag.Value can be null, which is fine
                    }
                }
            }
            return null;
        }

        private string? GetPropertyValue(Type type, object obj, string propertyName)
        {
            try
            {
                var property = type.GetProperty(propertyName);
                return property?.GetValue(obj)?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private string SerializeActivityToJson(Activity activity)
        {
            try
            {
                var activityData = new
                {
                    TraceId = activity.TraceId.ToString(),
                    SpanId = activity.SpanId.ToString(),
                    ParentSpanId = activity.ParentSpanId.ToString(),
                    OperationName = activity.OperationName,
                    DisplayName = activity.DisplayName,
                    Kind = activity.Kind.ToString(),
                    Status = activity.Status.ToString(),
                    StatusDescription = activity.StatusDescription,
                    StartTime = activity.StartTimeUtc,
                    Duration = activity.Duration,
                    Tags = activity.Tags?.ToDictionary(kv => kv.Key, kv => kv.Value),
                    Events = activity.Events?.Select(e => new { e.Name, e.Timestamp, Attributes = e.Tags?.ToDictionary(kv => kv.Key, kv => kv.Value) })
                };

                return JsonSerializer.Serialize(activityData, new JsonSerializerOptions { WriteIndented = false });
            }
            catch
            {
                return "{}";
            }
        }

        private string SerializeObjectToJson(object obj)
        {
            try
            {
                return JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = false });
            }
            catch
            {
                return obj?.ToString() ?? "{}";
            }
        }

        private string GenerateProblemId(Exception exception)
        {
            try
            {
                var stackTrace = exception.StackTrace;
                if (string.IsNullOrEmpty(stackTrace))
                    return exception.GetType().Name;

                // Extract the first method from the stack trace
                var lines = stackTrace.Split('\n');
                var firstLine = lines.FirstOrDefault(l => l.Contains(" at "))?.Trim();
                if (firstLine != null)
                {
                    return $"{exception.GetType().Name} at {firstLine.Substring(firstLine.IndexOf(" at ") + 4)}";
                }

                return exception.GetType().Name;
            }
            catch
            {
                return exception.GetType().Name;
            }
        }

        #endregion
    }
}
