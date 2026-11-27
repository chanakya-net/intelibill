using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Intelibill.Shared.Contracts.V1;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0613Tests
{
    [Fact]
    public async Task ConcurrentCorrectionsConvergeOnOneSuccessor()
    {
        await using var fixture = await P0611Tests.StartAsync(database);
        var first = await fixture.AcceptedQuoteAsync(1);
        var second = await fixture.AcceptedQuoteAsync(1);
        using var billing = await P0611Tests.SignInBillingAsync(fixture);

        var (v1First, v2First, v2Key, v2Command, pdf2) = await RaceSameKeyAsync(billing, first.Quote);
        await RaceDistinctKeysAsync(billing, second.Quote);

        var firstPath = P0611Tests.Path(first.Quote);
        var pdf3 = DistinctPdf("Third successor after replay");
        var v3Meta = SupersedeMetadata(pdf3, "v3.pdf", ThirdIssuer, ThirdReference, P0611Tests.IssueDate, ThirdReason);
        var v3 = await SupersedeAsync(billing, firstPath, v2First.GetProperty("versionId").GetGuid(),
            v3Meta, Digest(await PreviewSupersedeAsync(billing, firstPath, v2First.GetProperty("versionId").GetGuid(), v3Meta)), pdf3);
        var v3Id = v3.GetProperty("versionId").GetGuid();

        // Retry v2 operation key after v3 exists -> exact idempotent 200 OK before stale-head check.
        using (var lateReplay = await SendSupersedeAsync(billing, firstPath, v1First, v2Command, pdf2, "v2.pdf"))
            Assert.Equal(v2First.GetRawText(), (await ReadAsync(lateReplay, HttpStatusCode.OK)).GetRawText());
        using (var recovered = await billing.SendAsync(HttpMethod.Get, $"{firstPath}/operations/{v2Key:D}"))
            Assert.Equal(v2First.GetRawText(), (await ReadAsync(recovered, HttpStatusCode.OK)).GetRawText());

        // Stale predecessor preview and write -> 409 PredecessorStale.
        using (var stalePreview = await P0212Fixture.PostAsync(billing, $"{firstPath}/{v1First:D}/supersede/preview", v3Meta))
            await AssertProblemAsync(stalePreview, HttpStatusCode.Conflict, "BillingDocument.PredecessorStale");
        using (var staleWrite = await SendSupersedeAsync(billing, firstPath, v1First,
            SupersedeCommand(v3Meta, new string('a', 64)), pdf3, "v3.pdf"))
            await AssertProblemAsync(staleWrite, HttpStatusCode.Conflict, "BillingDocument.PredecessorStale");

        await AssertPayloadAndOperatorMismatchesAsync(fixture, billing, first, second, v1First, v3Id, v2Key, v2Command, pdf2);
        Assert.Equal(5L, await fixture.CountAsync("billing_document_versions"));
    }

    [Fact]
    public async Task CorrectionEnforcesAuthorityMfaBoundariesAndValidation()
    {
        var clock = new P0201Clock { Now = P0611Tests.Instant };
        await using var fixture = await P0302Fixture.StartAsync(database, clock, P0306Tests.Settings);
        var purchase = await fixture.AcceptedQuoteAsync(1);
        using var billing = await P0611Tests.SignInBillingAsync(fixture);
        var path = P0611Tests.Path(purchase.Quote);
        var v1 = await P0611Tests.AttachAsync(billing, path, DistinctPdf("Initial for boundary check"));
        var v1Id = v1.GetProperty("versionId").GetGuid();

        await AssertInvalidInputsLeaveChainIntactAsync(fixture, billing, path, v1Id);

        // Kolkata midnight boundary (18:29:59Z = 23:59:59 IST -> next day rejected until +1s).
        var unicodeReason = new string('न', 150) + " — café GSTIN fix " + new string('x', 200);
        Assert.InRange(unicodeReason.Length, 350, 400);
        var pdf2 = DistinctPdf("Unicode and midnight valid v2");
        var nextDayMeta = SupersedeMetadata(pdf2, "unicode-v2.pdf", "Śrī Accounting", "REF-UNICODE-02",
            P0611Tests.IssueDate.AddDays(1), unicodeReason);
        using (var early = await P0212Fixture.PostAsync(billing, $"{path}/{v1Id:D}/supersede/preview", nextDayMeta))
            await AssertProblemAsync(early, HttpStatusCode.BadRequest, "BillingDocument.Invalid");

        clock.Now = P0611Tests.Instant.AddSeconds(1);
        fixture.Trial.Catalogue.Clock.Advance(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59));
        var preview = await PreviewSupersedeAsync(billing, path, v1Id, nextDayMeta);
        var key = Guid.NewGuid();
        var cmd = SupersedeCommand(nextDayMeta, Digest(preview), key);
        using (var okAt4m59s = await SendSupersedeAsync(billing, path, v1Id, cmd, pdf2, "unicode-v2.pdf"))
            Assert.Equal(unicodeReason, (await ReadAsync(okAt4m59s, HttpStatusCode.Created)).GetProperty("supersessionReason").GetString());

        // Exact 5m00s MFA expiry refuses both replay write and operation recovery.
        fixture.Trial.Catalogue.Clock.Advance(TimeSpan.FromSeconds(1));
        using (var staleWrite = await SendSupersedeAsync(billing, path, v1Id, cmd, pdf2, "unicode-v2.pdf"))
            await AssertProblemAsync(staleWrite, HttpStatusCode.Forbidden, "ManualPayment.RecentMfaRequired");
        using (var staleRecover = await billing.SendAsync(HttpMethod.Get, $"{path}/operations/{key:D}"))
            await AssertProblemAsync(staleRecover, HttpStatusCode.Forbidden, "ManualPayment.RecentMfaRequired");

        // Revoking Billing capability invalidates the existing session (401) and blocks a fresh sign-in (403).
        using var owner = await fixture.Trial.Catalogue.SignInOwnerAsync();
        await P0206Fixture.GrantAsync(owner, P0306Tests.BillingEmail, "support");
        using (var revoked = await billing.SendAsync(HttpMethod.Get, $"{path}/operations/{key:D}"))
            Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);

        const string supportEmail = "p0613.support.only@example.test";
        var supportSeed = await P0611Tests.EnrollAsync(fixture, owner, supportEmail, "support");
        using var support = await fixture.Trial.Catalogue.SignInOperatorAsync(supportSeed, supportEmail);
        using (var deniedPreview = await P0212Fixture.PostAsync(support, $"{path}/{v1Id:D}/supersede/preview", nextDayMeta))
            await AssertProblemAsync(deniedPreview, HttpStatusCode.Forbidden, "ManualPayment.AccessDenied");
        using (var deniedWrite = await SendSupersedeAsync(support, path, v1Id, cmd, pdf2, "unicode-v2.pdf"))
            await AssertProblemAsync(deniedWrite, HttpStatusCode.Forbidden, "ManualPayment.AccessDenied");
        using (var deniedRecover = await support.SendAsync(HttpMethod.Get, $"{path}/operations/{key:D}"))
            await AssertProblemAsync(deniedRecover, HttpStatusCode.Forbidden, "ManualPayment.AccessDenied");
        Assert.Equal(2L, await fixture.CountAsync("billing_document_versions"));
    }

    [Fact]
    public async Task DatabaseGuardsProtectSupersessionLineage()
    {
        await using var fixture = await P0611Tests.StartAsync(database);
        var first = await fixture.AcceptedQuoteAsync(1);
        var sameAccountSecond = await AcceptSecondOrderOnSameAccountAsync(fixture, first);
        var crossAccount = await fixture.AcceptedQuoteAsync(1);
        using var billing = await P0611Tests.SignInBillingAsync(fixture);
        var v1 = await P0611Tests.AttachAsync(billing, P0611Tests.Path(first.Quote), DistinctPdf("Original v1 invoice"));
        var doc1 = v1.GetProperty("documentId").GetGuid();
        var v1Id = v1.GetProperty("versionId").GetGuid();
        var sameAccountV1 = await P0611Tests.AttachAsync(billing, P0611Tests.Path(sameAccountSecond), DistinctPdf("Same-account second order v1"));
        var sameAccountDocId = sameAccountV1.GetProperty("documentId").GetGuid();
        var sameAccountV1Id = sameAccountV1.GetProperty("versionId").GetGuid();
        var crossAccountV1 = await P0611Tests.AttachAsync(billing, P0611Tests.Path(crossAccount.Quote), DistinctPdf("Cross-account order v1"));
        var crossAccountV1Id = crossAccountV1.GetProperty("versionId").GetGuid();

        await VerifyDirectSqlGuardsAsync(fixture, first.Quote, doc1, v1Id, sameAccountDocId, sameAccountV1Id, crossAccountV1Id);
        Assert.Equal(1L, await fixture.CountAsync("billing_document_versions", $"order_id = '{first.Quote.OrderId:D}'"));
        Assert.Equal(1L, await fixture.CountAsync("billing_document_versions", $"order_id = '{sameAccountSecond.OrderId:D}'"));

        // Once v2 exists, a direct SQL fork is refused (23505) and retained versions cannot be changed (55000).
        var v2Bytes = DistinctPdf("Corrected v2 invoice");
        var v2Meta = SupersedeMetadata(v2Bytes, "v2.pdf", SecondIssuer, SecondReference, P0611Tests.IssueDate, SecondReason);
        await SupersedeAsync(billing, P0611Tests.Path(first.Quote), v1Id, v2Meta,
            Digest(await PreviewSupersedeAsync(billing, P0611Tests.Path(first.Quote), v1Id, v2Meta)), v2Bytes);

        var actor = await ReadLatestActorAsync(fixture);
        var fork = await Assert.ThrowsAsync<PostgresException>(() =>
            DirectInsertAsync(fixture, first.Quote, actor, doc1, 2, v1Id, "Direct SQL fork attempt"));
        Assert.Equal("23505", fork.SqlState);

        await AssertRetentionImmutableAsync(fixture);
        Assert.Equal(2L, await fixture.CountAsync("billing_document_versions", $"order_id = '{first.Quote.OrderId:D}'"));
        Assert.Equal(1L, await fixture.CountAsync("billing_document_versions", $"order_id = '{sameAccountSecond.OrderId:D}'"));
        Assert.Equal(4L, await fixture.CountAsync("billing_document_versions"));
    }

    private static async Task<(Guid V1Id, JsonElement V2, Guid Key, Dictionary<string, object> Command, byte[] Pdf2)>
        RaceSameKeyAsync(P0110CookieBrowser billing, CheckoutQuoteV1 quote)
    {
        var path = P0611Tests.Path(quote);
        var v1 = await P0611Tests.AttachAsync(billing, path, DistinctPdf("Race same key v1"));
        var v1Id = v1.GetProperty("versionId").GetGuid();
        var pdf2 = DistinctPdf("Race same key v2");
        var meta = SupersedeMetadata(pdf2, "v2.pdf", SecondIssuer, SecondReference, P0611Tests.IssueDate, SecondReason);
        var key = Guid.NewGuid();
        var command = SupersedeCommand(meta, Digest(await PreviewSupersedeAsync(billing, path, v1Id, meta)), key);
        var proof = await P0206Fixture.ProofAsync(billing);
        var replies = await Task.WhenAll(
            SendSupersedeAsync(billing, path, v1Id, command, pdf2, "v2.pdf", proof: proof),
            SendSupersedeAsync(billing, path, v1Id, command, pdf2, "v2.pdf", proof: proof));
        using var r1 = replies[0];
        using var r2 = replies[1];
        var created = Assert.Single(replies, r => r.StatusCode == HttpStatusCode.Created);
        var replayed = Assert.Single(replies, r => r != created);
        var receipt = await ReadAsync(created, HttpStatusCode.Created);
        Assert.Equal(receipt.GetRawText(), (await ReadAsync(replayed, HttpStatusCode.OK)).GetRawText());
        return (v1Id, receipt, key, command, pdf2);
    }

    private static async Task RaceDistinctKeysAsync(P0110CookieBrowser billing, CheckoutQuoteV1 quote)
    {
        var path = P0611Tests.Path(quote);
        var v1 = await P0611Tests.AttachAsync(billing, path, DistinctPdf("Race distinct key v1"));
        var v1Id = v1.GetProperty("versionId").GetGuid();
        var pdf2 = DistinctPdf("Race distinct key v2");
        var meta = SupersedeMetadata(pdf2, "v2.pdf", SecondIssuer, SecondReference, P0611Tests.IssueDate, SecondReason);
        var digest = Digest(await PreviewSupersedeAsync(billing, path, v1Id, meta));
        var proof = await P0206Fixture.ProofAsync(billing);
        var replies = await Task.WhenAll(
            SendSupersedeAsync(billing, path, v1Id, SupersedeCommand(meta, digest), pdf2, "v2.pdf", proof: proof),
            SendSupersedeAsync(billing, path, v1Id, SupersedeCommand(meta, digest), pdf2, "v2.pdf", proof: proof));
        using var r1 = replies[0];
        using var r2 = replies[1];
        var created = Assert.Single(replies, r => r.StatusCode == HttpStatusCode.Created);
        var loser = Assert.Single(replies, r => r != created);
        await ReadAsync(created, HttpStatusCode.Created);
        await AssertProblemAsync(loser, HttpStatusCode.Conflict, "BillingDocument.PredecessorStale");
    }

    private static async Task AssertPayloadAndOperatorMismatchesAsync(P0302Fixture fixture, P0110CookieBrowser billing,
        P0302Fixture.Purchase first, P0302Fixture.Purchase second, Guid v1First, Guid v3Id, Guid v2Key,
        Dictionary<string, object> v2Command, byte[] pdf2)
    {
        var firstPath = P0611Tests.Path(first.Quote);
        var changedReason = new Dictionary<string, object>(v2Command) { ["reason"] = "Different correction reason" };
        using (var r = await SendSupersedeAsync(billing, firstPath, v1First, changedReason, pdf2, "v2.pdf"))
            await AssertProblemAsync(r, HttpStatusCode.Conflict, "BillingDocument.PayloadMismatch");

        using (var r = await SendSupersedeAsync(billing, firstPath, v3Id, v2Command, pdf2, "v2.pdf"))
            await AssertProblemAsync(r, HttpStatusCode.Conflict, "BillingDocument.PayloadMismatch");

        var changedRef = new Dictionary<string, object>(v2Command) { ["issuerReference"] = "REF-CHANGED" };
        using (var r = await SendSupersedeAsync(billing, firstPath, v1First, changedRef, pdf2, "v2.pdf"))
            await AssertProblemAsync(r, HttpStatusCode.Conflict, "BillingDocument.PayloadMismatch");

        const string otherEmail = "p0613.other.finance@example.test";
        using var owner = await fixture.Trial.Catalogue.SignInOwnerAsync();
        var otherSeed = await P0611Tests.EnrollAsync(fixture, owner, otherEmail, "billing");
        using var other = await fixture.Trial.Catalogue.SignInOperatorAsync(otherSeed, otherEmail);
        using (var r = await SendSupersedeAsync(other, firstPath, v1First, v2Command, pdf2, "v2.pdf"))
            await AssertProblemAsync(r, HttpStatusCode.Conflict, "BillingDocument.PayloadMismatch");
        using (var r = await other.SendAsync(HttpMethod.Get, $"{firstPath}/operations/{v2Key:D}"))
            await AssertProblemAsync(r, HttpStatusCode.NotFound, "BillingDocument.OperationNotFound");

        // Wrong account, wrong order, unknown predecessor -> scoped 404.
        var wrongOrderPath = P0611Tests.Path(first.Quote with { OrderId = second.Quote.OrderId });
        using (var r = await P0212Fixture.PostAsync(billing, $"{wrongOrderPath}/{v1First:D}/supersede/preview",
            SupersedeMetadata(pdf2, "v2.pdf", SecondIssuer, SecondReference, P0611Tests.IssueDate, SecondReason)))
            await AssertProblemAsync(r, HttpStatusCode.NotFound, "BillingDocument.OrderNotFound");
        using (var r = await P0212Fixture.PostAsync(billing, $"{firstPath}/{Guid.NewGuid():D}/supersede/preview",
            SupersedeMetadata(pdf2, "v2.pdf", SecondIssuer, SecondReference, P0611Tests.IssueDate, SecondReason)))
            await AssertProblemAsync(r, HttpStatusCode.NotFound, "BillingDocument.PredecessorNotFound");
    }

    private static async Task AssertInvalidInputsLeaveChainIntactAsync(P0302Fixture fixture, P0110CookieBrowser billing,
        string path, Guid v1Id)
    {
        var valid = DistinctPdf("Valid for invalid tests");
        var meta = SupersedeMetadata(valid, "v2.pdf", SecondIssuer, SecondReference, P0611Tests.IssueDate, SecondReason);
        var digest = Digest(await PreviewSupersedeAsync(billing, path, v1Id, meta));

        foreach (var badReason in new[] { "", "   ", " leading", "bad\nline", new string('a', 401) })
        {
            var badMeta = new Dictionary<string, object>(meta) { ["reason"] = badReason };
            using var r = await P0212Fixture.PostAsync(billing, $"{path}/{v1Id:D}/supersede/preview", badMeta);
            await AssertProblemAsync(r, HttpStatusCode.BadRequest, "BillingDocument.Invalid");
        }
        using (var r = await SendSupersedeAsync(billing, path, v1Id, SupersedeCommand(meta, digest, confirmed: false), valid, "v2.pdf"))
            await AssertProblemAsync(r, HttpStatusCode.BadRequest, "BillingDocument.Invalid");
        using (var r = await SendSupersedeAsync(billing, path, v1Id, SupersedeCommand(meta, digest), "<html>no</html>"u8.ToArray(), "v2.pdf"))
            await AssertProblemAsync(r, HttpStatusCode.BadRequest, "BillingDocument.ContentInvalid");
        using (var r = await SendSupersedeAsync(billing, path, v1Id, SupersedeCommand(meta, digest), valid, "v2.pdf", proof: ""))
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        using (var r = await SendSupersedeAsync(billing, path, v1Id, SupersedeCommand(meta, digest), valid, "v2.pdf",
            origin: "https://evil.example.test"))
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal(1L, await fixture.CountAsync("billing_document_versions"));
    }

    private static async Task<CheckoutQuoteV1> AcceptSecondOrderOnSameAccountAsync(
        P0302Fixture fixture, P0302Fixture.Purchase existing)
    {
        var secondShop = await P0209Fixture.SignInAsync(fixture.Hosts, ShopRole.Owner);
        await P0213Fixture.LinkAsync(existing.Account, existing.Payer, secondShop);
        await fixture.Delivery.DrainAsync(source: true);
        await fixture.Delivery.DrainAsync(source: false);
        var plan = await P0301Tests.PublishPlanAsync(fixture.Trial);
        var quote = await P0301Tests.CreateQuoteAsync(existing.Payer.Client, existing.Account,
            new(Guid.NewGuid(), plan, "Month", [secondShop.ShopId], 0, 1));
        using var accepted = await existing.Payer.Client.PostAsJsonAsync(
            P0301Tests.AcceptPath(quote), P0301Tests.Accept(quote));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        return (await accepted.Content.ReadFromJsonAsync<CheckoutQuoteV1>())!;
    }

    private static async Task AssertRetentionImmutableAsync(P0302Fixture fixture)
    {
        Assert.Equal(true, await P0611Tests.ScalarAsync(fixture,
            "SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid = 'public.billing_document_versions'::regclass"));

        foreach (var mutation in new[]
        {
            "UPDATE billing_document_versions SET supersession_reason = 'Edited'",
            "UPDATE billing_document_versions SET content = '\\x00'",
            "DELETE FROM billing_document_versions",
            "TRUNCATE billing_document_versions",
        })
        {
            var err = await Assert.ThrowsAsync<PostgresException>(() =>
                LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, mutation));
            Assert.Equal("55000", err.SqlState);
        }
    }

    private static async Task VerifyDirectSqlGuardsAsync(P0302Fixture fixture, CheckoutQuoteV1 quote, Guid doc1,
        Guid v1Id, Guid sameAccountOtherDocId, Guid sameAccountOtherV1Id, Guid crossAccountV1Id)
    {
        var actor = await ReadLatestActorAsync(fixture);
        // Gap, missing/blank reason on v2, reason on v1, wrong doc, same-account foreign-order parent (23514),
        // cross-account parent (23503), and missing parent (23503).
        var cases = new (int Version, Guid DocumentId, Guid? Predecessor, string? Reason, string ExpectedState)[]
        {
            (3, doc1, v1Id, "Gap version", "23514"),
            (2, doc1, v1Id, null, "23514"),
            (2, doc1, v1Id, "   ", "23514"),
            (1, Guid.NewGuid(), null, "Reason on v1", "23514"),
            (2, Guid.NewGuid(), v1Id, "Wrong document", "23514"),
            (2, sameAccountOtherDocId, sameAccountOtherV1Id, "Parent from other order on same account", "23514"),
            (2, doc1, crossAccountV1Id, "Parent from other account", "23503"),
            (2, doc1, Guid.NewGuid(), "Missing parent", "23503"),
        };
        foreach (var item in cases)
        {
            var ex = await Assert.ThrowsAsync<PostgresException>(() => DirectInsertAsync(fixture, quote, actor,
                item.DocumentId, item.Version, item.Predecessor, item.Reason));
            Assert.Equal(item.ExpectedState, ex.SqlState);
        }
    }

    private static async Task<(Guid Admin, Guid Session, DateTime Mfa)> ReadLatestActorAsync(P0302Fixture fixture)
    {
        await using var conn = new NpgsqlConnection(fixture.Hosts.LicensingOwner.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT admin_id, id, mfa_verified_at FROM public.admin_sessions ORDER BY mfa_verified_at DESC LIMIT 1", conn);
        await using var r = await cmd.ExecuteReaderAsync();
        Assert.True(await r.ReadAsync());
        return (r.GetGuid(0), r.GetGuid(1), r.GetDateTime(2));
    }

    private static async Task DirectInsertAsync(P0302Fixture fixture, CheckoutQuoteV1 quote,
        (Guid Admin, Guid Session, DateTime Mfa) actor, Guid documentId, int version, Guid? predecessor, string? reason)
    {
        await using var conn = new NpgsqlConnection(fixture.Hosts.Database.Login(
            fixture.Hosts.Fixture.Owner.Database!, "licensing_runtime").ConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using (var scope = new NpgsqlCommand("""
            SELECT set_config('app.billing_account_id', $1, true), set_config('app.manual_order_id', $2, true),
                set_config('app.manual_admin_id', $3, true), set_config('app.manual_session_id', $4, true)
            """, conn, tx))
        {
            foreach (var v in new[] { quote.AccountId.ToString("D"), quote.OrderId.ToString("D"),
                actor.Admin.ToString("D"), actor.Session.ToString("D") })
                scope.Parameters.AddWithValue(v);
            await scope.ExecuteNonQueryAsync();
        }
        var pdf = DistinctPdf($"Direct SQL {version}");
        await using var insert = new NpgsqlCommand("""
            INSERT INTO public.billing_document_versions
                (account_id, order_id, version_id, document_id, version_number, supersedes_version_id,
                kind, media_type, file_name, byte_length, content_sha256, content, issuer_name, issuer_reference,
                issued_on, operation_key, payload_hash, admin_id, session_id, mfa_verified_at, uploaded_at, supersession_reason)
            VALUES ($1, $2, gen_random_uuid(), $3, $4, $5, 'AccountingInvoice', 'application/pdf', 'direct.pdf',
                $6, $7, $8, 'Direct Issuer', 'DIRECT-REF', $9, gen_random_uuid(), repeat('a', 64), $10, $11, $12, $13, $14)
            """, conn, tx);
        insert.Parameters.AddWithValue(quote.AccountId);
        insert.Parameters.AddWithValue(quote.OrderId);
        insert.Parameters.AddWithValue(documentId);
        insert.Parameters.AddWithValue(version);
        insert.Parameters.AddWithValue(predecessor is { } p ? p : DBNull.Value);
        insert.Parameters.AddWithValue(pdf.Length);
        insert.Parameters.AddWithValue(P0611Tests.Hash(pdf));
        insert.Parameters.AddWithValue(pdf);
        insert.Parameters.AddWithValue(P0611Tests.IssueDate);
        insert.Parameters.AddWithValue(actor.Admin);
        insert.Parameters.AddWithValue(actor.Session);
        insert.Parameters.AddWithValue(actor.Mfa);
        insert.Parameters.AddWithValue(P0611Tests.Instant);
        insert.Parameters.AddWithValue(reason is not null ? reason : DBNull.Value);
        await insert.ExecuteNonQueryAsync();
        await tx.CommitAsync();
    }
}
