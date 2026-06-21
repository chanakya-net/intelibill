import 'package:intelibill_mobile/src/features/discounts/data/dto/discount_rule_dto.dart';
import 'package:intelibill_mobile/src/features/discounts/data/dto/discount_rule_list_item_dto.dart';
import 'package:intelibill_mobile/src/features/discounts/data/dto/discount_rules_response_dto.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount_rule.dart';

class DiscountRuleMapper {
  const DiscountRuleMapper._();

  static DiscountRule toDomain(
    DiscountRuleDto dto, {
    DateTime? clock,
  }) {
    final now = clock ?? DateTime.now();
    return DiscountRule(
      discountRuleId: dto.id,
      ruleType: dto.ruleType,
      name: dto.name,
      description: dto.description,
      inventoryBatchId: dto.inventoryBatchId,
      percentage: dto.percentage,
      thresholdAmount: dto.thresholdAmount,
      startsAt: dto.startsAt?.toLocal(),
      endsAt: dto.endsAt?.toLocal(),
      isActive: dto.isActive,
      disabledAt: dto.disabledAt?.toLocal(),
      disabledReason: dto.disabledReason,
      belowCostConfirmed: dto.belowCostConfirmed,
      belowCostConfirmationReason: dto.belowCostConfirmationReason,
      replacesRuleId: dto.replacesRuleId,
      replacedByRuleId: dto.replacedByRuleId,
      createdAt: dto.createdAt.toLocal(),
      updatedAt: dto.updatedAt?.toLocal(),
      status: _deriveStatus(
        dto.isActive,
        dto.startsAt?.toLocal(),
        dto.endsAt?.toLocal(),
        now,
      ),
    );
  }

  static DiscountRule toDomainFromListItem(
    DiscountRuleListItemDto dto, {
    DateTime? clock,
  }) {
    final now = clock ?? DateTime.now();
    return DiscountRule(
      discountRuleId: dto.id,
      ruleType: dto.ruleType,
      name: dto.name,
      percentage: null,
      isActive: dto.isActive,
      startsAt: dto.startsAt?.toLocal(),
      endsAt: dto.endsAt?.toLocal(),
      createdAt: dto.createdAt.toLocal(),
      description: null,
      inventoryBatchId: null,
      thresholdAmount: null,
      belowCostConfirmed: false,
      belowCostConfirmationReason: null,
      disabledAt: null,
      disabledReason: null,
      replacesRuleId: null,
      replacedByRuleId: null,
      updatedAt: null,
      status: _deriveStatus(
        dto.isActive,
        dto.startsAt?.toLocal(),
        dto.endsAt?.toLocal(),
        now,
      ),
    );
  }

  static DiscountRulesResult toResult(DiscountRulesResponseDto dto) {
    return DiscountRulesResult(
      rules: dto.items.map(toDomainFromListItem).toList(),
      totalCount: dto.totalCount,
      pageNumber: dto.pageNumber,
      pageSize: dto.pageSize,
    );
  }

  static String _deriveStatus(
    bool isActive,
    DateTime? startsAt,
    DateTime? endsAt,
    DateTime now,
  ) {
    if (!isActive) return DiscountRuleStatus.disabled;
    if (startsAt != null && startsAt.isAfter(now))
      return DiscountRuleStatus.upcoming;
    if (endsAt != null && !endsAt.isAfter(now))
      return DiscountRuleStatus.expired;
    return DiscountRuleStatus.active;
  }
}

class DiscountRuleStatus {
  static const String active = 'active';
  static const String upcoming = 'upcoming';
  static const String expired = 'expired';
  static const String disabled = 'disabled';
}
