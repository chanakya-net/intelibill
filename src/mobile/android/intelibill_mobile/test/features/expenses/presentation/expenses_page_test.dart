import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expense_list_item.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expenses_page.dart';
import 'package:intelibill_mobile/src/features/expenses/presentation/controllers/expenses_controller.dart';
import 'package:intelibill_mobile/src/features/expenses/presentation/pages/expenses_page.dart';
import 'package:intelibill_mobile/src/features/expenses/presentation/widgets/expense_card.dart';

class _StubExpensesController extends ExpensesController {
  _StubExpensesController(
    this._state, {
    this.onOpenExpense,
    this.onLoadMore,
  });

  final ExpensesState _state;
  final Future<void> Function(String id)? onOpenExpense;
  final Future<void> Function()? onLoadMore;

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

  @override
  Future<void> loadMore() async {
    state = state.copyWith(isLoadingMore: true);
    await onLoadMore?.call();
  }

  @override
  void updateSearch(String query) {
    state = state.copyWith(searchQuery: query);
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
  Future<void> Function()? onLoadMore,
  Locale locale = const Locale('en', 'IN'),
  TextScaler textScaler = TextScaler.noScaling,
}) {
  return ProviderScope(
    overrides: [
      expensesControllerProvider.overrideWith(
        () => _StubExpensesController(
          state,
          onOpenExpense: onOpenExpense,
          onLoadMore: onLoadMore,
        ),
      ),
    ],
    child: MaterialApp(
      locale: locale,
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      builder: (context, child) => MediaQuery(
        data: MediaQuery.of(context).copyWith(textScaler: textScaler),
        child: child!,
      ),
      home: const ExpensesPage(),
    ),
  );
}

ExpenseListItem _item(String id) => ExpenseListItem(
  id: id,
  amount: 100,
  categoryName: 'Travel',
  paidTo: 'Taxi',
  expenseDate: DateTime(2026, 7),
  isVoided: false,
);

ExpensesState _paginatedState({
  required List<ExpenseListItem> items,
  int totalCount = 40,
  bool isLoadingMore = false,
  Failure? loadMoreFailure,
}) => ExpensesState(
  page: ExpensePage(
    items: items,
    totalCount: totalCount,
    pageNumber: 1,
    pageSize: 20,
  ),
  totalCount: totalCount,
  isLoadingMore: isLoadingMore,
  loadMoreFailure: loadMoreFailure,
);

void main() {
  testWidgets('renders first page in server order with ledger fields', (
    tester,
  ) async {
    await tester.pumpWidget(_buildApp(_loadedState));
    await tester.pumpAndSettle();

    expect(find.text('Expenses'), findsOneWidget);
    expect(find.text('Rent'), findsOneWidget);
    expect(find.text('Landlord'), findsOneWidget);
    expect(
      find.descendant(
        of: find.byType(ExpenseCard),
        matching: find.text('₹1,250'),
      ),
      findsOneWidget,
    );
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

  testWidgets('renders labeled search field with clear action', (tester) async {
    await tester.pumpWidget(
      _buildApp(_loadedState.copyWith(searchQuery: 'rent')),
    );
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('expenses-search')), findsOneWidget);
    expect(find.text('Search...'), findsOneWidget);
    expect(find.byIcon(Icons.clear), findsOneWidget);

    await tester.tap(find.byIcon(Icons.clear));
    await tester.pump();
    final container = ProviderScope.containerOf(
      tester.element(find.byType(ExpensesPage)),
    );
    expect(container.read(expensesControllerProvider).searchQuery, isEmpty);
    expect(find.byIcon(Icons.clear), findsNothing);
  });

  testWidgets('localizes clear-search semantics for Hindi', (tester) async {
    final semantics = tester.ensureSemantics();

    await tester.pumpWidget(
      _buildApp(
        _loadedState.copyWith(searchQuery: 'rent'),
        locale: const Locale('hi', 'IN'),
      ),
    );
    await tester.pumpAndSettle();

    expect(tester.getSemantics(find.byIcon(Icons.clear)).label, 'साफ़ करें');
    semantics.dispose();
  });

  testWidgets('handles narrow screens with large text without overflow', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(320, 640);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    await tester.pumpWidget(
      _buildApp(_loadedState, textScaler: const TextScaler.linear(1.8)),
    );
    await tester.pumpAndSettle();

    expect(tester.takeException(), isNull);
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

    expect(
      find.text('No loaded expenses match this filter'),
      findsOneWidget,
    );
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

  testWidgets('requests the next page near the end of the ledger', (
    tester,
  ) async {
    var loadMoreCalls = 0;
    await tester.pumpWidget(
      _buildApp(
        _paginatedState(items: List.generate(20, (index) => _item('$index'))),
        onLoadMore: () async => loadMoreCalls++,
      ),
    );
    await tester.pumpAndSettle();
    final container = ProviderScope.containerOf(
      tester.element(find.byType(ExpensesPage)),
    );
    expect(container.read(expensesControllerProvider).hasMore, isTrue);

    final scrollable = tester.state<ScrollableState>(
      find.descendant(
        of: find.byType(ListView),
        matching: find.byType(Scrollable),
      ),
    );
    expect(scrollable.position.maxScrollExtent, greaterThan(0));
    for (var index = 0; index < 6; index++) {
      await tester.drag(find.byType(ListView), const Offset(0, -500));
      await tester.pump();
    }
    expect(scrollable.position.pixels, greaterThan(0));
    expect(scrollable.position.extentAfter, lessThanOrEqualTo(200));
    expect(container.read(expensesControllerProvider).isLoadingMore, isTrue);

    expect(loadMoreCalls, greaterThanOrEqualTo(1));
  });

  testWidgets('shows append progress at the bottom', (tester) async {
    await tester.pumpWidget(
      _buildApp(
        _paginatedState(
          items: [_item('expense-1')],
          isLoadingMore: true,
        ),
      ),
    );
    await tester.pump();

    expect(find.byType(CircularProgressIndicator), findsOneWidget);
    expect(find.text('Travel'), findsOneWidget);
  });

  testWidgets('shows append retry while retaining loaded cards', (
    tester,
  ) async {
    var loadMoreCalls = 0;
    await tester.pumpWidget(
      _buildApp(
        _paginatedState(
          items: [_item('expense-1')],
          loadMoreFailure: const Failure.network(message: 'offline'),
        ),
        onLoadMore: () async => loadMoreCalls++,
      ),
    );
    await tester.pump();

    expect(find.text('Travel'), findsOneWidget);
    expect(find.text('Unable to load more expenses'), findsOneWidget);
    await tester.tap(find.widgetWithText(TextButton, 'Retry'));
    await tester.pump();

    expect(loadMoreCalls, 1);
    expect(find.text('Travel'), findsOneWidget);
  });
}
