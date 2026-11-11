using System.Globalization;

namespace Integrations.Todoist.TodoistClient;

internal static class Extensions
{
    extension(ITodoistApi api)
    {
        public Task<IEnumerable<TodoistProject>> GetProjectsAsync(CancellationToken ct = default) =>
            GetAllPagesAsync(cursor => api.GetProjectsAsync(cursor, ct));

        public Task<IEnumerable<TodoistCompletedTask>> GetCompletedTasksByCompletionDateAsync(
            DateTimeOffset since,
            DateTimeOffset until,
            CancellationToken ct = default)
        {
            var sinceUtc = since.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
            var untilUtc = until.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

            return GetAllPagesAsync(async cursor =>
            {
                var response = await api.GetCompletedTasksByCompletionDateAsync(sinceUtc, untilUtc, cursor, ct);
                return new TodoistResponse<TodoistCompletedTask>(response.Items, response.NextCursor);
            });
        }

        public Task<IEnumerable<TodoistLabel>> GetLabelsAsync(CancellationToken ct = default) =>
            GetAllPagesAsync(cursor => api.GetLabelsAsync(cursor, ct));

        public Task<IEnumerable<TodoistTask>> GetTasksAsync(IList<string> ids, CancellationToken ct = default) =>
            GetAllPagesAsync(cursor => api.GetTasksAsync(string.Join(",", ids), cursor, ct));

        public Task<IEnumerable<TodoistTask>> GetTasksAsync(CancellationToken ct = default) =>
            GetAllPagesAsync(cursor => api.GetTasksAsync(cursor, ct));

        public Task<IEnumerable<TodoistTask>> GetTasksByProjectAsync(string projectId, CancellationToken ct = default) =>
            GetAllPagesAsync(cursor => api.GetTasksByProjectAsync(projectId, cursor, ct));

        public Task<IEnumerable<TodoistTask>> GetTasksByFilterAsync(string query, CancellationToken ct = default) =>
            GetAllPagesAsync(cursor => api.GetTasksByFilterAsync(query, cursor, ct));

        public Task<IEnumerable<TodoistComment>> GetCommentsByTaskAsync(string taskId, CancellationToken ct = default) =>
            GetAllPagesAsync(cursor => api.GetCommentsByTaskAsync(taskId, cursor, ct));

        public Task<int> UpdateTasksAsync(
            IEnumerable<TodoistTask> tasks,
            Func<TodoistTask, object> createRequestBody,
            int concurrentRequests = 5,
            CancellationToken cancellationToken = default)
        {
            return ExecuteInParallelAsync(
                tasks,
                (task, ct) => api.UpdateTaskAsync(task.Id, createRequestBody(task), ct),
                concurrentRequests, cancellationToken);
        }

        public Task<int> DeleteLabelsAsync(
            IEnumerable<TodoistLabel> labels,
            int concurrentRequests = 5,
            CancellationToken cancellationToken = default)
        {
            return ExecuteInParallelAsync(
                labels,
                (label, ct) => api.DeleteLabelAsync(label.Id, ct),
                concurrentRequests,
                cancellationToken);
        }

        private static async Task<IEnumerable<T>> GetAllPagesAsync<T>(
            Func<string?, Task<TodoistResponse<T>>> getPageAsync) where T : ITodoistItem
        {
            TodoistResponse<T>? response = null;
            List<T> items = [];

            do
            {
                response = await getPageAsync(response?.NextCursor);
                items.AddRange(response.Results);
            }
            while (!string.IsNullOrWhiteSpace(response.NextCursor));

            return items;
        }
    }

    private static async Task<int> ExecuteInParallelAsync<T>(
        IEnumerable<T> items,
        Func<T, CancellationToken, Task> execute,
        int concurrentRequests,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(concurrentRequests);

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = concurrentRequests,
            CancellationToken = cancellationToken
        };

        var completedCount = 0;

        await Parallel.ForEachAsync(items, options, async (item, ct) =>
        {
            await execute(item, ct);
            Interlocked.Increment(ref completedCount);
        });

        return completedCount;
    }
}
