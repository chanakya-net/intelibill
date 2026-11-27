namespace Intelibill.Application.Common.Interfaces;

/// <summary>
/// The phone authentication switch and the code policy behind it. Application code reads these
/// through this interface so it never references an Infrastructure options type, and so a test can
/// drive the exact expiry and attempt boundaries without a configuration file.
/// </summary>
public interface IPhoneAuthPolicy
{
    /// <summary>
    /// False unless a deployment switches it on. Nothing that sends a code or accepts one may run
    /// while this is false.
    /// </summary>
    bool Enabled { get; }

    int CodeLength { get; }

    TimeSpan CodeLifetime { get; }

    /// <summary>Wrong guesses allowed against one challenge before it closes.</summary>
    int MaxAttempts { get; }

    /// <summary>The wait before a replacement code may be requested for the same destination.</summary>
    TimeSpan ResendSpacing { get; }

    int DestinationSendsPerHour { get; }

    /// <summary>
    /// The country code a bare national number may be read against, or null to require every
    /// number in international form. An India-first deployment sets "+91"; it still never rewrites
    /// a number that could mean something else.
    /// </summary>
    string? DefaultCountryCode { get; }
}
