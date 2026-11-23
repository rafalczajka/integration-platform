using Integrations.Options;

namespace Integrations.Gmail.Options;

[OptionsSection("Smtp")]
internal sealed class SmtpOptions
{
    public string Host { get; init; } = null!;

    public int Port { get; init; } = 587;

    public bool UseSsl { get; init; }

    public string From { get; init; } = null!;

    public string AllowedRecipients { get; init; } = null!;

    public string? FromName { get; init; }

    public string? User { get; init; }

    public string? Password { get; init; }
}
