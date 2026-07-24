import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount_rule_draft.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/repositories/discounts_repository.dart';

class PreviewDiscountRule {
  const PreviewDiscountRule(this._repository);

  final DiscountsRepository _repository;

  Future<DiscountRulePreview> call(PreviewDiscountRuleInput input) {
    return _repository.previewDiscountRule(input);
  }
}
