namespace Integrations.Todoist.Report;

internal sealed record ReportTask(
    string Id,
    string Content,
    int ApiPriority,
    IEnumerable<string> Labels,
    DateOnly CompletedOn)
{
    public int DisplayPriority => 5 - ApiPriority;
}
