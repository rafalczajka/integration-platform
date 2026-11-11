using Integrations.Notifications;
using Integrations.Todoist.TodoistClient;

namespace Integrations.Todoist.Reports;

internal sealed class WeeklyReport(ITodoistApi todoist)
{
    public async Task<WeeklyReportMessage> CreateAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var sinceDate = today.AddDays(-7);

        var since = new DateTimeOffset(sinceDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var until = new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var response = await todoist.GetCompletedTasksByCompletionDateAsync(since, until, cancellationToken);

        var taskIds = new HashSet<string>(StringComparer.Ordinal);

        var tasks = response.Where(task => task.Due?.IsRecurring != true)
            .Select(task => new
            {
                task,
                completedAt = task.CompletedAt ??
                    throw new InvalidOperationException($"Completed task '{task.Id}' has no completion date.")
            })
            .Where(t => t.completedAt >= since && t.completedAt < until)
            .Where(t => taskIds.Add(t.task.Id))
            .Select(t => new { t, completedOn = DateOnly.FromDateTime(t.completedAt.UtcDateTime) })
            .Select(t => new CompletedTask(t.t.task, t.completedOn)).ToList();

        var lastDate = today.AddDays(-1);
        var subject = $"Todoist - weekly tasks report ({sinceDate:yyyy-MM-dd} - {lastDate:yyyy-MM-dd})";

        string[] sections =
        [
            $"""
             Completed tasks ({sinceDate:yyyy-MM-dd} - {lastDate:yyyy-MM-dd}): {tasks.Count}
             (recurring tasks are excluded)
             """,
            BuildImportantTasks(tasks),
            BuildPriorities(tasks),
            BuildLabels(tasks),
            BuildDays(tasks, sinceDate),
            BuildCompletedTasks(tasks)
        ];

        return new WeeklyReportMessage(subject, NotificationFormatter.JoinSections(sections));
    }

    private static string BuildImportantTasks(IReadOnlyCollection<CompletedTask> tasks)
    {
        const string header = "Most important completed tasks (P1/P2):";

        var importantTasks = tasks
            .Where(task => task.Task.Priority >= 3)
            .OrderByDescending(task => task.Task.Priority)
            .ThenBy(task => task.CompletedOn)
            .ThenBy(task => task.Task.Content, StringComparer.OrdinalIgnoreCase)
            .ThenBy(task => task.Task.Content, StringComparer.Ordinal)
            .Select(task => $"{task.Task.Content} (P{5 - task.Task.Priority}, {task.CompletedOn:yyyy-MM-dd})")
            .ToArray();

        return importantTasks.Length == 0
            ? $"{header}{Environment.NewLine}No P1 or P2 tasks completed."
            : NotificationFormatter.BuildNumberedListMessage(header, importantTasks);
    }

    private static string BuildPriorities(IReadOnlyCollection<CompletedTask> tasks)
    {
        var counts = tasks
            .CountBy(task => task.Task.Priority)
            .ToDictionary();

        var rows = Enumerable.Range(1, 4).Select(priority =>
        {
            var count = counts.GetValueOrDefault(5 - priority);
            var percentage = tasks.Count == 0 ? 0M : count * 100M / tasks.Count;
            return $"P{priority}: {count} ({percentage:F1}%)";
        });

        return $"By priority:{Environment.NewLine}{string.Join(Environment.NewLine, rows)}";
    }

    private static string BuildLabels(IReadOnlyCollection<CompletedTask> tasks)
    {
        var rows = tasks
            .SelectMany(task => task.Task.Labels.Distinct(StringComparer.Ordinal))
            .CountBy(label => label, StringComparer.Ordinal)
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => $"{entry.Key}: {entry.Value}")
            .ToList();

        if (rows.Count < 1) rows.Add("No labeled tasks completed.");

        var withoutLabels = tasks.Count(task => !task.Task.Labels.Any());

        if (withoutLabels > 0) rows.Add($"Without labels: {withoutLabels}");

        rows.Add("(a task with multiple labels is counted under each label)");
        return $"By label:{Environment.NewLine}{string.Join(Environment.NewLine, rows)}";
    }

    private static string BuildDays(IReadOnlyCollection<CompletedTask> tasks, DateOnly sinceDate)
    {
        var counts = tasks
            .CountBy(task => task.CompletedOn)
            .ToDictionary();

        var rows = Enumerable.Range(0, 7).Select(day =>
        {
            var date = sinceDate.AddDays(day);
            var count = counts.GetValueOrDefault(date);
            return $"{date.DayOfWeek} | {date:yyyy-MM-dd}: {count}";
        });

        var nl = Environment.NewLine;
        return $"By day:{nl}{string.Join(nl, rows)}";
    }

    private static string BuildCompletedTasks(IReadOnlyCollection<CompletedTask> tasks)
    {
        const string header = "All completed tasks:";

        if (tasks.Count == 0)
            return $"{header}{Environment.NewLine}No tasks completed.";

        var titles = tasks
            .Select(task => task.Task.Content)
            .OrderBy(title => title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(title => title, StringComparer.Ordinal);

        return NotificationFormatter.BuildNumberedListMessage(header, titles);
    }

    private sealed record CompletedTask(TodoistCompletedTask Task, DateOnly CompletedOn);
}

internal sealed record WeeklyReportMessage(string Subject, string Body);
