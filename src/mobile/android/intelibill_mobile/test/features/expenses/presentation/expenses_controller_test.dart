import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expense_detail.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expense_list_item.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expenses_page.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/use_cases/get_expense_detail.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/use_cases/get_expenses.dart';
import 'package:intelibill_mobile/src/features/expenses/presentation/controllers/expenses_controller.dart';
import 'package:mocktail/mocktail.dart';

class MockGetExpenses extends Mock implements GetExpenses {}

class MockGetExpenseDetail extends Mock implements GetExpenseDetail {}

class _TestExpensesController extends ExpensesController {
  _TestExpensesController(this._initialState);

  final ExpensesState _initialState;

  @override
  ExpensesState build() => _initialState;
}

ExpenseDetail _detail(String id) => ExpenseDetail(
  id: id,
  shopId: 'shop-1',
  categoryId: 'category-1',
  categoryName: 'Rent',
  amount: 100,
  paidTo: 'Payee',
  expenseDate: DateTime(2026, 7),
  actorUserId: 'user-1',
  isVoided: false,
  createdAt: DateTime(2026, 7, 1, 8, 30),
);

ExpensePage _makePage({
  required List<ExpenseListItem> items,
  int pageNumber = 1,
  int pageSize = 20,
  int? totalCount,
}) => ExpensePage(
  items: items,
  totalCount: totalCount ?? items.length,
  pageNumber: pageNumber,
  pageSize: pageSize,
);

ExpenseListItem _item(String id) => ExpenseListItem(
  id: id,
  amount: 100,
  categoryName: 'Travel',
  paidTo: 'Taxi',
  expenseDate: DateTime(2026, 7),
  isVoided: false,
);

final _page = ExpensePage(
  items: [
    ExpenseListItem(
      id: 'expense-1',
      amount: 100,
      categoryName: 'Travel',
      paidTo: 'Taxi',
      expenseDate: DateTime(2026, 7),
      isVoided: false,
    ),
  ],
  totalCount: 1,
  pageNumber: 1,
  pageSize: 20,
);

final _pageWithStatuses = ExpensePage(
  items: [
    ExpenseListItem(
      id: 'active-expense',
      amount: 100,
      categoryName: 'Travel',
      paidTo: 'Taxi',
      expenseDate: DateTime(2026, 7),
      isVoided: false,
    ),
    ExpenseListItem(
      id: 'voided-expense',
      amount: 200,
      categoryName: 'Supplies',
      paidTo: 'Vendor',
      expenseDate: DateTime(2026, 7, 2),
      isVoided: true,
    ),
  ],
  totalCount: 2,
  pageNumber: 1,
  pageSize: 20,
);

