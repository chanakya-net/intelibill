using Azure.Identity;

namespace Intelibill.Infrastructure;

/// <summary>
/// One place that builds the credential used to reach Azure services, so
/// PostgreSQL and Key Vault cannot drift on which identity they authenticate as.
/// </summary>
internal static class AzureCredentials
{
    /// <param name="managedIdentityClientId">
    /// Null falls back to <c>AZURE_CLIENT_ID</c>. A host carrying more than one
    /// user-assigned identity has no default, so one of the two must say which.
    /// </param>
    public static DefaultAzureCredential Create(string? managedIdentityClientId) =>
        new(new DefaultAzureCredentialOptions
        {
            ManagedIdentityClientId = string.IsNullOrWhiteSpace(managedIdentityClientId)
                ? null
                : managedIdentityClientId,
        });
}
