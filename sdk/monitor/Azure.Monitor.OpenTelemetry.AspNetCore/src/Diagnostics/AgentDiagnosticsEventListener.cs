using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using OpenTelemetry.Resources;


namespace Azure.Monitor.OpenTelemetry.AspNetCore.Diagnostics
{
    public class AgentDiagnosticsEventListener : EventListener, IDisposable
    {
        private readonly HashSet<string> _enabledSources = new();
        private readonly object _lock = new();
        private StreamWriter _logWriter;
        private string _logDirectory = ".";
        private int _fileSizeLimitBytes = 10 * 1024 * 1024; // 10 MB
        private int _fileRotationCount = 5;
        private string _logFilePrefix = "agent-diagnostics";
        private int _currentFileIndex = 0;
        private bool _includeRawSdkLogs = false;
        private string _configPath;
        private DateTime _lastConfigWriteTime;
        private CancellationTokenSource _cts = new();
        private Task _pollingTask;
        private readonly string _machineName = Environment.MachineName;
        private readonly string _processName = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
        private readonly int _processId = Environment.ProcessId;
        private readonly string _logStartTime = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        private readonly Resource _resource;




        public AgentDiagnosticsEventListener() : this(null) { }

        public AgentDiagnosticsEventListener(Resource resource)
        {
            _resource = resource;
            _configPath = Path.Combine(AppContext.BaseDirectory, "OTEL_DIAGNOSTICS.json");
            LoadConfiguration();
            InitializeLogFile();
            StartConfigPolling();
        }

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name.StartsWith("OpenTelemetry-AzureMonitor-Diagnostics-") ||
                (_includeRawSdkLogs && (
                    eventSource.Name.StartsWith("OpenTelemetry-") ||
                    eventSource.Name.StartsWith("OpenTelemetry-AzureMonitor-Exporter"))))
            {
                EnableEvents(eventSource, EventLevel.LogAlways, EventKeywords.All);
                _enabledSources.Add(eventSource.Name);
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (_logWriter == null) return;

            try
            {
                var logEntry = new
                {
                    Timestamp = DateTime.UtcNow.ToString("o"),
                    SeverityText = eventData.Level.ToString(),
                    SeverityNumber = (int)eventData.Level,
                    EventName = eventData.EventName ?? "UnknownEvent",
                    InstrumentationScope = eventData.EventSource.Name,
                    Body = eventData.Message ?? eventData.Payload?[0]?.ToString(),
                    Attributes = new Dictionary<string, object>
                    {
                        { "agent.diag.event.id", eventData.EventId },
                        { "agent.diag.event.keywords", eventData.Keywords.ToString() },
                        { "agent.diag.event.opcode", eventData.Opcode.ToString() },
                        { "agent.diag.event.task", eventData.Task.ToString() },
                        { "agent.diag.raw_payload", eventData.Payload }
                    }
                };

                var json = JsonSerializer.Serialize(logEntry, new JsonSerializerOptions
                {
                    WriteIndented = false,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                });

                WriteLog(json);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"AgentDiagnosticsEventListener failed to log event: {ex}");
            }
        }

        private void LoadConfiguration()
        {
            var configLoaded = TryLoadLocalConfig();

            if (!configLoaded)
            {
                configLoaded = TryLoadRemoteConfig();
            }

            if (configLoaded)
            {
                AgentDiagnosticsCoreEventSource.Log.AttachStatusReport(
                    attached: true,
                    reason: "Diagnostics enabled and telemetry pipeline is active."
                );

                EmitAgentEnvironmentReport();
                EmitConnectionEndpointsReport();

            }

            // TODO: Once OpenTelemetry ASP.NET Core distro is integrated into auto-instrumentation,
            //       this logic will detect conflicting instrumentation and emit a back-off status.
            //AgentDiagnosticsCoreEventSource.Log.AttachStatusReport(
            //    attached: false,
            //    reason: "Agent backing off due to conflicting dll, ApplicationInsights.dll, loaded in the app domain. Manual instrumentation already detected."
            //);
        }

        private bool TryLoadLocalConfig()
        {
            try
            {
                if (!File.Exists(_configPath))
                {
                    AgentDiagnosticsCoreEventSource.Log.ConfigFileMissing();
                    DisposeLogWriter();
                    return false;
                }

                var writeTime = File.GetLastWriteTimeUtc(_configPath);
                if (writeTime == _lastConfigWriteTime)
                {
                    return true; // Config hasn't changed, but it's still valid
                }

                var json = File.ReadAllText(_configPath);
                var config = JsonSerializer.Deserialize<DiagnosticsConfig>(json);
                if (config != null)
                {
                    _logDirectory = config.LogDirectory ?? _logDirectory;
                    _fileSizeLimitBytes = config.FileSize * 1024;
                    _includeRawSdkLogs = config.IncludeRawSdkLogs;
                    _lastConfigWriteTime = writeTime;

                    InitializeLogFile();
                    return true;
                }
            }
            catch (Exception ex)
            {
                AgentDiagnosticsCoreEventSource.Log.ConfigurationLoadFailed(ex);
            }

            return false;
        }


                
        private void TryLoadRemoteConfig()
        {
            try
            {
                // TODO: Replace with actual HTTP call to Profile API
                var simulatedJson = @"
                {
                    ""logFilters"": {
                        ""defaultLevel"": ""WARN"",
                        ""sources"": {
                            ""OpenTelemetry-AzureMonitor-Diagnostics-Exporter"": ""DEBUG""
                        }
                    },
                    ""logFileSizeMb"": 10,
                    ""logFileCount"": 5,
                    ""includeRawSdkLogs"": true
                }";

                var config = JsonSerializer.Deserialize<DiagnosticsConfig>(simulatedJson);
                if (config != null)
                {
                    _logDirectory = config.LogDirectory ?? _logDirectory;
                    _fileSizeLimitBytes = config.FileSize * 1024;
                    _includeRawSdkLogs = config.IncludeRawSdkLogs;

                    InitializeLogFile();
                }
            }
            catch (Exception ex)
            {
                AgentDiagnosticsCoreEventSource.Log.ProfileApiCallFailed(ex);
            }
        }


        private void StartConfigPolling()
        {
            _pollingTask = Task.Run(async () =>
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    LoadConfiguration();
                    await Task.Delay(TimeSpan.FromSeconds(10), _cts.Token);
                }
            });
        }

        private void InitializeLogFile()
        {
            DisposeLogWriter();

            Directory.CreateDirectory(_logDirectory);
            var path = GetLogFilePath();
            _logWriter = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            {
                AutoFlush = true
            };
        }

        private void EmitAgentEnvironmentReport()
        {
            var cloudContext = ExtractCloudContextFromResource(_resource);

            var logEntry = new
            {
                Timestamp = DateTime.UtcNow.ToString("o"),
                SeverityText = "INFO",
                SeverityNumber = 9,
                EventName = "AgentEnvironmentReport",
                InstrumentationScope = "OpenTelemetry-AzureMonitor-Diagnostics-Core",
                Body = "Agent starting up. Reporting environment and configuration.",
                Resource = new
                {
                    service_name = cloudContext.GetValueOrDefault("serviceName"),
                    service_instance_id = cloudContext.GetValueOrDefault("serviceInstanceId"),
                    cloud_provider = cloudContext.GetValueOrDefault("cloudProvider"),
                    cloud_platform = cloudContext.GetValueOrDefault("cloudPlatform"),
                    cloud_resource_id = cloudContext.GetValueOrDefault("cloudResourceId")
                },
                Attributes = new Dictionary<string, object>
                {
                    { "agent.diag.config.source", File.Exists(_configPath) ? "OTEL_DIAGNOSTICS.json" : "ProfileAPI" },
                    { "agent.diag.config.instrumentation_key", "<placeholder>" },
                    { "agent.diag.config.sampling.type", "parent_based_trace_id_ratio" },
                    { "agent.diag.config.sampling.rate", 1.0 },
                    { "agent.diag.host.os_version", Environment.OSVersion.VersionString },
                    { "agent.diag.host.machine_name", Environment.MachineName },
                    { "agent.diag.host.process_id", Environment.ProcessId },
                    { "agent.diag.host.process_name", Process.GetCurrentProcess().ProcessName },
                    { "agent.diag.host.working_directory", Environment.CurrentDirectory }
                }
            };

            var json = JsonSerializer.Serialize(logEntry, new JsonSerializerOptions
            {
                WriteIndented = false,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });

            WriteLog(json);
        }

        private void EmitConnectionEndpointsReport()
        {
            //TODO: Figure out how to properly get hold of connection string if customer supplied
            var connectionString = Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");

            var endpoints = ResolveConnectionEndpoints(connectionString);
            AgentDiagnosticsCoreEventSource.Log.ConnectionEndpointsReport(endpoints);
        }

        private static Dictionary<string, string> ExtractCloudContextFromResource(Resource resource)
        {
            var context = new Dictionary<string, string>();

            if (resource == null)
            {
                context["cloudProvider"] = "unknown";
                context["cloudPlatform"] = "unknown";
                context["cloudResourceId"] = "unknown";
                context["serviceName"] = "unknown";
                context["serviceInstanceId"] = "unknown";
                return context;
            }

            foreach (var attribute in resource.Attributes)
            {
                switch (attribute.Key)
                {
                    case "cloud.provider":
                        context["cloudProvider"] = attribute.Value?.ToString();
                        break;
                    case "cloud.platform":
                        context["cloudPlatform"] = attribute.Value?.ToString();
                        break;
                    case "cloud.resource_id":
                        context["cloudResourceId"] = attribute.Value?.ToString();
                        break;
                    case "service.name":
                        context["serviceName"] = attribute.Value?.ToString();
                        break;
                    case "service.instance.id":
                        context["serviceInstanceId"] = attribute.Value?.ToString();
                        break;
                }
            }

            return context;
        }

        private List<AgentDiagnosticsCoreEventSource.EndpointInfo> ResolveConnectionEndpoints(string connectionString)
        {
            var endpoints = new List<AgentDiagnosticsCoreEventSource.EndpointInfo>();

            var defaultEndpoints = new Dictionary<string, string>
            {
                { "Ingestion", "https://dc.services.visualstudio.com/v2/track" },
                { "LiveMetrics", "https://quickpulse.live.com/" },
                { "Profiler", "https://agent.azureserviceprofiler.net/" },
                { "SnapshotDebugger", "https://snapshotdebugger.monitor.azure.com/" }
            };

            var parsed = ParseConnectionString(connectionString);
            var suffix = parsed.GetValueOrDefault("EndpointSuffix");

            endpoints.Add(new AgentDiagnosticsCoreEventSource.EndpointInfo
            {
                Name = "Ingestion",
                Url = parsed.GetValueOrDefault("IngestionEndpoint") ??
                    (suffix != null ? $"https://dc.{suffix}/v2/track" : defaultEndpoints["Ingestion"])
            });

            endpoints.Add(new AgentDiagnosticsCoreEventSource.EndpointInfo
            {
                Name = "LiveMetrics",
                Url = parsed.GetValueOrDefault("LiveEndpoint") ??
                    (suffix != null ? $"https://live.{suffix}/" : defaultEndpoints["LiveMetrics"])
            });

            endpoints.Add(new AgentDiagnosticsCoreEventSource.EndpointInfo
            {
                Name = "Profiler",
                Url = parsed.GetValueOrDefault("ProfilerEndpoint") ??
                    (suffix != null ? $"https://profiler.{suffix}/" : defaultEndpoints["Profiler"])
            });

            endpoints.Add(new AgentDiagnosticsCoreEventSource.EndpointInfo
            {
                Name = "SnapshotDebugger",
                Url = parsed.GetValueOrDefault("SnapshotEndpoint") ??
                    (suffix != null ? $"https://snapshot.{suffix}/" : defaultEndpoints["SnapshotDebugger"])
            });

            foreach (var endpoint in endpoints)
            {
                try
                {
                    var host = new Uri(endpoint.Url).Host;
                    var ips = System.Net.Dns.GetHostAddresses(host);
                    endpoint.ResolvedIps = ips.Select(ip => ip.ToString()).ToList();
                }
                catch
                {
                    endpoint.ResolvedIps.Add("ResolutionFailed");
                }
            }

            return endpoints;
        }

        private Dictionary<string, string> ParseConnectionString(string connectionString)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(connectionString))
                return dict;

            var parts = connectionString.Split(';');
            foreach (var part in parts)
            {
                var kv = part.Split('=', 2);
                if (kv.Length == 2)
                {
                    dict[kv[0].Trim()] = kv[1].Trim();
                }
            }

            return dict;
        }


        private void DisposeLogWriter()
        {
            lock (_lock)
            {
                _logWriter?.Dispose();
                _logWriter = null;
            }
        }

        private void WriteLog(string json)
        {
            lock (_lock)
            {
                if (_logWriter == null) return;

                if (_logWriter.BaseStream.Length > _fileSizeLimitBytes)
                {
                    RotateLogFile();
                }

                _logWriter.WriteLine(json);
            }
        }

        private void RotateLogFile()
        {
            DisposeLogWriter();
            _currentFileIndex = (_currentFileIndex + 1) % _fileRotationCount;
            var path = GetLogFilePath();
            _logWriter = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
            {
                AutoFlush = true
            };
        }

        private string GetLogFilePath()
        {
            var fileName = $"agent-diagnostics-{_machineName}-{_processName}-{_processId}-{_currentFileIndex}.json";
            return Path.Combine(_logDirectory, fileName);
        }

        public void Dispose()
        {
            _cts.Cancel();
            _pollingTask?.Wait();
            DisposeLogWriter();
        }

        private class DiagnosticsConfig
        {
            public string LogDirectory { get; set; }
            public int FileSize { get; set; } = 10240; // KB
            public bool IncludeRawSdkLogs { get; set; } = false;

            // Future fields from Profile API
            public Dictionary<string, string> LogFilters { get; set; }
            public int LogFileCount { get; set; } = 5;

        }
    }
}
