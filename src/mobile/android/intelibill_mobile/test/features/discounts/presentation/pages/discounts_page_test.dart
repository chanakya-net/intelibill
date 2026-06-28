import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/app/theme/app_theme.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount_rule.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount_rule_query.dart';
import 'package:intelibill_mobile/src/features/discounts/presentation/controllers/discounts_controller.dart';
import 'package:intelibill_mobile/src/features/discounts/presentation/pages/discounts_page.dart';
import 'package:intelibill_mobile/src/features/discounts/presentation/widgets/discount_rule_detail_sheet.dart';

class _StubDiscountsController extends DiscountsController {
  _StubDiscountsController(this._state);

  final DiscountsState _state;

  @override
  DiscountsState build() => _state;

  @override
  Future<void> selectRule(String ruleId) async {}
}

DiscountsState _loadingState() {
  return const DiscountsState(query: DiscountRulesQuery(), isLoading: true);
}

DiscountsState _emptyState() {
  return const DiscountsState(
    query: DiscountRulesQuery(),
  );
}

DiscountsState _loadedState() {
  return DiscountsState(
    query: const DiscountRulesQuery(),
    rules: [
      DiscountRule(
        discountRuleId: 'rule-1',
        ruleType: 'BatchPercentage',
        name: 'Summer Batch',
        description: '20% off',
        inventoryBatchId: 'batch-1',
        startsAt: DateTime.utc(2026, 5),
        endsAt: DateTime.utc(2026, 5, 10),
        isActive: true,
        belowCostConfirmed: false,
        createdAt: DateTime.utc(2026, 4),
        status: 'active',
      ),
    ],
    totalCount: 1,
  );
}

Widget _buildApp(DiscountsState state) {
  return ProviderScope(
    overrides: [
      discountsControllerProvider.overrideWith(
        () => _StubDiscountsController(state),
      ),
    ],
    child: MaterialApp(
      theme: AppTheme.lightTheme,
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      home: const DiscountsPage(),
    ),
  );
}

void main() {
  group('DiscountsPage', () {
    testWidgets('shows loading indicator', (tester) async {
      await tester.pumpWidget(_buildApp(_loadingState()));
      expect(find.byType(CircularProgressIndicator), findsOneWidget);
    });

    testWidgets('shows empty state when no rules returned', (tester) async {
      await tester.pumpWidget(_buildApp(_emptyState()));
      await tester.pumpAndSettle();
      expect(find.text('No discount rules found'), findsOneWidget);
    });

    testWidgets('shows rule rows', (tester) async {
      await tester.pumpWidget(_buildApp(_loadedState()));
      await tester.pumpAndSettle();

      expect(find.text('Summer Batch'), findsOneWidget);
      expect(find.text('Active'), findsAtLeastNWidgets(2));
      expect(find.text('Showing 1 of 1'), findsOneWidget);
    });

    testWidgets(
      'list card omits percentage row when list item has no percentage',
      (tester) async {
        await tester.pumpWidget(_buildApp(_loadedState()));
        await tester.pumpAndSettle();

        expect(find.text('Discount:'), findsNothing);
      },
    );
  });

  group('DiscountRuleDetailSheet', () {
    testWidgets('shows disabled failure state', (tester) async {
      final ruleState = _loadedState().copyWith(
        selectedRule: _loadedState().rules.first,
      );
      await tester.pumpWidget(
        ProviderScope(
          overrides: [
            discountsControllerProvider.overrideWith(
              () => _StubDiscountsController(ruleState),
            ),
          ],
          child: MaterialApp(
            theme: AppTheme.lightTheme,
            localizationsDelegates: AppLocalizations.localizationsDelegates,
            supportedLocales: AppLocalizations.supportedLocales,
            home: const Scaffold(body: DiscountRuleDetailSheet()),
          ),
        ),
      );

      await tester.pump();
      expect(find.text('Discount rule details'), findsOneWidget);
      expect(find.text('Summer Batch'), findsOneWidget);
      expect(find.text('Type:'), findsOneWidget);
    });
  });
}
