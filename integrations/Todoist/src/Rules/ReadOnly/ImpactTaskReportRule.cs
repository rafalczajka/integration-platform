using Integrations.Notifications;
using Integrations.Todoist.TodoistClient;
using Microsoft.Extensions.Logging;

namespace Integrations.Todoist.Rules.ReadOnly;

/// <summary>
/// Reports tasks with the impact label.
/// </summary>
internal sealed class ImpactTaskReportRule(
    ITodoistApi todoist,
    ILogger<ImpactTaskReportRule> logger) : ITodoistRule
{
    private const string ImpactFilter = $"@{Labels.Impact}";

    /// <inheritdoc />
    /// <seealso cref="ImpactTaskReportRule" />
    public async Task ExecuteAsync(TodoistRuleContext context, CancellationToken cancellationToken)
    {
        logger.LogInformation("Fetching tasks with the {Label} label...", Labels.Impact);

        var impactTasks = (await todoist.GetTasksByFilterAsync(ImpactFilter, cancellationToken))
            .OrderBy(task => task.Content, StringComparer.OrdinalIgnoreCase)
            .ThenBy(task => task.Id, StringComparer.Ordinal)
            .ToArray();

        TodoistGuards.EnsureAllTasksContainLabel(
            impactTasks,
            Labels.Impact,
            nameof(ImpactTaskReportRule));

        if (impactTasks.Length == 0)
        {
            logger.LogInformation("No tasks with the {Label} label found.", Labels.Impact);
            return;
        }

        logger.LogInformation("Found {TaskCount} tasks with the {Label} label.", impactTasks.Length, Labels.Impact);

        context.AddMessage(NotificationFormatter.BuildNumberedListMessage(
            $"Found {impactTasks.Length} tasks with the '{Labels.Impact}' label:",
            impactTasks.Select(task => task.Content)));
    }
}
