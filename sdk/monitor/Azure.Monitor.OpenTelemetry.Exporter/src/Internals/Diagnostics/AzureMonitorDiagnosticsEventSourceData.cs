// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
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

        #region Telemetry Production Events (Pillar 1)

        /// <summary>
        /// Logs when telemetry is produced by instrumentation (Pillar 1: Production).
        /// This is the "birth certificate" for telemetry items.
        /// </summary>
        [Event(1, Level = EventLevel.Informational, Message = "Telemetry produced. Type: {telemetryType}, Name: {operationName}")]
        public void TelemetryProduced(string telemetryType, string operationName, string? traceId, string? spanId)
        {
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                WriteEvent(1, telemetryType, operationName, traceId ?? "Unknown", spanId ?? "Unknown");
            }
        }

        /// <summary>
        /// Logs detailed telemetry production with payload information for verbose scenarios.
        /// </summary>
        [Event(2, Level = EventLevel.Verbose, Message = "Telemetry produced with details. Type: {telemetryType}")]
        public void TelemetryProducedDetailed(string telemetryType, string operationName, string? traceId,
            string? spanId, string telemetryData, int payloadSizeBytes)
        {
            if (IsEnabled(EventLevel.Verbose, EventKeywords.None))
            {
                WriteEvent(2, telemetryType, operationName, traceId ?? "Unknown", spanId ?? "Unknown", telemetryData, payloadSizeBytes);
            }
        }

        /// <summary>
        /// Logs when telemetry production fails.
        /// </summary>
        [Event(3, Level = EventLevel.Error, Message = "Telemetry production failed. Type: {telemetryType}, Error: {errorMessage}")]
        public void TelemetryProductionFailed(string telemetryType, string errorMessage, string operationName)
        {
            if (IsEnabled(EventLevel.Error, EventKeywords.None))
            {
                WriteEvent(3, telemetryType, errorMessage, operationName);
            }
        }

        /// <summary>
        /// Logs different telemetry types being created.
        /// </summary>
        [Event(4, Level = EventLevel.Informational, Message = "Request telemetry produced. Name: {requestName}, Duration: {durationMs}ms")]
        public void RequestTelemetryProduced(string requestName, string url, int durationMs, int responseCode, bool success)
        {
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                WriteEvent(4, requestName, url, durationMs, responseCode, success);
            }
        }

        /// <summary>
        /// Logs dependency telemetry creation.
        /// </summary>
        [Event(5, Level = EventLevel.Informational, Message = "Dependency telemetry produced. Name: {dependencyName}, Type: {dependencyType}")]
        public void DependencyTelemetryProduced(string dependencyName, string dependencyType, string target, int durationMs, bool success)
        {
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                WriteEvent(5, dependencyName, dependencyType, target, durationMs, success);
            }
        }

        /// <summary>
        /// Logs trace/log telemetry creation.
        /// </summary>
        [Event(6, Level = EventLevel.Informational, Message = "Trace telemetry produced. Level: {logLevel}, Message: {message}")]
        public void TraceTelemetryProduced(string logLevel, string message, string categoryName)
        {
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                WriteEvent(6, logLevel, message, categoryName);
            }
        }

        /// <summary>
        /// Logs metric telemetry creation.
        /// </summary>
        [Event(7, Level = EventLevel.Informational, Message = "Metric telemetry produced. Name: {metricName}, Value: {value}")]
        public void MetricTelemetryProduced(string metricName, double value, string unit, string metricType)
        {
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                WriteEvent(7, metricName, value, unit, metricType);
            }
        }

        /// <summary>
        /// Logs exception telemetry creation.
        /// </summary>
        [Event(8, Level = EventLevel.Error, Message = "Exception telemetry produced. Type: {exceptionType}, Message: {exceptionMessage}")]
        public void ExceptionTelemetryProduced(string exceptionType, string exceptionMessage, string stackTrace)
        {
            if (IsEnabled(EventLevel.Error, EventKeywords.None))
            {
                WriteEvent(8, exceptionType, exceptionMessage, stackTrace);
            }
        }

        #endregion

        #region Telemetry Processing and Enrichment Events

        /// <summary>
        /// Logs telemetry processing pipeline stages.
        /// </summary>
        [Event(20, Level = EventLevel.Verbose, Message = "Telemetry processing stage. Stage: {stageName}, Items: {itemCount}")]
        public void TelemetryProcessingStage(string stageName, int itemCount, int durationMs, string processorName)
        {
            if (IsEnabled(EventLevel.Verbose, EventKeywords.None))
            {
                WriteEvent(20, stageName, itemCount, durationMs, processorName);
            }
        }

        /// <summary>
        /// Logs when telemetry is dropped during processing.
        /// </summary>
        [Event(21, Level = EventLevel.Warning, Message = "Telemetry dropped during processing. Stage: {stageName}, Reason: {dropReason}")]
        public void TelemetryDroppedDuringProcessing(string stageName, string dropReason, int droppedCount, string processorName)
        {
            if (IsEnabled(EventLevel.Warning, EventKeywords.None))
            {
                WriteEvent(21, stageName, dropReason, droppedCount, processorName);
            }
        }

        /// <summary>
        /// Logs telemetry enrichment operations.
        /// </summary>
        [Event(22, Level = EventLevel.Verbose, Message = "Telemetry enriched. Enricher: {enricherName}, Properties added: {propertiesAdded}")]
        public void TelemetryEnriched(string enricherName, int propertiesAdded, string telemetryType)
        {
            if (IsEnabled(EventLevel.Verbose, EventKeywords.None))
            {
                WriteEvent(22, enricherName, propertiesAdded, telemetryType);
            }
        }

        /// <summary>
        /// Logs telemetry transformation operations.
        /// </summary>
        [Event(23, Level = EventLevel.Verbose, Message = "Telemetry transformed. Transformer: {transformerName}, Type: {telemetryType}")]
        public void TelemetryTransformed(string transformerName, string telemetryType, string beforeValue, string afterValue)
        {
            if (IsEnabled(EventLevel.Verbose, EventKeywords.None))
            {
                WriteEvent(23, transformerName, telemetryType, beforeValue, afterValue);
            }
        }

        #endregion

        #region Sampling Events

        /// <summary>
        /// Logs when telemetry is dropped due to sampling.
        /// </summary>
        [Event(30, Level = EventLevel.Verbose, Message = "Telemetry sampled out. Type: {telemetryType}, TraceId: {traceId}, Reason: {samplingReason}")]
        public void TelemetrySampledOut(string telemetryType, string traceId, string samplingReason, double samplingRate)
        {
            if (IsEnabled(EventLevel.Verbose, EventKeywords.None))
            {
                WriteEvent(30, telemetryType, traceId, samplingReason, samplingRate);
            }
        }

        /// <summary>
        /// Logs sampling decision details.
        /// </summary>
        [Event(31, Level = EventLevel.Verbose, Message = "Sampling decision made. TraceId: {traceId}, Decision: {decision}, Rate: {samplingRate}")]
        public void SamplingDecisionMade(string traceId, string decision, double samplingRate, string samplerType)
        {
            if (IsEnabled(EventLevel.Verbose, EventKeywords.None))
            {
                WriteEvent(31, traceId, decision, samplingRate, samplerType);
            }
        }

        #endregion

        #region Data Production Error Events

        /// <summary>
        /// Logs data layer exceptions.
        /// </summary>
        [Event(40, Level = EventLevel.Error, Message = "Data production exception. Component: {component}, Error: {errorMessage}")]
        public void DataProductionException(string component, string errorType, string errorMessage, string stackTrace)
        {
            if (IsEnabled(EventLevel.Error, EventKeywords.None))
            {
                WriteEvent(40, component, errorType, errorMessage, stackTrace);
            }
        }

        /// <summary>
        /// Logs telemetry serialization errors.
        /// </summary>
        [Event(41, Level = EventLevel.Error, Message = "Telemetry serialization failed. Type: {telemetryType}, Error: {errorMessage}")]
        public void TelemetrySerializationFailed(string telemetryType, string errorMessage, string telemetryData)
        {
            if (IsEnabled(EventLevel.Error, EventKeywords.None))
            {
                WriteEvent(41, telemetryType, errorMessage, telemetryData);
            }
        }

        #endregion

        #region Non-Event Helper Methods

        /// <summary>
        /// Helper method to log telemetry production with structured data.
        /// </summary>
        [NonEvent]
        public void LogTelemetryProduction(string telemetryType, object telemetryData,
            string? traceId = null, string? spanId = null)
        {
            if (!IsEnabled(EventLevel.Informational, EventKeywords.None))
                return;

            try
            {
                var operationName = ExtractOperationName(telemetryData);

                TelemetryProduced(telemetryType, operationName, traceId, spanId);

                // Log specific telemetry type details
                LogSpecificTelemetryType(telemetryType, telemetryData);

                // Log detailed information if verbose logging is enabled
                if (IsEnabled(EventLevel.Verbose, EventKeywords.None))
                {
                    var serializedData = SerializeTelemetryData(telemetryData);
                    var payloadSize = Encoding.UTF8.GetByteCount(serializedData);
                    TelemetryProducedDetailed(telemetryType, operationName, traceId, spanId, serializedData, payloadSize);
                }
            }
            catch (Exception ex)
            {
                DataProductionException("TelemetryProduction", ex.GetType().Name, ex.Message, ex.StackTrace ?? string.Empty);
            }
        }

        /// <summary>
        /// Helper method to log sampling decisions.
        /// </summary>
        [NonEvent]
        public void LogSamplingDecision(string traceId, bool sampled, double samplingRate,
            string samplerType, string? reason = null)
        {
            if (!IsEnabled(EventLevel.Verbose, EventKeywords.None))
                return;

            try
            {
                var decision = sampled ? "Sampled" : "NotSampled";
                SamplingDecisionMade(traceId, decision, samplingRate, samplerType);

                if (!sampled && reason != null)
                {
                    TelemetrySampledOut("Trace", traceId, reason, samplingRate);
                }
            }
            catch (Exception ex)
            {
                DataProductionException("SamplingDecision", ex.GetType().Name, ex.Message, ex.StackTrace ?? string.Empty);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void LogSpecificTelemetryType(string telemetryType, object telemetryData)
        {
            try
            {
                switch (telemetryType.ToLower())
                {
                    case "request":
                        LogRequestTelemetry(telemetryData);
                        break;
                    case "dependency":
                        LogDependencyTelemetry(telemetryData);
                        break;
                    case "trace":
                    case "log":
                        LogTraceTelemetry(telemetryData);
                        break;
                    case "metric":
                        LogMetricTelemetry(telemetryData);
                        break;
                    case "exception":
                        LogExceptionTelemetry(telemetryData);
                        break;
                }
            }
            catch
            {
                // Ignore errors in specific type logging
            }
        }

        private void LogRequestTelemetry(object telemetryData)
        {
            try
            {
                var type = telemetryData.GetType();
                var name = GetPropertyValue(type, telemetryData, "Name") ?? "Unknown";
                var url = GetPropertyValue(type, telemetryData, "Url") ?? GetPropertyValue(type, telemetryData, "Uri") ?? "Unknown";
                var duration = GetPropertyValue(type, telemetryData, "Duration");
                var responseCode = GetPropertyValue(type, telemetryData, "ResponseCode") ?? GetPropertyValue(type, telemetryData, "ResultCode");
                var success = GetPropertyValue(type, telemetryData, "Success");

                var durationMs = ParseDuration(duration);
                var responseCodeInt = ParseInt(responseCode);
                var successBool = ParseBool(success);

                RequestTelemetryProduced(name, url, durationMs, responseCodeInt, successBool);
            }
            catch
            {
                // Ignore parsing errors
            }
        }

        private void LogDependencyTelemetry(object telemetryData)
        {
            try
            {
                var type = telemetryData.GetType();
                var name = GetPropertyValue(type, telemetryData, "Name") ?? "Unknown";
                var dependencyType = GetPropertyValue(type, telemetryData, "Type") ?? "Unknown";
                var target = GetPropertyValue(type, telemetryData, "Target") ?? "Unknown";
                var duration = GetPropertyValue(type, telemetryData, "Duration");
                var success = GetPropertyValue(type, telemetryData, "Success");

                var durationMs = ParseDuration(duration);
                var successBool = ParseBool(success);

                DependencyTelemetryProduced(name, dependencyType, target, durationMs, successBool);
            }
            catch
            {
                // Ignore parsing errors
            }
        }

        private void LogTraceTelemetry(object telemetryData)
        {
            try
            {
                var type = telemetryData.GetType();
                var message = GetPropertyValue(type, telemetryData, "Message") ?? "Unknown";
                var severityLevel = GetPropertyValue(type, telemetryData, "SeverityLevel") ?? GetPropertyValue(type, telemetryData, "LogLevel");
                var categoryName = GetPropertyValue(type, telemetryData, "CategoryName") ?? GetPropertyValue(type, telemetryData, "Category") ?? "Unknown";

                TraceTelemetryProduced(severityLevel ?? "Unknown", message, categoryName);
            }
            catch
            {
                // Ignore parsing errors
            }
        }

        private void LogMetricTelemetry(object telemetryData)
        {
            try
            {
                var type = telemetryData.GetType();
                var name = GetPropertyValue(type, telemetryData, "Name") ?? "Unknown";
                var value = GetPropertyValue(type, telemetryData, "Value") ?? GetPropertyValue(type, telemetryData, "Sum");
                var unit = GetPropertyValue(type, telemetryData, "Unit") ?? "";
                var metricType = GetPropertyValue(type, telemetryData, "MetricType") ?? type.Name;

                var valueDouble = ParseDouble(value);

                MetricTelemetryProduced(name, valueDouble, unit, metricType);
            }
            catch
            {
                // Ignore parsing errors
            }
        }

        private void LogExceptionTelemetry(object telemetryData)
        {
            try
            {
                var type = telemetryData.GetType();
                var exceptionType = GetPropertyValue(type, telemetryData, "ExceptionType") ?? type.Name;
                var message = GetPropertyValue(type, telemetryData, "Message") ?? "Unknown";
                var stackTrace = GetPropertyValue(type, telemetryData, "StackTrace") ?? "";

                ExceptionTelemetryProduced(exceptionType, message, stackTrace);
            }
            catch
            {
                // Ignore parsing errors
            }
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

        private int ParseDuration(string? duration)
        {
            if (duration == null)
                return 0;

            if (TimeSpan.TryParse(duration, out var timeSpan))
                return (int)timeSpan.TotalMilliseconds;

            if (int.TryParse(duration, out var ms))
                return ms;

            return 0;
        }

        private int ParseInt(string? value)
        {
            return int.TryParse(value, out var result) ? result : 0;
        }

        private double ParseDouble(string? value)
        {
            return double.TryParse(value, out var result) ? result : 0.0;
        }

        private bool ParseBool(string? value)
        {
            return bool.TryParse(value, out var result) && result;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private string ExtractOperationName(object telemetryData)
        {
            try
            {
                var type = telemetryData.GetType();
                var nameProperty = type.GetProperty("Name") ?? type.GetProperty("OperationName");
                return nameProperty?.GetValue(telemetryData)?.ToString() ?? "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        private string SerializeTelemetryData(object telemetryData)
        {
            try
            {
                return JsonSerializer.Serialize(telemetryData, new JsonSerializerOptions
                {
                    WriteIndented = false,
                    MaxDepth = 5 // Prevent deep recursion
                });
            }
            catch
            {
                return telemetryData.ToString() ?? "null";
            }
        }

        #endregion
    }
}
