using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core.Shared;

namespace Azure.Monitor.OpenTelemetry.Exporter.Internals.Diagnostics
{
    /// <summary>
    /// EventListener implementation that monitors Azure Monitor diagnostic events
    /// and writes structured JSON logs according to the ADF specification.
    /// </summary>
    internal sealed class AzureMonitorDiagnosticsEventListener : EventListener, IDisposable
    {
        private const string ConfigFileName = "OTEL_DIAGNOSTICS.json";
        private const int ConfigCheckIntervalMs = 10000; // 10 seconds
        private const int MaxFileIndex = 99;
        private const int DefaultFileSizeMB = 10;
        private const int MaxFileSizeMB = 128;

        private static readonly Regex LogDirectoryRegex = new(
            @"""LogDirectory""\s*:\s*""(?<LogDirectory>.*?)""",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly Timer _configTimer;
        private readonly ConcurrentQueue<DiagnosticLogEntry> _logQueue;
        private readonly object _fileLock = new();
        private volatile bool _disposed;

        private string? _currentLogDirectory;
        private string? _currentLogFile;
        private EventLevel _currentLogLevel = EventLevel.Informational;
        private int _currentFileSizeMB = DefaultFileSizeMB;
        private int _currentFileIndex = 0;
        private long _currentFileSize = 0;

        private static readonly string _machineName = Environment.MachineName;
        private static readonly string _processName = Process.GetCurrentProcess().ProcessName;
        private static readonly int _processId = Process.GetCurrentProcess().Id;

        public AzureMonitorDiagnosticsEventListener()
        {
            _logQueue = new ConcurrentQueue<DiagnosticLogEntry>();

            // Start polling for configuration changes
            _configTimer = new Timer(CheckConfiguration, null, 0, ConfigCheckIntervalMs);
        }

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            // Listen to Azure Monitor diagnostic EventSources
            if (eventSource.Name != null &&
                eventSource.Name.StartsWith("OpenTelemetry-AzureMonitor-Diagnostics", StringComparison.OrdinalIgnoreCase))
            {
                EnableEvents(eventSource, _currentLogLevel);
            }

            // Also listen to core OpenTelemetry events when DEBUG/TRACE is enabled
            if (_currentLogLevel <= EventLevel.Verbose &&
                eventSource.Name != null &&
                eventSource.Name.StartsWith("OpenTelemetry", StringComparison.OrdinalIgnoreCase))
            {
                EnableEvents(eventSource, _currentLogLevel);
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (_disposed || _currentLogDirectory == null)
                return;

            try
            {
                var logEntry = CreateLogEntry(eventData);
                _logQueue.Enqueue(logEntry);

                // Process log queue asynchronously
                _ = Task.Run(ProcessLogQueue);
            }
            catch (Exception ex)
            {
                // Avoid throwing from EventListener to prevent application crashes
                Debug.WriteLine($"AzureMonitorDiagnosticsEventListener error: {ex}");
            }
        }

        private void CheckConfiguration(object? state)
        {
            if (_disposed)
                return;

            try
            {
                var configPath = FindConfigFile();
                if (configPath == null)
                {
                    // Config file not found, disable logging
                    if (_currentLogDirectory != null)
                    {
                        _currentLogDirectory = null;
                        _currentLogFile = null;
                        DisableAllEventSources();
                    }
                    return;
                }

                var configContent = File.ReadAllText(configPath);
                if (TryParseConfiguration(configContent, out var logDirectory, out var logLevel, out var fileSizeMB))
                {
                    var configChanged = _currentLogDirectory != logDirectory ||
                                      _currentLogLevel != logLevel ||
                                      _currentFileSizeMB != fileSizeMB;

                    if (configChanged)
                    {
                        _currentLogDirectory = logDirectory;
                        _currentLogLevel = logLevel;
                        _currentFileSizeMB = fileSizeMB;

                        UpdateEventSourceListening();
                        CreateNewLogFile();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Configuration check error: {ex}");
            }
        }

        private string? FindConfigFile()
        {
            // Check current working directory first
            var currentDir = Directory.GetCurrentDirectory();
            var configPath = Path.Combine(currentDir, ConfigFileName);

            if (File.Exists(configPath))
                return configPath;

            // Check application base directory
            var baseDir = AppContext.BaseDirectory;
            configPath = Path.Combine(baseDir, ConfigFileName);

            return File.Exists(configPath) ? configPath : null;
        }

        private bool TryParseConfiguration(string configContent, out string logDirectory,
            out EventLevel logLevel, out int fileSizeMB)
        {
            logDirectory = string.Empty;
            logLevel = EventLevel.Informational;
            fileSizeMB = DefaultFileSizeMB;

            try
            {
                // Parse LogDirectory
                var logDirMatch = LogDirectoryRegex.Match(configContent);
                if (!logDirMatch.Success)
                    return false;

                logDirectory = logDirMatch.Groups["LogDirectory"].Value;
                if (string.IsNullOrEmpty(logDirectory))
                    return false;

                // Make relative paths absolute
                if (!Path.IsPathRooted(logDirectory))
                {
                    logDirectory = Path.Combine(Directory.GetCurrentDirectory(), logDirectory);
                }

                // Ensure directory exists
                Directory.CreateDirectory(logDirectory);

                // Parse optional LogLevel (default to Info for Three Pillars)
                if (configContent.Contains("\"LogLevel\"", StringComparison.OrdinalIgnoreCase))
                {
                    var logLevelRegex = new Regex(@"""LogLevel""\s*:\s*""(?<LogLevel>.*?)""",
                        RegexOptions.IgnoreCase);
                    var logLevelMatch = logLevelRegex.Match(configContent);

                    if (logLevelMatch.Success)
                    {
                        var logLevelStr = logLevelMatch.Groups["LogLevel"].Value;
                        if (!Enum.TryParse<EventLevel>(logLevelStr, true, out logLevel))
                        {
                            logLevel = EventLevel.Informational;
                        }
                    }
                }

                // Parse optional FileSizeMB
                if (configContent.Contains("\"FileSizeMB\"", StringComparison.OrdinalIgnoreCase))
                {
                    var fileSizeRegex = new Regex(@"""FileSizeMB""\s*:\s*(?<FileSizeMB>\d+)",
                        RegexOptions.IgnoreCase);
                    var fileSizeMatch = fileSizeRegex.Match(configContent);

                    if (fileSizeMatch.Success &&
                        int.TryParse(fileSizeMatch.Groups["FileSizeMB"].Value, out var parsedSize))
                    {
                        fileSizeMB = Math.Min(Math.Max(parsedSize, 1), MaxFileSizeMB);
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private void UpdateEventSourceListening()
        {
            // Disable all first
            DisableAllEventSources();

            // Re-enable with new level
            foreach (var eventSource in GetEventSources())
            {
                if (eventSource.Name != null)
                {
                    if (eventSource.Name.StartsWith("OpenTelemetry-AzureMonitor-Diagnostics", StringComparison.OrdinalIgnoreCase))
                    {
                        EnableEvents(eventSource, _currentLogLevel);
                    }
                    else if (_currentLogLevel <= EventLevel.Verbose &&
                             eventSource.Name.StartsWith("OpenTelemetry", StringComparison.OrdinalIgnoreCase))
                    {
                        EnableEvents(eventSource, _currentLogLevel);
                    }
                }
            }
        }

        private void DisableAllEventSources()
        {
            foreach (var eventSource in GetEventSources())
            {
                DisableEvents(eventSource);
            }
        }

        private void CreateNewLogFile()
        {
            if (_currentLogDirectory == null)
                return;

            lock (_fileLock)
            {
                _currentFileIndex = 0;
                _currentFileSize = 0;
                _currentLogFile = GenerateLogFileName();
            }
        }

        private string GenerateLogFileName()
        {
            return Path.Combine(_currentLogDirectory!,
                $"agent-diagnostics-{_machineName}-{_processName}-{_processId}-{_currentFileIndex:D2}.json");
        }

        private DiagnosticLogEntry CreateLogEntry(EventWrittenEventArgs eventData)
        {
            var timestamp = DateTime.UtcNow;

            return new DiagnosticLogEntry
            {
                Timestamp = timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ"),
                ObservedTimestamp = timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ"),
                SeverityText = MapEventLevelToSeverityText(eventData.Level),
                SeverityNumber = MapEventLevelToSeverityNumber(eventData.Level),
                Body = eventData.Message ?? string.Empty,
                EventName = eventData.EventName ?? "UnknownEvent",
                InstrumentationScope = eventData.EventSource?.Name ?? "Unknown",
                Resource = new Dictionary<string, object>
                {
                    ["service.name"] = _processName,
                    ["service.instance.id"] = $"{_machineName}-{_processId}",
                    ["agent.version"] = GetAgentVersion()
                },
                Attributes = CreateAttributes(eventData)
            };
        }

        private static string MapEventLevelToSeverityText(EventLevel level)
        {
            return level switch
            {
                EventLevel.LogAlways => "FATAL",
                EventLevel.Critical => "ERROR",
                EventLevel.Error => "ERROR",
                EventLevel.Warning => "WARN",
                EventLevel.Informational => "INFO",
                EventLevel.Verbose => "DEBUG",
                _ => "TRACE"
            };
        }

        private static int MapEventLevelToSeverityNumber(EventLevel level)
        {
            return level switch
            {
                EventLevel.LogAlways => 21,
                EventLevel.Critical => 17,
                EventLevel.Error => 17,
                EventLevel.Warning => 13,
                EventLevel.Informational => 9,
                EventLevel.Verbose => 5,
                _ => 1
            };
        }

        private Dictionary<string, object> CreateAttributes(EventWrittenEventArgs eventData)
        {
            var attributes = new Dictionary<string, object>();

            // Add payload data as attributes
            if (eventData.Payload != null && eventData.PayloadNames != null)
            {
                for (int i = 0; i < Math.Min(eventData.Payload.Count, eventData.PayloadNames.Count); i++)
                {
                    var key = eventData.PayloadNames[i];
                    var value = eventData.Payload[i];

                    if (!string.IsNullOrEmpty(key) && value != null)
                    {
                        attributes[$"agent.diag.{key}"] = value;
                    }
                }
            }

            // Add event-specific metadata
            attributes["agent.diag.event.id"] = eventData.EventId;
            attributes["agent.diag.event.task"] = eventData.Task.ToString();
            attributes["agent.diag.event.opcode"] = eventData.Opcode.ToString();

            return attributes;
        }

        private string GetAgentVersion()
        {
            try
            {
                var assembly = typeof(AzureMonitorDiagnosticsEventListener).Assembly;
                var version = assembly.GetName().Version;
                return version?.ToString() ?? "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        private void ProcessLogQueue()
        {
            if (_disposed || _currentLogFile == null)
                return;

            var entriesToProcess = new List<DiagnosticLogEntry>();

            // Dequeue all pending entries
            while (_logQueue.TryDequeue(out var entry))
            {
                entriesToProcess.Add(entry);
            }

            if (entriesToProcess.Count == 0)
                return;

            lock (_fileLock)
            {
                foreach (var entry in entriesToProcess)
                {
                    WriteLogEntry(entry);
                }
            }
        }

        private void WriteLogEntry(DiagnosticLogEntry entry)
        {
            if (_currentLogFile == null)
                return;

            try
            {
                var jsonText = JsonSerializer.Serialize(entry, new JsonSerializerOptions
                {
                    WriteIndented = false,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }) + Environment.NewLine;

                var jsonBytes = Encoding.UTF8.GetBytes(jsonText);

                // Check if we need to rotate the file
                if (_currentFileSize + jsonBytes.Length > _currentFileSizeMB * 1024 * 1024)
                {
                    RotateLogFile();
                }

                File.AppendAllText(_currentLogFile, jsonText, Encoding.UTF8);
                _currentFileSize += jsonBytes.Length;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error writing log entry: {ex}");
            }
        }

        private void RotateLogFile()
        {
            _currentFileIndex = (_currentFileIndex + 1) % MaxFileIndex;
            _currentFileSize = 0;
            _currentLogFile = GenerateLogFileName();

            // Delete the file if it exists (circular buffer behavior)
            if (File.Exists(_currentLogFile))
            {
                try
                {
                    File.Delete(_currentLogFile);
                }
                catch
                {
                    // If we can't delete, just overwrite
                }
            }
        }

        public override void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            _configTimer?.Dispose();

            // Process any remaining log entries
            ProcessLogQueue();

            base.Dispose();
        }
    }

    /// <summary>
    /// Represents a structured diagnostic log entry according to the ADF specification.
    /// </summary>
    internal class DiagnosticLogEntry
    {
        public string Timestamp { get; set; } = string.Empty;
        public string ObservedTimestamp { get; set; } = string.Empty;
        public string? TraceId { get; set; }
        public string? SpanId { get; set; }
        public string SeverityText { get; set; } = string.Empty;
        public int SeverityNumber { get; set; }
        public string Body { get; set; } = string.Empty;
        public string EventName { get; set; } = string.Empty;
        public string InstrumentationScope { get; set; } = string.Empty;
        public Dictionary<string, object> Resource { get; set; } = new();
        public Dictionary<string, object> Attributes { get; set; } = new();
    }
}
