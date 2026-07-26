using Azure.Core;
using Azure.Identity;
using Intelibill.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Intelibill.Infrastructure.Data;

/// <summary>
/// The single place that turns <see cref="DatabaseOptions"/> into a data source.
/// The runtime container and the design-time/migration path both come through
/// here so they cannot drift on how they authenticate.
/// </summary>
internal static class NpgsqlDataSourceFactory
{
    private const string EntraTokenScope = "https://ossrdbms-aad.database.windows.net/.default";

    /// <summary>
    /// Entra access tokens expire after roughly an hour. Refreshing well inside
    /// that window is the whole point of a periodic provider: a one-shot provider
    /// authenticates, serves traffic for an hour, and then fails every single
    /// reconnection — passing every test and every deployment on the way there.
    /// </summary>
    private static readonly TimeSpan SuccessRefreshInterval = TimeSpan.FromMinutes(50);

    private static readonly TimeSpan FailureRefreshInterval = TimeSpan.FromSeconds(5);

    public static NpgsqlDataSource Create(DatabaseOptions options, ILoggerFactory? loggerFactory = null)
    {
        var builder = new NpgsqlDataSourceBuilder(options.ToConnectionString());

        if (loggerFactory is not null)
        {
            builder.UseLoggerFactory(loggerFactory);
        }

        if (options.UseEntraAuth)
        {
            var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                // Null falls back to AZURE_CLIENT_ID. A host carrying more than one
                // user-assigned identity has no default, so one of the two must say.
                ManagedIdentityClientId = options.ManagedIdentityClientId,
            });

            builder.UsePeriodicPasswordProvider(
                async (_, cancellationToken) =>
                {
                    var token = await credential
                        .GetTokenAsync(new TokenRequestContext([EntraTokenScope]), cancellationToken)
                        .ConfigureAwait(false);

                    return token.Token;
                },
                SuccessRefreshInterval,
                FailureRefreshInterval);
        }

        return builder.Build();
    }
}
