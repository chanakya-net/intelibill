namespace Intelibill.Application.Features.Auth.Commands.RevokeToken;

/// <remarks>The string form leaves the secret out, so logging the message cannot leak it.</remarks>
public sealed record RevokeTokenCommand(string RefreshToken)
{
    public override string ToString() => nameof(RevokeTokenCommand);
}
