using Intelibill.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Infrastructure;

/// <summary>
/// Makes an <see cref="IUnitOfWork"/> substitute classify persistence failures the way the real
/// EF Core unit of work does, so handler tests that throw EF exceptions still reach the catch paths.
/// </summary>
internal static class UnitOfWorkSubstituteExtensions
{
    public static IUnitOfWork ClassifiesFailuresLikeEf(this IUnitOfWork unitOfWork)
    {
        unitOfWork.IsConcurrencyConflict(Arg.Any<Exception>())
            .Returns(call => call.Arg<Exception>() is DbUpdateConcurrencyException);
        unitOfWork.IsWriteFailure(Arg.Any<Exception>())
            .Returns(call => call.Arg<Exception>() is DbUpdateException);
        unitOfWork.ConflictedEntities(Arg.Any<Exception>())
            .Returns(call => call.Arg<Exception>() is DbUpdateConcurrencyException concurrencyException
                ? concurrencyException.Entries.Select(entry => entry.Entity).ToList()
                : (IReadOnlyList<object>)[]);
        unitOfWork.ReloadConflictedEntitiesAsync(Arg.Any<Exception>(), Arg.Any<CancellationToken>())
            .Returns(call => ReloadAsync(call.Arg<Exception>(), call.Arg<CancellationToken>()));
        return unitOfWork;
    }

    private static async Task ReloadAsync(Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not DbUpdateConcurrencyException concurrencyException)
            return;

        foreach (var entry in concurrencyException.Entries)
            await entry.ReloadAsync(cancellationToken);
    }
}
