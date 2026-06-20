import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount_rule.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount_rule_query.dart';

interface class DiscountsRepository {
  Future<DiscountRulesResult> getDiscountRules(DiscountRulesQuery query) {
    throw UnimplementedError();
  }

  Future<DiscountRule> getDiscountRule({required String ruleId}) {
    throw UnimplementedError();
  }
}
