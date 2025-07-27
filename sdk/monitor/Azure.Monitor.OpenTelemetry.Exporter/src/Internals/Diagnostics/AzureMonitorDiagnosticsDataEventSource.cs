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
    /// Answers the question: "What telemetry did your application produce in memory?"
    /// </summary>
    [EventSource(Name = EventSourceName)]
    internal sealed class AzureMonitorDiagnosticsDataEventSource : EventSource
    {

        internal const string EventSourceName = "OpenTelemetry-AzureMonitor-Diagnostics-Data";

        internal static readonly AzureMonitorDiagnosticsDataEventSource Log = new AzureMonitorDiagnosticsDataEventSource();
#if DEBUG
        internal static readonly AzureMonitorDiagnosticsEventListener Listener = new AzureMonitorDiagnosticsEventListener();
#endif
        private AzureMonitorDiagnosticsDataEventSource() : base(EventSourceSettings.EtwSelfDescribingEventFormat)
        {
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
int bufferSize, int queueDepth, int maxCapacity)
        {
            if (IsEnabled(EventLevel.Verbose, EventKeywords.None))
            {
                WriteEvent(50, action, bufferSize, queueDepth, maxCapacity);
            }
        }

        /// <summary>
        /// Logs when buffer reaches capacity limits.
        /// </summary>
        [Event(51, Level = EventLevel.Warning, Message = "Buffer capacity warning. Current: {currentSize}, Max: {maxSize}, Action: {action}")]
public void BufferCapacityWarning(int currentSize, int maxSize, string action)
{
    if (IsEnabled(EventLevel.Warning, EventKeywords.None))
    {
        WriteEvent(51, currentSize, maxSize, action);
    }
}

/// <summary>
/// Logs telemetry persistence to disk.
/// </summary>
[Event(52, Level = EventLevel.Informational, Message = "Telemetry persisted to disk. File: {fileName}, Items: {itemCount}")]
public void TelemetryPersistedToDisk(string fileName, int itemCount, long fileSizeBytes, string storagePath)
{
    if (IsEnabled(EventLevel.Informational, EventKeywords.None))
    {
        WriteEvent(52, fileName, itemCount, fileSizeBytes, storagePath);
    }
}

/// <summary>
/// Logs telemetry restoration from disk.
/// </summary>
[Event(53, Level = EventLevel.Informational, Message = "Telemetry restored from disk. File: {fileName}, Items: {itemCount}")]
public void TelemetryRestoredFromDisk(string fileName, int itemCount, long fileSizeBytes)
{
    if (IsEnabled(EventLevel.Informational, EventKeywords.None))
    {
        WriteEvent(53, fileName, itemCount, fileSizeBytes);
    }
}

#endregion

#region Live Metrics Events

/// <summary>
/// Logs Live Metrics state transitions.
/// </summary>
[Event(60, Level = EventLevel.Informational, Message = "Live Metrics state changed. State: {state}, Endpoint: {endpoint}")]
public void LiveMetricsStateChanged(string state, string endpoint, string serverName, string roleName)
{
    if (IsEnabled(EventLevel.Informational, EventKeywords.None))
    {
        WriteEvent(60, state, endpoint, serverName, roleName);
    }
}

/// <summary>
/// Logs Live Metrics transmission.
/// </summary>
[Event(61, Level = EventLevel.Verbose, Message = "Live Metrics transmitted. Metrics: {metricsCount}, Documents: {documentsCount}")]
public void LiveMetricsTransmitted(int metricsCount, int documentsCount, int durationMs)
{
    if (IsEnabled(EventLevel.Verbose, EventKeywords.None))
    {
        WriteEvent(61, metricsCount, documentsCount, durationMs);
    }
}

#endregion

#region Error and Exception Events

/// <summary>
/// Logs data layer exceptions.
/// </summary>
[Event(70, Level = EventLevel.Error, Message = "Data layer exception. Component: {component}, Error: {errorMessage}")]
public void DataLayerException(string component, string errorType, string errorMessage, string stackTrace)
{
    if (IsEnabled(EventLevel.Error, EventKeywords.None))
    {
        WriteEvent(70, component, errorType, errorMessage, stackTrace);
    }
}

/// <summary>
/// Logs telemetry serialization errors.
/// </summary>
[Event(71, Level = EventLevel.Error, Message = "Telemetry serialization failed. Type: {telemetryType}, Error: {errorMessage}")]
public void TelemetrySerializationFailed(string telemetryType, string errorMessage, string telemetryData)
{
    if (IsEnabled(EventLevel.Error, EventKeywords.None))
    {
        WriteEvent(71, telemetryType, errorMessage, telemetryData);
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
        var safeTraceId = traceId ?? "00000000000000000000000000000000";
        var safeSpanId = spanId ?? "0000000000000000";

        TelemetryProduced(telemetryType, safeTraceId, safeSpanId, operationName);

        // Log detailed information if verbose logging is enabled
        if (IsEnabled(EventLevel.Verbose, EventKeywords.None))
        {
            var serializedData = SerializeTelemetryData(telemetryData);
            var payloadSize = Encoding.UTF8.GetByteCount(serializedData);
            TelemetryProducedDetailed(telemetryType, safeTraceId, safeSpanId, serializedData, payloadSize);
        }
    }
    catch (Exception ex)
    {
        DataLayerException("TelemetryProduction", ex.GetType().Name, ex.Message, ex.StackTrace ?? string.Empty);
    }
}

/// <summary>
/// Helper method to log transmission attempt with batch details.
/// </summary>
[NonEvent]
public void LogTransmissionAttempt(string endpoint, string resolvedIP,
    IEnumerable<object> telemetryBatch)
{
    if (!IsEnabled(EventLevel.Informational, EventKeywords.None))
        return;

    try
    {
        var batchList = telemetryBatch.ToList();
        var batchSize = batchList.Count;
        var payloadSize = EstimatePayloadSize(batchList);

        TransmissionAttemptStarted(endpoint, resolvedIP, batchSize, payloadSize);

        // Log batch composition if verbose logging is enabled
        if (IsEnabled(EventLevel.Verbose, EventKeywords.None))
        {
            var composition = AnalyzeBatchComposition(batchList);
            TransmissionBatchDetails(endpoint, composition.Summary,
                composition.RequestCount, composition.DependencyCount,
                composition.TraceCount, composition.MetricCount);
        }
    }
    catch (Exception ex)
    {
        DataLayerException("TransmissionAttempt", ex.GetType().Name, ex.Message, ex.StackTrace ?? string.Empty);
    }
}

/// <summary>
/// Helper method to log backend response with structured parsing.
/// </summary>
[NonEvent]
public void LogBackendResponse(int statusCode, string responseBody, string endpoint, int durationMs)
{
    if (!IsEnabled(EventLevel.Informational, EventKeywords.None))
        return;

    try
    {
        BackendResponseReceived(statusCode, durationMs, endpoint, responseBody);

        // Parse response details
        if (statusCode >= 200 && statusCode < 300)
        {
            if (TryParseSuccessResponse(responseBody, out var received, out var accepted, out var rejected))
            {
                BackendAcceptedTelemetry(received, accepted, rejected, endpoint);
            }
        }
        else if (statusCode == 429 || statusCode == 439) // Throttling
        {
            var retryAfter = ExtractRetryAfter(responseBody);
            var reason = ExtractThrottlingReason(responseBody);
            BackendThrottlingResponse(statusCode, retryAfter, endpoint, reason);
        }
        else if (statusCode >= 400)
        {
            var errorMessage = ExtractErrorMessage(responseBody);
            BackendErrorResponse(statusCode, errorMessage, endpoint, responseBody);
        }
    }
    catch (Exception ex)
    {
        DataLayerException("BackendResponse", ex.GetType().Name, ex.Message, ex.StackTrace ?? string.Empty);
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
        DataLayerException("SamplingDecision", ex.GetType().Name, ex.Message, ex.StackTrace ?? string.Empty);
    }
}

[MethodImpl(MethodImplOptions.NoInlining)]
private string ExtractOperationName(object telemetryData)
{
    try
    {
        // Use reflection or type checking to extract operation name
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

private int EstimatePayloadSize(IList<object> batch)
{
    try
    {
        // Rough estimation - in practice you'd use actual serialization
        return batch.Count * 1024; // Estimate 1KB per item
    }
    catch
    {
        return 0;
    }
}

private BatchComposition AnalyzeBatchComposition(IList<object> batch)
{
    var composition = new BatchComposition();

    foreach (var item in batch)
    {
        var typeName = item.GetType().Name.ToLower();

        if (typeName.Contains("request"))
            composition.RequestCount++;
        else if (typeName.Contains("dependency"))
            composition.DependencyCount++;
        else if (typeName.Contains("trace") || typeName.Contains("log"))
            composition.TraceCount++;
        else if (typeName.Contains("metric"))
            composition.MetricCount++;
    }

    composition.Summary = $"Requests: {composition.RequestCount}, Dependencies: {composition.DependencyCount}, Traces: {composition.TraceCount}, Metrics: {composition.MetricCount}";
    return composition;
}

private bool TryParseSuccessResponse(string responseBody, out int received, out int accepted, out int rejected)
{
    received = accepted = rejected = 0;

    try
    {
        if (string.IsNullOrEmpty(responseBody))
            return false;

        // Parse JSON response like {"itemsReceived":5,"itemsAccepted":4,"errors":[...]}
        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;

        if (root.TryGetProperty("itemsReceived", out var receivedElement))
            received = receivedElement.GetInt32();

        if (root.TryGetProperty("itemsAccepted", out var acceptedElement))
            accepted = acceptedElement.GetInt32();

        rejected = received - accepted;
        return true;
    }
    catch
    {
        return false;
    }
}

private int ExtractRetryAfter(string responseBody)
{
    try
    {
        using var document = JsonDocument.Parse(responseBody);
        if (document.RootElement.TryGetProperty("retryAfter", out var retryElement))
        {
            return retryElement.GetInt32();
        }
    }
    catch
    {
        // Fall back to default retry
    }

    return 60000; // Default 60 seconds
}

private string ExtractThrottlingReason(string responseBody)
{
    try
    {
        using var document = JsonDocument.Parse(responseBody);
        if (document.RootElement.TryGetProperty("message", out var messageElement))
        {
            return messageElement.GetString() ?? "Throttling";
        }
    }
    catch
    {
        // Fall back to generic message
    }

    return "Rate limit exceeded";
}

private string ExtractErrorMessage(string responseBody)
{
    try
    {
        using var document = JsonDocument.Parse(responseBody);
        if (document.RootElement.TryGetProperty("message", out var messageElement))
        {
            return messageElement.GetString() ?? "Unknown error";
        }

        if (document.RootElement.TryGetProperty("errors", out var errorsElement) &&
            errorsElement.ValueKind == JsonValueKind.Array)
        {
            var errors = new List<string>();
            foreach (var error in errorsElement.EnumerateArray())
            {
                if (error.TryGetProperty("message", out var errorMsg))
                {
                    errors.Add(errorMsg.GetString() ?? "Unknown");
                }
            }

            if (errors.Count > 0)
            {
                return string.Join("; ", errors);
            }
        }
    }
    catch
    {
        // Fall back to response body
    }

    return string.IsNullOrEmpty(responseBody) ? "Unknown error" : responseBody;
}

#endregion

/// <summary>
/// Helper class for analyzing batch composition.
/// </summary>
private class BatchComposition
{
    public int RequestCount { get; set; }
    public int DependencyCount { get; set; }
    public int TraceCount { get; set; }
    public int MetricCount { get; set; }
    public string Summary { get; set; } = string.Empty;
}
    }
}
