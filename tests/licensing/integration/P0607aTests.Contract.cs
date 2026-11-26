using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0607aTests
{
    /// <summary>The archived V1 fixture, read verbatim, pins names, strict bodies, narrow projections and the terms digest.</summary>
    [Fact]
    public void ContractMatchesTheArchivedV1Fixture()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(ProjectPaths.Root,
            "tests/contracts/licensing/coverage-transfer-v1.json")));
        var v = json.RootElement;
        var actions = v.GetProperty("actions");
        Assert.Equal(CoverageTransfersV1.PolicyVersion, v.GetProperty("policyVersion").GetString());
        Assert.Equal((CoverageTransfersV1.Preview, CoverageTransfersV1.Request, CoverageTransfersV1.Recover, CoverageTransfersV1.List),
            (actions.GetProperty("preview").GetString(), actions.GetProperty("request").GetString(),
                actions.GetProperty("recover").GetString(), actions.GetProperty("list").GetString()));
        Assert.Equal((CoverageTransfersV1.Read, CoverageTransfersV1.Consent, CoverageTransfersV1.ConsentRecover, CoverageTransfersV1.OwnerRead),
            (actions.GetProperty("read").GetString(), actions.GetProperty("consent").GetString(),
                actions.GetProperty("consentRecover").GetString(), actions.GetProperty("ownerRead").GetString()));
        Assert.All(actions.EnumerateObject(), a => Assert.True(CoverageTransfersV1.IsAction(a.Value.GetString())));
        Assert.True(MerchantBillingDelegationV1.IsActorScoped(CoverageTransfersV1.OwnerRead));
        Assert.False(MerchantBillingDelegationV1.IsActorScoped(CoverageTransfersV1.Consent));
        Assert.Equal(CoverageTransfersV1.ConsentKind, v.GetProperty("consentKind").GetString());
        Assert.Equal(CoverageTransfersV1.ConsentReceiptPath, v.GetProperty("consentReceiptPath").GetString());
        Assert.Equal(CoverageTransfersV1.ShopAuthorityPath, v.GetProperty("shopAuthorityPath").GetString());
        Assert.All(v.GetProperty("treatments").EnumerateArray(), t => Assert.True(CoverageTransfersV1.IsTreatment(t.GetString())));

        // Bodies are strict: every acknowledgement is required and unknown members are refused.
        var raw = v.GetProperty("request").GetRawText();
        var request = JsonSerializer.Deserialize<RequestCoverageTransferV1>(raw)!;
        Assert.True(request.IsValid());
        Assert.False((request with { AcknowledgedHistoricalBillsStay = false }).IsValid());
        Assert.False((request with { Treatment = CoverageTransfersV1.ReviewedAllocation }).IsValid());
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RequestCoverageTransferV1>(raw.TrimEnd().TrimEnd('}') + ",\"extra\":true}"));
        var decision = v.GetProperty("decision").GetRawText();
        Assert.True(JsonSerializer.Deserialize<CoverageTransferDecisionV1>(decision)!.IsValid());
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CoverageTransferDecisionV1>(decision.TrimEnd().TrimEnd('}') + ",\"purpose\":\"x\"}"));

        var t = v.GetProperty("terms");
        Guid Id(string name) => t.GetProperty(name).GetGuid();
        Assert.Equal(v.GetProperty("termsDigest").GetString(), CoverageTransfersV1.TermsDigest(Id("transferId"),
            t.GetProperty("proposalRevision").GetInt64(), Id("sourceAccountId"), Id("destinationAccountId"), Id("shopId"),
            Id("sourceAffiliationId"), Id("sourceTermId"), t.GetProperty("treatment").GetString()!, null,
            t.GetProperty("sourcePaidThroughAt").GetDateTimeOffset(), t.GetProperty("evidenceDigest").GetString()!,
            t.GetProperty("permitDigest").GetString()!, t.GetProperty("expiresAt").GetDateTimeOffset()));
        Assert.Equal(v.GetProperty("participantFields").EnumerateArray().Select(x => x.GetString()), ParticipantFields);
    }

    [Fact]
    public async Task ConcurrentConsentsAndRetainedEvidenceAreSafe()
    {
        using var rsa = RSA.Create(2048);
        await using var s = await StartAsync(database, rsa);
        var before = await s.BaselineAsync();
        var id = (await CreateAsync(s)).GetProperty("transferId").GetGuid();
        var (source, destination) = (s.Source.Payer.Client, s.Destination.Payer.Client);
        var shownToSource = await ReadAsync(source, s.Source.Account, id);
        var shownToDestination = await ReadAsync(destination, s.Destination.Account, id);

        // Both billing owners, and two competing keys for the same purpose, decide at once from opposite accounts.
        // Every decision takes both account locks in one canonical order: no deadlock, one acceptance per purpose.
        var raced = await Task.WhenAll(
            DecideAsync(source, ConsentPath(s.Source.Account, id, SourcePurpose), "accept", shownToSource),
            DecideAsync(source, ConsentPath(s.Source.Account, id, SourcePurpose), "accept", shownToSource),
            DecideAsync(destination, ConsentPath(s.Destination.Account, id, DestinationPurpose), "accept", shownToDestination),
            DecideAsync(destination, ConsentPath(s.Destination.Account, id, DestinationPurpose), "accept", shownToDestination));
        Assert.Equal(2, raced.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(2, raced.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(1L, await s.F.CountAsync("coverage_transfer_consents", "purpose='SourceBillingOwner'"));
        Assert.Equal(1L, await s.F.CountAsync("coverage_transfer_consents", "purpose='DestinationBillingOwner'"));
        await ShopAcceptAsync(s, id);
        Assert.True((await ReadAsync(source, s.Source.Account, id)).GetProperty("consentsCollected").GetBoolean());
        Assert.Equal(before, await s.BaselineAsync());

        // Consent evidence is immutable under the runtime role's own table owner, and both migrations refuse to drop it.
        var tamper = await Assert.ThrowsAsync<PostgresException>(() => LicensingPostgreSqlFixture.ExecuteAsync(
            s.F.Hosts.LicensingOwner, "UPDATE public.coverage_transfer_consents SET kind = 'Revoked'"));
        Assert.Equal("55000", tamper.SqlState);
        await using var licensing = s.F.Hosts.Fixture.OpenMigrationContext();
        await using var merchant = P0209Fixture.MerchantDb(database);
        var licensingDown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            licensing.GetService<IMigrator>().MigrateAsync("20261126100000_BillingDocumentVersions"));
        var merchantDown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            merchant.GetService<IMigrator>().MigrateAsync("20261125140000_AddMerchantCoverageExceptionEvidence"));
        Assert.Equal("55000", Assert.IsType<PostgresException>(licensingDown.GetBaseException()).SqlState);
        Assert.Equal("55000", Assert.IsType<PostgresException>(merchantDown.GetBaseException()).SqlState);
    }
}
