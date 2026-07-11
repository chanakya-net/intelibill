import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expense_category.dart';
import 'package:intelibill_mobile/src/features/expenses/presentation/controllers/expenses_controller.dart';
import 'package:intelibill_mobile/src/features/expenses/presentation/widgets/expense_form_sheet.dart';

class _FormController extends ExpensesController {
  _FormController(this._state, {this.onSubmit, this.onLoadCategories});

  final ExpensesState _state;
  final Future<bool> Function()? onSubmit;
  final Future<List<ExpenseCategory>> Function({required bool force})?
  onLoadCategories;

  @override
  ExpensesState build() => _state;

  @override
  Future<void> loadCategories({bool force = false}) async {
    await Future<void>.delayed(Duration.zero);
    if (state.isLoadingCategories || (!force && state.categories.isNotEmpty)) {
      return;
    }
    state = state.copyWith(
      isLoadingCategories: true,
      clearCategoryFailure: true,
    );
    try {
      final categories =
          await onLoadCategories?.call(force: force) ?? state.categories;
      if (!ref.mounted) return;
      state = state.copyWith(
        categories: categories,
        isLoadingCategories: false,
        clearCategoryFailure: true,
      );
    } on AppException catch (error) {
      if (!ref.mounted) return;
      state = state.copyWith(
        isLoadingCategories: false,
        categoryFailure: error.failure,
      );
    }
  }

  @override
  Future<bool> recordExpense({
    required String categoryName,
    required double amount,
    required String paidTo,
    String? description,
    required DateTime expenseDate,
  }) async {
    if (state.isSubmitting) return false;
    state = state.copyWith(isSubmitting: true, clearSubmitFailure: true);
    try {
      final success = await onSubmit?.call() ?? true;
      if (!ref.mounted) return false;
      state = state.copyWith(
        isSubmitting: false,
        clearSubmitFailure: success,
      );
      return success;
    } on AppException catch (error) {
      if (!ref.mounted) return false;
      state = state.copyWith(
        isSubmitting: false,
        submitFailure: error.failure,
      );
      return false;
    }
  }
}

Widget _buildApp(
  ExpensesState state, {
  Future<bool> Function()? onSubmit,
  Future<List<ExpenseCategory>> Function({required bool force})?
  onLoadCategories,
}) {
  return ProviderScope(
    overrides: [
      expensesControllerProvider.overrideWith(
        () => _FormController(
          state,
          onSubmit: onSubmit,
          onLoadCategories: onLoadCategories,
        ),
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

Future<void> _open(
  WidgetTester tester,
  ExpensesState state, {
  Future<List<ExpenseCategory>> Function({required bool force})?
  onLoadCategories,
}) async {
  await tester.pumpWidget(
    _buildApp(state, onLoadCategories: onLoadCategories),
  );
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

  testWidgets('blocks non-finite amounts without recording', (tester) async {
    var recordCalls = 0;
    await tester.pumpWidget(
      _buildApp(
        ExpensesState(categories: categories),
        onSubmit: () async {
          recordCalls++;
          return true;
        },
      ),
    );
    await tester.tap(find.text('Open'));
    await tester.pumpAndSettle();
    await tester.enterText(
      find.byKey(ExpenseFormSheet.categoryFieldKey),
      'Office',
    );
    await tester.enterText(
      find.byKey(ExpenseFormSheet.paidToFieldKey),
      'Vendor',
    );

    for (final value in ['NaN', 'Infinity']) {
      await tester.enterText(
        find.byKey(ExpenseFormSheet.amountFieldKey),
        value,
      );
      await tester.tap(find.byKey(ExpenseFormSheet.submitButtonKey));
      await tester.pump();
      expect(find.text('Enter an amount greater than 0.'), findsOneWidget);
    }
    expect(recordCalls, 0);
  });

  testWidgets('suppresses duplicate submits while recording', (tester) async {
    final submit = Completer<bool>();
    var recordCalls = 0;
    await tester.pumpWidget(
      _buildApp(
        ExpensesState(categories: categories),
        onSubmit: () {
          recordCalls++;
          return submit.future;
        },
      ),
    );
    await tester.tap(find.text('Open'));
    await tester.pumpAndSettle();
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
    await tester.pump();
    await tester.tap(find.byKey(ExpenseFormSheet.submitButtonKey));

    expect(recordCalls, 1);
    submit.complete(true);
    await tester.pumpAndSettle();
    expect(find.byType(ExpenseFormSheet), findsNothing);
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
    var attempts = 0;
    await _open(
      tester,
      const ExpensesState(),
      onLoadCategories: ({required bool force}) async {
        attempts++;
        if (!force) {
          throw AppException(failure: const Failure.network());
        }
        return const [ExpenseCategory(id: 'office', name: 'Office')];
      },
    );
    await tester.enterText(
      find.byKey(ExpenseFormSheet.categoryFieldKey),
      'Office',
    );
    await tester.tap(find.byKey(ExpenseFormSheet.categoryRetryButtonKey));
    await tester.pumpAndSettle();

    expect(attempts, 2);
    expect(
      find.byKey(const Key('expense-category-suggestion-Office')),
      findsOneWidget,
    );
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
