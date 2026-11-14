using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Data;

public sealed class LicensingDbContext(DbContextOptions<LicensingDbContext> options) : DbContext(options);
