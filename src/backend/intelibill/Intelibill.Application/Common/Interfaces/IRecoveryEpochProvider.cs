namespace Intelibill.Application.Common.Interfaces;

/// <summary>
/// The recovery epoch the deployment is serving. Sessions are stamped with it, so a database
/// restored from before an epoch change cannot hand back the sessions it held: they name the old
/// epoch and are refused.
/// </summary>
/// <remarks>
/// The configured implementation is a placeholder for an epoch held outside the database, which is
/// what makes the fence survive a restore. Until that exists, the fence is only as durable as the
/// deployment's configuration.
/// </remarks>
public interface IRecoveryEpochProvider
{
    ValueTask<string> GetCurrentAsync(CancellationToken ct = default);
}
