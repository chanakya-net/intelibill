using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0609BrowserTests
{
    private static async Task PrepareLedgerAsync(P0308Fixture fixture, Package package, Guid order,
        Guid confirmation, Dictionary<string, string> targets)
    {
        foreach (var (key, amount) in new[] { ("FIRST_HOLD", 3500), ("LAST_HOLD", 4000) })
            targets[key] = (await P0609Tests.RequestRefundAsync(package, order, confirmation, amount)).ToString("D");
        // Fill the intervening intake pages using the verified receipt and the persisted request shape.
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, $"""
            INSERT INTO public.refund_requests
            SELECT (jsonb_populate_record(NULL::public.refund_requests, to_jsonb(r) || jsonb_build_object(
                'refund_request_id', gen_random_uuid(), 'operation_key', gen_random_uuid(), 'replay_id', gen_random_uuid(),
                'requested_at', r.requested_at + interval '1 second'))).*
            FROM (SELECT * FROM public.refund_requests WHERE account_id='{package.Account:D}' LIMIT 1) r,
                generate_series(1,1000)
            """);
        fixture.At(fixture.Checkout.CheckoutClock.GetUtcNow().AddSeconds(2));
        targets["EXCESS"] = (await P0609Tests.RequestRefundAsync(package, order, confirmation, 7000)).ToString("D");
    }
}
