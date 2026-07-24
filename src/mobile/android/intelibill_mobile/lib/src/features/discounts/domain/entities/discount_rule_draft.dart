import 'package:equatable/equatable.dart';

class CreateDiscountRuleInput extends Equatable {
  const CreateDiscountRuleInput({
    required this.ruleType,
    required this.name,
    this.description,
    this.inventoryBatchId,
    required this.percentage,
    this.thresholdAmount,
    this.startsAt,
    this.endsAt,
    this.belowCostConfirmed = false,
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

  @override
  List<Object?> get props => [
    ruleType,
    name,
    description,
    inventoryBatchId,
    percentage,
    thresholdAmount,
    startsAt,
    endsAt,
    belowCostConfirmed,
    belowCostConfirmationReason,
  ];
}

class PreviewDiscountRuleInput extends Equatable {
  const PreviewDiscountRuleInput({
    required this.ruleType,
    required this.percentage,
    this.thresholdAmount,
    this.inventoryBatchId,
    this.startsAt,
    this.endsAt,
    this.belowCostConfirmed = false,
  });

  final String ruleType;
  final double percentage;
  final double? thresholdAmount;
  final String? inventoryBatchId;
  final DateTime? startsAt;
  final DateTime? endsAt;
  final bool belowCostConfirmed;

  @override
  List<Object?> get props => [
    ruleType,
    percentage,
    thresholdAmount,
    inventoryBatchId,
    startsAt,
    endsAt,
    belowCostConfirmed,
  ];
}

class DiscountRulePreview extends Equatable {
  const DiscountRulePreview({
    required this.affectedCount,
    required this.affectedSample,
    required this.belowCostSample,
    this.safeMaxPercentage,
    required this.errors,
    required this.infos,
  });

  final int affectedCount;
  final List<DiscountRulePreviewBatch> affectedSample;
  final List<DiscountRulePreviewBatch> belowCostSample;
  final double? safeMaxPercentage;
  final List<DiscountRulePreviewMessage> errors;
  final List<DiscountRulePreviewMessage> infos;

  bool get hasErrors => errors.isNotEmpty;
  bool get needsBelowCostConfirmation => belowCostSample.isNotEmpty;

  @override
  List<Object?> get props => [
    affectedCount,
    affectedSample,
    belowCostSample,
    safeMaxPercentage,
    errors,
    infos,
  ];
}

class DiscountRulePreviewBatch extends Equatable {
  const DiscountRulePreviewBatch({
    required this.batchId,
    required this.itemName,
    required this.batchNumber,
    required this.salesPrice,
    required this.costPrice,
    required this.discountedPrice,
  });

  final String batchId;
  final String itemName;
  final String batchNumber;
  final double salesPrice;
  final double costPrice;
  final double discountedPrice;

  @override
  List<Object?> get props => [
    batchId,
    itemName,
    batchNumber,
    salesPrice,
    costPrice,
    discountedPrice,
  ];
}

class DiscountRulePreviewMessage extends Equatable {
  const DiscountRulePreviewMessage({
    required this.code,
    required this.message,
  });

  final String code;
  final String message;

  @override
  List<Object?> get props => [code, message];
}
