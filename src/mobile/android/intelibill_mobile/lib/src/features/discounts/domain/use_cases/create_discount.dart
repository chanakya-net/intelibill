import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/repositories/discount_repository.dart';

class CreateDiscount {
  const CreateDiscount(this._repository);

  final DiscountRepository _repository;

  Future<Discount> call({
    required String name,
    required DiscountType discountType,
    required double discountValue,
    required double? batchPercentage,
  }) {
    return _repository.create(
      name: name,
      discountType: discountType,
      discountValue: discountValue,
      batchPercentage: batchPercentage,
    );
  }
}
