using Refit;

namespace Integrations.Todoist.TodoistClient;

internal interface ITodoistApi
{
    [Get("/tasks/completed/by_completion_date?limit=200")]
    Task<TodoistCompletedTasksResponse> GetCompletedTasksByCompletionDateAsync(
        [Query] string since,
        [Query] string until,
        [Query] string? cursor = null,
        CancellationToken cancellationToken = default);

    [Get("/labels?limit=200")]
    Task<TodoistResponse<TodoistLabel>> GetLabelsAsync(
        [Query] string? cursor = null,
        CancellationToken cancellationToken = default);

    [Get("/tasks?ids={ids}&limit=200")]
    Task<TodoistResponse<TodoistTask>> GetTasksAsync(
        string ids,
        [Query] string? cursor = null,
        CancellationToken cancellationToken = default);

    [Get("/tasks/{taskId}")]
    Task<TodoistTask> GetTaskAsync(
        string taskId,
        CancellationToken cancellationToken = default);

    [Get("/tasks?limit=200")]
    Task<TodoistResponse<TodoistTask>> GetTasksAsync(
        [Query] string? cursor = null,
        CancellationToken cancellationToken = default);

    [Get("/tasks?project_id={projectId}&limit=200")]
    Task<TodoistResponse<TodoistTask>> GetTasksByProjectAsync(
        string projectId,
        [Query] string? cursor = null,
        CancellationToken cancellationToken = default);

    [Get("/tasks/filter?query={query}&limit=200")]
    Task<TodoistResponse<TodoistTask>> GetTasksByFilterAsync(
        string query,
        [Query] string? cursor = null,
        CancellationToken cancellationToken = default);

    [Get("/comments?task_id={taskId}&limit=200")]
    Task<TodoistResponse<TodoistComment>> GetCommentsByTaskAsync(
        string taskId,
        [Query] string? cursor = null,
        CancellationToken cancellationToken = default);

    [Post("/tasks/{taskId}")]
    Task UpdateTaskAsync(
        string taskId,
        [Body] object request,
        CancellationToken cancellationToken = default);

    [Delete("/labels/{label_id}")]
    Task DeleteLabelAsync(
        [AliasAs("label_id")] string labelId,
        CancellationToken cancellationToken = default);
}
