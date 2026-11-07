using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Services.Time;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Common.Time;

/// <summary>Shared doubles for handlers that read the business date of the shop they post to.</summary>
internal static class BusinessTestDoubles
{
    /// <summary>A shop repository that answers every id with a default-zone shop.</summary>
    public static IShopRepository AnyShop()
    {
        var repository = Substitute.For<IShopRepository>();
        repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<Shop?>(Shop.Create("Main", "Address", "City", "State", "560001", null, null, null)));
        return repository;
    }

    public static BusinessClock ClockAt(MutableTimeProvider time) => new(time);

    public static BusinessClock RealClock() => new(TimeProvider.System);
}
