import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/discounts/data/dto/discount_rule_dto.dart';
import 'package:intelibill_mobile/src/features/discounts/data/dto/discount_rule_list_item_dto.dart';
import 'package:intelibill_mobile/src/features/discounts/data/mappers/discount_rule_mapper.dart';

void main() {
  test('maps list item DTO to domain and derives active status', () {
    final clock = DateTime.utc(2026, 5, 12, 12);
    final dto = DiscountRuleListItemDto(
      id: 'rule-1',
      ruleType: 'BatchPercentage',
      name: 'Summer batch discount',
      isActive: true,
      startsAt: DateTime.utc(2026, 5, 11, 10),
      endsAt: DateTime.utc(2026, 5, 20, 23),
      createdAt: DateTime.utc(2026, 5, 10, 6),
    );

    final domain = DiscountRuleMapper.toDomainFromListItem(dto, clock: clock);

    expect(domain.discountRuleId, 'rule-1');
    expect(domain.percentage, 0);
    expect(domain.status, 'active');
    expect(domain.thresholdAmount, isNull);
  });

  test('maps detail DTO and derives upcoming and expired statuses', () {
    final now = DateTime.utc(2026, 5, 12, 12);
    final upcoming = DiscountRuleMapper.toDomain(
      DiscountRuleDto(
        id: 'rule-2',
        ruleType: 'SalePercentage',
        name: 'Holiday sale',
        percentage: 10,
        isActive: true,
        startsAt: DateTime.utc(2026, 6, 1, 1),
        endsAt: DateTime.utc(2026, 6, 10, 1),
        createdAt: DateTime.utc(2026, 5, 10, 6),
        belowCostConfirmed: false,
      ),
      clock: now,
    );

    final expired = DiscountRuleMapper.toDomain(
      DiscountRuleDto(
        id: 'rule-3',
        ruleType: 'SaleThresholdPercentage',
        name: 'Clearance',
        percentage: 8,
        isActive: true,
        startsAt: DateTime.utc(2026, 4, 1, 1),
        endsAt: DateTime.utc(2026, 4, 2, 1),
        createdAt: DateTime.utc(2026, 4, 1, 1),
        belowCostConfirmed: false,
      ),
      clock: now,
    );

    expect(upcoming.status, 'upcoming');
    expect(expired.status, 'expired');
    expect(expired.thresholdAmount, isNull);
    expect(expired.belowCostConfirmed, isFalse);
  });

  test('maps detail DTO from JSON and keeps nullable fields', () {
    final json = {
      'id': 'rule-4',
      'ruleType': 'SalePercentage',
      'name': 'New year',
      'percentage': 12.5,
      'isActive': false,
      'belowCostConfirmed': true,
      'belowCostConfirmationReason': 'checked',
      'createdAt': '2026-01-01T00:00:00.000Z',
      'startsAt': null,
      'endsAt': null,
    };

    final dto = DiscountRuleDto.fromJson(json);

    final domain = DiscountRuleMapper.toDomain(
      dto,
      clock: DateTime.utc(2026, 2, 1),
    );

    expect(domain.description, isNull);
    expect(domain.disabledReason, isNull);
    expect(domain.status, 'disabled');
    expect(domain.belowCostConfirmationReason, 'checked');
  });
}
