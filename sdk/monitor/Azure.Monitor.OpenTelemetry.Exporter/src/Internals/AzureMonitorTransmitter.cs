// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core.Pipeline;
using Azure.Monitor.OpenTelemetry.Exporter.Internals.ConnectionString;
using Azure.Monitor.OpenTelemetry.Exporter.Internals.Diagnostics;
using Azure.Monitor.OpenTelemetry.Exporter.Internals.PersistentStorage;
using Azure.Monitor.OpenTelemetry.Exporter.Internals.Platform;
using Azure.Monitor.OpenTelemetry.Exporter.Internals.Statsbeat;
using Azure.Monitor.OpenTelemetry.Exporter.Models;
using OpenTelemetry;
using OpenTelemetry.PersistentStorage.Abstractions;
using OpenTelemetry.PersistentStorage.FileSystem;

namespace Azure.Monitor.OpenTelemetry.Exporter.Internals
{
    /// <summary>
    /// This class encapsulates transmitting a collection of <see cref="TelemetryItem"/> to the configured Ingestion Endpoint.
    /// </summary>
    internal class AzureMonitorTransmitter : ITransmitter
    {
        internal readonly ApplicationInsightsRestClient _applicationInsightsRestClient;
        internal PersistentBlobProvider? _fileBlobProvider;
        private readonly AzureMonitorStatsbeat? _statsbeat;
        private readonly ConnectionVars _connectionVars;
        internal readonly TransmissionStateManager _transmissionStateManager;
        internal readonly TransmitFromStorageHandler? _transmitFromStorageHandler;
        private readonly bool _isAadEnabled;
        private bool _disposed;

        public AzureMonitorTransmitter(AzureMonitorExporterOptions options, IPlatform platform)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            options.Retry.MaxRetries = 0;

            _connectionVars = InitializeConnectionVars(options, platform);

            _transmissionStateManager = new TransmissionStateManager();

            _applicationInsightsRestClient = InitializeRestClient(options, _connectionVars, out _isAadEnabled);

            _fileBlobProvider = InitializeOfflineStorage(platform, _connectionVars, options.DisableOfflineStorage, options.StorageDirectory);

            if (_fileBlobProvider != null)
            {
                _transmitFromStorageHandler = new TransmitFromStorageHandler(_applicationInsightsRestClient, _fileBlobProvider, _transmissionStateManager, _connectionVars, _isAadEnabled);
            }

            _statsbeat = InitializeStatsbeat(options, _connectionVars, platform);
        }

        internal static ConnectionVars InitializeConnectionVars(AzureMonitorExporterOptions options, IPlatform platform)
        {
            if (options.ConnectionString == null)
            {
                var connectionString = platform.GetEnvironmentVariable(EnvironmentVariableConstants.APPLICATIONINSIGHTS_CONNECTION_STRING);

                if (!string.IsNullOrWhiteSpace(connectionString))
                {
                    return ConnectionStringParser.GetValues(connectionString!);
                }
            }
            else
            {
                return ConnectionStringParser.GetValues(options.ConnectionString);
            }

            throw new InvalidOperationException("A connection string was not found. Please set your connection string.");
        }

        private static ApplicationInsightsRestClient InitializeRestClient(AzureMonitorExporterOptions options, ConnectionVars connectionVars, out bool isAadEnabled)
        {
            HttpPipeline pipeline;

            if (options.Credential != null)
            {
                var scope = AadHelper.GetScope(connectionVars.AadAudience);
                var httpPipelinePolicy = new HttpPipelinePolicy[]
                {
                    new BearerTokenAuthenticationPolicy(options.Credential, scope),
                    new IngestionRedirectPolicy()
                };

                isAadEnabled = true;
                pipeline = HttpPipelineBuilder.Build(options, httpPipelinePolicy);
                AzureMonitorExporterEventSource.Log.SetAADCredentialsToPipeline(options.Credential.GetType().Name, scope);
            }
            else
            {
                isAadEnabled = false;
                var httpPipelinePolicy = new HttpPipelinePolicy[] { new IngestionRedirectPolicy() };
                pipeline = HttpPipelineBuilder.Build(options, httpPipelinePolicy);
            }

            return new ApplicationInsightsRestClient(new ClientDiagnostics(options), pipeline, host: connectionVars.IngestionEndpoint);
        }

