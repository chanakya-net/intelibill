import 'package:equatable/equatable.dart';

class DiscountRule extends Equatable {
  const DiscountRule({
    required this.discountRuleId,
    required this.ruleType,
    required this.name,
    this.description,
    this.inventoryBatchId,
    required this.percentage,
    this.thresholdAmount,
    this.startsAt,
    this.endsAt,
    required this.isActive,
    this.disabledAt,
    this.disabledReason,
    required this.belowCostConfirmed,
    this.belowCostConfirmationReason,
    this.replacesRuleId,
    this.replacedByRuleId,
    required this.createdAt,
    this.updatedAt,
    required this.status,
  });

  final String discountRuleId;
  final String ruleType;
  final String name;
  final String? description;
  final String? inventoryBatchId;
  final double percentage;
  final double? thresholdAmount;
  final DateTime? startsAt;
  final DateTime? endsAt;
  final bool isActive;
  final DateTime? disabledAt;
  final String? disabledReason;
  final bool belowCostConfirmed;
  final String? belowCostConfirmationReason;
  final String? replacesRuleId;
  final String? replacedByRuleId;
  final DateTime createdAt;
  final DateTime? updatedAt;
  final String status;

  @override
  List<Object?> get props => [
    discountRuleId,
    ruleType,
    name,
    description,
    inventoryBatchId,
    percentage,
    thresholdAmount,
    startsAt,
    endsAt,
    isActive,
    disabledAt,
    disabledReason,
    belowCostConfirmed,
    belowCostConfirmationReason,
    replacesRuleId,
    replacedByRuleId,
    createdAt,
    updatedAt,
    status,
  ];
}

class DiscountRulesResult {
  const DiscountRulesResult({
    required this.rules,
    required this.totalCount,
    required this.pageNumber,
    required this.pageSize,
    required this.pageCount,
  });

  final List<DiscountRule> rules;
  final int totalCount;
  final int pageNumber;
  final int pageSize;
  final int pageCount;

  bool get hasMore => pageNumber < pageCount;
}
