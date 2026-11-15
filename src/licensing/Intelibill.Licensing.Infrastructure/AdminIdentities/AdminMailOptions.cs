namespace Intelibill.Licensing.Infrastructure.AdminIdentities;

/// <summary>
/// SMTP transport for provider-administrator messages. Unconfigured means invitations fail
/// closed. <see cref="LocalCapture"/> (no TLS, no login: Mailpit) is honored only in
/// Development and Testing.
/// </summary>
public sealed class AdminMailOptions
{
    public const string SectionName = "AdminMail";

    public string Host { get; init; } = "";
    public int Port { get; init; } = 587;
    public string FromEmail { get; init; } = "";
    public string FromName { get; init; } = "InteliBill Provider Console";
    public string Username { get; init; } = "";
    public string Password { get; init; } = "";
    public bool UseStartTls { get; init; } = true;
    public bool LocalCapture { get; init; }

    internal bool IsUsable(bool allowLocalCapture) =>
        Host.Length > 0 && Port is > 0 and <= 65535 && FromEmail.Length > 0
        && (LocalCapture ? allowLocalCapture : Username.Length > 0 && Password.Length > 0);
}
