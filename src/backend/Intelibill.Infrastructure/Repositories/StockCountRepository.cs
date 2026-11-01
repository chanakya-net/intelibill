using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class StockCountRepository(ApplicationDbContext context)
    : RepositoryBase<StockCount>(context), IStockCountRepository;
