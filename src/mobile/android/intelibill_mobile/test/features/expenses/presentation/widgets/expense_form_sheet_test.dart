import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expense_category.dart';
import 'package:intelibill_mobile/src/features/expenses/presentation/controllers/expenses_controller.dart';
import 'package:intelibill_mobile/src/features/expenses/presentation/widgets/expense_form_sheet.dart';

class _FormController extends ExpensesController {
  _FormController(this._state, {this.onSubmit});

  final ExpensesState _state;
  final Future<bool> Function()? onSubmit;

  @override
  ExpensesState build() => _state;

  @override
  Future<void> loadCategories({bool force = false}) async {}

  @override
  Future<bool> recordExpense({
    required String categoryName,
    required double amount,
    required String paidTo,
    String? description,
    required DateTime expenseDate,
  }) async {
    return onSubmit?.call() ?? true;
  }
}

Widget _buildApp(
  ExpensesState state, {
  Future<bool> Function()? onSubmit,
}) {
  return ProviderScope(
    overrides: [
      expensesControllerProvider.overrideWith(
        () => _FormController(state, onSubmit: onSubmit),
      ),
    ],
    child: MaterialApp(
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      home: Builder(
        builder: (context) => Scaffold(
          body: Center(
            child: ElevatedButton(
              onPressed: () => unawaited(
                showModalBottomSheet<bool>(
                  context: context,
                  isScrollControlled: true,
                  builder: (_) => const ExpenseFormSheet(),
                ),
              ),
              child: const Text('Open'),
            ),
          ),
        ),
      ),
    ),
  );
}

Future<void> _open(WidgetTester tester, ExpensesState state) async {
  await tester.pumpWidget(_buildApp(state));
  await tester.tap(find.text('Open'));
  await tester.pumpAndSettle();
}

void main() {
  final categories = [
    const ExpenseCategory(id: 'rent', name: 'Rent'),
    const ExpenseCategory(id: 'supplier', name: ' supplier payments '),
  ];

  testWidgets('uses today by default and excludes reserved category', (
    tester,
  ) async {
    await _open(tester, ExpensesState(categories: categories));

    expect(find.byKey(ExpenseFormSheet.dateFieldKey), findsOneWidget);
    expect(find.text('Rent'), findsOneWidget);
    expect(find.text('supplier payments'), findsNothing);
  });

  testWidgets('blocks invalid values at validation boundaries', (tester) async {
    await _open(tester, ExpensesState(categories: categories));

    await tester.enterText(
      find.byKey(ExpenseFormSheet.categoryFieldKey),
      '   ',
    );
    await tester.enterText(find.byKey(ExpenseFormSheet.amountFieldKey), '0');
    await tester.enterText(find.byKey(ExpenseFormSheet.paidToFieldKey), '');
    await tester.tap(find.byKey(ExpenseFormSheet.submitButtonKey));
    await tester.pump();

    expect(find.text('Category is required.'), findsOneWidget);
    expect(find.text('Enter an amount greater than 0.'), findsOneWidget);
    expect(find.text('Paid to is required.'), findsOneWidget);
  });

  testWidgets('retains values after submission failure', (tester) async {
    await tester.pumpWidget(
      _buildApp(
        ExpensesState(categories: categories),
        onSubmit: () async => false,
      ),
    );
    await tester.tap(find.text('Open'));
    await tester.pumpAndSettle();
    await tester.enterText(
      find.byKey(ExpenseFormSheet.categoryFieldKey),
      ' Office ',
    );
    await tester.enterText(find.byKey(ExpenseFormSheet.amountFieldKey), '10');
    await tester.enterText(
      find.byKey(ExpenseFormSheet.paidToFieldKey),
      ' Vendor ',
    );
    await tester.enterText(
      find.byKey(ExpenseFormSheet.descriptionFieldKey),
      ' Notes ',
    );
    await tester.tap(find.byKey(ExpenseFormSheet.submitButtonKey));
    await tester.pumpAndSettle();

    expect(find.byType(ExpenseFormSheet), findsOneWidget);
    expect(
      tester
          .widget<TextFormField>(
            find.byKey(ExpenseFormSheet.categoryFieldKey),
          )
          .controller!
          .text,
      ' Office ',
    );
    expect(
      tester
          .widget<TextFormField>(find.byKey(ExpenseFormSheet.amountFieldKey))
          .controller!
          .text,
      '10',
    );
  });

  testWidgets('closes after successful submission', (tester) async {
    await _open(tester, ExpensesState(categories: categories));
    await tester.enterText(
      find.byKey(ExpenseFormSheet.categoryFieldKey),
      'Office',
    );
    await tester.enterText(find.byKey(ExpenseFormSheet.amountFieldKey), '10');
    await tester.enterText(
      find.byKey(ExpenseFormSheet.paidToFieldKey),
      'Vendor',
    );
    await tester.tap(find.byKey(ExpenseFormSheet.submitButtonKey));
    await tester.pumpAndSettle();

    expect(find.byType(ExpenseFormSheet), findsNothing);
  });

  testWidgets('shows category retry without losing entered input', (
    tester,
  ) async {
    await _open(
      tester,
      const ExpensesState(categoryFailure: Failure.network()),
    );
    await tester.enterText(
      find.byKey(ExpenseFormSheet.categoryFieldKey),
      'Office',
    );
    await tester.tap(find.byKey(ExpenseFormSheet.categoryRetryButtonKey));
    await tester.pump();

    expect(
      tester
          .widget<TextFormField>(
            find.byKey(ExpenseFormSheet.categoryFieldKey),
          )
          .controller!
          .text,
      'Office',
    );
  });
}
