class CreateDiscountRuleRequestDto {
  const CreateDiscountRuleRequestDto({
    required this.ruleType,
    required this.name,
    this.description,
    this.inventoryBatchId,
    required this.percentage,
    this.thresholdAmount,
    this.startsAt,
    this.endsAt,
    required this.belowCostConfirmed,
    this.belowCostConfirmationReason,
  });

  final String ruleType;
  final String name;
  final String? description;
  final String? inventoryBatchId;
  final double percentage;
  final double? thresholdAmount;
  final DateTime? startsAt;
  final DateTime? endsAt;
  final bool belowCostConfirmed;
  final String? belowCostConfirmationReason;

  Map<String, dynamic> toJson() => {
    'ruleType': ruleType,
    'name': name,
    'description': description,
    'inventoryBatchId': inventoryBatchId,
    'percentage': percentage,
    'thresholdAmount': thresholdAmount,
    'startsAt': startsAt?.toUtc().toIso8601String(),
    'endsAt': endsAt?.toUtc().toIso8601String(),
    'belowCostConfirmed': belowCostConfirmed,
    'belowCostConfirmationReason': belowCostConfirmationReason,
  };
}

class PreviewDiscountRuleRequestDto {
  const PreviewDiscountRuleRequestDto({
    required this.ruleType,
    required this.percentage,
    this.thresholdAmount,
    this.inventoryBatchId,
    this.startsAt,
    this.endsAt,
    required this.belowCostConfirmed,
  });

  final String ruleType;
  final double percentage;
  final double? thresholdAmount;
  final String? inventoryBatchId;
  final DateTime? startsAt;
  final DateTime? endsAt;
  final bool belowCostConfirmed;

  Map<String, dynamic> toJson() => {
    'ruleType': ruleType,
    'percentage': percentage,
    'thresholdAmount': thresholdAmount,
    'inventoryBatchId': inventoryBatchId,
    'startsAt': startsAt?.toUtc().toIso8601String(),
    'endsAt': endsAt?.toUtc().toIso8601String(),
    'belowCostConfirmed': belowCostConfirmed,
  };
}

class DiscountRulePreviewDto {
  const DiscountRulePreviewDto({
    required this.affectedCount,
    required this.affectedSample,
    required this.belowCostSample,
    this.safeMaxPercentage,
    required this.errors,
    required this.infos,
  });

  factory DiscountRulePreviewDto.fromJson(Map<String, dynamic> json) {
    return DiscountRulePreviewDto(
      affectedCount: (json['affectedCount'] as num?)?.toInt() ?? 0,
      affectedSample: _mapBatches(json['affectedSample']),
      belowCostSample: _mapBatches(json['belowCostSample']),
      safeMaxPercentage: (json['safeMaxPercentage'] as num?)?.toDouble(),
      errors: _mapMessages(json['errors']),
      infos: _mapMessages(json['infos']),
    );
  }

  final int affectedCount;
  final List<DiscountRulePreviewBatchDto> affectedSample;
  final List<DiscountRulePreviewBatchDto> belowCostSample;
  final double? safeMaxPercentage;
  final List<DiscountRulePreviewMessageDto> errors;
  final List<DiscountRulePreviewMessageDto> infos;

  static List<DiscountRulePreviewBatchDto> _mapBatches(Object? raw) {
    if (raw is! List) return const [];
    return raw
        .whereType<Map<String, dynamic>>()
        .map(DiscountRulePreviewBatchDto.fromJson)
        .toList();
  }

  static List<DiscountRulePreviewMessageDto> _mapMessages(Object? raw) {
    if (raw is! List) return const [];
    return raw
        .whereType<Map<String, dynamic>>()
        .map(DiscountRulePreviewMessageDto.fromJson)
        .toList();
  }
}

class DiscountRulePreviewBatchDto {
  const DiscountRulePreviewBatchDto({
    required this.batchId,
    required this.itemName,
    required this.batchNumber,
    required this.salesPrice,
    required this.costPrice,
    required this.discountedPrice,
  });

  factory DiscountRulePreviewBatchDto.fromJson(Map<String, dynamic> json) {
    return DiscountRulePreviewBatchDto(
      batchId: json['batchId']?.toString() ?? '',
      itemName: json['itemName'] as String? ?? '',
      batchNumber: json['batchNumber'] as String? ?? '',
      salesPrice: (json['salesPrice'] as num?)?.toDouble() ?? 0,
      costPrice: (json['costPrice'] as num?)?.toDouble() ?? 0,
      discountedPrice: (json['discountedPrice'] as num?)?.toDouble() ?? 0,
    );
  }

  final String batchId;
  final String itemName;
  final String batchNumber;
  final double salesPrice;
  final double costPrice;
  final double discountedPrice;
}

class DiscountRulePreviewMessageDto {
  const DiscountRulePreviewMessageDto({
    required this.code,
    required this.message,
  });

  factory DiscountRulePreviewMessageDto.fromJson(Map<String, dynamic> json) {
    return DiscountRulePreviewMessageDto(
      code: json['code'] as String? ?? '',
      message: json['message'] as String? ?? '',
    );
  }

  final String code;
  final String message;
}
