import 'package:intelibill_mobile/src/features/sales/data/dto/sale_preview_dto.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_preview.dart';

class SalePreviewMapper {
  const SalePreviewMapper._();

  static SalePreview toDomain(SalePreviewResponseDto dto) {
    return SalePreview(
      totalAmount: dto.totalAmount,
      totalTaxableAmount: dto.totalTaxableAmount,
      totalTaxAmount: dto.totalTaxAmount,
      totalDiscountAmount: dto.totalDiscountAmount,
      saleLevelEligibleSubtotal: dto.saleLevelEligibleSubtotal,
      configuredSaleRule: dto.configuredSaleRule == null
          ? null
          : SalePreviewConfiguredSaleRule(
              ruleId: dto.configuredSaleRule!.ruleId,
              ruleType: dto.configuredSaleRule!.ruleType,
              percentage: dto.configuredSaleRule!.percentage,
              thresholdAmount: dto.configuredSaleRule!.thresholdAmount,
            ),
      lines: dto.lines.map(_lineToDomain).toList(growable: false),
      infos: dto.infos.map(_infoToDomain).toList(growable: false),
      warnings: dto.warnings.map(_warningToDomain).toList(growable: false),
    );
  }

  static SalePreviewLine _lineToDomain(SalePreviewLineDto dto) {
    return SalePreviewLine(
      lineType: dto.lineType,
      itemId: dto.itemId,
      serviceId: dto.serviceId,
      barcode: dto.barcode,
      itemName: dto.itemName,
      inventoryBatchId: dto.inventoryBatchId,
      batchNumber: dto.batchNumber,
      quantity: dto.quantity,
      costPrice: dto.costPrice,
      salesPrice: dto.salesPrice,
      mrp: dto.mrp,
      taxRatePercent: dto.taxRatePercent,
      isPriceIncludingTax: dto.isPriceIncludingTax,
      preTaxAmountBeforeDiscount: dto.preTaxAmountBeforeDiscount,
      itemDiscountAmount: dto.itemDiscountAmount,
      saleDiscountAmount: dto.saleDiscountAmount,
      taxableAmount: dto.taxableAmount,
      taxAmount: dto.taxAmount,
      lineTotalAmount: dto.lineTotalAmount,
      maxAllowedItemDiscountFlat: dto.maxAllowedItemDiscountFlat,
      maxAllowedItemDiscountPercent: dto.maxAllowedItemDiscountPercent,
      configuredBatchRuleId: dto.configuredBatchRuleId,
      configuredBatchRulePercentage: dto.configuredBatchRulePercentage,
      hasClientPriceMismatch: dto.hasClientPriceMismatch,
      clientLineKey: dto.clientLineKey,
    );
  }

  static SalePreviewInfo _infoToDomain(SalePreviewInfoDto dto) {
    return SalePreviewInfo(code: dto.code, message: dto.message);
  }

  static SalePreviewWarning _warningToDomain(SalePreviewWarningDto dto) {
    return SalePreviewWarning(
      code: dto.code,
      message: dto.message,
      severity: dto.severity,
      inventoryBatchId: dto.inventoryBatchId,
      clientLineKey: dto.clientLineKey,
    );
  }
}
