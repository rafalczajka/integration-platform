using Integrations.Todoist.TodoistClient;
using Microsoft.Extensions.Logging;

namespace Integrations.Todoist.Rules.Subtasks;

/// <summary>
/// Removes due dates from subtasks so that only parent tasks define scheduling.
/// </summary>
/// <remarks>
/// <b>Warning:</b> This rule modifies Todoist data.
/// </remarks>
internal sealed class SubtaskDueDateRule(
    ITodoistApi todoist,
    ILogger<SubtaskDueDateRule> logger) : ITodoistRule
{
    public int Order => 3;

    private const string RemoveDueDateValue = "no due date";
    private const string SubtaskFilter = "subtask";

    /// <inheritdoc />
    /// <seealso cref="SubtaskDueDateRule" />
    public async Task ExecuteAsync(TodoistRuleContext context, CancellationToken cancellationToken)
    {
        logger.LogInformation("Fetching subtasks for due date rule...");
        var subtasks = await FetchSubtasksAsync(cancellationToken);

        if (subtasks.Count == 0)
        {
            logger.LogInformation("No subtasks to update.");
            return;
        }

        var tasksWithDueDate = subtasks.Where(task => task.Due is not null).ToList();

        if (tasksWithDueDate.Count == 0)
        {
            logger.LogInformation("No subtasks due dates to remove.");
            return;
        }

        logger.LogInformation("Removing due dates from subtasks...");

        var updatedCount = await todoist.UpdateTasksAsync(
            tasksWithDueDate,
            _ => new { due_string = RemoveDueDateValue },
            cancellationToken: cancellationToken);

        logger.LogInformation("Removed due dates from {UpdatedCount} subtasks.", updatedCount);

        if (updatedCount > 0)
        {
            context.AddMessage(NotificationFormatter.BuildNumberedListMessage(
                $"Removed due dates from {updatedCount} subtasks:",
                tasksWithDueDate.Select(task => task.Content)));
        }
    }

    private async Task<List<TodoistTask>> FetchSubtasksAsync(CancellationToken cancellationToken)
    {
        var subtasks = (await todoist.GetTasksByFilterAsync(SubtaskFilter, cancellationToken)).ToArray();
        TodoistGuards.EnsureAllTasksAreSubtasks(subtasks, nameof(SubtaskDueDateRule));
        return [.. subtasks];
    }
}
