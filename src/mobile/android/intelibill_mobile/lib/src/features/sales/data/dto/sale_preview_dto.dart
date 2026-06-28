import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_preview.dart';

class SalePreviewDiscountDto {
  const SalePreviewDiscountDto({
    required this.type,
    required this.value,
  });

  factory SalePreviewDiscountDto.fromJson(Map<String, dynamic> json) {
    return SalePreviewDiscountDto(
      type: json['type'] as int,
      value: (json['value'] as num).toDouble(),
    );
  }

  factory SalePreviewDiscountDto.fromDomain({
    required int type,
    required double value,
  }) {
    return SalePreviewDiscountDto(type: type, value: value);
  }

  final int type;
  final double value;

  Map<String, dynamic> toJson() {
    return {
      'type': type,
      'value': value,
    };
  }
}

class SalePreviewItemRequestDto {
  const SalePreviewItemRequestDto({
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
    required this.itemDiscount,
    required this.clientLineKey,
    this.hsnCode,
    required this.lineType,
    this.serviceId,
  });

  factory SalePreviewItemRequestDto.fromDomain(PreviewSaleItemRequest item) {
    return SalePreviewItemRequestDto(
      inventoryBatchId: item.inventoryBatchId,
      barcode: item.barcode,
      batchNumber: item.batchNumber,
      itemName: item.itemName,
      quantity: item.quantity,
      costPrice: item.costPrice,
      salesPrice: item.salesPrice,
      mrp: item.mrp,
      taxRatePercent: item.taxRatePercent,
      isPriceIncludingTax: item.isPriceIncludingTax,
      itemDiscount: SalePreviewDiscountDto.fromDomain(
        type: item.itemDiscountType,
        value: item.itemDiscountValue,
      ),
      clientLineKey: item.clientLineKey,
      hsnCode: item.hsnCode,
      lineType: item.lineType,
      serviceId: item.serviceId,
    );
  }

  factory SalePreviewItemRequestDto.fromJson(Map<String, dynamic> json) {
    return SalePreviewItemRequestDto(
      inventoryBatchId: json['inventoryBatchId'] as String,
      barcode: json['barcode'] as String,
      batchNumber: json['batchNumber'] as String,
      itemName: json['itemName'] as String,
      quantity: (json['quantity'] as num).toDouble(),
      costPrice: (json['costPrice'] as num).toDouble(),
      salesPrice: (json['salesPrice'] as num).toDouble(),
      mrp: (json['mrp'] as num).toDouble(),
      taxRatePercent: (json['taxRatePercent'] as num).toDouble(),
      isPriceIncludingTax: json['isPriceIncludingTax'] as bool,
      itemDiscount: SalePreviewDiscountDto.fromJson(
        json['itemDiscount'] as Map<String, dynamic>,
      ),
      clientLineKey: json['clientLineKey'] as String,
      hsnCode: json['hsnCode'] as String?,
      lineType: json['lineType'] as String? ?? 'Goods',
      serviceId: json['serviceId'] as String?,
    );
  }

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
  final SalePreviewDiscountDto itemDiscount;
  final String clientLineKey;
  final String? hsnCode;
  final String lineType;
  final String? serviceId;

  Map<String, dynamic> toJson() {
    return {
      'inventoryBatchId': inventoryBatchId,
      'barcode': barcode,
      'batchNumber': batchNumber,
      'itemName': itemName,
      'quantity': quantity,
      'costPrice': costPrice,
      'salesPrice': salesPrice,
      'mrp': mrp,
      'taxRatePercent': taxRatePercent,
      'isPriceIncludingTax': isPriceIncludingTax,
      'itemDiscount': itemDiscount.toJson(),
      'clientLineKey': clientLineKey,
      if (hsnCode != null) 'hsnCode': hsnCode,
      'lineType': lineType,
      if (serviceId != null) 'serviceId': serviceId,
    };
  }
}

class SalePreviewRequestDto {
  const SalePreviewRequestDto({
    required this.saleDiscount,
    required this.items,
  });

  factory SalePreviewRequestDto.fromDomain(PreviewSaleRequest request) {
    return SalePreviewRequestDto(
      saleDiscount: SalePreviewDiscountDto.fromDomain(
        type: request.saleDiscountType,
        value: request.saleDiscountValue,
      ),
      items: request.items
          .map(SalePreviewItemRequestDto.fromDomain)
          .toList(growable: false),
    );
  }

  final SalePreviewDiscountDto saleDiscount;
  final List<SalePreviewItemRequestDto> items;

  Map<String, dynamic> toJson() {
    return {
      'saleDiscount': saleDiscount.toJson(),
      'items': items.map((item) => item.toJson()).toList(growable: false),
    };
  }
}

class SalePreviewConfiguredSaleRuleDto {
  const SalePreviewConfiguredSaleRuleDto({
    required this.ruleId,
    required this.ruleType,
    required this.percentage,
    this.thresholdAmount,
  });

  factory SalePreviewConfiguredSaleRuleDto.fromJson(
    Map<String, dynamic> json,
  ) {
    return SalePreviewConfiguredSaleRuleDto(
      ruleId: json['ruleId'] as String,
      ruleType: json['ruleType'] as String,
      percentage: (json['percentage'] as num).toDouble(),
      thresholdAmount: (json['thresholdAmount'] as num?)?.toDouble(),
    );
  }

