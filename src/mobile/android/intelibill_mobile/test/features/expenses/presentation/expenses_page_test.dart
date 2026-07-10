import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/app/theme/app_theme.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expense_list_item.dart';
import 'package:intelibill_mobile/src/features/expenses/presentation/controllers/expenses_controller.dart';
import 'package:intelibill_mobile/src/features/expenses/presentation/pages/expenses_page.dart';

class _StubExpensesController extends ExpensesController {
  _StubExpensesController(
    this._state, {
    this.onRefresh,
    this.refreshedState,
  });

  final ExpensesState _state;
  final Future<void> Function()? onRefresh;
  final ExpensesState? refreshedState;

  @override
  ExpensesState build() => _state;

  @override
  Future<void> refresh() async {
    await onRefresh?.call();
    if (refreshedState != null) state = refreshedState!;
  }
}

Widget _buildApp(
  ExpensesState state, {
  Future<void> Function()? onRefresh,
  ExpensesState? refreshedState,
  Locale locale = const Locale('en', 'IN'),
}) {
  return ProviderScope(
    overrides: [
      expensesControllerProvider.overrideWith(
        () => _StubExpensesController(
          state,
          onRefresh: onRefresh,
          refreshedState: refreshedState,
        ),
      ),
    ],
    child: MaterialApp(
      locale: locale,
      theme: AppTheme.lightTheme,
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      home: const ExpensesPage(),
    ),
  );
}

void main() {
  testWidgets('shows loading before first page arrives', (tester) async {
    await tester.pumpWidget(
      _buildApp(const ExpensesState(isLoading: true)),
    );

    expect(find.byType(CircularProgressIndicator), findsOneWidget);
    expect(find.text('Rent'), findsNothing);
  });

  testWidgets('shows localized empty state with pull-to-refresh', (
    tester,
  ) async {
    await tester.pumpWidget(_buildApp(const ExpensesState()));
    await tester.pumpAndSettle();

    expect(find.text('No expenses found'), findsOneWidget);
    expect(find.byType(RefreshIndicator), findsOneWidget);
  });

  testWidgets('shows translated empty state for a supported locale', (
    tester,
  ) async {
    await tester.pumpWidget(
      _buildApp(const ExpensesState(), locale: const Locale('hi', 'IN')),
    );
    await tester.pumpAndSettle();

    expect(find.text('कोई खर्च नहीं मिला'), findsOneWidget);
  });

  testWidgets('shows initial failure details and retries', (tester) async {
    var refreshCount = 0;
    await tester.pumpWidget(
      _buildApp(
        const ExpensesState(
          listFailure: Failure.network(message: 'offline'),
        ),
        onRefresh: () async => refreshCount += 1,
      ),
    );

    expect(find.text('Unable to load expenses'), findsOneWidget);
    expect(
      find.text('Unable to connect. Please check your network.'),
      findsOneWidget,
    );
    await tester.tap(find.text('Retry'));
    await tester.pump();
    expect(refreshCount, 1);
  });

  testWidgets('renders expense cards in backend order', (tester) async {
    final state = ExpensesState(
      expenses: [
        ExpenseListItem(
          id: 'expense-1',
          amount: 1250,
          categoryName: 'Rent',
          paidTo: 'Landlord',
          expenseDate: DateTime(2026, 7),
          isVoided: false,
        ),
        ExpenseListItem(
          id: 'expense-2',
          amount: 42.5,
          categoryName: 'Travel',
          paidTo: 'Metro',
          expenseDate: DateTime(2026, 6, 30),
          isVoided: true,
        ),
      ],
      totalCount: 22,
    );

    await tester.pumpWidget(_buildApp(state));
    await tester.pumpAndSettle();

    expect(find.text('Rent'), findsOneWidget);
    expect(find.text('Landlord'), findsOneWidget);
    expect(find.text('₹1,250.00'), findsOneWidget);
    expect(find.text('Jul 1, 2026'), findsOneWidget);
    expect(find.text('Active'), findsOneWidget);
    expect(find.text('Travel'), findsOneWidget);
    expect(find.text('Metro'), findsOneWidget);
    expect(find.text('₹42.50'), findsOneWidget);
    expect(find.text('Jun 30, 2026'), findsOneWidget);
    expect(find.text('Voided'), findsOneWidget);

    final rentTop = tester.getTopLeft(find.text('Rent')).dy;
    final travelTop = tester.getTopLeft(find.text('Travel')).dy;
    expect(rentTop, lessThan(travelTop));
  });

  testWidgets('keeps rows visible and offers retry after refresh failure', (
    tester,
  ) async {
    var refreshCount = 0;
    final state = ExpensesState(
      expenses: [
        ExpenseListItem(
          id: 'expense-1',
          amount: 1250,
          categoryName: 'Rent',
          paidTo: 'Landlord',
          expenseDate: DateTime(2026, 7),
          isVoided: false,
        ),
      ],
      totalCount: 1,
      listFailure: const Failure.network(message: 'offline'),
    );

    await tester.pumpWidget(
      _buildApp(
        state,
        onRefresh: () async => refreshCount += 1,
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Rent'), findsOneWidget);
    expect(find.text('Unable to refresh expenses'), findsOneWidget);
    expect(
      find.text('Unable to connect. Please check your network.'),
      findsOneWidget,
    );
    await tester.tap(find.text('Retry'));
    await tester.pump();
    expect(refreshCount, 1);
    expect(find.text('Rent'), findsOneWidget);
  });

  testWidgets('pull-to-refresh replaces ledger with refreshed page', (
    tester,
  ) async {
    ExpenseListItem item(String id, String category) => ExpenseListItem(
      id: id,
      amount: 100,
      categoryName: category,
      paidTo: 'Payee',
      expenseDate: DateTime(2026, 7),
      isVoided: false,
    );

    await tester.pumpWidget(
      _buildApp(
        ExpensesState(expenses: [item('expense-1', 'Rent')], totalCount: 1),
        refreshedState: ExpensesState(
          expenses: [item('expense-2', 'Travel')],
          totalCount: 1,
        ),
      ),
    );
    await tester.pumpAndSettle();

    await tester.drag(find.byType(ListView), const Offset(0, 300));
    await tester.pumpAndSettle();

    expect(find.text('Rent'), findsNothing);
    expect(find.text('Travel'), findsOneWidget);
  });
}
