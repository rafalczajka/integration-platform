using System;
using System.Net;
using System.Net.Http;
using Integrations.Todoist.Rules.Mutations.BlockedTaskCommentRule;
using Integrations.Todoist.TodoistClient;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Refit;

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

    [Theory]
    [InlineData("[blocker] [Blocking task](https://app.todoist.com/app/task/blocking-task-6fx4wVVQX5p25RxG)")]
    [InlineData("[blocker] [https://example.com/task/other](https://app.todoist.com/app/task/6fx4wVVQX5p25RxG)")]
    [InlineData("[blocker] [https://app.todoist.com/app/task/blocking-task-6fx4wVVQX5p25RxG](https://app.todoist.com/app/task/blocking-task-6fx4wVVQX5p25RxG)")]
    [InlineData("[blocker] [Blocking task](<https://app.todoist.com/app/task/6fx4wVVQX5p25RxG>)")]
    [InlineData("[blocker] [Outer [label]](https://app.todoist.com/app/task/6fx4wVVQX5p25RxG)")]
    [InlineData("[blocker] [[https://app.todoist.com/app/task/poszukac-mieszkanka-6fx4wVVQX5p25RxG\\](https://app.todoist.com/app/task/poszukac-mieszkanka-6fx4wVVQX5p25RxG)](https://app.todoist.com/app/task/poszukac-mieszkanka-6fx4wVVQX5p25RxG]\\(https://app.todoist.com/app/task/poszukac-mieszkanka-6fx4wVVQX5p25RxG\\)) &#x20;")]
    [InlineData("[blocker] https://app.todoist.com/app/task/6fx4wVVQX5p25RxG &#x20;")]
    public async Task ExecuteAsync_ShouldResolveMarkdownBlockerAndAddLabel(string comment)
    {
        var blockedTask = CreateBlockedTask("blocked-1", "Blocked task");
        var blockerTask = CreateTask("6fx4wVVQX5p25RxG", "Blocking task", "existing-label");

        _todoist.GetTasksByFilterAsync($"@{Constants.BlockedLabel}", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response(blockedTask)));
        _todoist.GetTasksByFilterAsync($"@{Constants.BlockerLabel}", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response<TodoistTask>()));
        _todoist.GetCommentsByTaskAsync(blockedTask.Id, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response(CreateComment("comment-1", blockedTask.Id, comment))));
        _todoist.GetTaskAsync(blockerTask.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(blockerTask));

        var context = new TodoistRuleContext();
        await CreateRule().ExecuteAsync(context, CancellationToken.None);

        await _todoist.Received(1).GetTaskAsync(blockerTask.Id, Arg.Any<CancellationToken>());
        await _todoist.Received(1).UpdateTaskAsync(
            blockerTask.Id,
            Arg.Is<object>(request => GetLabels(request).SequenceEqual(new[] { "existing-label", Constants.BlockerLabel })),
            Arg.Any<CancellationToken>());
        Assert.Single(context.Messages);
        Assert.DoesNotContain("invalid blocker", context.Messages.Single());
    }

    [Theory]
    [InlineData("[blocker] [https://app.todoist.com/app/task/123456789](https://example.com/task/123456789)")]
    [InlineData("[blocker] [Blocking task](http://app.todoist.com/app/task/123456789)")]
    [InlineData("[blocker] [Blocking task](https://app.todoist.com.evil.example/app/task/123456789)")]
    [InlineData("[blocker] [Blocking task](https://app.todoist.com/app/task/123456789?query=1)")]
    [InlineData("[blocker] [Blocking task](https://app.todoist.com/app/task/123456789#fragment)")]
    [InlineData("[blocker] [Blocking task](https://app.todoist.com/app/task/123456789/other)")]
    [InlineData("[blocker] [Blocking task](https://app.todoist.com/app/task/123456789]")]
    [InlineData("[blocker] [Blocking task](https://app.todoist.com/app/task/123456789]\\(https://app.todoist.com/app/task/987654321\\))")]
    [InlineData("[blocker] [Blocking task](https://app.todoist.com/app/task/123456789))")]
    [InlineData("[blocker] [Blocking task](https://app.todoist.com/app/task/123456789) additional text")]
    [InlineData("[blocker] [First](https://example.com/task/other) [Second](https://app.todoist.com/app/task/123456789)")]
    public async Task ExecuteAsync_ShouldReportInvalidMarkdownBlockerWithoutFetchingOrUpdatingTasks(string comment)
    {
        var blockedTask = CreateBlockedTask("blocked-1", "Blocked task");

        _todoist.GetTasksByFilterAsync($"@{Constants.BlockedLabel}", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response(blockedTask)));
        _todoist.GetTasksByFilterAsync($"@{Constants.BlockerLabel}", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response<TodoistTask>()));
        _todoist.GetCommentsByTaskAsync(blockedTask.Id, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response(CreateComment("comment-1", blockedTask.Id, comment))));

        var context = new TodoistRuleContext();
        await CreateRule().ExecuteAsync(context, CancellationToken.None);

        Assert.Single(context.Messages);
        Assert.Contains($"invalid blocker URL: {comment}", context.Messages.Single());
        await _todoist.DidNotReceive().GetTaskAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _todoist.DidNotReceive().UpdateTaskAsync(
            Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("[blocker] [Blocking task](https://app.todoist.com/app/task/123456789)")]
    [InlineData("[blocker] [https://app.todoist.com/app/task/123456789](https://app.todoist.com/app/task/123456789)")]
    public async Task ExecuteAsync_ShouldKeepBlockerLabel_WhenMarkdownReferenceIsStillPresent(string comment)
    {
        var blockedTask = CreateBlockedTask("blocked-1", "Blocked task");
        var blockerTask = CreateTask("123456789", "Blocking task", Constants.BlockerLabel);

        _todoist.GetTasksByFilterAsync($"@{Constants.BlockedLabel}", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response(blockedTask)));
        _todoist.GetTasksByFilterAsync($"@{Constants.BlockerLabel}", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response(blockerTask)));
        _todoist.GetCommentsByTaskAsync(blockedTask.Id, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response(CreateComment("comment-1", blockedTask.Id, comment))));
        _todoist.GetTaskAsync(blockerTask.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(blockerTask));

        var context = new TodoistRuleContext();
        await CreateRule().ExecuteAsync(context, CancellationToken.None);

        Assert.False(context.HasMessages);
        await _todoist.DidNotReceive().UpdateTaskAsync(
            Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>());
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

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, true)]
    [InlineData(HttpStatusCode.NotFound, true)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.NotFound, false)]
    public async Task ExecuteAsync_ShouldSkipInvalidBlockerTaskIdAndReportIt(HttpStatusCode statusCode, bool useRefit)
    {
        var blockedTask = CreateBlockedTask("blocked-1", "Blocked task");

        _todoist.GetTasksByFilterAsync($"@{Constants.BlockedLabel}", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response(blockedTask)));
        _todoist.GetTasksByFilterAsync($"@{Constants.BlockerLabel}", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response<TodoistTask>()));
        _todoist.GetCommentsByTaskAsync(blockedTask.Id, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response(
                CreateComment("comment-1", blockedTask.Id, "[blocker] https://app.todoist.com/app/task/blocking-task-123456789"))));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://app.todoist.com/api/v1/tasks/123456789");
        using var response = new HttpResponseMessage(statusCode);
        Exception exception = useRefit
            ? await ApiException.Create(request, HttpMethod.Get, response, new RefitSettings())
            : new HttpRequestException("Invalid blocker task.", null, statusCode);
        _todoist.GetTaskAsync("123456789", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<TodoistTask>(exception));

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

    [Fact]
    public async Task ExecuteAsync_ShouldFetchAndUpdateSharedBlockerOnlyOnce()
    {
        var firstTask = CreateBlockedTask("blocked-1", "First blocked task");
        var secondTask = CreateBlockedTask("blocked-2", "Second blocked task");
        var blocker = CreateTask("123456789", "Shared blocker");
        const string comment = "[blocker] https://app.todoist.com/app/task/123456789";

        _todoist.GetTasksByFilterAsync($"@{Constants.BlockedLabel}", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response(firstTask, secondTask)));
        _todoist.GetTasksByFilterAsync($"@{Constants.BlockerLabel}", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response<TodoistTask>()));
        foreach (var task in new[] { firstTask, secondTask })
        {
            _todoist.GetCommentsByTaskAsync(task.Id, Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(Response(CreateComment($"comment-{task.Id}", task.Id, comment))));
        }
        _todoist.GetTaskAsync(blocker.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(blocker));

        var context = new TodoistRuleContext();
        await CreateRule().ExecuteAsync(context, CancellationToken.None);

        await _todoist.Received(1).GetTaskAsync(blocker.Id, Arg.Any<CancellationToken>());
        await _todoist.Received(1).UpdateTaskAsync(
            blocker.Id, Arg.Is<object>(request => HasOnlyBlockerLabel(request)), Arg.Any<CancellationToken>());
        Assert.Single(context.Messages);
        Assert.Contains("for 1 tasks:", context.Messages.Single());
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task ExecuteAsync_ShouldPropagateUnexpectedApiErrorsWithoutUpdatingLabels(HttpStatusCode statusCode)
    {
        var blockedTask = CreateBlockedTask("blocked-1", "Blocked task");
        var staleBlocker = CreateTask("stale-1", "Stale blocker", Constants.BlockerLabel);
        var exception = new HttpRequestException("Unexpected API error.", null, statusCode);

        _todoist.GetTasksByFilterAsync($"@{Constants.BlockedLabel}", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response(blockedTask)));
        _todoist.GetTasksByFilterAsync($"@{Constants.BlockerLabel}", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response(staleBlocker)));
        _todoist.GetCommentsByTaskAsync(blockedTask.Id, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response(CreateComment("comment-1", blockedTask.Id,
                "[blocker] https://app.todoist.com/app/task/123456789"))));
        _todoist.GetTaskAsync("123456789", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<TodoistTask>(exception));

        var context = new TodoistRuleContext();
        var actual = await Assert.ThrowsAsync<HttpRequestException>(() =>
            CreateRule().ExecuteAsync(context, CancellationToken.None));

        Assert.Same(exception, actual);
        Assert.False(context.HasMessages);
        await _todoist.DidNotReceive().UpdateTaskAsync(
            Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectUnexpectedBlockerTaskIdBeforeUpdatingLabels()
    {
        var blockedTask = CreateBlockedTask("blocked-1", "Blocked task");
        var wrongTask = CreateTask("987654321", "Unexpected task");

        _todoist.GetTasksByFilterAsync($"@{Constants.BlockedLabel}", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response(blockedTask)));
        _todoist.GetTasksByFilterAsync($"@{Constants.BlockerLabel}", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response<TodoistTask>()));
        _todoist.GetCommentsByTaskAsync(blockedTask.Id, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response(CreateComment("comment-1", blockedTask.Id,
                "[blocker] https://app.todoist.com/app/task/123456789"))));
        _todoist.GetTaskAsync("123456789", Arg.Any<CancellationToken>()).Returns(Task.FromResult(wrongTask));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateRule().ExecuteAsync(new TodoistRuleContext(), CancellationToken.None));

        await _todoist.DidNotReceive().UpdateTaskAsync(
            Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    private BlockedTaskCommentRule CreateRule() => new(
        _todoist,
        new BlockerLabelSynchronizer(_todoist, Substitute.For<ILogger<BlockerLabelSynchronizer>>()),
        _logger);

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
}