        private static PersistentBlobProvider? InitializeOfflineStorage(IPlatform platform, ConnectionVars connectionVars, bool disableOfflineStorage, string? configuredStorageDirectory)
        {
            if (!disableOfflineStorage)
            {
                try
                {
                    var storageDirectory = StorageHelper.GetStorageDirectory(
                        platform: platform,
                        configuredStorageDirectory: configuredStorageDirectory,
                        instrumentationKey: connectionVars.InstrumentationKey);

                    AzureMonitorExporterEventSource.Log.InitializedPersistentStorage(connectionVars.InstrumentationKey, storageDirectory);
                    AzureMonitorDiagnosticsEventSourceCore.Log.InitializedPersistentStorage(connectionVars.InstrumentationKey, storageDirectory);

                    return new FileBlobProvider(storageDirectory);
                }
                catch (Exception ex)
                {
                    // TODO: Should we throw if customer has opted for storage?
                    AzureMonitorExporterEventSource.Log.FailedToInitializePersistentStorage(connectionVars.InstrumentationKey, ex);

                    return null;
                }
            }

            return null;
        }

        private static AzureMonitorStatsbeat? InitializeStatsbeat(AzureMonitorExporterOptions options, ConnectionVars connectionVars, IPlatform platform)
        {
            if (options.EnableStatsbeat && connectionVars != null)
            {
                try
                {
                    var disableStatsbeat = platform.GetEnvironmentVariable(EnvironmentVariableConstants.APPLICATIONINSIGHTS_STATSBEAT_DISABLED);
                    if (string.Equals(disableStatsbeat, "true", StringComparison.OrdinalIgnoreCase))
                    {
                        AzureMonitorExporterEventSource.Log.StatsbeatDisabled();

                        return null;
                    }

                    return new AzureMonitorStatsbeat(connectionVars, platform);
                }
                catch (Exception ex)
                {
                    AzureMonitorExporterEventSource.Log.ErrorInitializingStatsbeat(connectionVars, ex);
                }
            }

            return null;
        }

        public string InstrumentationKey => _connectionVars.InstrumentationKey;

        public async ValueTask<ExportResult> TrackAsync(IEnumerable<TelemetryItem> telemetryItems, TelemetryItemOrigin origin, bool async, CancellationToken cancellationToken)
        {
            ExportResult result = ExportResult.Failure;
            if (cancellationToken.IsCancellationRequested)
            {
                return result;
            }

            var telemetryList = telemetryItems.ToList();

            // ADF PILLAR 1: Log telemetry production (what telemetry was created for ingestion)
            LogTelemetryItemsProduced(telemetryList, origin);

            try
            {
                if (_transmissionStateManager.State == TransmissionState.Closed)
                {
                    using var httpMessage = async ?
                    await _applicationInsightsRestClient.InternalTrackAsync(telemetryItems, cancellationToken).ConfigureAwait(false) :
                    _applicationInsightsRestClient.InternalTrackAsync(telemetryItems, cancellationToken).Result;

                    result = HttpPipelineHelper.IsSuccess(httpMessage);

                    if (result == ExportResult.Failure && _fileBlobProvider != null)
                    {
                        _transmissionStateManager.EnableBackOff(httpMessage.HasResponse ? httpMessage.Response : null);
                        result = HttpPipelineHelper.HandleFailures(httpMessage, _fileBlobProvider, _connectionVars, origin, _isAadEnabled);
                    }
                    else
                    {
                        _transmissionStateManager.ResetConsecutiveErrors();
                        _transmissionStateManager.CloseTransmission();
                        AzureMonitorExporterEventSource.Log.TransmissionSuccess(origin, _isAadEnabled, _connectionVars.InstrumentationKey);
                    }
                }
                else
                {
                    byte[] requestContent = HttpPipelineHelper.GetSerializedContent(telemetryItems);
                    if (_fileBlobProvider != null)
                    {
                        result = _fileBlobProvider.SaveTelemetry(requestContent);
                    }
                }
            }
            catch (Exception ex)
            {
                AzureMonitorExporterEventSource.Log.TransmitterFailed(origin, _isAadEnabled, _connectionVars.InstrumentationKey, ex);
            }

            return result;
        }

        #region ADF Integration Helper Methods

        /// <summary>
        /// Logs all telemetry items using ADF Pillar 1 (Production logging)
        /// </summary>
        private void LogTelemetryItemsProduced(List<TelemetryItem> telemetryItems, TelemetryItemOrigin origin)
        {
            // Only log if diagnostics are enabled to avoid performance impact
            if (!AzureMonitorDiagnosticsEventSourceData.Log.IsEnabled())
                return;

            foreach (var item in telemetryItems)
            {
                try
                {
                    LogSingleTelemetryItem(item, origin);
                }
                catch (Exception ex)
                {
                    AzureMonitorDiagnosticsEventSourceData.Log.TelemetryProcessingFailed(
                        item.Name ?? "Unknown", ex.Message, "Pillar 1 logging",
                        ExtractTraceId(item), ExtractSpanId(item));
                }
            }
        }

