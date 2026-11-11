using System;
using Integrations.Notifications;
using Integrations.Telemetry;
using Integrations.WeightTracker.WeightTrackerClient;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Refit;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureApplicationInsightsTelemetry();
builder.ConfigureFunctionsWebApplication();

builder.Services.AddNotifications(
    builder.Configuration,
    optionsSectionName: "Notifications");

builder.Services
    .AddRefitGeneratedClient<IWeightTrackerApi>()
    .ConfigureHttpClient(client =>
    {
        var config = builder.Configuration;
        var baseUrl = config["WeightTrackerApiBaseUrl"];
        var apiKey = config["WeightTrackerApiKey"];

        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Invalid WeightTracker API configuration");

        client.BaseAddress = new Uri(baseUrl);
        client.DefaultRequestHeaders.Add("X-API-KEY", apiKey);
    });

builder.Build().Run();
