import 'package:equatable/equatable.dart';

class PreviewSaleItemRequest extends Equatable {
  const PreviewSaleItemRequest({
    required this.inventoryBatchId,
    required this.barcode,
    required this.batchNumber,
    required this.itemName,
    required this.quantity,
    required this.costPrice,
    required this.salesPrice,
    required this.mrp,
    required this.taxRatePercent,
    required this.isPriceIncludingTax,
    required this.itemDiscountType,
    required this.itemDiscountValue,
    required this.clientLineKey,
    this.hsnCode,
    required this.lineType,
    this.serviceId,
  });

  final String inventoryBatchId;
  final String barcode;
  final String batchNumber;
  final String itemName;
  final double quantity;
  final double costPrice;
  final double salesPrice;
  final double mrp;
  final double taxRatePercent;
  final bool isPriceIncludingTax;
  final int itemDiscountType;
  final double itemDiscountValue;
  final String clientLineKey;
  final String? hsnCode;
  final String lineType;
  final String? serviceId;

  @override
  List<Object?> get props => [
    inventoryBatchId,
    barcode,
    batchNumber,
    itemName,
    quantity,
    costPrice,
    salesPrice,
    mrp,
    taxRatePercent,
    isPriceIncludingTax,
    itemDiscountType,
    itemDiscountValue,
    clientLineKey,
    hsnCode,
    lineType,
    serviceId,
  ];
}

class PreviewSaleRequest extends Equatable {
  const PreviewSaleRequest({
    required this.saleDiscountType,
    required this.saleDiscountValue,
    required this.items,
  });

  final int saleDiscountType;
  final double saleDiscountValue;
  final List<PreviewSaleItemRequest> items;

  @override
  List<Object?> get props => [saleDiscountType, saleDiscountValue, items];
}

class SalePreviewConfiguredSaleRule extends Equatable {
  const SalePreviewConfiguredSaleRule({
    required this.ruleId,
    required this.ruleType,
    required this.percentage,
    this.thresholdAmount,
  });

  final String ruleId;
  final String ruleType;
  final double percentage;
  final double? thresholdAmount;

  @override
  List<Object?> get props => [ruleId, ruleType, percentage, thresholdAmount];
}

class SalePreviewLine extends Equatable {
  const SalePreviewLine({
    required this.lineType,
    this.itemId,
    this.serviceId,
    required this.barcode,
    required this.itemName,
    this.inventoryBatchId,
    this.batchNumber,
    required this.quantity,
    required this.costPrice,
    required this.salesPrice,
    required this.mrp,
    required this.taxRatePercent,
    required this.isPriceIncludingTax,
    required this.preTaxAmountBeforeDiscount,
    required this.itemDiscountAmount,
    required this.saleDiscountAmount,
    required this.taxableAmount,
    required this.taxAmount,
    required this.lineTotalAmount,
    required this.maxAllowedItemDiscountFlat,
    required this.maxAllowedItemDiscountPercent,
    this.configuredBatchRuleId,
    this.configuredBatchRulePercentage,
    required this.hasClientPriceMismatch,
    this.clientLineKey,
  });

  final String lineType;
  final String? itemId;
  final String? serviceId;
  final String barcode;
  final String itemName;
  final String? inventoryBatchId;
  final String? batchNumber;
  final double quantity;
  final double costPrice;
  final double salesPrice;
  final double mrp;
  final double taxRatePercent;
  final bool isPriceIncludingTax;
  final double preTaxAmountBeforeDiscount;
  final double itemDiscountAmount;
  final double saleDiscountAmount;
  final double taxableAmount;
  final double taxAmount;
  final double lineTotalAmount;
  final double maxAllowedItemDiscountFlat;
  final double maxAllowedItemDiscountPercent;
  final String? configuredBatchRuleId;
  final double? configuredBatchRulePercentage;
  final bool hasClientPriceMismatch;
  final String? clientLineKey;

  bool get isGoods => lineType == 'Goods';
  bool get isService => lineType == 'Service';

  @override
  List<Object?> get props => [
    lineType,
    itemId,
    serviceId,
    barcode,
    itemName,
    inventoryBatchId,
    batchNumber,
    quantity,
    costPrice,
    salesPrice,
    mrp,
    taxRatePercent,
    isPriceIncludingTax,
    preTaxAmountBeforeDiscount,
    itemDiscountAmount,
    saleDiscountAmount,
    taxableAmount,
    taxAmount,
    lineTotalAmount,
    maxAllowedItemDiscountFlat,
    maxAllowedItemDiscountPercent,
    configuredBatchRuleId,
    configuredBatchRulePercentage,
    hasClientPriceMismatch,
    clientLineKey,
  ];
}

class SalePreviewInfo extends Equatable {
  const SalePreviewInfo({
    required this.code,
    required this.message,
  });

  final String code;
  final String message;

  @override
  List<Object?> get props => [code, message];
}

class SalePreviewWarning extends Equatable {
  const SalePreviewWarning({
    required this.code,
    required this.message,
    required this.severity,
    this.inventoryBatchId,
    this.clientLineKey,
  });

  final String code;
  final String message;
  final String severity;
  final String? inventoryBatchId;
  final String? clientLineKey;

  @override
  List<Object?> get props => [
    code,
    message,
    severity,
    inventoryBatchId,
    clientLineKey,
  ];
}

class SalePreview extends Equatable {
  const SalePreview({
    required this.totalAmount,
    required this.totalTaxableAmount,
    required this.totalTaxAmount,
    required this.totalDiscountAmount,
    required this.saleLevelEligibleSubtotal,
    this.configuredSaleRule,
    required this.lines,
    required this.infos,
    required this.warnings,
  });

  final double totalAmount;
  final double totalTaxableAmount;
  final double totalTaxAmount;
  final double totalDiscountAmount;
  final double saleLevelEligibleSubtotal;
  final SalePreviewConfiguredSaleRule? configuredSaleRule;
  final List<SalePreviewLine> lines;
  final List<SalePreviewInfo> infos;
  final List<SalePreviewWarning> warnings;

  double get subtotalAmount => totalAmount - totalTaxAmount;

  bool get hasConfiguredSaleRule => configuredSaleRule != null;
  bool get hasWarnings => warnings.isNotEmpty;
  bool get hasInfos => infos.isNotEmpty;

  @override
  List<Object?> get props => [
    totalAmount,
    totalTaxableAmount,
    totalTaxAmount,
    totalDiscountAmount,
    saleLevelEligibleSubtotal,
    configuredSaleRule,
    lines,
    infos,
    warnings,
  ];
}
