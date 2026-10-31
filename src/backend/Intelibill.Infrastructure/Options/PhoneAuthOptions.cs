namespace Intelibill.Infrastructure.Options;

/// <summary>
/// Phone authentication configuration. <see cref="Enabled"/> is false unless a deployment sets it,
/// so a fresh environment cannot send a verification SMS or accept a code by accident.
/// </summary>
public sealed class PhoneAuthOptions
{
    public const string SectionName = "PhoneAuth";

    public bool Enabled { get; init; }

    /// <summary>
    /// Keeps codes in the outbox instead of handing them to a provider. Development and Testing
    /// only; the validator refuses it anywhere else.
    /// </summary>
    public bool LocalCapture { get; init; }

    /// <summary>"Twilio" is the only transport implemented. Ignored while <see cref="LocalCapture"/> is set.</summary>
    public string Provider { get; init; } = "Twilio";

    public string AccountSid { get; init; } = string.Empty;

    public string AuthToken { get; init; } = string.Empty;

    /// <summary>The sending number or alphanumeric sender id. Unused when <see cref="MessagingServiceSid"/> is set.</summary>
    public string FromNumber { get; init; } = string.Empty;

    public string MessagingServiceSid { get; init; } = string.Empty;

    /// <summary>
    /// TRAI DLT principal entity id. Empty means the international gateway, which needs no DLT
    /// registration but replaces the sender id with a random short code. Populated means the
    /// domestic gateway, which requires registered entity, header and template ids.
    /// </summary>
    public string DltEntityId { get; init; } = string.Empty;

    /// <summary>The DLT-approved template id matching the body this application sends.</summary>
    public string DltTemplateId { get; init; } = string.Empty;

    public int CodeLength { get; init; } = 6;

    public int CodeLifetimeMinutes { get; init; } = 5;

    public int MaxAttempts { get; init; } = 5;

    public int ResendSpacingSeconds { get; init; } = 60;

    public int DestinationSendsPerHour { get; init; } = 5;

    /// <summary>India-first default. Set to an empty string to require international format always.</summary>
    public string DefaultCountryCode { get; init; } = "+91";
}
