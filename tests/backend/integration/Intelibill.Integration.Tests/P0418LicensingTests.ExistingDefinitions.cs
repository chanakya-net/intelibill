using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

public sealed partial class P0418LicensingTests
{
    [Theory]
    [InlineData("service-edit")]
    [InlineData("service-activate")]
    [InlineData("service-deactivate")]
    [InlineData("customer-edit")]
    [InlineData("discount-disable")]
    public async Task Existing_definition_updates_keep_their_expiry_contract_and_wait_to_write(string action)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var end = _clock.Now.AddMinutes(1);
        var binding = await OpenEnrolledShopAsync(client, end);
        var (source, write) = await PrepareDefinitionAsync(client, action);
        _clock.Now = end;
        var before = await DefinitionFactsAsync(action, source);

        await using var holder = await HoldFenceAsync(binding);
        var request = write();
        var waited = await WaitsBehindAsync(request, Pid(holder));
        var whileHeld = await DefinitionFactsAsync(action, source);
        await holder.Database.RollbackTransactionAsync(Ct);
        using var outcome = await request;

        Assert.True(outcome.IsSuccessStatusCode, await outcome.Content.ReadAsStringAsync(Ct));
        Assert.True(waited);
        Assert.Equal(before, whileHeld);
        Assert.NotEqual(before, await DefinitionFactsAsync(action, source));
    }

    private static async Task<(Guid, Func<Task<HttpResponseMessage>>)> PrepareDefinitionAsync(HttpClient client, string action)
    {
        if (action == "discount-disable")
        {
            using var created = await client.PostAsJsonAsync("/api/discounts", DiscountInput("Existing discount"), Ct);
            var id = (await ReadSuccessAsync(created)).GetProperty("id").GetGuid();
            return (id, () => client.PostAsJsonAsync($"/api/discounts/{id:D}/disable", new { reason = "Stop offering discount" }, Ct));
        }

        if (action == "customer-edit")
        {
            const string phone = "+919111111111";
            using var created = await client.PostAsJsonAsync("/api/customers", new
            {
                name = "Existing contact", phoneNumber = phone, isActive = true, creditLimit = 0m, creditPolicyMode = "NoCredit",
            }, Ct);
            var id = (await ReadSuccessAsync(created)).GetProperty("customerId").GetGuid();
            return (id, () => client.PutAsJsonAsync($"/api/customers/{id:D}", new
            {
                name = "Updated contact", phoneNumber = phone, isActive = true, creditLimit = 0m, creditPolicyMode = "NoCredit",
            }, Ct));
        }

        using var service = await client.PostAsJsonAsync("/api/services", new
        {
            name = "Existing service", price = 50m, taxRatePercent = 0m, taxIncluded = false, isActive = action != "service-activate",
        }, Ct);
        var source = (await ReadSuccessAsync(service)).GetProperty("serviceId").GetGuid();
        var path = $"/api/services/{source:D}";
        return action == "service-edit"
            ? (source, () => client.PatchAsJsonAsync(path, new { name = "Updated service", price = 51m, taxRatePercent = 0m }, Ct))
            : (source, () => client.PostAsync(path + (action == "service-activate" ? "/activate" : "/deactivate"), null, Ct));
    }

    private async Task<string> DefinitionFactsAsync(string action, Guid id)
    {
        await using var db = Open(Migrator);
        var query = action switch
        {
            "customer-edit" => db.Database.SqlQuery<string>($"""SELECT to_jsonb(t)::text AS "Value" FROM customers t WHERE id = {id}"""),
            "discount-disable" => db.Database.SqlQuery<string>($"""SELECT to_jsonb(t)::text AS "Value" FROM discount_rules t WHERE id = {id}"""),
            _ => db.Database.SqlQuery<string>($"""SELECT to_jsonb(t)::text AS "Value" FROM services t WHERE id = {id}"""),
        };
        return await query.SingleAsync(Ct);
    }
}
