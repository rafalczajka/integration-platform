using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Integrations.Todoist.Options;
using Integrations.Todoist.TodoistClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Integrations.Todoist.Rules.Labels;

/// <summary>
/// Adds the direct project's name as a label to top-level tasks in Someday and its subprojects.
/// </summary>
/// <remarks>
/// <b>Warning:</b> This rule modifies Todoist data.
/// </remarks>
internal sealed class SomedayProjectLabelRule(
    ITodoistApi todoist,
    IOptions<TodoistProjectIdsOptions> options,
    ILogger<SomedayProjectLabelRule> logger) : ITodoistRule
{
    private const int MaxLabelLength = 128;

    private readonly string _somedayProjectId = options.Value.Someday;

    /// <inheritdoc />
    /// <seealso cref="SomedayProjectLabelRule" />
    public async Task ExecuteAsync(TodoistRuleContext context, CancellationToken cancellationToken)
    {
        var projects = await FetchProjectTreeAsync(context, cancellationToken);
        if (projects.Count < 1) return;

        var plannedUpdates = await PlanLabelUpdatesAsync(projects, context, cancellationToken);
        if (plannedUpdates.Length < 1) return;

        var labels = await EnsureLabelsExistAsync(plannedUpdates, cancellationToken);
        var updatedCount = await ApplyLabelUpdatesAsync(plannedUpdates, labels, cancellationToken);

        ReportUpdatedTasks(context, plannedUpdates, labels, updatedCount);
    }

    private async Task<IReadOnlyCollection<TodoistProject>> FetchProjectTreeAsync(
        TodoistRuleContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_somedayProjectId))
        {
            logger.LogWarning("Someday project ID is not configured.");
            context.AddMessage("Someday project ID is not configured.");
            return [];
        }

        var projects = (await todoist.GetProjectsAsync(cancellationToken)).DistinctBy(project => project.Id).ToArray();
        var root = projects.SingleOrDefault(project => project.Id == _somedayProjectId);

        if (root is null)
        {
            logger.LogWarning("Configured Someday project {ProjectId} was not found.", _somedayProjectId);
            context.AddMessage("Configured Someday project was not found among active projects.");
            return [];
        }

        return [.. GetProjectTree(projects, root)];
    }

    private async Task<TaskLabelUpdate[]> PlanLabelUpdatesAsync(
        IEnumerable<TodoistProject> projects,
        TodoistRuleContext context,
        CancellationToken cancellationToken)
    {
        var updates = new List<TaskLabelUpdate>();
        var skippedProjects = new List<string>();

        foreach (var project in projects)
        {
            var label = ToKebabCase(project.Name);

            if (TryGetInvalidLabelReason(label, out var invalidLabelReason))
            {
                logger.LogWarning("Skipping project {ProjectName}: {Reason}", project.Name, invalidLabelReason);
                skippedProjects.Add($"{project.Name} - {invalidLabelReason}");
                continue;
            }

            logger.LogInformation("Fetching tasks from project {ProjectName}...", project.Name);

            var tasks = (await todoist.GetTasksByProjectAsync(project.Id, cancellationToken)).ToArray();
            TodoistGuards.EnsureOnlyProjectTasks(tasks, project.Id, nameof(SomedayProjectLabelRule));

            updates.AddRange(tasks
                .Where(task => string.IsNullOrWhiteSpace(task.ParentId))
                .Where(task => !task.Labels.Contains(label, StringComparer.OrdinalIgnoreCase))
                .Select(task => new TaskLabelUpdate(task, label)));
        }

        if (skippedProjects.Count > 0)
        {
            context.AddMessage(NotificationFormatter.BuildNumberedListMessage(
                $"Skipped {skippedProjects.Count} projects while adding project labels:",
                skippedProjects.OrderBy(project => project, StringComparer.OrdinalIgnoreCase)));
        }

        var plannedUpdates = updates
            .DistinctBy(update => update.Task.Id)
            .ToArray();

        TodoistGuards.EnsureAllTasksAreTopLevel(
            plannedUpdates.Select(update => update.Task),
            nameof(SomedayProjectLabelRule));

        return plannedUpdates;
    }

    private async Task<IReadOnlyDictionary<string, string>> EnsureLabelsExistAsync(
        IEnumerable<TaskLabelUpdate> plannedUpdates,
        CancellationToken cancellationToken)
    {
        var labels = (await todoist.GetLabelsAsync(cancellationToken))
            .GroupBy(
                label => label.Name,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().Name,
                StringComparer.OrdinalIgnoreCase);

        var plannedUpdateLabels = plannedUpdates
            .Select(update => update.Label)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var labelName in plannedUpdateLabels)
        {
            if (labels.ContainsKey(labelName)) continue;

            var createdLabel = await todoist.CreateLabelAsync(new { name = labelName }, cancellationToken);
            labels.Add(labelName, createdLabel.Name);
        }

        return labels;
    }

    private async Task<int> ApplyLabelUpdatesAsync(
        IReadOnlyCollection<TaskLabelUpdate> plannedUpdates,
        IReadOnlyDictionary<string, string> labels,
        CancellationToken cancellationToken)
    {
        var labelsByTaskId = plannedUpdates.ToDictionary(
            update => update.Task.Id,
            update => update.Task.Labels.Append(labels[update.Label]).ToArray(),
            StringComparer.Ordinal);

        var updatedCount = await todoist.UpdateTasksAsync(
            plannedUpdates.Select(update => update.Task),
            task => new { labels = labelsByTaskId[task.Id] },
            cancellationToken: cancellationToken);

        logger.LogInformation("Added project labels to {UpdatedCount} Someday tasks.", updatedCount);

        return updatedCount;
    }

    private static void ReportUpdatedTasks(
        TodoistRuleContext context,
        IEnumerable<TaskLabelUpdate> plannedUpdates,
        IReadOnlyDictionary<string, string> labels,
        int updatedCount)
    {
        if (updatedCount < 1) return;

        context.AddMessage(NotificationFormatter.BuildNumberedListMessage(
            $"Added project labels to {updatedCount} tasks:",
            plannedUpdates
                .OrderBy(update => update.Task.Content, StringComparer.OrdinalIgnoreCase)
                .ThenBy(update => update.Task.Content, StringComparer.Ordinal)
                .Select(update => $"{update.Task.Content} (@{labels[update.Label]})")));
    }

    private static IEnumerable<TodoistProject> GetProjectTree(
        IEnumerable<TodoistProject> projects,
        TodoistProject root)
    {
        var children = projects
            .Where(project => !string.IsNullOrWhiteSpace(project.ParentId))
            .ToLookup(project => project.ParentId!, StringComparer.Ordinal);

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<TodoistProject>();

        pending.Enqueue(root);

        while (pending.TryDequeue(out var project))
        {
            if (!visited.Add(project.Id)) continue;

            yield return project;

            foreach (var child in children[project.Id]) pending.Enqueue(child);
        }
    }

    private static string ToKebabCase(string projectName)
    {
        var decomposed = projectName.Normalize(NormalizationForm.FormD)
            .Replace('\u0141', 'L')
            .Replace('\u0142', 'l');

        var name = string.Concat(decomposed.Where(character =>
            CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark));

        var kebabName = JsonNamingPolicy.KebabCaseLower.ConvertName(name);
        return Regex.Replace(kebabName, "[^a-z0-9]+", "-").Trim('-');
    }

    private static bool TryGetInvalidLabelReason(string label, out string? invalidLabelReason)
    {
        invalidLabelReason = label.Length switch
        {
            0 => "project name produces an empty label",
            > MaxLabelLength => $"project label exceeds {MaxLabelLength} characters",
            _ => null
        };

        return invalidLabelReason is not null;
    }

    private sealed record TaskLabelUpdate(TodoistTask Task, string Label);
}
