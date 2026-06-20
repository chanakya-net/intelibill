import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount.dart';

void main() {
  group('Discount', () {
    test('creates with required fields', () {
      final discount = Discount(
        discountId: 'disc-1',
        name: 'Summer Sale',
        discountType: DiscountType.fixed,
        discountValue: 50,
        batchPercentage: null,
        isEnabled: true,
        createdAt: DateTime(2024, 1, 1),
      );

      expect(discount.discountId, 'disc-1');
      expect(discount.name, 'Summer Sale');
      expect(discount.discountType, DiscountType.fixed);
      expect(discount.discountValue, 50);
      expect(discount.batchPercentage, null);
      expect(discount.isEnabled, true);
    });

    test('supports equality comparison', () {
      final d1 = Discount(
        discountId: 'disc-1',
        name: 'Summer Sale',
        discountType: DiscountType.percentage,
        discountValue: 10,
        batchPercentage: 0.2,
        isEnabled: true,
        createdAt: DateTime(2024, 1, 1),
      );
      final d2 = Discount(
        discountId: 'disc-1',
        name: 'Summer Sale',
        discountType: DiscountType.percentage,
        discountValue: 10,
        batchPercentage: 0.2,
        isEnabled: true,
        createdAt: DateTime(2024, 1, 1),
      );

      expect(d1, d2);
    });
  });
}
