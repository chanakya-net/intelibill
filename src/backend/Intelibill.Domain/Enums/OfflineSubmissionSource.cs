namespace Intelibill.Domain.Enums;

/// <summary>How an offline invoice reached the server. Stored as its number.</summary>
public enum OfflineSubmissionSource
{
    /// <summary>The cashier's own device synced it.</summary>
    Sync = 1,

    /// <summary>An owner or manager imported it from a support recovery bundle.</summary>
    Recovery = 2,
}
