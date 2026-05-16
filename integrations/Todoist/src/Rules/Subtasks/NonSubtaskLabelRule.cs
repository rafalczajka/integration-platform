using Integrations.Todoist.TodoistClient;
using Microsoft.Extensions.Logging;

namespace Integrations.Todoist.Rules.Subtasks;

/// <summary>
/// Removes the subtask label from tasks that are not subtasks but have it assigned.
/// </summary>
/// <remarks>
/// <b>Warning:</b> This rule modifies Todoist data.
/// </remarks>
internal sealed class NonSubtaskLabelRule(
    ITodoistApi todoist,
    ILogger<NonSubtaskLabelRule> logger) : ITodoistRule
{
    public int Order => 2;

    private const string NonSubtaskWithSubtaskLabelFilter = "!subtask & @subtask";

    /// <inheritdoc />
    /// <seealso cref="NonSubtaskLabelRule" />
    public async Task ExecuteAsync(TodoistRuleContext context, CancellationToken cancellationToken)
    {
        logger.LogInformation("Fetching non subtasks but with subtask label...");
        var tasks = await FetchNonSubtasksWithSubtaskLabelAsync(cancellationToken);

        if (tasks.Count == 0)
        {
            logger.LogInformation("No non-subtasks with subtask label to update.");
            return;
        }

        logger.LogInformation("Removing subtask label from non-subtasks...");

        var updatedCount = await todoist.UpdateTasksAsync(
            tasks,
            task => new
            {
                labels = task.Labels.Where(label => !IsSubtaskLabel(label)).ToArray()
            },
            cancellationToken: cancellationToken);

        logger.LogInformation("Updated labels for {UpdatedCount} non-subtasks.", updatedCount);

        if (updatedCount > 0)
        {
            context.AddMessage(NotificationFormatter.BuildNumberedListMessage(
                $"Removed '{Constants.SubtaskLabel}' label from {updatedCount} non-subtasks:",
                tasks.Select(task => task.Content)));
        }
    }

    private async Task<List<TodoistTask>> FetchNonSubtasksWithSubtaskLabelAsync(CancellationToken cancellationToken)
    {
        var tasks = (await todoist.GetTasksByFilterAsync(NonSubtaskWithSubtaskLabelFilter, cancellationToken)).ToArray();
        TodoistGuards.EnsureAllTasksAreTopLevel(tasks, nameof(NonSubtaskLabelRule));
        TodoistGuards.EnsureAllTasksContainLabel(tasks, Constants.SubtaskLabel, nameof(NonSubtaskLabelRule));

        return [.. tasks];
    }

    private static bool IsSubtaskLabel(string label)
    {
        return string.Equals(label, Constants.SubtaskLabel, StringComparison.Ordinal);
    }
}
