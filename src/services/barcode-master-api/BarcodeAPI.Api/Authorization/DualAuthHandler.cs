using Microsoft.AspNetCore.Authorization;

namespace BarcodeAPI.Api.Authorization;

public class DualAuthHandler : AuthorizationHandler<DualAuthRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        DualAuthRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
