using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Intelibill.Application.Common.Licensing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0418Tests
{
    [Theory]
    [InlineData("Security", "Licence.SecuritySuspended")]
    [InlineData("Trading", "Licence.TradingRestricted")]
    public async Task Request_gated_routes_refuse_before_binding_and_never_return_shop_data(string mode, string code)
    {
        await using var fixture = await P0213Fixture.StartAsync(database,
            businessClock: new P0201Clock { Now = P0202Fixture.WholeSecondNow() });
        var (shop, account) = await PrepareShopAsync(fixture);
        using var client = shop.Client;
        using var admin = await P0403Tests.SignInSecurityAsync(fixture);
        var path = $"/api/admin/accounts/{account:D}/restrictions";
        await P0404Tests.SuspendAsync(admin, path, "Shop", shop.ShopId, mode);
        await DrainAsync(fixture);

        var source = fixture.Hosts.Merchant.Services.GetRequiredService<EndpointDataSource>();
        var checkedRoutes = 0;
        foreach (var route in source.Endpoints.OfType<RouteEndpoint>().Where(route => RequestGated(route, mode)))
        {
            var template = route.RoutePattern.RawText!;
            var target = "/" + RouteParameter().Replace(template, match =>
                match.Groups[1].Value == "shopId" ? shop.ShopId.ToString("D") : Guid.NewGuid().ToString("D"));
            foreach (var method in route.Metadata.GetRequiredMetadata<IHttpMethodMetadata>().HttpMethods)
            {
                using var request = new HttpRequestMessage(HttpMethod.Parse(method), target);
                if (request.Method != HttpMethod.Get) request.Content = JsonContent.Create(new { });
                using var response = await client.SendAsync(request);
                var problem = await ReadAsync(response, HttpStatusCode.Forbidden);
                Assert.True(response.Headers.CacheControl?.NoStore, $"{method} {template}");
                Assert.True(problem.TryGetProperty("code", out var received) && received.GetString() == code,
                    $"{method} {template}: {problem}");
                checkedRoutes++;
            }
        }

        Assert.True(checkedRoutes >= (mode == "Security" ? 100 : 15), $"Only {checkedRoutes} routes were exercised.");
    }

    private static bool RequestGated(RouteEndpoint route, string mode)
    {
        if (route.Metadata.GetMetadata<IAllowAnonymous>() is not null || route.RoutePattern.RawText is null ||
            route.Metadata.GetMetadata<LicenceActionAttribute>() is not { Action: var action } ||
            action is LicenceAction.Authentication or LicenceAction.Health or LicenceAction.Billing
                or LicenceAction.InternalService or LicenceAction.MemberRemoval ||
            route.Metadata.GetMetadata<IHttpMethodMetadata>() is null ||
            route.Metadata.GetMetadata<SuspensionAtSourceAttribute>() is { CheckRecovery: false })
            return false;

        return mode == "Security" || route.Metadata.GetMetadata<SuspensionAtSourceAttribute>() is null &&
            action is LicenceAction.NewTrading or LicenceAction.OperationalCapacity or LicenceAction.GeneralStock;
    }

    [GeneratedRegex("\\{\\*{0,2}([^}:?]+)[^}]*\\}")]
    private static partial Regex RouteParameter();
}
