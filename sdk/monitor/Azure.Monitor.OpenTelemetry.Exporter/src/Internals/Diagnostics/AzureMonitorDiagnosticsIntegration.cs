using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;


namespace Azure.Monitor.OpenTelemetry.Exporter.Internals.Diagnostics
{
    /// <summary>
    /// Integration class that demonstrates how to wire the ADF diagnostic components
    /// into the Azure Monitor exporter pipeline.
    /// </summary>
    internal static class AzureMonitorDiagnosticsIntegration
    {
        private static readonly Lazy<AzureMonitorDiagnosticsEventListener> LazyEventListener =
            new(() => new AzureMonitorDiagnosticsEventListener(), LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>
        /// Gets the singleton event listener instance.
        /// </summary>
        public static AzureMonitorDiagnosticsEventListener EventListener => LazyEventListener.Value;

        /// <summary>
        /// Initializes the ADF diagnostic framework during exporter startup.
        /// Call this method from AzureMonitorTraceExporter constructor or similar initialization point.
        /// </summary>
        public static void Initialize()
        {
            try
            {
                // Initialize the event listener (this will start config polling)
                _ = EventListener;

                // Log agent startup information
                AzureMonitorDiagnosticsCoreEventSource.Shared.LogAgentStartupWithEnvironment();

                // Log initial performance metrics
                AzureMonitorDiagnosticsCoreEventSource.Shared.LogPerformanceMetrics();
            }
            catch (Exception ex)
            {
                // Log initialization error but don't throw to avoid breaking the main application
                AzureMonitorDiagnosticsCoreEventSource.Shared.UnhandledException(
                    ex.GetType().Name, ex.Message, ex.StackTrace ?? string.Empty);
            }
        }

        /// <summary>
        /// Logs telemetry production (Pillar 1) - call when telemetry is created.
        /// </summary>
        /// <param name="telemetryType">Type of telemetry (Request, Dependency, Trace, etc.)</param>
        /// <param name="telemetryData">The actual telemetry object</param>
        /// <param name="traceId">W3C Trace ID if available</param>
        /// <param name="spanId">W3C Span ID if available</param>
        public static void LogTelemetryProduced(string telemetryType, object telemetryData,
            string? traceId = null, string? spanId = null)
        {
            try
            {
                AzureMonitorDiagnosticsDataEventSource.Shared.LogTelemetryProduction(
                    telemetryType, telemetryData, traceId, spanId);
            }
            catch (Exception ex)
            {
                AzureMonitorDiagnosticsCoreEventSource.Shared.UnhandledException(
                    ex.GetType().Name, ex.Message, ex.StackTrace ?? string.Empty);
            }
        }

        /// <summary>
        /// Logs transmission attempt (Pillar 2) - call before sending HTTP request.
        /// </summary>
        /// <param name="endpoint">The ingestion endpoint URL</param>
        /// <param name="resolvedIP">The resolved IP address</param>
        /// <param name="telemetryBatch">The batch of telemetry being sent</param>
        public static void LogTransmissionAttempt(string endpoint, string resolvedIP,
            IEnumerable<object> telemetryBatch)
        {
            try
            {
                AzureMonitorDiagnosticsExporterEventSource.Shared.LogTransmissionAttempt(
                    endpoint, resolvedIP, telemetryBatch);
            }
            catch (Exception ex)
            {
                AzureMonitorDiagnosticsCoreEventSource.Shared.UnhandledException(
                    ex.GetType().Name, ex.Message, ex.StackTrace ?? string.Empty);
            }
        }

        /// <summary>
        /// Logs backend response (Pillar 3) - call after receiving HTTP response.
        /// </summary>
        /// <param name="statusCode">HTTP status code</param>
        /// <param name="responseBody">Response body from the server</param>
        /// <param name="endpoint">The endpoint that was called</param>
        /// <param name="durationMs">Request duration in milliseconds</param>
        public static void LogBackendResponse(int statusCode, string responseBody,
            string endpoint, int durationMs)
        {
            try
            {
                AzureMonitorDiagnosticsExporterEventSource.Shared.LogBackendResponse(
                    statusCode, responseBody, endpoint, durationMs);
            }
            catch (Exception ex)
            {
                AzureMonitorDiagnosticsCoreEventSource.Shared.UnhandledException(
                    ex.GetType().Name, ex.Message, ex.StackTrace ?? string.Empty);
            }
        }

        /// <summary>
        /// Logs transmission retry attempts.
        /// </summary>
        /// <param name="retryCount">Current retry attempt number</param>
        /// <param name="delay">Delay before retry</param>
        /// <param name="endpoint">The endpoint being retried</param>
        /// <param name="exception">The exception that caused the retry</param>
        public static void LogTransmissionRetry(int retryCount, TimeSpan delay,
            string endpoint, Exception exception)
        {
            try
            {
                AzureMonitorDiagnosticsExporterEventSource.Shared.LogTransmissionRetry(
                    retryCount, delay, endpoint, exception);
            }
            catch (Exception ex)
            {
                AzureMonitorDiagnosticsCoreEventSource.Shared.UnhandledException(
                    ex.GetType().Name, ex.Message, ex.StackTrace ?? string.Empty);
            }
        }

        /// <summary>
        /// Logs configuration loading events.
        /// </summary>
        /// <param name="configPath">Path to the configuration file or source</param>
        /// <param name="success">Whether configuration loading succeeded</param>
        /// <param name="errorMessage">Error message if loading failed</param>
        public static void LogConfigurationLoading(string configPath, bool success, string? errorMessage = null)
        {
            try
            {
                AzureMonitorDiagnosticsCoreEventSource.Shared.LogConfigurationLoading(configPath, success, errorMessage);
            }
            catch (Exception ex)
            {
                AzureMonitorDiagnosticsCoreEventSource.Shared.UnhandledException(
                    ex.GetType().Name, ex.Message, ex.StackTrace ?? string.Empty);
            }
        }

        /// <summary>
        /// Logs connection endpoint resolution.
        /// </summary>
        /// <param name="ingestionEndpoint">The main ingestion endpoint</param>
        /// <param name="resolvedIPs">Array of resolved IP addresses</param>
        public static void LogConnectionEndpointResolution(string ingestionEndpoint, string[] resolvedIPs)
        {
            try
            {
                AzureMonitorDiagnosticsCoreEventSource.Shared.LogConnectionEndpointResolution(
                    ingestionEndpoint, resolvedIPs);
            }
            catch (Exception ex)
            {
                AzureMonitorDiagnosticsCoreEventSource.Shared.UnhandledException(
                    ex.GetType().Name, ex.Message, ex.StackTrace ?? string.Empty);
            }
        }

        /// <summary>
        /// Logs sampling decisions.
        /// </summary>
        /// <param name="traceId">The trace ID</param>
        /// <param name="sampled">Whether the trace was sampled</param>
        /// <param name="samplingRate">The current sampling rate</param>
        /// <param name="samplerType">Type of sampler used</param>
        /// <param name="reason">Reason for the sampling decision</param>
        public static void LogSamplingDecision(string traceId, bool sampled, double samplingRate,
            string samplerType, string? reason = null)
        {
            try
            {
                AzureMonitorDiagnosticsDataEventSource.Shared.LogSamplingDecision(
                    traceId, sampled, samplingRate, samplerType, reason);
            }
            catch (Exception ex)
            {
                AzureMonitorDiagnosticsCoreEventSource.Shared.UnhandledException(
                    ex.GetType().Name, ex.Message, ex.StackTrace ?? string.Empty);
            }
        }

        /// <summary>
        /// Logs buffer operations and capacity warnings.
        /// </summary>
        /// <param name="action">The buffer action (enqueue, dequeue, etc.)</param>
        /// <param name="bufferSize">Current buffer size</param>
        /// <param name="queueDepth">Current queue depth</param>
        /// <param name="maxCapacity">Maximum capacity</param>
        public static void LogBufferOperation(string action, int bufferSize, int queueDepth, int maxCapacity)
        {
            try
            {
                AzureMonitorDiagnosticsExporterEventSource.Shared.BufferOperation(
                    action, bufferSize, queueDepth, maxCapacity);
            }
            catch (Exception ex)
            {
                AzureMonitorDiagnosticsCoreEventSource.Shared.UnhandledException(
                    ex.GetType().Name, ex.Message, ex.StackTrace ?? string.Empty);
            }
        }

        /// <summary>
        /// Logs telemetry persistence to disk.
        /// </summary>
        /// <param name="fileName">The persisted file name</param>
        /// <param name="itemCount">Number of items persisted</param>
        /// <param name="fileSizeBytes">Size of the persisted file</param>
        /// <param name="storagePath">Storage directory path</param>
        public static void LogTelemetryPersistedToDisk(string fileName, int itemCount, long fileSizeBytes, string storagePath)
        {
            try
            {
                AzureMonitorDiagnosticsExporterEventSource.Shared.TelemetryPersistedToDisk(
                    fileName, itemCount, fileSizeBytes, storagePath);
            }
            catch (Exception ex)
            {
                AzureMonitorDiagnosticsCoreEventSource.Shared.UnhandledException(
                    ex.GetType().Name, ex.Message, ex.StackTrace ?? string.Empty);
            }
        }

        /// <summary>
        /// Cleans up diagnostic resources. Call during exporter disposal.
        /// </summary>
        public static void Cleanup()
        {
            try
            {
                if (LazyEventListener.IsValueCreated)
                {
                    AzureMonitorDiagnosticsCoreEventSource.Shared.AgentShutdown();
                    EventListener.Dispose();
                }
            }
            catch (Exception ex)
            {
                // Log but don't throw during cleanup
                AzureMonitorDiagnosticsCoreEventSource.Shared.UnhandledException(
                    ex.GetType().Name, ex.Message, ex.StackTrace ?? string.Empty);
            }
        }
    }

