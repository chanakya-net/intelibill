import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount_rule.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount_rule_draft.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount_rule_query.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/repositories/discounts_repository.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/use_cases/create_discount_rule.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/use_cases/preview_discount_rule.dart';
import 'package:mocktail/mocktail.dart';

class _MockDiscountsRepository extends Mock implements DiscountsRepository {}

void main() {
  setUpAll(() {
    registerFallbackValue(
      const CreateDiscountRuleInput(
        ruleType: DiscountRuleTypeFilter.salePercentage,
        name: 'fallback',
        percentage: 1,
      ),
    );
    registerFallbackValue(
      const PreviewDiscountRuleInput(
        ruleType: DiscountRuleTypeFilter.salePercentage,
        percentage: 1,
      ),
    );
  });

  group('CreateDiscountRule', () {
    test('forwards input to repository', () async {
      final repository = _MockDiscountsRepository();
      const input = CreateDiscountRuleInput(
        ruleType: DiscountRuleTypeFilter.salePercentage,
        name: 'Weekend sale',
        percentage: 12.5,
      );
      final created = DiscountRule(
        discountRuleId: 'rule-1',
        ruleType: input.ruleType,
        name: input.name,
        percentage: input.percentage,
        isActive: true,
        belowCostConfirmed: false,
        createdAt: DateTime.utc(2026, 7),
        status: 'active',
      );
      when(() => repository.createDiscountRule(any())).thenAnswer(
        (_) async => created,
      );

      final result = await CreateDiscountRule(repository)(input);

      expect(result, created);
      verify(() => repository.createDiscountRule(input)).called(1);
    });
  });

  group('PreviewDiscountRule', () {
    test('forwards input to repository', () async {
      final repository = _MockDiscountsRepository();
      const input = PreviewDiscountRuleInput(
        ruleType: DiscountRuleTypeFilter.salePercentage,
        percentage: 15,
      );
      const preview = DiscountRulePreview(
        affectedCount: 2,
        affectedSample: [],
        belowCostSample: [],
        errors: [],
        infos: [],
      );
      when(() => repository.previewDiscountRule(any())).thenAnswer(
        (_) async => preview,
      );

      final result = await PreviewDiscountRule(repository)(input);

      expect(result, preview);
      verify(() => repository.previewDiscountRule(input)).called(1);
    });
  });
}
