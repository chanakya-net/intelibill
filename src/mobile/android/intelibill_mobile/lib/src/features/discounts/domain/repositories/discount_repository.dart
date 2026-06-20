import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount_preview.dart';

abstract class DiscountRepository {
  Future<DiscountPreview> preview({
    required String name,
    required DiscountType discountType,
    required double discountValue,
    required double? batchPercentage,
  });

  Future<Discount> create({
    required String name,
    required DiscountType discountType,
    required double discountValue,
    required double? batchPercentage,
  });

  Future<Discount> replace({
    required String discountId,
    required String name,
    required DiscountType discountType,
    required double discountValue,
    required double? batchPercentage,
  });

  Future<void> disable({required String discountId});

  Future<List<Discount>> getAll();
}
