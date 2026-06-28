import 'package:intelibill_mobile/src/features/sales/data/dto/sale_return_preview_dto.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_return.dart';

class SaleReturnMapper {
  const SaleReturnMapper._();

  static SaleReturnPreview toDomain(SaleReturnPreviewResponseDto dto) {
    return SaleReturnPreview(
      saleId: dto.saleId,
      hasFinancialAccess: dto.hasFinancialAccess,
      lines: dto.lines.map(_lineToDomain).toList(),
      financial: dto.financial == null
          ? null
          : _financialToDomain(dto.financial!),
      warnings: dto.warnings.map((item) => item.message).toList(),
    );
  }

  static SaleReturnPreviewLine _lineToDomain(
    SaleReturnPreviewLineDto dto,
  ) {
    return SaleReturnPreviewLine(
      saleItemId: dto.saleItemId,
      itemId: dto.itemId,
      inventoryBatchId: dto.inventoryBatchId,
      requestedQuantity: dto.requestedQuantity,
      returnedQuantity: dto.returnedQuantity,
      returnableQuantity: dto.returnableQuantity,
      condition: dto.condition,
      willRestock: dto.willRestock,
      financial: dto.financial == null
          ? null
          : SaleReturnPreviewLineFinancial(
              originalCostPrice: dto.financial!.originalCostPrice,
              originalSalesPrice: dto.financial!.originalSalesPrice,
              originalTaxRatePercent: dto.financial!.originalTaxRatePercent,
              originalIsPriceIncludingTax:
                  dto.financial!.originalIsPriceIncludingTax,
              maxRefundAmount: dto.financial!.maxRefundAmount,
              approvedRefundAmount: dto.financial!.approvedRefundAmount,
              taxableAmount: dto.financial!.taxableAmount,
              taxAmount: dto.financial!.taxAmount,
            ),
    );
  }

  static SaleReturnPreviewFinancial _financialToDomain(
    SaleReturnPreviewFinancialDto dto,
  ) {
    return SaleReturnPreviewFinancial(
      totalRefundAmount: dto.totalRefundAmount,
      dueReductionAmount: dto.dueReductionAmount,
      payoutAmount: dto.payoutAmount,
      totalTaxableAmount: dto.totalTaxableAmount,
      totalTaxAmount: dto.totalTaxAmount,
      customerBalanceBefore: dto.customerBalanceBefore,
      customerBalanceAfter: dto.customerBalanceAfter,
    );
  }
}
