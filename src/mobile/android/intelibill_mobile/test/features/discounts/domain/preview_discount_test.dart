import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount_preview.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/repositories/discount_repository.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/use_cases/preview_discount.dart';
import 'package:mocktail/mocktail.dart';

class MockDiscountRepository extends Mock implements DiscountRepository {}

void _setupMocktailFallbacks() {
  registerFallbackValue(DiscountType.fixed);
}

void main() {
  setUpAll(_setupMocktailFallbacks);

  late MockDiscountRepository repo;
  late PreviewDiscount previewDiscount;

  setUp(() {
    repo = MockDiscountRepository();
    previewDiscount = PreviewDiscount(repo);
  });

  group('PreviewDiscount', () {
    test(
      'returns preview with calculated total cost below cost error',
      () async {
        when(
          () => repo.preview(
            name: any(named: 'name'),
            discountType: any(named: 'discountType'),
            discountValue: any(named: 'discountValue'),
            batchPercentage: any(named: 'batchPercentage'),
          ),
        ).thenAnswer(
          (_) async => const DiscountPreview(
            totalCostReduction: 500,
            error: 'below-cost',
            estimatedProfit: -100,
          ),
        );

        final result = await previewDiscount(
          name: 'Clearance',
          discountType: DiscountType.fixed,
          discountValue: 500,
          batchPercentage: null,
        );

        expect(result.error, 'below-cost');
        expect(result.totalCostReduction, 500);
      },
    );

    test('returns preview without error for valid discount', () async {
      when(
        () => repo.preview(
          name: any(named: 'name'),
          discountType: any(named: 'discountType'),
          discountValue: any(named: 'discountValue'),
          batchPercentage: any(named: 'batchPercentage'),
        ),
      ).thenAnswer(
        (_) async => const DiscountPreview(
          totalCostReduction: 100,
          error: null,
          estimatedProfit: 200,
        ),
      );

      final result = await previewDiscount(
        name: 'Summer Sale',
        discountType: DiscountType.percentage,
        discountValue: 10,
        batchPercentage: 0.2,
      );

      expect(result.error, null);
      expect(result.totalCostReduction, 100);
      expect(result.estimatedProfit, 200);
    });
  });
}
