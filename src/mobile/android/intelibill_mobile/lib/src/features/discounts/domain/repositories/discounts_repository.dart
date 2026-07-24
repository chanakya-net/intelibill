import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount_rule.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount_rule_draft.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount_rule_query.dart';

interface class DiscountsRepository {
  Future<DiscountRulesResult> getDiscountRules(DiscountRulesQuery query) {
    throw UnimplementedError();
  }

  Future<DiscountRule> getDiscountRule({required String ruleId}) {
    throw UnimplementedError();
  }

  Future<DiscountRulePreview> previewDiscountRule(
    PreviewDiscountRuleInput input,
  ) {
    throw UnimplementedError();
  }

  Future<DiscountRule> createDiscountRule(CreateDiscountRuleInput input) {
    throw UnimplementedError();
  }
}
