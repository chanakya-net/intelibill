import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expense_list_item.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expenses_page.dart';
import 'package:intelibill_mobile/src/features/expenses/presentation/controllers/expenses_controller.dart';
import 'package:intelibill_mobile/src/features/expenses/presentation/pages/expenses_page.dart';

class _StubExpensesController extends ExpensesController {
  _StubExpensesController(this._state, {this.onOpenExpense});

  final ExpensesState _state;
  final Future<void> Function(String id)? onOpenExpense;

  @override
  ExpensesState build() => _state;

  @override
  Future<void> openExpense(String id) async {
    state = state.copyWith(
      selectedExpenseId: id,
      isDetailLoading: true,
      clearSelectedExpense: true,
    );
    await onOpenExpense?.call(id);
  }
}

final _loadedState = ExpensesState(
  page: ExpensePage(
    items: [
      ExpenseListItem(
        id: 'expense-2',
        amount: 1250,
        categoryName: 'Rent',
        paidTo: 'Landlord',
        expenseDate: DateTime(2026, 7, 2),
        isVoided: false,
      ),
      ExpenseListItem(
        id: 'expense-1',
        amount: 499.5,
        categoryName: 'Utilities',
        paidTo: 'Power Company',
        expenseDate: DateTime(2026, 7),
        isVoided: true,
      ),
    ],
    totalCount: 2,
    pageNumber: 1,
    pageSize: 20,
  ),
);

Widget _buildApp(
  ExpensesState state, {
  Future<void> Function(String id)? onOpenExpense,
}) {
  return ProviderScope(
    overrides: [
      expensesControllerProvider.overrideWith(
        () => _StubExpensesController(state, onOpenExpense: onOpenExpense),
      ),
    ],
    child: const MaterialApp(
      locale: Locale('en', 'IN'),
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      home: ExpensesPage(),
    ),
  );
}

void main() {
  testWidgets('renders first page in server order with ledger fields', (
    tester,
  ) async {
    await tester.pumpWidget(_buildApp(_loadedState));
    await tester.pumpAndSettle();

    expect(find.text('Expenses'), findsOneWidget);
    expect(find.text('Rent'), findsOneWidget);
    expect(find.text('Landlord'), findsOneWidget);
    expect(find.text('₹1,250'), findsOneWidget);
    expect(find.text('02 Jul 2026'), findsOneWidget);
    expect(find.text('Active'), findsNWidgets(2));
    expect(find.text('Utilities'), findsOneWidget);
    expect(find.text('Power Company'), findsOneWidget);
    expect(find.text('₹500'), findsOneWidget);
    expect(find.text('01 Jul 2026'), findsOneWidget);
    expect(find.text('Voided'), findsNWidgets(2));

    final rentTop = tester.getTopLeft(find.text('Rent')).dy;
    final utilitiesTop = tester.getTopLeft(find.text('Utilities')).dy;
    expect(rentTop, lessThan(utilitiesTop));
  });

  testWidgets('shows loading state', (tester) async {
    await tester.pumpWidget(
      _buildApp(const ExpensesState(isLoading: true)),
    );
    expect(find.byType(CircularProgressIndicator), findsOneWidget);
  });

  testWidgets('shows empty state', (tester) async {
    await tester.pumpWidget(_buildApp(const ExpensesState()));
    await tester.pump();
    expect(find.text('No expenses found'), findsOneWidget);
  });

  testWidgets('filters loaded rows with All, Active, and Voided chips', (
    tester,
  ) async {
    await tester.pumpWidget(_buildApp(_loadedState));
    await tester.pumpAndSettle();

    expect(find.byType(FilterChip), findsNWidgets(3));
    expect(find.text('Rent'), findsOneWidget);
    expect(find.text('Utilities'), findsOneWidget);

    await tester.tap(find.widgetWithText(FilterChip, 'Active'));
    await tester.pump();
    expect(find.text('Rent'), findsOneWidget);
    expect(find.text('Utilities'), findsNothing);

    await tester.tap(find.widgetWithText(FilterChip, 'Voided'));
    await tester.pump();
    expect(find.text('Rent'), findsNothing);
    expect(find.text('Utilities'), findsOneWidget);

    await tester.tap(find.widgetWithText(FilterChip, 'All'));
    await tester.pump();
    expect(find.text('Rent'), findsOneWidget);
    expect(find.text('Utilities'), findsOneWidget);
  });

  testWidgets('shows a distinct message when a filter has no matches', (
    tester,
  ) async {
    await tester.pumpWidget(
      _buildApp(
        ExpensesState(
          page: ExpensePage(
            items: [_loadedState.page!.items.last],
            totalCount: 1,
            pageNumber: 1,
            pageSize: 20,
          ),
          statusFilter: ExpenseStatusFilter.active,
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('No expenses match the selected filter'), findsOneWidget);
    expect(find.text('No expenses found'), findsNothing);
  });

  testWidgets('shows failure state with retry button', (tester) async {
    await tester.pumpWidget(
      _buildApp(
        const ExpensesState(failure: Failure.network()),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('Unable to load expenses'), findsOneWidget);
    expect(find.byType(FilledButton), findsOneWidget);
  });

  testWidgets('shows failure overlay with cards preserved', (tester) async {
    await tester.pumpWidget(
      _buildApp(
        _loadedState.copyWith(failure: const Failure.network()),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('Rent'), findsOneWidget);
    expect(find.text('Unable to load expenses'), findsOneWidget);
    expect(find.byType(FilledButton), findsOneWidget);
  });

  testWidgets('tapping a card loads and opens expense detail', (tester) async {
    String? openedId;
    await tester.pumpWidget(
      _buildApp(
        _loadedState,
        onOpenExpense: (id) async => openedId = id,
      ),
    );
    await tester.pumpAndSettle();

    await tester.tap(find.text('Rent'));
    await tester.pump();

    expect(openedId, 'expense-2');
    expect(find.byType(BottomSheet), findsOneWidget);
    expect(
      tester.widget<BottomSheet>(find.byType(BottomSheet)).showDragHandle,
      isTrue,
    );
    expect(find.byType(CircularProgressIndicator), findsOneWidget);
  });
}
