import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/repositories/discount_repository.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/use_cases/create_discount.dart';
import 'package:mocktail/mocktail.dart';

void _setupMocktailFallbacks() {
  registerFallbackValue(DiscountType.fixed);
}

class MockDiscountRepository extends Mock implements DiscountRepository {}

void main() {
  setUpAll(_setupMocktailFallbacks);

  late MockDiscountRepository mockRepository;
  late CreateDiscount useCase;

  setUp(() {
    mockRepository = MockDiscountRepository();
    useCase = CreateDiscount(mockRepository);
  });

  test('delegates to repository with correct params', () async {
    final expectedDiscount = Discount(
      discountId: 'disc-1',
      name: 'Test',
      discountType: DiscountType.fixed,
      discountValue: 10,
      batchPercentage: null,
      isEnabled: true,
      createdAt: DateTime(2024),
    );

    when(
      () => mockRepository.create(
        name: any(named: 'name'),
        discountType: any(named: 'discountType'),
        discountValue: any(named: 'discountValue'),
        batchPercentage: any(named: 'batchPercentage'),
      ),
    ).thenAnswer((_) async => expectedDiscount);

    final result = await useCase(
      name: 'Test',
      discountType: DiscountType.fixed,
      discountValue: 10,
      batchPercentage: null,
    );

    expect(result, expectedDiscount);
    verify(
      () => mockRepository.create(
        name: 'Test',
        discountType: DiscountType.fixed,
        discountValue: 10,
        batchPercentage: null,
      ),
    ).called(1);
  });
}
