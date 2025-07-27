// <copyright file="AzureMonitorDiagnosticsListenerManager.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

using System;
using Azure.Monitor.OpenTelemetry.Exporter.Internals.Diagnostics;

namespace Azure.Monitor.OpenTelemetry.Exporter.Internals.Diagnostics
{
    internal static class AzureMonitorDiagnosticsEventListenerManager
    {
        private static AzureMonitorDiagnosticsEventListener? _listener;

        public static void EnsureInitialized()
        {
            if (_listener == null)
            {
                _listener = new AzureMonitorDiagnosticsEventListener();
            }
        }
        public static AzureMonitorDiagnosticsEventListener? Listener => _listener;
    }
}
