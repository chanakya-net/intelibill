import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/app/shell/menu_visibility.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:intelibill_mobile/src/features/expenses/presentation/controllers/expenses_controller.dart';
import 'package:intelibill_mobile/src/features/expenses/presentation/widgets/expense_card.dart';
import 'package:intelibill_mobile/src/features/expenses/presentation/widgets/expense_detail_sheet.dart';
import 'package:intelibill_mobile/src/features/expenses/presentation/widgets/expense_form_sheet.dart';
import 'package:intelibill_mobile/src/features/expenses/presentation/widgets/expenses_summary_strip.dart';

class ExpensesPage extends ConsumerStatefulWidget {
  const ExpensesPage({super.key});

  static const recordExpenseFabKey = Key('expenses-record-fab');

  @override
  ConsumerState<ExpensesPage> createState() => _ExpensesPageState();
}

class _ExpensesPageState extends ConsumerState<ExpensesPage> {
  late final TextEditingController _searchController;

  @override
  void initState() {
    super.initState();
    _searchController = TextEditingController();
  }

  @override
  void dispose() {
    _searchController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final state = ref.watch(expensesControllerProvider);
    final authState = ref.watch(authControllerProvider);
    final l10n = AppLocalizations.of(context)!;
    final canRecord = canManageExpenses(authState.value?.session);
    if (_searchController.text != state.searchQuery) {
      _searchController.text = state.searchQuery;
    }

    return Scaffold(
      appBar: AppBar(title: Text(l10n.shellManageExpenses)),
      floatingActionButton: canRecord
          ? FloatingActionButton.extended(
              key: ExpensesPage.recordExpenseFabKey,
              onPressed: _openRecordExpenseSheet,
              icon: const Icon(Icons.add),
              label: Text(l10n.expensesRecordExpense),
            )
          : null,
      body: Column(
        children: [
          ExpensesSummaryStrip(
            totalCount: state.totalCount,
            loadedAmount: state.loadedAmount,
            loadedActiveCount: state.loadedActiveCount,
            loadedVoidedCount: state.loadedVoidedCount,
          ),
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 12, 16, 4),
            child: TextField(
              key: const Key('expenses-search'),
              controller: _searchController,
              decoration: InputDecoration(
                labelText: l10n.commonSearch,
                prefixIcon: const Icon(Icons.search),
                suffixIcon: state.searchQuery.isEmpty
                    ? null
                    : IconButton(
                        tooltip: l10n.commonClear,
                        icon: Icon(
                          Icons.clear,
                          semanticLabel: l10n.commonClear,
                        ),
                        onPressed: () {
                          _searchController.clear();
                          ref
                              .read(expensesControllerProvider.notifier)
                              .updateSearch('');
                        },
                      ),
                border: const OutlineInputBorder(),
              ),
              onChanged: (value) => ref
                  .read(expensesControllerProvider.notifier)
                  .updateSearch(value),
            ),
          ),
          _ExpenseStatusFilters(
            selectedFilter: state.statusFilter,
            onFilterChanged: (filter) {
              ref
                  .read(expensesControllerProvider.notifier)
                  .updateStatusFilter(filter);
            },
          ),
          Expanded(child: _ExpensesBody(state: state)),
        ],
      ),
    );
  }

  Future<void> _openRecordExpenseSheet() async {
    final l10n = AppLocalizations.of(context)!;
    final recorded = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      useSafeArea: true,
      showDragHandle: true,
      builder: (_) => const ExpenseFormSheet(),
    );
    if (!mounted || recorded != true) return;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(l10n.expensesRecordSuccess)),
    );
  }
}

class _ExpenseStatusFilters extends StatelessWidget {
  const _ExpenseStatusFilters({
    required this.selectedFilter,
    required this.onFilterChanged,
  });

  final ExpenseStatusFilter selectedFilter;
  final ValueChanged<ExpenseStatusFilter> onFilterChanged;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    return SingleChildScrollView(
      scrollDirection: Axis.horizontal,
      padding: const EdgeInsets.fromLTRB(16, 8, 16, 4),
      child: Row(
        children: [
          _buildChip(l10n.expensesFilterAll, ExpenseStatusFilter.all),
          const SizedBox(width: 8),
          _buildChip(l10n.expensesFilterActive, ExpenseStatusFilter.active),
          const SizedBox(width: 8),
          _buildChip(l10n.expensesFilterVoided, ExpenseStatusFilter.voided),
        ],
      ),
    );
  }

  Widget _buildChip(String label, ExpenseStatusFilter filter) {
    return FilterChip(
      label: Text(label),
      selected: selectedFilter == filter,
      onSelected: (_) => onFilterChanged(filter),
    );
  }
}

