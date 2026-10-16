using System;
using System.Net;
using Integrations.Todoist.Rules.Mutations;
using Integrations.Todoist.TodoistClient;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Integrations.Todoist.Tests.Rules.Mutations;

public sealed class BlockedTaskCommentRuleTests
{
    private readonly ITodoistApi _todoist = Substitute.For<ITodoistApi>();
    private readonly ILogger<BlockedTaskCommentRule> _logger = Substitute.For<ILogger<BlockedTaskCommentRule>>();

    [Fact]
    public async Task ExecuteAsync_ShouldNotReport_WhenNoBlockedTasksExist()
    {
        _todoist.GetTasksByFilterAsync($"@{Constants.BlockedLabel}", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response<TodoistTask>()));
        _todoist.GetTasksByFilterAsync($"@{Constants.BlockerLabel}", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response<TodoistTask>()));

        var rule = CreateRule();
        var context = new TodoistRuleContext();

        await rule.ExecuteAsync(context, CancellationToken.None);

        Assert.False(context.HasMessages);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReportMissingBlockerComment_WhenBlockedTaskHasNoBlockerComment()
    {
        var task = CreateBlockedTask("blocked-1", "Blocked task");

        _todoist.GetTasksByFilterAsync($"@{Constants.BlockedLabel}", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response(task)));
        _todoist.GetTasksByFilterAsync($"@{Constants.BlockerLabel}", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response<TodoistTask>()));

        _todoist.GetCommentsByTaskAsync(task.Id, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response<TodoistComment>()));

        var rule = CreateRule();
        var context = new TodoistRuleContext();

        await rule.ExecuteAsync(context, CancellationToken.None);

        Assert.Single(context.Messages);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNotReport_WhenBlockedTaskHasSingleValidBlockerComment()
    {
        var task = CreateBlockedTask("blocked-1", "Blocked task");

        _todoist.GetTasksByFilterAsync($"@{Constants.BlockedLabel}", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response(task)));
        _todoist.GetTasksByFilterAsync($"@{Constants.BlockerLabel}", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response(CreateTask("123456789", "Blocking task", [Constants.BlockerLabel]))));

        _todoist.GetCommentsByTaskAsync(task.Id, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response(CreateComment("comment-1", task.Id, "[blocker] https://app.todoist.com/app/task/123456789"))));
        _todoist.GetTaskAsync("123456789", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreateTask("123456789", "Blocking task", [Constants.BlockerLabel])));

        var rule = CreateRule();
        var context = new TodoistRuleContext();

        await rule.ExecuteAsync(context, CancellationToken.None);

        Assert.False(context.HasMessages);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldExtractTaskIdFromSluggedTodoistUrl()
    {
        var blockedTask = CreateBlockedTask("blocked-1", "Blocked task");
        var blockerTask = CreateTask("123456789", "Blocking task");

        _todoist.GetTasksByFilterAsync($"@{Constants.BlockedLabel}", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response(blockedTask)));
        _todoist.GetTasksByFilterAsync($"@{Constants.BlockerLabel}", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response<TodoistTask>()));
        _todoist.GetCommentsByTaskAsync(blockedTask.Id, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response(
                CreateComment("comment-1", blockedTask.Id, "[blocker] https://app.todoist.com/app/task/blocking-task-123456789"))));
        _todoist.GetTaskAsync("123456789", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(blockerTask));

        var rule = CreateRule();
        var context = new TodoistRuleContext();

        await rule.ExecuteAsync(context, CancellationToken.None);

        await _todoist.Received(1).GetTaskAsync("123456789", Arg.Any<CancellationToken>());
        Assert.Single(context.Messages);
        Assert.Contains($"Updated '{Constants.BlockerLabel}' label", context.Messages.Single());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNotReport_WhenAllBlockerCommentsAreValid()
    {
        var task = CreateBlockedTask("blocked-1", "Blocked task");

        _todoist.GetTasksByFilterAsync($"@{Constants.BlockedLabel}", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response(task)));
        _todoist.GetTasksByFilterAsync($"@{Constants.BlockerLabel}", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response(
                CreateTask("123456789", "Blocking task A", [Constants.BlockerLabel]),
                CreateTask("987654321", "Blocking task B", [Constants.BlockerLabel]))));

        _todoist.GetCommentsByTaskAsync(task.Id, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response(
                CreateComment("comment-1", task.Id, "[blocker] https://app.todoist.com/app/task/123456789"),
                CreateComment("comment-2", task.Id, "[blocker] https://app.todoist.com/app/task/987654321"))));
        _todoist.GetTaskAsync("123456789", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreateTask("123456789", "Blocking task A", [Constants.BlockerLabel])));
        _todoist.GetTaskAsync("987654321", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreateTask("987654321", "Blocking task B", [Constants.BlockerLabel])));

        var rule = CreateRule();
        var context = new TodoistRuleContext();

        await rule.ExecuteAsync(context, CancellationToken.None);

        Assert.False(context.HasMessages);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReportInvalidBlockerUrl_WhenAnyBlockerCommentIsInvalid()
    {
        var task = CreateBlockedTask("blocked-1", "Blocked task");
        const string invalidComment = "[blocker] https://example.com/task/123";

        _todoist.GetTasksByFilterAsync($"@{Constants.BlockedLabel}", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response(task)));
        _todoist.GetTasksByFilterAsync($"@{Constants.BlockerLabel}", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response(CreateTask("123456789", "Blocking task", [Constants.BlockerLabel]))));

        _todoist.GetCommentsByTaskAsync(task.Id, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response(
                CreateComment("comment-1", task.Id, "[blocker] https://app.todoist.com/app/task/123456789"),
                CreateComment("comment-2", task.Id, invalidComment))));
        _todoist.GetTaskAsync("123456789", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreateTask("123456789", "Blocking task", [Constants.BlockerLabel])));

        var rule = CreateRule();
        var context = new TodoistRuleContext();

        await rule.ExecuteAsync(context, CancellationToken.None);

        Assert.Single(context.Messages);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReportInvalidBlockerUrl_WhenBlockerCommentUsesNonTodoistUrl()
    {
        var task = CreateBlockedTask("blocked-1", "Blocked task");
        const string invalidComment = "[blocker] https://google.com/task/123";

        _todoist.GetTasksByFilterAsync($"@{Constants.BlockedLabel}", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response(task)));
        _todoist.GetTasksByFilterAsync($"@{Constants.BlockerLabel}", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response<TodoistTask>()));

        _todoist.GetCommentsByTaskAsync(task.Id, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response(CreateComment("comment-1", task.Id, invalidComment))));

        var rule = CreateRule();
        var context = new TodoistRuleContext();

        await rule.ExecuteAsync(context, CancellationToken.None);

        Assert.Single(context.Messages);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldIgnoreNonBlockerComments_WhenValidBlockerCommentExists()
    {
        var task = CreateBlockedTask("blocked-1", "Blocked task");

        _todoist.GetTasksByFilterAsync($"@{Constants.BlockedLabel}", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response(task)));
        _todoist.GetTasksByFilterAsync($"@{Constants.BlockerLabel}", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response(CreateTask("123456789", "Blocking task", [Constants.BlockerLabel]))));

        _todoist.GetCommentsByTaskAsync(task.Id, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response(
                CreateComment("comment-1", task.Id, "regular note"),
                CreateComment("comment-2", task.Id, "[blocker] https://app.todoist.com/app/task/123456789"))));
        _todoist.GetTaskAsync("123456789", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreateTask("123456789", "Blocking task", [Constants.BlockerLabel])));

        var rule = CreateRule();
        var context = new TodoistRuleContext();

        await rule.ExecuteAsync(context, CancellationToken.None);

        Assert.False(context.HasMessages);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldAddBlockerLabel_WhenReferencedTaskDoesNotHaveIt()
    {
        var blockedTask = CreateBlockedTask("blocked-1", "Blocked task");
        var blockerTask = CreateTask("123456789", "Blocking task");

        _todoist.GetTasksByFilterAsync($"@{Constants.BlockedLabel}", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response(blockedTask)));
        _todoist.GetTasksByFilterAsync($"@{Constants.BlockerLabel}", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response<TodoistTask>()));
        _todoist.GetCommentsByTaskAsync(blockedTask.Id, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response(CreateComment("comment-1", blockedTask.Id, "[blocker] https://app.todoist.com/app/task/123456789"))));
        _todoist.GetTaskAsync("123456789", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(blockerTask));

        var rule = CreateRule();
        var context = new TodoistRuleContext();

        await rule.ExecuteAsync(context, CancellationToken.None);

        Assert.Single(context.Messages);
        Assert.Contains($"Updated '{Constants.BlockerLabel}' label", context.Messages.Single());
        await _todoist.Received(1).UpdateTaskAsync(
            blockerTask.Id,
            Arg.Is<object>(request => HasOnlyBlockerLabel(request)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRemoveBlockerLabel_WhenTaskIsNoLongerReferenced()
    {
        var staleBlockerTask = CreateTask("stale-1", "Stale blocker", [Constants.BlockerLabel]);

        _todoist.GetTasksByFilterAsync($"@{Constants.BlockedLabel}", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response<TodoistTask>()));
        _todoist.GetTasksByFilterAsync($"@{Constants.BlockerLabel}", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response(staleBlockerTask)));

        var rule = CreateRule();
        var context = new TodoistRuleContext();

        await rule.ExecuteAsync(context, CancellationToken.None);

        Assert.Single(context.Messages);
        Assert.Contains(staleBlockerTask.Content, context.Messages.Single());
        await _todoist.Received(1).UpdateTaskAsync(
            staleBlockerTask.Id,
            Arg.Is<object>(request => GetLabels(request).Length == 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldSkipInvalidBlockerTaskIdAndReportIt()
    {
        var blockedTask = CreateBlockedTask("blocked-1", "Blocked task");

        _todoist.GetTasksByFilterAsync($"@{Constants.BlockedLabel}", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response(blockedTask)));
        _todoist.GetTasksByFilterAsync($"@{Constants.BlockerLabel}", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response<TodoistTask>()));
        _todoist.GetCommentsByTaskAsync(blockedTask.Id, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response(
                CreateComment("comment-1", blockedTask.Id, "[blocker] https://app.todoist.com/app/task/blocking-task-123456789"))));
        _todoist.GetTaskAsync("123456789", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<TodoistTask>(new FakeApiException(HttpStatusCode.BadRequest)));

        var rule = CreateRule();
        var context = new TodoistRuleContext();

        await rule.ExecuteAsync(context, CancellationToken.None);

        Assert.Single(context.Messages);
        var message = context.Messages.Single();
        Assert.Contains("Ignored 1 blocker references with invalid task IDs:", message);
        Assert.Contains("Blocked task - [blocker] https://app.todoist.com/app/task/blocking-task-123456789", message);
        await _todoist.DidNotReceive().UpdateTaskAsync(
            Arg.Any<string>(),
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
    }

    private BlockedTaskCommentRule CreateRule() => new(_todoist, _logger);

    private static TodoistResponse<T> Response<T>(params T[] items) where T : ITodoistItem =>
        new(items, string.Empty);

    private static TodoistTask CreateTask(string id, string content, params string[] labels) =>
        new(id, "project-1", string.Empty, labels, null, null, content, string.Empty);

    private static TodoistTask CreateBlockedTask(string id, string content) =>
        CreateTask(id, content, Constants.BlockedLabel);

    private static TodoistComment CreateComment(string id, string taskId, string content) =>
        new(id, taskId, content);

    private static string[] GetLabels(object request) =>
        (string[])request.GetType().GetProperty("labels")!.GetValue(request)!;

    private static bool HasOnlyBlockerLabel(object request)
    {
        var labels = GetLabels(request);
        return labels.Length == 1 && labels[0] == Constants.BlockerLabel;
    }

    private sealed class FakeApiException(HttpStatusCode statusCode) : Exception
    {
        public HttpStatusCode StatusCode { get; } = statusCode;
    }
}
