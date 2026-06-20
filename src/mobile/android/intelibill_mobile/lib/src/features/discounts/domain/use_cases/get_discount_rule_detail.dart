import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount_rule.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/repositories/discounts_repository.dart';

class GetDiscountRuleDetail {
  const GetDiscountRuleDetail(this._repository);

  final DiscountsRepository _repository;

  Future<DiscountRule> call({required String ruleId}) {
    return _repository.getDiscountRule(ruleId: ruleId);
  }
}