void main() {
  ProviderContainer detailContainer(MockGetExpenseDetail getDetail) {
    return ProviderContainer(
      overrides: [
        getExpenseDetailUseCaseProvider.overrideWithValue(getDetail),
        expensesControllerProvider.overrideWith(
          () => _TestExpensesController(ExpensesState(page: _page)),
        ),
      ],
    );
  }

  test('loads first expenses page', () async {
    final getExpenses = MockGetExpenses();
    when(getExpenses.call).thenAnswer((_) async => _page);
    final container = ProviderContainer(
      overrides: [getExpensesUseCaseProvider.overrideWithValue(getExpenses)],
    );
    addTearDown(container.dispose);

    expect(container.read(expensesControllerProvider).isLoading, isTrue);
    await container.read(expensesControllerProvider.notifier).refresh();

    final state = container.read(expensesControllerProvider);
    expect(state.page, _page);
    expect(state.isLoading, isFalse);
    expect(state.failure, isNull);
    verify(getExpenses.call).called(greaterThanOrEqualTo(1));
  });

  test('exposes mapped failure when loading fails', () async {
    final getExpenses = MockGetExpenses();
    when(getExpenses.call).thenThrow(
      AppException(failure: const Failure.network()),
    );
    final container = ProviderContainer(
      overrides: [getExpensesUseCaseProvider.overrideWithValue(getExpenses)],
    );
    addTearDown(container.dispose);

    await container.read(expensesControllerProvider.notifier).refresh();

    final state = container.read(expensesControllerProvider);
    expect(state.isLoading, isFalse);
    expect(state.failure, isA<NetworkFailure>());
  });

  test('preserves prior page data when refresh fails', () async {
    final getExpenses = MockGetExpenses();
    var callCount = 0;
    when(getExpenses.call).thenAnswer((_) async {
      callCount++;
      if (callCount == 1) return _page;
      throw AppException(failure: const Failure.network());
    });
    final container = ProviderContainer(
      overrides: [getExpensesUseCaseProvider.overrideWithValue(getExpenses)],
    );
    addTearDown(container.dispose);

    await container.read(expensesControllerProvider.notifier).refresh();
    var state = container.read(expensesControllerProvider);
    expect(state.page, _page);
    expect(state.failure, isNull);

    await container.read(expensesControllerProvider.notifier).refresh();

    state = container.read(expensesControllerProvider);
    expect(state.page, _page);
    expect(state.failure, isA<NetworkFailure>());
  });

  test('debounces search, trims query, and resets to page one', () async {
    final getExpenses = MockGetExpenses();
    final searchedPage = _makePage(items: [_item('searched')]);
    when(
      () => getExpenses(search: 'rent', page: 1, pageSize: 20),
    ).thenAnswer((_) async => searchedPage);
    final container = ProviderContainer(
      overrides: [
        getExpensesUseCaseProvider.overrideWithValue(getExpenses),
        expensesControllerProvider.overrideWith(
          () => _TestExpensesController(
            ExpensesState(
              page: _makePage(
                items: [_item('old')],
                pageNumber: 3,
                totalCount: 60,
              ),
              pageNumber: 3,
              totalCount: 60,
            ),
          ),
        ),
      ],
    );
    addTearDown(container.dispose);
    final subscription = container.listen(
      expensesControllerProvider,
      (_, _) {},
    );
    addTearDown(subscription.close);
    final controller = container.read(expensesControllerProvider.notifier);

    controller.updateSearch('  rent  ');

    await Future<void>.delayed(const Duration(milliseconds: 250));
    verifyNever(() => getExpenses(search: 'rent', page: 1, pageSize: 20));
    expect(container.read(expensesControllerProvider).pageNumber, 3);

    await Future<void>.delayed(const Duration(milliseconds: 100));
    await Future<void>.delayed(Duration.zero);
    verify(() => getExpenses(search: 'rent', page: 1, pageSize: 20)).called(1);
    final state = container.read(expensesControllerProvider);
    expect(state.searchQuery, '  rent  ');
    expect(state.pageNumber, 1);
    expect(state.page, searchedPage);
  });

  test('clearing search reloads unfiltered page one', () async {
    final getExpenses = MockGetExpenses();
    final unfilteredPage = _makePage(items: [_item('all-expenses')]);
    when(getExpenses.call).thenAnswer((_) async => unfilteredPage);
    final container = ProviderContainer(
      overrides: [
        getExpensesUseCaseProvider.overrideWithValue(getExpenses),
        expensesControllerProvider.overrideWith(
          () => _TestExpensesController(
            ExpensesState(
              page: _makePage(
                items: [_item('old')],
                pageNumber: 2,
                totalCount: 40,
              ),
              pageNumber: 2,
              totalCount: 40,
              searchQuery: 'rent',
            ),
          ),
        ),
      ],
    );
    addTearDown(container.dispose);
    final subscription = container.listen(
      expensesControllerProvider,
      (_, _) {},
    );
    addTearDown(subscription.close);
    final controller = container.read(expensesControllerProvider.notifier);

    controller.updateSearch('');
    await Future<void>.delayed(const Duration(milliseconds: 300));
    await Future<void>.delayed(Duration.zero);
    verify(getExpenses.call).called(1);

    final state = container.read(expensesControllerProvider);
    expect(state.searchQuery, isEmpty);
    expect(state.pageNumber, 1);
    expect(state.page, unfilteredPage);
  });

  test('stale search response cannot replace newer results', () async {
    final getExpenses = MockGetExpenses();
    final older = Completer<ExpensePage>();
    final newer = Completer<ExpensePage>();
    when(
      () => getExpenses(search: 'old', page: 1, pageSize: 20),
    ).thenAnswer((_) => older.future);
    when(
      () => getExpenses(search: 'new', page: 1, pageSize: 20),
    ).thenAnswer((_) => newer.future);
    final container = ProviderContainer(
      overrides: [
        getExpensesUseCaseProvider.overrideWithValue(getExpenses),
        expensesControllerProvider.overrideWith(
          () => _TestExpensesController(const ExpensesState()),
        ),
      ],
    );
    addTearDown(container.dispose);
    final subscription = container.listen(
      expensesControllerProvider,
      (_, _) {},
    );
    addTearDown(subscription.close);
    final controller = container.read(expensesControllerProvider.notifier);

    controller.updateSearch('old');
    await Future<void>.delayed(const Duration(milliseconds: 300));
    await Future<void>.delayed(Duration.zero);
    verify(() => getExpenses(search: 'old', page: 1, pageSize: 20)).called(1);
    controller.updateSearch('new');
    await Future<void>.delayed(const Duration(milliseconds: 300));
    await Future<void>.delayed(Duration.zero);
    verify(() => getExpenses(search: 'new', page: 1, pageSize: 20)).called(1);

    newer.complete(_makePage(items: [_item('new-result')]));
    await Future<void>.delayed(Duration.zero);
    older.complete(_makePage(items: [_item('old-result')]));
    await Future<void>.delayed(Duration.zero);

    expect(
      container.read(expensesControllerProvider).page?.items.single.id,
      'new-result',
    );
  });

  test(
    'filters loaded expenses by status without another repository call',
    () async {
      final getExpenses = MockGetExpenses();
      when(getExpenses.call).thenAnswer((_) async => _pageWithStatuses);
      final container = ProviderContainer(
        overrides: [getExpensesUseCaseProvider.overrideWithValue(getExpenses)],
      );
      addTearDown(container.dispose);

      await container.read(expensesControllerProvider.notifier).refresh();
      clearInteractions(getExpenses);
      final controller = container.read(expensesControllerProvider.notifier);

      controller.updateStatusFilter(ExpenseStatusFilter.active);
      expect(
        container.read(expensesControllerProvider).filteredExpenses,
        hasLength(1),
      );
      expect(
        container.read(expensesControllerProvider).filteredExpenses.single.id,
        'active-expense',
      );

      controller.updateStatusFilter(ExpenseStatusFilter.voided);
      expect(
        container.read(expensesControllerProvider).filteredExpenses.single.id,
        'voided-expense',
      );

      controller.updateStatusFilter(ExpenseStatusFilter.all);
      expect(
        container.read(expensesControllerProvider).filteredExpenses,
        hasLength(2),
      );
      verifyNever(getExpenses.call);
    },
  );

  test(
    'returns empty filtered results without another repository call',
    () async {
      final getExpenses = MockGetExpenses();
      when(getExpenses.call).thenAnswer((_) async => _page);
      final container = ProviderContainer(
        overrides: [getExpensesUseCaseProvider.overrideWithValue(getExpenses)],
      );
      addTearDown(container.dispose);

      await container.read(expensesControllerProvider.notifier).refresh();
      clearInteractions(getExpenses);

      container
          .read(expensesControllerProvider.notifier)
          .updateStatusFilter(ExpenseStatusFilter.voided);

      expect(
        container.read(expensesControllerProvider).filteredExpenses,
        isEmpty,
      );
      verifyNever(getExpenses.call);
    },
  );

  test('openExpense loads detail without changing ledger state', () async {
    final getDetail = MockGetExpenseDetail();
    final completer = Completer<ExpenseDetail>();
    when(() => getDetail('expense-1')).thenAnswer((_) => completer.future);
    final container = detailContainer(getDetail);
    addTearDown(container.dispose);
    final page = container.read(expensesControllerProvider).page;

    final opening = container
        .read(expensesControllerProvider.notifier)
        .openExpense('expense-1');
    expect(container.read(expensesControllerProvider).isDetailLoading, isTrue);
    expect(container.read(expensesControllerProvider).page, page);

    final detail = _detail('expense-1');
    completer.complete(detail);
    await opening;
    final state = container.read(expensesControllerProvider);
    expect(state.selectedExpense, detail);
    expect(state.isDetailLoading, isFalse);
    expect(state.page, page);
  });

  test('detail failure and retry stay operation-specific', () async {
    final getDetail = MockGetExpenseDetail();
    when(() => getDetail('expense-1')).thenThrow(
      AppException(failure: const Failure.timeout(message: 'detail slow')),
    );
    final container = detailContainer(getDetail);
    addTearDown(container.dispose);

    await container
        .read(expensesControllerProvider.notifier)
        .openExpense('expense-1');
    expect(
      container.read(expensesControllerProvider).detailFailure,
      isA<TimeoutFailure>(),
    );

    final detail = _detail('expense-1');
    when(() => getDetail('expense-1')).thenAnswer((_) async => detail);
    await container
        .read(expensesControllerProvider.notifier)
        .retryExpenseDetail();
    expect(container.read(expensesControllerProvider).selectedExpense, detail);
    expect(container.read(expensesControllerProvider).detailFailure, isNull);
  });

  test('stale same-id failure cannot replace reopened detail state', () async {
    final getDetail = MockGetExpenseDetail();
    final first = Completer<ExpenseDetail>();
    final second = Completer<ExpenseDetail>();
    var calls = 0;
    when(() => getDetail('expense-1')).thenAnswer(
      (_) => ++calls == 1 ? first.future : second.future,
    );
    final container = detailContainer(getDetail);
    addTearDown(container.dispose);
    final controller = container.read(expensesControllerProvider.notifier);

    final firstOpening = controller.openExpense('expense-1');
    controller.clearSelectedExpense();
    final secondOpening = controller.openExpense('expense-1');
    final detail = _detail('expense-1');
    second.complete(detail);
    await secondOpening;
    first.completeError(
      AppException(failure: const Failure.network(message: 'stale')),
    );
    await firstOpening;

    final state = container.read(expensesControllerProvider);
    expect(state.selectedExpense, detail);
    expect(state.detailFailure, isNull);
  });

  test('clearSelectedExpense removes only detail state', () {
    final container = detailContainer(MockGetExpenseDetail());
    addTearDown(container.dispose);
    final controller = container.read(expensesControllerProvider.notifier);
    controller.clearSelectedExpense();
    final state = container.read(expensesControllerProvider);
    expect(state.selectedExpenseId, isNull);
    expect(state.selectedExpense, isNull);
    expect(state.detailFailure, isNull);
  });

  group('loadMore', () {
    test(
      'requests next page and appends unique rows in backend order',
      () async {
        final getExpenses = MockGetExpenses();
        final page1 = _makePage(
          items: [
            ExpenseListItem(
              id: 'expense-1',
              amount: 100,
              categoryName: 'Rent',
              paidTo: 'Landlord',
              expenseDate: DateTime(2026, 7),
              isVoided: false,
            ),
          ],
          totalCount: 21,
        );
        final page2 = _makePage(
          items: [
            ExpenseListItem(
              id: 'expense-1',
              amount: 100,
              categoryName: 'Duplicate',
              paidTo: 'Landlord',
              expenseDate: DateTime(2026, 7),
              isVoided: false,
            ),
            ExpenseListItem(
              id: 'expense-2',
              amount: 50,
              categoryName: 'Travel',
              paidTo: 'Uber',
              expenseDate: DateTime(2026, 7),
              isVoided: false,
            ),
          ],
          pageNumber: 2,
          totalCount: 21,
        );
        when(getExpenses.call).thenAnswer((_) async => page1);
        var callCount = 0;
        when(
          () => getExpenses(
            page: any(named: 'page'),
            pageSize: any(named: 'pageSize'),
          ),
        ).thenAnswer((_) async {
          callCount++;
          return callCount == 1 ? page1 : page2;
        });
        final container = ProviderContainer(
          overrides: [
            getExpensesUseCaseProvider.overrideWithValue(getExpenses),
          ],
        );
        addTearDown(container.dispose);

        await container.read(expensesControllerProvider.notifier).refresh();
        await container.read(expensesControllerProvider.notifier).loadMore();

        final state = container.read(expensesControllerProvider);
        expect(state.page?.items.map((e) => e.id).toList(), [
          'expense-1',
          'expense-2',
        ]);
        expect(state.page?.pageNumber, 2);
        expect(state.page?.pageSize, 20);
        expect(state.page?.totalCount, 21);
        expect(state.isLoadingMore, isFalse);
        expect(state.loadMoreFailure, isNull);
      },
    );

    test('requests next page without refreshing data', () async {
      final getExpenses = MockGetExpenses();
      final page1 = _makePage(
        items: [
          ExpenseListItem(
            id: 'expense-1',
            amount: 100,
            categoryName: 'Rent',
            paidTo: 'Landlord',
            expenseDate: DateTime(2026, 7),
            isVoided: false,
          ),
        ],
        totalCount: 21,
      );
      final page2 = _makePage(
        items: [
          ExpenseListItem(
            id: 'expense-2',
            amount: 50,
            categoryName: 'Travel',
            paidTo: 'Uber',
            expenseDate: DateTime(2026, 7),
            isVoided: false,
          ),
        ],
        pageNumber: 2,
        totalCount: 21,
      );
      when(getExpenses.call).thenAnswer((_) async => page1);
      when(
        () => getExpenses(page: 2, pageSize: 20),
      ).thenAnswer((_) async => page2);
      final container = ProviderContainer(
        overrides: [getExpensesUseCaseProvider.overrideWithValue(getExpenses)],
      );
      addTearDown(container.dispose);

      await container.read(expensesControllerProvider.notifier).refresh();
      await container.read(expensesControllerProvider.notifier).loadMore();

      final state = container.read(expensesControllerProvider);
      expect(state.page?.items.map((e) => e.id).toList(), [
        'expense-1',
        'expense-2',
      ]);
      expect(state.page?.pageNumber, 2);
      expect(state.isLoadingMore, isFalse);
      verify(() => getExpenses(page: 2, pageSize: 20)).called(1);
    });

    test('prevents loadMore when at end of list', () async {
      final getExpenses = MockGetExpenses();
      final lastPage = _makePage(
        items: [
          ExpenseListItem(
            id: 'expense-1',
            amount: 100,
            categoryName: 'Rent',
            paidTo: 'Landlord',
            expenseDate: DateTime(2026, 7),
            isVoided: false,
          ),
        ],
        totalCount: 1,
      );
      when(getExpenses.call).thenAnswer((_) async => lastPage);
      final container = ProviderContainer(
        overrides: [getExpensesUseCaseProvider.overrideWithValue(getExpenses)],
      );
      addTearDown(container.dispose);

      await container.read(expensesControllerProvider.notifier).refresh();
      await container.read(expensesControllerProvider.notifier).loadMore();

      final state = container.read(expensesControllerProvider);
      expect(state.page?.items.length, 1);
      expect(state.isLoadingMore, isFalse);
      verifyNever(() => getExpenses(page: 2, pageSize: 20));
    });

    test(
      'loadMore failure preserves loaded rows and exposes failure',
      () async {
        final getExpenses = MockGetExpenses();
        const failure = Failure.network(message: 'offline');
        final page1 = _makePage(
          items: [
            ExpenseListItem(
              id: 'expense-1',
              amount: 100,
              categoryName: 'Rent',
              paidTo: 'Landlord',
              expenseDate: DateTime(2026, 7),
              isVoided: false,
            ),
          ],
          totalCount: 21,
        );
        var callCount = 0;
        when(getExpenses.call).thenAnswer((_) async => page1);
        when(
          () => getExpenses(
            page: any(named: 'page'),
            pageSize: any(named: 'pageSize'),
          ),
        ).thenAnswer((_) async {
          callCount++;
          if (callCount == 1) return page1;
          throw AppException(failure: failure);
        });
        final container = ProviderContainer(
          overrides: [
            getExpensesUseCaseProvider.overrideWithValue(getExpenses),
          ],
        );
        addTearDown(container.dispose);

        await container.read(expensesControllerProvider.notifier).refresh();
        await container.read(expensesControllerProvider.notifier).loadMore();

        final state = container.read(expensesControllerProvider);
        expect(state.page?.items.length, 1);
        expect(state.page?.items.single.id, 'expense-1');
        expect(state.isLoadingMore, isFalse);
        expect(state.loadMoreFailure, isA<NetworkFailure>());
      },
    );

    test('loadMore failure retry recovers to success', () async {
      final getExpenses = MockGetExpenses();
      const failure = Failure.network(message: 'offline');
      final page1 = _makePage(
        items: [
          ExpenseListItem(
            id: 'expense-1',
            amount: 100,
            categoryName: 'Rent',
            paidTo: 'Landlord',
            expenseDate: DateTime(2026, 7),
            isVoided: false,
          ),
        ],
        totalCount: 21,
      );
      final page2 = _makePage(
        items: [
          ExpenseListItem(
            id: 'expense-2',
            amount: 50,
            categoryName: 'Travel',
            paidTo: 'Uber',
            expenseDate: DateTime(2026, 7),
            isVoided: false,
          ),
        ],
        pageNumber: 2,
        totalCount: 21,
      );
      var callCount = 0;
      when(getExpenses.call).thenAnswer((_) async => page1);
      when(() => getExpenses(page: 2, pageSize: 20)).thenAnswer((_) async {
        callCount++;
        if (callCount == 1) throw AppException(failure: failure);
        return page2;
      });
      final container = ProviderContainer(
        overrides: [getExpensesUseCaseProvider.overrideWithValue(getExpenses)],
      );
      addTearDown(container.dispose);

      await container.read(expensesControllerProvider.notifier).refresh();
      await container.read(expensesControllerProvider.notifier).loadMore();

      var state = container.read(expensesControllerProvider);
      expect(state.isLoadingMore, isFalse);
      expect(state.loadMoreFailure, isA<NetworkFailure>());

      await container.read(expensesControllerProvider.notifier).loadMore();

      state = container.read(expensesControllerProvider);
      expect(state.page?.items.map((e) => e.id).toList(), [
        'expense-1',
        'expense-2',
      ]);
      expect(state.isLoadingMore, isFalse);
      expect(state.loadMoreFailure, isNull);
    });

    test('does not issue concurrent append requests', () async {
      final getExpenses = MockGetExpenses();
      final page1 = _makePage(
        items: [_item('expense-1')],
        totalCount: 21,
      );
      final page2 = _makePage(
        items: [_item('expense-2')],
        pageNumber: 2,
        totalCount: 21,
      );
      final nextPage = Completer<ExpensePage>();
      when(getExpenses.call).thenAnswer((_) async => page1);
      when(
        () => getExpenses(page: 2, pageSize: 20),
      ).thenAnswer((_) => nextPage.future);
      final container = ProviderContainer(
        overrides: [getExpensesUseCaseProvider.overrideWithValue(getExpenses)],
      );
      addTearDown(container.dispose);

      final controller = container.read(expensesControllerProvider.notifier);
      await controller.refresh();
      final firstRequest = controller.loadMore();
      final secondRequest = controller.loadMore();

      expect(container.read(expensesControllerProvider).isLoadingMore, isTrue);
      verify(() => getExpenses(page: 2, pageSize: 20)).called(1);

      nextPage.complete(page2);
      await Future.wait([firstRequest, secondRequest]);

      final state = container.read(expensesControllerProvider);
      expect(state.page?.items.map((e) => e.id).toList(), [
        'expense-1',
        'expense-2',
      ]);
      expect(state.isLoadingMore, isFalse);
    });

    test('refresh invalidates a stale append result', () async {
      final getExpenses = MockGetExpenses();
      final initialPage = _makePage(
        items: [_item('old-expense')],
        totalCount: 21,
      );
      final appendPage = _makePage(
        items: [_item('stale-expense')],
        pageNumber: 2,
        totalCount: 21,
      );
      final refreshedPage = _makePage(
        items: [_item('refreshed-expense')],
        totalCount: 1,
      );
      final appendCompleter = Completer<ExpensePage>();
      final refreshCompleter = Completer<ExpensePage>();
      when(getExpenses.call).thenAnswer((_) => refreshCompleter.future);
      when(
        () => getExpenses(page: 2, pageSize: 20),
      ).thenAnswer((_) => appendCompleter.future);
      final container = ProviderContainer(
        overrides: [getExpensesUseCaseProvider.overrideWithValue(getExpenses)],
      );
      addTearDown(container.dispose);
      container.read(expensesControllerProvider.notifier).state = ExpensesState(
        page: initialPage,
        totalCount: 21,
      );
      final controller = container.read(expensesControllerProvider.notifier);

      final appendRequest = controller.loadMore();
      final refreshRequest = controller.refresh();
      refreshCompleter.complete(refreshedPage);
      await refreshRequest;
      appendCompleter.complete(appendPage);
      await appendRequest;

      final state = container.read(expensesControllerProvider);
      expect(state.page?.items.map((e) => e.id).toList(), [
        'refreshed-expense',
      ]);
      expect(state.pageNumber, 1);
      expect(state.totalCount, 1);
      expect(state.isLoadingMore, isFalse);
      expect(state.hasMore, isFalse);
    });

    test(
      'loadMore issues a fresh request after refresh invalidates a pending append',
      () async {
        final getExpenses = MockGetExpenses();
        final initialPage = _makePage(
          items: [_item('old-expense')],
          totalCount: 21,
        );
        final refreshedPage = _makePage(
          items: [_item('refreshed-expense')],
          totalCount: 21,
        );
        final freshAppendPage = _makePage(
          items: [_item('fresh-page2')],
          pageNumber: 2,
          totalCount: 21,
        );
        final staleAppendPage = _makePage(
          items: [_item('stale-page2')],
          pageNumber: 2,
          totalCount: 21,
        );

        final staleAppendCompleter = Completer<ExpensePage>();
        final refreshCompleter = Completer<ExpensePage>();
        var page2CallCount = 0;
        when(getExpenses.call).thenAnswer((_) => refreshCompleter.future);
        when(() => getExpenses(page: 2, pageSize: 20)).thenAnswer((_) {
          page2CallCount++;
          return page2CallCount == 1
              ? staleAppendCompleter.future
              : Future.value(freshAppendPage);
        });

        final container = ProviderContainer(
          overrides: [
            getExpensesUseCaseProvider.overrideWithValue(getExpenses),
          ],
        );
        addTearDown(container.dispose);
        container.read(expensesControllerProvider.notifier).state =
            ExpensesState(page: initialPage, totalCount: 21);
        final controller = container.read(
          expensesControllerProvider.notifier,
        );

        final staleAppend = controller.loadMore();
        final refreshRequest = controller.refresh();
        refreshCompleter.complete(refreshedPage);
        await refreshRequest;

        final freshAppend = controller.loadMore();
        await freshAppend;

        expect(page2CallCount, 2);
        var state = container.read(expensesControllerProvider);
        expect(state.page?.items.map((e) => e.id).toList(), [
          'refreshed-expense',
          'fresh-page2',
        ]);
        expect(state.isLoadingMore, isFalse);

        staleAppendCompleter.complete(staleAppendPage);
        await staleAppend;

        state = container.read(expensesControllerProvider);
        expect(
          state.page?.items.map((e) => e.id).contains('stale-page2'),
          isFalse,
        );
      },
    );
  });
}
