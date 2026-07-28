using Azure.Identity;

namespace Intelibill.Infrastructure;

/// <summary>
/// One place that builds the credential used to reach Azure services, so
/// PostgreSQL and Key Vault cannot drift on which identity they authenticate as.
/// </summary>
internal static class AzureCredentials
{
    /// <param name="managedIdentityClientId">
    /// Null or blank falls back to <c>AZURE_CLIENT_ID</c>. A host carrying more
    /// than one user-assigned identity has no default, so one of the two must
    /// say which.
    /// </param>
    /// <remarks>
    /// The property must be left untouched to get that fallback:
    /// <see cref="DefaultAzureCredentialOptions.ManagedIdentityClientId"/> is
    /// seeded from <c>AZURE_CLIENT_ID</c> by its own initializer, so assigning
    /// null erases it and the credential then asks for the system-assigned
    /// identity instead. A Container App carrying only user-assigned identities
    /// answers that request with HTTP 400 "Unable to load the proper Managed
    /// Identity".
    /// </remarks>
    public static DefaultAzureCredential Create(string? managedIdentityClientId)
    {
        var options = new DefaultAzureCredentialOptions();

        if (!string.IsNullOrWhiteSpace(managedIdentityClientId))
        {
            options.ManagedIdentityClientId = managedIdentityClientId;
        }

        return new DefaultAzureCredential(options);
    }
}
