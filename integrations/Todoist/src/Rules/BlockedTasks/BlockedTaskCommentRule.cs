using Integrations.Todoist.TodoistClient;
using Microsoft.Extensions.Logging;
using Refit;

namespace Integrations.Todoist.Rules.BlockedTasks;

/// <summary>
/// Validates that blocked tasks have blocker comments pointing to Todoist task URLs.
/// Also adds blocker labels to tasks from the comments.
/// </summary>
/// <remarks>
/// <b>Warning:</b> This rule modifies Todoist data.
/// </remarks>
internal sealed class BlockedTaskCommentRule(
    ITodoistApi todoist,
    ILogger<BlockedTaskCommentRule> logger) : ITodoistRule
{
    private const string BlockedFilter = "@blocked";
    private const string BlockerFilter = "@blocker";
    private const string BlockerCommentPrefix = "[blocker]";
    private const string TodoistTaskPathPrefix = "/app/task/";

    public int Order => 6;

    /// <inheritdoc />
    /// <seealso cref="BlockedTaskCommentRule" />
    public async Task ExecuteAsync(TodoistRuleContext context, CancellationToken cancellationToken)
    {
        logger.LogInformation("Fetching tasks with the {Label} label...", Constants.BlockedLabel);
        var blockedTasks = (await todoist.GetTasksByFilterAsync(BlockedFilter, cancellationToken)).ToArray();
        TodoistGuards.EnsureAllTasksContainLabel(blockedTasks, Constants.BlockedLabel, nameof(BlockedTaskCommentRule));

        if (blockedTasks.Length == 0)
            logger.LogInformation("No blocked tasks found.");

        var validationResults = await Task.WhenAll(blockedTasks.Select(task => ValidateCommentsAsync(task, cancellationToken)));

        var syncResult = await SyncBlockerLabelsAsync(
            validationResults.SelectMany(result => result.BlockerReferences).ToArray(),
            cancellationToken);

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

    private async Task<ValidationResult> ValidateCommentsAsync(TodoistTask task, CancellationToken cancellationToken)
    {
        var blockerComments = (await todoist.GetCommentsByTaskAsync(task.Id, cancellationToken))
            .Select(comment => comment.Content.Trim())
            .Where(comment => comment.StartsWith(BlockerCommentPrefix, StringComparison.Ordinal))
            .ToArray();

        if (blockerComments.Length == 0)
            return new ValidationResult(task, [], "missing blocker comment");

        var blockerReferences = new List<BlockerReference>(blockerComments.Length);

        foreach (var blockerComment in blockerComments)
        {
            if (!TryGetBlockerTaskId(blockerComment, out var blockerTaskId))
                return new ValidationResult(task, blockerReferences, $"invalid blocker URL: {blockerComment}");

            blockerReferences.Add(new BlockerReference(task, blockerComment, blockerTaskId));
        }

        return new ValidationResult(task, blockerReferences, null);
    }

    private async Task<SyncBlockerLabelsResult> SyncBlockerLabelsAsync(
        IReadOnlyCollection<BlockerReference> blockerReferences,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Fetching tasks with the {Label} label...", Constants.BlockerLabel);
        var currentBlockerTasks = (await todoist.GetTasksByFilterAsync(BlockerFilter, cancellationToken)).ToArray();
        TodoistGuards.EnsureAllTasksContainLabel(
            currentBlockerTasks,
            Constants.BlockerLabel,
            $"{nameof(BlockedTaskCommentRule)} blocker fetch");

        var desiredBlockerTaskIdSet = new HashSet<string>(StringComparer.Ordinal);
        var blockerTasksToAdd = new List<TodoistTask>();
        var invalidReferences = new List<BlockerReference>();

        foreach (var referencesByTaskId in blockerReferences
                     .GroupBy(reference => reference.BlockerTaskId, StringComparer.Ordinal))
        {
            try
            {
                var blockerTask = await todoist.GetTaskAsync(referencesByTaskId.Key, cancellationToken);
                TodoistGuards.EnsureOnlyExpectedTaskIds(
                    [blockerTask],
                    [referencesByTaskId.Key],
                    $"{nameof(BlockedTaskCommentRule)} blocker task fetch");
                desiredBlockerTaskIdSet.Add(blockerTask.Id);

                if (!HasBlockerLabel(blockerTask.Labels))
                    blockerTasksToAdd.Add(blockerTask);
            }
            catch (Exception ex) when (IsInvalidBlockerTaskException(ex))
            {
                logger.LogWarning(ex, "Ignoring blocker task references for invalid task ID {TaskId}.", referencesByTaskId.Key);
                invalidReferences.AddRange(referencesByTaskId);
            }
        }

        var blockerTasksToRemove = currentBlockerTasks
            .Where(task => !desiredBlockerTaskIdSet.Contains(task.Id))
            .ToArray();

        if (blockerTasksToAdd.Count == 0 && blockerTasksToRemove.Length == 0)
        {
            logger.LogInformation("No blocker label updates required.");
            return new SyncBlockerLabelsResult([], invalidReferences);
        }

        if (blockerTasksToAdd.Count > 0)
        {
            logger.LogInformation("Adding {Label} label to {TaskCount} tasks...", Constants.BlockerLabel, blockerTasksToAdd.Count);
            await todoist.UpdateTasksAsync(
                blockerTasksToAdd,
                task => new
                {
                    labels = task.Labels
                        .Append(Constants.BlockerLabel)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray()
                },
                cancellationToken: cancellationToken);
        }

        if (blockerTasksToRemove.Length > 0)
        {
            logger.LogInformation("Removing {Label} label from {TaskCount} tasks...", Constants.BlockerLabel, blockerTasksToRemove.Length);
            await todoist.UpdateTasksAsync(
                blockerTasksToRemove,
                task => new
                {
                    labels = task.Labels
                        .Where(label => !IsBlockerLabel(label))
                        .ToArray()
                },
                cancellationToken: cancellationToken);
        }

        return new SyncBlockerLabelsResult(
            blockerTasksToAdd
            .Concat(blockerTasksToRemove)
            .DistinctBy(task => task.Id)
            .ToArray(),
            invalidReferences);
    }

    private static bool TryGetBlockerTaskId(string blockerComment, out string blockerTaskId)
    {
        blockerTaskId = "";

        if (!blockerComment.StartsWith(BlockerCommentPrefix, StringComparison.Ordinal))
            return false;

        var url = blockerComment[BlockerCommentPrefix.Length..].Trim();

        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var parsedUrl))
            return false;

        if (!string.Equals(parsedUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.Equals(parsedUrl.Host, "app.todoist.com", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrEmpty(parsedUrl.Query) || !string.IsNullOrEmpty(parsedUrl.Fragment))
            return false;

        if (!parsedUrl.AbsolutePath.StartsWith(TodoistTaskPathPrefix, StringComparison.Ordinal))
            return false;

        var taskSegment = parsedUrl.AbsolutePath[TodoistTaskPathPrefix.Length..];

        if (string.IsNullOrWhiteSpace(taskSegment) || taskSegment.Contains('/'))
            return false;

        var separatorIndex = taskSegment.LastIndexOf('-');
        var taskId = separatorIndex >= 0
            ? taskSegment[(separatorIndex + 1)..]
            : taskSegment;

        if (string.IsNullOrWhiteSpace(taskId))
            return false;

        blockerTaskId = taskId;
        return true;
    }

    private static bool HasBlockerLabel(IEnumerable<string> labels)
    {
        return labels.Any(IsBlockerLabel);
    }

    private static bool IsBlockerLabel(string label)
    {
        return string.Equals(label, Constants.BlockerLabel, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInvalidBlockerTaskException(Exception exception)
    {
        if (exception is ApiException apiException)
            return apiException.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.NotFound;

        var statusCode = exception.GetType().GetProperty("StatusCode")?.GetValue(exception);

        return statusCode switch
        {
            System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.NotFound => true,
            400 or 404 => true,
            _ => false
        };
    }

    private sealed record ValidationResult(
        TodoistTask Task,
        IReadOnlyCollection<BlockerReference> BlockerReferences,
        string? Error);

    private sealed record BlockerReference(
        TodoistTask BlockedTask,
        string Comment,
        string BlockerTaskId);

    private sealed record SyncBlockerLabelsResult(
        IReadOnlyCollection<TodoistTask> UpdatedTasks,
        IReadOnlyCollection<BlockerReference> InvalidReferences);
}
