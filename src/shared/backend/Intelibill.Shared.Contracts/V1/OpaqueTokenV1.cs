using System.Text.Json.Serialization;

namespace Intelibill.Shared.Contracts.V1;

/// <summary>An opaque credential and its stored digest. The issuing application owns its lifetime and authority.</summary>
public sealed record OpaqueTokenV1(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("tokenHash")] string TokenHash)
{
    public override string ToString() => nameof(OpaqueTokenV1);
}
