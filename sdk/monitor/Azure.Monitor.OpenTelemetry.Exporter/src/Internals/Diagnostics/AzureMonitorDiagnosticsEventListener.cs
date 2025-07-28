// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

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
    /// and writes structured JSON logs according to the Agent Diagnostics Framework specification.
    /// </summary>
    public sealed class AzureMonitorDiagnosticsEventListener : EventListener, IDisposable
    {
        private const string ConfigFileName = "OTEL_DIAGNOSTICS.json";
        private const int ConfigCheckIntervalMs = 10000; // 10 seconds
        private const int MaxFileIndex = 99;
        private const int DefaultFileSizeMB = 10;
        private const int MaxFileSizeMB = 128;

        private readonly Timer _configTimer;
        private readonly Timer _logLevelDurationTimer;
        private readonly ConcurrentQueue<DiagnosticLogEntry> _logQueue;
        private readonly object _fileLock = new();
        private volatile bool _disposed;
        private volatile bool _startupSequenceCompleted = false;

        private string? _currentLogDirectory;
        private string? _currentLogFile;
        private SelfDiagnosticsConfig _currentConfig = new SelfDiagnosticsConfig();
        private Dictionary<string, EventLevel> _eventSourceLevels = new Dictionary<string, EventLevel>();
        private int _currentFileIndex = 0;
        private long _currentFileSize = 0;
        private DateTime _logLevelStartTime = DateTime.MinValue;

        private static readonly string _machineName = Environment.MachineName;
        private static readonly string _processName = Process.GetCurrentProcess().ProcessName;
        private static readonly int _processId = Process.GetCurrentProcess().Id;

        private static volatile bool _loggingEnabled = false;

        /// <summary>
        /// Initializes a new instance of the diagnostics event listener and starts polling for configuration.
        /// </summary>
        public AzureMonitorDiagnosticsEventListener()
        {
            _logQueue = new ConcurrentQueue<DiagnosticLogEntry>();

            // Start polling for configuration changes
            _configTimer = new Timer(CheckConfiguration, null, 0, ConfigCheckIntervalMs);

            // Timer for log level duration management
            _logLevelDurationTimer = new Timer(CheckLogLevelDuration, null, Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>
        /// Determines which event sources will get enabled and written to the custom structured json log file.
        /// </summary>
        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (!_loggingEnabled || eventSource.Name == null)
                return;

            var sourceName = eventSource.Name;

            // Always listen to Azure Monitor diagnostic EventSources
            if (sourceName.StartsWith("OpenTelemetry-AzureMonitor-Diagnostics", StringComparison.OrdinalIgnoreCase))
            {
                var level = GetEventSourceLevel(sourceName);
                EnableEvents(eventSource, level);
                return;
            }

            // Listen to other OpenTelemetry events if IncludeOtelSdkLogs is enabled
            if (_currentConfig.IncludeOtelSdkLogs &&
                sourceName.StartsWith("OpenTelemetry", StringComparison.OrdinalIgnoreCase))
            {
                var level = GetEventSourceLevel(sourceName);
                EnableEvents(eventSource, level);
                return;
            }
        }

        /// <summary>
        /// Responds to any events from our monitored event sources
        /// </summary>
        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (_disposed || !_loggingEnabled || _currentLogDirectory == null)
                return;

            var sourceName = eventData.EventSource.Name;
            if (sourceName == null)
                return;

            // Filter events based on configuration
            if (!ShouldProcessEvent(sourceName))
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

        private bool ShouldProcessEvent(string sourceName)
        {
            // Always process Azure Monitor diagnostic events
            if (sourceName.StartsWith("OpenTelemetry-AzureMonitor-Diagnostics", StringComparison.OrdinalIgnoreCase))
                return true;

            // Process other OpenTelemetry events only if explicitly enabled
            if (_currentConfig.IncludeOtelSdkLogs &&
                sourceName.StartsWith("OpenTelemetry", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private EventLevel GetEventSourceLevel(string sourceName)
        {
            // Check if there's a specific filter for this event source
            if (_eventSourceLevels.TryGetValue(sourceName, out var specificLevel))
                return specificLevel;

            // Use the default log level
            return _currentConfig.LogLevel;
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
                    // Config file not found, try Profile API (Phase 2 - stub for now)
                    if (_currentLogDirectory != null)
                    {
                        AzureMonitorDiagnosticsEventSourceCore.Log.ConfigFileMissing(Path.Combine(Directory.GetCurrentDirectory(), ConfigFileName));
                        // TODO: Attempt Profile API call here in Phase 2
                        DisableLogging();
                    }
                    return;
                }

                var configContent = File.ReadAllText(configPath);
                if (TryParseConfiguration(configContent, configPath, out var newConfig))
                {
                    var configChanged = HasConfigurationChanged(newConfig);

                    if (configChanged || !_loggingEnabled)
                    {
                        _currentConfig = newConfig;
                        _loggingEnabled = true;

                        UpdateEventSourceConfiguration();
                        CreateNewLogFile();

                        // Setup log level duration timer if specified
                        if (_currentConfig.LogLevelDurationSeconds > 0)
                        {
                            _logLevelStartTime = DateTime.UtcNow;
                            _logLevelDurationTimer.Change(_currentConfig.LogLevelDurationSeconds * 1000, Timeout.Infinite);
                        }

                        if (!_startupSequenceCompleted)
                        {
                            // Run the startup sequence only once
                            _startupSequenceCompleted = true;
                            RunStartupSequence();
                        }
                    }
                }
                else
                {
                    if (_loggingEnabled)
                    {
                        AzureMonitorDiagnosticsEventSourceCore.Log.ConfigurationLoadFailed(configPath, "Configuration parsing failed");
                        DisableLogging();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Configuration check error: {ex}");
                if (_loggingEnabled)
                {
                    AzureMonitorDiagnosticsEventSourceCore.Log.UnhandledException(ex.GetType().Name, ex.Message, ex.StackTrace ?? string.Empty);
                }
            }
        }

        private void CheckLogLevelDuration(object? state)
        {
            if (_disposed || !_loggingEnabled)
                return;

            if (_currentConfig.LogLevelDurationSeconds > 0 &&
                _logLevelStartTime != DateTime.MinValue)
            {
                var elapsed = DateTime.UtcNow - _logLevelStartTime;
                if (elapsed.TotalSeconds >= _currentConfig.LogLevelDurationSeconds)
                {
                    // Duration expired, disable logging
                    DisableLogging();
                    _logLevelDurationTimer.Change(Timeout.Infinite, Timeout.Infinite);
                }
            }
        }

        private void RunStartupSequence()
        {
            try
            {
                // Get connection string from environment or configuration
                var connectionString = Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING") ??
                                     Environment.GetEnvironmentVariable("ApplicationInsights__ConnectionString");

                AzureMonitorDiagnosticsEventSourceCore.Log.RunStartupSequence(_currentConfig, connectionString);
            }
            catch (Exception ex)
            {
                AzureMonitorDiagnosticsEventSourceCore.Log.UnhandledException(ex.GetType().Name, ex.Message, ex.StackTrace ?? string.Empty);
            }
        }

        private bool HasConfigurationChanged(SelfDiagnosticsConfig newConfig)
        {
            return _currentConfig.LogDirectory != newConfig.LogDirectory ||
                   _currentConfig.LogLevel != newConfig.LogLevel ||
                   _currentConfig.FileSizeMB != newConfig.FileSizeMB ||
                   _currentConfig.IncludeOtelSdkLogs != newConfig.IncludeOtelSdkLogs ||
                   !DictionariesEqual(_currentConfig.LogFilters, newConfig.LogFilters) ||
                   _currentConfig.LogLevelDurationSeconds != newConfig.LogLevelDurationSeconds;
        }

        private bool DictionariesEqual(Dictionary<string, string> dict1, Dictionary<string, string> dict2)
        {
            if (dict1.Count != dict2.Count)
                return false;

            foreach (var kvp in dict1)
            {
                if (!dict2.TryGetValue(kvp.Key, out var value) || value != kvp.Value)
                    return false;
            }

            return true;
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

        private bool TryParseConfiguration(string configContent, string configPath, out SelfDiagnosticsConfig config)
        {
            config = new SelfDiagnosticsConfig();

            try
            {
                using var document = JsonDocument.Parse(configContent);
                var root = document.RootElement;

                // Required: LogDirectory
                if (!root.TryGetProperty("LogDirectory", out var logDirElement))
                    return false;

                var logDirectory = logDirElement.GetString();
                if (string.IsNullOrEmpty(logDirectory))
                    return false;

                // Make relative paths absolute
                if (!Path.IsPathRooted(logDirectory))
                {
                    logDirectory = Path.Combine(Directory.GetCurrentDirectory(), logDirectory);
                }

                // Ensure directory exists
                Directory.CreateDirectory(logDirectory);

                config.LogDirectory = logDirectory!;
                config.ConfigDirectory = Path.GetDirectoryName(configPath) ?? Directory.GetCurrentDirectory();
                config.ConfigSource = "local config";

                // Optional: LogLevel
                if (root.TryGetProperty("LogLevel", out var logLevelElement))
                {
                    var logLevelStr = logLevelElement.GetString();
                    if (Enum.TryParse<EventLevel>(logLevelStr, true, out var logLevel))
                    {
                        config.LogLevel = logLevel;
                    }
                }

                // Optional: FileSizeMB
                if (root.TryGetProperty("FileSizeMB", out var fileSizeElement))
                {
                    if (fileSizeElement.TryGetInt32(out var fileSizeMB))
                    {
                        config.FileSizeMB = Math.Min(Math.Max(fileSizeMB, 1), MaxFileSizeMB);
                    }
                }

                // Optional: IncludeOtelSdkLogs
                if (root.TryGetProperty("IncludeOtelSdkLogs", out var includeOtelElement))
                {
                    config.IncludeOtelSdkLogs = includeOtelElement.GetBoolean();
                }

                // Optional: LogLevelDurationSeconds
                if (root.TryGetProperty("LogLevelDurationSeconds", out var durationElement))
                {
                    if (durationElement.TryGetInt32(out var duration))
                    {
                        config.LogLevelDurationSeconds = Math.Max(duration, 0);
                    }
                }

                // Optional: LogFilters
                if (root.TryGetProperty("LogFilters", out var logFiltersElement))
                {
                    ParseLogFilters(logFiltersElement, config);
                }

                return true;
            }
            catch (Exception ex)
            {
                AzureMonitorDiagnosticsEventSourceCore.Log.ConfigurationValidationFailed(ex.Message, configPath);
                return false;
            }
        }

        private void ParseLogFilters(JsonElement logFiltersElement, SelfDiagnosticsConfig config)
        {
            try
            {
                if (logFiltersElement.TryGetProperty("EventSources", out var eventSourcesElement))
                {
                    foreach (var property in eventSourcesElement.EnumerateObject())
                    {
                        var eventSourceName = property.Name;
                        var logLevelStr = property.Value.GetString();

                        if (Enum.TryParse<EventLevel>(logLevelStr, true, out var eventLevel))
                        {
                            config.LogFilters[eventSourceName] = logLevelStr!;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AzureMonitorDiagnosticsEventSourceCore.Log.ConfigurationValidationFailed($"LogFilters parsing error: {ex.Message}", config.ConfigSource);
            }
        }

        private void UpdateEventSourceConfiguration()
        {
            // Update event source level mappings
            _eventSourceLevels.Clear();

            foreach (var filter in _currentConfig.LogFilters)
            {
                if (Enum.TryParse<EventLevel>(filter.Value, true, out var level))
                {
                    _eventSourceLevels[filter.Key] = level;
                }
            }

            // Disable all first
            DisableAllEventSources();

            // Re-enable with new configuration
            foreach (var eventSource in EventSource.GetSources())
            {
                if (eventSource.Name != null)
                {
                    if (eventSource.Name.StartsWith("OpenTelemetry-AzureMonitor-Diagnostics", StringComparison.OrdinalIgnoreCase))
                    {
                        var level = GetEventSourceLevel(eventSource.Name);
                        EnableEvents(eventSource, level);
                    }
                    else if (_currentConfig.IncludeOtelSdkLogs &&
                             eventSource.Name.StartsWith("OpenTelemetry", StringComparison.OrdinalIgnoreCase))
                    {
                        var level = GetEventSourceLevel(eventSource.Name);
                        EnableEvents(eventSource, level);
                    }
                }
            }
        }

        private void DisableAllEventSources()
        {
            foreach (var eventSource in EventSource.GetSources())
            {
                DisableEvents(eventSource);
            }
        }

        private void DisableLogging()
        {
            _loggingEnabled = false;
            _currentLogDirectory = null;
            _currentLogFile = null;
            _currentFileSize = 0;
            _currentFileIndex = 0;
            _logLevelStartTime = DateTime.MinValue;
            DisableAllEventSources();
        }

        private void CreateNewLogFile()
        {
            if (_currentConfig.LogDirectory == null)
                return;

            lock (_fileLock)
            {
                _currentLogDirectory = _currentConfig.LogDirectory;
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
                InstrumentationScope = eventData.EventSource?.Name ?? "Unknown",
                EventName = eventData.EventName ?? "UnknownEvent",
                TraceId = null, // Will be populated by specific events when available
                SpanId = null,  // Will be populated by specific events when available
                SeverityText = MapEventLevelToSeverityText(eventData.Level),
                SeverityNumber = MapEventLevelToSeverityNumber(eventData.Level),
                Body = FormatEventMessage(eventData),
                Resource = new Dictionary<string, object>
                {
                    ["service.name"] = _processName,
                    ["service.instance.id"] = $"{_machineName}-{_processId}",
                    ["agent.version"] = GetAgentVersion()
                },
                Attributes = CreateAttributes(eventData)
            };
        }

        private string FormatEventMessage(EventWrittenEventArgs eventData)
        {
            // Return the formatted message without placeholders
            var message = eventData.Message ?? string.Empty;

            // For non-Azure Monitor events (when IncludeOtelSdkLogs is true), return message as-is
            if (!eventData.EventSource.Name.StartsWith("OpenTelemetry-AzureMonitor-Diagnostics"))
            {
                return message;
            }

            // For Azure Monitor events, the message should already be properly formatted
            // since we're using proper string formatting in the EventSource methods
            return message;
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
            if (_disposed || !_loggingEnabled || _currentLogFile == null)
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
                if (_currentFileSize + jsonBytes.Length > _currentConfig.FileSizeMB * 1024 * 1024)
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

        /// <summary>
        /// Disposes the diagnostics listener and flushes any remaining log entries.
        /// </summary>
        public override void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            _configTimer?.Dispose();
            _logLevelDurationTimer?.Dispose();

            // Process any remaining log entries
            ProcessLogQueue();

            base.Dispose();
        }
    }
}
