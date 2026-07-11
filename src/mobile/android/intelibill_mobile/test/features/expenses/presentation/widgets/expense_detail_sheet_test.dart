import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/app/theme/app_theme.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expense_detail.dart';
import 'package:intelibill_mobile/src/features/expenses/presentation/controllers/expenses_controller.dart';
import 'package:intelibill_mobile/src/features/expenses/presentation/widgets/expense_detail_sheet.dart';

class _StubExpensesController extends ExpensesController {
  _StubExpensesController(this.initialState, {this.onRetryExpenseDetail});

  final ExpensesState initialState;
  final Future<void> Function()? onRetryExpenseDetail;
  int retryCount = 0;

  @override
  ExpensesState build() => initialState;

  @override
  Future<void> retryExpenseDetail() async {
    retryCount += 1;
    await onRetryExpenseDetail?.call();
  }
}

Widget _buildApp(
  ExpensesState state, {
  Future<void> Function()? onRetryExpenseDetail,
}) {
  return ProviderScope(
    overrides: [
      expensesControllerProvider.overrideWith(
        () => _StubExpensesController(
          state,
          onRetryExpenseDetail: onRetryExpenseDetail,
        ),
      ),
    ],
    child: MaterialApp(
      theme: AppTheme.lightTheme,
      locale: const Locale('en', 'IN'),
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      home: const Scaffold(body: ExpenseDetailSheet()),
    ),
  );
}

ExpenseDetail _detail({
  bool isVoided = false,
  String? description = 'July rent',
  String? originalExpenseId = 'expense-original',
  String? supplierLedgerEntryId = 'ledger-1',
}) {
  return ExpenseDetail(
    id: 'expense-1',
    shopId: 'shop-1',
    categoryId: 'category-1',
    categoryName: 'Rent',
    amount: 1250.5,
    paidTo: 'Landlord',
    description: description,
    expenseDate: DateTime(2026, 7),
    actorUserId: 'user-1',
    isVoided: isVoided,
    originalExpenseId: originalExpenseId,
    supplierLedgerEntryId: supplierLedgerEntryId,
    createdAt: DateTime(2026, 7, 1, 14),
  );
}

void main() {
  testWidgets('shows loading state', (tester) async {
    await tester.pumpWidget(
      _buildApp(
        const ExpensesState(
          selectedExpenseId: 'expense-1',
          isDetailLoading: true,
        ),
      ),
    );

    expect(find.byType(SafeArea), findsOneWidget);
    expect(find.byType(CircularProgressIndicator), findsOneWidget);
  });

  testWidgets('shows complete detail with exact amount and dates', (
    tester,
  ) async {
    await tester.pumpWidget(
      _buildApp(
        ExpensesState(
          selectedExpenseId: 'expense-1',
          selectedExpense: _detail(),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Expense details'), findsOneWidget);
    expect(find.text('₹1,250.50'), findsOneWidget);
    expect(find.text('Rent'), findsOneWidget);
    expect(find.text('Landlord'), findsOneWidget);
    expect(find.text('July rent'), findsOneWidget);
    expect(find.text('Jul 1, 2026'), findsOneWidget);
    expect(find.text('Jul 1, 2026, 2:00 PM'), findsOneWidget);
    expect(find.text('Supplier payment'), findsOneWidget);
    expect(find.text('expense-original'), findsOneWidget);
    expect(find.text('ledger-1'), findsOneWidget);
    expect(find.byType(SingleChildScrollView), findsOneWidget);
  });

  testWidgets('renders nullable fields with localized empty values', (
    tester,
  ) async {
    await tester.pumpWidget(
      _buildApp(
        ExpensesState(
          selectedExpenseId: 'expense-1',
          selectedExpense: _detail(
            description: null,
            originalExpenseId: null,
            supplierLedgerEntryId: null,
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Not provided'), findsOneWidget);
    expect(find.text('Manual expense'), findsOneWidget);
    expect(find.text('Not linked'), findsNWidgets(2));
  });

  testWidgets('identifies correction source when original expense is linked', (
    tester,
  ) async {
    await tester.pumpWidget(
      _buildApp(
        ExpensesState(
          selectedExpenseId: 'expense-1',
          selectedExpense: _detail(supplierLedgerEntryId: null),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Expense correction'), findsOneWidget);
    expect(find.text('expense-original'), findsOneWidget);
    expect(find.text('Not linked'), findsOneWidget);
  });

  testWidgets('shows voided status', (tester) async {
    await tester.pumpWidget(
      _buildApp(
        ExpensesState(
          selectedExpenseId: 'expense-1',
          selectedExpense: _detail(isVoided: true),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Voided'), findsOneWidget);
    expect(find.text('Active'), findsNothing);
  });

  testWidgets('shows failure and retries selected detail', (tester) async {
    await tester.pumpWidget(
      _buildApp(
        const ExpensesState(
          selectedExpenseId: 'expense-1',
          detailFailure: Failure.network(message: 'offline'),
        ),
      ),
    );

    expect(find.text('Unable to load expense details'), findsOneWidget);
    expect(
      find.text('Unable to connect. Please check your network.'),
      findsOneWidget,
    );

    await tester.tap(find.text('Retry'));
    await tester.pump();

    final scope = ProviderScope.containerOf(
      tester.element(find.byType(ExpenseDetailSheet)),
    );
    final controller = scope.read(expensesControllerProvider.notifier);
    expect((controller as _StubExpensesController).retryCount, 1);
  });

  testWidgets('shows correct action for active expense', (tester) async {
    await tester.pumpWidget(
      _buildApp(
        ExpensesState(
          selectedExpenseId: 'expense-1',
          selectedExpense: _detail(isVoided: false),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Correct expense'), findsOneWidget);
  });

  testWidgets('hides correct action for voided expense', (tester) async {
    await tester.pumpWidget(
      _buildApp(
        ExpensesState(
          selectedExpenseId: 'expense-1',
          selectedExpense: _detail(isVoided: true),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Correct expense'), findsNothing);
    expect(find.text('Voided'), findsOneWidget);
  });

  testWidgets('correct action is visible for active expense', (tester) async {
    await tester.pumpWidget(
      _buildApp(
        ExpensesState(
          selectedExpenseId: 'expense-1',
          selectedExpense: _detail(isVoided: false),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Correct expense'), findsOneWidget);
    expect(find.byType(TextButton), findsWidgets);
  });
}
