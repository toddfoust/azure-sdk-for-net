// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using static System.Net.Mime.MediaTypeNames;

namespace Azure.Monitor.OpenTelemetry.Exporter.Internals.Diagnostics
{
    /// <summary>
    /// EventSource for Azure Monitor exporter diagnostic events (Pillars 2 + 3).
    /// Answers the questions: Where was telemetry sent? and What was the backend response?
    /// </summary>
    [EventSource(Name = EventSourceName)]
    internal sealed class AzureMonitorDiagnosticsEventSourceExporter : EventSource
    {
        internal const string EventSourceName = "OpenTelemetry-AzureMonitor-Diagnostics-Exporter";

        internal static readonly AzureMonitorDiagnosticsEventSourceExporter Log = new();

        private AzureMonitorDiagnosticsEventSourceExporter() : base(EventSourceSettings.EtwSelfDescribingEventFormat)
        {
            AzureMonitorDiagnosticsEventListenerManager.Initialize();
        }

        #region Pillar 2: Transmission Attempt Events

        /// <summary>
        /// Logs when telemetry transmission is attempted (Pillar 2: Transmission).
        /// This is the "shipping manifest" for telemetry batches.
        /// </summary>
        [Event(1, Level = EventLevel.Informational, Message = "Transmission attempt started. Endpoint: {0}, Batch size: {2} items")]
        public void TransmissionAttempt(string endpoint, string resolvedIP, int batchSize,
            string batchComposition, int requestCount, int dependencyCount, int traceCount,
            int metricCount, int threadId)
        {
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                WriteEvent(1, endpoint, resolvedIP, batchSize, batchComposition, requestCount, dependencyCount, traceCount, metricCount, threadId);
            }
        }

