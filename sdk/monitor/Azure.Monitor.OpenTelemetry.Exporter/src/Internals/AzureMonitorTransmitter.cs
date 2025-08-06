// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.IO;
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
        private readonly DiagnosticsDnsCache? _dnsCache;

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

            // Initialize DNS cache and prewarm with ingestion endpoint
            _dnsCache = new DiagnosticsDnsCache();
            _dnsCache.PrewarmCache(_connectionVars.IngestionEndpoint);

            // Also prewarm Live Metrics and other endpoints if needed
            _dnsCache.PrewarmCache("https://rt.services.visualstudio.com");
            _dnsCache.PrewarmCache("https://snapshot.monitor.azure.com");
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
                    AzureMonitorDiagnosticsEventSourceCore.Log.OfflineStorageEnabled(connectionVars.InstrumentationKey, storageDirectory, Environment.CurrentManagedThreadId);

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

            // Create a new batch summary for this transmission, we'll use it to track types and counts of records
            var batchSummary = new TelemetryBatchSummary();

            // ADF PILLAR 1: Log telemetry production (what telemetry was created, survived sampling, went through filters/alterations and ready for ingestion)
            LogTelemetryItemsReadyForTransmission(telemetryList, origin, batchSummary);

            try
            {
                if (_transmissionStateManager.State == TransmissionState.Closed)
                {
                    if (async)
                    {
                        // ADF PILLAR 2: Log transmission attempt with batch details
                        await LogTransmissionAttempt(batchSummary, origin).ConfigureAwait(false);
                    }
                    else
                    {
                        // ADF PILLAR 2: Log transmission attempt with batch details
                        LogTransmissionAttemptSynchronous(batchSummary, origin);
                    }

                    using var httpMessage = async ?
                        await _applicationInsightsRestClient.InternalTrackAsync(telemetryItems, cancellationToken).ConfigureAwait(false) :
                        _applicationInsightsRestClient.InternalTrackAsync(telemetryItems, cancellationToken).Result;

                    result = HttpPipelineHelper.IsSuccess(httpMessage);

                    // ADF PILLAR 3: First log the raw backend response, both successes and failures here, just show it to the customer
                    LogBackendResponse(httpMessage, batchSummary, origin);

                    // I think this original logic is slightly off. What if result is failure but blob provider IS null?
                    //if (result == ExportResult.Failure && _fileBlobProvider != null)
                    //{
                    //    _transmissionStateManager.EnableBackOff(httpMessage.HasResponse ? httpMessage.Response : null);
                    //    result = HttpPipelineHelper.HandleFailures(httpMessage, _fileBlobProvider, _connectionVars, origin, _isAadEnabled);
                    //}
                    //else
                    //{
                    //    _transmissionStateManager.ResetConsecutiveErrors();
                    //    _transmissionStateManager.CloseTransmission();
                    //    AzureMonitorExporterEventSource.Log.TransmissionSuccess(origin, _isAadEnabled, _connectionVars.InstrumentationKey);
                    //}

                    if (result == ExportResult.Success)
                    {
                        _transmissionStateManager.ResetConsecutiveErrors();
                        _transmissionStateManager.CloseTransmission();
                        // ADF Note: Log nothing, we already logged the response payload, so customers will see the 200 response
                        // In the case of toggling between OpenTransmission to CloseTransmission, the backendresponse event will let customers know API is accessible again
                        // So don't think we need to log each time the CloseTransmission is toggled, just when we start writing to disk may be enough.
                    }
                    else if (result == ExportResult.Failure && _fileBlobProvider != null)
                    {
                        _transmissionStateManager.EnableBackOff(httpMessage.HasResponse ? httpMessage.Response : null); // ADF: < In statemgr we log backoff retry starting
                        result = HttpPipelineHelper.HandleFailures(httpMessage, _fileBlobProvider, _connectionVars, origin, _isAadEnabled);
                    }
                    else if (result == ExportResult.Failure)
                    {
                        // Handle failure case when no file provider is available
                        // Maybe log a different event or take other action
                        AzureMonitorExporterEventSource.Log.TransmitterFailed(origin, _isAadEnabled, _connectionVars.InstrumentationKey, new Exception("Transmission failed and writing to disk failed resulting in dropped telemetry."));
                    }
                }
                else
                {
                    byte[] requestContent = HttpPipelineHelper.GetSerializedContent(telemetryItems);
                    if (_fileBlobProvider != null)
                    {
                        result = _fileBlobProvider.SaveTelemetry(requestContent);
                        if (result == ExportResult.Success)
                        {
                            // Agent is in backoff state - log that we've successfully persisted the telemetryitems to disk instead
                            LogStoragePersistence(batchSummary, origin);
                        }
                    }
                    else
                    {
                        // This else means we are in backoff mode, writing data to disk but customer does
                        // not have persistent storage enabled, so we just drop those records, lets inform
                        // customer about that here.
                        //AzureMonitorExporterEventSource.Log.FailedToSaveTelemetryToStorage(
                        //    "No offline storage provider configured", _connectionVars.InstrumentationKey);
                        result = ExportResult.Failure;
                    }
                }
            }
            catch (Exception ex)
            {
                // Log transmission failure with batch details. This catch happens if we fail to write items to disk too
                LogTransmissionFailure(ex, batchSummary, origin);
                AzureMonitorExporterEventSource.Log.TransmitterFailed(origin, _isAadEnabled, _connectionVars.InstrumentationKey, ex);
            }

            return result;
        }

        #region Agent Diagnostics Framework - Pillar 1 - Telemetry Production Events

        /// <summary>
        /// Logs all telemetry items using ADF Pillar 1 (What telemetry did your app produce and attempt to send?)
        /// </summary>
        private void LogTelemetryItemsReadyForTransmission(List<TelemetryItem> telemetryItems, TelemetryItemOrigin origin, TelemetryBatchSummary batchSummary)
        {
            // Only log if diagnostics are enabled to avoid performance impact
            if (!AzureMonitorDiagnosticsEventSourceData.Log.IsEnabled())
                return;

            // Reset the batch summary for this transmission
            batchSummary.Reset();

            foreach (var item in telemetryItems)
            {
                try
                {
                    LogSingleTelemetryItem(item, origin, batchSummary);
                }
                catch (Exception ex)
                {
                    // Count as unknown if we can't process it
                    batchSummary.UnknownCount++;

                    AzureMonitorDiagnosticsEventSourceData.Log.TelemetryProcessingFailed(
                        item.Name ?? "Unknown", ex.Message, "Pillar 1 logging",
                        ExtractTraceId(item), ExtractSpanId(item));
                }
            }
        }

        /// <summary>
        /// Logs a single telemetry item based on its type
        /// </summary>
        private void LogSingleTelemetryItem(TelemetryItem item, TelemetryItemOrigin origin, TelemetryBatchSummary batchSummary)
        {
            var telemetryType = item.Name?.ToLowerInvariant() ?? "unknown";
            var traceId = ExtractTraceId(item);
            var spanId = ExtractSpanId(item);

            switch (telemetryType)
            {
                case "request":
                    batchSummary.RequestCount++;
                    LogRequestTelemetryItem(item, traceId, spanId, origin);
                    break;

                case "remotedependency":
                    batchSummary.DependencyCount++;
                    LogDependencyTelemetryItem(item, traceId, spanId, origin);
                    break;

                case "message":
                    batchSummary.TraceCount++;
                    LogTraceTelemetryItem(item, traceId, spanId, origin);
                    break;

                case "exception":
                    batchSummary.ExceptionCount++;
                    LogExceptionTelemetryItem(item, traceId, spanId, origin);
                    break;

                case "metric":
                    batchSummary.MetricCount++;
                    LogMetricTelemetryItem(item, traceId, spanId, origin);
                    break;

                // TODO: Add options for perfcounters, availability tests, pageviews or other supported types
                default:
                    batchSummary.UnknownCount++;
                    LogGenericTelemetryItem(item, traceId, spanId, origin);
                    break;
            }
        }

        /// <summary>
        /// Logs Request telemetry items (HTTP requests, incoming calls, inbound Service Bus messages)
        /// </summary>
        private void LogRequestTelemetryItem(TelemetryItem item, string traceId, string spanId, TelemetryItemOrigin origin)
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
                    var telemetryTimestamp = item.Time.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ");

                    //string telemetryDetails = "";
                    int payloadSize = 0;

                    try
                    {
                        using var content = new NDJsonWriter();
                        content.JsonWriter.WriteObjectValue(item);
                        content.JsonWriter.Flush();
                        payloadSize = content.ToBytes().ToArray().Length;
                    }
                    catch { payloadSize = 0; }

                    //telemetryDetails = System.Text.Encoding.UTF8.GetString(content.ToBytes().ToArray());

                    // Store the TelemetryItem in cache and pass the ID
                    var telemetryDataId = Guid.NewGuid().ToString();
                    TelemetryDataCache.Store(telemetryDataId, item);

                    //if (AzureMonitorDiagnosticsEventSourceData.Log.IsEnabled(EventLevel.Verbose,
                    //    AzureMonitorDiagnosticsEventSourceData.Keywords.Requests))
                    //{
                    //    telemetryDetails = JsonSerializer.Serialize(item);
                    //    payloadSize = System.Text.Encoding.UTF8.GetByteCount(telemetryDetails);
                    //}

                    AzureMonitorDiagnosticsEventSourceData.Log.Request(
                        operationName, httpMethod, url, duration, responseCode, success,
                        traceId, spanId, "Server", "Azure.Monitor.OpenTelemetry.Exporter",
                        origin.ToString(), payloadSize, telemetryTimestamp, telemetryDataId, Environment.CurrentManagedThreadId);
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
        private void LogDependencyTelemetryItem(TelemetryItem item, string traceId, string spanId, TelemetryItemOrigin origin)
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
                    var telemetryTimestamp = item.Time.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ");

                    //string telemetryDetails = "";
                    int payloadSize = 0;

                    try
                    {
                        using var content = new NDJsonWriter();
                        content.JsonWriter.WriteObjectValue(item);
                        content.JsonWriter.Flush();
                        payloadSize = content.ToBytes().ToArray().Length;
                    }
                    catch { payloadSize = 0; }

                    //telemetryDetails = System.Text.Encoding.UTF8.GetString(content.ToBytes().ToArray());
                    //payloadSize = content.ToBytes().ToArray().Length;

                    // Store the TelemetryItem in cache and pass the ID
                    var telemetryDataId = Guid.NewGuid().ToString();
                    TelemetryDataCache.Store(telemetryDataId, item);

                    //if (AzureMonitorDiagnosticsEventSourceData.Log.IsEnabled(EventLevel.Verbose,
                    //    AzureMonitorDiagnosticsEventSourceData.Keywords.Dependencies))
                    //{
                    //    telemetryDetails = JsonSerializer.Serialize(item);
                    //    payloadSize = System.Text.Encoding.UTF8.GetByteCount(telemetryDetails);
                    //}

                    AzureMonitorDiagnosticsEventSourceData.Log.RemoteDependency(
                        dependencyName, dependencyType, target, data, duration, success, resultCode,
                        traceId, spanId, "Client", "Azure.Monitor.OpenTelemetry.Exporter",
                        origin.ToString(), payloadSize, telemetryTimestamp, telemetryDataId, Environment.CurrentManagedThreadId);
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
        private void LogTraceTelemetryItem(TelemetryItem item, string traceId, string spanId, TelemetryItemOrigin origin)
        {
            try
            {
                if (item.Data?.BaseData is MessageData messageData)
                {
                    var message = messageData.Message ?? "Unknown";
                    var severity = MapSeverityLevel(messageData.SeverityLevel);
                    var categoryName = ExtractCategoryName(messageData.Properties);
                    var telemetryTimestamp = item.Time.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ");

                    //string telemetryDetails = "";
                    int payloadSize = 0;

                    try
                    {
                        using var content = new NDJsonWriter();
                        content.JsonWriter.WriteObjectValue(item);
                        content.JsonWriter.Flush();
                        payloadSize = content.ToBytes().ToArray().Length;
                    }
                    catch { payloadSize = 0; }

                    //telemetryDetails = System.Text.Encoding.UTF8.GetString(content.ToBytes().ToArray());

                    // Store the TelemetryItem in cache and pass the ID
                    var telemetryDataId = Guid.NewGuid().ToString();
                    TelemetryDataCache.Store(telemetryDataId, item);

                    //// Considered maybe only log raw logs if verbose mode enabled?
                    //if (AzureMonitorDiagnosticsEventSourceData.Log.IsEnabled(EventLevel.Verbose,
                    //    AzureMonitorDiagnosticsEventSourceData.Keywords.Traces))
                    //{
                    //    using var content = new NDJsonWriter();
                    //    content.JsonWriter.WriteObjectValue(item);
                    //    content.JsonWriter.Flush(); // Ensure all data is written to the stream

                    //    telemetryDetails = System.Text.Encoding.UTF8.GetString(content.ToBytes().ToArray());
                    //    payloadSize = content.ToBytes().ToArray().Length;
                    //}
                    //else
                    //{
                    //    telemetryDetails = "Enable verbose logging in OTEL_DIAGNOSTICS.json to view full telemetry payloads.";
                    //}

                    AzureMonitorDiagnosticsEventSourceData.Log.Message(
                        message, severity, categoryName, traceId, spanId,
                        "Microsoft.Extensions.Logging", "Azure.Monitor.OpenTelemetry.Exporter",
                        origin.ToString(), payloadSize, telemetryTimestamp, telemetryDataId, Environment.CurrentManagedThreadId);
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
        private void LogExceptionTelemetryItem(TelemetryItem item, string traceId, string spanId, TelemetryItemOrigin origin)
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
                        var telemetryTimestamp = item.Time.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ");

                        //string telemetryDetails = "";
                        int payloadSize = 0;

                        try
                        {
                            using var content = new NDJsonWriter();
                            content.JsonWriter.WriteObjectValue(item);
                            content.JsonWriter.Flush();
                            payloadSize = content.ToBytes().ToArray().Length;
                        }
                        catch { payloadSize = 0; }

                        //telemetryDetails = System.Text.Encoding.UTF8.GetString(content.ToBytes().ToArray());
                        //payloadSize = content.ToBytes().ToArray().Length;

                        // Store the TelemetryItem in cache and pass the ID
                        var telemetryDataId = Guid.NewGuid().ToString();
                        TelemetryDataCache.Store(telemetryDataId, item);

                        //if (AzureMonitorDiagnosticsEventSourceData.Log.IsEnabled(EventLevel.Verbose,
                        //    AzureMonitorDiagnosticsEventSourceData.Keywords.Exceptions))
                        //{
                        //    telemetryDetails = JsonSerializer.Serialize(item);
                        //    payloadSize = System.Text.Encoding.UTF8.GetByteCount(telemetryDetails);
                        //}

                        AzureMonitorDiagnosticsEventSourceData.Log.Exception(
                            exceptionType, exceptionMessage, problemId, traceId, spanId,
                            "Azure.Monitor.OpenTelemetry.Exporter", hasStackTrace, origin.ToString(),
                            payloadSize, telemetryTimestamp, telemetryDataId, Environment.CurrentManagedThreadId);
                    }
                }
            }
            catch (Exception ex)
            {
                AzureMonitorDiagnosticsEventSourceData.Log.TelemetryProcessingFailed(
                    "Exception", ex.Message, "Exception parsing", traceId, spanId);
            }
        }

        /// <summary>
        /// Logs Metric telemetry items (custom metrics, performance counters)
        /// </summary>
        private void LogMetricTelemetryItem(TelemetryItem item, string traceId, string spanId, TelemetryItemOrigin origin)
        {
            try
            {
                // Metrics are not serializing as I'd expect, may require more investigation
                // I will share what this data type should look like in the specification doc and
                // review serialization implementation later.
                if (item.Data?.BaseData is Azure.Monitor.OpenTelemetry.Exporter.Models.MetricsData metricData)
                {
                    var metricName = metricData.Metrics?.FirstOrDefault()?.Name ?? "Unknown";
                    var value = metricData.Metrics?.FirstOrDefault()?.Value ?? 0.0;
                    var telemetryTimestamp = item.Time.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ");

                    //string telemetryDetails = "";
                    int payloadSize = 0;

                    try
                    {
                        using var content = new NDJsonWriter();
                        content.JsonWriter.WriteObjectValue(item);
                        content.JsonWriter.Flush();
                        payloadSize = content.ToBytes().ToArray().Length;
                    }
                    catch { payloadSize = 0; }

                    // Store the TelemetryItem in cache and pass the ID
                    var telemetryDataId = Guid.NewGuid().ToString();
                    TelemetryDataCache.Store(telemetryDataId, item);

                    //            if (AzureMonitorDiagnosticsEventSourceData.Log.IsEnabled(EventLevel.Verbose,
                    //                AzureMonitorDiagnosticsEventSourceData.Keywords.Metrics))
                    //            {
                    //                telemetryDetails = JsonSerializer.Serialize(item);
                    //                payloadSize = System.Text.Encoding.UTF8.GetByteCount(telemetryDetails);
                    //            }

                    AzureMonitorDiagnosticsEventSourceData.Log.Metric(
                        metricName, value, "", "CustomMetric", "Sum", "Counter",
                        "Azure.Monitor.OpenTelemetry.Exporter", 1, origin.ToString(), payloadSize,
                        telemetryTimestamp, telemetryDataId, Environment.CurrentManagedThreadId);
                }
            }
            catch (Exception ex)
            {
                AzureMonitorDiagnosticsEventSourceData.Log.TelemetryProcessingFailed(
                    "Metric", ex.Message, "Metric parsing", traceId, spanId);
            }
        }

        /// <summary>
        /// Logs unknown/generic telemetry types
        /// </summary>
        private void LogGenericTelemetryItem(TelemetryItem item, string traceId, string spanId, TelemetryItemOrigin origin)
        {
            // For unknown telemetry types, log basic info
            AzureMonitorDiagnosticsEventSourceData.Log.TelemetryProcessingFailed(
                item.Name ?? "Unknown", "Unknown telemetry type processed",
                "Generic telemetry", traceId, spanId);
        }

        #endregion

        #region Agent Diagnostics Framework - Pillar 2 - What and where are the telemetry items going?

        // Agent Diagnostics Framework Pillar 2 methods
        private async Task LogTransmissionAttempt(TelemetryBatchSummary batchSummary, TelemetryItemOrigin origin)
        {
            if (!AzureMonitorDiagnosticsEventSourceExporter.Log.IsEnabled())
                return;

            try
            {
                var endpoint = GetEndpointUrl();
                var resolvedIP = await ResolveEndpointIP(endpoint).ConfigureAwait(false);
                var batchDescription = batchSummary.GetSummaryString();
                //var estimatedPayloadSize = EstimatePayloadSize(batchSummary);
                var counts = batchSummary.GetCountsDictionary();

                // Log the transmission attempt with detailed batch composition
                AzureMonitorDiagnosticsEventSourceExporter.Log.TransmissionAttempt(
                    endpoint, resolvedIP, batchSummary.TotalCount, batchDescription,
                    counts["Request"], counts["Dependency"], counts["Trace"], counts["Metric"], Environment.CurrentManagedThreadId);
            }
            catch (Exception ex)
            {
                AzureMonitorDiagnosticsEventSourceExporter.Log.ExporterException(
                    "TransmissionAttempt", ex.GetType().Name, ex.Message, ex.StackTrace ?? string.Empty, Environment.CurrentManagedThreadId);
            }
        }

        // Synchronous version for when async=false
        private void LogTransmissionAttemptSynchronous(TelemetryBatchSummary batchSummary, TelemetryItemOrigin origin)
        {
            if (!AzureMonitorDiagnosticsEventSourceExporter.Log.IsEnabled())
                return;

            try
            {
                var endpoint = GetEndpointUrl();
                var resolvedIP = _dnsCache?.GetResolvedIP(endpoint) ?? "Unknown"; // Use DNS cache object in sync mode
                var batchDescription = batchSummary.GetSummaryString();
                //var estimatedPayloadSize = EstimatePayloadSize(batchSummary);

                var counts = batchSummary.GetCountsDictionary();

                // Log the transmission attempt with detailed batch composition
                AzureMonitorDiagnosticsEventSourceExporter.Log.TransmissionAttempt(
                    endpoint, resolvedIP, batchSummary.TotalCount, batchDescription,
                    counts["Request"], counts["Dependency"], counts["Trace"], counts["Metric"], Environment.CurrentManagedThreadId);

                //// Log detailed batch composition if verbose logging is enabled (todo: adding as informational for now)
                //if (AzureMonitorDiagnosticsEventSourceExporter.Log.IsEnabled(EventLevel.Informational, EventKeywords.None))
                //{
                //    var counts = batchSummary.GetCountsDictionary();
                //    AzureMonitorDiagnosticsEventSourceExporter.Log.TransmissionBatchDetails(
                //        endpoint, batchDescription,
                //        counts["Request"], counts["Dependency"], counts["Trace"], counts["Metric"], Environment.CurrentManagedThreadId);
                //}
            }
            catch (Exception ex)
            {
                AzureMonitorDiagnosticsEventSourceExporter.Log.ExporterException(
                    "TransmissionAttempt", ex.GetType().Name, ex.Message, ex.StackTrace ?? string.Empty, Environment.CurrentManagedThreadId);
            }
        }

        #endregion

        #region Agent Diagnostics Framework - Pillar 3 - What was the response from the backend endpoints?

        private void LogBackendResponse(object httpMessage, TelemetryBatchSummary batchSummary, TelemetryItemOrigin origin)
        {
            if (!AzureMonitorDiagnosticsEventSourceExporter.Log.IsEnabled())
                return;

            try
            {
                // Extract response details from httpMessage
                // You'll need to adapt this based on your actual HttpMessage type
                var statusCode = ExtractStatusCode(httpMessage);
                var responseBodyObject = ExtractResponseBody(httpMessage);
                var duration = ExtractDuration(httpMessage); // in milliseconds
                var endpoint = GetEndpointUrl();
                var requestId = ExtractRequestId(httpMessage); // Optional, if available
                var batchDescription = batchSummary.GetSummaryString();

                // Serialize the response body object to JSON string for EventSource
                string serializedResponseBody;
                try
                {
                    serializedResponseBody = responseBodyObject != null ?
                        JsonSerializer.Serialize(responseBodyObject, new JsonSerializerOptions { WriteIndented = false }) :
                        "null";
                }
                catch (Exception serializationEx)
                {
                    // Fallback if serialization fails
                    serializedResponseBody = responseBodyObject?.ToString() ?? "null";
                    AzureMonitorDiagnosticsEventSourceExporter.Log.ExporterException(
                        "ResponseBodySerialization", serializationEx.GetType().Name, serializationEx.Message,
                        serializationEx.StackTrace ?? string.Empty, Environment.CurrentManagedThreadId);
                }

                AzureMonitorDiagnosticsEventSourceExporter.Log.BackendResponseReceived(
                    statusCode, duration, endpoint, batchDescription, requestId, serializedResponseBody ?? "Failure extracting response body", Environment.CurrentManagedThreadId);

                //// Parse response details for more specific logging
                //if (statusCode >= 200 && statusCode < 300)
                //{
                //    if (TryParseSuccessResponse(responseBody, out var received, out var accepted, out var rejected))
                //    {
                //        if (rejected > 0)
                //        {
                //            var rejectionReasons = ExtractRejectionReasons(responseBody);
                //            AzureMonitorDiagnosticsEventSourceExporter.Log.BackendPartialSuccess(
                //                accepted, rejected, endpoint, rejectionReasons);
                //        }
                //        else
                //        {
                //            AzureMonitorDiagnosticsEventSourceExporter.Log.BackendAcceptedTelemetry(
                //                received, accepted, rejected, endpoint);
                //        }
                //    }
                //}
                //else if (statusCode == 429 || statusCode == 439) // Throttling
                //{
                //    var retryAfter = ExtractRetryAfter(responseBody);
                //    var reason = ExtractThrottlingReason(responseBody);
                //    AzureMonitorDiagnosticsEventSourceExporter.Log.BackendThrottlingResponse(
                //        statusCode, retryAfter, endpoint, reason);
                //}
                //else if (statusCode >= 400)
                //{
                //    var errorMessage = ExtractErrorMessage(responseBody);
                //    AzureMonitorDiagnosticsEventSourceExporter.Log.BackendErrorResponse(
                //        statusCode, errorMessage, endpoint, responseBody);
                //}
            }
            catch (Exception ex)
            {
                AzureMonitorDiagnosticsEventSourceExporter.Log.ExporterException(
                    "BackendResponse", ex.GetType().Name, ex.Message, ex.StackTrace ?? string.Empty, Environment.CurrentManagedThreadId);
            }
        }

        private void LogStoragePersistence(TelemetryBatchSummary batchSummary, TelemetryItemOrigin origin)
        {
            if (!AzureMonitorDiagnosticsEventSourceExporter.Log.IsEnabled())
                return;

            try
            {
                var batchDescription = batchSummary.GetSummaryString();
                var estimatedSize = EstimatePayloadSize(batchSummary);
                var storageDirectory = _fileBlobProvider?.ToString() ?? "Unknown";

                AzureMonitorDiagnosticsEventSourceExporter.Log.TelemetryPersistedToDisk(
                    $"batch_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json",
                    batchSummary.TotalCount,
                    estimatedSize,
                    storageDirectory);
            }
            catch (Exception ex)
            {
                AzureMonitorDiagnosticsEventSourceExporter.Log.ExporterException(
                    "StoragePersistence", ex.GetType().Name, ex.Message, ex.StackTrace ?? string.Empty, Environment.CurrentManagedThreadId);
            }
        }

        private void LogTransmissionFailure(Exception exception, TelemetryBatchSummary batchSummary, TelemetryItemOrigin origin)
        {
            if (!AzureMonitorDiagnosticsEventSourceExporter.Log.IsEnabled())
                return;

            try
            {
                var endpoint = GetEndpointUrl();
                var batchDescription = batchSummary.GetSummaryString();

                AzureMonitorDiagnosticsEventSourceExporter.Log.TransmissionFailed(
                    endpoint, exception.Message, exception.GetType().Name, 0);
            }
            catch
            {
                // Swallow exceptions in error logging to prevent cascading failures
            }
        }

        #endregion

        #region Agent Diagnostics Framework - Helper Methods

        // Helper methods for response processing
        private int EstimatePayloadSize(TelemetryBatchSummary batchSummary)
        {
            // Rough estimation based on telemetry types
            // Adjust these estimates based on your actual data
            var estimatedSize = 0;
            estimatedSize += batchSummary.RequestCount * 1200;      // ~1.2KB per request
            estimatedSize += batchSummary.DependencyCount * 800;    // ~0.8KB per dependency
            estimatedSize += batchSummary.TraceCount * 600;         // ~0.6KB per trace
            estimatedSize += batchSummary.ExceptionCount * 2000;    // ~2KB per exception (stack traces)
            estimatedSize += batchSummary.MetricCount * 400;        // ~0.4KB per metric
            estimatedSize += batchSummary.UnknownCount * 500;       // ~0.5KB per unknown item

            return estimatedSize;
        }

        // Implementation for parsing Azure.Core.HttpMessage responses
        private int ExtractStatusCode(object httpMessage)
        {
            try
            {
                if (httpMessage is Azure.Core.HttpMessage azureHttpMessage &&
                    azureHttpMessage.HasResponse &&
                    azureHttpMessage.Response != null)
                {
                    return azureHttpMessage.Response.Status;
                }
            }
            catch (Exception ex)
            {
                // Log parsing error but don't throw - diagnostic info shouldn't break the pipeline
                AzureMonitorDiagnosticsEventSourceExporter.Log.ExporterException(
                    "ExtractStatusCode", ex.GetType().Name, ex.Message,
                    ex.StackTrace ?? string.Empty, Environment.CurrentManagedThreadId);
            }

            return 0; // Unknown/no response
        }

        //private string ExtractResponseBody(object httpMessage)
        //{
        //    try
        //    {
        //        if (httpMessage is Azure.Core.HttpMessage azureHttpMessage &&
        //            azureHttpMessage.HasResponse &&
        //            azureHttpMessage.Response?.Content != null)
        //        {
        //            // BinaryData.ToString() returns the content as UTF-8 string
        //            var content = azureHttpMessage.Response.Content.ToString();

        //            // Limit response body size for diagnostic logs to prevent excessive log volume
        //            const int maxBodyLength = 1024; // 1KB limit
        //            if (content.Length > maxBodyLength)
        //            {
        //                return content.Substring(0, maxBodyLength) + "... [truncated]";
        //            }

        //            return content;
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        // Log parsing error but don't throw
        //        AzureMonitorDiagnosticsEventSourceExporter.Log.ExporterException(
        //            "ExtractResponseBody", ex.GetType().Name, ex.Message,
        //            ex.StackTrace ?? string.Empty, Environment.CurrentManagedThreadId);

        //        return "Error reading response body";
        //    }

        //    return string.Empty; // No response or no content
        //}

        private object? ExtractResponseBody(object httpMessage)
        {
            try
            {
                if (httpMessage is Azure.Core.HttpMessage azureHttpMessage &&
                    azureHttpMessage.HasResponse &&
                    azureHttpMessage.Response?.Content != null)
                {
                    // Get the raw content as string
                    var rawContent = azureHttpMessage.Response.Content.ToString();

                    if (string.IsNullOrEmpty(rawContent))
                    {
                        return new { message = "Empty response body" };
                    }

                    // Try to parse as JSON first
                    if (TryParseAsJsonObject(rawContent, out var jsonObject))
                    {
                        return jsonObject;
                    }
                    else
                    {
                        // Not JSON - treat as plain text, but still limit size
                        const int maxBodyLength = 1024;
                        if (rawContent.Length > maxBodyLength)
                        {
                            return new
                            {
                                message = "Non-JSON response (truncated)",
                                content = rawContent.Substring(0, maxBodyLength),
                                truncated = true,
                                originalLength = rawContent.Length
                            };
                        }
                        else
                        {
                            return new
                            {
                                message = "Non-JSON response",
                                content = rawContent
                            };
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Log parsing error but don't throw
                AzureMonitorDiagnosticsEventSourceExporter.Log.ExporterException(
                    "ExtractResponseBody", ex.GetType().Name, ex.Message,
                    ex.StackTrace ?? string.Empty, Environment.CurrentManagedThreadId);

                return new { error = "Error reading response body", exception = ex.Message };
            }

            return new { message = "No response or no content" };
        }

        private bool TryParseAsJsonObject(string content, out object? jsonObject)
        {
            jsonObject = null;

            try
            {
                // For large JSON responses that might get truncated, we need to be smart about it
                const int maxContentLength = 8192; // 8KB - much larger than before to accommodate 500 items

                string contentToParse = content;
                bool wasTruncated = false;

                if (content.Length > maxContentLength)
                {
                    // Find a good truncation point that preserves JSON structure
                    contentToParse = TruncateJsonSafely(content, maxContentLength);
                    wasTruncated = true;
                }

                // Try to parse the JSON
                using var document = JsonDocument.Parse(contentToParse);
                var rootElement = document.RootElement;

                // Convert to a regular object that can be serialized properly
                var result = ConvertJsonElementToObject(rootElement);

                // If we truncated, add metadata about truncation
                if (wasTruncated)
                {
                    if (result is Dictionary<string, object> dict)
                    {
                        dict["_truncated"] = true;
                        dict["_originalLength"] = content.Length;
                        dict["_truncatedLength"] = contentToParse.Length;
                    }
                }

                jsonObject = result;
                return true;
            }
            catch (JsonException)
            {
                // Not valid JSON
                return false;
            }
            catch (Exception)
            {
                // Other parsing errors
                return false;
            }
        }

        private string TruncateJsonSafely(string json, int maxLength)
        {
            if (json.Length <= maxLength)
                return json;

            // Try to truncate at a reasonable JSON boundary
            string truncated = json.Substring(0, maxLength);

            // Look for the last complete JSON object/array element
            int lastComma = truncated.LastIndexOf(',');
            int lastCloseBrace = truncated.LastIndexOf('}');
            int lastCloseBracket = truncated.LastIndexOf(']');

            // Find the best truncation point
            int truncateAt = Math.Max(Math.Max(lastComma, lastCloseBrace), lastCloseBracket);

            if (truncateAt > maxLength / 2) // Only use it if it's not too short
            {
                truncated = json.Substring(0, truncateAt + 1);
            }

            // Try to close any unclosed structures
            int openBraces = 0;
            int openBrackets = 0;

            foreach (char c in truncated)
            {
                switch (c)
                {
                    case '{':
                        openBraces++;
                        break;
                    case '}':
                        openBraces--;
                        break;
                    case '[':
                        openBrackets++;
                        break;
                    case ']':
                        openBrackets--;
                        break;
                }
            }

            // Close unclosed structures
            for (int i = 0; i < openBrackets; i++)
                truncated += "]";
            for (int i = 0; i < openBraces; i++)
                truncated += "}";

            return truncated;
        }

        private object? ConvertJsonElementToObject(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    var dict = new Dictionary<string, object?>();
                    foreach (var property in element.EnumerateObject())
                    {
                        dict[property.Name] = ConvertJsonElementToObject(property.Value);
                    }
                    return dict;

                case JsonValueKind.Array:
                    var list = new List<object?>();
                    foreach (var item in element.EnumerateArray())
                    {
                        list.Add(ConvertJsonElementToObject(item));
                    }
                    return list;

                case JsonValueKind.String:
                    return element.GetString() ?? string.Empty;

                case JsonValueKind.Number:
                    if (element.TryGetInt32(out int intValue))
                        return intValue;
                    if (element.TryGetInt64(out long longValue))
                        return longValue;
                    if (element.TryGetDouble(out double doubleValue))
                        return doubleValue;
                    return element.GetRawText();

                case JsonValueKind.True:
                    return true;

                case JsonValueKind.False:
                    return false;

                case JsonValueKind.Null:
                    return null;

                default:
                    return element.GetRawText();
            }
        }

        private int ExtractDuration(object httpMessage)
        {
            try
            {
                if (httpMessage is Azure.Core.HttpMessage azureHttpMessage)
                {
                    var processingStartTime = azureHttpMessage.ProcessingContext.StartTime;
                    var endTime = DateTimeOffset.UtcNow;

                    // Calculate duration from start of processing to now
                    var duration = endTime - processingStartTime;

                    // Return duration in milliseconds, ensuring non-negative
                    return Math.Max(0, (int)duration.TotalMilliseconds);
                }
            }
            catch (Exception ex)
            {
                // Log parsing error but don't throw
                AzureMonitorDiagnosticsEventSourceExporter.Log.ExporterException(
                    "ExtractDuration", ex.GetType().Name, ex.Message,
                    ex.StackTrace ?? string.Empty, Environment.CurrentManagedThreadId);
            }

            return 0; // Unknown duration
        }

        private string ExtractRequestId(object httpMessage)
        {
            try
            {
                if (httpMessage is Azure.Core.HttpMessage azureHttpMessage)
                {
                    var requestid = azureHttpMessage.Request.ClientRequestId ?? "unknown";

                    return requestid;
                }
            }
            catch (Exception ex)
            {
                // Log parsing error but don't throw
                AzureMonitorDiagnosticsEventSourceExporter.Log.ExporterException(
                    "ExtractRequestId", ex.GetType().Name, ex.Message,
                    ex.StackTrace ?? string.Empty, Environment.CurrentManagedThreadId);
            }

            return "unknown"; // Unknown requestId
        }

        // Response parsing methods (implement based on your backend response format)
        private bool TryParseSuccessResponse(string responseBody, out int received, out int accepted, out int rejected)
        {
            received = accepted = rejected = 0;
            // Implement based on your backend response format
            return false; // Placeholder
        }

        private string ExtractRejectionReasons(string responseBody)
        {
            return "Items rejected by backend"; // Placeholder
        }

        private int ExtractRetryAfter(string responseBody)
        {
            return 60000; // Default 60 seconds
        }

        private string ExtractThrottlingReason(string responseBody)
        {
            return "Rate limit exceeded"; // Placeholder
        }

        private string ExtractErrorMessage(string responseBody)
        {
            return responseBody; // Placeholder
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
                    _dnsCache?.Dispose();
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
