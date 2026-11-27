using Intelibill.Application.Features.Expiry;
using Intelibill.Application.Unit.Tests.Common;
using ErrorOr;
using Intelibill.Application.Common.Operations;
using Intelibill.Application.Common.Time;
using Intelibill.Application.Common.Posting;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Application.Features.Sales.Services.Cancellations;
using Intelibill.Application.Features.Tills;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;
using Wolverine;

namespace Intelibill.Application.Unit.Tests.Features.Sales.Cancellations;

public sealed class SaleCancellationHistoricalHashTests
{
    [Fact]
    public async Task AbsentTill_PinsOriginalHistoricalCancellationCanonicalHash()
    {
        // Canonical fixture from fad87708 SaleCancellationService.CancelAsync (before till fields):
        // {"saleId":"11111111-2222-3333-4444-555555555555","expectedVersion":42,"reason":"Mistaken entry","goodsRecoveredConfirmed":true,"cashRefundConfirmed":true}
        var saleId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var actor = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var shop = Shop.Create("Historical", "Address", "City", "State", "560001", null, null, null);
        var shops = Substitute.For<IShopRepository>();
        shops.GetActiveRoleAsync(actor, shop.Id, Arg.Any<CancellationToken>()).Returns(ShopRole.Owner);
        shops.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        var sales = Substitute.For<ISaleRepository>();
        sales.ExistsAsync(shop.Id, saleId, Arg.Any<CancellationToken>()).Returns(true);
        var executor = Substitute.For<IFinancialOperationExecutor>();
        string? captured = null;
        executor.ExecuteAsync(Arg.Any<FinancialOperationScope>(), actor, saleId, Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, Task<ErrorOr<SaleCancellationDto>>>>(), Arg.Any<CancellationToken>())
            .Returns(call => { captured = call.ArgAt<string>(3); return Task.FromResult<ErrorOr<SaleCancellationDto>>(Error.Conflict("Fixture.Replay", "Captured original request")); });
        var service = new SaleCancellationService(shops, sales, null!, Substitute.For<ISaleCancellationRollout>(), Substitute.For<IBusinessClock>(),
            Substitute.For<IPostingPeriodPolicy>(), executor, Substitute.For<IInventoryBatchRepository>(), Substitute.For<IInventoryRepository>(),
            Substitute.For<IStockTransactionRepository>(), Substitute.For<ICustomerLedgerEntryRepository>(), Substitute.For<ISaleCancellationRepository>(),
            Substitute.For<IUnitOfWork>(), Substitute.For<IMessageBus>(), Substitute.For<ITillPostingService>(), Substitute.For<IExpiryPolicyFence>(), null!);
        await service.CancelAsync(actor, shop.Id, saleId, new("historical-key", 42, "  Mistaken entry  ", true, true), default);
        Assert.Equal("B3CDB6C23D66D110185D41A989E792BF3C66FA10B608314CEAEB4F40E66B2A03", captured);
    }
}
