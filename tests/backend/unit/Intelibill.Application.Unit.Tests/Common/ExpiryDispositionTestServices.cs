using Intelibill.Application.Common.Operations;
using Intelibill.Application.Common.Posting;
using Intelibill.Application.Common.Time;
using Intelibill.Application.Features.Expiry;
using Intelibill.Application.Features.Inventory.Services;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using NSubstitute;
namespace Intelibill.Application.Unit.Tests.Common;

internal static class ExpiryDispositionTestServices
{
    public static BatchDispositionService Create(IInventoryBatchRepository batches, IBusinessClock clock)
    {
        var journal = Substitute.For<IBatchDispositionRepository>();
        journal.LockBatchAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => batches.GetByIdAsync(call.ArgAt<Guid>(1), call.ArgAt<CancellationToken>(2)));
        var policies = Substitute.For<IExpiryPolicyRepository>();
        policies.GetEffectiveAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(new EffectiveExpiryPolicy(ExpiryPolicy.Default, 1, true, true, DateTimeOffset.MinValue));
        return new(journal, policies, Substitute.For<IExpiryPolicyFence>(), Substitute.For<IShopRepository>(), batches,
            Substitute.For<IInventoryRepository>(), Substitute.For<IStockTransactionRepository>(), Substitute.For<IInventoryAdjustmentRepository>(),
            Substitute.For<IInventoryAdjustmentNumberGenerator>(), Substitute.For<IFinancialOperationExecutor>(), Substitute.For<IPostingPeriodPolicy>(), clock, Substitute.For<IUnitOfWork>());
    }
}
