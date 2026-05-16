using Integrations.Todoist.TodoistClient;
using Microsoft.Extensions.Logging;

namespace Integrations.Todoist.Rules.Labels;

/// <summary>
/// Reports tasks with the impact label.
/// </summary>
internal sealed class ImpactTaskReportRule(
    ITodoistApi todoist,
    ILogger<ImpactTaskReportRule> logger) : ITodoistRule
{
    private const string ImpactFilter = $"@{Constants.ImpactLable}";

    public int Order => 10;

    /// <inheritdoc />
    /// <seealso cref="ImpactTaskReportRule" />
    public async Task ExecuteAsync(TodoistRuleContext context, CancellationToken cancellationToken)
    {
        logger.LogInformation("Fetching tasks with the {Label} label...", Constants.ImpactLable);

        var impactTasks = (await todoist.GetTasksByFilterAsync(ImpactFilter, cancellationToken))
            .OrderBy(task => task.Content, StringComparer.OrdinalIgnoreCase)
            .ThenBy(task => task.Id, StringComparer.Ordinal)
            .ToArray();

        TodoistGuards.EnsureAllTasksContainLabel(
            impactTasks,
            Constants.ImpactLable,
            nameof(ImpactTaskReportRule));

        if (impactTasks.Length == 0)
        {
            logger.LogInformation("No tasks with the {Label} label found.", Constants.ImpactLable);
            return;
        }

        logger.LogInformation("Found {TaskCount} tasks with the {Label} label.", impactTasks.Length, Constants.ImpactLable);

        context.AddMessage(NotificationFormatter.BuildNumberedListMessage(
            $"Found {impactTasks.Length} tasks with the '{Constants.ImpactLable}' label:",
            impactTasks.Select(task => task.Content)));
    }
}
