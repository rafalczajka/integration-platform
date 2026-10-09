using Integrations.Notifications;
using Integrations.Todoist.Reports;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Integrations.Todoist.Functions;

internal sealed class SendWeeklyReport(
    WeeklyReport report,
    INotificationSender notificationSender,
    ILogger<SendWeeklyReport> logger)
{
    [Function(nameof(SendWeeklyReport))]
    public async Task RunAsync(
        [TimerTrigger(
            "%WeeklyReportSchedule%",
            UseMonitor = false
#if DEBUG
            , RunOnStartup = true
#endif
            )] TimerInfo _,
        CancellationToken cancellationToken)
    {
        try
        {
            var message = await report.CreateAsync(cancellationToken);
            await notificationSender.SendAsync(message.Subject, message.Body, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error while preparing the weekly report.");
            await notificationSender.SendExceptionAsync("Todoist - weekly report error", ex, cancellationToken);
            throw;
        }
    }
}
