using System.Net;
using System.Net.Http;
using Integrations.Todoist.TodoistClient;
using Microsoft.Extensions.Logging;
using Refit;

namespace Integrations.Todoist.Rules.Mutations.BlockedTaskCommentRule;

internal sealed class BlockerLabelSynchronizer(
    ITodoistApi todoist,
    ILogger<BlockerLabelSynchronizer> logger)
{
    public async Task<SyncBlockerLabelsResult> SyncAsync(
        IReadOnlyCollection<BlockerReference> references,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Fetching tasks with the {Label} label...", Constants.BlockerLabel);

        var currentBlockers = (await todoist.GetTasksByFilterAsync($"@{Constants.BlockerLabel}", cancellationToken)).ToArray();

        TodoistGuards.EnsureAllTasksContainLabel(
            currentBlockers,
            Constants.BlockerLabel,
            $"{nameof(BlockedTaskCommentRule)} @{Constants.BlockerLabel} fetch");

        var desiredBlockers = new List<TodoistTask>();
        var invalidReferences = new List<BlockerReference>();

        foreach (var group in references.GroupBy(reference => reference.BlockerTaskId, StringComparer.Ordinal))
        {
            try
            {
                var task = await todoist.GetTaskAsync(group.Key, cancellationToken);

                TodoistGuards.EnsureOnlyExpectedTaskIds(
                    [task],
                    [group.Key],
                    $"{nameof(BlockedTaskCommentRule)} blocker task fetch");

                desiredBlockers.Add(task);
            }
            catch (Exception ex) when (IsInvalidBlockerTaskException(ex))
            {
                logger.LogWarning(ex, "Ignoring blocker task references for invalid task ID {TaskId}.", group.Key);
                invalidReferences.AddRange(group);
            }
        }

        var desiredIds = desiredBlockers
            .Select(task => task.Id)
            .ToHashSet(StringComparer.Ordinal);

        var toAdd = desiredBlockers
            .Where(task => !task.Labels.Contains(Constants.BlockerLabel, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        var toRemove = currentBlockers
            .Where(task => !desiredIds.Contains(task.Id))
            .ToArray();

        await UpdateLabelsAsync(toAdd, add: true, cancellationToken);
        await UpdateLabelsAsync(toRemove, add: false, cancellationToken);

        return new SyncBlockerLabelsResult(
            [.. toAdd.Concat(toRemove).DistinctBy(task => task.Id)], invalidReferences);
    }

    private async Task UpdateLabelsAsync(
        IReadOnlyCollection<TodoistTask> tasks,
        bool add,
        CancellationToken cancellationToken)
    {
        if (tasks.Count == 0) return;

        logger.LogInformation("Updating {Label} label for {TaskCount} tasks (add: {Add})...",
            Constants.BlockerLabel, tasks.Count, add);

        await todoist.UpdateTasksAsync(tasks, task => new
        {
            labels = add
                ? task.Labels.Append(Constants.BlockerLabel).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                : task.Labels.Where(label => !string.Equals(
                    label, Constants.BlockerLabel, StringComparison.OrdinalIgnoreCase)).ToArray()
        }, cancellationToken: cancellationToken);
    }

    private static bool IsInvalidBlockerTaskException(Exception exception) => exception switch
    {
        ApiException { StatusCode: HttpStatusCode.BadRequest or HttpStatusCode.NotFound } => true,
        HttpRequestException { StatusCode: HttpStatusCode.BadRequest or HttpStatusCode.NotFound } => true,
        _ => false
    };
}

internal sealed record BlockerReference(
    TodoistTask BlockedTask,
    string Comment,
    string BlockerTaskId);

internal sealed record SyncBlockerLabelsResult(
    IReadOnlyCollection<TodoistTask> UpdatedTasks,
    IReadOnlyCollection<BlockerReference> InvalidReferences);