        /// <summary>
        /// Logs detailed transmission attempt with batch composition.
        /// </summary>
        [Event(2, Level = EventLevel.Informational, Message = "Transmission batch details")]
        public void TransmissionBatchDetails(string endpoint, string batchComposition,
            int requestCount, int dependencyCount, int traceCount, int metricCount, int threadId)
        {
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                WriteEvent(2, endpoint, batchComposition, requestCount, dependencyCount, traceCount, metricCount, threadId);
            }
        }

        /// <summary>
        /// Logs transmission retry attempts.
        /// </summary>
        [Event(3, Level = EventLevel.Warning, Message = "Transmission retry attempt. Retry: {0}, Delay: {1}ms")]
        public void TransmissionRetryAttempt(int retryCount, int delayMs, string endpoint, string reason)
        {
            if (IsEnabled(EventLevel.Warning, EventKeywords.None))
            {
                WriteEvent(3, retryCount, delayMs, endpoint, reason);
            }
        }

        /// <summary>
        /// Logs when transmission fails before getting a response.
        /// </summary>
        [Event(4, Level = EventLevel.Error, Message = "Transmission failed. Endpoint: {0}, Error: {1}")]
        public void TransmissionFailed(string endpoint, string errorMessage, string exceptionType, int attemptDurationMs)
        {
            if (IsEnabled(EventLevel.Error, EventKeywords.None))
            {
                WriteEvent(4, endpoint, errorMessage, exceptionType, attemptDurationMs);
            }
        }

        /// <summary>
        /// Logs HTTP request details for transmission.
        /// </summary>
        [Event(5, Level = EventLevel.Verbose, Message = "HTTP request prepared. Method: {0}, Content-Type: {1}")]
        public void HttpRequestPrepared(string httpMethod, string contentType, string endpoint, int contentLength)
        {
            if (IsEnabled(EventLevel.Verbose, EventKeywords.None))
            {
                WriteEvent(5, httpMethod, contentType, endpoint, contentLength);
            }
        }

        /// <summary>
        /// Logs when telemetry transmission is attempted, however these telemetry items were loaded from persistent storage, not JUST produced by currently running app (Pillar 2: Transmission).
        /// This is the "shipping manifest" for telemetry batches.
        /// </summary>
        [Event(6, Level = EventLevel.Informational, Message = "Transmission of telemetry loaded from storage attempt started.")]
        public void TransmissionFromStorageAttempt(string body)
        {
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                WriteEvent(6, body);
            }
        }

        #endregion

        #region Pillar 3: Backend Response Events

        /// <summary>
        /// Logs backend response for transmission (Pillar 3: Response).
        /// This is the "delivery receipt" for telemetry transmission.
        /// </summary>
        [Event(10, Level = EventLevel.Informational, Message = "Backend response received. Status: {0}, Duration: {1}ms")]
        public void BackendResponseReceived(int statusCode, int durationMs, string endpoint,
            string telemetrySent, string requestId, string responseBody, int threadId)
        {
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                WriteEvent(10, statusCode, durationMs, endpoint ?? "Unknown", telemetrySent ?? "Unknown", requestId ?? "Unknown", responseBody ?? "null", threadId);
            }
        }

        /// <summary>
        /// Logs successful backend responses with acceptance details.
        /// </summary>
        [Event(11, Level = EventLevel.Informational, Message = "Backend accepted telemetry. Received: {0}, Accepted: {1}")]
        public void BackendAcceptedTelemetry(int itemsReceived, int itemsAccepted, int itemsRejected, string endpoint)
        {
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                WriteEvent(11, itemsReceived, itemsAccepted, itemsRejected, endpoint);
            }
        }

        /// <summary>
        /// Logs backend error responses.
        /// </summary>
        [Event(12, Level = EventLevel.Error, Message = "Backend error response. Status: {0}, Error: {1}")]
        public void BackendErrorResponse(int statusCode, string errorMessage, string endpoint, string responseBody)
        {
            if (IsEnabled(EventLevel.Error, EventKeywords.None))
            {
                WriteEvent(12, statusCode, errorMessage, endpoint, responseBody);
            }
        }

        /// <summary>
        /// Logs backend throttling responses.
        /// </summary>
        [Event(13, Level = EventLevel.Warning, Message = "Backend throttling response. Status: {0}, Retry-After: {1}ms")]
        public void BackendThrottlingResponse(int statusCode, int retryAfterMs, string endpoint, string reason)
        {
            if (IsEnabled(EventLevel.Warning, EventKeywords.None))
            {
                WriteEvent(13, statusCode, retryAfterMs, endpoint, reason);
            }
        }

        /// <summary>
        /// Logs backend partial success responses where some items were rejected.
        /// </summary>
        [Event(14, Level = EventLevel.Warning, Message = "Backend partial success. Accepted: {0}, Rejected: {1}")]
        public void BackendPartialSuccess(int itemsAccepted, int itemsRejected, string endpoint, string rejectionReasons)
        {
            if (IsEnabled(EventLevel.Warning, EventKeywords.None))
            {
                WriteEvent(14, itemsAccepted, itemsRejected, endpoint, rejectionReasons);
            }
        }

        #endregion

        #region Buffer and Persistence Events

        /// <summary>
        /// Logs telemetry buffer operations.
        /// </summary>
        [Event(20, Level = EventLevel.Verbose, Message = "Buffer operation. Action: {0}, Buffer size: {1}, Queue depth: {2}")]
        public void BufferOperation(string action, int bufferSize, int queueDepth, int maxCapacity)
        {
            if (IsEnabled(EventLevel.Verbose, EventKeywords.None))
            {
                WriteEvent(20, action, bufferSize, queueDepth, maxCapacity);
            }
        }

        /// <summary>
        /// Logs when buffer reaches capacity limits.
        /// </summary>
        [Event(21, Level = EventLevel.Warning, Message = "Buffer capacity warning. Current: {0}, Max: {1}, Action: {2}")]
        public void BufferCapacityWarning(int currentSize, int maxSize, string action)
        {
            if (IsEnabled(EventLevel.Warning, EventKeywords.None))
            {
                WriteEvent(21, currentSize, maxSize, action);
            }
        }

        /// <summary>
        /// Logs telemetry persistence to disk.
        /// </summary>
        [Event(22, Level = EventLevel.Informational, Message = "Telemetry persisted to disk. File: {0}, Items: {1}")]
        public void TelemetryPersistedToDisk(string fileName, int itemCount, long fileSizeBytes, string storagePath)
        {
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                WriteEvent(22, fileName, itemCount, fileSizeBytes, storagePath);
            }
        }

        /// <summary>
        /// Logs telemetry restoration from disk.
        /// </summary>
        [Event(23, Level = EventLevel.Informational, Message = "Telemetry restored from disk. File: {0}, Items: {1}")]
        public void TelemetryRestoredFromDisk(string fileName, int itemCount, long fileSizeBytes)
        {
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                WriteEvent(23, fileName, itemCount, fileSizeBytes);
            }
        }

        /// <summary>
        /// Logs when offline storage fails.
        /// </summary>
        [Event(24, Level = EventLevel.Error, Message = "Offline storage failed. Path: {0}, Error: {1}, Items Lost: {2}")]
        public void OfflineStorageFailed(string storagePath, string errorMessage, int itemsLost)
        {
            if (IsEnabled(EventLevel.Error, EventKeywords.None))
            {
                WriteEvent(24, storagePath, errorMessage, itemsLost);
            }
        }

        /// <summary>
        /// Logs when the exporter enables backoff mode due to retryable responses from ingestion endpoint
        /// </summary>
        [Event(25, Level = EventLevel.Warning, Message = "Transmission temporarily deferred")]
        public void TransmissionBackoffEnabled(int statusCode, string reasonPhrase, string backoffDurationDescription,
            double backoffDurationMs, bool persistentStorageEnabled, string endpoint, string localStorageFolderPath, string? statusDetails)
        {
            if (IsEnabled(EventLevel.Warning, EventKeywords.None))
            {
                statusDetails = $"The SDK attempted to send telemetry to the Application Insights ingestion endpoint but encountered a failure or received a retry response. Status: {statusCode} {reasonPhrase}. Retry scheduled after: {backoffDurationDescription}. New telemetry will be saved locally and retried once connectivity is restored. Telemetry files stored in: {localStorageFolderPath}";
                WriteEvent(25, statusCode, reasonPhrase ?? "Unknown", backoffDurationDescription ?? "Unknown",
                    backoffDurationMs, persistentStorageEnabled, endpoint ?? "Unknown", statusDetails ?? "Unknown");
            }
        }

        /// <summary>
        /// Logs when ingestion returns non-retriable response, or retriable response but customer has local cache disabled and data is lost/dropped.
        /// </summary>
        [Event(26, Level = EventLevel.Error, Message = "Transmission failed")]
        public void TelemetryDropped(int statusCode, string reasonPhrase, string backoffDurationDescription,
            double backoffDurationMs, bool persistentStorageEnabled, string endpoint, string statusDetails)
        {
            if (IsEnabled(EventLevel.Error, EventKeywords.None))
            {
                statusDetails = $"The SDK attempted to send telemetry to the Application Insights ingestion endpoint but encountered a failure or received a retry response. Status:{statusCode} {reasonPhrase}. Retry scheduled after:{backoffDurationDescription} Note: Offline storage is not enabled, so telemetry generated during this backoff period will not be retained and will be permanently dropped.";
                WriteEvent(26, statusCode, reasonPhrase ?? "Unknown", backoffDurationDescription ?? "Unknown",
                    backoffDurationMs, persistentStorageEnabled, endpoint ?? "Unknown", statusDetails ?? "Unknown");
            }
        }

