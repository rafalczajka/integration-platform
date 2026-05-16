using Integrations.Todoist.Rules.BlockedTasks;
using Integrations.Todoist.Rules.Deadlines;
using Integrations.Todoist.Rules.Labels;
using Integrations.Todoist.Rules.Priorities;
using Integrations.Todoist.Rules.RecurringTasks;
using Integrations.Todoist.Rules.Subtasks;

namespace Integrations.Todoist.Rules;

internal static class TodoistRuleOrder
{
    private static readonly Type[] OrderedRuleTypes =
    [
        typeof(BlockedTaskCommentRule),
        typeof(SubtaskLabelRule),
        typeof(NonSubtaskLabelRule),
        typeof(SubtaskDueDateRule),
        typeof(RecurringTaskInactiveLabelRule),
        typeof(UnusedLabelsCleanupRule),
        typeof(BlockedTaskReportRule),
        typeof(UpcomingDeadlineTaskReportRule),
        typeof(HighestPriorityTaskReportRule),
        typeof(ImpactTaskReportRule),
        typeof(RecurringTaskInactiveReportRule)
    ];

    public static IReadOnlyList<Type> OrderedTypes => OrderedRuleTypes;

    public static IReadOnlyList<ITodoistRule> Resolve(IEnumerable<ITodoistRule> rules)
    {
        var orderedTypeIndexes = OrderedRuleTypes
            .Select((type, index) => new { type, index })
            .ToDictionary(item => item.type, item => item.index);

        return rules
            .OrderBy(rule => orderedTypeIndexes.GetValueOrDefault(rule.GetType(), int.MaxValue))
            .ThenBy(rule => rule.GetType().Name, StringComparer.Ordinal)
            .ToArray();
    }
}
