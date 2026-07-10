import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/app/theme/app_theme.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expense_list_item.dart';
import 'package:intelibill_mobile/src/features/expenses/presentation/controllers/expenses_controller.dart';
import 'package:intelibill_mobile/src/features/expenses/presentation/pages/expenses_page.dart';

class _StubExpensesController extends ExpensesController {
  _StubExpensesController(this._state);

  final ExpensesState _state;

  @override
  ExpensesState build() => _state;
}

Widget _buildApp(ExpensesState state) {
  return ProviderScope(
    overrides: [
      expensesControllerProvider.overrideWith(
        () => _StubExpensesController(state),
      ),
    ],
    child: MaterialApp(
      locale: const Locale('en', 'IN'),
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
}
