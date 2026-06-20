import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/features/discounts/data/dto/discount_dto.dart';

void main() {
  group('DiscountDto.toDomain', () {
    test('throws AppException on unknown DiscountType', () {
      const dto = DiscountDto(
        discountId: 'disc-1',
        name: 'Test',
        discountType: 'unknown_type',
        discountValue: 10,
        batchPercentage: null,
        isEnabled: true,
        createdAt: '2024-01-01T00:00:00Z',
      );

      expect(
        () => dto.toDomain(),
        throwsA(isA<AppException>()),
      );
    });

    test('successfully converts known DiscountType', () {
      const dto = DiscountDto(
        discountId: 'disc-1',
        name: 'Test',
        discountType: 'fixed',
        discountValue: 10,
        batchPercentage: null,
        isEnabled: true,
        createdAt: '2024-01-01T00:00:00Z',
      );

      final domain = dto.toDomain();
      expect(domain.discountId, 'disc-1');
      expect(domain.name, 'Test');
      expect(domain.discountValue, 10);
    });
  });
}
