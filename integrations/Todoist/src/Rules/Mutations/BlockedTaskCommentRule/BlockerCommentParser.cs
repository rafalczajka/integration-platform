using System.Net;
using System.Text.RegularExpressions;

namespace Integrations.Todoist.Rules.Mutations.BlockedTaskCommentRule;

internal static class BlockerCommentParser
{
    public const string Prefix = "[blocker]";
    private const string TaskPathPrefix = "/app/task/";

    public static bool TryGetTaskId(string comment, out string taskId)
    {
        taskId = "";

        if (!comment.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var url = GetUrl(comment[Prefix.Length..]);

        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, "app.todoist.com", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            !uri.AbsolutePath.StartsWith(TaskPathPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var taskSegment = uri.AbsolutePath[TaskPathPrefix.Length..];

        if (string.IsNullOrWhiteSpace(taskSegment) || taskSegment.Contains('/'))
        {
            return false;
        }

        var id = taskSegment[(taskSegment.LastIndexOf('-') + 1)..];

        if (string.IsNullOrWhiteSpace(id) || !id.All(char.IsAsciiLetterOrDigit))
        {
            return false;
        }

        taskId = id;
        return true;
    }

    private static string? GetUrl(string value)
    {
        var url = WebUtility.HtmlDecode(value).Trim();

        if (!url.StartsWith('['))
        {
            return url;
        }

        var link = Regex.Match(url, @"^\[(?:\\.|[^\\\r\n])*?\]\((?<url>.+)\)$", RegexOptions.NonBacktracking);

        if (!link.Success)
        {
            return null;
        }

        url = link.Groups["url"].Value.Trim();
        url = Regex.Replace(url, @"\\([\\\[\]()])", "$1", RegexOptions.NonBacktracking);

        var duplicateSeparator = url.IndexOf("](", StringComparison.Ordinal);

        if (duplicateSeparator < 0)
        {
            return url.StartsWith('<') && url.EndsWith('>') ? url[1..^1] : url;
        }

        if (!url.EndsWith(')'))
        {
            return null;
        }

        var originalUrl = url[..duplicateSeparator];
        var repeatedUrl = url[(duplicateSeparator + 2)..^1];

        if (!string.Equals(originalUrl, repeatedUrl, StringComparison.Ordinal))
        {
            return null;
        }

        url = originalUrl;

        return url.StartsWith('<') && url.EndsWith('>') ? url[1..^1] : url;
    }
}
