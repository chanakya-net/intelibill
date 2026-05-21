using Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;
using Intelibill.Application.Features.Sales.Commands.RecordSale;
using Intelibill.Application.Features.Sales.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Sales.Commands.SyncOfflineSales;

public class SyncOfflineSalesCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IInvoiceLeaseRepository _invoiceLeaseRepository = Substitute.For<IInvoiceLeaseRepository>();
    private readonly ISaleLineValidator _saleLineValidator = Substitute.For<ISaleLineValidator>();
    private readonly ISaleRepository _saleRepository = Substitute.For<ISaleRepository>();
    private readonly IStockTransactionRepository _stockTransactionRepository = Substitute.For<IStockTransactionRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private SyncOfflineSalesCommandHandler CreateHandler() =>
        new(_userRepository, _invoiceLeaseRepository, _saleLineValidator, _saleRepository, _stockTransactionRepository, _unitOfWork);

    [Fact]
    public async Task HandleAsync_WhenDuplicate_ReturnsExistingResult()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var deviceId = "device-1";

        var user = User.CreateWithEmail("user@test.com", "hash", "Test", "User");
        user.AddShopMembership(ShopMembership.Create(shopId, user.Id, ShopRole.Owner, true));

        var line = new OfflineSaleSyncLineCommand(
            "BC-001",
            "B-01",
            "Rice",
            1m,
            80m,
            100m,
            120m,
            18m,
            false,
            Guid.NewGuid(),
            100m,
            0m,
            0m,
            100m,
            18m,
            118m,
            null,
            null,
            InstantDiscountType.None,
            0m,
            null);

        var sale = new OfflineSaleSyncCommand(
            $"offline-{Guid.NewGuid():N}",
            "INV-2025-26-000001",
            DateTimeOffset.UtcNow,
            null,
            "Ravi",
            "+919876543210",
            PaymentMethod.Cash,
            118m,
            0m,
            100m,
            118m,
            0m,
            18m,
            118m,
            InstantDiscountType.None,
            0m,
            null,
            null,
            null,
            null,
            [line]);

        var command = new SyncOfflineSalesCommand(actorId, shopId, deviceId, [sale]);
        var requestHash = OfflineSaleSyncIdempotencyHasher.ComputeHash(shopId, deviceId, sale);

        var saleItem = SaleItem.Create(
            shopId,
            Guid.NewGuid(),
            line.InventoryBatchId,
            line.Quantity,
            line.CostPrice,
            line.SalesPrice,
            line.Mrp,
            line.TaxRatePercent,
            line.IsPriceIncludingTax,
            false,
            preTaxAmountBeforeDiscount: line.PreTaxAmountBeforeDiscount,
            itemDiscountAmount: line.ItemDiscountAmount,
            saleDiscountAmount: line.SaleDiscountAmount,
            taxableAmount: line.TaxableAmount,
            taxAmount: line.TaxAmount,
            totalAmount: line.TotalAmount,
            configuredBatchRuleId: line.ConfiguredBatchRuleId,
            configuredBatchRulePercentage: line.ConfiguredBatchRulePercentage,
            itemDiscountOverrideType: line.ItemDiscountOverrideType,
            itemDiscountOverrideValue: line.ItemDiscountOverrideValue,
            hsnCode: line.HsnCode);

        var existingSale = Sale.Create(
            shopId,
            actorId,
            OfflineSaleSyncIdempotencyHasher.ComputeKey(deviceId, sale.ClientSaleId),
            requestHash,
            sale.InvoiceNumber,
            sale.CustomerId,
            sale.CustomerName,
            sale.CustomerPhone,
            sale.PaymentMethod,
            sale.SoldAt,
            sale.PaidAmount,
            sale.DueAmount,
            sale.TotalAmount,
            sale.TotalTaxAmount,
            [saleItem],
            subtotalBeforeDiscount: sale.SubtotalBeforeDiscount,
            totalBeforeDiscount: sale.TotalBeforeDiscount,
            totalDiscountAmount: sale.TotalDiscountAmount,
            configuredSaleRuleId: sale.ConfiguredSaleRuleId,
            configuredSaleRuleType: sale.ConfiguredSaleRuleType,
            configuredSaleRulePercentage: sale.ConfiguredSaleRulePercentage,
            configuredSaleRuleThresholdAmount: sale.ConfiguredSaleRuleThresholdAmount,
            saleDiscountOverrideType: sale.SaleDiscountOverrideType,
            saleDiscountOverrideValue: sale.SaleDiscountOverrideValue,
            source: SaleSource.Offline,
            clientSaleId: sale.ClientSaleId,
            deviceId: deviceId,
            syncedAt: DateTimeOffset.UtcNow);

        _userRepository.GetByIdWithDetailsAsync(actorId, Arg.Any<CancellationToken>())
            .Returns(user);
        _invoiceLeaseRepository.GetActiveByDeviceAsync(shopId, deviceId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _saleRepository.GetByClientSaleIdAsync(shopId, deviceId, sale.ClientSaleId, Arg.Any<CancellationToken>())
            .Returns(existingSale);

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        var item = Assert.Single(result.Value.Results);
        Assert.Equal("duplicate", item.Status);
        Assert.Equal(existingSale.Id, item.SaleId);
        await _saleLineValidator.DidNotReceive()
            .ValidateLinesAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(), Arg.Any<List<string>>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
