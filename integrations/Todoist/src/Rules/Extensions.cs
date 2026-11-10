using Integrations.Todoist.Rules.Mutations.BlockedTaskCommentRule;
using Microsoft.Extensions.DependencyInjection;

namespace Integrations.Todoist.Rules;

internal static class Extensions
{
    public static IServiceCollection AddTodoistRules(this IServiceCollection services)
    {
        services.AddScoped<BlockerLabelSynchronizer>();

        var ruleType = typeof(ITodoistRule);

        var ruleTypes = ruleType.Assembly.GetTypes()
            .Where(type =>
                type is { IsAbstract: false, IsInterface: false } &&
                ruleType.IsAssignableFrom(type));

        foreach (var rule in ruleTypes)
        {
            services.AddScoped(ruleType, rule);
        }

        return services;
    }

    public static async Task ExecuteInOrderAsync(this IEnumerable<ITodoistRule> rules,
        TodoistRuleContext context,
        CancellationToken cancellationToken)
    {
        var orderedRules = TodoistRuleOrder.Resolve(rules);

        foreach (var rule in orderedRules)
        {
            await rule.ExecuteAsync(context, cancellationToken);
        }
    }
}
