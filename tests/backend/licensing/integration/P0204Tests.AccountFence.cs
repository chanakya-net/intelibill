using System.Data.Common;
using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0204Tests
{
    private static async Task QueueAtAccountFenceAsync(NpgsqlConnection connection, NpgsqlTransaction fence,
        AccountFenceArrival first, AccountFenceArrival second)
    {
        try
        {
            // Let both replicas finish preparing the SQL before either starts its database lock timeout.
            var backendPids = await Task.WhenAll(first.Arrived, second.Arrived).WaitAsync(TimeSpan.FromSeconds(20));
            first.Resume();
            await P0203bTests.WaitForLockAsync(connection, fence, "advisory", 1, backendPids[0]);
            second.Resume();
            await P0203bTests.WaitForLockAsync(connection, fence, "advisory", 2, backendPids);
        }
        finally
        {
            first.Resume();
            second.Resume();
        }
    }

    private sealed class AccountFenceArrival : DbCommandInterceptor
    {
        private readonly TaskCompletionSource<int> _arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private string? _accountKey;

        internal Task<int> Arrived => _arrived.Task;

        internal void Watch(Guid account) => Volatile.Write(ref _accountKey, $"b{account:D}");

        internal void Resume() => _resume.TrySetResult();

        internal void Configure(IServiceCollection services) =>
            services.ConfigureDbContext<LicensingDbContext>(options => options.AddInterceptors(this));

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _accountKey) is { } key
                && command.CommandText.StartsWith("SELECT pg_advisory_xact_lock(hashtextextended(", StringComparison.Ordinal)
                && command.Parameters.Count == 1
                && command.Parameters[0].Value is string parameter
                && parameter == key)
            {
                _arrived.TrySetResult(((NpgsqlConnection)command.Connection!).ProcessID);
                await _resume.Task.WaitAsync(cancellationToken);
            }

            return result;
        }
    }
}
