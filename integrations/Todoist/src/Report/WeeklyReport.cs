using Integrations.Todoist.TodoistClient;
using Integrations.Utils;

namespace Integrations.Todoist.Report;

internal sealed class WeeklyReport(ITodoistApi todoist)
{
    public async Task<WeeklyReportMessage> CreateAsync(CancellationToken cancellationToken)
    {
        var untilDateExclusive = DateOnly.FromDateTime(DateTime.UtcNow);
        var sinceDate = untilDateExclusive.AddDays(-7);

        var since = new DateTimeOffset(sinceDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var untilExclusive = new DateTimeOffset(untilDateExclusive.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var response = await todoist.GetCompletedTasksByCompletionDateAsync(since, untilExclusive, cancellationToken);
        var tasks = PrepareTasks(response, since, untilExclusive);

        return WeeklyReportBuilder.Build(tasks, sinceDate, untilDateExclusive);
    }

    private static ReportTask[] PrepareTasks(
        IEnumerable<TodoistCompletedTask> completedTasks,
        DateTimeOffset since,
        DateTimeOffset untilExclusive)
    {
        var tasks = new List<ReportTask>();

        foreach (var task in completedTasks)
        {
            var isRecurringTask = task.Due?.IsRecurring == true;

            if (isRecurringTask || !HasNonPersonalLabel(task.Labels)) continue;

            var completedAt = task.CompletedAt
                ?? throw new InvalidOperationException($"Completed task '{task.Id}' has no completion date.");

            if (completedAt < since || completedAt >= untilExclusive) continue;

            var reportTask = new ReportTask(
                task.Id,
                task.Content,
                task.Priority,
                task.Labels,
                DateOnly.FromDateTime(completedAt.UtcDateTime));

            tasks.Add(reportTask);
        }

        return [.. tasks.DistinctBy(task => task.Id, StringComparer.Ordinal)];
    }

    private static bool HasNonPersonalLabel(IEnumerable<string> labels)
    {
        return labels.Any(label => !string.Equals(
            label,
            Labels.Personal,
            StringComparison.OrdinalIgnoreCase));
    }
}

internal sealed record WeeklyReportMessage(string Subject, string Body);
