using System.Reflection;
using Intelibill.Application.Common.Licensing;
using Intelibill.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.Runtime.Handlers;

namespace Intelibill.Integration.Tests;

public sealed partial class LicenceActionClassificationTests
{
    [Fact]
    public async Task Inventory_pins_routes_handlers_writers_and_reconciliation_actions()
    {
        using var factory = new ApiWebApplicationFactory(fixture);
        await factory.InitializeAsync();

        var services = factory.Services;
        var endpoints = services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();
        var handlers = services.GetRequiredService<HandlerGraph>().AllChains()
            .SelectMany(chain => chain.HandlerCalls()).Select(call => call.Method).Distinct();
        var writers = MerchantAssemblies.SelectMany(assembly => assembly.GetTypes()).Where(IsWriter);
        var actual = endpoints.SelectMany(DescribeRoute)
            .Concat(handlers.Select(handler => DescribeMember("handler", handler)))
            .Concat(writers.Select(writer => DescribeMember("writer", writer)))
            .Concat(Enum.GetValues<ReconciliationAction>()
                .Select(action => Entry("reconciliation", action.ToString(), ReconciliationLicenceActions.For(action), "-")))
            .Order(StringComparer.Ordinal).ToArray();

        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "global.json")))
            root = root.Parent;
        Assert.NotNull(root);
        var path = Path.Combine(root.FullName, "tests/licensing/acceptance/p04-enforcement-inventory.tsv");
        var expected = (await File.ReadAllLinesAsync(path)).Where(line => !line.StartsWith('#')).ToArray();
        AssertInventory(expected, actual);
    }

    [Fact]
    public void Inventory_detects_added_removed_and_reclassified_actions()
    {
        var known = Entry("route", "POST api/test", LicenceAction.NewTrading, "-");
        var added = Entry("route", "GET api/other", LicenceAction.History, "-");
        var reclassified = Entry("route", "POST api/test", LicenceAction.History, "-");
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => AssertInventory([known], [known, added]));
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => AssertInventory([known], []));
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => AssertInventory([known], [reclassified]));
    }

    private static void AssertInventory(string[] expected, string[] actual)
    {
        var missing = expected.Except(actual, StringComparer.Ordinal).ToArray();
        var newEntries = actual.Except(expected, StringComparer.Ordinal).ToArray();
        Assert.True(expected.Length == actual.Length && missing.Length == 0 && newEntries.Length == 0,
            $"Removed or changed:\n{string.Join('\n', missing)}\nAdded or changed:\n{string.Join('\n', newEntries)}");
    }

    private static IEnumerable<string> DescribeRoute(RouteEndpoint route)
    {
        var action = Assert.Single(route.Metadata.OfType<LicenceActionAttribute>()).Action;
        var source = route.Metadata.GetMetadata<SuspensionAtSourceAttribute>() is { } atSource
            ? atSource.CheckRecovery ? "recover-then-source" : "source" : "request";
        var auth = route.Metadata.GetMetadata<IAllowAnonymous>() is null ? "protected" : "anonymous";
        var methods = route.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"];
        return methods.Select(method => Entry("route", $"{method} {route.RoutePattern.RawText}", action, $"{auth}/{source}"));
    }

    private static string DescribeMember(string kind, MemberInfo member)
    {
        var action = Assert.Single(member.GetCustomAttributes<LicenceActionAttribute>()).Action;
        var name = member is Type type ? type.FullName! : $"{member.DeclaringType!.FullName}.{member.Name}";
        return Entry(kind, name, action, "-");
    }

    private static string Entry(string kind, string name, LicenceAction action, string boundary) =>
        $"{kind}\t{name}\t{action}\t{MatrixRow(name, action)}\t{boundary}";

    private static string MatrixRow(string name, LicenceAction action) => action switch
    {
        LicenceAction.Authentication => "M01",
        LicenceAction.BasicShopSetup => "M02",
        LicenceAction.NewTrading => "M03",
        LicenceAction.OperationalCapacity => "M04",
        LicenceAction.History => "M05",
        LicenceAction.TillCompletion => "M06",
        LicenceAction.DueSettlement => "M07",
        LicenceAction.ExistingPayment => "M08",
        LicenceAction.SourceCorrection => "M09",
        LicenceAction.GeneralStock => "M10",
        LicenceAction.SafetyDisposition => "M11",
        LicenceAction.Recovery => "M12",
        LicenceAction.Billing => name.Contains("renew", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("invoice", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("refund", StringComparison.OrdinalIgnoreCase) ? "M13" : "M01/M13",
        LicenceAction.MemberRemoval => "M13",
        LicenceAction.Health => "health",
        LicenceAction.InternalService => "internal",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };
}
