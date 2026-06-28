using System;
using System.Threading;
using System.Threading.Tasks;
using Integrations.Notifications;
using Integrations.WeightTracker.WeightTrackerClient;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Integrations.WeightTracker.Functions;

internal sealed class CheckStatus(
    IWeightTrackerApi client,
    INotificationSender notificationSender,
    ILogger<CheckStatus> logger)
{
    [Function(nameof(CheckStatus))]
    public async Task RunAsync(
        [TimerTrigger(
            "%CheckStatusSchedule%",
            UseMonitor = false
#if DEBUG
            , RunOnStartup = true
#endif
        )] TimerInfo _,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Timer trigger function executed at: {DateTime}", DateTime.Now);

        try
        {
            await HandleFunctionAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error while executing function.");
            await SendExceptionNotificationAsync(ex, cancellationToken);
            throw;
        }
    }

    private async Task HandleFunctionAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Checking if today's weight entry exists.");

        var summary = await client.GetSummaryAsync(cancellationToken);

        if (summary.Today.HasEntry)
        {
            logger.LogInformation("Today's weight entry already exists. No notification will be sent.");
            return;
        }

        const string subject = "WeightTracker - missing daily weight entry";

        var body = $"""
            No weight entry for today was detected.
            Checked at: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC
            """;

        await notificationSender.SendAsync(subject, body, cancellationToken);
    }

    private async Task SendExceptionNotificationAsync(Exception exception, CancellationToken cancellationToken)
    {
        const string subject = "WeightTracker - failed to check daily weight entry";
        await notificationSender.SendExceptionAsync(subject, exception, cancellationToken);
    }
}
