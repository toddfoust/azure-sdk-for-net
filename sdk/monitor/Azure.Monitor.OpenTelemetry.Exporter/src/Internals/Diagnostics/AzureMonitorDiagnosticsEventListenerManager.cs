// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Azure.Monitor.OpenTelemetry.Exporter.Internals.Diagnostics
{
    /// <summary>
    /// Manages the lifecycle of the Azure Monitor self-diagnostics event listener.
    /// </summary>
    public static class AzureMonitorDiagnosticsEventListenerManager
    {
        private static AzureMonitorDiagnosticsEventListener? _listener;

        /// <summary>
        /// Initializes the diagnostics event listener if not already started.
        /// </summary>
        public static void Initialize()
        {
            if (_listener == null)
            {
                _listener = new AzureMonitorDiagnosticsEventListener();
            }
        }

        /// <summary>
        /// Gets the current diagnostics event listener instance.
        /// </summary>
        public static AzureMonitorDiagnosticsEventListener? Listener => _listener;
    }
}
