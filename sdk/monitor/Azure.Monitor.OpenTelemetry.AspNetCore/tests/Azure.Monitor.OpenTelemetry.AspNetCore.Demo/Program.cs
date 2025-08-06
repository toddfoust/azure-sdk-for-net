// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

#if NET
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net.Http;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenTelemetry().UseAzureMonitor();

/*
builder.Services.AddOpenTelemetry().UseAzureMonitor(o =>
{
    o.ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-00000000CODE";
    // Set the Credential property to enable AAD based authentication:
    // o.Credential = new DefaultAzureCredential();
});
*/

var app = builder.Build();
app.MapGet("/", () =>
{
    app.Logger.LogInformation("Hello World!");

    using var client = new HttpClient();
    var response = client.GetAsync("https://www.bing.com/").Result;

    //Todd ADF Edits:

    app.Logger.LogInformation("Hello World!");

    // Trace
    app.Logger.LogInformation("Trace log");

    // Exception
    try
    {
        throw new InvalidOperationException("Simulated exception for telemetry.");
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "An exception occurred.");
    }

    // Custom Metric
    var meter = new Meter("SampleApp.Metrics", "1.0");
    var counter = meter.CreateCounter<int>("custom_counter");
    counter.Add(1, KeyValuePair.Create<string, object?>("tag1", "value1"));

    // Event
    var activitySource = new ActivitySource("SampleApp.Events");
    using var activity = activitySource.StartActivity("CustomEventActivity");
    activity?.AddEvent(new ActivityEvent("CustomEventOccurred"));

    // Span with Attributes
    using var spanActivity = activitySource.StartActivity("SpanWithAttributes");
    spanActivity?.SetTag("custom.tag", "value");
    spanActivity?.SetTag("http.status_code", 200);

    //End Todd ADF Edits

    return $"Hello World! OpenTelemetry Trace: {Activity.Current?.Id}";
});

app.MapGet("/exception", () =>
{
    // This will throw an unhandled exception when the /exception endpoint is hit
    throw new Exception("Unhandled exception for telemetry test", new ArgumentException("parameter cannot be null", "userId"));
    ;
});

app.MapGet("/customevent", () =>
{
    var activitySource = new ActivitySource("SampleApp.Events");

    using var activity = activitySource.StartActivity("CustomEventActivity", ActivityKind.Internal);
    if (activity != null)
    {
        activity.AddEvent(new ActivityEvent("CustomEventOccurred"));
        activity.SetTag("custom.tag", "value");
        activity.SetTag("event.type", "custom");
    }

    // Activity will be stopped when exiting the using block
    return $"CustomEventActivity completed. TraceId: {activity?.TraceId}";
});

app.Run();
#endif
