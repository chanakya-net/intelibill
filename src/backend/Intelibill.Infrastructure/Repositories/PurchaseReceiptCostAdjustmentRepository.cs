using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class PurchaseReceiptCostAdjustmentRepository(ApplicationDbContext context)
    : RepositoryBase<PurchaseReceiptCostAdjustment>(context), IPurchaseReceiptCostAdjustmentRepository;