        /// <summary>
        /// Logs a single telemetry item based on its type
        /// </summary>
        private void LogSingleTelemetryItem(TelemetryItem item, TelemetryItemOrigin origin)
        {
            var telemetryType = item.Name?.ToLowerInvariant() ?? "unknown";
            var traceId = ExtractTraceId(item);
            var spanId = ExtractSpanId(item);

            switch (telemetryType)
            {
                case "request":
                    LogRequestTelemetryItem(item, traceId, spanId);
                    break;

                case "remotedependency":
                    LogDependencyTelemetryItem(item, traceId, spanId);
                    break;

                case "message":
                    LogTraceTelemetryItem(item, traceId, spanId);
                    break;

                case "exception":
                    LogExceptionTelemetryItem(item, traceId, spanId);
                    break;

                case "metric":
                    //LogMetricTelemetryItem(item, traceId, spanId);
                    break;

                default:
                    LogGenericTelemetryItem(item, traceId, spanId);
                    break;
            }
        }

        /// <summary>
        /// Logs Request telemetry items (HTTP requests, incoming calls, inbound Service Bus messages)
        /// </summary>
        private void LogRequestTelemetryItem(TelemetryItem item, string traceId, string spanId)
        {
            try
            {
                if (item.Data?.BaseData is RequestData requestData)
                {
                    var operationName = requestData.Name ?? "Unknown";
                    var url = requestData.Url ?? "Unknown";
                    var httpMethod = ExtractHttpMethod(requestData, item.Tags);
                    var duration = ParseDuration(requestData.Duration);
                    var responseCode = ParseResponseCode(requestData.ResponseCode);
                    var success = requestData.Success;

                    string telemetryDetails = "";
                    int payloadSize = 0;

                    if (AzureMonitorDiagnosticsEventSourceData.Log.IsEnabled(EventLevel.Verbose,
                        AzureMonitorDiagnosticsEventSourceData.Keywords.Requests))
                    {
                        telemetryDetails = JsonSerializer.Serialize(item);
                        payloadSize = System.Text.Encoding.UTF8.GetByteCount(telemetryDetails);
                    }

                    AzureMonitorDiagnosticsEventSourceData.Log.RequestTelemetryProduced(
                        operationName, httpMethod, url, duration, responseCode, success,
                        traceId, spanId, "Server", "Azure.Monitor.OpenTelemetry.Exporter",
                        telemetryDetails, payloadSize);
                }
            }
            catch (Exception ex)
            {
                AzureMonitorDiagnosticsEventSourceData.Log.TelemetryProcessingFailed(
                    "Request", ex.Message, "Request parsing", traceId, spanId);
            }
        }

        /// <summary>
        /// Logs Dependency telemetry items (outgoing HTTP calls, database calls)
        /// </summary>
        private void LogDependencyTelemetryItem(TelemetryItem item, string traceId, string spanId)
        {
            try
            {
                if (item.Data?.BaseData is RemoteDependencyData dependencyData)
                {
                    var dependencyName = dependencyData.Name ?? "Unknown";
                    var dependencyType = dependencyData.Type ?? "Unknown";
                    var target = dependencyData.Target ?? "Unknown";
                    var data = dependencyData.Data ?? "";
                    var duration = ParseDuration(dependencyData.Duration);
                    var success = dependencyData.Success ?? true;
                    var resultCode = dependencyData.ResultCode ?? "0";

                    string telemetryDetails = "";
                    int payloadSize = 0;

                    if (AzureMonitorDiagnosticsEventSourceData.Log.IsEnabled(EventLevel.Verbose,
                        AzureMonitorDiagnosticsEventSourceData.Keywords.Dependencies))
                    {
                        telemetryDetails = JsonSerializer.Serialize(item);
                        payloadSize = System.Text.Encoding.UTF8.GetByteCount(telemetryDetails);
                    }

                    AzureMonitorDiagnosticsEventSourceData.Log.DependencyTelemetryProduced(
                        dependencyName, dependencyType, target, data, duration, success, resultCode,
                        traceId, spanId, "Client", "Azure.Monitor.OpenTelemetry.Exporter",
                        telemetryDetails, payloadSize);
                }
            }
            catch (Exception ex)
            {
                AzureMonitorDiagnosticsEventSourceData.Log.TelemetryProcessingFailed(
                    "Dependency", ex.Message, "Dependency parsing", traceId, spanId);
            }
        }

