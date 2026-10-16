using Integrations.Notifications;
using Integrations.Todoist.TodoistClient;
using Microsoft.Extensions.Logging;

namespace Integrations.Todoist.Rules.ReadOnly;

/// <summary>
/// Reports tasks that are currently marked as blocked and their blockers.
/// </summary>
internal sealed class BlockedTaskReportRule(
    ITodoistApi todoist,
    ILogger<BlockedTaskReportRule> logger) : ITodoistRule
{
    private const string BlockedFilter = "@blocked";
    private const string BlockerFilter = "@blocker";

    /// <inheritdoc />
    /// <seealso cref="BlockedTaskReportRule" />
    public async Task ExecuteAsync(TodoistRuleContext context, CancellationToken cancellationToken)
    {
        logger.LogInformation("Fetching tasks with the {Label} label for blocked task report...", Constants.BlockedLabel);

        var blockedTasks = (await todoist.GetTasksByFilterAsync(BlockedFilter, cancellationToken))
            .OrderBy(task => task.Content, StringComparer.OrdinalIgnoreCase)
            .ThenBy(task => task.Id, StringComparer.Ordinal)
            .ToArray();
        TodoistGuards.EnsureAllTasksContainLabel(blockedTasks, Constants.BlockedLabel, nameof(BlockedTaskReportRule));

        logger.LogInformation("Fetching tasks with the {Label} label for blocker task report...", Constants.BlockerLabel);

        var blockerTasks = (await todoist.GetTasksByFilterAsync(BlockerFilter, cancellationToken))
            .OrderBy(task => task.Content, StringComparer.OrdinalIgnoreCase)
            .ThenBy(task => task.Id, StringComparer.Ordinal)
            .ToArray();
        TodoistGuards.EnsureAllTasksContainLabel(
            blockerTasks,
            Constants.BlockerLabel,
            $"{nameof(BlockedTaskReportRule)} blocker fetch");

        if (blockedTasks.Length == 0 && blockerTasks.Length == 0)
        {
            logger.LogInformation("No blocked or blocker tasks found for report.");
            return;
        }

        logger.LogInformation("Found {BlockedTaskCount} blocked tasks and {BlockerTaskCount} blocker tasks.", blockedTasks.Length, blockerTasks.Length);

        var messageSections = new List<string>();

        if (blockedTasks.Length > 0)
        {
            messageSections.Add(NotificationFormatter.BuildNumberedListMessage(
                $"Found {blockedTasks.Length} blocked tasks:",
                blockedTasks.Select(task => task.Content)));
        }

        if (blockerTasks.Length > 0)
        {
            messageSections.Add(NotificationFormatter.BuildNumberedListMessage(
                $"Found {blockerTasks.Length} blocker tasks:",
                blockerTasks.Select(task => task.Content)));
        }

        context.AddMessage(string.Join($"{Environment.NewLine}{Environment.NewLine}", messageSections));
    }
}
