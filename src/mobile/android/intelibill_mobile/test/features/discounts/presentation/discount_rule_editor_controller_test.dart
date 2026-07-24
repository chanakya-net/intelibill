import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount_rule.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount_rule_draft.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount_rule_query.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/use_cases/create_discount_rule.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/use_cases/preview_discount_rule.dart';
import 'package:intelibill_mobile/src/features/discounts/presentation/controllers/discount_rule_editor_controller.dart';
import 'package:intelibill_mobile/src/features/discounts/presentation/controllers/discounts_controller.dart';
import 'package:mocktail/mocktail.dart';

class _MockCreateDiscountRule extends Mock implements CreateDiscountRule {}

class _MockPreviewDiscountRule extends Mock implements PreviewDiscountRule {}

class _StubDiscountsController extends DiscountsController {
  int refreshCalls = 0;

  @override
  DiscountsState build() => const DiscountsState(query: DiscountRulesQuery());

  @override
  Future<void> refresh() async {
    refreshCalls += 1;
  }
}

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

  group('DiscountRuleEditorController', () {
    test('create blocks when below-cost confirmation missing', () async {
      final previewUseCase = _MockPreviewDiscountRule();
      final createUseCase = _MockCreateDiscountRule();
      final discountsController = _StubDiscountsController();

      when(() => previewUseCase(any())).thenAnswer(
        (_) async => const DiscountRulePreview(
          affectedCount: 1,
          affectedSample: [],
          belowCostSample: [
            DiscountRulePreviewBatch(
              batchId: 'batch-1',
              itemName: 'Soap',
              batchNumber: 'B-1',
              salesPrice: 100,
              costPrice: 90,
              discountedPrice: 80,
            ),
          ],
          errors: [],
          infos: [],
        ),
      );

      final container = ProviderContainer(
        overrides: [
          previewDiscountRuleProvider.overrideWithValue(previewUseCase),
          createDiscountRuleProvider.overrideWithValue(createUseCase),
          discountsControllerProvider.overrideWith(() => discountsController),
        ],
      );
      addTearDown(container.dispose);

      final success = await container
          .read(discountRuleEditorControllerProvider.notifier)
          .create(
            const CreateDiscountRuleInput(
              ruleType: DiscountRuleTypeFilter.salePercentage,
              name: 'Deep cut',
              percentage: 20,
            ),
          );

      expect(success, isFalse);
      expect(
        container
            .read(discountRuleEditorControllerProvider)
            .localValidationMessage,
        'belowCostConfirmationRequired',
      );
      verifyNever(() => createUseCase(any()));
    });

    test('create succeeds after preview and refreshes list', () async {
      final previewUseCase = _MockPreviewDiscountRule();
      final createUseCase = _MockCreateDiscountRule();
      final discountsController = _StubDiscountsController();

      when(() => previewUseCase(any())).thenAnswer(
        (_) async => const DiscountRulePreview(
          affectedCount: 3,
          affectedSample: [],
          belowCostSample: [],
          errors: [],
          infos: [],
        ),
      );
      when(() => createUseCase(any())).thenAnswer(
        (_) async => DiscountRule(
          discountRuleId: 'rule-9',
          ruleType: DiscountRuleTypeFilter.salePercentage,
          name: 'Weekend',
          percentage: 10,
          isActive: true,
          belowCostConfirmed: false,
          createdAt: DateTime.utc(2026, 7),
          status: 'active',
        ),
      );

      final container = ProviderContainer(
        overrides: [
          previewDiscountRuleProvider.overrideWithValue(previewUseCase),
          createDiscountRuleProvider.overrideWithValue(createUseCase),
          discountsControllerProvider.overrideWith(() => discountsController),
        ],
      );
      addTearDown(container.dispose);

      final success = await container
          .read(discountRuleEditorControllerProvider.notifier)
          .create(
            const CreateDiscountRuleInput(
              ruleType: DiscountRuleTypeFilter.salePercentage,
              name: 'Weekend',
              percentage: 10,
            ),
          );

      expect(success, isTrue);
      expect(discountsController.refreshCalls, 1);
      verify(() => createUseCase(any())).called(1);
    });
  });
}