class _ExpensesBody extends ConsumerWidget {
  const _ExpensesBody({required this.state});

  static const _loadMoreThreshold = 200.0;

  final ExpensesState state;

  bool _onScroll(ScrollNotification notification, WidgetRef ref) {
    if (notification.metrics.extentAfter > _loadMoreThreshold ||
        !state.hasMore ||
        state.isLoadingMore ||
        state.loadMoreFailure != null) {
      return false;
    }
    unawaited(ref.read(expensesControllerProvider.notifier).loadMore());
    return false;
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    if (state.isLoading && state.page == null) {
      return Center(
        child: Semantics(
          label: AppLocalizations.of(context)!.commonLoading,
          child: const CircularProgressIndicator(),
        ),
      );
    }

    final expenses = state.filteredExpenses;
    final hasLoadedExpenses = state.page?.items.isNotEmpty ?? false;
    final hasFailure = state.failure != null;
    final hasData = state.page != null;

    if (hasFailure && !hasData) {
      return Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(AppLocalizations.of(context)!.expensesUnableToLoad),
            const SizedBox(height: 12),
            FilledButton(
              onPressed: () => unawaited(
                ref.read(expensesControllerProvider.notifier).refresh(),
              ),
              child: Text(AppLocalizations.of(context)!.expensesRetry),
            ),
          ],
        ),
      );
    }

    return Stack(
      children: [
        NotificationListener<ScrollNotification>(
          onNotification: (notification) => _onScroll(notification, ref),
          child: RefreshIndicator(
            onRefresh: ref.read(expensesControllerProvider.notifier).refresh,
            child: expenses.isEmpty
                ? ListView(
                    physics: const AlwaysScrollableScrollPhysics(),
                    children: [
                      const SizedBox(height: 160),
                      Center(
                        child: Text(
                          hasLoadedExpenses
                              ? AppLocalizations.of(
                                  context,
                                )!.expensesNoFilteredResults
                              : AppLocalizations.of(
                                  context,
                                )!.expensesNoExpensesFound,
                        ),
                      ),
                    ],
                  )
                : ListView.builder(
                    physics: const AlwaysScrollableScrollPhysics(),
                    padding: const EdgeInsets.symmetric(vertical: 8),
                    itemCount: expenses.length + _appendRowCount,
                    itemBuilder: (context, index) {
                      if (index == expenses.length) {
                        return _buildAppendRow(context, ref);
                      }
                      return ExpenseCard(
                        expense: expenses[index],
                        onTap: () => unawaited(
                          _openExpenseDetail(context, ref, expenses[index].id),
                        ),
                      );
                    },
                  ),
          ),
        ),
        if (hasFailure)
          Center(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                Text(AppLocalizations.of(context)!.expensesUnableToLoad),
                const SizedBox(height: 12),
                FilledButton(
                  onPressed: () => unawaited(
                    ref.read(expensesControllerProvider.notifier).refresh(),
                  ),
                  child: Text(AppLocalizations.of(context)!.expensesRetry),
                ),
              ],
            ),
          ),
      ],
    );
  }

  int get _appendRowCount =>
      state.isLoadingMore || state.loadMoreFailure != null ? 1 : 0;

  Widget _buildAppendRow(BuildContext context, WidgetRef ref) {
    if (state.isLoadingMore) {
      return Padding(
        padding: const EdgeInsets.symmetric(vertical: 16),
        child: Center(
          child: Semantics(
            label: AppLocalizations.of(context)!.expensesLoadingMore,
            child: const CircularProgressIndicator(),
          ),
        ),
      );
    }
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 16, horizontal: 24),
      child: Column(
        children: [
          Text(AppLocalizations.of(context)!.expensesUnableToLoadMore),
          const SizedBox(height: 8),
          TextButton(
            onPressed: () => unawaited(
              ref.read(expensesControllerProvider.notifier).loadMore(),
            ),
            child: Text(AppLocalizations.of(context)!.expensesRetry),
          ),
        ],
      ),
    );
  }
}

Future<void> _openExpenseDetail(
  BuildContext context,
  WidgetRef ref,
  String id,
) async {
  final controller = ref.read(expensesControllerProvider.notifier);
  unawaited(controller.openExpense(id));
  await showExpenseDetailSheet(context);
  if (!context.mounted) return;
  controller.clearSelectedExpense();
}
