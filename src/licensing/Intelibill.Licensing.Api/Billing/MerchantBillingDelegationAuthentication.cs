using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Infrastructure.Billing;
using Intelibill.Shared.Web.PublicErrors;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Intelibill.Licensing.Api.Billing;

internal sealed class MerchantBillingDelegationAuthentication(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    IMerchantBillingDelegationVerifier verifier) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    internal const string SchemeName = "MerchantBillingDelegation";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization;
        if (header.Count != 1 || !AuthenticationHeaderValue.TryParse(header[0], out var credential) ||
            !credential.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase) ||
            credential.Parameter is not { Length: > 0 } token || verifier.Verify(token) is not { } delegation)
            return Task.FromResult(AuthenticateResult.Fail("Invalid billing delegation."));

        Context.Features.Set(delegation);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = "Bearer";
        return CustomerBillingEndpoints.Problem(Context, StatusCodes.Status401Unauthorized, BillingDelegationErrors.Invalid)
            .ExecuteAsync(Context);
    }
}