    /// <summary>
    /// Example integration points showing where to call ADF logging in the exporter pipeline.
    /// These are示例代码 showing integration patterns - actual implementation would be in the 
    /// respective Azure Monitor exporter classes.
    /// </summary>
    internal static class ExampleIntegrationPoints
    {
        /// <summary>
        /// Example: How to integrate Pillar 1 (Production) logging in a telemetry processor.
        /// </summary>
        public static void ExampleTelemetryProcessorIntegration(object telemetryItem)
        {
            // Extract trace context if available
            string? traceId = null;
            string? spanId = null;

            // In actual implementation, extract from OpenTelemetry Activity or telemetry item
            // var activity = Activity.Current;
            // traceId = activity?.TraceId.ToString();
            // spanId = activity?.SpanId.ToString();

            // Determine telemetry type
            var telemetryType = telemetryItem.GetType().Name;

            // Log telemetry production (Pillar 1)
            AzureMonitorDiagnosticsIntegration.LogTelemetryProduced(
                telemetryType, telemetryItem, traceId, spanId);
        }

        /// <summary>
        /// Example: How to integrate Pillar 2 (Transmission) and Pillar 3 (Response) logging in HTTP client.
        /// </summary>
        public static async Task<HttpResponseMessage> ExampleHttpClientIntegration(
            HttpClient httpClient, string endpoint, IEnumerable<object> telemetryBatch)
        {
            // Resolve IP address for endpoint
            var resolvedIP = await ResolveIPAddress(endpoint);

            // Log transmission attempt (Pillar 2)
            AzureMonitorDiagnosticsIntegration.LogTransmissionAttempt(endpoint, resolvedIP, telemetryBatch);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                // Make the actual HTTP request
                var response = await httpClient.PostAsync(endpoint, CreateHttpContent(telemetryBatch));

                stopwatch.Stop();

                // Read response body
                var responseBody = await response.Content.ReadAsStringAsync();

                // Log backend response (Pillar 3)
                AzureMonitorDiagnosticsIntegration.LogBackendResponse(
                    (int)response.StatusCode, responseBody, endpoint, (int)stopwatch.ElapsedMilliseconds);

                return response;
            }
            catch (HttpRequestException ex)
            {
                stopwatch.Stop();

                // Log transmission failure
                AzureMonitorDiagnosticsExporterEventSource.Shared.TransmissionFailed(
                    endpoint, ex.Message, ex.GetType().Name, (int)stopwatch.ElapsedMilliseconds);

                throw;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();

                // Log error response
                AzureMonitorDiagnosticsIntegration.LogBackendResponse(
                    0, ex.Message, endpoint, (int)stopwatch.ElapsedMilliseconds);

                throw;
            }
        }

