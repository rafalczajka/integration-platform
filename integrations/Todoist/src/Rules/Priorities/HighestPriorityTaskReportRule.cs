using Integrations.Todoist.TodoistClient;
using Microsoft.Extensions.Logging;

namespace Integrations.Todoist.Rules.Priorities;

/// <summary>
/// Reports tasks with the highest Todoist priority.
/// </summary>
internal sealed class HighestPriorityTaskReportRule(
    ITodoistApi todoist,
    ILogger<HighestPriorityTaskReportRule> logger) : ITodoistRule
{
    private const string HighestPriorityFilter = "p1";

    public int Order => 9;

    /// <inheritdoc />
    /// <seealso cref="HighestPriorityTaskReportRule" />
    public async Task ExecuteAsync(TodoistRuleContext context, CancellationToken cancellationToken)
    {
        logger.LogInformation("Fetching tasks with the highest priority...");

        var highestPriorityTasks = (await todoist.GetTasksByFilterAsync(HighestPriorityFilter, cancellationToken))
            .OrderBy(task => task.Content, StringComparer.OrdinalIgnoreCase)
            .ThenBy(task => task.Id, StringComparer.Ordinal)
            .ToArray();

        if (highestPriorityTasks.Length == 0)
        {
            logger.LogInformation("No tasks with the highest priority found.");
            return;
        }

        logger.LogInformation("Found {TaskCount} tasks with the highest priority.", highestPriorityTasks.Length);

        context.AddMessage(NotificationFormatter.BuildNumberedListMessage(
            $"Found {highestPriorityTasks.Length} tasks with the highest priority:",
            highestPriorityTasks.Select(task => task.Content)));
    }
}
