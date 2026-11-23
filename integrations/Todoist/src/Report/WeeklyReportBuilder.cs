using Integrations.Notifications;

namespace Integrations.Todoist.Report;

internal static class WeeklyReportBuilder
{
    public static WeeklyReportMessage Build(
        ReportTask[] tasks,
        DateOnly sinceDate,
        DateOnly untilDateExclusive)
    {
        var lastDate = untilDateExclusive.AddDays(-1);
        var subject = $"Todoist - weekly tasks report ({sinceDate:yyyy-MM-dd} - {lastDate:yyyy-MM-dd})";

        string[] sections =
        [
            $"""
             Completed tasks ({sinceDate:yyyy-MM-dd} - {lastDate:yyyy-MM-dd}): {tasks.Length}
             (recurring tasks and quick-add tasks are excluded)
             """,
            BuildImportantTasks(tasks),
            BuildPriorities(tasks),
            BuildLabels(tasks),
            BuildDays(tasks, sinceDate),
            BuildCompletedTasks(tasks)
        ];

        return new WeeklyReportMessage(subject, NotificationFormatter.JoinSections(sections));
    }

    private static string BuildImportantTasks(ReportTask[] tasks)
    {
        const string header = "Most important completed tasks (P1/P2):";

        var importantTasks = tasks
            .Where(task => task.ApiPriority >= 3)
            .OrderByDescending(task => task.ApiPriority)
            .ThenBy(task => task.CompletedOn)
            .ThenBy(task => task.Content, StringComparer.OrdinalIgnoreCase)
            .ThenBy(task => task.Content, StringComparer.Ordinal)
            .Select(task => $"{task.Content} (P{task.DisplayPriority}, {task.CompletedOn:yyyy-MM-dd})")
            .ToArray();

        return importantTasks.Length == 0
            ? $"{header}{Environment.NewLine}No P1 or P2 tasks completed."
            : NotificationFormatter.BuildNumberedListMessage(header, importantTasks);
    }

    private static string BuildPriorities(ReportTask[] tasks)
    {
        var counts = tasks
            .CountBy(task => task.DisplayPriority)
            .ToDictionary(entry => entry.Key, entry => entry.Value);

        var rows = Enumerable.Range(1, 4).Select(priority =>
        {
            var count = counts.GetValueOrDefault(priority);
            var percentage = tasks.Length == 0 ? 0M : count * 100M / tasks.Length;
            return $"P{priority}: {count} ({percentage:F1}%)";
        });

        var nl = Environment.NewLine;
        return $"By priority:{nl}{string.Join(nl, rows)}";
    }

    private static string BuildLabels(ReportTask[] tasks)
    {
        var rows = tasks
            .SelectMany(task => task.Labels.Distinct(StringComparer.Ordinal))
            .CountBy(label => label, StringComparer.Ordinal)
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => $"{entry.Key}: {entry.Value}")
            .ToList();

        if (rows.Count < 1) rows.Add("No labeled tasks completed.");

        var withoutLabels = tasks.Count(task => !task.Labels.Any());

        if (withoutLabels > 0) rows.Add($"Without labels: {withoutLabels}");

        rows.Add("(a task with multiple labels is counted under each label)");
        var nl = Environment.NewLine;
        return $"By label:{nl}{string.Join(nl, rows)}";
    }

    private static string BuildDays(ReportTask[] tasks, DateOnly sinceDate)
    {
        var counts = tasks
            .CountBy(task => task.CompletedOn)
            .ToDictionary(entry => entry.Key, entry => entry.Value);

        var rows = Enumerable.Range(0, 7).Select(day =>
        {
            var date = sinceDate.AddDays(day);
            var count = counts.GetValueOrDefault(date);
            return $"{date.DayOfWeek} | {date:yyyy-MM-dd}: {count}";
        });

        var nl = Environment.NewLine;
        return $"By day:{nl}{string.Join(nl, rows)}";
    }

    private static string BuildCompletedTasks(ReportTask[] tasks)
    {
        const string header = "All completed tasks:";

        if (tasks.Length == 0)
            return $"{header}{Environment.NewLine}No tasks completed.";

        var titles = tasks
            .Select(task => task.Content)
            .OrderBy(title => title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(title => title, StringComparer.Ordinal);

        return NotificationFormatter.BuildNumberedListMessage(header, titles);
    }
}
