using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Intelibill.Licensing.Infrastructure.Payments;

internal static class PaymentStorageFailure
{
    /// <summary>EF wraps transient Npgsql query failures, including a terminated backend (57P01).</summary>
    internal static bool IsUnavailable(Exception error) => error is NpgsqlException or TimeoutException
        or DbUpdateException { InnerException: NpgsqlException }
        or InvalidOperationException { InnerException: NpgsqlException { IsTransient: true } };
}
