import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount_rule.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount_rule_query.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/repositories/discounts_repository.dart';

class GetDiscountRules {
  const GetDiscountRules(this._repository);

  final DiscountsRepository _repository;

  Future<DiscountRulesResult> call(DiscountRulesQuery query) {
    return _repository.getDiscountRules(query);
  }
}
