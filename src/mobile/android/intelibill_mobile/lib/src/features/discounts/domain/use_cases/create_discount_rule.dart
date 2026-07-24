import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount_rule.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount_rule_draft.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/repositories/discounts_repository.dart';

class CreateDiscountRule {
  const CreateDiscountRule(this._repository);

  final DiscountsRepository _repository;

  Future<DiscountRule> call(CreateDiscountRuleInput input) {
    return _repository.createDiscountRule(input);
  }
}