        /// <summary>
        /// Example: How to integrate retry logic with diagnostic logging.
        /// </summary>
        public static async Task<HttpResponseMessage> ExampleRetryIntegration(
            HttpClient httpClient, string endpoint, IEnumerable<object> telemetryBatch, int maxRetries = 3)
        {
            var retryCount = 0;
            Exception? lastException = null;

            while (retryCount <= maxRetries)
            {
                try
                {
                    return await ExampleHttpClientIntegration(httpClient, endpoint, telemetryBatch);
                }
                catch (Exception ex) when (retryCount < maxRetries)
                {
                    lastException = ex;
                    retryCount++;

                    var delay = TimeSpan.FromSeconds(Math.Pow(2, retryCount)); // Exponential backoff

                    // Log retry attempt
                    AzureMonitorDiagnosticsIntegration.LogTransmissionRetry(retryCount, delay, endpoint, ex);

                    await Task.Delay(delay);
                }
            }

            // All retries exhausted
            throw lastException ?? new InvalidOperationException("All retries exhausted");
        }

        /// <summary>
        /// Example: How to integrate configuration logging during exporter initialization.
        /// </summary>
        public static void ExampleConfigurationIntegration(string connectionString)
        {
            try
            {
                // Parse configuration
                var config = ParseConnectionString(connectionString);

                // Log successful configuration
                AzureMonitorDiagnosticsIntegration.LogConfigurationLoading(
                    "ConnectionString", true);

                // Resolve endpoints
                var resolvedIPs = ResolveEndpointIPs(config.IngestionEndpoint);

                // Log endpoint resolution
                AzureMonitorDiagnosticsIntegration.LogConnectionEndpointResolution(
                    config.IngestionEndpoint, resolvedIPs);
            }
            catch (Exception ex)
            {
                // Log configuration failure
                AzureMonitorDiagnosticsIntegration.LogConfigurationLoading(
                    "ConnectionString", false, ex.Message);

                throw;
            }
        }

        // Helper methods for examples (would be implemented in actual Azure Monitor exporter)
        private static async Task<string> ResolveIPAddress(string endpoint)
        {
            try
            {
                var uri = new Uri(endpoint);
                var addresses = await System.Net.Dns.GetHostAddressesAsync(uri.Host);
                return addresses.FirstOrDefault()?.ToString() ?? "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        private static HttpContent CreateHttpContent(IEnumerable<object> telemetryBatch)
        {
            // In actual implementation, serialize telemetry batch to JSON
            var json = System.Text.Json.JsonSerializer.Serialize(telemetryBatch);
            return new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        }

        private static string  ParseConnectionString(string connectionString)
        {
            // Simplified parsing - actual implementation would be more robust
            return ("https://dc.services.visualstudio.com/v2/track");
        }

        private static string[] ResolveEndpointIPs(string endpoint)
        {
            try
            {
                var uri = new Uri(endpoint);
                var addresses = System.Net.Dns.GetHostAddresses(uri.Host);
                return addresses.Select(a => a.ToString()).ToArray();
            }
            catch
            {
                return new[] { "Unknown" };
            }
        }
    }
}
