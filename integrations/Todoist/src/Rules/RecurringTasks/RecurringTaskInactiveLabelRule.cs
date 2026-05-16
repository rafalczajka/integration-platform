using Integrations.Todoist.Options;
using Integrations.Todoist.TodoistClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Integrations.Todoist.Rules.RecurringTasks;

/// <summary>
/// Ensures tasks in the Recurring project follow inactive label rules:
/// tasks without a due date get only the inactive label, and recurring tasks
/// never keep the inactive label.
/// </summary>
/// <remarks>
/// <b>Warning:</b> This rule modifies Todoist data.
/// </remarks>
internal sealed class RecurringTaskInactiveLabelRule(
    ITodoistApi todoist,
    IOptions<TodoistProjectIdsOptions> options,
    ILogger<RecurringTaskInactiveLabelRule> logger) : ITodoistRule
{
    public int Order => 4;

    private readonly string _recurringProjectId = options.Value.Recurring;

    /// <inheritdoc />
    /// <seealso cref="RecurringTaskInactiveLabelRule" />
    public async Task ExecuteAsync(TodoistRuleContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_recurringProjectId))
        {
            logger.LogWarning("Recurring project ID is not configured.");
            context.AddMessage("Recurring project ID is not configured.");
            return;
        }

        logger.LogInformation("Fetching tasks from Recurring project...");

        var recurringProjectTasks = (await todoist.GetTasksByProjectAsync(_recurringProjectId, cancellationToken))
            .ToList();

        TodoistGuards.EnsureOnlyProjectTasks(
            recurringProjectTasks,
            _recurringProjectId,
            nameof(RecurringTaskInactiveLabelRule));

        var tasksInRecurringProject = recurringProjectTasks
            .Where(task => string.IsNullOrWhiteSpace(task.ParentId))
            .ToList();

        if (tasksInRecurringProject.Count == 0)
        {
            logger.LogInformation("No tasks found.");
            return;
        }

        var labelsToUpdate = new Dictionary<string, IReadOnlyCollection<string>>();
        var tasksWithNonRecurringDueDate = new List<TodoistTask>();

        foreach (var task in tasksInRecurringProject)
        {
            ApplyRules(task, labelsToUpdate, tasksWithNonRecurringDueDate);
        }

        var updatedTasks = await SaveChangesAsync(tasksInRecurringProject, labelsToUpdate, cancellationToken);

        if (updatedTasks.Count > 0)
        {
            context.AddMessage(NotificationFormatter.BuildNumberedListMessage(
                $"Updated inactive labels for {updatedTasks.Count} tasks in the Recurring project:",
                updatedTasks.Select(task => task.Content)));
        }

        ReportNonRecurringDueDates(tasksWithNonRecurringDueDate, context);
    }

    private static void ApplyRules(
        TodoistTask task,
        IDictionary<string, IReadOnlyCollection<string>> labelsToUpdate,
        List<TodoistTask> tasksWithNonRecurringDueDate)
    {
        var labels = GetLabelsSnapshot(task.Labels);
        var due = task.Due;

        if (due is null)
        {
            ApplyInactiveLabelOnly(task, labels, labelsToUpdate);
            return;
        }

        if (!due.IsRecurring)
        {
            tasksWithNonRecurringDueDate.Add(task);
            return;
        }

        RemoveInactiveLabel(task, labels, labelsToUpdate);
    }

    private async Task<IReadOnlyCollection<TodoistTask>> SaveChangesAsync(
        IReadOnlyCollection<TodoistTask> tasks,
        Dictionary<string, IReadOnlyCollection<string>> labelsToUpdate,
        CancellationToken cancellationToken)
    {
        if (labelsToUpdate.Count == 0)
        {
            logger.LogInformation("No label updates required.");
            return [];
        }

        var tasksToUpdate = tasks.Where(task => labelsToUpdate.ContainsKey(task.Id)).ToList();

        var updatedCount = await todoist.UpdateTasksAsync(
            tasksToUpdate,
            task => new { labels = labelsToUpdate[task.Id] },
            cancellationToken: cancellationToken);

        logger.LogInformation("Updated labels for {UpdatedCount} tasks.", updatedCount);
        return tasksToUpdate;
    }

    private static IReadOnlyCollection<string> GetLabelsSnapshot(IEnumerable<string> labels)
    {
        return labels as IReadOnlyCollection<string> ?? [.. labels];
    }

    private static void ApplyInactiveLabelOnly(
        TodoistTask task,
        IReadOnlyCollection<string> labels,
        IDictionary<string, IReadOnlyCollection<string>> labelsToUpdate)
    {
        if (HasOnlyInactiveLabel(labels)) return;
        labelsToUpdate[task.Id] = [Constants.InactiveLabel];
    }

    private static void RemoveInactiveLabel(
        TodoistTask task,
        IReadOnlyCollection<string> labels,
        IDictionary<string, IReadOnlyCollection<string>> labelsToUpdate)
    {
        if (!HasInactiveLabel(labels)) return;

        var newLabels = labels
            .Where(label => !IsInactiveLabel(label))
            .ToArray();

        labelsToUpdate[task.Id] = newLabels;
    }

    private static bool HasOnlyInactiveLabel(IReadOnlyCollection<string> labels)
    {
        return labels.Count == 1 && HasInactiveLabel(labels);
    }

    private static bool HasInactiveLabel(IEnumerable<string> labels)
    {
        return labels.Any(IsInactiveLabel);
    }

    private static bool IsInactiveLabel(string label)
    {
        return string.Equals(label, Constants.InactiveLabel, StringComparison.OrdinalIgnoreCase);
    }

    private void ReportNonRecurringDueDates(
        List<TodoistTask> tasksWithNonRecurringDueDate,
        TodoistRuleContext context)
    {
        var count = tasksWithNonRecurringDueDate.Count;

        if (count < 1) return;

        logger.LogWarning("Found {TaskCount} tasks with non-recurring due dates in Recurring project.", count);

        context.AddMessage(NotificationFormatter.BuildNumberedListMessage(
            $"Found {count} tasks with non-recurring due dates in Recurring project:",
            tasksWithNonRecurringDueDate.Select(task => task.Content)));
    }
}