#endregion

#region Export Processing Events

/// <summary>
/// Logs when export batch is being prepared.
/// </summary>
[Event(30, Level = EventLevel.Verbose, Message = "Export batch preparation started. Items: {0}")]
        public void ExportBatchPreparationStarted(int itemCount, string exporterType)
        {
            if (IsEnabled(EventLevel.Verbose, EventKeywords.None))
            {
                WriteEvent(30, itemCount, exporterType);
            }
        }

        /// <summary>
        /// Logs when export batch preparation completes.
        /// </summary>
        [Event(31, Level = EventLevel.Verbose, Message = "Export batch preparation completed. Serialized size: {0}")]
        public void ExportBatchPreparationCompleted(int serializedSizeBytes, int processingDurationMs)
        {
            if (IsEnabled(EventLevel.Verbose, EventKeywords.None))
            {
                WriteEvent(31, serializedSizeBytes, processingDurationMs);
            }
        }

        /// <summary>
        /// Logs when export batch processing fails.
        /// </summary>
        [Event(32, Level = EventLevel.Error, Message = "Export batch processing failed. Error: {0}")]
        public void ExportBatchProcessingFailed(string errorMessage, int itemCount, string processingStage)
        {
            if (IsEnabled(EventLevel.Error, EventKeywords.None))
            {
                WriteEvent(32, errorMessage, itemCount, processingStage);
            }
        }

        #endregion

        #region Connection and Network Events

        /// <summary>
        /// Logs connection establishment attempts.
        /// </summary>
        [Event(40, Level = EventLevel.Verbose, Message = "Connection attempt to {0}")]
        public void ConnectionAttempt(string endpoint, string resolvedIP, int timeoutMs)
        {
            if (IsEnabled(EventLevel.Verbose, EventKeywords.None))
            {
                WriteEvent(40, endpoint, resolvedIP, timeoutMs);
            }
        }

        /// <summary>
        /// Logs connection establishment results.
        /// </summary>
        [Event(41, Level = EventLevel.Verbose, Message = "Connection established to {0}, Duration: {1}ms")]
        public void ConnectionEstablished(string endpoint, int durationMs, string protocol)
        {
            if (IsEnabled(EventLevel.Verbose, EventKeywords.None))
            {
                WriteEvent(41, endpoint, durationMs, protocol);
            }
        }

        /// <summary>
        /// Logs connection failures.
        /// </summary>
        [Event(42, Level = EventLevel.Warning, Message = "Connection failed to {0}. Error: {1}")]
        public void ConnectionFailed(string endpoint, string errorMessage, int attemptDurationMs)
        {
            if (IsEnabled(EventLevel.Warning, EventKeywords.None))
            {
                WriteEvent(42, endpoint, errorMessage, attemptDurationMs);
            }
        }

        #endregion

        #region Export Error Events

        /// <summary>
        /// Logs exporter exceptions.
        /// </summary>
        [Event(50, Level = EventLevel.Error, Message = "Exporter exception. Component: {0}, Error type: {1}, Error message: {2}")]
        public void ExporterException(string component, string errorType, string errorMessage, string stackTrace, int threadId)
        {
            if (IsEnabled(EventLevel.Error, EventKeywords.None))
            {
                WriteEvent(50, component, errorType, errorMessage, stackTrace, threadId);
            }
        }

        /// <summary>
        /// Logs when export is cancelled or times out.
        /// </summary>
        [Event(51, Level = EventLevel.Warning, Message = "Export cancelled. Reason: {0}, Items lost: {1}")]
        public void ExportCancelled(string reason, int itemsLost, int timeoutMs, int threadId)
        {
            if (IsEnabled(EventLevel.Warning, EventKeywords.None))
            {
                WriteEvent(51, reason, itemsLost, timeoutMs, threadId);
            }
        }

        #endregion

        #region Non-Event Helper Methods

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
                //var payloadSize = EstimatePayloadSize(batchList);
                var composition = AnalyzeBatchComposition(batchList);

                TransmissionAttempt(endpoint, resolvedIP, batchSize, composition.Summary,
                    composition.RequestCount, composition.DependencyCount, composition.TraceCount,
                    composition.MetricCount, Environment.CurrentManagedThreadId);

                //// Log batch composition if verbose logging is enabled
                //if (IsEnabled(EventLevel.Verbose, EventKeywords.None))
                //{
                //    var composition = AnalyzeBatchComposition(batchList);
                //    TransmissionBatchDetails(endpoint, composition.Summary,
                //        composition.RequestCount, composition.DependencyCount,
                //        composition.TraceCount, composition.MetricCount, Environment.CurrentManagedThreadId);
                //}
            }
            catch (Exception ex)
            {
                ExporterException("TransmissionAttempt", ex.GetType().Name, ex.Message, ex.StackTrace ?? string.Empty, Environment.CurrentManagedThreadId);
            }
        }

        /// <summary>
        /// Helper method to log transmission attempt with batch details.
        /// </summary>
        [NonEvent]
        public void LogTransmissionFromStorageAttempt(ReadOnlyMemory<byte> content)
        {
            if (!IsEnabled(EventLevel.Informational, EventKeywords.None))
                return;

            try
            {
                var body = Encoding.UTF8.GetString(content.ToArray());

                TransmissionFromStorageAttempt(body);
            }
            catch (Exception ex)
            {
                ExporterException("TransmissionFromStorageAttempt", ex.GetType().Name, ex.Message, ex.StackTrace ?? string.Empty, Environment.CurrentManagedThreadId);
            }
        }
        /// <summary>
        /// Helper method to format the backoff duration in a human-readable way
        /// </summary>
        [NonEvent]
        public void LogBackoffEnabled(Azure.Response? response, TimeSpan backOffTimeInterval,
            bool persistentStorageEnabled, string endpoint)
        {
            if (!IsEnabled(EventLevel.Warning, EventKeywords.None))
                return;

            try
            {
                var statusCode = response?.Status ?? 0;
                var reasonPhrase = response?.ReasonPhrase ?? "Unknown";
                var backoffMs = backOffTimeInterval.TotalMilliseconds;
                var backoffDescription = FormatBackoffDuration(backOffTimeInterval);

                TransmissionBackoffEnabled(statusCode, reasonPhrase, backoffDescription, backoffMs,
                    persistentStorageEnabled, endpoint, "c:\temp", "");
            }
            catch (Exception ex)
            {
                ExporterException("BackoffEnabled", ex.GetType().Name, ex.Message,
                    ex.StackTrace ?? string.Empty, Environment.CurrentManagedThreadId);
            }
        }

        [NonEvent]
        private string FormatBackoffDuration(TimeSpan duration)
        {
            if (duration.TotalDays >= 1)
                return $"{duration.TotalDays:F1} days";
            else if (duration.TotalHours >= 1)
                return $"{duration.TotalHours:F1} hours";
            else if (duration.TotalMinutes >= 1)
                return $"{duration.TotalMinutes:F1} minutes";
            else
                return $"{duration.TotalSeconds:F1} seconds";
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
                BackendResponseReceived(statusCode, durationMs, endpoint, responseBody, "todo", "todo", 0);

                // Parse response details
                if (statusCode >= 200 && statusCode < 300)
                {
                    if (TryParseSuccessResponse(responseBody, out var received, out var accepted, out var rejected))
                    {
                        if (rejected > 0)
                        {
                            var rejectionReasons = ExtractRejectionReasons(responseBody);
                            BackendPartialSuccess(accepted, rejected, endpoint, rejectionReasons);
                        }
                        else
                        {
                            BackendAcceptedTelemetry(received, accepted, rejected, endpoint);
                        }
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
                ExporterException("BackendResponse", ex.GetType().Name, ex.Message, ex.StackTrace ?? string.Empty, Environment.CurrentManagedThreadId);
            }
        }

        /// <summary>
        /// Helper method to log transmission retry with backoff details.
        /// </summary>
        [NonEvent]
        public void LogTransmissionRetry(int retryCount, TimeSpan delay, string endpoint, Exception exception)
        {
            if (!IsEnabled(EventLevel.Warning, EventKeywords.None))
                return;

            try
            {
                var delayMs = (int)delay.TotalMilliseconds;
                var reason = $"{exception.GetType().Name}: {exception.Message}";

                TransmissionRetryAttempt(retryCount, delayMs, endpoint, reason);
            }
            catch (Exception ex)
            {
                ExporterException("TransmissionRetry", ex.GetType().Name, ex.Message, ex.StackTrace ?? string.Empty, Environment.CurrentManagedThreadId);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
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
                //todo: Add more specific type checks if needed
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

        private string ExtractRejectionReasons(string responseBody)
        {
            try
            {
                using var document = JsonDocument.Parse(responseBody);
                if (document.RootElement.TryGetProperty("errors", out var errorsElement) &&
                    errorsElement.ValueKind == JsonValueKind.Array)
                {
                    var reasons = new List<string>();
                    foreach (var error in errorsElement.EnumerateArray())
                    {
                        if (error.TryGetProperty("message", out var messageElement))
                        {
                            var message = messageElement.GetString();
                            if (!string.IsNullOrEmpty(message))
                            {
                                reasons.Add(message!);
                            }
                        }
                    }

                    if (reasons.Count > 0)
                    {
                        return string.Join("; ", reasons.Take(3)); // Limit to first 3 reasons
                    }
                }
            }
            catch
            {
                // Fall back to generic message
            }

            return "Items rejected by backend";
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
