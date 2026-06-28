class SaleReturnPreviewWarningDto {
  const SaleReturnPreviewWarningDto({
    required this.code,
    required this.message,
    required this.severity,
  });

  factory SaleReturnPreviewWarningDto.fromJson(Map<String, dynamic> json) {
    return SaleReturnPreviewWarningDto(
      code: json['code'] as String,
      message: json['message'] as String,
      severity: json['severity'] as String,
    );
  }

  final String code;
  final String message;
  final String severity;
}

class SaleReturnPreviewLineFinancialDto {
  const SaleReturnPreviewLineFinancialDto({
    required this.originalCostPrice,
    required this.originalSalesPrice,
    required this.originalTaxRatePercent,
    required this.originalIsPriceIncludingTax,
    required this.maxRefundAmount,
    required this.approvedRefundAmount,
    required this.taxableAmount,
    required this.taxAmount,
  });

  factory SaleReturnPreviewLineFinancialDto.fromJson(
    Map<String, dynamic> json,
  ) {
    return SaleReturnPreviewLineFinancialDto(
      originalCostPrice: (json['originalCostPrice'] as num).toDouble(),
      originalSalesPrice: (json['originalSalesPrice'] as num).toDouble(),
      originalTaxRatePercent: (json['originalTaxRatePercent'] as num)
          .toDouble(),
      originalIsPriceIncludingTax: json['originalIsPriceIncludingTax'] as bool,
      maxRefundAmount: (json['maxRefundAmount'] as num).toDouble(),
      approvedRefundAmount: (json['approvedRefundAmount'] as num).toDouble(),
      taxableAmount: (json['taxableAmount'] as num).toDouble(),
      taxAmount: (json['taxAmount'] as num).toDouble(),
    );
  }

  final double originalCostPrice;
  final double originalSalesPrice;
  final double originalTaxRatePercent;
  final bool originalIsPriceIncludingTax;
  final double maxRefundAmount;
  final double approvedRefundAmount;
  final double taxableAmount;
  final double taxAmount;
}

class SaleReturnPreviewLineDto {
  const SaleReturnPreviewLineDto({
    required this.saleItemId,
    this.itemId,
    this.inventoryBatchId,
    required this.requestedQuantity,
    required this.returnedQuantity,
    required this.returnableQuantity,
    required this.condition,
    required this.willRestock,
    this.financial,
  });

  factory SaleReturnPreviewLineDto.fromJson(Map<String, dynamic> json) {
    return SaleReturnPreviewLineDto(
      saleItemId: json['saleItemId'] as String,
      itemId: json['itemId'] as String?,
      inventoryBatchId: json['inventoryBatchId'] as String?,
      requestedQuantity: (json['requestedQuantity'] as num).toDouble(),
      returnedQuantity: (json['returnedQuantity'] as num).toDouble(),
      returnableQuantity: (json['returnableQuantity'] as num).toDouble(),
      condition: json['condition'] as int?,
      willRestock: json['willRestock'] as bool,
      financial: json['financial'] == null
          ? null
          : SaleReturnPreviewLineFinancialDto.fromJson(
              json['financial'] as Map<String, dynamic>,
            ),
    );
  }

  final String saleItemId;
  final String? itemId;
  final String? inventoryBatchId;
  final double requestedQuantity;
  final double returnedQuantity;
  final double returnableQuantity;
  final int? condition;
  final bool willRestock;
  final SaleReturnPreviewLineFinancialDto? financial;
}

class SaleReturnPreviewFinancialDto {
  const SaleReturnPreviewFinancialDto({
    required this.totalRefundAmount,
    required this.dueReductionAmount,
    required this.payoutAmount,
    required this.totalTaxableAmount,
    required this.totalTaxAmount,
    this.customerBalanceBefore,
    this.customerBalanceAfter,
  });

  factory SaleReturnPreviewFinancialDto.fromJson(Map<String, dynamic> json) {
    return SaleReturnPreviewFinancialDto(
      totalRefundAmount: (json['totalRefundAmount'] as num).toDouble(),
      dueReductionAmount: (json['dueReductionAmount'] as num).toDouble(),
      payoutAmount: (json['payoutAmount'] as num).toDouble(),
      totalTaxableAmount: (json['totalTaxableAmount'] as num).toDouble(),
      totalTaxAmount: (json['totalTaxAmount'] as num).toDouble(),
      customerBalanceBefore: (json['customerBalanceBefore'] as num?)
          ?.toDouble(),
      customerBalanceAfter: (json['customerBalanceAfter'] as num?)?.toDouble(),
    );
  }

  final double totalRefundAmount;
  final double dueReductionAmount;
  final double payoutAmount;
  final double totalTaxableAmount;
  final double totalTaxAmount;
  final double? customerBalanceBefore;
  final double? customerBalanceAfter;
}

class SaleReturnPreviewResponseDto {
  const SaleReturnPreviewResponseDto({
    required this.saleId,
    required this.hasFinancialAccess,
    required this.lines,
    this.financial,
    required this.warnings,
  });

  factory SaleReturnPreviewResponseDto.fromJson(Map<String, dynamic> json) {
    return SaleReturnPreviewResponseDto(
      saleId: json['saleId'] as String,
      hasFinancialAccess: json['hasFinancialAccess'] as bool,
      lines: ((json['lines'] as List<dynamic>?) ?? const <dynamic>[])
          .cast<Map<String, dynamic>>()
          .map(SaleReturnPreviewLineDto.fromJson)
          .toList(growable: false),
      financial: json['financial'] == null
          ? null
          : SaleReturnPreviewFinancialDto.fromJson(
              json['financial'] as Map<String, dynamic>,
            ),
      warnings: ((json['warnings'] as List<dynamic>?) ?? const <dynamic>[])
          .cast<Map<String, dynamic>>()
          .map(SaleReturnPreviewWarningDto.fromJson)
          .toList(growable: false),
    );
  }

  final String saleId;
  final bool hasFinancialAccess;
  final List<SaleReturnPreviewLineDto> lines;
  final SaleReturnPreviewFinancialDto? financial;
  final List<SaleReturnPreviewWarningDto> warnings;
}
