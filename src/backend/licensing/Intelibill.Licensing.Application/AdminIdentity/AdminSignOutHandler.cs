namespace Intelibill.Licensing.Application.AdminIdentities;

public sealed class AdminSignOutHandler(IAdminRefreshCredentials credentials, IAdminSessionStore sessions)
{
    public async Task HandleAsync(string? refresh, AdminSessionCaller? access, CancellationToken token)
    {
        if (await credentials.EndAsync(refresh, token) || access is null) return;
        await sessions.SignOutDeviceAsync(access, token);
    }
}
