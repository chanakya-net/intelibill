import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/repositories/discount_repository.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/use_cases/disable_discount.dart';
import 'package:mocktail/mocktail.dart';

class MockDiscountRepository extends Mock implements DiscountRepository {}

void main() {
  late MockDiscountRepository mockRepository;
  late DisableDiscount useCase;

  setUp(() {
    mockRepository = MockDiscountRepository();
    useCase = DisableDiscount(mockRepository);
  });

  test('delegates to repository with correct discountId', () async {
    when(
      () => mockRepository.disable(discountId: any(named: 'discountId')),
    ).thenAnswer((_) async => {});

    await useCase(discountId: 'disc-1');

    verify(
      () => mockRepository.disable(discountId: 'disc-1'),
    ).called(1);
  });
}