        /// <summary>
        /// Logs Trace/Log telemetry items (ILogger messages, debug traces)
        /// </summary>
        private void LogTraceTelemetryItem(TelemetryItem item, string traceId, string spanId)
        {
            try
            {
                if (item.Data?.BaseData is MessageData messageData)
                {
                    var message = messageData.Message ?? "Unknown";
                    var severity = MapSeverityLevel(messageData.SeverityLevel);
                    var categoryName = ExtractCategoryName(messageData.Properties);

                    string telemetryDetails = "";
                    int payloadSize = 0;

                    if (AzureMonitorDiagnosticsEventSourceData.Log.IsEnabled(EventLevel.Verbose,
                        AzureMonitorDiagnosticsEventSourceData.Keywords.Traces))
                    {
                        telemetryDetails = JsonSerializer.Serialize(item);
                        payloadSize = System.Text.Encoding.UTF8.GetByteCount(telemetryDetails);
                    }

                    AzureMonitorDiagnosticsEventSourceData.Log.TraceTelemetryProduced(
                        message, severity, categoryName, traceId, spanId,
                        "Microsoft.Extensions.Logging", "Azure.Monitor.OpenTelemetry.Exporter",
                        telemetryDetails, payloadSize);
                }
            }
            catch (Exception ex)
            {
                AzureMonitorDiagnosticsEventSourceData.Log.TelemetryProcessingFailed(
                    "Trace", ex.Message, "Trace parsing", traceId, spanId);
            }
        }

        /// <summary>
        /// Logs Exception telemetry items (unhandled exceptions, error conditions)
        /// </summary>
        private void LogExceptionTelemetryItem(TelemetryItem item, string traceId, string spanId)
        {
            try
            {
                if (item.Data?.BaseData is TelemetryExceptionData exceptionData)
                {
                    var firstException = exceptionData.Exceptions?.FirstOrDefault();
                    if (firstException != null)
                    {
                        var exceptionType = firstException.TypeName ?? "Unknown";
                        var exceptionMessage = firstException.Message ?? "Unknown";
                        var problemId = exceptionData.ProblemId ?? GenerateProblemId(firstException);
                        var hasStackTrace = !string.IsNullOrEmpty(firstException.Stack);

                        string telemetryDetails = "";
                        int payloadSize = 0;

                        if (AzureMonitorDiagnosticsEventSourceData.Log.IsEnabled(EventLevel.Verbose,
                            AzureMonitorDiagnosticsEventSourceData.Keywords.Exceptions))
                        {
                            telemetryDetails = JsonSerializer.Serialize(item);
                            payloadSize = System.Text.Encoding.UTF8.GetByteCount(telemetryDetails);
                        }

                        AzureMonitorDiagnosticsEventSourceData.Log.ExceptionTelemetryProduced(
                            exceptionType, exceptionMessage, problemId, traceId, spanId,
                            "Azure.Monitor.OpenTelemetry.Exporter", hasStackTrace, telemetryDetails, payloadSize);
                    }
                }
            }
            catch (Exception ex)
            {
                AzureMonitorDiagnosticsEventSourceData.Log.TelemetryProcessingFailed(
                    "Exception", ex.Message, "Exception parsing", traceId, spanId);
            }
        }

        ///// <summary>
        ///// Logs Metric telemetry items (custom metrics, performance counters)
        ///// </summary>
        //private void LogMetricTelemetryItem(TelemetryItem item, string traceId, string spanId)
        //{
        //    try
        //    {
        //        if (item.Data?.BaseData is MetricData metricData)
        //        {
        //            var metricName = metricData.Metrics?.FirstOrDefault()?.Name ?? "Unknown";
        //            var value = metricData.Metrics?.FirstOrDefault()?.Value ?? 0.0;

        //            string telemetryDetails = "";
        //            int payloadSize = 0;

        //            if (AzureMonitorDiagnosticsEventSourceData.Log.IsEnabled(EventLevel.Verbose,
        //                AzureMonitorDiagnosticsEventSourceData.Keywords.Metrics))
        //            {
        //                telemetryDetails = JsonSerializer.Serialize(item);
        //                payloadSize = System.Text.Encoding.UTF8.GetByteCount(telemetryDetails);
        //            }

