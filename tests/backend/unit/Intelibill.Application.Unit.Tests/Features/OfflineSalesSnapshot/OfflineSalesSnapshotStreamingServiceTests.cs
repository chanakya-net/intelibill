using Intelibill.Application.Common.Time;
using Intelibill.Application.Features.Expiry;
using Intelibill.Domain.Interfaces;
using ErrorOr;
using Intelibill.Application.Features.OfflineSalesSnapshot.DTOs;
using Intelibill.Application.Features.OfflineSalesSnapshot.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.OfflineSalesSnapshot;

public class OfflineSalesSnapshotStreamingServiceTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly ICustomerRepository _customerRepository = Substitute.For<ICustomerRepository>();
    private readonly IDiscountRuleRepository _discountRuleRepository = Substitute.For<IDiscountRuleRepository>();
    private readonly IInvoiceLeaseRepository _invoiceLeaseRepository = Substitute.For<IInvoiceLeaseRepository>();
    private readonly IServiceRepository _serviceRepository = Substitute.For<IServiceRepository>();

    private readonly IOfflineExpiryEvidenceRepository _expiryEvidence = Substitute.For<IOfflineExpiryEvidenceRepository>();
    private readonly IExpiryPolicyRepository _expiryPolicies = Substitute.For<IExpiryPolicyRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private OfflineSalesSnapshotStreamingService CreateService()
    {
        var shops = Substitute.For<IShopRepository>();
        shops.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Shop.Create("Shop", "address", "city", "state", "560001", null, null, null));
        var fence = Substitute.For<IExpiryPolicyFence>();
        fence.AcquireAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<ErrorOr<Success>>(Result.Success));
        _expiryPolicies.GetStateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(x => ShopExpiryState.Create(x.Arg<Guid>(), DateTimeOffset.UtcNow));
        _expiryEvidence.HasRetainedCatalogAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(true);
        _expiryPolicies.GetEffectiveForItemsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(x => (IReadOnlyDictionary<Guid, EffectiveExpiryPolicy>)x.Arg<IReadOnlyList<Guid>>().ToDictionary(id => id, _ => new EffectiveExpiryPolicy(ExpiryPolicy.Default, 1, true)));
        return new(_userRepository, _customerRepository, _discountRuleRepository, _invoiceLeaseRepository, _serviceRepository,
            _expiryEvidence, _expiryPolicies, fence, _unitOfWork, shops, Clock());
    }

    private static IBusinessClock Clock()
    {
        var clock = Substitute.For<IBusinessClock>(); clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        clock.GetBusinessDate(Arg.Any<DateTimeOffset>(), Arg.Any<string>()).Returns(DateOnly.FromDateTime(DateTime.UtcNow)); return clock;
    }

    private static async IAsyncEnumerable<T> EmptyAsync<T>()
    {
        await Task.CompletedTask;
        yield break;
    }

    private static async IAsyncEnumerable<T> StreamOf<T>(T item)
    {
        yield return item;
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<Service> ServiceStreamOf(Service service)
    {
        yield return service;
        await Task.CompletedTask;
    }

    private void ArrangeEmptyStreams()
    {
        _customerRepository.StreamActiveByShopAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(EmptyAsync<Customer>());
        _discountRuleRepository.StreamActiveByShopAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(EmptyAsync<DiscountRule>());
        _invoiceLeaseRepository.StreamActiveByShopAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(EmptyAsync<InvoiceLease>());
        _serviceRepository.StreamActiveByShopAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(EmptyAsync<Service>());
    }

    private async Task<List<IOfflineSalesSnapshotStreamRecord>> CollectAsync(Guid shopId)
    {
        var records = new List<IOfflineSalesSnapshotStreamRecord>();
        await foreach (var record in CreateService().StreamAsync(shopId, Guid.NewGuid(), DateTimeOffset.UtcNow, CancellationToken.None))
        {
            records.Add(record);
        }

        return records;
    }

    [Fact]
    public async Task StreamAsync_ActiveLeaseCarriesTheSellerProfileItFroze()
    {
        var shopId = Guid.NewGuid();
        var profile = new InvoiceSellerProfile(
            new InvoiceSeller("Main Shop Pvt Ltd", "Main Shop", "42 MG Road, Bengaluru, Karnataka 560001", "29", "Karnataka", "29AAPFU0939F1ZR", TaxRegime.Regular),
            EInvoiceStatus.BelowThreshold,
            "A");
        var lease = InvoiceLease.Create(
            shopId, Guid.NewGuid(), "device-1", 2026, "A/2627/", 1, 100, 6,
            DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(1), sellerProfile: profile);
        ArrangeEmptyStreams();
        _invoiceLeaseRepository.StreamActiveByShopAsync(shopId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(StreamOf(lease));

        var records = await CollectAsync(shopId);

        var leaseRecord = Assert.Single(records.OfType<OfflineSalesSnapshotActiveLeaseRecord>());
        Assert.Equal(profile, leaseRecord.ActiveLease.SellerProfile);
    }

    [Fact]
    public async Task StreamAsync_BatchCarriesTheItemQuantityScaleAndDocumentUnit()
    {
        var shopId = Guid.NewGuid();
        var item = Item.Create(shopId, "Rice", null, "kg", "BC-1", true, Guid.NewGuid());
        var batch = InventoryBatch.Create(shopId, item.Id, "B-1", 5m, 80m, 120m, 100m, 18m, false, null, null, null, Guid.NewGuid()).Value;
        typeof(InventoryBatch).GetProperty(nameof(InventoryBatch.Item))!.SetValue(batch, item);
        ArrangeEmptyStreams();
        _expiryEvidence.GetCapturePageAsync(shopId, null, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new[] { batch });

        var records = await CollectAsync(shopId);

        var dto = Assert.Single(records.OfType<OfflineSalesSnapshotBatchRecord>()).Batch;
        Assert.Equal((3, "KGS"), (dto.QuantityScale, dto.DocumentUnit));
    }

    [Fact]
    public async Task StreamAsync_CustomerCarriesTheBuyerParticularsADocumentNeeds()
    {
        var shopId = Guid.NewGuid();
        var customer = Customer.Create(shopId, "Buyer Pvt Ltd", "9123456780", "4 Market St, Chennai", gstin: "33AABCT1332L1ZL");
        ArrangeEmptyStreams();
        _customerRepository.StreamActiveByShopAsync(shopId, Arg.Any<CancellationToken>())
            .Returns(StreamOf(customer));

        var records = await CollectAsync(shopId);

        var customerRecord = Assert.Single(records.OfType<OfflineSalesSnapshotCustomerRecord>());
        Assert.Equal(
            ("33AABCT1332L1ZL", "33", "4 Market St, Chennai"),
            (customerRecord.Customer.Gstin, customerRecord.Customer.StateCode, customerRecord.Customer.Address));
    }

    [Fact]
    public async Task StreamAsync_EmitsSchemaVersion4()
    {
        _customerRepository.StreamActiveByShopAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(EmptyAsync<Customer>());
        _discountRuleRepository.StreamActiveByShopAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(EmptyAsync<DiscountRule>());
        _invoiceLeaseRepository.StreamActiveByShopAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(EmptyAsync<InvoiceLease>());
        _serviceRepository.StreamActiveByShopAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(EmptyAsync<Service>());

        var snapshotId = Guid.NewGuid();
        var records = new List<IOfflineSalesSnapshotStreamRecord>();
        await foreach (var record in CreateService().StreamAsync(Guid.NewGuid(), snapshotId, DateTimeOffset.UtcNow, CancellationToken.None))
        {
            records.Add(record);
        }

        var metadata = Assert.IsType<OfflineSalesSnapshotMetadataRecord>(records[0]);
        Assert.Equal(4, metadata.Metadata.SchemaVersion);
    }

    [Fact]
    public async Task StreamAsync_EmitsActiveServicesAsServiceRecords()
    {
        var shopId = Guid.NewGuid();
        var service = Service.Create(shopId, "SVC-001", "Plumbing", null, 300m, "998511", 18m, false, true, Guid.NewGuid());

        _customerRepository.StreamActiveByShopAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(EmptyAsync<Customer>());
        _discountRuleRepository.StreamActiveByShopAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(EmptyAsync<DiscountRule>());
        _invoiceLeaseRepository.StreamActiveByShopAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(EmptyAsync<InvoiceLease>());
        _serviceRepository.StreamActiveByShopAsync(shopId, Arg.Any<CancellationToken>())
            .Returns(ServiceStreamOf(service));

        var records = new List<IOfflineSalesSnapshotStreamRecord>();
        await foreach (var record in CreateService().StreamAsync(shopId, Guid.NewGuid(), DateTimeOffset.UtcNow, CancellationToken.None))
        {
            records.Add(record);
        }

        var serviceRecord = records.OfType<OfflineSalesSnapshotServiceRecord>().SingleOrDefault();
        Assert.NotNull(serviceRecord);
        Assert.Equal(service.Id, serviceRecord.Service.ServiceId);
        Assert.Equal("SVC-001", serviceRecord.Service.Code);
        Assert.Equal("Plumbing", serviceRecord.Service.Name);
        Assert.Equal(300m, serviceRecord.Service.Price);
        Assert.Equal(18m, serviceRecord.Service.TaxRatePercent);
        Assert.False(serviceRecord.Service.TaxIncluded);
        Assert.Equal("998511", serviceRecord.Service.HsnCode);

        var completeRecord = Assert.IsType<OfflineSalesSnapshotCompleteRecord>(records[^1]);
        Assert.Equal(1, completeRecord.Complete.ServiceCount);
    }

    [Fact]
    public async Task StreamAsync_WhenNoServices_ServiceCountIsZero()
    {
        _customerRepository.StreamActiveByShopAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(EmptyAsync<Customer>());
        _discountRuleRepository.StreamActiveByShopAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(EmptyAsync<DiscountRule>());
        _invoiceLeaseRepository.StreamActiveByShopAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(EmptyAsync<InvoiceLease>());
        _serviceRepository.StreamActiveByShopAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(EmptyAsync<Service>());

        var records = new List<IOfflineSalesSnapshotStreamRecord>();
        await foreach (var record in CreateService().StreamAsync(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, CancellationToken.None))
        {
            records.Add(record);
        }

        var completeRecord = Assert.IsType<OfflineSalesSnapshotCompleteRecord>(records[^1]);
        Assert.Equal(0, completeRecord.Complete.ServiceCount);
    }
}
