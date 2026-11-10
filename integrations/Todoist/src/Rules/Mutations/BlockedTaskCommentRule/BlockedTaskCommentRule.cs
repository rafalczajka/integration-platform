using Integrations.Notifications;
using Integrations.Todoist.TodoistClient;
using Microsoft.Extensions.Logging;

namespace Integrations.Todoist.Rules.Mutations.BlockedTaskCommentRule;

/// <summary>
/// Validates that blocked tasks have blocker comments pointing to Todoist task URLs.
/// Synchronizes blocker labels with task references in those comments.
/// </summary>
/// <remarks>
/// <b>Warning:</b> This rule modifies Todoist data.
/// </remarks>
internal sealed class BlockedTaskCommentRule(
    ITodoistApi todoist,
    BlockerLabelSynchronizer synchronizer,
    ILogger<BlockedTaskCommentRule> logger) : ITodoistRule
{
    /// <inheritdoc />
    /// <seealso cref="BlockedTaskCommentRule" />
    public async Task ExecuteAsync(TodoistRuleContext context, CancellationToken cancellationToken)
    {
        logger.LogInformation("Fetching tasks with the {Label} label...", Constants.BlockedLabel);

        var blockedTasks = (await todoist.GetTasksByFilterAsync($"@{Constants.BlockedLabel}", cancellationToken)).ToArray();

        TodoistGuards.EnsureAllTasksContainLabel(
            blockedTasks,
            Constants.BlockedLabel,
            $"{nameof(BlockedTaskCommentRule)} @{Constants.BlockedLabel} fetch");

        var validationResults = await Task.WhenAll(
            blockedTasks.Select(task => ValidateCommentsAsync(task, cancellationToken)));

        var syncResult = await synchronizer.SyncAsync(
            [.. validationResults.SelectMany(result => result.BlockerReferences)],
            cancellationToken);

        ReportResults(context, validationResults, syncResult);
    }

    private async Task<ValidationResult> ValidateCommentsAsync(TodoistTask task, CancellationToken cancellationToken)
    {
        var blockerComments = (await todoist.GetCommentsByTaskAsync(task.Id, cancellationToken))
            .Select(comment => comment.Content.Trim())
            .Where(comment => comment.StartsWith(BlockerCommentParser.Prefix, StringComparison.Ordinal))
            .ToArray();

        if (blockerComments.Length == 0)
            return new ValidationResult(task, [], "missing blocker comment");

        var references = new List<BlockerReference>(blockerComments.Length);

        foreach (var comment in blockerComments)
        {
            if (!BlockerCommentParser.TryGetTaskId(comment, out var taskId))
                return new ValidationResult(task, references, $"invalid blocker URL: {comment}");

            references.Add(new BlockerReference(task, comment, taskId));
        }

        return new ValidationResult(task, references, null);
    }

    private void ReportResults(
        TodoistRuleContext context,
        IEnumerable<ValidationResult> validationResults,
        SyncBlockerLabelsResult syncResult)
    {
        if (syncResult.UpdatedTasks.Count > 0)
        {
            context.AddMessage(NotificationFormatter.BuildNumberedListMessage(
                $"Updated '{Constants.BlockerLabel}' label for {syncResult.UpdatedTasks.Count} tasks:",
                syncResult.UpdatedTasks
                    .OrderBy(task => task.Content, StringComparer.OrdinalIgnoreCase)
                    .Select(task => task.Content)));
        }

        if (syncResult.InvalidReferences.Count > 0)
        {
            context.AddMessage(NotificationFormatter.BuildNumberedListMessage(
                $"Ignored {syncResult.InvalidReferences.Count} blocker references with invalid task IDs:",
                syncResult.InvalidReferences
                    .OrderBy(reference => reference.BlockedTask.Content, StringComparer.OrdinalIgnoreCase)
                    .Select(reference => $"{reference.BlockedTask.Content} - {reference.Comment}")));
        }

        var invalidTasks = validationResults
            .Where(result => result.Error is not null)
            .OrderBy(result => result.Task.Content, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (invalidTasks.Length == 0)
        {
            logger.LogInformation("All blocked tasks contain valid blocker comments.");
            return;
        }

        logger.LogWarning("Found {TaskCount} blocked tasks with invalid blocker comments.", invalidTasks.Length);

        context.AddMessage(NotificationFormatter.BuildNumberedListMessage(
            $"Found {invalidTasks.Length} blocked tasks with invalid blocker comments:",
            invalidTasks.Select(task => $"{task.Task.Content} - {task.Error!}")));
    }

    private sealed record ValidationResult(
        TodoistTask Task,
        IReadOnlyCollection<BlockerReference> BlockerReferences,
        string? Error);
}
