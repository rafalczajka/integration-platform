using Integrations.Todoist.Options;
using Integrations.Todoist.Rules.Labels;
using Integrations.Todoist.Rules.RecurringTasks;
using Integrations.Todoist.TodoistClient;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Integrations.Todoist.Tests.Rules;

public sealed class TodoistRuleOrderTests
{
    [Fact]
    public void OrderedTypes_ShouldContainEveryRegisteredRuleExactlyOnce()
    {
        var rules = ResolveRules();
        var registeredRuleTypes = rules.Select(rule => rule.GetType()).Distinct().ToArray();

        Assert.NotEmpty(registeredRuleTypes);
        Assert.Equal(registeredRuleTypes.Length, TodoistRuleOrder.OrderedTypes.Count);
        Assert.All(registeredRuleTypes, type => Assert.Contains(type, TodoistRuleOrder.OrderedTypes));
    }

    [Fact]
    public void OrderedTypes_ShouldNotContainDuplicates()
    {
        Assert.Equal(
            TodoistRuleOrder.OrderedTypes.Count,
            TodoistRuleOrder.OrderedTypes.Distinct().Count());
    }

    [Fact]
    public void SomedayProjectLabelRule_ShouldRunAfterInactiveLabelsAndBeforeCleanup()
    {
        var orderedTypes = TodoistRuleOrder.OrderedTypes.ToList();

        Assert.True(orderedTypes.IndexOf(typeof(RecurringTaskInactiveLabelRule))
            < orderedTypes.IndexOf(typeof(SomedayProjectLabelRule)));
        Assert.True(orderedTypes.IndexOf(typeof(SomedayProjectLabelRule))
            < orderedTypes.IndexOf(typeof(UnusedLabelsCleanupRule)));
    }

    private static ITodoistRule[] ResolveRules(ITodoistApi? todoist = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(
            Microsoft.Extensions.Options.Options.Create(new TodoistProjectIdsOptions
            {
                Recurring = "test-project-id",
                Someday = "test-someday-project-id"
            }));
        services.AddSingleton(todoist ?? Substitute.For<ITodoistApi>());
        services.AddTodoistRules();

        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();
        return [.. scope.ServiceProvider.GetServices<ITodoistRule>()];
    }

}
