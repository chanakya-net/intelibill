using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Intelibill.Shared.Web.Persistence;

/// <summary>
/// How long a transaction waits for a row or advisory lock before PostgreSQL gives up
/// (<c>lock_timeout</c>). A lock held longer than this is treated as an unavailable store,
/// not as a reason to keep a request open.
/// </summary>
public sealed class DatabaseLockOptions
{
    public const string SectionName = "DatabaseLocks";

    /// <summary>Budget for short critical sections: fences, budgets, single-row claims.</summary>
    public TimeSpan Brief { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>Budget for mutations that may wait behind a longer financial or stock write.</summary>
    public TimeSpan Standard { get; init; } = TimeSpan.FromSeconds(5);

    internal bool IsValid() =>
        Brief >= TimeSpan.FromMilliseconds(1) && Standard >= TimeSpan.FromMilliseconds(1)
        && Brief <= TimeSpan.FromMinutes(1) && Standard <= TimeSpan.FromMinutes(1);
}

public enum LockWait
{
    Brief,
    Standard,
}

public static class DatabaseLockTimeouts
{
    private static readonly DatabaseLockOptions Defaults = new();

    public static IServiceCollection AddDatabaseLockTimeouts(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DatabaseLockOptions>()
            .Bind(configuration.GetSection(DatabaseLockOptions.SectionName))
            .Validate(options => options.IsValid(), "DatabaseLocks budgets must be between 1 ms and 1 minute.")
            .ValidateOnStart();
        return services;
    }

    /// <summary>
    /// The <c>lock_timeout</c> value for <paramref name="wait"/>, from the configuration of the
    /// host that created <paramref name="db"/>. A context built outside a host uses the defaults.
    /// </summary>
    public static string Value(DbContext db, LockWait wait)
    {
        var options = Resolve(db);
        var budget = wait == LockWait.Brief ? options.Brief : options.Standard;
        return $"{(long)Math.Ceiling(budget.TotalMilliseconds)}ms";
    }

    /// <summary>Sets <c>lock_timeout</c> for the rest of the current transaction.</summary>
    public static Task SetLockTimeoutAsync(this DbContext db, LockWait wait, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.Database.ExecuteSqlAsync($"SELECT set_config('lock_timeout', {Value(db, wait)}, true)", cancellationToken);
    }

    private static DatabaseLockOptions Resolve(DbContext db)
    {
        try
        {
            return db.GetService<IOptions<DatabaseLockOptions>>().Value;
        }
        catch (InvalidOperationException)
        {
            return Defaults;
        }
    }
}
