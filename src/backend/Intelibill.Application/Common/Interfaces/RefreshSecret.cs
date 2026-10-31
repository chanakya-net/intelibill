namespace Intelibill.Application.Common.Interfaces;

/// <summary>
/// A freshly minted refresh secret: <see cref="Token"/> goes to the client once and is never
/// stored, <see cref="TokenHash"/> is what the database keeps.
/// </summary>
public sealed record RefreshSecret(string Token, string TokenHash, DateTimeOffset ExpiresAt)
{
    /// <summary>Leaves the secret out, so logging this value cannot leak a usable credential.</summary>
    public override string ToString() => $"RefreshSecret {{ TokenHash = {TokenHash}, ExpiresAt = {ExpiresAt:O} }}";
}