        //            AzureMonitorDiagnosticsEventSourceData.Log.MetricTelemetryProduced(
        //                metricName, value, "", "CustomMetric", "Sum", "Counter",
        //                "Azure.Monitor.OpenTelemetry.Exporter", 1, telemetryDetails, payloadSize);
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        AzureMonitorDiagnosticsEventSourceData.Log.TelemetryProcessingFailed(
        //            "Metric", ex.Message, "Metric parsing", traceId, spanId);
        //    }
        //}

        /// <summary>
        /// Logs unknown/generic telemetry types
        /// </summary>
        private void LogGenericTelemetryItem(TelemetryItem item, string traceId, string spanId)
        {
            // For unknown telemetry types, log basic info
            AzureMonitorDiagnosticsEventSourceData.Log.TelemetryProcessingFailed(
                item.Name ?? "Unknown", "Unknown telemetry type processed",
                "Generic telemetry", traceId, spanId);
        }

        // Utility helper methods
        private string ExtractTraceId(TelemetryItem item)
        {
            return item.Tags?.TryGetValue("ai.operation.id", out var traceId) == true ? traceId : "";
        }

        private string ExtractSpanId(TelemetryItem item)
        {
            return item.Tags?.TryGetValue("ai.operation.parentId", out var spanId) == true ? spanId : "";
        }

        private string ExtractHttpMethod(RequestData requestData, IDictionary<string, string> tags)
        {
            if (requestData.Properties?.TryGetValue("httpMethod", out var method) == true)
                return method;

            // Try to extract from tags
            if (tags?.TryGetValue("ai.http.method", out var tagMethod) == true)
                return tagMethod;

            return "Unknown";
        }

        private double ParseDuration(string duration)
        {
            if (string.IsNullOrEmpty(duration))
                return 0.0;

            if (TimeSpan.TryParse(duration, out var timeSpan))
                return timeSpan.TotalMilliseconds;

            return 0.0;
        }

        private int ParseResponseCode(string responseCode)
        {
            if (string.IsNullOrEmpty(responseCode))
                return 200;

            if (int.TryParse(responseCode, out var code))
                return code;

            return 200;
        }

        private string MapSeverityLevel(SeverityLevel? severityLevel)
        {
            if (severityLevel.HasValue)
            {
                if (severityLevel.Value.Equals(SeverityLevel.Verbose))
                    return "Verbose";
                if (severityLevel.Value.Equals(SeverityLevel.Information))
                    return "Information";
                if (severityLevel.Value.Equals(SeverityLevel.Warning))
                    return "Warning";
                if (severityLevel.Value.Equals(SeverityLevel.Error))
                    return "Error";
                if (severityLevel.Value.Equals(SeverityLevel.Critical))
                    return "Critical";
            }
            return "Information";
        }

        private string ExtractCategoryName(IDictionary<string, string> properties)
        {
            return properties?.TryGetValue("CategoryName", out var categoryName) == true ? categoryName : "Unknown";
        }

        private string GenerateProblemId(TelemetryExceptionDetails exception)
        {
            var typeName = exception.TypeName ?? "Unknown";
            if (!string.IsNullOrEmpty(exception.Stack))
            {
                var firstLine = exception.Stack.Split('\n').FirstOrDefault()?.Trim();
                if (!string.IsNullOrEmpty(firstLine))
                    return $"{typeName} at {firstLine}";
            }
            return typeName;
        }

        private async Task<string> ResolveEndpointIP(string endpoint)
        {
            try
            {
                var uri = new Uri(endpoint);
                var addresses = await Dns.GetHostAddressesAsync(uri.Host).ConfigureAwait(false);
                return addresses.FirstOrDefault()?.ToString() ?? "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        private string GetEndpointUrl()
        {
            // This should return the ingestion endpoint URL like:
            // "https://dc.services.visualstudio.com/v2.1/track"
            // You'll need to adapt this based on your actual implementation
            return _connectionVars.IngestionEndpoint;
        }

        //private async Task<string> GetResponseBodyAsync(object response)
        //{
        //    try
        //    {
        //        // This depends on your actual Response type
        //        // You'll need to adapt this based on how your REST client returns responses
        //        // Typically something like: return await response.Content.ReadAsStringAsync();
        //        return response?.ToString() ?? "";
        //    }
        //    catch
        //    {
        //        return "";
        //    }
        //}

        #endregion

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    AzureMonitorExporterEventSource.Log.DisposedObject(nameof(AzureMonitorTransmitter));
                    _statsbeat?.Dispose();
                    var fileBlobProvider = _fileBlobProvider as FileBlobProvider;
                    if (fileBlobProvider != null)
                    {
                        fileBlobProvider.Dispose();
                    }
                }

                _disposed = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
