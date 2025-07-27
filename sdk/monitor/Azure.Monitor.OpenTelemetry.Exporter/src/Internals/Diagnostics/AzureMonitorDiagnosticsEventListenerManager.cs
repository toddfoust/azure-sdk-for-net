// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Azure.Monitor.OpenTelemetry.Exporter.Internals.Diagnostics
{
    internal static class AzureMonitorDiagnosticsEventListenerManager
    {
        private static AzureMonitorDiagnosticsEventListener? _listener;

        public static void Initialize()
        {
            if (_listener == null)
            {
                _listener = new AzureMonitorDiagnosticsEventListener();
            }
        }
        public static AzureMonitorDiagnosticsEventListener? Listener => _listener;
    }
}
