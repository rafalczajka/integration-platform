using Integrations.Todoist.TodoistClient;
using Microsoft.Extensions.Logging;

namespace Integrations.Todoist.Rules.Labels;

/// <summary>
/// Deletes labels that are not assigned to any task.
/// </summary>
/// <remarks>
/// <b>Warning:</b> This rule modifies Todoist data.
/// </remarks>
internal sealed class UnusedLabelsCleanupRule(
    ITodoistApi todoist,
    ILogger<UnusedLabelsCleanupRule> logger) : ITodoistRule
{
    private static readonly HashSet<string> ExcludedLabels =
    [
        Constants.ImpactLable,
        Constants.InactiveLabel,
        Constants.SubtaskLabel,
        Constants.BlockedLabel,
        Constants.BlockerLabel
    ];

    public int Order => 8;

    /// <inheritdoc />
    /// <seealso cref="UnusedLabelsCleanupRule" />
    public async Task ExecuteAsync(TodoistRuleContext context, CancellationToken cancellationToken)
    {
        logger.LogInformation("Fetching labels for cleanup...");
        var labels = (await todoist.GetLabelsAsync(cancellationToken)).ToArray();

        if (labels.Length == 0)
        {
            logger.LogInformation("No labels found.");
            return;
        }

        logger.LogInformation("Fetching tasks for label usage analysis...");
        var tasks = await todoist.GetTasksAsync(cancellationToken);

        var usedLabels = tasks
            .SelectMany(task => task.Labels)
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var labelsToDelete = labels
            .Where(label =>
                !string.IsNullOrWhiteSpace(label.Name) &&
                !usedLabels.Contains(label.Name) &&
                !ExcludedLabels.Contains(label.Name))
            .ToList();

        if (labelsToDelete.Count == 0)
        {
            logger.LogInformation("No unused labels found.");
            return;
        }

        TodoistGuards.EnsureNoProtectedLabelsAreDeleted(
            labelsToDelete,
            ExcludedLabels,
            nameof(UnusedLabelsCleanupRule));

        logger.LogInformation("Deleting {LabelCount} unused labels...", labelsToDelete.Count);
        var deletedCount = await todoist.DeleteLabelsAsync(labelsToDelete, cancellationToken: cancellationToken);

        logger.LogInformation("Deleted {DeletedCount} unused labels.", deletedCount);

        if (deletedCount > 0)
        {
            var deletedLabels = string.Join(", ", labelsToDelete.Select(label => label.Name).OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
            context.AddMessage($"Deleted {deletedCount} unused Todoist labels: {deletedLabels}.");
        }
    }
}
