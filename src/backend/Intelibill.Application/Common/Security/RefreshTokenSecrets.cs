using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;

namespace Intelibill.Application.Common.Security;

/// <summary>Merchant refresh-credential format, shared across issuance, lookup and revocation.</summary>
public static class RefreshTokenSecrets
{
    private static readonly OpaqueTokenPrimitives Primitives = new();

    public static OpaqueTokenV1 Create() => Primitives.Create();

    public static string NewToken() => Primitives.NewToken();

    public static string Hash(string token) => Primitives.Hash(token);
}
