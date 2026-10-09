using Integrations.Notifications;
using Integrations.Todoist.Rules;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Integrations.Todoist.Functions;

internal sealed class EnforceRules(
    IEnumerable<ITodoistRule> rules,
    INotificationSender notificationSender,
    ILogger<EnforceRules> logger)
{
    [Function(nameof(EnforceRules))]
    public async Task RunAsync(
        [TimerTrigger(
            "%EnforceRulesSchedule%",
            UseMonitor = false
#if DEBUG
            , RunOnStartup = true
#endif
        )] TimerInfo _,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Timer trigger function executed at: {TriggerTime}", DateTime.Now);

        try
        {
            var context = new TodoistRuleContext();
            await rules.ExecuteInOrderAsync(context, cancellationToken);
            await SendNotificationsAsync(context, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error while enforcing Todoist rules.");
            await SendExceptionNotificationsAsync(ex, cancellationToken);
            throw;
        }
    }

    private async Task SendNotificationsAsync(TodoistRuleContext context, CancellationToken cancellationToken)
    {
        if (!context.HasMessages) return;

        const string subject = "Todoist - rules notification";
        var body = NotificationFormatter.JoinSections(context.Messages);
        await notificationSender.SendAsync(subject, body, cancellationToken);
    }

    private async Task SendExceptionNotificationsAsync(Exception exception, CancellationToken cancellationToken)
    {
        const string subject = "Todoist - enforcing rules error.";
        await notificationSender.SendExceptionAsync(subject, exception, cancellationToken);
    }
}
