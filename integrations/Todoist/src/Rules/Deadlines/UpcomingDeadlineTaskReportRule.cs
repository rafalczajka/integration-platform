using Integrations.Notifications;
using Integrations.Todoist.TodoistClient;
using Microsoft.Extensions.Logging;

namespace Integrations.Todoist.Rules.Deadlines;

/// <summary>
/// Reports tasks with a deadline ending within the next week.
/// </summary>
internal sealed class UpcomingDeadlineTaskReportRule(
    ITodoistApi todoist,
    ILogger<UpcomingDeadlineTaskReportRule> logger) : ITodoistRule
{
    private const string UpcomingDeadlineFilter = "deadline after: yesterday & deadline before: in 7 days";

    /// <inheritdoc />
    /// <seealso cref="UpcomingDeadlineTaskReportRule" />
    public async Task ExecuteAsync(TodoistRuleContext context, CancellationToken cancellationToken)
    {
        logger.LogInformation("Fetching tasks with a deadline within the next week...");

        var tasks = (await todoist.GetTasksByFilterAsync(UpcomingDeadlineFilter, cancellationToken)).ToArray();
        TodoistGuards.EnsureAllTasksHaveDeadline(tasks, nameof(UpcomingDeadlineTaskReportRule));

        if (tasks.Length == 0)
        {
            logger.LogInformation("No tasks with a deadline within the next week found.");
            return;
        }

        tasks = tasks
            .OrderBy(task => task.Deadline!.Date, StringComparer.Ordinal)
            .ThenBy(task => task.Content, StringComparer.OrdinalIgnoreCase)
            .ThenBy(task => task.Id, StringComparer.Ordinal)
            .ToArray();

        logger.LogInformation("Found {TaskCount} tasks with a deadline within the next week.", tasks.Length);

        context.AddMessage(NotificationFormatter.BuildNumberedListMessage(
            $"Found {tasks.Length} tasks with a deadline within the next week:",
            tasks.Select(task => $"{task.Content} - deadline: {task.Deadline!.Date}")));
    }
}
