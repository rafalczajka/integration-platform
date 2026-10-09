namespace Integrations.Todoist.Rules;

internal static class NotificationFormatter
{
    private const int SeparatorLength = 60;

    private static readonly string SectionSeparator =
        $"{Environment.NewLine}{Environment.NewLine}{new string('-', SeparatorLength)}{Environment.NewLine}{Environment.NewLine}";

    public static string JoinSections(IEnumerable<string> sections) => string.Join(SectionSeparator, sections);

    public static string BuildNumberedListMessage(string header, IEnumerable<string> items)
    {
        var numberedItems = items.Select((item, index) => $"{index + 1}) {item}");
        return $"{header}{Environment.NewLine}{string.Join(Environment.NewLine, numberedItems)}";
    }
}
