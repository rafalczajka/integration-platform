using Integrations.Options;

namespace Integrations.Todoist.Options;

[OptionsSection("TodoistProjectIds")]
internal sealed class TodoistProjectIdsOptions
{
    public string Recurring { get; init; } = null!;
    
    public string Someday { get; init; } = null!;
}
