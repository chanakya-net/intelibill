using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Unit.Tests.Entities;

public sealed class OfflineSubmissionTests
{
    private static readonly Guid ShopId = Guid.NewGuid();
    private static readonly Guid Submitter = Guid.NewGuid();
    private static readonly DateTimeOffset Sold = new(2026, 5, 3, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Received = new(2026, 5, 10, 10, 0, 0, TimeSpan.Zero);

    private static OfflineSubmission Create(
        OfflineSubmissionSource source = OfflineSubmissionSource.Sync,
        Guid? asserted = null,
        Guid? original = null) =>
        OfflineSubmission.Record(
            ShopId, "device-1", "client-1", "INV-0001", new string('a', 64), "{}", Sold, Received,
            source, Submitter, asserted, original, null, null);

    [Fact]
    public void Record_KeepsOriginalIdentity()
    {
        var asserted = Guid.NewGuid();
        var original = Guid.NewGuid();
        var lease = Guid.NewGuid();

        var submission = OfflineSubmission.Record(
            ShopId, " device-1 ", " client-1 ", "INV-0001", new string('b', 64), "{\"a\":1}", Sold, Received,
            OfflineSubmissionSource.Recovery, Submitter, asserted, original, lease, "op-1");

        Assert.Equal(ShopId, submission.ShopId);
        Assert.Equal("device-1", submission.DeviceId);
        Assert.Equal("client-1", submission.ClientSaleId);
        Assert.Equal("INV-0001", submission.InvoiceNumber);
        Assert.Equal(new string('b', 64), submission.PayloadHash);
        Assert.Equal("{\"a\":1}", submission.PayloadJson);
        Assert.Equal(Sold, submission.ClaimedSoldAt);
        Assert.Equal(Received, submission.ReceivedAt);
        Assert.Equal(OfflineSubmissionSource.Recovery, submission.Source);
        Assert.Equal(Submitter, submission.SubmittedByUserId);
        Assert.Equal(asserted, submission.AssertedIssuerUserId);
        Assert.Equal(original, submission.OriginalIssuerUserId);
        Assert.Equal(lease, submission.OriginalLeaseId);
        Assert.Equal("op-1", submission.RecoveryOperationId);
        Assert.Null(submission.SaleId);
    }

    [Fact]
    public void LinkSale_SameIdTwice_Succeeds()
    {
        var submission = Create();
        var saleId = Guid.NewGuid();

        Assert.False(submission.LinkSale(saleId).IsError);
        Assert.False(submission.LinkSale(saleId).IsError);
        Assert.Equal(saleId, submission.SaleId);
    }

    [Fact]
    public void LinkSale_DifferentId_Conflicts()
    {
        var submission = Create();
        var saleId = Guid.NewGuid();
        submission.LinkSale(saleId);

        var result = submission.LinkSale(Guid.NewGuid());

        Assert.True(result.IsError);
        Assert.Equal("OfflineSubmission.SaleAlreadyLinked", result.FirstError.Code);
        Assert.Equal(saleId, submission.SaleId);
    }

    [Fact]
    public void AttributableUser_LegacySync_IsSubmitter() =>
        Assert.Equal(Submitter, Create().AttributableUserId);

    [Fact]
    public void AttributableUser_SyncWithMismatchedAssertion_IsNull() =>
        Assert.Null(Create(asserted: Guid.NewGuid()).AttributableUserId);

    [Fact]
    public void AttributableUser_RecoveryWithoutVerifiedIssuer_IsNull() =>
        Assert.Null(Create(OfflineSubmissionSource.Recovery).AttributableUserId);

    [Fact]
    public void AttributableUser_VerifiedIssuer_IsIssuer()
    {
        var issuer = Guid.NewGuid();
        Assert.Equal(issuer, Create(OfflineSubmissionSource.Recovery, issuer, issuer).AttributableUserId);
    }
}
