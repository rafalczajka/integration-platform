using Integrations.Notifications;
using Integrations.Todoist.Options;
using Integrations.Todoist.TodoistClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Integrations.Todoist.Rules.ReadOnly;

/// <summary>
/// Reports inactive tasks in the Recurring project.
/// </summary>
internal sealed class RecurringTaskInactiveReportRule(
    ITodoistApi todoist,
    IOptions<TodoistProjectIdsOptions> options,
    ILogger<RecurringTaskInactiveReportRule> logger) : ITodoistRule
{
    private readonly string _recurringProjectId = options.Value.Recurring;

    /// <inheritdoc />
    /// <seealso cref="RecurringTaskInactiveReportRule" />
    public async Task ExecuteAsync(TodoistRuleContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_recurringProjectId))
        {
            logger.LogWarning("Recurring project ID is not configured.");
            return;
        }

        logger.LogInformation("Fetching tasks from Recurring project for inactive report...");

        var recurringProjectTasks = (await todoist.GetTasksByProjectAsync(_recurringProjectId, cancellationToken))
            .ToArray();

        TodoistGuards.EnsureOnlyProjectTasks(
            recurringProjectTasks,
            _recurringProjectId,
            nameof(RecurringTaskInactiveReportRule));

        var inactiveTasks = recurringProjectTasks
            .Where(task => string.IsNullOrWhiteSpace(task.ParentId))
            .Where(task => task.Labels.Any(IsInactiveLabel))
            .OrderBy(task => task.Content, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (inactiveTasks.Length == 0)
        {
            logger.LogInformation("No inactive tasks found in Recurring project.");
            return;
        }

        logger.LogInformation("Found {TaskCount} inactive tasks in Recurring project.", inactiveTasks.Length);
        context.AddMessage(NotificationFormatter.BuildNumberedListMessage(
            $"Found {inactiveTasks.Length} inactive tasks in Recurring project:",
            inactiveTasks.Select(task => task.Content)));
    }

    private static bool IsInactiveLabel(string label)
    {
        return string.Equals(label, Constants.InactiveLabel, StringComparison.OrdinalIgnoreCase);
    }
}
