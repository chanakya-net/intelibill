import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount_rule.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount_rule_query.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/use_cases/get_discount_rule_detail.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/use_cases/get_discount_rules.dart';
import 'package:intelibill_mobile/src/features/discounts/presentation/controllers/discounts_controller.dart';
import 'package:mocktail/mocktail.dart';

class _MockGetDiscountRules extends Mock implements GetDiscountRules {}

class _MockGetDiscountRuleDetail extends Mock
    implements GetDiscountRuleDetail {}

DiscountRule _rule(String id, String name) {
  return DiscountRule(
    discountRuleId: id,
    ruleType: 'BatchPercentage',
    name: name,
    percentage: 10,
    isActive: true,
    startsAt: DateTime.utc(2026, 5),
    endsAt: DateTime.utc(2026, 5, 20),
    createdAt: DateTime.utc(2026, 4),
    belowCostConfirmed: true,
    status: 'active',
  );
}

void main() {
  late _MockGetDiscountRules getRules;
  late _MockGetDiscountRuleDetail getRuleDetail;

  setUpAll(() {
    registerFallbackValue(const DiscountRulesQuery());
  });

  setUp(() {
    getRules = _MockGetDiscountRules();
    getRuleDetail = _MockGetDiscountRuleDetail();
  });

  ProviderContainer buildContainer() {
    return ProviderContainer(
      overrides: [
        getDiscountRulesProvider.overrideWithValue(getRules),
        getDiscountRuleDetailProvider.overrideWithValue(getRuleDetail),
      ],
    );
  }

  group('DiscountsController', () {
    test('starts in loading state and then loads rules', () async {
      when(() => getRules.call(any())).thenAnswer(
        (_) async => const DiscountRulesResult(
          rules: [],
          totalCount: 0,
          pageNumber: 1,
          pageSize: 20,
        ),
      );

      final container = buildContainer();
      addTearDown(container.dispose);

      expect(container.read(discountsControllerProvider).isLoading, isTrue);
      await Future<void>.delayed(const Duration(milliseconds: 100));
      verify(() => getRules.call(any())).called(greaterThanOrEqualTo(1));
    });

    test('updates filters and reloads list', () async {
      when(() => getRules.call(any())).thenAnswer(
        (_) async => const DiscountRulesResult(
          rules: [],
          totalCount: 0,
          pageNumber: 1,
          pageSize: 20,
        ),
      );

      final container = buildContainer();
      addTearDown(container.dispose);

      await container
          .read(discountsControllerProvider.notifier)
          .updateStatusFilter('active');

      verify(() => getRules(any())).called(greaterThanOrEqualTo(1));
      final state = container.read(discountsControllerProvider);
      expect(state.statusFilter, 'active');
    });

    test('loads selected rule detail and updates selectedRule', () async {
      final rule = _rule('rule-1', 'Batch 1');
      when(() => getRuleDetail.call(ruleId: any(named: 'ruleId'))).thenAnswer(
        (_) async => rule,
      );
      when(() => getRules.call(any())).thenAnswer(
        (_) async => const DiscountRulesResult(
          rules: [],
          totalCount: 0,
          pageNumber: 1,
          pageSize: 20,
        ),
      );

      final container = buildContainer();
      addTearDown(container.dispose);

      await Future<void>.delayed(const Duration(milliseconds: 20));
      await container
          .read(discountsControllerProvider.notifier)
          .selectRule('rule-1');

      final state = container.read(discountsControllerProvider);
      expect(state.selectedRule, isNotNull);
      expect(state.selectedRule!.discountRuleId, 'rule-1');
      expect(state.isDetailLoading, isFalse);
      expect(state.selectedRuleId, 'rule-1');
    });

    test('sets detail failure state on detail exception', () async {
      when(() => getRuleDetail.call(ruleId: any(named: 'ruleId'))).thenThrow(
        AppException(failure: const Failure.network(message: 'offline')),
      );
      when(() => getRules.call(any())).thenAnswer(
        (_) async => const DiscountRulesResult(
          rules: [],
          totalCount: 0,
          pageNumber: 1,
          pageSize: 20,
        ),
      );

      final container = buildContainer();
      addTearDown(container.dispose);

      await Future<void>.delayed(const Duration(milliseconds: 20));
      await container
          .read(discountsControllerProvider.notifier)
          .selectRule('rule-9');

      expect(
        container.read(discountsControllerProvider).detailFailure,
        isA<NetworkFailure>(),
      );
    });
  });
}
