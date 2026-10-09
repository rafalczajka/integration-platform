using Newtonsoft.Json;

namespace Integrations.Todoist.TodoistClient;

internal interface ITodoistItem
{
    string Id { get; }
}

internal sealed record TodoistResponse<T>(
    IEnumerable<T> Results,
    string? NextCursor) where T : ITodoistItem;

internal sealed record TodoistCompletedTasksResponse(
    IEnumerable<TodoistCompletedTask> Items,
    string? NextCursor);

internal sealed record TodoistCompletedTask(
    string Id,
    string Content,
    int Priority,
    IEnumerable<string> Labels,
    DateTimeOffset? CompletedAt,
    TodoistTaskDue? Due) : ITodoistItem;

internal sealed record TodoistTask(
    string Id,
    string ProjectId,
    string ParentId,
    IEnumerable<string> Labels,
    TodoistTaskDue? Due,
    TodoistTaskDeadline? Deadline,
    string Content,
    string Description) : ITodoistItem;

internal sealed record TodoistLabel(
    string Id,
    string Name) : ITodoistItem;

internal sealed record TodoistComment(
    string Id,
    string TaskId,
    string Content) : ITodoistItem;

internal sealed record TodoistTaskDue(
    bool IsRecurring,
    string? String,
    string? Date,
    string? Timezone);

internal sealed record TodoistTaskDeadline(
    string Date,
    string? Lang);
