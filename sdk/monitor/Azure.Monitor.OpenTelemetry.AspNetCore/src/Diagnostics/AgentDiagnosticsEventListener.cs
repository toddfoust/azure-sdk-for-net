using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

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

        public AgentDiagnosticsEventListener()
        {
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
            if (TryLoadLocalConfig()) return;
            TryLoadRemoteConfig(); // Profile API fallback
        }

        private bool TryLoadLocalConfig()
        {
            try
            {
                if (!File.Exists(_configPath))
                {
                    DisposeLogWriter();
                    return;
                }

                var writeTime = File.GetLastWriteTimeUtc(_configPath);
                if (writeTime == _lastConfigWriteTime) return;

                var json = File.ReadAllText(_configPath);
                var config = JsonSerializer.Deserialize<DiagnosticsConfig>(json);
                if (config != null)
                {
                    _logDirectory = config.LogDirectory ?? _logDirectory;
                    _fileSizeLimitBytes = config.FileSize * 1024;
                    _includeRawSdkLogs = config.IncludeRawSdkLogs;
                    _lastConfigWriteTime = writeTime;

                    InitializeLogFile();
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to load diagnostics config: {ex}");
            }
        }
        
        private void TryLoadRemoteConfig()
        {
            try
            {
                // TODO: Replace with actual HTTP call to Profile API
                // Simulated response for now
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
                Console.Error.WriteLine($"Failed to load remote diagnostics config: {ex}");
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
            return Path.Combine(_logDirectory, $"{_logFilePrefix}.{_currentFileIndex}.json");
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
