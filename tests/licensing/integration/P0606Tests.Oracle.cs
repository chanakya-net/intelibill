using System.Text.Json;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0606Tests
{
    internal static async Task AssertSlotConservationOracleAsync(P0308Fixture f, Guid account, Guid term,
        int capacity, DateTimeOffset expectedStart, DateTimeOffset expectedEnd)
    {
        var (expected, handoffs) = await BuildIndependentOracleAsync(f, account, term, capacity, expectedStart, expectedEnd);
        VerifyOracleInvariants(expected, capacity, expectedStart, expectedEnd);
        await VerifyLinkedCourtesyGrantsAsync(f, account, handoffs);
        await CompareProductionRosterFunctionsAsync(f, account, term, expected, expectedStart, expectedEnd, handoffs);
    }

    // Independent persisted-facts oracle: reads ONLY base tables (never term_paid_slot_intervals,
    // term_effective_roster or shop_effective_paid_link).
    private static async Task<(OracleInterval[] Expected, PersistedHandoff[] Handoffs)> BuildIndependentOracleAsync(
        P0308Fixture f, Guid account, Guid term, int expectedCapacity, DateTimeOffset expectedStart, DateTimeOffset expectedEnd)
    {
        var termJson = (string)(await f.ScalarAsync($"""
            SELECT jsonb_build_object(
                'startsAt', t.starts_at, 'endsAt', t.ends_at,
                'fundedCapacity', coalesce((SELECT i.capacity FROM public.capacity_increases i
                    WHERE i.account_id = t.account_id AND i.term_id = t.term_id AND i.state = 'Applied'
                    ORDER BY i.target_revision DESC LIMIT 1), t.capacity),
                'hasFulfilment', EXISTS (SELECT FROM public.payment_fulfilments pf
                    WHERE pf.account_id = t.account_id AND pf.term_id = t.term_id))::text
            FROM public.subscription_coverage_terms t
            WHERE t.account_id = '{account}' AND t.term_id = '{term}'
            """))!;
        var termRow = JsonSerializer.Deserialize<JsonElement>(termJson);
        Assert.True(termRow.GetProperty("hasFulfilment").GetBoolean());
        Assert.Equal(expectedCapacity, termRow.GetProperty("fundedCapacity").GetInt32());
        Assert.Equal(expectedStart, termRow.GetProperty("startsAt").GetDateTimeOffset().ToUniversalTime());
        Assert.Equal(expectedEnd, termRow.GetProperty("endsAt").GetDateTimeOffset().ToUniversalTime());

        var baseSlots = JsonSerializer.Deserialize<JsonElement>(await ReadBaseSlotsJsonAsync(f, account, term, expectedStart))
            .EnumerateArray().ToArray();
        var handoffs = JsonSerializer.Deserialize<JsonElement>(await ReadHandoffsJsonAsync(f, account, term))
            .EnumerateArray().Select(ParseHandoff).ToArray();
        var expected = ExpandExpectedLineages(baseSlots, handoffs, expectedEnd);
        return (expected, handoffs);
    }

    private static async Task<string> ReadBaseSlotsJsonAsync(P0308Fixture f, Guid account, Guid term, DateTimeOffset expectedStart) =>
        (string)(await f.ScalarAsync($"""
            SELECT coalesce(jsonb_agg(jsonb_build_object(
                'origin', origin_shop_id, 'aff', origin_aff_id, 'start', base_start) ORDER BY origin_shop_id), '[]'::jsonb)::text
            FROM (
                SELECT p.shop_id AS origin_shop_id, p.affiliation_request_id AS origin_aff_id,
                    greatest('{expectedStart:O}'::timestamptz, coalesce(p.coverage_from, '{expectedStart:O}'::timestamptz)) AS base_start
                FROM public.payment_fulfilment_shops p
                WHERE p.account_id = '{account}' AND p.term_id = '{term}'
                UNION ALL
                SELECT a.shop_id, a.request_id, '{expectedStart:O}'::timestamptz
                FROM public.shop_affiliations a
                WHERE a.account_id = '{account}' AND a.term_id = '{term}'
                    AND NOT EXISTS (SELECT FROM public.payment_fulfilment_shops p
                        WHERE p.account_id = '{account}' AND p.term_id = '{term}' AND p.shop_id = a.shop_id)
            ) s
            """))!;

    private static async Task<string> ReadHandoffsJsonAsync(P0308Fixture f, Guid account, Guid term) =>
        (string)(await f.ScalarAsync($"""
            SELECT coalesce(jsonb_agg(jsonb_build_object(
                'approvalId', approval_id, 'origin', slot_origin_shop_id, 'slotGen', slot_generation,
                'outShop', outgoing_shop_id, 'outAff', outgoing_affiliation_id,
                'inShop', incoming_shop_id, 'inAff', incoming_affiliation_id,
                'prevStart', previous_starts_at, 'cutover', cutover_at, 'origEnd', original_ends_at,
                'grantId', courtesy_grant_id, 'grantTermId', courtesy_term_id,
                'courtesyStart', courtesy_starts_at)
                ORDER BY slot_origin_shop_id, slot_generation), '[]'::jsonb)::text
            FROM public.paid_slot_handoffs
            WHERE account_id = '{account}' AND term_id = '{term}'
            """))!;

    private static OracleInterval[] ExpandExpectedLineages(JsonElement[] baseSlots, PersistedHandoff[] handoffs, DateTimeOffset expectedEnd)
    {
        var byOrigin = handoffs.ToLookup(h => h.Origin);
        var expected = new List<OracleInterval>();
        foreach (var slot in baseSlots)
        {
            var origin = slot.GetProperty("origin").GetGuid();
            var curShop = origin;
            var curAff = slot.GetProperty("aff").GetGuid();
            var curStart = slot.GetProperty("start").GetDateTimeOffset().ToUniversalTime();
            var gen = 0;
            foreach (var h in byOrigin[origin].OrderBy(x => x.SlotGen))
            {
                Assert.Equal(++gen, h.SlotGen);
                Assert.Equal(curShop, h.OutShop);
                Assert.Equal(curAff, h.OutAff);
                Assert.Equal(curStart, h.PrevStart);
                Assert.Equal(expectedEnd, h.OrigEnd);
                Assert.True(curStart < h.Cutover && h.Cutover < expectedEnd);
                expected.Add(new(origin, curShop, curAff, curStart, h.Cutover, IsFinal: false));
                curShop = h.InShop;
                curAff = h.InAff;
                curStart = h.Cutover;
            }
            expected.Add(new(origin, curShop, curAff, curStart, expectedEnd, IsFinal: true));
        }
        return [.. expected.OrderBy(i => i.Origin).ThenBy(i => i.Start)];
    }

    private static void VerifyOracleInvariants(OracleInterval[] expected, int capacity,
        DateTimeOffset expectedStart, DateTimeOffset expectedEnd)
    {
        var expectedSpan = expectedEnd - expectedStart;
        var byOrigin = expected.GroupBy(r => r.Origin).ToArray();
        Assert.Equal(capacity, byOrigin.Length);
        foreach (var lineage in byOrigin)
        {
            var ordered = lineage.OrderBy(x => x.Start).ToArray();
            Assert.Equal(expectedStart, ordered[0].Start);
            Assert.Equal(expectedEnd, ordered[^1].End);
            var sum = TimeSpan.Zero;
            for (var i = 0; i < ordered.Length; i++)
            {
                Assert.True(ordered[i].Start < ordered[i].End);
                if (i > 0) Assert.Equal(ordered[i - 1].End, ordered[i].Start);
                for (var j = i + 1; j < ordered.Length; j++)
                    Assert.False(ordered[i].Start < ordered[j].End && ordered[j].Start < ordered[i].End);
                sum += ordered[i].End - ordered[i].Start;
            }
            Assert.Equal(expectedSpan, sum);
        }

        // Probe every distinct boundary instant t and t - 1us: paid holders <= capacity, all distinct shops.
        var probes = expected.SelectMany(r => new[] { r.Start, r.End.AddTicks(-10) }).Distinct().ToArray();
        foreach (var probe in probes)
        {
            var active = expected.Where(r => r.Start <= probe && probe < r.End).ToArray();
            Assert.True(active.Length <= capacity, $"Holders at {probe:O}: {active.Length} > {capacity}");
            Assert.Equal(active.Length, active.Select(a => a.Shop).Distinct().Count());
        }
    }

    private static async Task VerifyLinkedCourtesyGrantsAsync(P0308Fixture f, Guid account, PersistedHandoff[] handoffs)
    {
        foreach (var h in handoffs)
        {
            if (h.GrantId is not { } grantId)
            {
                Assert.Equal(0L, await f.CountAsync("complimentary_grant_receipts",
                    $"account_id='{account}' AND operation_key='{h.ApprovalId}'"));
                continue;
            }
            var json = (string)(await f.ScalarAsync($"""
                SELECT jsonb_build_object(
                    'termId', g.term_id, 'startsAt', g.starts_at, 'endsAt', g.ends_at, 'recordedAt', g.recorded_at,
                    'shopId', s.shop_id, 'affId', s.affiliation_request_id, 'shopStartsAt', s.starts_at, 'shopEndsAt', s.ends_at,
                    'opKey', r.operation_key)::text
                FROM public.complimentary_grants g
                JOIN public.complimentary_grant_shops s USING (grant_id, account_id, term_id)
                JOIN public.complimentary_grant_receipts r USING (grant_id, account_id)
                WHERE g.account_id = '{account}' AND g.grant_id = '{grantId}'
                """))!;
            var g = JsonSerializer.Deserialize<JsonElement>(json);
            var starts = g.GetProperty("startsAt").GetDateTimeOffset().ToUniversalTime();
            var ends = g.GetProperty("endsAt").GetDateTimeOffset().ToUniversalTime();
            var recorded = g.GetProperty("recordedAt").GetDateTimeOffset().ToUniversalTime();
            Assert.Equal(h.GrantTermId, g.GetProperty("termId").GetGuid());
            Assert.Equal(h.InShop, g.GetProperty("shopId").GetGuid());
            Assert.Equal(h.InAff, g.GetProperty("affId").GetGuid());
            Assert.Equal(h.CourtesyStart, starts);
            Assert.Equal(h.Cutover, ends);
            Assert.Equal(starts, g.GetProperty("shopStartsAt").GetDateTimeOffset().ToUniversalTime());
            Assert.Equal(ends, g.GetProperty("shopEndsAt").GetDateTimeOffset().ToUniversalTime());
            Assert.True(starts >= recorded && starts < ends);
            Assert.Equal(h.ApprovalId, g.GetProperty("opKey").GetGuid());
            Assert.Equal(1L, await f.CountAsync("complimentary_grant_shops",
                $"account_id='{account}' AND shop_id='{h.InShop}' AND starts_at < '{ends:O}' AND '{starts:O}' < ends_at"));
        }
    }

    private static async Task CompareProductionRosterFunctionsAsync(P0308Fixture f, Guid account, Guid term,
        OracleInterval[] expected, DateTimeOffset expectedStart, DateTimeOffset expectedEnd, PersistedHandoff[] handoffs)
    {
        var prodJson = (string)(await f.ScalarAsync($"""
            SELECT jsonb_agg(jsonb_build_object(
                'origin', slot_origin_shop_id, 'shop', shop_id, 'aff', affiliation_request_id,
                'start', interval_starts_at, 'end', interval_ends_at, 'isFinal', is_final_holder)
                ORDER BY slot_origin_shop_id, interval_starts_at)::text
            FROM public.term_paid_slot_intervals('{account}', '{term}')
            """))!;
        var prod = JsonSerializer.Deserialize<JsonElement>(prodJson).EnumerateArray()
            .Select(r => new OracleInterval(r.GetProperty("origin").GetGuid(), r.GetProperty("shop").GetGuid(),
                r.GetProperty("aff").GetGuid(), r.GetProperty("start").GetDateTimeOffset().ToUniversalTime(),
                r.GetProperty("end").GetDateTimeOffset().ToUniversalTime(), r.GetProperty("isFinal").GetBoolean()))
            .ToArray();
        Assert.Equal(expected, prod);

        DateTimeOffset?[] probes = [null, expectedStart, .. handoffs.SelectMany(h => new DateTimeOffset?[]
            { h.Cutover.AddTicks(-10), h.Cutover }), expectedEnd.AddTicks(-10), expectedEnd];
        foreach (var probe in probes.Distinct())
        {
            var expectedHolders = (probe is null || probe >= expectedEnd
                ? expected.Where(i => i.IsFinal)
                : expected.Where(i => i.Start <= probe && probe < i.End))
                .Select(i => (i.Origin, i.Shop, i.Aff)).OrderBy(x => x.Origin).ToArray();
            var sqlAt = probe is { } p ? $"'{p:O}'::timestamptz" : "NULL::timestamptz";
            var rosterJson = (string)(await f.ScalarAsync($"""
                SELECT jsonb_agg(jsonb_build_object(
                    'origin', slot_origin_shop_id, 'shop', shop_id, 'aff', affiliation_request_id)
                    ORDER BY slot_origin_shop_id)::text
                FROM public.term_effective_roster('{account}', '{term}', {sqlAt})
                """))!;
            var actualHolders = JsonSerializer.Deserialize<JsonElement>(rosterJson).EnumerateArray()
                .Select(r => (r.GetProperty("origin").GetGuid(), r.GetProperty("shop").GetGuid(), r.GetProperty("aff").GetGuid()))
                .OrderBy(x => x.Item1).ToArray();
            Assert.Equal(expectedHolders, actualHolders);
        }
    }

    private static PersistedHandoff ParseHandoff(JsonElement h) => new(
        h.GetProperty("approvalId").GetGuid(), h.GetProperty("origin").GetGuid(), h.GetProperty("slotGen").GetInt32(),
        h.GetProperty("outShop").GetGuid(), h.GetProperty("outAff").GetGuid(),
        h.GetProperty("inShop").GetGuid(), h.GetProperty("inAff").GetGuid(),
        h.GetProperty("prevStart").GetDateTimeOffset().ToUniversalTime(),
        h.GetProperty("cutover").GetDateTimeOffset().ToUniversalTime(),
        h.GetProperty("origEnd").GetDateTimeOffset().ToUniversalTime(),
        h.GetProperty("grantId").ValueKind == JsonValueKind.Null ? null : h.GetProperty("grantId").GetGuid(),
        h.GetProperty("grantTermId").ValueKind == JsonValueKind.Null ? null : h.GetProperty("grantTermId").GetGuid(),
        h.GetProperty("courtesyStart").ValueKind == JsonValueKind.Null ? null : h.GetProperty("courtesyStart").GetDateTimeOffset().ToUniversalTime());

    private sealed record OracleInterval(Guid Origin, Guid Shop, Guid Aff, DateTimeOffset Start, DateTimeOffset End, bool IsFinal);

    private sealed record PersistedHandoff(Guid ApprovalId, Guid Origin, int SlotGen, Guid OutShop, Guid OutAff,
        Guid InShop, Guid InAff, DateTimeOffset PrevStart, DateTimeOffset Cutover, DateTimeOffset OrigEnd,
        Guid? GrantId, Guid? GrantTermId, DateTimeOffset? CourtesyStart);
}