  final String ruleId;
  final String ruleType;
  final double percentage;
  final double? thresholdAmount;
}

class SalePreviewLineDto {
  const SalePreviewLineDto({
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

  factory SalePreviewLineDto.fromJson(Map<String, dynamic> json) {
    return SalePreviewLineDto(
      lineType: json['lineType'] as String? ?? 'Goods',
      itemId: json['itemId'] as String?,
      serviceId: json['serviceId'] as String?,
      barcode: json['barcode'] as String,
      itemName: json['itemName'] as String,
      inventoryBatchId: json['inventoryBatchId'] as String?,
      batchNumber: json['batchNumber'] as String?,
      quantity: (json['quantity'] as num).toDouble(),
      costPrice: (json['costPrice'] as num).toDouble(),
      salesPrice: (json['salesPrice'] as num).toDouble(),
      mrp: (json['mrp'] as num).toDouble(),
      taxRatePercent: (json['taxRatePercent'] as num).toDouble(),
      isPriceIncludingTax: json['isPriceIncludingTax'] as bool,
      preTaxAmountBeforeDiscount: (json['preTaxAmountBeforeDiscount'] as num)
          .toDouble(),
      itemDiscountAmount: (json['itemDiscountAmount'] as num).toDouble(),
      saleDiscountAmount: (json['saleDiscountAmount'] as num).toDouble(),
      taxableAmount: (json['taxableAmount'] as num).toDouble(),
      taxAmount: (json['taxAmount'] as num).toDouble(),
      lineTotalAmount: (json['lineTotalAmount'] as num).toDouble(),
      maxAllowedItemDiscountFlat: (json['maxAllowedItemDiscountFlat'] as num)
          .toDouble(),
      maxAllowedItemDiscountPercent:
          (json['maxAllowedItemDiscountPercent'] as num).toDouble(),
      configuredBatchRuleId: json['configuredBatchRuleId'] as String?,
      configuredBatchRulePercentage:
          (json['configuredBatchRulePercentage'] as num?)?.toDouble(),
      hasClientPriceMismatch: json['hasClientPriceMismatch'] as bool? ?? false,
      clientLineKey: json['clientLineKey'] as String?,
    );
  }

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
}

class SalePreviewInfoDto {
  const SalePreviewInfoDto({
    required this.code,
    required this.message,
  });

  factory SalePreviewInfoDto.fromJson(Map<String, dynamic> json) {
    return SalePreviewInfoDto(
      code: json['code'] as String,
      message: json['message'] as String,
    );
  }

  final String code;
  final String message;
}

class SalePreviewWarningDto {
  const SalePreviewWarningDto({
    required this.code,
    required this.message,
    required this.severity,
    this.inventoryBatchId,
    this.clientLineKey,
  });

  factory SalePreviewWarningDto.fromJson(Map<String, dynamic> json) {
    return SalePreviewWarningDto(
      code: json['code'] as String,
      message: json['message'] as String,
      severity: json['severity'] as String,
      inventoryBatchId: json['inventoryBatchId'] as String?,
      clientLineKey: json['clientLineKey'] as String?,
    );
  }

  final String code;
  final String message;
  final String severity;
  final String? inventoryBatchId;
  final String? clientLineKey;
}

class SalePreviewResponseDto {
  const SalePreviewResponseDto({
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

  factory SalePreviewResponseDto.fromJson(Map<String, dynamic> json) {
    return SalePreviewResponseDto(
      totalAmount: (json['totalAmount'] as num).toDouble(),
      totalTaxableAmount: (json['totalTaxableAmount'] as num).toDouble(),
      totalTaxAmount: (json['totalTaxAmount'] as num).toDouble(),
      totalDiscountAmount: (json['totalDiscountAmount'] as num).toDouble(),
      saleLevelEligibleSubtotal: (json['saleLevelEligibleSubtotal'] as num)
          .toDouble(),
      configuredSaleRule: json['configuredSaleRule'] == null
          ? null
          : SalePreviewConfiguredSaleRuleDto.fromJson(
              json['configuredSaleRule'] as Map<String, dynamic>,
            ),
      lines: ((json['lines'] as List<dynamic>?) ?? const <dynamic>[])
          .cast<Map<String, dynamic>>()
          .map(SalePreviewLineDto.fromJson)
          .toList(growable: false),
      infos: ((json['infos'] as List<dynamic>?) ?? const <dynamic>[])
          .cast<Map<String, dynamic>>()
          .map(SalePreviewInfoDto.fromJson)
          .toList(growable: false),
      warnings: ((json['warnings'] as List<dynamic>?) ?? const <dynamic>[])
          .cast<Map<String, dynamic>>()
          .map(SalePreviewWarningDto.fromJson)
          .toList(growable: false),
    );
  }

  final double totalAmount;
  final double totalTaxableAmount;
  final double totalTaxAmount;
  final double totalDiscountAmount;
  final double saleLevelEligibleSubtotal;
  final SalePreviewConfiguredSaleRuleDto? configuredSaleRule;
  final List<SalePreviewLineDto> lines;
  final List<SalePreviewInfoDto> infos;
  final List<SalePreviewWarningDto> warnings;
}
