using Intelibill.Application.Common.Interfaces;
using Intelibill.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Intelibill.Infrastructure.Services.Auth;

/// <summary>
/// Reads the epoch from deployment configuration. A restore cannot roll this value back with the
/// database, but a misconfigured replica can serve the wrong one, so every serving replica must
/// agree on it before a restored database takes traffic.
/// </summary>
internal sealed class ConfiguredRecoveryEpochProvider(IOptions<AuthSessionOptions> options)
    : IRecoveryEpochProvider
{
    public ValueTask<string> GetCurrentAsync(CancellationToken ct = default) =>
        ValueTask.FromResult(options.Value.RecoveryEpoch);
}
