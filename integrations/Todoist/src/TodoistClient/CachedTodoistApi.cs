using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Integrations.Todoist.TodoistClient;

internal sealed class CachedTodoistApi(
    ITodoistRawApi api,
    IMemoryCache cache,
    ILogger<CachedTodoistApi> logger) : ITodoistApi
{
    private static readonly MemoryCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(3)
    };

    private readonly ConcurrentDictionary<string, byte> _taskKeys = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _commentKeys = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _labelKeys = new(StringComparer.Ordinal);

    public Task<TodoistCompletedTasksResponse> GetCompletedTasksByCompletionDateAsync(
        string since,
        string until,
        string? cursor = null,
        CancellationToken cancellationToken = default) =>
        api.GetCompletedTasksByCompletionDateAsync(since, until, cursor, cancellationToken);

    public Task<TodoistResponse<TodoistLabel>> GetLabelsAsync(
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var key = $"labels:all:cursor:{GetCacheCursorKey(cursor)}";
        return GetOrCreateAsync(key, _labelKeys, () => api.GetLabelsAsync(cursor, cancellationToken));
    }

    public Task<TodoistResponse<TodoistTask>> GetTasksAsync(
        string ids,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var key = $"tasks:ids:{ids}:cursor:{GetCacheCursorKey(cursor)}";
        return GetOrCreateAsync(key, _taskKeys, () => api.GetTasksAsync(ids, cursor, cancellationToken));
    }

    public Task<TodoistTask> GetTaskAsync(
        string taskId,
        CancellationToken cancellationToken = default)
    {
        var key = $"tasks:single:{taskId}";
        return GetOrCreateAsync(key, _taskKeys, () => api.GetTaskAsync(taskId, cancellationToken));
    }

    public Task<TodoistResponse<TodoistTask>> GetTasksAsync(
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var key = $"tasks:all:cursor:{GetCacheCursorKey(cursor)}";
        return GetOrCreateAsync(key, _taskKeys, () => api.GetTasksAsync(cursor, cancellationToken));
    }

    public Task<TodoistResponse<TodoistTask>> GetTasksByProjectAsync(
        string projectId,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var key = $"tasks:project:{projectId}:cursor:{GetCacheCursorKey(cursor)}";
        return GetOrCreateAsync(key, _taskKeys, () => api.GetTasksByProjectAsync(projectId, cursor, cancellationToken));
    }

    public Task<TodoistResponse<TodoistTask>> GetTasksByFilterAsync(
        string query,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var key = $"tasks:filter:{query}:cursor:{GetCacheCursorKey(cursor)}";
        return GetOrCreateAsync(key, _taskKeys, () => api.GetTasksByFilterAsync(query, cursor, cancellationToken));
    }

    public Task<TodoistResponse<TodoistComment>> GetCommentsByTaskAsync(
        string taskId,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var key = $"comments:task:{taskId}:cursor:{GetCacheCursorKey(cursor)}";
        return GetOrCreateAsync(key, _commentKeys, () => api.GetCommentsByTaskAsync(taskId, cursor, cancellationToken));
    }

    public async Task UpdateTaskAsync(
        string taskId,
        object request,
        CancellationToken cancellationToken = default)
    {
        await api.UpdateTaskAsync(taskId, request, cancellationToken);
        InvalidateTaskEntries();
    }

    public async Task DeleteLabelAsync(
        string labelId,
        CancellationToken cancellationToken = default)
    {
        await api.DeleteLabelAsync(labelId, cancellationToken);
        InvalidateLabelEntries();
        InvalidateTaskEntries();
    }

    private async Task<T> GetOrCreateAsync<T>(
        string key,
        ConcurrentDictionary<string, byte> keyRegistry,
        Func<Task<T>> factory)
    {
        if (cache.TryGetValue<T>(key, out var cached))
        {
            logger.LogDebug("Using cache for key {key}", key);
            return cached!;
        }

        var value = await factory();

        cache.Set(key, value, CacheOptions);
        keyRegistry.TryAdd(key, default);

        return value;
    }

    private void InvalidateTaskEntries() => InvalidateEntries(_taskKeys);

    private void InvalidateLabelEntries() => InvalidateEntries(_labelKeys);

    private static string GetCacheCursorKey(string? cursor) =>
        string.IsNullOrWhiteSpace(cursor) ? "<first-page>" : cursor;

    private void InvalidateEntries(ConcurrentDictionary<string, byte> keyRegistry)
    {
        foreach (var key in keyRegistry.Keys)
        {
            logger.LogDebug("Removing cached key {key}", key);
            cache.Remove(key);
            keyRegistry.TryRemove(key, out _);
        }
    }
}
