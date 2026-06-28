import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount_preview.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/repositories/discount_repository.dart';

class PreviewDiscount {
  const PreviewDiscount(this._repository);

  final DiscountRepository _repository;

  Future<DiscountPreview> call({
    required String name,
    required DiscountType discountType,
    required double discountValue,
    required double? batchPercentage,
  }) {
    return _repository.preview(
      name: name,
      discountType: discountType,
      discountValue: discountValue,
      batchPercentage: batchPercentage,
    );
  }
}
