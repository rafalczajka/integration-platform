using Integrations.Todoist.TodoistClient;
using Microsoft.Extensions.Logging;

namespace Integrations.Todoist.Rules.Subtasks;

/// <summary>
/// Enforces that subtasks have only the subtask label and propagates any other
/// labels from subtasks to their parent tasks.
/// </summary>
/// <remarks>
/// <b>Warning:</b> This rule modifies Todoist data.
/// </remarks>
internal sealed class SubtaskLabelRule(
    ITodoistApi todoist,
    ILogger<SubtaskLabelRule> logger) : ITodoistRule
{
    public int Order => 1;

    private const string SubtaskFilter = "subtask";

    /// <inheritdoc />
    /// <seealso cref="SubtaskLabelRule" />
    public async Task ExecuteAsync(TodoistRuleContext context, CancellationToken cancellationToken)
    {
        logger.LogInformation("Fetching subtasks for label rule...");
        var subtasks = await FetchSubtasksAsync(cancellationToken);

        if (subtasks.Count == 0)
        {
            logger.LogInformation("No subtasks to update.");
            return;
        }

        var subtaskLabelUpdates = new Dictionary<string, IReadOnlyCollection<string>>();
        var parentLabelsToAdd = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var task in subtasks)
        {
            var labels = GetLabelsSnapshot(task.Labels);
            var shouldResetLabels = !HasOnlySubtaskLabel(labels);

            if (!shouldResetLabels) continue;

            subtaskLabelUpdates[task.Id] = [Constants.SubtaskLabel];
            AddLabelsToParent(parentLabelsToAdd, task.ParentId, labels);
        }

        var updatedSubtasks = await ApplySubtaskLabelUpdatesAsync(subtasks, subtaskLabelUpdates, cancellationToken);
        var updatedParents = await UpdateParentLabelsAsync(parentLabelsToAdd, cancellationToken);

        if (updatedSubtasks.Count > 0)
        {
            context.AddMessage(NotificationFormatter.BuildNumberedListMessage(
                $"Reset labels to only '{Constants.SubtaskLabel}' for {updatedSubtasks.Count} subtasks:",
                updatedSubtasks.Select(task => task.Content)));
        }

        if (updatedParents.Count > 0)
        {
            context.AddMessage(NotificationFormatter.BuildNumberedListMessage(
                $"Propagated labels from subtasks to {updatedParents.Count} parent tasks:",
                updatedParents.Select(task => task.Content)));
        }
    }

    private async Task<List<TodoistTask>> FetchSubtasksAsync(CancellationToken cancellationToken)
    {
        var subtasks = (await todoist.GetTasksByFilterAsync(SubtaskFilter, cancellationToken)).ToArray();
        TodoistGuards.EnsureAllTasksAreSubtasks(subtasks, nameof(SubtaskLabelRule));
        return [.. subtasks];
    }

    private async Task<IReadOnlyCollection<TodoistTask>> ApplySubtaskLabelUpdatesAsync(
        IReadOnlyCollection<TodoistTask> subtasks,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> subtaskLabelUpdates,
        CancellationToken cancellationToken)
    {
        if (subtaskLabelUpdates.Count == 0)
        {
            logger.LogInformation("No subtasks to update.");
            return [];
        }

        logger.LogInformation("Updating subtasks...");

        var tasksToUpdate = subtasks.Where(task => subtaskLabelUpdates.ContainsKey(task.Id)).ToList();

        var updatedCount = await todoist.UpdateTasksAsync(
            tasksToUpdate,
            task => new { labels = subtaskLabelUpdates[task.Id] },
            cancellationToken: cancellationToken);

        logger.LogInformation("Updated {UpdatedCount} subtasks.", updatedCount);
        return tasksToUpdate;
    }

    private async Task<IReadOnlyCollection<TodoistTask>> UpdateParentLabelsAsync(
        IReadOnlyDictionary<string, HashSet<string>> parentLabelsToAdd,
        CancellationToken cancellationToken)
    {
        if (parentLabelsToAdd.Count == 0)
        {
            logger.LogInformation("No parent labels to update.");
            return [];
        }

        logger.LogInformation("Updating parent labels...");

        var parentIds = parentLabelsToAdd.Keys.ToArray();
        var parents = (await todoist.GetTasksAsync(parentIds, cancellationToken)).ToList();
        TodoistGuards.EnsureOnlyExpectedTaskIds(
            parents,
            parentIds,
            $"{nameof(SubtaskLabelRule)} parent fetch");

        var updatedCount = await todoist.UpdateTasksAsync(
            parents,
            parentTask =>
            {
                if (!parentLabelsToAdd.TryGetValue(parentTask.Id, out var labelsToAdd))
                    return new { labels = parentTask.Labels.ToArray() };

                var newLabels = MergeParentLabels(parentTask.Labels, labelsToAdd);
                return new { labels = newLabels };
            },
            cancellationToken: cancellationToken);

        logger.LogInformation("Updated labels for {UpdatedCount} parents.", updatedCount);
        return parents;
    }

    private static void AddLabelsToParent(
        IDictionary<string, HashSet<string>> parentLabelsToAdd,
        string parentId,
        IReadOnlyCollection<string> labels)
    {
        if (string.IsNullOrWhiteSpace(parentId)) return;

        if (!parentLabelsToAdd.TryGetValue(parentId, out var existingLabels))
        {
            existingLabels = new HashSet<string>(StringComparer.Ordinal);
            parentLabelsToAdd[parentId] = existingLabels;
        }

        foreach (var label in labels.Where(label => !IsSubtaskLabel(label)))
        {
            existingLabels.Add(label);
        }
    }

    private static string[] MergeParentLabels(
        IEnumerable<string> parentLabels,
        IEnumerable<string> subtaskLabels)
    {
        return [..parentLabels
            .Concat(subtaskLabels)
            .Distinct()
            .Where(label => !IsSubtaskLabel(label))];
    }

    private static IReadOnlyCollection<string> GetLabelsSnapshot(IEnumerable<string> labels)
    {
        return labels as IReadOnlyCollection<string> ?? [.. labels];
    }

    private static bool HasOnlySubtaskLabel(IReadOnlyCollection<string> labels)
    {
        return labels.Count == 1 && labels.Any(IsSubtaskLabel);
    }

    private static bool IsSubtaskLabel(string label)
    {
        return string.Equals(label, Constants.SubtaskLabel, StringComparison.Ordinal);
    }
}
